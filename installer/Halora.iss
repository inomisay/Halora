; Halora installer (Inno Setup 6.3+).
; Build: dotnet publish (framework-dependent, see build.yml), then
;   iscc /DAppVersion=0.2.0 /DPublishDir=..\publish-setup installer\Halora.iss
;
; Installs per user, no admin rights. Halora itself is ~25 MB (~4 MB compressed) because it uses the
; shared .NET Desktop Runtime. When no runtime 8 or newer is installed, setup downloads .NET 10 from
; Microsoft and installs it (that step asks for admin once).

#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish-setup"
#endif

#define RuntimeUrl "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe"
#define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"

[Setup]
AppId={{6B1E3C52-7F0A-4D7B-9C1E-2A4F5D8E9B10}
AppName=Halora
AppVersion={#AppVersion}
AppPublisher=Halora
AppPublisherURL=https://github.com/inomisay/Halora
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\Halora
DefaultGroupName=Halora
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\dist
OutputBaseFilename=Halora-Setup-{#AppVersion}
SetupIconFile=..\src\Halora.App\Assets\halora.ico
UninstallDisplayIcon={app}\Halora.exe
UninstallDisplayName=Halora
LicenseFile=..\LICENSE
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=no

[Tasks]
Name: autostart; Description: "Start Halora with Windows"
Name: ffmpeg; Description: "Install FFmpeg with winget (needed for all conversions except PDF to image)"; Check: not HasFfmpeg

[Files]
Source: "{#PublishDir}\Halora.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Halora"; Filename: "{app}\Halora.exe"

[Registry]
; Same value the tray's "Start with Windows" writes, so the two stay in sync.
Root: HKCU; Subkey: "{#RunKey}"; ValueType: string; ValueName: "Halora"; ValueData: """{app}\Halora.exe"""; Tasks: autostart

[Run]
Filename: "{localappdata}\Microsoft\WindowsApps\winget.exe"; \
  Parameters: "install --exact --id Gyan.FFmpeg --accept-source-agreements --accept-package-agreements"; \
  StatusMsg: "Installing FFmpeg..."; Tasks: ffmpeg; Flags: waituntilterminated skipifdoesntexist
Filename: "{app}\Halora.exe"; Description: "Start Halora now"; Flags: nowait postinstall skipifsilent

[Code]
var
  RuntimePage: TDownloadWizardPage;

function HasDesktopRuntime: Boolean;
var
  Rec: TFindRec;
  Major: Integer;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\*'), Rec) then
  try
    repeat
      if (Rec.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0) and (Pos('.', Rec.Name) > 1) then
      begin
        Major := StrToIntDef(Copy(Rec.Name, 1, Pos('.', Rec.Name) - 1), 0);
        if Major >= 8 then
          Result := True;
      end;
    until Result or not FindNext(Rec);
  finally
    FindClose(Rec);
  end;
end;

function HasFfmpeg: Boolean;
var
  Dirs: String;
begin
  // The same places Halora looks, minus the machine PATH setup can't see changes to.
  Dirs := GetEnv('PATH') + ';' + ExpandConstant('{localappdata}\Microsoft\WinGet\Links');
  Result := FileSearch('ffmpeg.exe', Dirs) <> '';
end;

function HaloraRunning: Boolean;
var
  Code: Integer;
begin
  // find exits with 0 when tasklist printed a Halora.exe line.
  Result := Exec(ExpandConstant('{cmd}'), '/c tasklist /nh /fi "imagename eq Halora.exe" | find /i "Halora.exe"',
    '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
end;

procedure StopHalora;
var
  Code, Waited: Integer;
begin
  // Halora is a tray app with nothing to save, so ending it is safe. Needed to replace or remove the exe.
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im Halora.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);

  // taskkill returns before Windows has released the exe file. Wait (up to 10 s) until the process is gone.
  Waited := 0;
  while HaloraRunning and (Waited < 10000) do
  begin
    Sleep(250);
    Waited := Waited + 250;
  end;
  Sleep(500); // the file lock can outlive the process by a moment
end;

procedure InitializeWizard;
begin
  RuntimePage := CreateDownloadPage('Getting .NET', 'Halora needs the free .NET Desktop Runtime from Microsoft.', nil);
end;

function InstallRuntime: Boolean;
var
  Code: Integer;
begin
  Result := True;
  RuntimePage.Clear;
  RuntimePage.Add('{#RuntimeUrl}', 'windowsdesktop-runtime.exe', '');
  RuntimePage.Show;
  try
    try
      RuntimePage.Download;
    except
      if RuntimePage.AbortedByUser then
        Result := False
      else
        MsgBox('Couldn''t download .NET: ' + GetExceptionMessage + #13#10#13#10 +
          'Halora will be installed anyway. Windows will offer the .NET download when Halora starts.',
          mbInformation, MB_OK);
      Exit;
    end;
  finally
    RuntimePage.Hide;
  end;

  if not ShellExec('runas', ExpandConstant('{tmp}\windowsdesktop-runtime.exe'), '/install /quiet /norestart', '',
    SW_SHOW, ewWaitUntilTerminated, Code) or not ((Code = 0) or (Code = 3010)) then
    MsgBox('.NET wasn''t installed (code ' + IntToStr(Code) + '). ' +
      'Halora will be installed anyway. Windows will offer the .NET download when Halora starts.',
      mbInformation, MB_OK);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and not HasDesktopRuntime then
    Result := InstallRuntime;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopHalora;
  Result := '';
end;

function InitializeUninstall: Boolean;
begin
  StopHalora;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // Also removes the value when autostart was turned on from the tray, not by setup.
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKCU, '{#RunKey}', 'Halora');
end;
