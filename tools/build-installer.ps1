<#
.SYNOPSIS
    Builds the SOW Broadcast installer (Inno Setup): publish, add the overlay files and the shipped images, pack.

.DESCRIPTION
    Output goes to artifacts\installer: SOWBroadcast-Setup-<version>.exe and its checksum SOWBroadcast-Setup-<version>.exe.sha256
    (what "Check for updates" in the program downloads and verifies).

    The installer lets the user choose the folder (default C:\SOW Broadcast, no administrator rights needed). The program keeps its data
    (autosave, generated overlay files, logs, settings) in the Data folder next to it. Uninstalling leaves Data in place.

    Needs Inno Setup 6 (iscc.exe on the path or in its default folder; winget install JRSoftware.InnoSetup).

    Smaller installer: the shipped pictures are copied and tools/optimize-assets.py shrinks the copies (drops pictures the manifest does not
    list, shrinks oversized full-body hero pictures, recompresses PNGs losslessly). The repository's own pictures are never changed. It runs when
    Python with Pillow is available (otherwise a warning says the installer is bigger); -NoOptimize skips it.

    The music library (22 MB) is an optional component of the installer, taken from the Music folder of the repository; -NoMusic leaves it out.

.EXAMPLE
    .\tools\build-installer.ps1
    .\tools\build-installer.ps1 -NoMusic
#>
param(
    [switch] $NoMusic,
    [switch] $NoPack,
    [switch] $NoOptimize
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\SowBroadcast.App\SowBroadcast.App.csproj'
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'
$output = Join-Path $artifacts 'installer'

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
# Smaller pictures (see the description). Failing here is not fatal: the installer is just bigger.
if (-not $NoOptimize) {
    $python = Get-Command python -ErrorAction SilentlyContinue
    $hasPillow = $python -and ((& python -c "import PIL; print('ok')" 2>$null) -eq 'ok')
    if ($hasPillow) {
        & python (Join-Path $PSScriptRoot 'optimize-assets.py') (Join-Path $publish 'Scoreboard')
        if ($LASTEXITCODE -ne 0) { Write-Warning 'optimize-assets.py failed; the installer keeps the full-size pictures.' }
    }
    else {
        Write-Warning 'Python with Pillow was not found, so the pictures are not shrunk (pip install pillow). The installer will be bigger.'
    }
}
# Generated name lists are rebuilt by the program; only the catalogues (index.json) are shipped.
Get-ChildItem (Join-Path $publish 'Scoreboard') -Recurse -Filter 'index.txt' | Remove-Item -Force

if ($NoPack) { Write-Host "Published to $publish"; return }

# Inno Setup's compiler: on the path, or in its default folders.
$iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) { throw 'Inno Setup 6 was not found (winget install JRSoftware.InnoSetup).' }

if (Test-Path $output) { Remove-Item $output -Recurse -Force }
New-Item -ItemType Directory -Force $output | Out-Null

$arguments = @("/DAppVersion=$version", "/DPublishDir=$publish", "/DOutputDir=$output")
$music = Join-Path $root 'Music'
if (-not $NoMusic) {
    if (Test-Path $music) { $arguments += "/DMusicDir=$music" }
    else { Write-Warning "No Music folder at $music, so the installer has no music component." }
}
$arguments += (Join-Path $PSScriptRoot 'installer\SOWBroadcast.iss')

& $iscc @arguments
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed.' }

$setup = Join-Path $output "SOWBroadcast-Setup-$version.exe"
if (-not (Test-Path $setup)) { throw "The installer was not produced: $setup" }
# The program checks the download against this before it runs it.
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$setup.sha256", "$hash  $(Split-Path -Leaf $setup)`n")

Write-Host ("Done. Installer: {0} ({1:N0} MB)" -f $setup, ((Get-Item $setup).Length / 1MB))
