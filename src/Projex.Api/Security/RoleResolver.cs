using Microsoft.EntityFrameworkCore;
using Projex.Application.Security;
using Projex.Infrastructure.Persistence;

namespace Projex.Api.Security;

/// <summary>Reads a user's roles from the RBAC tables (user_role_assignments) instead of the legacy ASP.NET Identity role tables.</summary>
public static class RoleResolver
{
    /// <summary>
    /// Role claims for access tokens: the roles the user holds tenant-wide. System roles use their friendly claim name
    /// (so "CompanyAdmin" keeps working in IsInRole checks); custom roles use their code. Scoped roles are evaluated per project.
    /// </summary>
    public static async Task<IReadOnlyList<string>> TenantWideClaimsAsync(ProjexDbContext db, Guid userId, CancellationToken ct)
    {
        var codes = await (from a in db.RoleAssignments.AsNoTracking()
                           where a.UserId == userId && a.CompanyId == null && a.BranchId == null && a.ProjectId == null
                           join r in db.TenantRoles.AsNoTracking() on a.RoleId equals r.Id
                           select r.Code).Distinct().ToListAsync(ct);
        return codes.Select(RoleCatalog.ClaimNameFor).ToList();
    }

    /// <summary>The tenant-wide CompanyAdmin role of this tenant's system catalog.</summary>
    public static Task<Guid> CompanyAdminRoleIdAsync(ProjexDbContext db, CancellationToken ct) =>
        db.TenantRoles.AsNoTracking().Where(r => r.TenantId == null && r.Code == RoleCatalog.CompanyAdminCode).Select(r => r.Id).SingleAsync(ct);

    /// <summary>Active users (other than <paramref name="exceptUserId"/>) who hold CompanyAdmin tenant-wide.</summary>
    public static Task<bool> AnotherActiveCompanyAdminExistsAsync(ProjexDbContext db, Guid exceptUserId, CancellationToken ct) =>
        (from a in db.RoleAssignments.AsNoTracking()
         join r in db.TenantRoles.AsNoTracking() on a.RoleId equals r.Id
         join u in db.Users.AsNoTracking() on a.UserId equals u.Id
         where r.TenantId == null && r.Code == RoleCatalog.CompanyAdminCode
               && a.CompanyId == null && a.BranchId == null && a.ProjectId == null
               && u.IsActive && u.Id != exceptUserId
         select a.Id).AnyAsync(ct);

    public static Task<bool> IsCompanyAdminAsync(ProjexDbContext db, Guid userId, CancellationToken ct) =>
        (from a in db.RoleAssignments.AsNoTracking()
         join r in db.TenantRoles.AsNoTracking() on a.RoleId equals r.Id
         where a.UserId == userId && r.TenantId == null && r.Code == RoleCatalog.CompanyAdminCode
               && a.CompanyId == null && a.BranchId == null && a.ProjectId == null
         select a.Id).AnyAsync(ct);
}
