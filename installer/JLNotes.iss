; installer/JLNotes.iss -- JL Notes installer.
;
; JL Notes is a per-user WPF sticky-notes tray app that shares markdown notes
; with Claude Code. This is a PERSONAL PRODUCTIVITY TOOL, NOT medical software --
; there are deliberately no HIPAA / drive-encryption / data-handling gates here.
; Notes live in %USERPROFILE%\.jlnotes and are treated as the user's documents:
; never touched on install, preserved on uninstall unless the user opts out.
;
; The wizard chrome (modern style, branded top-right image, fresh / upgrade /
; repair / downgrade detection, disk precheck, kill-the-running-tray-app before
; overwrite, clean version stamping) is modeled on the CorVascular vg-fleet
; installer, but stripped to what a normal desktop app needs and rebranded to
; JL Notes. The .NET Desktop Runtime bootstrap is JL-Notes-specific.

#define MyAppName        "JL Notes"
; Version is normally injected by build.ps1 (/DMyAppVersion=X.Y.Z) from the
; single source of truth in src\JLNotes\JLNotes.csproj. This fallback only
; applies when ISCC is run by hand without that switch.
#ifndef MyAppVersion
  #define MyAppVersion   "1.1.0"
#endif
#define MyAppExeName     "JLNotes.exe"
#define MyAppPublisher   "JL"
; AppId -- DO NOT change between versions. It is the upgrade key, and matches the
; original setup.iss so this installs cleanly over an existing 1.0.0.
#define MyAppId          "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}"
#define MyAppComments    "Sticky-notes app that shares markdown notes with Claude Code."
#define MyAppDefaultDir  "{autopf}\JLNotes"

; .NET 10 Desktop Runtime bootstrap (framework-dependent build -> needs the runtime).
#define DotNetVersion      "10"
#define DotNetInstallerUrl "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe"

