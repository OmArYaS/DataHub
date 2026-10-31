using System.Collections.Concurrent;

using DataHub.Application.Sync;

namespace DataHub.Tests.TestSupport;

/// <summary>
/// In-memory IReportSink mirroring the Blob contract — records paths and
/// content so sync tests can assert the partition layout and the CSV body.
/// </summary>
public sealed class FakeReportSink : IReportSink
{
    private readonly ConcurrentDictionary<string, string> written = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Written => written;

    public Task<string> WriteAsync(string path, string content, CancellationToken ct = default)
    {
        written[path] = content;
        return Task.FromResult(path);
    }
}
