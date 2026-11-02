@echo off
setlocal
title PROJEX - Client Setup

net session >nul 2>&1
if errorlevel 1 (
  echo.
  echo   Please close this window, then right-click Setup-Client.bat
  echo   and choose "Run as administrator".
  echo.
  pause
  exit /b 1
)

set "SRC=%~dp0Desktop"
set "DEST=%ProgramFiles%\PROJEX\Client"

if not exist "%SRC%\Projex.Desktop.exe" (
  echo.
  echo   The Desktop folder was not found next to this file.
  echo   Copy the complete Clients folder to this computer and try again.
  echo.
  pause
  exit /b 1
)

echo.
echo   Installing PROJEX client...
robocopy "%SRC%" "%DEST%" /E /R:2 /W:2 /NFL /NDL /NP >nul
if errorlevel 8 (
  echo   Copy failed. Close PROJEX if it is open, then try again.
  pause
  exit /b 1
)

rem --- shortcuts (Desktop + Start menu) ---
powershell -NoProfile -ExecutionPolicy Bypass -Command "$w=New-Object -ComObject WScript.Shell; foreach($d in @([Environment]::GetFolderPath('CommonDesktopDirectory'), (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs'))){ $s=$w.CreateShortcut((Join-Path $d 'PROJEX Enterprise.lnk')); $s.TargetPath='%DEST%\Projex.Desktop.exe'; $s.WorkingDirectory='%DEST%'; $s.Save() }"

rem --- server address (written by the Server Control Center into connection.json) ---
set "SERVER_URL="
if exist "%~dp0connection.json" (
  for /f "usebackq delims=" %%A in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "try { (Get-Content -Raw -LiteralPath '%~dp0connection.json' | ConvertFrom-Json).ServerUrl } catch { '' }"`) do set "SERVER_URL=%%A"
)
if defined SERVER_URL (
  if not exist "%LOCALAPPDATA%\PROJEX" mkdir "%LOCALAPPDATA%\PROJEX"
  > "%LOCALAPPDATA%\PROJEX\desktop-settings.json" echo {"ServerUrl":"%SERVER_URL%"}
  echo   Server address set to: %SERVER_URL%
) else (
  echo   No server address found. Enter it inside PROJEX on first start.
)

echo.
echo   Done. Open "PROJEX Enterprise" from the Desktop or Start menu.
echo.
pause
