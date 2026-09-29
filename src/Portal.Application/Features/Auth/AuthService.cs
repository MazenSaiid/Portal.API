using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Portal.Application.Common.Exceptions;
using Portal.Application.Common.Interfaces;
using Portal.Application.Common.Security;
using Portal.Application.Features.Permissions;
using Portal.Domain.Entities;

namespace Portal.Application.Features.Auth;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<LoginResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken ct = default);
    Task LogoutAsync(RefreshTokenRequest request, CancellationToken ct = default);
    Task<CurrentUserDto> GetCurrentUserAsync(Guid userId, CancellationToken ct = default);
    Task<LoginResponse> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default);
}

public sealed class AuthService(
    UserManager<ApplicationUser> userManager,
    IApplicationDbContext db,
    IJwtTokenGenerator tokenGenerator,
    IPermissionService permissionService,
    ISessionRevoker sessionRevoker,
    IValidator<LoginRequest> loginValidator,
    IValidator<RefreshTokenRequest> refreshValidator,
    IValidator<ChangePasswordRequest> changePasswordValidator,
    ILogger<AuthService> logger) : IAuthService
{
    private const string InvalidCredentials = "Invalid email or password.";
    private const string SessionExpired = "Your session has expired. Please sign in again.";

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        await loginValidator.ValidateAndThrowAsync(request, ct);

        var user = await userManager.FindByEmailAsync(request.Email.Trim())
                   ?? throw new AuthenticationFailedException(InvalidCredentials);

        if (await userManager.IsLockedOutAsync(user))
            throw new AuthenticationFailedException("Account is locked due to repeated failed sign-ins. Try again later.");

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);
            throw new AuthenticationFailedException(InvalidCredentials);
        }

        // Checked only after the password so that account state is not revealed to strangers.
        if (!user.IsActive)
            throw new AuthenticationFailedException("Your account is disabled. Contact an administrator.");

        await userManager.ResetAccessFailedCountAsync(user);
        user.LastLoginAt = DateTime.UtcNow;
        return await IssueSessionAsync(user, replacing: null, ct);
    }

    public async Task<LoginResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken ct = default)
    {
        await refreshValidator.ValidateAndThrowAsync(request, ct);

        var hash = tokenGenerator.HashRefreshToken(request.RefreshToken);
        var stored = await db.RefreshTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
                     ?? throw new AuthenticationFailedException(SessionExpired);

        if (stored.ReplacedByTokenHash is not null)
        {
            // S2 — an already-rotated token came back: someone else holds a copy. End every session of this user.
            logger.LogWarning("Refresh token reuse detected for user {UserId}; revoking all sessions", stored.UserId);
            await sessionRevoker.RevokeAllAsync(stored.UserId, ct);
            throw new AuthenticationFailedException(SessionExpired);
        }

        // Revoked by logout / password change / deactivation: simply no longer valid.
        if (stored.RevokedAt is not null || stored.ExpiresAt <= DateTime.UtcNow)
            throw new AuthenticationFailedException(SessionExpired);

        if (!stored.User.IsActive || await userManager.IsLockedOutAsync(stored.User))
        {
            await sessionRevoker.RevokeAllAsync(stored.UserId, ct);
            throw new AuthenticationFailedException("Your account is disabled. Contact an administrator.");
        }

        return await IssueSessionAsync(stored.User, replacing: stored, ct);
    }

    public async Task LogoutAsync(RefreshTokenRequest request, CancellationToken ct = default)
    {
        await refreshValidator.ValidateAndThrowAsync(request, ct);
        var hash = tokenGenerator.HashRefreshToken(request.RefreshToken);
        var now = DateTime.UtcNow;
        await db.RefreshTokens
            .Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    public async Task<CurrentUserDto> GetCurrentUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking()
                       .Where(u => u.Id == userId)
                       .Select(u => new
                       {
                           u.Id, u.Email, u.FirstName, u.LastName,
                           Role = u.UserRoles.Select(ur => new { ur.RoleId, ur.Role.Name }).FirstOrDefault()
                       })
                       .FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException("User", userId);

        var permissions = await permissionService.GetUserPermissionsAsync(userId, ct);

        return new CurrentUserDto(
            user.Id, user.Email!, user.FirstName, user.LastName, $"{user.FirstName} {user.LastName}".Trim(),
            user.Role?.RoleId, user.Role?.Name, permissions.Order().ToList());
    }

    public async Task<LoginResponse> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        await changePasswordValidator.ValidateAndThrowAsync(request, ct);

        var user = await userManager.FindByIdAsync(userId.ToString())
                   ?? throw new NotFoundException("User", userId);

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
            throw new ValidationException([new ValidationFailure(nameof(request.CurrentPassword), "Current password is incorrect.")]);
        result.ThrowIfFailed(nameof(request.NewPassword));

        // S5 — sign out every other device, keep the current one signed in with a fresh session.
        await sessionRevoker.RevokeAllAsync(userId, ct);
        return await IssueSessionAsync(user, replacing: null, ct);
    }

    /// <summary>Creates an access + refresh token pair. When <paramref name="replacing"/> is given it is rotated out (B3).</summary>
    private async Task<LoginResponse> IssueSessionAsync(ApplicationUser user, RefreshToken? replacing, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var refresh = tokenGenerator.CreateRefreshToken();

        if (replacing is not null)
        {
            replacing.RevokedAt = now;
            replacing.ReplacedByTokenHash = refresh.TokenHash;
        }
        db.RefreshTokens.Add(new RefreshToken { UserId = user.Id, TokenHash = refresh.TokenHash, ExpiresAt = refresh.ExpiresAt });

        // Housekeeping: expired sessions are no longer needed, even for reuse detection.
        await db.RefreshTokens.Where(t => t.UserId == user.Id && t.ExpiresAt < now).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);

        var profile = await GetCurrentUserAsync(user.Id, ct);
        var access = tokenGenerator.Generate(user, profile.RoleName);
        return new LoginResponse(access.Token, access.ExpiresAt, refresh.Token, refresh.ExpiresAt, profile);
    }
}
