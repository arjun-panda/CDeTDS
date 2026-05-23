; TDS Pro — Inno Setup 6 Installer Script
; Build: Run ISCC.exe TDSPro_Setup.iss
; Output: installer\Output\TDSPro_Setup_v3.2.0.exe

#define AppName        "TDS Pro"
#define AppVersion     "3.2.0"
#define AppPublisher   "Arjun Panda"
#define AppURL         "https://tdspro.in"
#define AppExeName     "TDSPro.exe"
#define AppId          "{{A3F7B2E1-4C8D-4F5A-9B6E-1D2C3E4F5A6B}"
#define PublishDir     "AppFiles"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} v{#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL=mailto:admin@capitaldesk.co.in
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
ChangesAssociations=no
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE.txt

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
; Main application — all files from publish folder
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; Launch app after install (optional, user can uncheck)
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Remove app data only if user confirms — we DON'T auto-delete %AppData%\TDSPro (user data!)
; Just remove leftover empty dirs from the install folder
Type: dirifempty; Name: "{app}"

[Code]
// ── WebView2 Runtime check ────────────────────────────────────────────────────
// TDS Pro requires Microsoft Edge WebView2. Check registry before completing setup.

function IsWebView2Installed(): Boolean;
var
  Version: String;
begin
  Result := False;
  // Machine-wide (most installs)
  if RegQueryStringValue(HKLM,
      'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
      'pv', Version) then
  begin
    if (Version <> '') and (Version <> '0.0.0.0') then
    begin
      Result := True;
      Exit;
    end;
  end;
  if RegQueryStringValue(HKLM,
      'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
      'pv', Version) then
  begin
    if (Version <> '') and (Version <> '0.0.0.0') then
    begin
      Result := True;
      Exit;
    end;
  end;
  // Per-user install
  if RegQueryStringValue(HKCU,
      'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
      'pv', Version) then
  begin
    if (Version <> '') and (Version <> '0.0.0.0') then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

procedure InitializeWizard();
begin
  // Nothing extra needed at wizard init
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  // On the Ready page, warn if WebView2 is missing
  if CurPageID = wpReady then
  begin
    if not IsWebView2Installed() then
    begin
      if MsgBox(
        'Microsoft Edge WebView2 Runtime is required by TDS Pro but does not appear to be installed.' + #13#10 + #13#10 +
        'You can install it for free from Microsoft after this setup completes.' + #13#10 +
        'TDS Pro will remind you on first launch if WebView2 is still missing.' + #13#10 + #13#10 +
        'Continue installing TDS Pro anyway?',
        mbConfirmation, MB_YESNO) = IDNO then
      begin
        Result := False;
      end;
    end;
  end;
end;
