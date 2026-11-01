using Projex.Domain.Common;

namespace Projex.Domain.Security;

/// <summary>
/// A user holds a role either tenant-wide (no scope), for one company, for one branch of a company, or on one project.
/// A role given tenant-wide or per company applies to every project in that scope the user can see.
/// </summary>
public sealed class UserRoleAssignment : Entity
{
    private UserRoleAssignment() { }
    public UserRoleAssignment(Guid tenantId, Guid userId, Guid roleId, Guid? companyId, Guid? branchId, Guid? projectId, Guid? assignedBy)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty || roleId == Guid.Empty) throw new ArgumentException("Tenant, user and role are required.");
        if (branchId is not null && companyId is null) throw new ArgumentException("A branch scope needs its company.");
        if (projectId is not null && (companyId is not null || branchId is not null)) throw new ArgumentException("Use either a project scope or a company/branch scope, not both.");
        TenantId = tenantId; UserId = userId; RoleId = roleId; CompanyId = companyId; BranchId = branchId; ProjectId = projectId; AssignedBy = assignedBy;
    }
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public Guid? CompanyId { get; private set; }
    public Guid? BranchId { get; private set; }
    public Guid? ProjectId { get; private set; }
    public Guid? AssignedBy { get; private set; }
    public bool IsTenantWide => CompanyId is null && BranchId is null && ProjectId is null;
}
