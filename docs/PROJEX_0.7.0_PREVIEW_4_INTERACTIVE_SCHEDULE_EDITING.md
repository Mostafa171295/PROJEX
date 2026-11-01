# PROJEX ENTERPRISE 0.7.0-preview.4 — Interactive Schedule Editing

## Scope delivered

This preview advances the authenticated Desktop Gantt integration from read-only viewing to a first controlled editing workflow.

- The Desktop window now retains the authenticated `GanttApiClient` for its lifetime and disposes it when the schedule window closes.
- Each activity row exposes an editable duration in working days (8 hours / 480 minutes per day).
- Duration changes are sent to `PATCH /api/v1/projects/{projectId}/planning/schedule/activities/{activityId}` with the activity's current `ExpectedRowVersion`.
- The client handles authentication, authorization, validation, missing-activity, and optimistic-concurrency failures with actionable messages.
- After a successful write, the client reloads the schedule from the server. The API's stale/recalculation-required status remains authoritative.
- A manual refresh action is available to discard stale local row versions and fetch the latest server state.

## Explicit limitations

- This is controlled duration editing, not yet a graphical timeline with drag-and-drop date movement.
- Start/finish dates are not directly edited. They remain outputs of schedule calculation.
- Relationship create/edit/delete UI is not included in this preview.
- No client-side undo/redo is included. A safe undo workflow needs server-side command history or compensating commands, not merely restoring local text fields.
- The current API mutation endpoint updates the activity duration and marks schedule data stale; this preview does not claim that CPM recalculation is automatically run.
- Audit behavior depends on the existing API/database audit implementation and still requires integration verification.

## Validation status

Static source review only. Build, unit tests, Desktop launch, API integration, PostgreSQL integration, tenant-isolation, and concurrency tests have not been executed in this environment. Run these in a .NET 10 SDK environment with the configured PostgreSQL services before accepting the preview.

## Recommended validation commands

```bash
dotnet restore Projex.slnx
dotnet build Projex.slnx --no-restore
dotnet test Projex.slnx --no-build
```

Then run the API and Desktop app against a seeded test tenant and verify: successful edit; stale RowVersion conflict; forbidden cross-tenant edit; invalid duration; milestone duration constraint; schedule status changes to `Stale`; and refreshed activity RowVersion.
