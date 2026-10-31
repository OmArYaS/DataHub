using System.Collections.Concurrent;

using DataHub.Application.Common;

namespace DataHub.Tests.TestSupport;

/// <summary>
/// In-memory ICacheStore — the HIT/MISS tests' recorder. Keys and values are
/// kept verbatim so assertions can check the key contract (rw:{report}:{page}).
/// </summary>
public sealed class MemoryCacheStore : ICacheStore
{
    private readonly ConcurrentDictionary<string, string> entries = new(StringComparer.Ordinal);

    private int misses;

    public IReadOnlyCollection<string> Keys => entries.Keys.ToArray();

    public int Misses => misses;

    public Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        Interlocked.Increment(ref misses);
        return Task.FromResult(entries.TryGetValue(key, out var value) ? value : null);
    }

    public Task SetAsync(string key, string value, TimeSpan? ttl, CancellationToken ct = default)
    {
        entries[key] = value;
        return Task.CompletedTask;
    }

    public void Clear() => entries.Clear();
}
