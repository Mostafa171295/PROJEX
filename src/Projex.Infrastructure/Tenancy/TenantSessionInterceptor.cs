using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Projex.Application.Abstractions;

namespace Projex.Infrastructure.Tenancy;

/// <summary>Sets the PostgreSQL session tenant for RLS. Npgsql resets pooled sessions on checkout.</summary>
public sealed class TenantSessionInterceptor(ITenantContext tenantContext) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        => SetTenant(connection);

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        => await SetTenantAsync(connection, cancellationToken);

    private void SetTenant(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.tenant_id', @tenant_id, false)";
        var parameter = command.CreateParameter(); parameter.ParameterName = "@tenant_id";
        parameter.Value = tenantContext.TenantId?.ToString() ?? string.Empty;
        command.Parameters.Add(parameter); command.ExecuteNonQuery();
    }

    private async Task SetTenantAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.tenant_id', @tenant_id, false)";
        var parameter = command.CreateParameter(); parameter.ParameterName = "@tenant_id";
        parameter.Value = tenantContext.TenantId?.ToString() ?? string.Empty;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
