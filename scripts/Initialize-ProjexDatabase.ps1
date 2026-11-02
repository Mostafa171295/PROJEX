[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [string]$DbHost = "127.0.0.1",
    [ValidateRange(1, 65535)][int]$Port = 5432,
    [ValidatePattern('^[A-Za-z_][A-Za-z0-9_]{0,62}$')][string]$Database = "projex_enterprise",
    [string]$AdminUser = "postgres",
    [string]$PsqlPath = "psql",
    [string]$MigrationsPath = (Join-Path $PSScriptRoot "..\database\migrations"),
    [string]$MigrateScriptPath = (Join-Path $PSScriptRoot "migrate.ps1"),
    [string]$InstallServiceScriptPath = (Join-Path $PSScriptRoot "Install-ProjexApiService.ps1"),
    [string]$TestServiceScriptPath = (Join-Path $PSScriptRoot "Test-ProjexApiService.ps1"),
    [string]$ListenUrl = "http://127.0.0.1:5080",
    [ValidateSet("Prefer", "Require", "VerifyCA", "VerifyFull")][string]$SslMode = "Prefer",
    [string]$RootCertificate
)
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

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
function ConvertFrom-SecureInput([Security.SecureString]$SecureValue) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw "Run this setup from an elevated PowerShell session." }
if ($Database -eq "postgres") { throw "Choose an application database name other than the PostgreSQL maintenance database." }
foreach ($required in @($MigrateScriptPath, $InstallServiceScriptPath, $TestServiceScriptPath)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required setup script not found: $required" }
}
if (-not (Test-Path -LiteralPath $MigrationsPath -PathType Container)) { throw "Migration directory not found: $MigrationsPath" }
if (-not (Test-Path -LiteralPath $PublishDirectory -PathType Container)) { throw "API publish directory not found: $PublishDirectory" }

$listenUri = $null
if (-not [Uri]::TryCreate($ListenUrl, [UriKind]::Absolute, [ref]$listenUri)) { throw "ListenUrl must be an absolute URL." }
if ($listenUri.Scheme -notin @("http", "https")) { throw "ListenUrl must use HTTP or HTTPS." }
if ($listenUri.Scheme -eq "http" -and -not $listenUri.IsLoopback) { throw "Plain HTTP is permitted only on loopback. Use HTTPS for a non-loopback listener." }
if ($listenUri.Host -in @("0.0.0.0", "::", "*", "+")) { throw "Wildcard listeners are not allowed." }

$ip = $null
$isLoopback = ([Net.IPAddress]::TryParse($DbHost, [ref]$ip) -and [Net.IPAddress]::IsLoopback($ip)) -or ($DbHost -in @("localhost", "localhost."))
if (-not $isLoopback) {
    if ($SslMode -ne "VerifyFull") { throw "Remote PostgreSQL requires -SslMode VerifyFull and a trusted -RootCertificate file." }
    if ([string]::IsNullOrWhiteSpace($RootCertificate) -or -not (Test-Path -LiteralPath $RootCertificate -PathType Leaf)) { throw "A valid -RootCertificate file is required for remote PostgreSQL." }
}
elseif ($SslMode -eq "VerifyFull" -and ([string]::IsNullOrWhiteSpace($RootCertificate) -or -not (Test-Path -LiteralPath $RootCertificate -PathType Leaf))) {
    throw "VerifyFull requires a valid -RootCertificate file."
}

$psql = (Get-Command $PsqlPath -ErrorAction Stop).Source
$adminSecure = Read-Host "PostgreSQL administrator password for '$AdminUser' (input is hidden)" -AsSecureString
$adminPassword = ConvertFrom-SecureInput $adminSecure
if ([string]::IsNullOrEmpty($adminPassword)) { throw "The PostgreSQL administrator password cannot be empty." }

$ownerPassword = New-ProjexSecret
$appPassword = New-ProjexSecret
$jwtKey = New-ProjexSecret 64
$tempRoot = Join-Path $env:ProgramData "PROJEX\Provisioning"
$tempSql = Join-Path $tempRoot ([Guid]::NewGuid().ToString("N") + ".sql")
$previousPgPassword = $env:PGPASSWORD
$previousPgSslMode = $env:PGSSLMODE
$previousSetupConnection = $env:PROJEX_SETUP_CONNECTION_STRING
$previousSetupJwt = $env:PROJEX_SETUP_JWT_KEY
$previousOwnerPassword = $env:PROJEX_SETUP_OWNER_PASSWORD

