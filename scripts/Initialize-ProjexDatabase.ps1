[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [string]$DbHost = "127.0.0.1",
    [ValidateRange(1, 65535)][int]$Port = 5432,
    [ValidatePattern('^[A-Za-z_][A-Za-z0-9_]{0,62}$')][string]$Database = "projex_enterprise",
    [string]$AdminUser = "postgres",
    [string]$PsqlPath = "psql",
    [string]$MigrationsPath,
    [string]$MigrateScriptPath = (Join-Path $PSScriptRoot "migrate.ps1"),
    [string]$InstallServiceScriptPath = (Join-Path $PSScriptRoot "Install-ProjexApiService.ps1"),
    [string]$TestServiceScriptPath = (Join-Path $PSScriptRoot "Test-ProjexApiService.ps1"),
    [string]$ListenUrl = "http://127.0.0.1:5080",
    [ValidateSet("Prefer", "Require", "VerifyCA", "VerifyFull")][string]$SslMode = "Prefer",
    [string]$RootCertificate
)
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($MigrationsPath)) {
    $packagedMigrations = Join-Path $PSScriptRoot "..\Database\Migrations"
    $repositoryMigrations = Join-Path $PSScriptRoot "..\database\migrations"
    $MigrationsPath = if (Test-Path -LiteralPath $packagedMigrations -PathType Container) {
        $packagedMigrations
    } else {
        $repositoryMigrations
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
function ConvertTo-SqlLiteral([string]$Value) {
    return "'" + $Value.Replace("'", "''") + "'"
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
if (-not $listenUri.IsLoopback) { throw "The API service must bind to loopback. Terminate remote HTTPS on a reverse proxy or VPN gateway on this server." }
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

$ownerSecure = Read-Host "Choose a strong password for the projex_owner migration account (at least 32 UTF-8 bytes)" -AsSecureString
$ownerPassword = ConvertFrom-SecureInput $ownerSecure
$ownerConfirmSecure = Read-Host "Confirm the projex_owner password" -AsSecureString
$ownerConfirmation = ConvertFrom-SecureInput $ownerConfirmSecure
if ([Text.Encoding]::UTF8.GetByteCount($ownerPassword) -lt 32) { throw "The projex_owner password must be at least 32 UTF-8 bytes." }
if ($ownerPassword -cne $ownerConfirmation) { throw "The projex_owner password confirmation did not match." }

$existingServiceEnvironment = @()
$serviceRegistryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\PROJEX API"
if (Test-Path -LiteralPath $serviceRegistryPath) {
    $serviceEnvironmentProperty = Get-ItemProperty -LiteralPath $serviceRegistryPath -Name Environment -ErrorAction SilentlyContinue
    if ($null -ne $serviceEnvironmentProperty) { $existingServiceEnvironment = @($serviceEnvironmentProperty.Environment) }
}
$existingConnectionString = $existingServiceEnvironment | Where-Object { $_ -like "ConnectionStrings__Projex=*" } | Select-Object -First 1
$existingJwtEntry = $existingServiceEnvironment | Where-Object { $_ -like "Authentication__JwtSigningKey=*" } | Select-Object -First 1
$appPassword = New-ProjexSecret
if ($null -ne $existingConnectionString) {
    $oldBuilder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $oldBuilder.ConnectionString = $existingConnectionString.Substring("ConnectionStrings__Projex=".Length)
    if (-not $oldBuilder.ContainsKey("Password") -or [string]::IsNullOrWhiteSpace([string]$oldBuilder["Password"])) {
        throw "The existing service connection string has no password. Repair service credentials before rerunning provisioning."
    }
    $appPassword = [string]$oldBuilder["Password"]
}
$jwtKey = if ($null -ne $existingJwtEntry -and $existingJwtEntry.Substring("Authentication__JwtSigningKey=".Length).Length -ge 32) {
    $existingJwtEntry.Substring("Authentication__JwtSigningKey=".Length)
} else {
    New-ProjexSecret 64
}
$tempRoot = Join-Path $env:ProgramData "PROJEX\Provisioning"
$tempSql = Join-Path $tempRoot ([Guid]::NewGuid().ToString("N") + ".sql")
$previousPgPassword = $env:PGPASSWORD
$previousPgSslMode = $env:PGSSLMODE
$previousPgSslRootCert = $env:PGSSLROOTCERT
$previousSetupConnection = $env:PROJEX_SETUP_CONNECTION_STRING
$previousSetupJwt = $env:PROJEX_SETUP_JWT_KEY

try {
    $null = New-Item -ItemType Directory -Path $tempRoot -Force
    $currentSid = $identity.User.Value
    & icacls.exe $tempRoot /inheritance:r /grant:r "*$($currentSid):(OI)(CI)F" "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" /T /C | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not restrict permissions on the temporary provisioning directory." }

    $ownerSql = ConvertTo-SqlLiteral $ownerPassword
    $appSql = ConvertTo-SqlLiteral $appPassword
    $sql = @"
DO `$projex`$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'projex_owner') THEN
        EXECUTE format('CREATE ROLE projex_owner LOGIN PASSWORD %L NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB NOREPLICATION', $ownerSql);
    ELSE
        EXECUTE format('ALTER ROLE projex_owner WITH LOGIN PASSWORD %L NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB NOREPLICATION', $ownerSql);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'projex_app') THEN
        EXECUTE format('CREATE ROLE projex_app LOGIN PASSWORD %L NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB NOREPLICATION', $appSql);
    ELSE
        EXECUTE format('ALTER ROLE projex_app WITH LOGIN PASSWORD %L NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB NOREPLICATION', $appSql);
    END IF;
END
`$projex`$;
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
    if (-not [string]::IsNullOrWhiteSpace($RootCertificate)) { $env:PGSSLROOTCERT = (Resolve-Path -LiteralPath $RootCertificate).Path }
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
    $env:PGSSLROOTCERT = $previousPgSslRootCert
    $env:PROJEX_SETUP_CONNECTION_STRING = $previousSetupConnection
    $env:PROJEX_SETUP_JWT_KEY = $previousSetupJwt
    if (Test-Path -LiteralPath $tempSql) { Remove-Item -LiteralPath $tempSql -Force }
    $adminPassword = $null
    $ownerPassword = $null
    $ownerConfirmation = $null
    $appPassword = $null
    $jwtKey = $null
    $ownerSql = $null
    $appSql = $null
    $sql = $null
    $connectionString = $null
    $connection = $null
    $oldBuilder = $null
    if ($null -ne $adminSecure) { $adminSecure.Dispose() }
    if ($null -ne $ownerSecure) { $ownerSecure.Dispose() }
    if ($null -ne $ownerConfirmSecure) { $ownerConfirmSecure.Dispose() }
}
