using Microsoft.EntityFrameworkCore;
using Portal.Application.Common.Interfaces;
using Portal.Application.Common.Security;

namespace Portal.Application.Features.Permissions;

public interface IPermissionService
{
    /// <summary>Effective permission keys of an active user; empty for inactive or unknown users.</summary>
    Task<IReadOnlySet<string>> GetUserPermissionsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The full permission catalogue grouped by module (<c>IsGranted</c> is always false).</summary>
    Task<IReadOnlyList<PermissionModuleDto>> GetCatalogAsync(CancellationToken ct = default);
}

public sealed class PermissionService(IApplicationDbContext db, PermissionCache cache) : IPermissionService
{
    public Task<IReadOnlySet<string>> GetUserPermissionsAsync(Guid userId, CancellationToken ct = default) =>
        cache.GetOrAddAsync(userId, async () =>
        {
            var keys = await db.Users
                .Where(u => u.Id == userId && u.IsActive)
                .SelectMany(u => u.UserRoles)
                .SelectMany(ur => ur.Role.RolePermissions)
                .Select(rp => rp.Permission.Key)
                .Distinct()
                .ToListAsync(ct);
            return new HashSet<string>(keys, StringComparer.Ordinal);
        });

    public async Task<IReadOnlyList<PermissionModuleDto>> GetCatalogAsync(CancellationToken ct = default)
    {
        var permissions = await db.Permissions.AsNoTracking().OrderBy(p => p.SortOrder).ToListAsync(ct);
        return PermissionGrouping.ByModule(permissions, _ => false);
    }
}

internal static class PermissionGrouping
{
    public static IReadOnlyList<PermissionModuleDto> ByModule(
        IEnumerable<Domain.Entities.Permission> orderedPermissions, Func<int, bool> isGranted) =>
        orderedPermissions
            .GroupBy(p => p.Module)
            .Select(g => new PermissionModuleDto(
                g.Key,
                g.Select(p => new PermissionItemDto(p.Id, p.Key, p.Description, isGranted(p.Id))).ToList()))
            .ToList();
}
