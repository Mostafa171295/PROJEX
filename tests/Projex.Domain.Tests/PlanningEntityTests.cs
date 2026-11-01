using Projex.Domain.Planning;

namespace Projex.Domain.Tests;

public sealed class PlanningEntityTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly Guid Wbs = Guid.NewGuid();

    [Fact]
    public void Activity_rejects_negative_duration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProjectActivity(Tenant, Project, Wbs, "A-001", "Excavation", -1));
    }

    [Fact]
    public void Milestone_must_have_zero_duration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProjectActivity(Tenant, Project, Wbs, "M-001", "Handover", 60, ActivityType.Milestone));
    }

    [Fact]
    public void Activity_progress_is_bounded()
    {
        var activity = new ProjectActivity(Tenant, Project, Wbs, "A-001", "Excavation", 480);
        Assert.Throws<ArgumentOutOfRangeException>(() => activity.SetProgress(101, ActivityStatus.InProgress));
    }

    [Fact]
    public void Relationship_rejects_self_dependency()
    {
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() =>
            new ActivityRelationshipEntity(Tenant, Project, id, id, PlanningRelationshipType.FS));
    }

    [Fact]
    public void Baseline_can_only_be_approved_once()
    {
        var baseline = new ScheduleBaseline(Tenant, Project, "Approved baseline", 1);
        baseline.Approve(Guid.NewGuid());
        Assert.Equal(BaselineStatus.Approved, baseline.Status);
        Assert.Throws<InvalidOperationException>(() => baseline.Approve(Guid.NewGuid()));
    }

    [Fact]
    public void Calendar_exception_requires_valid_time_range()
    {
        Assert.Throws<ArgumentException>(() => new CalendarException(Tenant, Guid.NewGuid(),
            new DateOnly(2026, 12, 25), true, new TimeOnly(17, 0), new TimeOnly(9, 0), "Invalid"));
    }
    [Fact]
    public void Updating_activity_details_increments_row_version()
    {
        var activity = new ProjectActivity(Tenant, Project, Wbs, "A-002", "Concrete", 480);
        var initialVersion = activity.RowVersion;

        activity.UpdateDetails("Concrete pour", 600, Wbs, null);

        Assert.Equal(initialVersion + 1, activity.RowVersion);
        Assert.Equal("Concrete pour", activity.Name);
        Assert.Equal(600, activity.DurationMinutes);
    }

    [Fact]
    public void Milestone_duration_cannot_be_changed_to_nonzero()
    {
        var activity = new ProjectActivity(Tenant, Project, Wbs, "M-002", "Handover", 0, ActivityType.Milestone);

        Assert.Throws<ArgumentOutOfRangeException>(() => activity.UpdateDetails("Handover", 60, Wbs, null));
    }

}
