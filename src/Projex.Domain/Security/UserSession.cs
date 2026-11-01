using Projex.Domain.Common;

namespace Projex.Domain.Security;

/// <summary>
/// A sign-in session. The refresh token is stored only as a hash and rotates on every use; the previous hash is kept so a
/// replayed (stolen) token is detected and the whole session is revoked. Access tokens carry the session id, so revoking
/// the session (logout, disabled user, reuse) takes effect immediately.
/// </summary>
public sealed class UserSession : Entity
{
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(30);

    private UserSession() { }
    public UserSession(Guid tenantId, Guid userId, string refreshTokenHash, DateTimeOffset expiresAt, string? ipAddress, string? userAgent)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty) throw new ArgumentException("Tenant and user are required.");
        if (string.IsNullOrWhiteSpace(refreshTokenHash) || refreshTokenHash.Length != 64) throw new ArgumentException("A SHA-256 hex hash is required.", nameof(refreshTokenHash));
        TenantId = tenantId; UserId = userId; RefreshTokenHash = refreshTokenHash;
        var cap = CreatedAt + AbsoluteLifetime;
        ExpiresAt = expiresAt < cap ? expiresAt : cap;
        IpAddress = Trim(ipAddress, 64); UserAgent = Trim(userAgent, 400);
    }
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public string RefreshTokenHash { get; private set; } = string.Empty;
    public string? PreviousRefreshTokenHash { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokedReason { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public DateTimeOffset? LastRefreshedAt { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Rotate(string newHash, DateTimeOffset newExpiry, DateTimeOffset now)
    {
        if (!IsActive(now)) throw new InvalidOperationException("Only an active session can be refreshed.");
        if (string.IsNullOrWhiteSpace(newHash) || newHash.Length != 64) throw new ArgumentException("A SHA-256 hex hash is required.", nameof(newHash));
        PreviousRefreshTokenHash = RefreshTokenHash; RefreshTokenHash = newHash; LastRefreshedAt = now;
        var cap = CreatedAt + AbsoluteLifetime;
        ExpiresAt = newExpiry < cap ? newExpiry : cap;
        Touch();
    }

    public void Revoke(string reason)
    {
        if (RevokedAt is not null) return;
        RevokedAt = DateTimeOffset.UtcNow; RevokedReason = Trim(reason, 40) ?? "revoked"; Touch();
    }

    private static string? Trim(string? v, int max) => string.IsNullOrEmpty(v) ? null : v.Length <= max ? v : v[..max];
}
