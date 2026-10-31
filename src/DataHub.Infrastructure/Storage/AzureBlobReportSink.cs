using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

using DataHub.Application.Sync;

namespace DataHub.Infrastructure.Storage;

/// <summary>
/// Production sink: one container (datahub-sync), date-partitioned virtual
/// paths straight from the sync service; blob overwrite makes idempotent
/// re-runs (scheduler + manual double-click) safe.
/// </summary>
public sealed class AzureBlobReportSink(string connectionString, string containerName = "datahub-sync") : IReportSink
{
    public async Task<string> WriteAsync(string path, string content, CancellationToken ct = default)
    {
        var service = new BlobServiceClient(connectionString);
        var container = service.GetBlobContainerClient(containerName);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);

        var blob = container.GetBlobClient(path);
        // No access conditions: unconditionally above the existing blob, so
        // scheduled and manual re-runs stay idempotent (a second write of the
        // same partition path replaces, never fails).
        await blob.UploadAsync(
            new BinaryData(content),
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "text/csv" } },
            ct);

        return blob.Name.StartsWith(containerName + "/", StringComparison.Ordinal) ? blob.Name : $"{containerName}/{blob.Name}";
    }
}
