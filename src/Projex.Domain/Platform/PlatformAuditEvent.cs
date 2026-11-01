namespace Projex.Domain.Platform;

/// <summary>Append-only record of a platform-level action (tenant created/suspended, licence changed, admin sign-in...). Never carries secrets.</summary>
public sealed class PlatformAuditEvent
{
    private PlatformAuditEvent() { }
    public PlatformAuditEvent(Guid? adminId, string action, Guid? targetTenantId, string? detailsJson, string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("An action is required.", nameof(action));
        Id = Guid.NewGuid(); OccurredAt = DateTimeOffset.UtcNow; AdminId = adminId; Action = action.Length <= 100 ? action : action[..100];
        TargetTenantId = targetTenantId; Details = detailsJson; IpAddress = ipAddress is { Length: > 64 } ? ipAddress[..64] : ipAddress;
    }
    public Guid Id { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public Guid? AdminId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public Guid? TargetTenantId { get; private set; }
    public string? Details { get; private set; }
    public string? IpAddress { get; private set; }
}
