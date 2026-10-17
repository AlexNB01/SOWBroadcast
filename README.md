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

## License
This project is licensed under the terms of the MIT License. See [LICENSE](LICENSE) for details.
