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
; Only offered when no .NET 10 SDK is installed: it goes to {app}\dotnet, which Faro's launcher looks at first.
Name: "dotnet"; Description: "Download and install the .NET 10 SDK next to Faro (needed; about 200 MB)"; Check: DotnetMissing

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[UninstallDelete]
Type: filesandordirs; Name: "{app}\dotnet"

[Code]
function HasSdk(Dir: String): Boolean;
var
  Found: TFindRec;
begin
  Result := FindFirst(Dir + '\sdk\10.*', Found);
  if Result then FindClose(Found);
end;

function DotnetMissing: Boolean;
begin
  Result := not HasSdk(ExpandConstant('{commonpf64}\dotnet')) and not HasSdk(WizardDirValue + '\dotnet');
end;

// Microsoft's dotnet-install script, per user: no admin rights needed.
procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  if (CurStep <> ssPostInstall) or not WizardIsTaskSelected('dotnet') then Exit;
  WizardForm.StatusLabel.Caption := 'Installing the .NET 10 SDK...';
  try
    DownloadTemporaryFile('https://dot.net/v1/dotnet-install.ps1', 'dotnet-install.ps1', '', nil);
    if not Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\dotnet-install.ps1') +
        '" -Channel 10.0 -InstallDir "' + ExpandConstant('{app}\dotnet') + '"', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
      RaiseException('dotnet-install exited with ' + IntToStr(Code));
  except
    SuppressibleMsgBox('Couldn''t install the .NET 10 SDK (' + GetExceptionMessage + '). Install it from https://dotnet.microsoft.com/download/dotnet/10.0',
      mbError, MB_OK, IDOK);
  end;
end;

[Icons]
Name: "{group}\Faro"; Filename: "{app}\Faro.Editor.exe"
Name: "{autodesktop}\Faro"; Filename: "{app}\Faro.Editor.exe"; Tasks: desktopicon

[Run]
; No skipifsilent: after a silent update Faro starts again by itself.
Filename: "{app}\Faro.Editor.exe"; Description: "{cm:LaunchProgram,Faro}"; Flags: nowait postinstall
