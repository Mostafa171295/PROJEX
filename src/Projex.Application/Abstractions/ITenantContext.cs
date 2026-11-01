namespace Projex.Application.Abstractions;

/// <summary>Tenant identity resolved from a validated authentication principal, never from an untrusted request header.</summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
    Guid? UserId { get; }
    bool IsResolved { get; }
}
