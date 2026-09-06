; ============================================================
; SOWBroadcast – Inno Setup Script
; ============================================================
; Vaatimukset ennen kääntämistä:
;   1. pip install pyinstaller
;   2. pyinstaller SOWBroadcast.spec   → luo dist\SOWBroadcast.exe
;   3. pyinstaller SOWServer.spec      → luo dist\SOWServer.exe
;   4. Käännä tämä tiedosto Inno Setup Compilerilla
; ============================================================

#define AppName      "SOWBroadcast"
#define AppVersion   "2.2.0"
#define AppPublisher "Suomi OW"
#define AppExeName   "SOWBroadcast.exe"
#define ServerExe    "SOWServer.exe"
#define LauncherBat  "launch.bat"

[Setup]
AppId={{B3A2F7C1-4E8D-4A9B-92D0-5F6E3C1D8A47}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/AlexNB01/SOWBroadcast
AppSupportURL=https://github.com/AlexNB01/SOWBroadcast/issues
AppUpdatesURL=https://github.com/AlexNB01/SOWBroadcast/releases
DefaultDirName=C:\{#AppName}
DefaultGroupName={#AppName}
LicenseFile=LICENSE
OutputDir=installer
OutputBaseFilename=SOWBroadcast-{#AppVersion}-Setup
SetupIconFile=icon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=no
; Estä käynnissä olevan version päällä asennus
CloseApplications=yes
CloseApplicationsFilter=SOWBroadcast.exe,SOWServer.exe

[Languages]
Name: "english";  MessagesFile: "compiler:Default.isl"
Name: "finnish";  MessagesFile: "compiler:Languages\Finnish.isl"

[Tasks]
Name: "desktopicon"; \
  Description: "{cm:CreateDesktopIcon}"; \
  GroupDescription: "{cm:AdditionalIcons}"; \
  Flags: checkedonce

[Files]
; ---- Suoritettavat tiedostot ----
Source: "dist\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "dist\{#ServerExe}";  DestDir: "{app}"; Flags: ignoreversion
Source: "{#LauncherBat}";     DestDir: "{app}"; Flags: ignoreversion

; ---- HTML-overlayt ----
Source: "HTML\*"; \
  DestDir: "{app}\HTML"; \
  Flags: ignoreversion recursesubdirs createallsubdirs

; ---- Staattiset Scoreboard-resurssit ----
; Sankarikuvat
Source: "Scoreboard\Heroes\*"; \
  DestDir: "{app}\Scoreboard\Heroes"; \
  Flags: ignoreversion recursesubdirs createallsubdirs

; Sankarien kehokuvat
Source: "Scoreboard\HeroesBody\*"; \
  DestDir: "{app}\Scoreboard\HeroesBody"; \
  Flags: ignoreversion recursesubdirs createallsubdirs

; Karttakuvat
Source: "Scoreboard\Maps\*"; \
  DestDir: "{app}\Scoreboard\Maps"; \
  Flags: ignoreversion recursesubdirs createallsubdirs

; Pelityyppikuvat
Source: "Scoreboard\Gametypes\*"; \
  DestDir: "{app}\Scoreboard\Gametypes"; \
  Flags: ignoreversion recursesubdirs createallsubdirs

; Rooli-ikonit
Source: "Scoreboard\Roles\*"; \
  DestDir: "{app}\Scoreboard\Roles"; \
  Flags: ignoreversion recursesubdirs createallsubdirs

; Sosiaalisen median ikonit (SVG)
Source: "Scoreboard\General\Icons\*"; \
  DestDir: "{app}\Scoreboard\General\Icons"; \
  Flags: ignoreversion recursesubdirs createallsubdirs

; ---- Asetusten pohjatiedosto ----
; assets.json asennetaan vain jos kohdetta ei vielä ole (säilyttää käyttäjän muutokset)
Source: "assets.json"; DestDir: "{app}"; Flags: onlyifdoesntexist

; Scenekonfiguraatio – asennetaan vain jos kohdetta ei vielä ole (säilyttää käyttäjän muutokset)
Source: "sowbroadcastscenes.json"; DestDir: "{app}"; Flags: onlyifdoesntexist

; ---- Musiikki ----
Source: "Music\*"; \
  DestDir: "{app}\Music"; \
  Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
; Luodaan käyttäjädatakansiot valmiiksi (sovellus luo ne muutenkin, mutta siistimpää näin)
Name: "{app}\Scoreboard\Match"
Name: "{app}\Scoreboard\Teams"
Name: "{app}\Scoreboard\General"
Name: "{app}\Scoreboard\Waiting"
Name: "{app}\Scoreboard\Replay\Playlist"
Name: "{app}\Scoreboard\Bracket"
Name: "{app}\Scoreboard\Standings"
Name: "{app}\Scoreboard\Temp"
Name: "{app}\Highlights"
Name: "{app}\Videos"

[Icons]
; Käynnistysvalikko
Name: "{group}\{#AppName}"; \
  Filename: "{app}\{#LauncherBat}"; \
  WorkingDir: "{app}"; \
  Comment: "Käynnistä SOWBroadcast"

Name: "{group}\Poista {#AppName}"; \
  Filename: "{uninstallexe}"

; Työpöytäpikakuvake (valinnainen)
Name: "{commondesktop}\{#AppName}"; \
  Filename: "{app}\{#LauncherBat}"; \
  WorkingDir: "{app}"; \
  Comment: "Käynnistä SOWBroadcast"; \
  Tasks: desktopicon

[Run]
; Tarjoa käynnistys asennuksen jälkeen
Filename: "{app}\{#LauncherBat}"; \
  Description: "{cm:LaunchProgram,{#AppName}}"; \
  Flags: nowait postinstall skipifsilent shellexec; \
  WorkingDir: "{app}"

[UninstallRun]
; Tapetaan mahdolliset käynnissä olevat prosessit ennen poistoa
Filename: "taskkill"; Parameters: "/IM {#AppExeName} /F"; Flags: runhidden; RunOnceId: "KillGUI"
Filename: "taskkill"; Parameters: "/IM {#ServerExe} /F";  Flags: runhidden; RunOnceId: "KillServer"

[UninstallDelete]
; Poistetaan ajonaikaiset väliaikaistiedostot – käyttäjädata (Teams, Match jne.) jätetään
Type: filesandordirs; Name: "{app}\Scoreboard\Temp"
Type: filesandordirs; Name: "{app}\__pycache__"
