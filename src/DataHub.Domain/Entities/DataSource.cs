namespace DataHub.Domain.Entities;

/// <summary>
/// One federated database the hub can query. No secrets live here: the
/// connection string resolves from configuration at call time via
/// <c>Sources:{ConnectionName}:ConnectionString</c> (docs/architecture.md §
/// Secrets), so a registry dump never leaks credentials.
/// </summary>
public sealed class DataSource
{
    public int Id { get; set; }

    /// <summary>Stable machine key, e.g. <c>local_warehouse</c> (unique).</summary>
    public string SourceKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Human camera-ready note, e.g. "Retail OLTP replica — MySQL 8".</summary>
    public string? Description { get; set; }

    public SourceProvider Provider { get; set; } = SourceProvider.SqlServer;

    /// <summary>Configuration lookup key; the string itself stays in appsettings/KeyVault.</summary>
    public string ConnectionName { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    /// <summary>Query timeout for source calls, seconds (dialect-independent cap).</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    public DateTime RegisteredUtc { get; set; }
}
