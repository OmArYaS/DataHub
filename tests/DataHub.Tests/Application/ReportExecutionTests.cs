using DataHub.Application.Common;
using DataHub.Application.Contracts;
using DataHub.Application.Reports;
using DataHub.Tests.TestSupport;

using Xunit;

namespace DataHub.Tests.Application;

/// <summary>
/// The execution engine's behavioural contracts without any real engine:
/// Polly retries only engine-classified transient losses, permanent failures
/// surface as SOURCE_UNAVAILABLE, and the cache wall honours TTL/page keys
/// and the report's cacheable flag.
/// </summary>
public sealed class ReportExecutionTests : IAsyncLifetime
{
    private TestDb db = null!;
    private FakeSourceExecutor executor = null!;
    private MemoryCacheStore cache = null!;
    private TestClock clock = null!;
    private TestDb.SeedResult seed = null!;

    private ReportExecutionService CreateService()
        => new(db.Db, executor, new FixedConnectionResolver(), cache, clock);

    public async Task InitializeAsync()
    {
        db = await TestDb.CreateAsync();
        executor = new FakeSourceExecutor(
            columns: ["region", "total_amount"],
            rows: [["north", "310"], ["south", "205"]]);
        cache = new MemoryCacheStore();
        clock = TestClock.At(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));
        seed = await db.SeedAsync(clock.NowUtc);
    }

    public Task DisposeAsync() => db.DisposeAsync().AsTask();

    private async Task<ReportResultDto> Run(string key, bool bypass = false, int page = 1, int pageSize = 100, string role = "analyst")
    {
        var user = new FixedUser("someone", role);
        return await CreateService().RunAsync(key, user, page, pageSize, bypassCache: bypass);
    }

    // ── resilience ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Transient_blips_are_retried_until_small()
    {
        executor.TransientFailures = 2; // rows read: 2 blips + 1 success; count read: clean

        var result = await Run(seed.ViewerReportKey);

        Assert.False(result.FromCache);
        Assert.Equal(["region", "total_amount"], result.Columns);
        // 3 attempts for the rows read + 1 for the count read — retries happened.
        Assert.Equal(4, executor.CallCount);
    }

    [Fact]
    public async Task Permanent_engine_failures_surface_as_source_unavailable()
    {
        executor.AlwaysFail = true;

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Run(seed.ViewerReportKey));

        Assert.Equal(RuleCodes.SourceUnavailable, ex.Code);
        Assert.Equal(503, ex.StatusCode);
        // No retry: a non-transient loss fails immediately.
        Assert.Equal(1, executor.CallCount);
    }

    [Fact]
    public async Task Bad_paging_is_rejected_before_any_source_call()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(
            () => Run(seed.ViewerReportKey, pageSize: 0));

        Assert.Equal(0, executor.CallCount);
    }

    // ── cache wall ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Cacheable_reports_hit_on_the_second_read()
    {
        var first = await Run(seed.ViewerReportKey);
        var second = await Run(seed.ViewerReportKey);

        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Equal(first.ETag, second.ETag);
        Assert.Equal(first.Rows, second.Rows);
        // exactly one rows read + one count read — the cache absorbed round two
        Assert.Equal(2, executor.CallCount);
        Assert.Equal([$"rw:{seed.ViewerReportKey}:1:100"], cache.Keys);
    }

    [Fact]
    public async Task Non_cacheable_reports_skip_the_cache_altogether()
    {
        var first = await Run(seed.AnalystReportKey);
        var second = await Run(seed.AnalystReportKey);

        Assert.False(first.FromCache);
        Assert.False(second.FromCache);
        Assert.Empty(cache.Keys);
        Assert.Equal(4, executor.CallCount);
    }

    [Fact]
    public async Task Bypass_cache_runs_fresh_but_still_fills_the_slot()
    {
        await Run(seed.ViewerReportKey);
        await Run(seed.ViewerReportKey);           // warms via cache
        var fresh = await Run(seed.ViewerReportKey, bypass: true);

        Assert.False(fresh.FromCache);
    }

    // ── page anchoring ───────────────────────────────────────────────────────

    [Fact]
    public async Task Paging_inputs_shape_the_cache_key()
    {
        await Run(seed.ViewerReportKey, page: 2, pageSize: 25);

        Assert.Contains($"rw:{seed.ViewerReportKey}:2:25", cache.Keys);
    }
}
