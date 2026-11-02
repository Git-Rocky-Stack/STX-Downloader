; The Windows installer. tools\Publish.ps1 builds it, passing the folder to pack and the version.
; Installs for the current user only: the app keeps its data per user and the browser hand-off registers per user,
; so nothing here needs an administrator.

#ifndef Stage
  #error Build this with tools\Publish.ps1, which passes /DStage=<published folder>
#endif
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef OutputFolder
  #define OutputFolder "..\..\dist"
#endif

#define ExtensionIdFile FileOpen(AddBackslash(SourcePath) + "..\..\extension\extension-id.txt")
#define ExtensionId Trim(FileRead(ExtensionIdFile))
#expr FileClose(ExtensionIdFile)

#define HostName "com.stx.downloader"

[Setup]
AppId={{7E2C6A44-3B1D-4F58-9C21-5D0A8E6F1B37}
AppName=STX Downloader
AppVersion={#AppVersion}
AppVerName=STX Downloader {#AppVersion}
AppPublisher=Strategia-X
AppPublisherURL=https://strategia-x.com
AppSupportURL=mailto:support@strategia-x.com
AppContact=support@strategia-x.com
AppCopyright=Copyright (c) 2026 Rocky Elsalaymeh, Strategia-X
VersionInfoCompany=Strategia-X
VersionInfoCopyright=Copyright (c) 2026 Rocky Elsalaymeh, Strategia-X
VersionInfoDescription=STX Downloader Setup
VersionInfoProductName=STX Downloader
VersionInfoVersion={#AppVersion}
LicenseFile=..\..\LICENSE
PrivilegesRequired=lowest
DefaultDirName={autopf}\STX Downloader
DefaultGroupName=STX Downloader
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputFolder}
OutputBaseFilename=STXDownloader-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
SetupIconFile=..\..\src\Stx.App\Assets\stx.ico
UninstallDisplayIcon={app}\STXDownloader.exe
UninstallDisplayName=STX Downloader
CloseApplications=force
RestartApplications=no
; The look of the rack: the dark faceplate color from src\Stx.Look\LookTokens.cs, with no stock artwork or lines.
WizardStyle=modern dark hidebevels includetitlebar
WizardBackColor=#2A2C2F
WizardImageBackColor=#2A2C2F
WizardSmallImageBackColor=none
WizardSmallImageFile=art\corner-58.png,art\corner-77.png,art\corner-97.png,art\corner-116.png,art\corner-124.png,art\corner-143.png,art\corner-159.png
WizardImageFile=art\panel-202.png,art\panel-430.png
#ifdef Signed
SignTool=stx
SignedUninstaller=yes
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Add a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#Stage}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\STX Downloader"; Filename: "{app}\STXDownloader.exe"
Name: "{group}\Uninstall STX Downloader"; Filename: "{uninstallexe}"
Name: "{autodesktop}\STX Downloader"; Filename: "{app}\STXDownloader.exe"; Tasks: desktopicon

[Registry]
; Lets the extension, and only it, start the native host. The file these point at is written below.
Root: HKCU; Subkey: "Software\Google\Chrome\NativeMessagingHosts\{#HostName}"; ValueType: string; ValueData: "{app}\{#HostName}.json"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Edge\NativeMessagingHosts\{#HostName}"; ValueType: string; ValueData: "{app}\{#HostName}.json"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Chromium\NativeMessagingHosts\{#HostName}"; ValueType: string; ValueData: "{app}\{#HostName}.json"; Flags: uninsdeletekey
; The app adds this itself when Start with Windows is on. Uninstalling mustn't leave it pointing at nothing.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "STXDownloader"; ValueType: none; Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\STXDownloader.exe"; Description: "Start STX Downloader"; Flags: nowait postinstall skipifsilent
Filename: "{app}\extension"; Description: "Show the browser extension folder (in Chrome or Edge: Extensions, Developer mode, Load unpacked)"; Flags: shellexec postinstall skipifsilent unchecked

[UninstallDelete]
Type: files; Name: "{app}\{#HostName}.json"
; A job stopped mid-download can leave its cookies here. Only the next launch clears them, and after this there is no next launch.
Type: filesandordirs; Name: "{localappdata}\STXDownloader\run"
Type: dirifempty; Name: "{app}"

[Code]
function JsonText(const Value: String): String;
begin
  Result := Value;
  StringChangeEx(Result, '\', '\\', True);
  StringChangeEx(Result, '"', '\"', True);
end;

// Written here, because the path inside it is wherever this install went.
procedure WriteHostManifest();
var
  Body: String;
begin
  Body :=
    '{' + #13#10 +
    '  "name": "{#HostName}",' + #13#10 +
    '  "description": "STX Downloader hand-off",' + #13#10 +
    '  "path": "' + JsonText(ExpandConstant('{app}\STXNativeHost.exe')) + '",' + #13#10 +
    '  "type": "stdio",' + #13#10 +
    '  "allowed_origins": [ "chrome-extension://{#ExtensionId}/" ]' + #13#10 +
    '}' + #13#10;
  if not SaveStringToFile(ExpandConstant('{app}\{#HostName}.json'), Body, False) then
    MsgBox('The browser extension could not be connected. Reinstalling may fix this.', mbError, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then WriteHostManifest();
end;

// In tray mode the app answers a polite close by hiding, so it has to be told firmly, or its files stay behind.
// A stopped process lets go of its files a moment later, and nothing can be deleted until it has.
procedure StopTheApp();
var
  ExitCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM STXNativeHost.exe', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM STXDownloader.exe', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  Log('Stopping the app: taskkill exit code ' + IntToStr(ExitCode));
  if ExitCode = 0 then Sleep(2500);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then StopTheApp();
  if CurUninstallStep = usPostUninstall then Log('Removal finished, asking about data');
  // Saved videos live elsewhere and are never touched. A silent uninstall keeps everything.
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent() then
    if MsgBox('Also remove your STX Downloader settings, queue history and downloaded tools (about 430 MB)?' + #13#10 + #13#10 +
              'Your saved videos are not touched either way.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{localappdata}\STXDownloader'), True, True, True);
end;
