using Projex.Domain.Common;

namespace Projex.Domain.Platform;

/// <summary>
/// A platform operator (SRS "Platform Super Admin"). Deliberately NOT a tenant user: it has no tenant_id, so row-level security
/// gives it no access to any customer's business data. It can manage tenants and licences only.
/// </summary>
public sealed class PlatformAdmin : Entity
{
    private PlatformAdmin() { }
    public PlatformAdmin(string email, string displayName, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Trim().Length > 256) throw new ArgumentException("A valid email is required.", nameof(email));
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 200) throw new ArgumentException("A display name of 1-200 characters is required.", nameof(displayName));
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new ArgumentException("A password hash is required.", nameof(passwordHash));
        Email = email.Trim(); NormalizedEmail = Email.ToUpperInvariant(); DisplayName = displayName.Trim(); PasswordHash = passwordHash; IsActive = true;
    }
    public string Email { get; private set; } = string.Empty;
    public string NormalizedEmail { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public int AccessFailedCount { get; private set; }
    public DateTimeOffset? LockoutEnd { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }

    public bool IsLockedOut(DateTimeOffset now) => LockoutEnd is not null && LockoutEnd > now;

    public void RegisterFailure(DateTimeOffset now, int maxAttempts, TimeSpan lockoutFor)
    {
        AccessFailedCount++;
        if (AccessFailedCount >= maxAttempts) { LockoutEnd = now.Add(lockoutFor); AccessFailedCount = 0; }
        Touch();
    }
    public void RegisterSuccess(DateTimeOffset now) { AccessFailedCount = 0; LockoutEnd = null; LastLoginAt = now; Touch(); }
    public void SetPasswordHash(string hash) { if (string.IsNullOrWhiteSpace(hash)) throw new ArgumentException("A password hash is required."); PasswordHash = hash; Touch(); }
    public void Disable() { IsActive = false; Touch(); }
    public void Enable() { IsActive = true; Touch(); }
}
