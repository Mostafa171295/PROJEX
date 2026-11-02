#requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)][int]$Port = 5443,
    [string]$ServiceName = "PROJEX API"
)
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Run PROJEX Server Control Center as Administrator."
    }
}
function ConvertFrom-SecureInput([Security.SecureString]$Value) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}
function Assert-PrivateIpv4Cidr([string]$Value) {
    $parts = $Value.Split("/")
    if ($parts.Count -ne 2) { throw "Enter a client network in CIDR format, for example 192.168.10.0/24." }
    $address = $null
    if (-not [Net.IPAddress]::TryParse($parts[0], [ref]$address) -or $address.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork) {
        throw "The client network must be a valid IPv4 CIDR range."
    }
    $prefix = 0
    if (-not [int]::TryParse($parts[1], [ref]$prefix) -or $prefix -lt 8 -or $prefix -gt 32) {
        throw "The client network prefix must be between /8 and /32."
    }
    $bytes = $address.GetAddressBytes()
    $numeric = ([uint64]$bytes[0] -shl 24) -bor ([uint64]$bytes[1] -shl 16) -bor ([uint64]$bytes[2] -shl 8) -bor [uint64]$bytes[3]
    $mask = [uint64]([math]::Pow(2, 32) - [math]::Pow(2, 32 - $prefix))
    if (($numeric -band $mask) -ne $numeric) { throw "Use the network address, not a host address, for the CIDR range." }
    $privateRange = ($bytes[0] -eq 10) -or ($bytes[0] -eq 172 -and $bytes[1] -ge 16 -and $bytes[1] -le 31) -or ($bytes[0] -eq 192 -and $bytes[1] -eq 168)
    if (-not $privateRange) { throw "Remote access is limited to private IPv4 networks (10/8, 172.16/12 or 192.168/16)." }
}

Assert-Administrator
if (-not [Environment]::Is64BitOperatingSystem) { throw "PROJEX server requires 64-bit Windows." }
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -eq $service) { throw "The PROJEX API Windows Service is not installed. Run server setup first." }

$serverDnsName = (Read-Host "Server DNS name used by client devices (for example projex-server.company.local)").Trim()
if ($serverDnsName -notmatch '^(?=.{1,253}$)(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)(?:\.(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?))+$') {
    throw "Enter a fully qualified DNS name. Client devices must resolve this name to the server."
}
try {
    $resolvedAddresses = @([Net.Dns]::GetHostAddresses($serverDnsName) | ForEach-Object { $_.IPAddressToString })
} catch {
    throw "The server DNS name does not resolve from this computer. Configure DNS before enabling remote access."
}
$localAddresses = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop | Where-Object {
    $_.IPAddress -notlike "169.254.*" -and $_.IPAddress -ne "127.0.0.1"
} | Select-Object -ExpandProperty IPAddress)
if (-not ($resolvedAddresses | Where-Object { $_ -in $localAddresses })) {
    throw "The server DNS name must resolve to an IPv4 address assigned to this server."
}

$allowedSubnet = (Read-Host "Allowed private client subnet in CIDR format (for example 192.168.10.0/24)").Trim()
Assert-PrivateIpv4Cidr $allowedSubnet
$pfxPathInput = (Read-Host "Full path to the server TLS certificate PFX file").Trim().Trim('"')
if (-not (Test-Path -LiteralPath $pfxPathInput -PathType Leaf)) { throw "The PFX certificate file was not found." }
$pfxPath = (Resolve-Path -LiteralPath $pfxPathInput).Path
$pfxSecure = Read-Host "PFX password (input is hidden)" -AsSecureString
$pfxPassword = ConvertFrom-SecureInput $pfxSecure
if ([string]::IsNullOrEmpty($pfxPassword)) { throw "The PFX password cannot be empty." }

