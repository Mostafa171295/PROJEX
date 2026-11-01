# Authentication & Multi-Tenant Foundation — 0.2.0

## Delivered
- Tenant, Company and Branch domain entities.
- Tenant-scoped user, role, membership and permission model foundations.
- ASP.NET Core Identity password hashing and lockout policy.
- JWT bearer login. A login request must include the tenant key; the user lookup is constrained to that tenant.
- JWT carries `tenant_id`; the tenant context is resolved from the validated principal, not from a caller-controlled tenant header.
- EF Core tenant query filters and write-boundary checks for tenant-owned rows.
- PostgreSQL session variable `app.tenant_id` set from the resolved tenant context on connection open.
- Optional PostgreSQL RLS policies in `database/rls/001_enable_tenant_rls.sql`.
- One-time platform bootstrap endpoint to create the first tenant, company and CompanyAdmin user.

## First-time setup
Configure these values through environment variables or a secret manager:

```text
ConnectionStrings__Projex=<PostgreSQL connection string>
Authentication__JwtSigningKey=<random secret, at least 32 bytes>
Bootstrap__Key=<separate random one-time bootstrap secret>
```

For local development, the sample appsettings contains a development-only JWT key. Never use it outside a local development environment. The bootstrap key is intentionally not included in source control.

Start the API and call `POST /api/v1/platform/bootstrap` once with `X-Bootstrap-Key`. Example body:

```json
{
  "tenantKey": "acme",
  "tenantName": "Acme Group",
  "companyCode": "HQ",
  "companyName": "Acme Main Company",
  "adminEmail": "admin@example.com",
  "adminPassword": "Use-A-Unique-Strong-Password-2026!"
}
```

The bootstrap operation is rejected after a tenant exists. It creates the initial CompanyAdmin identity role and user. Disable/remove the bootstrap key after successful initialization.

Login: `POST /api/v1/auth/login`

```json
{ "tenantKey": "acme", "email": "admin@example.com", "password": "..." }
```

Use the returned bearer token for `GET /api/v1/auth/me`.

## Security boundaries and limitations
- The tenant key is a tenant locator, not an authentication factor. Password verification is still mandatory.
- `IgnoreQueryFilters()` is used only in the login lookup after the tenant is resolved, and the predicate explicitly includes that tenant ID. PostgreSQL RLS provides a second boundary when enabled.
- The bootstrap endpoint must be protected by a high-entropy secret, called only once, and disabled operationally afterward. Do not expose it publicly without network-level restrictions.
- RLS script is a deployment step and must be applied only after the EF schema exists. Test it using the actual non-owner application database role; PostgreSQL superusers and roles with `BYPASSRLS` bypass policies.
- The application database role should not own tenant tables and must not have `BYPASSRLS`.
- Tenant query filters are defense in depth, not a substitute for database permissions and RLS. Raw SQL and future bulk operations require explicit tenant review.
- Company/branch/project authorization and per-project RBAC enforcement are not complete yet. The initial CompanyAdmin role is a bootstrap identity role only.
- Email verification, MFA, refresh-token rotation/revocation, password reset, invitation workflows, audit events and account recovery remain future work.
- Before production, add integration tests proving cross-tenant reads/writes fail with RLS enabled, plus secret rotation and security review.

## Next implementation slice
1. Tenant-aware company/branch/project API endpoints.
2. Role-permission assignment and authorization policies at company, branch and project scopes.
3. EF Core migrations and automated PostgreSQL RLS integration tests.
4. Refresh token/session management, MFA and audit logging.
