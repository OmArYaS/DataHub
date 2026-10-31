using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using DataHub.Api.Authorization;
using DataHub.Application.Common;
using DataHub.Application.Contracts;
using DataHub.Application.Sync;

namespace DataHub.Api.Controllers;

/// <summary>
/// Blob Storage sync operations: trigger a run, read the ledger, and the
/// dashboard tiles. Analysts may inspect; only admins may trigger.
/// </summary>
[ApiController]
[Route("api/sync")]
[Authorize]
[Produces("application/json")]
public sealed class SyncController(SyncJobService sync) : ControllerBase
{
    /// <summary>Immediate run for one catalog report — scheduled or human-triggered alike.</summary>
    [HttpPost("run/{reportKey}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<SyncRunDto>> Run(string reportKey, ICurrentUser currentUser, CancellationToken ct)
        => Ok(await sync.RunAsync(reportKey, currentUser.UserKey, ct));

    /// <summary>Every cadence-declared report whose window has elapsed (scheduler logic, on demand).</summary>
    [HttpPost("run-due")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> RunDue(ICurrentUser currentUser, CancellationToken ct)
    {
        await sync.RunDueAsync(currentUser.UserKey, ct);
        return NoContent();
    }

    /// <summary>The ledger, newest first.</summary>
    [HttpGet("runs")]
    [Authorize(Policy = Policies.Analyst)]
    public async Task<ActionResult<PagedResult<SyncRunDto>>> Runs(
        int page = 1,
        int pageSize = 20,
        [FromQuery(Name = "reportKey")] string? reportKey = null,
        CancellationToken ct = default) =>
        Ok(await sync.RunsAsync(page, pageSize, reportKey, ct));

    /// <summary>One tile per cadence-declared report with its latest run.</summary>
    [HttpGet("dashboard")]
    [Authorize(Policy = Policies.Analyst)]
    public async Task<ActionResult<IReadOnlyList<SyncTileDto>>> Dashboard(CancellationToken ct) =>
        Ok(await sync.TilesAsync(ct));
}
