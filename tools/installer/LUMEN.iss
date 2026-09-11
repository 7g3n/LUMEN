; LUMEN installer (spec §72-73).
;
; Compiled by tools/release.ps1, which passes the version and the folder to package:
;   ISCC.exe /DAppVersion=0.1.0 /DSourceDir=..\..\publish\app /DOutputDir=..\..\dist LUMEN.iss
;
; Two rules this script exists to honour:
;
;   - The game installs to Program Files and writes nothing there at runtime. All user
;     data lives under %LOCALAPPDATA%\LUMEN (§73), which is also why uninstalling does
;     not delete it: somebody who reinstalls should find their profile, scores and charts
;     where they left them, and somebody who wants them gone can say so.
;
;   - Nothing here reaches the network. No updater, no telemetry, no bundled runtime
;     download — the published folder already contains everything the game needs.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\..\publish\app"
#endif

#ifndef OutputDir
  #define OutputDir "..\..\dist"
#endif

#define AppName "LUMEN"
#define AppPublisher "LUMEN project"
#define AppExeName "LUMEN.exe"

[Setup]
; A stable GUID: this is what lets an upgrade replace an install rather than sit
; beside it. It must not change between releases.
AppId={{8E3C6F14-2B7A-4E59-9C41-6A0D5B8F7E22}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName} {#AppVersion}

; x64 only, matching the win-x64 publish.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Installing for everyone needs elevation; the game itself never does, at any point.
;
; "Just for me" is offered as well, and not only as a convenience: plenty of people cannot
; elevate on the machine they play on, and a rhythm game is not worth an argument with an
; IT department. A per-user install lands in %LOCALAPPDATA%\Programs\LUMEN and behaves
; identically — the data folder is per-user either way (§73).
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog commandline

OutputDir={#OutputDir}
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
; The whole published folder: the executable, the native libraries MonoGame and SQLite
; load at runtime, and the bundled OFL fonts. The symbol files travel with it on purpose —
; they are what turns a stack trace in a crash report into one with line numbers in it,
; and this game asks players to send those reports rather than phoning home.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Anything the game wrote inside its own install folder — there should be nothing, but
; a portable sentinel dropped here by hand would otherwise be left behind.
Type: files; Name: "{app}\portable.txt"
Type: dirifempty; Name: "{app}"

[Code]
{
  Player data is deliberately left alone by the uninstaller, so this asks once rather
  than deciding for them. Declining is the default: losing a rating and a chart library
  to an uninstall you meant as an upgrade is not a recoverable mistake.

  And it only asks when there is somebody there to answer. A silent uninstall — which is
  what every package manager does, and what IT deployment does — cannot show a prompt, and
  MB_DEFBUTTON2 does not save you there: with /SUPPRESSMSGBOXES the box is answered Yes and
  the data goes. That was found the only way these things are found, by running a silent
  uninstall and watching a data folder disappear. "Nobody could be asked" must never be
  read as "yes, delete my rating and my chart library", so in silent mode the data is
  simply kept. Somebody who genuinely wants it gone can delete the folder, which is one
  folder, in a place the game tells them about.
}
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if UninstallSilent then
      Exit;

    DataDir := ExpandConstant('{localappdata}\{#AppName}');
    if DirExists(DataDir) then
    begin
      if MsgBox('Remove your {#AppName} data as well?' + #13#10 + #13#10 +
                'This deletes your profiles, scores, ratings, charts, songs and replays in:' + #13#10 +
                DataDir + #13#10 + #13#10 +
                'Choose No if you are reinstalling or upgrading.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        DelTree(DataDir, True, True, True);
      end;
    end;
  end;
end;
