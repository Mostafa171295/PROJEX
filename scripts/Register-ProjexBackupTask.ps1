[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "Medium")]
param(
    [string]$TaskName = "PROJEX Database Backup",
    [string]$BackupScriptPath = (Join-Path $PSScriptRoot "Backup-ProjexDatabase.ps1"),
    [string]$BackupDirectory = "C:\ProgramData\PROJEX\Backups",
    [string]$At = "02:00",
    [int]$RetentionDays = 14,
    [switch]$Unregister
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($Unregister) {
    if ($PSCmdlet.ShouldProcess($TaskName, "Remove scheduled backup task")) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
        Write-Host "Scheduled backup task removed (if it existed)."
    }
    return
}

if (-not (Test-Path -LiteralPath $BackupScriptPath -PathType Leaf)) {
    throw "Backup script not found: '$BackupScriptPath'."
}
if ($RetentionDays -lt 1) { throw "RetentionDays must be at least 1." }
$parsedTime = [DateTime]::MinValue
if (-not [DateTime]::TryParseExact($At, "HH:mm", [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$parsedTime)) {
    throw "At must use 24-hour HH:mm format, for example 02:00."
}

foreach ($name in @("PGPASSWORD", "PROJEX_BACKUP_ENCRYPTION_PASSWORD")) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name, "Machine"))) {
        throw "Machine-level environment variable '$name' is required because the scheduled task runs as SYSTEM."
    }
}

$null = New-Item -ItemType Directory -Path $BackupDirectory -Force
$resolvedScript = (Resolve-Path -LiteralPath $BackupScriptPath).Path
$resolvedDirectory = (Resolve-Path -LiteralPath $BackupDirectory).Path
$powerShell = Join-Path $PSHOME "powershell.exe"
$arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{0}" -BackupDirectory "{1}" -RetentionDays {2}' -f $resolvedScript, $resolvedDirectory, $RetentionDays
$action = New-ScheduledTaskAction -Execute $powerShell -Argument $arguments
$trigger = New-ScheduledTaskTrigger -Daily -At $parsedTime
$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Hours 4)

if ($PSCmdlet.ShouldProcess($TaskName, "Register daily PostgreSQL backup at $At")) {
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description "Encrypted PROJEX PostgreSQL backup with SHA-256 manifest and retention." -Force | Out-Null
    Write-Host ("Registered daily backup task '{0}' at {1}." -f $TaskName, $At)
    Write-Host "The task runs as SYSTEM and reads machine-level PGPASSWORD and PROJEX_BACKUP_ENCRYPTION_PASSWORD."
}
