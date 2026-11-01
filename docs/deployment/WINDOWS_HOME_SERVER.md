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
