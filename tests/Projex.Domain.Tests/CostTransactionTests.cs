using Projex.Domain.Costing;

namespace Projex.Domain.Tests;

public sealed class CostTransactionTests
{
    private static readonly Guid Tenant = Guid.NewGuid(), Project = Guid.NewGuid(), Creator = Guid.NewGuid(), Approver = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 11, 1);

    private static CostTransaction NewDraft(decimal amount = 100m, CostTransactionType type = CostTransactionType.Actual)
        => new(Tenant, Project, type, "Concrete delivery", Day, amount, "usd", Creator, documentReference: "INV-1");

    private static CostTransaction Approved()
    {
        var t = NewDraft(); t.Submit(); t.Approve(Approver); return t;
    }

    [Fact]
    public void Normalizes_currency_and_rounds_amount_to_four_decimals()
    {
        var t = NewDraft(10.123456m);
        Assert.Equal("USD", t.CurrencyCode);
        Assert.Equal(10.1235m, t.Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Ordinary_transactions_must_be_positive(int amount)
        => Assert.Throws<ArgumentOutOfRangeException>(() => NewDraft(amount));

    [Fact]
    public void Follows_draft_submit_approve_workflow()
    {
        var t = NewDraft();
        Assert.False(t.CountsTowardTotals);
        t.Submit(); Assert.Equal(CostTransactionStatus.Submitted, t.Status);
        t.Approve(Approver);
        Assert.Equal(CostTransactionStatus.Approved, t.Status);
        Assert.Equal(Approver, t.ApprovedBy);
        Assert.True(t.CountsTowardTotals);
    }

    [Fact]
    public void Creator_cannot_approve_their_own_transaction()
    {
        var t = NewDraft(); t.Submit();
        Assert.Throws<InvalidOperationException>(() => t.Approve(Creator));
    }

    [Fact]
    public void Cannot_approve_without_submitting()
        => Assert.Throws<InvalidOperationException>(() => NewDraft().Approve(Approver));

    [Fact]
    public void Rejection_requires_a_reason_and_does_not_count()
    {
        var t = NewDraft(); t.Submit();
        Assert.Throws<ArgumentException>(() => t.Reject(Approver, " "));
        t.Reject(Approver, "Missing invoice");
        Assert.Equal(CostTransactionStatus.Rejected, t.Status);
        Assert.False(t.CountsTowardTotals);
    }

    [Fact]
    public void Approved_transaction_cannot_be_edited()
        => Assert.Throws<InvalidOperationException>(() => Approved().UpdateDraft("x", Day, 5m, null, null, null, null));

    [Fact]
    public void Draft_can_be_edited_and_bumps_row_version()
    {
        var t = NewDraft(); var before = t.RowVersion;
        t.UpdateDraft("Revised", Day.AddDays(1), 250m, "INV-2", null, null, null);
        Assert.Equal(250m, t.Amount);
        Assert.True(t.RowVersion > before);
    }

    [Fact]
    public void Reversal_offsets_the_original_and_nets_to_zero()
    {
        var original = Approved();
        var reversal = original.Reverse(Approver, "Duplicate posting", Day.AddDays(2));

        Assert.Equal(CostTransactionStatus.Reversed, original.Status);
        Assert.True(reversal.IsReversal);
        Assert.Equal(original.Id, reversal.ReversesTransactionId);
        Assert.Equal(-original.Amount, reversal.Amount);
        Assert.Equal(original.Type, reversal.Type);
        Assert.Equal(CostTransactionStatus.Approved, reversal.Status);
        Assert.Equal(0m, new[] { original, reversal }.Where(x => x.CountsTowardTotals).Sum(x => x.Amount));
    }

    [Fact]
    public void Reversal_guards()
    {
        var draft = NewDraft();
        Assert.Throws<InvalidOperationException>(() => draft.Reverse(Approver, "x", Day));
        var original = Approved();
        var reversal = original.Reverse(Approver, "Wrong project", Day);
        Assert.Throws<InvalidOperationException>(() => original.Reverse(Approver, "again", Day));   // already reversed
        Assert.Throws<InvalidOperationException>(() => reversal.Reverse(Approver, "undo", Day));    // reversal of a reversal
        Assert.Throws<ArgumentException>(() => Approved().Reverse(Approver, "", Day));
    }
}
