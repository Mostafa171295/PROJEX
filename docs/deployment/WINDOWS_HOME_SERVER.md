# PROJEX on a Windows home/office server

This deployment mode is intended for one always-on Windows machine hosting the API and PostgreSQL, with desktop clients connecting from other locations.

## Security boundary

Windows Service hosting provides background execution and automatic startup; it does not make the API safe for public Internet access. Prefer a properly configured VPN for a small private team, or a hardened HTTPS reverse proxy with DNS, a valid TLS certificate, firewall rules, and restricted administration. Never expose PostgreSQL port 5432 publicly.

## Install and update the API service

If you selected the optional **server payload** in the PROJEX Windows Setup wizard, the self-contained API and administration scripts are installed under `C:\Program Files\PROJEX\Server\API` and `C:\Program Files\PROJEX\Server\Scripts`. Install PostgreSQL and run the automated provisioning procedure below for the first installation; it creates the service only after the database and credentials are ready.

The service runs as the built-in `NT AUTHORITY\NetworkService` account, uses delayed automatic startup and restart recovery, and reads its connection string and JWT signing key from service-specific environment values. The service registry key is restricted to Local System and local Administrators; these values are not encrypted, so local administrator access must be protected. Do not configure secrets as machine-wide environment variables.

After publishing a new API build to the stable publish directory, run this from an elevated PowerShell session to update the service while retaining its existing service-scoped credentials:

~~~powershell
& "C:\Program Files\PROJEX\Server\Scripts\Install-ProjexApiService.ps1" -PublishDirectory "C:\Program Files\PROJEX\Server\API" -ListenUrl "http://127.0.0.1:5080"
~~~

Check service and database readiness:

~~~powershell
Get-Service -Name "PROJEX API"
& "C:\Program Files\PROJEX\Server\Scripts\Test-ProjexApiService.ps1" -HealthUrl "http://127.0.0.1:5080/health/ready"
~~~

For updates, publish to a staging directory, stop the service, replace the stable API directory contents, then rerun the installer with the stable directory. Keep a verified copy of the previous publish directory for rollback.

Remove only the service (not published files or database data):

~~~powershell
& "C:\Program Files\PROJEX\Server\Scripts\Uninstall-ProjexApiService.ps1"
~~~

## Automated database and service setup on Windows

The optional server payload includes `Initialize-ProjexDatabase.ps1` and the numbered SQL migrations. Install PostgreSQL 16 Server separately and make sure its Windows service is running. The PostgreSQL client tool `psql.exe` must also be available on PATH. The PROJEX installer does not silently install or reconfigure a database server.

Open PowerShell as Administrator and run the setup script from the installed server folder:

~~~powershell
& "C:\Program Files\PROJEX\Server\Scripts\Initialize-ProjexDatabase.ps1" -PublishDirectory "C:\Program Files\PROJEX\Server\API"
~~~

The script:

1. Prompts for the PostgreSQL administrator password without echoing it.
2. Prompts for a new `projex_owner` migration password (minimum 32 UTF-8 bytes) and confirmation. Save this password in an approved password vault.
3. Creates or updates the `projex_owner` and `projex_app` roles as non-superusers without `BYPASSRLS`; generates the application password and JWT signing key cryptographically.
4. Creates the application database if it does not exist, applies the packaged numbered migrations using `projex_owner`, and builds the API connection string for the restricted `projex_app` role.
5. Installs or updates the `PROJEX API` Windows service, stores the API connection string and JWT key only in that service's registry environment, and restricts that service registry key to Local System and local Administrators.
6. Starts the service and checks `/health/ready` to confirm PostgreSQL connectivity.

The temporary SQL file containing role credentials is created under `C:\ProgramData\PROJEX\Provisioning` with restricted NTFS permissions and removed at the end. Secrets are not printed or passed as command-line arguments. The service-specific registry values are not encrypted; local Administrators and Local System can access them, so protect administrator access to the server.

For a PostgreSQL server on another machine, use `-SslMode VerifyFull -RootCertificate "C:\path\to\root.crt"`. The setup refuses remote database hosts without full certificate and hostname verification. For a local PostgreSQL instance, the default loopback connection uses `SSL Mode=Prefer`.

To check the service later:

~~~powershell
& "C:\Program Files\PROJEX\Server\Scripts\Test-ProjexApiService.ps1" -HealthUrl "http://127.0.0.1:5080/health/ready"
~~~

Running the provisioning script again reuses the existing service's `projex_app` password and JWT key when present, updates the database target/service configuration, and reapplies only migrations not already recorded. On a first installation, the application password and JWT key are generated. Keep the `projex_owner` password in the password vault for controlled future migration work.

### Docker Compose preview option

For a local preview rather than a separately installed PostgreSQL Windows service, the repository's Docker Compose file runs PostgreSQL 16 with a persistent named volume and binds port 5432 to loopback only. On Windows with Docker Desktop in Linux-container mode:

1. Install Docker Desktop and PostgreSQL client tools (`psql.exe`).
2. Copy `.env.example` to `.env` and replace all three placeholder passwords. Never commit or share `.env`.
3. Run `docker compose up -d` and confirm `docker compose ps` reports PostgreSQL healthy. Role initialization runs only when the data volume is first created.
4. Use the `projex_owner` credential to run the migration script if you are not using the automated setup above.

Do not delete the Docker volume to troubleshoot; that destroys database contents. Docker Desktop is a preview/development option and must not be assumed to provide a hardened always-on production database service.

## Readiness requirements

Migrations, RLS, tenant isolation, authentication and production security still require independent validation. Do not use real company data or expose the system to remote users until database backup/restore, TLS, firewall, account-permission and security reviews have passed.

## Desktop clients

Desktop clients connect to the server's HTTPS/VPN address. They must not start a second API or local database on each employee PC. Remote clients cannot directly start or stop the Windows service on the host.


On each employee PC:

1. Install the PROJEX desktop client.
2. Open **Settings → Connection settings** (or enter the address on the sign-in screen).
3. Enter the API base address published by the organization, for example `https://projex.company.com/` or the HTTPS/VPN hostname configured by IT.
4. Select **Test connection**. The client checks `/health/live` and `/health/ready` separately so an API outage can be distinguished from a database-readiness failure.
5. Save the server address, then sign in with the employee's tenant key and account credentials.

The desktop client permits plain HTTP only for loopback/local development addresses. Remote HTTP, URL-embedded credentials, query strings and fragments are rejected. The saved setting contains only the server URL; passwords and access/refresh tokens are not persisted. Changing the server while signed in signs out of the previous server before applying the new endpoint. While signed in, the desktop header rechecks `/health/live` and `/health/ready` every 15 seconds and displays API availability separately from database readiness. Monitoring stops on sign-out or when the desktop window closes.

If the API is available but readiness fails, check PostgreSQL and the API service on the host. If the API cannot be reached, check VPN connectivity, DNS, the HTTPS certificate, reverse-proxy routing and firewall rules. Do not point employee clients at PostgreSQL port 5432.

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
