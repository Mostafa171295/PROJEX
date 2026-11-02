# PROJEX Windows product requirements and local installation

This document defines the initial supported product target. Hardware figures are **planning targets**, not benchmark-certified minimums. They must be confirmed with representative Windows installation and workload tests before being advertised as guaranteed requirements.

## Supported deployment profiles

### Single-computer edition (initial commercial target)

The desktop client, ASP.NET Core API Windows service, and PostgreSQL database run on one Windows PC. The API and database use loopback-only endpoints; no Internet connection or external server is required for normal local operation.

### Small-office edition (future expansion target)

One always-on Windows host runs the API and PostgreSQL. Desktop clients on other PCs connect over a private, secured company network. This mode is not enabled by the single-computer setup script. Before offering it commercially, deploy HTTPS, firewall restrictions, authentication hardening, backup monitoring, and an explicit network installation procedure. Never expose PostgreSQL port 5432 to the Internet.

## Initial hardware targets

| Component | Entry target | Recommended target |
|---|---:|---:|
| Operating system | Windows 10/11, 64-bit | Windows 11, 64-bit |
| CPU | Modern Intel Core i3 / AMD Ryzen 3 | Intel Core i5 / AMD Ryzen 5 or better |
| Memory | 8 GB RAM | 16 GB RAM |
| Storage | SSD, 20 GB free for PROJEX and working data | SSD/NVMe, 50 GB free |
| Display | 1366 x 768 | 1920 x 1080 |

These targets assume a small single-user workload. Large schedules, reports, concurrent users, and long data retention can require more memory and storage. Windows 10 can run the x64 application, but Windows 11 is recommended because standard Windows 10 support has ended.

## Software prerequisites

- 64-bit Windows 10 or Windows 11.
- PostgreSQL 16 Server installed locally for the single-computer edition.
- PostgreSQL command-line tools, including `psql.exe`.
- Administrator rights for initial database provisioning and Windows service installation.
- The PROJEX release package. The desktop and API are self-contained; a separate .NET runtime is not required.

The installer packages the PROJEX desktop, API, Server Control Center and setup scripts. When the server component is selected (the default), the installer opens Server Control Center after installation. PostgreSQL is installed/configured only when the user explicitly selects **Run server setup** there; it is never silently installed during the file-copy phase.

## First-time single-computer setup

1. Extract the PROJEX package (or run the PROJEX Setup installer with **Server components** selected). Double-click **Start-PROJEX-Server-Setup.bat** (or open Start → PROJEX Enterprise → PROJEX Server Control Center) and approve the Windows administrator prompt.
2. In Server Control Center open **Database and setup** and press **step 1**. PostgreSQL 16 is downloaded and installed automatically through Windows Package Manager (WinGet). This requires Internet access and Microsoft's App Installer. If PostgreSQL 16 already exists, step 1 is not needed.
3. Press **step 2**. Setup creates the application roles and the projex_enterprise database, applies numbered migrations, installs/starts the PROJEX API Windows service and checks the health/ready endpoint. Database passwords are generated automatically and stored encrypted by Windows (not displayed). If PostgreSQL was installed earlier by someone else, the page asks once for the `postgres` administrator password.
4. When the **Status** page shows green, open **PROJEX Enterprise** (Start-PROJEX.bat). Its default local API address is http://127.0.0.1:5080/.

Opening Desktop alone does not install PostgreSQL, create the database or start the API. Settings and setup logs are stored in `Server\Settings` (setup logs in `Server\Settings\Logs`). If a step fails, press **Open latest log**, correct the reported prerequisite and run the step again. Do not delete the PostgreSQL data directory or the PROJEX database to troubleshoot.

## Clients

Each client computer needs only the **Clients** folder: copy it to the client, right-click **Setup-Client.bat** and choose Run as administrator. In the **Clients** page of Server Control Center the administrator saves the server HTTPS address once; it is written to `Clients\connection.json` and applied automatically by Setup-Client.bat.

The local setup refuses non-loopback database endpoints and a PostgreSQL listener bound to a non-loopback address. It does not expose PostgreSQL or the API to the network.

## Backup and recovery

Configure the scheduled encrypted backup task using the instructions in [POSTGRES_BACKUP_RESTORE.md](POSTGRES_BACKUP_RESTORE.md). Store the backup encryption password and migration-owner credential in a password manager. Keep at least one backup copy on a separate physical device; a backup on the same disk does not protect against disk failure. Test restores regularly on a separate database.

## Release-readiness gate

Do not advertise the hardware table as a certified minimum or describe the product as production-ready until all of the following are complete:

- Clean installation on supported Windows 10 and Windows 11 x64 test machines.
- Startup after reboot and recovery after API/PostgreSQL service restart.
- Desktop-to-local-API connection and first-run account/company setup.
- Functional tests with small, medium, and large representative project schedules.
- Backup, off-device copy, and restore drill.
- Uninstall/upgrade/rollback tests confirming that user database files are preserved.
- Security review of service credentials, local administrator access, firewall rules, authentication, tenant isolation, and audit logging.

The repository CI validates builds, automated tests, database integration, deployment scripts, and packaging. It does not replace the real-machine installation and operational tests above.
