using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Projex.Application.Abstractions;
using Projex.Domain.Organizations;
using Projex.Api.Security;
using Projex.Domain.Platform;
using Projex.Domain.Security;
using Projex.Infrastructure.Persistence;
using Projex.Infrastructure.Tenancy;

namespace Projex.Api.Accounts;

/// <summary>Sessions (refresh, logout) and tenant user administration. User administration is CompanyAdmin-only and audited.</summary>
public static class AccountEndpoints
{
    private const string AdminRole = "CompanyAdmin";

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/auth/refresh", async (RefreshRequest request, ProjexDbContext db, UserManager<ProjexUser> users, ITenantContext tenantContext,
            TokenIssuer issuer, IAuditWriter audit, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.TenantKey) || string.IsNullOrWhiteSpace(request.RefreshToken))
                return Results.BadRequest(new { error = "TenantKey and refreshToken are required." });
            var tenantKey = request.TenantKey.Trim().ToLowerInvariant();
            var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.Code == tenantKey && x.Status == TenantStatus.Active, ct);
            if (tenant is null) return Results.Unauthorized();
            ((TenantContext)tenantContext).SetBootstrapTenant(tenant.Id);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id', {tenant.Id.ToString()}, false)", ct);

            var now = DateTimeOffset.UtcNow;
            var hash = TokenIssuer.Hash(request.RefreshToken);
            var session = await db.UserSessions.IgnoreQueryFilters().SingleOrDefaultAsync(s => s.TenantId == tenant.Id && s.RefreshTokenHash == hash, ct);
            if (session is null)
            {
                // A token that was already rotated away is being replayed: assume theft and kill the session.
                var reused = await db.UserSessions.IgnoreQueryFilters().SingleOrDefaultAsync(s => s.TenantId == tenant.Id && s.PreviousRefreshTokenHash == hash, ct);
                if (reused is not null && reused.IsActive(now))
                {
                    reused.Revoke("refresh_token_reuse");
                    audit.Record("auth.refresh.reuse_detected", "UserSession", reused.Id, null, new { reused.UserId }, reused.UserId);
                    await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                }
                return Results.Unauthorized();
            }
            if (!session.IsActive(now)) return Results.Unauthorized();
            var user = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == session.UserId && x.TenantId == tenant.Id && x.IsActive, ct);
            if (user is null)
            {
                session.Revoke("user_inactive");
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                return Results.Unauthorized();
            }
            var newRefresh = TokenIssuer.NewRefreshToken();
            session.Rotate(TokenIssuer.Hash(newRefresh), now.Add(TokenIssuer.SessionLifetime), now);
            audit.Record("auth.session.refreshed", "UserSession", session.Id, null, null, user.Id);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            var roles = await RoleResolver.TenantWideClaimsAsync(db, user.Id, ct);
            return Results.Ok(new TokenResponse(issuer.Issue(user, tenant.Id, tenant.Code, roles, session.Id), "Bearer", TokenIssuer.AccessTokenSeconds, newRefresh));
        }).AllowAnonymous().RequireRateLimiting("auth");

        app.MapPost("/api/v1/auth/logout", async (ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal principal, IAuditWriter audit, CancellationToken ct) =>
        {
            if (!Guid.TryParse(principal.FindFirst(TokenIssuer.SessionClaim)?.Value, out var sessionId)) return Results.Unauthorized();
            var session = await db.UserSessions.SingleOrDefaultAsync(s => s.Id == sessionId && s.UserId == tenant.UserId, ct);
            if (session is not null && session.RevokedAt is null)
            {
                session.Revoke("logout");
                audit.Record("auth.logout", "UserSession", session.Id);
                await db.SaveChangesAsync(ct);
            }
            return Results.NoContent();
        }).RequireAuthorization();

        var g = app.MapGroup("/api/v1/identity/users").RequireAuthorization();

        g.MapGet("", async (int? take, ProjexDbContext db, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            if (!principal.IsInRole(AdminRole)) return Results.Forbid();
            var now = DateTimeOffset.UtcNow;
            var rows = await db.Users.AsNoTracking().OrderBy(x => x.NormalizedEmail).Take(Math.Clamp(take ?? 200, 1, 500))
                .Select(x => new { x.Id, x.Email, x.IsActive, x.LockoutEnd, x.LastLoginAt }).ToListAsync(ct);
            var ids = rows.Select(r => r.Id).ToList();
            var memberships = await db.UserMemberships.AsNoTracking().Where(m => m.IsActive && ids.Contains(m.UserId)).Select(m => new { m.UserId, m.CompanyId }).ToListAsync(ct);
            return Results.Ok(rows.Select(r => new UserDto(r.Id, r.Email ?? string.Empty, r.IsActive, r.LockoutEnd > now, r.LastLoginAt,
                memberships.Where(m => m.UserId == r.Id).Select(m => m.CompanyId).ToList())));
        });

        g.MapPost("", async (UserCreate request, ProjexDbContext db, UserManager<ProjexUser> users, ITenantContext tenant, ClaimsPrincipal principal, IAuditWriter audit, CancellationToken ct) =>
        {
            if (!principal.IsInRole(AdminRole)) return Results.Forbid();
            if (tenant.TenantId is not Guid tid) return Results.Unauthorized();
            var tenantKey = principal.FindFirst("tenant_key")?.Value;
            var email = request.Email?.Trim();
            if (string.IsNullOrEmpty(tenantKey) || string.IsNullOrEmpty(email) || !MailAddress.TryCreate(email, out _) || string.IsNullOrEmpty(request.Password))
                return Results.BadRequest(new { error = "A valid email and a password are required." });
            if (!await db.Companies.AnyAsync(c => c.Id == request.CompanyId, ct)) return Results.BadRequest(new { error = "Company not found." });
            var seatError = await SeatLimitErrorAsync(db, tid, ct);
            if (seatError is not null) return Results.Conflict(new { error = seatError });
            var normalized = email.ToUpperInvariant();
            if (await db.Users.AnyAsync(x => x.NormalizedEmail == normalized, ct)) return Results.Conflict(new { error = "A user with this email already exists." });

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var created = new ProjexUser { Id = Guid.NewGuid(), UserName = $"{tenantKey}:{email}", Email = email, TenantId = tid, IsActive = true };
            try
            {
                var result = await users.CreateAsync(created, request.Password);
                if (!result.Succeeded) return Results.BadRequest(new { errors = result.Errors.Select(e => e.Description) });
                db.UserMemberships.Add(new UserMembership(tid, request.CompanyId, created.Id));
                audit.Record("identity.user.created", "User", created.Id, null, new { email, request.CompanyId });
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException) { return Results.Conflict(new { error = "A user with this email already exists." }); }
            return Results.Created($"/api/v1/identity/users/{created.Id}", new { created.Id, created.Email });
        });

        g.MapPost("/{userId:guid}/disable", (Guid userId, ProjexDbContext db, UserManager<ProjexUser> users, ITenantContext tenant, ClaimsPrincipal principal, IAuditWriter audit, CancellationToken ct)
            => SetActiveAsync(userId, false, db, users, tenant, principal, audit, ct));
        g.MapPost("/{userId:guid}/enable", (Guid userId, ProjexDbContext db, UserManager<ProjexUser> users, ITenantContext tenant, ClaimsPrincipal principal, IAuditWriter audit, CancellationToken ct)
            => SetActiveAsync(userId, true, db, users, tenant, principal, audit, ct));

        g.MapPost("/{userId:guid}/unlock", async (Guid userId, ProjexDbContext db, UserManager<ProjexUser> users, ClaimsPrincipal principal, IAuditWriter audit, CancellationToken ct) =>
        {
            if (!principal.IsInRole(AdminRole)) return Results.Forbid();
            var target = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct);
            if (target is null) return Results.NotFound();
            await users.SetLockoutEndDateAsync(target, null);
            await users.ResetAccessFailedCountAsync(target);
            audit.Record("identity.user.unlocked", "User", target.Id);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return app;
    }

    /// <summary>
    /// Licence check for adding or re-enabling a user (SRS 11): the tenant's live subscription must allow new users and have a free seat.
    /// A tenant with no subscription row (created before platform administration existed) is not limited. Existing users are never blocked.
    /// </summary>
    internal static async Task<string?> SeatLimitErrorAsync(ProjexDbContext db, Guid tenantId, CancellationToken ct)
    {
        var subscription = await db.Subscriptions.AsNoTracking().Where(s => s.TenantId == tenantId).OrderByDescending(s => s.StartsAt).FirstOrDefaultAsync(ct);
        if (subscription is null) return null;
        if (!subscription.AllowsNewUsers(DateTimeOffset.UtcNow)) return "The subscription does not currently allow adding users. Contact your platform administrator.";
        var activeUsers = await db.Users.CountAsync(u => u.IsActive, ct);
        return activeUsers >= subscription.SeatLimit ? $"The seat limit of {subscription.SeatLimit} users has been reached." : null;
    }

    private static async Task<IResult> SetActiveAsync(Guid userId, bool active, ProjexDbContext db, UserManager<ProjexUser> users, ITenantContext tenant,
        ClaimsPrincipal principal, IAuditWriter audit, CancellationToken ct)
    {
        if (!principal.IsInRole(AdminRole)) return Results.Forbid();
        var target = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct);
        if (target is null) return Results.NotFound();
        if (target.IsActive == active) return Results.NoContent();
        if (active && tenant.TenantId is Guid enablingTenant)
        {
            var seatError = await SeatLimitErrorAsync(db, enablingTenant, ct);
            if (seatError is not null) return Results.Conflict(new { error = seatError });
        }
        if (!active)
        {
            if (userId == tenant.UserId) return Results.Conflict(new { error = "You cannot disable your own account." });
            if (await RoleResolver.IsCompanyAdminAsync(db, userId, ct) && !await RoleResolver.AnotherActiveCompanyAdminExistsAsync(db, userId, ct))
                return Results.Conflict(new { error = "The last active company administrator cannot be disabled." });
        }
        target.IsActive = active;
        if (!active)
        {
            // Immediate effect: every session of this user is revoked, and access tokens are checked against their session on each request.
            var sessions = await db.UserSessions.Where(s => s.UserId == userId && s.RevokedAt == null).ToListAsync(ct);
            foreach (var s in sessions) s.Revoke("user_disabled");
        }
        audit.Record(active ? "identity.user.enabled" : "identity.user.disabled", "User", target.Id, new { isActive = !active }, new { isActive = active });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}

public sealed record RefreshRequest(string TenantKey, string RefreshToken);
public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn, string RefreshToken);
public sealed record UserCreate(string Email, string Password, Guid CompanyId);
public sealed record UserDto(Guid Id, string Email, bool IsActive, bool IsLockedOut, DateTimeOffset? LastLoginAt, IReadOnlyList<Guid> CompanyIds);
