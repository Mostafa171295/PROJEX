using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Projex.Api.Accounts;
using Projex.Application.Abstractions;
using Projex.Domain.Organizations;
using Projex.Domain.Platform;
using Projex.Infrastructure.Persistence;
using Projex.Infrastructure.Tenancy;

namespace Projex.Api.Platform;

/// <summary>
/// Platform administration (SRS FR-ORG-001, section 3.1 "Platform Super Admin"): tenants, licences and platform administrators.
/// Platform administrators sign in separately and carry no tenant_id, so they can never read a customer's business data.
/// </summary>
public static class PlatformEndpoints
{
    public const string Policy = "PlatformAdmin";
    private static readonly PlatformAdmin Dummy = new("timing@projex.invalid", "timing", "x");

    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/platform/bootstrap", Bootstrap).AllowAnonymous().RequireRateLimiting("auth");
        app.MapPost("/api/v1/platform/auth/login", Login).AllowAnonymous().RequireRateLimiting("auth");

        var g = app.MapGroup("/api/v1/platform").RequireAuthorization(Policy);

        g.MapGet("/admins", async (ProjexDbContext db, CancellationToken ct) =>
            Results.Ok(await db.PlatformAdmins.AsNoTracking().OrderBy(a => a.NormalizedEmail)
                .Select(a => new PlatformAdminDto(a.Id, a.Email, a.DisplayName, a.IsActive, a.LastLoginAt)).ToListAsync(ct)));

        g.MapPost("/admins", async (PlatformAdminCreate request, ProjexDbContext db, IPasswordHasher<PlatformAdmin> hasher, IOptions<IdentityOptions> options,
            ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            var email = request.Email?.Trim();
            if (string.IsNullOrEmpty(email) || !MailAddress.TryCreate(email, out _)) return Results.BadRequest(new { error = "A valid email is required." });
            var passwordError = ValidatePassword(request.Password, options.Value.Password);
            if (passwordError is not null) return Results.BadRequest(new { error = passwordError });
            var normalized = email.ToUpperInvariant();
            if (await db.PlatformAdmins.AnyAsync(a => a.NormalizedEmail == normalized, ct)) return Results.Conflict(new { error = "A platform administrator with this email already exists." });
            var admin = new PlatformAdmin(email, string.IsNullOrWhiteSpace(request.DisplayName) ? email : request.DisplayName, "pending");
            admin.SetPasswordHash(hasher.HashPassword(admin, request.Password));
            db.PlatformAdmins.Add(admin);
            Audit(db, http, CurrentAdminId(principal), "platform.admin.created", null, new { admin.Id, admin.Email });
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/platform/admins/{admin.Id}", new PlatformAdminDto(admin.Id, admin.Email, admin.DisplayName, admin.IsActive, admin.LastLoginAt));
        });

