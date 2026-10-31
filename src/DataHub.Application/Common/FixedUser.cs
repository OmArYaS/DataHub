using DataHub.Application.Common;

namespace DataHub.Application.Common;

/// <summary>
/// Token-independent ICurrentUser (tests, the sync scheduler's system
/// identity, unit fixtures). The value form of the JWT mapping.
/// </summary>
public sealed class FixedUser(string userKey, params string[] roles) : ICurrentUser
{
    public static FixedUser Viewer => new("viewer.demo", HubRoles.Viewer);

    public static FixedUser Analyst => new("analyst.demo", HubRoles.Analyst);

    public static FixedUser Admin => new("admin.demo", HubRoles.Admin);

    public string UserKey { get; } = userKey;

    public IReadOnlyCollection<string> Roles { get; } =
        roles.Length == 0 ? Array.Empty<string>() : roles.Select(r => r.ToLowerInvariant()).Distinct().ToArray();

    public bool HasRole(string role) => Roles.Contains(role.ToLowerInvariant());

    public bool IsAdmin => HasRole(HubRoles.Admin);
}

/// <summary>The sync scheduler's identity — full bypass, but audited by name.</summary>
public static class SystemPrincipal
{
    public static readonly ICurrentUser Scheduler = new FixedUser("datahub-scheduler", HubRoles.Admin);

    public const string SchedulerTrigger = "scheduler";
}
