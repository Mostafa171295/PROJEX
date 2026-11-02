# PROJEX on a Windows home/office server

This deployment mode is intended for one always-on Windows machine hosting the API and
PostgreSQL, with desktop clients connecting from other locations.

## Important deployment boundary

Running the API as a Windows Service only solves automatic startup and background
hosting. It does **not** by itself make the system safely reachable over the public
Internet. Do not expose the API or PostgreSQL by opening arbitrary router ports.
Use a properly configured VPN (preferred for a small private team) or a hardened
HTTPS reverse proxy with a DNS name, valid TLS certificate, firewall rules, and
restricted administrative access.

PostgreSQL must remain private to the server. Do not expose port 5432 publicly.

## API Windows Service support

The API host uses the Windows Service lifetime when installed as a service. Publish
the API as self-contained for win-x64, copy the complete publish directory to a
stable location such as `C:\\Program Files\\PROJEX\\API`, and register
`Projex.Api.exe` with the Windows Service Control Manager.

Before starting the service, configure these machine-level environment variables
for the service account:

- `ConnectionStrings__Projex`: PostgreSQL connection string for the least-privileged
  PROJEX application database user.
- `Authentication__JwtSigningKey`: independently generated random secret of at
  least 32 bytes. Never use the development key or commit production secrets.
- `Bootstrap__Key`: strong one-time bootstrap secret. Remove/rotate it after the
  first platform administrator is provisioned.

Set `ASPNETCORE_URLS` to a loopback-only HTTP listener when a local HTTPS reverse
proxy terminates TLS, for example `http://127.0.0.1:5080`. If using a VPN, bind
only to the private VPN interface/address and firewall it to approved client
devices. Do not bind to all interfaces without a deliberate firewall and TLS plan.

Example elevated PowerShell commands (replace paths and values; secrets are
intentionally not included here):

```powershell
sc.exe create "PROJEX API" binPath= '"C:\Program Files\PROJEX\API\Projex.Api.exe"' start= auto
sc.exe failure "PROJEX API" reset= 86400 actions= restart/5000/restart/15000/restart/30000
sc.exe start "PROJEX API"
```

For updates, stop the service, replace the published files, then start it again.
To remove it: `sc.exe stop "PROJEX API"` followed by
`sc.exe delete "PROJEX API"`.

## Windows database setup (operator-run preview procedure)

The repository's Compose file runs PostgreSQL 16 with a persistent named volume and
binds port 5432 to loopback only. On a Windows host with Docker Desktop in Linux
container mode:

1. Install Docker Desktop and the PostgreSQL 16 client tools (`psql.exe`).
2. From the repository root, copy `.env.example` to `.env` and replace all three
   placeholder passwords with unique strong values. Do not commit or share `.env`.
3. Start the database with `docker compose up -d` and confirm
   `docker compose ps` reports PostgreSQL healthy. The role initialization script
   runs only when the PostgreSQL data volume is first created.
4. Set `PGPASSWORD` for the current PowerShell process to the
   `PROJEX_OWNER_PASSWORD` value from `.env`, then run
   `./scripts/migrate.ps1`. The script applies numbered SQL files in order and
   records each successful migration in `public.schema_migrations`. Clear the
   process environment variable immediately afterward.
5. Configure the API connection string with the `PROJEX_APP_PASSWORD` value,
   not the owner or superuser password.

Example migration invocation (enter the owner password at the secure prompt):

```powershell
$secure = Read-Host "PROJEX owner password" -AsSecureString
$env:PGPASSWORD = [System.Net.NetworkCredential]::new("", $secure).Password
try {
    .\scripts\migrate.ps1
}
finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
}
```

The PowerShell migration runner requires `psql.exe` on PATH (or pass
`-PsqlPath`), network access to the database, and the `projex_owner` role.
It does not create the PostgreSQL server, database, or roles. Back up the database
before applying schema changes. Do not delete the Docker volume to troubleshoot:
that destroys the database contents.

## Database and readiness requirements

The PostgreSQL 16 service and the PROJEX database/schema must be installed and
configured separately. The current repository documentation explicitly treats
migrations, RLS, tenant isolation, authentication and production security as
requiring further validation. Do not put real company data or expose this system
to remote users until those checks, database backup/restore, TLS, firewall and
account-permission reviews have passed.

## Desktop client behavior

The desktop application remains a separate client. It should connect to the
server's HTTPS/VPN address; it must not start a second API or local database on
every employee PC. A future desktop connection/settings screen can show API
health and provide an administrator-only link to service operations, but remote
clients cannot directly start or stop the Windows service on the host.
