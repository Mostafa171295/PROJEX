# PROJEX ENTERPRISE 0.6.1 — Package validation report

## Performed in this environment

- Confirmed the 0.6.1 working tree was extracted from the delivered 0.6.0 package; existing projects, API, database migrations and prior tests were retained.
- Added the calendar engine and API read/write integration without introducing a new database migration.
- Added unit tests for split shifts, lunch-boundary starts versus finishes, next-working-day rollover, holiday exceptions, negative lead time, overlapping intervals and malformed JSON.
- Validated project XML and application JSON files with local parsers.
- Validated the final ZIP archive structure and generated a SHA-256 checksum.

## Not performed

The execution environment does not contain the .NET 10 SDK or PostgreSQL. Therefore `dotnet restore`, `dotnet build`, `dotnet test`, database migration execution, and end-to-end API tests have **not** been run. This package must not be represented as build-verified or production-ready until those commands pass in a .NET 10 / PostgreSQL environment.

## Required verification on a developer machine

```bash
dotnet restore Projex.slnx
dotnet build Projex.slnx --configuration Release
dotnet test Projex.slnx --configuration Release
```

Then start PostgreSQL, apply the existing 0.6.0 migrations and RLS scripts, bootstrap a tenant/project, create a calendar and sample WBS/activity network, and exercise the schedule calculation and Gantt endpoints.
