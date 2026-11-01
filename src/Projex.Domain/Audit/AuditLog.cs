using System.Net;

namespace Projex.Domain.Audit;

/// <summary>Append-only audit record (FR-AUD-001/002). Deliberately not an Entity: no UpdatedAt, never modified.</summary>
public sealed class AuditLog
{
    private AuditLog() { }
    public AuditLog(Guid tenantId, Guid? actorUserId, string action, string entityType, Guid? entityId,
        Guid? correlationId, IPAddress? ipAddress, string? userAgent, string? beforeData, string? afterData)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(action) || action.Length > 100) throw new ArgumentException("Action is required (max 100).", nameof(action));
        if (string.IsNullOrWhiteSpace(entityType) || entityType.Length > 100) throw new ArgumentException("Entity type is required (max 100).", nameof(entityType));
        Id = Guid.NewGuid(); TenantId = tenantId; ActorUserId = actorUserId; Action = action; EntityType = entityType; EntityId = entityId;
        OccurredAt = DateTimeOffset.UtcNow; CorrelationId = correlationId; IpAddress = ipAddress; UserAgent = userAgent;
        BeforeData = beforeData; AfterData = afterData;
    }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public Guid? EntityId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public Guid? CorrelationId { get; private set; }
    public IPAddress? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string? BeforeData { get; private set; }
    public string? AfterData { get; private set; }
}
