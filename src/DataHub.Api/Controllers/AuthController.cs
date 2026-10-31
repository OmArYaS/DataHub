using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using DataHub.Api.Auth;
using DataHub.Application.Contracts;

namespace DataHub.Api.Controllers;

/// <summary>
/// Demo credential endpoint — in production this is the identity provider's
/// login flow; the API never stores passwords, only validates JWTs.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(IDevTokenIssuer issuer) : ControllerBase
{
    /// <summary>Issues a demo token; unknown users come back as 401 (CREDENTIALS_REJECTED).</summary>
    [HttpPost("token")]
    [AllowAnonymous]
    public ActionResult<TokenResponse> Token([FromBody] TokenRequest request) =>
        Ok(issuer.Issue(request.UserName?.Trim() ?? string.Empty));
}
