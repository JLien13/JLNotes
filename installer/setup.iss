#define MyAppName "JL Notes"
#define MyAppVersion "1.0.0"
#define MyAppExeName "JLNotes.exe"
#define DotNetVersion "10"
#define DotNetInstallerUrl "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=JL
DefaultDirName={autopf}\JLNotes
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\output
OutputBaseFilename=JLNotes-Setup
SetupIconFile=..\src\JLNotes\tray.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startup"; Description: "Run JL Notes when Windows starts"; GroupDescription: "Startup:"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\tray.ico"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\tray.ico"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "JLNotes"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{tmp}\windowsdesktop-runtime-win-x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Installing .NET Desktop Runtime..."; Check: not IsDotNetInstalled
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
function IsDotNetInstalled: Boolean;
var
  ResultCode: Integer;
begin
  // dotnet --list-runtimes and check for Microsoft.WindowsDesktop.App 10.x
  Result := False;
  if Exec('cmd.exe', '/c dotnet --list-runtimes 2>nul | findstr /C:"Microsoft.WindowsDesktop.App {#DotNetVersion}."', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := (ResultCode = 0);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  DownloadPage: TDownloadWizardPage;
begin
  Result := True;
  if CurPageID = wpReady then
  begin
    if not IsDotNetInstalled then
    begin
      DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
      DownloadPage.Clear;
      DownloadPage.Add('{#DotNetInstallerUrl}', 'windowsdesktop-runtime-win-x64.exe', '');
      DownloadPage.Show;
      try
        try
          DownloadPage.Download;
        except
          if DownloadPage.AbortedByUser then
            Log('Download aborted by user.')
          else
            SuppressibleMsgBox(AddPeriod(GetExceptionMessage), mbCriticalError, MB_OK, IDOK);
          Result := False;
        end;
      finally
        DownloadPage.Hide;
      end;
    end
    else
      Log('.NET Desktop Runtime {#DotNetVersion} already installed, skipping download.');
  end;
end;