[Setup]
AppId={{#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppComments={#MyAppComments}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
VersionInfoCopyright=MIT License
DefaultDirName={#MyAppDefaultDir}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Show the Welcome page -- we customize its text per install scenario in [Code].
DisableWelcomePage=no
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
OutputDir=Output
OutputBaseFilename=JLNotes-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; The setup .exe icon + wizard title-bar/taskbar icon.
SetupIconFile=..\src\JLNotes\tray.ico
; The JL Notes mark in the top-right of every inner page. Inno picks the size
; closest to the running display scale; baked onto white (BMP has no alpha).
WizardSmallImageFile=branding\JLNWizardSmall-055.bmp,branding\JLNWizardSmall-069.bmp,branding\JLNWizardSmall-083.bmp,branding\JLNWizardSmall-097.bmp,branding\JLNWizardSmall-110.bmp,branding\JLNWizardSmall-124.bmp,branding\JLNWizardSmall-138.bmp
WizardSmallImageBackColor=$FFFFFF
; MIT license shown on its own page, and shipped next to the exe.
LicenseFile=..\LICENSE
; Auto-write a setup log to %TEMP%\Setup Log YYYY-MM-DD #NNN.txt on every run,
; so a failed install can be diagnosed after the fact.
SetupLogging=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Per-user install -- no admin rights needed. (The .NET runtime sub-installer
; will raise its own UAC prompt if the runtime is actually missing.)
PrivilegesRequired=lowest
; Force-close a running JL Notes (it's a tray app holding its files open) so an
; upgrade can overwrite them. PrepareToInstall in [Code] also taskkills it.
CloseApplications=force
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
; Friendlier wording than Inno's stock strings.
SelectDirDesc=Choose where {#MyAppName} should be installed, then click Next.
ReadyLabel1=Setup is ready to install {#MyAppName} on your computer.
ReadyLabel2a=Click Install to continue. Your existing notes in your user folder are never touched.

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"
; Auto-start is opt-in: unchecked by default on install. After install the
; in-app Settings window ("Launch JL Notes when Windows starts") owns the same
; HKCU Run value, so users can flip it either way without reinstalling.
Name: "startupicon"; Description: "&Launch JL Notes when Windows starts"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; License redistributed next to the exe so the installed version is self-describing.
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "License.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}";                       Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\tray.ico"
Name: "{group}\Uninstall {#MyAppName}";             Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}";                 Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\tray.ico"; Tasks: desktopicon

[Registry]
; Per-user auto-start. uninsdeletevalue cleans it on uninstall. Same value name
; and format as SettingsService.SetAutoStart, which owns this key after install.
; --minimized keeps a boot launch in the tray instead of popping the panel.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "JLNotes"; ValueData: """{app}\{#MyAppExeName}"" --minimized"; \
  Flags: uninsdeletevalue; Tasks: startupicon

[Run]
; Install the .NET 10 Desktop Runtime only if it's actually missing. The .exe is
; downloaded on the Ready page (NextButtonClick below) to {tmp} first.
Filename: "{tmp}\windowsdesktop-runtime-win-x64.exe"; Parameters: "/install /quiet /norestart"; \
  StatusMsg: "Installing .NET {#DotNetVersion} Desktop Runtime..."; \
  Check: not IsDotNetInstalled; Flags: skipifdoesntexist
; Offer to launch on the Finished page. Default checked.
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName} now"; \
  Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Remove the install dir (shipped files are auto-removed; this sweeps any
; leftover empty folders). The user's notes under %USERPROFILE%\.jlnotes are
; NOT listed here -- they are handled by the opt-in prompt in [Code].
Type: filesandordirs; Name: "{app}"

[Code]
const
  NotesSubPath = '\.jlnotes';
  // Per-user (PrivilegesRequired=lowest) -> Inno registers uninstall info in HKCU.
  UninstallRegKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#MyAppId}_is1';
  MinDiskSpaceMb  = 250;

var
  // Install-state flags, set in InitializeSetup, consumed by CurPageChanged.
  IsFreshInstall: Boolean;
  IsUpgrade: Boolean;       // installed < this version
  IsRepair: Boolean;        // installed == this version
  IsDowngrade: Boolean;     // installed > this version (user confirmed)
  InstalledVersion: String; // empty if no prior install

  // Set in InitializeUninstall by the keep-my-notes prompt; read in
  // CurUninstallStepChanged to actually delete the notes folder.
  WipeNotes: Boolean;

{ ---------- shell icon-cache refresh (taskbar / desktop / Start) ---------- }
// On an upgrade the .exe and tray.ico are replaced, but Windows keeps painting
// the OLD cached icon on pinned taskbar buttons and existing shortcuts until the
// shell is told to re-read them. (Inno already overwrites the Start Menu / desktop
// shortcuts and force-closes the running tray app before copying files; this is the
// remaining gap.) SHChangeNotify(SHCNE_ASSOCCHANGED) forces Explorer to refresh
// icons so the current JL Notes mark shows up everywhere -- no sign-out needed.
const
  SHCNE_ASSOCCHANGED = $08000000;
  SHCNF_IDLIST       = $0000;

procedure SHChangeNotify(wEventId: Integer; uFlags: Cardinal; dwItem1, dwItem2: Cardinal);
  external 'SHChangeNotify@shell32.dll stdcall';

procedure RefreshShellIcons();
begin
  SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, 0, 0);
end;

{ ---------- .NET runtime detection ---------- }

function IsDotNetInstalled: Boolean;
var
  ResultCode: Integer;
begin
  Result := False;
  if Exec('cmd.exe',
          '/c dotnet --list-runtimes 2>nul | findstr /C:"Microsoft.WindowsDesktop.App {#DotNetVersion}."',
          '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := (ResultCode = 0);
end;

{ ---------- running-app detection / kill ---------- }

function IsAppRunning(): Boolean;
var
  ResultCode: Integer;
  TmpFile: String;
  Lines: TArrayOfString;
  i: Integer;
begin
  Result := False;
  TmpFile := ExpandConstant('{tmp}\jln-tasklist.txt');
  if Exec(ExpandConstant('{cmd}'),
          '/c tasklist /NH /FO CSV /FI "IMAGENAME eq {#MyAppExeName}" > "' + TmpFile + '"',
          '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    if LoadStringsFromFile(TmpFile, Lines) then
      for i := 0 to GetArrayLength(Lines) - 1 do
        if Pos('{#MyAppExeName}', Lines[i]) > 0 then
        begin
          Result := True;
          Break;
        end;
    DeleteFile(TmpFile);
  end;
end;

procedure StopApp();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{cmd}'), '/c taskkill /F /IM {#MyAppExeName} /T',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(600); // let the OS release file handles before we overwrite the exe
end;

{ ---------- disk space precheck ---------- }

function HasEnoughDiskSpace(): Boolean;
var
  FreeBytes, TotalBytes: Int64;
  FreeMB: Int64;
  Drive, Msg: String;
begin
  Result := True;
  Drive := ExtractFileDrive(ExpandConstant('{autopf}'));
  if Drive = '' then Exit;
  if not GetSpaceOnDisk64(Drive, FreeBytes, TotalBytes) then Exit;
  FreeMB := FreeBytes div 1048576;
  if FreeMB < MinDiskSpaceMb then
  begin
    Msg := 'Not enough free disk space on ' + Drive + #13#10 + #13#10 +
           '    Required:   ' + IntToStr(MinDiskSpaceMb) + ' MB' + #13#10 +
           '    Available:  ' + IntToStr(FreeMB) + ' MB' + #13#10 + #13#10 +
           'Free up some space on ' + Drive + ' and run this installer again.';
    MsgBox(Msg, mbCriticalError, MB_OK);
    Result := False;
  end;
end;

{ ---------- version compare / install-state detection ---------- }

function GetInstalledVersion(): String;
var
  V: String;
begin
  Result := '';
  // Per-user install lives in HKCU; check HKLM too in case an older build was
  // ever installed for all users.
  if RegQueryStringValue(HKCU, UninstallRegKey, 'DisplayVersion', V) then
    Result := V
  else if RegQueryStringValue(HKLM, UninstallRegKey, 'DisplayVersion', V) then
    Result := V;
end;

function CompareVersionParts(A, B: String): Integer;
var
  ai, bi, dot: Integer;
  pa, pb: String;
begin
  Result := 0;
  while (Length(A) > 0) or (Length(B) > 0) do
  begin
    dot := Pos('.', A);
    if dot > 0 then begin pa := Copy(A, 1, dot - 1); Delete(A, 1, dot); end
    else begin pa := A; A := ''; end;
    dot := Pos('.', B);
    if dot > 0 then begin pb := Copy(B, 1, dot - 1); Delete(B, 1, dot); end
    else begin pb := B; B := ''; end;
    ai := StrToIntDef(pa, 0);
    bi := StrToIntDef(pb, 0);
    if ai < bi then begin Result := -1; Exit; end;
    if ai > bi then begin Result :=  1; Exit; end;
  end;
end;

function InitializeSetup(): Boolean;
var
  Cmp: Integer;
  Prompt: String;
begin
  Result := True;

  if not HasEnoughDiskSpace() then
  begin
    Result := False;
    Exit;
  end;

  InstalledVersion := GetInstalledVersion();
  IsFreshInstall := (InstalledVersion = '');
  IsUpgrade := False; IsRepair := False; IsDowngrade := False;

  if not IsFreshInstall then
  begin
    Cmp := CompareVersionParts(InstalledVersion, '{#MyAppVersion}');
    if Cmp = 0 then
      IsRepair := True
    else if Cmp < 0 then
      IsUpgrade := True
    else
    begin
      Prompt :=
        'A newer version of {#MyAppName} is already installed.' + #13#10 + #13#10 +
        '    Currently installed:   ' + InstalledVersion + #13#10 +
        '    About to install:      {#MyAppVersion}  (older)' + #13#10 + #13#10 +
        'Your notes will be preserved regardless. Continue with the downgrade?';
      if MsgBox(Prompt, mbConfirmation, MB_YESNO or MB_DEFBUTTON2) <> IDYES then
        Result := False
      else
        IsDowngrade := True;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  // Kill a running tray instance before file copy so its .exe/.dll aren't locked.
  StopApp();
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // Files are in place -- nudge Explorer to drop stale shortcut / taskbar icons.
  if CurStep = ssPostInstall then
    RefreshShellIcons();
end;

{ ---------- .NET runtime download on the Ready page ---------- }

function NextButtonClick(CurPageID: Integer): Boolean;
var
  DownloadPage: TDownloadWizardPage;
begin
  Result := True;
  if CurPageID = wpReady then
  begin
    if not IsDotNetInstalled then
    begin
      DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing),
        SetupMessage(msgPreparingDesc), nil);
      DownloadPage.Clear;
      DownloadPage.Add('{#DotNetInstallerUrl}', 'windowsdesktop-runtime-win-x64.exe', '');
      DownloadPage.Show;
      try
        try
          DownloadPage.Download;
        except
          if DownloadPage.AbortedByUser then
            Log('.NET runtime download aborted by user.')
          else
            SuppressibleMsgBox(AddPeriod(GetExceptionMessage), mbCriticalError, MB_OK, IDOK);
          Result := False;
        end;
      finally
        DownloadPage.Hide;
      end;
    end
    else
      Log('.NET {#DotNetVersion} Desktop Runtime already present -- skipping download.');
  end;
end;

{ ---------- per-scenario Welcome / Finished captions ---------- }

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpWelcome then
  begin
    if IsFreshInstall then
    begin
      WizardForm.WelcomeLabel1.Caption := 'Welcome -- {#MyAppName} {#MyAppVersion} Setup';
      WizardForm.WelcomeLabel2.Caption :=
        '{#MyAppName} is a lightweight sticky-notes app that lives in your system tray. ' +
        'Your notes are plain markdown files, so Claude Code can read and write them too -- ' +
        'a note created in either place shows up in the other instantly.' + #13#10 + #13#10 +
        'This installer will:' + #13#10 +
        '   -  Install JL Notes to your user Program Files (no admin needed)' + #13#10 +
        '   -  Add Start Menu, and optional desktop / start-up shortcuts' + #13#10 +
        '   -  Fetch the .NET 10 Desktop Runtime automatically if it is missing' + #13#10 + #13#10 +
        'Your existing notes (in your user folder) are never touched.' + #13#10 + #13#10 +
        'Click Next to continue, or Cancel to exit.';
    end
    else if IsRepair then
    begin
      WizardForm.WelcomeLabel1.Caption := 'Repair {#MyAppName} {#MyAppVersion}';
      WizardForm.WelcomeLabel2.Caption :=
        '{#MyAppName} {#MyAppVersion} is already installed.' + #13#10 + #13#10 +
        'This will reinstall it in place. Your notes and settings are left alone.' + #13#10 + #13#10 +
        'Click Next to continue, or Cancel to exit.';
    end
    else if IsUpgrade then
    begin
      WizardForm.WelcomeLabel1.Caption := 'Upgrading {#MyAppName}';
      WizardForm.WelcomeLabel2.Caption :=
        'You are about to upgrade {#MyAppName}.' + #13#10 + #13#10 +
        '    Currently installed:   ' + InstalledVersion + #13#10 +
        '    Upgrading to:          {#MyAppVersion}' + #13#10 + #13#10 +
        'Your notes are preserved. If JL Notes is running, it will be closed, ' +
        'updated, and you can relaunch it on the last page.' + #13#10 + #13#10 +
        'Click Next to continue, or Cancel to keep the current version.';
    end
    else if IsDowngrade then
    begin
      WizardForm.WelcomeLabel1.Caption := 'Downgrading {#MyAppName}';
      WizardForm.WelcomeLabel2.Caption :=
        'You are about to install an older version of {#MyAppName}.' + #13#10 + #13#10 +
        '    Currently installed:   ' + InstalledVersion + #13#10 +
        '    Reverting to:          {#MyAppVersion}  (older)' + #13#10 + #13#10 +
        'Your notes are preserved.' + #13#10 + #13#10 +
        'Click Next to continue, or Cancel to keep the current version.';
    end;
  end
  else if CurPageID = wpFinished then
  begin
    if IsFreshInstall then
    begin
      WizardForm.FinishedHeadingLabel.Caption := 'JL Notes is ready';
      WizardForm.FinishedLabel.Caption :=
        '{#MyAppName} {#MyAppVersion} has been installed.' + #13#10 + #13#10 +
        'Look for the JL Notes icon in your system tray (bottom-right of the taskbar). ' +
        'Click it to open the notes panel. Notes you create are saved as markdown in ' +
        'your user folder, where Claude Code can pick them up.';
    end
    else if IsUpgrade then
    begin
      WizardForm.FinishedHeadingLabel.Caption := 'Upgrade complete';
      WizardForm.FinishedLabel.Caption :=
        '{#MyAppName} was upgraded from ' + InstalledVersion + ' to {#MyAppVersion}. ' +
        'Your notes are unchanged.';
    end
    else if IsDowngrade then
    begin
      WizardForm.FinishedHeadingLabel.Caption := 'Downgrade complete';
      WizardForm.FinishedLabel.Caption :=
        '{#MyAppName} was reverted from ' + InstalledVersion + ' to {#MyAppVersion}. ' +
        'Your notes are unchanged.';
    end
    else if IsRepair then
    begin
      WizardForm.FinishedHeadingLabel.Caption := 'Repair complete';
      WizardForm.FinishedLabel.Caption :=
        '{#MyAppName} {#MyAppVersion} has been reinstalled. Your notes were not touched.';
    end;
  end;
end;

{ ---------- uninstall: protect the user's notes ---------- }

function InitializeUninstall(): Boolean;
var
  NotesDir, Prompt: String;
begin
  if IsAppRunning() then
    StopApp();

  WipeNotes := False;
  NotesDir := ExpandConstant('{%USERPROFILE%}') + NotesSubPath;

  if DirExists(NotesDir) then
  begin
    Prompt :=
      'Also delete your JL Notes notes from this PC?' + #13#10 + #13#10 +
      'They live in:' + #13#10 +
      '    ' + NotesDir + #13#10 + #13#10 +
      'Click No to keep them (recommended) so a reinstall picks up right where you left off.';
    WipeNotes := (MsgBox(Prompt, mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES);
  end;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  NotesDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if WipeNotes then
    begin
      NotesDir := ExpandConstant('{%USERPROFILE%}') + NotesSubPath;
      DelTree(NotesDir, True, True, True);
    end;
    // The Run value may have been written by the in-app Settings toggle rather
    // than the startupicon task, in which case uninsdeletevalue never recorded
    // it -- delete it unconditionally so no stale auto-start survives.
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'JLNotes');
    // Clear the removed shortcuts' icons from the shell cache too.
    RefreshShellIcons();
  end;
end;
