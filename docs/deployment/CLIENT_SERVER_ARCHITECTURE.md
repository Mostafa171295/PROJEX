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

The desktop client accepts remote HTTPS server URLs and rejects remote plain HTTP. The default API provisioning profile remains loopback-only. After local server setup, use the Server Control Center's **Enable secure LAN access** action to run Enable-PROJEX-RemoteAccess.ps1.

The remote-access setup requires:
1. A stable DNS name for the server that every client device can resolve to the server's private IP address.
2. A trusted TLS PFX certificate whose DNS name matches that server name and whose issuer is trusted by the server and all client devices.
3. The private client subnet in CIDR notation (for example 192.168.10.0/24).

The script validates the certificate and private subnet, copies the PFX to ProgramData with restricted ACLs, stores the PFX password only in the protected PROJEX API service environment, adds an HTTPS Kestrel listener on port 5443, and creates a Windows Firewall rule limited to the specified private subnet and Domain/Private profiles. The API keeps its local-only HTTP health endpoint on 127.0.0.1:5080. PostgreSQL remains local and is never opened to client devices.

Configure each desktop client with https://SERVER-DNS-NAME:5443/. Ensure DNS and certificate trust are correctly configured on every client. The firewall rule is deliberately scoped to a private subnet; do not replace it with Any, Public, or Internet-wide access. Use Disable-PROJEX-RemoteAccess.ps1 to return the API to local-only mode.

Certificate issuance/renewal and real-machine client connectivity must still be validated in the target company network before production use.

## Installation profiles

1. **Server:** extract the complete package, run Server/Manager/Projex.ServerManager.exe as an administrator, then use Run server setup. PostgreSQL 16 is installed automatically through WinGet if it is missing; an existing PostgreSQL 16 service is reused. Keep database access local.
2. **Client:** extract the same ZIP and run Install-PROJEX-Client.ps1 as administrator. The script installs only Desktop into Program Files\\PROJEX\\Client and creates shortcuts. Set the server HTTPS URL in connection settings, test connectivity, then sign in.
3. **Single-machine development:** the desktop and server may temporarily run on one Windows machine using the local API endpoint.

## Release packaging

Each public GitHub Release must contain exactly one downloadable artifact: PROJEX-Windows-win-x64.zip. The ZIP contains the desktop client, server API, Server Control Center, setup/maintenance scripts, migrations, and deployment documentation. Installer executables and duplicate archives are not release assets.
