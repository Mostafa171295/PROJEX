namespace Projex.Cost.Engine.Evm;

/// <summary>One budgeted work item with its baseline dates and current physical progress.</summary>
public sealed record BudgetedWork(
    string Key, decimal Budget, DateTimeOffset BaselineStart, DateTimeOffset BaselineFinish, decimal PercentComplete);

/// <summary>
/// Planned Value and Earned Value from activity-linked budget.
/// Documented assumption: planned value accrues LINEARLY over elapsed calendar time between baseline start
/// and finish (a milestone, or zero-length item, is planned at its finish instant). Working-time weighting
/// is a later refinement and must be validated against reference projects before it replaces this rule.
/// </summary>
public static class PlannedValueCalculator
{
    public static decimal PlannedFraction(DateTimeOffset start, DateTimeOffset finish, DateTimeOffset dataDate)
    {
        if (finish < start) throw new ArgumentException("Finish must not precede start.");
        if (dataDate >= finish) return 1m;
        if (dataDate <= start) return 0m;
        var total = (finish - start).Ticks;
        return total == 0 ? 1m : (decimal)(dataDate - start).Ticks / total;
    }

    public static decimal PlannedValue(IEnumerable<BudgetedWork> work, DateTimeOffset dataDate)
        => work.Sum(w => w.Budget * PlannedFraction(w.BaselineStart, w.BaselineFinish, dataDate));

    public static decimal EarnedValue(IEnumerable<BudgetedWork> work)
        => work.Sum(w => w.PercentComplete is < 0 or > 100
            ? throw new ArgumentOutOfRangeException(nameof(work), $"Percent complete out of range for {w.Key}.")
            : w.Budget * w.PercentComplete / 100m);
}
