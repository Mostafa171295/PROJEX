using Projex.Domain.Costing;

namespace Projex.Domain.Tests;

public sealed class BudgetVersionTests
{
    private static readonly Guid Tenant = Guid.NewGuid(), Project = Guid.NewGuid(), User = Guid.NewGuid(), Approver = Guid.NewGuid();
    private static BudgetVersion NewVersion() => new(Tenant, Project, 1, BudgetType.Original, new DateOnly(2026, 11, 1), User);

    [Fact]
    public void Approval_requires_review_and_at_least_one_line()
    {
        var v = NewVersion();
        Assert.Throws<InvalidOperationException>(() => v.SubmitForReview(0));
        Assert.Throws<InvalidOperationException>(() => v.Approve(Approver));      // not in review yet
        v.SubmitForReview(3);
        v.Approve(Approver);
        Assert.Equal(BudgetStatus.Approved, v.Status);
        Assert.NotNull(v.ApprovedAt);
    }

    [Fact]
    public void Approved_version_is_immutable_until_superseded()
    {
        var v = NewVersion(); v.SubmitForReview(1); v.Approve(Approver);
        Assert.Throws<InvalidOperationException>(() => v.EnsureEditable());
        Assert.Throws<InvalidOperationException>(() => v.ReturnToDraft());
        v.Supersede();
        Assert.Equal(BudgetStatus.Superseded, v.Status);
        Assert.Throws<InvalidOperationException>(() => v.EnsureEditable());
    }

    [Fact]
    public void Creator_cannot_approve_their_own_budget()
    {
        var v = NewVersion(); v.SubmitForReview(1);
        Assert.Throws<InvalidOperationException>(() => v.Approve(User));
    }

    [Fact]
    public void Review_can_return_to_draft_for_changes()
    {
        var v = NewVersion(); v.SubmitForReview(1); v.ReturnToDraft();
        v.EnsureEditable();
    }

    [Fact]
    public void Quantity_line_computes_amount_with_four_decimal_rounding()
    {
        var line = BudgetLine.FromQuantity(Tenant, Guid.NewGuid(), "Rebar", 12.5m, 3.33333m, "usd");
        Assert.Equal(41.6666m, line.Amount);   // 12.5 * 3.33333 = 41.666625 -> 41.6666 (round-half-even to 4 dp)
        Assert.Equal("USD", line.CurrencyCode);
    }

    [Fact]
    public void Lump_sum_line_has_no_quantity_or_rate_and_rejects_negatives()
    {
        var line = BudgetLine.LumpSum(Tenant, Guid.NewGuid(), "Mobilisation", 5000m, "USD");
        Assert.Null(line.Quantity);
        Assert.Null(line.UnitRate);
        Assert.Throws<ArgumentOutOfRangeException>(() => BudgetLine.LumpSum(Tenant, Guid.NewGuid(), "Bad", -1m, "USD"));
    }

    [Fact]
    public void Boq_amount_is_derived_from_quantity_and_rate()
    {
        var item = new BoqItem(Tenant, Project, "1.01", "Excavation", "m3", 250m, 12.5m, "USD");
        Assert.Equal(3125m, item.Amount);
    }
}
