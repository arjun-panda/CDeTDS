#define AppName      "CDeTDS"
#ifndef AppVersion
#define AppVersion   "1.0.2"
#endif
#define AppPublisher "CDeTDS Software"
#define AppURL       "https://capitaldesk.co.in"
#define AppExeName   "CDeTDS.exe"
#define AppDesc      "TDS Compliance Software - Income-tax Act 2025"
#define SourceDir    "publish_out"
#define FvuDir       "TDS_STANDALONE_FVU_9.4"
#define JreDir       "installer\bundled_jre"
#define BuildStamp   GetDateTimeString('yyyymmdd_hhnnss', '', '')

[Setup]
AppId={{A3F2B1C4-9D8E-4F7A-B2C3-D1E5F6A7B8C9}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} v{#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/support
AppUpdatesURL={#AppURL}/updates
AppCopyright=Copyright 2026 CDeTDS Software
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=no
DisableProgramGroupPage=yes
LicenseFile=LICENSE.txt
OutputDir=installer_output
OutputBaseFilename=CDeTDS_Setup_v{#AppVersion}_{#BuildStamp}
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=120
PrivilegesRequired=admin
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppDesc}
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
ShowLanguageDialog=no
CloseApplications=yes
RestartApplications=no
MinVersion=10.0
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#AppName} v{#AppVersion}
CreateUninstallRegKey=yes
AlwaysShowComponentsList=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
; ── Main application (self-contained .NET 8) ─────────────────────────────────
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "publish\*;*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

; ── Bundled JRE (Eclipse Temurin / OpenJDK 8 x64) ───────────────────────────
; Place the extracted JRE folder as "bundled_jre\" next to this script before building.
; Download: https://adoptium.net/temurin/releases/?version=8&os=windows&arch=x64&package_type=jre
Source: "{#JreDir}\*"; DestDir: "{app}\jre"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

; ── FVU validation engine (NSDL JARs) ────────────────────────────────────────
Source: "{#FvuDir}\*"; DestDir: "{app}\FVU"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

; ── WebView2 bootstrapper ─────────────────────────────────────────────────────
Source: "MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall skipifsourcedoesntexist

; ── Docs ──────────────────────────────────────────────────────────────────────
Source: "README.txt";    DestDir: "{app}"; Flags: ignoreversion isreadme skipifsourcedoesntexist
Source: "CHANGELOG.txt"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "LICENSE.txt";   DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Dirs]
Name: "{userappdata}\CDeTDS";        Permissions: users-full
Name: "{userappdata}\CDeTDS\Backup"; Permissions: users-full
Name: "{userappdata}\CDeTDS\Logs";   Permissions: users-full
Name: "{userdocs}\CDeTDS";           Permissions: users-full
Name: "{userdocs}\CDeTDS\Companies"; Permissions: users-full
Name: "{userdocs}\CDeTDS\Reports";   Permissions: users-full
Name: "{userdocs}\CDeTDS\Form16";    Permissions: users-full

[Icons]
Name: "{group}\{#AppName}";            Filename: "{app}\{#AppExeName}"; Comment: "{#AppDesc}"
Name: "{group}\Uninstall {#AppName}";  Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";      Filename: "{app}\{#AppExeName}"; Tasks: desktopicon; Comment: "{#AppDesc}"

[Registry]
Root: HKCU; Subkey: "Software\CDeTDS"; ValueType: string; ValueName: "InstallPath";    ValueData: "{app}";                    Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\CDeTDS"; ValueType: string; ValueName: "Version";        ValueData: "{#AppVersion}"
Root: HKCU; Subkey: "Software\CDeTDS"; ValueType: string; ValueName: "DataPath";       ValueData: "{userappdata}\CDeTDS"
Root: HKCU; Subkey: "Software\CDeTDS"; ValueType: string; ValueName: "FvuPath";        ValueData: "{app}\FVU\TDS_STANDALONE_FVU_9.4.jar"
Root: HKCU; Subkey: "Software\CDeTDS"; ValueType: string; ValueName: "JavaPath";       ValueData: "{app}\jre\bin\java.exe"
; Bundled JRE path — app uses this to auto-detect java.exe without user configuration
Root: HKCU; Subkey: "Software\CDeTDS"; ValueType: string; ValueName: "BundledJrePath"; ValueData: "{app}\jre"

[Run]
; Install WebView2 silently if not already present
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; \
  StatusMsg: "Installing Microsoft Edge WebView2 (required for UI)..."; \
  Flags: waituntilterminated skipifdoesntexist; \
  Check: WebView2NotInstalled

; Launch app after install
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName} now"; Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\CDeTDS\Logs"

[Code]

// ── WebView2 detection ────────────────────────────────────────────────────────
function WebView2NotInstalled(): Boolean;
var
  Version: String;
begin
  Result := True;
  if RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) then
    if (Version <> '') and (Version <> '0.0.0.0') then begin Result := False; Exit; end;
  if RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) then
    if (Version <> '') and (Version <> '0.0.0.0') then begin Result := False; Exit; end;
  if RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) then
    if (Version <> '') and (Version <> '0.0.0.0') then begin Result := False; Exit; end;
end;

// ── Bundled JRE check ─────────────────────────────────────────────────────────
function BundledJrePresent(): Boolean;
begin
  // Returns true if bundled_jre\bin\java.exe exists next to the installer script
  Result := FileExists(ExpandConstant('{src}\{#JreDir}\bin\java.exe'));
end;

// ── Uninstall: offer to delete user data ─────────────────────────────────────
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataPath: String;
  DeleteData: Boolean;
begin
  if CurUninstallStep = usUninstall then
  begin
    DataPath := ExpandConstant('{userappdata}\CDeTDS');
    if DirExists(DataPath) then
    begin
      DeleteData := MsgBox(
        'Do you want to DELETE your CDeTDS data?' + #13#10#13#10 +
        'Location: ' + DataPath + #13#10#13#10 +
        'This includes your database, all TDS entries, challans and backups.' + #13#10 +
        'THIS CANNOT BE UNDONE.' + #13#10#13#10 +
        'Click YES to delete all data.' + #13#10 +
        'Click NO to keep your data (recommended).',
        mbConfirmation, MB_YESNO) = IDYES;
      if DeleteData then
        DelTree(DataPath, True, True, True);
    end;
  end;
end;

// ── Post-install: write FVU config to app settings ──────────────────────────
procedure CurStepChanged(CurStep: TSetupStep);
var
  JrePath: String;
  FvuPath: String;
  HasJre:  Boolean;
begin
  if CurStep = ssPostInstall then
  begin
    JrePath := ExpandConstant('{app}\jre');
    FvuPath := ExpandConstant('{app}\FVU\TDS_STANDALONE_FVU_9.4.jar');
    HasJre  := FileExists(JrePath + '\bin\java.exe');

    if not HasJre then
    begin
      MsgBox(
        'CDeTDS installed successfully.' + #13#10#13#10 +
        'NOTE: Bundled Java Runtime was not found in the installer.' + #13#10 +
        'FVU return generation requires Java.' + #13#10#13#10 +
        'You can configure Java later in CDeTDS → Settings → FVU & Java.' + #13#10 +
        'Or download Java from: https://www.java.com/download/',
        mbInformation, MB_OK);
    end;
  end;
end;
