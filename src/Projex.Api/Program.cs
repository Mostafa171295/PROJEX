using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Projex.Api.Accounts;
using Projex.Api.Costing;
using Projex.Api.Platform;
using Projex.Api.Reports;
using Projex.Api.Security;
using Projex.Application.Abstractions;
using Projex.Application.Security;
using Projex.Infrastructure.Auditing;
using Projex.Scheduling.Engine.Validation;
using Projex.Domain.Organizations;
using Projex.Domain.Projects;
using System.Text.Json;
using Projex.Domain.Planning;
using Projex.Domain.Platform;
using Projex.Domain.Progress;
using Projex.Domain.Security;
using Projex.Infrastructure.Persistence;
using Projex.Infrastructure.Tenancy;
using Projex.Scheduling.Engine.Calculation;
using Projex.Scheduling.Engine.Analysis;
using Projex.Scheduling.Engine.Calendars;
using Projex.Scheduling.Engine.Models;

var builder = WebApplication.CreateBuilder(args);
// When installed as a Windows Service, run without an interactive console and
// use the executable directory as the content root for configuration files.
builder.Host.UseWindowsService(options => options.ServiceName = "PROJEX API");
var jwtKey = builder.Configuration["Authentication:JwtSigningKey"];
if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
    throw new InvalidOperationException("Authentication:JwtSigningKey must be configured with at least 32 bytes. Use a secret manager/environment variable in production.");

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<TenantSessionInterceptor>();
builder.Services.AddScoped<IAuditWriter, AuditWriter>();
builder.Services.AddScoped<TenantProvisioner>();
builder.Services.AddSingleton<IPasswordHasher<PlatformAdmin>, PasswordHasher<PlatformAdmin>>();
builder.Services.AddDbContext<ProjexDbContext>((sp, options) =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Projex"))
        .AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()));
// Security policy (SRS FR-AUTH-005: configurable). Section "Security" in appsettings / environment (Security__LockoutMaxAttempts ...).
// Defaults are the previous hard-coded values; floors stop a configuration mistake from weakening the platform below a sane minimum.
var security = builder.Configuration.GetSection("Security");
int SecurityInt(string key, int fallback, int floor) => Math.Max(floor, security.GetValue<int?>(key) ?? fallback);
bool SecurityBool(string key, bool fallback) => security.GetValue<bool?>(key) ?? fallback;
var authRequestsPerMinute = SecurityInt("AuthRequestsPerMinute", 20, 1);

builder.Services.AddIdentityCore<ProjexUser>(options =>
{
    options.Password.RequiredLength = SecurityInt("PasswordMinLength", 12, 10);
    options.Password.RequireDigit = SecurityBool("PasswordRequireDigit", true);
    options.Password.RequireUppercase = SecurityBool("PasswordRequireUppercase", true);
    options.Password.RequireLowercase = SecurityBool("PasswordRequireLowercase", true);
    options.Password.RequireNonAlphanumeric = SecurityBool("PasswordRequireNonAlphanumeric", true);
    options.Lockout.MaxFailedAccessAttempts = SecurityInt("LockoutMaxAttempts", 5, 3);
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(SecurityInt("LockoutMinutes", 15, 1));
    options.User.RequireUniqueEmail = false;
})
.AddRoles<IdentityRole<Guid>>()
.AddEntityFrameworkStores<ProjexDbContext>()
.AddSignInManager();

var issuer = builder.Configuration["Authentication:JwtIssuer"] ?? "ProjexEnterprise";
var audience = builder.Configuration["Authentication:JwtAudience"] ?? "ProjexEnterpriseClients";
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
builder.Services.AddSingleton(new TokenIssuer(issuer, audience, signingKey));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = issuer,
        ValidateAudience = true, ValidAudience = audience,
        ValidateIssuerSigningKey = true, IssuerSigningKey = signingKey,
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = ClaimTypes.NameIdentifier, RoleClaimType = ClaimTypes.Role
    };
    // A valid signature is not enough: the session behind the token must still be active (logout, disabled user, refresh-token reuse).
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            // Platform administrator token: no session or tenant; valid only while the administrator account is active.
            if (context.Principal?.FindFirst(TokenIssuer.PlatformAdminClaim)?.Value is { } platformAdminValue)
            {
                var platformDb = context.HttpContext.RequestServices.GetRequiredService<ProjexDbContext>();
                var adminActive = Guid.TryParse(platformAdminValue, out var platformAdminId)
                    && await platformDb.PlatformAdmins.AsNoTracking().AnyAsync(a => a.Id == platformAdminId && a.IsActive, context.HttpContext.RequestAborted);
                if (!adminActive) context.Fail("The platform administrator is not active.");
                return;
            }
            if (context.Principal is null || !Guid.TryParse(context.Principal.FindFirst(TokenIssuer.SessionClaim)?.Value, out var sessionId))
            { context.Fail("The token is not bound to a session."); return; }
            context.HttpContext.User = context.Principal;   // lets the tenant context resolve for this lookup
            var sessionDb = context.HttpContext.RequestServices.GetRequiredService<ProjexDbContext>();
            var now = DateTimeOffset.UtcNow;
            // The tenant must still be Active too: suspending a tenant cuts off tokens that were issued before the suspension.
            var active = await sessionDb.UserSessions.AsNoTracking().AnyAsync(s => s.Id == sessionId && s.RevokedAt == null && s.ExpiresAt > now
                && sessionDb.Tenants.Any(t => t.Id == s.TenantId && t.Status == TenantStatus.Active), context.HttpContext.RequestAborted);
            if (!active) context.Fail("The session was revoked, has expired, or the tenant is not active.");
        }
    };
});
// Brute-force protection for the anonymous credential endpoints (login, refresh, bootstrap), partitioned per client IP.
// Behind a reverse proxy, configure ForwardedHeaders so RemoteIpAddress is the real client, not the proxy.
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authRequestsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddAuthorization(authorization =>
{
    // Tenant endpoints require a tenant-bound token; platform administrator tokens (no tenant_id) are rejected by all of them.
    authorization.DefaultPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireClaim("tenant_id").Build();
    authorization.AddPolicy(PlatformEndpoints.Policy, policy => policy.RequireAuthenticatedUser().RequireClaim(TokenIssuer.PlatformAdminClaim));
});
builder.Services.AddSingleton<CriticalPathCalculator>();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

var app = builder.Build();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { product = "PROJEX ENTERPRISE", version = "0.9.2-preview.1", status = "Identity hardening, platform administration and RBAC preview" }));
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", timestampUtc = DateTimeOffset.UtcNow }));

app.MapPost("/api/v1/auth/login", async (LoginRequest request, ProjexDbContext db,
    UserManager<ProjexUser> users, SignInManager<ProjexUser> signInManager, ITenantContext tenantContext, IAuditWriter audit,
    IHttpContextAccessor httpAccessor, TokenIssuer tokenIssuer, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.TenantKey) || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
        return Results.BadRequest(new { error = "TenantKey, email and password are required." });
    var tenantKey = request.TenantKey.Trim().ToLowerInvariant();
    var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.Code == tenantKey && x.Status == TenantStatus.Active, ct);
    if (tenant is null) return Results.Unauthorized();
    ((TenantContext)tenantContext).SetBootstrapTenant(tenant.Id);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id', {tenant.Id.ToString()}, false)", ct);
    var normalizedEmail = request.Email.Trim().ToUpperInvariant();
    var user = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.TenantId == tenant.Id && x.NormalizedEmail == normalizedEmail && x.IsActive, ct);
    if (user is null)
    {
        audit.Record("auth.login.failed", "User", null, null, new { reason = "unknown_or_inactive_user" });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Results.Unauthorized();
    }
    var signIn = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
    UserSession? session = null; string? refreshToken = null;
    if (signIn.Succeeded)
    {
        refreshToken = TokenIssuer.NewRefreshToken();
        var http = httpAccessor.HttpContext;
        session = new UserSession(tenant.Id, user.Id, TokenIssuer.Hash(refreshToken), DateTimeOffset.UtcNow.Add(TokenIssuer.SessionLifetime),
            http?.Connection.RemoteIpAddress?.ToString(), http?.Request.Headers.UserAgent.ToString());
        db.UserSessions.Add(session);
        user.LastLoginAt = DateTimeOffset.UtcNow;
    }
    audit.Record(signIn.Succeeded ? "auth.login.succeeded" : signIn.IsLockedOut ? "auth.login.locked_out" : "auth.login.failed",
        "User", user.Id, null, null, user.Id);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    if (!signIn.Succeeded) return Results.Unauthorized();
    var roles = await RoleResolver.TenantWideClaimsAsync(db, user.Id, ct);
    return Results.Ok(new LoginResponse(tokenIssuer.Issue(user, tenant.Id, tenant.Code, roles, session!.Id), "Bearer", TokenIssuer.AccessTokenSeconds,
        user.Id, tenant.Id, user.Email ?? string.Empty, refreshToken));
}).AllowAnonymous().RequireRateLimiting("auth");

