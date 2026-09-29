using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Portal.Application.Common.Exceptions;
using Portal.Application.Common.Interfaces;
using Portal.Application.Common.Security;
using Portal.Application.Features.Permissions;
using Portal.Domain.Entities;

namespace Portal.Application.Features.Roles;

public interface IRoleService
{
    Task<IReadOnlyList<RoleDto>> GetAllAsync(string? search, CancellationToken ct = default);
    Task<IReadOnlyList<RoleLookupDto>> GetLookupAsync(CancellationToken ct = default);
    Task<RoleDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RoleDto> CreateAsync(CreateRoleRequest request, CancellationToken ct = default);
    Task<RoleDto> UpdateAsync(Guid id, UpdateRoleRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<RolePermissionsDto> GetPermissionsAsync(Guid id, CancellationToken ct = default);
    Task<RolePermissionsDto> SetPermissionsAsync(Guid id, SetRolePermissionsRequest request, CancellationToken ct = default);
}

public sealed class RoleService(
    IApplicationDbContext db,
    RoleManager<ApplicationRole> roleManager,
    PermissionCache permissionCache,
    IValidator<CreateRoleRequest> createValidator,
    IValidator<UpdateRoleRequest> updateValidator,
    IValidator<SetRolePermissionsRequest> setPermissionsValidator) : IRoleService
{
    public async Task<IReadOnlyList<RoleDto>> GetAllAsync(string? search, CancellationToken ct = default)
    {
        var roles = db.Roles.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            roles = roles.Where(r => r.Name!.Contains(term) || (r.Description != null && r.Description.Contains(term)));
        }

        return await roles
            .OrderByDescending(r => r.IsSystem).ThenBy(r => r.Name)
            .Select(r => new RoleDto(r.Id, r.Name!, r.Description, r.IsSystem,
                r.UserRoles.Count, r.RolePermissions.Count, r.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RoleLookupDto>> GetLookupAsync(CancellationToken ct = default) =>
        await db.Roles.AsNoTracking().OrderBy(r => r.Name).Select(r => new RoleLookupDto(r.Id, r.Name!)).ToListAsync(ct);

    public async Task<RoleDto> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.Roles.AsNoTracking().Where(r => r.Id == id)
            .Select(r => new RoleDto(r.Id, r.Name!, r.Description, r.IsSystem,
                r.UserRoles.Count, r.RolePermissions.Count, r.CreatedAt))
            .FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("Role", id);

    public async Task<RoleDto> CreateAsync(CreateRoleRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        var name = request.Name.Trim();
        await EnsureNameIsUniqueAsync(name, excludeRoleId: null, ct);

        var permissionIds = request.PermissionIds?.Distinct().ToList() ?? [];
        await EnsurePermissionsExistAsync(permissionIds, ct);

        var role = new ApplicationRole { Name = name, Description = NullIfBlank(request.Description) };
        foreach (var permissionId in permissionIds)
            role.RolePermissions.Add(new RolePermission { PermissionId = permissionId });

        var result = await roleManager.CreateAsync(role);
        if (!result.Succeeded)
            throw new ValidationException(result.Errors.Select(e => new ValidationFailure(nameof(request.Name), e.Description)));

        return await GetByIdAsync(role.Id, ct);
    }

    public async Task<RoleDto> UpdateAsync(Guid id, UpdateRoleRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        var role = await FindRoleAsync(id, ct);
        var name = request.Name.Trim();

        if (role.IsSystem && !string.Equals(role.Name, name, StringComparison.Ordinal))
            throw new BusinessRuleException($"The system role '{role.Name}' cannot be renamed.");
        await EnsureNameIsUniqueAsync(name, excludeRoleId: id, ct);

        role.Name = name;
        role.NormalizedName = roleManager.NormalizeKey(name);
        role.Description = NullIfBlank(request.Description);
        role.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return await GetByIdAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var role = await FindRoleAsync(id, ct);
        if (role.IsSystem)
            throw new BusinessRuleException($"The system role '{role.Name}' cannot be deleted.");

        var userCount = await db.UserRoles.CountAsync(ur => ur.RoleId == id, ct);
        if (userCount > 0)
            throw new ConflictException($"Role '{role.Name}' is assigned to {userCount} user(s). Reassign them before deleting it.");

        db.Roles.Remove(role);
        await db.SaveChangesAsync(ct);
        permissionCache.InvalidateAll();
    }

    public async Task<RolePermissionsDto> GetPermissionsAsync(Guid id, CancellationToken ct = default)
    {
        var role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct)
                   ?? throw new NotFoundException("Role", id);

        var granted = (await db.RolePermissions.Where(rp => rp.RoleId == id).Select(rp => rp.PermissionId).ToListAsync(ct))
            .ToHashSet();
        var permissions = await db.Permissions.AsNoTracking().OrderBy(p => p.SortOrder).ToListAsync(ct);

        return new RolePermissionsDto(role.Id, role.Name!, role.IsSystem,
            PermissionGrouping.ByModule(permissions, granted.Contains));
    }

    public async Task<RolePermissionsDto> SetPermissionsAsync(Guid id, SetRolePermissionsRequest request, CancellationToken ct = default)
    {
        await setPermissionsValidator.ValidateAndThrowAsync(request, ct);
        var role = await FindRoleAsync(id, ct);
        if (role.IsSystem)
            throw new BusinessRuleException($"The system role '{role.Name}' always has every permission.");

        var requestedIds = request.PermissionIds.Distinct().ToList();
        await EnsurePermissionsExistAsync(requestedIds, ct);

        var existing = await db.RolePermissions
            .Where(rp => rp.RoleId == id && requestedIds.Contains(rp.PermissionId))
            .ToListAsync(ct);

        if (request.IsGranted)
        {
            var existingIds = existing.Select(rp => rp.PermissionId).ToHashSet();
            db.RolePermissions.AddRange(requestedIds.Where(pid => !existingIds.Contains(pid))
                .Select(pid => new RolePermission { RoleId = id, PermissionId = pid }));
        }
        else
        {
            db.RolePermissions.RemoveRange(existing);
        }

        role.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        permissionCache.InvalidateAll();

        return await GetPermissionsAsync(id, ct);
    }

    private async Task<ApplicationRole> FindRoleAsync(Guid id, CancellationToken ct) =>
        await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Role", id);

    private async Task EnsureNameIsUniqueAsync(string name, Guid? excludeRoleId, CancellationToken ct)
    {
        var normalized = roleManager.NormalizeKey(name);
        if (await db.Roles.AnyAsync(r => r.NormalizedName == normalized && r.Id != excludeRoleId, ct))
            throw new ConflictException($"A role named '{name}' already exists.");
    }

    /// <summary>R8 — unknown permission ids are rejected rather than silently ignored.</summary>
    private async Task EnsurePermissionsExistAsync(IReadOnlyCollection<int> permissionIds, CancellationToken ct)
    {
        if (permissionIds.Count == 0) return;
        var known = await db.Permissions.CountAsync(p => permissionIds.Contains(p.Id), ct);
        if (known != permissionIds.Count)
            throw new ValidationException([new ValidationFailure("PermissionIds", "One or more permissions do not exist.")]);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
