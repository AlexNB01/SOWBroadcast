# SOWBroadcast Wiki

Huge thanks to [BroadcastGG / OW_Community_Modern](https://github.com/BroadcastGG/OW_Community_Modern)!
This project draws inspiration from their fantastic community broadcast package. Many overlay ideas, layout choices, and workflows are based on their work.

SOW Broadcast 3 is a native Windows program (C#, .NET 8, Avalonia) from Suomi OW ry for running Overwatch 2 broadcasts. You enter the match details in the program, and it keeps the overlays in OBS up to date. It has its own built-in overlay server, so there is nothing else to start.

---

# Quick Start

## Install
1. Download **SOWBroadcast-Setup-&lt;version&gt;.exe** from the [latest release](https://github.com/AlexNB01/SOWBroadcast/releases/latest) and run it.
2. Pick the installation folder. The default is `C:\SOW Broadcast`; you can choose any other folder. No administrator rights are needed.
3. Choose **Program and music library** (includes the background music for the OBS scenes) or **Program only**.
4. Start **SOW Broadcast** from the Start menu or the desktop icon. There is no `launch.bat` any more.

Everything the program saves (autosave, settings, logs, generated overlay files, team library, profiles) lives in the **`Data`** folder next to the program. Uninstalling or updating never touches it. Help → *Open program folder* and *Open data folder* open the folders.

Your own files have visible folders next to the program too: `Logos\Team Logos`, `Logos\Broadcast Logos` and `Team Files`.

> Updating: Help → **Check for updates…** downloads the newest installer from GitHub and installs it over the old version (the program closes and starts again). Don't update during a broadcast. *Check for updates when the program starts* is on by default: when a newer version exists, a window offers to install it now or later (it never opens while OBS is streaming or recording). You can switch it off in the Help menu.

## Add the overlays to OBS
1. In SOW Broadcast open **Control → OBS panel…** and press **Export scene collection…**. Save the file.
2. In OBS: **Scene Collection → Import** and choose that file, then switch to the new "SOW Broadcast" collection.
3. Pick your cameras: the camera scenes use NDI sources (`Host`, `Caster 1`, `Caster 2`). Choose the NDI sender for each one in OBS. Replace them with another source type if you don't use NDI.

The exported collection is made for *your* installation: the overlay addresses, folders and port are filled in for you, so it also works when you installed to a different folder.

## Recommended OBS plugins
- [VLC](https://www.videolan.org/vlc/download-windows.html), needed for the music (VLC Video Source).
- [DistroAV / NDI](https://github.com/DistroAV/DistroAV) if your cameras come over NDI.

## Operate
- **Match tab**: teams, abbreviations, logos, colours, players, maps, picks, bans, scores.
- **General tab**: host, casters, logos, status text and the overlay colours.
- **Waiting Screen tab**: countdown, on-screen texts, ticker and social links.
- **Standings** and **Bracket** tabs: tables and the tournament bracket.
- Press the tab's **Update** button to send changes to the overlays (see below).
- **Swap Teams** flips the teams (scores, players, map picks, bans). **Reset** clears the tab.

## The "Unsent changes" marker
**Only the Update buttons change what viewers see.** Editing a field, Swap Teams, Reset, loading a file or importing a team only saves your work and lights up the *Unsent changes* marker in the title bar until you press Update. Each tab has its own Update button (Match, General, Waiting, Standings, Bracket), and each sends only its own part.

The exceptions, which update the overlays straight away: data that arrives from the SOWDraft link, the waiting screen countdown buttons, a finished replay clip, and the program's start-up (it shows the last saved state).

---

# Link to SOWDraft

If the tournament runs on [SOWDraft](https://sowdraft.fi), the program can fill the match for you.

1. On the **Match** tab paste the **tournament address** (`https://sowdraft.fi/tournament/...`) into the link box and press **Link**.
2. Choose the match from the **Match** list. You always choose the match yourself; nothing is picked for you. Matches in play are at the top, then the upcoming ones, finished matches last (they are hidden while another match is in play).
3. The teams (name, abbreviation, colour, logo), players with their roles and heroes, the maps, picks, bans and results fill in and are sent to the overlays at once.
4. If the teams are on the wrong sides, switch **Swap sides** (the Swap Teams button does the same while linked).

Good to know:
- A field is filled in only when SOWDraft's value *changes*, so a manual correction you made stays until SOWDraft changes that value.
- Standings and Bracket fill in from the tournament and are read-only until you **Unlink**. Bracket round titles are the round names only ("Upper Semifinals").
- The status text on the General tab is taken from the match (round name and so on).
- Unknown maps or heroes keep their name and a warning appears in the status line. Add them in *Customize*.
- The link is restored when the program starts.
- Advanced → *link a draft room manually* accepts a single draft room address (`.../stream?matchId=...`).

---

# Overlays

All overlays are HTML pages served by the program (`http://127.0.0.1:8324/HTML/...`). If port 8324 is busy the program takes the next free one; the exported OBS collection uses the right one. Each overlay animates in and out when OBS switches scenes.

You can look at any overlay without OBS from the **Preview** menu: it opens the page in your browser, and **All overlays at once** shows them all live as small tiles.

| Overlay | Page | Scene |
|---|---|---|
| In-game scoreboard | `scoreboard.html` | In Game |
| Map draft | `MapDraft.html` | MapDraft |
| Maps | `maps.html` | Maps |
| Team rosters | `team.html?side=1` / `?side=2` | Team1 / Team2 |
| Waiting screens | `waiting.html?mode=start` / `brb` / `end` | Starting Soon / Be Right Back / Thank You for Watching |
| Standings | `standings.html` | Standings |
| Bracket | `bracket.html` | Bracket |
| Camera rows | `singlecam.html`, `duorow.html`, `triorow.html` | camera scenes |
| Instant replay | `replay.html?mode=instant` | InstantReplay |
| Replay clips | `replay.html?mode=clips` | Replay Clips |
| Transition | `transition.html` | on top of every scene |

## Scoreboard
- Team plates with logo, name and team colour (defaults `#55aaff` and `#ff557f`), banned hero portraits, score strip.
- The compact map strip at the top shows `{logo} {ABBR} WIN` for played maps and `PICK` for the current one. The number of maps comes from **Maps** on the Match tab.

## Team rosters
- Players 1–5 as the main roster; players 6–8 appear as smaller *Subs* when they have a name. Each player shows role and hero.

## Camera rows
- `singlecam`, `duorow` and `triorow` show host and casters with the status text and the score.
- In the OBS collection the camera scenes hold a full-canvas camera source, and the Single, Duo and Trio scenes place the cameras into the windows of these pages. The names come from **Host**, **Caster 1** and **Caster 2** on the General tab (`Name @handle` is understood).

## Waiting screens
- Main text, overlay logo and the countdown on the left (the end screen has no countdown), a video area on the right, a scrolling ticker below it and an optional socials bar.
- Videos come from the **Videos** folder you choose on the Waiting Screen tab (default `Data\Highlights`). Suomi OW highlights can be downloaded [here](https://drive.google.com/drive/folders/17rejGGozPrFVTmtVFBdsPO31HZsTpOE_?usp=sharing).

## Map draft and Maps
- **Map draft** shows the current map (and the banned hero) full screen. If a video named like the map exists as `Maps/<map>.mp4` it plays instead of the picture.
- **Maps** is the series overview: up to 7 maps with scores, bans, winners and the next map.

## Standings
- Shows the group tables. The team column keeps room for the team names; the extended columns (played, drawn, for, against) are used when the table is wide enough. Qualified and eliminated teams get a green or red stripe.

## Bracket
- Single and double elimination, drawn from the **Bracket** tab. With byes (for example 6 teams in an 8-team bracket) the first-round matches stand in line with the matches they feed.

## Transition
- A full-frame wipe with your transition logo, in the colours of the two last colour slots.

---

# Program reference

## Match tab
- **Maps** and **Players** (top bar): how many map cards and player rows are shown. Hidden data is kept.
- **Team 1 / Team 2**: *Name*, *Abbreviation* (up to 6 characters), *Score*, *Team Colour*, *Load Logo…* (png, jpg or webp; opens in `Logos\Team Logos`) and 8 player rows (name, role, hero). Choosing a hero sets the role for you. The hero, map and ban lists are searchable: type to filter, arrow keys and Enter to choose.
- **Map cards** (7): pick (TBA / Team 1 / Team 2), map, scores, *Completed*, and a ban for each team.
- **Buttons**: **Update**, **Swap Teams**, **Reset**. Reset keeps the format bar, the General settings and the link.

## General tab
- *Host*, *Caster 1*, *Caster 2*, *Status text*.
- *Overlay logo* and *Transition logo* (open in `Logos\Broadcast Logos`).
- **Overlay colours**, 8 slots:
  - Primary: background behind texts.
  - Secondary: main text colour.
  - Tertiary: accents, scores, "vs".
  - Quaternary: background behind scores outside the game scenes.
  - Quinary and Senary: label and message text (bracket, away, social).
  - Septenary and Octonary: the two colours of the transition wipe.
- **Update (General)** and **Reset this tab**.

## Waiting Screen tab
- *Videos*: the folder of looping videos, *Use default* for `Data\Highlights`.
- **Countdown**: **−30s / −1m / −3m / −5m** and **+30s / +1m / +3m / +5m**, **Start**, **Pause**, **Reset**. It is exact (no drifting) and the overlays count by themselves. The buttons act immediately.
- *Texts*: Starting Soon, Be Right Back, End screen.
- *Ticker*: write your own ticker text instead of the match text.
- *Socials*: Twitch, Twitter/X, YouTube, Instagram, Discord, Website. Paste a full address or a handle; the program cleans it.
- **Update (Waiting)** and **Reset this tab**.

## Standings tab
- Add groups and rows: rank, team, abbreviation, W, L, +/−, points, status (qualified / eliminated) and logo.
- *Show in standings.html* chooses one group or all of them.
- **Update + sort** removes empty rows, sorts by points, map difference and name, and ranks the rows.

## Bracket tab
- Choose a *Template* (4, 6 or 8 teams, single or double elimination) and **Load template**, then pick the teams for each match from the team list and set the scores. **Import from Standings** fills the team list. Single elimination can have a bronze match; a double elimination bracket can show the upper or the lower bracket.
- **Update (Bracket)** and **Reset this tab**.
- Optional automations are in *Control → Hotkeys, remote control and automation…* (see below).

## Menus
- **File**: Load, Save, Save As, **Restore previous match…** (the program archives matches automatically), **Profiles…**.
- **Edit**: Undo / Redo (Ctrl+Z / Ctrl+Y).
- **Customize**: **Manage Heroes / Maps / Game Modes**, **Bulk Import from Folders**, **Update hero and map list…** (downloads new heroes and maps from the latest release).
- **Preview**: open any overlay in the browser.
- **Control**: **OBS panel…**, **Hotkeys, remote control and automation…**.
- **Teams**: **Team library…**, *Keep Home / Away in the library*, **Import SOWDraft teams**, and **Team files** for exporting and importing `.sowteam.json` files.
- **Help**: About, updates, log, program and data folders.

## Team library and files
- The team library (**Teams → Team library…**) keeps teams with their name, abbreviation, colour, logo and players, and you can search it. Double-click a team to load it as Home or Away (the score stays).
- **Import SOWDraft teams** (when a tournament is linked) brings in all the tournament's teams with players and heroes.
- Team files (`.sowteam.json`) are for sharing teams between computers. They don't hold colour overrides, scores or bans.

## Profiles
- **File → Profiles…** saves the whole event set-up (General, Waiting, Standings, Bracket, the tournament address and the match) under a name. *Apply settings only* changes just the event settings; *Apply whole match* also brings the saved match and archives the current one first.

---

# OBS control, hotkeys and automation

## OBS panel (Control → OBS panel…)
Connects to OBS over its built-in WebSocket (OBS 28 or newer): switch scenes with buttons, start or stop the replay buffer, save a replay, and run the **Go-live check**. The check goes through the connection, the overlay server, the scenes, that every browser source points to this program, the replay buffer and OBS's recording folder.

In OBS: **Tools → WebSocket Server Settings → Enable WebSocket server** (note the port and password). The password is stored encrypted for your Windows user. *Connect automatically when the program starts* is optional.

## Hotkeys
These work while OBS or the game has the focus (**Control → Hotkeys…**; each can be changed):

| Hotkey | Action |
|---|---|
| Ctrl+Alt+F1 / F2 | Team 1 score +1 / −1 |
| Ctrl+Alt+F3 / F4 | Team 2 score +1 / −1 |
| Ctrl+Alt+F5 | Finish the current map |
| Ctrl+Alt+F6 | Swap teams |
| Ctrl+Alt+F7 / F8 | Countdown start-pause / reset |
| Ctrl+Alt+F9 | **Update** |
| Ctrl+Alt+Z / Y | Undo / Redo |
| Ctrl+Alt+R | OBS: save the replay |

Score, swap and map keys change the screen only; press the **Update** key to send them (the countdown keys act at once).

## Stream Deck and other remote control
The program answers simple web addresses (use the token shown in the Hotkeys window):

`http://127.0.0.1:8324/api/control/team1.score.up?token=...`

`GET /api/control` lists all commands. Add `&publish=1` to send an Update straight after the command. The answer is JSON with the new scores and whether anything is unsent; **409** means the command couldn't be done (for example the score is already 0). Others: `team2.score.down`, `map.next`, `swap`, `timer.toggle`, `timer.reset`, `update`, `undo`, `redo`, `obs.replay.save`, `obs.scene?name=Scene name`.

## Automations (all off by default)
- *Team score = maps won*.
- *Finish a map when a team has the points that win it* (per game mode).
- *Bracket: move the winner to the next round*.

---

# Replay

1. In OBS turn the **Replay Buffer** on and set the recording folder to the program's replay folder: `<program folder>\Data\Scoreboard\Replay`.
2. Save the clip as `Replay Replay.mp4` in that folder (press **Ctrl+Alt+R**, or use the OBS panel).
3. SOW Broadcast copies it to `Replay\Playlist\{n}-Replay-Replay.mp4` (counting up) and the **InstantReplay** scene (`replay.html?mode=instant`) loops the newest one. The overlay is sent immediately and the program keeps the latest 25 clips.

Keep the replay browser source always loaded for instant playback.

**Replay Clips scene.** The Replay tab's *Show … on stream* buttons play a clip, the newest clip or a whole category once in the **Replay Clips** scene (`replay.html?mode=clips`); the page is empty when they have played. The **ON STREAM** card on the tab (and a pill in the header, on every tab) counts down how long the clips still last, so you can change scene in time. It is an estimate to the second. The InstantReplay scene ignores these requests. The Go-live check warns you if the recording folder is wrong.

Correct OBS settings:

Output → Replay Buffer
<img width="787" height="105" alt="image" src="https://github.com/user-attachments/assets/89fb3832-e9c5-4e18-9600-167538baac27" />

Output → Recording
<img width="769" height="147" alt="image" src="https://github.com/user-attachments/assets/b246dfd2-b8eb-47a6-9a18-037faa230162" />

Advanced → Recording
<img width="769" height="189" alt="image" src="https://github.com/user-attachments/assets/b0123bb3-cd6a-4962-9580-6cf9eee2e9ec" />

---

# Troubleshooting

- **An overlay doesn't change**: press the right **Update** button (check the *Unsent changes* marker). In OBS, right-click the browser source → *Refresh* if you changed scenes while the program was closed.
- **The overlays are blank in OBS**: the program must be running. If port 8324 was busy when the program started, it uses the next free port and the old browser source addresses no longer match: export the scene collection again from the OBS panel (the Go-live check shows which sources point to the wrong address).
- **Something is wrong**: Help → *Open log folder*. The logs have no player or match data. If the program closed unexpectedly, the next start tells you where the report is.
- **Old scene collection**: scene collections from version 2 don't work with version 3. Export a new one from the OBS panel.
