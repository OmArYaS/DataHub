using System.Security.Cryptography;
using System.Text;

namespace DataHub.Application.Reports;

/// <summary>
/// Deterministic ETag for a result page: SHA-256 over shape + first rows +
/// totals, rendered as 16 hex chars. Two calls that would return the same
/// table share the stamp — If-None-Match becomes a real 304 instead of
/// faith-based caching.
/// </summary>
public static class CacheStamp
{
    public static string Compute(string reportKey, string[] columns, string?[][] rows, long totalCount, int page, int pageSize)
    {
        var sb = new StringBuilder();

        sb.Append("rk:").Append(reportKey);
        sb.Append("|pg:").Append(page).Append('|').Append(pageSize);
        sb.Append("|tc:").Append(totalCount);
        sb.Append("|cols:").AppendJoin(',', columns);

        foreach (var row in rows.Take(64))
        {
            sb.Append("|r:").AppendJoin(',', row);
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));

        return Convert.ToHexString(hash, 0, 8); // 16 hex chars
    }
}
