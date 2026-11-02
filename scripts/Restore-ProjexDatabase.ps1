[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "High")]
param(
    [Parameter(Mandatory = $true)]
    [string]$BackupFile,
    [Parameter(Mandatory = $true)]
    [string]$TargetDatabase,
    [string]$DbHost = "127.0.0.1",
    [int]$Port = 5432,
    [string]$DbUser = "projex_owner",
    [string]$PgRestorePath = "pg_restore",
    [string]$OpenSslPath = "openssl"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($Port -lt 1 -or $Port -gt 65535) { throw "Port must be between 1 and 65535." }
if ([string]::IsNullOrWhiteSpace($env:PGPASSWORD)) {
    throw "Set PGPASSWORD for the target database restore role in the process environment."
}
if ([string]::IsNullOrWhiteSpace($env:PROJEX_BACKUP_ENCRYPTION_PASSWORD)) {
    throw "Set PROJEX_BACKUP_ENCRYPTION_PASSWORD in the process environment."
}

$pgRestore = (Get-Command $PgRestorePath -ErrorAction Stop).Source
$openSsl = (Get-Command $OpenSslPath -ErrorAction Stop).Source
$resolvedBackup = (Resolve-Path -LiteralPath $BackupFile -ErrorAction Stop).Path
$manifestPath = $resolvedBackup + ".json"
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Backup manifest not found: '$manifestPath'."
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.format -ne 1 -or $manifest.encryption -notlike "OpenSSL AES-256-CBC*") {
    throw "Unsupported backup manifest format or encryption algorithm."
}
$actualHash = (Get-FileHash -LiteralPath $resolvedBackup -Algorithm SHA256).Hash
if ($actualHash -ne $manifest.sha256) {
    throw "Backup SHA-256 does not match its manifest. The archive may be damaged or altered."
}

$tempArchive = Join-Path ([IO.Path]::GetTempPath()) ("projex-restore-" + [Guid]::NewGuid().ToString("N") + ".dump")
try {
    Write-Host "Decrypting and validating backup archive..."
    & $openSsl enc -d -aes-256-cbc -pbkdf2 -iter 600000 -md sha256 -pass env:PROJEX_BACKUP_ENCRYPTION_PASSWORD -in $resolvedBackup -out $tempArchive
    if ($LASTEXITCODE -ne 0) { throw "OpenSSL decryption failed. Check the backup password and archive integrity." }

    & $pgRestore --list $tempArchive | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "pg_restore could not read the decrypted archive." }

    $target = "{0}:{1}/{2}" -f $DbHost, $Port, $TargetDatabase
    Write-Warning "Restore replaces objects in the target database that are present in this archive. It does not drop the database or restore cluster roles."
    if ($PSCmdlet.ShouldProcess($target, "Restore PostgreSQL backup '$([IO.Path]::GetFileName($resolvedBackup))'")) {
        & $pgRestore --no-password --host $DbHost --port $Port --username $DbUser --dbname $TargetDatabase --clean --if-exists --no-owner --no-acl --single-transaction --exit-on-error $tempArchive
        if ($LASTEXITCODE -ne 0) { throw "pg_restore failed with exit code $LASTEXITCODE." }
        Write-Host "Database restore completed successfully."
    }
}
finally {
    if (Test-Path -LiteralPath $tempArchive) { Remove-Item -LiteralPath $tempArchive -Force }
}
