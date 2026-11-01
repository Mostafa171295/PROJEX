using Projex.Domain.Common;
namespace Projex.Domain.Planning;
public sealed class ProjectCalendar : Entity
{
    private ProjectCalendar() { }
    public ProjectCalendar(Guid tenantId, Guid projectId, string name, string timeZone, string workingWeekJson, bool isDefault = false)
    {
        if (tenantId == Guid.Empty || projectId == Guid.Empty) throw new ArgumentException("Tenant and project are required.");
        TenantId = tenantId; ProjectId = projectId; Name = Required(name, 120); TimeZone = Required(timeZone, 80); WorkingWeekJson = string.IsNullOrWhiteSpace(workingWeekJson) ? "{}" : workingWeekJson; IsDefault = isDefault;
    }
    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string TimeZone { get; private set; } = "UTC";
    public string WorkingWeekJson { get; private set; } = "{}";
    public bool IsDefault { get; private set; }
    private static string Required(string value, int max) { if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > max) throw new ArgumentException("A valid value is required."); return value.Trim(); }
}
