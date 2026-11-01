namespace Projex.Scheduling.Engine.Models;

/// <summary>Scheduling input. Duration uses the same scalar unit as relationship lag (working minutes in PROJEX API).</summary>
public sealed record ScheduleActivity(string Id, string Name, decimal Duration);