$certificate = $null
try {
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($pfxPath, $pfxPassword, [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::MachineKeySet)
    if (-not $certificate.HasPrivateKey) { throw "The PFX certificate does not contain its private key." }
    $now = [DateTime]::UtcNow
    if ($certificate.NotBefore.ToUniversalTime() -gt $now -or $certificate.NotAfter.ToUniversalTime() -lt $now.AddDays(14)) {
        throw "The TLS certificate is not currently valid or expires within 14 days."
    }
    $certificateDnsName = $certificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::DnsName, $false)
    if (-not [string]::Equals($certificateDnsName, $serverDnsName, [StringComparison]::OrdinalIgnoreCase)) {
        throw "The certificate DNS name '$certificateDnsName' does not match '$serverDnsName'. Use a certificate whose DNS name matches the address used by clients."
    }
    $chain = [Security.Cryptography.X509Certificates.X509Chain]::new()
    try {
        if (-not $chain.Build($certificate)) {
            $chainErrors = ($chain.ChainStatus | ForEach-Object { $_.StatusInformation.Trim() }) -join "; "
            throw "The certificate chain is not trusted on this server: $chainErrors. Install the issuing CA certificate before continuing."
        }
    } finally { $chain.Dispose() }
} finally {
    if ($null -ne $certificate) { $certificate.Dispose() }
}

$registryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
if (-not (Test-Path -LiteralPath $registryPath)) { throw "The PROJEX API service registry configuration was not found." }
$registry = Get-ItemProperty -LiteralPath $registryPath -Name Environment -ErrorAction SilentlyContinue
$existing = if ($null -ne $registry -and $null -ne $registry.Environment) { @($registry.Environment) } else { @() }
if (-not ($existing | Where-Object { $_ -like "ConnectionStrings__Projex=*" })) { throw "The API database connection is not configured. Run server setup first." }
if (-not ($existing | Where-Object { $_ -like "Authentication__JwtSigningKey=*" })) { throw "The API signing key is not configured. Run server setup first." }

$programData = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
$certificateDirectory = Join-Path $programData "PROJEX\Certificates"
$storedCertificate = Join-Path $certificateDirectory "server.pfx"
$null = New-Item -ItemType Directory -Path $certificateDirectory -Force
$directoryAcl = [Security.AccessControl.DirectorySecurity]::new()
$directoryAcl.SetAccessRuleProtection($true, $false)
foreach ($entry in @(
    @("NT AUTHORITY\SYSTEM", [Security.AccessControl.FileSystemRights]::FullControl),
    @("BUILTIN\Administrators", [Security.AccessControl.FileSystemRights]::FullControl),
    @("NT AUTHORITY\NETWORK SERVICE", [Security.AccessControl.FileSystemRights]::ReadAndExecute)
)) {
    $directoryAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
        $entry[0], $entry[1],
        [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit,
        [Security.AccessControl.PropagationFlags]::None,
        [Security.AccessControl.AccessControlType]::Allow
    ))
}
Set-Acl -LiteralPath $certificateDirectory -AclObject $directoryAcl
Copy-Item -LiteralPath $pfxPath -Destination $storedCertificate -Force

$fileAcl = [Security.AccessControl.FileSecurity]::new()
$fileAcl.SetAccessRuleProtection($true, $false)
$fileAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new("NT AUTHORITY\SYSTEM", [Security.AccessControl.FileSystemRights]::FullControl, [Security.AccessControl.AccessControlType]::Allow))
$fileAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new("BUILTIN\Administrators", [Security.AccessControl.FileSystemRights]::FullControl, [Security.AccessControl.AccessControlType]::Allow))
$fileAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new("NT AUTHORITY\NETWORK SERVICE", [Security.AccessControl.FileSystemRights]::Read, [Security.AccessControl.AccessControlType]::Allow))
Set-Acl -LiteralPath $storedCertificate -AclObject $fileAcl

