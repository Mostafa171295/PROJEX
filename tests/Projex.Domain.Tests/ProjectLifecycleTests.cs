using Projex.Domain.Projects;

namespace Projex.Domain.Tests;

public class ProjectLifecycleTests
{
    private static Project Create() => new(Guid.NewGuid(), Guid.NewGuid(), "P-100", "Test Project", "USD", "UTC");

    [Fact]
    public void Complete_RequiresActiveOrOnHold()
    {
        var project = Create();
        Assert.Throws<InvalidOperationException>(() => project.Complete());
        project.Activate();
        project.Complete();
        Assert.Equal(ProjectStatus.Completed, project.Status);
    }

    [Fact]
    public void Cancel_ClosesDraftProject()
    {
        var project = Create();
        project.Cancel();
        Assert.Equal(ProjectStatus.Cancelled, project.Status);
        Assert.Throws<InvalidOperationException>(() => project.Activate());
    }

    [Fact]
    public void ClosedProject_CannotBeRenamedOrReassigned()
    {
        var project = Create();
        project.Cancel();
        Assert.Throws<InvalidOperationException>(() => project.Rename("Changed"));
        Assert.Throws<InvalidOperationException>(() => project.AssignBranch(Guid.NewGuid()));
    }
}
