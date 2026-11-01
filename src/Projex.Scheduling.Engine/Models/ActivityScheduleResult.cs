namespace Projex.Scheduling.Engine.Models;

public sealed record ActivityScheduleResult(
    string ActivityId, string Name, decimal Duration,
    decimal EarlyStart, decimal EarlyFinish,
    decimal LateStart, decimal LateFinish,
    decimal TotalFloat, bool IsCritical);
