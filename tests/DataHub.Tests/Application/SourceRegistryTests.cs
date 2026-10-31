using Microsoft.Extensions.Configuration;

using DataHub.Application.Common;
using DataHub.Application.Contracts;
using DataHub.Application.Sources;
using DataHub.Domain;
using DataHub.Infrastructure.Sources;
using DataHub.Tests.TestSupport;

using Xunit;

namespace DataHub.Tests.Application;

/// <summary>
/// Registry battery: the secret-masking contract (config key travels, the
/// live string does not), key-format validation, and the ping verdicts the
/// admin console buttons render.
/// </summary>
public sealed class SourceRegistryTests : IAsyncLifetime
{
    private TestDb db = null!;
    private TestClock clock = null!;
    private FakeSourceExecutor executor = null!;
    private SourceRegistryService service = null!;

    public async Task InitializeAsync()
    {
        db = await TestDb.CreateAsync();
        clock = TestClock.At(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));
        await db.SeedAsync(clock.NowUtc);
        executor = new FakeSourceExecutor();
        service = new SourceRegistryService(
            db.Db,
            new ConfiguredSourceConnectionResolver(BuildConfig()),
            executor,
            clock);
    }

    public Task DisposeAsync() => db.DisposeAsync().AsTask();

    private static IConfigurationRoot BuildConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sources:Demo:ConnectionString"] = "Data Source=demo.db",
            ["Empty"] = ""
        })
        .Build();

    [Fact]
    public async Task Resolver_reads_the_config_slot_by_connection_name()
    {
        var resolver = new ConfiguredSourceConnectionResolver(BuildConfig());

        Assert.Equal("Data Source=demo.db", resolver.Resolve("Demo"));
        Assert.True(resolver.IsConfigured("Demo"));
        Assert.False(resolver.IsConfigured("Never"));
    }

    [Fact]
    public async Task Resolver_refuses_a_blank_slot()
    {
        var resolver = new ConfiguredSourceConnectionResolver(
            new ConfigurationBuilder().AddInMemoryCollection([]).Build());

        var ex = Assert.Throws<InvalidOperationException>(() => resolver.Resolve("Demo"));

        // the wording is the same one the API surfaces as SOURCE_NOT_CONFIGURED detail
        Assert.Contains("Sources:Demo:ConnectionString", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_shows_configured_flag_never_the_secret()
    {
        var sources = await service.ListAsync();

        var demo = Assert.Single(sources);
        Assert.Equal("local_demo", demo.SourceKey);
        Assert.True(demo.ConnectionConfigured);
    }

    [Fact]
    public async Task Register_validates_key_display_and_slot()
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.RegisterAsync(new RegisterSourceRequest("Bad key!", "X", null, "mysqL", "Ghost")));

        Assert.Equal(RuleCodes.InvalidSource, ex.Code);
    }

    [Fact]
    public async Task Register_refuses_a_slot_with_no_configured_secret()
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.RegisterAsync(new RegisterSourceRequest(
                "fresh_warehouse", "Fresh warehouse", null, "mysql", "Ghost")));

        Assert.Equal(RuleCodes.InvalidSource, ex.Code);
        Assert.Contains("Sources:Ghost", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Register_accepts_a_provider_case_insensitively()
    {
        var dto = await service.RegisterAsync(new RegisterSourceRequest(
            "second_warehouse", "Second warehouse", null, "Mysql", "Demo"));

        Assert.Equal("second_warehouse", dto.SourceKey);
    }

    [Fact]
    public async Task Dupe_key_is_invalid_source()
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.RegisterAsync(new RegisterSourceRequest(
                "local_demo", "Local demo again", null, "sqlite", "Demo")));

        Assert.Equal(RuleCodes.InvalidSource, ex.Code);
    }

    // ── the /test ping ───────────────────────────────────────────────────────

    [Fact]
    public async Task Test_reports_reachable_for_a_running_source()
    {
        var health = await service.TestAsync("local_demo");

        Assert.True(health.Reachable);
        Assert.Null(health.FailureCode);
        Assert.Equal(SourceProvider.Sqlite, executor.LastProvider);
    }

    [Fact]
    public async Task Test_maps_engine_failure_to_source_unavailable()
    {
        executor.AlwaysFail = true;

        var health = await service.TestAsync("local_demo");

        Assert.False(health.Reachable);
        Assert.Equal(RuleCodes.SourceUnavailable, health.FailureCode);
    }

    [Fact]
    public async Task Test_unknown_key_is_a_404()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => service.TestAsync("ghost"));
    }
}
