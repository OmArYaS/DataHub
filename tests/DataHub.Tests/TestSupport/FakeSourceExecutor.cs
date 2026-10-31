using System.Data.Common;

using DataHub.Application.Sources;
using DataHub.Domain;

namespace DataHub.Tests.TestSupport;

/// <summary>
/// Scripted ISourceReadExecutor: answers with a canned result set, can fail
/// transiently N times before answering, or fail permanently — the exact
/// surface the retry-pipeline tests rotate over. Never touches an engine.
/// </summary>
public sealed class FakeSourceExecutor(
    string[]? columns = null,
    string?[][]? rows = null) : ISourceReadExecutor
{
    private readonly string[] columns = columns ?? ["dummy"];
    private readonly string?[][] rows = rows ?? [["ok"]];

    private int callCount;

    /// <summary>First N calls throw a transient DbException, then it behaves.</summary>
    public int TransientFailures { get; set; }

    /// <summary>Whether calls fail permanently with a non-transient DbException.</summary>
    public bool AlwaysFail { get; set; }

    public int CallCount => callCount;

    public string? LastSql { get; private set; }

    public SourceProvider? LastProvider { get; private set; }

    public Task<SourceReadResult> ReadAsync(
        SourceProvider provider,
        string connectionString,
        string sql,
        int commandTimeoutSeconds,
        CancellationToken ct = default)
    {
        _ = commandTimeoutSeconds;

        Interlocked.Increment(ref callCount);
        LastSql = sql;
        LastProvider = provider;

        if (AlwaysFail)
        {
            throw new SteadyDbException("Source unavailable permanently.");
        }

        if (TransientFailures > 0)
        {
            TransientFailures--;
            throw new TransientDbException("Connection blip — classified transient by the engine.");
        }

        return Task.FromResult(new SourceReadResult(columns, rows));
    }
}

/// <summary>Engine-classified transient loss — retry pipeline honours IsTransient.</summary>
public sealed class TransientDbException(string message) : DbException(message)
{
    public override bool IsTransient => true;
}

/// <summary>Permanent loss — the retry wall must never touch it.</summary>
public sealed class SteadyDbException(string message) : DbException(message)
{
    public override bool IsTransient => false;
}
