using Projex.Domain.Common;

namespace Projex.Domain.Organizations;

public sealed class Tenant : Entity
{
    public const string DefaultLocaleCode = "en";

    private Tenant() { }
    public Tenant(string code, string name, string? defaultLocale = null)
    {
        Code = Required(code, 60).ToLowerInvariant();
        Name = Required(name, 200);
        DefaultLocale = string.IsNullOrWhiteSpace(defaultLocale) ? DefaultLocaleCode : Required(defaultLocale, 10);
        Status = TenantStatus.Active;
    }
    /// <summary>Short unique tenant code used at sign-in (ERD platform.tenants.code).</summary>
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string DefaultLocale { get; private set; } = DefaultLocaleCode;
    public TenantStatus Status { get; private set; }

    /// <summary>Suspension is reversible; a closed tenant cannot be suspended or reactivated.</summary>
    public void Suspend()
    {
        if (Status == TenantStatus.Closed) throw new InvalidOperationException("A closed tenant cannot be suspended.");
        Status = TenantStatus.Suspended; Touch();
    }
    public void Activate()
    {
        if (Status == TenantStatus.Closed) throw new InvalidOperationException("A closed tenant cannot be reactivated.");
        Status = TenantStatus.Active; Touch();
    }
    /// <summary>Closing is terminal. Data is retained; only sign-in and writes stop.</summary>
    public void Close() { Status = TenantStatus.Closed; Touch(); }

    private static string Required(string value, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max
            ? value.Trim() : throw new ArgumentException($"A value of 1-{max} characters is required.");
}
public enum TenantStatus { Active, Suspended, Closed }
