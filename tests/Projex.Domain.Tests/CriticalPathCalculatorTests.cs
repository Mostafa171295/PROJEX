using Projex.Scheduling.Engine.Calculation;
using Projex.Scheduling.Engine.Models;

namespace Projex.Domain.Tests;

public class CriticalPathCalculatorTests
{
    private readonly CriticalPathCalculator _calculator = new();

    [Fact]
    public void CalculatesForwardBackwardPassAndCriticalPath()
    {
        var activities = new[]
        {
            new ScheduleActivity("A", "Design", 3),
            new ScheduleActivity("B", "Procurement", 4),
            new ScheduleActivity("C", "Execution", 5),
            new ScheduleActivity("D", "Commissioning", 2)
        };
        var links = new[]
        {
            new ActivityRelationship("A", "B"),
            new ActivityRelationship("A", "C"),
            new ActivityRelationship("B", "D"),
            new ActivityRelationship("C", "D")
        };

        var result = _calculator.Calculate(activities, links);

        Assert.Equal(10m, result.ProjectDuration);
        Assert.Equal(new[] { "A", "C", "D" }, result.CriticalPath);
        Assert.Equal(1m, result.Activities.Single(x => x.ActivityId == "B").TotalFloat);
        Assert.Equal(0m, result.Activities.Single(x => x.ActivityId == "C").TotalFloat);
    }

    [Theory]
    [InlineData(RelationshipType.FinishToStart, 3, 6)]
    [InlineData(RelationshipType.StartToStart, 0, 3)]
    [InlineData(RelationshipType.FinishToFinish, 0, 3)]
    [InlineData(RelationshipType.StartToFinish, 0, 3)]
    public void SupportsAllFourRelationshipTypes(RelationshipType type, decimal expectedStart, decimal expectedFinish)
    {
        var activities = new[] { new ScheduleActivity("A", "A", 3), new ScheduleActivity("B", "B", 3) };
        var result = _calculator.Calculate(activities, new[] { new ActivityRelationship("A", "B", type) });
        var b = result.Activities.Single(x => x.ActivityId == "B");
        Assert.Equal(expectedStart, b.EarlyStart);
        Assert.Equal(expectedFinish, b.EarlyFinish);
    }

    [Fact]
    public void RejectsDependencyCycles()
    {
        var activities = new[] { new ScheduleActivity("A", "A", 1), new ScheduleActivity("B", "B", 1) };
        var links = new[] { new ActivityRelationship("A", "B"), new ActivityRelationship("B", "A") };
        Assert.Throws<InvalidOperationException>(() => _calculator.Calculate(activities, links));
    }

    [Fact]
    public void RejectsUnknownActivityReferences()
    {
        var activities = new[] { new ScheduleActivity("A", "A", 1) };
        Assert.Throws<ArgumentException>(() => _calculator.Calculate(activities, new[] { new ActivityRelationship("A", "X") }));
    }
}
