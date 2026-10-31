using DataHub.Application.Reports;

using Xunit;

namespace DataHub.Tests.Application;

/// <summary>
/// ETag determinism: identical table content shares a stamp wherever it was
/// produced (fresh read or cache wall) — that is what makes If-None-Match a
/// real 304 rather than hope.
/// </summary>
public sealed class CacheStampTests
{
    private static readonly string[] Columns = ["region", "total_amount", "order_count"];

    [Fact]
    public void Identical_inputs_share_a_stamp()
    {
        var first = CacheStamp.Compute("region_revenue", Columns, [["north", "120.50", "11"]], 4, 1, 100);
        var second = CacheStamp.Compute("region_revenue", Columns, [["north", "120.50", "11"]], 4, 1, 100);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Any_input_change_moves_the_stamp()
    {
        var baseline = CacheStamp.Compute("region_revenue", Columns, [["north", "120.50", "11"]], 4, 1, 100);

        var otherKey = CacheStamp.Compute("other_report", Columns, [["north", "120.50", "11"]], 4, 1, 100);
        var otherColumn = CacheStamp.Compute("region_revenue", ["region", "total_spend"], [["north", "120.50", "11"]], 4, 1, 100);
        var otherCell = CacheStamp.Compute("region_revenue", Columns, [["north", "120.51", "11"]], 4, 1, 100);
        var otherPage = CacheStamp.Compute("region_revenue", Columns, [["north", "120.50", "11"]], 4, 2, 100);

        Assert.NotEqual(baseline, otherKey);
        Assert.NotEqual(baseline, otherColumn);
        Assert.NotEqual(baseline, otherCell);
        Assert.NotEqual(baseline, otherPage);
    }

    [Fact]
    public void Stamp_is_sixteen_hex_characters()
    {
        var stamp = CacheStamp.Compute("region_revenue", Columns, [["north", "120.50", "11"]], 4, 1, 100);

        Assert.Matches("^[0-9A-F]{16}$", stamp);
    }
}
