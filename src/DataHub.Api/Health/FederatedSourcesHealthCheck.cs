using Microsoft.Extensions.Diagnostics.HealthChecks;

using DataHub.Application;
using DataHub.Application.Sources;

namespace DataHub.Api.Health;

/// <summary>
/// Aggregate health of every enabled federated source (parallel ping,
/// SELECT 1 each). Reports Healthy / Degraded per outcome, but is never
/// ready-tagged: an unreachable MySQL in the demo profile must not take the
/// whole platform out of readiness — it answers /health as degraded detail.
/// </summary>
public sealed class FederatedSourcesHealthCheck(SourceHealthOrchestrator orchestrator) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var probes = await orchestrator.ProbeAsync(cancellationToken);

        if (probes.Count == 0)
        {
            return HealthCheckResult.Healthy("No enabled sources registered.");
        }

        var data = probes.ToDictionary(
            p => p.SourceKey,
            p => (object)(p.Verdict == "healthy" ? "answered SELECT 1" : (p.Detail ?? "unreachable")));

        var reachable = probes.Count(p => p.Verdict == "healthy");

        if (reachable == probes.Count)
        {
            return HealthCheckResult.Healthy($"All {probes.Count} federated sources answered.", data);
        }

        if (reachable == 0)
        {
            return HealthCheckResult.Unhealthy("No federated source answered.", data: data);
        }

        return HealthCheckResult.Degraded(
            $"{reachable}/{probes.Count} federated sources answered.", data: data);
    }
}
