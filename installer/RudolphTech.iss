; Installer for Rudolph Tech (Inno Setup 6).
;
; Built by .github/workflows/build.yml, never by hand on the office PC. It packs two things: the
; published self contained application (dotnet publish -c Release -r win-x64 --self-contained) and
; the portable Node runtime that tools/get-node.ps1 leaves in build\node.
;
; The password is passed in from the workflow as a define, sourced from the INSTALLER_PASSWORD
; repository secret:
;   iscc /DInstallerPassword=... installer\RudolphTech.iss
; Compiling without that define produces an installer with no password, which is what a local test
; build gets. There is no secret of any kind inside this file or inside the installer: the app url
; and the ingest token are asked for and downloaded on first run.

#define AppName "Rudolph Tech"
; The version comes from the git tag through the CI (/DAppVersion=x.y.z). Building locally without
; that define falls back to 0.0.0-dev, which makes an untagged local build obvious in its file name.
#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#define AppPublisher "Rudolph Electronics"
#define AppExeName "RudolphTech.exe"
#define PublishDir "..\src\RudolphTech\bin\Release\net10.0-windows\win-x64\publish"
#define NodeDir "..\build\node"

#ifndef InstallerPassword
  #define InstallerPassword ""
#endif

[Setup]
AppId={{7B1F5E42-3C6D-4E0B-9E52-0B7A2C9D4F18}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\build\installer
OutputBaseFilename=RudolphTech-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
MinVersion=10.0
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
SetupIconFile=..\src\RudolphTech\Assets\rudolph.ico
#if InstallerPassword != ""
Password={#InstallerPassword}
Encryption=yes
#endif

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "startupicon"; Description: "Iniciar Rudolph Tech al encender la PC"; GroupDescription: "Al iniciar Windows:"

[Files]
; The published application, self contained: no .NET runtime needed on the office PC.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; The portable Node runtime the survey scripts run on (tools\get-node.ps1 put it in build\node).
Source: "{#NodeDir}\*"; DestDir: "{app}\node"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{userstartup}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Parameters: "--startup"; Tasks: startupicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Abrir Rudolph Tech ahora"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Settings, the downloaded agent and the logs all live per user, outside Program Files. The agent
; folder is removed because it is only a copy of what the web app hands out; the logs are kept on
; purpose, in case somebody is uninstalling to fix a problem and still needs to read them.
Type: filesandordirs; Name: "{localappdata}\RudolphTech\agent"
Type: files; Name: "{localappdata}\RudolphTech\settings.json"

[UninstallRun]
; The tray app runs in the user's session; closing it first frees the files being removed. It is
; asked politely first (--salir closes it the same way the Salir menu item does) and only then
; forced, in case it was already gone or unresponsive.
Filename: "{app}\{#AppExeName}"; Parameters: "--salir"; Flags: runhidden skipifdoesntexist; RunOnceId: "pedircierre"
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im {#AppExeName}"; Flags: runhidden; RunOnceId: "cerrarbandeja"
