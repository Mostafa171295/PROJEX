using Projex.Domain.Platform;
using Xunit;

namespace Projex.Domain.Tests;

public sealed class PlatformTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Subscription_RequiresPositiveSeatsAndSaneDates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Subscription(Guid.NewGuid(), "pro", SubscriptionStatus.Active, Now, null, 0));
        Assert.Throws<ArgumentException>(() => new Subscription(Guid.NewGuid(), "pro", SubscriptionStatus.Active, Now, Now, 5));
        Assert.Throws<ArgumentException>(() => new Subscription(Guid.Empty, "pro", SubscriptionStatus.Active, Now, null, 5));
        Assert.Throws<ArgumentException>(() => new Subscription(Guid.NewGuid(), " ", SubscriptionStatus.Active, Now, null, 5));
    }

    [Fact]
    public void Subscription_EntitlementsMustBeAJsonObject()
    {
        var sub = new Subscription(Guid.NewGuid(), "pro", SubscriptionStatus.Active, Now, null, 5);
        Assert.Equal("{}", sub.ModuleEntitlements);
        sub.Change("pro", SubscriptionStatus.Active, Now, null, 5, "{\"costing\":true}");
        Assert.Equal("{\"costing\":true}", sub.ModuleEntitlements);
        Assert.Throws<ArgumentException>(() => sub.Change("pro", SubscriptionStatus.Active, Now, null, 5, "[1,2]"));
        Assert.Throws<ArgumentException>(() => sub.Change("pro", SubscriptionStatus.Active, Now, null, 5, "{broken"));
    }

    [Theory]
    [InlineData(SubscriptionStatus.Trial, true)]
    [InlineData(SubscriptionStatus.Active, true)]
    [InlineData(SubscriptionStatus.PastDue, true)]
    [InlineData(SubscriptionStatus.Suspended, false)]
    [InlineData(SubscriptionStatus.Cancelled, false)]
    public void Subscription_AllowsNewUsersOnlyWhileLive(SubscriptionStatus status, bool expected)
        => Assert.Equal(expected, new Subscription(Guid.NewGuid(), "pro", status, Now.AddDays(-1), null, 5).AllowsNewUsers(Now));

    [Fact]
    public void Subscription_PastItsEndDateDoesNotAllowNewUsers()
        => Assert.False(new Subscription(Guid.NewGuid(), "trial", SubscriptionStatus.Trial, Now.AddDays(-31), Now.AddDays(-1), 5).AllowsNewUsers(Now));

    [Fact]
    public void PlatformAdmin_LocksOutAfterConfiguredFailuresAndRecoversOnSuccess()
    {
        var admin = new PlatformAdmin("Root@Projex.Test", "Root", "hash");
        Assert.Equal("ROOT@PROJEX.TEST", admin.NormalizedEmail);
        for (var i = 0; i < 2; i++) admin.RegisterFailure(Now, 3, TimeSpan.FromMinutes(15));
        Assert.False(admin.IsLockedOut(Now));
        admin.RegisterFailure(Now, 3, TimeSpan.FromMinutes(15));
        Assert.True(admin.IsLockedOut(Now.AddMinutes(14)));
        Assert.False(admin.IsLockedOut(Now.AddMinutes(16)));
        admin.RegisterSuccess(Now.AddMinutes(16));
        Assert.False(admin.IsLockedOut(Now.AddMinutes(16)));
        Assert.Equal(Now.AddMinutes(16), admin.LastLoginAt);
    }

    [Fact]
    public void PlatformAuditEvent_RequiresAnAction()
        => Assert.Throws<ArgumentException>(() => new PlatformAuditEvent(null, " ", null, null, null));
}
