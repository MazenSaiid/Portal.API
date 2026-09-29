using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Portal.Application.Common.Interfaces;
using Portal.Domain.Entities;

namespace Portal.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required, MinLength(32, ErrorMessage = "Jwt:Key must be at least 32 characters.")]
    public string Key { get; init; } = string.Empty;

    [Required] public string Issuer { get; init; } = string.Empty;
    [Required] public string Audience { get; init; } = string.Empty;
    [Range(1, 1440)] public int ExpiryMinutes { get; init; } = 15;
    [Range(1, 90)] public int RefreshTokenDays { get; init; } = 7;
}

public sealed class JwtTokenGenerator(IOptions<JwtOptions> options, TimeProvider clock) : IJwtTokenGenerator
{
    public AccessToken Generate(ApplicationUser user, string? roleName)
    {
        var jwt = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(jwt.ExpiryMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Name, user.FullName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        // Informational only — authorization is decided by permissions, never by role name.
        if (roleName is not null) claims.Add(new Claim("role", roleName));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(jwt.Issuer, jwt.Audience, claims, now, expires, credentials);
        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public NewRefreshToken CreateRefreshToken()
    {
        var token = Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
        var expires = clock.GetUtcNow().UtcDateTime.AddDays(options.Value.RefreshTokenDays);
        return new NewRefreshToken(token, HashRefreshToken(token), expires);
    }

    public string HashRefreshToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
