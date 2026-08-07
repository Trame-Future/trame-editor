; Installer TrameEditor - Inno Setup 6
; Compilare con: ISCC.exe TrameEditor.iss
; Prerequisito: publish self-contained in ..\dist\publish
;   dotnet publish src/TrameEditor.App -c Release -r win-x64 --self-contained true -o dist/publish

#define MyAppName "TrameEditor"
#define MyAppVersion "2.0.1"
#define MyAppPublisher "Trame Future srls"
#define MyAppURL "https://www.tramefuture.com"
#define MyAppExeName "TrameEditor.exe"

[Setup]
AppId={{7C1E30F2-9A44-4B2E-B1D0-52A6E8D3C9A1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE.txt
; Installazione anche senza diritti di amministratore (per-utente), con scelta
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=TrameEditor-Setup-{#MyAppVersion}
SetupIconFile=..\src\TrameEditor.App\Assets\trameeditor.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "assoc_txt"; Description: "Associa i file .txt a TrameEditor"; GroupDescription: "Associazioni file:"; Flags: unchecked
Name: "assoc_md"; Description: "Associa i file .md (Markdown) a TrameEditor"; GroupDescription: "Associazioni file:"; Flags: unchecked
Name: "assoc_pdf"; Description: "Associa i file .pdf a TrameEditor"; GroupDescription: "Associazioni file:"; Flags: unchecked

[Files]
Source: "..\dist\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE.txt"; DestDir: "{app}"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; ProgId comune
Root: HKA; Subkey: "Software\Classes\TrameEditor.Document"; ValueType: string; ValueData: "Documento TrameEditor"; Flags: uninsdeletekey; Tasks: assoc_txt assoc_md assoc_pdf
Root: HKA; Subkey: "Software\Classes\TrameEditor.Document\DefaultIcon"; ValueType: string; ValueData: "{app}\{#MyAppExeName},0"; Tasks: assoc_txt assoc_md assoc_pdf
Root: HKA; Subkey: "Software\Classes\TrameEditor.Document\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: assoc_txt assoc_md assoc_pdf
; Estensioni (solo se l'utente le ha scelte)
Root: HKA; Subkey: "Software\Classes\.txt\OpenWithProgids"; ValueType: string; ValueName: "TrameEditor.Document"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assoc_txt
Root: HKA; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: string; ValueName: "TrameEditor.Document"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assoc_md
Root: HKA; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: string; ValueName: "TrameEditor.Document"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assoc_md
Root: HKA; Subkey: "Software\Classes\.pdf\OpenWithProgids"; ValueType: string; ValueName: "TrameEditor.Document"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assoc_pdf

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