$environment = @($existing | Where-Object {
    $_ -notmatch '^(ASPNETCORE_URLS|AllowedHosts|Kestrel__Certificates__Default__Path|Kestrel__Certificates__Default__Password)='
})
$environment += "ASPNETCORE_URLS=http://127.0.0.1:5080;https://0.0.0.0:$Port"
$environment += "AllowedHosts=$serverDnsName;localhost;127.0.0.1"
$environment += "Kestrel__Certificates__Default__Path=$storedCertificate"
$environment += "Kestrel__Certificates__Default__Password=$pfxPassword"
New-ItemProperty -LiteralPath $registryPath -Name Environment -PropertyType MultiString -Value $environment -Force | Out-Null

$registryAcl = Get-Acl -LiteralPath $registryPath
$registryAcl.SetAccessRuleProtection($true, $false)
foreach ($rule in @($registryAcl.Access)) { $registryAcl.RemoveAccessRuleAll($rule) }
$registryAcl.AddAccessRule([Security.AccessControl.RegistryAccessRule]::new("NT AUTHORITY\SYSTEM", [Security.AccessControl.RegistryRights]::FullControl, [Security.AccessControl.AccessControlType]::Allow))
$registryAcl.AddAccessRule([Security.AccessControl.RegistryAccessRule]::new("BUILTIN\Administrators", [Security.AccessControl.RegistryRights]::FullControl, [Security.AccessControl.AccessControlType]::Allow))
Set-Acl -LiteralPath $registryPath -AclObject $registryAcl

$ruleName = "PROJEX API HTTPS $Port"
Get-NetFirewallRule -DisplayName "PROJEX API HTTPS *" -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName $ruleName -Description "PROJEX HTTPS API; limited to $allowedSubnet" -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -RemoteAddress $allowedSubnet -Profile Domain,Private -EdgeTraversalPolicy Block | Out-Null

$remoteConfig = [ordered]@{ Enabled = $true; ServerDnsName = $serverDnsName; Port = $Port; AllowedClientSubnet = $allowedSubnet }
$configPath = Join-Path $programData "PROJEX\remote-access.json"
$remoteConfig | ConvertTo-Json | Set-Content -LiteralPath $configPath -Encoding UTF8

$service = Get-Service -Name $ServiceName
if ($service.Status -ne "Stopped") { Restart-Service -Name $ServiceName -Force } else { Start-Service -Name $ServiceName }
(Get-Service -Name $ServiceName).WaitForStatus("Running", [TimeSpan]::FromSeconds(30))

$response = $null
$deadline = [DateTime]::UtcNow.AddSeconds(30)
do {
    try {
        $response = Invoke-WebRequest -Uri "http://127.0.0.1:5080/health/live" -UseBasicParsing -TimeoutSec 3
        if ($response.StatusCode -eq 200) { break }
    } catch { Start-Sleep -Seconds 2 }
} while ([DateTime]::UtcNow -lt $deadline)
if ($null -eq $response -or $response.StatusCode -ne 200) {
    throw "The API service started, but its local liveness check did not return HTTP 200. Review the PROJEX API service logs."
}
try {
    $remoteUri = "https://{0}:{1}/health/live" -f $serverDnsName, $Port
    $remoteResponse = Invoke-WebRequest -Uri $remoteUri -UseBasicParsing -TimeoutSec 8
    if ($remoteResponse.StatusCode -ne 200) { throw "HTTPS liveness check returned HTTP $($remoteResponse.StatusCode)." }
} catch {
    throw "The local API is running, but the HTTPS endpoint could not be validated from this server. Check DNS, certificate trust, certificate SAN and the service log. Details: $($_.Exception.Message)"
}

Write-Host ""
Write-Host ("Secure client endpoint configured: https://{0}:{1}/" -f $serverDnsName, $Port)
Write-Host "Allowed client network: $allowedSubnet"
Write-Host "The API retains its local HTTP health endpoint; PostgreSQL remains local."
Write-Host "Install/trust the certificate issuer on every client device before connecting."
Write-Host "The PFX and service environment secrets are protected by Windows ACLs; secret values were not displayed."
