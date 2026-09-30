using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Portal.Application.Common.Interfaces;

namespace Portal.API.Infrastructure;

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : null;
}

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId => accessor.HttpContext?.User.GetUserId();
    public string? UserName => accessor.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Name)?.Value;
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
