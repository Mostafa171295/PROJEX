# PROJEX ENTERPRISE

> **Current source snapshot:** `0.9.2-preview.1` (development preview).
> Build, automated tests, PostgreSQL migrations, and production security validation have
> not yet been independently verified for this archive. Treat this as a development
> preview, not a production-ready release.

Cross-platform enterprise project management platform.

## Technology baseline
- .NET 10 (LTS)
- C# 14
- ASP.NET Core Web API
- Avalonia UI (desktop shell)
- PostgreSQL + EF Core
- Modular monolith architecture

## Repository layout
- `src/Projex.Api` — HTTP API host
- `src/Projex.Desktop` — cross-platform desktop shell
- `src/Projex.Domain` — core domain entities and rules
- `src/Projex.Application` — use cases and contracts
- `src/Projex.Infrastructure` — persistence and infrastructure
- `tests/Projex.Domain.Tests` — domain tests
- `docs` — product and architecture documents

## Repository checks

GitHub Actions runs the .NET restore/build/test pipeline on pushes and pull requests.
CodeQL performs scheduled C# security analysis, and Dependabot checks NuGet and
GitHub Actions dependencies weekly. Repository branch protection must be enabled
in GitHub Settings; see `GITHUB_SETUP.md`.

## Prerequisites
Install the .NET 10 SDK and PostgreSQL 16+.

## Build
```bash
dotnet restore
dotnet build
dotnet test
```

## Run the API
```bash
dotnet run --project src/Projex.Api
```

The API exposes OpenAPI in Development. The initial health endpoint is:
`GET /health`

## Important security note
This repository is a development preview. Authentication, tenant isolation, PostgreSQL RLS, migrations, logging, backups, and security controls require independent verification before production use. See `SECURITY.md` and the validation notes in `docs/`.

## Authentication & tenant setup (0.2.0)
The API now includes ASP.NET Core Identity password hashing, tenant-scoped login and JWT bearer authentication.

1. Start PostgreSQL with `docker compose up -d`.
2. Set `Bootstrap__Key` to a strong one-time secret and set `Authentication__JwtSigningKey` to a separate random secret (32+ bytes). The development JWT key exists only in `appsettings.Development.json` and must never be used in production.
3. Start the API, then call `POST /api/v1/platform/bootstrap` (header `X-Bootstrap-Key`, body `email`, `displayName`, `password`) to create the first **platform administrator**. It succeeds only while no platform administrator exists. Sign in at `POST /api/v1/platform/auth/login`, then create customers with `POST /api/v1/platform/tenants` (tenant, first company, first CompanyAdmin and licence in one call). See `docs/PROJEX_0.9.2_IDENTITY_PLATFORM_RBAC.md`.
4. Tenant users sign in at `POST /api/v1/auth/login` using `tenantKey` (the tenant code), `email` and `password`; use the returned token as a bearer token.
5. RLS is applied by `database/migrations/002_enable_tenant_rls.sql` (see *Database setup* below).

See `docs/security/AUTHENTICATION_AND_TENANCY_0.2.md` for endpoint examples, security boundaries and unfinished controls. EF migrations and PostgreSQL integration tests are not included yet.

## 0.3.0 — Organization & Project APIs

This increment adds tenant-membership-scoped company and project reads, CompanyAdmin-only organization/project writes, branch validation, project lifecycle endpoints, and a basic project dashboard summary. See `docs/organization/COMPANY_PROJECT_MANAGEMENT_0.3.md` for the route list and security limitations.


## Starter 0.4.0 — Project Access
Adds project-scoped membership assignments and CompanyAdmin APIs for assigning, changing and revoking roles for existing company users. See `docs/security/PROJECT_ACCESS_MANAGEMENT_0.4.md`.


