using DataHub.Application.Common;
using DataHub.Application.Contracts;
using DataHub.Application.Reports;
using DataHub.Tests.TestSupport;

using Xunit;

namespace DataHub.Tests.Application;

/// <summary>
/// Catalog registration: the admin's editor path with the full validation
/// battery — key syntax, source existence, guard, role vocabulary, dupes,
/// and the clamps that keep catalog rows sane.
/// </summary>
public sealed class ReportCatalogServiceTests : IAsyncLifetime
{
    private TestDb db = null!;
    private TestClock clock = null!;
    private TestDb.SeedResult seed = null!;

    private ReportCatalogService Service => new(db.Db, clock);

    public async Task InitializeAsync()
    {
        db = await TestDb.CreateAsync();
        clock = TestClock.At(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));
        seed = await db.SeedAsync(clock.NowUtc);
    }

    public Task DisposeAsync() => db.DisposeAsync().AsTask();

    private static RegisterReportRequest Request(
        string key = "chase_list",
        string sourceKey = "local_demo",
        string query = "SELECT account AS account, amount AS amount FROM demo_facts ORDER BY amount DESC",
        string[]? roles = null,
        int cacheTtl = 60) => new(key, "Chase list", null, sourceKey, query, roles ?? ["Admin"], CacheSecondsTtl: cacheTtl);

    [Fact]
    public async Task Register_normalizes_and_clamps()
    {
        var dto = await Service.RegisterAsync(Request(
            query: "  SELECT account AS account FROM demo_facts ORDER BY account ASC \n",
            roles: ["Admin", "viewer", "ADMIN"],
            cacheTtl: 9999));

        Assert.Equal("chase_list", dto.ReportKey);
        Assert.Equal(["admin", "viewer"], dto.AllowedRoles); // lowercased + distinct
        Assert.Equal(3600, dto.CacheSecondsTtl);             // clamped from 9999
        Assert.EndsWith("ORDER BY account ASC", dto.QueryText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_key_syntax_is_invalid_report()
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => Service.RegisterAsync(Request(key: "9-bad-key")));

        Assert.Equal(RuleCodes.InvalidReport, ex.Code);
    }

    [Fact]
    public async Task Unknown_source_is_a_404()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => Service.RegisterAsync(Request(sourceKey: "ghost_source")));
    }

    [Fact]
    public async Task Guard_rejection_maps_to_422_invalid_query()
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => Service.RegisterAsync(Request(query: "DELETE FROM demo_facts")));

        Assert.Equal(RuleCodes.InvalidQuery, ex.Code);
        Assert.Equal(422, ex.StatusCode);
        Assert.Equal("Only SELECT statements are catalogued.", ex.Message);
    }

    [Fact]
    public async Task Duplicate_key_is_invalid_report()
    {
        await Service.RegisterAsync(Request());

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => Service.RegisterAsync(Request()));

        Assert.Equal(RuleCodes.InvalidReport, ex.Code);
        Assert.Contains("chase_list", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_grants_are_rejected()
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => Service.RegisterAsync(Request(roles: ["   "])));

        Assert.Equal(RuleCodes.InvalidReport, ex.Code);
    }

    [Fact]
    public async Task Grants_outside_the_vocabulary_are_rejected_by_name()
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => Service.RegisterAsync(Request(roles: ["viewer", "supergod"])));

        Assert.Contains("super", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Listing_is_filtered_by_the_caller_grants()
    {
        await Service.RegisterAsync(Request(key: "admin_only_report", roles: ["admin"]));

        var viewer = await Service.ListAsync(FixedUser.Viewer);
        var admin = await Service.ListAsync(FixedUser.Admin);

        Assert.DoesNotContain(viewer, r => r.ReportKey == "admin_only_report");
        Assert.Contains(admin, r => r.ReportKey == "admin_only_report");
        Assert.Contains(viewer, r => r.ReportKey == seed.ViewerReportKey);
    }
}
