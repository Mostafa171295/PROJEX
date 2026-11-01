using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Projex.IntegrationTests;

/// <summary>End-to-end through the real API against real PostgreSQL as the restricted app role.</summary>
[Collection(ApiCollection.Name)]   // API test classes set process-wide environment variables, so they must not run in parallel
public class ApiSmokeTests : IClassFixture<DatabaseFixture>, IDisposable
{
    private const string BootstrapKey = "test-bootstrap-key-0123456789", Password = "Str0ng!Passw0rd#2026", PlatformEmail = "root@projex.test";
    private readonly WebApplicationFactory<Program> _factory;

    private readonly DatabaseFixture _db;

    public ApiSmokeTests(DatabaseFixture db)
    {
        _db = db;
        // Program.cs reads configuration eagerly, so use environment variables (visible before the host is built).
        Environment.SetEnvironmentVariable("ConnectionStrings__Projex", db.AppPooledConnectionString);
        Environment.SetEnvironmentVariable("Authentication__JwtSigningKey", "integration-test-signing-key-at-least-32-bytes!!");
        Environment.SetEnvironmentVariable("Bootstrap__Key", BootstrapKey);
        Environment.SetEnvironmentVariable("Security__AuthRequestsPerMinute", "10000");   // the rate limiter has its own test; do not trip it here
        _factory = new WebApplicationFactory<Program>();
    }

    public void Dispose() => _factory.Dispose();

    private HttpClient Authed(string token)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private static async Task<string> TokenAsync(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task Health_is_public_and_planning_requires_authentication()
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/planning/schedule/gantt")).StatusCode);
    }

