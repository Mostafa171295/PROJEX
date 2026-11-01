using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Projex.Application.Abstractions;
using Projex.Application.Security;
using Projex.Domain.Security;
using Projex.Infrastructure.Persistence;

namespace Projex.Api.Security;

/// <summary>
/// Roles, permissions and scoped role assignments (SRS 3.2, FR-IAM-003). CompanyAdmin only; every change is audited.
/// System roles are built in and immutable; custom roles are tenant-owned sets of project-level permissions.
/// </summary>
public static class RbacEndpoints
{
    private const string AdminRole = "CompanyAdmin";

    /// <summary>Permissions that the server enforces on projects today; only these can be put into a custom role.</summary>
    private static readonly HashSet<string> AssignableToCustomRoles =
        Enum.GetValues<ProjectPermission>().Select(ProjectAccess.RequiredCode).ToHashSet(StringComparer.Ordinal);

    public static IEndpointRouteBuilder MapRbacEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/identity").RequireAuthorization();

        g.MapGet("/permissions", async (ProjexDbContext db, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            if (!principal.IsInRole(AdminRole)) return Results.Forbid();
            var rows = await db.Permissions.AsNoTracking().OrderBy(p => p.Module).ThenBy(p => p.Code)
                .Select(p => new PermissionDto(p.Code, p.Module, p.Description, AssignableToCustomRoles.Contains(p.Code))).ToListAsync(ct);
            return Results.Ok(rows);
        });

        g.MapGet("/roles", async (ProjexDbContext db, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            if (!principal.IsInRole(AdminRole)) return Results.Forbid();
            var roles = await db.TenantRoles.AsNoTracking().OrderByDescending(r => r.IsSystem).ThenBy(r => r.Code).ToListAsync(ct);
            var granted = await (from rp in db.RolePermissions.AsNoTracking()
                                 join p in db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
                                 select new { rp.RoleId, p.Code }).ToListAsync(ct);
            return Results.Ok(roles.Select(r => new RoleDto(r.Id, r.Code, r.Name, r.IsSystem,
                granted.Where(x => x.RoleId == r.Id).Select(x => x.Code).OrderBy(c => c).ToList())));
        });

