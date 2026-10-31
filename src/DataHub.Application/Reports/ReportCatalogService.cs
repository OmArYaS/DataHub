using DataHub.Application.Common;
using DataHub.Application.Contracts;
using DataHub.Domain.Entities;
using DataHub.Domain.Rules;

using Microsoft.EntityFrameworkCore;

namespace DataHub.Application.Reports;

/// <summary>
/// The query catalog: role-filtered listing, keyed reads, and admin
/// registration with the full validation battery (key syntax, source
/// existence, ReadOnlyQueryGuard, grant vocabulary).
/// </summary>
public sealed class ReportCatalogService(ICatalogDbContext db, TimeProvider clock)
{
    public async Task<IReadOnlyList<ReportDto>> ListAsync(ICurrentUser user, CancellationToken ct = default)
    {
        var rows = await db.Reports
            .Include(r => r.Permissions)
            .OrderBy(r => r.DisplayName)
            .ToListAsync(ct);

        return rows
            .Where(r => ReportAccess.CanRun(r, user))
            .Select(ToDto)
            .ToList();
    }

    public async Task<ReportDto> GetAsync(string reportKey, ICurrentUser user, CancellationToken ct = default)
    {
        var report = await RequireByKeyAsync(reportKey, ct);
        ReportAccess.EnsureCanRun(report, user);

        return ToDto(report);
    }

    public async Task<ReportDto> RegisterAsync(RegisterReportRequest request, CancellationToken ct = default)
    {
        var reportKey = (request.ReportKey ?? string.Empty).Trim();
        var displayName = (request.DisplayName ?? string.Empty).Trim();

        if (!KeyFormat.IsCatalogKey(reportKey))
        {
            throw new BusinessRuleException(RuleCodes.InvalidReport,
                "Report key must be 3–40 chars of lowercase letters/digits/underscores starting with a letter.");
        }

        if (displayName.Length is 0 or > 120)
        {
            throw new BusinessRuleException(RuleCodes.InvalidReport, "Display name is required (max 120 chars).");
        }

        if (!await db.DataSources.AnyAsync(s => s.SourceKey == request.SourceKey, ct))
        {
            throw new NotFoundException($"Source '{request.SourceKey}' is not registered.");
        }

        string queryText;
        try
        {
            queryText = ReadOnlyQueryGuard.Normalize(request.QueryText);
        }
        catch (ReadOnlyQueryException ex)
        {
            throw new BusinessRuleException(RuleCodes.InvalidQuery, ex.Message, 422);
        }

        var roles = (request.AllowedRoles ?? [])
            .Select(r => (r ?? string.Empty).Trim().ToLowerInvariant())
            .Where(r => r.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (roles.Length == 0)
        {
            throw new BusinessRuleException(RuleCodes.InvalidReport,
                "At least one granted role is required (viewer/analyst/admin).");
        }

        if (roles.Except(HubRoles.All).Any())
        {
            throw new BusinessRuleException(RuleCodes.InvalidReport,
                $"Grant vocabulary is viewer/analyst/admin — unknown: {string.Join(", ", roles.Except(HubRoles.All))}.");
        }

        if (await db.Reports.AnyAsync(r => r.ReportKey == reportKey, ct))
        {
            throw new BusinessRuleException(RuleCodes.InvalidReport, $"Report '{reportKey}' is already catalogued.");
        }

        var report = new ReportDefinition
        {
            ReportKey = reportKey,
            DisplayName = displayName,
            Description = request.Description?.Trim(),
            SourceKey = request.SourceKey.Trim(),
            QueryText = queryText,
            Permissions = roles.Select(role => new ReportGrant { RoleName = role }).ToList(),
            IsCacheable = request.IsCacheable,
            CacheSecondsTtl = Math.Clamp(request.CacheSecondsTtl, 5, 3600),
            SyncIntervalMinutes = request.SyncIntervalMinutes is > 0 and <= 720 ? request.SyncIntervalMinutes : null,
            RegisteredUtc = clock.GetUtcNow().UtcDateTime
        };

        db.Reports.Add(report);
        await db.SaveChangesAsync(ct);

        return ToDto(report);
    }

    private async Task<ReportDefinition> RequireByKeyAsync(string reportKey, CancellationToken ct)
    {
        var report = await db.Reports
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.ReportKey == reportKey, ct);

        if (report is null)
        {
            throw new NotFoundException($"Report '{reportKey}' is not catalogued.");
        }

        return report;
    }

    private static ReportDto ToDto(ReportDefinition report) => new(
        report.ReportKey,
        report.DisplayName,
        report.Description,
        report.SourceKey,
        report.QueryText,
        report.Permissions.Select(p => p.RoleName).OrderBy(r => r, StringComparer.Ordinal).ToArray(),
        report.IsCacheable,
        report.CacheSecondsTtl,
        report.SyncIntervalMinutes,
        report.RegisteredUtc);
}
