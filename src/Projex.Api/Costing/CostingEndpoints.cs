using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Projex.Api.Security;
using Projex.Application.Abstractions;
using Projex.Application.Security;
using Projex.Cost.Engine.CashFlow;
using Projex.Cost.Engine.Evm;
using Projex.Domain.Costing;
using Projex.Domain.Planning;
using Projex.Domain.Progress;
using Projex.Domain.Projects;
using Projex.Infrastructure.Persistence;

namespace Projex.Api.Costing;

/// <summary>
/// PROJEX 0.8.0 cost control: cost codes, BOQ, versioned budgets, cost transactions with approval and reversal,
/// EVM and cash flow. Every handler authorizes on the server per project (SRS 3.2); every state change is audited in the
/// same unit of work as the change itself.
/// </summary>
public static class CostingEndpoints
{
    private sealed record Auth(Guid TenantId, Guid UserId, Project Project);

    public static IEndpointRouteBuilder MapCostingEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/projects/{projectId:guid}/costing").RequireAuthorization();

        // ---------------- Cost codes ----------------
        g.MapGet("/cost-codes", async (Guid projectId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (await AuthorizeAsync(ProjectPermission.ReadCost, projectId, db, tenant, user, ct) is null) return Results.Forbid();
            var rows = await db.CostCodes.AsNoTracking().Where(x => x.ProjectId == projectId).OrderBy(x => x.Code)
                .Select(x => new CostCodeDto(x.Id, x.ParentId, x.Code, x.Name, x.Category.ToString(), x.IsActive)).ToListAsync(ct);
            return Results.Ok(rows);
        });

        g.MapPost("/cost-codes", async (Guid projectId, CostCodeWrite request, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, IAuditWriter audit, CancellationToken ct) =>
        {
            var a = await AuthorizeAsync(ProjectPermission.ManageCost, projectId, db, tenant, user, ct);
            if (a is null) return Results.Forbid();
            if (IsClosed(a.Project)) return Results.Conflict(new { error = "The project is closed." });
            if (!TryEnum<CostCategory>(request.Category, out var category)) return Results.BadRequest(new { error = "Invalid cost category." });
            if (request.ParentId is Guid parentId && !await db.CostCodes.AnyAsync(x => x.Id == parentId && x.ProjectId == projectId, ct))
                return Results.BadRequest(new { error = "Parent cost code must belong to this project." });
            CostCode entity;
            try { entity = new CostCode(a.TenantId, projectId, request.Code, request.Name, category, request.ParentId); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            db.CostCodes.Add(entity);
            audit.Record("costing.cost_code.created", "CostCode", entity.Id, null, new { projectId, entity.Code, entity.Name, category = category.ToString() });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Results.Conflict(new { error = "Cost code already exists or the parent is invalid." }); }
            return Results.Created($"/api/v1/projects/{projectId}/costing/cost-codes/{entity.Id}", new CostCodeDto(entity.Id, entity.ParentId, entity.Code, entity.Name, category.ToString(), entity.IsActive));
        });

        // ---------------- BOQ ----------------
        g.MapGet("/boq", async (Guid projectId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (await AuthorizeAsync(ProjectPermission.ReadCost, projectId, db, tenant, user, ct) is null) return Results.Forbid();
            var items = await db.BoqItems.AsNoTracking().Where(x => x.ProjectId == projectId).OrderBy(x => x.SortOrder).ThenBy(x => x.ItemCode).ToListAsync(ct);
            var dtos = items.Select(x => ToDto(x)).ToList();
            return Results.Ok(new { total = dtos.Sum(x => x.Amount), items = dtos });
        });

        g.MapPost("/boq", async (Guid projectId, BoqItemWrite request, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, IAuditWriter audit, CancellationToken ct) =>
        {
            var a = await AuthorizeAsync(ProjectPermission.ManageCost, projectId, db, tenant, user, ct);
            if (a is null) return Results.Forbid();
            if (IsClosed(a.Project)) return Results.Conflict(new { error = "The project is closed." });
            if (await ValidateReferencesAsync(db, projectId, request.CostCodeId, request.WbsNodeId, null, ct) is { } refError) return Results.BadRequest(new { error = refError });
            BoqItem item;
            try
            {
                item = new BoqItem(a.TenantId, projectId, request.ItemCode, request.Description, request.Unit, request.Quantity, request.UnitRate,
                    request.CurrencyCode ?? a.Project.CurrencyCode, request.SortOrder, request.CostCodeId, request.WbsNodeId);
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            if (item.CurrencyCode != a.Project.CurrencyCode) return Results.BadRequest(new { error = "MVP supports one currency per project: the BOQ currency must equal the project currency." });
            db.BoqItems.Add(item);
            audit.Record("costing.boq.item_created", "BoqItem", item.Id, null, new { projectId, item.ItemCode, item.Quantity, item.UnitRate, item.Amount });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Results.Conflict(new { error = "BOQ item code already exists or a reference is invalid." }); }
            return Results.Created($"/api/v1/projects/{projectId}/costing/boq/{item.Id}", ToDto(item));
        });

