using Portal.Application.Common.Models;

namespace Portal.Application.Features.Users;

public sealed record UserDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? PhoneNumber,
    bool IsActive,
    DateTimeOffset? LockoutEnd,
    Guid? RoleId,
    string? RoleName,
    DateTime CreatedAt,
    DateTime? LastLoginAt)
{
    public string FullName => $"{FirstName} {LastName}".Trim();
    public bool IsLockedOut => LockoutEnd > DateTimeOffset.UtcNow;
}

public sealed class UserListQuery : PagedQuery
{
    public string? Search { get; set; }
    public Guid? RoleId { get; set; }
    public bool? IsActive { get; set; }
}

/// <summary>Fields shared by create and update requests, validated by one set of rules.</summary>
public interface IUserProfileRequest
{
    string FirstName { get; }
    string LastName { get; }
    string Email { get; }
    string? PhoneNumber { get; }
    Guid RoleId { get; }
    bool IsActive { get; }
}

public sealed record CreateUserRequest(
    string FirstName,
    string LastName,
    string Email,
    string? PhoneNumber,
    string Password,
    Guid RoleId,
    bool IsActive = true) : IUserProfileRequest;

public sealed record UpdateUserRequest(
    string FirstName,
    string LastName,
    string Email,
    string? PhoneNumber,
    Guid RoleId,
    bool IsActive) : IUserProfileRequest;

public sealed record UpdateUserStatusRequest(bool IsActive);

public sealed record ResetPasswordRequest(string NewPassword);
