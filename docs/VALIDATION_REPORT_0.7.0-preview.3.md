# Validation Report — PROJEX ENTERPRISE 0.7.0-preview.3

## Delivered

- Desktop Planning & Scheduling now opens an API connection dialog rather than silently opening the sample-data Gantt preview.
- Added a typed HTTP client for the existing authenticated Gantt read endpoint: `GET /api/v1/projects/{projectId}/planning/schedule/gantt`.
- Added a live read-only schedule view displaying the returned activities, durations, planned dates, progress, schedule status, relationship count, and baseline metadata.
- Connection failures are surfaced to the user. The desktop client does not substitute sample data after an API failure.
- Bearer access tokens are not written to disk by this feature. Remote endpoints require HTTPS; HTTP is allowed only for localhost.
- Updated the API root version to `0.7.0-preview.3`.

## Important limitations

- This is a read-only API integration slice. Drag/drop, date/duration editing from the live Gantt, relationship editing, baseline selection, and schedule recalculation orchestration are not yet connected to the desktop UI.
- The API connection dialog currently accepts a bearer access token directly. A complete production authentication flow should replace manual token entry before general release.
- The live schedule view is a tabular activity presentation, not yet a true time-scaled Gantt renderer. The sample Gantt preview remains in the source as a design prototype but is no longer the default Planning button action.
- The client DTO contract follows the current API JSON shape and must be exercised against a running API before release.

## Validation performed

- Inspected the existing API route group and confirmed the endpoint path and authentication requirement in source.
- Inspected the API response projection and aligned the desktop DTO property names with its JSON fields.
- Confirmed the desktop project uses the existing Avalonia application and requires no new external package for `HttpClient`/`System.Text.Json`.
- Updated the API root version string to match this preview.

## Not performed

- `dotnet build` / `dotnet test`: not run; `dotnet` is not installed in the execution environment.
- Live API authentication/authorization test: not run.
- PostgreSQL integration, tenant-isolation, and RLS tests: not run.
- Manual Avalonia UI execution on Windows, Linux, or macOS: not run.

## Release gate before production

1. Build with the pinned .NET SDK 10.0.100 or a compatible approved SDK.
2. Add automated DTO contract tests using a representative Gantt API JSON payload.
3. Verify expired token, invalid token, forbidden project, missing project, and stale schedule cases.
4. Verify tenant isolation and PostgreSQL RLS with at least two tenants.
5. Replace manual token entry with the platform authentication/session workflow.
6. Complete true time-scaled Gantt rendering and persisted editing before describing the desktop Gantt as interactive.
