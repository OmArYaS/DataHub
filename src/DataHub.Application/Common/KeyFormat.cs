using System.Text.RegularExpressions;

namespace DataHub.Application.Common;

/// <summary>Catalog-key syntax shared by sources and reports (a-z, digits, underscore).</summary>
public static class KeyFormat
{
    private static readonly Regex Pattern = new("^[a-z][a-z0-9_]{2,39}$", RegexOptions.Compiled);

    public static bool IsCatalogKey(string? key) =>
        key is not null && Pattern.IsMatch(key);
}
