using Projex.Domain.Common;
namespace Projex.Domain.Planning;
public sealed class ScheduleRun : Entity
{
    private ScheduleRun() { }
    public ScheduleRun(Guid tenantId, Guid projectId, Guid calculatedBy, string engineVersion, string resultJson)
    {
        if (tenantId == Guid.Empty || projectId == Guid.Empty || calculatedBy == Guid.Empty) throw new ArgumentException("Schedule run scope and actor are required.");
        TenantId = tenantId; ProjectId = projectId; CalculatedBy = calculatedBy; EngineVersion = engineVersion; ResultJson = resultJson; CalculatedAt = DateTimeOffset.UtcNow;
    }
    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid CalculatedBy { get; private set; }
    public string EngineVersion { get; private set; } = "1.0";
    public string ResultJson { get; private set; } = "{}";
    public DateTimeOffset CalculatedAt { get; private set; }
}
