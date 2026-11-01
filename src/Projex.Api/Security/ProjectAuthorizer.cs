using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Projex.Application.Abstractions;
using Projex.Application.Security;
using Projex.Infrastructure.Persistence;

namespace Projex.Api.Security;

/// <summary>
/// Server-side project authorization shared by every endpoint module (SRS 3.2). The caller must be an active member of the
/// project's company, and either be CompanyAdmin, hold a system project role through a project membership that grants the permission,
/// or hold a role (system or custom) through an assignment whose scope covers this project and whose permissions include it.
/// A project from another tenant is invisible (query filter + RLS), so it is simply "not authorized".
/// </summary>
public static class ProjectAuthorizer
{
    public static async Task<bool> HasAsync(ProjectPermission permission, Guid projectId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal principal, CancellationToken ct)
    {
        if (tenant.UserId is not Guid uid) return false;
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return false;
        if (!await db.UserMemberships.AnyAsync(m => m.CompanyId == project.CompanyId && m.UserId == uid && m.IsActive, ct)) return false;
        if (principal.IsInRole("CompanyAdmin")) return true;

        var memberRoles = await db.ProjectMemberships.Where(m => m.ProjectId == projectId && m.UserId == uid && m.IsActive).Select(m => m.RoleCode).ToListAsync(ct);
        if (ProjectAccess.IsAllowed(permission, false, memberRoles)) return true;

        // Assignments whose scope covers this project: tenant-wide, its company, its branch, or the project itself.
        var required = ProjectAccess.RequiredCode(permission);
        var branchId = project.BranchId;
        return await (from a in db.RoleAssignments.AsNoTracking()
                      where a.UserId == uid
                            && (a.ProjectId == null || a.ProjectId == projectId)
                            && (a.CompanyId == null || a.CompanyId == project.CompanyId)
                            && (a.BranchId == null || a.BranchId == branchId)
                      join rp in db.RolePermissions on a.RoleId equals rp.RoleId
                      join p in db.Permissions on rp.PermissionId equals p.Id
                      where p.Code == required
                      select p.Id).AnyAsync(ct);
    }
}
