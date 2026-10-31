using System.Globalization;

using DataHub.Domain.Entities;

namespace DataHub.Domain.Rules;

/// <summary>
/// Translates provider-neutral paging/count constructs into each engine's
/// syntax. The piece that makes "unifying SQL Server / Azure SQL / MySQL"
/// more than a claim: one catalog SELECT, three dialects, and no user text
/// ever concatenated into the wrapper (only integers from validated paging).
/// </summary>
public sealed class SqlDialect(SourceProvider provider)
{
    public static SqlDialect For(SourceProvider provider) => new(provider);

    /// <summary>`SELECT 1` — the hello-world every connector pings with.</summary>
    public static string Ping => "SELECT 1";

    /// <summary>
    /// Rows for one page: wraps the catalog SELECT in a derived table, orders
    /// by the first output column (verified present by ReadOnlyQueryGuard's
    /// ORDER BY contract so wrapper ordering cannot scramble row identity).
    /// </summary>
    public string Rows(string catalogQuery, int page, long pageSize)
    {
        var skip = (page - 1) * pageSize;

        return provider switch
        {
            SourceProvider.MySql or SourceProvider.Sqlite =>
                $"SELECT * FROM ({catalogQuery}) AS sub ORDER BY 1 LIMIT {Num(pageSize)} OFFSET {Num((long)skip)}",
            _ =>
                // SQL Server / Azure SQL: OFFSET...FETCH (ORDER BY is mandatory).
                $"SELECT * FROM ({catalogQuery}) AS sub ORDER BY 1 OFFSET {Num((long)skip)} ROWS FETCH NEXT {Num(pageSize)} ROWS ONLY"
        };
    }

    /// <summary>Total rows for the same wrapped query — the body of the paging envelope.</summary>
    public static string Count(string catalogQuery) =>
        $"SELECT COUNT(1) FROM ({catalogQuery}) AS sub";

    /// <summary>Invariant-culture integer rendering for wrapper literals.</summary>
    private static string Num(long value) => value.ToString(CultureInfo.InvariantCulture);
}
