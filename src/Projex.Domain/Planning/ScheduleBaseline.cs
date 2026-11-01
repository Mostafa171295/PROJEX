using Projex.Domain.Common;
namespace Projex.Domain.Planning;
public enum BaselineStatus { Draft, Approved, Superseded }
public sealed class ScheduleBaseline : Entity
{
    private ScheduleBaseline() { }
    public ScheduleBaseline(Guid tenantId, Guid projectId, string name, int version, string? changeReason = null)
    {
        if (tenantId == Guid.Empty || projectId == Guid.Empty) throw new ArgumentException("Tenant and project are required.");
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        TenantId = tenantId; ProjectId = projectId; Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Name is required.") : name.Trim(); Version = version; ChangeReason = changeReason?.Trim(); Status = BaselineStatus.Draft; CapturedAt = DateTimeOffset.UtcNow;
    }
    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int Version { get; private set; }
    public BaselineStatus Status { get; private set; }
    public DateTimeOffset CapturedAt { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public string? ChangeReason { get; private set; }
    public void Approve(Guid userId) { if (Status != BaselineStatus.Draft) throw new InvalidOperationException("Only a draft baseline can be approved."); if (userId == Guid.Empty) throw new ArgumentException("Approver is required."); ApprovedBy = userId; ApprovedAt = DateTimeOffset.UtcNow; Status = BaselineStatus.Approved; Touch(); }
}
public sealed class BaselineActivityDate : Entity
{
    private BaselineActivityDate() { }
    public BaselineActivityDate(Guid tenantId, Guid baselineId, Guid activityId, DateTimeOffset? plannedStart, DateTimeOffset? plannedFinish, int durationMinutes, decimal percentComplete)
    {
        if (tenantId == Guid.Empty || baselineId == Guid.Empty || activityId == Guid.Empty) throw new ArgumentException("Baseline scope is required.");
        if (durationMinutes < 0 || percentComplete is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(durationMinutes));
        TenantId = tenantId; BaselineId = baselineId; ActivityId = activityId; PlannedStart = plannedStart; PlannedFinish = plannedFinish; DurationMinutes = durationMinutes; PercentComplete = percentComplete;
    }
    public Guid TenantId { get; private set; }
    public Guid BaselineId { get; private set; }
    public Guid ActivityId { get; private set; }
    public DateTimeOffset? PlannedStart { get; private set; }
    public DateTimeOffset? PlannedFinish { get; private set; }
    public int DurationMinutes { get; private set; }
    public decimal PercentComplete { get; private set; }
}
