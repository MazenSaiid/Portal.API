namespace Portal.Application.Common.Interfaces;

public interface ICurrentUser
{
    Guid? UserId { get; }

    /// <summary>Display name from the token; used for audit snapshots without a database lookup.</summary>
    string? UserName { get; }

    string? IpAddress { get; }
}
