using Projex.Domain.Common;

namespace Projex.Domain.Planning;

public sealed class WbsNode : Entity
{
    private WbsNode() { }
    public WbsNode(Guid tenantId, Guid projectId, string code, string name, Guid? parentId = null, int sortOrder = 0)
    {
        if (tenantId == Guid.Empty || projectId == Guid.Empty) throw new ArgumentException("Tenant and project are required.");
        TenantId = tenantId; ProjectId = projectId; ParentId = parentId;
        Code = Required(code, 80, nameof(code)); Name = Required(name, 200, nameof(name)); SortOrder = sortOrder;
    }
    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid? ParentId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }
    public void Rename(string name) { Name = Required(name, 200, nameof(name)); Touch(); }
    public void Move(Guid? parentId, int sortOrder) { if (parentId == Id) throw new InvalidOperationException("A WBS node cannot be its own parent."); ParentId = parentId; SortOrder = sortOrder; Touch(); }
    private static string Required(string value, int max, string arg) { if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > max) throw new ArgumentException($"{arg} is required and must not exceed {max} characters.", arg); return value.Trim(); }
}
