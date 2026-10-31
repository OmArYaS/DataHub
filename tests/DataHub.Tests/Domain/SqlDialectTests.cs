using DataHub.Domain;
using DataHub.Domain.Rules;

using Xunit;

namespace DataHub.Tests.Domain;

/// <summary>
/// Dialect translations for the three engines the hub federates. The wrapper
/// ordering (ORDER BY 1) and invariant page arithmetic are pinned here —
/// locale-dependent integers or missing ORDER BY inside the wrapper would
/// scramble page identity downstream.
/// </summary>
public sealed class SqlDialectTests
{
    private const string Query =
        "SELECT region AS region, SUM(amount) AS total FROM demo_facts GROUP BY region ORDER BY total DESC";

    // ── ping ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Ping_is_a_bare_select_one()
    {
        Assert.Equal("SELECT 1", SqlDialect.Ping);
    }

    // ── page wrappers ────────────────────────────────────────────────────────

    [Fact]
    public void MySql_pages_with_limit_and_offset()
    {
        var sql = SqlDialect.For(SourceProvider.MySql).Rows(Query, page: 2, pageSize: 25);

        Assert.Equal(
            "SELECT * FROM (" + Query + ") AS sub ORDER BY 1 LIMIT 25 OFFSET 25",
            sql);
    }

    [Fact]
    public void Sqlite_pages_like_mysql()
    {
        var sql = SqlDialect.For(SourceProvider.Sqlite).Rows(Query, page: 1, pageSize: 50);

        Assert.Equal(
            "SELECT * FROM (" + Query + ") AS sub ORDER BY 1 LIMIT 50 OFFSET 0",
            sql);
    }

    [Fact]
    public void SqlServer_pages_with_offset_and_fetch()
    {
        var sql = SqlDialect.For(SourceProvider.SqlServer).Rows(Query, page: 3, pageSize: 25);

        Assert.Equal(
            "SELECT * FROM (" + Query + ") AS sub ORDER BY 1 OFFSET 50 ROWS FETCH NEXT 25 ROWS ONLY",
            sql);
    }

    [Fact]
    public void AzureSql_rides_the_sqlserver_dialect()
    {
        var sql = SqlDialect.For(SourceProvider.AzureSql).Rows(Query, page: 2, pageSize: 10);

        Assert.Contains("OFFSET 10 ROWS FETCH NEXT 10 ROWS ONLY", sql, StringComparison.Ordinal);
    }

    // ── count wrap ───────────────────────────────────────────────────────────

    [Fact]
    public void Count_wraps_the_query_in_a_derived_table()
    {
        Assert.Equal(
            "SELECT COUNT(1) FROM (" + Query + ") AS sub",
            SqlDialect.Count(Query));
    }

    [Fact]
    public void Page_numbers_render_locale_proof_integers()
    {
        var sql = SqlDialect.For(SourceProvider.SqlServer).Rows(Query, page: 7, pageSize: 90);

        Assert.Contains("OFFSET 540 ROWS", sql, StringComparison.Ordinal);
    }
}
