"""Makes a copy of the shipped Scoreboard images smaller for the installer (the originals in the repository are never touched).

    python tools/optimize-assets.py <Scoreboard folder of the publish output> [--max-body-height 1500]

What it does, in this order:
  1. Removes images the manifest does not list (old spellings and duplicates such as antartic-peninsula.png).
  2. Shrinks full-body hero pictures taller than --max-body-height (default 1500 px: the overlays show them at most 1080 px tall,
     1440 px when OBS scales the scenes up 1.33x, so nothing visible is lost).
  3. Saves every PNG again with maximum lossless compression, keeping the smaller file.
Nothing here changes the look of a picture beyond the shrinking in step 2. Map pictures are only recompressed: they are photographic, and
reducing the colours (which would shrink them a lot) is visible as banding, so it is not done.
Needs Pillow (pip install pillow). tools/build-installer.ps1 runs this when Python and Pillow are there.
"""
import argparse
import json
import os
import sys
from pathlib import Path

from PIL import Image

FOLDERS = ("Heroes", "HeroesBody", "Maps", "Gametypes")
IMAGE_EXTENSIONS = (".png", ".jpg", ".jpeg", ".webp")


def referenced(manifest_path):
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    names = set()
    for key in ("heroes", "maps", "modes"):
        for entry in manifest.get(key, []):
            for field in ("image", "body_image"):
                if entry.get(field):
                    names.add(entry[field].replace("\\", "/").lower())
    return names


def megabytes(count):
    return f"{count / 1_000_000:.1f} MB"


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("scoreboard", help="the Scoreboard folder inside the installer's publish folder")
    parser.add_argument("--max-body-height", type=int, default=1500)
    args = parser.parse_args()

    root = Path(args.scoreboard)
    manifest = root / "manifest.json"
    if not manifest.exists():
        print(f"No manifest.json in {root}: nothing is removed or changed.", file=sys.stderr)
        return 1
    keep = referenced(manifest)

    before_total = after_total = 0
    for folder in FOLDERS:
        directory = root / folder
        if not directory.is_dir():
            continue
        before = after = removed = resized = 0
        for path in sorted(directory.iterdir()):
            if path.suffix.lower() not in IMAGE_EXTENSIONS:
                continue
            size = path.stat().st_size
            before += size
            relative = f"{folder}/{path.name}".lower()
            if relative not in keep:
                path.unlink()
                removed += 1
                continue
            if path.suffix.lower() == ".png":
                with Image.open(path) as opened:
                    image = opened.copy()
                if folder == "HeroesBody" and image.height > args.max_body_height:
                    width = round(image.width * args.max_body_height / image.height)
                    image = image.resize((width, args.max_body_height), Image.LANCZOS)
                    resized += 1
                temp = path.with_suffix(".tmp.png")
                image.save(temp, format="PNG", optimize=True)
                if temp.stat().st_size < size:
                    os.replace(temp, path)
                else:
                    temp.unlink()
                size = path.stat().st_size
            after += size
        print(f"{folder:11} {megabytes(before):>9} -> {megabytes(after):>9}   ({removed} unlisted removed, {resized} shrunk)")
        before_total += before
        after_total += after
    print(f"{'Total':11} {megabytes(before_total):>9} -> {megabytes(after_total):>9}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
