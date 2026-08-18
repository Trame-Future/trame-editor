; Installer TrameEditor - Inno Setup 6
; Compilare con: ISCC.exe TrameEditor.iss
; Prerequisito: publish self-contained in ..\dist\publish
;   dotnet publish src/TrameEditor.App -c Release -r win-x64 --self-contained true -o dist/publish

#define MyAppName "TrameEditor"
#define MyAppVersion "2.12.0"
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
Name: "shellmenu"; Description: "Aggiungi TrameEditor al menu del tasto destro (su Windows 11: ""Mostra altre opzioni"")"; GroupDescription: "Integrazione con Windows:"

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

; ── Menu contestuale (voci di menu, NON associazioni: il programma predefinito non cambia) ──
; Sempre in HKCU, come fa l'app da Impostazioni: così i due percorsi vedono le stesse voci.
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.pdf\shell\TrameEditor.apri"; ValueType: string; ValueData: "Apri con TrameEditor"; Flags: uninsdeletekey; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.pdf\shell\TrameEditor.apri"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.pdf\shell\TrameEditor.apri\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.pdf\shell\TrameEditor.pdfa"; ValueType: string; ValueData: "Converti in PDF/A (archiviazione)"; Flags: uninsdeletekey; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.pdf\shell\TrameEditor.pdfa"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.pdf\shell\TrameEditor.pdfa\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" --pdfa ""%1"""; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.pdf\shell\TrameEditor.redact"; ValueType: string; ValueData: "Anonimizza con TrameEditor"; Flags: uninsdeletekey; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.pdf\shell\TrameEditor.redact"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.pdf\shell\TrameEditor.redact\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" --anonimizza ""%1"""; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.p7m\shell\TrameEditor.apri"; ValueType: string; ValueData: "Apri con TrameEditor (firme e contenuto)"; Flags: uninsdeletekey; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.p7m\shell\TrameEditor.apri"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.p7m\shell\TrameEditor.apri\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.xml\shell\TrameEditor.apri"; ValueType: string; ValueData: "Apri con TrameEditor (fattura leggibile)"; Flags: uninsdeletekey; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.xml\shell\TrameEditor.apri"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.xml\shell\TrameEditor.apri\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.txt\shell\TrameEditor.apri"; ValueType: string; ValueData: "Apri con TrameEditor"; Flags: uninsdeletekey; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.txt\shell\TrameEditor.apri"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.txt\shell\TrameEditor.apri\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.md\shell\TrameEditor.apri"; ValueType: string; ValueData: "Apri con TrameEditor"; Flags: uninsdeletekey; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.md\shell\TrameEditor.apri"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.md\shell\TrameEditor.apri\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\TrameEditor.cerca"; ValueType: string; ValueData: "Cerca nei PDF di questa cartella"; Flags: uninsdeletekey; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\TrameEditor.cerca"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\TrameEditor.cerca\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" --cerca ""%1"""; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\TrameEditor.estrai"; ValueType: string; ValueData: "Estrai dai file firmati (.p7m)"; Flags: uninsdeletekey; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\TrameEditor.estrai"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: shellmenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\TrameEditor.estrai\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" --estrai-firmati ""%1"""; Tasks: shellmenu

; Pulizia alla disinstallazione anche delle voci attivate dopo, da Impostazioni:
; dontcreatekey = non scrive niente ora, cancella solo quando si disinstalla.
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.pdf\shell\TrameEditor.apri"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.pdf\shell\TrameEditor.pdfa"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.pdf\shell\TrameEditor.redact"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.p7m\shell\TrameEditor.apri"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.xml\shell\TrameEditor.apri"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.txt\shell\TrameEditor.apri"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.md\shell\TrameEditor.apri"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\TrameEditor.cerca"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\TrameEditor.estrai"; Flags: dontcreatekey uninsdeletekey

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
