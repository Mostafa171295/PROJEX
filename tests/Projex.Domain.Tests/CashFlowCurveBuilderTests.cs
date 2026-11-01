using Projex.Cost.Engine.CashFlow;

namespace Projex.Domain.Tests;

public sealed class CashFlowCurveBuilderTests
{
    [Fact]
    public void Spreads_amount_by_calendar_days_across_months()
    {
        // 20 days: Jan 21-31 (11 days) and Feb 1-9 (9 days)
        var buckets = CashFlowCurveBuilder.BuildMonthly([new SpreadItem(400m, new DateOnly(2026, 1, 21), new DateOnly(2026, 2, 9))]);
        Assert.Equal(2, buckets.Count);
        Assert.Equal(220m, buckets[0].Amount);
        Assert.Equal(180m, buckets[1].Amount);
        Assert.Equal(400m, buckets[1].Cumulative);
    }

    [Fact]
    public void Totals_stay_exact_when_the_split_does_not_divide_evenly()
    {
        var buckets = CashFlowCurveBuilder.BuildMonthly([new SpreadItem(100m, new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 2))]);
        Assert.Equal(33.3333m, buckets[0].Amount);
        Assert.Equal(100m, buckets[^1].Cumulative);
    }

    [Fact]
    public void Merges_items_in_the_same_month_and_orders_periods()
    {
        var buckets = CashFlowCurveBuilder.BucketDated([
            (new DateOnly(2026, 3, 1), 7m), (new DateOnly(2026, 1, 15), 10m), (new DateOnly(2026, 1, 20), 5m)]);
        Assert.Equal(new[] { new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1) }, buckets.Select(b => b.PeriodStart));
        Assert.Equal(new[] { 15m, 7m }, buckets.Select(b => b.Amount));
        Assert.Equal(new[] { 15m, 22m }, buckets.Select(b => b.Cumulative));
    }

    [Fact]
    public void Rejects_finish_before_start()
        => Assert.Throws<ArgumentException>(() =>
            CashFlowCurveBuilder.BuildMonthly([new SpreadItem(1m, new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1))]));
}
