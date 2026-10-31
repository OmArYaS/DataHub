using DataHub.Application.Common;
using DataHub.Domain.Entities;

namespace DataHub.Application.Reports;

/// <summary>
/// Permission engine: a report runs for admins and for callers whose token
/// carries at least one role granted on the report. Kept static and pure —
/// the unit tests iterate the decision matrix.
/// </summary>
public static class ReportAccess
{
    /// <summary>Admins bypass grants; everyone else needs one matching grant.</summary>
    public static bool CanRun(ReportDefinition report, ICurrentUser user) =>
        user.IsAdmin
        || report.Permissions.Any(p =>
            user.HasRole(p.RoleName));

    public static void EnsureCanRun(ReportDefinition report, ICurrentUser user)
    {
        if (!CanRun(report, user))
        {
            throw new BusinessRuleException(RuleCodes.PermissionDenied,
                $"Role(s) [{string.Join(", ", user.Roles)}] are not granted on '{report.ReportKey}'.", 403);
        }
    }
}
