#requires -Version 5.1
[CmdletBinding()]
param([string]$ServiceName = "PROJEX API")
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw "Run PROJEX Server Control Center as Administrator." }

$registryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
if (-not (Test-Path -LiteralPath $registryPath)) { throw "The PROJEX API Windows Service is not installed." }
$registry = Get-ItemProperty -LiteralPath $registryPath -Name Environment -ErrorAction SilentlyContinue
$existing = if ($null -ne $registry -and $null -ne $registry.Environment) { @($registry.Environment) } else { @() }
$environment = @($existing | Where-Object { $_ -notmatch '^(ASPNETCORE_URLS|Kestrel__Certificates__Default__Path|Kestrel__Certificates__Default__Password)=' })
$environment += "ASPNETCORE_URLS=http://127.0.0.1:5080"
New-ItemProperty -LiteralPath $registryPath -Name Environment -PropertyType MultiString -Value $environment -Force | Out-Null

$programData = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
Get-NetFirewallRule -DisplayName "PROJEX API HTTPS *" -ErrorAction SilentlyContinue | Remove-NetFirewallRule
$certificatePath = Join-Path $programData "PROJEX\Certificates\server.pfx"
if (Test-Path -LiteralPath $certificatePath) { Remove-Item -LiteralPath $certificatePath -Force }
$certificateDirectory = Split-Path -Parent $certificatePath
if (Test-Path -LiteralPath $certificateDirectory) { Remove-Item -LiteralPath $certificateDirectory -Force -Recurse }
$configPath = Join-Path $programData "PROJEX\remote-access.json"
if (Test-Path -LiteralPath $configPath) { Remove-Item -LiteralPath $configPath -Force }

$service = Get-Service -Name $ServiceName
if ($service.Status -ne "Stopped") { Restart-Service -Name $ServiceName -Force } else { Start-Service -Name $ServiceName }
(Get-Service -Name $ServiceName).WaitForStatus("Running", [TimeSpan]::FromSeconds(30))
Write-Host "Remote HTTPS listener and its firewall rule were removed. PROJEX API is local-only again."
