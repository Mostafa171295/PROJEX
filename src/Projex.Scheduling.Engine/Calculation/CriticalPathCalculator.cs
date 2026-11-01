using Projex.Scheduling.Engine.Models;

namespace Projex.Scheduling.Engine.Calculation;

/// <summary>
/// Deterministic CPM calculation for FS/SS/FF/SF relationships.
/// Durations and lags must use the same scalar time unit (the API uses working minutes).
/// Calendar placement is handled by WorkingCalendarEngine after the network calculation.
/// </summary>
public sealed class CriticalPathCalculator
{
    private const decimal Epsilon = 0.000001m;

    public ScheduleCalculationResult Calculate(
        IReadOnlyCollection<ScheduleActivity> activities,
        IReadOnlyCollection<ActivityRelationship> relationships)
    {
        ArgumentNullException.ThrowIfNull(activities);
        ArgumentNullException.ThrowIfNull(relationships);
        if (activities.Count == 0) throw new ArgumentException("At least one activity is required.", nameof(activities));

        var byId = new Dictionary<string, ScheduleActivity>(StringComparer.OrdinalIgnoreCase);
        foreach (var activity in activities)
        {
            if (string.IsNullOrWhiteSpace(activity.Id)) throw new ArgumentException("Activity ID is required.");
            if (string.IsNullOrWhiteSpace(activity.Name)) throw new ArgumentException($"Activity {activity.Id} requires a name.");
            if (activity.Duration < 0) throw new ArgumentOutOfRangeException(nameof(activities), $"Activity {activity.Id} has a negative duration.");
            if (!byId.TryAdd(activity.Id, activity)) throw new ArgumentException($"Duplicate activity ID: {activity.Id}.");
        }

        var incoming = byId.Keys.ToDictionary(x => x, _ => new List<ActivityRelationship>(), StringComparer.OrdinalIgnoreCase);
        var outgoing = byId.Keys.ToDictionary(x => x, _ => new List<ActivityRelationship>(), StringComparer.OrdinalIgnoreCase);
        foreach (var relation in relationships)
        {
            if (!byId.ContainsKey(relation.PredecessorId) || !byId.ContainsKey(relation.SuccessorId))
                throw new ArgumentException($"Relationship references an unknown activity: {relation.PredecessorId} -> {relation.SuccessorId}.");
            if (string.Equals(relation.PredecessorId, relation.SuccessorId, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("An activity cannot depend on itself.");
            incoming[relation.SuccessorId].Add(relation);
            outgoing[relation.PredecessorId].Add(relation);
        }

        var order = TopologicalSort(byId.Keys, outgoing);
        var es = byId.Keys.ToDictionary(x => x, _ => 0m, StringComparer.OrdinalIgnoreCase);
        var ef = byId.Keys.ToDictionary(x => x, _ => 0m, StringComparer.OrdinalIgnoreCase);

        foreach (var id in order)
        {
            var duration = byId[id].Duration;
            foreach (var relation in incoming[id])
            {
                var p = relation.PredecessorId;
                var candidate = relation.Type switch
                {
                    RelationshipType.FinishToStart => ef[p] + relation.Lag,
                    RelationshipType.StartToStart => es[p] + relation.Lag,
                    RelationshipType.FinishToFinish => ef[p] + relation.Lag - duration,
                    RelationshipType.StartToFinish => es[p] + relation.Lag - duration,
                    _ => throw new ArgumentOutOfRangeException(nameof(relationships), "Unsupported relationship type.")
                };
                es[id] = Math.Max(es[id], candidate);
            }
            ef[id] = es[id] + duration;
        }

        var projectDuration = ef.Values.Max();
        var ls = byId.Keys.ToDictionary(x => x, _ => projectDuration, StringComparer.OrdinalIgnoreCase);
        var lf = byId.Keys.ToDictionary(x => x, _ => projectDuration, StringComparer.OrdinalIgnoreCase);

        for (var i = order.Count - 1; i >= 0; i--)
        {
            var id = order[i];
            var duration = byId[id].Duration;
            if (outgoing[id].Count == 0)
            {
                lf[id] = projectDuration;
                ls[id] = projectDuration - duration;
                continue;
            }

            var latestStart = decimal.MaxValue;
            foreach (var relation in outgoing[id])
            {
                var s = relation.SuccessorId;
                var successorDuration = byId[s].Duration;
                var bound = relation.Type switch
                {
                    RelationshipType.FinishToStart => ls[s] - duration - relation.Lag,
                    RelationshipType.StartToStart => ls[s] - relation.Lag,
                    RelationshipType.FinishToFinish => ls[s] + successorDuration - duration - relation.Lag,
                    RelationshipType.StartToFinish => ls[s] + successorDuration - relation.Lag,
                    _ => throw new ArgumentOutOfRangeException(nameof(relationships), "Unsupported relationship type.")
                };
                latestStart = Math.Min(latestStart, bound);
            }
            ls[id] = latestStart;
            lf[id] = latestStart + duration;
        }

        var results = order.Select(id =>
        {
            var totalFloat = ls[id] - es[id];
            var critical = Math.Abs(totalFloat) <= Epsilon;
            return new ActivityScheduleResult(id, byId[id].Name, byId[id].Duration,
                es[id], ef[id], ls[id], lf[id], totalFloat, critical);
        }).ToList();

        var criticalPath = results.Where(x => x.IsCritical).Select(x => x.ActivityId).ToList();
        return new ScheduleCalculationResult(projectDuration, results, criticalPath, order);
    }

    private static List<string> TopologicalSort(IEnumerable<string> ids,
        IReadOnlyDictionary<string, List<ActivityRelationship>> outgoing)
    {
        var indegree = ids.ToDictionary(x => x, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var relationList in outgoing.Values)
            foreach (var relation in relationList) indegree[relation.SuccessorId]++;

        var queue = new Queue<string>(indegree.Where(x => x.Value == 0).Select(x => x.Key).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        var result = new List<string>(indegree.Count);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            result.Add(id);
            foreach (var relation in outgoing[id])
            {
                indegree[relation.SuccessorId]--;
                if (indegree[relation.SuccessorId] == 0) queue.Enqueue(relation.SuccessorId);
            }
        }
        if (result.Count != indegree.Count)
            throw new InvalidOperationException("Schedule network contains a dependency cycle.");
        return result;
    }
}
