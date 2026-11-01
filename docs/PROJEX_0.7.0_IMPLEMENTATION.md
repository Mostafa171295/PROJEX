# PROJEX ENTERPRISE 0.7.0 — Implementation Notes

## Slice 1 — Gantt editing contract

- Added `PATCH /api/v1/projects/{projectId}/planning/schedule/activities/{activityId}` for duration updates.
- The command uses an expected row version, rejects stale writes, protects milestone duration invariants, and marks the schedule stale so users cannot mistake old CPM output for recalculated output.

## Slice 2 — Desktop Gantt presentation preview

- Added a dedicated Planning & Scheduling window reachable from the desktop navigation.
- The view renders activity rows, a calendar timeline, dependency connectors, critical-path styling, progress overlays, and baseline comparison bars.
- Zoom controls adjust timeline scale; selecting an activity displays its details.
- **Important:** this first desktop view is an explicitly labeled sample-data preview. It is not yet connected to the authenticated API, does not persist edits, and does not implement drag-and-drop, dependency editing, undo/redo, or live CPM recalculation.

## Data and correctness boundaries

- Dates in the preview are illustrative only and must not be used for project decisions.
- Baseline bars are visual comparison fixtures, not records read from `ScheduleBaseline`.
- The API-backed read adapter must consume `/schedule/gantt`; editing must continue to use the concurrency-protected PATCH endpoint and schedule recalculation endpoint.

## Validation status

- Static source review performed.
- `dotnet build` and `dotnet test` were not run because the current execution environment does not provide the .NET SDK.
- Avalonia desktop rendering and interaction must be verified on Windows, Linux, and macOS before treating this preview as validated.
