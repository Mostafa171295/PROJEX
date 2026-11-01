# PROJEX ENTERPRISE 0.6.0 — Integrated Planning & Scheduling

## Scope delivered in this increment

- Persisted WBS nodes with project/tenant-aware parent constraints.
- Persisted activities with WBS and optional project-calendar assignment.
- Persisted FS/SS/FF/SF activity relationships and signed lag in minutes.
- Project calendar definitions and exception records in the domain/data model.
- Persisted CPM runs, per-activity early/late working-day offsets, total float and critical flag.
- Draft schedule baselines with immutable snapshot rows created from the current activity state.
- API endpoints for WBS, activities, relationships, calendar creation, schedule calculation/latest run, and baseline capture.
- Tenant filters, tenant-aware foreign keys where supported, and PostgreSQL RLS policies for the new planning tables.
- Domain tests for duration, milestones, progress bounds, self-links, baseline approval and calendar exception validation.

## API surface

All endpoints require authentication and project planning authorization (CompanyAdmin, PROJECTMANAGER, or PLANNINGENGINEER, plus active company membership).

- `GET /api/v1/projects/{projectId}/planning/wbs`
- `POST /api/v1/projects/{projectId}/planning/wbs`
- `GET /api/v1/projects/{projectId}/planning/activities?wbsNodeId={id}`
- `POST /api/v1/projects/{projectId}/planning/activities`
- `PUT /api/v1/projects/{projectId}/planning/activities/{activityId}` (optimistic concurrency via `ExpectedRowVersion`)
- `POST /api/v1/projects/{projectId}/planning/relationships`
- `POST /api/v1/projects/{projectId}/planning/calendars`
- `GET /api/v1/projects/{projectId}/planning/calendars`
- `POST /api/v1/projects/{projectId}/planning/calendars/{calendarId}/exceptions`
- `PUT /api/v1/projects/{projectId}/planning/activities/{activityId}/progress`
- `POST /api/v1/projects/{projectId}/planning/schedule/calculate`
- `GET /api/v1/projects/{projectId}/planning/schedule/latest`
- `POST /api/v1/projects/{projectId}/planning/baselines`
- `POST /api/v1/projects/{projectId}/planning/baselines/{baselineId}/approve`

## Scheduling units and limitations

The existing CPM engine uses normalized working-day units. This increment converts duration and lag from minutes using an 8-hour (480-minute) working day. It persists the calculated offsets and results. Calendar definitions are persisted but are **not yet applied** to map offsets onto real calendar dates. Calendar-aware date placement, working-time calculations, resource leveling, baseline approval workflow, and schedule change event/outbox processing remain subsequent increments; do not represent these capabilities as complete.

## Database migration

Apply `database/migrations/005_planning_scheduling_060.sql` after the Starter 0.5.0 schema. The migration adds the planning schema and tenant RLS policies. Review the existing database for a pre-existing constraint named `uq_projects_tenant_id` before applying. Apply migrations through a controlled deployment process, not manually in production.

## Verification status

The current execution environment did not provide the .NET SDK or a PostgreSQL server. Therefore `dotnet build`, `dotnet test`, migration application, API integration tests, and RLS penetration tests have **not** been run. Run them in a .NET 10 environment against PostgreSQL before accepting this increment.

## Suggested verification commands

```bash
dotnet restore Projex.slnx
dotnet build Projex.slnx --no-restore
dotnet test Projex.slnx --no-build
```

## Next increment

- Complete calendar CRUD and exception API.
- Apply working-week and exception calendars to schedule date placement.
- Add activity progress update endpoint and audit events.
- Add baseline approval and comparison/variance APIs.
- Add integration tests with PostgreSQL and RLS enabled.
