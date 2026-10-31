using DataHub.Domain;
using DataHub.Domain.Rules;
using DataHub.Infrastructure.Sources;

using Xunit;

namespace DataHub.Tests.Integration;

/// <summary>
/// Live-engine integration against real servers. Returns early on machines
/// without the engine — the CI pipeline attaches MySQL and SQL Server
/// service containers and passes their connection strings as environment
/// variables (docs/architecture.md § External engines). The dialect wrappers
/// (LIMIT/OFFSET vs OFFSET/FETCH) get consumed here end to end, not just
/// string-checked.
/// </summary>
public sealed class ExternalSourceTests
{
    private static readonly AdoSourceExecutor Executor = new();

    private static string? MysqlConn => Environment.GetEnvironmentVariable("DATAHUB_MYSQL_CONN");

    private static string? SqlServerConn => Environment.GetEnvironmentVariable("DATAHUB_SQLSERVER_CONN");

    private static bool MysqlAttached => !string.IsNullOrWhiteSpace(MysqlConn);

    private static bool SqlServerAttached => !string.IsNullOrWhiteSpace(SqlServerConn);

    private const string StorefrontQuery =
        "SELECT order_state AS state, COUNT(1) AS n FROM storefront_orders GROUP BY order_state ORDER BY n DESC";

    private const string PayrollQuery =
        "SELECT department AS dept, SUM(salary) AS payroll FROM hr_employees GROUP BY department ORDER BY payroll DESC";

    [Fact]
    public async Task MySQL_ping_answers_select_one()
    {
        if (!MysqlAttached)
        {
            return; // skip: set DATAHUB_MYSQL_CONN in CI against the service container
        }

        var result = await Executor.ReadAsync(SourceProvider.MySql, MysqlConn!, SqlDialect.Ping, 5);

        Assert.Single(result.Rows);
        Assert.Equal("1", result.Rows[0][0]);
    }

    [Fact]
    public async Task MySQL_dialect_chain_returns_the_seeded_table()
    {
        if (!MysqlAttached)
        {
            return; // skip: set DATAHUB_MYSQL_CONN in CI against the service container
        }

        var paged = SqlDialect.For(SourceProvider.MySql).Rows(StorefrontQuery, page: 1, pageSize: 10);
        var pagedResult = await Executor.ReadAsync(SourceProvider.MySql, MysqlConn!, paged, 10);

        Assert.Equal(["state", "n"], pagedResult.Columns);
        Assert.NotEmpty(pagedResult.Rows);

        var counted = await Executor.ReadAsync(SourceProvider.MySql, MysqlConn!, SqlDialect.Count(StorefrontQuery), 10);
        Assert.Equal("1", counted.Rows[0][0]); // the seed ships exactly one distinct order state
    }

    [Fact]
    public async Task SQL_Server_ping_answers_select_one()
    {
        if (!SqlServerAttached)
        {
            return; // skip: set DATAHUB_SQLSERVER_CONN in CI against the service container
        }

        var result = await Executor.ReadAsync(SourceProvider.SqlServer, SqlServerConn!, SqlDialect.Ping, 5);

        Assert.Single(result.Rows);
        Assert.Equal("1", result.Rows[0][0]);
    }

    [Fact]
    public async Task SQL_Server_offset_fetch_chain_returns_the_seeded_table()
    {
        if (!SqlServerAttached)
        {
            return; // skip: set DATAHUB_SQLSERVER_CONN in CI against the service container
        }

        var paged = SqlDialect.For(SourceProvider.SqlServer).Rows(PayrollQuery, page: 1, pageSize: 10);
        var result = await Executor.ReadAsync(SourceProvider.SqlServer, SqlServerConn!, paged, 10);

        Assert.Equal(["dept", "payroll"], result.Columns);
        Assert.NotEmpty(result.Rows);
    }
}
