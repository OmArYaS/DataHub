using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using DataHub.Api.Authorization;
using DataHub.Api.Auth;
using DataHub.Application.Contracts;
using DataHub.Application.Sources;

namespace DataHub.Api.Controllers;

/// <summary>
/// Federated-source registry. Listing/tests are for insiders; mutations are
/// admin policy. Responses never contain connection strings — only the
/// configured/not-configured bit and the config key.
/// </summary>
[ApiController]
[Route("api/sources")]
[Authorize]
[Produces("application/json")]
public sealed class SourcesController(SourceRegistryService registry) : ControllerBase
{
    /// <summary>The registry (secrets masked to configured/not-configured).</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SourceDto>>> List(CancellationToken ct) =>
        Ok(await registry.ListAsync(ct));

    [HttpGet("{sourceKey}")]
    public async Task<ActionResult<SourceDto>> Get(string sourceKey, CancellationToken ct) =>
        Ok(await registry.GetAsync(sourceKey, ct));

    /// <summary>Registers a source; provider + config slot validated, secrets stay in config.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<SourceDto>> Register([FromBody] RegisterSourceRequest request, CancellationToken ct)
    {
        var created = await registry.RegisterAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { sourceKey = created.SourceKey }, created);
    }

    /// <summary>Pings the source with SELECT 1 and reports reachable/failure code.</summary>
    [HttpPost("{sourceKey}/test")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<SourceHealthDto>> Test(string sourceKey, CancellationToken ct) =>
        Ok(await registry.TestAsync(sourceKey, ct));

    /// <summary>Disabled sources refuse queries but keep their catalog rows.</summary>
    [HttpPost("{sourceKey}/disable")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<SourceDto>> Disable(string sourceKey, CancellationToken ct) =>
        Ok(await registry.SetEnabledAsync(sourceKey, enabled: false, ct));

    [HttpPost("{sourceKey}/enable")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<SourceDto>> Enable(string sourceKey, CancellationToken ct) =>
        Ok(await registry.SetEnabledAsync(sourceKey, enabled: true, ct));
}
