using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Projex.Api.Security;
using Projex.Application.Abstractions;
using Projex.Application.Reporting;
using Projex.Application.Security;
using Projex.Domain.Costing;
using Projex.Domain.Planning;
using Projex.Infrastructure.Persistence;

namespace Projex.Api.Reports;

/// <summary>
/// Project overview report (JSON) and CSV exports. Cost figures appear only for callers holding ReadCost; every report states when it
/// was generated and with which criteria (SRS FR-REP-007); exports are audited (FR-REP-006).
/// </summary>
public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/v1/projects/{projectId:guid}/reports").RequireAuthorization();

        grp.MapGet("/overview", async (Guid projectId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (!await ProjectAuthorizer.HasAsync(ProjectPermission.Read, projectId, db, tenant, user, ct)) return Results.Forbid();
            var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, ct);
            if (project is null) return Results.NotFound();
            var now = DateTimeOffset.UtcNow;

            var activities = await db.Activities.AsNoTracking().Where(x => x.ProjectId == projectId)
                .Select(x => new { x.Status, x.IsCritical, x.PlannedFinish, x.PercentComplete }).ToListAsync(ct);
            var lastRun = await db.ScheduleRuns.AsNoTracking().Where(x => x.ProjectId == projectId).OrderByDescending(x => x.CalculatedAt)
                .Select(x => (DateTimeOffset?)x.CalculatedAt).FirstOrDefaultAsync(ct);
            var schedule = new
            {
                totalActivities = activities.Count,
                byStatus = activities.GroupBy(x => x.Status).Select(s => new { status = s.Key.ToString(), count = s.Count() }).ToList(),
                criticalOpen = activities.Count(x => x.IsCritical && x.Status != ActivityStatus.Completed),
                delayed = activities.Count(x => x.Status != ActivityStatus.Completed && x.PlannedFinish < now),
                averagePercentComplete = activities.Count == 0 ? 0m : Math.Round(activities.Average(x => x.PercentComplete), 2),
                lastCalculatedAt = lastRun
            };

            object? cost = null;
            if (await ProjectAuthorizer.HasAsync(ProjectPermission.ReadCost, projectId, db, tenant, user, ct))
            {
                var approved = await db.BudgetVersions.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Status == BudgetStatus.Approved, ct);
                var bac = approved is null ? 0m : await db.BudgetLines.Where(l => l.BudgetVersionId == approved.Id).SumAsync(l => l.Amount, ct);
                var counted = await db.CostTransactions.AsNoTracking()
                    .Where(x => x.ProjectId == projectId && (x.Status == CostTransactionStatus.Approved || x.Status == CostTransactionStatus.Reversed))
                    .Select(x => new { x.Type, x.Amount }).ToListAsync(ct);
                var latest = await db.PerformanceSnapshots.AsNoTracking().Where(x => x.ProjectId == projectId).OrderByDescending(x => x.DataDate).ThenByDescending(x => x.CalculatedAt)
                    .Select(x => new { x.DataDate, x.Cpi, x.Spi, x.Eac, x.Vac, x.BudgetCoverage }).FirstOrDefaultAsync(ct);
                cost = new
                {
                    currency = project.CurrencyCode, budgetAtCompletion = bac, approvedBudgetVersion = approved?.VersionNumber,
                    actualCost = counted.Where(x => x.Type == CostTransactionType.Actual).Sum(x => x.Amount),
                    committedCost = counted.Where(x => x.Type == CostTransactionType.Commitment).Sum(x => x.Amount),
                    awaitingApproval = await db.CostTransactions.CountAsync(x => x.ProjectId == projectId && x.Status == CostTransactionStatus.Submitted, ct),
                    latestSnapshot = latest
                };
            }
            return Results.Ok(new
            {
                generatedAt = now, criteria = new { projectId },
                project = new { project.Id, project.Code, project.Name, status = project.Status.ToString(), project.CurrencyCode },
                schedule, cost
            });
        });

        grp.MapGet("/boq.csv", async (Guid projectId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, IAuditWriter audit, CancellationToken ct) =>
        {
            if (!await ProjectAuthorizer.HasAsync(ProjectPermission.ReadCost, projectId, db, tenant, user, ct)) return Results.Forbid();
            var items = await db.BoqItems.AsNoTracking().Where(x => x.ProjectId == projectId).OrderBy(x => x.SortOrder).ThenBy(x => x.ItemCode).ToListAsync(ct);
            var csv = CsvWriter.Write(new[] { "ItemCode", "Description", "Unit", "Quantity", "UnitRate", "Amount", "Currency" },
                items.Select(i => (IReadOnlyList<string?>)new string?[] { i.ItemCode, i.Description, i.Unit, CsvWriter.Num(i.Quantity), CsvWriter.Num(i.UnitRate), CsvWriter.Num(i.Amount), i.CurrencyCode }));
            audit.Record("reports.exported", "Report", null, null, new { projectId, report = "boq", rows = items.Count });
            await db.SaveChangesAsync(ct);
            return CsvFile(csv, $"boq-{DateTime.UtcNow:yyyyMMdd}.csv");
        });

        grp.MapGet("/transactions.csv", async (Guid projectId, DateOnly? fromDate, DateOnly? toDate, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, IAuditWriter audit, CancellationToken ct) =>
        {
            if (!await ProjectAuthorizer.HasAsync(ProjectPermission.ReadCost, projectId, db, tenant, user, ct)) return Results.Forbid();
            var query = db.CostTransactions.AsNoTracking().Where(x => x.ProjectId == projectId);
            if (fromDate is DateOnly f) query = query.Where(x => x.TransactionDate >= f);
            if (toDate is DateOnly t) query = query.Where(x => x.TransactionDate <= t);
            var rows = await query.OrderBy(x => x.TransactionDate).ThenBy(x => x.CreatedAt).Take(50_000).ToListAsync(ct);
            var csv = CsvWriter.Write(new[] { "Date", "Type", "Status", "Reversal", "Reference", "Description", "Amount", "Currency" },
                rows.Select(r => (IReadOnlyList<string?>)new string?[] { CsvWriter.Date(r.TransactionDate), r.Type.ToString(), r.Status.ToString(), r.IsReversal ? "Yes" : "No",
                    r.DocumentReference, r.Description, CsvWriter.Num(r.Amount), r.CurrencyCode }));
            audit.Record("reports.exported", "Report", null, null, new { projectId, report = "transactions", from = fromDate, to = toDate, rows = rows.Count });
            await db.SaveChangesAsync(ct);
            return CsvFile(csv, $"transactions-{DateTime.UtcNow:yyyyMMdd}.csv");
        });

        return app;
    }

    // UTF-8 with BOM so Excel opens Arabic text correctly.
    private static IResult CsvFile(string csv, string fileName)
        => Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv; charset=utf-8", fileName);
}
