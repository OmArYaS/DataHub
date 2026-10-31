using Microsoft.Extensions.DependencyInjection;

using DataHub.Application.Common;
using DataHub.Application.Sources;
using DataHub.Application.Sync;

using DataHub.Application.Reports;

using Microsoft.EntityFrameworkCore;

namespace DataHub.Application;

/// <summary>Use-case wiring — one call per service family, no surprises.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddDataHubApplication(this IServiceCollection services)
    {
        services.AddScoped<SourceRegistryService>();
        services.AddScoped<ReportCatalogService>();
        services.AddScoped<ReportExecutionService>();
        services.AddScoped<SyncJobService>();
        services.AddScoped<SourceHealthOrchestrator>();

        return services;
    }
}

/// <summary>
/// Aggregate source health for the /health endpoint: pings every enabled
/// source in parallel (short timeout each) and reports degraded when any
/// one of them cannot answer — informational, never gates readiness.
/// </summary>
public sealed class SourceHealthOrchestrator(ICatalogDbContext db, SourceRegistryService registry)
{
    public async Task<IReadOnlyList<(string SourceKey, string Verdict, string? Detail)>> ProbeAsync(CancellationToken ct = default)
    {
        var enabled = await db.DataSources
            .Where(s => s.IsEnabled)
            .OrderBy(s => s.SourceKey)
            .ToListAsync(ct);

        var results = await Task.WhenAll(enabled.Select(async source =>
        {
            try
            {
                var health = await registry.TestAsync(source.SourceKey, ct);
                return (source.SourceKey, health.Reachable ? "healthy" : "degraded", health.FailureCode ?? health.Detail);
            }
            catch (Exception)
            {
                // A registry hiccup (missing row etc.) degrades, never unhandles.
                return (source.SourceKey, "degraded", "probe failed");
            }
        }));

        return results;
    }
}
