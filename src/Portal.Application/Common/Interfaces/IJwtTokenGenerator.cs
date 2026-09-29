using Portal.Domain.Entities;

namespace Portal.Application.Common.Interfaces;

public sealed record AccessToken(string Token, DateTime ExpiresAt);

/// <summary>A newly generated refresh token: the plain value goes to the client, only the hash is stored.</summary>
public sealed record NewRefreshToken(string Token, string TokenHash, DateTime ExpiresAt);

public interface IJwtTokenGenerator
{
    AccessToken Generate(ApplicationUser user, string? roleName);
    NewRefreshToken CreateRefreshToken();
    string HashRefreshToken(string token);
}
