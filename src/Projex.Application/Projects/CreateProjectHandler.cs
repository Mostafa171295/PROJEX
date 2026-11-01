using Projex.Application.Abstractions;
using Projex.Domain.Projects;

namespace Projex.Application.Projects;

public sealed class CreateProjectHandler(IProjectRepository projects)
{
    public async Task<Guid> Handle(CreateProjectCommand command, CancellationToken cancellationToken)
    {
        if (await projects.CodeExistsAsync(command.TenantId, command.CompanyId, command.Code, cancellationToken))
            throw new InvalidOperationException("A project with this code already exists in the company.");

        var project = new Project(
            command.TenantId,
            command.CompanyId,
            command.Code,
            command.Name,
            command.CurrencyCode,
            command.TimeZone);

        await projects.AddAsync(project, cancellationToken);
        return project.Id;
    }
}
