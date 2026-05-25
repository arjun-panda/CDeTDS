; TDS Pro — Inno Setup 6 Installer Script
; Build: .\installer\build_installer.ps1
; Output: installer\Output\TDSPro_Setup_v1.0.0.exe

#define AppName        "TDS Pro"
#define AppVersion     "1.0.0"
#define AppPublisher   "CapitalDesk"
#define AppURL         "https://capitaldesk.co.in"
#define AppExeName     "TDSPro.exe"
#define AppId          "{{A3F7B2E1-4C8D-4F5A-9B6E-1D2C3E4F5A6B}"
#define AppFiles       "AppFiles"
#define FvuDir         "..\TDS_STANDALONE_FVU_9.4"
#define JreDir         "bundled_jre"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} v{#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL=mailto:support@capitaldesk.co.in
AppUpdatesURL={#AppURL}
DefaultDirName={autopf}\TDSPro
DefaultGroupName={#AppName}
AllowNoIcons=no
OutputDir=Output
OutputBaseFilename=TDSPro_Setup_v{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
MinVersion=10.0.17763
UninstallDisplayName={#AppName} v{#AppVersion}
UninstallDisplayIcon={app}\{#AppExeName}
CloseApplications=yes
RestartApplications=no
LicenseFile=..\LICENSE.txt
ShowLanguageDialog=no
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName={#AppName}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=TDS Compliance Software

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
; Main application
Source: "{#AppFiles}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

; Bundled JRE (Java 8 x64 — required to run FVU)
Source: "{#JreDir}\*"; DestDir: "{app}\jre"; Flags: ignoreversion recursesubdirs createallsubdirs

; FVU validation engine (NSDL JARs)
Source: "{#FvuDir}\*"; DestDir: "{app}\FVU"; Flags: ignoreversion recursesubdirs createallsubdirs

; WebView2 bootstrapper (optional — install silently if missing)
Source: "..\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall skipifsourcedoesntexist

; Docs
Source: "..\README.txt";    DestDir: "{app}"; Flags: ignoreversion isreadme skipifsourcedoesntexist
Source: "..\CHANGELOG.txt"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\LICENSE.txt";   DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Dirs]
Name: "{userappdata}\TDSPro";        Permissions: users-full
Name: "{userappdata}\TDSPro\Backup"; Permissions: users-full
Name: "{userappdata}\TDSPro\Logs";   Permissions: users-full

[Registry]
Root: HKCU; Subkey: "Software\TDSPro"; ValueType: string; ValueName: "InstallPath";    ValueData: "{app}";                Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\TDSPro"; ValueType: string; ValueName: "Version";        ValueData: "{#AppVersion}"
Root: HKCU; Subkey: "Software\TDSPro"; ValueType: string; ValueName: "FvuPath";        ValueData: "{app}\FVU\TDS_STANDALONE_FVU_9.4.jar"
Root: HKCU; Subkey: "Software\TDSPro"; ValueType: string; ValueName: "JavaPath";       ValueData: "{app}\jre\bin\java.exe"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Comment: "TDS Compliance Software"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; Install WebView2 silently if not already present
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; \
  StatusMsg: "Installing Microsoft Edge WebView2 (required)..."; \
  Flags: waituntilterminated skipifdoesntexist; Check: WebView2NotInstalled

; Launch app after install
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName} now"; \
  Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
Type: dirifempty; Name: "{app}"

[Code]
function WebView2NotInstalled(): Boolean;
var
  Version: String;
begin
  Result := True;
  if RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) then
    if (Version <> '') and (Version <> '0.0.0.0') then begin Result := False; Exit; end;
  if RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) then
    if (Version <> '') and (Version <> '0.0.0.0') then begin Result := False; Exit; end;
  if RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) then
    if (Version <> '') and (Version <> '0.0.0.0') then begin Result := False; Exit; end;
end;

// Offer to delete user data on uninstall
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataPath: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataPath := ExpandConstant('{userappdata}\TDSPro');
    if DirExists(DataPath) then
    begin
      if MsgBox(
        'Would you like to delete your TDS Pro data (database, backups, logs)?' + #13#10 + #13#10 +
        'Location: ' + DataPath + #13#10 + #13#10 +
        'Click Yes to delete all data, or No to keep it.',
        mbConfirmation, MB_YESNO) = IDYES then
      begin
        DelTree(DataPath, True, True, True);
      end;
    end;
  end;
end;
