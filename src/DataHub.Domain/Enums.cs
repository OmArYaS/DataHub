namespace DataHub.Domain;

/// <summary>Database engines a registered source can speak. Stored lowercase (ENUM contract).</summary>
public enum SourceProvider
{
    SqlServer = 1,

    /// <summary>Azure SQL Database — same dialect as SqlServer, different connection surface.</summary>
    AzureSql = 2,

    MySql = 3,

    /// <summary>Local file-backed demo source (also serves tests) — never a production provider.</summary>
    Sqlite = 4
}

/// <summary>Lifecycle of a Blob Storage sync run. Stored lowercase (ENUM contract).</summary>
public enum SyncStatus
{
    Pending = 1,
    Completed = 2,
    Failed = 3
}

/// <summary>The fixed role vocabulary on the token; environment grants map onto it. Stored lowercase.</summary>
public enum HubRole
{
    Viewer = 1,
    Analyst = 2,
    Admin = 3
}

/// <summary>Helper for the lowercase string contract shared by every DataHub enum column.</summary>
public static class EnumText
{
    public static string Of<TEnum>(TEnum value) where TEnum : struct, Enum =>
        value.ToString().ToLowerInvariant();

    public static TEnum ParseOr<TEnum>(string? text, TEnum fallback) where TEnum : struct, Enum
    {
        return Enum.TryParse<TEnum>(text, ignoreCase: true, out var parsed) ? parsed : fallback;
    }
}
