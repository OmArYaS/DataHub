namespace DataHub.Application.Common;

/// <summary>
/// Tiny cache abstraction so the application layer stays framework-free:
/// the API/infrastructure side binds it to IDistributedCache — Redis in
/// production, the in-memory provider in demos and contract tests
/// (docs/architecture.md § Caching).
/// </summary>
public interface ICacheStore
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    Task SetAsync(string key, string value, TimeSpan? ttl, CancellationToken ct = default);
}
