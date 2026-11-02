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

# Pin the official EDB PostgreSQL installer and verify it before execution.
$PostgreSqlInstallerUri = "https://get.enterprisedb.com/postgresql/postgresql-16.15-5-windows-x64.exe"
$PostgreSqlInstallerSha256 = "4065dbc5ffb52010e7357dfd6a74934878f1c6faf7a75cad1411a22e11b7b929"

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
$logRoot = Join-Path $env:ProgramData "PROJEX\Logs"
$setupLog = Join-Path $logRoot ("server-setup-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".log")
$installerOptions = Join-Path $provisioningRoot "postgresql16-install.ini"
$installerPath = Join-Path $provisioningRoot "postgresql-16.15-5-windows-x64.exe"
$installerDebugLog = Join-Path $logRoot ("postgresql-installer-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".log")
$protectedPasswordPath = Join-Path $provisioningRoot "postgresql16-admin.dpapi"
$previousAdminPassword = $env:PROJEX_POSTGRES_ADMIN_PASSWORD
$generatedOrRecoveredPassword = $null

try {
    Add-Type -AssemblyName System.Security
    $null = New-Item -ItemType Directory -Path $provisioningRoot -Force
    $null = New-Item -ItemType Directory -Path $logRoot -Force
    Set-ProjexProvisioningAcl $provisioningRoot
    Set-ProjexProvisioningAcl $logRoot
    Start-Transcript -LiteralPath $setupLog -Force | Out-Null
    Write-Host "PROJEX server setup started: $(Get-Date -Format o)"
    Write-Host "A diagnostic log is being saved locally for this setup run."

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

        if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
            Write-Host "Downloading the official PostgreSQL 16.15-5 Windows installer directly from EnterpriseDB..."
            [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest -Uri $PostgreSqlInstallerUri -OutFile $installerPath -UseBasicParsing
        }

        Write-Host "Verifying the PostgreSQL installer signature hash before execution..."
        $actualInstallerHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualInstallerHash -ne $PostgreSqlInstallerSha256) {
            Remove-Item -LiteralPath $installerPath -Force -ErrorAction SilentlyContinue
            throw "The downloaded PostgreSQL installer failed SHA-256 verification. It was deleted and was not executed."
        }

        Write-Host "Installing verified PostgreSQL 16 with the locally generated database password..."
        $installerArguments = '--mode unattended --optionfile "' + $installerOptions + '" --unattendedmodeui minimal --debugtrace "' + $installerDebugLog + '"'
        $installerProcess = Start-Process -FilePath $installerPath -ArgumentList $installerArguments -Wait -PassThru -NoNewWindow
        if ($installerProcess.ExitCode -ne 0) {
            throw "The official PostgreSQL installer failed (exit code $($installerProcess.ExitCode)). Installer diagnostics: $installerDebugLog. The protected recovery credential was retained for a safe retry."
        }
        Remove-Item -LiteralPath $installerPath -Force -ErrorAction SilentlyContinue
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
    if ($LASTEXITCODE -ne 0) { throw "PROJEX database and API provisioning failed. Review the error above and rerun setup after correcting it." }

    if (Test-Path -LiteralPath $protectedPasswordPath -PathType Leaf) {
        Remove-Item -LiteralPath $protectedPasswordPath -Force
    }
    Write-Host ""
    Write-Host "PROJEX server setup completed successfully."
    Write-Host "PostgreSQL 16 is local-only. The API is installed as a Windows service."
    Write-Host "Keep the projex_owner migration password in an approved password vault."
}
catch {
    Write-Host ""
    Write-Host "PROJEX SERVER SETUP FAILED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Diagnostic log: $setupLog" -ForegroundColor Yellow
    throw
}
finally {
    $env:PROJEX_POSTGRES_ADMIN_PASSWORD = $previousAdminPassword
    if (Test-Path -LiteralPath $installerOptions) { Remove-Item -LiteralPath $installerOptions -Force }
    $generatedOrRecoveredPassword = $null
    try { Stop-Transcript | Out-Null } catch { }
}
