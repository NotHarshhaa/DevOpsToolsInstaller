; =====================================================================
; DevOps Tools Installer - Inno Setup 6 Script
; Generates a native-feeling Windows 11 setup wizard
; =====================================================================

#ifndef AppVersion
  #define AppVersion "2.8.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\src\DevOpsToolsInstaller\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish"
#endif

#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#ifndef OutputBaseFilename
  #define OutputBaseFilename "DevOpsToolsInstaller_v" + AppVersion + "_x64_Setup"
#endif

[Setup]
AppId={{D3V0P5-T00L5-1N5T4LL3R-2026-V2}}
AppName=DevOps Tools Installer
AppVersion={#AppVersion}
AppVerName=DevOps Tools Installer {#AppVersion}
AppPublisher=NotHarshhaa
AppPublisherURL=https://github.com/NotHarshhaa/DevOpsToolsInstaller
AppSupportURL=https://github.com/NotHarshhaa/DevOpsToolsInstaller/issues
AppUpdatesURL=https://github.com/NotHarshhaa/DevOpsToolsInstaller/releases
DefaultDirName={autopf}\DevOpsToolsInstaller
DefaultGroupName=DevOps Tools Installer
AllowNoIcons=yes
LicenseFile=..\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
SetupIconFile=..\src\DevOpsToolsInstaller\Assets\app.ico
WizardImageFile=assets\wizardlarge.bmp
WizardSmallImageFile=assets\wizardsmall.bmp
UninstallDisplayIcon={app}\DevOpsToolsInstaller.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
ArchitecturesInstallIn64BitMode=x64compatible
DisableWelcomePage=no
DisableDirPage=no
DisableProgramGroupPage=yes
ChangesEnvironment=yes
CloseApplications=yes
RestartApplications=no

; Setup binary properties (shown in Explorer > Properties and by AV/tooling)
VersionInfoVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}
VersionInfoCompany=NotHarshhaa
VersionInfoDescription=Installs DevOps Tools Installer v{#AppVersion}
VersionInfoProductName=DevOps Tools Installer
VersionInfoProductTextVersion={#AppVersion}
VersionInfoCopyright=Apache-2.0 License - https://github.com/NotHarshhaa/DevOpsToolsInstaller

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel1=Welcome to the [name] Setup Wizard
WelcomeLabel2=This will install [name/ver] on your computer.%n%nA native catalog installer for DevOps and cloud tools: official vendor binaries, SHA-256 verification, curated stacks, and automatic PATH integration.%n%nIt is recommended that you close the DevOps Tools Installer before continuing.
FinishedLabelNoIcons=[name] v[ver] was installed successfully.%n%nTips:%n  - Closing the window keeps the app in the system tray.%n  - Install tools from a script: DevOpsToolsInstaller.exe --help
FinishedLabel=[name] v[ver] was installed successfully.%n%nTips:%n  - Closing the window keeps the app in the system tray.%n  - Install tools from a script: DevOpsToolsInstaller.exe --help
UninstallAppFullTitle=[name] - Uninstall
ConfirmUninstall=Are you sure you want to completely remove %1 and all of its components?

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "addtopath"; Description: "Add to PATH (run DevOpsToolsInstaller from any terminal)"; \
    GroupDescription: "System Integration:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*_x64.exe,*_arm64.exe,*_Setup.exe,*.msi"

[Icons]
Name: "{group}\DevOps Tools Installer"; Filename: "{app}\DevOpsToolsInstaller.exe"
Name: "{group}\Uninstall DevOps Tools Installer"; Filename: "{uninstallexe}"
Name: "{autodesktop}\DevOps Tools Installer"; Filename: "{app}\DevOpsToolsInstaller.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\DevOpsToolsInstaller.exe"; Description: "{cm:LaunchProgram,DevOps Tools Installer}"; Flags: nowait postinstall skipifsilent

; Leftover self-update downloads are safe to remove on uninstall.
[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\DevOpsToolsInstaller\Updates"

[Code]
const
  EnvironmentKeyUser = 'Environment';
  EnvironmentKeySystem = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';
  AppExeName = 'DevOpsToolsInstaller.exe';
  ToolsBinDir = 'DevOpsToolsInstaller\Tools\bin';

// ── Running-instance handling ────────────────────────────────────────
// The app minimizes to the tray when closed, so a plain WM_CLOSE is not
// enough during an upgrade — offer to close (and terminate) it explicitly.

function IsAppRunning(): Boolean;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{cmd}'),
    ExpandConstant('/C tasklist /FI "IMAGENAME eq ' + AppExeName + '" /NH | find /I "' + AppExeName + '" > nul'),
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := (ResultCode = 0);
end;

function CloseRunningApp(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\taskkill.exe'),
    ExpandConstant('/F /T /IM ' + AppExeName),
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Attempt: Integer;
begin
  Result := '';

  for Attempt := 1 to 3 do
  begin
    if not IsAppRunning() then
      Exit;

    if WizardSilent() then
    begin
      // Silent / scripted upgrade: terminate a running instance without prompting.
      CloseRunningApp();
      Sleep(1000);
      if not IsAppRunning() then
        Exit;
    end
    else
    begin
      if MsgBox(
          'DevOps Tools Installer is currently running.' + #13#10#13#10 +
          'It must be closed before it can be updated. Close it now?',
          mbConfirmation, MB_YESNO) = IDYES then
      begin
        CloseRunningApp();
        Sleep(1000);
        if not IsAppRunning() then
          Exit;
      end
      else
      begin
        Result := 'Setup was cancelled because DevOps Tools Installer is still running.';
        Exit;
      end;
    end;
  end;

  Result := 'DevOps Tools Installer could not be closed. Please close it manually and run Setup again.';
end;

function InitializeUninstall(): Boolean;
var
  Attempt: Integer;
begin
  Result := True;

  for Attempt := 1 to 3 do
  begin
    if not IsAppRunning() then
      Exit;

    if UninstallSilent() or
       (MsgBox('DevOps Tools Installer is currently running. Close it now?',
               mbConfirmation, MB_YESNO) = IDYES) then
    begin
      CloseRunningApp();
      Sleep(1000);
    end
    else
    begin
      MsgBox('Uninstall cannot continue while the app is running.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  MsgBox('DevOps Tools Installer could not be closed. Please close it manually and try again.',
         mbError, MB_OK);
  Result := False;
end;

// ── PATH environment handling ────────────────────────────────────────

procedure AddPathToEnvironment();
var
  Paths: string;
  AppDir: string;
  RootKey: Integer;
  SubKey: string;
begin
  AppDir := ExpandConstant('{app}');

  if IsAdminInstallMode() then
  begin
    RootKey := HKEY_LOCAL_MACHINE;
    SubKey := EnvironmentKeySystem;
  end
  else
  begin
    RootKey := HKEY_CURRENT_USER;
    SubKey := EnvironmentKeyUser;
  end;

  // Bug fix: previously a missing PATH value silently skipped the task.
  if RegQueryStringValue(RootKey, SubKey, 'Path', Paths) then
  begin
    if Pos(';' + Uppercase(AppDir) + ';', ';' + Uppercase(Paths) + ';') = 0 then
    begin
      if (Length(Paths) > 0) and (Paths[Length(Paths)] <> ';') then
        Paths := Paths + ';';
      Paths := Paths + AppDir;
      RegWriteStringValue(RootKey, SubKey, 'Path', Paths);
    end;
  end
  else
  begin
    RegWriteStringValue(RootKey, SubKey, 'Path', AppDir);
  end;
end;

procedure RemovePathEntryFromEnvironment(const Entry: string);
var
  Paths: string;
  RootKey: Integer;
  SubKey: string;
begin
  if IsAdminInstallMode() then
  begin
    RootKey := HKEY_LOCAL_MACHINE;
    SubKey := EnvironmentKeySystem;
  end
  else
  begin
    RootKey := HKEY_CURRENT_USER;
    SubKey := EnvironmentKeyUser;
  end;

  if RegQueryStringValue(RootKey, SubKey, 'Path', Paths) then
  begin
    if Pos(';' + Uppercase(Entry) + ';', ';' + Uppercase(Paths) + ';') > 0 then
    begin
      Paths := ';' + Paths + ';';
      StringChangeEx(Paths, ';' + Entry + ';', ';', True);
      if (Length(Paths) > 0) and (Paths[1] = ';') then
        Delete(Paths, 1, 1);
      if (Length(Paths) > 0) and (Paths[Length(Paths)] = ';') then
        Delete(Paths, Length(Paths), 1);
      RegWriteStringValue(RootKey, SubKey, 'Path', Paths);
    end;
  end;
end;

procedure RemovePathFromEnvironment();
begin
  // Remove the install directory added by the "Add to PATH" task...
  RemovePathEntryFromEnvironment(ExpandConstant('{app}'));
  // ...and the Tools\bin folder the app itself may have added at runtime.
  RemovePathEntryFromEnvironment(ExpandConstant('{localappdata}') + '\' + ToolsBinDir);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('addtopath') then
  begin
    AddPathToEnvironment();
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  UserDataDir: string;
begin
  if CurUninstallStep = usUninstall then
  begin
    RemovePathFromEnvironment();

    // Offer to remove cached downloads, portable tools, settings, and logs.
    UserDataDir := ExpandConstant('{localappdata}') + '\DevOpsToolsInstaller';
    if DirExists(UserDataDir) then
    begin
      if UninstallSilent() or
         (MsgBox(
            'Do you also want to remove your DevOps Tools Installer data?' + #13#10#13#10 +
            'This deletes downloaded installers, extracted portable tools, and settings from:' + #13#10 +
            UserDataDir,
            mbConfirmation, MB_YESNO) = IDYES) then
      begin
        DelTree(UserDataDir, True, True, True);
      end;
    end;
  end;
end;
