using Projex.Domain.Common;

namespace Projex.Domain.Progress;

/// <summary>One dated progress observation for an activity. Append-only: a correction is a new row with the same or a later data date.</summary>
public sealed class ProgressUpdate : Entity
{
    private ProgressUpdate() { }
    public ProgressUpdate(Guid tenantId, Guid projectId, Guid activityId, DateOnly dataDate, decimal percentComplete, string status, Guid submittedBy,
        string measurementMethod = "PhysicalPercent")
    {
        if (tenantId == Guid.Empty || projectId == Guid.Empty || activityId == Guid.Empty || submittedBy == Guid.Empty) throw new ArgumentException("Tenant, project, activity and submitter are required.");
        if (percentComplete is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percentComplete));
        if (string.IsNullOrWhiteSpace(status) || status.Length > 20) throw new ArgumentException("Status is required.", nameof(status));
        TenantId = tenantId; ProjectId = projectId; ActivityId = activityId; DataDate = dataDate; PercentComplete = percentComplete;
        Status = status; SubmittedBy = submittedBy; MeasurementMethod = measurementMethod;
    }
    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid ActivityId { get; private set; }
    public DateOnly DataDate { get; private set; }
    public string MeasurementMethod { get; private set; } = "PhysicalPercent";
    public decimal PercentComplete { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public Guid SubmittedBy { get; private set; }
}
