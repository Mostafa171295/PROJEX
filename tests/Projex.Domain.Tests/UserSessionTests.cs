using Projex.Domain.Security;

namespace Projex.Domain.Tests;

public sealed class UserSessionTests
{
    private static readonly string H1 = new('a', 64), H2 = new('b', 64);
    private static UserSession New(TimeSpan? life = null) => new(Guid.NewGuid(), Guid.NewGuid(), H1, DateTimeOffset.UtcNow.Add(life ?? TimeSpan.FromDays(14)), "127.0.0.1", "test");

    [Fact]
    public void New_session_is_active_until_expiry_or_revocation()
    {
        var s = New();
        Assert.True(s.IsActive(DateTimeOffset.UtcNow));
        Assert.False(s.IsActive(DateTimeOffset.UtcNow.AddDays(15)));
        s.Revoke("logout");
        Assert.False(s.IsActive(DateTimeOffset.UtcNow));
        Assert.Equal("logout", s.RevokedReason);
    }

    [Fact]
    public void Rotation_remembers_the_previous_hash_for_reuse_detection()
    {
        var s = New();
        s.Rotate(H2, DateTimeOffset.UtcNow.AddDays(14), DateTimeOffset.UtcNow);
        Assert.Equal(H2, s.RefreshTokenHash);
        Assert.Equal(H1, s.PreviousRefreshTokenHash);
    }

    [Fact]
    public void Cannot_rotate_a_revoked_session()
    {
        var s = New(); s.Revoke("user_inactive");
        Assert.Throws<InvalidOperationException>(() => s.Rotate(H2, DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Sliding_expiry_never_exceeds_the_absolute_lifetime()
    {
        var s = New();
        s.Rotate(H2, DateTimeOffset.UtcNow.AddDays(365), DateTimeOffset.UtcNow);
        Assert.True(s.ExpiresAt <= s.CreatedAt + UserSession.AbsoluteLifetime);
    }

    [Fact]
    public void Rejects_malformed_hash()
        => Assert.Throws<ArgumentException>(() => new UserSession(Guid.NewGuid(), Guid.NewGuid(), "short", DateTimeOffset.UtcNow.AddDays(1), null, null));

    [Fact]
    public void Revoke_is_idempotent_and_keeps_the_first_reason()
    {
        var s = New(); s.Revoke("logout"); s.Revoke("other");
        Assert.Equal("logout", s.RevokedReason);
    }
}
