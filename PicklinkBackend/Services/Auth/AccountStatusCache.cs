using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PicklinkBackend.Repositories;

namespace PicklinkBackend.Services.Auth;

/// <summary>
/// Caches the "is this account still usable?" lookup that JWT validation runs on every request.
/// Without it each authenticated call pays a full database round trip before the controller starts,
/// which dominates response time whenever the database is remote.
/// Lock/unlock invalidates the entry, so the TTL only matters for changes made outside the API.
/// </summary>
public sealed class AccountStatusCache
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);
    private readonly IMemoryCache _cache;

    public AccountStatusCache(IMemoryCache cache)
    {
        _cache = cache;
    }

    public async ValueTask<bool> IsUsableAsync(
        int userId,
        IUserRepository users,
        CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(CacheKey(userId), out bool cachedIsUsable))
        {
            return cachedIsUsable;
        }

        var account = await users.Users
            .AsNoTracking()
            .Where(user => user.UserId == userId)
            .Select(user => new { user.IsLocked })
            .SingleOrDefaultAsync(cancellationToken);

        var isUsable = account is not null && !account.IsLocked;
        _cache.Set(CacheKey(userId), isUsable, Lifetime);
        return isUsable;
    }

    public void Invalidate(int userId) => _cache.Remove(CacheKey(userId));

    private static string CacheKey(int userId) => $"account-status:{userId}";
}
