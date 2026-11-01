using Projex.Application.Security;
using Projex.Domain.Security;
using Xunit;

namespace Projex.Domain.Tests;

public sealed class RbacTests
{
    [Fact]
    public void Catalog_has_the_eight_tenant_roles_with_unique_codes()
    {
        Assert.Equal(8, RoleCatalog.SystemRoles.Count);
        Assert.Equal(RoleCatalog.SystemRoles.Count, RoleCatalog.SystemRoles.Select(r => r.Code).Distinct().Count());
        Assert.Equal(RoleCatalog.SystemRoles.Count, RoleCatalog.SystemRoles.Select(r => r.ClaimName).Distinct().Count());
    }

    [Fact]
    public void Every_role_permission_exists_in_the_permission_catalog()
    {
        var known = RoleCatalog.AllPermissions.Select(p => p.Code).ToHashSet();
        Assert.Equal(known.Count, RoleCatalog.AllPermissions.Count);
        foreach (var role in RoleCatalog.SystemRoles)
            Assert.All(role.Permissions, code => Assert.Contains(code, known));
    }

    [Fact]
    public void Company_admin_holds_every_permission_and_nobody_else_can_approve_cost_except_the_project_manager()
    {
        Assert.Equal(RoleCatalog.AllPermissions.Count, RoleCatalog.PermissionsFor("companyadmin").Count);
        var approvers = RoleCatalog.SystemRoles.Where(r => r.Permissions.Contains(RoleCatalog.Permissions.CostingApprove)).Select(r => r.Code).OrderBy(c => c);
        Assert.Equal(new[] { "COMPANYADMIN", "PROJECTMANAGER" }, approvers.ToArray());
    }

    [Fact]
    public void Read_only_roles_cannot_change_anything()
    {
        foreach (var code in new[] { "VIEWER", "AUDITOR" })
        {
            var granted = RoleCatalog.PermissionsFor(code);
            Assert.DoesNotContain(RoleCatalog.Permissions.ProgressUpdate, granted);
            Assert.DoesNotContain(RoleCatalog.Permissions.PlanningManage, granted);
            Assert.DoesNotContain(RoleCatalog.Permissions.CostingManage, granted);
            Assert.DoesNotContain(RoleCatalog.Permissions.CostingTransactionsEnter, granted);
            Assert.DoesNotContain(RoleCatalog.Permissions.CostingApprove, granted);
        }
    }

    [Fact]
    public void Claim_names_and_project_roles()
    {
        Assert.Equal("CompanyAdmin", RoleCatalog.ClaimNameFor("COMPANYADMIN"));
        Assert.Equal("SENIOR_PLANNER", RoleCatalog.ClaimNameFor("SENIOR_PLANNER"));
        Assert.True(RoleCatalog.IsProjectRole("accountant"));
        Assert.False(RoleCatalog.IsProjectRole("COMPANYADMIN"));
        Assert.False(RoleCatalog.IsProjectRole("SENIOR_PLANNER"));
        Assert.Empty(RoleCatalog.PermissionsFor("SENIOR_PLANNER"));
    }

    [Fact]
    public void Every_project_permission_maps_to_a_catalog_permission()
    {
        var known = RoleCatalog.AllPermissions.Select(p => p.Code).ToHashSet();
        foreach (var permission in Enum.GetValues<ProjectPermission>()) Assert.Contains(ProjectAccess.RequiredCode(permission), known);
    }

    [Fact]
    public void Permissions_resolved_from_the_database_are_checked_by_code()
    {
        IReadOnlySet<string> granted = new HashSet<string> { RoleCatalog.Permissions.ProjectsRead, RoleCatalog.Permissions.CostingTransactionsEnter };
        Assert.True(ProjectAccess.IsAllowedByPermissions(ProjectPermission.Read, granted));
        Assert.True(ProjectAccess.IsAllowedByPermissions(ProjectPermission.EnterCost, granted));
        Assert.False(ProjectAccess.IsAllowedByPermissions(ProjectPermission.ApproveCost, granted));
    }

    [Theory]
    [InlineData("senior_planner", "SENIOR_PLANNER")]
    [InlineData(" Site_Lead2 ", "SITE_LEAD2")]
    public void Custom_role_codes_are_normalized(string input, string expected) => Assert.Equal(expected, new Role(Guid.NewGuid(), input, "Name").Code);

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("1ABC")]
    [InlineData("HAS SPACE")]
    [InlineData("dash-ed")]
    public void Invalid_custom_role_codes_are_rejected(string input) => Assert.Throws<ArgumentException>(() => new Role(Guid.NewGuid(), input, "Name"));

    [Fact]
    public void Assignment_scopes_are_consistent()
    {
        Guid t = Guid.NewGuid(), u = Guid.NewGuid(), r = Guid.NewGuid(), c = Guid.NewGuid(), b = Guid.NewGuid(), p = Guid.NewGuid();
        Assert.True(new UserRoleAssignment(t, u, r, null, null, null, null).IsTenantWide);
        Assert.False(new UserRoleAssignment(t, u, r, c, null, null, null).IsTenantWide);
        Assert.False(new UserRoleAssignment(t, u, r, c, b, null, null).IsTenantWide);
        Assert.False(new UserRoleAssignment(t, u, r, null, null, p, null).IsTenantWide);
        Assert.Throws<ArgumentException>(() => new UserRoleAssignment(t, u, r, null, b, null, null));   // branch without company
        Assert.Throws<ArgumentException>(() => new UserRoleAssignment(t, u, r, c, null, p, null));      // project + company
        Assert.Throws<ArgumentException>(() => new UserRoleAssignment(Guid.Empty, u, r, null, null, null, null));
    }

    [Fact]
    public void Project_membership_accepts_the_new_roles_only_from_the_catalog()
    {
        Assert.Equal("ACCOUNTANT", new ProjectMembership(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "accountant").RoleCode);
        Assert.Equal("AUDITOR", new ProjectMembership(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Auditor").RoleCode);
        Assert.Throws<ArgumentException>(() => new ProjectMembership(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "COMPANYADMIN"));
    }
}
