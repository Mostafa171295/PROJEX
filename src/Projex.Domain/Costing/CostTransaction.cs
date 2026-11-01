using Projex.Domain.Common;

namespace Projex.Domain.Costing;

/// <summary>Kind of cost. A correction keeps the kind of the transaction it reverses (see <see cref="CostTransaction.IsReversal"/>).</summary>
public enum CostTransactionType { Actual, Commitment, Accrual }
public enum CostTransactionStatus { Draft, Submitted, Approved, Rejected, Reversed }

/// <summary>
/// Budget (plan) and transactions (what happened) are separate tables (SRS 9.2).
/// Once approved, a transaction is never edited: it is corrected by a reversing entry that points back to it
/// (SRS FR-COST-009). The only permitted change to an approved row is Approved -> Reversed.
/// </summary>
public sealed class CostTransaction : Entity
{
    private CostTransaction() { }
    public CostTransaction(Guid tenantId, Guid projectId, CostTransactionType type, string description, DateOnly transactionDate,
        decimal amount, string currencyCode, Guid createdBy, Guid? costCodeId = null, Guid? wbsNodeId = null, Guid? activityId = null,
        string? documentReference = null, string? idempotencyKey = null)
    {
        if (tenantId == Guid.Empty || projectId == Guid.Empty || createdBy == Guid.Empty) throw new ArgumentException("Tenant, project and creator are required.");
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be positive; use a reversal to correct an approved transaction.");
        TenantId = tenantId; ProjectId = projectId; Type = type; TransactionDate = transactionDate; Amount = Money.Round(amount);
        CurrencyCode = Money.NormalizeCurrency(currencyCode); CreatedBy = createdBy; CostCodeId = costCodeId; WbsNodeId = wbsNodeId; ActivityId = activityId;
        Description = Required(description, 1000, nameof(description)); DocumentReference = Optional(documentReference, 100); IdempotencyKey = Optional(idempotencyKey, 100);
        Status = CostTransactionStatus.Draft;
    }
    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid? CostCodeId { get; private set; }
    public Guid? WbsNodeId { get; private set; }
    public Guid? ActivityId { get; private set; }
    public CostTransactionType Type { get; private set; }
    public bool IsReversal { get; private set; }
    public Guid? ReversesTransactionId { get; private set; }
    public string? DocumentReference { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public DateOnly TransactionDate { get; private set; }
    /// <summary>Positive for ordinary transactions, negative for reversals. Sum amounts to net a ledger.</summary>
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public CostTransactionStatus Status { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string? RejectionReason { get; private set; }
    public string? ReversalReason { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public long RowVersion { get; private set; }

    /// <summary>True for rows that count toward totals: approved, or reversed originals (offset by their approved reversal row).</summary>
    public bool CountsTowardTotals => Status is CostTransactionStatus.Approved or CostTransactionStatus.Reversed;

    public void UpdateDraft(string description, DateOnly transactionDate, decimal amount, string? documentReference, Guid? costCodeId, Guid? wbsNodeId, Guid? activityId)
    {
        if (Status != CostTransactionStatus.Draft) throw new InvalidOperationException("Only a draft transaction can be edited; correct an approved one with a reversal.");
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        Description = Required(description, 1000, nameof(description)); TransactionDate = transactionDate; Amount = Money.Round(amount);
        DocumentReference = Optional(documentReference, 100); CostCodeId = costCodeId; WbsNodeId = wbsNodeId; ActivityId = activityId; RowVersion++; Touch();
    }
    public void Submit() { if (Status != CostTransactionStatus.Draft) throw new InvalidOperationException("Only a draft transaction can be submitted."); Status = CostTransactionStatus.Submitted; RowVersion++; Touch(); }
    public void Approve(Guid userId)
    {
        if (Status != CostTransactionStatus.Submitted) throw new InvalidOperationException("Only a submitted transaction can be approved.");
        if (userId == Guid.Empty) throw new ArgumentException("Approver is required.");
        if (userId == CreatedBy) throw new InvalidOperationException("Segregation of duties: the creator cannot approve their own transaction.");
        Status = CostTransactionStatus.Approved; ApprovedBy = userId; ApprovedAt = DateTimeOffset.UtcNow; RowVersion++; Touch();
    }
    public void Reject(Guid userId, string reason)
    {
        if (Status != CostTransactionStatus.Submitted) throw new InvalidOperationException("Only a submitted transaction can be rejected.");
        if (userId == Guid.Empty) throw new ArgumentException("Reviewer is required.");
        RejectionReason = Required(reason, 500, nameof(reason)); Status = CostTransactionStatus.Rejected; RowVersion++; Touch();
    }

    /// <summary>Marks this approved transaction Reversed and returns the offsetting, already-approved reversal entry.</summary>
    public CostTransaction Reverse(Guid userId, string reason, DateOnly reversalDate)
    {
        if (Status != CostTransactionStatus.Approved) throw new InvalidOperationException("Only an approved transaction can be reversed.");
        if (IsReversal) throw new InvalidOperationException("A reversal entry cannot itself be reversed; post a new transaction instead.");
        if (userId == Guid.Empty) throw new ArgumentException("User is required.");
        var why = Required(reason, 500, nameof(reason));
        Status = CostTransactionStatus.Reversed; ReversalReason = why; RowVersion++; Touch();
        return new CostTransaction
        {
            TenantId = TenantId, ProjectId = ProjectId, CostCodeId = CostCodeId, WbsNodeId = WbsNodeId, ActivityId = ActivityId,
            Type = Type, IsReversal = true, ReversesTransactionId = Id, DocumentReference = DocumentReference,
            Description = $"Reversal: {why}", TransactionDate = reversalDate, Amount = -Amount, CurrencyCode = CurrencyCode,
            Status = CostTransactionStatus.Approved, CreatedBy = userId, ApprovedBy = userId, ApprovedAt = DateTimeOffset.UtcNow, ReversalReason = why
        };
    }
    private static string Required(string value, int max, string arg) { if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > max) throw new ArgumentException($"{arg} is required and must not exceed {max} characters.", arg); return value.Trim(); }
    private static string? Optional(string? value, int max) { if (string.IsNullOrWhiteSpace(value)) return null; if (value.Trim().Length > max) throw new ArgumentException($"Value must not exceed {max} characters."); return value.Trim(); }
}
