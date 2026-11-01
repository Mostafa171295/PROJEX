namespace Projex.Application.Security;

public enum ProjectPermission
{
    Read, UpdateProgress, Plan,
    /// <summary>Cost figures are sensitive: SiteEngineer and PlanningEngineer can see schedules but not budgets/costs.</summary>
    ReadCost,
    /// <summary>Create cost codes, BOQ, draft budgets; capture performance snapshots.</summary>
    ManageCost,
    /// <summary>Approve budgets and cost transactions, and post reversals.</summary>
    ApproveCost,
    /// <summary>Enter and submit draft cost transactions (Accountant, Cost Control Engineer, Project Manager).</summary>
    EnterCost
}

/// <summary>
/// What each role may do on a project. The permission catalog (<see cref="RoleCatalog"/>) is the single source of truth;
/// this class only maps the API-level <see cref="ProjectPermission"/> onto catalog permission codes. Enforced on the server for every endpoint.
/// </summary>
public static class ProjectAccess
{
    public static string RequiredCode(ProjectPermission permission) => permission switch
    {
        ProjectPermission.Read => RoleCatalog.Permissions.ProjectsRead,
        ProjectPermission.UpdateProgress => RoleCatalog.Permissions.ProgressUpdate,
        ProjectPermission.Plan => RoleCatalog.Permissions.PlanningManage,
        ProjectPermission.ReadCost => RoleCatalog.Permissions.CostingRead,
        ProjectPermission.EnterCost => RoleCatalog.Permissions.CostingTransactionsEnter,
        ProjectPermission.ManageCost => RoleCatalog.Permissions.CostingManage,
        ProjectPermission.ApproveCost => RoleCatalog.Permissions.CostingApprove,
        _ => throw new ArgumentOutOfRangeException(nameof(permission), permission, null)
    };

    /// <summary>Company administrators may do everything; otherwise any of the member's system project roles must grant the permission.</summary>
    public static bool IsAllowed(ProjectPermission permission, bool isCompanyAdmin, IEnumerable<string> activeProjectRoles)
    {
        if (isCompanyAdmin) return true;
        var required = RequiredCode(permission);
        return activeProjectRoles.Any(role => RoleCatalog.PermissionsFor(role).Contains(required));
    }

    /// <summary>Evaluation against permission codes already resolved from the database (custom roles and scoped assignments).</summary>
    public static bool IsAllowedByPermissions(ProjectPermission permission, IReadOnlySet<string> grantedCodes) =>
        grantedCodes.Contains(RequiredCode(permission));
}
