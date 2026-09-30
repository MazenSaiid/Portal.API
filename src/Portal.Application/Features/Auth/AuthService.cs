using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Portal.Application.Common.Exceptions;
using Portal.Application.Common.Interfaces;
using Portal.Application.Common.Security;
using Portal.Application.Features.Auditing;
using Portal.Domain.Entities.Auditing;
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
    IAuditLogger audit,
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

        var email = request.Email.Trim();
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            await audit.LogAsync(AuditAction.SignInFailed, $"Failed sign-in for {email} (no such account)", userName: email, entityId: email, ct: ct);
            throw new AuthenticationFailedException(InvalidCredentials);
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            await audit.LogAsync(AuditAction.SignInFailed, $"Sign-in blocked for {user.Email}: account is locked", user.Id, user.FullName, ct: ct);
            throw new AuthenticationFailedException("Account is locked due to repeated failed sign-ins. Try again later.");
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);
            await audit.LogAsync(AuditAction.SignInFailed, $"Failed sign-in for {user.Email} (wrong password)", user.Id, user.FullName, ct: ct);
            if (await userManager.IsLockedOutAsync(user))
                await audit.LogAsync(AuditAction.LockedOut, $"{user.Email} locked out after repeated failed sign-ins", user.Id, user.FullName, ct: ct);
            throw new AuthenticationFailedException(InvalidCredentials);
        }

        // Checked only after the password so that account state is not revealed to strangers.
        if (!user.IsActive)
        {
            await audit.LogAsync(AuditAction.SignInFailed, $"Sign-in refused for {user.Email}: account disabled", user.Id, user.FullName, ct: ct);
            throw new AuthenticationFailedException("Your account is disabled. Contact an administrator.");
        }

        await userManager.ResetAccessFailedCountAsync(user);
        user.LastLoginAt = DateTime.UtcNow;
        var session = await IssueSessionAsync(user, replacing: null, ct);
        await audit.LogAsync(AuditAction.SignedIn, $"{user.Email} signed in", user.Id, user.FullName, ct: ct);
        return session;
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
            await audit.LogAsync(AuditAction.TokenReuseDetected,
                $"Reused session token for {stored.User.Email}; all sessions were ended", stored.UserId, stored.User.FullName, ct: ct);
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
        var owner = await db.RefreshTokens.Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .Select(t => new { t.UserId, t.User.Email, Name = t.User.FirstName + " " + t.User.LastName }).FirstOrDefaultAsync(ct);
        await db.RefreshTokens
            .Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
        if (owner is not null)
            await audit.LogAsync(AuditAction.SignedOut, $"{owner.Email} signed out", owner.UserId, owner.Name, ct: ct);
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
        await audit.LogAsync(AuditAction.PasswordChanged, $"{user.Email} changed their password", user.Id, user.FullName, ct: ct);
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
