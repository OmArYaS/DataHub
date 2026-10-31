using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using DataHub.Application.Contracts;

using Xunit;

namespace DataHub.Tests.Api;

/// <summary>
/// Source-registry surface and health endpoints: secrets masked to the
/// configured/not-configured bit, admin policies on mutations, ping
/// verdicts for a live and an unreachable engine, and the readiness/
/// degradation roles of /health vs /health/all.
/// </summary>
public sealed class SourceAndHealthContractTests
{
    // ── secrets never cross the wire ──────────────────────────────────────────

    [Fact]
    public async Task Source_list_masks_connection_strings()
    {
        await using var host = await TestHost.StartAsync("sara");

        var response = await host.Client.GetAsync("/api/sources");
        var raw = await response.Content.ReadAsStringAsync();
        var sources = await host.Client.GetFromJsonAsync<List<SourceDto>>("/api/sources");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(sources);
        Assert.All(sources!, s => Assert.True(s.ConnectionConfigured));
        Assert.Contains("local_warehouse", sources.Select(s => s.SourceKey).ToArray());
        Assert.DoesNotContain("not-a-real-secret", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Viewer_cannot_register_or_test_sources()
    {
        await using var host = await TestHost.StartAsync("sara");

        var register = await host.Client.PostAsJsonAsync("/api/sources", new
        {
            sourceKey = "peek_source",
            displayName = "Peek",
            provider = "mysql",
            connectionName = "Demo"
        });
        var test = await host.Client.PostAsync("/api/sources/local_warehouse/test", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, register.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, test.StatusCode);
    }

    [Fact]
    public async Task Registration_refuses_a_slot_without_a_secret()
    {
        await using var host = await TestHost.StartAsync("nadia");

        var response = await host.Client.PostAsJsonAsync("/api/sources", new
        {
            sourceKey = "postgre_oltp",
            displayName = "Postgres OLTP",
            provider = "mysql",
            connectionName = "Never"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_SOURCE", await TestHost.ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Ping_versus_a_live_sqlite_source_reports_reachable()
    {
        await using var host = await TestHost.StartAsync("nadia");

        var response = await host.Client.PostAsJsonAsync("/api/sources/local_warehouse/test", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var health = await response.Content.ReadFromJsonAsync<SourceHealthDto>();

        Assert.True(health!.Reachable);
        Assert.True(health.ElapsedMilliseconds >= 0);
    }

    [Fact]
    public async Task Ping_versus_the_unreachable_mysql_replica_reports_the_failure_code()
    {
        await using var host = await TestHost.StartAsync("nadia");

        var response = await host.Client.PostAsJsonAsync("/api/sources/mysql_retail/test", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // the *ping* completed; the source is down
        var health = await response.Content.ReadFromJsonAsync<SourceHealthDto>();

        Assert.False(health!.Reachable);
        Assert.Equal("SOURCE_UNAVAILABLE", health.FailureCode);
    }

    [Fact]
    public async Task Unknown_source_ping_is_a_404()
    {
        await using var host = await TestHost.StartAsync("nadia");

        var response = await host.Client.PostAsync("/api/sources/ghost/test", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("NOT_FOUND", await TestHost.ProblemCodeAsync(response));
    }

    // ── health endpoints ─────────────────────────────────────────────────────

    [Fact]
    public async Task Ready_probe_is_green_when_the_catalog_answers()
    {
        var factory = new DataHubApiFactory();
        await using var _ = factory;

        var response = await factory.CreateClient().GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Healthy", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Full_health_degrades_but_never_gates_the_platform()
    {
        var factory = new DataHubApiFactory();
        await using var _ = factory;

        var response = await factory.CreateClient().GetAsync("/health/all");
        var body = await response.Content.ReadAsStringAsync();

        // Degraded federated sources still answer 200 — readiness is not their job.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Degraded", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Healthy", body, StringComparison.OrdinalIgnoreCase);
    }

    // ── harness ──────────────────────────────────────────────────────────────

    /// <summary>A booted host: isolated DB + a client logged in as the demo user.</summary>
    private sealed class TestHost(DataHubApiFactory factory, HttpClient client) : IAsyncDisposable
    {
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
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token!.AccessToken);

            return new TestHost(factory, client);
        }
    }
}