        g.MapPost("/roles", async (RoleWrite request, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal principal, IAuditWriter audit, CancellationToken ct) =>
        {
            if (!principal.IsInRole(AdminRole)) return Results.Forbid();
            if (tenant.TenantId is not Guid tid) return Results.Unauthorized();
            Role role;
            try { role = new Role(tid, request.Code, request.Name); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            if (RoleCatalog.IsSystemRole(role.Code)) return Results.Conflict(new { error = "This code is reserved for a built-in role." });
            if (await db.TenantRoles.AnyAsync(r => r.TenantId == tid && r.Code == role.Code, ct)) return Results.Conflict(new { error = "A role with this code already exists." });
            var (permissions, error) = await ResolvePermissionsAsync(db, request.Permissions, ct);
            if (error is not null) return Results.BadRequest(new { error });

            db.TenantRoles.Add(role);
            foreach (var p in permissions) db.RolePermissions.Add(new RolePermission(role.Id, p.Id));
            audit.Record("identity.role.created", "Role", role.Id, null, new { role.Code, role.Name, permissions = permissions.Select(p => p.Code) });
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/identity/roles/{role.Id}", new RoleDto(role.Id, role.Code, role.Name, false, permissions.Select(p => p.Code).OrderBy(c => c).ToList()));
        });

        g.MapPut("/roles/{roleId:guid}/permissions", async (Guid roleId, RolePermissionsWrite request, ProjexDbContext db, ClaimsPrincipal principal, IAuditWriter audit, CancellationToken ct) =>
        {
            if (!principal.IsInRole(AdminRole)) return Results.Forbid();
            var role = await db.TenantRoles.SingleOrDefaultAsync(r => r.Id == roleId, ct);
            if (role is null) return Results.NotFound();
            if (role.IsSystem) return Results.Conflict(new { error = "Built-in roles cannot be changed." });
            var (permissions, error) = await ResolvePermissionsAsync(db, request.Permissions, ct);
            if (error is not null) return Results.BadRequest(new { error });

            var current = await db.RolePermissions.Where(x => x.RoleId == roleId).ToListAsync(ct);
            var wanted = permissions.Select(p => p.Id).ToHashSet();
            db.RolePermissions.RemoveRange(current.Where(x => !wanted.Contains(x.PermissionId)));
            var have = current.Select(x => x.PermissionId).ToHashSet();
            foreach (var p in permissions.Where(p => !have.Contains(p.Id))) db.RolePermissions.Add(new RolePermission(roleId, p.Id));
            audit.Record("identity.role.permissions_set", "Role", roleId, null, new { role.Code, permissions = permissions.Select(p => p.Code) });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapDelete("/roles/{roleId:guid}", async (Guid roleId, ProjexDbContext db, ClaimsPrincipal principal, IAuditWriter audit, CancellationToken ct) =>
        {
            if (!principal.IsInRole(AdminRole)) return Results.Forbid();
            var role = await db.TenantRoles.SingleOrDefaultAsync(r => r.Id == roleId, ct);
            if (role is null) return Results.NotFound();
            if (role.IsSystem) return Results.Conflict(new { error = "Built-in roles cannot be deleted." });
            if (await db.RoleAssignments.AnyAsync(a => a.RoleId == roleId, ct)) return Results.Conflict(new { error = "The role is still assigned to users; revoke those assignments first." });
            db.TenantRoles.Remove(role);
            audit.Record("identity.role.deleted", "Role", roleId, new { role.Code }, null);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapGet("/users/{userId:guid}/roles", async (Guid userId, ProjexDbContext db, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            if (!principal.IsInRole(AdminRole)) return Results.Forbid();
            if (!await db.Users.AnyAsync(u => u.Id == userId, ct)) return Results.NotFound();
            var rows = await (from a in db.RoleAssignments.AsNoTracking()
                              where a.UserId == userId
                              join r in db.TenantRoles.AsNoTracking() on a.RoleId equals r.Id
                              orderby r.Code
                              select new AssignmentDto(a.Id, r.Id, r.Code, r.Name, a.CompanyId, a.BranchId, a.ProjectId, a.CreatedAt)).ToListAsync(ct);
            return Results.Ok(rows);
        });

        g.MapPost("/users/{userId:guid}/roles", async (Guid userId, AssignmentWrite request, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal principal, IAuditWriter audit, CancellationToken ct) =>
        {
            if (!principal.IsInRole(AdminRole)) return Results.Forbid();
            if (tenant.TenantId is not Guid tid) return Results.Unauthorized();
            if (!await db.Users.AnyAsync(u => u.Id == userId, ct)) return Results.NotFound();
            var role = await db.TenantRoles.AsNoTracking().SingleOrDefaultAsync(r => r.Id == request.RoleId, ct);
            if (role is null) return Results.BadRequest(new { error = "Role not found." });

            if (request.ProjectId is not null && (request.CompanyId is not null || request.BranchId is not null))
                return Results.BadRequest(new { error = "Use either a project scope or a company/branch scope, not both." });
            if (request.BranchId is not null && request.CompanyId is null)
                return Results.BadRequest(new { error = "A branch scope needs its company." });
            if (role.Code == RoleCatalog.CompanyAdminCode && (request.CompanyId is not null || request.BranchId is not null || request.ProjectId is not null))
                return Results.BadRequest(new { error = "CompanyAdmin applies to the whole tenant and cannot be scoped." });
            if (request.CompanyId is Guid cid && !await db.Companies.AnyAsync(c => c.Id == cid, ct)) return Results.BadRequest(new { error = "Company not found." });
            if (request.BranchId is Guid bid && !await db.Branches.AnyAsync(b => b.Id == bid && b.CompanyId == request.CompanyId, ct)) return Results.BadRequest(new { error = "Branch not found in that company." });
            if (request.ProjectId is Guid pid && !await db.Projects.AnyAsync(p => p.Id == pid, ct)) return Results.BadRequest(new { error = "Project not found." });
            if (await db.RoleAssignments.AnyAsync(a => a.UserId == userId && a.RoleId == role.Id && a.CompanyId == request.CompanyId
                    && a.BranchId == request.BranchId && a.ProjectId == request.ProjectId, ct))
                return Results.Conflict(new { error = "The user already holds this role in this scope." });

            var assignment = new UserRoleAssignment(tid, userId, role.Id, request.CompanyId, request.BranchId, request.ProjectId, tenant.UserId);
            db.RoleAssignments.Add(assignment);
            audit.Record("identity.role.assigned", "User", userId, null, new { role = role.Code, request.CompanyId, request.BranchId, request.ProjectId });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Results.Conflict(new { error = "The assignment conflicts with existing data." }); }
            return Results.Created($"/api/v1/identity/users/{userId}/roles/{assignment.Id}",
                new AssignmentDto(assignment.Id, role.Id, role.Code, role.Name, assignment.CompanyId, assignment.BranchId, assignment.ProjectId, assignment.CreatedAt));
        });

        g.MapDelete("/users/{userId:guid}/roles/{assignmentId:guid}", async (Guid userId, Guid assignmentId, ProjexDbContext db, ClaimsPrincipal principal, IAuditWriter audit, CancellationToken ct) =>
        {
            if (!principal.IsInRole(AdminRole)) return Results.Forbid();
            var assignment = await db.RoleAssignments.SingleOrDefaultAsync(a => a.Id == assignmentId && a.UserId == userId, ct);
            if (assignment is null) return Results.NotFound();
            var role = await db.TenantRoles.AsNoTracking().SingleAsync(r => r.Id == assignment.RoleId, ct);
            if (role.Code == RoleCatalog.CompanyAdminCode && assignment.IsTenantWide
                && !await RoleResolver.AnotherActiveCompanyAdminExistsAsync(db, userId, ct))
                return Results.Conflict(new { error = "The last company administrator cannot lose the role." });

            db.RoleAssignments.Remove(assignment);
            if (assignment.IsTenantWide)
            {
                // Tenant-wide roles live in the access token. Revoking the sessions makes the change effective immediately.
                var sessions = await db.UserSessions.Where(s => s.UserId == userId && s.RevokedAt == null).ToListAsync(ct);
                foreach (var s in sessions) s.Revoke("roles_changed");
            }
            audit.Record("identity.role.revoked", "User", userId, new { role = role.Code, assignment.CompanyId, assignment.BranchId, assignment.ProjectId }, null);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return app;
    }

    private static async Task<(List<Permission> Permissions, string? Error)> ResolvePermissionsAsync(ProjexDbContext db, IReadOnlyList<string>? requested, CancellationToken ct)
    {
        var codes = (requested ?? []).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim().ToLowerInvariant()).Distinct().ToList();
        if (codes.Count == 0) return ([], "At least one permission is required.");
        var found = await db.Permissions.AsNoTracking().Where(p => codes.Contains(p.Code)).ToListAsync(ct);
        var unknown = codes.Except(found.Select(p => p.Code)).ToList();
        if (unknown.Count > 0) return ([], "Unknown permissions: " + string.Join(", ", unknown));
        var notAllowed = found.Where(p => !AssignableToCustomRoles.Contains(p.Code)).Select(p => p.Code).ToList();
        if (notAllowed.Count > 0) return ([], "These permissions cannot be given to a custom role: " + string.Join(", ", notAllowed));
        return (found, null);
    }
}

public sealed record PermissionDto(string Code, string Module, string Description, bool AssignableToCustomRoles);
public sealed record RoleDto(Guid Id, string Code, string Name, bool IsSystem, IReadOnlyList<string> Permissions);
public sealed record RoleWrite(string Code, string Name, IReadOnlyList<string>? Permissions);
public sealed record RolePermissionsWrite(IReadOnlyList<string>? Permissions);
public sealed record AssignmentDto(Guid Id, Guid RoleId, string RoleCode, string RoleName, Guid? CompanyId, Guid? BranchId, Guid? ProjectId, DateTimeOffset CreatedAt);
public sealed record AssignmentWrite(Guid RoleId, Guid? CompanyId, Guid? BranchId, Guid? ProjectId);
