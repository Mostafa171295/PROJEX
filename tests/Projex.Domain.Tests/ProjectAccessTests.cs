using Projex.Application.Security;

namespace Projex.Domain.Tests;

public class ProjectAccessTests
{
    [Theory]
    [InlineData("VIEWER", ProjectPermission.Read, true)]
    [InlineData("VIEWER", ProjectPermission.UpdateProgress, false)]
    [InlineData("VIEWER", ProjectPermission.Plan, false)]
    [InlineData("SITEENGINEER", ProjectPermission.Read, true)]
    [InlineData("SITEENGINEER", ProjectPermission.UpdateProgress, true)]
    [InlineData("SITEENGINEER", ProjectPermission.Plan, false)]
    [InlineData("COSTCONTROLENGINEER", ProjectPermission.Read, true)]
    [InlineData("COSTCONTROLENGINEER", ProjectPermission.Plan, false)]
    [InlineData("PLANNINGENGINEER", ProjectPermission.Plan, true)]
    [InlineData("PROJECTMANAGER", ProjectPermission.Plan, true)]
    [InlineData("planningengineer", ProjectPermission.UpdateProgress, true)]
    [InlineData("COSTCONTROLENGINEER", ProjectPermission.ReadCost, true)]
    [InlineData("COSTCONTROLENGINEER", ProjectPermission.ManageCost, true)]
    [InlineData("COSTCONTROLENGINEER", ProjectPermission.ApproveCost, false)]
    [InlineData("PROJECTMANAGER", ProjectPermission.ApproveCost, true)]
    [InlineData("VIEWER", ProjectPermission.ReadCost, true)]
    [InlineData("VIEWER", ProjectPermission.ManageCost, false)]
    [InlineData("SITEENGINEER", ProjectPermission.ReadCost, false)]
    [InlineData("PLANNINGENGINEER", ProjectPermission.ReadCost, false)]
    [InlineData("ACCOUNTANT", ProjectPermission.Read, true)]
    [InlineData("ACCOUNTANT", ProjectPermission.ReadCost, true)]
    [InlineData("ACCOUNTANT", ProjectPermission.EnterCost, true)]
    [InlineData("ACCOUNTANT", ProjectPermission.ManageCost, false)]
    [InlineData("ACCOUNTANT", ProjectPermission.ApproveCost, false)]
    [InlineData("ACCOUNTANT", ProjectPermission.UpdateProgress, false)]
    [InlineData("AUDITOR", ProjectPermission.Read, true)]
    [InlineData("AUDITOR", ProjectPermission.ReadCost, true)]
    [InlineData("AUDITOR", ProjectPermission.EnterCost, false)]
    [InlineData("AUDITOR", ProjectPermission.Plan, false)]
    [InlineData("COSTCONTROLENGINEER", ProjectPermission.EnterCost, true)]
    [InlineData("PROJECTMANAGER", ProjectPermission.EnterCost, true)]
    [InlineData("VIEWER", ProjectPermission.EnterCost, false)]
    [InlineData("SITEENGINEER", ProjectPermission.EnterCost, false)]
    public void Project_roles_get_least_privilege(string role, ProjectPermission permission, bool expected)
        => Assert.Equal(expected, ProjectAccess.IsAllowed(permission, false, [role]));

    [Fact]
    public void No_project_membership_means_no_access_even_for_reads()
        => Assert.False(ProjectAccess.IsAllowed(ProjectPermission.Read, false, []));

    [Theory]
    [InlineData(ProjectPermission.Read)]
    [InlineData(ProjectPermission.UpdateProgress)]
    [InlineData(ProjectPermission.Plan)]
    [InlineData(ProjectPermission.ReadCost)]
    [InlineData(ProjectPermission.ManageCost)]
    [InlineData(ProjectPermission.ApproveCost)]
    [InlineData(ProjectPermission.EnterCost)]
    public void Company_admin_is_allowed_everything(ProjectPermission permission)
        => Assert.True(ProjectAccess.IsAllowed(permission, true, []));
}
