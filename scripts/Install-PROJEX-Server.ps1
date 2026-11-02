#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$PublishDirectory = (Join-Path $PSScriptRoot "..\API"),
    [string]$DatabaseHost = "127.0.0.1",
    [ValidateRange(1, 65535)][int]$DatabasePort = 5432,
    [string]$PostgreSqlServiceName = "postgresql-x64-16"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Run PROJEX Server Control Center as Administrator and retry server setup."
    }
}

function New-ProjexSecret([int]$ByteCount = 48) {
    $bytes = New-Object byte[] $ByteCount
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
        return [Convert]::ToBase64String($bytes).TrimEnd("=").Replace("+", "-").Replace("/", "_")
    }
    finally {
        [Array]::Clear($bytes, 0, $bytes.Length)
        $rng.Dispose()
    }
}

function Set-ProjexProvisioningAcl([string]$Path) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    & icacls.exe $Path /inheritance:r /grant:r "*$($identity.User.Value):(OI)(CI)F" "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" /T /C | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not secure the PROJEX provisioning directory ACL." }
}

function Protect-ProjexSecret([string]$Value, [string]$Path) {
    $plainBytes = [Text.Encoding]::UTF8.GetBytes($Value)
    try {
        $protected = [Security.Cryptography.ProtectedData]::Protect(
            $plainBytes, $null, [Security.Cryptography.DataProtectionScope]::LocalMachine)
        [IO.File]::WriteAllBytes($Path, $protected)
        [Array]::Clear($protected, 0, $protected.Length)
    }
    finally {
        [Array]::Clear($plainBytes, 0, $plainBytes.Length)
    }
}

function Unprotect-ProjexSecret([string]$Path) {
    $protected = [IO.File]::ReadAllBytes($Path)
    try {
        $plainBytes = [Security.Cryptography.ProtectedData]::Unprotect(
            $protected, $null, [Security.Cryptography.DataProtectionScope]::LocalMachine)
        try { return [Text.Encoding]::UTF8.GetString($plainBytes) }
        finally { [Array]::Clear($plainBytes, 0, $plainBytes.Length) }
    }
    finally { [Array]::Clear($protected, 0, $protected.Length) }
}

Assert-Administrator
if (-not [Environment]::Is64BitOperatingSystem) { throw "PROJEX requires 64-bit Windows." }
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw "PROJEX server setup is supported only on Windows." }

$publish = (Resolve-Path -LiteralPath $PublishDirectory -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath (Join-Path $publish "Projex.Api.exe") -PathType Leaf)) {
    throw "Projex.Api.exe was not found in $publish. Extract the complete PROJEX server package first."
}

$provisioningRoot = Join-Path $env:ProgramData "PROJEX\Provisioning"
$installerOptions = Join-Path $provisioningRoot "postgresql16-install.ini"
$protectedPasswordPath = Join-Path $provisioningRoot "postgresql16-admin.dpapi"
$previousAdminPassword = $env:PROJEX_POSTGRES_ADMIN_PASSWORD
$generatedOrRecoveredPassword = $null

