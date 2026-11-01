using Projex.Domain.Common;

namespace Projex.Domain.Costing;

public enum CostCategory { Labor, Material, Equipment, Subcontract, Overhead, Other }

public sealed class CostCode : Entity
{
    private CostCode() { }
    public CostCode(Guid tenantId, Guid projectId, string code, string name, CostCategory category, Guid? parentId = null)
    {
        if (tenantId == Guid.Empty || projectId == Guid.Empty) throw new ArgumentException("Tenant and project are required.");
        TenantId = tenantId; ProjectId = projectId; ParentId = parentId; Category = category; IsActive = true;
        Code = Required(code, 80, nameof(code)); Name = Required(name, 200, nameof(name));
    }
    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid? ParentId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public CostCategory Category { get; private set; }
    public bool IsActive { get; private set; }
    public void Deactivate() { IsActive = false; Touch(); }
    private static string Required(string value, int max, string arg) { if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > max) throw new ArgumentException($"{arg} is required and must not exceed {max} characters.", arg); return value.Trim(); }
}
