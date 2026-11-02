# PROJEX on a Windows home/office server

This deployment mode is intended for one always-on Windows machine hosting the API and PostgreSQL, with desktop clients connecting from other locations.

## Security boundary

Windows Service hosting provides background execution and automatic startup; it does not make the API safe for public Internet access. Prefer a properly configured VPN for a small private team, or a hardened HTTPS reverse proxy with DNS, a valid TLS certificate, firewall rules, and restricted administration. Never expose PostgreSQL port 5432 publicly.

## Install the API service

If you selected the optional **server payload** in the PROJEX Windows Setup wizard, the API publish directory and administration scripts are installed under `C:\Program Files\PROJEX\Server\API` and `C:\Program Files\PROJEX\Server\Scripts`. After PostgreSQL is provisioned and the required machine-level settings have been securely configured, run this from an elevated PowerShell session:

~~~powershell
& "C:\Program Files\PROJEX\Server\Scripts\Install-ProjexApiService.ps1" -PublishDirectory "C:\Program Files\PROJEX\Server\API" -ListenUrl "http://127.0.0.1:5080"
~~~

Publish the API as self-contained win-x64 and place the complete publish directory in a stable location, for example C:\Program Files\PROJEX\API.

Configure these machine-level environment variables before installation. Use an approved secret-management process; never put secret values in source control or command history.

- ConnectionStrings__Projex: PostgreSQL connection string for the least-privileged projex_app role.
- Authentication__JwtSigningKey: independently generated random secret of at least 32 UTF-8 bytes.
- Bootstrap__Key: strong one-time bootstrap secret only while first platform administrator provisioning is needed; remove or rotate afterward.

Open PowerShell as Administrator from the repository root and run:

~~~powershell
.\scripts\Install-ProjexApiService.ps1 -PublishDirectory "C:\Program Files\PROJEX\API" -ListenUrl "http://127.0.0.1:5080"
~~~

The installer validates the executable and required machine-level settings without displaying secret values. It creates or updates the PROJEX API service under the built-in NT AUTHORITY\NetworkService account, configures delayed automatic startup and restart recovery, scopes ASPNETCORE_URLS to the service, and starts it. Plain HTTP is accepted only on loopback; non-loopback listeners require HTTPS. Wildcard listeners are rejected.

Check service and API health:

~~~powershell
Get-Service -Name "PROJEX API"
.\scripts\Test-ProjexApiService.ps1 -HealthUrl "http://127.0.0.1:5080/health"
~~~

For an update, publish to a staging directory, stop the service, replace the stable API directory contents, then rerun the installer with the stable directory. Keep a verified copy of the previous publish directory for rollback.

Remove only the service (not published files or database data):

~~~powershell
.\scripts\Uninstall-ProjexApiService.ps1
~~~

## Database setup (operator-run preview procedure)

The Compose file runs PostgreSQL 16 with a persistent named volume and binds port 5432 to loopback only. On Windows with Docker Desktop in Linux-container mode:

1. Install Docker Desktop and PostgreSQL client tools (psql.exe).
2. Copy .env.example to .env and replace all three placeholder passwords. Never commit or share .env.
3. Run docker compose up -d and confirm docker compose ps reports PostgreSQL healthy. Role initialization runs only when the data volume is first created.
4. Set PGPASSWORD for the current PowerShell process to PROJEX_OWNER_PASSWORD, run .\scripts\migrate.ps1, then clear PGPASSWORD. The runner applies numbered SQL files in order and tracks them in public.schema_migrations.
5. Configure the API connection string with PROJEX_APP_PASSWORD, not the owner or superuser password.

The migration runner requires psql.exe on PATH (or -PsqlPath), network access, and the projex_owner role. It does not create the PostgreSQL server, database, or roles. Back up the database before migrations. Do not delete the Docker volume to troubleshoot; that destroys database contents.

## Readiness requirements

Migrations, RLS, tenant isolation, authentication and production security still require independent validation. Do not use real company data or expose the system to remote users until database backup/restore, TLS, firewall, account-permission and security reviews have passed.

## Desktop clients

Desktop clients connect to the server's HTTPS/VPN address. They must not start a second API or local database on each employee PC. Remote clients cannot directly start or stop the Windows service on the host.


## Health probes

The API exposes three unauthenticated probes intended for local service monitoring and reverse-proxy health checks:

- `GET /health` — backwards-compatible liveness response.
- `GET /health/live` — confirms the API process is responding; it does not require PostgreSQL.
- `GET /health/ready` — checks whether the API can connect to PostgreSQL. Returns HTTP 200 when ready and HTTP 503 when the database is unavailable. The response deliberately omits provider exception details and connection information.

After installing or updating the service, run the readiness check from an elevated PowerShell session:

```powershell
.\scripts\Test-ProjexApiService.ps1 -HealthUrl "http://127.0.0.1:5080/health/ready"
```

The check verifies both the Windows Service Control Manager state and the API/database readiness. Use `-SkipServiceStatus` only when checking an API hosted outside the Windows Service.
