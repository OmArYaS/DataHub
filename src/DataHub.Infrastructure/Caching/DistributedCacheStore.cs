using Microsoft.Extensions.Caching.Distributed;

using DataHub.Application.Common;

namespace DataHub.Infrastructure.Caching;

/// <summary>ICacheStore over IDistributedCache (Redis production, memory demo).</summary>
public sealed class DistributedCacheStore(IDistributedCache cache) : ICacheStore
{
    public async Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        await cache.GetStringAsync(key, ct);

    public async Task SetAsync(string key, string value, TimeSpan? ttl, CancellationToken ct = default)
    {
        var options = new DistributedCacheEntryOptions();
        if (ttl is { } lifetime)
        {
            options.SetAbsoluteExpiration(lifetime);
        }

        await cache.SetStringAsync(key, value, options, ct);
    }
}