        g.MapPost("/admins/{adminId:guid}/disable", async (Guid adminId, ProjexDbContext db, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            var me = CurrentAdminId(principal);
            if (adminId == me) return Results.Conflict(new { error = "You cannot disable your own account." });
            var target = await db.PlatformAdmins.SingleOrDefaultAsync(a => a.Id == adminId, ct);
            if (target is null) return Results.NotFound();
            if (!target.IsActive) return Results.NoContent();
            if (!await db.PlatformAdmins.AnyAsync(a => a.IsActive && a.Id != adminId, ct)) return Results.Conflict(new { error = "The last active platform administrator cannot be disabled." });
            target.Disable();
            Audit(db, http, me, "platform.admin.disabled", null, new { target.Id });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapPost("/admins/{adminId:guid}/enable", async (Guid adminId, ProjexDbContext db, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            var target = await db.PlatformAdmins.SingleOrDefaultAsync(a => a.Id == adminId, ct);
            if (target is null) return Results.NotFound();
            if (target.IsActive) return Results.NoContent();
            target.Enable();
            Audit(db, http, CurrentAdminId(principal), "platform.admin.enabled", null, new { target.Id });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        var t = g.MapGroup("/tenants");

        t.MapGet("", async (ProjexDbContext db, CancellationToken ct) =>
        {
            var tenants = await db.Tenants.AsNoTracking().OrderBy(x => x.Code).ToListAsync(ct);
            var subs = await db.Subscriptions.AsNoTracking().OrderByDescending(s => s.StartsAt).ToListAsync(ct);
            return Results.Ok(tenants.Select(x => ToDto(x, subs.FirstOrDefault(s => s.TenantId == x.Id))));
        });

        t.MapGet("/{tenantId:guid}", async (Guid tenantId, ProjexDbContext db, CancellationToken ct) =>
        {
            var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == tenantId, ct);
            if (tenant is null) return Results.NotFound();
            var sub = await LatestSubscription(db, tenantId, ct);
            return Results.Ok(ToDto(tenant, sub));
        });

        t.MapPost("", async (ProvisionTenantRequest request, TenantProvisioner provisioner, ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            try
            {
                var result = await provisioner.ProvisionAsync(request, CurrentAdminId(principal), http.Connection.RemoteIpAddress?.ToString(), ct);
                return Results.Created($"/api/v1/platform/tenants/{result.TenantId}", result);
            }
            catch (ProvisioningException ex)
            {
                var body = new { error = ex.Message, errors = ex.Errors };
                return ex.Status switch { 409 => Results.Conflict(body), 500 => Results.Problem(ex.Message), _ => Results.BadRequest(body) };
            }
        });

        t.MapPost("/{tenantId:guid}/suspend", (Guid tenantId, ProjexDbContext db, ClaimsPrincipal principal, HttpContext http, CancellationToken ct)
            => ChangeStatus(tenantId, "suspended", x => x.Suspend(), db, principal, http, ct));
        t.MapPost("/{tenantId:guid}/activate", (Guid tenantId, ProjexDbContext db, ClaimsPrincipal principal, HttpContext http, CancellationToken ct)
            => ChangeStatus(tenantId, "activated", x => x.Activate(), db, principal, http, ct));
        t.MapPost("/{tenantId:guid}/close", (Guid tenantId, ProjexDbContext db, ClaimsPrincipal principal, HttpContext http, CancellationToken ct)
            => ChangeStatus(tenantId, "closed", x => x.Close(), db, principal, http, ct));

        t.MapGet("/{tenantId:guid}/subscription", async (Guid tenantId, ProjexDbContext db, CancellationToken ct) =>
        {
            if (!await db.Tenants.AnyAsync(x => x.Id == tenantId, ct)) return Results.NotFound();
            var sub = await LatestSubscription(db, tenantId, ct);
            return sub is null ? Results.NotFound() : Results.Ok(ToDto(sub));
        });

        t.MapPut("/{tenantId:guid}/subscription", async (Guid tenantId, SubscriptionWrite request, ProjexDbContext db, ITenantContext tenantContext,
            ClaimsPrincipal principal, HttpContext http, CancellationToken ct) =>
        {
            var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == tenantId, ct);
            if (tenant is null) return Results.NotFound();
            // Subscription carries a tenant id, so the write guard needs this tenant's context (platform admins have none of their own).
            ((TenantContext)tenantContext).SetBootstrapTenant(tenantId);

            var activeUsers = await db.Users.CountAsync(u => u.IsActive, ct);
            if (request.SeatLimit < activeUsers)
                return Results.Conflict(new { error = $"The tenant has {activeUsers} active users; the seat limit cannot be lower." });

            var startsAt = request.StartsAt ?? DateTimeOffset.UtcNow;
            var entitlements = request.ModuleEntitlements is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } m ? m.GetRawText() : null;
            var current = await LatestSubscription(db, tenantId, ct);
            var isLive = request.Status is SubscriptionStatus.Trial or SubscriptionStatus.Active or SubscriptionStatus.PastDue;
            try
            {
                Subscription target;
                if (current is null || (current.Status == SubscriptionStatus.Cancelled && isLive))
                {
                    target = new Subscription(tenantId, request.PlanCode, request.Status, startsAt, request.EndsAt, request.SeatLimit, entitlements);
                    db.Subscriptions.Add(target);
                }
                else
                {
                    target = current;
                    target.Change(request.PlanCode, request.Status, request.StartsAt ?? current.StartsAt, request.EndsAt, request.SeatLimit, entitlements ?? current.ModuleEntitlements);
                }
                Audit(db, http, CurrentAdminId(principal), "platform.subscription.changed", tenantId,
                    new { request.PlanCode, request.Status, request.SeatLimit, request.EndsAt });
                await db.SaveChangesAsync(ct);
                return Results.Ok(ToDto(target));
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (DbUpdateException) { return Results.Conflict(new { error = "The tenant already has a live subscription; update it instead." }); }
        });

        return app;
    }

    private static async Task<IResult> Bootstrap([FromHeader(Name = "X-Bootstrap-Key")] string? suppliedKey, PlatformBootstrapRequest request, ProjexDbContext db,
        IPasswordHasher<PlatformAdmin> hasher, IOptions<IdentityOptions> options, IConfiguration configuration, HttpContext http, CancellationToken ct)
    {
        var expectedKey = configuration["Bootstrap:Key"];
        if (string.IsNullOrWhiteSpace(expectedKey) || string.IsNullOrWhiteSpace(suppliedKey) ||
            !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expectedKey), Encoding.UTF8.GetBytes(suppliedKey)))
            return Results.NotFound();
        var email = request.Email?.Trim();
        if (string.IsNullOrEmpty(email) || !MailAddress.TryCreate(email, out _)) return Results.BadRequest(new { error = "A valid email is required." });
        var passwordError = ValidatePassword(request.Password, options.Value.Password);
        if (passwordError is not null) return Results.BadRequest(new { error = passwordError });

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(727001)", ct);   // two concurrent bootstraps cannot both pass the check below
        if (await db.PlatformAdmins.AnyAsync(ct)) return Results.Conflict(new { error = "Platform bootstrap has already been completed." });
        var admin = new PlatformAdmin(email, string.IsNullOrWhiteSpace(request.DisplayName) ? email : request.DisplayName, "pending");
        admin.SetPasswordHash(hasher.HashPassword(admin, request.Password));
        db.PlatformAdmins.Add(admin);
        Audit(db, http, admin.Id, "platform.bootstrap", null, new { admin.Id });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Created("/api/v1/platform/auth/login", new { adminId = admin.Id });
    }

    private static async Task<IResult> Login(PlatformLoginRequest request, ProjexDbContext db, IPasswordHasher<PlatformAdmin> hasher,
        IOptions<IdentityOptions> options, TokenIssuer issuer, HttpContext http, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password)) return Results.BadRequest(new { error = "Email and password are required." });
        var normalized = request.Email.Trim().ToUpperInvariant();
        var admin = await db.PlatformAdmins.SingleOrDefaultAsync(a => a.NormalizedEmail == normalized, ct);
        var now = DateTimeOffset.UtcNow;
        var lockout = options.Value.Lockout;

        if (admin is null || !admin.IsActive)
        {
            hasher.HashPassword(Dummy, request.Password);   // keep the response time of unknown and known accounts alike
            Audit(db, http, null, "platform.login.failed", null, new { reason = "unknown_or_inactive" });
            await db.SaveChangesAsync(ct);
            return Results.Unauthorized();
        }
        if (admin.IsLockedOut(now))
        {
            Audit(db, http, admin.Id, "platform.login.locked_out", null, null);
            await db.SaveChangesAsync(ct);
            return Results.Unauthorized();
        }
        var verified = hasher.VerifyHashedPassword(admin, admin.PasswordHash, request.Password);
        if (verified == PasswordVerificationResult.Failed)
        {
            admin.RegisterFailure(now, lockout.MaxFailedAccessAttempts, lockout.DefaultLockoutTimeSpan);
            Audit(db, http, admin.Id, "platform.login.failed", null, new { reason = "bad_password" });
            await db.SaveChangesAsync(ct);
            return Results.Unauthorized();
        }
        if (verified == PasswordVerificationResult.SuccessRehashNeeded) admin.SetPasswordHash(hasher.HashPassword(admin, request.Password));
        admin.RegisterSuccess(now);
        Audit(db, http, admin.Id, "platform.login.succeeded", null, null);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new PlatformLoginResponse(issuer.IssuePlatform(admin), "Bearer", TokenIssuer.AccessTokenSeconds, admin.Id, admin.Email, admin.DisplayName));
    }

    private static async Task<IResult> ChangeStatus(Guid tenantId, string verb, Action<Tenant> apply, ProjexDbContext db, ClaimsPrincipal principal, HttpContext http, CancellationToken ct)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == tenantId, ct);
        if (tenant is null) return Results.NotFound();
        var before = tenant.Status;
        try { apply(tenant); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        Audit(db, http, CurrentAdminId(principal), $"platform.tenant.{verb}", tenantId, new { before, after = tenant.Status });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static Task<Subscription?> LatestSubscription(ProjexDbContext db, Guid tenantId, CancellationToken ct) =>
        db.Subscriptions.AsNoTracking().Where(s => s.TenantId == tenantId).OrderByDescending(s => s.StartsAt).FirstOrDefaultAsync(ct);

    private static Guid? CurrentAdminId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirst(TokenIssuer.PlatformAdminClaim)?.Value, out var id) ? id : null;

    private static void Audit(ProjexDbContext db, HttpContext http, Guid? adminId, string action, Guid? tenantId, object? details) =>
        db.PlatformAuditEvents.Add(new PlatformAuditEvent(adminId, action, tenantId, details is null ? null : JsonSerializer.Serialize(details), http.Connection.RemoteIpAddress?.ToString()));

    private static TenantDto ToDto(Tenant x, Subscription? s) => new(x.Id, x.Code, x.Name, x.DefaultLocale, x.Status.ToString(), x.CreatedAt, s is null ? null : ToDto(s));
    private static SubscriptionDto ToDto(Subscription s)
    {
        using var entitlements = JsonDocument.Parse(s.ModuleEntitlements);
        return new SubscriptionDto(s.Id, s.PlanCode, s.Status.ToString(), s.StartsAt, s.EndsAt, s.SeatLimit, entitlements.RootElement.Clone());
    }

    /// <summary>Same rules ASP.NET Identity applies to tenant users, read from the shared (configurable) options.</summary>
    internal static string? ValidatePassword(string? password, PasswordOptions o)
    {
        if (string.IsNullOrEmpty(password) || password.Length < o.RequiredLength) return $"The password must be at least {o.RequiredLength} characters.";
        if (o.RequireDigit && !password.Any(char.IsDigit)) return "The password must contain a digit.";
        if (o.RequireLowercase && !password.Any(char.IsLower)) return "The password must contain a lowercase letter.";
        if (o.RequireUppercase && !password.Any(char.IsUpper)) return "The password must contain an uppercase letter.";
        if (o.RequireNonAlphanumeric && password.All(char.IsLetterOrDigit)) return "The password must contain a symbol.";
        return null;
    }
}

public sealed record PlatformBootstrapRequest(string Email, string? DisplayName, string Password);
public sealed record PlatformLoginRequest(string Email, string Password);
public sealed record PlatformLoginResponse(string AccessToken, string TokenType, int ExpiresIn, Guid AdminId, string Email, string DisplayName);
public sealed record PlatformAdminCreate(string Email, string? DisplayName, string Password);
public sealed record PlatformAdminDto(Guid Id, string Email, string DisplayName, bool IsActive, DateTimeOffset? LastLoginAt);
public sealed record SubscriptionWrite(string PlanCode, SubscriptionStatus Status, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, int SeatLimit, JsonElement? ModuleEntitlements);
public sealed record SubscriptionDto(Guid Id, string PlanCode, string Status, DateTimeOffset StartsAt, DateTimeOffset? EndsAt, int SeatLimit, JsonElement ModuleEntitlements);
public sealed record TenantDto(Guid Id, string Code, string Name, string DefaultLocale, string Status, DateTimeOffset CreatedAt, SubscriptionDto? Subscription);