// Platform administration (tenants, licences, platform admins) lives in Platform/PlatformEndpoints.cs.
app.MapPlatformEndpoints();
app.MapRbacEndpoints();

app.MapGet("/api/v1/auth/me", (ITenantContext tenant) => Results.Ok(new { userId = tenant.UserId, tenantId = tenant.TenantId })).RequireAuthorization();

// Organization: tenant-scoped reads; company administrators alone can create or rename companies/branches.
var org = app.MapGroup("/api/v1/organization").RequireAuthorization();
org.MapGet("/companies", async (ProjexDbContext db, ITenantContext tenant, CancellationToken ct) =>
{
    if (tenant.UserId is not Guid uid) return Results.Unauthorized();
    var companyIds = db.UserMemberships.Where(m => m.UserId == uid && m.IsActive).Select(m => m.CompanyId);
    var rows = await db.Companies.AsNoTracking().Where(c => companyIds.Contains(c.Id)).OrderBy(c => c.Code)
        .Select(c => new CompanyDto(c.Id, c.Code, c.Name, c.IsActive)).ToListAsync(ct);
    return Results.Ok(rows);
});
org.MapPost("/companies", async (CompanyWrite request, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct) =>
{
    if (!user.IsInRole("CompanyAdmin")) return Results.Forbid();
    if (tenant.TenantId is not Guid tid || tenant.UserId is not Guid uid) return Results.Unauthorized();
    var company = new Company(tid, request.Code, request.Name);
    db.Companies.Add(company);
    db.UserMemberships.Add(new UserMembership(tid, company.Id, uid));
    await db.SaveChangesAsync(ct);
    return Results.Created($"/api/v1/organization/companies/{company.Id}", new CompanyDto(company.Id, company.Code, company.Name, company.IsActive));
});
org.MapGet("/companies/{companyId:guid}/branches", async (Guid companyId, ProjexDbContext db, ITenantContext tenant, CancellationToken ct) =>
{
    if (tenant.UserId is not Guid uid) return Results.Unauthorized();
    var member = await db.UserMemberships.AnyAsync(m => m.CompanyId == companyId && m.UserId == uid && m.IsActive, ct);
    if (!member) return Results.NotFound();
    var rows = await db.Branches.AsNoTracking().Where(b => b.CompanyId == companyId).OrderBy(b => b.Code)
        .Select(b => new BranchDto(b.Id, b.CompanyId, b.Code, b.Name, b.IsActive)).ToListAsync(ct);
    return Results.Ok(rows);
});
org.MapPost("/companies/{companyId:guid}/branches", async (Guid companyId, BranchWrite request, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct) =>
{
    if (!user.IsInRole("CompanyAdmin")) return Results.Forbid();
    if (tenant.TenantId is not Guid tid) return Results.Unauthorized();
    var companyExists = await db.Companies.AnyAsync(c => c.Id == companyId && c.IsActive, ct);
    if (!companyExists) return Results.NotFound();
    var branch = new Branch(tid, companyId, request.Code, request.Name);
    db.Branches.Add(branch);
    await db.SaveChangesAsync(ct);
    return Results.Created($"/api/v1/organization/companies/{companyId}/branches/{branch.Id}", new BranchDto(branch.Id, companyId, branch.Code, branch.Name, branch.IsActive));
});

// Projects: members can view projects within their companies; only CompanyAdmin can create or change lifecycle.
var projects = app.MapGroup("/api/v1/projects").RequireAuthorization();
projects.MapGet("", async (Guid? companyId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal principal, CancellationToken ct) =>
{
    if (tenant.UserId is not Guid uid) return Results.Unauthorized();
    var companyIds = db.UserMemberships.Where(m => m.UserId == uid && m.IsActive).Select(m => m.CompanyId);
    var query = db.Projects.AsNoTracking().Where(p => companyIds.Contains(p.CompanyId) &&
        (principal.IsInRole("CompanyAdmin") || (db.ProjectMemberships.Any(m => m.ProjectId == p.Id && m.UserId == uid && m.IsActive) || db.RoleAssignments.Any(a => a.UserId == uid && (a.ProjectId == null || a.ProjectId == p.Id) && (a.CompanyId == null || a.CompanyId == p.CompanyId) && (a.BranchId == null || a.BranchId == p.BranchId)))));
    if (companyId.HasValue) query = query.Where(p => p.CompanyId == companyId.Value);
    var rows = await query.OrderBy(p => p.Code).Select(p => new ProjectDto(p.Id, p.CompanyId, p.BranchId, p.Code, p.Name, p.CurrencyCode, p.TimeZone, p.Status.ToString())).ToListAsync(ct);
    return Results.Ok(rows);
});
projects.MapGet("/{projectId:guid}", async (Guid projectId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal principal, CancellationToken ct) =>
{
    if (tenant.UserId is not Guid uid) return Results.Unauthorized();
    var row = await db.Projects.AsNoTracking().Where(p => p.Id == projectId && db.UserMemberships.Any(m => m.CompanyId == p.CompanyId && m.UserId == uid && m.IsActive) &&
            (principal.IsInRole("CompanyAdmin") || (db.ProjectMemberships.Any(m => m.ProjectId == p.Id && m.UserId == uid && m.IsActive) || db.RoleAssignments.Any(a => a.UserId == uid && (a.ProjectId == null || a.ProjectId == p.Id) && (a.CompanyId == null || a.CompanyId == p.CompanyId) && (a.BranchId == null || a.BranchId == p.BranchId)))))
        .Select(p => new ProjectDto(p.Id, p.CompanyId, p.BranchId, p.Code, p.Name, p.CurrencyCode, p.TimeZone, p.Status.ToString())).SingleOrDefaultAsync(ct);
    return row is null ? Results.NotFound() : Results.Ok(row);
});
projects.MapPost("", async (ProjectWrite request, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct) =>
{
    if (!user.IsInRole("CompanyAdmin")) return Results.Forbid();
    if (tenant.TenantId is not Guid tid) return Results.Unauthorized();
    var company = await db.Companies.AnyAsync(c => c.Id == request.CompanyId && c.IsActive, ct);
    if (!company) return Results.NotFound(new { error = "Company was not found or is inactive." });
    if (request.BranchId.HasValue && !await db.Branches.AnyAsync(b => b.Id == request.BranchId && b.CompanyId == request.CompanyId && b.IsActive, ct))
        return Results.BadRequest(new { error = "Branch must be active and belong to the selected company." });
    var project = new Project(tid, request.CompanyId, request.Code, request.Name, request.CurrencyCode, request.TimeZone);
    if (request.BranchId.HasValue) project.AssignBranch(request.BranchId.Value);
    db.Projects.Add(project);
    await db.SaveChangesAsync(ct);
    return Results.Created($"/api/v1/projects/{project.Id}", new ProjectDto(project.Id, project.CompanyId, project.BranchId, project.Code, project.Name, project.CurrencyCode, project.TimeZone, project.Status.ToString()));
});
projects.MapPatch("/{projectId:guid}/name", async (Guid projectId, RenameProject request, ProjexDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
{
    if (!user.IsInRole("CompanyAdmin")) return Results.Forbid();
    var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, ct);
    if (project is null) return Results.NotFound();
    project.Rename(request.Name);
    await db.SaveChangesAsync(ct);
    return Results.Ok(new { project.Id, project.Name });
});
projects.MapPost("/{projectId:guid}/activate", (Guid projectId, ProjexDbContext db, IAuditWriter audit, ClaimsPrincipal user, CancellationToken ct) => ChangeProjectStatus(projectId, db, audit, user, ct, p => p.Activate()));
projects.MapPost("/{projectId:guid}/hold", (Guid projectId, ProjexDbContext db, IAuditWriter audit, ClaimsPrincipal user, CancellationToken ct) => ChangeProjectStatus(projectId, db, audit, user, ct, p => p.PutOnHold()));
projects.MapPost("/{projectId:guid}/complete", (Guid projectId, ProjexDbContext db, IAuditWriter audit, ClaimsPrincipal user, CancellationToken ct) => ChangeProjectStatus(projectId, db, audit, user, ct, p => p.Complete()));
projects.MapPost("/{projectId:guid}/cancel", (Guid projectId, ProjexDbContext db, IAuditWriter audit, ClaimsPrincipal user, CancellationToken ct) => ChangeProjectStatus(projectId, db, audit, user, ct, p => p.Cancel()));


