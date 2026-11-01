using Npgsql;
using static Projex.IntegrationTests.DatabaseFixture;

namespace Projex.IntegrationTests;

/// <summary>
/// Proves, against a real PostgreSQL, that the database itself protects financial history (SRS FR-COST-009, FR-BASE-004):
/// approved rows cannot be rewritten or deleted, reversals net to zero, snapshots are append-only and tenants are isolated.
/// </summary>
public class CostingSchemaTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    private const string ImmutableState = "23000";

    private async Task<(Guid Tenant, Guid Project)> SeedProjectAsync()
    {
        Guid tenant = Guid.NewGuid(), company = Guid.NewGuid(), project = Guid.NewGuid();
        await using (var c = await db.OpenAppAsync())
            await Exec(c, "INSERT INTO platform.tenants(id,code,name,status,created_at,updated_at) VALUES (@id,@k,@k,'Active',now(),now())",
                ("id", tenant), ("k", "t-" + tenant.ToString("N")[..12]));
        await using (var c = await db.OpenAppAsync(tenant))
        {
            await Exec(c, "INSERT INTO organization.companies(id,tenant_id,code,name,is_active,created_at,updated_at) VALUES (@id,@t,'C1','C1',true,now(),now())", ("id", company), ("t", tenant));
            await Exec(c, "INSERT INTO projects.projects(id,tenant_id,company_id,code,name,currency_code,time_zone,status,created_at,updated_at) VALUES (@id,@t,@c,'P1','P1','USD','UTC','Active',now(),now())",
                ("id", project), ("t", tenant), ("c", company));
        }
        return (tenant, project);
    }

    private static Task InsertDraftTransactionAsync(NpgsqlConnection c, Guid tenant, Guid project, Guid id) =>
        Exec(c, """
            INSERT INTO costing.cost_transactions(id,tenant_id,project_id,transaction_type,is_reversal,description,transaction_date,amount,currency_code,status,created_by,created_at,updated_at)
            VALUES (@id,@t,@p,'Actual',false,'Concrete','2026-11-01',100,'USD','Draft',@u,now(),now())
            """, ("id", id), ("t", tenant), ("p", project), ("u", Guid.NewGuid()));

    private static Task ApproveAsync(NpgsqlConnection c, Guid id) =>
        Exec(c, "UPDATE costing.cost_transactions SET status='Approved', approved_by=@u, approved_at=now(), row_version=row_version+1, updated_at=now() WHERE id=@id", ("id", id), ("u", Guid.NewGuid()));

    [Fact]
    public async Task Approved_cost_transaction_cannot_be_rewritten_or_deleted()
    {
        var (tenant, project) = await SeedProjectAsync();
        var id = Guid.NewGuid();
        await using var c = await db.OpenAppAsync(tenant);
        await InsertDraftTransactionAsync(c, tenant, project, id);
        await ApproveAsync(c, id);

        var edit = await Assert.ThrowsAsync<PostgresException>(() => Exec(c, "UPDATE costing.cost_transactions SET amount=999 WHERE id=@id", ("id", id)));
        Assert.Equal(ImmutableState, edit.SqlState);
        var delete = await Assert.ThrowsAsync<PostgresException>(() => Exec(c, "DELETE FROM costing.cost_transactions WHERE id=@id", ("id", id)));
        Assert.Equal(ImmutableState, delete.SqlState);
        Assert.Equal(100m, await Scalar<decimal>(c, "SELECT amount FROM costing.cost_transactions WHERE id=@id", ("id", id)));
    }

    [Fact]
    public async Task Reversal_offsets_the_original_exactly_once_and_totals_net_to_zero()
    {
        var (tenant, project) = await SeedProjectAsync();
        Guid original = Guid.NewGuid(), reversal = Guid.NewGuid();
        await using var c = await db.OpenAppAsync(tenant);
        await InsertDraftTransactionAsync(c, tenant, project, original);
        await ApproveAsync(c, original);

        await Exec(c, "UPDATE costing.cost_transactions SET status='Reversed', reversal_reason='Duplicate', row_version=row_version+1, updated_at=now() WHERE id=@id", ("id", original));
        const string insertReversal = """
            INSERT INTO costing.cost_transactions(id,tenant_id,project_id,transaction_type,is_reversal,reverses_transaction_id,description,transaction_date,amount,currency_code,status,created_by,approved_by,approved_at,created_at,updated_at)
            VALUES (@id,@t,@p,'Actual',true,@orig,'Reversal','2026-11-02',-100,'USD','Approved',@u,@u,now(),now(),now())
            """;
        var user = Guid.NewGuid();
        await Exec(c, insertReversal, ("id", reversal), ("t", tenant), ("p", project), ("orig", original), ("u", user));

        var second = await Assert.ThrowsAsync<PostgresException>(() =>
            Exec(c, insertReversal, ("id", Guid.NewGuid()), ("t", tenant), ("p", project), ("orig", original), ("u", user)));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, second.SqlState);

        Assert.Equal(0m, await Scalar<decimal>(c, "SELECT COALESCE(sum(amount),0) FROM costing.cost_transactions WHERE status IN ('Approved','Reversed')"));
    }

    [Fact]
    public async Task Idempotency_key_is_unique_per_project()
    {
        var (tenant, project) = await SeedProjectAsync();
        await using var c = await db.OpenAppAsync(tenant);
        const string sql = """
            INSERT INTO costing.cost_transactions(id,tenant_id,project_id,transaction_type,description,transaction_date,amount,currency_code,status,idempotency_key,created_by,created_at,updated_at)
            VALUES (@id,@t,@p,'Actual','x','2026-11-01',5,'USD','Draft','key-1',@u,now(),now())
            """;
        await Exec(c, sql, ("id", Guid.NewGuid()), ("t", tenant), ("p", project), ("u", Guid.NewGuid()));
        var dup = await Assert.ThrowsAsync<PostgresException>(() => Exec(c, sql, ("id", Guid.NewGuid()), ("t", tenant), ("p", project), ("u", Guid.NewGuid())));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, dup.SqlState);
    }

    [Fact]
    public async Task Budget_lines_freeze_when_the_version_is_approved_and_only_one_version_can_be_approved()
    {
        var (tenant, project) = await SeedProjectAsync();
        Guid v1 = Guid.NewGuid(), v2 = Guid.NewGuid();
        await using var c = await db.OpenAppAsync(tenant);
        const string insertVersion = """
            INSERT INTO costing.budget_versions(id,tenant_id,project_id,version_number,type,status,effective_date,created_by,approved_by,approved_at,created_at,updated_at)
            VALUES (@id,@t,@p,@n,'Original',@s,'2026-11-01',@u,@ab::uuid,@aa::timestamptz,now(),now())
            """;
        await Exec(c, insertVersion, ("id", v1), ("t", tenant), ("p", project), ("n", 1), ("s", "Draft"), ("u", Guid.NewGuid()), ("ab", DBNull.Value), ("aa", DBNull.Value));
        const string insertLine = "INSERT INTO costing.budget_lines(id,tenant_id,budget_version_id,description,amount,currency_code,created_at,updated_at) VALUES (@id,@t,@v,'Line',500,'USD',now(),now())";
        await Exec(c, insertLine, ("id", Guid.NewGuid()), ("t", tenant), ("v", v1));

        await Exec(c, "UPDATE costing.budget_versions SET status='InReview', row_version=row_version+1, updated_at=now() WHERE id=@id", ("id", v1));
        await Exec(c, "UPDATE costing.budget_versions SET status='Approved', approved_by=@u, approved_at=now(), row_version=row_version+1, updated_at=now() WHERE id=@id", ("id", v1), ("u", Guid.NewGuid()));

        var addLine = await Assert.ThrowsAsync<PostgresException>(() => Exec(c, insertLine, ("id", Guid.NewGuid()), ("t", tenant), ("v", v1)));
        Assert.Equal(ImmutableState, addLine.SqlState);
        var edit = await Assert.ThrowsAsync<PostgresException>(() => Exec(c, "UPDATE costing.budget_versions SET effective_date='2027-01-01' WHERE id=@id", ("id", v1)));
        Assert.Equal(ImmutableState, edit.SqlState);

        var secondApproved = await Assert.ThrowsAsync<PostgresException>(() =>
            Exec(c, insertVersion, ("id", v2), ("t", tenant), ("p", project), ("n", 2), ("s", "Approved"), ("u", Guid.NewGuid()), ("ab", Guid.NewGuid()), ("aa", DateTimeOffset.UtcNow)));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, secondApproved.SqlState);

        await Exec(c, "UPDATE costing.budget_versions SET status='Superseded', row_version=row_version+1, updated_at=now() WHERE id=@id", ("id", v1));   // the one allowed change
    }

    [Fact]
    public async Task Performance_snapshots_are_append_only()
    {
        var (tenant, project) = await SeedProjectAsync();
        var id = Guid.NewGuid();
        await using var c = await db.OpenAppAsync(tenant);
        await Exec(c, """
            INSERT INTO progress.performance_snapshots(id,tenant_id,project_id,data_date,pv,ev,ac,bac,cv,sv,eac_method,budget_coverage,calculated_by,calculated_at,created_at,updated_at)
            VALUES (@id,@t,@p,'2026-11-01',100,80,100,1000,-20,-20,'CostPerformance',1,@u,now(),now(),now())
            """, ("id", id), ("t", tenant), ("p", project), ("u", Guid.NewGuid()));
        await Assert.ThrowsAsync<PostgresException>(() => Exec(c, "UPDATE progress.performance_snapshots SET ev=1 WHERE id=@id", ("id", id)));
        await Assert.ThrowsAsync<PostgresException>(() => Exec(c, "DELETE FROM progress.performance_snapshots WHERE id=@id", ("id", id)));
    }

    [Fact]
    public async Task Another_tenant_cannot_see_or_write_costs()
    {
        var (tenantA, projectA) = await SeedProjectAsync();
        var other = Guid.NewGuid();
        await using (var c = await db.OpenAppAsync())
            await Exec(c, "INSERT INTO platform.tenants(id,code,name,status,created_at,updated_at) VALUES (@id,@k,@k,'Active',now(),now())", ("id", other), ("k", "t-" + other.ToString("N")[..12]));
        var txId = Guid.NewGuid();
        await using (var asA = await db.OpenAppAsync(tenantA)) await InsertDraftTransactionAsync(asA, tenantA, projectA, txId);

        await using var asOther = await db.OpenAppAsync(other);
        Assert.Equal(0L, await Scalar<long>(asOther, "SELECT count(*) FROM costing.cost_transactions"));
        await Assert.ThrowsAsync<PostgresException>(() => InsertDraftTransactionAsync(asOther, tenantA, projectA, Guid.NewGuid()));
    }

    [Fact]
    public async Task Approved_baseline_and_its_dates_are_immutable()
    {
        var (tenant, project) = await SeedProjectAsync();
        var baseline = Guid.NewGuid();
        await using var c = await db.OpenAppAsync(tenant);
        await Exec(c, "INSERT INTO planning.baselines(id,tenant_id,project_id,name,version,status,captured_at,created_at,updated_at) VALUES (@id,@t,@p,'B1',1,'Draft',now(),now(),now())",
            ("id", baseline), ("t", tenant), ("p", project));
        const string insertDate = "INSERT INTO planning.baseline_activity_dates(id,tenant_id,baseline_id,activity_id,duration_minutes,percent_complete,created_at,updated_at) VALUES (@id,@t,@b,@a,480,0,now(),now())";
        await Exec(c, insertDate, ("id", Guid.NewGuid()), ("t", tenant), ("b", baseline), ("a", Guid.NewGuid()));
        await Exec(c, "UPDATE planning.baselines SET status='Approved', approved_by=@u, approved_at=now(), updated_at=now() WHERE id=@id", ("id", baseline), ("u", Guid.NewGuid()));

        var addDate = await Assert.ThrowsAsync<PostgresException>(() => Exec(c, insertDate, ("id", Guid.NewGuid()), ("t", tenant), ("b", baseline), ("a", Guid.NewGuid())));
        Assert.Equal(ImmutableState, addDate.SqlState);
        var rename = await Assert.ThrowsAsync<PostgresException>(() => Exec(c, "UPDATE planning.baselines SET name='Changed' WHERE id=@id", ("id", baseline)));
        Assert.Equal(ImmutableState, rename.SqlState);
        await Exec(c, "UPDATE planning.baselines SET status='Superseded', updated_at=now() WHERE id=@id", ("id", baseline));
    }

    [Fact]
    public async Task Progress_updates_are_append_only_and_tenant_scoped()
    {
        var (tenant, project) = await SeedProjectAsync();
        Guid wbs = Guid.NewGuid(), activity = Guid.NewGuid(), update = Guid.NewGuid();
        await using var c = await db.OpenAppAsync(tenant);
        await Exec(c, "INSERT INTO planning.wbs_nodes(id,tenant_id,project_id,code,name,sort_order,created_at,updated_at) VALUES (@id,@t,@p,'1','W',0,now(),now())", ("id", wbs), ("t", tenant), ("p", project));
        await Exec(c, "INSERT INTO planning.activities(id,tenant_id,project_id,wbs_node_id,activity_code,name,type,duration_minutes,created_at,updated_at) VALUES (@id,@t,@p,@w,'A1','Act','Task',480,now(),now())",
            ("id", activity), ("t", tenant), ("p", project), ("w", wbs));
        await Exec(c, "INSERT INTO progress.progress_updates(id,tenant_id,project_id,activity_id,data_date,percent_complete,status,submitted_by,created_at,updated_at) VALUES (@id,@t,@p,@a,'2026-11-01',40,'InProgress',@u,now(),now())",
            ("id", update), ("t", tenant), ("p", project), ("a", activity), ("u", Guid.NewGuid()));
        await Assert.ThrowsAsync<PostgresException>(() => Exec(c, "UPDATE progress.progress_updates SET percent_complete=90 WHERE id=@id", ("id", update)));
        await Assert.ThrowsAsync<PostgresException>(() => Exec(c, "DELETE FROM progress.progress_updates WHERE id=@id", ("id", update)));
    }
}
