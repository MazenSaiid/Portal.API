namespace Portal.Domain.Entities.Auditing;

public enum AuditAction
{
    Created,
    Updated,
    Deleted,
    SignedIn,
    SignInFailed,
    LockedOut,
    SignedOut,
    PasswordChanged,
    PasswordReset,
    TokenReuseDetected,
}

/// <summary>One changed field of an audited record. Values are display strings.</summary>
public sealed record AuditChange(string Field, string? From, string? To);

/// <summary>
/// Append-only record of a data change or security event (Spec 006). User name and values are snapshots,
/// so entries stay readable after renames or deletions.
/// </summary>
public class AuditLog
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string? IpAddress { get; set; }
    public AuditAction Action { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string Summary { get; set; } = string.Empty;

    /// <summary>JSON array of <see cref="AuditChange"/>; null for security events.</summary>
    public string? Changes { get; set; }
}