// Project-scoped access management. CompanyAdmin can manage assignments for existing company users.
var access = app.MapGroup("/api/v1/projects/{projectId:guid}/members").RequireAuthorization();
access.MapGet("", async (Guid projectId, ProjexDbContext db, ITenantContext tenant, CancellationToken ct) =>
{
    if (tenant.UserId is not Guid uid) return Results.Unauthorized();
    var allowed = await db.Projects.AnyAsync(p => p.Id == projectId &&
        db.UserMemberships.Any(m => m.CompanyId == p.CompanyId && m.UserId == uid && m.IsActive), ct);
    if (!allowed) return Results.NotFound();
    var rows = await (from pm in db.ProjectMemberships.AsNoTracking()
                      join u in db.Users.AsNoTracking() on pm.UserId equals u.Id
                      where pm.ProjectId == projectId && pm.IsActive
                      orderby u.Email
                      select new ProjectMemberDto(pm.UserId, u.Email ?? string.Empty, pm.RoleCode, pm.IsActive))
        .ToListAsync(ct);
    return Results.Ok(rows);
});
access.MapPut("/{userId:guid}", async (Guid projectId, Guid userId, ProjectMemberWrite request,
    ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal principal, IAuditWriter audit, CancellationToken ct) =>
{
    if (!principal.IsInRole("CompanyAdmin")) return Results.Forbid();
    if (tenant.TenantId is not Guid tid || tenant.UserId is not Guid actorId) return Results.Unauthorized();
    var allowedRoles = new[] { "PROJECTMANAGER", "PLANNINGENGINEER", "COSTCONTROLENGINEER", "SITEENGINEER", "VIEWER" };
    if (string.IsNullOrWhiteSpace(request.RoleCode) || !allowedRoles.Contains(request.RoleCode.Trim().ToUpperInvariant()))
        return Results.BadRequest(new { error = "Unsupported project role." });
    var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, ct);
    if (project is null) return Results.NotFound();
    if (!await db.UserMemberships.AnyAsync(m => m.CompanyId == project.CompanyId && m.UserId == actorId && m.IsActive, ct))
        return Results.Forbid();
    var target = await db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.TenantId == tid && u.IsActive, ct);
    if (target is null) return Results.NotFound(new { error = "Active user was not found in this tenant." });
    if (!await db.UserMemberships.AnyAsync(m => m.CompanyId == project.CompanyId && m.UserId == userId && m.IsActive, ct))
        return Results.BadRequest(new { error = "User must first be an active member of the project's company." });
    var membership = await db.ProjectMemberships.SingleOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId, ct);
    var previousRole = membership?.RoleCode;
    if (membership is null) db.ProjectMemberships.Add(new ProjectMembership(tid, projectId, userId, request.RoleCode));
    else if (!membership.IsActive) return Results.Conflict(new { error = "Revoked membership cannot be reactivated through this endpoint." });
    else membership.ChangeRole(request.RoleCode);
    audit.Record("project.member.role_set", "ProjectMembership", userId, previousRole is null ? null : new { roleCode = previousRole },
        new { projectId, roleCode = request.RoleCode.Trim().ToUpperInvariant() });
    await db.SaveChangesAsync(ct);
    return Results.Ok(new ProjectMemberDto(target.Id, target.Email ?? string.Empty, request.RoleCode.Trim().ToUpperInvariant(), true));
});
access.MapDelete("/{userId:guid}", async (Guid projectId, Guid userId, ProjexDbContext db,
    ITenantContext tenant, ClaimsPrincipal principal, IAuditWriter audit, CancellationToken ct) =>
{
    if (!principal.IsInRole("CompanyAdmin")) return Results.Forbid();
    if (tenant.UserId is not Guid actorId) return Results.Unauthorized();
    var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, ct);
    if (project is null) return Results.NotFound();
    if (!await db.UserMemberships.AnyAsync(m => m.CompanyId == project.CompanyId && m.UserId == actorId && m.IsActive, ct))
        return Results.Forbid();
    var membership = await db.ProjectMemberships.SingleOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId && m.IsActive, ct);
    if (membership is null) return Results.NotFound();
    var revokedRole = membership.RoleCode;
    membership.Revoke();
    audit.Record("project.member.revoked", "ProjectMembership", userId, new { projectId, roleCode = revokedRole }, new { isActive = false });
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
});


