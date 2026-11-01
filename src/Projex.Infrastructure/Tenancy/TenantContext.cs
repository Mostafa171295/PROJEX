using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Projex.Application.Abstractions;

namespace Projex.Infrastructure.Tenancy;

public sealed class TenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    private Guid? _bootstrapTenantId;
    private ClaimsPrincipal? User => accessor.HttpContext?.User;
    public Guid? TenantId => _bootstrapTenantId ?? ReadGuid("tenant_id");
    public void SetBootstrapTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant ID is required.", nameof(tenantId));
        _bootstrapTenantId = tenantId;
    }
    public Guid? UserId => ReadGuid(ClaimTypes.NameIdentifier) ?? ReadGuid("sub");
    public bool IsResolved => TenantId.HasValue && UserId.HasValue;
    private Guid? ReadGuid(string claimType) => Guid.TryParse(User?.FindFirst(claimType)?.Value, out var value) ? value : null;
}
