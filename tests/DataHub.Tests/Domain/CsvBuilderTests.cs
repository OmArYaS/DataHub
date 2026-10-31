using DataHub.Domain.Rules;

using Xunit;

namespace DataHub.Tests.Domain;

/// <summary>
/// RFC 4180 writer pinned: header + rows, quote-when-necessary, doubled
/// embedded quotes, CRLF endings, empty for null. Excel/Power-BI round-trip
/// safety lives on these exact choices — the on-disk sync contract.
/// </summary>
public sealed class CsvBuilderTests
{
    private static string Build(string?[][] rows) =>
        CsvBuilder.Build(["col_a", "col_b"], rows);

    [Fact]
    public void Plain_rows_write_as_is_with_a_header()
    {
        var csv = Build([["north", "120.50"]]);

        Assert.Equal("col_a,col_b\r\nnorth,120.50\r\n", csv);
    }

    [Fact]
    public void Commas_force_quoting()
    {
        var csv = Build([["a,b", "plain"]]);

        Assert.Equal("col_a,col_b\r\n\"a,b\",plain\r\n", csv);
    }

    [Fact]
    public void Embedded_quotes_are_doubled_inside_quotes()
    {
        var csv = Build([["he said \"ok\"", "plain"]]);

        Assert.Equal("col_a,col_b\r\n\"he said \"\"ok\"\"\",plain\r\n", csv);
    }

    [Fact]
    public void CrLf_needs_quotes_too()
    {
        var csv = Build([["line1\r\nline2", "plain"]]);

        Assert.Equal("col_a,col_b\r\n\"line1\r\nline2\",plain\r\n", csv);
    }

    [Fact]
    public void Null_cells_render_empty_not_the_word_null()
    {
        var csv = Build([["val", null]]);

        Assert.Equal("col_a,col_b\r\nval,\r\n", csv);
    }

    [Fact]
    public void Empty_table_still_carries_the_header()
    {
        var csv = Build([]);

        Assert.Equal("col_a,col_b\r\n", csv);
    }
}
