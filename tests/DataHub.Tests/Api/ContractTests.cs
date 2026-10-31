using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using DataHub.Application.Contracts;

using Xunit;

namespace DataHub.Tests.Api;

/// <summary>
/// End-to-end contract tests through the real ASP.NET Core pipeline
/// (routing, JWT binding, authorization, ProblemDetails mapping, ETag/304,
/// cache wall) with the SQLite demo profile. Each test boots an isolated
/// host — the same migrate + seed path a container start exercises.
/// </summary>
public sealed class ContractTests
{
    private static readonly string[] Empty = [];
    private static readonly string[] ViewerRole = ["viewer"];
    private static readonly string[] AnalystRole = ["analyst"];
    private static readonly string[] AnalystAdminRoles = ["Analyst", "admin"];
    private static readonly string[] SupergenuserRole = ["supergenuser"];

    // ── authentication ───────────────────────────────────────────────────────

    [Fact]
    public async Task Anonymous_requests_are_rejected_401()
    {
        await using var host = await TestHost.StartAsync(login: false);

        var response = await host.Client.GetAsync("/api/reports");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_demo_user_cannot_obtain_a_token()
    {
        await using var host = await TestHost.StartAsync("intruder");

        Assert.Equal(HttpStatusCode.Unauthorized, host.LastTokenStatus);
        Assert.Equal("CREDENTIALS_REJECTED", await TestHost.ProblemCodeAsync(host.LastTokenResponse!));
    }

    [Fact]
    public async Task Token_carries_the_oid_and_role_bag()
    {
        await using var host = await TestHost.StartAsync("lucas");

        Assert.Equal(HttpStatusCode.OK, host.LastTokenStatus);
        Assert.Equal("lucas", host.Token!.UserKey);
        Assert.Equal(["analyst"], host.Token.Roles);
    }

    // ── the catalog list is grant-filtered ───────────────────────────────────

    [Fact]
    public async Task Viewer_sees_only_their_granted_reports()
    {
        await using var host = await TestHost.StartAsync("sara");

        var reports = await host.Client.GetFromJsonAsync<List<ReportDto>>("/api/reports");

        Assert.Single(reports!);
        Assert.Equal("revenue_by_region", reports![0].ReportKey);
    }

    [Fact]
    public async Task Analyst_sees_every_granted_report_including_the_mysql_one()
    {
        await using var host = await TestHost.StartAsync("lucas");

        var reports = await host.Client.GetFromJsonAsync<List<ReportDto>>("/api/reports");

        Assert.Equal(5, reports!.Count);
        Assert.Contains(reports, r => r.ReportKey == "mysql_storefront_orders");
        Assert.Contains(reports, r => r.ReportKey == "orders_needing_chase" && !r.IsCacheable);
    }

    [Fact]
    public async Task Admin_sees_all_five_seeded_reports()
    {
        await using var host = await TestHost.StartAsync("nadia");

        var reports = await host.Client.GetFromJsonAsync<List<ReportDto>>("/api/reports");

        Assert.Equal(5, reports!.Count);
    }

    [Fact]
    public async Task Viewer_gets_detail_view_of_a_granted_report()
    {
        await using var host = await TestHost.StartAsync("sara");

        var response = await host.Client.GetAsync("/api/reports/revenue_by_region");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var roles = json.GetProperty("allowedRoles")
            .EnumerateArray()
            .Select(r => r.GetString() ?? "")
            .ToArray();

        Assert.Equal(["admin", "analyst", "viewer"], roles); // ordinal-sorted grant list
    }

    // ── catalog registration (admin editor path) ─────────────────────────────

    [Fact]
    public async Task Viewer_cannot_register_a_report_policy_gate()
    {
        await using var host = await TestHost.StartAsync("sara");

        var response = await host.Client.PostAsJsonAsync("/api/reports", new
        {
            reportKey = "shady_report",
            displayName = "Shady",
            sourceKey = "local_warehouse",
            queryText = "SELECT 1 AS one ORDER BY 1",
            allowedRoles = ViewerRole
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_query_registration_surfaces_422_invalid_query()
    {
        await using var host = await TestHost.StartAsync("nadia");

        var response = await host.Client.PostAsJsonAsync("/api/reports", new
        {
            reportKey = "no_order_report",
            displayName = "No order",
            sourceKey = "local_warehouse",
            queryText = "SELECT region FROM dim_region GROUP BY region",
            allowedRoles = AnalystRole
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("INVALID_QUERY", await TestHost.ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Admin_registers_a_report_and_analyst_can_run_it()
    {
        await using var host = await TestHost.StartAsync("nadia");

        var created = await host.Client.PostAsJsonAsync("/api/reports", new
        {
            reportKey = "region_regions",
            displayName = "Region census",
            sourceKey = "local_warehouse",
            queryText = "SELECT region_name AS region, COUNT(1) AS n FROM dim_region GROUP BY region_name ORDER BY region_name",
            allowedRoles = AnalystAdminRoles,
            cacheSecondsTtl = 9999
        });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = await created.Content.ReadFromJsonAsync<ReportDto>();
        Assert.NotNull(created.Headers.Location);
        Assert.Equal("region_regions", dto!.ReportKey);

        // A different identity on the same booted host sees the new catalog row.
        var analyst = await host.MintAsync("lucas");
        var response = await analyst.PostAsJsonAsync("/api/reports/region_regions/query", new { page = 1, pageSize = 100 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ReportResultDto>();
        Assert.Equal("region_regions", result!.ReportKey);
        Assert.Equal(3600, dto.CacheSecondsTtl); // clamped from 9999 — the catalog keeps rows sane
    }

    [Fact]
    public async Task Duplicate_report_key_is_invalid_report()
    {
        await using var host = await TestHost.StartAsync("nadia");

        var response = await host.Client.PostAsJsonAsync("/api/reports", new
        {
            reportKey = "revenue_by_region",
            displayName = "Revenue by region (dupe)",
            sourceKey = "local_warehouse",
            queryText = "SELECT region FROM dim_region ORDER BY region",
            allowedRoles = AnalystRole
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_REPORT", await TestHost.ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Grant_vocabulary_enforced_on_registration()
    {
        await using var host = await TestHost.StartAsync("nadia");

        var response = await host.Client.PostAsJsonAsync("/api/reports", new
        {
            reportKey = "odd_grants",
            displayName = "Odd grants",
            sourceKey = "local_warehouse",
            queryText = "SELECT region FROM dim_region ORDER BY region",
            allowedRoles = SupergenuserRole
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_REPORT", await TestHost.ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Registration_against_an_unknown_source_is_404()
    {
        await using var host = await TestHost.StartAsync("nadia");

        var response = await host.Client.PostAsJsonAsync("/api/reports", new
        {
            reportKey = "ghost_backed",
            displayName = "Ghost",
            sourceKey = "not_here",
            queryText = "SELECT 1 AS one ORDER BY 1",
            allowedRoles = AnalystRole
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("NOT_FOUND", await TestHost.ProblemCodeAsync(response));
    }

    // ── running a report: rows, cache wall, ETag ─────────────────────────────

    [Fact]
    public async Task Viewer_runs_the_region_revenue_table()
    {
        await using var host = await TestHost.StartAsync("sara");

        var response = await host.Client.PostAsJsonAsync("/api/reports/revenue_by_region/query", new { page = 1, pageSize = 100 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ReportResultDto>();

        Assert.NotNull(result);
        Assert.Equal(["region", "total_amount", "order_count"], result!.Columns);
        Assert.Equal(4, result.TotalCount);
        Assert.Equal(4, result.Rows.Length);
        Assert.False(result.FromCache);
        Assert.Matches("^[0-9A-F]{16}$", result.ETag);
        Assert.Equal(result.ETag, response.Headers.ETag?.Tag.Trim('"'));
        // invariant decimal rendering even on regional machines
        var amounts = result.Rows.Select(r => r[1]).ToArray();
        Assert.All(amounts, a => Assert.DoesNotContain(',', a!));
    }

    [Fact]
    public async Task Second_read_within_the_ttl_is_a_cache_hit()
    {
        await using var host = await TestHost.StartAsync("sara");

        var first = await host.Client.PostAsJsonAsync("/api/reports/revenue_by_region/query", new { page = 1, pageSize = 100 });
        var one = await first.Content.ReadFromJsonAsync<ReportResultDto>();
        var second = await host.Client.PostAsJsonAsync("/api/reports/revenue_by_region/query", new { page = 1, pageSize = 100 });
        var two = await second.Content.ReadFromJsonAsync<ReportResultDto>();

        Assert.False(one!.FromCache);
        Assert.True(two!.FromCache);
        Assert.Equal(one.ETag, two.ETag);
    }

    [Fact]
    public async Task Matching_etag_is_a_real_304()
    {
        await using var host = await TestHost.StartAsync("sara");

        var first = await host.Client.PostAsJsonAsync("/api/reports/revenue_by_region/query", new { page = 1, pageSize = 100 });
        var one = await first.Content.ReadFromJsonAsync<ReportResultDto>();

        host.Client.DefaultRequestHeaders.IfNoneMatch.Add(
            new EntityTagHeaderValue($"\"{one!.ETag}\""));

        var second = await host.Client.PostAsJsonAsync("/api/reports/revenue_by_region/query", new { page = 1, pageSize = 100 });

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Equal(0, (await second.Content.ReadAsStringAsync()).Length);
    }

    [Fact]
    public async Task Paging_writes_the_page_envelope()
    {
        await using var host = await TestHost.StartAsync("lucas");

        var response = await host.Client.PostAsJsonAsync("/api/reports/revenue_by_region/query", new { page = 2, pageSize = 2 });

        var result = await response.Content.ReadFromJsonAsync<ReportResultDto>();

        Assert.Equal(2, result!.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(4, result.TotalCount);

        // page 3 is empty but still shaped
        var third = await host.Client.PostAsJsonAsync("/api/reports/revenue_by_region/query", new { page = 3, pageSize = 2 });
        var pagedOut = await third.Content.ReadFromJsonAsync<ReportResultDto>();
        Assert.Empty(pagedOut!.Rows);
    }

    [Fact]
    public async Task Insane_page_size_is_422()
    {
        await using var host = await TestHost.StartAsync("sara");

        var tooBig = await host.Client.PostAsJsonAsync("/api/reports/revenue_by_region/query", new { page = 1, pageSize = 501 });
        var zero = await host.Client.PostAsJsonAsync("/api/reports/revenue_by_region/query", new { page = 1, pageSize = 0 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooBig.StatusCode);
        Assert.Equal("INVALID_QUERY", await TestHost.ProblemCodeAsync(tooBig));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, zero.StatusCode);
    }

    [Fact]
    public async Task Viewer_cannot_run_an_analyst_only_report_403_permission_denied()
    {
        await using var host = await TestHost.StartAsync("sara");

        var run = await host.Client.PostAsJsonAsync("/api/reports/top_accounts/query", new { page = 1, pageSize = 100 });
        var detail = await host.Client.GetAsync("/api/reports/top_accounts");

        Assert.Equal(HttpStatusCode.Forbidden, run.StatusCode);
        Assert.Equal("PERMISSION_DENIED", await TestHost.ProblemCodeAsync(run));
        Assert.Equal(HttpStatusCode.Forbidden, detail.StatusCode);
    }

    [Fact]
    public async Task Non_cacheable_report_never_serves_from_cache()
    {
        await using var host = await TestHost.StartAsync("lucas");

        var first = await host.Client.PostAsJsonAsync("/api/reports/orders_needing_chase/query", new { page = 1, pageSize = 100, bypassCache = false });
        var second = await host.Client.PostAsJsonAsync("/api/reports/orders_needing_chase/query", new { page = 1, pageSize = 100, bypassCache = true });

        var one = await first.Content.ReadFromJsonAsync<ReportResultDto>();
        var two = await second.Content.ReadFromJsonAsync<ReportResultDto>();

        Assert.False(one!.FromCache);
        Assert.True(two!.FromCache is false);
    }

    [Fact]
    public async Task Unreachable_mysql_source_maps_to_503_source_unavailable()
    {
        await using var host = await TestHost.StartAsync("lucas");

        var response = await host.Client.PostAsJsonAsync("/api/reports/mysql_storefront_orders/query", new { page = 1, pageSize = 100 });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("SOURCE_UNAVAILABLE", await TestHost.ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Disabled_source_blocks_its_reports_with_409()
    {
        await using var host = await TestHost.StartAsync("nadia");
        await host.Client.PostAsync("/api/sources/local_warehouse/disable", content: null);

        var analyst = await host.MintAsync("lucas");
        var response = await analyst.PostAsJsonAsync("/api/reports/revenue_by_region/query", new { page = 1, pageSize = 100 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("SOURCE_DISABLED", await TestHost.ProblemCodeAsync(response));
    }

    // ── harness ──────────────────────────────────────────────────────────────

    /// <summary>A booted host: isolated DB + a client logged in as the demo user.</summary>
    private sealed class TestHost(DataHubApiFactory factory, HttpClient client) : IAsyncDisposable
    {
        public DataHubApiFactory Factory => factory;

        public HttpClient Client => client;

        public TokenResponse? Token { get; private set; }

        public HttpResponseMessage? LastTokenResponse { get; private set; }

        public HttpStatusCode LastTokenStatus { get; private set; }

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

        public static async Task<TestHost> StartAsync(string user = "nadia", bool login = true)
        {
            var factory = new DataHubApiFactory();
            var host = new TestHost(factory, factory.CreateClient());

            if (!login)
            {
                return host;
            }

            var response = await host.Client.PostAsJsonAsync("/api/auth/token", new { userName = user });
            host.LastTokenStatus = response.StatusCode;
            host.LastTokenResponse = response;

            if (response.IsSuccessStatusCode)
            {
                var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
                host.Token = token;
                host.Client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token!.AccessToken);
            }

            return host;
        }

        /// <summary>A second signed-in client sharing the booted host's database.</summary>
        public async Task<HttpClient> MintAsync(string user)
        {
            var client = Factory.CreateClient();
            var response = await client.PostAsJsonAsync("/api/auth/token", new { userName = user });
            response.EnsureSuccessStatusCode();

            var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token!.AccessToken);

            return client;
        }
    }
}
