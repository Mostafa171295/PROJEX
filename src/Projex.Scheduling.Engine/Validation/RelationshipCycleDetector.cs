namespace Projex.Scheduling.Engine.Validation;

public static class RelationshipCycleDetector
{
    /// <summary>True when adding predecessor -> successor would close a loop (or is a self-link).</summary>
    public static bool WouldCreateCycle(IEnumerable<(Guid Predecessor, Guid Successor)> existing, Guid predecessor, Guid successor)
    {
        if (predecessor == successor) return true;
        var next = existing.GroupBy(e => e.Predecessor).ToDictionary(g => g.Key, g => g.Select(e => e.Successor).ToList());
        var stack = new Stack<Guid>();
        var seen = new HashSet<Guid>();
        stack.Push(successor);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node == predecessor) return true; // successor already reaches predecessor
            if (!seen.Add(node)) continue;
            if (next.TryGetValue(node, out var children)) foreach (var child in children) stack.Push(child);
        }
        return false;
    }
}
