[CmdletBinding(SupportsShouldProcess=$true,ConfirmImpact="High")]
param([string]$ServiceName="PROJEX API")
$ErrorActionPreference="Stop"
$id=[Security.Principal.WindowsIdentity]::GetCurrent()
$p=[Security.Principal.WindowsPrincipal]::new($id)
if(-not $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw "Run from elevated PowerShell."}
$s=Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if($null -eq $s){Write-Host "Service '$ServiceName' is not installed.";return}
if($PSCmdlet.ShouldProcess($ServiceName,"Stop and delete Windows service")){
    if($s.Status -ne "Stopped"){
        Stop-Service -Name $ServiceName -Force
        (Get-Service -Name $ServiceName).WaitForStatus("Stopped",[TimeSpan]::FromSeconds(30))
    }
    & sc.exe delete $ServiceName | Out-Host
    if($LASTEXITCODE -ne 0){throw "sc.exe delete failed ($LASTEXITCODE)."}
    Write-Host "Service deleted. Published files and database data were not changed."
}
