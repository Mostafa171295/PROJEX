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

The current installer packages the PROJEX desktop and API payload. PostgreSQL is a separate prerequisite and is not silently installed or reconfigured by the PROJEX installer.

## First-time single-computer setup

1. Install PostgreSQL 16 using its official Windows installer. Keep its listener restricted to localhost; do not enable remote access for the single-computer edition.
2. Run the PROJEX Setup installer. Keep the **Local API server and database setup files** component selected.
3. Open Windows PowerShell as Administrator.
4. Run the local setup helper from the installed server scripts directory:

   ```powershell
   & "C:\Program Files\PROJEX\Server\Scripts\Install-PROJEX-Local.ps1"
   ```

5. Enter the PostgreSQL administrator password when prompted. Choose and securely store the migration-owner password when prompted. Passwords are not echoed to the console.
6. Wait for database provisioning, migrations, Windows service installation, and the readiness check to finish.
7. Open PROJEX Desktop and use the local API endpoint `http://127.0.0.1:5080/` in Connection settings if it is not already configured.

The helper refuses non-loopback database endpoints and refuses to continue if PostgreSQL is listening on a non-loopback address on the selected port. It does not install PostgreSQL or change PostgreSQL network configuration automatically.

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
