using System.Security.Claims;

using Microsoft.AspNetCore.Http;

using DataHub.Application.Common;

namespace DataHub.Infrastructure.Security;

/// <summary>
/// JWT → ICurrentUser. The dev issuer and the prod IdP both carry the user
/// key in `oid` and the role list in `roles` (raw claim types —
/// MapInboundClaims is false).
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string UserKey =>
        accessor.HttpContext?.User?.FindFirst("oid")?.Value ?? string.Empty;

    public IReadOnlyCollection<string> Roles { get; } =
        accessor.HttpContext?.User?.Claims
            .Where(c => c.Type == "roles" || c.Type == ClaimTypes.Role)
            .Select(c => c.Value.ToLowerInvariant())
            .Where(r => r.Length > 0)
            .Distinct()
            .ToArray() ?? [];

    public bool HasRole(string role) => Roles.Contains(role.ToLowerInvariant());

    public bool IsAdmin => HasRole(HubRoles.Admin);
}
