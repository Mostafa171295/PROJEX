using Projex.Domain.Projects;

namespace Projex.Application.Abstractions;

public interface IProjectRepository
{
    Task AddAsync(Project project, CancellationToken cancellationToken);
    Task<Project?> GetByIdAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken);
    Task<bool> CodeExistsAsync(Guid tenantId, Guid companyId, string code, CancellationToken cancellationToken);
}
