namespace DataHub.Application.Sync;

/// <summary>
/// Where sync partitions land. Production binds Azure Blob Storage
/// (container datahub-sync); the demo binds a local folder with the same
/// path layout, and tests bind an in-memory recorder.
/// </summary>
public interface IReportSink
{
    /// <summary>Writes one partition and returns the resolved canonical path.</summary>
    Task<string> WriteAsync(string path, string content, CancellationToken ct = default);
}
