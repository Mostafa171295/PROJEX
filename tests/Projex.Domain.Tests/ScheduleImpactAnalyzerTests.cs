using Projex.Scheduling.Engine.Analysis;
using Projex.Scheduling.Engine.Models;
using Xunit;

namespace Projex.Domain.Tests;

public sealed class ScheduleImpactAnalyzerTests
{
    private readonly ScheduleImpactAnalyzer _analyzer = new();

    [Fact]
    public void Finds_All_Downstream_Activities_In_Breadth_First_Order()
    {
        var links = new[]
        {
            new ActivityRelationship("A", "B"),
            new ActivityRelationship("A", "C", RelationshipType.StartToStart, 30),
            new ActivityRelationship("B", "D"),
            new ActivityRelationship("C", "D")
        };

        var result = _analyzer.Analyze("A", links);

        Assert.Equal(new[] { "B", "C", "D" }, result.Select(x => x.ActivityId));
        Assert.Equal(new[] { 1, 1, 2 }, result.Select(x => x.DependencyDepth));
    }

    [Fact]
    public void Stops_At_Configured_Depth()
    {
        var links = new[] { new ActivityRelationship("A", "B"), new ActivityRelationship("B", "C") };
        var result = _analyzer.Analyze("A", links, maxDepth: 1);
        Assert.Single(result);
        Assert.Equal("B", result[0].ActivityId);
    }

    [Fact]
    public void Handles_Cycles_Without_Repeating_Activities()
    {
        var links = new[] { new ActivityRelationship("A", "B"), new ActivityRelationship("B", "C"), new ActivityRelationship("C", "A") };
        var result = _analyzer.Analyze("A", links);
        Assert.Equal(new[] { "B", "C" }, result.Select(x => x.ActivityId));
    }

    [Fact]
    public void Rejects_Invalid_Source_And_Depth()
    {
        Assert.Throws<ArgumentException>(() => _analyzer.Analyze(" ", Array.Empty<ActivityRelationship>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => _analyzer.Analyze("A", Array.Empty<ActivityRelationship>(), 0));
    }
}
