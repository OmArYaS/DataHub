using System.Text;

namespace DataHub.Domain.Rules;

/// <summary>
/// Minimal RFC 4180 writer for sync partitions: header line, quoted fields
/// when necessary (comma, quote, CR/LF), doubled embedded quotes, invariant
/// everything. The file layout is the sync job's on-disk contract
/// (datahub-sync/{reportKey}/{yyyy/MM/dd}/part-*.csv), so formatting is
/// specified once here and tested exhaustively.
/// </summary>
public static class CsvBuilder
{
    public static string Build(IReadOnlyList<string> columns, string?[][] rows)
    {
        var sb = new StringBuilder();

        AppendRow(sb, columns);

        foreach (var row in rows)
        {
            AppendRow(sb, row);
        }

        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, IEnumerable<string?> values)
    {
        var first = true;

        foreach (var value in values)
        {
            if (!first)
            {
                sb.Append(',');
            }

            first = false;
            sb.Append(Escape(value));
        }

        sb.Append("\r\n");
    }

    private static string Escape(string? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var needsQuotes = value.Contains(',')
            || value.Contains('"')
            || value.Contains('\r')
            || value.Contains('\n');

        return needsQuotes
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }
}
