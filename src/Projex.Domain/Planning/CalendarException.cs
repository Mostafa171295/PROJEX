using Projex.Domain.Common;
namespace Projex.Domain.Planning;
public sealed class CalendarException : Entity
{
    private CalendarException() { }
    public CalendarException(Guid tenantId, Guid calendarId, DateOnly date, bool isWorkingDay, TimeOnly? startTime, TimeOnly? endTime, string? description)
    {
        if (tenantId == Guid.Empty || calendarId == Guid.Empty) throw new ArgumentException("Tenant and calendar are required.");
        if (isWorkingDay && startTime.HasValue != endTime.HasValue) throw new ArgumentException("Both working-day times must be provided together.");
        if (startTime.HasValue && endTime <= startTime) throw new ArgumentException("Calendar exception end must be after start.");
        TenantId = tenantId; CalendarId = calendarId; ExceptionDate = date; IsWorkingDay = isWorkingDay; StartTime = startTime; EndTime = endTime; Description = description?.Trim();
    }
    public Guid TenantId { get; private set; }
    public Guid CalendarId { get; private set; }
    public DateOnly ExceptionDate { get; private set; }
    public bool IsWorkingDay { get; private set; }
    public TimeOnly? StartTime { get; private set; }
    public TimeOnly? EndTime { get; private set; }
    public string? Description { get; private set; }
}
