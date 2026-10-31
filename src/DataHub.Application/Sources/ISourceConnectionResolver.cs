namespace DataHub.Application.Sources;

/// <summary>
/// Secrets never travel through the catalog: a source row stores the
/// configuration key, and this resolver turns it into the live string at
/// call time (appsettings { Sources:{ConnectionName}:ConnectionString } or a
/// KeyVault reference standing in its place).
/// </summary>
public interface ISourceConnectionResolver
{
    /// <summary>Throws when the configured slot is missing/blank.</summary>
    string Resolve(string connectionName);

    /// <summary>Registry listing uses this to show "configured"/"not configured" without leaking.</summary>
    bool IsConfigured(string connectionName);
}
