using Projex.Domain.Common;

namespace Projex.Domain.Security;

public sealed class UserMembership : Entity
{
    private UserMembership() { }
    public UserMembership(Guid tenantId, Guid companyId, Guid userId)
    {
        if (tenantId == Guid.Empty || companyId == Guid.Empty || userId == Guid.Empty) throw new ArgumentException("Tenant, company and user are required.");
        TenantId = tenantId; CompanyId = companyId; UserId = userId; IsActive = true;
    }
    public Guid TenantId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid UserId { get; private set; }
    public bool IsActive { get; private set; }
    public void Revoke() { IsActive = false; Touch(); }
}
