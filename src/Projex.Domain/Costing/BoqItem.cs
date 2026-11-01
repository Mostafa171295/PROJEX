using Projex.Domain.Common;

namespace Projex.Domain.Costing;

public sealed class BoqItem : Entity
{
    private BoqItem() { }
    public BoqItem(Guid tenantId, Guid projectId, string itemCode, string description, string unit, decimal quantity, decimal unitRate,
        string currencyCode, int sortOrder = 0, Guid? costCodeId = null, Guid? wbsNodeId = null)
    {
        if (tenantId == Guid.Empty || projectId == Guid.Empty) throw new ArgumentException("Tenant and project are required.");
        if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (unitRate < 0) throw new ArgumentOutOfRangeException(nameof(unitRate));
        TenantId = tenantId; ProjectId = projectId; CostCodeId = costCodeId; WbsNodeId = wbsNodeId; SortOrder = sortOrder;
        ItemCode = Required(itemCode, 80, nameof(itemCode)); Description = Required(description, 2000, nameof(description)); Unit = Required(unit, 30, nameof(unit));
        Quantity = quantity; UnitRate = unitRate; CurrencyCode = Money.NormalizeCurrency(currencyCode);
    }
    public Guid TenantId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid? CostCodeId { get; private set; }
    public Guid? WbsNodeId { get; private set; }
    public string ItemCode { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Unit { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal UnitRate { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }
    /// <summary>Derived, never persisted: one calculation rule for every consumer.</summary>
    public decimal Amount => Money.Extend(Quantity, UnitRate);
    private static string Required(string value, int max, string arg) { if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > max) throw new ArgumentException($"{arg} is required and must not exceed {max} characters.", arg); return value.Trim(); }
}
