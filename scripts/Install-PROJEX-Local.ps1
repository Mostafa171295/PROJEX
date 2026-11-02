#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$PublishDirectory = (Join-Path $PSScriptRoot "..\API"),
    [string]$DatabaseHost = "127.0.0.1",
    [ValidateRange(1, 65535)][int]$DatabasePort = 5432,
    [string]$PostgreSqlServiceName = "postgresql-x64-16",
    [string]$PsqlPath = "psql"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Open Windows PowerShell with Run as administrator, then run this setup again."
    }
}

function Test-LoopbackAddress([string]$Address) {
    $parsed = $null
    return [Net.IPAddress]::TryParse($Address, [ref]$parsed) -and [Net.IPAddress]::IsLoopback($parsed)
}

Assert-Administrator
if (-not [Environment]::Is64BitOperatingSystem) { throw "PROJEX requires 64-bit Windows." }
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw "PROJEX local setup is supported only on Windows." }

$publish = (Resolve-Path -LiteralPath $PublishDirectory -ErrorAction Stop).Path
if (-not (Test-Path -LiteralPath (Join-Path $publish "Projex.Api.exe") -PathType Leaf)) {
    throw "Projex.Api.exe was not found in $publish. Install the PROJEX server component first."
}

$psqlCommand = Get-Command $PsqlPath -ErrorAction SilentlyContinue
if ($null -eq $psqlCommand) {
    $defaultPgBin = Join-Path $env:ProgramFiles "PostgreSQL\16\bin"
    $candidate = Join-Path $defaultPgBin "psql.exe"
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        $env:Path = "$defaultPgBin;$env:Path"
        $psqlCommand = Get-Command $candidate -ErrorAction Stop
    }
}
if ($null -eq $psqlCommand) {
    throw "PostgreSQL 16 client tools were not found. Install PostgreSQL 16 locally or pass -PsqlPath with the full path to psql.exe."
}
$psqlVersion = (& $psqlCommand.Source --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $psqlVersion -notmatch '\\b16\\.') {
    throw "PostgreSQL 16 client tools are required. Detected: $psqlVersion"
}

$service = Get-Service -Name $PostgreSqlServiceName -ErrorAction SilentlyContinue
if ($null -eq $service) {
    $candidates = @(Get-Service -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "postgresql-x64-*" })
    if ($candidates.Count -eq 1) { $service = $candidates[0] }
}
if ($null -eq $service) { throw "A local PostgreSQL Windows service was not found. Install PostgreSQL 16 and verify its service name." }
if ($service.Status -ne "Running") { throw "PostgreSQL service $($service.Name) is not running. Start it from Windows Services, then rerun setup." }

if (-not (Test-LoopbackAddress $DatabaseHost) -and $DatabaseHost -notin @("localhost", "localhost.")) {
    throw "The single-computer setup accepts only a loopback PostgreSQL host (127.0.0.1 or localhost)."
}
$listeners = @(Get-NetTCPConnection -State Listen -LocalPort $DatabasePort -ErrorAction SilentlyContinue)
if ($listeners.Count -eq 0) { throw "No PostgreSQL listener was found on local port $DatabasePort. Check PostgreSQL configuration and port." }
$remoteListeners = @($listeners | Where-Object { -not (Test-LoopbackAddress $_.LocalAddress) })
if ($remoteListeners.Count -gt 0) {
    $addresses = ($remoteListeners | Select-Object -ExpandProperty LocalAddress -Unique) -join ", "
    throw "PostgreSQL is listening on a non-loopback address ($addresses). Restrict listen_addresses to localhost before continuing; port 5432 must not be exposed to the network."
}

Write-Host "Local prerequisites passed."
Write-Host "PostgreSQL service: $($service.Name)"
Write-Host "PostgreSQL client: $($psqlCommand.Source)"
Write-Host "Database endpoint: $DatabaseHost`:$DatabasePort (loopback only)"
Write-Host ""
Write-Host "The next step provisions the PROJEX database, applies migrations, installs the API Windows service, and verifies readiness."
Write-Host "You will be prompted for the PostgreSQL administrator password and a new migration-owner password. Password input is hidden."
Write-Host ""

$setupArgs = @{ PublishDirectory = $publish; DbHost = $DatabaseHost; Port = $DatabasePort; PsqlPath = $psqlCommand.Source }
& (Join-Path $PSScriptRoot "Initialize-ProjexDatabase.ps1") @setupArgs

Write-Host ""
Write-Host "PROJEX local setup completed. Set the desktop API address to http://127.0.0.1:5080/ if needed."
Write-Host "Run Test-ProjexApiService.ps1 to repeat the service and database readiness check."
