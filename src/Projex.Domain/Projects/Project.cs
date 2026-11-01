using Projex.Domain.Common;

namespace Projex.Domain.Projects;

public sealed class Project : Entity
{
    private Project() { } // For persistence

    public Project(Guid tenantId, Guid companyId, string code, string name,
        string currencyCode, string timeZone)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        if (companyId == Guid.Empty) throw new ArgumentException("Company is required.", nameof(companyId));

        TenantId = tenantId;
        CompanyId = companyId;
        Code = RequireText(code, nameof(code), 50);
        Name = RequireText(name, nameof(name), 200);
        CurrencyCode = ValidateCurrency(currencyCode);
        TimeZone = RequireText(timeZone, nameof(timeZone), 80);
        Status = ProjectStatus.Draft;
    }

    public Guid TenantId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? BranchId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string CurrencyCode { get; private set; } = "USD";
    public string TimeZone { get; private set; } = "UTC";
    public ProjectStatus Status { get; private set; }

    public void Activate()
    {
        if (Status is ProjectStatus.Completed or ProjectStatus.Cancelled or ProjectStatus.Archived)
            throw new InvalidOperationException("A closed project cannot be activated.");
        Status = ProjectStatus.Active;
        Touch();
    }

    public void PutOnHold()
    {
        if (Status != ProjectStatus.Active)
            throw new InvalidOperationException("Only an active project can be put on hold.");
        Status = ProjectStatus.OnHold;
        Touch();
    }

    public void Rename(string name)
    {
        if (Status is ProjectStatus.Completed or ProjectStatus.Cancelled or ProjectStatus.Archived)
            throw new InvalidOperationException("A closed project cannot be renamed.");
        Name = RequireText(name, nameof(name), 200);
        Touch();
    }

    public void AssignBranch(Guid branchId)
    {
        if (branchId == Guid.Empty) throw new ArgumentException("Branch is required.", nameof(branchId));
        if (Status is ProjectStatus.Completed or ProjectStatus.Cancelled or ProjectStatus.Archived)
            throw new InvalidOperationException("A closed project cannot be reassigned.");
        BranchId = branchId;
        Touch();
    }

    public void Complete()
    {
        if (Status is not (ProjectStatus.Active or ProjectStatus.OnHold))
            throw new InvalidOperationException("Only active or on-hold projects can be completed.");
        Status = ProjectStatus.Completed;
        Touch();
    }

    public void Cancel()
    {
        if (Status is ProjectStatus.Completed or ProjectStatus.Cancelled or ProjectStatus.Archived)
            throw new InvalidOperationException("A closed project cannot be cancelled.");
        Status = ProjectStatus.Cancelled;
        Touch();
    }

    private static string RequireText(string value, string parameter, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A value is required.", parameter);
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new ArgumentOutOfRangeException(parameter, $"Maximum length is {maxLength}.");
        return normalized;
    }

    private static string ValidateCurrency(string value)
    {
        var currency = RequireText(value, nameof(value), 3).ToUpperInvariant();
        if (currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z'))
            throw new ArgumentException("Currency must be a three-letter ISO-style code.", nameof(value));
        return currency;
    }
}
