using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using Projex.Application.Abstractions;
using Projex.Infrastructure.Persistence;

namespace Projex.IntegrationTests;

/// <summary>Guards against drift between the hand-written SQL migrations and the EF Core model.</summary>
public class SchemaMatchesModelTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    private sealed class NoTenant : ITenantContext { public Guid? TenantId => null; public Guid? UserId => null; public bool IsResolved => false; }

    [Fact]
    public async Task Every_EF_column_exists_in_the_migrated_database_and_required_columns_are_writable()
    {
        var options = new DbContextOptionsBuilder<ProjexDbContext>().UseNpgsql(db.AppConnectionString).Options;
        await using var ctx = new ProjexDbContext(options, new NoTenant());

        var actual = new Dictionary<string, (bool NotNull, bool HasDefault)>(StringComparer.Ordinal);
        await using (var conn = new NpgsqlConnection(db.OwnerConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("""
                SELECT table_schema||'.'||table_name||'.'||column_name, is_nullable='NO',
                       column_default IS NOT NULL OR is_identity='YES'
                FROM information_schema.columns
                """, conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) actual[r.GetString(0)] = (r.GetBoolean(1), r.GetBoolean(2));
        }

        var problems = new List<string>();
        foreach (var entity in ctx.Model.GetEntityTypes())
        {
            var table = entity.GetTableName(); if (table is null) continue;
            var schema = entity.GetSchema() ?? ctx.Model.GetDefaultSchema();
            var id = StoreObjectIdentifier.Table(table, schema);
            foreach (var p in entity.GetProperties())
            {
                var key = $"{schema}.{table}.{p.GetColumnName(id)}";
                if (!actual.TryGetValue(key, out var col)) { problems.Add($"MISSING column {key} ({entity.DisplayName()}.{p.Name})"); continue; }
                if (col.NotNull && !col.HasDefault && p.IsNullable) problems.Add($"NOT NULL without default but EF property is nullable: {key}");
            }
        }
        Assert.True(problems.Count == 0, Environment.NewLine + string.Join(Environment.NewLine, problems.Distinct()));
    }
}
