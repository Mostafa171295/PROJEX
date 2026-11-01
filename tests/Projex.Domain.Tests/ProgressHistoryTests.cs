using Projex.Cost.Engine.Evm;

namespace Projex.Domain.Tests;

public sealed class ProgressHistoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
    private static (DateOnly, DateTimeOffset, decimal) U(int day, int hour, decimal pct) => (new DateOnly(2026, 11, day), T0.AddHours(hour), pct);

    [Fact]
    public void Uses_latest_update_on_or_before_the_date()
    {
        var h = new[] { U(1, 0, 10m), U(5, 0, 40m), U(9, 0, 80m) };
        Assert.Equal(40m, ProgressHistory.PercentAsOf(h, new DateOnly(2026, 11, 7)));
        Assert.Equal(40m, ProgressHistory.PercentAsOf(h, new DateOnly(2026, 11, 5)));
        Assert.Equal(80m, ProgressHistory.PercentAsOf(h, new DateOnly(2026, 12, 1)));
    }

    [Fact]
    public void Returns_null_before_any_update()
        => Assert.Null(ProgressHistory.PercentAsOf(new[] { U(5, 0, 40m) }, new DateOnly(2026, 11, 4)));

    [Fact]
    public void Same_data_date_resolves_to_last_recorded_correction()
    {
        var h = new[] { U(5, 1, 30m), U(5, 3, 35m), U(5, 2, 33m) };
        Assert.Equal(35m, ProgressHistory.PercentAsOf(h, new DateOnly(2026, 11, 5)));
    }

    [Fact]
    public void Empty_history_is_null()
        => Assert.Null(ProgressHistory.PercentAsOf(Array.Empty<(DateOnly, DateTimeOffset, decimal)>(), new DateOnly(2026, 11, 5)));
}
