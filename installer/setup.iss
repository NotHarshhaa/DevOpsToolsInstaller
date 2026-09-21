; =====================================================================
; DevOps Tools Installer - Inno Setup 6 Script
; Generates an enterprise-grade Windows Setup Wizard
; =====================================================================

#ifndef AppVersion
  #define AppVersion "2.5.0"
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
UninstallDisplayIcon={app}\DevOpsToolsInstaller.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
ArchitecturesInstallIn64BitMode=x64compatible
DisableWelcomePage=no
DisableDirPage=no
ChangesEnvironment=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "addtopath"; Description: "Add application directory to PATH environment variable"; GroupDescription: "System Integration:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\DevOps Tools Installer"; Filename: "{app}\DevOpsToolsInstaller.exe"
Name: "{group}\Uninstall DevOps Tools Installer"; Filename: "{uninstallexe}"
Name: "{autodesktop}\DevOps Tools Installer"; Filename: "{app}\DevOpsToolsInstaller.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\DevOpsToolsInstaller.exe"; Description: "{cm:LaunchProgram,DevOps Tools Installer}"; Flags: nowait postinstall skipifsilent

[Code]
const
  EnvironmentKeyUser = 'Environment';
  EnvironmentKeySystem = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';

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

  if RegQueryStringValue(RootKey, SubKey, 'Path', Paths) then
  begin
    if Pos(';' + Uppercase(AppDir) + ';', ';' + Uppercase(Paths) + ';') = 0 then
    begin
      if (Length(Paths) > 0) and (Paths[Length(Paths)] <> ';') then
        Paths := Paths + ';';
      Paths := Paths + AppDir;
      RegWriteStringValue(RootKey, SubKey, 'Path', Paths);
    end;
  end;
end;

procedure RemovePathFromEnvironment();
var
  Paths: string;
  AppDir: string;
  P: Integer;
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

  if RegQueryStringValue(RootKey, SubKey, 'Path', Paths) then
  begin
    P := Pos(';' + Uppercase(AppDir) + ';', ';' + Uppercase(Paths) + ';');
    if P > 0 then
    begin
      Paths := ';' + Paths + ';';
      StringChangeEx(Paths, ';' + AppDir + ';', ';', True);
      if (Length(Paths) > 0) and (Paths[1] = ';') then
        Delete(Paths, 1, 1);
      if (Length(Paths) > 0) and (Paths[Length(Paths)] = ';') then
        Delete(Paths, Length(Paths), 1);
      RegWriteStringValue(RootKey, SubKey, 'Path', Paths);
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('addtopath') then
  begin
    AddPathToEnvironment();
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    RemovePathFromEnvironment();
  end;
end;
