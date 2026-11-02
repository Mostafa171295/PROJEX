#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$SourceDirectory = (Join-Path $PSScriptRoot "Desktop"),
    [string]$InstallDirectory = (Join-Path $env:ProgramFiles "PROJEX\Client"),
    [switch]$NoDesktopShortcut
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this installer as Administrator."
}

$source = (Resolve-Path -LiteralPath $SourceDirectory -ErrorAction Stop).Path
$sourceExe = Join-Path $source "Projex.Desktop.exe"
if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
    throw "Projex.Desktop.exe was not found in '$source'. Extract the complete PROJEX ZIP and rerun this installer."
}

$null = New-Item -ItemType Directory -Path $InstallDirectory -Force
& robocopy.exe $source $InstallDirectory /E /COPY:DAT /DCOPY:DAT /R:2 /W:2 /NFL /NDL /NP
$copyExitCode = $LASTEXITCODE
if ($copyExitCode -ge 8) { throw "PROJEX client file copy failed (robocopy exit code $copyExitCode)." }

$startMenu = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\PROJEX"
$null = New-Item -ItemType Directory -Path $startMenu -Force
$shortcutPath = Join-Path $startMenu "PROJEX Enterprise.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $InstallDirectory "Projex.Desktop.exe"
$shortcut.WorkingDirectory = $InstallDirectory
$shortcut.Description = "PROJEX Enterprise desktop client"
$shortcut.Save()

if (-not $NoDesktopShortcut) {
    $desktopShortcutPath = Join-Path ([Environment]::GetFolderPath("CommonDesktopDirectory")) "PROJEX Enterprise.lnk"
    $desktopShortcut = $shell.CreateShortcut($desktopShortcutPath)
    $desktopShortcut.TargetPath = Join-Path $InstallDirectory "Projex.Desktop.exe"
    $desktopShortcut.WorkingDirectory = $InstallDirectory
    $desktopShortcut.Description = "PROJEX Enterprise desktop client"
    $desktopShortcut.Save()
}

Write-Host "PROJEX Client installed successfully."
Write-Host "Launch PROJEX Enterprise from the Start menu, then enter the organization's HTTPS server address."
Write-Host "PostgreSQL and the PROJEX API are not installed on this client computer."
