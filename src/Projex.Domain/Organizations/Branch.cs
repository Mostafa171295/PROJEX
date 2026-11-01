using Projex.Domain.Common;

namespace Projex.Domain.Organizations;

public sealed class Branch : Entity
{
    private Branch() { }
    public Branch(Guid tenantId, Guid companyId, string code, string name)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty) throw new ArgumentException("Tenant and company are required.");
        TenantId = tenantId; CompanyId = companyId;
        Code = Required(code, 40); Name = Required(name, 200); IsActive = true;
    }
    public Guid TenantId { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    private static string Required(string value, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max
            ? value.Trim() : throw new ArgumentException($"A value of 1-{max} characters is required.");
}