        // ---------------- Budgets ----------------
        g.MapGet("/budgets", async (Guid projectId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (await AuthorizeAsync(ProjectPermission.ReadCost, projectId, db, tenant, user, ct) is null) return Results.Forbid();
            var versions = await db.BudgetVersions.AsNoTracking().Where(x => x.ProjectId == projectId).OrderByDescending(x => x.VersionNumber).ToListAsync(ct);
            var ids = versions.Select(v => v.Id).ToList();
            var totals = await db.BudgetLines.AsNoTracking().Where(l => ids.Contains(l.BudgetVersionId))
                .GroupBy(l => l.BudgetVersionId).Select(x => new { Id = x.Key, Count = x.Count(), Total = x.Sum(l => l.Amount) }).ToListAsync(ct);
            return Results.Ok(versions.Select(v =>
            {
                var t = totals.FirstOrDefault(x => x.Id == v.Id);
                return new BudgetVersionDto(v.Id, v.VersionNumber, v.Type.ToString(), v.Status.ToString(), v.EffectiveDate, t?.Count ?? 0, t?.Total ?? 0m, v.CreatedBy, v.ApprovedBy, v.ApprovedAt, v.RowVersion);
            }));
        });

        g.MapPost("/budgets", async (Guid projectId, BudgetVersionWrite request, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, IAuditWriter audit, CancellationToken ct) =>
        {
            var a = await AuthorizeAsync(ProjectPermission.ManageCost, projectId, db, tenant, user, ct);
            if (a is null) return Results.Forbid();
            if (IsClosed(a.Project)) return Results.Conflict(new { error = "The project is closed." });
            if (!TryEnum<BudgetType>(request.Type, out var type)) return Results.BadRequest(new { error = "Budget type must be Original, Revised or Forecast." });
            if (type == BudgetType.Original && await db.BudgetVersions.AnyAsync(x => x.ProjectId == projectId && x.Type == BudgetType.Original, ct))
                return Results.Conflict(new { error = "An original budget already exists; create a Revised version." });
            var next = (await db.BudgetVersions.Where(x => x.ProjectId == projectId).Select(x => (int?)x.VersionNumber).MaxAsync(ct) ?? 0) + 1;
            var version = new BudgetVersion(a.TenantId, projectId, next, type, request.EffectiveDate, a.UserId);
            db.BudgetVersions.Add(version);
            audit.Record("costing.budget.created", "BudgetVersion", version.Id, null, new { projectId, version.VersionNumber, type = type.ToString() });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Results.Conflict(new { error = "A concurrent budget version was created. Retry." }); }
            return Results.Created($"/api/v1/projects/{projectId}/costing/budgets/{version.Id}", new { version.Id, version.VersionNumber, status = version.Status.ToString() });
        });

        g.MapGet("/budgets/{budgetId:guid}", async (Guid projectId, Guid budgetId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (await AuthorizeAsync(ProjectPermission.ReadCost, projectId, db, tenant, user, ct) is null) return Results.Forbid();
            var v = await db.BudgetVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == budgetId && x.ProjectId == projectId, ct);
            if (v is null) return Results.NotFound();
            var lines = await db.BudgetLines.AsNoTracking().Where(l => l.BudgetVersionId == budgetId).OrderBy(l => l.Description).ToListAsync(ct);
            return Results.Ok(new
            {
                version = new BudgetVersionDto(v.Id, v.VersionNumber, v.Type.ToString(), v.Status.ToString(), v.EffectiveDate, lines.Count, lines.Sum(l => l.Amount), v.CreatedBy, v.ApprovedBy, v.ApprovedAt, v.RowVersion),
                lines = lines.Select(l => new BudgetLineDto(l.Id, l.CostCodeId, l.WbsNodeId, l.ActivityId, l.Description, l.Quantity, l.UnitRate, l.Amount, l.CurrencyCode))
            });
        });

        g.MapPost("/budgets/{budgetId:guid}/lines", async (Guid projectId, Guid budgetId, BudgetLineWrite request, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, IAuditWriter audit, CancellationToken ct) =>
        {
            var a = await AuthorizeAsync(ProjectPermission.ManageCost, projectId, db, tenant, user, ct);
            if (a is null) return Results.Forbid();
            var version = await db.BudgetVersions.SingleOrDefaultAsync(x => x.Id == budgetId && x.ProjectId == projectId, ct);
            if (version is null) return Results.NotFound();
            if (await ValidateReferencesAsync(db, projectId, request.CostCodeId, request.WbsNodeId, request.ActivityId, ct) is { } refError) return Results.BadRequest(new { error = refError });
            var currency = request.CurrencyCode ?? a.Project.CurrencyCode;
            BudgetLine line;
            try
            {
                version.EnsureEditable();
                var hasQuantity = request.Quantity.HasValue || request.UnitRate.HasValue;
                if (hasQuantity && request.Amount.HasValue) throw new ArgumentException("Provide either quantity and unitRate, or a lump-sum amount, not both.");
                if (hasQuantity)
                {
                    if (!request.Quantity.HasValue || !request.UnitRate.HasValue) throw new ArgumentException("Both quantity and unitRate are required.");
                    line = BudgetLine.FromQuantity(a.TenantId, budgetId, request.Description, request.Quantity.Value, request.UnitRate.Value, currency, request.CostCodeId, request.WbsNodeId, request.ActivityId);
                }
                else if (request.Amount.HasValue)
                    line = BudgetLine.LumpSum(a.TenantId, budgetId, request.Description, request.Amount.Value, currency, request.CostCodeId, request.WbsNodeId, request.ActivityId);
                else throw new ArgumentException("Provide quantity and unitRate, or an amount.");
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
            if (line.CurrencyCode != a.Project.CurrencyCode) return Results.BadRequest(new { error = "MVP supports one currency per project: the budget currency must equal the project currency." });
            db.BudgetLines.Add(line);
            audit.Record("costing.budget.line_added", "BudgetLine", line.Id, null, new { projectId, budgetId, line.Description, line.Amount, line.ActivityId });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Results.Conflict(new { error = "The budget is no longer editable or a reference is invalid." }); }
            return Results.Created($"/api/v1/projects/{projectId}/costing/budgets/{budgetId}", new BudgetLineDto(line.Id, line.CostCodeId, line.WbsNodeId, line.ActivityId, line.Description, line.Quantity, line.UnitRate, line.Amount, line.CurrencyCode));
        });

        g.MapDelete("/budgets/{budgetId:guid}/lines/{lineId:guid}", async (Guid projectId, Guid budgetId, Guid lineId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, IAuditWriter audit, CancellationToken ct) =>
        {
            var a = await AuthorizeAsync(ProjectPermission.ManageCost, projectId, db, tenant, user, ct);
            if (a is null) return Results.Forbid();
            var version = await db.BudgetVersions.SingleOrDefaultAsync(x => x.Id == budgetId && x.ProjectId == projectId, ct);
            if (version is null) return Results.NotFound();
            var line = await db.BudgetLines.SingleOrDefaultAsync(l => l.Id == lineId && l.BudgetVersionId == budgetId, ct);
            if (line is null) return Results.NotFound();
            try { version.EnsureEditable(); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
            db.BudgetLines.Remove(line);
            audit.Record("costing.budget.line_removed", "BudgetLine", line.Id, new { line.Description, line.Amount }, null);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Results.Conflict(new { error = "The budget is no longer editable." }); }
            return Results.NoContent();
        });

        g.MapPost("/budgets/{budgetId:guid}/submit", (Guid projectId, Guid budgetId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, IAuditWriter audit, CancellationToken ct)
            => BudgetTransitionAsync(projectId, budgetId, ProjectPermission.ManageCost, "costing.budget.submitted", db, tenant, user, audit, ct,
                async (v, _) => v.SubmitForReview(await db.BudgetLines.CountAsync(l => l.BudgetVersionId == v.Id, ct))));

        g.MapPost("/budgets/{budgetId:guid}/return", (Guid projectId, Guid budgetId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, IAuditWriter audit, CancellationToken ct)
            => BudgetTransitionAsync(projectId, budgetId, ProjectPermission.ApproveCost, "costing.budget.returned", db, tenant, user, audit, ct,
                (v, _) => { v.ReturnToDraft(); return Task.CompletedTask; }));

        g.MapPost("/budgets/{budgetId:guid}/approve", async (Guid projectId, Guid budgetId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, IAuditWriter audit, CancellationToken ct) =>
        {
            var a = await AuthorizeAsync(ProjectPermission.ApproveCost, projectId, db, tenant, user, ct);
            if (a is null) return Results.Forbid();
            if (IsClosed(a.Project)) return Results.Conflict(new { error = "The project is closed." });
            var version = await db.BudgetVersions.SingleOrDefaultAsync(x => x.Id == budgetId && x.ProjectId == projectId, ct);
            if (version is null) return Results.NotFound();
            if (version.Status != BudgetStatus.InReview) return Results.Conflict(new { error = "Only a budget in review can be approved." });
            try
            {
                // One approved budget per project (unique partial index): supersede the current one first, in the same transaction.
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var current = await db.BudgetVersions.Where(x => x.ProjectId == projectId && x.Status == BudgetStatus.Approved && x.Id != budgetId).ToListAsync(ct);
                foreach (var old in current) old.Supersede();
                if (current.Count > 0) await db.SaveChangesAsync(ct);
                version.Approve(a.UserId);
                audit.Record("costing.budget.approved", "BudgetVersion", version.Id, new { status = "InReview" },
                    new { status = "Approved", version.VersionNumber, supersededVersions = current.Select(c => c.VersionNumber) });
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
            catch (DbUpdateException) { return Results.Conflict(new { error = "Concurrent budget change detected. Reload and retry." }); }
            return Results.Ok(new { version.Id, status = version.Status.ToString(), version.ApprovedAt, version.ApprovedBy });
        });

        // ---------------- Cost transactions ----------------
        g.MapGet("/transactions", async (Guid projectId, string? status, string? type, int? skip, int? take, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (await AuthorizeAsync(ProjectPermission.ReadCost, projectId, db, tenant, user, ct) is null) return Results.Forbid();
            var query = db.CostTransactions.AsNoTracking().Where(x => x.ProjectId == projectId);
            if (status is not null)
            {
                if (!TryEnum<CostTransactionStatus>(status, out var s)) return Results.BadRequest(new { error = "Invalid status filter." });
                query = query.Where(x => x.Status == s);
            }
            if (type is not null)
            {
                if (!TryEnum<CostTransactionType>(type, out var t)) return Results.BadRequest(new { error = "Invalid type filter." });
                query = query.Where(x => x.Type == t);
            }
            var total = await query.CountAsync(ct);
            var page = await query.OrderByDescending(x => x.TransactionDate).ThenByDescending(x => x.CreatedAt)
                .Skip(Math.Max(skip ?? 0, 0)).Take(Math.Clamp(take ?? 50, 1, 200)).ToListAsync(ct);
            return Results.Ok(new { total, items = page.Select(x => ToDto(x)).ToList() });
        });

        g.MapPost("/transactions", async (Guid projectId, CostTransactionWrite request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, IAuditWriter audit, CancellationToken ct) =>
        {
            var a = await AuthorizeAsync(ProjectPermission.EnterCost, projectId, db, tenant, user, ct);
            if (a is null) return Results.Forbid();
            if (IsClosed(a.Project)) return Results.Conflict(new { error = "The project is closed." });
            var key = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
            if (key is not null)
            {
                var existing = await db.CostTransactions.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.IdempotencyKey == key, ct);
                if (existing is not null) return Results.Ok(new { idempotentReplay = true, transaction = ToDto(existing) });
            }
            if (!TryEnum<CostTransactionType>(request.Type, out var type)) return Results.BadRequest(new { error = "Type must be Actual, Commitment or Accrual." });
            if (await ValidateReferencesAsync(db, projectId, request.CostCodeId, request.WbsNodeId, request.ActivityId, ct) is { } refError) return Results.BadRequest(new { error = refError });
            CostTransaction entity;
            try
            {
                entity = new CostTransaction(a.TenantId, projectId, type, request.Description, request.TransactionDate, request.Amount,
                    request.CurrencyCode ?? a.Project.CurrencyCode, a.UserId, request.CostCodeId, request.WbsNodeId, request.ActivityId, request.DocumentReference, key);
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            if (entity.CurrencyCode != a.Project.CurrencyCode) return Results.BadRequest(new { error = "MVP supports one currency per project: the transaction currency must equal the project currency." });
            db.CostTransactions.Add(entity);
            audit.Record("costing.transaction.created", "CostTransaction", entity.Id, null, new { projectId, type = type.ToString(), entity.Amount, entity.CurrencyCode, entity.DocumentReference });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                if (key is not null)
                {
                    var raced = await db.CostTransactions.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.IdempotencyKey == key, ct);
                    if (raced is not null) return Results.Ok(new { idempotentReplay = true, transaction = ToDto(raced) });
                }
                return Results.Conflict(new { error = "The transaction could not be saved; a reference is invalid." });
            }
            return Results.Created($"/api/v1/projects/{projectId}/costing/transactions/{entity.Id}", new { idempotentReplay = false, transaction = ToDto(entity) });
        });

        g.MapPut("/transactions/{transactionId:guid}", async (Guid projectId, Guid transactionId, CostTransactionUpdate request, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, IAuditWriter audit, CancellationToken ct) =>
        {
            var a = await AuthorizeAsync(ProjectPermission.EnterCost, projectId, db, tenant, user, ct);
            if (a is null) return Results.Forbid();
            var tx = await db.CostTransactions.SingleOrDefaultAsync(x => x.Id == transactionId && x.ProjectId == projectId, ct);
            if (tx is null) return Results.NotFound();
            if (tx.RowVersion != request.ExpectedRowVersion) return Results.Conflict(new { error = "The transaction was changed by another user. Reload and retry." });
            if (await ValidateReferencesAsync(db, projectId, request.CostCodeId, request.WbsNodeId, request.ActivityId, ct) is { } refError) return Results.BadRequest(new { error = refError });
            try
            {
                var before = new { tx.Description, tx.Amount, tx.TransactionDate, tx.DocumentReference };
                tx.UpdateDraft(request.Description, request.TransactionDate, request.Amount, request.DocumentReference, request.CostCodeId, request.WbsNodeId, request.ActivityId);
                audit.Record("costing.transaction.updated", "CostTransaction", tx.Id, before, new { tx.Description, tx.Amount, tx.TransactionDate, tx.DocumentReference });
                await db.SaveChangesAsync(ct);
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
            catch (DbUpdateException) { return Results.Conflict(new { error = "Concurrent update detected or a reference is invalid." }); }
            return Results.Ok(ToDto(tx));
        });

        g.MapPost("/transactions/{transactionId:guid}/submit", (Guid projectId, Guid transactionId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, IAuditWriter audit, CancellationToken ct)
            => TransactionTransitionAsync(projectId, transactionId, ProjectPermission.EnterCost, "costing.transaction.submitted", db, tenant, user, audit, ct,
                (t, _) => { t.Submit(); return null; }));

        g.MapPost("/transactions/{transactionId:guid}/approve", (Guid projectId, Guid transactionId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, IAuditWriter audit, CancellationToken ct)
            => TransactionTransitionAsync(projectId, transactionId, ProjectPermission.ApproveCost, "costing.transaction.approved", db, tenant, user, audit, ct,
                (t, a) => { t.Approve(a.UserId); return null; }));

        g.MapPost("/transactions/{transactionId:guid}/reject", (Guid projectId, Guid transactionId, ReasonWrite request, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, IAuditWriter audit, CancellationToken ct)
            => TransactionTransitionAsync(projectId, transactionId, ProjectPermission.ApproveCost, "costing.transaction.rejected", db, tenant, user, audit, ct,
                (t, a) => { t.Reject(a.UserId, request.Reason); return null; }));

        g.MapPost("/transactions/{transactionId:guid}/reverse", (Guid projectId, Guid transactionId, ReverseWrite request, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, IAuditWriter audit, CancellationToken ct)
            => TransactionTransitionAsync(projectId, transactionId, ProjectPermission.ApproveCost, "costing.transaction.reversed", db, tenant, user, audit, ct,
                (t, a) => t.Reverse(a.UserId, request.Reason, request.Date ?? Today(a.Project))));

        // ---------------- Summary, EVM, cash flow ----------------
        g.MapGet("/summary", async (Guid projectId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var a = await AuthorizeAsync(ProjectPermission.ReadCost, projectId, db, tenant, user, ct);
            if (a is null) return Results.Forbid();
            var approved = await db.BudgetVersions.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Status == BudgetStatus.Approved, ct);
            var bac = approved is null ? 0m : await db.BudgetLines.Where(l => l.BudgetVersionId == approved.Id).SumAsync(l => l.Amount, ct);
            var counted = await db.CostTransactions.AsNoTracking()
                .Where(x => x.ProjectId == projectId && (x.Status == CostTransactionStatus.Approved || x.Status == CostTransactionStatus.Reversed))
                .Select(x => new { x.Type, x.Amount }).ToListAsync(ct);
            var pending = await db.CostTransactions.CountAsync(x => x.ProjectId == projectId && x.Status == CostTransactionStatus.Submitted, ct);
            var actual = counted.Where(x => x.Type == CostTransactionType.Actual).Sum(x => x.Amount);
            var committed = counted.Where(x => x.Type == CostTransactionType.Commitment).Sum(x => x.Amount);
            var accrued = counted.Where(x => x.Type == CostTransactionType.Accrual).Sum(x => x.Amount);
            return Results.Ok(new
            {
                currency = a.Project.CurrencyCode, approvedBudgetVersion = approved?.VersionNumber, budgetAtCompletion = bac,
                actualCost = actual, committedCost = committed, accruedCost = accrued,
                budgetMinusActual = bac - actual, budgetMinusActualAndCommitted = bac - actual - committed,
                transactionsAwaitingApproval = pending
            });
        });

        g.MapGet("/evm", async (Guid projectId, DateOnly? dataDate, string? method, Guid? baselineId, decimal? bottomUpEtc, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var a = await AuthorizeAsync(ProjectPermission.ReadCost, projectId, db, tenant, user, ct);
            if (a is null) return Results.Forbid();
            var (run, error) = await ComputeEvmAsync(a, projectId, dataDate, method, baselineId, bottomUpEtc, db, ct);
            return error ?? Results.Ok(run!.ToResponse());
        });

        g.MapGet("/evm/snapshots", async (Guid projectId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (await AuthorizeAsync(ProjectPermission.ReadCost, projectId, db, tenant, user, ct) is null) return Results.Forbid();
            var rows = await db.PerformanceSnapshots.AsNoTracking().Where(x => x.ProjectId == projectId).OrderByDescending(x => x.DataDate).ThenByDescending(x => x.CalculatedAt).Take(500)
                .Select(x => new SnapshotDto(x.Id, x.DataDate, x.BaselineId, x.BudgetVersionId, x.Pv, x.Ev, x.Ac, x.Bac, x.Eac, x.Etc, x.Vac, x.Cv, x.Sv, x.Cpi, x.Spi, x.EacMethod, x.BudgetCoverage, x.CalculatedAt)).ToListAsync(ct);
            return Results.Ok(rows);
        });

        g.MapPost("/evm/snapshots", async (Guid projectId, SnapshotWrite request, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, IAuditWriter audit, CancellationToken ct) =>
        {
            var a = await AuthorizeAsync(ProjectPermission.ManageCost, projectId, db, tenant, user, ct);
            if (a is null) return Results.Forbid();
            if (IsClosed(a.Project)) return Results.Conflict(new { error = "The project is closed." });
            var (run, error) = await ComputeEvmAsync(a, projectId, request.DataDate, request.Method, request.BaselineId, request.BottomUpEtc, db, ct);
            if (error is not null) return error;
            var r = run!.Result;
            var snapshot = new PerformanceSnapshot(a.TenantId, projectId, run.DataDate, run.BaselineId, run.BudgetVersionId, r.PlannedValue, r.EarnedValue, r.ActualCost, r.BudgetAtCompletion,
                r.EstimateAtCompletion, r.EstimateToComplete, r.VarianceAtCompletion, r.CostVariance, r.ScheduleVariance, r.CostPerformanceIndex, r.SchedulePerformanceIndex,
                r.EacMethod.ToString(), run.Coverage, a.UserId);
            db.PerformanceSnapshots.Add(snapshot);
            audit.Record("progress.snapshot.captured", "PerformanceSnapshot", snapshot.Id, null, new { projectId, run.DataDate, run.BaselineId, run.Coverage, method = r.EacMethod.ToString() });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return Results.Conflict(new { error = "A snapshot already exists for this data date and baseline. Snapshots are immutable." }); }
            return Results.Created($"/api/v1/projects/{projectId}/costing/evm/snapshots/{snapshot.Id}", run.ToResponse());
        });

        g.MapGet("/cash-flow", async (Guid projectId, Guid? baselineId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var a = await AuthorizeAsync(ProjectPermission.ReadCost, projectId, db, tenant, user, ct);
            if (a is null) return Results.Forbid();
            var (plan, error) = await LoadPlanAsync(projectId, baselineId, db, ct);
            if (error is not null) return error;
            var spread = new List<SpreadItem>();
            var unscheduled = 0m;
            foreach (var group in plan!.Lines.GroupBy(l => l.ActivityId))
            {
                var amount = group.Sum(l => l.Amount);
                if (group.Key is Guid activityId && plan.Dates.TryGetValue(activityId, out var d) && d.Start is DateTimeOffset s && d.Finish is DateTimeOffset f)
                    spread.Add(new SpreadItem(amount, DateOnly.FromDateTime(s.DateTime), DateOnly.FromDateTime(f.DateTime)));
                else unscheduled += amount;
            }
            var actualRows = await db.CostTransactions.AsNoTracking()
                .Where(x => x.ProjectId == projectId && x.Type == CostTransactionType.Actual && (x.Status == CostTransactionStatus.Approved || x.Status == CostTransactionStatus.Reversed))
                .Select(x => new { x.TransactionDate, x.Amount }).ToListAsync(ct);
            return Results.Ok(new
            {
                currency = a.Project.CurrencyCode, baselineId = plan.Baseline.Id, budgetVersionId = plan.Budget.Id,
                basis = "Budget spread linearly over baseline calendar days; actual = approved Actual transactions by transaction date.",
                unscheduledBudget = unscheduled,
                planned = CashFlowCurveBuilder.BuildMonthly(spread),
                actual = CashFlowCurveBuilder.BucketDated(actualRows.Select(x => (x.TransactionDate, x.Amount)))
            });
        });

        return app;
    }

    // ---------------- Shared helpers ----------------

    private static async Task<Auth?> AuthorizeAsync(ProjectPermission permission, Guid projectId, ProjexDbContext db, ITenantContext tenant, ClaimsPrincipal user, CancellationToken ct)
    {
        if (tenant.TenantId is not Guid tid || tenant.UserId is not Guid uid) return null;
        if (!await ProjectAuthorizer.HasAsync(permission, projectId, db, tenant, user, ct)) return null;
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, ct);
        return project is null ? null : new Auth(tid, uid, project);
    }

    private static bool IsClosed(Project p) => p.Status is ProjectStatus.Completed or ProjectStatus.Cancelled or ProjectStatus.Archived;

    private static bool TryEnum<T>(string? value, out T result) where T : struct, Enum
        => Enum.TryParse(value, true, out result) && Enum.IsDefined(result) && !int.TryParse(value, out _);

    private static TimeZoneInfo ZoneOf(Project p)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(p.TimeZone); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }

    private static DateOnly Today(Project p) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ZoneOf(p)));

    private static async Task<string?> ValidateReferencesAsync(ProjexDbContext db, Guid projectId, Guid? costCodeId, Guid? wbsNodeId, Guid? activityId, CancellationToken ct)
    {
        if (costCodeId is Guid c && !await db.CostCodes.AnyAsync(x => x.Id == c && x.ProjectId == projectId, ct)) return "Cost code must belong to this project.";
        if (wbsNodeId is Guid w && !await db.WbsNodes.AnyAsync(x => x.Id == w && x.ProjectId == projectId, ct)) return "WBS node must belong to this project.";
        if (activityId is Guid act && !await db.Activities.AnyAsync(x => x.Id == act && x.ProjectId == projectId, ct)) return "Activity must belong to this project.";
        return null;
    }

    private static BoqItemDto ToDto(BoqItem x) => new(x.Id, x.CostCodeId, x.WbsNodeId, x.ItemCode, x.Description, x.Unit, x.Quantity, x.UnitRate, x.Amount, x.CurrencyCode, x.SortOrder);

    private static CostTransactionDto ToDto(CostTransaction x) => new(x.Id, x.Type.ToString(), x.IsReversal, x.ReversesTransactionId, x.Description, x.DocumentReference, x.TransactionDate,
        x.Amount, x.CurrencyCode, x.Status.ToString(), x.CostCodeId, x.WbsNodeId, x.ActivityId, x.CreatedBy, x.ApprovedBy, x.ApprovedAt, x.RejectionReason, x.RowVersion);

    private static async Task<IResult> BudgetTransitionAsync(Guid projectId, Guid budgetId, ProjectPermission permission, string auditAction, ProjexDbContext db, ITenantContext tenant,
        ClaimsPrincipal user, IAuditWriter audit, CancellationToken ct, Func<BudgetVersion, Auth, Task> apply)
    {
        var a = await AuthorizeAsync(permission, projectId, db, tenant, user, ct);
        if (a is null) return Results.Forbid();
        if (IsClosed(a.Project)) return Results.Conflict(new { error = "The project is closed." });
        var version = await db.BudgetVersions.SingleOrDefaultAsync(x => x.Id == budgetId && x.ProjectId == projectId, ct);
        if (version is null) return Results.NotFound();
        var before = new { status = version.Status.ToString() };
        try
        {
            await apply(version, a);
            audit.Record(auditAction, "BudgetVersion", version.Id, before, new { status = version.Status.ToString(), version.VersionNumber });
            await db.SaveChangesAsync(ct);
        }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (DbUpdateException) { return Results.Conflict(new { error = "Concurrent budget change detected. Reload and retry." }); }
        return Results.Ok(new { version.Id, status = version.Status.ToString(), version.RowVersion });
    }

    private static async Task<IResult> TransactionTransitionAsync(Guid projectId, Guid transactionId, ProjectPermission permission, string auditAction, ProjexDbContext db, ITenantContext tenant,
        ClaimsPrincipal user, IAuditWriter audit, CancellationToken ct, Func<CostTransaction, Auth, CostTransaction?> apply)
    {
        var a = await AuthorizeAsync(permission, projectId, db, tenant, user, ct);
        if (a is null) return Results.Forbid();
        if (IsClosed(a.Project)) return Results.Conflict(new { error = "The project is closed." });
        var tx = await db.CostTransactions.SingleOrDefaultAsync(x => x.Id == transactionId && x.ProjectId == projectId, ct);
        if (tx is null) return Results.NotFound();
        var before = new { status = tx.Status.ToString() };
        CostTransaction? created;
        try
        {
            created = apply(tx, a);
            if (created is not null) db.CostTransactions.Add(created);
            audit.Record(auditAction, "CostTransaction", tx.Id, before, new { status = tx.Status.ToString(), tx.Amount, reversalId = created?.Id });
            await db.SaveChangesAsync(ct);
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (DbUpdateException) { return Results.Conflict(new { error = "Concurrent change detected or the transaction is already settled. Reload and retry." }); }
        return Results.Ok(new { transaction = ToDto(tx), reversal = created is null ? null : ToDto(created) });
    }

    // ---------------- Plan data shared by EVM and cash flow ----------------

    private sealed record PlanData(BudgetVersion Budget, ScheduleBaseline Baseline, decimal Bac,
        IReadOnlyList<(Guid? ActivityId, decimal Amount)> Lines, IReadOnlyDictionary<Guid, (DateTimeOffset? Start, DateTimeOffset? Finish)> Dates);

    private static async Task<(PlanData? Plan, IResult? Error)> LoadPlanAsync(Guid projectId, Guid? baselineId, ProjexDbContext db, CancellationToken ct)
    {
        var budget = await db.BudgetVersions.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Status == BudgetStatus.Approved, ct);
        if (budget is null) return (null, Results.Conflict(new { error = "An approved budget is required.", code = "NO_APPROVED_BUDGET" }));

        ScheduleBaseline? baseline;
        if (baselineId is Guid requested)
        {
            baseline = await db.ScheduleBaselines.AsNoTracking().SingleOrDefaultAsync(x => x.Id == requested && x.ProjectId == projectId, ct);
            if (baseline is null) return (null, Results.NotFound(new { error = "Baseline not found." }));
        }
        else baseline = await db.ScheduleBaselines.AsNoTracking().Where(x => x.ProjectId == projectId && x.Status == BaselineStatus.Approved).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
        if (baseline is null || baseline.Status != BaselineStatus.Approved)
            return (null, Results.Conflict(new { error = "An approved schedule baseline is required.", code = "NO_APPROVED_BASELINE" }));

        var lines = (await db.BudgetLines.AsNoTracking().Where(l => l.BudgetVersionId == budget.Id).Select(l => new { l.ActivityId, l.Amount }).ToListAsync(ct))
            .Select(l => (ActivityId: l.ActivityId, Amount: l.Amount)).ToList();
        var dates = (await db.BaselineActivityDates.AsNoTracking().Where(d => d.BaselineId == baseline.Id).Select(d => new { d.ActivityId, d.PlannedStart, d.PlannedFinish }).ToListAsync(ct))
            .ToDictionary(d => d.ActivityId, d => (Start: d.PlannedStart, Finish: d.PlannedFinish));
        return (new PlanData(budget, baseline, lines.Sum(l => l.Amount), lines, dates), null);
    }

    private sealed record EvmRun(EvmResult Result, DateOnly DataDate, Guid BaselineId, Guid BudgetVersionId, decimal Coverage, string Currency, IReadOnlyList<string> Warnings)
    {
        public object ToResponse() => new
        {
            dataDate = DataDate, currency = Currency, baselineId = BaselineId, budgetVersionId = BudgetVersionId, budgetCoverage = Coverage, isReliable = Coverage == 1m,
            eacMethod = Result.EacMethod.ToString(), plannedValue = Result.PlannedValue, earnedValue = Result.EarnedValue, actualCost = Result.ActualCost, budgetAtCompletion = Result.BudgetAtCompletion,
            costVariance = Result.CostVariance, scheduleVariance = Result.ScheduleVariance, costPerformanceIndex = Result.CostPerformanceIndex, schedulePerformanceIndex = Result.SchedulePerformanceIndex,
            estimateAtCompletion = Result.EstimateAtCompletion, estimateToComplete = Result.EstimateToComplete, varianceAtCompletion = Result.VarianceAtCompletion,
            warnings = Warnings.Concat(Result.Warnings).ToList()
        };
    }

    private static async Task<(EvmRun? Run, IResult? Error)> ComputeEvmAsync(Auth a, Guid projectId, DateOnly? dataDateInput, string? methodInput, Guid? baselineId, decimal? bottomUpEtc,
        ProjexDbContext db, CancellationToken ct)
    {
        var method = EacMethod.CostPerformance;
        if (methodInput is not null && !TryEnum(methodInput, out method))
            return (null, Results.BadRequest(new { error = "Method must be CostPerformance, CostAndSchedulePerformance or BottomUp." }));

        var (plan, error) = await LoadPlanAsync(projectId, baselineId, db, ct);
        if (error is not null) return (null, error);

        var today = Today(a.Project);
        var dataDate = dataDateInput ?? today;
        var zone = ZoneOf(a.Project);
        var cutoffLocal = dataDate.AddDays(1).ToDateTime(TimeOnly.MinValue);              // end of the data date, project-local
        var cutoff = new DateTimeOffset(cutoffLocal, zone.GetUtcOffset(cutoffLocal));

        var activityIds = plan!.Lines.Where(l => l.ActivityId.HasValue).Select(l => l.ActivityId!.Value).Distinct().ToList();
        var currentPercents = (await db.Activities.AsNoTracking().Where(x => x.ProjectId == projectId && activityIds.Contains(x.Id)).Select(x => new { x.Id, x.PercentComplete }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.PercentComplete);
        var history = await db.ProgressUpdates.AsNoTracking().Where(x => x.ProjectId == projectId && activityIds.Contains(x.ActivityId))
            .Select(x => new { x.ActivityId, x.DataDate, x.CreatedAt, x.PercentComplete }).ToListAsync(ct);
        var percents = new Dictionary<Guid, decimal>();
        var usedFallback = false;
        foreach (var id in activityIds)
        {
            var own = history.Where(h => h.ActivityId == id).Select(h => (h.DataDate, h.CreatedAt, h.PercentComplete)).ToList();
            var asOf = ProgressHistory.PercentAsOf(own, dataDate);
            if (asOf is decimal known) percents[id] = known;
            else if (own.Count > 0) percents[id] = 0m;                                  // history exists but nothing recorded yet by that date
            else if (currentPercents.TryGetValue(id, out var current)) { percents[id] = current; usedFallback = true; }
        }

        var work = new List<BudgetedWork>();
        var skippedActivities = 0;
        foreach (var group in plan.Lines.Where(l => l.ActivityId.HasValue).GroupBy(l => l.ActivityId!.Value))
        {
            if (plan.Dates.TryGetValue(group.Key, out var d) && d.Start is DateTimeOffset s && d.Finish is DateTimeOffset f && percents.TryGetValue(group.Key, out var pct))
                work.Add(new BudgetedWork(group.Key.ToString(), group.Sum(l => l.Amount), s, f, pct));
            else skippedActivities++;
        }

        var linked = work.Sum(w => w.Budget);
        var coverage = plan.Bac == 0m ? 0m : Math.Round(linked / plan.Bac, 6);
        var warnings = new List<string>();
        if (coverage < 1m) warnings.Add("BUDGET_NOT_FULLY_LINKED_TO_BASELINED_ACTIVITIES: PV/EV cover only part of BAC, so indices are partial.");
        if (skippedActivities > 0) warnings.Add($"ACTIVITIES_NOT_IN_BASELINE_OR_WITHOUT_DATES: {skippedActivities}");
        if (usedFallback && dataDate != today) warnings.Add("PROGRESS_HISTORY_MISSING_USING_CURRENT_PERCENT: some activities have no recorded progress history, so EV uses their current percent complete.");

        var actualRows = await db.CostTransactions.AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.Type == CostTransactionType.Actual && x.TransactionDate <= dataDate
                        && (x.Status == CostTransactionStatus.Approved || x.Status == CostTransactionStatus.Reversed))
            .Select(x => x.Amount).ToListAsync(ct);

        try
        {
            var input = new EvmInput(Money.Round(PlannedValueCalculator.PlannedValue(work, cutoff)), Money.Round(PlannedValueCalculator.EarnedValue(work)), Money.Round(actualRows.Sum()), Money.Round(plan.Bac));
            var result = new EvmCalculator().Calculate(input, method, bottomUpEtc);
            return (new EvmRun(result, dataDate, plan.Baseline.Id, plan.Budget.Id, coverage, a.Project.CurrencyCode, warnings), null);
        }
        catch (ArgumentException ex) { return (null, Results.BadRequest(new { error = ex.Message })); }
    }
}

