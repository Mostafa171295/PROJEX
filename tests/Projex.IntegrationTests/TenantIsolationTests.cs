using Npgsql;
using static Projex.IntegrationTests.DatabaseFixture;

namespace Projex.IntegrationTests;

/// <summary>Proves RLS is enforced for the application role against a real PostgreSQL.</summary>
public class TenantIsolationTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    private static async Task SeedTenantAsync(NpgsqlConnection c, Guid tenant) =>
        await Exec(c, "INSERT INTO platform.tenants(id,code,name,status,created_at,updated_at) VALUES (@id,@k,@k,'Active',now(),now())",
            ("id", tenant), ("k", "t-" + tenant.ToString("N")[..12]));

    private static async Task SeedCompanyAsync(NpgsqlConnection c, Guid tenant, Guid company, string code) =>
        await Exec(c, "INSERT INTO organization.companies(id,tenant_id,code,name,is_active,created_at,updated_at) VALUES (@id,@t,@c,@c,true,now(),now())",
            ("id", company), ("t", tenant), ("c", code));

    [Fact]
    public async Task Application_role_cannot_bypass_row_level_security()
    {
        await using var c = await db.OpenAppAsync();
        Assert.False(await Scalar<bool>(c, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user"));
        Assert.NotEqual("projex_app", await Scalar<string>(c, "SELECT tableowner FROM pg_tables WHERE schemaname='projects' AND tablename='projects'"));
    }

    [Fact]
    public async Task Every_table_with_a_tenant_id_has_forced_RLS_and_a_policy()
    {
        await using var c = await db.OpenAppAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT n.nspname || '.' || cl.relname
            FROM information_schema.columns col
            JOIN pg_namespace n ON n.nspname = col.table_schema
            JOIN pg_class cl ON cl.relnamespace = n.oid AND cl.relname = col.table_name AND cl.relkind = 'r'
            WHERE col.column_name = 'tenant_id'
              AND col.table_schema IN ('organization','identity','projects','planning','audit','costing','progress')
              AND NOT (cl.relrowsecurity AND cl.relforcerowsecurity
                       AND EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid = cl.oid))
            """, c);
        var unprotected = new List<string>();
        await using (var r = await cmd.ExecuteReaderAsync()) while (await r.ReadAsync()) unprotected.Add(r.GetString(0));
        Assert.True(unprotected.Count == 0, "Tables with tenant_id but no forced RLS policy: " + string.Join(", ", unprotected));
    }

    [Fact]
    public async Task A_tenant_sees_only_its_own_rows_and_nothing_without_context()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), ca = Guid.NewGuid(), cb = Guid.NewGuid();
        await using (var seed = await db.OpenAppAsync())
        {
            await SeedTenantAsync(seed, a); await SeedTenantAsync(seed, b);
        }
        await using (var asA = await db.OpenAppAsync(a)) await SeedCompanyAsync(asA, a, ca, "A1");
        await using (var asB = await db.OpenAppAsync(b)) await SeedCompanyAsync(asB, b, cb, "B1");

        await using (var asA = await db.OpenAppAsync(a))
        {
            Assert.Equal(1L, await Scalar<long>(asA, "SELECT count(*) FROM organization.companies"));
            Assert.Equal(0L, await Scalar<long>(asA, "SELECT count(*) FROM organization.companies WHERE id=@id", ("id", cb)));
        }
        await using (var none = await db.OpenAppAsync())
            Assert.Equal(0L, await Scalar<long>(none, "SELECT count(*) FROM organization.companies"));
    }

    [Fact]
    public async Task A_tenant_cannot_update_delete_or_insert_into_another_tenant()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), cb = Guid.NewGuid();
        await using (var seed = await db.OpenAppAsync()) { await SeedTenantAsync(seed, a); await SeedTenantAsync(seed, b); }
        await using (var asB = await db.OpenAppAsync(b)) await SeedCompanyAsync(asB, b, cb, "VICTIM");

        await using var asA = await db.OpenAppAsync(a);
        Assert.Equal(0, await Exec(asA, "UPDATE organization.companies SET name='hacked' WHERE id=@id", ("id", cb)));
        Assert.Equal(0, await Exec(asA, "DELETE FROM organization.companies WHERE id=@id", ("id", cb)));
        var ex = await Assert.ThrowsAsync<PostgresException>(() => SeedCompanyAsync(asA, b, Guid.NewGuid(), "FORGED"));
        Assert.Equal("42501", ex.SqlState); // RLS WITH CHECK violation

        await using var asB2 = await db.OpenAppAsync(b);
        Assert.Equal("VICTIM", await Scalar<string>(asB2, "SELECT name FROM organization.companies WHERE id=@id", ("id", cb)));
    }

    [Fact]
    public async Task Composite_foreign_keys_reject_references_to_another_tenants_parent()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), cb = Guid.NewGuid();
        await using (var seed = await db.OpenAppAsync()) { await SeedTenantAsync(seed, a); await SeedTenantAsync(seed, b); }
        await using (var asB = await db.OpenAppAsync(b)) await SeedCompanyAsync(asB, b, cb, "OTHER");

        await using var asA = await db.OpenAppAsync(a);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => Exec(asA, """
            INSERT INTO projects.projects(id,tenant_id,company_id,code,name,currency_code,time_zone,status,created_at,updated_at)
            VALUES (@id,@t,@c,'P1','P1','USD','UTC','Draft',now(),now())
            """, ("id", Guid.NewGuid()), ("t", a), ("c", cb)));
        Assert.Equal("23503", ex.SqlState); // foreign_key_violation
    }

    [Fact]
    public async Task Audit_log_is_append_only_and_tenant_scoped()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        await using (var seed = await db.OpenAppAsync()) { await SeedTenantAsync(seed, a); await SeedTenantAsync(seed, b); }
        const string insert = "INSERT INTO audit.audit_logs(id,tenant_id,action,entity_type,occurred_at) VALUES (@id,@t,'test.action','Test',now())";
        var id = Guid.NewGuid();
        await using var asA = await db.OpenAppAsync(a);
        Assert.Equal(1, await Exec(asA, insert, ("id", id), ("t", a)));

        // cannot write an audit row for another tenant
        var forged = await Assert.ThrowsAsync<PostgresException>(() => Exec(asA, insert, ("id", Guid.NewGuid()), ("t", b)));
        Assert.Equal("42501", forged.SqlState);
        // cannot update or delete (no privilege, plus trigger)
        var upd = await Assert.ThrowsAsync<PostgresException>(() => Exec(asA, "UPDATE audit.audit_logs SET action='tampered' WHERE id=@id", ("id", id)));
        Assert.Equal("42501", upd.SqlState);
        var del = await Assert.ThrowsAsync<PostgresException>(() => Exec(asA, "DELETE FROM audit.audit_logs WHERE id=@id", ("id", id)));
        Assert.Equal("42501", del.SqlState);
        // another tenant cannot read it
        await using var asB = await db.OpenAppAsync(b);
        Assert.Equal(0L, await Scalar<long>(asB, "SELECT count(*) FROM audit.audit_logs WHERE id=@id", ("id", id)));
    }

    private static Task SeedUserAsync(NpgsqlConnection c, Guid tenant, Guid user) =>
        Exec(c, "INSERT INTO identity.users(id,tenant_id,email_confirmed,phone_number_confirmed,two_factor_enabled,lockout_enabled,access_failed_count) VALUES (@id,@t,false,false,false,true,0)",
            ("id", user), ("t", tenant));

    [Fact]
    public async Task Memberships_and_sessions_cannot_reference_a_user_of_another_tenant()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), ca = Guid.NewGuid(), userB = Guid.NewGuid();
        await using (var seed = await db.OpenAppAsync()) { await SeedTenantAsync(seed, a); await SeedTenantAsync(seed, b); }
        await using (var asA0 = await db.OpenAppAsync(a)) await SeedCompanyAsync(asA0, a, ca, "A1");
        await using (var asB = await db.OpenAppAsync(b)) await SeedUserAsync(asB, b, userB);

        await using var asA = await db.OpenAppAsync(a);
        var membership = await Assert.ThrowsAsync<PostgresException>(() => Exec(asA,
            "INSERT INTO identity.user_memberships(id,tenant_id,company_id,user_id,is_active,created_at,updated_at) VALUES (@id,@t,@c,@u,true,now(),now())",
            ("id", Guid.NewGuid()), ("t", a), ("c", ca), ("u", userB)));
        Assert.Equal("23503", membership.SqlState);

        var session = await Assert.ThrowsAsync<PostgresException>(() => Exec(asA, """
            INSERT INTO identity.user_sessions(id,tenant_id,user_id,refresh_token_hash,expires_at,created_at,updated_at)
            VALUES (@id,@t,@u,@h,now() + interval '1 day',now(),now())
            """, ("id", Guid.NewGuid()), ("t", a), ("u", userB), ("h", new string('a', 64))));
        Assert.Equal("23503", session.SqlState);
    }

    [Fact]
    public async Task Identity_child_tables_without_tenant_id_are_isolated_through_their_user()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), userB = Guid.NewGuid(), role = Guid.NewGuid();
        var roleName = "R" + role.ToString("N");
        await using (var seed = await db.OpenAppAsync()) { await SeedTenantAsync(seed, a); await SeedTenantAsync(seed, b); }
        await using (var asB = await db.OpenAppAsync(b))
        {
            await SeedUserAsync(asB, b, userB);
            await Exec(asB, "INSERT INTO identity.identity_roles(id,name,normalized_name) VALUES (@id,@n,@n)", ("id", role), ("n", roleName));
            await Exec(asB, "INSERT INTO identity.user_roles(user_id,role_id) VALUES (@u,@r)", ("u", userB), ("r", role));
            await Exec(asB, "INSERT INTO identity.user_claims(user_id,claim_type,claim_value) VALUES (@u,'x','y')", ("u", userB));
        }

        await using (var asB = await db.OpenAppAsync(b))
        {
            Assert.Equal(1L, await Scalar<long>(asB, "SELECT count(*) FROM identity.user_roles WHERE user_id=@u", ("u", userB)));
            Assert.Equal(1L, await Scalar<long>(asB, "SELECT count(*) FROM identity.user_claims WHERE user_id=@u", ("u", userB)));
        }
        await using (var asA = await db.OpenAppAsync(a))
        {
            Assert.Equal(0L, await Scalar<long>(asA, "SELECT count(*) FROM identity.user_roles WHERE user_id=@u", ("u", userB)));
            Assert.Equal(0L, await Scalar<long>(asA, "SELECT count(*) FROM identity.user_claims WHERE user_id=@u", ("u", userB)));
            Assert.Equal(0, await Exec(asA, "DELETE FROM identity.user_roles WHERE user_id=@u", ("u", userB)));
            var forged = await Assert.ThrowsAsync<PostgresException>(() => Exec(asA,
                "INSERT INTO identity.user_claims(user_id,claim_type,claim_value) VALUES (@u,'evil','1')", ("u", userB)));
            Assert.Equal("42501", forged.SqlState);
        }
        await using (var none = await db.OpenAppAsync())
            Assert.Equal(0L, await Scalar<long>(none, "SELECT count(*) FROM identity.user_roles"));
    }

    [Fact]
    public async Task Every_identity_table_with_user_id_has_forced_RLS()
    {
        await using var c = await db.OpenAppAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT cl.relname
            FROM information_schema.columns col
            JOIN pg_namespace n ON n.nspname = col.table_schema
            JOIN pg_class cl ON cl.relnamespace = n.oid AND cl.relname = col.table_name AND cl.relkind = 'r'
            WHERE col.table_schema = 'identity' AND col.column_name = 'user_id'
              AND NOT (cl.relrowsecurity AND cl.relforcerowsecurity
                       AND EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid = cl.oid))
            """, c);
        var unprotected = new List<string>();
        await using (var r = await cmd.ExecuteReaderAsync()) while (await r.ReadAsync()) unprotected.Add(r.GetString(0));
        Assert.True(unprotected.Count == 0, "identity tables with user_id but no forced RLS: " + string.Join(", ", unprotected));
    }
}
