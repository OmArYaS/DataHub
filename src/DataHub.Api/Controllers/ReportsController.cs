using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using DataHub.Api.Authorization;
using DataHub.Application.Common;
using DataHub.Application.Contracts;
using DataHub.Application.Reports;

namespace DataHub.Api.Controllers;

/// <summary>
/// The query catalog + runner. Users run named reports; registration is
/// admin-only and passes through the ReadOnlyQueryGuard battery.
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize]
[Produces("application/json")]
public sealed class ReportsController(
    ReportCatalogService catalog,
    ReportExecutionService execution,
    ICurrentUser currentUser) : ControllerBase
{
    /// <summary>The reports this caller may run (admins see everything).</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReportDto>>> List(CancellationToken ct) =>
        Ok(await catalog.ListAsync(currentUser, ct));

    [HttpGet("{reportKey}")]
    public async Task<ActionResult<ReportDto>> Get(string reportKey, CancellationToken ct) =>
        Ok(await catalog.GetAsync(reportKey, currentUser, ct));

    /// <summary>Catalogues a read-only SELECT; 422 INVALID_QUERY for anything the guard rejects.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<ReportDto>> Register([FromBody] RegisterReportRequest request, CancellationToken ct)
    {
        var created = await catalog.RegisterAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { reportKey = created.ReportKey }, created);
    }

    /// <summary>
    /// Runs one catalog report (role-gated, Redis/­memory-cached, ETag'd).
    /// Send If-None-Match for a 304 when the table has not changed.
    /// </summary>
    [HttpPost("{reportKey}/query")]
    public async Task<IActionResult> Query(
        string reportKey,
        [FromBody] ReportQueryRequest request,
        CancellationToken ct)
    {
        var result = await execution.RunAsync(
            reportKey,
            currentUser,
            request.Page,
            request.PageSize,
            bypassCache: request.BypassCache,
            ct);

        // 304 contract: matching ETag means the client already holds this table.
        // Cached or fresh, identical content shares the stamp (CacheStamp).
        var clientStamp = Request.Headers.IfNoneMatch.ToString().Trim().Trim('"');
        if (clientStamp.Length > 0 && string.Equals(clientStamp, result.ETag, StringComparison.Ordinal))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        Response.Headers.ETag = $"\"{result.ETag}\""; // RFC 7232: quoted opaque value
        return Ok(result);
    }
}

/// <summary>POST /api/reports/{key}/query body.</summary>
public sealed record ReportQueryRequest(int Page = 1, int PageSize = 100, bool BypassCache = false);
