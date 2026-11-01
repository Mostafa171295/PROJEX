using Projex.Domain.Common;
namespace Projex.Domain.Planning;
public enum PlanningRelationshipType { FS, SS, FF, SF }
public sealed class ActivityRelationshipEntity : Entity
{
    private ActivityRelationshipEntity() { }
    public ActivityRelationshipEntity(Guid tenantId, Guid projectId, Guid predecessorId, Guid successorId, PlanningRelationshipType type, int lagMinutes = 0)
    {
        if (tenantId == Guid.Empty || projectId == Guid.Empty || predecessorId == Guid.Empty || successorId == Guid.Empty) throw new ArgumentException("Relationship scope and activities are required.");
        if (predecessorId == successorId) throw new ArgumentException("An activity cannot depend on itself.");
        TenantId = tenantId; ProjectId = projectId; PredecessorActivityId = predecessorId; SuccessorActivityId = successorId; Type = type; LagMinutes = lagMinutes;
    }
    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid PredecessorActivityId { get; private set; }
    public Guid SuccessorActivityId { get; private set; }
    public PlanningRelationshipType Type { get; private set; }
    public int LagMinutes { get; private set; }
}
