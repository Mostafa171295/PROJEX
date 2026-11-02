# PROJEX PostgreSQL backup and restore

These operator-run scripts create encrypted PostgreSQL custom-format backups, verify their SHA-256 checksum before restore, and optionally schedule daily backups on the Windows server. They do not replace off-host backup storage, disaster-recovery drills, or a managed secrets system.

## Requirements and secrets

- PostgreSQL client tools matching the server major version: `pg_dump`, `pg_restore`, and `psql` (for migrations).
- OpenSSL available on PATH (or pass `-OpenSslPath`).
- A database role with sufficient read access for backup and restore permissions for the target database. Use the dedicated `projex_owner` operator role for these scripts; the API must continue to use the least-privileged `projex_app` role.
- Set `PGPASSWORD` for the current PowerShell process using your approved secret manager or secure credential bootstrap. Never put it in a command argument, script, or source control.
- Set `PROJEX_BACKUP_ENCRYPTION_PASSWORD` to a unique, high-entropy secret (at least 16 characters). Keep it separately from the backup files. Losing it makes encrypted backups unrecoverable.

Do not use `ConvertFrom-SecureString` output as `PGPASSWORD`: that output is DPAPI-encrypted text, not the plaintext password expected by PostgreSQL client tools. Use a secret manager that can securely inject process or machine environment variables.

## Create an encrypted backup

~~~powershell
.\scripts\Backup-ProjexDatabase.ps1 -DbHost "127.0.0.1" -Database "projex_enterprise" -BackupDirectory "D:\PROJEX-Backups" -RetentionDays 14
~~~

The script:
1. Runs `pg_dump` in PostgreSQL custom format without ownership or ACL entries.
2. Encrypts the temporary archive using OpenSSL AES-256-CBC with PBKDF2-HMAC-SHA256 (600,000 iterations).
3. Writes a sidecar JSON manifest containing database metadata and the SHA-256 hash of the encrypted archive.
4. Removes temporary plaintext files and prunes matching backups older than the retention window.

Protect the backup directory with restrictive NTFS ACLs. The SHA-256 manifest detects accidental corruption or mismatched files; it is not a digital signature and does not prove who created a backup. Copy completed backup and manifest pairs to a separate encrypted/off-site destination. Do not keep the only backup on the same physical disk as PostgreSQL.

## Restore a backup

Restore into an existing target database. Prefer a newly created, empty database for recovery rehearsals. The script verifies the manifest checksum, decrypts to a temporary file, checks the archive listing, and then invokes `pg_restore` in a single transaction.

~~~powershell
.\scripts\Restore-ProjexDatabase.ps1 -BackupFile "D:\PROJEX-Backups\projex_projex_enterprise_20261102T020000Z.dump.enc" -TargetDatabase "projex_restore_test" -Confirm
~~~

The restore uses `--clean --if-exists`: objects represented in the archive are replaced in the target database. It does not drop the database, restore cluster-wide roles, or overwrite unrelated databases. Review the target host/database carefully before confirming. Do not restore over the live database until you have a verified current backup and an approved maintenance window.

## Schedule daily backups on Windows

Set machine-level `PGPASSWORD` and `PROJEX_BACKUP_ENCRYPTION_PASSWORD` through an approved secret-management process. The scheduled task runs as the built-in SYSTEM account, so user-scoped environment variables are not available. Restrict access to machine secrets and the backup directory to administrators and the task identity.

~~~powershell
.\scripts\Register-ProjexBackupTask.ps1 -At "02:00" -BackupDirectory "D:\PROJEX-Backups" -RetentionDays 14 -Confirm
~~~

Check the task in Task Scheduler and run a test manually. Remove it with:

~~~powershell
.\scripts\Register-ProjexBackupTask.ps1 -Unregister -Confirm
~~~

The task assumes `pg_dump` and `openssl` are available through the machine PATH. Test the scheduled task under its actual SYSTEM identity before relying on it.

## Recovery checklist

1. Provision a healthy PostgreSQL instance and create the intended target database and required roles.
2. Retrieve both the encrypted archive and its matching `.json` manifest from off-host storage.
3. Restore using the command above, targeting a separate recovery database first.
4. Run application smoke tests and tenant-isolation checks before directing the API to the restored database.
5. Record the recovery point, recovery duration, and any missing transactions; then perform a controlled cutover.

A backup is not proven until a restore has been rehearsed. Define and test the required recovery point objective (RPO) and recovery time objective (RTO) for the organization.