## Starter 0.5.0 — Scheduling Engine Foundation
Adds a standalone CPM calculation library with forward/backward pass, total float, critical-path identification, cycle/reference validation and FS/SS/FF/SF relationship support. An authorized planning endpoint accepts a schedule network and returns the calculated result. Durations/lags are normalized working-day units; calendars, persisted WBS/activities, constraints, baselines and Gantt UI are not yet included.


## PROJEX 0.6.0 — Integrated Planning & Scheduling

Adds tenant-scoped persistent WBS, activities, activity relationships (FS/SS/FF/SF), project calendars and calendar exceptions, progress updates, persisted CPM calculation runs, and draft/approved schedule baselines. Planning endpoints are project-authorized and use optimistic concurrency for activity edits. The calculation engine currently normalizes 480 minutes to one working day; stored calendar definitions are not yet applied to real date placement.

Apply `database/migrations/005_planning_scheduling_060.sql` after the Starter 0.5.0 schema, then run the API. Route details and known limitations are documented in `docs/PROJEX_0.6.0_IMPLEMENTATION.md`.

**Verification warning:** this source package was prepared without a .NET 10 SDK or PostgreSQL server in the build environment. Restore/build/test, migration execution, and PostgreSQL RLS integration tests must be completed before deployment.

## PROJEX 0.6.1 — Calendar-aware scheduling

The scheduling API now calculates CPM in **working minutes**, places early/late dates on a project working calendar, persists `planned_start` / `planned_finish`, and exposes a Gantt-ready read model.

### Calendar JSON contract

`workingWeekJson` is a JSON object with English day names. Omitted days are non-working days. Each day contains one or more non-overlapping intervals:

```json
{
  "monday": [
    { "start": "08:00", "end": "12:00" },
    { "start": "13:00", "end": "17:00" }
  ],
  "tuesday": [
    { "start": "08:00", "end": "12:00" },
    { "start": "13:00", "end": "17:00" }
  ],
  "wednesday": [
    { "start": "08:00", "end": "12:00" },
    { "start": "13:00", "end": "17:00" }
  ],
  "thursday": [
    { "start": "08:00", "end": "12:00" },
    { "start": "13:00", "end": "17:00" }
  ],
  "friday": [
    { "start": "08:00", "end": "12:00" },
    { "start": "13:00", "end": "17:00" }
  ]
}
```

Calendar exceptions override the weekly pattern. A non-working exception is a holiday; a working exception may provide custom `startTime` / `endTime`, or inherit that weekday's intervals.

### Scheduling endpoints

- `POST /api/v1/projects/{projectId}/planning/schedule/calculate?startDate=2026-11-02` — calculate CPM and persist calendar-aware planned dates. `startDate` is optional; the project's calendar-local current date is used by default.
- `GET /api/v1/projects/{projectId}/planning/schedule/gantt` — returns WBS-labelled activities, planned dates, progress, critical flags, float and dependency links for a Gantt client.
- `GET /api/v1/projects/{projectId}/planning/schedule/latest` — returns the latest persisted calculation snapshot.

If no project calendar is assigned, the engine uses a standard Monday–Friday 08:00–12:00 / 13:00–17:00 calendar in the project's time zone. For mathematically consistent CPM, all activities in one schedule network must resolve to one common calendar in 0.6.1. Mixed calendars and resource leveling are deliberately deferred to a later scheduling-engine increment.

No database migration is required for 0.6.1: the schedule date and calendar columns were introduced in migration `005_planning_scheduling_060.sql`.

## PROJEX 0.6.2 — Advanced Project Control

This release extends the 0.6.1 planning foundation with a Gantt data contract and approved-baseline overlay, baseline variance reporting, bounded downstream dependency impact analysis, and schedule health/freshness reporting. See `docs/PROJEX_0.6.2_ADVANCED_PROJECT_CONTROL.md` for endpoint contracts and limitations.

## PROJEX 0.7.0-preview.2 — Desktop Gantt presentation slice

