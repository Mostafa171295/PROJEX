[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$PublishDirectory,
    [string]$ServiceName="PROJEX API",
    [string]$DisplayName="PROJEX API",
    [string]$ListenUrl="http://127.0.0.1:5080"
)
$ErrorActionPreference="Stop"
$id=[Security.Principal.WindowsIdentity]::GetCurrent()
$p=[Security.Principal.WindowsPrincipal]::new($id)
if(-not $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw "Run from elevated PowerShell."}
$dir=(Resolve-Path -LiteralPath $PublishDirectory -ErrorAction Stop).Path
$exe=Join-Path $dir "Projex.Api.exe"
if(-not (Test-Path -LiteralPath $exe -PathType Leaf)){throw "Projex.Api.exe is missing from $dir."}
$conn=[Environment]::GetEnvironmentVariable("ConnectionStrings__Projex","Machine")
$key=[Environment]::GetEnvironmentVariable("Authentication__JwtSigningKey","Machine")
if([string]::IsNullOrWhiteSpace($conn)){throw "Set machine variable ConnectionStrings__Projex first."}
if([string]::IsNullOrWhiteSpace($key) -or [Text.Encoding]::UTF8.GetByteCount($key) -lt 32){throw "Set machine variable Authentication__JwtSigningKey to a random secret of at least 32 UTF-8 bytes."}
$uri=$null
if(-not [Uri]::TryCreate($ListenUrl,[UriKind]::Absolute,[ref]$uri)){throw "ListenUrl must be an absolute URL."}
if($uri.Scheme -notin @("http","https")){throw "ListenUrl must use HTTP or HTTPS."}
if($uri.Scheme -eq "http" -and -not $uri.IsLoopback){throw "Plain HTTP is allowed only on loopback; use HTTPS otherwise."}
if($uri.Host -in @("0.0.0.0","::","*","+")){throw "Wildcard listeners are not allowed."}
function Invoke-Sc([string[]]$ArgsList){
    & sc.exe @ArgsList | Out-Host
    if($LASTEXITCODE -ne 0){throw "sc.exe failed ($LASTEXITCODE): $($ArgsList -join ' ')"}
}
$bin='"'+$exe+'"'
$s=Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if($null -eq $s){
    New-Service -Name $ServiceName -DisplayName $DisplayName -Description "PROJEX Enterprise API background service." -BinaryPathName $bin -StartupType Automatic | Out-Null
}else{
    if($s.Status -ne "Stopped"){
        Stop-Service -Name $ServiceName -Force
        (Get-Service -Name $ServiceName).WaitForStatus("Stopped",[TimeSpan]::FromSeconds(30))
    }
    Invoke-Sc @("config",$ServiceName,"binPath=",$bin,"start=","delayed-auto")
}
Invoke-Sc @("description",$ServiceName,"PROJEX Enterprise API background service.")
Invoke-Sc @("config",$ServiceName,"start=","delayed-auto")
Invoke-Sc @("failure",$ServiceName,"reset=","86400","actions=","restart/5000/restart/15000/restart/30000")
# Scope the listener setting to this service; do not alter machine-wide ASP.NET settings.
$reg="HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
$envList=@()
$current=(Get-ItemProperty -LiteralPath $reg -Name Environment -ErrorAction SilentlyContinue).Environment
if($null -ne $current){$envList=@($current | Where-Object {$_ -notmatch '^ASPNETCORE_URLS='})}
$envList+="ASPNETCORE_URLS=$ListenUrl"
New-ItemProperty -LiteralPath $reg -Name Environment -PropertyType MultiString -Value $envList -Force | Out-Null
Start-Service -Name $ServiceName
(Get-Service -Name $ServiceName).WaitForStatus("Running",[TimeSpan]::FromSeconds(30))
Write-Host "Installed and started '$ServiceName' at $ListenUrl. Secret values were not displayed."
