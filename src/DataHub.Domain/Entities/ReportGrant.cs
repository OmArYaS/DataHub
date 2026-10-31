namespace DataHub.Domain.Entities;

/// <summary>
/// Role grant on one report (join entity). Role strings come from the token's
/// <c>roles</c> claim and must match the lowercase hub vocabulary
/// (viewer/analyst/admin) — enforced by <see cref="Rules.ReportRuleSet"/>.
/// </summary>
public sealed class ReportGrant
{
    public int Id { get; set; }

    public int ReportId { get; set; }

    public string RoleName { get; set; } = string.Empty;
}
