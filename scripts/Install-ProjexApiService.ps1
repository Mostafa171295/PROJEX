[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [string]$ServiceName = "PROJEX API",
    [string]$DisplayName = "PROJEX API",
    [string]$ListenUrl = "http://127.0.0.1:5080"
)
$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run from elevated PowerShell."
}
$dir = (Resolve-Path -LiteralPath $PublishDirectory -ErrorAction Stop).Path
$exe = Join-Path $dir "Projex.Api.exe"
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Projex.Api.exe is missing from $dir." }

$uri = $null
if (-not [Uri]::TryCreate($ListenUrl, [UriKind]::Absolute, [ref]$uri)) { throw "ListenUrl must be an absolute URL." }
if ($uri.Scheme -notin @("http", "https")) { throw "ListenUrl must use HTTP or HTTPS." }
if (-not $uri.IsLoopback) { throw "The API service must bind to loopback. Terminate remote HTTPS on a reverse proxy or VPN gateway on this server." }
if ($uri.Host -in @("0.0.0.0", "::", "*", "+")) { throw "Wildcard listeners are not allowed." }

function Invoke-Sc([string[]]$ArgsList) {
    & sc.exe @ArgsList | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "sc.exe failed ($LASTEXITCODE): $($ArgsList -join ' ')" }
}

$binaryPath = '"' + $exe + '"'
$registryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
$existingValues = @()
$currentValues = (Get-ItemProperty -LiteralPath $registryPath -Name Environment -ErrorAction SilentlyContinue).Environment
if ($null -ne $currentValues) { $existingValues = @($currentValues) }

$connectionString = [Environment]::GetEnvironmentVariable("PROJEX_SETUP_CONNECTION_STRING", "Process")
$jwtKey = [Environment]::GetEnvironmentVariable("PROJEX_SETUP_JWT_KEY", "Process")
if ([string]::IsNullOrWhiteSpace($connectionString)) {
    $entry = $existingValues | Where-Object { $_ -like "ConnectionStrings__Projex=*" } | Select-Object -First 1
    if ($null -ne $entry) { $connectionString = $entry.Substring("ConnectionStrings__Projex=".Length) }
}
if ([string]::IsNullOrWhiteSpace($jwtKey)) {
    $entry = $existingValues | Where-Object { $_ -like "Authentication__JwtSigningKey=*" } | Select-Object -First 1
    if ($null -ne $entry) { $jwtKey = $entry.Substring("Authentication__JwtSigningKey=".Length) }
}
if ([string]::IsNullOrWhiteSpace($connectionString) -or $connectionString -match "CHANGE_ME") {
    throw "Provide PROJEX_SETUP_CONNECTION_STRING in this PowerShell process or provision the service once before updating it."
}
if ([string]::IsNullOrWhiteSpace($jwtKey) -or [Text.Encoding]::UTF8.GetByteCount($jwtKey) -lt 32) {
    throw "Provide PROJEX_SETUP_JWT_KEY with at least 32 UTF-8 bytes in this process, or retain an existing service key."
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -eq $service) {
    New-Service -Name $ServiceName -DisplayName $DisplayName -Description "PROJEX Enterprise API background service." -BinaryPathName $binaryPath -StartupType Automatic | Out-Null
}
else {
    if ($service.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force
        (Get-Service -Name $ServiceName).WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
    }
    Invoke-Sc @("config", $ServiceName, "binPath=", $binaryPath, "start=", "delayed-auto")
}
Invoke-Sc @("description", $ServiceName, "PROJEX Enterprise API background service.")
Invoke-Sc @("config", $ServiceName, "obj=", "NT AUTHORITY\NetworkService", "password=", "")
Invoke-Sc @("config", $ServiceName, "start=", "delayed-auto")
Invoke-Sc @("failure", $ServiceName, "reset=", "86400", "actions=", "restart/5000/restart/15000/restart/30000")

$environment = @($existingValues | Where-Object {
    $_ -notmatch '^(ASPNETCORE_URLS|ConnectionStrings__Projex|Authentication__JwtSigningKey)='
})
$environment += "ASPNETCORE_URLS=$ListenUrl"
$environment += "ConnectionStrings__Projex=$connectionString"
$environment += "Authentication__JwtSigningKey=$jwtKey"
New-ItemProperty -LiteralPath $registryPath -Name Environment -PropertyType MultiString -Value $environment -Force | Out-Null

# Service-specific environment values include credentials. Restrict the service
# registry key to SYSTEM and local Administrators; ordinary users cannot read it.
$acl = Get-Acl -LiteralPath $registryPath
$acl.SetAccessRuleProtection($true, $false)
foreach ($rule in @($acl.Access)) { $acl.RemoveAccessRuleAll($rule) }
$systemSid = [Security.Principal.SecurityIdentifier]::new("S-1-5-18")
$administratorsSid = [Security.Principal.SecurityIdentifier]::new("S-1-5-32-544")
$acl.AddAccessRule([Security.AccessControl.RegistryAccessRule]::new(
    $systemSid, [Security.AccessControl.RegistryRights]::FullControl,
    [Security.AccessControl.InheritanceFlags]::ContainerInherit,
    [Security.AccessControl.PropagationFlags]::None,
    [Security.AccessControl.AccessControlType]::Allow))
$acl.AddAccessRule([Security.AccessControl.RegistryAccessRule]::new(
    $administratorsSid, [Security.AccessControl.RegistryRights]::FullControl,
    [Security.AccessControl.InheritanceFlags]::ContainerInherit,
    [Security.AccessControl.PropagationFlags]::None,
    [Security.AccessControl.AccessControlType]::Allow))
Set-Acl -LiteralPath $registryPath -AclObject $acl

Start-Service -Name $ServiceName
(Get-Service -Name $ServiceName).WaitForStatus("Running", [TimeSpan]::FromSeconds(30))
Write-Host "Installed and started '$ServiceName' at $ListenUrl. Secret values were not displayed."
