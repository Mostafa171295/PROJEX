namespace Projex.Scheduling.Engine.Models;

public enum RelationshipType { FinishToStart, StartToStart, FinishToFinish, StartToFinish }

/// <summary>Lag uses the same scalar time unit as activity duration; negative values represent lead.</summary>
public sealed record ActivityRelationship(string PredecessorId, string SuccessorId,
    RelationshipType Type = RelationshipType.FinishToStart, decimal Lag = 0m);
