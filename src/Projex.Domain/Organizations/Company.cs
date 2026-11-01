using Projex.Domain.Common;

namespace Projex.Domain.Organizations;

public sealed class Company : Entity
{
    private Company() { }
    public Company(Guid tenantId, string code, string name)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        TenantId = tenantId;
        Code = Required(code, 40);
        Name = Required(name, 200);
        IsActive = true;
    }
    public Guid TenantId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public void Rename(string name) { Name = Required(name, 200); Touch(); }
    public void Deactivate() { IsActive = false; Touch(); }
    private static string Required(string value, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max
            ? value.Trim() : throw new ArgumentException($"A value of 1-{max} characters is required.");
}
