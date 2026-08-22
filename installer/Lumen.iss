; Lumen installer (Inno Setup 6).
;
; Built by build.ps1, which passes three values on the command line so this script never
; hardcodes a path or a version:
;   /DLumenVersion=1.0.0
;   /DLumenPublishDir=<path to the dotnet publish output>
;   /DLumenSourceRoot=<repo root, for the icon path>
;
; Running iscc.exe directly against this file also works, using the fallback defines below.

#define MyAppName "Lumen"
#define MyAppExeName "Lumen.exe"
#define MyAppPublisher "Lumen"

#ifndef LumenVersion
  #define LumenVersion "1.0.0"
#endif

#ifndef LumenSourceRoot
  #define LumenSourceRoot ".."
#endif

#ifndef LumenPublishDir
  #define LumenPublishDir LumenSourceRoot + "\src\Lumen.App\bin\Release\net8.0-windows\win-x64\publish"
#endif

[Setup]
; Fixed forever: changing this GUID would make every future version look like a different
; program to Windows and stop in-place upgrades from finding the previous install.
AppId={{8FC1D30C-3659-4D1D-9935-AD56A7086C86}
AppName={#MyAppName}
AppVersion={#LumenVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#LumenVersion}

; Per-user, no-admin install. This is what makes "double-click, next, next, done" true even on
; a locked-down machine: nothing here ever asks Windows for elevation.
DefaultDirName={localappdata}\Programs\Lumen
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

DefaultGroupName=Lumen
DisableProgramGroupPage=yes

OutputDir={#LumenSourceRoot}\dist
OutputBaseFilename=Lumen-Setup-{#LumenVersion}

SetupIconFile={#LumenSourceRoot}\installer\assets\lumen.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

WizardStyle=modern
Compression=lzma2
SolidCompression=yes

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Inno Setup 6's Restart Manager integration: if Lumen.exe is running during an upgrade, the
; installer detects the lock on the file it needs to replace and offers to close it, rather than
; failing outright.
CloseApplications=yes
CloseApplicationsFilter=*.exe
RestartApplications=no

; No telemetry screen, no bundled extras, nothing to opt out of.

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "pdfassoc"; Description: "Open PDF files with Lumen"; GroupDescription: "File associations:"; Flags: unchecked

[Files]
; self-test-result.txt is a build-time artefact of build.ps1's native gate, not part of the
; product; everything else the publish step produced ships as-is.
Source: "{#LumenPublishDir}\*"; DestDir: "{app}"; Excludes: "self-test-result.txt"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Lumen"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall Lumen"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Lumen"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; OpenWithProgids adds Lumen to the "Open with" list for .pdf without silently seizing the
; default handler, which on modern Windows a user has to grant through Settings anyway. Both
; keys live entirely under HKCU, so no admin rights are needed and per-user isolation holds.
Root: HKCU; Subkey: "Software\Classes\.pdf\OpenWithProgids"; ValueType: string; ValueName: "LumenPDF"; ValueData: ""; Flags: uninsdeletevalue; Tasks: pdfassoc
Root: HKCU; Subkey: "Software\Classes\LumenPDF"; ValueType: string; ValueName: ""; ValueData: "PDF Document"; Flags: uninsdeletekey; Tasks: pdfassoc
Root: HKCU; Subkey: "Software\Classes\LumenPDF\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: pdfassoc
Root: HKCU; Subkey: "Software\Classes\LumenPDF\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: pdfassoc

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,Lumen}"; Flags: nowait postinstall skipifsilent

[Code]
// Settings and the DPAPI-encrypted API key live in %APPDATA%\Lumen, entirely outside {app}, so
// the [Files]-driven uninstall never touches them on its own — they are kept by construction.
// This prompt is the one place the user can choose to also remove them. A plain confirmation box
// is used rather than a custom uninstall wizard page: Inno Setup's uninstaller wizard-page API
// is real but easy to get subtly wrong without a machine to compile and click through it on, and
// a wrong uninstaller is a worse failure mode than a slightly plainer prompt.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  SettingsDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    SettingsDir := ExpandConstant('{userappdata}\Lumen');
    if DirExists(SettingsDir) then
    begin
      if MsgBox('Also remove your Lumen settings and saved API key?' + #13#10 + SettingsDir,
                mbConfirmation, MB_YESNO) = IDYES then
      begin
        DelTree(SettingsDir, True, True, True);
      end;
    end;
  end;
end;
