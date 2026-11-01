using Projex.Domain.Common;

namespace Projex.Domain.Security;

public sealed class ProjectMembership : Entity
{
    private ProjectMembership() { }
    public ProjectMembership(Guid tenantId, Guid projectId, Guid userId, string roleCode)
    {
        if (tenantId == Guid.Empty || projectId == Guid.Empty || userId == Guid.Empty)
            throw new ArgumentException("Tenant, project and user are required.");
        TenantId = tenantId; ProjectId = projectId; UserId = userId;
        RoleCode = NormalizeRole(roleCode); IsActive = true;
    }
    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid UserId { get; private set; }
    public string RoleCode { get; private set; } = "VIEWER";
    public bool IsActive { get; private set; }
    public void ChangeRole(string roleCode) { RoleCode = NormalizeRole(roleCode); Touch(); }
    public void Revoke() { IsActive = false; Touch(); }
    private static string NormalizeRole(string value)
    {
        var role = (value ?? string.Empty).Trim().ToUpperInvariant();
        string[] allowed = ["PROJECTMANAGER", "PLANNINGENGINEER", "COSTCONTROLENGINEER", "ACCOUNTANT", "SITEENGINEER", "VIEWER", "AUDITOR"];
        return allowed.Contains(role) ? role : throw new ArgumentException("Unsupported project role.", nameof(value));
    }
}
