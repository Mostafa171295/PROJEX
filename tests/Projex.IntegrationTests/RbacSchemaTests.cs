using Npgsql;
using Projex.Application.Security;
using static Projex.IntegrationTests.DatabaseFixture;

namespace Projex.IntegrationTests;

/// <summary>The RBAC seed matches the in-code catalog, and the database enforces who may change what.</summary>
public class RbacSchemaTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    private static async Task SeedTenantAsync(NpgsqlConnection c, Guid tenant) =>
        await Exec(c, "INSERT INTO platform.tenants(id,code,name,status,created_at,updated_at) VALUES (@id,@k,@k,'Active',now(),now())",
            ("id", tenant), ("k", "t-" + tenant.ToString("N")[..12]));

    private static Task SeedUserAsync(NpgsqlConnection c, Guid tenant, Guid user) =>
        Exec(c, "INSERT INTO identity.users(id,tenant_id,email_confirmed,phone_number_confirmed,two_factor_enabled,lockout_enabled,access_failed_count) VALUES (@id,@t,false,false,false,true,0)",
            ("id", user), ("t", tenant));

    private static async Task<Guid> SystemRoleIdAsync(NpgsqlConnection c, string code) =>
        await Scalar<Guid>(c, "SELECT id FROM identity.roles WHERE tenant_id IS NULL AND code=@c", ("c", code));

    [Fact]
    public async Task Seeded_permissions_and_system_roles_match_the_code_catalog()
    {
        var tenant = Guid.NewGuid();
        await using (var seed = await db.OpenAppAsync()) await SeedTenantAsync(seed, tenant);
        await using var c = await db.OpenAppAsync(tenant);

        var permissions = new Dictionary<string, string>();
        await using (var cmd = new NpgsqlCommand("SELECT code, module FROM identity.permissions", c))
        await using (var r = await cmd.ExecuteReaderAsync()) while (await r.ReadAsync()) permissions[r.GetString(0)] = r.GetString(1);
        Assert.Equal(RoleCatalog.AllPermissions.ToDictionary(p => p.Code, p => p.Module).OrderBy(x => x.Key), permissions.OrderBy(x => x.Key));

        var granted = new Dictionary<string, SortedSet<string>>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT r.code, p.code FROM identity.roles r
            JOIN identity.role_permissions rp ON rp.role_id = r.id JOIN identity.permissions p ON p.id = rp.permission_id
            WHERE r.tenant_id IS NULL AND r.is_system
            """, c))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) { if (!granted.TryGetValue(r.GetString(0), out var set)) granted[r.GetString(0)] = set = []; set.Add(r.GetString(1)); }

        Assert.Equal(RoleCatalog.SystemRoles.Select(r => r.Code).Order(), granted.Keys.Order());
        foreach (var role in RoleCatalog.SystemRoles) Assert.Equal(role.Permissions.Order(), granted[role.Code]);
    }

    [Fact]
    public async Task The_application_role_cannot_change_system_roles_or_the_permission_catalog()
    {
        var tenant = Guid.NewGuid();
        await using (var seed = await db.OpenAppAsync()) await SeedTenantAsync(seed, tenant);
        await using var c = await db.OpenAppAsync(tenant);

        Assert.Equal(0, await Exec(c, "UPDATE identity.roles SET name='hacked' WHERE tenant_id IS NULL"));
        Assert.Equal(0, await Exec(c, "DELETE FROM identity.roles WHERE tenant_id IS NULL"));
        var forged = await Assert.ThrowsAsync<PostgresException>(() => Exec(c,
            "INSERT INTO identity.roles(id,tenant_id,code,name,is_system,created_at,updated_at) VALUES (gen_random_uuid(),NULL,'EVIL','Evil',true,now(),now())"));
        Assert.Equal("42501", forged.SqlState);
        var perm = await Assert.ThrowsAsync<PostgresException>(() => Exec(c,
            "INSERT INTO identity.permissions(id,code,module,description,created_at,updated_at) VALUES (gen_random_uuid(),'evil.all','evil','x',now(),now())"));
        Assert.Equal("42501", perm.SqlState);
        // system role permissions cannot be edited either
        Assert.Equal(0, await Exec(c, "DELETE FROM identity.role_permissions"));
    }

    [Fact]
    public async Task Custom_roles_are_tenant_private_and_cannot_reuse_a_system_code()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), role = Guid.NewGuid();
        await using (var seed = await db.OpenAppAsync()) { await SeedTenantAsync(seed, a); await SeedTenantAsync(seed, b); }
        await using (var asA = await db.OpenAppAsync(a))
            await Exec(asA, "INSERT INTO identity.roles(id,tenant_id,code,name,is_system,created_at,updated_at) VALUES (@id,@t,'SENIOR_PLANNER','Senior Planner',false,now(),now())", ("id", role), ("t", a));

        await using (var asB = await db.OpenAppAsync(b))
        {
            Assert.Equal(0L, await Scalar<long>(asB, "SELECT count(*) FROM identity.roles WHERE id=@id", ("id", role)));
            Assert.Equal(8L, await Scalar<long>(asB, "SELECT count(*) FROM identity.roles WHERE tenant_id IS NULL"));
            var clash = await Assert.ThrowsAsync<PostgresException>(() => Exec(asB,
                "INSERT INTO identity.roles(id,tenant_id,code,name,is_system,created_at,updated_at) VALUES (gen_random_uuid(),@t,'VIEWER','Mine',false,now(),now())", ("t", b)));
            Assert.Equal("23505", clash.SqlState);
        }
    }

    [Fact]
    public async Task Role_assignments_are_tenant_scoped_and_reject_other_tenants_roles()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), userA = Guid.NewGuid(), userB = Guid.NewGuid(), roleB = Guid.NewGuid();
        await using (var seed = await db.OpenAppAsync()) { await SeedTenantAsync(seed, a); await SeedTenantAsync(seed, b); }
        await using (var asB = await db.OpenAppAsync(b))
        {
            await SeedUserAsync(asB, b, userB);
            await Exec(asB, "INSERT INTO identity.roles(id,tenant_id,code,name,is_system,created_at,updated_at) VALUES (@id,@t,'B_ONLY','B only',false,now(),now())", ("id", roleB), ("t", b));
        }

        await using var asA = await db.OpenAppAsync(a);
        await SeedUserAsync(asA, a, userA);
        var viewer = await SystemRoleIdAsync(asA, "VIEWER");
        const string insert = "INSERT INTO identity.user_role_assignments(id,tenant_id,user_id,role_id,created_at,updated_at) VALUES (gen_random_uuid(),@t,@u,@r,now(),now())";

        // system role: fine, once (NULLS NOT DISTINCT makes the tenant-wide assignment unique)
        Assert.Equal(1, await Exec(asA, insert, ("t", a), ("u", userA), ("r", viewer)));
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => Exec(asA, insert, ("t", a), ("u", userA), ("r", viewer)));
        Assert.Equal("23505", duplicate.SqlState);
        // another tenant's custom role is invisible, so the trigger refuses it
        var foreignRole = await Assert.ThrowsAsync<PostgresException>(() => Exec(asA, insert, ("t", a), ("u", userA), ("r", roleB)));
        Assert.Equal("23503", foreignRole.SqlState);
        // another tenant's user is refused by the composite foreign key
        var foreignUser = await Assert.ThrowsAsync<PostgresException>(() => Exec(asA, insert, ("t", a), ("u", userB), ("r", viewer)));
        Assert.Equal("23503", foreignUser.SqlState);
        // a project scope cannot be combined with a company scope
        var badScope = await Assert.ThrowsAsync<PostgresException>(() => Exec(asA, """
            INSERT INTO identity.user_role_assignments(id,tenant_id,user_id,role_id,company_id,project_id,created_at,updated_at)
            VALUES (gen_random_uuid(),@t,@u,@r,gen_random_uuid(),gen_random_uuid(),now(),now())
            """, ("t", a), ("u", userA), ("r", viewer)));
        Assert.True(badScope.SqlState is "23514" or "23503");

        await using var asB2 = await db.OpenAppAsync(b);
        Assert.Equal(0L, await Scalar<long>(asB2, "SELECT count(*) FROM identity.user_role_assignments"));
    }
}
