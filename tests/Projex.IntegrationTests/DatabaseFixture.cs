using Npgsql;
using Testcontainers.PostgreSql;

namespace Projex.IntegrationTests;

/// <summary>
/// Starts a real PostgreSQL 16, creates the owner/app roles exactly like database/init/000_roles.sh,
/// and applies every file in database/migrations as the owner role. Requires Docker.
/// </summary>
public class DatabaseFixture : IAsyncLifetime
{
    public const string OwnerPassword = "owner-test-pw-1", AppPassword = "app-test-pw-1", Db = "projex_enterprise";
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16").WithDatabase(Db).Build();

    public string OwnerConnectionString { get; private set; } = "";
    /// <summary>Non-pooled so session-level tenant settings never leak between tests.</summary>
    public string AppConnectionString { get; private set; } = "";
    public string AppPooledConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        var admin = new NpgsqlConnectionStringBuilder(_pg.GetConnectionString());
        await using (var conn = new NpgsqlConnection(admin.ConnectionString))
        {
            await conn.OpenAsync();
            await Exec(conn, $"""
                CREATE ROLE projex_owner LOGIN PASSWORD '{OwnerPassword}' NOSUPERUSER NOBYPASSRLS NOCREATEROLE;
                CREATE ROLE projex_app LOGIN PASSWORD '{AppPassword}' NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB;
                GRANT CREATE, CONNECT ON DATABASE {Db} TO projex_owner;
                GRANT CONNECT ON DATABASE {Db} TO projex_app;
                GRANT CREATE ON SCHEMA public TO projex_owner;
                """);
        }
        var owner = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Username = "projex_owner", Password = OwnerPassword, Pooling = false };
        OwnerConnectionString = owner.ConnectionString;
        var app = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Username = "projex_app", Password = AppPassword, Pooling = false };
        AppConnectionString = app.ConnectionString;
        AppPooledConnectionString = new NpgsqlConnectionStringBuilder(app.ConnectionString) { Pooling = true }.ConnectionString;

        await using var o = new NpgsqlConnection(OwnerConnectionString);
        await o.OpenAsync();
        foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot(), "database", "migrations"), "*.sql").OrderBy(f => f, StringComparer.Ordinal))
        {
            try { await Exec(o, await File.ReadAllTextAsync(file)); }
            catch (Exception ex) { throw new InvalidOperationException($"Migration {Path.GetFileName(file)} failed: {ex.Message}", ex); }
        }
    }

    public async Task DisposeAsync() => await _pg.DisposeAsync();

    public async Task<NpgsqlConnection> OpenAppAsync(Guid? tenantId = null)
    {
        var conn = new NpgsqlConnection(AppConnectionString);
        await conn.OpenAsync();
        if (tenantId is not null)
            await Exec(conn, "SELECT set_config('app.tenant_id', @t, false)", ("t", tenantId.Value.ToString()));
        return conn;
    }

    public static async Task<int> Exec(NpgsqlConnection conn, string sql, params (string Name, object Value)[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        return await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<T> Scalar<T>(NpgsqlConnection conn, string sql, params (string Name, object Value)[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Projex.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Projex.slnx not found above the test output directory.");
    }
}
