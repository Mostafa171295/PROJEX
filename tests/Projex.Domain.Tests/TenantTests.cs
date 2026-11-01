using Projex.Domain.Organizations;
using Xunit;

namespace Projex.Domain.Tests;

public sealed class TenantTests
{
    [Fact]
    public void TenantKey_IsNormalizedToLowercase()
    {
        var tenant = new Tenant(" ACME ", "Acme Group");
        Assert.Equal("acme", tenant.Code);
        Assert.Equal(TenantStatus.Active, tenant.Status);
    }

    [Fact]
    public void SuspendedTenant_CanBeReactivated()
    {
        var tenant = new Tenant("acme", "Acme Group");
        tenant.Suspend();
        Assert.Equal(TenantStatus.Suspended, tenant.Status);
        tenant.Activate();
        Assert.Equal(TenantStatus.Active, tenant.Status);
    }

    [Fact]
    public void Company_RequiresTenantId()
    {
        Assert.Throws<ArgumentException>(() => new Company(Guid.Empty, "HQ", "Head Office"));
    }

    [Fact]
    public void ClosedTenant_CannotBeSuspendedOrReactivated()
    {
        var tenant = new Tenant("acme", "Acme Group");
        tenant.Close();
        Assert.Equal(TenantStatus.Closed, tenant.Status);
        Assert.Throws<InvalidOperationException>(() => tenant.Suspend());
        Assert.Throws<InvalidOperationException>(() => tenant.Activate());
    }

    [Fact]
    public void Tenant_DefaultsToEnglishLocale_AndAcceptsAnother()
    {
        Assert.Equal("en", new Tenant("acme", "Acme").DefaultLocale);
        Assert.Equal("ar", new Tenant("acme", "Acme", "ar").DefaultLocale);
    }
}
