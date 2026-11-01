# Company, Branch & Project Management — 0.3.0

## Delivered API surface

All routes except login/bootstrap/health require a validated JWT.

### Organization
- `GET /api/v1/organization/companies` — returns only companies where the signed-in user has an active membership.
- `POST /api/v1/organization/companies` — CompanyAdmin only; creates a company and grants the creator an active company membership.
- `GET /api/v1/organization/companies/{companyId}/branches` — active company members only.
- `POST /api/v1/organization/companies/{companyId}/branches` — CompanyAdmin only; company must exist in the authenticated tenant.

### Projects
- `GET /api/v1/projects?companyId={id}` — projects restricted to companies where the user has an active membership.
- `GET /api/v1/projects/{projectId}` — membership-scoped detail.
- `POST /api/v1/projects` — CompanyAdmin only; validates company and optional branch relationship.
- `PATCH /api/v1/projects/{projectId}/name` — CompanyAdmin only.
- `POST /api/v1/projects/{projectId}/activate|hold|complete|cancel` — CompanyAdmin only; domain lifecycle rules enforced.
- `GET /api/v1/dashboard/summary` — membership-scoped project counts by status.

## Authorization boundary

The initial role set remains the role created by the existing bootstrap (`CompanyAdmin`). No undocumented role names are assumed. Read operations are scoped through `UserMembership`; write operations in this increment are restricted to CompanyAdmin. Fine-grained project-level roles and invitation flows are intentionally deferred to the next increment.

## Tenant isolation

- `TenantContext.TenantId` is read from the validated JWT `tenant_id` claim during authenticated requests.
- Tenant-scoped EF Core query filters remain enabled.
- The existing PostgreSQL RLS script remains a second isolation layer; apply it only after schema migrations exist.
- Company/branch/project foreign-key relationships enforce tenant and company consistency at the database level.

## Important limitations / release gates

This increment is a development starter, not production-ready. It does not include EF Core migrations, API integration tests, project membership, fine-grained permission policies, invitation/email verification, audit events for these endpoints, or rate limiting. Bootstrap still uses a deployment secret and must be run once in a controlled environment. Configure secrets outside source control. Before production: generate and review migrations, apply RLS with a non-owner application DB role, add cross-tenant integration tests, run `dotnet restore/build/test`, and perform a security review.
