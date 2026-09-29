using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Portal.API.Infrastructure;
using Portal.Application.Features.Auth;

namespace Portal.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public Task<LoginResponse> Login(LoginRequest request, CancellationToken ct) =>
        authService.LoginAsync(request, ct);

    /// <summary>Exchanges a refresh token for a new token pair (the old refresh token is revoked).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Refresh)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public Task<LoginResponse> Refresh(RefreshTokenRequest request, CancellationToken ct) =>
        authService.RefreshAsync(request, ct);

    /// <summary>Revokes the given refresh token. Idempotent.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshTokenRequest request, CancellationToken ct)
    {
        await authService.LogoutAsync(request, ct);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public Task<CurrentUserDto> Me(CancellationToken ct) =>
        authService.GetCurrentUserAsync(User.GetUserId()!.Value, ct);

    [HttpPost("change-password")]
    [Authorize]
    public Task<LoginResponse> ChangePassword(ChangePasswordRequest request, CancellationToken ct) =>
        authService.ChangePasswordAsync(User.GetUserId()!.Value, request, ct);
}
