[CmdletBinding()]
param(
    [string]$DbHost = "127.0.0.1",
    [int]$Port = 5432,
    [string]$Database = "projex_enterprise",
    [string]$DbUser = "projex_owner",
    [string]$BackupDirectory = "C:\ProgramData\PROJEX\Backups",
    [int]$RetentionDays = 14,
    [string]$PgDumpPath = "pg_dump",
    [string]$OpenSslPath = "openssl"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($Port -lt 1 -or $Port -gt 65535) { throw "Port must be between 1 and 65535." }
if ($RetentionDays -lt 1) { throw "RetentionDays must be at least 1." }
if ([string]::IsNullOrWhiteSpace($env:PGPASSWORD)) {
    throw "Set PGPASSWORD for the least-privileged database backup role in the process environment."
}
if ([string]::IsNullOrWhiteSpace($env:PROJEX_BACKUP_ENCRYPTION_PASSWORD) -or $env:PROJEX_BACKUP_ENCRYPTION_PASSWORD.Length -lt 16) {
    throw "Set PROJEX_BACKUP_ENCRYPTION_PASSWORD to a strong secret of at least 16 characters."
}

$pgDump = (Get-Command $PgDumpPath -ErrorAction Stop).Source
$openSsl = (Get-Command $OpenSslPath -ErrorAction Stop).Source
$null = New-Item -ItemType Directory -Path $BackupDirectory -Force
$resolvedBackupDirectory = (Resolve-Path -LiteralPath $BackupDirectory).Path
$safeDatabase = $Database -replace '[^A-Za-z0-9_.-]', '_'
$stamp = [DateTime]::UtcNow.ToString("yyyyMMddTHHmmssZ")
$baseName = "projex_{0}_{1}" -f $safeDatabase, $stamp
$plainTemp = Join-Path $resolvedBackupDirectory ($baseName + ".dump.tmp")
$encryptedTemp = Join-Path $resolvedBackupDirectory ($baseName + ".dump.enc.tmp")
$backupFile = Join-Path $resolvedBackupDirectory ($baseName + ".dump.enc")
$manifestFile = $backupFile + ".json"

try {
    Write-Host "Creating PostgreSQL custom-format backup for '$Database'..."
    & $pgDump --no-password --host $DbHost --port $Port --username $DbUser --dbname $Database --format=custom --no-owner --no-acl --file $plainTemp
    if ($LASTEXITCODE -ne 0) { throw "pg_dump failed with exit code $LASTEXITCODE." }
    if (-not (Test-Path -LiteralPath $plainTemp) -or (Get-Item -LiteralPath $plainTemp).Length -eq 0) {
        throw "pg_dump did not produce a non-empty archive."
    }

    Write-Host "Encrypting backup with OpenSSL AES-256-CBC and PBKDF2..."
    & $openSsl enc -aes-256-cbc -salt -pbkdf2 -iter 600000 -md sha256 -pass env:PROJEX_BACKUP_ENCRYPTION_PASSWORD -in $plainTemp -out $encryptedTemp
    if ($LASTEXITCODE -ne 0) { throw "OpenSSL encryption failed with exit code $LASTEXITCODE." }

    $hash = (Get-FileHash -LiteralPath $encryptedTemp -Algorithm SHA256).Hash
    $pgVersion = (& $pgDump --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw "Could not read pg_dump version." }

    Move-Item -LiteralPath $encryptedTemp -Destination $backupFile
    $manifest = [ordered]@{
        format = 1
        database = $Database
        host = $DbHost
        port = $Port
        database_role = $DbUser
        created_utc = [DateTime]::UtcNow.ToString("o")
        archive = [IO.Path]::GetFileName($backupFile)
        encryption = "OpenSSL AES-256-CBC / PBKDF2-HMAC-SHA256 (600000 iterations)"
        sha256 = $hash
        pg_dump_version = $pgVersion
    }
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestFile -Encoding UTF8

    $cutoff = [DateTime]::UtcNow.AddDays(-$RetentionDays)
    Get-ChildItem -LiteralPath $resolvedBackupDirectory -Filter ("projex_{0}_*.dump.enc" -f $safeDatabase) -File |
        Where-Object { $_.LastWriteTimeUtc -lt $cutoff } |
        ForEach-Object {
            $sidecar = $_.FullName + ".json"
            Remove-Item -LiteralPath $_.FullName -Force
            if (Test-Path -LiteralPath $sidecar) { Remove-Item -LiteralPath $sidecar -Force }
            Write-Host ("Removed expired backup: {0}" -f $_.Name)
        }

    Write-Host ("Backup completed: {0}" -f $backupFile)
    Write-Host ("SHA-256: {0}" -f $hash)
}
finally {
    foreach ($temporary in @($plainTemp, $encryptedTemp)) {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}
