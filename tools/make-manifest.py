"""Builds Scoreboard/manifest.json (the asset manifest, EHD-10) from the three index.json files and the alias rules below.

    python tools/make-manifest.py [--bump] [--out Scoreboard/manifest.json]

Run it after adding or changing a hero, map or game mode in Scoreboard/{Heroes,Maps,Gametypes}/index.json (or edit the manifest by
hand). --bump raises the revision (publish a manifest with a higher revision and every installation takes it on its next update).
Aliases are the spellings other programs use for a name; the app matches them exactly, it does not guess. They are made from the name
(accents removed, punctuation removed, punctuation turned into a space) plus the EXTRA table for names that differ in other ways.
"""
import argparse
import datetime
import json
import re
import unicodedata
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SCOREBOARD = ROOT / "Scoreboard"

# Spellings that cannot be made from the name by a rule.
EXTRA = {
    "heroes": {
        "Torbjorn": ["Torbjörn"],
        "Wrecking Ball": ["Hammond"],
        "Cassidy": ["McCree"],
        "Soldier: 76": ["Soldier 76", "Soldier76"],
        "D.Va": ["Dva", "D Va"],
    },
    "maps": {},
    "modes": {},
}


def variants(name):
    found = set()
    folded = "".join(c for c in unicodedata.normalize("NFKD", name) if unicodedata.category(c) != "Mn")
    for base in {name, folded}:
        found.add(base)
        found.add(re.sub(r"[^\w\s]", "", base))             # punctuation removed: "Kings Row"
        found.add(re.sub(r"[^\w\s]", " ", base))            # punctuation to space: "King S Row"
        found.add(base.replace("'", "’"))               # typographic apostrophe
    clean = {" ".join(v.split()) for v in found}
    clean.discard(name)
    return sorted(v for v in clean if v)


def load(folder, key):
    path = SCOREBOARD / folder / "index.json"
    return json.loads(path.read_text(encoding="utf-8"))[key]


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", default=str(SCOREBOARD / "manifest.json"))
    parser.add_argument("--bump", action="store_true", help="raise the revision by one")
    args = parser.parse_args()

    out = Path(args.out)
    previous = json.loads(out.read_text(encoding="utf-8")) if out.exists() else {}
    revision = int(previous.get("revision", 0)) + (1 if args.bump or not previous else 0)

    manifest = {"schema": 1, "revision": revision, "updated": datetime.date.today().isoformat()}
    for folder, key in (("Heroes", "heroes"), ("Maps", "maps"), ("Gametypes", "modes")):
        entries = []
        for item in load(folder, key):
            entry = {"name": item["name"], "slug": item["slug"]}
            if key == "heroes":
                entry["role"] = item.get("role", "")
            if key == "maps":
                entry["mode"] = item.get("mode", "")
            entry["image"] = item["image"]
            if key == "heroes" and item.get("body_image"):
                entry["body_image"] = item["body_image"]
            aliases = sorted(set(variants(item["name"])) | set(EXTRA[key].get(item["name"], [])))
            aliases = [a for a in aliases if a != item["name"]]
            if aliases:
                entry["aliases"] = aliases
            entries.append(entry)
        manifest[key] = entries

    out.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"{out}: revision {revision}, {len(manifest['heroes'])} heroes, {len(manifest['maps'])} maps, {len(manifest['modes'])} modes")


if __name__ == "__main__":
    main()
