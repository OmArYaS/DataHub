namespace DataHub.Application.Contracts;

// ── Sources ──────────────────────────────────────────────────────────────

/// <summary>Registry listing item; never the connection string itself.</summary>
public sealed record SourceDto(
    string SourceKey,
    string DisplayName,
    string? Description,
    string Provider,
    string ConnectionName,
    bool ConnectionConfigured,
    bool IsEnabled,
    int CommandTimeoutSeconds,
    DateTime RegisteredUtc);

/// <summary>POST /api/sources body.</summary>
public sealed record RegisterSourceRequest(
    string SourceKey,
    string DisplayName,
    string? Description,
    string Provider,
    string ConnectionName,
    int CommandTimeoutSeconds = 30);

/// <summary>Result of the /test ping endpoint.</summary>
public sealed record SourceHealthDto(
    string SourceKey,
    bool Reachable,
    string? FailureCode,
    string? Detail,
    long ElapsedMilliseconds);

// ── Reports ──────────────────────────────────────────────────────────────

/// <summary>Catalog listing item with the role grants visible.</summary>
public sealed record ReportDto(
    string ReportKey,
    string DisplayName,
    string? Description,
    string SourceKey,
    string QueryText,
    string[] AllowedRoles,
    bool IsCacheable,
    int CacheSecondsTtl,
    int? SyncIntervalMinutes,
    DateTime RegisteredUtc);

/// <summary>POST /api/reports body (admin only).</summary>
public sealed record RegisterReportRequest(
    string ReportKey,
    string DisplayName,
    string? Description,
    string SourceKey,
    string QueryText,
    string[] AllowedRoles,
    bool IsCacheable = true,
    int CacheSecondsTtl = 60,
    int? SyncIntervalMinutes = null);

// ── Results ──────────────────────────────────────────────────────────────

/// <summary>The query outcome; ETag + FromCache note what the cache served.</summary>
public sealed record ReportResultDto(
    string ReportKey,
    string[] Columns,
    string?[][] Rows,
    long TotalCount,
    int Page,
    int PageSize,
    long ElapsedMilliseconds,
    bool FromCache,
    string ETag);

// ── Sync ─────────────────────────────────────────────────────────────────

/// <summary>Ledger item for the sync history table.</summary>
public sealed record SyncRunDto(
    long Id,
    string ReportKey,
    string BlobPath,
    int RowCount,
    string Status,
    string TriggeredBy,
    DateTime StartedUtc,
    DateTime? CompletedUtc,
    string? FailureCode);

/// <summary>Dashboard tile per report with sync cadence.</summary>
public sealed record SyncTileDto(
    string ReportKey,
    string? LastStatus,
    DateTime? LastRunUtc,
    int? LastRowCount,
    string? LastBlobPath,
    int? IntervalMinutes);

// ── Auth ─────────────────────────────────────────────────────────────────

/// <summary>POST /api/auth/token body (demo issuer; prod identity is the IdP).</summary>
public sealed record TokenRequest(string UserName);

public sealed record TokenResponse(
    string AccessToken,
    string TokenType,
    DateTime ExpiresUtc,
    string UserKey,
    string[] Roles);
