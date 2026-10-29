# SOW Broadcast

SOW Broadcast is the broadcast control program of Suomi OW ry for Overwatch 2 productions. A native Windows app (C#, .NET 8, Avalonia) for editing match details, with a built-in local server that keeps the HTML overlays (OBS Browser Sources) in sync.

## Features
- Match, General, Waiting Screen, Standings and Bracket tabs; asset manager for heroes, maps and game modes.
- SOWDraft link: pick a tournament match and the teams, maps, picks, bans and scores fill in live.
- Overlays in `HTML/` served from the program itself (`http://127.0.0.1:8324`), updated over Server-Sent Events. Only the Update buttons publish to the overlays.
- OBS WebSocket control, hotkeys and a local HTTP control API (Stream Deck), undo/redo, team library, match profiles.
- Installer with a user-chosen folder (default `C:\SOW Broadcast`); data lives in the `Data` folder next to the program.

## Build
```
dotnet build src/SowBroadcast.sln
dotnet test src/SowBroadcast.sln
powershell -ExecutionPolicy Bypass -File tools/build-installer.ps1   # needs Inno Setup 6; Python + Pillow shrink the images
```
The design and decisions are in [CLAUDE.md](CLAUDE.md). The earlier Python/PyQt5 version is kept in git under the tag `legacy-python-app`.

## Code signing policy
Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).

- **What is signed:** the installer `SOWBroadcast-Setup-<version>.exe` of each release, built from this repository by the GitHub Actions workflow `release.yml`. Only releases built from a tagged commit of this repository are signed.
- **Roles:** author, reviewer and approver of signing requests: [AlexNB01](https://github.com/AlexNB01).
- **Privacy:** the program does not send any information about you or your computer to its authors. It connects to the network only to SOWDraft (`sowdraft.fi`) when you link a tournament, to GitHub when you ask it to check for updates or to update the hero and map list, and to OBS on your own computer.

## License
This project is licensed under the terms of the MIT License. See [LICENSE](LICENSE) for details.
