# PROJEX ENTERPRISE 0.6.1 — Calendar-aware scheduling

## Scope delivered

1. Working calendar JSON parser with day-of-week intervals.
2. Split shifts and multiple daily working intervals.
3. Calendar exception overrides for holidays and special working days.
4. Time-zone-aware working-minute placement.
5. Forward and backward placement, including negative relationship leads.
6. CPM input uses raw working minutes for activity durations and relationship lags.
7. Schedule calculation persists planned start/finish dates and CPM metrics on activities.
8. Gantt-ready endpoint returns WBS labels, dates, progress, critical state, float and relationships.
9. Domain tests cover split shifts, next-day rollover, holidays, negative leads and invalid overlapping intervals.

## Important calculation boundary

PROJEX 0.6.1 requires one effective calendar for all activities in a single CPM network. This is intentional: the existing CPM calculator uses one scalar time axis. Silently combining calendars with different working periods would produce incorrect relationship constraints. Per-activity calendars, resource calendars, leveling and calendars with multiple time zones require a calendar-aware network algorithm and are reserved for a later release.

## Time-zone behavior

Calendar interval definitions are interpreted in the calendar's configured time zone. Persisted `DateTimeOffset` values retain the corresponding UTC offset. Invalid local times in daylight-saving gaps are rejected. Deployments should use IANA time-zone identifiers consistently across Linux and PostgreSQL environments.

## No migration

This increment uses existing `planning.activities.planned_start`, `planned_finish`, `calendar_id`, and `planning.calendar_exceptions` columns created in 0.6.0. No schema change is introduced.

## Validation status

The source-level tests have been added but could not be executed in this build environment because the .NET 10 SDK is not installed. Run:

```bash
dotnet restore Projex.slnx
dotnet test Projex.slnx --configuration Release
dotnet build Projex.slnx --configuration Release
```
