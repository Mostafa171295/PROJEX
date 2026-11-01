using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Projex.Application.Abstractions;
using Projex.Domain.Audit;
using Projex.Infrastructure.Persistence;

namespace Projex.Infrastructure.Auditing;

public sealed class AuditWriter(ProjexDbContext db, ITenantContext tenant, IHttpContextAccessor accessor) : IAuditWriter
{
    private const string CorrelationKey = "projex.correlation_id";

    public void Record(string action, string entityType, Guid? entityId, object? before = null, object? after = null, Guid? actorUserId = null)
    {
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("Cannot audit without a resolved tenant.");
        var http = accessor.HttpContext;
        db.AuditLogs.Add(new AuditLog(tenantId, actorUserId ?? tenant.UserId, action, entityType, entityId,
            Correlation(http), http?.Connection.RemoteIpAddress, Truncate(http?.Request.Headers.UserAgent.ToString(), 400),
            before is null ? null : JsonSerializer.Serialize(before), after is null ? null : JsonSerializer.Serialize(after)));
    }

    private static Guid? Correlation(HttpContext? http)
    {
        if (http is null) return null;
        if (http.Items[CorrelationKey] is Guid existing) return existing;
        var supplied = Guid.TryParse(http.Request.Headers["X-Correlation-Id"].ToString(), out var g) ? g : Guid.NewGuid();
        http.Items[CorrelationKey] = supplied;
        return supplied;
    }

    private static string? Truncate(string? value, int max) => string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];
}
