; AccentHold installer (Inno Setup 6). Installs to Program Files, registers an uninstaller
; in "Installed apps", and sets up run-at-logon via a scheduled task so it can optionally
; run elevated (needed to send accents to apps that themselves run as administrator).
;
; Build with scripts\build-installer.ps1, which passes AppVersion and PublishDir.

#ifndef AppVersion
  #define AppVersion "1.0.1"
#endif
#ifndef PublishDir
  #define PublishDir "..\out\publish"
#endif

#define AppName "AccentHold"
#define AppPublisher "Adrien Masanet"
#define AppExe "AccentHold.exe"
#define AppUrl "https://github.com/AdrienMasanet/AccentHold"

[Setup]
AppId={{7C80ECFE-C67C-4B2F-AAFF-8FDF7F95AA08}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppSupportURL={#AppUrl}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
UninstallDisplayIcon={app}\{#AppExe}
LicenseFile=..\LICENSE
OutputDir=..\out
OutputBaseFilename=AccentHold-Setup-{#AppVersion}
SetupIconFile=..\src\AccentHold\Assets\icon.ico
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequired=admin

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

[Tasks]
Name: "startup"; Description: "Start {#AppName} automatically when I sign in"
Name: "startup\admin"; Description: "Run with administrator privileges (recommended, so accents also work in apps running as admin)"

[Registry]
; Non-elevated startup goes through the Run key so it appears in Task Manager's Startup apps;
; the elevated variant needs a scheduled task instead (created in code below).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; \
    ValueData: """{app}\{#AppExe}"""; Tasks: startup and not startup\admin; Flags: uninsdeletevalue

[Code]
const
  TaskName = 'AccentHold';

var
  ErrorCode: Integer;

// Runs a hidden command and waits for it; returns the process exit code (or -1).
function Run(const FileName, Params: string): Integer;
var
  Code: Integer;
begin
  if Exec(FileName, Params, '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Result := Code
  else
    Result := -1;
end;

procedure KillRunning;
begin
  Run(ExpandConstant('{sys}\taskkill.exe'), '/IM {#AppExe} /F');
end;

procedure RemoveAutostart;
begin
  Run(ExpandConstant('{sys}\schtasks.exe'), '/Delete /TN "' + TaskName + '" /F');
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#AppName}');
end;

// Registers an elevated logon task (interactive token, no stored password) that starts
// the app at sign-in; used only for the admin option, then runs it for this session.
procedure CreateElevatedLogonTask;
var
  User, RunLevel, Xml, Path: string;
begin
  User := GetEnv('USERDOMAIN') + '\' + GetEnv('USERNAME');
  RunLevel := 'HighestAvailable';

  Xml :=
    '<?xml version="1.0" encoding="UTF-16"?>' + #13#10 +
    '<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">' + #13#10 +
    '  <RegistrationInfo><Description>Starts AccentHold at logon.</Description></RegistrationInfo>' + #13#10 +
    '  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>' + User + '</UserId></LogonTrigger></Triggers>' + #13#10 +
    '  <Principals><Principal id="Author"><UserId>' + User + '</UserId>' +
    '<LogonType>InteractiveToken</LogonType><RunLevel>' + RunLevel + '</RunLevel></Principal></Principals>' + #13#10 +
    '  <Settings>' +
    '<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>' +
    '<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>' +
    '<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>' +
    '<AllowHardTerminate>true</AllowHardTerminate>' +
    '<StartWhenAvailable>true</StartWhenAvailable>' +
    '<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>' +
    '<Priority>7</Priority></Settings>' + #13#10 +
    '  <Actions Context="Author"><Exec><Command>' + ExpandConstant('{app}\{#AppExe}') + '</Command></Exec></Actions>' + #13#10 +
    '</Task>';

  Path := ExpandConstant('{tmp}\AccentHold-task.xml');
  SaveStringToFile(Path, Xml, False);
  Run(ExpandConstant('{sys}\schtasks.exe'), '/Create /TN "' + TaskName + '" /XML "' + Path + '" /F');
  DeleteFile(Path);
  Run(ExpandConstant('{sys}\schtasks.exe'), '/Run /TN "' + TaskName + '"');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    KillRunning;
    // Clear autostart from previous installs; the [Registry] entry re-creates the
    // Run key afterwards when the non-elevated startup option is selected.
    RemoveAutostart;
  end
  else if CurStep = ssPostInstall then
  begin
    if WizardIsTaskSelected('startup\admin') then
      CreateElevatedLogonTask
    else
      // Launch non-elevated for this session (the installer itself runs elevated).
      ExecAsOriginalUser(ExpandConstant('{app}\{#AppExe}'), '', '', SW_SHOW, ewNoWait, ErrorCode);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    KillRunning;
    RemoveAutostart;
  end;
end;
