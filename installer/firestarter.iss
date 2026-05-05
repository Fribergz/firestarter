; Inno Setup script for Firestarter.
;
; Compile with:
;   iscc /DMyAppVersion=1.0.0 installer\firestarter.iss
;
; Or, end-to-end (publish + compile + drop into dist\):
;   pwsh installer\build-installer.ps1
;
; The script assumes you've already produced a self-contained publish output via:
;   dotnet publish src\Firestarter.App -c Release -r win-x64 --self-contained true

#define MyAppName        "Firestarter"
#ifndef MyAppVersion
  #define MyAppVersion   "0.0.0"
#endif
#define MyAppPublisher   "Firestarter"
#define MyAppExeName     "Firestarter.exe"
#define MyPublishDir     "..\src\Firestarter.App\bin\Release\net10.0-windows\win-x64\publish"
#define MyIconFile       "..\src\Firestarter.App\Assets\Firestarter.ico"

[Setup]
; Stable AppId — never change this once the installer ships, otherwise upgrades become reinstalls.
AppId={{F1A35B9E-9C2A-4F8D-9A6E-2D7B4A9C0E11}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={code:DefaultInstallDir}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Per-user install by default (no UAC). User can elevate from the dialog if they want a system-wide install.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile={#MyIconFile}
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
OutputDir=..\dist
OutputBaseFilename=Firestarter-setup
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
; Pull the entire self-contained publish output verbatim.
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\Firestarter.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\Assets\Firestarter.ico"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
// Pin per-user installs to %LOCALAPPDATA%\Programs\Firestarter regardless of how `{autopf}`
// resolves on different Windows versions. System-wide installs (after the user accepts the
// elevation dialog) land in Program Files\Firestarter.
function DefaultInstallDir(Param: string): string;
begin
  if IsAdminInstallMode then
    Result := ExpandConstant('{commonpf}\{#MyAppName}')
  else
    Result := ExpandConstant('{localappdata}\Programs\{#MyAppName}');
end;
