; Windows installer (Inno Setup 6). Built by .github/workflows/release.yml:
;   iscc /DAppVersion=0.2.4 /DSourceDir=<dotnet publish output> installer\faro.iss
; Updates run the newer Setup with /SILENT; it replaces the files in place and starts Faro again.
#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\win-x64"
#endif

[Setup]
AppId={{6B64228E-85D8-4767-A012-92DAB33F27CC}
AppName=Faro
AppVersion={#AppVersion}
AppPublisher=Faro
AppPublisherURL=https://github.com/thesilver0913/faro-editor
AppUpdatesURL=https://github.com/thesilver0913/faro-editor/releases
DefaultDirName={autopf}\Faro
DefaultGroupName=Faro
DisableProgramGroupPage=yes
; Per-user by default (no admin prompt, updates install silently); the dialog offers "all users" too.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=..\LICENSE
InfoBeforeFile=requirements.txt
SetupIconFile=..\assets\faro.ico
UninstallDisplayIcon={app}\Faro.Editor.exe
CloseApplications=yes
OutputDir=..\dist
OutputBaseFilename=Faro-{#AppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ja"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Faro"; Filename: "{app}\Faro.Editor.exe"
Name: "{autodesktop}\Faro"; Filename: "{app}\Faro.Editor.exe"; Tasks: desktopicon

[Run]
; No skipifsilent: after a silent update Faro starts again by itself.
Filename: "{app}\Faro.Editor.exe"; Description: "{cm:LaunchProgram,Faro}"; Flags: nowait postinstall
