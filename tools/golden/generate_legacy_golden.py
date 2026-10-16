"""Generate golden files from the legacy (Python/PyQt5) SOWBroadcast app.

Runs the old app headless (Qt offscreen) against a synthetic state, then records what its
exporters write and which change keys its diff reports. The C# rewrite must reproduce these
files (see src/SowBroadcast.Tests/GoldenTests.cs). Re-run only when the legacy contract
is deliberately re-captured:

    python tools/golden/generate_legacy_golden.py

Only synthetic data is used (src/SowBroadcast.Tests/Fixtures).
"""
import copy
import json
import os
import shutil
import sys
import tempfile
from pathlib import Path

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

REPO = Path(__file__).resolve().parents[2]
TESTS = REPO / "src" / "SowBroadcast.Tests"
FIXTURES = TESTS / "Fixtures"
GOLDEN = TESTS / "Golden"

# Text/JSON outputs compared byte for byte. PNGs are compared by size only (encoder differs).
CAPTURE_DIRS = ["Match", "General", "Waiting", "Standings", "Bracket"]
SKIP_NAMES = {"updated_at.txt", "__last_gui_touch.txt"}
TEXT_SUFFIXES = {".txt", ".json"}


def load_input():
    text = (FIXTURES / "legacy_input_full.json").read_text(encoding="utf-8")
    text = text.replace("{FIXTURES}", FIXTURES.as_posix())
    return json.loads(text)


def capture(root: Path, dest: Path):
    if dest.exists():
        # Delete files only: a directory may be in use as some tool's working directory on Windows.
        for old in dest.rglob("*"):
            if old.is_file():
                old.unlink()
    dest.mkdir(parents=True, exist_ok=True)
    manifest = {"png": {}, "absent": []}
    sb = root / "Scoreboard"
    for d in CAPTURE_DIRS:
        base = sb / d
        if not base.exists():
            continue
        for path in sorted(base.rglob("*")):
            if not path.is_file() or path.name in SKIP_NAMES:
                continue
            rel = path.relative_to(sb).as_posix()
            if "Playlist" in rel.split("/"):
                continue
            if path.suffix.lower() in TEXT_SUFFIXES:
                target = dest / "Scoreboard" / rel
                target.parent.mkdir(parents=True, exist_ok=True)
                data = path.read_bytes().replace(FIXTURES.as_posix().encode(), b"{FIXTURES}")
                target.write_bytes(data)
            elif path.suffix.lower() == ".png":
                from PIL import Image
                with Image.open(path) as im:
                    manifest["png"][rel] = [im.width, im.height]
    root_match = root / "match.json"
    if root_match.exists():
        (dest / "match.json").write_bytes(
            root_match.read_bytes().replace(FIXTURES.as_posix().encode(), b"{FIXTURES}"))
    (dest / "manifest.json").write_text(json.dumps(manifest, indent=2, sort_keys=True), encoding="utf-8")


def capture_assets(root: Path, dest: Path):
    """Raw copy of the asset folders (index files and the copied images)."""
    if dest.exists():
        for old in dest.rglob("*"):
            if old.is_file():
                old.unlink()
    dest.mkdir(parents=True, exist_ok=True)
    sb_root = root / "Scoreboard"
    for d in ["Heroes", "HeroesBody", "Maps", "Gametypes"]:
        base = sb_root / d
        if not base.exists():
            continue
        for path in sorted(base.rglob("*")):
            if path.is_file():
                target = dest / d / path.name
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(path.read_bytes())


