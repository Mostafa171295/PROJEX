using Projex.Cost.Engine.Evm;

namespace Projex.Domain.Tests;

public sealed class EvmCalculatorTests
{
    private readonly EvmCalculator _calc = new();
    private static readonly EvmInput Behind = new(PlannedValue: 100m, EarnedValue: 80m, ActualCost: 100m, BudgetAtCompletion: 1000m);

    [Fact]
    public void Computes_variances_and_indices_from_reference_values()
    {
        var r = _calc.Calculate(Behind);
        Assert.Equal(-20m, r.CostVariance);
        Assert.Equal(-20m, r.ScheduleVariance);
        Assert.Equal(0.8m, r.CostPerformanceIndex);
        Assert.Equal(0.8m, r.SchedulePerformanceIndex);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void Cost_performance_eac_follows_SRS_formulas()
    {
        var r = _calc.Calculate(Behind, EacMethod.CostPerformance);
        Assert.Equal(1250m, r.EstimateAtCompletion);   // BAC / CPI
        Assert.Equal(1150m, r.EstimateToComplete);     // EAC - AC
        Assert.Equal(-250m, r.VarianceAtCompletion);   // BAC - EAC
    }

    [Fact]
    public void Cost_and_schedule_eac_uses_both_indices()
    {
        var r = _calc.Calculate(Behind, EacMethod.CostAndSchedulePerformance);
        Assert.Equal(1537.5m, r.EstimateAtCompletion); // 100 + (1000-80)/(0.8*0.8)
        Assert.Equal(1437.5m, r.EstimateToComplete);
    }

    [Fact]
    public void Bottom_up_eac_adds_supplied_etc_to_actual_cost()
    {
        var r = _calc.Calculate(Behind, EacMethod.BottomUp, 700m);
        Assert.Equal(800m, r.EstimateAtCompletion);
        Assert.Equal(700m, r.EstimateToComplete);
        Assert.Equal(200m, r.VarianceAtCompletion);
    }

    [Fact]
    public void Bottom_up_without_etc_yields_no_forecast_instead_of_a_guess()
    {
        var r = _calc.Calculate(Behind, EacMethod.BottomUp);
        Assert.Null(r.EstimateAtCompletion);
        Assert.Contains("EAC_UNAVAILABLE_BOTTOM_UP_ETC_REQUIRED", r.Warnings);
    }

    [Fact]
    public void No_actual_cost_means_no_cpi_and_no_eac_not_zero_or_infinity()
    {
        var r = _calc.Calculate(new EvmInput(100m, 0m, 0m, 1000m));
        Assert.Null(r.CostPerformanceIndex);
        Assert.Null(r.EstimateAtCompletion);
        Assert.Equal(0m, r.SchedulePerformanceIndex);
        Assert.Contains("CPI_UNAVAILABLE_NO_ACTUAL_COST", r.Warnings);
    }

    [Fact]
    public void Spending_without_earned_value_gives_zero_cpi_but_no_eac()
    {
        var r = _calc.Calculate(new EvmInput(100m, 0m, 50m, 1000m));
        Assert.Equal(0m, r.CostPerformanceIndex);
        Assert.Null(r.EstimateAtCompletion);
        Assert.Contains("EAC_UNAVAILABLE_ZERO_EARNED_VALUE", r.Warnings);
    }

    [Fact]
    public void No_planned_value_means_no_spi()
    {
        var r = _calc.Calculate(new EvmInput(0m, 10m, 10m, 100m));
        Assert.Null(r.SchedulePerformanceIndex);
        Assert.Contains("SPI_UNAVAILABLE_NO_PLANNED_VALUE", r.Warnings);
    }

    [Fact]
    public void Flags_earned_value_above_budget()
    {
        var r = _calc.Calculate(new EvmInput(100m, 120m, 100m, 100m));
        Assert.Contains("EARNED_VALUE_EXCEEDS_BUDGET", r.Warnings);
    }

    [Fact]
    public void Rejects_negative_inputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _calc.Calculate(new EvmInput(-1m, 0m, 0m, 0m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _calc.Calculate(Behind, EacMethod.BottomUp, -1m));
    }
}
