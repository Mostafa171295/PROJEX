using Projex.Cost.Engine.Evm;

namespace Projex.Domain.Tests;

public sealed class PlannedValueCalculatorTests
{
    private static readonly DateTimeOffset Jan1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Jan11 = new(2026, 1, 11, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-1, 0)]   // before start
    [InlineData(0, 0)]    // at start
    [InlineData(5, 0.5)]  // halfway through 10 days
    [InlineData(10, 1)]   // at finish
    [InlineData(30, 1)]   // after finish
    public void Planned_fraction_is_linear_over_elapsed_time(int dayOffset, double expected)
        => Assert.Equal((decimal)expected, PlannedValueCalculator.PlannedFraction(Jan1, Jan11, Jan1.AddDays(dayOffset)));

    [Fact]
    public void Milestone_is_planned_only_at_its_finish()
    {
        Assert.Equal(0m, PlannedValueCalculator.PlannedFraction(Jan11, Jan11, Jan11.AddTicks(-1)));
        Assert.Equal(1m, PlannedValueCalculator.PlannedFraction(Jan11, Jan11, Jan11));
    }

    [Fact]
    public void Rejects_finish_before_start()
        => Assert.Throws<ArgumentException>(() => PlannedValueCalculator.PlannedFraction(Jan11, Jan1, Jan1));

    [Fact]
    public void Sums_planned_and_earned_value_across_work()
    {
        var work = new[]
        {
            new BudgetedWork("A", 1000m, Jan1, Jan11, 25m),
            new BudgetedWork("B", 200m, Jan11, Jan11, 100m)
        };
        var dataDate = Jan1.AddDays(5);
        Assert.Equal(500m, PlannedValueCalculator.PlannedValue(work, dataDate));   // A 50% planned; milestone B not yet due
        Assert.Equal(1200m, PlannedValueCalculator.PlannedValue(work, Jan11));     // both fully planned
        Assert.Equal(450m, PlannedValueCalculator.EarnedValue(work));  // 1000*25% + 200*100%
    }

    [Fact]
    public void Rejects_out_of_range_percent()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlannedValueCalculator.EarnedValue([new BudgetedWork("A", 10m, Jan1, Jan11, 101m)]));
}
