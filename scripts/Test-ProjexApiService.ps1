[CmdletBinding()]
param(
    [string]$HealthUrl="http://127.0.0.1:5080/health/ready",
    [string]$ServiceName="PROJEX API",
    [switch]$SkipServiceStatus,
    [ValidateRange(1,120)][int]$TimeoutSec=10
)
$ErrorActionPreference="Stop"
if(-not $SkipServiceStatus){
    $service=Get-Service -Name $ServiceName -ErrorAction Stop
    if($service.Status -ne "Running"){throw "Windows service '$ServiceName' is $($service.Status), not Running."}
    Write-Host "Windows service '$ServiceName' is Running."
}
$uri=$null
if(-not [Uri]::TryCreate($HealthUrl,[UriKind]::Absolute,[ref]$uri)){throw "HealthUrl must be an absolute URL."}
if($uri.Scheme -notin @("http","https")){throw "HealthUrl must use HTTP or HTTPS."}
try{
    $r=Invoke-WebRequest -Uri $uri -Method Get -TimeoutSec $TimeoutSec
    if([int]$r.StatusCode -lt 200 -or [int]$r.StatusCode -ge 300){throw "HTTP $($r.StatusCode)"}
    Write-Host "PROJEX API readiness check passed (HTTP $([int]$r.StatusCode))."
    if(-not [string]::IsNullOrWhiteSpace($r.Content)){Write-Host $r.Content}
}catch{throw "PROJEX API health check failed for '$HealthUrl': $($_.Exception.Message)"}
