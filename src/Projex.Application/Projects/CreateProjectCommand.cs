namespace Projex.Application.Projects;

public sealed record CreateProjectCommand(
    Guid TenantId,
    Guid CompanyId,
    string Code,
    string Name,
    string CurrencyCode,
    string TimeZone);