Adds a dedicated Planning & Scheduling desktop window reachable from the workspace navigation. The initial timeline view presents activity rows, a calendar scale, dependency connectors, critical-path highlighting, progress overlays, baseline comparison bars, zoom controls, and activity selection details.

**Preview boundary:** the desktop Gantt currently uses clearly labeled illustrative sample data. It is not yet API-backed and does not persist drag/drop edits. The API duration-edit contract from preview.1 remains concurrency-protected and marks the schedule stale; API-backed Gantt loading and persisted editing are the next implementation slices. See `docs/PROJEX_0.7.0_IMPLEMENTATION.md`.


## PROJEX 0.7.0-preview.3 — Authenticated live Gantt data integration

- Added a desktop API connection dialog for API base URL, project UUID, and bearer access token.
- Added `GanttApiClient` to call the existing authenticated `GET /api/v1/projects/{projectId}/planning/schedule/gantt` contract.
- Added a live schedule view populated from API response DTOs, including activity dates, duration, progress, schedule freshness, relationships, and optional approved-baseline metadata.
- The bearer token is held in memory for the request and is not persisted. Remote API connections require HTTPS; plain HTTP is allowed only for localhost.
- The live view is deliberately read-only in this slice. It does not silently fall back to sample data when connection/authentication fails.

**Validation boundary:** source-level inspection and archive integrity checks only. Build, automated tests, API authentication, and PostgreSQL integration were not run because the .NET 10 SDK and PostgreSQL are unavailable in this execution environment. See `docs/VALIDATION_REPORT_0.7.0-preview.3.md`.


## Database setup (0.7.1 foundation fix)

The API connects as `projex_app` (no superuser, no table ownership, no `BYPASSRLS`), so RLS is really enforced. Schema changes run as `projex_owner`.

```bash
cp .env.example .env            # then change the three passwords
docker compose up -d            # creates roles via database/init/000_roles.sh (first start only)
PGPASSWORD=<PROJEX_OWNER_PASSWORD> scripts/migrate.sh
```

Set `ConnectionStrings__Projex` to `Host=localhost;Database=projex_enterprise;Username=projex_app;Password=<PROJEX_APP_PASSWORD>`.

EF columns are mapped to snake_case in `ProjexDbContext` to match the SQL migrations. **Unverified:** `001_foundation_schema.sql` is hand-written from the EF model and has not been executed; compare it with `dotnet ef migrations script` and run the tenant-isolation tests before relying on it. Changing `POSTGRES_*` passwords after first start requires `docker compose down -v` (dev only).


## PROJEX 0.8.0-preview.1 — Cost Control & EVM
Adds `Projex.Cost.Engine`, costing schema (migrations 007–008), `/costing` API, and DB-enforced immutability for approved budgets, cost transactions and baselines. **Not yet built or run** — see `docs/PROJEX_0.8.0_COST_CONTROL_EVM.md` and `docs/GAP_ANALYSIS_0.7.4.md`.

## PROJEX 0.9.0-preview.1 — Users & Sessions
Refresh-token sessions with rotation and reuse detection, logout, immediate revocation, and CompanyAdmin user management (migration 010). **Not yet built or run** — see `docs/PROJEX_0.9.0_USERS_SESSIONS.md`. Existing access tokens are rejected after upgrade; sign in again.

## 0.9.2-preview.1 — Step 1: Identity, Platform, RBAC
Hardened identity (composite tenant-safe foreign keys, RLS on Identity child tables, tenant-status check on every request, rate limiting, configurable password/lockout policy), a separate platform administration plane (`platform` schema, tenants, subscriptions and seat limits, platform audit) and role-based access control (permission catalog, 8 system roles, custom roles, scoped role assignments). Migrations 011-013. **Not yet built or run** — see `docs/PROJEX_0.9.2_IDENTITY_PLATFORM_RBAC.md`. The old one-shot tenant bootstrap is gone; `organization.tenants` moved to `platform.tenants` and its `key` column is now `code`.
