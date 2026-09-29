namespace Portal.Application.Features.Auth;

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record CurrentUserDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string FullName,
    Guid? RoleId,
    string? RoleName,
    IReadOnlyList<string> Permissions);

/// <summary>A signed-in session: short-lived access token plus a rotating refresh token.</summary>
public sealed record LoginResponse(
    string AccessToken,
    DateTime ExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    CurrentUserDto User);