    [Fact]
    public async Task Platform_admin_provisions_tenants_and_the_tenant_signs_in()
    {
        var client = _factory.CreateClient();

        // Bootstrap creates the first PLATFORM administrator once; it does not create a tenant.
        Assert.Equal(HttpStatusCode.NotFound, (await Bootstrap(client, "wrong")).StatusCode);
        var first = await Bootstrap(client, BootstrapKey);
        Assert.True(first.StatusCode == HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await Bootstrap(client, BootstrapKey)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = PlatformEmail, password = "wrong" })).StatusCode);
        var platform = Authed(await TokenAsync(await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = PlatformEmail, password = Password })));

        // More than one tenant can now exist (the old one-shot bootstrap allowed exactly one).
        var acme = await platform.PostAsJsonAsync("/api/v1/platform/tenants", Provision("acme", "admin@acme.test", seatLimit: 2));
        Assert.True(acme.StatusCode == HttpStatusCode.Created, await acme.Content.ReadAsStringAsync());
        var acmeId = (await acme.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
        Assert.Equal(HttpStatusCode.Created, (await platform.PostAsJsonAsync("/api/v1/platform/tenants", Provision("globex", "admin@globex.test", seatLimit: 5))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await platform.PostAsJsonAsync("/api/v1/platform/tenants", Provision("acme", "other@acme.test", seatLimit: 5))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await platform.PostAsJsonAsync("/api/v1/platform/tenants", Provision("Bad Code!", "x@acme.test", seatLimit: 5))).StatusCode);
        Assert.Equal(2, (await platform.GetFromJsonAsync<JsonElement>("/api/v1/platform/tenants")).GetArrayLength());

        // The tenant administrator signs in with the tenant code. Same email in another tenant is a separate account.
        var acmeAdmin = Authed(await TokenAsync(await client.PostAsJsonAsync("/api/v1/auth/login", new { tenantKey = "acme", email = "admin@acme.test", password = Password })));
        var companies = await acmeAdmin.GetFromJsonAsync<JsonElement>("/api/v1/organization/companies");
        Assert.Equal(1, companies.GetArrayLength());
        Assert.Equal("MAIN", companies[0].GetProperty("code").GetString());
        var companyId = companies[0].GetProperty("id").GetGuid();

        // Platform tokens cannot reach tenant data, and tenant tokens cannot reach platform administration.
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.GetAsync("/api/v1/organization/companies")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await acmeAdmin.GetAsync("/api/v1/platform/tenants")).StatusCode);

        // Seat limit (2): the administrator plus one more user; the third is refused.
        Assert.Equal(HttpStatusCode.Created, (await acmeAdmin.PostAsJsonAsync("/api/v1/identity/users", new { email = "eng1@acme.test", password = Password, companyId })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await acmeAdmin.PostAsJsonAsync("/api/v1/identity/users", new { email = "eng2@acme.test", password = Password, companyId })).StatusCode);
        // The seat limit cannot be lowered below the active users; raising it lets the user in.
        Assert.Equal(HttpStatusCode.Conflict, (await platform.PutAsJsonAsync($"/api/v1/platform/tenants/{acmeId}/subscription",
            new { planCode = "pro", status = "Active", seatLimit = 1 })).StatusCode);
        var increasedSubscription = await platform.PutAsJsonAsync($"/api/v1/platform/tenants/{acmeId}/subscription",
            new { planCode = "pro", status = "Active", seatLimit = 5 });
        Assert.Equal(HttpStatusCode.OK, increasedSubscription.StatusCode);
        Assert.Equal(5, (await increasedSubscription.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("seatLimit").GetInt32());
        var secondEngineer = await acmeAdmin.PostAsJsonAsync("/api/v1/identity/users", new { email = "eng2@acme.test", password = Password, companyId });
        Assert.True(secondEngineer.StatusCode == HttpStatusCode.Created, $"Expected Created after increasing the seat limit, got {secondEngineer.StatusCode}: {await secondEngineer.Content.ReadAsStringAsync()}");

        // FR-AUTH-005: five failures lock the account; even the right password is then refused.
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await client.PostAsJsonAsync("/api/v1/auth/login", new { tenantKey = "acme", email = "eng1@acme.test", password = "wrong-" + i })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/v1/auth/login", new { tenantKey = "acme", email = "eng1@acme.test", password = Password })).StatusCode);

        // FR-AUTH-006: sign-ins (successful, failed, locked out) are in the tenant's audit trail.
        await using (var conn = await _db.OpenAppAsync(acmeId))
        {
            Assert.True(await DatabaseFixture.Scalar<long>(conn, "SELECT count(*) FROM audit.audit_logs WHERE action='auth.login.succeeded'") >= 1);
            // The fifth failed password attempt is recorded separately as auth.login.locked_out.
            Assert.True(await DatabaseFixture.Scalar<long>(conn, "SELECT count(*) FROM audit.audit_logs WHERE action='auth.login.failed'") >= 4);
            Assert.True(await DatabaseFixture.Scalar<long>(conn, "SELECT count(*) FROM audit.audit_logs WHERE action='auth.login.locked_out'") >= 1);
        }

        // Suspending the tenant cuts off an already-issued token and new sign-ins; platform actions are audited.
        Assert.Equal(HttpStatusCode.OK, (await acmeAdmin.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await platform.PostAsync($"/api/v1/platform/tenants/{acmeId}/suspend", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await acmeAdmin.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/v1/auth/login", new { tenantKey = "acme", email = "admin@acme.test", password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await platform.PostAsync($"/api/v1/platform/tenants/{acmeId}/activate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/login", new { tenantKey = "acme", email = "admin@acme.test", password = Password })).StatusCode);

        await using var platformConn = await _db.OpenAppAsync();
        Assert.True(await DatabaseFixture.Scalar<long>(platformConn, "SELECT count(*) FROM platform.audit_events WHERE action='platform.tenant.created'") >= 2);
        Assert.True(await DatabaseFixture.Scalar<long>(platformConn, "SELECT count(*) FROM platform.audit_events WHERE action='platform.tenant.suspended'") >= 1);
    }

    [Fact]
    public async Task Credential_endpoints_are_rate_limited_per_client()
    {
        Environment.SetEnvironmentVariable("Security__AuthRequestsPerMinute", "3");
        try
        {
            await using var limited = new WebApplicationFactory<Program>();
            var client = limited.CreateClient();
            var statuses = new List<HttpStatusCode>();
            for (var i = 0; i < 6; i++)
                statuses.Add((await client.PostAsJsonAsync("/api/v1/auth/login", new { tenantKey = "nobody", email = "a@b.test", password = "x" })).StatusCode);
            Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
            Assert.Equal(HttpStatusCode.Unauthorized, statuses[0]);
        }
        finally { Environment.SetEnvironmentVariable("Security__AuthRequestsPerMinute", "10000"); }
    }

    private static Task<HttpResponseMessage> Bootstrap(HttpClient client, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/platform/bootstrap")
            { Content = JsonContent.Create(new { email = PlatformEmail, displayName = "Root", password = Password }) };
        request.Headers.Add("X-Bootstrap-Key", key);
        return client.SendAsync(request);
    }

    private static object Provision(string code, string adminEmail, int seatLimit) => new
    {
        tenantCode = code, tenantName = code.ToUpperInvariant(), companyCode = "MAIN", companyName = code + " Main",
        adminEmail, adminPassword = Password, planCode = "trial", seatLimit
    };
}
