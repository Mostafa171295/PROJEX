using Projex.Domain.Common;

namespace Projex.Domain.Planning;

public enum ActivityType { Task, Milestone }
public enum ActivityStatus { NotStarted, InProgress, Completed, OnHold }

public sealed class ProjectActivity : Entity
{
    private ProjectActivity() { }
    public ProjectActivity(Guid tenantId, Guid projectId, Guid wbsNodeId, string code, string name, int durationMinutes, ActivityType type = ActivityType.Task, Guid? calendarId = null)
    {
        if (tenantId == Guid.Empty || projectId == Guid.Empty || wbsNodeId == Guid.Empty) throw new ArgumentException("Tenant, project and WBS node are required.");
        if (durationMinutes < 0 || (type == ActivityType.Milestone && durationMinutes != 0)) throw new ArgumentOutOfRangeException(nameof(durationMinutes));
        TenantId = tenantId; ProjectId = projectId; WbsNodeId = wbsNodeId; ActivityCode = Required(code, 50, nameof(code)); Name = Required(name, 250, nameof(name)); DurationMinutes = durationMinutes; Type = type; CalendarId = calendarId;
    }
    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid WbsNodeId { get; private set; }
    public Guid? CalendarId { get; private set; }
    public string ActivityCode { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public ActivityType Type { get; private set; }
    public int DurationMinutes { get; private set; }
    public DateTimeOffset? PlannedStart { get; private set; }
    public DateTimeOffset? PlannedFinish { get; private set; }
    public DateTimeOffset? ActualStart { get; private set; }
    public DateTimeOffset? ActualFinish { get; private set; }
    public decimal PercentComplete { get; private set; }
    public ActivityStatus Status { get; private set; } = ActivityStatus.NotStarted;
    public bool IsCritical { get; private set; }
    public decimal TotalFloatMinutes { get; private set; }
    public decimal? EarlyStartWorkingDays { get; private set; }
    public decimal? EarlyFinishWorkingDays { get; private set; }
    public decimal? LateStartWorkingDays { get; private set; }
    public decimal? LateFinishWorkingDays { get; private set; }
    public long RowVersion { get; private set; }
    public void UpdateDetails(string name, int durationMinutes, Guid wbsNodeId, Guid? calendarId)
    {
        if (durationMinutes < 0 || (Type == ActivityType.Milestone && durationMinutes != 0)) throw new ArgumentOutOfRangeException(nameof(durationMinutes));
        Name = Required(name, 250, nameof(name)); DurationMinutes = durationMinutes; WbsNodeId = wbsNodeId; CalendarId = calendarId; RowVersion++; Touch();
    }
    public void SetProgress(decimal percent, ActivityStatus status, DateTimeOffset? actualStart = null, DateTimeOffset? actualFinish = null)
    {
        if (percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        if (actualFinish.HasValue && (!actualStart.HasValue || actualFinish < actualStart)) throw new ArgumentException("Actual finish must be on or after actual start.");
        PercentComplete = percent; Status = status; ActualStart = actualStart; ActualFinish = actualFinish; RowVersion++; Touch();
    }
    public void ApplySchedule(decimal es, decimal ef, decimal ls, decimal lf, decimal totalFloat)
    {
        EarlyStartWorkingDays = es; EarlyFinishWorkingDays = ef; LateStartWorkingDays = ls; LateFinishWorkingDays = lf;
        TotalFloatMinutes = totalFloat * 480m; IsCritical = Math.Abs(totalFloat) < 0.000001m; RowVersion++; Touch();
    }

    /// <summary>Persists calendar-aware schedule offsets (in minutes) and their real local-calendar dates.</summary>
    public void ApplyCalendarSchedule(decimal earlyStartMinutes, decimal earlyFinishMinutes,
        decimal lateStartMinutes, decimal lateFinishMinutes, decimal totalFloatMinutes,
        DateTimeOffset plannedStart, DateTimeOffset plannedFinish)
    {
        if (plannedFinish < plannedStart) throw new ArgumentException("Planned finish cannot precede planned start.");
        const decimal minutesPerStandardWorkingDay = 480m;
        EarlyStartWorkingDays = earlyStartMinutes / minutesPerStandardWorkingDay;
        EarlyFinishWorkingDays = earlyFinishMinutes / minutesPerStandardWorkingDay;
        LateStartWorkingDays = lateStartMinutes / minutesPerStandardWorkingDay;
        LateFinishWorkingDays = lateFinishMinutes / minutesPerStandardWorkingDay;
        TotalFloatMinutes = totalFloatMinutes;
        IsCritical = Math.Abs(totalFloatMinutes) < 0.000001m;
        PlannedStart = plannedStart;
        PlannedFinish = plannedFinish;
        RowVersion++;
        Touch();
    }
    private static string Required(string value, int max, string arg) { if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > max) throw new ArgumentException($"{arg} is required and must not exceed {max} characters.", arg); return value.Trim(); }
}
