namespace Projex.Domain.Security;

public sealed class RolePermission
{
    private RolePermission() { }
    public RolePermission(Guid roleId, Guid permissionId)
    {
        if (roleId == Guid.Empty || permissionId == Guid.Empty) throw new ArgumentException("Role and permission are required.");
        RoleId = roleId; PermissionId = permissionId;
    }
    public Guid RoleId { get; private set; }
    public Guid PermissionId { get; private set; }
}
