namespace Projex.Cost.Engine.Evm;

/// <summary>Method used to forecast Estimate At Completion (SRS FR-EVM-004: never assume a single formula).</summary>
public enum EacMethod
{
    /// <summary>EAC = BAC / CPI. Assumes current cost efficiency continues (SRS 9.3).</summary>
    CostPerformance,
    /// <summary>EAC = AC + (BAC - EV) / (CPI * SPI). Assumes both cost and schedule efficiency continue.</summary>
    CostAndSchedulePerformance,
    /// <summary>EAC = AC + bottom-up ETC supplied by the user.</summary>
    BottomUp
}

public sealed record EvmInput(decimal PlannedValue, decimal EarnedValue, decimal ActualCost, decimal BudgetAtCompletion);

/// <summary>
/// Indicators are null (never zero, never infinity) when their inputs do not support them,
/// and <see cref="Warnings"/> explains why (SRS FR-EVM-005).
/// </summary>
public sealed record EvmResult(
    decimal PlannedValue, decimal EarnedValue, decimal ActualCost, decimal BudgetAtCompletion,
    decimal CostVariance, decimal ScheduleVariance,
    decimal? CostPerformanceIndex, decimal? SchedulePerformanceIndex,
    decimal? EstimateAtCompletion, decimal? EstimateToComplete, decimal? VarianceAtCompletion,
    EacMethod EacMethod, IReadOnlyList<string> Warnings);

public sealed class EvmCalculator
{
    public EvmResult Calculate(EvmInput input, EacMethod method = EacMethod.CostPerformance, decimal? bottomUpEstimateToComplete = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.PlannedValue < 0 || input.EarnedValue < 0 || input.ActualCost < 0 || input.BudgetAtCompletion < 0)
            throw new ArgumentOutOfRangeException(nameof(input), "PV, EV, AC and BAC must not be negative.");
        if (bottomUpEstimateToComplete < 0)
            throw new ArgumentOutOfRangeException(nameof(bottomUpEstimateToComplete), "Bottom-up ETC must not be negative.");

        var warnings = new List<string>();
        var (pv, ev, ac, bac) = (input.PlannedValue, input.EarnedValue, input.ActualCost, input.BudgetAtCompletion);

        decimal? cpi = null, spi = null;
        if (ac > 0) cpi = ev / ac; else warnings.Add("CPI_UNAVAILABLE_NO_ACTUAL_COST");
        if (pv > 0) spi = ev / pv; else warnings.Add("SPI_UNAVAILABLE_NO_PLANNED_VALUE");

        decimal? eac = null;
        switch (method)
        {
            case EacMethod.CostPerformance:
                if (cpi is > 0) eac = bac / cpi.Value;
                else if (cpi is not null) warnings.Add("EAC_UNAVAILABLE_ZERO_EARNED_VALUE");
                else warnings.Add("EAC_UNAVAILABLE_CPI_MISSING");
                break;
            case EacMethod.CostAndSchedulePerformance:
                if (cpi is > 0 && spi is > 0) eac = ac + (bac - ev) / (cpi.Value * spi.Value);
                else warnings.Add("EAC_UNAVAILABLE_CPI_OR_SPI_MISSING_OR_ZERO");
                break;
            case EacMethod.BottomUp:
                if (bottomUpEstimateToComplete is null) warnings.Add("EAC_UNAVAILABLE_BOTTOM_UP_ETC_REQUIRED");
                else eac = ac + bottomUpEstimateToComplete.Value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(method));
        }

        if (ev > bac && bac > 0) warnings.Add("EARNED_VALUE_EXCEEDS_BUDGET");
        return new EvmResult(pv, ev, ac, bac, ev - ac, ev - pv, cpi, spi, eac, eac - ac, eac is null ? null : bac - eac, method, warnings);
    }
}
