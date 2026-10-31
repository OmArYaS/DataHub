using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using DataHub.Application.Common;

#pragma warning disable CA1848 // cold-path demo logging

namespace DataHub.Application.Sync;

/// <summary>
/// Cadence runner: every IntervalSeconds it picks the due reports and syncs
/// them. Failures never kill the loop (logged; the ledger row already
/// records the code inside SyncRun); the demo profile can disable the loop
/// entirely via Sync:SchedulerEnabled=false (contract tests do exactly that).
/// </summary>
public sealed class SyncSchedulerService(
    IServiceScopeFactory scopes,
    ILogger<SyncSchedulerService> logger,
    int intervalSeconds = 900,
    bool enabled = true) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!enabled)
        {
            logger.LogInformation("DataHub sync scheduler disabled by configuration.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, 10, 86_400));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var jobs = scope.ServiceProvider.GetRequiredService<SyncJobService>();
                await jobs.RunDueAsync(SystemPrincipal.SchedulerTrigger, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "DataHub sync scheduler tick failed.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
