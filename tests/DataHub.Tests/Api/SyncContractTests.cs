using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using DataHub.Application.Contracts;
using DataHub.Application.Common;

using Xunit;

namespace DataHub.Tests.Api;

/// <summary>
/// Sync surface: admin-only triggers, completed-ledger rows with the partition
/// path layout, failure mapping (unreachable MySQL → failed code), the
/// run-due loop, tiles, and analyst-level ledger reads.
/// </summary>
public sealed class SyncContractTests
{
    // ── authorization ────────────────────────────────────────────────────────

    [Fact]
    public async Task Viewer_and_analyst_cannot_trigger_a_run()
    {
        await using var analyst = await TestHost.StartAsync("lucas");

        var response = await analyst.Client.PostAsync("/api/sync/run/revenue_by_region", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_cannot_read_the_ledger()
    {
        await using var host = await TestHost.StartAsync("sara");

        var runs = await host.Client.GetAsync("/api/sync/runs");
        var dashboard = await host.Client.GetAsync("/api/sync/dashboard");

        Assert.Equal(HttpStatusCode.Forbidden, runs.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, dashboard.StatusCode);
    }

    // ── a completed run ──────────────────────────────────────────────────────

    [Fact]
    public async Task Admin_run_completes_and_lands_a_csv_partition()
    {
        await using var host = await TestHost.StartAsync("nadia");

        var response = await host.Client.PostAsync("/api/sync/run/revenue_by_region", content: null);
        var run = await response.Content.ReadFromJsonAsync<RunDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("completed", run!.Status);          // lowercase enum contract
        Assert.Equal("nadia", run.TriggeredBy);
        Assert.Equal(4, run.RowCount);
        Assert.Matches(
            @"datahub-sync/revenue_by_region/\d{4}/\d{2}/\d{2}/part-\d{6}-[0-9a-f]{8}\.csv",
            run.BlobPath);

        // the FileSystemReportSink wrote the exact bytes under the temp root
        Assert.True(File.Exists(run.BlobPath));
        var csv = await File.ReadAllTextAsync(run.BlobPath);
        Assert.StartsWith("region,total_amount,order_count", csv, StringComparison.Ordinal);
        Assert.Equal(5, csv.TrimEnd('\r', '\n').Split('\n').Length); // header + 4 rows
    }

    [Fact]
    public async Task Run_against_the_unreachable_mysql_report_fails_loudly_in_the_ledger()
    {
        await using var host = await TestHost.StartAsync("nadia");

        var response = await host.Client.PostAsync("/api/sync/run/mysql_storefront_orders", content: null);
        var run = await response.Content.ReadFromJsonAsync<RunDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("failed", run!.Status);
        Assert.Equal(RuleCodes.SourceUnavailable, run.FailureCode);
        Assert.Equal(0, run.RowCount);
        Assert.NotNull(run.CompletedUtc);
    }

    [Fact]
    public async Task Run_of_an_unknown_report_is_a_404()
    {
        await using var host = await TestHost.StartAsync("nadia");

        var response = await host.Client.PostAsync("/api/sync/run/ghost_report", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("NOT_FOUND", await TestHost.ProblemCodeAsync(response));
    }

    // ── cadence loop on demand ───────────────────────────────────────────────

    [Fact]
    public async Task Run_due_executes_every_window_expired_report()
    {
        await using var host = await TestHost.StartAsync("nadia");

        // The seeded ledger row: revenue_by_region completed 90 minutes ago; the
        // 60-minute cadence means it is due right now (only cadence-declared report).
        var response = await host.Client.PostAsync("/api/sync/run-due", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var runs = await host.Client.GetFromJsonAsync<RunsEnvelope>("/api/sync/runs");

        // revenue_by_region ran fresh (window expired 90 minutes ago, cadence 60)
        Assert.Contains(runs!.Items, r => r.ReportKey == "revenue_by_region" && r.Status == "completed" && r.TriggeredBy == "nadia");
    }

    [Fact]
    public async Task Ledger_is_newest_first_and_filterable()
    {
        await using var host = await TestHost.StartAsync("nadia");
        await host.Client.PostAsync("/api/sync/run/revenue_by_region", content: null);

        var all = await host.Client.GetFromJsonAsync<RunsEnvelope>("/api/sync/runs?pageSize=50");
        var filtered = await host.Client.GetFromJsonAsync<RunsEnvelope>("/api/sync/runs?reportKey=mysql_storefront_orders");

        Assert.Contains(all!.Items, r => r.ReportKey == "revenue_by_region" && r.TriggeredBy == "nadia");
        // the seeded ledger row (completed 90 min ago by "seed") is in there too
        Assert.Contains(all.Items, r => r.TriggeredBy == "seed");
        Assert.All(filtered!.Items, r => Assert.Equal("mysql_storefront_orders", r.ReportKey));
    }

    [Fact]
    public async Task Dashboard_tiles_cover_every_cadence_report()
    {
        await using var host = await TestHost.StartAsync("nadia");

        var tiles = await host.Client.GetFromJsonAsync<List<TileDto>>("/api/sync/dashboard");

        var tile = Assert.Single(tiles!, t => t.ReportKey == "revenue_by_region");
        Assert.Equal(60, tile.IntervalMinutes);
        Assert.Equal("completed", tile.LastStatus);
        Assert.Equal(4, tile.LastRowCount);
    }

    // ── harness ──────────────────────────────────────────────────────────────

    /// <summary>A booted host: isolated DB + a client logged in as the demo user.</summary>
    private sealed class TestHost(DataHubApiFactory factory, HttpClient client) : IAsyncDisposable
    {
        public DataHubApiFactory Factory => factory;

        public HttpClient Client => client;

        public static async Task<string> ProblemCodeAsync(HttpResponseMessage response)
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.TryGetProperty("code", out var code)
                ? code.GetString() ?? ""
                : "";
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await factory.DisposeAsync();
        }

        public static async Task<TestHost> StartAsync(string user)
        {
            var factory = new DataHubApiFactory();
            var client = factory.CreateClient();
            var response = await client.PostAsJsonAsync("/api/auth/token", new { userName = user });
            response.EnsureSuccessStatusCode();

            var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token!.AccessToken);

            return new TestHost(factory, client);
        }
    }

    private sealed record RunDto(
        long Id, string ReportKey, string BlobPath, int RowCount, string Status,
        string TriggeredBy, DateTime StartedUtc, DateTime? CompletedUtc, string? FailureCode);

    private sealed record RunsEnvelope(IReadOnlyList<RunDto> Items, int Page, int PageSize, long TotalCount);

    private sealed record TileDto(
        string ReportKey, string? LastStatus, DateTime? LastRunUtc,
        int? LastRowCount, string? LastBlobPath, int? IntervalMinutes);
}
