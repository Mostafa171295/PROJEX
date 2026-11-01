namespace Projex.Application.Security;

public sealed record PermissionDefinition(string Code, string Module, string Description);
/// <param name="ClaimName">Value of the role claim in access tokens (e.g. "CompanyAdmin"); the code is the database/API identifier.</param>
public sealed record SystemRoleDefinition(string Code, string ClaimName, string Name, IReadOnlyList<string> Permissions);

/// <summary>
/// The built-in permission catalog and system roles (SRS 3.1/3.2). It is the single source of truth in code and is seeded by
/// database/migrations/013_rbac.sql; an integration test fails if the two ever drift apart.
/// </summary>
public static class RoleCatalog
{
    public const string CompanyAdminCode = "COMPANYADMIN";

    public static class Permissions
    {
        public const string ProjectsRead = "projects.read";
        public const string ProgressUpdate = "progress.update";
        public const string PlanningManage = "planning.manage";
        public const string CostingRead = "costing.read";
        public const string CostingTransactionsEnter = "costing.transactions.enter";
        public const string CostingManage = "costing.manage";
        public const string CostingApprove = "costing.approve";
        public const string ReportsRead = "reports.read";
        public const string ReportsExport = "reports.export";
        public const string AuditRead = "audit.read";
        public const string OrganizationManage = "organization.manage";
        public const string IdentityManage = "identity.manage";
    }

    public static readonly IReadOnlyList<PermissionDefinition> AllPermissions =
    [
        new("projects.read", "projects", "View projects the user is a member of"),
        new("progress.update", "progress", "Record physical progress on activities"),
        new("planning.manage", "planning", "Edit WBS, activities, relationships, calendars, run schedules, manage baselines"),
        new("costing.read", "costing", "View cost codes, BOQ, budgets, transactions and EVM"),
        new("costing.transactions.enter", "costing", "Enter and submit draft cost transactions"),
        new("costing.manage", "costing", "Manage cost codes, BOQ and draft budgets; capture performance snapshots"),
        new("costing.approve", "costing", "Approve budgets and cost transactions; post reversals"),
        new("reports.read", "reports", "View reports"),
        new("reports.export", "reports", "Export reports"),
        new("audit.read", "audit", "View the tenant audit trail"),
        new("organization.manage", "organization", "Manage companies and branches"),
        new("identity.manage", "identity", "Manage users, roles and role assignments"),
    ];

    public static readonly IReadOnlyList<SystemRoleDefinition> SystemRoles =
    [
        new("COMPANYADMIN", "CompanyAdmin", "Company Administrator", AllPermissions.Select(p => p.Code).ToArray()),
        new("PROJECTMANAGER", "ProjectManager", "Project Manager", ["projects.read", "progress.update", "planning.manage", "costing.read", "costing.transactions.enter", "costing.manage", "costing.approve", "reports.read", "reports.export"]),
        new("PLANNINGENGINEER", "PlanningEngineer", "Planning Engineer", ["projects.read", "progress.update", "planning.manage", "reports.read"]),
        new("COSTCONTROLENGINEER", "CostControlEngineer", "Cost Control Engineer", ["projects.read", "costing.read", "costing.transactions.enter", "costing.manage", "reports.read", "reports.export"]),
        new("ACCOUNTANT", "Accountant", "Accountant", ["projects.read", "costing.read", "costing.transactions.enter", "reports.read"]),
        new("SITEENGINEER", "SiteEngineer", "Site Engineer", ["projects.read", "progress.update"]),
        new("VIEWER", "Viewer", "Viewer", ["projects.read", "costing.read", "reports.read"]),
        new("AUDITOR", "Auditor", "Auditor", ["projects.read", "costing.read", "audit.read", "reports.read", "reports.export"]),
    ];

    public static bool IsSystemRole(string? code) => code is not null && SystemRoles.Any(r => r.Code == code.Trim().ToUpperInvariant());

    /// <summary>Roles that can be given on a single project through a project membership (everything except CompanyAdmin).</summary>
    public static bool IsProjectRole(string? code) => IsSystemRole(code) && code!.Trim().ToUpperInvariant() != CompanyAdminCode;

    public static IReadOnlySet<string> PermissionsFor(string? roleCode)
    {
        var role = SystemRoles.FirstOrDefault(r => r.Code == (roleCode ?? string.Empty).Trim().ToUpperInvariant());
        return role is null ? new HashSet<string>() : role.Permissions.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The role claim for a role code: the friendly name for system roles, the code itself for custom roles.</summary>
    public static string ClaimNameFor(string roleCode) => SystemRoles.FirstOrDefault(r => r.Code == roleCode)?.ClaimName ?? roleCode;
}
