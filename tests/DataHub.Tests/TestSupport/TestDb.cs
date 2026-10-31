using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using DataHub.Application.Common;
using DataHub.Application.Sources;
using DataHub.Domain;
using DataHub.Domain.Entities;
using DataHub.Infrastructure.Persistence;

namespace DataHub.Tests.TestSupport;

/// <summary>
/// Unit-test persistence host: a real SQLite catalog held open in memory
/// (same provider family the API runs — no dual-provider tricks), schema
/// from the shared entity configurations, plus a tiny deterministic seed.
/// </summary>
public sealed class TestDb : IAsyncDisposable
{
    private readonly SqliteConnection connection;

    private TestDb(SqliteConnection connection, DataHubDbContext db)
    {
        this.connection = connection;
        this.Db = db;
    }

    public DataHubDbContext Db { get; }

    public static async Task<TestDb> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<DataHubDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new DataHubDbContext(options);
        await db.Database.EnsureCreatedAsync();

        return new TestDb(connection, db);
    }

    /// <summary>One SQLite source + three reports covering the grant matrix.</summary>
    public async Task<SeedResult> SeedAsync(DateTimeOffset? nowUtc = null)
    {
        var now = (nowUtc ?? DateTimeOffset.UtcNow).UtcDateTime;

        var source = new DataSource
        {
            SourceKey = "local_demo",
            DisplayName = "Local demo source",
            Provider = SourceProvider.Sqlite,
            ConnectionName = "Demo",
            IsEnabled = true,
            CommandTimeoutSeconds = 10,
            RegisteredUtc = now
        };

        var viewerReport = new ReportDefinition
        {
            ReportKey = "region_revenue",
            DisplayName = "Region revenue",
            SourceKey = source.SourceKey,
            QueryText = "SELECT region AS region, SUM(amount) AS total FROM demo_facts GROUP BY region ORDER BY total DESC",
            Permissions = HubRoles.All.Select(r => new ReportGrant { RoleName = r }).ToList(),
            IsCacheable = true,
            CacheSecondsTtl = 60,
            RegisteredUtc = now
        };

        var analystReport = new ReportDefinition
        {
            ReportKey = "pending_chase",
            DisplayName = "Pending chase list",
            SourceKey = source.SourceKey,
            QueryText = "SELECT account AS account, amount AS amount FROM demo_facts WHERE state = 'pending' ORDER BY created DESC",
            Permissions = [new ReportGrant { RoleName = HubRoles.Analyst }],
            IsCacheable = false,
            CacheSecondsTtl = 60,
            RegisteredUtc = now
        };

        Db.DataSources.Add(source);
        Db.Reports.AddRange(viewerReport, analystReport);
        await Db.SaveChangesAsync();

        return new SeedResult(source.SourceKey, viewerReport.ReportKey, analystReport.ReportKey);
    }

    public ValueTask DisposeAsync()
    {
        Db.Dispose();
        connection.Dispose();
        return ValueTask.CompletedTask;
    }
    public sealed record SeedResult(string SourceKey, string ViewerReportKey, string AnalystReportKey);
}

/// <summary>Resolver that always answers — the unit execution tests never reach a real engine.</summary>
public sealed class FixedConnectionResolver(string connectionString = "Data Source=demo.db") : ISourceConnectionResolver
{
    public string Resolve(string connectionName) => connectionString;

    public bool IsConfigured(string connectionName) => true;
}
