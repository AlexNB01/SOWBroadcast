"""Writes an OBS scene collection for the current overlays: python tools/obs/make-obs-scenes.py [options]

    --out FILE          where to write it (default: artifacts/obs/SOW Broadcast.json)
    --base-url URL      the program's server (default http://127.0.0.1:8324; change it if the program had to use another port)
    --install-dir DIR   where the program is installed (music and the camera mask are read from there;
                        default %LOCALAPPDATA%/SuomiOW.SOWBroadcast/current)
    --data-dir DIR      the program's data folder (caster/host text files; default %LOCALAPPDATA%/SOWBroadcast, or SOWB_ROOT)
    --check-only        build in memory, validate, write nothing

Import in OBS with Scene Collection > Import. The starting point is tools/obs/scene-template.json (the 18 scenes of the old
collection); this script points every browser source at the current pages and removes everything that belonged to one machine:
NDI sender names, the webcam, absolute C:/SOWBroadcast paths, the dead localhost:899 address. Pick the NDI senders and the
camera in OBS afterwards.
"""
import argparse
import json
import os
import re
import sys
import uuid
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent

# Old page -> new page (the waiting screens and the rosters are one page each now; mapdraft.html was a case-sensitivity accident).
PAGES = {
    "berightback.html": "waiting.html?mode=brb",
    "startingsoon.html": "waiting.html?mode=start",
    "thankyouforwatching.html": "waiting.html?mode=end",
    "team1.html": "team.html?side=1",
    "team2.html": "team.html?side=2",
    "mapdraft.html": "MapDraft.html",
}

RENAMES = {"Musiikki Highlights": "Highlights Music"}


def slash(path):
    return str(path).replace("\\", "/")


def new_uuid(old):
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "https://suomiow.fi/sowbroadcast/obs/" + old))


def page_of(url):
    """'http://127.0.0.1:8324/HTML/startingsoon.html' -> 'waiting.html?mode=start'. The blank-screen source had a dead address and a lower-case path."""
    name = re.split(r"/html/", url, flags=re.I)[-1]
    return PAGES.get(name.split("?")[0].lower(), name)


def rename(name):
    if name.startswith("v") and name.endswith("ritausta"):
        return "Background Colour"  # "väritausta", which the old file had lost its ä in
    return RENAMES.get(name, name)


def build(template, base_url, install_dir, data_dir):
    base_url = base_url.rstrip("/")
    install_dir = Path(install_dir)
    general = Path(data_dir) / "Scoreboard" / "General"

    sources = template["sources"]
    groups = template["groups"]

    # Sources that belong to one machine (the webcam) go, together with the scene items that show them.
    dropped = {s["name"] for s in sources if s["id"] == "dshow_input"}

    for s in sources:
        settings = s["settings"]
        if s["id"] == "browser_source":
            settings["url"] = f"{base_url}/HTML/{page_of(settings['url'])}"
            settings["width"], settings["height"] = 1920, 1080
        elif s["id"] == "text_gdiplus":
            settings["file"] = slash(general / Path(settings["file"]).name)
        elif s["id"] == "ndi_source":
            settings["ndi_source_name"] = ""
        elif s["id"] == "vlc_source":
            for entry in settings["playlist"]:
                entry["value"] = str(install_dir / "Music")
        elif s["id"] == "image_source":
            settings["file"] = slash(install_dir / "HTML" / "assets" / "imagemask" / Path(settings["file"]).name)

    sources[:] = [s for s in sources if s["name"] not in dropped]
    known = {s["name"] for s in sources} | {g["name"] for g in groups}

    for container in sources + groups:
        items = container["settings"].get("items")
        if items is not None:
            container["settings"]["items"] = [i for i in items if i["name"] in known]
    for container in sources + groups:
        for item in container["settings"].get("items", []):
            item["name"] = rename(item["name"])
        container["name"] = rename(container["name"])

    # Fresh, stable ids so importing this next to the old collection does not collide with it.
    text = json.dumps(template, ensure_ascii=False)
    for old in sorted({s["uuid"] for s in sources + groups}):
        text = text.replace(old, new_uuid(old))
    out = json.loads(text)

    out["name"] = "SOW Broadcast"
    out["current_scene"] = "In Game"
    out["current_program_scene"] = "In Game"
    out["modules"] = {}
    for name in ("scene_order",):
        out[name] = [{"name": rename(o["name"])} for o in out[name]]
    return out


def check(collection, html_dir):
    """Every browser source must point at a page that exists; nothing may carry an old absolute path or a device id."""
    problems = []
    names = {s["name"] for s in collection["sources"]} | {g["name"] for g in collection["groups"]}
    for s in collection["sources"]:
        if s["id"] == "browser_source":
            page = re.search(r"/HTML/([^?]+)", s["settings"]["url"])
            if not page or not (html_dir / page.group(1)).exists():
                problems.append(f"{s['name']}: {s['settings']['url']} is not a page in HTML/")
        if s["id"] == "scene":
            for i in s["settings"]["items"]:
                if i["name"] not in names:
                    problems.append(f"{s['name']}: item {i['name']} has no source")
    blob = json.dumps(collection)
    for bad in ("C:/SOWBroadcast", "SOWBroadcast\\\\Music", "HEXEON", "BRIO", "localhost:899"):
        if bad in blob:
            problems.append(f"leftover {bad}")
    return problems


def main():
    local = os.environ.get("LOCALAPPDATA", str(Path.home()))
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", default=str(REPO / "artifacts" / "obs" / "SOW Broadcast.json"))
    parser.add_argument("--base-url", default="http://127.0.0.1:8324")
    parser.add_argument("--install-dir", default=str(Path(local) / "SuomiOW.SOWBroadcast" / "current"))
    parser.add_argument("--data-dir", default=os.environ.get("SOWB_ROOT") or str(Path(local) / "SOWBroadcast"))
    parser.add_argument("--check-only", action="store_true")
    args = parser.parse_args()

    template = json.loads((HERE / "scene-template.json").read_text(encoding="utf-8"))
    collection = build(template, args.base_url, args.install_dir, args.data_dir)

    problems = check(collection, REPO / "HTML")
    if problems:
        print("\n".join(problems), file=sys.stderr)
        return 1
    scenes = [s["name"] for s in collection["sources"] if s["id"] == "scene"]
    if args.check_only:
        print(f"ok: {len(scenes)} scenes, {len(collection['sources'])} sources")
        return 0
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(collection, ensure_ascii=False, indent=4), encoding="utf-8")
    print(f"{out}: {len(scenes)} scenes, {len(collection['sources'])} sources")
    return 0


if __name__ == "__main__":
    sys.exit(main())
