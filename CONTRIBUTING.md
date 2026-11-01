# Contributing

Thanks for helping improve PROJEX.

## Development setup

1. Install the .NET 10 SDK.
2. Install Docker Desktop / Docker Engine for PostgreSQL integration tests.
3. Copy `.env.example` to `.env` and replace every example password.
4. Start the database with `docker compose up -d`.
5. Configure the API secrets through environment variables. For example, in PowerShell:

   ```powershell
   $env:Authentication__JwtSigningKey = "<generate-a-unique-secret-of-at-least-32-bytes>"
   $env:Bootstrap__Key = "<generate-a-strong-one-time-bootstrap-secret>"
   ```

   On Bash:

   ```bash
   export Authentication__JwtSigningKey='<generate-a-unique-secret-of-at-least-32-bytes>'
   export Bootstrap__Key='<generate-a-strong-one-time-bootstrap-secret>'
   ```

6. Restore, build, and test:

   ```bash
   dotnet restore Projex.slnx
   dotnet build Projex.slnx --configuration Release --no-restore
   dotnet test Projex.slnx --configuration Release
   ```

## Pull requests

- Create a feature branch from `main`.
- Keep changes focused and include tests for behavior changes.
- Update relevant documentation and database migration notes.
- Do not commit secrets, local settings, build outputs, or customer data.
- Ensure CI passes before requesting review.
