using Microsoft.EntityFrameworkCore;
using Portal.Application.Common.Interfaces;

namespace Portal.Application.Features.Auth;

/// <summary>Ends a user's refresh-token sessions (S4). Their current access token still expires on its own within minutes.</summary>
public interface ISessionRevoker
{
    Task RevokeAllAsync(Guid userId, CancellationToken ct = default);
}

public sealed class SessionRevoker(IApplicationDbContext db) : ISessionRevoker
{
    public Task RevokeAllAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }
}
