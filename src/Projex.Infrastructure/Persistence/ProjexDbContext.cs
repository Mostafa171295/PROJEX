using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Projex.Application.Abstractions;
using Projex.Domain.Audit;
using Projex.Domain.Costing;
using Projex.Domain.Progress;
using Projex.Domain.Organizations;
using Projex.Domain.Projects;
using Projex.Domain.Planning;
using Projex.Domain.Platform;
using Projex.Domain.Security;
using Projex.Infrastructure.Tenancy;

namespace Projex.Infrastructure.Persistence;

public sealed class ProjexDbContext(
    DbContextOptions<ProjexDbContext> options,
    ITenantContext tenantContext)
    : IdentityDbContext<ProjexUser, IdentityRole<Guid>, Guid>(options)
{
    public Guid? CurrentTenantId => tenantContext.TenantId;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<PlatformAdmin> PlatformAdmins => Set<PlatformAdmin>();
    public DbSet<PlatformAuditEvent> PlatformAuditEvents => Set<PlatformAuditEvent>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Role> TenantRoles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRoleAssignment> RoleAssignments => Set<UserRoleAssignment>();
    public DbSet<UserMembership> UserMemberships => Set<UserMembership>();
    public DbSet<ProjectMembership> ProjectMemberships => Set<ProjectMembership>();
    public DbSet<WbsNode> WbsNodes => Set<WbsNode>();
    public DbSet<ProjectActivity> Activities => Set<ProjectActivity>();
    public DbSet<ActivityRelationshipEntity> ActivityRelationships => Set<ActivityRelationshipEntity>();
    public DbSet<ProjectCalendar> ProjectCalendars => Set<ProjectCalendar>();
    public DbSet<CalendarException> CalendarExceptions => Set<CalendarException>();
    public DbSet<ScheduleRun> ScheduleRuns => Set<ScheduleRun>();
    public DbSet<ScheduleBaseline> ScheduleBaselines => Set<ScheduleBaseline>();
    public DbSet<BaselineActivityDate> BaselineActivityDates => Set<BaselineActivityDate>();
    public DbSet<CostCode> CostCodes => Set<CostCode>();
    public DbSet<BoqItem> BoqItems => Set<BoqItem>();
    public DbSet<BudgetVersion> BudgetVersions => Set<BudgetVersion>();
    public DbSet<BudgetLine> BudgetLines => Set<BudgetLine>();
    public DbSet<CostTransaction> CostTransactions => Set<CostTransaction>();
    public DbSet<PerformanceSnapshot> PerformanceSnapshots => Set<PerformanceSnapshot>();
    public DbSet<ProgressUpdate> ProgressUpdates => Set<ProgressUpdate>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("identity");

        modelBuilder.Entity<ProjexUser>(e =>
        {
            e.ToTable("users");
            e.Property(x => x.TenantId).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.NormalizedEmail }).IsUnique();
            e.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<IdentityRole<Guid>>().ToTable("identity_roles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");

        modelBuilder.Entity<Tenant>(e =>
        {
            e.ToTable("tenants", "platform"); e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(60).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.DefaultLocale).HasMaxLength(10).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.Code).IsUnique();
        });
        // Platform-level tables carry no tenant RLS: only platform administrators (a separate identity) reach them through the API.
        modelBuilder.Entity<Subscription>(e =>
        {
            e.ToTable("subscriptions", "platform"); e.HasKey(x => x.Id);
            e.Property(x => x.PlanCode).HasMaxLength(50).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ModuleEntitlements).HasColumnType("jsonb").IsRequired();
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PlatformAdmin>(e =>
        {
            e.ToTable("platform_admins", "platform"); e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.Property(x => x.NormalizedEmail).HasMaxLength(256).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            e.Property(x => x.PasswordHash).IsRequired();
            e.HasIndex(x => x.NormalizedEmail).IsUnique();
        });
        modelBuilder.Entity<PlatformAuditEvent>(e =>
        {
            e.ToTable("audit_events", "platform"); e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(100).IsRequired();
            e.Property(x => x.Details).HasColumnType("jsonb");
            e.Property(x => x.IpAddress).HasMaxLength(64);
            // Keep EF's insert ordering aligned with the database FK: bootstrap creates
            // the admin and its first audit event in the same SaveChanges call.
            e.HasOne<PlatformAdmin>().WithMany().HasForeignKey(x => x.AdminId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Company>(e =>
        {
            e.ToTable("companies", "organization"); e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(40).IsRequired(); e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            e.HasAlternateKey(x => new { x.TenantId, x.Id });
            e.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<Branch>(e =>
        {
            e.ToTable("branches", "organization"); e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(40).IsRequired(); e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.CompanyId, x.Code }).IsUnique();
            e.HasAlternateKey(x => new { x.TenantId, x.CompanyId, x.Id });
            e.HasOne<Company>().WithMany().HasForeignKey(x => new { x.TenantId, x.CompanyId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<Project>(e =>
        {
            e.ToTable("projects", "projects"); e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(50).IsRequired(); e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.CurrencyCode).HasColumnType("char(3)").IsRequired();
            e.Property(x => x.TimeZone).HasMaxLength(80).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.TenantId, x.CompanyId, x.Code }).IsUnique();
            e.HasAlternateKey(x => new { x.TenantId, x.Id });
            e.HasIndex(x => new { x.TenantId, x.CompanyId, x.Status });
            e.HasOne<Company>().WithMany().HasForeignKey(x => new { x.TenantId, x.CompanyId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.TenantId, x.CompanyId, x.BranchId }).HasPrincipalKey(x => new { x.TenantId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<Role>(e =>
        {
            e.ToTable("roles", "identity"); e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(60).IsRequired(); e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            // System roles (TenantId null) are visible to every tenant; custom roles only to their own.
            e.HasQueryFilter(x => CurrentTenantId != null && (x.TenantId == null || x.TenantId == CurrentTenantId));
        });
        modelBuilder.Entity<UserRoleAssignment>(e =>
        {
            e.ToTable("user_role_assignments", "identity"); e.HasKey(x => x.Id);
            e.Ignore(x => x.IsTenantWide);
            e.HasIndex(x => new { x.TenantId, x.UserId });
            e.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<Permission>(e =>
        {
            e.ToTable("permissions", "identity"); e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(120).IsRequired(); e.Property(x => x.Description).HasMaxLength(300);
            e.Property(x => x.Module).HasMaxLength(60).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
        });
        modelBuilder.Entity<RolePermission>(e =>
        {
            e.ToTable("role_permissions", "identity"); e.HasKey(x => new { x.RoleId, x.PermissionId });
            e.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Permission>().WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<UserMembership>(e =>
        {
            e.ToTable("user_memberships", "identity"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.CompanyId, x.UserId }).IsUnique();
            e.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<ProjectMembership>(e =>
        {
            e.ToTable("project_memberships", "identity"); e.HasKey(x => x.Id);
            e.Property(x => x.RoleCode).HasMaxLength(40).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.ProjectId, x.UserId }).IsUnique();
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<WbsNode>(e =>
        {
            e.ToTable("wbs_nodes", "planning"); e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(80).IsRequired(); e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasAlternateKey(x => new { x.TenantId, x.ProjectId, x.Id });
            e.HasIndex(x => new { x.ProjectId, x.Code }).IsUnique();
            e.HasOne<Project>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<WbsNode>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId, x.ParentId }).HasPrincipalKey(x => new { x.TenantId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<ProjectCalendar>(e =>
        {
            e.ToTable("project_calendars", "planning"); e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired(); e.Property(x => x.TimeZone).HasMaxLength(80).IsRequired();
            e.Property(x => x.WorkingWeekJson).HasColumnName("working_week").HasColumnType("jsonb");
            e.HasAlternateKey(x => new { x.TenantId, x.Id });
            e.HasAlternateKey(x => new { x.TenantId, x.ProjectId, x.Id });
            e.HasIndex(x => new { x.ProjectId, x.Name }).IsUnique();
            e.HasOne<Project>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<ProjectActivity>(e =>
        {
            e.ToTable("activities", "planning"); e.HasKey(x => x.Id);
            e.Property(x => x.ActivityCode).HasMaxLength(50).IsRequired(); e.Property(x => x.Name).HasMaxLength(250).IsRequired();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PercentComplete).HasPrecision(5, 2); e.Property(x => x.TotalFloatMinutes).HasPrecision(20, 4);
            e.Property(x => x.RowVersion).IsConcurrencyToken();
            e.HasAlternateKey(x => new { x.TenantId, x.ProjectId, x.Id });
            e.HasIndex(x => new { x.ProjectId, x.ActivityCode }).IsUnique();
            e.HasOne<Project>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<WbsNode>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId, x.WbsNodeId }).HasPrincipalKey(x => new { x.TenantId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProjectCalendar>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId, x.CalendarId }).HasPrincipalKey(x => new { x.TenantId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<ActivityRelationshipEntity>(e =>
        {
            e.ToTable("activity_relationships", "planning"); e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(2);
            e.HasIndex(x => new { x.PredecessorActivityId, x.SuccessorActivityId, x.Type }).IsUnique();
            e.HasOne<ProjectActivity>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId, x.PredecessorActivityId }).HasPrincipalKey(x => new { x.TenantId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ProjectActivity>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId, x.SuccessorActivityId }).HasPrincipalKey(x => new { x.TenantId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<CalendarException>(e =>
        {
            e.ToTable("calendar_exceptions", "planning"); e.HasKey(x => x.Id);
            e.Property(x => x.Description).HasMaxLength(250);
            e.HasIndex(x => new { x.CalendarId, x.ExceptionDate }).IsUnique();
            e.HasOne<ProjectCalendar>().WithMany().HasForeignKey(x => new { x.TenantId, x.CalendarId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<ScheduleRun>(e =>
        {
            e.ToTable("schedule_runs", "planning"); e.HasKey(x => x.Id); e.Property(x => x.ResultJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.ProjectId, x.CalculatedAt });
            e.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<ScheduleBaseline>(e =>
        {
            e.ToTable("baselines", "planning"); e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(150).IsRequired(); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasAlternateKey(x => new { x.TenantId, x.Id }); e.HasIndex(x => new { x.ProjectId, x.Version }).IsUnique();
            e.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<BaselineActivityDate>(e =>
        {
            e.ToTable("baseline_activity_dates", "planning"); e.HasKey(x => x.Id);
            e.Property(x => x.PercentComplete).HasPrecision(5, 2);
            e.HasIndex(x => new { x.BaselineId, x.ActivityId }).IsUnique();
            e.HasOne<ScheduleBaseline>().WithMany().HasForeignKey(x => new { x.TenantId, x.BaselineId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });


        // ---- Cost control (0.8.0): schema "costing" / "progress". Money and quantities are numeric(20,4); never floating point.
        modelBuilder.Entity<CostCode>(e =>
        {
            e.ToTable("cost_codes", "costing"); e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(80).IsRequired(); e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(30);
            e.HasAlternateKey(x => new { x.TenantId, x.ProjectId, x.Id });
            e.HasIndex(x => new { x.ProjectId, x.Code }).IsUnique();
            e.HasOne<Project>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<BoqItem>(e =>
        {
            e.ToTable("boq_items", "costing"); e.HasKey(x => x.Id); e.Ignore(x => x.Amount);
            e.Property(x => x.ItemCode).HasMaxLength(80).IsRequired(); e.Property(x => x.Description).IsRequired(); e.Property(x => x.Unit).HasMaxLength(30).IsRequired();
            e.Property(x => x.Quantity).HasPrecision(20, 4); e.Property(x => x.UnitRate).HasPrecision(20, 4);
            e.Property(x => x.CurrencyCode).HasColumnType("char(3)").IsRequired();
            e.HasIndex(x => new { x.ProjectId, x.ItemCode }).IsUnique();
            e.HasOne<Project>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<CostCode>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId, x.CostCodeId }).HasPrincipalKey(x => new { x.TenantId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WbsNode>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId, x.WbsNodeId }).HasPrincipalKey(x => new { x.TenantId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<BudgetVersion>(e =>
        {
            e.ToTable("budget_versions", "costing"); e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RowVersion).IsConcurrencyToken();
            e.HasAlternateKey(x => new { x.TenantId, x.Id });
            e.HasIndex(x => new { x.ProjectId, x.VersionNumber }).IsUnique();
            e.HasOne<Project>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<BudgetLine>(e =>
        {
            e.ToTable("budget_lines", "costing"); e.HasKey(x => x.Id);
            e.Property(x => x.Description).HasMaxLength(250).IsRequired();
            e.Property(x => x.Quantity).HasPrecision(20, 4); e.Property(x => x.UnitRate).HasPrecision(20, 4); e.Property(x => x.Amount).HasPrecision(20, 4);
            e.Property(x => x.CurrencyCode).HasColumnType("char(3)").IsRequired();
            e.HasIndex(x => x.BudgetVersionId); e.HasIndex(x => x.ActivityId);
            e.HasOne<BudgetVersion>().WithMany().HasForeignKey(x => new { x.TenantId, x.BudgetVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<CostTransaction>(e =>
        {
            e.ToTable("cost_transactions", "costing"); e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).HasColumnName("transaction_type");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Amount).HasPrecision(20, 4);
            e.Property(x => x.CurrencyCode).HasColumnType("char(3)").IsRequired();
            e.Property(x => x.Description).IsRequired(); e.Property(x => x.DocumentReference).HasMaxLength(100); e.Property(x => x.IdempotencyKey).HasMaxLength(100);
            e.Property(x => x.RowVersion).IsConcurrencyToken();
            e.HasIndex(x => new { x.ProjectId, x.TransactionDate, x.Status });
            e.HasIndex(x => new { x.ProjectId, x.IdempotencyKey }).IsUnique().HasFilter("idempotency_key IS NOT NULL");
            e.HasOne<Project>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<CostCode>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId, x.CostCodeId }).HasPrincipalKey(x => new { x.TenantId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WbsNode>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId, x.WbsNodeId }).HasPrincipalKey(x => new { x.TenantId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProjectActivity>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId, x.ActivityId }).HasPrincipalKey(x => new { x.TenantId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<PerformanceSnapshot>(e =>
        {
            e.ToTable("performance_snapshots", "progress"); e.HasKey(x => x.Id);
            foreach (var m in new[] { nameof(PerformanceSnapshot.Pv), nameof(PerformanceSnapshot.Ev), nameof(PerformanceSnapshot.Ac), nameof(PerformanceSnapshot.Bac),
                nameof(PerformanceSnapshot.Eac), nameof(PerformanceSnapshot.Etc), nameof(PerformanceSnapshot.Vac), nameof(PerformanceSnapshot.Cv), nameof(PerformanceSnapshot.Sv) })
                e.Property(m).HasPrecision(20, 4);
            e.Property(x => x.Cpi).HasPrecision(12, 6); e.Property(x => x.Spi).HasPrecision(12, 6); e.Property(x => x.BudgetCoverage).HasPrecision(7, 6);
            e.Property(x => x.EacMethod).HasMaxLength(40).IsRequired();
            e.HasIndex(x => new { x.ProjectId, x.DataDate });
            e.HasOne<Project>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<ProgressUpdate>(e =>
        {
            e.ToTable("progress_updates", "progress"); e.HasKey(x => x.Id);
            e.Property(x => x.PercentComplete).HasPrecision(5, 2);
            e.Property(x => x.Status).HasMaxLength(20).IsRequired(); e.Property(x => x.MeasurementMethod).HasMaxLength(30).IsRequired();
            e.HasIndex(x => new { x.ProjectId, x.ActivityId, x.DataDate });
            e.HasOne<ProjectActivity>().WithMany().HasForeignKey(x => new { x.TenantId, x.ProjectId, x.ActivityId }).HasPrincipalKey(x => new { x.TenantId, x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<UserSession>(e =>
        {
            e.ToTable("user_sessions", "identity"); e.HasKey(x => x.Id);
            e.Property(x => x.RefreshTokenHash).HasMaxLength(64).IsRequired(); e.Property(x => x.PreviousRefreshTokenHash).HasMaxLength(64);
            e.Property(x => x.RevokedReason).HasMaxLength(40); e.Property(x => x.IpAddress).HasMaxLength(64); e.Property(x => x.UserAgent).HasMaxLength(400);
            e.HasIndex(x => x.RefreshTokenHash).IsUnique();
            e.HasOne<ProjexUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });
        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_logs", "audit"); e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(100).IsRequired(); e.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
            e.Property(x => x.IpAddress).HasColumnType("inet");
            e.Property(x => x.BeforeData).HasColumnType("jsonb"); e.Property(x => x.AfterData).HasColumnType("jsonb");
            e.HasIndex(x => new { x.TenantId, x.OccurredAt });
            e.HasQueryFilter(x => CurrentTenantId != null && x.TenantId == CurrentTenantId);
        });

        // Column names are snake_case to match database/migrations and the RLS policies.
        // Explicit HasColumnName(...) calls above take precedence.
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
            if (property.FindAnnotation(RelationalAnnotationNames.ColumnName) is null)
                property.SetColumnName(ToSnakeCase(property.Name));
    }

    private static string ToSnakeCase(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsUpper(c)) { sb.Append(c); continue; }
            if (i > 0 && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1])))) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateTenantWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateTenantWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ValidateTenantWrites()
    {
        var tenantId = tenantContext.TenantId;
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            var property = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "TenantId");
            if (property is null) continue;
            if (tenantId is null || property.CurrentValue is not Guid entityTenant || entityTenant != tenantId.Value)
                throw new InvalidOperationException("Tenant boundary violation: write rejected because the entity tenant does not match the authenticated tenant context.");
        }
    }
}