try {
    Add-Type -AssemblyName System.Security
    $null = New-Item -ItemType Directory -Path $provisioningRoot -Force
    Set-ProjexProvisioningAcl $provisioningRoot

    $service = Get-Service -Name $PostgreSqlServiceName -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        $otherPostgreSqlServices = @(Get-Service -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "postgresql-x64-*" })
        if ($otherPostgreSqlServices.Count -gt 0) {
            $names = ($otherPostgreSqlServices | Select-Object -ExpandProperty Name) -join ", "
            throw "A different PostgreSQL major version is installed ($names). PROJEX requires PostgreSQL 16; the setup will not replace or modify another major version."
        }

        $listeners = @(Get-NetTCPConnection -State Listen -LocalPort $DatabasePort -ErrorAction SilentlyContinue)
        if ($listeners.Count -gt 0) {
            throw "Port $DatabasePort is already in use by another process. Stop and identify that service before installing PostgreSQL 16."
        }

        $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
        if ($null -eq $winget) {
            throw "Windows Package Manager (winget) was not found. Install or update Microsoft's App Installer, then run server setup again. No database changes were made."
        }

        if (Test-Path -LiteralPath $protectedPasswordPath -PathType Leaf) {
            $generatedOrRecoveredPassword = Unprotect-ProjexSecret $protectedPasswordPath
        }
        else {
            $generatedOrRecoveredPassword = New-ProjexSecret
            Protect-ProjexSecret $generatedOrRecoveredPassword $protectedPasswordPath
        }

        $optionLines = @(
            "mode=unattended",
            "unattendedmodeui=minimal",
            "superaccount=postgres",
            "superpassword=$generatedOrRecoveredPassword",
            "serverport=$DatabasePort",
            "servicename=$PostgreSqlServiceName",
            "prefix=$($env:ProgramFiles)\PostgreSQL\16",
            "datadir=$($env:ProgramFiles)\PostgreSQL\16\data"
        )
        [IO.File]::WriteAllLines($installerOptions, $optionLines, [Text.UTF8Encoding]::new($false))

        Write-Host "Installing PostgreSQL 16 from the verified Windows Package Manager source..."
        Write-Host "The database superuser password is generated locally and is not displayed."
        $override = '--mode unattended --optionfile "' + $installerOptions + '" --unattendedmodeui minimal'
        & $winget.Source install --id PostgreSQL.PostgreSQL.16 --exact --silent --accept-package-agreements --accept-source-agreements --override $override
        if ($LASTEXITCODE -ne 0) {
            throw "PostgreSQL 16 installation failed (winget exit code $LASTEXITCODE). The protected recovery credential was retained so setup can be retried safely."
        }
    }
    elseif ($service.Status -ne "Running") {
        Write-Host "Starting the existing PostgreSQL 16 service..."
        Start-Service -Name $service.Name
    }

    $deadline = [DateTime]::UtcNow.AddMinutes(3)
    do {
        $service = Get-Service -Name $PostgreSqlServiceName -ErrorAction SilentlyContinue
        if ($null -ne $service -and $service.Status -eq "Running") { break }
        Start-Sleep -Seconds 3
    } while ([DateTime]::UtcNow -lt $deadline)

    if ($null -eq $service -or $service.Status -ne "Running") {
        throw "PostgreSQL 16 service '$PostgreSqlServiceName' did not reach Running state. The protected recovery credential was retained; fix the service and rerun setup."
    }

    $listenerDeadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        $listeners = @(Get-NetTCPConnection -State Listen -LocalPort $DatabasePort -ErrorAction SilentlyContinue)
        if ($listeners.Count -gt 0) { break }
        Start-Sleep -Seconds 2
    } while ([DateTime]::UtcNow -lt $listenerDeadline)
    if ($listeners.Count -eq 0) {
        throw "PostgreSQL 16 service is running but no database listener appeared on port $DatabasePort. The protected recovery credential was retained; inspect PostgreSQL logs and retry."
    }

    if (Test-Path -LiteralPath $protectedPasswordPath -PathType Leaf) {
        $generatedOrRecoveredPassword = Unprotect-ProjexSecret $protectedPasswordPath
        $env:PROJEX_POSTGRES_ADMIN_PASSWORD = $generatedOrRecoveredPassword
    }

    Write-Host "PostgreSQL 16 is running. PROJEX will now create its database, apply migrations, install the API service, and verify readiness."
    & (Join-Path $PSScriptRoot "Install-PROJEX-Local.ps1") -PublishDirectory $publish -DatabaseHost $DatabaseHost -DatabasePort $DatabasePort -PostgreSqlServiceName $PostgreSqlServiceName
    if ($LASTEXITCODE -ne 0) { throw "PROJEX database and API provisioning failed. Review the error above and rerun server setup after correcting it." }

    if (Test-Path -LiteralPath $protectedPasswordPath -PathType Leaf) {
        Remove-Item -LiteralPath $protectedPasswordPath -Force
    }
    Write-Host ""
    Write-Host "PROJEX server setup completed successfully."
    Write-Host "PostgreSQL 16 is local-only. The API is installed as a Windows service."
    Write-Host "Keep the projex_owner migration password in an approved password vault."
}
finally {
    $env:PROJEX_POSTGRES_ADMIN_PASSWORD = $previousAdminPassword
    if (Test-Path -LiteralPath $installerOptions) { Remove-Item -LiteralPath $installerOptions -Force }
    $generatedOrRecoveredPassword = $null
}
