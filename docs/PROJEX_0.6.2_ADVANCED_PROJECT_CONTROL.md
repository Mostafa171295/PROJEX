# PROJEX ENTERPRISE 0.6.2 — Advanced Project Control

**Release track:** 0.6.x  
**Base:** PROJEX ENTERPRISE 0.6.1  
**Focus:** Gantt data with baseline overlay, baseline variance, schedule freshness, and downstream dependency impact.

## 1. Scope delivered

### 1.1 Gantt data with optional baseline overlay

`GET /api/v1/projects/{projectId}/planning/schedule/gantt?baselineId={approvedBaselineId}`

Returns activity rows, WBS labels, progress, critical-path flags, total float, dependencies, latest schedule run, schedule freshness, and optional baseline dates with start/finish variance in elapsed calendar days. The baseline query parameter is optional. When supplied, the baseline must belong to the project and be approved.

This endpoint provides the data contract for the Gantt client. It does not claim to implement drag-and-drop editing in the desktop client yet.

### 1.2 Baseline register

`GET /api/v1/projects/{projectId}/planning/baselines`

Lists project baselines by descending version, including approval metadata and activity counts.

### 1.3 Baseline variance

`GET /api/v1/projects/{projectId}/planning/baselines/{baselineId}/variance`

Requires an approved baseline. Returns activity-level comparisons for:

- Start variance (elapsed calendar days)
- Finish variance (elapsed calendar days)
- Duration variance (working minutes as stored)
- Progress variance (percentage points)
- Activities missing from the current schedule
- Activities added after the baseline

Calendar-day variance is an elapsed timestamp difference divided by 24 hours. It is not working-day variance.

### 1.4 Downstream dependency impact

`GET /api/v1/projects/{projectId}/planning/schedule/impact/{activityId}?maxDepth=100`

Performs a bounded breadth-first traversal of successor relationships. Returns downstream activities, dependency depth, relationship type, lag, and their currently stored schedule/criticality values.

This is a reachability analysis, not a simulated delay propagation. After an activity or relationship change, call the schedule calculation endpoint to recalculate dates and float.

### 1.5 Schedule health and freshness

`GET /api/v1/projects/{projectId}/planning/schedule/health`

Returns activity/dependency counts, critical activity count, activities missing calculated dates, completion summary, forecast finish, latest schedule run, and one of:

- `NotCalculated`
- `Stale`
- `Current`

Freshness checks compare the latest schedule calculation with activity, relationship, calendar, and calendar-exception timestamps. A stale result indicates that the schedule should be recalculated before it is used for reporting.

## 2. Scheduling engine addition

`Projex.Scheduling.Engine.Analysis.ScheduleImpactAnalyzer` provides a pure, testable graph traversal with cycle protection and a configurable depth bound. Unit tests cover breadth-first order, depth limiting, cycle handling, and invalid arguments.

## 3. API examples

```http
GET /api/v1/projects/{projectId}/planning/schedule/gantt
GET /api/v1/projects/{projectId}/planning/schedule/gantt?baselineId={baselineId}
GET /api/v1/projects/{projectId}/planning/baselines
GET /api/v1/projects/{projectId}/planning/baselines/{baselineId}/variance
GET /api/v1/projects/{projectId}/planning/schedule/impact/{activityId}?maxDepth=100
GET /api/v1/projects/{projectId}/planning/schedule/health
```

All endpoints inherit authorization from the planning route group and call project access validation. No database schema migration is required for 0.6.2.

## 4. Known limitations

1. The Gantt endpoint is a server-side data contract; interactive drag/drop editing and a timeline visualization in Avalonia are not included in this release.
2. Impact analysis reports reachable successors; it does not estimate the date movement caused by a hypothetical duration change.
3. Baseline variance reports elapsed calendar-day date differences. Working-calendar variance is a future enhancement.
4. Mixed activity calendars remain outside the current common-calendar CPM model.
5. Full compile, automated test execution, and PostgreSQL integration validation require a .NET 10 SDK and PostgreSQL environment.

## 5. Validation status

Source-level checks and archive integrity checks can be performed in the packaging environment. `dotnet build`, `dotnet test`, endpoint integration tests, and database migration tests must be run in a .NET 10/PostgreSQL environment before production acceptance.
