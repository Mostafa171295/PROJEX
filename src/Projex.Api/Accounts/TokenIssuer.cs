using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Projex.Domain.Platform;
using Projex.Infrastructure.Tenancy;

namespace Projex.Api.Accounts;

/// <summary>Issues short-lived access tokens bound to a session, and opaque refresh tokens (stored only as SHA-256).</summary>
public sealed class TokenIssuer(string issuer, string audience, SecurityKey signingKey)
{
    public const int AccessTokenSeconds = 900;
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(14);
    public const string SessionClaim = "session_id";
    public const string PlatformAdminClaim = "platform_admin_id";
    public const string PlatformAdminRole = "PlatformAdmin";

    public string Issue(ProjexUser user, Guid tenantId, string tenantKey, IEnumerable<string> roles, Guid sessionId)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new("tenant_id", tenantId.ToString()), new("tenant_key", tenantKey), new(SessionClaim, sessionId.ToString())
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var token = new JwtSecurityToken(issuer, audience, claims, DateTime.UtcNow, DateTime.UtcNow.AddSeconds(AccessTokenSeconds),
            new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Short-lived platform administrator token. It carries no tenant_id, so every tenant endpoint and every RLS policy rejects it.</summary>
    public string IssuePlatform(PlatformAdmin admin)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, admin.Id.ToString()), new(ClaimTypes.NameIdentifier, admin.Id.ToString()),
            new(ClaimTypes.Email, admin.Email), new(PlatformAdminClaim, admin.Id.ToString()), new(ClaimTypes.Role, PlatformAdminRole)
        };
        var token = new JwtSecurityToken(issuer, audience, claims, DateTime.UtcNow, DateTime.UtcNow.AddSeconds(AccessTokenSeconds),
            new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static string NewRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
