; =========================
; SOWBroadcast Inno Setup (C:\-asennus, launcher.bat)
; =========================

#define Dist "C:\Suomi OW koodiprojektit\SOWBroadcast\dist"
#define Base "C:\Suomi OW koodiprojektit\SOWBroadcast"

[Setup]
AppId={{7D2E6A98-6B9A-4E61-9F2A-2B3E6F2C1A01}
AppName=SOW Broadcast
AppVersion=2.1
AppPublisher=Suomi OW
DefaultDirName=C:\SOWBroadcast
DisableDirPage=no
DefaultGroupName=SOW Broadcast
UninstallDisplayIcon={app}\SOWBroadcast.exe
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=SOWBroadcast-Setup
OutputDir={#Base}\installer
Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Files]
; SOWBroadcast.spec/SOWServer.spec build in PyInstaller onefile mode (no COLLECT
; step), so dist\ holds just these two standalone exes — not a whole folder.
Source: "{#Dist}\SOWBroadcast.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Dist}\SOWServer.exe";   DestDir: "{app}"; Flags: ignoreversion

; Launcher ja OBS Scene Collection (varmuudeksi Base:sta)
Source: "{#Base}\launch.bat";       DestDir: "{app}"; Flags: ignoreversion
Source: "{#Base}\SOWBROADCAST.json"; DestDir: "{app}"; Flags: ignoreversion

; Jos HTML/Highlights/Music/Videos eivät ole Distissä, kopioi ne Basesta
Source: "{#Base}\HTML\*";       DestDir: "{app}\HTML";       Flags: recursesubdirs createallsubdirs ignoreversion
Source: "{#Base}\Music\*";      DestDir: "{app}\Music";      Flags: recursesubdirs createallsubdirs ignoreversion

; Scoreboard: toimita template/sisältö, mutta älä ylikirjoita jos käyttäjä on muokannut/asentanut aiemmin
Source: "{#Base}\Scoreboard\*"; DestDir: "{app}\Scoreboard"; Flags: recursesubdirs createallsubdirs onlyifdoesntexist; Excludes: "__pycache__\*"

[Dirs]
; Kirjoitusoikeudet C:\-asennuksessa
Name: "{app}\Scoreboard";                 Flags: uninsneveruninstall; Permissions: users-modify
Name: "{app}\Scoreboard\General";         Permissions: users-modify
Name: "{app}\Scoreboard\Match";           Permissions: users-modify
Name: "{app}\Scoreboard\Heroes";          Permissions: users-modify
Name: "{app}\Scoreboard\Maps";            Permissions: users-modify
Name: "{app}\Scoreboard\Gametypes";       Permissions: users-modify
Name: "{app}\Scoreboard\Replay";          Permissions: users-modify
Name: "{app}\Scoreboard\Replay\Playlist"; Permissions: users-modify
Name: "{app}\Scoreboard\Roles";           Permissions: users-modify
Name: "{app}\Scoreboard\Teams";           Permissions: users-modify
Name: "{app}\Scoreboard\Temp";            Permissions: users-modify

; Myös näihin halutaan Modify-oikeus käyttäjille
Name: "{app}\Highlights"; Permissions: users-modify
Name: "{app}\Music";      Permissions: users-modify
Name: "{app}\Videos";     Permissions: users-modify

[Icons]
; Pikakuvakkeet launcheriin (ikoniksi exe)
Name: "{group}\SOW Broadcast";           Filename: "{app}\launch.bat"; WorkingDir: "{app}"; IconFilename: "{app}\SOWBroadcast.exe"
; Hyötylinkit
Name: "{group}\Open App Folder";         Filename: "explorer.exe"; Parameters: """{app}"""
Name: "{group}\SOWBROADCAST.json";       Filename: "{app}\SOWBROADCAST.json"

[Run]
; (Valinnainen) käynnistä heti asennuksen lopuksi
Filename: "{app}\launch.bat"; Description: "Launch SOW Broadcast"; Flags: nowait postinstall skipifsilent

; (Varmistus) aseta ACL:t myös päivitys-/uudelleenasennuksissa
Filename: "{cmd}"; Parameters: "/c icacls ""{app}\Scoreboard""  /grant *S-1-5-32-545:(OI)(CI)M /T /C"; Flags: runhidden waituntilterminated
Filename: "{cmd}"; Parameters: "/c icacls ""{app}\Highlights""  /grant *S-1-5-32-545:(OI)(CI)M /T /C"; Flags: runhidden waituntilterminated
Filename: "{cmd}"; Parameters: "/c icacls ""{app}\Music""       /grant *S-1-5-32-545:(OI)(CI)M /T /C"; Flags: runhidden waituntilterminated
Filename: "{cmd}"; Parameters: "/c icacls ""{app}\Videos""      /grant *S-1-5-32-545:(OI)(CI)M /T /C"; Flags: runhidden waituntilterminated
