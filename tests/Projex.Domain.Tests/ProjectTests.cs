using Projex.Domain.Projects;

namespace Projex.Domain.Tests;

public class ProjectTests
{
    [Fact]
    public void NewProject_StartsAsDraft()
    {
        var project = new Project(Guid.NewGuid(), Guid.NewGuid(), "PRJ-001",
            "Airport Expansion", "USD", "UTC");

        Assert.Equal(ProjectStatus.Draft, project.Status);
    }

    [Fact]
    public void Activate_ChangesStatusToActive()
    {
        var project = new Project(Guid.NewGuid(), Guid.NewGuid(), "PRJ-002",
            "ERP Implementation", "EUR", "Europe/Berlin");

        project.Activate();

        Assert.Equal(ProjectStatus.Active, project.Status);
    }

    [Fact]
    public void ProjectCode_IsRequired()
    {
        Assert.Throws<ArgumentException>(() =>
            new Project(Guid.NewGuid(), Guid.NewGuid(), " ", "Test", "USD", "UTC"));
    }
}