// Persistent planning and scheduling endpoints (PROJEX 0.6.1).
var planning = app.MapGroup("/api/v1/projects/{projectId:guid}/planning").RequireAuthorization();
planning.MapGet("/wbs", async (Guid projectId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct) =>
{
    if (!await CanReadProjectAsync(projectId, db, tenant, user, ct)) return Results.Forbid();
    var rows = await db.WbsNodes.AsNoTracking().Where(x => x.ProjectId == projectId).OrderBy(x => x.SortOrder).ThenBy(x => x.Code)
        .Select(x => new WbsNodeDto(x.Id, x.ParentId, x.Code, x.Name, x.SortOrder)).ToListAsync(ct);
    return Results.Ok(rows);
});
planning.MapPost("/wbs", async (Guid projectId, WbsNodeWrite request, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct) =>
{
    if (tenant.TenantId is not Guid tid || !await CanPlanProjectAsync(projectId, db, tenant, user, ct)) return Results.Forbid();
    if (request.ParentId.HasValue && !await db.WbsNodes.AnyAsync(x => x.Id == request.ParentId && x.ProjectId == projectId, ct)) return Results.BadRequest(new { error = "Parent WBS node must belong to this project." });
    var entity = new WbsNode(tid, projectId, request.Code, request.Name, request.ParentId, request.SortOrder);
    db.WbsNodes.Add(entity);
    try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { return Results.Conflict(new { error = "WBS code already exists or the parent relationship is invalid." }); }
    return Results.Created($"/api/v1/projects/{projectId}/planning/wbs/{entity.Id}", new WbsNodeDto(entity.Id, entity.ParentId, entity.Code, entity.Name, entity.SortOrder));
});
planning.MapGet("/activities", async (Guid projectId, Guid? wbsNodeId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct) =>
{
    if (!await CanReadProjectAsync(projectId, db, tenant, user, ct)) return Results.Forbid();
    var query = db.Activities.AsNoTracking().Where(x => x.ProjectId == projectId);
    if (wbsNodeId.HasValue) query = query.Where(x => x.WbsNodeId == wbsNodeId.Value);
    var rows = await query.OrderBy(x => x.ActivityCode).Select(x => new ActivityDto(x.Id, x.WbsNodeId, x.CalendarId, x.ActivityCode, x.Name, x.Type.ToString(), x.DurationMinutes, x.PercentComplete, x.Status.ToString(), x.IsCritical, x.TotalFloatMinutes, x.RowVersion)).ToListAsync(ct);
    return Results.Ok(rows);
});
planning.MapPost("/activities", async (Guid projectId, ActivityWrite request, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct) =>
{
    if (tenant.TenantId is not Guid tid || !await CanPlanProjectAsync(projectId, db, tenant, user, ct)) return Results.Forbid();
    if (!await db.WbsNodes.AnyAsync(x => x.Id == request.WbsNodeId && x.ProjectId == projectId, ct)) return Results.BadRequest(new { error = "WBS node must belong to this project." });
    if (request.CalendarId.HasValue && !await db.ProjectCalendars.AnyAsync(x => x.Id == request.CalendarId && x.ProjectId == projectId, ct)) return Results.BadRequest(new { error = "Calendar must belong to this project." });
    if (!Enum.TryParse<ActivityType>(request.Type, true, out var type)) return Results.BadRequest(new { error = "Activity type must be Task or Milestone." });
    ProjectActivity activity;
    try { activity = new ProjectActivity(tid, projectId, request.WbsNodeId, request.Code, request.Name, request.DurationMinutes, type, request.CalendarId); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    db.Activities.Add(activity);
    try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { return Results.Conflict(new { error = "Activity code already exists or a referenced record is invalid." }); }
    return Results.Created($"/api/v1/projects/{projectId}/planning/activities/{activity.Id}", new { activity.Id, activity.ActivityCode, activity.Name });
});
planning.MapPut("/activities/{activityId:guid}", async (Guid projectId, Guid activityId, ActivityUpdate request, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, IAuditWriter audit, CancellationToken ct) =>
{
    if (!await CanPlanProjectAsync(projectId, db, tenant, user, ct)) return Results.Forbid();
    var activity = await db.Activities.SingleOrDefaultAsync(x => x.Id == activityId && x.ProjectId == projectId, ct);
    if (activity is null) return Results.NotFound();
    if (activity.RowVersion != request.ExpectedRowVersion) return Results.Conflict(new { error = "The activity was changed by another user. Reload and retry." });
    if (!await db.WbsNodes.AnyAsync(x => x.Id == request.WbsNodeId && x.ProjectId == projectId, ct)) return Results.BadRequest(new { error = "WBS node must belong to this project." });
    if (request.CalendarId.HasValue && !await db.ProjectCalendars.AnyAsync(x => x.Id == request.CalendarId && x.ProjectId == projectId, ct)) return Results.BadRequest(new { error = "Calendar must belong to this project." });
    try
    {
        var before = new { activity.Name, activity.DurationMinutes, activity.WbsNodeId, activity.CalendarId };
        activity.UpdateDetails(request.Name, request.DurationMinutes, request.WbsNodeId, request.CalendarId);
        audit.Record("planning.activity.updated", "Activity", activity.Id, before, new { request.Name, request.DurationMinutes, request.WbsNodeId, request.CalendarId });
        await db.SaveChangesAsync(ct);
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (DbUpdateConcurrencyException) { return Results.Conflict(new { error = "Concurrent update detected." }); }
    return Results.Ok(new { activity.Id, activity.RowVersion });
});
// Gantt editing command: duration changes are optimistic-concurrency protected and mark the schedule stale.
planning.MapPatch("/schedule/activities/{activityId:guid}", async (Guid projectId, Guid activityId, GanttActivityEdit request,
    ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, IAuditWriter audit, CancellationToken ct) =>
{
    if (!await CanPlanProjectAsync(projectId, db, tenant, user, ct)) return Results.Forbid();
    var activity = await db.Activities.SingleOrDefaultAsync(x => x.Id == activityId && x.ProjectId == projectId, ct);
    if (activity is null) return Results.NotFound();
    if (activity.RowVersion != request.ExpectedRowVersion)
        return Results.Conflict(new { error = "The activity was changed by another user. Reload the Gantt data and retry." });
    try
    {
        var previousDuration = activity.DurationMinutes;
        activity.UpdateDetails(activity.Name, request.DurationMinutes, activity.WbsNodeId, activity.CalendarId);
        audit.Record("planning.activity.duration_changed", "Activity", activity.Id, new { durationMinutes = previousDuration }, new { durationMinutes = request.DurationMinutes });
        await db.SaveChangesAsync(ct);
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (DbUpdateConcurrencyException) { return Results.Conflict(new { error = "Concurrent update detected. Reload the Gantt data and retry." }); }
    return Results.Ok(new { activity.Id, activity.DurationMinutes, activity.RowVersion,
        scheduleStatus = "Stale", requiresRecalculation = true });
});

planning.MapPost("/relationships", async (Guid projectId, RelationshipWrite request, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, IAuditWriter audit, CancellationToken ct) =>
{
    if (tenant.TenantId is not Guid tid || !await CanPlanProjectAsync(projectId, db, tenant, user, ct)) return Results.Forbid();
    if (!Enum.TryParse<PlanningRelationshipType>(request.Type, true, out var type)) return Results.BadRequest(new { error = "Relationship type must be FS, SS, FF or SF." });
    var ids = await db.Activities.Where(x => x.ProjectId == projectId && (x.Id == request.PredecessorActivityId || x.Id == request.SuccessorActivityId)).Select(x => x.Id).ToListAsync(ct);
    if (ids.Count != 2) return Results.BadRequest(new { error = "Both activities must belong to this project." });
    var edges = await db.ActivityRelationships.Where(x => x.ProjectId == projectId)
        .Select(x => new { x.PredecessorActivityId, x.SuccessorActivityId }).ToListAsync(ct);
    if (RelationshipCycleDetector.WouldCreateCycle(edges.Select(e => (e.PredecessorActivityId, e.SuccessorActivityId)),
            request.PredecessorActivityId, request.SuccessorActivityId))
        return Results.Conflict(new { error = "This relationship would create a circular dependency." });
    try
    {
        var relationship = new ActivityRelationshipEntity(tid, projectId, request.PredecessorActivityId, request.SuccessorActivityId, type, request.LagMinutes);
        db.ActivityRelationships.Add(relationship);
        audit.Record("planning.relationship.created", "ActivityRelationship", relationship.Id, null,
            new { projectId, request.PredecessorActivityId, request.SuccessorActivityId, type = type.ToString(), request.LagMinutes });
        await db.SaveChangesAsync(ct);
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (DbUpdateException) { return Results.Conflict(new { error = "This relationship already exists or violates project integrity." }); }
    return Results.Created($"/api/v1/projects/{projectId}/planning/relationships", new { request.PredecessorActivityId, request.SuccessorActivityId, type = type.ToString(), request.LagMinutes });
});
planning.MapPost("/calendars", async (Guid projectId, CalendarWrite request, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct) =>
{
    if (tenant.TenantId is not Guid tid || !await CanPlanProjectAsync(projectId, db, tenant, user, ct)) return Results.Forbid();
    try { var calendar = new ProjectCalendar(tid, projectId, request.Name, request.TimeZone, request.WorkingWeekJson, request.IsDefault); db.ProjectCalendars.Add(calendar); await db.SaveChangesAsync(ct); return Results.Created($"/api/v1/projects/{projectId}/planning/calendars/{calendar.Id}", new { calendar.Id, calendar.Name, calendar.TimeZone, calendar.IsDefault }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (DbUpdateException) { return Results.Conflict(new { error = "Calendar name already exists or calendar data is invalid." }); }
});
planning.MapGet("/calendars", async (Guid projectId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct) =>
{
    if (!await CanReadProjectAsync(projectId, db, tenant, user, ct)) return Results.Forbid();
    var calendars = await db.ProjectCalendars.AsNoTracking().Where(x => x.ProjectId == projectId).OrderBy(x => x.Name)
        .Select(x => new { x.Id, x.Name, x.TimeZone, x.WorkingWeekJson, x.IsDefault }).ToListAsync(ct);
    return Results.Ok(calendars);
});
planning.MapPost("/calendars/{calendarId:guid}/exceptions", async (Guid projectId, Guid calendarId, CalendarExceptionWrite request, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct) =>
{
    if (tenant.TenantId is not Guid tid || !await CanPlanProjectAsync(projectId, db, tenant, user, ct)) return Results.Forbid();
    if (!await db.ProjectCalendars.AnyAsync(x => x.Id == calendarId && x.ProjectId == projectId, ct)) return Results.NotFound();
    try { var exception = new CalendarException(tid, calendarId, request.Date, request.IsWorkingDay, request.StartTime, request.EndTime, request.Description); db.CalendarExceptions.Add(exception); await db.SaveChangesAsync(ct); return Results.Created($"/api/v1/projects/{projectId}/planning/calendars/{calendarId}/exceptions/{exception.Id}", new { exception.Id, exception.ExceptionDate, exception.IsWorkingDay }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (DbUpdateException) { return Results.Conflict(new { error = "An exception already exists for this date or the exception is invalid." }); }
});
planning.MapPut("/activities/{activityId:guid}/progress", async (Guid projectId, Guid activityId, ActivityProgressWrite request, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, IAuditWriter audit, CancellationToken ct) =>
{
    if (!await CanUpdateProgressAsync(projectId, db, tenant, user, ct)) return Results.Forbid();
    var activity = await db.Activities.SingleOrDefaultAsync(x => x.Id == activityId && x.ProjectId == projectId, ct);
    if (activity is null) return Results.NotFound();
    if (activity.RowVersion != request.ExpectedRowVersion) return Results.Conflict(new { error = "The activity was changed by another user. Reload and retry." });
    if (!Enum.TryParse<ActivityStatus>(request.Status, true, out var status)) return Results.BadRequest(new { error = "Invalid activity status." });
    try
    {
        var before = new { percentComplete = activity.PercentComplete, status = activity.Status.ToString() };
        activity.SetProgress(request.PercentComplete, status, request.ActualStart, request.ActualFinish);
        if (tenant.TenantId is Guid progressTenant && tenant.UserId is Guid progressUser)
            db.ProgressUpdates.Add(new ProgressUpdate(progressTenant, projectId, activity.Id, request.DataDate ?? DateOnly.FromDateTime(DateTime.UtcNow), request.PercentComplete, status.ToString(), progressUser));
        audit.Record("planning.activity.progress_updated", "Activity", activity.Id, before, new { request.PercentComplete, status = status.ToString(), request.ActualStart, request.ActualFinish });
        await db.SaveChangesAsync(ct);
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (DbUpdateConcurrencyException) { return Results.Conflict(new { error = "Concurrent update detected." }); }
    return Results.Ok(new { activity.Id, activity.PercentComplete, status = activity.Status.ToString(), activity.RowVersion });
});
planning.MapPost("/schedule/calculate", async (Guid projectId, DateOnly? startDate, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CriticalPathCalculator calculator, IAuditWriter audit, CancellationToken ct) =>
{
    if (tenant.TenantId is not Guid tid || tenant.UserId is not Guid uid) return Results.Unauthorized();
    if (!await CanPlanProjectAsync(projectId, db, tenant, user, ct)) return Results.Forbid();
    var activities = await db.Activities.Where(x => x.ProjectId == projectId).ToListAsync(ct);
    if (activities.Count == 0) return Results.BadRequest(new { error = "Add at least one activity before calculating the schedule." });
    var links = await db.ActivityRelationships.Where(x => x.ProjectId == projectId).ToListAsync(ct);
    try
    {
        // One common project calendar is required for a mathematically consistent CPM time axis.
        var explicitCalendarIds = activities.Where(x => x.CalendarId.HasValue).Select(x => x.CalendarId!.Value).Distinct().ToArray();
        if (explicitCalendarIds.Length > 1)
            return Results.Conflict(new { error = "PROJEX 0.6.1 requires one common calendar per schedule network. Mixed activity calendars will be supported by the resource-aware scheduling engine in a later release." });

        var calendars = await db.ProjectCalendars.Where(x => x.ProjectId == projectId).ToListAsync(ct);
        ProjectCalendar? selectedCalendar = null;
        if (explicitCalendarIds.Length == 1)
            selectedCalendar = calendars.SingleOrDefault(x => x.Id == explicitCalendarIds[0]);
        else
        {
            var defaults = calendars.Where(x => x.IsDefault).ToList();
            if (defaults.Count > 1)
                return Results.Conflict(new { error = "The project has more than one default calendar. Keep exactly one default calendar before calculating." });
            selectedCalendar = defaults.SingleOrDefault() ?? (calendars.Count == 1 ? calendars[0] : null);
        }
        if (explicitCalendarIds.Length == 1 && selectedCalendar is null)
            return Results.BadRequest(new { error = "An activity references a calendar that does not exist in this project." });

        var projectTimeZone = await db.Projects.Where(x => x.Id == projectId).Select(x => x.TimeZone).SingleAsync(ct);
        const string standardWeek = "{\"monday\":[{\"start\":\"08:00\",\"end\":\"12:00\"},{\"start\":\"13:00\",\"end\":\"17:00\"}],\"tuesday\":[{\"start\":\"08:00\",\"end\":\"12:00\"},{\"start\":\"13:00\",\"end\":\"17:00\"}],\"wednesday\":[{\"start\":\"08:00\",\"end\":\"12:00\"},{\"start\":\"13:00\",\"end\":\"17:00\"}],\"thursday\":[{\"start\":\"08:00\",\"end\":\"12:00\"},{\"start\":\"13:00\",\"end\":\"17:00\"}],\"friday\":[{\"start\":\"08:00\",\"end\":\"12:00\"},{\"start\":\"13:00\",\"end\":\"17:00\"}]}";
        var timeZoneId = selectedCalendar?.TimeZone ?? projectTimeZone;
        var weekJson = selectedCalendar?.WorkingWeekJson ?? standardWeek;
        var exceptions = selectedCalendar is null
            ? Array.Empty<CalendarExceptionDay>()
            : (await db.CalendarExceptions.Where(x => x.CalendarId == selectedCalendar.Id).ToListAsync(ct))
                .Select(x => new CalendarExceptionDay(x.ExceptionDate, x.IsWorkingDay, x.StartTime, x.EndTime)).ToArray();
        var calendarDefinition = WorkingCalendarDefinition.Parse(timeZoneId, weekJson, exceptions);
        var effectiveStartDate = startDate ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, calendarDefinition.TimeZone).DateTime);

        // CPM uses working minutes end-to-end; no hidden 8-hour conversion is applied to task durations or lags.
        var inputActivities = activities.Select(x => new ScheduleActivity(x.Id.ToString(), x.Name, x.DurationMinutes)).ToArray();
        var inputLinks = links.Select(x => new Projex.Scheduling.Engine.Models.ActivityRelationship(
            x.PredecessorActivityId.ToString(), x.SuccessorActivityId.ToString(),
            x.Type switch { PlanningRelationshipType.FS => RelationshipType.FinishToStart, PlanningRelationshipType.SS => RelationshipType.StartToStart, PlanningRelationshipType.FF => RelationshipType.FinishToFinish, _ => RelationshipType.StartToFinish },
            x.LagMinutes)).ToArray();
        var result = calculator.Calculate(inputActivities, inputLinks);
        var calendarEngine = new WorkingCalendarEngine();
        var byId = result.Activities.ToDictionary(x => Guid.Parse(x.ActivityId));
        var placements = new List<object>(activities.Count);
        foreach (var activity in activities)
        {
            var item = byId[activity.Id];
            var plannedStart = calendarEngine.AddWorkingMinutesForStart(calendarDefinition, effectiveStartDate, item.EarlyStart);
            var plannedFinish = calendarEngine.AddWorkingMinutes(calendarDefinition, effectiveStartDate, item.EarlyFinish);
            var lateStart = calendarEngine.AddWorkingMinutesForStart(calendarDefinition, effectiveStartDate, item.LateStart);
            var lateFinish = calendarEngine.AddWorkingMinutes(calendarDefinition, effectiveStartDate, item.LateFinish);
            activity.ApplyCalendarSchedule(item.EarlyStart, item.EarlyFinish, item.LateStart, item.LateFinish,
                item.TotalFloat, plannedStart, plannedFinish);
            placements.Add(new { activityId = activity.Id, activity.ActivityCode, activity.Name, plannedStart, plannedFinish, lateStart, lateFinish,
                earlyStartMinutes = item.EarlyStart, earlyFinishMinutes = item.EarlyFinish,
                lateStartMinutes = item.LateStart, lateFinishMinutes = item.LateFinish,
                totalFloatMinutes = item.TotalFloat, item.IsCritical });
        }
        var response = new { projectStartDate = effectiveStartDate, calendarId = selectedCalendar?.Id,
            calendarName = selectedCalendar?.Name ?? "Project Standard Calendar", timeZone = timeZoneId,
            durationUnit = "working minutes", projectDurationMinutes = result.ProjectDuration,
            projectFinish = calendarEngine.AddWorkingMinutes(calendarDefinition, effectiveStartDate, result.ProjectDuration),
            cpm = result, activities = placements };
        var run = new ScheduleRun(tid, projectId, uid, "CPM-CALENDAR-0.6.1", JsonSerializer.Serialize(response));
        db.ScheduleRuns.Add(run);
        audit.Record("planning.schedule.calculated", "Project", projectId, null, new { runId = run.Id, activityCount = activities.Count, projectDurationMinutes = result.ProjectDuration });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { runId = run.Id, run.CalculatedAt, response });
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
});
planning.MapGet("/schedule/gantt", async (Guid projectId, Guid? baselineId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct) =>
{
    if (!await CanReadProjectAsync(projectId, db, tenant, user, ct)) return Results.Forbid();
    var activities = await (from activity in db.Activities.AsNoTracking()
                            join wbs in db.WbsNodes.AsNoTracking() on new { activity.TenantId, activity.ProjectId, Id = activity.WbsNodeId } equals new { wbs.TenantId, wbs.ProjectId, wbs.Id }
                            where activity.ProjectId == projectId
                            orderby wbs.SortOrder, activity.ActivityCode
                            select new { activity.Id, activity.ActivityCode, activity.Name, wbsNodeId = wbs.Id,
                                wbsCode = wbs.Code, wbsName = wbs.Name, activity.DurationMinutes,
                                activity.PlannedStart, activity.PlannedFinish, activity.PercentComplete,
                                status = activity.Status.ToString(), activity.IsCritical, activity.TotalFloatMinutes,
                                activity.RowVersion }).ToListAsync(ct);
    var relationships = await db.ActivityRelationships.AsNoTracking().Where(x => x.ProjectId == projectId)
        .Select(x => new { x.PredecessorActivityId, x.SuccessorActivityId, type = x.Type.ToString(), x.LagMinutes }).ToListAsync(ct);
    var latestRun = await db.ScheduleRuns.AsNoTracking().Where(x => x.ProjectId == projectId)
        .OrderByDescending(x => x.CalculatedAt).FirstOrDefaultAsync(ct);
    var currentActivities = await db.Activities.AsNoTracking().Where(x => x.ProjectId == projectId).ToListAsync(ct);
    var isStale = latestRun is null || currentActivities.Any(x => x.UpdatedAt > latestRun.CalculatedAt) ||
        (latestRun is not null && (await db.ActivityRelationships.AsNoTracking().AnyAsync(x => x.ProjectId == projectId && x.CreatedAt > latestRun.CalculatedAt, ct) ||
         await db.ProjectCalendars.AsNoTracking().AnyAsync(x => x.ProjectId == projectId && x.UpdatedAt > latestRun.CalculatedAt, ct) ||
         await db.CalendarExceptions.AsNoTracking().AnyAsync(x => db.ProjectCalendars.Any(c => c.Id == x.CalendarId && c.ProjectId == projectId) && x.CreatedAt > latestRun.CalculatedAt, ct)));
    Dictionary<Guid, BaselineActivityDate> baselineByActivity = new();
    object? baselineInfo = null;
    if (baselineId.HasValue)
    {
        var baseline = await db.ScheduleBaselines.AsNoTracking().SingleOrDefaultAsync(x => x.Id == baselineId.Value && x.ProjectId == projectId, ct);
        if (baseline is null) return Results.NotFound(new { error = "Baseline not found in this project." });
        if (baseline.Status != BaselineStatus.Approved) return Results.Conflict(new { error = "Gantt baseline overlay requires an approved baseline." });
        baselineByActivity = await db.BaselineActivityDates.AsNoTracking().Where(x => x.BaselineId == baseline.Id).ToDictionaryAsync(x => x.ActivityId, ct);
        baselineInfo = new { baseline.Id, baseline.Name, baseline.Version, status = baseline.Status.ToString() };
    }
    var ganttRows = activities.Select(a =>
    {
        baselineByActivity.TryGetValue(a.Id, out var baseline);
        var baselineStart = baseline?.PlannedStart;
        var baselineFinish = baseline?.PlannedFinish;
        var startVariance = baselineStart.HasValue && a.PlannedStart.HasValue ? (decimal?)((a.PlannedStart.Value - baselineStart.Value).TotalDays) : null;
        var finishVariance = baselineFinish.HasValue && a.PlannedFinish.HasValue ? (decimal?)((a.PlannedFinish.Value - baselineFinish.Value).TotalDays) : null;
        return new { a.Id, a.ActivityCode, a.Name, a.wbsNodeId, a.wbsCode, a.wbsName, a.DurationMinutes,
            a.PlannedStart, a.PlannedFinish, a.PercentComplete, a.status, a.IsCritical, a.TotalFloatMinutes, a.RowVersion,
            baselineStart, baselineFinish, startVarianceCalendarDays = startVariance, finishVarianceCalendarDays = finishVariance };
    }).ToList();
    return Results.Ok(new { projectId, latestScheduleRun = latestRun is null ? null : new { latestRun.Id, latestRun.CalculatedAt, latestRun.EngineVersion },
        scheduleStatus = latestRun is null ? "NotCalculated" : isStale ? "Stale" : "Current", requiresRecalculation = isStale,
        baseline = baselineInfo, activities = ganttRows, relationships,
        hasCalculatedDates = activities.All(x => x.PlannedStart.HasValue && x.PlannedFinish.HasValue) });
});
planning.MapGet("/schedule/latest", async (Guid projectId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct) =>
{
    if (!await CanReadProjectAsync(projectId, db, tenant, user, ct)) return Results.Forbid();
    var run = await db.ScheduleRuns.AsNoTracking().Where(x => x.ProjectId == projectId).OrderByDescending(x => x.CalculatedAt).FirstOrDefaultAsync(ct);
    return run is null ? Results.NotFound() : Results.Ok(new { run.Id, run.CalculatedAt, run.EngineVersion, result = JsonSerializer.Deserialize<JsonElement>(run.ResultJson) });
});
planning.MapPost("/baselines", async (Guid projectId, BaselineWrite request, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, IAuditWriter audit, CancellationToken ct) =>
{
    if (tenant.TenantId is not Guid tid || !await CanPlanProjectAsync(projectId, db, tenant, user, ct)) return Results.Forbid();
    var activities = await db.Activities.Where(x => x.ProjectId == projectId).ToListAsync(ct);
    if (activities.Count == 0) return Results.BadRequest(new { error = "A baseline requires at least one project activity." });
    var latestRun = await db.ScheduleRuns.Where(x => x.ProjectId == projectId).OrderByDescending(x => x.CalculatedAt).FirstOrDefaultAsync(ct);
    if (latestRun is null || activities.Any(x => x.PlannedStart is null || x.PlannedFinish is null || x.UpdatedAt > latestRun.CalculatedAt))
        return Results.Conflict(new { error = "Calculate the schedule before capturing a baseline: the stored dates are missing or stale." });
    var version = (await db.ScheduleBaselines.Where(x => x.ProjectId == projectId).Select(x => (int?)x.Version).MaxAsync(ct) ?? 0) + 1;
    var baseline = new ScheduleBaseline(tid, projectId, request.Name, version, request.ChangeReason);
    db.ScheduleBaselines.Add(baseline);
    foreach (var activity in activities) db.BaselineActivityDates.Add(new BaselineActivityDate(tid, baseline.Id, activity.Id, activity.PlannedStart, activity.PlannedFinish, activity.DurationMinutes, activity.PercentComplete));
    audit.Record("planning.baseline.captured", "Baseline", baseline.Id, null, new { projectId, baseline.Name, baseline.Version, activityCount = activities.Count });
    await db.SaveChangesAsync(ct);
    return Results.Created($"/api/v1/projects/{projectId}/planning/baselines/{baseline.Id}", new { baseline.Id, baseline.Name, baseline.Version, status = baseline.Status.ToString(), activityCount = activities.Count });
});

planning.MapPost("/baselines/{baselineId:guid}/approve", async (Guid projectId, Guid baselineId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, IAuditWriter audit, CancellationToken ct) =>
{
    if (tenant.UserId is not Guid uid) return Results.Unauthorized();
    if (!await CanPlanProjectAsync(projectId, db, tenant, user, ct)) return Results.Forbid();
    var baseline = await db.ScheduleBaselines.SingleOrDefaultAsync(x => x.Id == baselineId && x.ProjectId == projectId, ct);
    if (baseline is null) return Results.NotFound();
    try { baseline.Approve(uid); audit.Record("planning.baseline.approved", "Baseline", baseline.Id, new { status = "Draft" }, new { status = "Approved", baseline.Version }); await db.SaveChangesAsync(ct); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
    return Results.Ok(new { baseline.Id, status = baseline.Status.ToString(), baseline.ApprovedAt, baseline.ApprovedBy });
});

// Advanced project controls (PROJEX 0.6.2): baseline variance, schedule health and dependency impact.
planning.MapGet("/baselines", async (Guid projectId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct) =>
{
    if (!await CanReadProjectAsync(projectId, db, tenant, user, ct)) return Results.Forbid();
    var rows = await db.ScheduleBaselines.AsNoTracking().Where(x => x.ProjectId == projectId)
        .OrderByDescending(x => x.Version)
        .Select(x => new { x.Id, x.Name, x.Version, status = x.Status.ToString(), x.CapturedAt, x.ApprovedAt, x.ApprovedBy, x.ChangeReason,
            activityCount = db.BaselineActivityDates.Count(d => d.BaselineId == x.Id) }).ToListAsync(ct);
    return Results.Ok(rows);
});

planning.MapGet("/baselines/{baselineId:guid}/variance", async (Guid projectId, Guid baselineId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct) =>
{
    if (!await CanReadProjectAsync(projectId, db, tenant, user, ct)) return Results.Forbid();
    var baseline = await db.ScheduleBaselines.AsNoTracking().SingleOrDefaultAsync(x => x.Id == baselineId && x.ProjectId == projectId, ct);
    if (baseline is null) return Results.NotFound();
    if (baseline.Status != BaselineStatus.Approved)
        return Results.Conflict(new { error = "Variance reporting requires an approved baseline.", baselineStatus = baseline.Status.ToString() });

    var baselineRows = await db.BaselineActivityDates.AsNoTracking().Where(x => x.BaselineId == baselineId).ToListAsync(ct);
    var currentRows = await db.Activities.AsNoTracking().Where(x => x.ProjectId == projectId).ToListAsync(ct);
    var currentById = currentRows.ToDictionary(x => x.Id);
    var variance = baselineRows.Select(b =>
    {
        currentById.TryGetValue(b.ActivityId, out var current);
        var currentStart = current?.PlannedStart;
        var currentFinish = current?.PlannedFinish;
        var startVarianceDays = b.PlannedStart.HasValue && currentStart.HasValue
            ? (decimal)(currentStart.Value - b.PlannedStart.Value).TotalDays : (decimal?)null;
        var finishVarianceDays = b.PlannedFinish.HasValue && currentFinish.HasValue
            ? (decimal)(currentFinish.Value - b.PlannedFinish.Value).TotalDays : (decimal?)null;
        return new
        {
            activityId = b.ActivityId,
            activityName = current?.Name,
            baselineStart = b.PlannedStart,
            currentStart = current?.PlannedStart,
            startVarianceCalendarDays = startVarianceDays,
            baselineFinish = b.PlannedFinish,
            currentFinish = current?.PlannedFinish,
            finishVarianceCalendarDays = finishVarianceDays,
            baselineDurationMinutes = b.DurationMinutes,
            currentDurationMinutes = current?.DurationMinutes,
            durationVarianceMinutes = current is null ? (int?)null : current.DurationMinutes - b.DurationMinutes,
            baselinePercentComplete = b.PercentComplete,
            currentPercentComplete = current?.PercentComplete,
            progressVariancePercentagePoints = current is null ? (decimal?)null : current.PercentComplete - b.PercentComplete,
            missingFromCurrentSchedule = current is null
        };
    }).ToList();
    var currentOnlyCount = currentRows.Count(x => baselineRows.All(b => b.ActivityId != x.Id));
    return Results.Ok(new { baseline = new { baseline.Id, baseline.Name, baseline.Version, baseline.ApprovedAt },
        summary = new { comparedActivities = variance.Count, missingActivities = variance.Count(x => x.missingFromCurrentSchedule), addedActivities = currentOnlyCount,
            delayedActivities = variance.Count(x => x.finishVarianceCalendarDays > 0), earlyActivities = variance.Count(x => x.finishVarianceCalendarDays < 0),
            averageFinishVarianceCalendarDays = variance.Where(x => x.finishVarianceCalendarDays.HasValue).Select(x => x.finishVarianceCalendarDays!.Value).DefaultIfEmpty().Average() },
        activities = variance, addedActivityIds = currentRows.Where(x => baselineRows.All(b => b.ActivityId != x.Id)).Select(x => x.Id).ToList() });
});

planning.MapGet("/schedule/impact/{activityId:guid}", async (Guid projectId, Guid activityId, int? maxDepth, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct) =>
{
    if (!await CanReadProjectAsync(projectId, db, tenant, user, ct)) return Results.Forbid();
    var source = await db.Activities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == activityId && x.ProjectId == projectId, ct);
    if (source is null) return Results.NotFound();
    var allActivities = await db.Activities.AsNoTracking().Where(x => x.ProjectId == projectId)
        .Select(x => new { x.Id, x.ActivityCode, x.Name, x.PlannedStart, x.PlannedFinish, x.IsCritical, x.TotalFloatMinutes }).ToListAsync(ct);
    var links = await db.ActivityRelationships.AsNoTracking().Where(x => x.ProjectId == projectId).ToListAsync(ct);
    var activityMap = allActivities.ToDictionary(x => x.Id);
    var depthLimit = Math.Clamp(maxDepth ?? 100, 1, 500);
    var engineLinks = links.Select(x => new Projex.Scheduling.Engine.Models.ActivityRelationship(
        x.PredecessorActivityId.ToString(), x.SuccessorActivityId.ToString(),
        x.Type switch { PlanningRelationshipType.FS => RelationshipType.FinishToStart, PlanningRelationshipType.SS => RelationshipType.StartToStart,
            PlanningRelationshipType.FF => RelationshipType.FinishToFinish, _ => RelationshipType.StartToFinish }, x.LagMinutes)).ToArray();
    var impactNodes = new ScheduleImpactAnalyzer().Analyze(activityId.ToString(), engineLinks, depthLimit);
    var impacted = impactNodes.Where(x => Guid.TryParse(x.ActivityId, out _)).Select(x =>
    {
        var item = activityMap[Guid.Parse(x.ActivityId)];
        return new { activityId = item.Id, item.ActivityCode, item.Name, dependencyDepth = x.DependencyDepth,
            relationshipType = x.RelationshipType.ToString(), lag = x.Lag, item.PlannedStart, item.PlannedFinish, item.IsCritical, item.TotalFloatMinutes };
    }).ToList();
    return Results.Ok(new { sourceActivity = new { source.Id, source.ActivityCode, source.Name, source.DurationMinutes, source.PlannedStart, source.PlannedFinish, source.IsCritical },
        impactedActivityCount = impacted.Count, traversalDepthLimit = depthLimit,
        warning = "This is a dependency reachability analysis. Run schedule calculation after changing activity data to obtain recalculated dates and float.",
        impactedActivities = impacted });
});

planning.MapGet("/schedule/health", async (Guid projectId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct) =>
{
    if (!await CanReadProjectAsync(projectId, db, tenant, user, ct)) return Results.Forbid();
    var activities = await db.Activities.AsNoTracking().Where(x => x.ProjectId == projectId).ToListAsync(ct);
    var relationships = await db.ActivityRelationships.AsNoTracking().Where(x => x.ProjectId == projectId).ToListAsync(ct);
    var latestRun = await db.ScheduleRuns.AsNoTracking().Where(x => x.ProjectId == projectId).OrderByDescending(x => x.CalculatedAt).FirstOrDefaultAsync(ct);
    var isStale = latestRun is null || activities.Any(x => x.UpdatedAt > latestRun.CalculatedAt) ||
        (latestRun is not null && (relationships.Any(x => x.CreatedAt > latestRun.CalculatedAt) ||
         await db.ProjectCalendars.AsNoTracking().AnyAsync(x => x.ProjectId == projectId && x.UpdatedAt > latestRun.CalculatedAt, ct) ||
         await db.CalendarExceptions.AsNoTracking().AnyAsync(x => db.ProjectCalendars.Any(c => c.Id == x.CalendarId && c.ProjectId == projectId) && x.CreatedAt > latestRun.CalculatedAt, ct)));
    var critical = activities.Count(x => x.IsCritical);
    var missingDates = activities.Count(x => !x.PlannedStart.HasValue || !x.PlannedFinish.HasValue);
    var completed = activities.Count(x => x.Status == ActivityStatus.Completed);
    var averageProgress = activities.Count == 0 ? 0m : activities.Average(x => x.PercentComplete);
    var finish = activities.Where(x => x.PlannedFinish.HasValue).Select(x => x.PlannedFinish!.Value).DefaultIfEmpty().Max();
    return Results.Ok(new { projectId, activityCount = activities.Count, relationshipCount = relationships.Count,
        criticalActivityCount = critical, activitiesWithoutCalculatedDates = missingDates,
        completedActivityCount = completed, averagePercentComplete = averageProgress,
        forecastProjectFinish = finish == default ? (DateTimeOffset?)null : finish,
        latestScheduleRun = latestRun is null ? null : new { latestRun.Id, latestRun.CalculatedAt, latestRun.EngineVersion },
        scheduleStatus = latestRun is null ? "NotCalculated" : isStale ? "Stale" : "Current",
        requiresRecalculation = isStale });
});

app.MapGet("/api/v1/dashboard/summary", async (ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal principal, CancellationToken ct) =>
{
    if (tenant.UserId is not Guid uid) return Results.Unauthorized();
    var companyIds = db.UserMemberships.Where(m => m.UserId == uid && m.IsActive).Select(m => m.CompanyId);
    var query = db.Projects.AsNoTracking().Where(p => companyIds.Contains(p.CompanyId) &&
        (principal.IsInRole("CompanyAdmin") || (db.ProjectMemberships.Any(m => m.ProjectId == p.Id && m.UserId == uid && m.IsActive) || db.RoleAssignments.Any(a => a.UserId == uid && (a.ProjectId == null || a.ProjectId == p.Id) && (a.CompanyId == null || a.CompanyId == p.CompanyId) && (a.BranchId == null || a.BranchId == p.BranchId)))));
    var grouped = await query.GroupBy(p => p.Status).Select(g => new { status = g.Key, count = g.Count() }).ToListAsync(ct);
    var counts = grouped.Select(x => new { status = x.status.ToString(), x.count }).ToList();
    return Results.Ok(new { totalProjects = counts.Sum(x => x.count), byStatus = counts });
}).RequireAuthorization();

app.MapAccountEndpoints();
app.MapCostingEndpoints();
app.MapReportEndpoints();

app.Run();

static Task<bool> HasProjectPermissionAsync(ProjectPermission permission, Guid projectId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal principal, CancellationToken ct)
    => ProjectAuthorizer.HasAsync(permission, projectId, db, tenant, principal, ct);
static Task<bool> CanPlanProjectAsync(Guid projectId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal principal, CancellationToken ct)
    => HasProjectPermissionAsync(ProjectPermission.Plan, projectId, db, tenant, principal, ct);
static Task<bool> CanReadProjectAsync(Guid projectId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal principal, CancellationToken ct)
    => HasProjectPermissionAsync(ProjectPermission.Read, projectId, db, tenant, principal, ct);
static Task<bool> CanUpdateProgressAsync(Guid projectId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal principal, CancellationToken ct)
    => HasProjectPermissionAsync(ProjectPermission.UpdateProgress, projectId, db, tenant, principal, ct);

static async Task<IResult> ChangeProjectStatus(Guid id, ProjexDbContext db, IAuditWriter audit, ClaimsPrincipal user, CancellationToken ct, Action<Project> change)
{
    if (!user.IsInRole("CompanyAdmin")) return Results.Forbid();
    var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == id, ct);
    if (project is null) return Results.NotFound();
    var previousStatus = project.Status;
    try { change(project); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
    audit.Record("project.status_changed", "Project", project.Id, new { status = previousStatus.ToString() }, new { status = project.Status.ToString() });
    await db.SaveChangesAsync(ct);
    return Results.Ok(new { project.Id, status = project.Status.ToString() });
}

public sealed record LoginRequest(string TenantKey, string Email, string Password);
public sealed record LoginResponse(string AccessToken, string TokenType, int ExpiresIn, Guid UserId, Guid TenantId, string Email, string? RefreshToken = null);
public sealed record CompanyWrite(string Code, string Name);
public sealed record BranchWrite(string Code, string Name);
public sealed record ProjectWrite(Guid CompanyId, Guid? BranchId, string Code, string Name, string CurrencyCode, string TimeZone);
public sealed record RenameProject(string Name);
public sealed record CompanyDto(Guid Id, string Code, string Name, bool IsActive);
public sealed record BranchDto(Guid Id, Guid CompanyId, string Code, string Name, bool IsActive);
public sealed record ProjectDto(Guid Id, Guid CompanyId, Guid? BranchId, string Code, string Name, string CurrencyCode, string TimeZone, string Status);

public sealed record ProjectMemberWrite(string RoleCode);
public sealed record ProjectMemberDto(Guid UserId, string Email, string RoleCode, bool IsActive);

public sealed record ScheduleCalculationRequest(IReadOnlyList<ScheduleActivityInput> Activities, IReadOnlyList<ScheduleRelationshipInput> Relationships);
public sealed record ScheduleActivityInput(string Id, string Name, decimal Duration);
public sealed record ScheduleRelationshipInput(string PredecessorId, string SuccessorId, RelationshipType Type, decimal Lag = 0m);

public sealed record WbsNodeWrite(string Code, string Name, Guid? ParentId, int SortOrder = 0);
public sealed record WbsNodeDto(Guid Id, Guid? ParentId, string Code, string Name, int SortOrder);
public sealed record ActivityWrite(Guid WbsNodeId, Guid? CalendarId, string Code, string Name, int DurationMinutes, string Type = "Task");
public sealed record ActivityUpdate(Guid WbsNodeId, Guid? CalendarId, string Name, int DurationMinutes, long ExpectedRowVersion);
public sealed record GanttActivityEdit(int DurationMinutes, long ExpectedRowVersion);
public sealed record ActivityDto(Guid Id, Guid WbsNodeId, Guid? CalendarId, string Code, string Name, string Type, int DurationMinutes, decimal PercentComplete, string Status, bool IsCritical, decimal TotalFloatMinutes, long RowVersion);
public sealed record RelationshipWrite(Guid PredecessorActivityId, Guid SuccessorActivityId, string Type, int LagMinutes = 0);
public sealed record CalendarWrite(string Name, string TimeZone, string WorkingWeekJson, bool IsDefault = false);
public sealed record CalendarExceptionWrite(DateOnly Date, bool IsWorkingDay, TimeOnly? StartTime, TimeOnly? EndTime, string? Description);
public sealed record ActivityProgressWrite(decimal PercentComplete, string Status, DateTimeOffset? ActualStart, DateTimeOffset? ActualFinish, long ExpectedRowVersion, DateOnly? DataDate = null);
public sealed record BaselineWrite(string Name, string? ChangeReason);

public partial class Program { } // required by WebApplicationFactory<Program>
