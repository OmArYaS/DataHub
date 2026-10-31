using Microsoft.Data.Sqlite;

using DataHub.Domain;
using DataHub.Infrastructure.Sources;

using Xunit;

namespace DataHub.Tests.Infrastructure;

/// <summary>
/// The concrete SQLite roundtrip: columns, typed cells and nulls leave as
/// invariant strings — the exact mapping the API payload relies on. The
/// heavier engines (MySQL/SQL Server) are exercised by the env-gated
/// Integration suite against CI containers.
/// </summary>
public sealed class AdoSourceExecutorTests
{
    private static async Task<string> CreateWarehouseFileAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"datahub-warehouse-{Guid.NewGuid():N}.db");

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();

        await using var ddl = connection.CreateCommand();
        ddl.CommandText = """
                CREATE TABLE demo_facts (
                    fact_id   INTEGER PRIMARY KEY,
                    region    TEXT NOT NULL,
                    amount    REAL NOT NULL,
                    booked    TEXT NOT NULL,
                    note      TEXT
                );

                INSERT INTO demo_facts (fact_id, region, amount, booked, note) VALUES
                (1, 'north', 120.5, '2026-03-01 09:15:00', 'spot'),
                (2, 'south', 90,    '2026-03-02 10:00:00', NULL);
            """;
        await ddl.ExecuteNonQueryAsync();

        return path;
    }

    private static readonly AdoSourceExecutor Executor = new();

    [Fact]
    public async Task Columns_and_cells_come_back_as_strings()
    {
        var path = await CreateWarehouseFileAsync();
        var result = await Executor.ReadAsync(
            SourceProvider.Sqlite, $"Data Source={path}",
            "SELECT fact_id, region, amount FROM demo_facts ORDER BY fact_id", 10);

        Assert.Equal(["fact_id", "region", "amount"], result.Columns);
        Assert.Equal("1", result.Rows[0][0]);
        Assert.Equal("north", result.Rows[0][1]);
        // invariant decimal digits — never "120,50"
        Assert.Equal("120.5", result.Rows[0][2]);
        Assert.Equal("2", result.Rows[1][0]);
        Assert.Equal("90", result.Rows[1][2]);
    }

    [Fact]
    public async Task Null_cells_stay_null()
    {
        var path = await CreateWarehouseFileAsync();

        var result = await Executor.ReadAsync(
            SourceProvider.Sqlite, $"Data Source={path}",
            "SELECT note FROM demo_facts ORDER BY fact_id", 10);

        Assert.Equal("spot", result.Rows[0][0]);
        Assert.Null(result.Rows[1][0]);
    }

    [Fact]
    public async Task Datetime_cells_render_the_canonical_shape()
    {
        var path = await CreateWarehouseFileAsync();

        var result = await Executor.ReadAsync(
            SourceProvider.Sqlite, $"Data Source={path}",
            "SELECT booked FROM demo_facts ORDER BY fact_id", 10);

        Assert.Equal("2026-03-01 09:15:00", result.Rows[0][0]);
    }

    [Fact]
    public async Task Count_wrap_beats_the_grouping()
    {
        var path = await CreateWarehouseFileAsync();
        // the total for revenue_by_region is 4 seeded fact rows in the demo; here 2
        var wrapped = "SELECT COUNT(1) FROM (SELECT fact_id FROM demo_facts GROUP BY fact_id) AS sub";

        var result = await Executor.ReadAsync(SourceProvider.Sqlite, $"Data Source={path}", wrapped, 10);

        Assert.Single(result.Rows);
        Assert.Equal("2", result.Rows[0][0]);
    }

    [Fact]
    public async Task Engine_failure_propagates_as_db_exception()
    {
        var path = await CreateWarehouseFileAsync();

        // A syntax error against a genuinely missing object — the caller maps
        // this to SOURCE_UNAVAILABLE (conventionally wrapped by the retry wall).
        var ex = await Assert.ThrowsAsync<SqliteException>(() => Executor.ReadAsync(
            SourceProvider.Sqlite, $"Data Source={path}",
            "SELECT * FROM nowhere_delegate", 10));

        Assert.NotNull(ex);
    }
}
