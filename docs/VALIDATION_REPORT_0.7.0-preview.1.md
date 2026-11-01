# Validation Report — PROJEX ENTERPRISE 0.7.0-preview.1

## Source-level checks performed

- Confirmed the supplied 0.6.2 archive SHA-256 before modifying the extracted source.
- Confirmed the API root version now identifies this package as `0.7.0-preview.1`.
- Confirmed the Gantt duration-edit route is project-scoped and calls `CanPlanProjectAsync` before reading or changing the activity.
- Confirmed the command checks `ExpectedRowVersion`, delegates duration validation to the domain entity, and returns a stale-schedule marker after a successful edit.
- Added domain tests for row-version increment and milestone duration constraints.
- Checked changed source files for unresolved merge-conflict markers and verified the generated archive structure.

## Not executed

The environment does not contain the .NET SDK (`dotnet` is unavailable) or PostgreSQL. Therefore:

- `dotnet build Projex.slnx`: not run
- `dotnet test Projex.slnx`: not run
- API runtime tests: not run
- PostgreSQL integration and tenant-isolation tests: not run

This is a development preview, not a production-validated release. Execute the above checks in a .NET 10 / PostgreSQL environment before merging or deploying.
