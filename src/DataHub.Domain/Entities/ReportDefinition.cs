namespace DataHub.Domain.Entities;

/// <summary>
/// A named, admin-authored SELECT in the query catalog. Users never submit
/// SQL — they run catalog entries, and <see cref="Rules.ReadOnlyQueryGuard"/>
/// polices what an editor could store in the first place.
/// </summary>
public sealed class ReportDefinition
{
    public int Id { get; set; }

    /// <summary>Stable machine key, e.g. <c>revenue_by_region</c> (unique).</summary>
    public string ReportKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>What the report answers, one sentence, shown in the dashboard.</summary>
    public string? Description { get; set; }

    public string SourceKey { get; set; } = string.Empty;

    /// <summary>Read-only SELECT text validated by <see cref="Rules.ReadOnlyQueryGuard"/>.</summary>
    public string QueryText { get; set; } = string.Empty;

    /// <summary>Roles allowed to run it (lowercase vocabulary: viewer/analyst/admin).</summary>
    public List<ReportGrant> Permissions { get; set; } = new();

    public bool IsCacheable { get; set; } = true;

    /// <summary>Cache TTL seconds (used only when <see cref="IsCacheable"/>).</summary>
    public int CacheSecondsTtl { get; set; } = 60;

    /// <summary>Sync cadence minutes; null = no automatic Blob sync.</summary>
    public int? SyncIntervalMinutes { get; set; }

    public DateTime RegisteredUtc { get; set; }
}
