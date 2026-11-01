# PROJEX ENTERPRISE 0.5.0 — Planning & Scheduling Foundation

## Scope delivered
- New independent library: `Projex.Scheduling.Engine`.
- CPM forward pass and backward pass.
- Early Start / Early Finish / Late Start / Late Finish.
- Total Float and Critical flag.
- Critical activity path and deterministic topological ordering.
- Relationship types: FS, SS, FF, SF.
- Positive lag and negative lead support.
- Validation for duplicate IDs, missing references, self-links, negative durations and cyclic networks.
- API: `POST /api/v1/projects/{projectId}/schedule/calculate`.
- Endpoint requires an authenticated company member and either CompanyAdmin or active project role PROJECTMANAGER / PLANNINGENGINEER.
- Unit tests for network calculations, all relationship types, cycles and invalid references.

## Request example
```json
{
  "activities": [
    { "id": "A", "name": "Design", "duration": 3 },
    { "id": "B", "name": "Execution", "duration": 5 }
  ],
  "relationships": [
    { "predecessorId": "A", "successorId": "B", "type": "FinishToStart", "lag": 0 }
  ]
}
```

## Calculation model
All dates are normalized to project-relative working-day units. `Duration` and `Lag` are decimals. FS/SS/FF/SF are supported using generalized precedence constraints. The earliest project start is zero. Project duration is the maximum early finish.

## Not yet included
- Persisted WBS and activity entities / database migrations.
- Calendars, holidays, shift patterns and multiple calendars.
- Date-based scheduling and time zones.
- Constraints, milestones, actual progress and out-of-sequence progress.
- Baselines, schedule comparison, resource leveling and Gantt UI.
- Full Primavera P6 compatibility.

## Important
The API calculates the submitted network in memory and does not save activities or schedule results. The project access check is a first authorization boundary; before production, test it with PostgreSQL RLS and every project role. Apply reviewed EF migrations, run `dotnet build` and `dotnet test`, then perform security and algorithm validation.
