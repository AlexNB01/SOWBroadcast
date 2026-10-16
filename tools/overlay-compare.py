"""Compares two folders of overlay pictures (tools/overlay-shots.mjs output): the visual regression check.

    python tools/overlay-compare.py <beforeDir> <afterDir> [--pair after=before ...] [--diff <outDir>] [--tolerance 24]

Pictures are matched by file name; --pair NAME=OTHER compares after/NAME.png with before/OTHER.png (for pages that were renamed).
Prints, per picture, how many pixels differ by more than the tolerance (per colour channel) and where. Exit code 1 when any picture
differs by more than 0.05 % of its pixels, so animations that are caught mid-way show up as small, named differences to look at.
"""
import sys
from pathlib import Path

from PIL import Image, ImageChops

args = sys.argv[1:]
if len(args) < 2:
    print(__doc__)
    sys.exit(2)

before_dir, after_dir = Path(args[0]), Path(args[1])
pairs = {}
diff_dir = None
tolerance = 24
rest = args[2:]
i = 0
while i < len(rest):
    if rest[i] == "--pair":
        name, other = rest[i + 1].split("=")
        pairs[name] = other
        i += 2
    elif rest[i] == "--diff":
        diff_dir = Path(rest[i + 1])
        diff_dir.mkdir(parents=True, exist_ok=True)
        i += 2
    elif rest[i] == "--tolerance":
        tolerance = int(rest[i + 1])
        i += 2
    else:
        i += 1

worst = 0.0
for after in sorted(after_dir.glob("*.png")):
    name = after.stem
    before = before_dir / f"{pairs.get(name, name)}.png"
    if not before.exists():
        print(f"{name:22} no picture to compare with ({before.name})")
        continue
    a = Image.open(before).convert("RGB")
    b = Image.open(after).convert("RGB")
    if a.size != b.size:
        print(f"{name:22} different size {a.size} vs {b.size}")
        worst = 1.0
        continue
    diff = ImageChops.difference(a, b)
    mask = diff.point(lambda v: 255 if v > tolerance else 0).convert("L") if False else None
    # A pixel counts as different when any channel differs by more than the tolerance.
    r, g, bl = diff.split()
    biggest = ImageChops.lighter(ImageChops.lighter(r, g), bl)
    flagged = biggest.point(lambda v: 255 if v > tolerance else 0)
    box = flagged.getbbox()
    count = sum(1 for v in flagged.getdata() if v)
    share = count / (a.size[0] * a.size[1])
    worst = max(worst, share)
    where = f" in {box}" if box else ""
    print(f"{name:22} {share * 100:6.3f} % differ ({count} px){where}")
    if diff_dir and box:
        out = Image.new("RGB", (a.size[0] * 3, a.size[1]))
        out.paste(a, (0, 0))
        out.paste(b, (a.size[0], 0))
        out.paste(flagged.convert("RGB"), (a.size[0] * 2, 0))
        out.resize((out.size[0] // 3, out.size[1] // 3)).save(diff_dir / f"{name}.png")

sys.exit(1 if worst > 0.0005 else 0)
