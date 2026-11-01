namespace Projex.Cost.Engine.CashFlow;

/// <summary>An amount spread evenly across the calendar days from Start to Finish inclusive.</summary>
public sealed record SpreadItem(decimal Amount, DateOnly Start, DateOnly Finish);

public sealed record CashFlowBucket(DateOnly PeriodStart, decimal Amount, decimal Cumulative);

/// <summary>Monthly distribution and S-curve. Totals are exact: rounding remainders are assigned to the last bucket of each item.</summary>
public static class CashFlowCurveBuilder
{
    private const int Scale = 4;

    public static IReadOnlyList<CashFlowBucket> BuildMonthly(IEnumerable<SpreadItem> items)
    {
        var perMonth = new SortedDictionary<DateOnly, decimal>();
        foreach (var item in items)
        {
            if (item.Finish < item.Start) throw new ArgumentException("Finish must not precede start.");
            var totalDays = item.Finish.DayNumber - item.Start.DayNumber + 1;
            var allocated = 0m;
            var cursor = item.Start;
            while (cursor <= item.Finish)
            {
                var monthStart = new DateOnly(cursor.Year, cursor.Month, 1);
                var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                var segmentEnd = monthEnd < item.Finish ? monthEnd : item.Finish;
                var days = segmentEnd.DayNumber - cursor.DayNumber + 1;
                var isLast = segmentEnd == item.Finish;
                var part = isLast ? item.Amount - allocated : Math.Round(item.Amount * days / totalDays, Scale, MidpointRounding.ToEven);
                allocated += part;
                perMonth[monthStart] = (perMonth.TryGetValue(monthStart, out var existing) ? existing : 0m) + part;
                cursor = segmentEnd.AddDays(1);
            }
        }
        var cumulative = 0m;
        var result = new List<CashFlowBucket>(perMonth.Count);
        foreach (var (period, amount) in perMonth) { cumulative += amount; result.Add(new CashFlowBucket(period, amount, cumulative)); }
        return result;
    }

    /// <summary>Buckets dated amounts (e.g. approved actual costs) by month.</summary>
    public static IReadOnlyList<CashFlowBucket> BucketDated(IEnumerable<(DateOnly Date, decimal Amount)> entries)
        => BuildMonthly(entries.Select(e => new SpreadItem(e.Amount, e.Date, e.Date)));
}
