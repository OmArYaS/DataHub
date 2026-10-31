namespace DataHub.Application.Common;

/// <summary>
/// Identity of the caller resolved from the JWT (oid = user key, roles =
/// role bag). Role vocabulary is fixed: viewer, analyst, admin.
/// </summary>
public interface ICurrentUser
{
    string UserKey { get; }

    /// <summary>Distinct role strings from the token, lowercase.</summary>
    IReadOnlyCollection<string> Roles { get; }

    bool HasRole(string role);

    bool IsAdmin { get; }
}

/// <summary>Nothing is hidden from admins; other roles see what they are granted.</summary>
public static class HubRoles
{
    public const string Viewer = "viewer";
    public const string Analyst = "analyst";
    public const string Admin = "admin";

    /// <summary>The complete grant vocabulary — registry writes validate against this.</summary>
    public static readonly string[] All = [Viewer, Analyst, Admin];
}
