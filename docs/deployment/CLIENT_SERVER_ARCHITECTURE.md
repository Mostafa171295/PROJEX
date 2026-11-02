# PROJEX Client–Server Architecture

## Target deployment

PROJEX is designed for a dedicated Windows server and multiple Windows desktop clients.

- **Server machine:** hosts the PROJEX ASP.NET Core API as a Windows Service, PostgreSQL, database migrations, backups, and the separate PROJEX Server Control Center.
- **Client machines:** install only the PROJEX Desktop application. They connect to the server using its HTTPS address and do not connect directly to PostgreSQL.
- **Database boundary:** PostgreSQL remains bound to loopback on the server. TCP 5432 must not be opened to client machines or the Internet.
- **Authentication:** each client signs in using its tenant key, email, and password. API authorization and tenant isolation remain server-side.

## Server Control Center

Projex.ServerManager.exe is a separate, administrator-elevated Windows application. It provides:

- PROJEX API Windows Service state and start/stop/restart controls.
- PostgreSQL Windows Service state.
- API liveness and database readiness checks.
- Shortcuts to the setup script, deployment guide, backup directory, and Windows Services console.

The manager currently checks the local API endpoint http://127.0.0.1:5080/, matching the existing single-machine provisioning profile. It is an administrative console, not a public web dashboard, and should be installed only on the server.

## Secure client connectivity

The desktop client already accepts remote HTTPS server URLs and rejects remote plain HTTP. The API service's current provisioning script deliberately binds the API to loopback. For a multi-device deployment, publish a trusted HTTPS endpoint on the server using an approved TLS reverse proxy or VPN gateway, forwarding only to the loopback API endpoint.

Before production multi-device rollout, deployment automation must provision and validate the HTTPS endpoint, certificate renewal, firewall rules, server naming/address stability, and remote health checks. Do not change the API listener to a wildcard address or expose port 5080 directly as a substitute for this work.

## Installation profiles

1. **Server:** install PostgreSQL 16, extract the complete package, run Server/Manager/Projex.ServerManager.exe as an administrator, then use Run server setup. Keep database access local.
2. **Client:** install/extract the desktop payload only, open PROJEX, set the server's HTTPS URL in connection settings, test connectivity, then sign in.
3. **Single-machine development:** the desktop and server may temporarily run on one Windows machine using the local API endpoint.

## Release packaging

Each public GitHub Release must contain exactly one downloadable artifact: PROJEX-Windows-win-x64.zip. The ZIP contains the desktop client, server API, Server Control Center, setup/maintenance scripts, migrations, and deployment documentation. Installer executables and duplicate archives are not release assets.
