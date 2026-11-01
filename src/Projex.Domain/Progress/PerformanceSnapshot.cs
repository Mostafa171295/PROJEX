using Projex.Domain.Common;

namespace Projex.Domain.Progress;

/// <summary>Immutable record of EVM indicators at a data date (SRS FR-EVM-006). Never updated after insert.</summary>
public sealed class PerformanceSnapshot : Entity
{
    private PerformanceSnapshot() { }
    public PerformanceSnapshot(Guid tenantId, Guid projectId, DateOnly dataDate, Guid? baselineId, Guid? budgetVersionId,
        decimal pv, decimal ev, decimal ac, decimal bac, decimal? eac, decimal? etc, decimal? vac, decimal cv, decimal sv,
        decimal? cpi, decimal? spi, string eacMethod, decimal budgetCoverage, Guid calculatedBy)
    {
        if (tenantId == Guid.Empty || projectId == Guid.Empty || calculatedBy == Guid.Empty) throw new ArgumentException("Tenant, project and user are required.");
        if (budgetCoverage is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(budgetCoverage));
        TenantId = tenantId; ProjectId = projectId; DataDate = dataDate; BaselineId = baselineId; BudgetVersionId = budgetVersionId;
        Pv = pv; Ev = ev; Ac = ac; Bac = bac; Eac = eac; Etc = etc; Vac = vac; Cv = cv; Sv = sv; Cpi = cpi; Spi = spi;
        EacMethod = eacMethod; BudgetCoverage = budgetCoverage; CalculatedBy = calculatedBy; CalculatedAt = DateTimeOffset.UtcNow;
    }
    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public DateOnly DataDate { get; private set; }
    public Guid? BaselineId { get; private set; }
    public Guid? BudgetVersionId { get; private set; }
    public decimal Pv { get; private set; }
    public decimal Ev { get; private set; }
    public decimal Ac { get; private set; }
    public decimal Bac { get; private set; }
    public decimal? Eac { get; private set; }
    public decimal? Etc { get; private set; }
    public decimal? Vac { get; private set; }
    public decimal Cv { get; private set; }
    public decimal Sv { get; private set; }
    public decimal? Cpi { get; private set; }
    public decimal? Spi { get; private set; }
    public string EacMethod { get; private set; } = string.Empty;
    /// <summary>Share of BAC that is linked to activities and therefore contributes to PV/EV (0..1). Below 1, indices are partial.</summary>
    public decimal BudgetCoverage { get; private set; }
    public Guid CalculatedBy { get; private set; }
    public DateTimeOffset CalculatedAt { get; private set; }
}
