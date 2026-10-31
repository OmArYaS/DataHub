using DataHub.Application.Sync;

namespace DataHub.Infrastructure.Storage;

/// <summary>
/// Demo sink: writes CSV partitions under a local root while keeping the
/// exact blob layout (datahub-sync/{reportKey}/{yyyy/MM/dd}/part-*.csv) —
/// swapping to Azure later is a registration flip, not a code change.
/// </summary>
public sealed class FileSystemReportSink(string rootDirectory) : IReportSink
{
    public async Task<string> WriteAsync(string path, string content, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(rootDirectory, path.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(fullPath, content, ct);

        return fullPath.Replace('\\', '/');
    }
}
