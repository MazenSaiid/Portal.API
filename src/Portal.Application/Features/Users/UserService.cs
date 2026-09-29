using System.Linq.Expressions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Portal.Application.Common.Exceptions;
using Portal.Application.Common.Interfaces;
using Portal.Application.Common.Models;
using Portal.Application.Common.Security;
using Portal.Application.Features.Auth;
using Portal.Domain.Authorization;
using Portal.Domain.Entities;

namespace Portal.Application.Features.Users;

public interface IUserService
{
    Task<PagedResult<UserDto>> GetPagedAsync(UserListQuery query, CancellationToken ct = default);
    Task<UserDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct = default);
    Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default);
    Task<UserDto> SetStatusAsync(Guid id, bool isActive, CancellationToken ct = default);
    Task ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class UserService(
    IApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    ICurrentUser currentUser,
    PermissionCache permissionCache,
    ISessionRevoker sessionRevoker,
    IValidator<CreateUserRequest> createValidator,
    IValidator<UpdateUserRequest> updateValidator,
    IValidator<ResetPasswordRequest> resetPasswordValidator) : IUserService
{
    private static readonly Expression<Func<ApplicationUser, UserDto>> ToDto = u => new UserDto(
        u.Id, u.FirstName, u.LastName, u.Email!, u.PhoneNumber, u.IsActive, u.LockoutEnd,
        u.UserRoles.Select(ur => (Guid?)ur.RoleId).FirstOrDefault(),
        u.UserRoles.Select(ur => ur.Role.Name).FirstOrDefault(),
        u.CreatedAt, u.LastLoginAt);

    public async Task<PagedResult<UserDto>> GetPagedAsync(UserListQuery query, CancellationToken ct = default)
    {
        var users = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            users = users.Where(u => u.FirstName.Contains(term) || u.LastName.Contains(term)
                                     || u.Email!.Contains(term)
                                     || (u.PhoneNumber != null && u.PhoneNumber.Contains(term)));
        }
        if (query.RoleId is { } roleId)
            users = users.Where(u => u.UserRoles.Any(ur => ur.RoleId == roleId));
        if (query.IsActive is { } isActive)
            users = users.Where(u => u.IsActive == isActive);

        var total = await users.CountAsync(ct);
        var items = await Sort(users, query)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(ToDto)
            .ToListAsync(ct);

        return new PagedResult<UserDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<UserDto> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.Users.AsNoTracking().Where(u => u.Id == id).Select(ToDto).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("User", id);

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        var email = request.Email.Trim();
        await EnsureEmailIsUniqueAsync(email, excludeUserId: null, ct);
        await EnsureRoleExistsAsync(request.RoleId, ct);

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PhoneNumber = NullIfBlank(request.PhoneNumber),
            IsActive = request.IsActive,
        };
        user.UserRoles.Add(new ApplicationUserRole { RoleId = request.RoleId });

        var result = await userManager.CreateAsync(user, request.Password);
        if (result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName"))
            throw new ConflictException($"A user with email '{email}' already exists.");
        result.ThrowIfFailed(nameof(request.Password));

        return await GetByIdAsync(user.Id, ct);
    }

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        var user = await LoadUserAsync(id, ct);
        var email = request.Email.Trim();
        await EnsureEmailIsUniqueAsync(email, excludeUserId: id, ct);

        var currentRoleId = user.UserRoles.Select(ur => (Guid?)ur.RoleId).FirstOrDefault();
        if (currentRoleId != request.RoleId)
        {
            EnsureNotSelf(id, "change the role of");
            await EnsureRoleExistsAsync(request.RoleId, ct);
            await EnsureNotLastActiveAdministratorAsync(user, ct);
            user.UserRoles.Clear();
            user.UserRoles.Add(new ApplicationUserRole { UserId = id, RoleId = request.RoleId });
        }
        if (user.IsActive && !request.IsActive)
        {
            EnsureNotSelf(id, "deactivate");
            await EnsureNotLastActiveAdministratorAsync(user, ct);
        }

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.Email = email;
        user.UserName = email;
        user.NormalizedEmail = userManager.NormalizeEmail(email);
        user.NormalizedUserName = userManager.NormalizeName(email);
        user.PhoneNumber = NullIfBlank(request.PhoneNumber);

        var deactivated = user.IsActive && !request.IsActive;
        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        if (deactivated) await sessionRevoker.RevokeAllAsync(id, ct);
        permissionCache.InvalidateAll();
        return await GetByIdAsync(id, ct);
    }

    public async Task<UserDto> SetStatusAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        var user = await LoadUserAsync(id, ct);
        if (user.IsActive == isActive) return await GetByIdAsync(id, ct);

        if (!isActive)
        {
            EnsureNotSelf(id, "deactivate");
            await EnsureNotLastActiveAdministratorAsync(user, ct);
        }

        user.IsActive = isActive;
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        if (!isActive) await sessionRevoker.RevokeAllAsync(id, ct);
        permissionCache.InvalidateAll();
        return await GetByIdAsync(id, ct);
    }

    public async Task ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct = default)
    {
        await resetPasswordValidator.ValidateAndThrowAsync(request, ct);
        var user = await userManager.FindByIdAsync(id.ToString()) ?? throw new NotFoundException("User", id);

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, request.NewPassword);
        result.ThrowIfFailed(nameof(request.NewPassword));

        // An admin reset also clears a lockout so the user can sign in with the new password.
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);
        await sessionRevoker.RevokeAllAsync(id, ct); // S4 — the old password's sessions end

    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var user = await LoadUserAsync(id, ct);
        EnsureNotSelf(id, "delete");
        await EnsureNotLastActiveAdministratorAsync(user, ct);

        (await userManager.DeleteAsync(user)).ThrowIfFailed("User");
        permissionCache.InvalidateAll();
    }

    private async Task<ApplicationUser> LoadUserAsync(Guid id, CancellationToken ct) =>
        await db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role).FirstOrDefaultAsync(u => u.Id == id, ct)
        ?? throw new NotFoundException("User", id);

    /// <summary>R3 — protects admins from locking themselves out.</summary>
    private void EnsureNotSelf(Guid userId, string action)
    {
        if (currentUser.UserId == userId)
            throw new BusinessRuleException($"You cannot {action} your own account.");
    }

    /// <summary>R4 — the system must always keep at least one active Administrator.</summary>
    private async Task EnsureNotLastActiveAdministratorAsync(ApplicationUser user, CancellationToken ct)
    {
        var isActiveAdmin = user.IsActive && user.UserRoles.Any(ur => ur.Role.Name == SystemRoles.Administrator);
        if (!isActiveAdmin) return;

        var otherActiveAdmins = await db.UserRoles.CountAsync(
            ur => ur.Role.Name == SystemRoles.Administrator && ur.UserId != user.Id && ur.User.IsActive, ct);
        if (otherActiveAdmins == 0)
            throw new BusinessRuleException("This is the last active Administrator. Assign another Administrator first.");
    }

    private async Task EnsureEmailIsUniqueAsync(string email, Guid? excludeUserId, CancellationToken ct)
    {
        var normalized = userManager.NormalizeEmail(email);
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized && u.Id != excludeUserId, ct))
            throw new ConflictException($"A user with email '{email}' already exists.");
    }

    private async Task EnsureRoleExistsAsync(Guid roleId, CancellationToken ct)
    {
        if (!await db.Roles.AnyAsync(r => r.Id == roleId, ct))
            throw new ValidationException([new ValidationFailure("RoleId", "Selected role does not exist.")]);
    }

    private static IQueryable<ApplicationUser> Sort(IQueryable<ApplicationUser> users, UserListQuery q) =>
        (q.SortBy?.ToLowerInvariant(), q.IsDescending) switch
        {
            ("name", false) => users.OrderBy(u => u.FirstName).ThenBy(u => u.LastName),
            ("name", true) => users.OrderByDescending(u => u.FirstName).ThenByDescending(u => u.LastName),
            ("email", false) => users.OrderBy(u => u.Email),
            ("email", true) => users.OrderByDescending(u => u.Email),
            ("role", false) => users.OrderBy(u => u.UserRoles.Select(ur => ur.Role.Name).FirstOrDefault()),
            ("role", true) => users.OrderByDescending(u => u.UserRoles.Select(ur => ur.Role.Name).FirstOrDefault()),
            ("status", false) => users.OrderBy(u => u.IsActive),
            ("status", true) => users.OrderByDescending(u => u.IsActive),
            ("lastloginat", false) => users.OrderBy(u => u.LastLoginAt),
            ("lastloginat", true) => users.OrderByDescending(u => u.LastLoginAt),
            ("createdat", false) => users.OrderBy(u => u.CreatedAt),
            _ => users.OrderByDescending(u => u.CreatedAt),
        };

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
