using System.Data.Common;

using DataHub.Domain;

namespace DataHub.Application.Sources;

public sealed record SourceReadResult(string[] Columns, string?[][] Rows);

/// <summary>
/// The only gateway into federated data: open a connection of the right
/// engine and read back an anonymous result set as string cells. The
/// executor never composes SQL — it runs what the catalog + dialect
/// produced (ReadOnlyQueryGuard vets the catalog input upstream).
/// </summary>
public interface ISourceReadExecutor
{
    /// <summary>
    /// Runs one read-only statement against a registered source.
    /// Throws <see cref="DbException"/> on engine-level failures — callers
    /// map connectivity losses to SOURCE_UNAVAILABLE.
    /// </summary>
    Task<SourceReadResult> ReadAsync(
        SourceProvider provider,
        string connectionString,
        string sql,
        int commandTimeoutSeconds,
        CancellationToken ct = default);
}
