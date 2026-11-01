using Projex.Domain.Common;

namespace Projex.Domain.Security;

/// <summary>A role is either a built-in system role (TenantId null, immutable, seeded) or a custom role owned by one tenant.</summary>
public sealed class Role : Entity
{
    private Role() { }
    public Role(Guid tenantId, string code, string name)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        TenantId = tenantId; Code = NormalizeCode(code); Name = Required(name, 120); IsSystem = false;
    }
    public Guid? TenantId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsSystem { get; private set; }
    public void Rename(string name) { if (IsSystem) throw new InvalidOperationException("System roles cannot be changed."); Name = Required(name, 120); Touch(); }

    /// <summary>Codes are upper-case identifiers such as SENIOR_PLANNER (2-60 chars: letters, digits, underscore).</summary>
    public static string NormalizeCode(string code)
    {
        var value = (code ?? string.Empty).Trim().ToUpperInvariant();
        return value.Length is >= 2 and <= 60 && char.IsLetter(value[0]) && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            ? value : throw new ArgumentException("A role code is 2-60 characters: letters, digits and underscores, starting with a letter.", nameof(code));
    }
    private static string Required(string value, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max
            ? value.Trim() : throw new ArgumentException($"A value of 1-{max} characters is required.");
}
