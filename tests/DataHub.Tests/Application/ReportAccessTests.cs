using DataHub.Application.Common;
using DataHub.Application.Reports;
using DataHub.Domain.Entities;

using Xunit;

namespace DataHub.Tests.Application;

/// <summary>
/// The permission decision matrix: admin bypass, grant matching (any
/// intersecting role wins), case-insensitivity against the token vocabulary,
/// and the 403 codes the API contract depends on.
/// </summary>
public sealed class ReportAccessTests
{
    private readonly ReportDefinition report = new()
    {
        ReportKey = "region_revenue",
        DisplayName = "Region revenue",
        Permissions = [new ReportGrant { RoleName = "viewer" }, new ReportGrant { RoleName = "analyst" }]
    };

    [Fact]
    public void Admin_runs_even_without_a_grant()
    {
        Assert.True(ReportAccess.CanRun(report, FixedUser.Analyst)!);

        var admin = new FixedUser("nadia", "admin");
        Assert.True(ReportAccess.CanRun(report, admin));
    }

    [Theory]
    [InlineData("viewer", true)]
    [InlineData("analyst", true)]
    [InlineData("admin", true)]
    [InlineData("auditor", false)]
    public void Granted_vocabulary_decides_access(string role, bool allowed)
    {
        var user = new FixedUser("someone", role);

        Assert.Equal(allowed, ReportAccess.CanRun(report, user));
    }

    [Fact]
    public void Role_comparison_is_case_insensitive_against_token_bag()
    {
        var user = new FixedUser("someone", "VIEWER");

        Assert.True(ReportAccess.CanRun(report, user));
    }

    [Fact]
    public void No_matching_grant_is_a_403_permission_denied()
    {
        var user = new FixedUser("someone", "auditor");

        var ex = Assert.Throws<BusinessRuleException>(() => ReportAccess.EnsureCanRun(report, user));

        Assert.Equal(RuleCodes.PermissionDenied, ex.Code);
        Assert.Equal(403, ex.StatusCode);
        Assert.Contains("auditor", ex.Message, StringComparison.Ordinal);
        Assert.Contains("region_revenue", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Grant_with_a_role_outside_the_vocabulary_grants_nothing()
    {
        var odd = new ReportDefinition
        {
            ReportKey = "mismatched",
            Permissions = [new ReportGrant { RoleName = "superuser" }]
        };

        Assert.False(ReportAccess.CanRun(odd, FixedUser.Viewer));
    }
}
