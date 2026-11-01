using Microsoft.AspNetCore.Identity;

namespace Projex.Infrastructure.Tenancy;

public sealed class ProjexUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastLoginAt { get; set; }
}
