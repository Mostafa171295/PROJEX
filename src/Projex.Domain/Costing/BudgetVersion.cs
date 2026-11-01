using Projex.Domain.Common;

namespace Projex.Domain.Costing;

public enum BudgetType { Original, Revised, Forecast }
public enum BudgetStatus { Draft, InReview, Approved, Superseded }

/// <summary>A versioned budget. Lines can change only while Draft; an approved version is immutable (ERD rule 7).</summary>
public sealed class BudgetVersion : Entity
{
    private BudgetVersion() { }
    public BudgetVersion(Guid tenantId, Guid projectId, int versionNumber, BudgetType type, DateOnly effectiveDate, Guid createdBy)
    {
        if (tenantId == Guid.Empty || projectId == Guid.Empty || createdBy == Guid.Empty) throw new ArgumentException("Tenant, project and creator are required.");
        if (versionNumber < 1) throw new ArgumentOutOfRangeException(nameof(versionNumber));
        TenantId = tenantId; ProjectId = projectId; VersionNumber = versionNumber; Type = type; EffectiveDate = effectiveDate; CreatedBy = createdBy; Status = BudgetStatus.Draft;
    }
    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public int VersionNumber { get; private set; }
    public BudgetType Type { get; private set; }
    public BudgetStatus Status { get; private set; }
    public DateOnly EffectiveDate { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public long RowVersion { get; private set; }

    public void EnsureEditable() { if (Status != BudgetStatus.Draft) throw new InvalidOperationException("Only a draft budget version can be changed."); }
    public void SubmitForReview(int lineCount)
    {
        if (Status != BudgetStatus.Draft) throw new InvalidOperationException("Only a draft budget can be submitted.");
        if (lineCount < 1) throw new InvalidOperationException("A budget needs at least one line before review.");
        Status = BudgetStatus.InReview; RowVersion++; Touch();
    }
    public void ReturnToDraft() { if (Status != BudgetStatus.InReview) throw new InvalidOperationException("Only a budget in review can be returned to draft."); Status = BudgetStatus.Draft; RowVersion++; Touch(); }
    public void Approve(Guid userId)
    {
        if (Status != BudgetStatus.InReview) throw new InvalidOperationException("Only a budget in review can be approved.");
        if (userId == Guid.Empty) throw new ArgumentException("Approver is required.");
        if (userId == CreatedBy) throw new InvalidOperationException("Segregation of duties: the creator cannot approve their own budget.");
        Status = BudgetStatus.Approved; ApprovedBy = userId; ApprovedAt = DateTimeOffset.UtcNow; RowVersion++; Touch();
    }
    public void Supersede() { if (Status != BudgetStatus.Approved) throw new InvalidOperationException("Only an approved budget can be superseded."); Status = BudgetStatus.Superseded; RowVersion++; Touch(); }
}

public sealed class BudgetLine : Entity
{
    private BudgetLine() { }
    private BudgetLine(Guid tenantId, Guid budgetVersionId, string description, decimal amount, string currencyCode,
        decimal? quantity, decimal? unitRate, Guid? costCodeId, Guid? wbsNodeId, Guid? activityId)
    {
        if (tenantId == Guid.Empty || budgetVersionId == Guid.Empty) throw new ArgumentException("Tenant and budget version are required.");
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "Budget amounts must not be negative.");
        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 250) throw new ArgumentException("Description is required and must not exceed 250 characters.", nameof(description));
        TenantId = tenantId; BudgetVersionId = budgetVersionId; Description = description.Trim(); Amount = Money.Round(amount); CurrencyCode = Money.NormalizeCurrency(currencyCode);
        Quantity = quantity; UnitRate = unitRate; CostCodeId = costCodeId; WbsNodeId = wbsNodeId; ActivityId = activityId;
    }
    public static BudgetLine FromQuantity(Guid tenantId, Guid budgetVersionId, string description, decimal quantity, decimal unitRate, string currencyCode,
        Guid? costCodeId = null, Guid? wbsNodeId = null, Guid? activityId = null)
    {
        if (quantity < 0 || unitRate < 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity and rate must not be negative.");
        return new(tenantId, budgetVersionId, description, Money.Extend(quantity, unitRate), currencyCode, quantity, unitRate, costCodeId, wbsNodeId, activityId);
    }
    public static BudgetLine LumpSum(Guid tenantId, Guid budgetVersionId, string description, decimal amount, string currencyCode,
        Guid? costCodeId = null, Guid? wbsNodeId = null, Guid? activityId = null)
        => new(tenantId, budgetVersionId, description, amount, currencyCode, null, null, costCodeId, wbsNodeId, activityId);

    public Guid TenantId { get; private set; }
    public Guid BudgetVersionId { get; private set; }
    public Guid? CostCodeId { get; private set; }
    public Guid? WbsNodeId { get; private set; }
    public Guid? ActivityId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public decimal? Quantity { get; private set; }
    public decimal? UnitRate { get; private set; }
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
}
