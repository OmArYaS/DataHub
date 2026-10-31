namespace DataHub.Domain.Entities;

/// <summary>
/// Ledger row for one Blob Storage sync run: where the CSV partition landed,
/// how many rows it carried, and who pushed the button (or the scheduler).
/// The dashboard's sync tiles are queries over this ledger.
/// </summary>
public sealed class SyncRun
{
    public long Id { get; set; }

    public string ReportKey { get; set; } = string.Empty;

    /// <summary>Blob path (or filesystem-relative path in the demo sink).</summary>
    public string BlobPath { get; set; } = string.Empty;

    public int RowCount { get; set; }

    public SyncStatus Status { get; set; } = SyncStatus.Pending;

    public string TriggeredBy { get; set; } = string.Empty;

    public DateTime StartedUtc { get; set; }

    public DateTime? CompletedUtc { get; set; }

    /// <summary>Machine-readable failure reason (SOURCE_UNAVAILABLE, BLOB_WRITE_FAILED…).</summary>
    public string? FailureCode { get; set; }
}
