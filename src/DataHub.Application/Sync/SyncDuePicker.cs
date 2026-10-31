using DataHub.Domain.Entities;

namespace DataHub.Application.Sync;

/// <summary>
/// Pure "is it time?" rule for the sync cadence: a report is due when it
/// declares an interval and its most recent run (any status) is older than
/// that interval — or it never ran. Unit-tested exhaustively; the scheduler
/// simply consumes it.
/// </summary>
public static class SyncDuePicker
{
    public static IReadOnlyList<ReportDefinition> SelectDue(
        IReadOnlyList<(ReportDefinition Report, SyncRun? Last)> candidates,
        DateTimeOffset now)
    {
        var due = new List<ReportDefinition>();

        foreach (var (report, last) in candidates)
        {
            if (report.SyncIntervalMinutes is not { } minutes || minutes <= 0)
            {
                continue;
            }

            if (last is null || last.StartedUtc.AddMinutes(minutes) <= now.UtcDateTime)
            {
                due.Add(report);
            }
        }

        return due;
    }
}
