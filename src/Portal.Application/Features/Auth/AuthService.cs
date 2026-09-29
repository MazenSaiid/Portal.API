using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Portal.Application.Common.Exceptions;
using Portal.Application.Common.Interfaces;
using Portal.Application.Common.Security;
using Portal.Application.Features.Permissions;
using Portal.Domain.Entities;

namespace Portal.Application.Features.Auth;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<CurrentUserDto> GetCurrentUserAsync(Guid userId, CancellationToken ct = default);
    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default);
}

public sealed class AuthService(
    UserManager<ApplicationUser> userManager,
    IApplicationDbContext db,
    IJwtTokenGenerator tokenGenerator,
    IPermissionService permissionService,
    IValidator<LoginRequest> loginValidator,
    IValidator<ChangePasswordRequest> changePasswordValidator) : IAuthService
{
    private const string InvalidCredentials = "Invalid email or password.";

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
        await db.SaveChangesAsync(ct);

        var profile = await GetCurrentUserAsync(user.Id, ct);
        var token = tokenGenerator.Generate(user, profile.RoleName);
        return new LoginResponse(token.Token, token.ExpiresAt, profile);
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

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        await changePasswordValidator.ValidateAndThrowAsync(request, ct);

        var user = await userManager.FindByIdAsync(userId.ToString())
                   ?? throw new NotFoundException("User", userId);

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
            throw new ValidationException([new ValidationFailure(nameof(request.CurrentPassword), "Current password is incorrect.")]);

        result.ThrowIfFailed(nameof(request.NewPassword));
    }
}
