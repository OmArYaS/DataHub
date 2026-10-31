using System.Data.Common;

using DataHub.Application.Common;
using DataHub.Application.Contracts;
using DataHub.Application.Reports;
using DataHub.Application.Sources;
using DataHub.Domain;
using DataHub.Domain.Entities;
using DataHub.Domain.Rules;

using Microsoft.EntityFrameworkCore;

namespace DataHub.Application.Sync;

/// <summary>
/// Puts one report partition into storage and books the whole act in the
/// SyncRun ledger — completed, failed, code included. Nothing throws to the
/// caller except a missing report: failures are data, displayed in the
/// dashboard, retried by the scheduler.
/// </summary>
public sealed class SyncJobService(
    ICatalogDbContext db,
    ReportExecutionService execution,
    IReportSink sink,
    TimeProvider clock)
{
    /// <summary>Row cap for one sync partition (blowout guard).</summary>
    public const int MaxRowsPerRun = 50_000;

    public async Task<SyncRunDto?> RunAsync(string reportKey, string triggeredBy, CancellationToken ct = default)
    {
        if (!await db.Reports.AnyAsync(r => r.ReportKey == reportKey, ct))
        {
            throw new NotFoundException($"Report '{reportKey}' is not catalogued.");
        }

        var started = clock.GetUtcNow().UtcDateTime;
        var run = new SyncRun
        {
            ReportKey = reportKey,
            Status = SyncStatus.Pending,
            TriggeredBy = triggeredBy,
            StartedUtc = started,
            BlobPath = PartitionPath(reportKey, started)
        };

        db.SyncRuns.Add(run);
        await db.SaveChangesAsync(ct);

        try
        {
            // Pull the table in bounded pages — the execution engine's page cap
            // (500) is the source-protection ceiling and the sync path honours it —
            // concatenating until the last page or the partition cap. Every page
            // runs cache-bypassed: a sync always writes fresh data.
            var pageSize = ReportExecutionService.MaxPageSize;

            string[] columns = [];
            var rows = new List<string?[]>();

            for (var page = 1; (page - 1) * (long)pageSize < MaxRowsPerRun; page++)
            {
                var part = await execution.RunAsync(
                    reportKey,
                    SystemPrincipal.Scheduler,
                    page,
                    pageSize,
                    bypassCache: true,
                    ct);

                if (page == 1)
                {
                    columns = part.Columns;
                }

                rows.AddRange(part.Rows);

                if (part.Rows.Length < pageSize)
                {
                    break; // last page reached
                }
            }

            var content = CsvBuilder.Build(columns, [.. rows]);
            run.BlobPath = await sink.WriteAsync(run.BlobPath, content, ct);
            run.RowCount = rows.Count;
            run.Status = SyncStatus.Completed;
            run.CompletedUtc = clock.GetUtcNow().UtcDateTime;
        }
        catch (Exception ex)
        {
            run.Status = SyncStatus.Failed;
            run.CompletedUtc = clock.GetUtcNow().UtcDateTime;
            run.FailureCode = MapFailure(ex);
        }

        await db.SaveChangesAsync(ct);

        return ToDto(run);
    }

    /// <summary>One scheduler tick: run every cadence-declared report whose window has elapsed.</summary>
    public async Task RunDueAsync(string triggeredBy = SystemPrincipal.SchedulerTrigger, CancellationToken ct = default)
    {
        var reports = await db.Reports
            .Where(r => r.SyncIntervalMinutes != null)
            .ToListAsync(ct);

        var keys = reports.Select(r => r.ReportKey).ToArray();
        var lastRuns = await db.SyncRuns
            .Where(r => keys.Contains(r.ReportKey))
            .OrderByDescending(r => r.StartedUtc)
            .ToListAsync(ct);

        var latest = lastRuns
            .GroupBy(r => r.ReportKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var due = SyncDuePicker.SelectDue(
            reports.Select(r => (r, latest.GetValueOrDefault(r.ReportKey))).ToList(),
            clock.GetUtcNow());

        foreach (var report in due)
        {
            await RunAsync(report.ReportKey, triggeredBy, ct);
        }
    }

    /// <summary>Ledger reads need analyst eyes; the controller enforces the same policy.</summary>
    public async Task<PagedResult<SyncRunDto>> RunsAsync(int page, int pageSize, string? reportKey = null, CancellationToken ct = default)
    {
        var normalized = Math.Clamp(pageSize, 1, MaxRowsPerRun);
        var current = Math.Max(1, page);

        var filtered = db.SyncRuns.AsQueryable();
        if (!string.IsNullOrWhiteSpace(reportKey))
        {
            filtered = filtered.Where(r => r.ReportKey == reportKey);
        }

        var total = await filtered.CountAsync(ct);
        var rows = await filtered
            .OrderByDescending(r => r.StartedUtc).ThenByDescending(r => r.Id)
            .Skip((current - 1) * normalized)
            .Take(normalized)
            .ToListAsync(ct);

        return new PagedResult<SyncRunDto>(
            rows.Select(ToDto).ToList(), current, normalized, total);
    }

    /// <summary>Dashboard tiles: latest run per cadence-declared report.</summary>
    public async Task<IReadOnlyList<SyncTileDto>> TilesAsync(CancellationToken ct = default)
    {
        var reports = await db.Reports
            .Where(r => r.SyncIntervalMinutes != null)
            .ToListAsync(ct);

        var runs = await db.SyncRuns.ToListAsync(ct);
        var latest = runs
            .GroupBy(r => r.ReportKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var tiles = new List<SyncTileDto>();

        foreach (var report in reports.OrderBy(r => r.ReportKey, StringComparer.Ordinal))
        {
            var run = latest.GetValueOrDefault(report.ReportKey);
            tiles.Add(new SyncTileDto(
                report.ReportKey,
                run is null ? null : EnumText.Of(run.Status),
                run?.StartedUtc,
                run?.RowCount,
                run?.BlobPath,
                report.SyncIntervalMinutes));
        }

        return tiles;
    }

    /// <summary>Blob layout: datahub-sync/{reportKey}/{yyyy/MM/dd}/part-{HHmmss}-{nonce}.csv.</summary>
    internal static string PartitionPath(string reportKey, DateTime startedUtc)
    {
        var nonce = Guid.NewGuid().ToString("N")[..8];

        return FormattableString.Invariant(
            $"datahub-sync/{reportKey}/{startedUtc:yyyy/MM/dd}/part-{startedUtc:HHmmss}-{nonce}.csv");
    }

    private static string MapFailure(Exception exception) => exception switch
    {
        BusinessRuleException br => br.Code,
        DbException => RuleCodes.SourceUnavailable,
        _ => RuleCodes.SyncEngineError
    };

    private static SyncRunDto ToDto(SyncRun run) => new(
        run.Id,
        run.ReportKey,
        run.BlobPath,
        run.RowCount,
        EnumText.Of(run.Status),
        run.TriggeredBy,
        run.StartedUtc,
        run.CompletedUtc,
        run.FailureCode);
}
