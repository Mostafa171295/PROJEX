using Projex.Scheduling.Engine.Models;

namespace Projex.Scheduling.Engine.Analysis;

public sealed record ScheduleImpactNode(string ActivityId, int DependencyDepth,
    string PredecessorActivityId, RelationshipType RelationshipType, decimal Lag);

/// <summary>
/// Finds all downstream activities reachable from a changed activity.
/// This is dependency impact analysis, not a substitute for recalculating CPM.
/// </summary>
public sealed class ScheduleImpactAnalyzer
{
    public IReadOnlyList<ScheduleImpactNode> Analyze(
        string sourceActivityId,
        IEnumerable<ActivityRelationship> relationships,
        int maxDepth = 100)
    {
        if (string.IsNullOrWhiteSpace(sourceActivityId))
            throw new ArgumentException("Source activity ID is required.", nameof(sourceActivityId));
        if (maxDepth < 1) throw new ArgumentOutOfRangeException(nameof(maxDepth));

        var outgoing = relationships
            .GroupBy(x => x.PredecessorId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal) { sourceActivityId };
        var queue = new Queue<(string ActivityId, int Depth)>();
        var result = new List<ScheduleImpactNode>();
        queue.Enqueue((sourceActivityId, 0));

        while (queue.Count > 0)
        {
            var (currentId, depth) = queue.Dequeue();
            if (depth >= maxDepth || !outgoing.TryGetValue(currentId, out var successors)) continue;
            foreach (var relationship in successors)
            {
                if (!visited.Add(relationship.SuccessorId)) continue;
                var nextDepth = depth + 1;
                result.Add(new ScheduleImpactNode(relationship.SuccessorId, nextDepth,
                    currentId, relationship.Type, relationship.Lag));
                queue.Enqueue((relationship.SuccessorId, nextDepth));
            }
        }

        return result;
    }
}
