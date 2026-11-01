using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Projex.IntegrationTests;

/// <summary>Roles, custom roles and scoped assignments end to end (SRS 3.2).</summary>
[Collection(ApiCollection.Name)]
public class RbacApiTests : IClassFixture<DatabaseFixture>, IDisposable
{
    private const string BootstrapKey = "test-bootstrap-key-0123456789", Password = "Str0ng!Passw0rd#2026";
    private readonly WebApplicationFactory<Program> _factory;

    public RbacApiTests(DatabaseFixture db)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Projex", db.AppPooledConnectionString);
        Environment.SetEnvironmentVariable("Authentication__JwtSigningKey", "integration-test-signing-key-at-least-32-bytes!!");
        Environment.SetEnvironmentVariable("Bootstrap__Key", BootstrapKey);
        Environment.SetEnvironmentVariable("Security__AuthRequestsPerMinute", "10000");
        _factory = new WebApplicationFactory<Program>();
    }

    public void Dispose() => _factory.Dispose();

    private static HttpClient With(HttpClient c, string token) { c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); return c; }

    private static async Task<JsonElement> OkJson(HttpResponseMessage r)
    {
        Assert.True(r.IsSuccessStatusCode, r.StatusCode + " " + await r.Content.ReadAsStringAsync());
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<(HttpClient Admin, Guid AdminId, Guid CompanyId)> NewTenantAsync(string code)
    {
        var client = _factory.CreateClient();
        var boot = new HttpRequestMessage(HttpMethod.Post, "/api/v1/platform/bootstrap") { Content = JsonContent.Create(new { email = "root@projex.test", password = Password }) };
        boot.Headers.Add("X-Bootstrap-Key", BootstrapKey);
        var bootStatus = (await client.SendAsync(boot)).StatusCode;
        Assert.True(bootStatus is HttpStatusCode.Created or HttpStatusCode.Conflict);
        var platform = With(_factory.CreateClient(), (await OkJson(await client.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "root@projex.test", password = Password }))).GetProperty("accessToken").GetString()!);
        Assert.Equal(HttpStatusCode.Created, (await platform.PostAsJsonAsync("/api/v1/platform/tenants", new
        { tenantCode = code, tenantName = code, companyCode = "MAIN", companyName = "Main", adminEmail = $"admin@{code}.test", adminPassword = Password, planCode = "pro", seatLimit = 20 })).StatusCode);
        var login = await OkJson(await client.PostAsJsonAsync("/api/v1/auth/login", new { tenantKey = code, email = $"admin@{code}.test", password = Password }));
        var admin = With(_factory.CreateClient(), login.GetProperty("accessToken").GetString()!);
        var companyId = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/organization/companies"))[0].GetProperty("id").GetGuid();
        return (admin, login.GetProperty("userId").GetGuid(), companyId);
    }

    [Fact]
    public async Task System_roles_custom_roles_and_scoped_assignments()
    {
        var (admin, adminId, companyId) = await NewTenantAsync("rbac");

        // The catalog is visible: 8 built-in roles, with their permissions.
        var roles = await admin.GetFromJsonAsync<JsonElement>("/api/v1/identity/roles");
        Assert.Equal(8, roles.GetArrayLength());
        Assert.Equal(12, (await admin.GetFromJsonAsync<JsonElement>("/api/v1/identity/permissions")).GetArrayLength());

        // Custom role rules: reserved code, unknown permission, a permission the server does not enforce yet.
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/v1/identity/roles", new { code = "VIEWER", name = "x", permissions = new[] { "projects.read" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/identity/roles", new { code = "BAD_ONE", name = "x", permissions = new[] { "nope.nope" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/identity/roles", new { code = "BAD_TWO", name = "x", permissions = new[] { "identity.manage" } })).StatusCode);
        var custom = await OkJson(await admin.PostAsJsonAsync("/api/v1/identity/roles", new { code = "senior_planner", name = "Senior Planner", permissions = new[] { "projects.read", "planning.manage" } }));
        var customId = custom.GetProperty("id").GetGuid();
        Assert.Equal("SENIOR_PLANNER", custom.GetProperty("code").GetString());

        // A project and an engineer with no membership: the engineer sees nothing.
        var project = await OkJson(await admin.PostAsJsonAsync("/api/v1/projects", new { companyId, code = "P1", name = "Ring Road", currencyCode = "USD", timeZone = "UTC" }));
        var projectId = project.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/v1/identity/users", new { email = "eng@rbac.test", password = Password, companyId })).StatusCode);
        var anon = _factory.CreateClient();
        var engLogin = await OkJson(await anon.PostAsJsonAsync("/api/v1/auth/login", new { tenantKey = "rbac", email = "eng@rbac.test", password = Password }));
        var engId = engLogin.GetProperty("userId").GetGuid();
        var eng = With(_factory.CreateClient(), engLogin.GetProperty("accessToken").GetString()!);
        Assert.Equal(0, (await eng.GetFromJsonAsync<JsonElement>("/api/v1/projects")).GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await eng.GetAsync("/api/v1/identity/roles")).StatusCode);   // not an administrator

        // Scoped assignment: the custom role on exactly this project makes it visible. Scope rules are validated.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/v1/identity/users/{engId}/roles", new { roleId = customId, companyId, projectId })).StatusCode);
        var assigned = await admin.PostAsJsonAsync($"/api/v1/identity/users/{engId}/roles", new { roleId = customId, projectId });
        Assert.Equal(HttpStatusCode.Created, assigned.StatusCode);
        var assignmentId = (await assigned.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/v1/identity/users/{engId}/roles", new { roleId = customId, projectId })).StatusCode);
        Assert.Equal(1, (await eng.GetFromJsonAsync<JsonElement>("/api/v1/projects")).GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await eng.GetAsync($"/api/v1/projects/{projectId}")).StatusCode);

        // A role that is still assigned cannot be deleted; after revoking it can, and visibility goes away.
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/v1/identity/roles/{customId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/identity/users/{engId}/roles/{assignmentId}")).StatusCode);
        Assert.Equal(0, (await eng.GetFromJsonAsync<JsonElement>("/api/v1/projects")).GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/identity/roles/{customId}")).StatusCode);

        // A built-in role scoped to the company: a Viewer sees the company's projects only through the assignment.
        var viewerRoleId = roles.EnumerateArray().First(r => r.GetProperty("code").GetString() == "VIEWER").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"/api/v1/identity/users/{engId}/roles", new { roleId = viewerRoleId, companyId })).StatusCode);
        Assert.Equal(1, (await eng.GetFromJsonAsync<JsonElement>("/api/v1/projects")).GetArrayLength());

        // Tenant-wide role removal takes effect at once: the user's sessions are revoked.
        var wide = await OkJson(await admin.PostAsJsonAsync($"/api/v1/identity/users/{engId}/roles", new { roleId = viewerRoleId }));
        Assert.Equal(HttpStatusCode.OK, (await eng.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/identity/users/{engId}/roles/{wide.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await eng.GetAsync("/api/v1/auth/me")).StatusCode);

        // The only company administrator cannot lose the role or be disabled.
        var adminRoles = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/identity/users/{adminId}/roles");
        var adminAssignment = adminRoles.EnumerateArray().First(r => r.GetProperty("roleCode").GetString() == "COMPANYADMIN").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/v1/identity/users/{adminId}/roles/{adminAssignment}")).StatusCode);
    }

    [Fact]
    public async Task Another_tenants_roles_and_assignments_are_invisible()
    {
        var (adminA, _, _) = await NewTenantAsync("rbac-a");
        var custom = await OkJson(await adminA.PostAsJsonAsync("/api/v1/identity/roles", new { code = "A_PRIVATE", name = "A private", permissions = new[] { "projects.read" } }));
        var (adminB, adminBId, _) = await NewTenantAsync("rbac-b");

        var rolesB = await adminB.GetFromJsonAsync<JsonElement>("/api/v1/identity/roles");
        Assert.DoesNotContain(rolesB.EnumerateArray(), r => r.GetProperty("code").GetString() == "A_PRIVATE");
        Assert.Equal(HttpStatusCode.BadRequest, (await adminB.PostAsJsonAsync($"/api/v1/identity/users/{adminBId}/roles", new { roleId = custom.GetProperty("id").GetGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminB.DeleteAsync($"/api/v1/identity/roles/{custom.GetProperty("id").GetGuid()}")).StatusCode);
    }
}
