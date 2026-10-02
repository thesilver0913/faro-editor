; The usual Windows download (Inno Setup 6): a small installer that fetches Faro from GitHub Releases at install time,
; the newest release by default, or another version (betas too) from "Other versions…". It downloads that version's
; full Setup (faro.iss) and runs it silently with the choices made here; that Setup then starts Faro.
; The version list is versions.txt, which release.yml keeps on the latest release next to this Faro-Setup.exe
; (no GitHub API calls, so no rate limit). Built by release.yml:  iscc /DRepo=owner/name installer\web.iss
; /FAROVERSION=1.0.0 skips the list (the workflow's smoke test), /FAROLIST=<url> reads it from elsewhere (its screenshots);
; /DIR= is passed on to the full Setup.
#ifndef Repo
  #define Repo "thesilver0913/faro-editor"
#endif

[Setup]
AppName=Faro
AppVersion=latest
AppPublisher=Faro
AppPublisherURL=https://github.com/{#Repo}
CreateAppDir=no
Uninstallable=no
DisableProgramGroupPage=yes
DisableReadyPage=yes
DisableFinishedPage=yes
; Per-user by default, like the full Setup; the dialog offers "all users" too (passed on as /ALLUSERS).
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
LicenseFile=..\LICENSE
SetupIconFile=..\assets\faro.ico
OutputDir=..\dist
OutputBaseFilename=Faro-Setup
WizardStyle=modern

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ja"; MessagesFile: "compiler:Languages\Japanese.isl"

[CustomMessages]
en.VersionTitle=Version
en.VersionSubtitle=Faro is downloaded from GitHub while it installs.
en.WillInstall=Faro %1 will be downloaded and installed.
en.Newest=(newest)
en.OtherVersions=Other versions…
en.DesktopIcon=Create a desktop shortcut
en.NoList=Couldn't get the list of Faro versions (%1).%n%nCheck the internet connection, or download a Setup from https://github.com/{#Repo}/releases
en.DownloadFailed=Couldn't download Faro %1: %2
en.SetupFailed=The Faro %1 Setup stopped (exit code %2).
ja.VersionTitle=バージョン
ja.VersionSubtitle=インストール中に GitHub から Faro をダウンロードします。
ja.WillInstall=Faro %1 をダウンロードしてインストールします。
ja.Newest=(最新)
ja.OtherVersions=他のバージョン…
ja.DesktopIcon=デスクトップにショートカットを作る
ja.NoList=Faro のバージョンの一覧を取得できませんでした(%1)。%n%nインターネットの接続を確かめるか、https://github.com/{#Repo}/releases からセットアップをダウンロードしてください。
ja.DownloadFailed=Faro %1 をダウンロードできませんでした: %2
ja.SetupFailed=Faro %1 のセットアップが止まりました(終了コード %2)。

[Code]
var
  Versions: TStringList;
  Newest: Integer;
  VersionPage: TWizardPage;
  Summary: TNewStaticText;
  OtherButton: TNewButton;
  VersionList: TNewListBox;
  DesktopIcon: TNewCheckBox;
  DownloadPage: TDownloadWizardPage;
  Downloaded: String;

// versions.txt: one version per line ("1.0.1-beta.3", "1.0.0"), newest first; the newest without "-" is the default.
function InitializeSetup: Boolean;
var
  Requested: String;
  I: Integer;
begin
  Result := True;
  Versions := TStringList.Create;
  Requested := ExpandConstant('{param:FAROVERSION|}');
  if Requested <> '' then
    Versions.Add(Requested)
  else
    try
      DownloadTemporaryFile(ExpandConstant('{param:FAROLIST|https://github.com/{#Repo}/releases/latest/download/versions.txt}'), 'versions.txt', '', nil);
      Versions.LoadFromFile(ExpandConstant('{tmp}\versions.txt'));
    except
      SuppressibleMsgBox(FmtMessage(CustomMessage('NoList'), [GetExceptionMessage]), mbError, MB_OK, IDOK);
      Result := False;
      Exit;
    end;
  for I := Versions.Count - 1 downto 0 do
  begin
    Versions[I] := Trim(Versions[I]);
    if Versions[I] = '' then Versions.Delete(I);
  end;
  if Versions.Count = 0 then
  begin
    SuppressibleMsgBox(FmtMessage(CustomMessage('NoList'), ['versions.txt']), mbError, MB_OK, IDOK);
    Result := False;
    Exit;
  end;
  Newest := 0;
  for I := Versions.Count - 1 downto 0 do
    if Pos('-', Versions[I]) = 0 then Newest := I;
end;

function Chosen: String;
begin
  if VersionList.ItemIndex >= 0 then Result := Versions[VersionList.ItemIndex] else Result := Versions[Newest];
end;

procedure ShowChoice(Sender: TObject);
begin
  Summary.Caption := FmtMessage(CustomMessage('WillInstall'), [Chosen]);
end;

procedure ShowOthers(Sender: TObject);
begin
  VersionList.Visible := True;
  OtherButton.Enabled := False;
end;

procedure InitializeWizard;
var
  I: Integer;
begin
  VersionPage := CreateCustomPage(wpLicense, CustomMessage('VersionTitle'), CustomMessage('VersionSubtitle'));
  Summary := TNewStaticText.Create(VersionPage);
  Summary.Parent := VersionPage.Surface;
  Summary.WordWrap := True;
  Summary.Width := VersionPage.SurfaceWidth;
  OtherButton := TNewButton.Create(VersionPage);
  OtherButton.Parent := VersionPage.Surface;
  OtherButton.Top := Summary.Top + ScaleY(32);
  OtherButton.Width := ScaleX(150);
  OtherButton.Height := ScaleY(25);
  OtherButton.Caption := CustomMessage('OtherVersions');
  OtherButton.OnClick := @ShowOthers;
  VersionList := TNewListBox.Create(VersionPage);
  VersionList.Parent := VersionPage.Surface;
  VersionList.Top := OtherButton.Top + OtherButton.Height + ScaleY(8);
  VersionList.Width := VersionPage.SurfaceWidth;
  VersionList.Height := ScaleY(110);
  for I := 0 to Versions.Count - 1 do
    if I = Newest then VersionList.Items.Add(Versions[I] + '  ' + CustomMessage('Newest')) else VersionList.Items.Add(Versions[I]);
  VersionList.ItemIndex := Newest;
  VersionList.OnClick := @ShowChoice;
  VersionList.Visible := False;
  DesktopIcon := TNewCheckBox.Create(VersionPage);
  DesktopIcon.Parent := VersionPage.Surface;
  DesktopIcon.Top := VersionList.Top + VersionList.Height + ScaleY(12);
  DesktopIcon.Width := VersionPage.SurfaceWidth;
  DesktopIcon.Caption := CustomMessage('DesktopIcon');
  ShowChoice(nil);
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
end;

function SetupUrl(Version: String): String;
begin
  Result := 'https://github.com/{#Repo}/releases/download/v' + Version + '/Faro-' + Version + '-win-x64-setup.exe';
end;

// With the wizard: the download page shows the progress. Silent installs download in PrepareToInstall.
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID <> VersionPage.ID then Exit;
  DownloadPage.Clear;
  DownloadPage.Add(SetupUrl(Chosen), 'faro-setup.exe', '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
      Downloaded := Chosen;
    except
      if not DownloadPage.AbortedByUser then
        SuppressibleMsgBox(FmtMessage(CustomMessage('DownloadFailed'), [Chosen, GetExceptionMessage]), mbError, MB_OK, IDOK);
      Result := False;
    end;
  finally
    DownloadPage.Hide;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Params, Dir: String;
  Code: Integer;
begin
  Result := '';
  if Downloaded <> Chosen then
    try
      DownloadTemporaryFile(SetupUrl(Chosen), 'faro-setup.exe', '', nil);
    except
      Result := FmtMessage(CustomMessage('DownloadFailed'), [Chosen, GetExceptionMessage]);
      Exit;
    end;
  if WizardSilent then Params := '/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
  else Params := '/SP- /SILENT /SUPPRESSMSGBOXES /NORESTART /NOCANCEL';
  if IsAdminInstallMode then Params := Params + ' /ALLUSERS' else Params := Params + ' /CURRENTUSER';
  Dir := ExpandConstant('{param:DIR|}');
  if Dir <> '' then Params := Params + ' /DIR="' + Dir + '"';
  if DesktopIcon.Checked then Params := Params + ' /MERGETASKS="desktopicon"';
  Params := Params + ' /LANG=' + ActiveLanguage;
  if not Exec(ExpandConstant('{tmp}\faro-setup.exe'), Params, '', SW_SHOW, ewWaitUntilTerminated, Code) or (Code <> 0) then
    Result := FmtMessage(CustomMessage('SetupFailed'), [Chosen, IntToStr(Code)]);
end;
