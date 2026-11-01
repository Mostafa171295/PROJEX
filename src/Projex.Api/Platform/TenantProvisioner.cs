using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Projex.Api.Security;
using Projex.Application.Abstractions;
using Projex.Domain.Organizations;
using Projex.Domain.Platform;
using Projex.Domain.Security;
using Projex.Infrastructure.Persistence;
using Projex.Infrastructure.Tenancy;

namespace Projex.Api.Platform;

public sealed record ProvisionTenantRequest(string TenantCode, string TenantName, string? DefaultLocale, string CompanyCode, string CompanyName,
    string AdminEmail, string AdminPassword, string? PlanCode, int? SeatLimit);

public sealed record ProvisionResult(Guid TenantId, string TenantCode, Guid CompanyId, Guid AdminUserId, Guid SubscriptionId);

/// <summary>A provisioning rule was broken. Status is the HTTP status the API should return.</summary>
public sealed class ProvisioningException(int status, string message, IEnumerable<string>? errors = null) : Exception(message)
{
    public int Status { get; } = status;
    public IReadOnlyList<string> Errors { get; } = (errors ?? []).ToList();
}

/// <summary>
/// Creates a complete tenant in one transaction: tenant, first company, first company administrator, licence and the platform audit record.
/// Platform administrators call this; it replaces the old one-shot bootstrap that could only ever create a single tenant.
/// </summary>
public sealed class TenantProvisioner(ProjexDbContext db, UserManager<ProjexUser> users, ITenantContext tenantContext)
{
    public const int TrialDays = 30, DefaultSeatLimit = 10;
    private static readonly Regex CodePattern = new("^[a-z0-9][a-z0-9-]{1,58}$", RegexOptions.Compiled);

    public async Task<ProvisionResult> ProvisionAsync(ProvisionTenantRequest r, Guid? platformAdminId, string? ipAddress, CancellationToken ct)
    {
        var code = (r.TenantCode ?? string.Empty).Trim().ToLowerInvariant();
        if (!CodePattern.IsMatch(code)) throw new ProvisioningException(400, "The tenant code must be 2-59 characters: lowercase letters, digits and hyphens, starting with a letter or digit.");
        if (new[] { r.TenantName, r.CompanyCode, r.CompanyName, r.AdminEmail, r.AdminPassword }.Any(string.IsNullOrWhiteSpace))
            throw new ProvisioningException(400, "TenantName, CompanyCode, CompanyName, AdminEmail and AdminPassword are required.");
        if (!System.Net.Mail.MailAddress.TryCreate(r.AdminEmail.Trim(), out _)) throw new ProvisioningException(400, "A valid administrator email is required.");
        var seatLimit = r.SeatLimit ?? DefaultSeatLimit;
        if (seatLimit < 1) throw new ProvisioningException(400, "The seat limit must be at least 1.");
        if (await db.Tenants.AnyAsync(t => t.Code == code, ct)) throw new ProvisioningException(409, "A tenant with this code already exists.");

        var plan = string.IsNullOrWhiteSpace(r.PlanCode) ? "trial" : r.PlanCode.Trim();
        var isTrial = string.Equals(plan, "trial", StringComparison.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var tenant = new Tenant(code, r.TenantName, r.DefaultLocale);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(ct);

            // From here on every statement is tenant-scoped (RLS), exactly as for a signed-in user of the new tenant.
            ((TenantContext)tenantContext).SetBootstrapTenant(tenant.Id);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id', {tenant.Id.ToString()}, false)", ct);

            var company = new Company(tenant.Id, r.CompanyCode, r.CompanyName);
            db.Companies.Add(company);

            var email = r.AdminEmail.Trim();
            var admin = new ProjexUser { Id = Guid.NewGuid(), UserName = $"{tenant.Code}:{email}", Email = email, TenantId = tenant.Id, EmailConfirmed = false, IsActive = true };
            var userResult = await users.CreateAsync(admin, r.AdminPassword);
            if (!userResult.Succeeded) throw new ProvisioningException(400, "The administrator account was rejected.", userResult.Errors.Select(e => e.Description));
            // The first administrator holds the built-in CompanyAdmin system role, tenant-wide.
            var adminRoleId = await RoleResolver.CompanyAdminRoleIdAsync(db, ct);
            db.RoleAssignments.Add(new UserRoleAssignment(tenant.Id, admin.Id, adminRoleId, null, null, null, platformAdminId));
            db.UserMemberships.Add(new UserMembership(tenant.Id, company.Id, admin.Id));

            var subscription = new Subscription(tenant.Id, plan, isTrial ? SubscriptionStatus.Trial : SubscriptionStatus.Active, now,
                isTrial ? now.AddDays(TrialDays) : null, seatLimit);
            db.Subscriptions.Add(subscription);
            db.PlatformAuditEvents.Add(new PlatformAuditEvent(platformAdminId, "platform.tenant.created", tenant.Id,
                System.Text.Json.JsonSerializer.Serialize(new { tenant.Code, plan, seatLimit, companyCode = company.Code }), ipAddress));

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new ProvisionResult(tenant.Id, tenant.Code, company.Id, admin.Id, subscription.Id);
        }
        catch (DbUpdateException)
        {
            throw new ProvisioningException(409, "The tenant, company or administrator conflicts with existing data.");
        }
    }
}
