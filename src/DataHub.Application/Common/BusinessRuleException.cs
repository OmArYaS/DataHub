namespace DataHub.Application.Common;

/// <summary>
/// A business-rule decision the caller can act on. The API middleware turns
/// Code + StatusCode into ProblemDetails; the codes mirror the legacy hub
/// vocabulary documented in docs/architecture.md.
/// </summary>
public sealed class BusinessRuleException(string code, string message, int statusCode = 400)
    : Exception(message)
{
    /// <summary>Stable machine-readable code, e.g. PERMISSION_DENIED.</summary>
    public string Code { get; } = code;

    /// <summary>HTTP status the API should surface for this rule.</summary>
    public int StatusCode { get; } = statusCode;
}

/// <summary>Requested catalog resource does not exist (mapped to 404).</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>The stable rule-code vocabulary, also used by the webapp.</summary>
public static class RuleCodes
{
    public const string SourceNotFound = "SOURCE_NOT_FOUND";
    public const string SourceDisabled = "SOURCE_DISABLED";
    public const string SourceNotConfigured = "SOURCE_NOT_CONFIGURED";
    public const string SourceUnavailable = "SOURCE_UNAVAILABLE";
    public const string ReportNotFound = "REPORT_NOT_FOUND";
    public const string PermissionDenied = "PERMISSION_DENIED";
    public const string InvalidQuery = "INVALID_QUERY";
    public const string InvalidReport = "INVALID_REPORT";
    public const string InvalidSource = "INVALID_SOURCE";
    public const string BlobWriteFailed = "BLOB_WRITE_FAILED";
    public const string SyncEngineError = "SYNC_ENGINE_ERROR";
}
