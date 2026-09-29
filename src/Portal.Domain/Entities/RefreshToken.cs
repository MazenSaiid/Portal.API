namespace Portal.Domain.Entities;

/// <summary>A refresh token session. Only the SHA-256 hash of the token is stored.</summary>
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    /// <summary>Hash of the token issued when this one was rotated; null if revoked for another reason.</summary>
    public string? ReplacedByTokenHash { get; set; }

    public bool IsActive(DateTime now) => RevokedAt is null && ExpiresAt > now;
}
