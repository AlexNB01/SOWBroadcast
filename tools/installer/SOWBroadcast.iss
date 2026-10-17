; SOW Broadcast installer (Inno Setup 6). Built by tools/build-installer.ps1, which passes:
;   /DAppVersion=3.0.0          the program version (read from the project file)
;   /DPublishDir=...            the published program with HTML/ and the shipped images
;   /DOutputDir=...             where SOWBroadcast-Setup-<version>.exe goes
;   /DMusicDir=...              (optional) the Music folder; without it the Music component is left out
;   /DSignToolName=...          (optional) name of a sign tool given with /S on the ISCC command line
;
; The program goes into a folder the user can see and choose (default C:\SOW Broadcast). Everything the program writes
; (autosave, the generated overlay files, logs, settings) lives in the Data folder next to it, so there is one place to look.

#ifndef AppVersion
  #error Pass /DAppVersion=<version>
#endif
#ifndef PublishDir
  #error Pass /DPublishDir=<folder>
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

#define AppName "SOW Broadcast"
#define AppExe "SOWBroadcast.exe"

[Setup]
; A new AppId: this is the C# program, not the old Python one (its installer used another id).
AppId={{6D2F4B7A-1C93-4E58-A0B6-3E7A5C21D9F4}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Suomi OW ry
AppPublisherURL=https://suomiow.fi
DefaultDirName=C:\SOW Broadcast
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Installs for the current user without administrator rights; the dialog offers "for all users" for those who want it.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
SetupIconFile=..\..\src\SowBroadcast.App\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir={#OutputDir}
OutputBaseFilename=SOWBroadcast-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
; An update runs this installer silently over the running program: close it, then start it again ourselves (see [Run]).
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
VersionInfoVersion={#AppVersion}
VersionInfoCompany=Suomi OW ry
VersionInfoDescription={#AppName} setup
#ifdef SignToolName
SignTool={#SignToolName}
SignedUninstaller=yes
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "full"; Description: "Program and music library"
Name: "compact"; Description: "Program only"
Name: "custom"; Description: "Custom"; Flags: iscustom

[Components]
Name: "program"; Description: "SOW Broadcast"; Types: full compact custom; Flags: fixed
#ifdef MusicDir
Name: "music"; Description: "Music library for the stream (about 22 MB)"; Types: full
#endif

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "datashortcut"; Description: "Create a Start menu shortcut to the &data folder"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Dirs]
; The data folder: its presence next to the program is what tells the program to keep its data here.
Name: "{app}\Data"; Components: program; Permissions: users-modify; Check: IsAdminInstallMode
Name: "{app}\Data"; Components: program; Check: not IsAdminInstallMode
; Folders the user fills and the file dialogs open in (the program creates them too, so a copy without installer works).
Name: "{app}\Logos\Team Logos"; Components: program; Permissions: users-modify; Check: IsAdminInstallMode
Name: "{app}\Logos\Team Logos"; Components: program; Check: not IsAdminInstallMode
Name: "{app}\Logos\Broadcast Logos"; Components: program; Permissions: users-modify; Check: IsAdminInstallMode
Name: "{app}\Logos\Broadcast Logos"; Components: program; Check: not IsAdminInstallMode
Name: "{app}\Team Files"; Components: program; Permissions: users-modify; Check: IsAdminInstallMode
Name: "{app}\Team Files"; Components: program; Check: not IsAdminInstallMode

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Components: program; Flags: ignoreversion recursesubdirs createallsubdirs
#ifdef MusicDir
Source: "{#MusicDir}\*"; DestDir: "{app}\Music"; Components: music; Flags: ignoreversion recursesubdirs createallsubdirs
#endif

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{#AppName} data folder"; Filename: "{app}\Data"; Tasks: datashortcut
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Wizard install: offer to start. Silent install (the update): start the program again without asking.
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: WizardSilent

[UninstallDelete]
; Only what the installer's own program created. Data\ (autosave, settings, generated overlay files, logs) is never removed.
Type: filesandordirs; Name: "{app}\HTML"
Type: filesandordirs; Name: "{app}\Scoreboard\Heroes"
Type: filesandordirs; Name: "{app}\Scoreboard\HeroesBody"
Type: filesandordirs; Name: "{app}\Scoreboard\Maps"
Type: filesandordirs; Name: "{app}\Scoreboard\Gametypes"
Type: filesandordirs; Name: "{app}\Scoreboard\Roles"
