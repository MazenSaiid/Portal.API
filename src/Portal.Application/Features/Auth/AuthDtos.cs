namespace Portal.Application.Features.Auth;

public sealed record LoginRequest(string Email, string Password);

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

public sealed record LoginResponse(string AccessToken, DateTime ExpiresAt, CurrentUserDto User);
