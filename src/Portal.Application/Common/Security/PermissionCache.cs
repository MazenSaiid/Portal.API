using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace Portal.Application.Common.Security;

/// <summary>
/// Caches each user's effective permissions. Any change to grants, roles or user status calls
/// <see cref="InvalidateAll"/>, so permission changes apply on the very next request.
/// </summary>
public sealed class PermissionCache(IMemoryCache cache)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private CancellationTokenSource _reset = new();

    public async Task<IReadOnlySet<string>> GetOrAddAsync(Guid userId, Func<Task<IReadOnlySet<string>>> factory)
    {
        var key = $"permissions:user:{userId}";
        if (cache.TryGetValue(key, out IReadOnlySet<string>? cached) && cached is not null)
            return cached;

        var token = _reset.Token;
        var permissions = await factory();
        cache.Set(key, permissions, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = Lifetime,
            ExpirationTokens = { new CancellationChangeToken(token) }
        });
        return permissions;
    }

    public void InvalidateAll()
    {
        var previous = Interlocked.Exchange(ref _reset, new CancellationTokenSource());
        // Not disposed: a concurrent reader may still be reading its Token.
        previous.Cancel();
    }
}
