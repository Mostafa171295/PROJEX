# PROJEX ENTERPRISE 0.4.0 — Project Access Management

## Delivered
- ProjectMembership entity with tenant, project, user, role code, active state, role changes and revocation.
- Project list, project details and dashboard are now restricted to active project members, with CompanyAdmin visibility retained within their company.
- GET `/api/v1/projects/{projectId}/members`: members visible only to active members of the owning company.
- PUT `/api/v1/projects/{projectId}/members/{userId}`: CompanyAdmin assigns/changes a project role for an active user who already belongs to the project's company.
- DELETE `/api/v1/projects/{projectId}/members/{userId}`: CompanyAdmin revokes project access.
- Allowlisted roles: PROJECTMANAGER, PLANNINGENGINEER, COSTCONTROLENGINEER, SITEENGINEER, VIEWER.
- SQL starter includes uniqueness, index and tenant RLS.

## Deliberate limits
- Assigns existing tenant users only. Invitations, email delivery and password setup are not configured.
- Existing project read/write endpoints still require a follow-up authorization pass to enforce these project-level roles on every operation.
- SQL is a starter script, not an EF Core migration. Validate schema names and migration order before applying.
- Do not deploy before EF migrations, cross-tenant/cross-project integration tests, Build/Test and security review.

## Example
`PUT /api/v1/projects/{projectId}/members/{userId}` with body `{ "roleCode": "PLANNINGENGINEER" }`.
