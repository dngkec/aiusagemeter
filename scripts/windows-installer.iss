; Installer for the Windows build. Driven by scripts/package-windows.ps1, which passes the
; version, the published single-file executable, and the architecture it was published for.
;
; Per-user by design. The app writes launch-at-login under HKCU\...\Run, so a machine-wide
; install would put the executable somewhere one user could install and another could not
; start. PrivilegesRequired=lowest also means no UAC prompt on an unsigned build, which is
; one fewer warning to talk a user through.

#ifndef AppVersion
  #define AppVersion "1.2.2"
#endif
#ifndef SourceExe
  #error SourceExe must be passed with /D
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif
#ifndef OutputBase
  #define OutputBase "AIUsageMeter-setup"
#endif

#define AppName "AIUsageMeter"
#define AppPublisher "Dinesh Goswami"
#define AppUrl "https://github.com/dngkec/aiusagemeter"
#define ExeName "AIUsageMeter.exe"
#define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"

[Setup]
; Fixed for the life of the app: this is what lets one release upgrade the next in place rather than
; installing beside it. Never regenerate it.
AppId={{B7B4F0C2-3E5A-4D8E-9C1F-2A6D5E8B4C13}
AppName={#AppName}
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBase}
SetupIconFile=..\Resources\icons\aiusagemeter.ico
UninstallDisplayIcon={app}\{#ExeName}
UninstallDisplayName={#AppName} {#AppVersion}
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
; The payload is one self-contained executable that is already largely incompressible, so
; there is nothing to gain from a second solid pass over it.
LZMANumBlockThreads=2
; Restart Manager asks the running tray app to close before the executable is replaced.
CloseApplications=yes
RestartApplications=no
ArchitecturesInstallIn64BitMode={#Arch == "arm64" ? "arm64" : "x64compatible"}
ArchitecturesAllowed={#Arch == "arm64" ? "arm64" : "x64compatible"}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "{#ExeName}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#ExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#ExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#ExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; The in-app updater's own relaunch. A silent install must not start the app on its own -- that is
; what `skipifsilent` above is for -- but an update the user asked for has to put back the tray icon
; it just closed, so the updater passes /UPDATE and this entry answers only to that.
Filename: "{app}\{#ExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall; Check: RelaunchRequested

[Code]
// True when the command line carries /UPDATE. Inno has no built-in test for a custom switch.
function RelaunchRequested: Boolean;
var
  Index: Integer;
begin
  Result := False;
  for Index := 1 to ParamCount do
    if CompareText(ParamStr(Index), '/UPDATE') = 0 then
    begin
      Result := True;
      Exit;
    end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // Uninstalling leaves preferences and Credential Manager entries alone — a reinstall should
  // find the setup it had — but the Run value points at an executable that is about to go, so
  // it has to be removed or Windows fails a login task on every boot.
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, '{#RunKey}', '{#AppName}');
end;