try {
    $null = New-Item -ItemType Directory -Path $tempRoot -Force
    $currentSid = $identity.User.Value
    & icacls.exe $tempRoot /inheritance:r /grant:r "*$($currentSid):(OI)(CI)F" "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" /T /C | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not restrict permissions on the temporary provisioning directory." }

    $sql = @"
\set owner_pw '$ownerPassword'
\set app_pw '$appPassword'
SELECT format('CREATE ROLE projex_owner LOGIN PASSWORD %L NOSUPERUSER NOBYPASSRLS NOCREATEROLE', :'owner_pw')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'projex_owner') \gexec
ALTER ROLE projex_owner WITH LOGIN PASSWORD :'owner_pw' NOSUPERUSER NOBYPASSRLS NOCREATEROLE;
SELECT format('CREATE ROLE projex_app LOGIN PASSWORD %L NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB', :'app_pw')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'projex_app') \gexec
ALTER ROLE projex_app WITH LOGIN PASSWORD :'app_pw' NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB;
SELECT format('CREATE DATABASE %I OWNER projex_owner', '$Database')
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = '$Database') \gexec
ALTER DATABASE "$Database" OWNER TO projex_owner;
GRANT CONNECT ON DATABASE "$Database" TO projex_app;
GRANT CREATE ON DATABASE "$Database" TO projex_owner;
\connect "$Database"
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT CREATE ON SCHEMA public TO projex_owner;
GRANT USAGE ON SCHEMA public TO projex_app;
"@
    [IO.File]::WriteAllText($tempSql, $sql, [Text.UTF8Encoding]::new($false))

    $env:PGPASSWORD = $adminPassword
    $env:PGSSLMODE = $SslMode.ToLowerInvariant()
    Write-Host "Creating or updating the least-privileged PostgreSQL roles and database..."
    & $psql --no-password --host $DbHost --port $Port --username $AdminUser --dbname postgres --set ON_ERROR_STOP=1 --file $tempSql
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL role/database provisioning failed (psql exit code $LASTEXITCODE)." }

    $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
    $connection["Host"] = $DbHost
    $connection["Port"] = $Port
    $connection["Database"] = $Database
    $connection["Username"] = "projex_app"
    $connection["Password"] = $appPassword
    $connection["SSL Mode"] = $SslMode
    if (-not [string]::IsNullOrWhiteSpace($RootCertificate)) { $connection["Root Certificate"] = (Resolve-Path -LiteralPath $RootCertificate).Path }
    $connection["Timeout"] = 15
    $connection["Command Timeout"] = 30
    $connection["Include Error Detail"] = $false
    $connectionString = $connection.ConnectionString

    Write-Host "Applying numbered database migrations using the separate projex_owner role..."
    $env:PGPASSWORD = $ownerPassword
    & $MigrateScriptPath -DbHost $DbHost -Port $Port -Database $Database -MigrationsPath $MigrationsPath -PsqlPath $psql
    if ($LASTEXITCODE -ne 0) { throw "Database migrations failed." }

    $env:PROJEX_SETUP_CONNECTION_STRING = $connectionString
    $env:PROJEX_SETUP_JWT_KEY = $jwtKey
    $env:PROJEX_SETUP_OWNER_PASSWORD = $ownerPassword
    $env:PGPASSWORD = $null

    Write-Host "Installing the API as a Windows service and applying service-scoped configuration..."
    & $InstallServiceScriptPath -PublishDirectory $PublishDirectory -ListenUrl $ListenUrl
    if ($LASTEXITCODE -ne 0) { throw "Windows API service installation failed." }

    Write-Host "Checking API readiness and PostgreSQL connectivity..."
    & $TestServiceScriptPath -HealthUrl ($ListenUrl.TrimEnd("/") + "/health/ready")
    if ($LASTEXITCODE -ne 0) { throw "The API service did not pass readiness. Inspect the Windows Application event log and service status." }

    Write-Host "PROJEX database provisioning completed. Database: $Database; API listener: $ListenUrl."
    Write-Host "Generated database and JWT secrets were not displayed. Store the owner credential in an approved password vault."
}
finally {
    $env:PGPASSWORD = $previousPgPassword
    $env:PGSSLMODE = $previousPgSslMode
    $env:PROJEX_SETUP_CONNECTION_STRING = $previousSetupConnection
    $env:PROJEX_SETUP_JWT_KEY = $previousSetupJwt
    $env:PROJEX_SETUP_OWNER_PASSWORD = $previousOwnerPassword
    if (Test-Path -LiteralPath $tempSql) { Remove-Item -LiteralPath $tempSql -Force }
    $adminPassword = $null
    $ownerPassword = $null
    $appPassword = $null
    $jwtKey = $null
    $connectionString = $null
    if ($null -ne $adminSecure) { $adminSecure.Dispose() }
}
