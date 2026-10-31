using System.Text.RegularExpressions;

namespace DataHub.Domain.Rules;

/// <summary>
/// The wall between the catalog and a live database. Only single-statement,
/// read-only SELECTs with an ORDER BY (deterministic paging contract) pass.
/// The guard is code, not policy: every editor path — POST /api/reports —
/// calls it before a row is written, and the unit tests enumerate the
/// keyword blocklist so a drift is caught, not guessed.
/// </summary>
public static partial class ReadOnlyQueryGuard
{
    /// <summary>Query longer than this is a smell; keep the catalog reviewable.</summary>
    public const int MaxQueryChars = 4000;

    /// <summary>Error code the API surfaces as 422 INVALID_QUERY.</summary>
    public const string RejectCode = "INVALID_QUERY";

    private static readonly string[] ForbiddenKeywords =
    [
        "insert", "update", "delete", "drop", "alter", "create", "truncate",
        "merge", "grant", "revoke", "exec", "execute", "prepare", "detach",
        "attach", "pragma", "reindex", "vacuum", "shutdown", "kill"
    ];

    private static readonly Regex LeadingWord = SelectFirstRegex();

    [GeneratedRegex(@"^\s*select\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SelectFirstRegex();

    /// <summary>Keyword seeds inside identifiers: whole-word matching keeps <c>created_utc</c> safe.</summary>
    private static readonly Regex KeywordProbe = new(
        @"\b(" + string.Join("|", ForbiddenKeywords) + @")\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Validates or throws with the EXACT reason — callers wrap it in a
    /// BusinessRule-style rejection that keeps the guard's wording.
    /// </summary>
    public static void Validate(string? queryText)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            throw new ReadOnlyQueryException(RejectCode, "Query text is required.");
        }

        if (queryText.Length > MaxQueryChars)
        {
            throw new ReadOnlyQueryException(RejectCode,
                $"Query exceeds the {MaxQueryChars}-character catalog limit.");
        }

        if (!LeadingWord.IsMatch(queryText))
        {
            throw new ReadOnlyQueryException(RejectCode, "Only SELECT statements are catalogued.");
        }

        if (queryText.Contains(';'))
        {
            throw new ReadOnlyQueryException(RejectCode, "Multiple statements are not allowed.");
        }

        // Infrastructure at the edges: comments can hide keywords past any word list.
        if (queryText.Contains("--") || queryText.Contains("/*"))
        {
            throw new ReadOnlyQueryException(RejectCode, "SQL comments are not allowed.");
        }

        if (KeywordProbe.IsMatch(queryText))
        {
            var hit = KeywordProbe.Match(queryText).Value;
            throw new ReadOnlyQueryException(RejectCode,
                $"Write/DDL keyword '{hit.ToUpperInvariant()}' is not allowed; the catalog is read-only.");
        }

        // Deterministic paging contract: the dialect wrapper orders by first column,
        // so an explicit ORDER BY keeps row stability between pages.
        if (!queryText.Contains("order by", StringComparison.OrdinalIgnoreCase))
        {
            throw new ReadOnlyQueryException(RejectCode,
                "Catalog queries must end in an ORDER BY clause for stable paging.");
        }
    }

    /// <summary>Runs the guard for the registration path; returns the text if clean.</summary>
    public static string Normalize(string? queryText)
    {
        queryText = queryText is null ? string.Empty : queryText.Trim().TrimEnd();
        Validate(queryText);
        return queryText;
    }
}

/// <summary>Exception shape the middleware turns into 422 INVALID_QUERY ProblemDetails.</summary>
public sealed class ReadOnlyQueryException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
