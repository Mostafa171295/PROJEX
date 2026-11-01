using Projex.Domain.Common;

namespace Projex.Domain.Security;

/// <summary>A permission from the built-in catalog (seeded; read-only for the application).</summary>
public sealed class Permission : Entity
{
    private Permission() { }
    public string Code { get; private set; } = string.Empty;
    public string Module { get; private set; } = "general";
    public string Description { get; private set; } = string.Empty;
}
