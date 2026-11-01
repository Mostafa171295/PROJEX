namespace Projex.Scheduling.Engine.Models;

public sealed record ScheduleCalculationResult(
    decimal ProjectDuration,
    IReadOnlyList<ActivityScheduleResult> Activities,
    IReadOnlyList<string> CriticalPath,
    IReadOnlyList<string> TopologicalOrder);