def main():
    tmp = Path(tempfile.mkdtemp(prefix="sowb-golden-"))
    os.environ["SOWB_ROOT"] = str(tmp)
    sys.path.insert(0, str(REPO))

    import SOWBroadcast as sb  # noqa: E402  (legacy app)

    app = sb.QApplication([])
    win = sb.TournamentApp()
    win.show()  # widgets must be visible, otherwise "current_map" (first visible card) is always null
    app.processEvents()
    notified = []
    win._notify_overlays = lambda keys: notified.append(list(keys))

    # Case "default": a fresh app, first Update.
    win._update()
    capture(tmp, GOLDEN / "default")
    default_state = win._collect_state()

    # Case "full": synthetic match.
    win._apply_state(load_input())
    win._update()
    capture(tmp, GOLDEN / "full")
    full_state = win._collect_state()
    def tokenised(state):
        text = json.dumps({k: v for k, v in state.items() if k != "assets"}, indent=2, ensure_ascii=False)
        return text.replace(FIXTURES.as_posix(), "{FIXTURES}")

    (GOLDEN / "full").joinpath("state.json").write_text(tokenised(full_state), encoding="utf-8")
    (GOLDEN / "default").joinpath("state.json").write_text(tokenised(default_state), encoding="utf-8")

    # Case "assets": exporter of the hero/map/mode catalogues with a synthetic catalogue.
    for name in ["Heroes", "HeroesBody", "Maps", "Gametypes"]:
        shutil.rmtree(tmp / "Scoreboard" / name, ignore_errors=True)
    fx = FIXTURES / "assets"

    def asset(name, src, **kw):
        return sb.Asset(name=name, source_path=str(fx / src), **kw)

    win.heroes = {
        "Ana": asset("Ana", "ana.png", role="Support"),
        "D.Va": asset("D.Va", "dva.png", role="Tank", body_source_path=str(fx / "dva_body.png")),
        "Lúcio": asset("Lúcio", "lucio.png", role="Support", body_source_path=str(fx / "lucio_body.png")),
    }
    win.maps = {
        "Busan": asset("Busan", "busan.png", mode="Control"),
        "King's Row": asset("King's Row", "kingsrow.png", mode="Hybrid"),
        "Esperança": asset("Esperança", "esperanca.png", mode="Push"),
    }
    win.modes = {"Control": asset("Control", "control.png"), "Hybrid": asset("Hybrid", "hybrid.png")}
    win._export_assets_category("Heroes", win.heroes)
    win._export_assets_category("Maps", win.maps)
    win._export_assets_category("Gametypes", win.modes)
    capture_assets(tmp, GOLDEN / "assets")

    # Diff cases: pure function over two collected states.
    def mutate(fn):
        new = copy.deepcopy(full_state)
        fn(new)
        return new

    steps = [
        ("team1 score", lambda s: s["team1"].__setitem__("score", 2)),
        ("team2 name", lambda s: s["team2"].__setitem__("name", "Midnight Sun 2")),
        ("team1 color", lambda s: s["team1"].__setitem__("color_hex", "#112233")),
        ("team1 abbr", lambda s: s["team1"].__setitem__("abbr", "NLX")),
        ("team1 player name", lambda s: s["team1"]["players"][0].__setitem__("name", "Renamed")),
        ("map1 ban", lambda s: s["maps"][0].__setitem__("t1_ban", "Echo")),
        ("map2 score", lambda s: s["maps"][1].__setitem__("t1", 1)),
        ("map2 pick", lambda s: s["maps"][1].__setitem__("pick", "T1")),
        ("map name", lambda s: s["maps"][2].__setitem__("map", "Havana")),
        ("map completed", lambda s: s["maps"][1].__setitem__("completed", True)),
        ("general host", lambda s: s["general"].__setitem__("host", "New Host")),
        ("general caster1", lambda s: s["general"].__setitem__("caster1", "Other")),
        ("general status", lambda s: s["general"].__setitem__("status_text", "Another status")),
        ("general maps_count", lambda s: s["general"].__setitem__("maps_count", 5)),
        ("general colors", lambda s: s["general"]["colors"].__setitem__("primary", "#000000")),
        ("waiting text", lambda s: s["waiting"].__setitem__("text_brb", "BRB 2")),
        ("waiting timer", lambda s: s["waiting"].__setitem__("timer_seconds", 120)),
        ("waiting timer running", lambda s: s["waiting"].__setitem__("timer_running", True)),
        ("waiting socials", lambda s: s["waiting"]["socials"].__setitem__("twitch", "other")),
        ("waiting ticker", lambda s: s["waiting"].__setitem__("ticker_override", "New ticker")),
        ("standings title", lambda s: s["standings"].__setitem__("title", "New title")),
        ("bracket title", lambda s: s["bracket"].__setitem__("title", "New bracket")),
        ("no change", lambda s: None),
    ]
    diffs = {"first_update": list(win._diff_for_scoreboard(None, full_state)), "steps": []}
    for name, fn in steps:
        new = mutate(fn)
        diffs["steps"].append({"name": name, "keys": sorted(win._diff_for_scoreboard(full_state, new))})
    (GOLDEN / "diff_keys.json").write_text(json.dumps(diffs, indent=2, ensure_ascii=False), encoding="utf-8")

    print("golden written to", GOLDEN)
    print("first_update keys:", diffs["first_update"])
    os._exit(0)  # the legacy app starts daemon threads; skip interpreter teardown


if __name__ == "__main__":
    main()
