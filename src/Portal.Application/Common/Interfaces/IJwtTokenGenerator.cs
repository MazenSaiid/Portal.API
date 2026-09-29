using Portal.Domain.Entities;

namespace Portal.Application.Common.Interfaces;

public sealed record AccessToken(string Token, DateTime ExpiresAt);

public interface IJwtTokenGenerator
{
    AccessToken Generate(ApplicationUser user, string? roleName);
}
