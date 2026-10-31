using System.Data;
using System.Data.Common;
using System.Globalization;

using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

using MySqlConnector;

using DataHub.Application.Sources;
using DataHub.Domain;

namespace DataHub.Infrastructure.Sources;

/// <summary>
/// The concrete gateway into every registered engine. One implementation,
/// three connectors, one mapping discipline: cells leave as invariant-culture
/// strings or nulls — the dashboard never sees engine-specific shapes.
/// </summary>
public sealed class AdoSourceExecutor : ISourceReadExecutor
{
    public async Task<SourceReadResult> ReadAsync(
        SourceProvider provider,
        string connectionString,
        string sql,
        int commandTimeoutSeconds,
        CancellationToken ct = default)
    {
        await using var connection = CreateConnection(provider, connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = Math.Clamp(commandTimeoutSeconds, 1, 300);

        await using var reader = await command.ExecuteReaderAsync(ct);

        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();

        var rows = new List<string?[]>();
        while (await reader.ReadAsync(ct))
        {
            var row = new string?[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.GetValue(i);
                row[i] = value is DBNull ? null : FormatCell(value);
            }

            rows.Add(row);
        }

        return new SourceReadResult(columns, [.. rows]);
    }

    private static DbConnection CreateConnection(SourceProvider provider, string connectionString) => provider switch
    {
        SourceProvider.SqlServer or SourceProvider.AzureSql => new SqlConnection(connectionString),
        SourceProvider.MySql => new MySqlConnection(connectionString),
        SourceProvider.Sqlite => new SqliteConnection(connectionString),
        _ => throw new NotSupportedException($"Provider '{provider}' has no connector registered.")
    };

    /// <summary>Invariant forms for every cell so the API payload is locale-proof.</summary>
    private static string FormatCell(object value) => value switch
    {
        string text => text,
        bool flag => flag ? "true" : "false",
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
}
