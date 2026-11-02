#ifndef MyAppVersion
  #define MyAppVersion "0.9.2-preview.1"
#endif

[Setup]
AppId={{B42B5D2A-2C9E-4D8A-9A62-8C1E4E7A0F31}
AppName=PROJEX Enterprise
AppVersion={#MyAppVersion}
AppPublisher=PROJEX
DefaultDirName={autopf}\PROJEX
DefaultGroupName=PROJEX Enterprise
UninstallDisplayIcon={app}\Desktop\Projex.Desktop.exe
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
OutputDir=..\artifacts
OutputBaseFilename=PROJEX-Setup-win-x64
SetupLogging=yes
CloseApplications=yes
RestartApplications=no
DisableProgramGroupPage=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "server"; Description: "Install the API server payload and Windows service administration scripts"; GroupDescription: "Optional components:"; Flags: unchecked

[Files]
Source: "..\artifacts\package\Desktop\*"; DestDir: "{app}\Desktop"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\package\README-RUN.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\package\Server\API\*"; DestDir: "{app}\Server\API"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: server
Source: "..\artifacts\package\Server\Scripts\*"; DestDir: "{app}\Server\Scripts"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: server
Source: "..\artifacts\package\Server\Database\Migrations\*"; DestDir: "{app}\Server\Database\Migrations"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: server
Source: "..\artifacts\package\Server\Docs\*"; DestDir: "{app}\Server\Docs"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: server

[Icons]
Name: "{group}\PROJEX Enterprise"; Filename: "{app}\Desktop\Projex.Desktop.exe"; WorkingDir: "{app}\Desktop"
Name: "{autodesktop}\PROJEX Enterprise"; Filename: "{app}\Desktop\Projex.Desktop.exe"; Tasks: desktopicon; WorkingDir: "{app}\Desktop"
Name: "{group}\Server deployment guide"; Filename: "{app}\Server\Docs\WINDOWS_HOME_SERVER.md"; Tasks: server

[Run]
Filename: "{app}\Desktop\Projex.Desktop.exe"; Description: "Launch PROJEX Enterprise"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Server\Scripts\Uninstall-ProjexApiService.ps1"" -Confirm:$false"; Flags: runhidden; RunOnceId: "RemoveProjexApiService"; Check: FileExists(ExpandConstant('{app}\Server\Scripts\Uninstall-ProjexApiService.ps1'))
