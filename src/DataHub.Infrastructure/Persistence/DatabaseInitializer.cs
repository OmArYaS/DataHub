using System.Globalization;

using DataHub.Application.Common;
using DataHub.Domain;
using DataHub.Domain.Entities;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DataHub.Infrastructure.Persistence;

/// <summary>
/// Applies the hand-authored migration, then seeds the demo dataset —
/// idempotently (keys checked before insert; warehouse DDL is IF NOT
/// EXISTS). Production deployments run the same apply path from the Azure
/// DevOps release stage; the seed step is demo-profile only.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(DataHubDbContext db, TimeProvider clock, CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);
        await SeedAsync(db, clock, ct);
    }

    private static async Task SeedAsync(DataHubDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        // ── source registry ──────────────────────────────────────────
        await EnsureSourceAsync(db,
            sourceKey: "local_warehouse", displayName: "Local warehouse (demo)",
            description: "File-backed SQLite warehouse bundled with the demo profile — the sandbox revenue mart.",
            provider: SourceProvider.Sqlite, connectionName: "Catalog", commandTimeout: 15, ct: ct);

        await EnsureSourceAsync(db,
            sourceKey: "mysql_retail", displayName: "Retail OLTP (MySQL)",
            description: "MySQL 8 storefront replica — the CI pipeline attaches a container; locally it stays configured but unreachable.",
            provider: SourceProvider.MySql, connectionName: "MysqlRetail", commandTimeout: 20, ct: ct);

        await EnsureSourceAsync(db,
            sourceKey: "azure_sql_finance", displayName: "Finance warehouse (Azure SQL)",
            description: "Azure SQL reader — the demo profile ships it unreachable to keep health reporting honest.",
            provider: SourceProvider.AzureSql, connectionName: "AzureSqlFinance", commandTimeout: 30, ct: ct);

        // ── the local demo warehouse (same SQLite file, warehouse tables) ──
        var connectionString = db.Database.GetConnectionString();
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            await SeedWarehouseAsync(connectionString, ct);
        }

        // ── query catalog ────────────────────────────────────────────
        var local = "local_warehouse";

        await EnsureReportAsync(db, now, reportKey: "revenue_by_region",
            displayName: "Revenue by region",
            description: "Total revenue and order count per warehouse region, biggest first.",
            sourceKey: local,
            queryText: """
                SELECT r.region_name AS region,
                       ROUND(SUM(f.amount), 2) AS total_amount,
                       COUNT(1) AS order_count
                FROM fact_revenue f
                JOIN dim_region r ON r.region_id = f.region_id
                GROUP BY r.region_name
                ORDER BY total_amount DESC
                """,
            roles: [HubRoles.Viewer, HubRoles.Analyst, HubRoles.Admin],
            cacheable: true,
            cacheTtl: 60,
            syncInterval: 60, ct: ct);

        await EnsureReportAsync(db, now, reportKey: "revenue_by_month",
            displayName: "Revenue by month and region",
            description: "Monthly revenue per region — the trendline behind the quarterly deck.",
            sourceKey: local,
            queryText: """
                SELECT f.month_key AS month,
                       r.region_name AS region,
                       ROUND(SUM(f.amount), 2) AS total_amount
                FROM fact_revenue f
                JOIN dim_region r ON r.region_id = f.region_id
                GROUP BY f.month_key, r.region_name
                ORDER BY f.month_key DESC, total_amount DESC
                """,
            roles: [HubRoles.Analyst, HubRoles.Admin],
            cacheable: true,
            cacheTtl: 120,
            syncInterval: null, ct: ct);

        await EnsureReportAsync(db, now, reportKey: "top_accounts",
            displayName: "Top accounts by spend",
            description: "Which accounts move the money — cumulative spend per customer key.",
            sourceKey: local,
            queryText: """
                SELECT f.account_key AS account,
                       ROUND(SUM(f.amount), 2) AS total_spend,
                       COUNT(1) AS order_count
                FROM fact_revenue f
                GROUP BY f.account_key
                ORDER BY total_spend DESC
                """,
            roles: [HubRoles.Analyst, HubRoles.Admin],
            cacheable: true,
            cacheTtl: 60,
            syncInterval: null, ct: ct);

        await EnsureReportAsync(db, now, reportKey: "orders_needing_chase",
            displayName: "Orders needing a chase",
            description: "Open pending orders, freshest first — the analyst morning list.",
            sourceKey: local,
            queryText: """
                SELECT f.month_key AS month,
                       f.account_key AS account,
                       f.amount AS amount,
                       f.order_status AS status,
                       f.created_utc AS created_utc
                FROM fact_revenue f
                WHERE f.order_status = 'pending'
                  AND f.amount >= 0
                ORDER BY f.created_utc DESC, f.fact_id DESC
                """,
            roles: [HubRoles.Analyst, HubRoles.Admin],
            cacheable: false,
            cacheTtl: 60,
            syncInterval: null, ct: ct);

        await EnsureReportAsync(db, now, reportKey: "mysql_storefront_orders",
            displayName: "Storefront orders (MySQL)",
            description: "Live storefront orders from the retail replica — unreachable until the MySQL container is attached.",
            sourceKey: "mysql_retail",
            queryText: """
                SELECT s.order_ref AS order_ref,
                       s.customer_ref AS customer,
                       s.channel AS channel,
                       s.order_total AS total,
                       s.placed_at AS placed
                FROM storefront_orders s
                WHERE s.order_state = 'placed'
                ORDER BY s.placed_at DESC
                """,
            roles: [HubRoles.Analyst, HubRoles.Admin],
            cacheable: true,
            cacheTtl: 300,
            syncInterval: null, ct: ct);

        // ── one historical ledger row so the sync dashboard has history ──
        if (!await db.SyncRuns.AnyAsync(ct))
        {
            db.SyncRuns.Add(new SyncRun
            {
                ReportKey = "revenue_by_region",
                BlobPath = $"datahub-sync/revenue_by_region/{now.AddDays(-1):yyyy/MM/dd}/part-seed.csv",
                RowCount = 4,
                Status = SyncStatus.Completed,
                TriggeredBy = "seed",
                StartedUtc = now.AddMinutes(-90),
                CompletedUtc = now.AddMinutes(-90).AddSeconds(2)
            });

            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task<int> EnsureReportAsync(
        DataHubDbContext db, DateTime now,
        string reportKey, string displayName, string? description,
        string sourceKey, string queryText, string[] roles,
        bool cacheable, int cacheTtl, int? syncInterval,
        CancellationToken ct)
    {
        var existing = await db.Reports
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.ReportKey == reportKey, ct);

        if (existing is not null)
        {
            return existing.Id;
        }

        var report = new ReportDefinition
        {
            ReportKey = reportKey,
            DisplayName = displayName,
            Description = description,
            SourceKey = sourceKey,
            QueryText = queryText,
            Permissions = roles.Select(r => new ReportGrant { RoleName = r }).ToList(),
            IsCacheable = cacheable,
            CacheSecondsTtl = cacheTtl,
            SyncIntervalMinutes = syncInterval,
            RegisteredUtc = now
        };

        db.Reports.Add(report);
        await db.SaveChangesAsync(ct);

        return report.Id;
    }

    private static async Task EnsureSourceAsync(
        DataHubDbContext db,
        string sourceKey, string displayName, string? description,
        SourceProvider provider, string connectionName, int commandTimeout,
        CancellationToken ct)
    {
        if (await db.DataSources.AnyAsync(s => s.SourceKey == sourceKey, ct))
        {
            return;
        }

        db.DataSources.Add(new DataSource
        {
            SourceKey = sourceKey,
            DisplayName = displayName,
            Description = description,
            Provider = provider,
            ConnectionName = connectionName,
            IsEnabled = true,
            CommandTimeoutSeconds = commandTimeout,
            RegisteredUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
    }

    // ── demo warehouse (raw SQLite DDL + deterministic rows) ────────────

    private static async Task SeedWarehouseAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);

        await using (var ddl = connection.CreateCommand())
        {
            ddl.CommandText = """
                CREATE TABLE IF NOT EXISTS dim_region (
                    region_id   INTEGER PRIMARY KEY,
                    region_name TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS fact_revenue (
                    fact_id      INTEGER PRIMARY KEY,
                    region_id    INTEGER NOT NULL REFERENCES dim_region(region_id),
                    account_key  TEXT NOT NULL,
                    month_key    TEXT NOT NULL,
                    amount       REAL NOT NULL,
                    order_status TEXT NOT NULL,
                    created_utc  TEXT NOT NULL
                );
                """;

            await ddl.ExecuteNonQueryAsync(ct);
        }

        var exists = connection.CreateCommand();
        exists.CommandText = "SELECT COUNT(1) FROM fact_revenue";
        var existing = Convert.ToInt64(await exists.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        if (existing > 0)
        {
            return;
        }

        var regions = new[] { "North", "South", "EMEA", "APAC" };

        var insertRegions = connection.CreateCommand();
        var regionValues = string.Join(",\n", regions.Select((_, i) => $"({i + 1}, '{regions[i]}')"));
        insertRegions.CommandText = $"INSERT INTO dim_region (region_id, region_name) VALUES {regionValues}";
        await insertRegions.ExecuteNonQueryAsync(ct);

        var facts = connection.CreateCommand();

        var rows = new List<string>();
        var factId = 0;
        string[] accounts = ["acc_alfa", "acc_bravo", "acc_charlie", "acc_delta", "acc_echo", "acc_foxtrot", "acc_golf", "acc_hotel"];

        foreach (var monthIndex in Enumerable.Range(1, 9))
        {
            var monthKey = $"2026-{monthIndex:D2}";

            foreach (var regionIndex in Enumerable.Range(1, 4))
            {
                for (var n = 0; n < 2; n++)
                {
                    var seed = monthIndex * 100 + regionIndex * 10 + n;
                    var amount = 250 + seed % 90 * 10 + n * 7;
                    var status = seed % 7 == 0 ? "pending" : seed % 3 == 0 ? "delivered" : "placed";
                    var account = accounts[seed % accounts.Length];
                    var created = new DateTime(2026, monthIndex, 3 + n * 2, 9, 0, 0, DateTimeKind.Utc);

                    rows.Add($"({++factId}, {regionIndex}, '{account}', '{monthKey}', {amount}, '{status}', '{created:yyyy-MM-dd HH:mm:ss}')");
                }
            }
        }

        facts.CommandText = """
            INSERT INTO fact_revenue (fact_id, region_id, account_key, month_key, amount, order_status, created_utc)
            VALUES
            """ + string.Join(",\n", rows);
        await facts.ExecuteNonQueryAsync(ct);
    }
}