public sealed record CostCodeWrite(string Code, string Name, string Category, Guid? ParentId);
public sealed record CostCodeDto(Guid Id, Guid? ParentId, string Code, string Name, string Category, bool IsActive);
public sealed record BoqItemWrite(string ItemCode, string Description, string Unit, decimal Quantity, decimal UnitRate, string? CurrencyCode, int SortOrder = 0, Guid? CostCodeId = null, Guid? WbsNodeId = null);
public sealed record BoqItemDto(Guid Id, Guid? CostCodeId, Guid? WbsNodeId, string ItemCode, string Description, string Unit, decimal Quantity, decimal UnitRate, decimal Amount, string CurrencyCode, int SortOrder);
public sealed record BudgetVersionWrite(string Type, DateOnly EffectiveDate);
public sealed record BudgetVersionDto(Guid Id, int VersionNumber, string Type, string Status, DateOnly EffectiveDate, int LineCount, decimal Total, Guid CreatedBy, Guid? ApprovedBy, DateTimeOffset? ApprovedAt, long RowVersion);
public sealed record BudgetLineWrite(string Description, decimal? Quantity, decimal? UnitRate, decimal? Amount, string? CurrencyCode, Guid? CostCodeId, Guid? WbsNodeId, Guid? ActivityId);
public sealed record BudgetLineDto(Guid Id, Guid? CostCodeId, Guid? WbsNodeId, Guid? ActivityId, string Description, decimal? Quantity, decimal? UnitRate, decimal Amount, string CurrencyCode);
public sealed record CostTransactionWrite(string Type, string Description, DateOnly TransactionDate, decimal Amount, string? CurrencyCode, Guid? CostCodeId, Guid? WbsNodeId, Guid? ActivityId, string? DocumentReference);
public sealed record CostTransactionUpdate(string Description, DateOnly TransactionDate, decimal Amount, string? DocumentReference, Guid? CostCodeId, Guid? WbsNodeId, Guid? ActivityId, long ExpectedRowVersion);
public sealed record CostTransactionDto(Guid Id, string Type, bool IsReversal, Guid? ReversesTransactionId, string Description, string? DocumentReference, DateOnly TransactionDate, decimal Amount, string CurrencyCode,
    string Status, Guid? CostCodeId, Guid? WbsNodeId, Guid? ActivityId, Guid CreatedBy, Guid? ApprovedBy, DateTimeOffset? ApprovedAt, string? RejectionReason, long RowVersion);
public sealed record ReasonWrite(string Reason);
public sealed record ReverseWrite(string Reason, DateOnly? Date);
public sealed record SnapshotWrite(DateOnly? DataDate, string? Method, Guid? BaselineId, decimal? BottomUpEtc);
public sealed record SnapshotDto(Guid Id, DateOnly DataDate, Guid? BaselineId, Guid? BudgetVersionId, decimal Pv, decimal Ev, decimal Ac, decimal Bac, decimal? Eac, decimal? Etc, decimal? Vac, decimal Cv, decimal Sv,
    decimal? Cpi, decimal? Spi, string EacMethod, decimal BudgetCoverage, DateTimeOffset CalculatedAt);
