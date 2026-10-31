using DataHub.Domain.Rules;

using Xunit;

namespace DataHub.Tests.Domain;

/// <summary>
/// The guard wall: every rule enumerated so a keyword-list drift or a paging
/// contract regression is caught, not guessed. Each rejection keeps its exact
/// reason — the webapp surfaces it as 422 INVALID_QUERY detail text.
/// </summary>
public sealed class ReadOnlyQueryGuardTests
{
    private const string CleanQuery =
        "SELECT region AS region, SUM(amount) AS total FROM demo_facts GROUP BY region ORDER BY total DESC";

    // ── happy path ───────────────────────────────────────────────────────────

    [Fact]
    public void Clean_select_with_order_by_passes()
    {
        var ex = Record.Exception(() => ReadOnlyQueryGuard.Validate(CleanQuery));

        Assert.Null(ex);
    }

    [Fact]
    public void Leading_whitespace_is_no_trick()
    {
        var ex = Record.Exception(() => ReadOnlyQueryGuard.Validate("   " + CleanQuery));

        Assert.Null(ex);
    }

    [Fact]
    public void Keyword_inside_identifier_does_not_trip_the_blocklist()
    {
        // whole-word matching keeps created_utc / executed_at-style columns safe
        var query =
            "SELECT o.created_utc AS when_utc, o.user_key AS user_key FROM order_log o " +
            "ORDER BY o.created_utc DESC";

        var ex = Record.Exception(() => ReadOnlyQueryGuard.Validate(query));

        Assert.Null(ex);
    }

    // ── statement shape ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, "Query text is required.")]
    [InlineData("   ", "Query text is required.")]
    public void Empty_query_is_rejected_for_missing_text(string? query, string message)
    {
        var ex = Assert.Throws<ReadOnlyQueryException>(() => ReadOnlyQueryGuard.Validate(query));

        Assert.Equal(message, ex.Message);
        Assert.Equal("INVALID_QUERY", ex.Code);
    }

    [Fact]
    public void Overlong_query_hits_the_catalog_limit()
    {
        var query = "SELECT '" + new string('a', ReadOnlyQueryGuard.MaxQueryChars + 1) + "' AS blob FROM t ORDER BY blob";

        var ex = Assert.Throws<ReadOnlyQueryException>(() => ReadOnlyQueryGuard.Validate(query));

        Assert.Contains("4000", ex.Message);
    }

    [Theory]
    [InlineData("WITH stale AS (SELECT 1) SELECT * FROM stale ORDER BY 1")]
    [InlineData("DELETE FROM demo_facts WHERE id = 1 ORDER BY id")]
    [InlineData("UPDATE demo_facts SET amount = 0 ORDER BY amount")]
    [InlineData("EXEC usp_run_daily ORDER BY 1")]
    public void Non_select_leading_words_are_rejected(string query)
    {
        var ex = Assert.Throws<ReadOnlyQueryException>(() => ReadOnlyQueryGuard.Validate(query));

        Assert.Equal("Only SELECT statements are catalogued.", ex.Message);
    }

    [Fact]
    public void Multi_statement_attempt_is_rejected()
    {
        var ex = Assert.Throws<ReadOnlyQueryException>(
            () => ReadOnlyQueryGuard.Validate(CleanQuery + "; DROP TABLE demo_facts"));

        Assert.Equal("Multiple statements are not allowed.", ex.Message);
    }

    [Fact]
    public void Leading_comment_looks_like_a_non_select()
    {
        // The leading-word check runs first: a comment-led statement is still
        // not a well-formed catalog SELECT.
        var ex = Assert.Throws<ReadOnlyQueryException>(
            () => ReadOnlyQueryGuard.Validate("-- comment\nSELECT region FROM t ORDER BY region"));

        Assert.Equal("Only SELECT statements are catalogued.", ex.Message);
    }

    [Theory]
    [InlineData("SELECT region FROM t ORDER BY region -- sneaky")]
    [InlineData("SELECT region /* tricky */ FROM t ORDER BY region")]
    public void Sql_comments_are_rejected_regardless_of_position(string query)
    {
        var ex = Assert.Throws<ReadOnlyQueryException>(() => ReadOnlyQueryGuard.Validate(query));

        Assert.Equal("SQL comments are not allowed.", ex.Message);
    }

    // ── keyword blocklist ────────────────────────────────────────────────────

    [Theory]
    [InlineData("SELECT region FROM (INSERT INTO t VALUES (1)) AS x ORDER BY region")]
    [InlineData("SELECT region FROM t WHERE region = (DROP TABLE t) ORDER BY region")]
    [InlineData("SELECT region, lower(kill) AS verdict FROM watchlist ORDER BY verdict")]
    [InlineData("SELECT grant AS g FROM donations ORDER BY 1")]
    [InlineData("select vacuum as mode from robot order by mode")]
    public void Blocklisted_keywords_fail_even_nested(string query)
    {
        var ex = Assert.Throws<ReadOnlyQueryException>(() => ReadOnlyQueryGuard.Validate(query));

        Assert.Contains("not allowed", ex.Message, StringComparison.Ordinal);
        Assert.Equal("INVALID_QUERY", ex.Code);
    }

    // ── paging contract ──────────────────────────────────────────────────────

    [Fact]
    public void Select_without_order_by_breaks_the_paging_contract()
    {
        var ex = Assert.Throws<ReadOnlyQueryException>(
            () => ReadOnlyQueryGuard.Validate("SELECT region, COUNT(1) AS n FROM demo_facts GROUP BY region"));

        Assert.Contains("ORDER BY", ex.Message, StringComparison.Ordinal);
    }

    // ── normalize + constants ────────────────────────────────────────────────

    [Fact]
    public void Normalize_trims_and_passes_through_a_clean_query()
    {
        var normalized = ReadOnlyQueryGuard.Normalize("\r\n  " + CleanQuery + " \t ");

        Assert.Equal(CleanQuery, normalized);
    }

    [Fact]
    public void Reject_code_is_the_webapp_contract()
    {
        Assert.Equal("INVALID_QUERY", ReadOnlyQueryGuard.RejectCode);
        Assert.Equal(4000, ReadOnlyQueryGuard.MaxQueryChars);
    }
}
