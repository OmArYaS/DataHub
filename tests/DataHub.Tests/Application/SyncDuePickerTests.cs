using DataHub.Application.Sync;
using DataHub.Domain;
using DataHub.Domain.Entities;

using Xunit;

namespace DataHub.Tests.Application;

/// <summary>
/// The pure cadence rule: interval-declared reports fall due when their last
/// run (any status — a failure is still a run) leaves the window; cadence-free
/// reports never do.
/// </summary>
public sealed class SyncDuePickerTests
{
    private static ReportDefinition Daily(string key = "region_revenue", int minutes = 60) => new()
    {
        ReportKey = key,
        SyncIntervalMinutes = minutes
    };

    private static SyncRun RanAt(DateTimeOffset startedUtc, SyncStatus status = SyncStatus.Completed) => new()
    {
        ReportKey = "region_revenue",
        Status = status,
        StartedUtc = startedUtc.UtcDateTime
    };

    [Fact]
    public void Cadence_free_reports_never_run()
    {
        var due = SyncDuePicker.SelectDue([(new ReportDefinition { ReportKey = "no_cadence" }, null)],
            new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));

        Assert.Empty(due);
    }

    [Fact]
    public void Never_run_means_due()
    {
        var due = SyncDuePicker.SelectDue([(Daily(), null)],
            new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));

        Assert.Single(due);
        Assert.Equal("region_revenue", due[0].ReportKey);
    }

    [Fact]
    public void Window_boundary_includes_exact_expiration()
    {
        var started = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        var ran = RanAt(started);

        // At exactly 60 minutes the slot is due (<=); one tick earlier it is not.
        var early = SyncDuePicker.SelectDue([(Daily(), ran)], started.AddMinutes(59));
        var exact = SyncDuePicker.SelectDue([(Daily(), ran)], started.AddMinutes(60));

        Assert.Empty(early);
        Assert.Single(exact);
    }

    [Fact]
    public void A_failed_run_still_occupies_the_window()
    {
        var recentFailure = RanAt(
            new DateTimeOffset(2026, 3, 1, 8, 40, 0, TimeSpan.Zero),
            SyncStatus.Failed);

        var due = SyncDuePicker.SelectDue([(Daily(), recentFailure)],
            new DateTimeOffset(2026, 3, 1, 9, 10, 0, TimeSpan.Zero));

        Assert.Empty(due);
    }
}
