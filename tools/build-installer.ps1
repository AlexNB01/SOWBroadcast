<#
.SYNOPSIS
    Builds the SOW Broadcast installer (per-user, Velopack): publish, add the overlay files and the shipped images, pack.

.DESCRIPTION
    Output goes to artifacts\releases: Setup.exe (the installer), the .nupkg packages and RELEASES metadata for updates.
    The installer puts the program in %LOCALAPPDATA%\SuomiOW.SOWBroadcast (no administrator rights); the user's data
    (autosave, generated overlay files, logs) lives separately in %LOCALAPPDATA%\SOWBroadcast and survives updates and uninstalling.

    The music library is not part of the installer (size and licence): with -MusicZip a SOWBroadcast-Music.zip is made from the Music folder
    of the repository, to be unpacked into the data folder's Music folder.

.EXAMPLE
    .\tools\build-installer.ps1
    .\tools\build-installer.ps1 -MusicZip
#>
param(
    [switch] $MusicZip,
    [switch] $NoPack
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\SowBroadcast.App\SowBroadcast.App.csproj'
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'
$releases = Join-Path $artifacts 'releases'

# The version lives in one place: <Version> in the project file.
$version = (& dotnet msbuild $project -getProperty:Version -nologo).Trim()
if (-not $version) { throw 'Could not read the version from the project file.' }
Write-Host "Building SOW Broadcast $version"

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
& dotnet publish $project -c Release -r win-x64 --self-contained true -o $publish --nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

# The overlay pages and the shipped catalogue images sit next to the program (AppPaths.ContentRoot finds them there).
Copy-Item (Join-Path $root 'HTML') (Join-Path $publish 'HTML') -Recurse
foreach ($folder in 'Heroes', 'HeroesBody', 'Maps', 'Gametypes', 'Roles', 'General\Icons') {
    $from = Join-Path $root "Scoreboard\$folder"
    if (-not (Test-Path $from)) { throw "Missing $from" }
    $to = Join-Path $publish "Scoreboard\$folder"
    New-Item -ItemType Directory -Force (Split-Path -Parent $to) | Out-Null
    Copy-Item $from $to -Recurse
}
# The asset manifest: every hero, map and game mode with roles, picture files and aliases (a newer one can be fetched later).
$manifest = Join-Path $root 'Scoreboard\manifest.json'
if (-not (Test-Path $manifest)) { throw "Missing $manifest (run tools/make-manifest.py)" }
Copy-Item $manifest (Join-Path $publish 'Scoreboard\manifest.json')
# Generated name lists are rebuilt by the program; only the catalogues (index.json) are shipped.
Get-ChildItem (Join-Path $publish 'Scoreboard') -Recurse -Filter 'index.txt' | Remove-Item -Force

if ($NoPack) { Write-Host "Published to $publish"; return }

# vpk (the Velopack packer) is kept in a tool folder of its own, not installed globally.
$toolDir = Join-Path $artifacts 'tools'
$vpk = Join-Path $toolDir 'vpk.exe'
if (-not (Test-Path $vpk)) {
    & dotnet tool install vpk --tool-path $toolDir
    if ($LASTEXITCODE -ne 0) { throw 'Could not install vpk.' }
}

if (Test-Path $releases) { Remove-Item $releases -Recurse -Force }
& $vpk pack --packId 'SuomiOW.SOWBroadcast' --packVersion $version --packDir $publish --mainExe 'SOWBroadcast.exe' `
    --packTitle 'SOW Broadcast' --runtime win-x64 --packAuthors 'Suomi OW ry' --icon (Join-Path $root 'src\SowBroadcast.App\app.ico') --outputDir $releases
if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed.' }

if ($MusicZip) {
    $music = Join-Path $root 'Music'
    if (-not (Test-Path $music)) { throw "No Music folder at $music" }
    $zip = Join-Path $artifacts 'SOWBroadcast-Music.zip'
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path $music -DestinationPath $zip
    Write-Host "Music pack: $zip"
}

Write-Host "Done. Installer: $(Join-Path $releases 'SuomiOW.SOWBroadcast-win-Setup.exe')"
