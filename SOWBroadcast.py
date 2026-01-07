import sys, os, json, re, shutil, time, threading, unicodedata, shutil
import server as _sb__force_include
from dataclasses import dataclass, asdict
from typing import Dict, List, Optional

from PyQt5.QtCore import Qt, QStandardPaths, pyqtSignal
from PyQt5.QtGui import QPixmap, QColor
from PyQt5.QtWidgets import (
    QApplication, QMainWindow, QWidget, QVBoxLayout, QHBoxLayout,
    QLabel, QLineEdit, QPushButton, QComboBox, QSpinBox, QCheckBox,
    QAction, QFileDialog, QRadioButton, QGroupBox, QGridLayout, QDialog,
    QFormLayout, QListWidget, QListWidgetItem, QMessageBox, QSplitter,
    QSizePolicy, QColorDialog, QTabWidget, QTreeWidget, QTreeWidgetItem, QScrollArea
)

# -----------------------------
# Data models
# -----------------------------
ROLES = ["Tank", "Damage", "Support", "Flex"]

DEV_ASSET_DIRS = {
    "maps":      r"C:\Suomi OW koodiprojektit\SOWBroadcast\Scoreboard\Maps",
    "gametypes": r"C:\Suomi OW koodiprojektit\SOWBroadcast\Scoreboard\Gametypes",
    "heroes":    r"C:\Suomi OW koodiprojektit\SOWBroadcast\Scoreboard\Heroes",
}

def _bundled_scoreboard_dir():
    """
    Palauta asennuksen mukana tulleen Scoreboard-puun sijainti:
    <app_base>/SOWBroadcast/Scoreboard
    """
    base = os.environ.get("SOWB_ROOT") or _app_base()
    cand = os.path.join(base, "SOWBroadcast", "Scoreboard")
    return cand if os.path.isdir(cand) else None

def _copy_tree_if_missing(src_dir: str, dst_dir: str):
    """
    Kopioi src_dir -> dst_dir vain puuttuvat tiedostot/alihakemistot.
    Ei ylikirjoita olemassa olevia (säästää käyttäjän muokkaukset).
    """
    if not (src_dir and os.path.isdir(src_dir)):
        return
    os.makedirs(dst_dir, exist_ok=True)
    for root, dirs, files in os.walk(src_dir):
        rel = os.path.relpath(root, src_dir)
        out_root = os.path.join(dst_dir, rel) if rel != "." else dst_dir
        os.makedirs(out_root, exist_ok=True)
        for d in dirs:
            os.makedirs(os.path.join(out_root, d), exist_ok=True)
        for f in files:
            s = os.path.join(root, f)
            d = os.path.join(out_root, f)
            if not os.path.exists(d):
                try:
                    shutil.copy2(s, d)
                except Exception:
                    pass


@dataclass
class Asset:
    name: str
    image_path: Optional[str] = None
    mode: Optional[str] = None
    source_path: Optional[str] = None

@dataclass
class Player:
    name: str = ""
    hero: str = ""
    role: str = ""

@dataclass
class Team:
    name: str = ""
    abbr: str = ""
    logo_path: Optional[str] = None
    score: int = 0
    color_hex: str = "#FFFFFF"
    players: List[Player] = None
    banned_hero: str = ""

    def __post_init__(self):
        if self.players is None:
            self.players = [Player() for _ in range(8)]

# ---- Brackets data ----
@dataclass
class BracketTeam:
    name: str = ""
    abbr: str = ""
    logo_path: Optional[str] = None
    group: str = ""

@dataclass
class BracketMatch:
    id: int
    round: int
    slot: int
    teamA: Optional[int] = None
    teamB: Optional[int] = None
    scoreA: int = 0
    scoreB: int = 0
    winnerToMatchId: Optional[int] = None
    loserToMatchId: Optional[int] = None
    winnerToSlot: Optional[str] = None
    loserToSlot: Optional[str] = None
    bracket: str = ""
    group: Optional[str] = None
    seedA: Optional[int] = None
    seedB: Optional[int] = None

@dataclass
class BracketSettings:
    bracket_type: str = "4 team single elimination"
    team_count: int = 4
    display_stage: str = "All"
    display_group: str = "All"
    display_round: str = "All"

@dataclass
class BracketState:
    settings: BracketSettings
    teams: List[BracketTeam]
    matches: List[BracketMatch]

# ---- General tab data ----
@dataclass
class GeneralSettings:
    first_to: int = 3
    host: str = ""
    caster1: str = ""
    caster2: str = ""
    status_text: str = ""
    overlay_logo_path: Optional[str] = None
    transition_logo_path: Optional[str] = None
    colors: Dict[str, str] = None

    def __post_init__(self):
        if self.colors is None:
                self.colors = {
                    "primary":   "#FFFFFF",  
                    "secondary": "#000000",  
                    "tertiary":  "#55aaff",  
                    "quaternary":"#006ea1",  
                    "quinary":   "#FFFFFF",  
                    "senary":    "#FFFFFF",  
                    "septenary": "#FFFFFF",  
                    "octonary":  "#006ea1",  
                }

@dataclass
class WaitingSettings:
    videos_dir: str = ""         
    timer_seconds: int = 0
    text_starting: str = "STARTING SOON!"
    text_brb: str = "BE RIGHT BACK!"
    text_end: str = "THANK YOU FOR WATCHING"
    timer_running: bool = False
    socials: Dict[str, str] = None
    ticker_override: str = ""
    ticker_override_enabled: bool = False

# -----------------------------
# Asset Manager Dialog
# -----------------------------
class AssetManagerDialog(QDialog):
    def __init__(self, parent, title: str, assets: Dict[str, Asset], mode_names: Optional[List[str]] = None):
        super().__init__(parent)
        self._last_state_for_diff = None
        self.setWindowTitle(title)
        self.title = title
        self.assets = assets
        self._mode_names = mode_names or []

        self.resize(700, 420)

        root = QHBoxLayout(self)

        self.listw = QListWidget()
        self.listw.itemSelectionChanged.connect(self._on_select)
        root.addWidget(self.listw, 2)

        right = QVBoxLayout()
        form = QFormLayout()
        self.name_edit = QLineEdit()
        form.addRow("Name", self.name_edit)
        self.mode_combo = None
        if self.title == "Maps":
            self.mode_combo = QComboBox()
            for n in sorted(self._mode_names):
                self.mode_combo.addItem(n)
            form.addRow("Mode", self.mode_combo)


        logo_row = QHBoxLayout()
        self.logo_edit = QLineEdit(); self.logo_edit.setReadOnly(True)
        browse = QPushButton("Browse…")
        browse.clicked.connect(self._browse_image)
        logo_row.addWidget(self.logo_edit)
        logo_row.addWidget(browse)
        form.addRow("Logo", logo_row)
        right.addLayout(form)

        self.preview = QLabel("No Image")
        self.preview.setAlignment(Qt.AlignCenter)
        self.preview.setFixedHeight(180)
        self.preview.setStyleSheet("QLabel{border:1px solid #CCC;border-radius:8px;background:#FAFAFA}")
        right.addWidget(self.preview)

        btns = QHBoxLayout()
        self.add_btn = QPushButton("Add / Update")
        self.add_btn.clicked.connect(self._add_or_update)
        self.del_btn = QPushButton("Delete")
        self.del_btn.clicked.connect(self._delete)
        btns.addWidget(self.add_btn)
        btns.addWidget(self.del_btn)
        right.addLayout(btns)

        root.addLayout(right, 3)

        self._reload()

    def _reload(self):
        self.listw.clear()
        for name in sorted(self.assets.keys()):
            self.listw.addItem(name)

    def _on_select(self):
        items = self.listw.selectedItems()
        if not items:
            return
        name = items[0].text()
        asset = self.assets.get(name)
        if asset:
            p = asset.source_path or asset.image_path or ""
            self.logo_edit.setText(p)
            self._load_preview(p)
            if self.title == "Maps" and self.mode_combo:
                ix = self.mode_combo.findText(asset.mode or "", Qt.MatchExactly)
                self.mode_combo.setCurrentIndex(ix if ix >= 0 else 0)


    def _browse_image(self):
        path, _ = QFileDialog.getOpenFileName(self, "Select image", "", "Images (*.png *.jpg *.jpeg *.webp)")
        if path:
            self.logo_edit.setText(path)
            self._load_preview(path)

    def _load_preview(self, path: Optional[str]):
        if path:
            pix = QPixmap(path)
            if not pix.isNull():
                self.preview.setPixmap(pix.scaled(self.preview.width(), self.preview.height(), Qt.KeepAspectRatio, Qt.SmoothTransformation))
                return
        self.preview.setText("No Image")
        self.preview.setPixmap(QPixmap())

    def _add_or_update(self):
        name = self.name_edit.text().strip()
        if not name:
            QMessageBox.warning(self, "Missing name", "Please enter a name.")
            return
        mode = None
        if self.title == "Maps" and self.mode_combo:
            mode = self.mode_combo.currentText().strip()
        slug = TournamentApp._slugify(name)

        if self.title == "Heroes":
            rel_dir = os.path.join("Scoreboard", "Heroes")
        elif self.title == "Game Modes":
            rel_dir = os.path.join("Scoreboard", "Gametypes")
        else:
            rel_dir = os.path.join("Scoreboard", "Maps")

        image_path = os.path.join(rel_dir, f"{slug}.png")

        source_path = self.logo_edit.text().strip() or None

        self.assets[name] = Asset(
            name=name,
            image_path=image_path,
            mode=mode,
            source_path=source_path
        )
        self._reload()
        matches = self.listw.findItems(name, Qt.MatchExactly)
        if matches:
            self.listw.setCurrentItem(matches[0])

    def _delete(self):
        items = self.listw.selectedItems()
        if not items:
            return
        name = items[0].text()
        if name not in self.assets:
            return

        asset = self.assets.pop(name, None)

        try:
            if self.title == "Heroes":
                category = "Heroes"
            elif self.title in ("Game Modes", "Gametypes"):
                category = "Gametypes"
            else:
                category = "Maps"

            slug = type(self.parent())._slugify(name)
            root = self.parent()._scoreboard_root()
            png_path = os.path.join(root, category, f"{slug}.png")
            if os.path.isfile(png_path):
                os.remove(png_path)
        except Exception:
            pass
        try:
            self.parent()._export_assets_category(category, self.assets)
        except Exception:
            pass

        self._reload()
        self.name_edit.clear()
        self.logo_edit.clear()
        self._load_preview(None)

# -----------------------------
# Team Panel
# -----------------------------
class PlayerRow(QWidget):
    def __init__(self, index: int, get_hero_names):
        super().__init__()
        self.get_hero_names = get_hero_names
        row = QHBoxLayout(self)
        row.setContentsMargins(0, 0, 0, 0)
        self.label = QLabel(f"Player {index}")
        self.name = QLineEdit(); self.name.setPlaceholderText("Name")
        self.hero = QComboBox(); self.refresh_heroes()
        self.role = QComboBox()
        self.role.addItem("- Role -")
        self.role.addItems(ROLES)
        row.addWidget(self.label)
        row.addWidget(self.name, 2)
        row.addWidget(self.hero, 2)
        row.addWidget(self.role, 1)

    def refresh_heroes(self):
        current = self.hero.currentText() if hasattr(self, 'hero') else ""
        self.hero.clear()
        self.hero.addItem("— Hero —")
        self.hero.addItems(self.get_hero_names())
        if current:
            ix = self.hero.findText(current)
            if ix >= 0:
                self.hero.setCurrentIndex(ix)

class TeamPanel(QGroupBox):
    def __init__(self, title: str, get_hero_names, default_color: str = "#FFFFFF"):
        super().__init__(title)
        self.get_hero_names = get_hero_names
        lay = QVBoxLayout(self)

        top = QHBoxLayout()
        self.team_name = QLineEdit(); self.team_name.setPlaceholderText("Team name")
        self.team_abbr = QLineEdit(); self.team_abbr.setPlaceholderText("ABC")
        self.team_abbr.setMaxLength(6)
        self.score = QSpinBox(); self.score.setRange(0, 200)
        self.logo_preview = QLabel(); self.logo_preview.setFixedSize(120, 120)
        self.logo_preview.setStyleSheet("QLabel{border:1px solid #DDD;border-radius:8px;background:#FFF}")
        self.logo_preview.setAlignment(Qt.AlignCenter)
        self.logo_btn = QPushButton("Load Logo…")
        self.logo_btn.clicked.connect(self._select_logo)

        self.default_color = default_color
        self.color_hex = default_color
        self.color_btn = QPushButton("Color")
        self.color_btn.clicked.connect(self._pick_color)

        left = QVBoxLayout()
        left.addWidget(QLabel("Name"))
        left.addWidget(self.team_name)
        left.addWidget(QLabel("Abbreviation"))          
        left.addWidget(self.team_abbr)                   

        score_row = QHBoxLayout(); score_row.addWidget(QLabel("Score")); score_row.addWidget(self.score)
        color_row = QHBoxLayout(); color_row.addWidget(QLabel("Team Color")); color_row.addWidget(self.color_btn)
        left.addLayout(score_row)
        left.addLayout(color_row)
        left.addWidget(self.logo_btn)

        top.addLayout(left, 2)
        top.addWidget(self.logo_preview, 1)
        lay.addLayout(top)

        grid = QVBoxLayout()
        self.player_rows: List[PlayerRow] = []
        for i in range(1, 9):
            pr = PlayerRow(i, self.get_hero_names)
            self.player_rows.append(pr)
            grid.addWidget(pr)
        lay.addLayout(grid)

        spacer = QWidget(); spacer.setSizePolicy(QSizePolicy.Expanding, QSizePolicy.Expanding)
        lay.addWidget(spacer)

        self.logo_path: Optional[str] = None

    def _apply_color_style(self):
        self.color_btn.setStyleSheet(f"QPushButton{{border:1px solid #CCC; border-radius:6px; padding:6px; background:{self.color_hex};}}")

    def _pick_color(self):
        color = QColorDialog.getColor(QColor(self.color_hex), self, "Pick Team Color")
        if color.isValid():
            self.color_hex = color.name(QColor.HexRgb)
            self._apply_color_style()

    def _select_logo(self):
        base = os.environ.get("SOWB_ROOT") or _app_base()
        start_dir = os.path.join(base, "Scoreboard", "Temp", "Team Logos")
        os.makedirs(start_dir, exist_ok=True)

        path, _ = QFileDialog.getOpenFileName(
            self,
            "Valitse tiimin logo",
            start_dir,
            "Images (*.png *.jpg *.jpeg *.webp *.svg)"
        )
        if path:
            self.logo_path = path
            pix = QPixmap(path)
            if not pix.isNull():
                self.logo_preview.setPixmap(
                    pix.scaled(self.logo_preview.size(), Qt.KeepAspectRatio, Qt.SmoothTransformation)
                )

    def to_team(self) -> Team:
        t = Team()
        t.name = self.team_name.text().strip()
        t.abbr = self.team_abbr.text().strip()
        t.logo_path = self.logo_path
        t.score = self.score.value()
        t.color_hex = self.color_hex

        t.players = []
        for pr in self.player_rows:
            p = Player(
                name=pr.name.text().strip(),
                hero=pr.hero.currentText(),
                role=pr.role.currentText()
            )
            if p.hero == "— Hero —":
                p.hero = ""
            if p.role == "- Role -":
                p.role = ""
            t.players.append(p)
        return t

    def from_team(self, t: Team):
        self.team_name.setText(t.name)
        self.team_abbr.setText(getattr(t, "abbr", "") or "")
        self.logo_path = t.logo_path
        if t.logo_path:
            pix = QPixmap(t.logo_path)
            if not pix.isNull():
                self.logo_preview.setPixmap(pix.scaled(self.logo_preview.width(), self.logo_preview.height(), Qt.KeepAspectRatio, Qt.SmoothTransformation))
        else:
            self.logo_preview.clear()
        self.score.setValue(t.score)
        self.color_hex = t.color_hex or getattr(self, "default_color", "#FFFFFF")
        self._apply_color_style()
        for pr, pdata in zip(self.player_rows, t.players + [Player()] * (8 - len(t.players))):
            pr.name.setText(pdata.name)
            pr.refresh_heroes()
            if pdata.hero:
                ix = pr.hero.findText(pdata.hero)
                pr.hero.setCurrentIndex(ix if ix >= 0 else 0)
            else:
                pr.hero.setCurrentIndex(0)
            if pdata.role:
                ixr = pr.role.findText(pdata.role)
                pr.role.setCurrentIndex(ixr if ixr >= 0 else 0)
            else:
                pr.role.setCurrentIndex(0)

    def refresh_hero_lists(self):
        for pr in self.player_rows:
            pr.refresh_heroes()
    
    def reset(self):
        self.team_name.clear()
        self.team_abbr.clear()
        self.score.setValue(0)
        self.logo_path = None
        self.logo_preview.clear()
        for pr in self.player_rows:
            pr.name.clear(); pr.hero.setCurrentIndex(0); pr.role.setCurrentIndex(0)

# -----------------------------
# Map rows
# -----------------------------
class MapRow(QWidget):
    def __init__(self, index: int, get_map_names, get_hero_names):
        super().__init__()
        self.get_map_names = get_map_names
        self.get_hero_names = get_hero_names
        self.index = index

        row = QHBoxLayout(self)
        row.setContentsMargins(0, 0, 0, 0)

        self.label = QLabel(f"Map {index}")

        self.map_combo = QComboBox(); self.refresh_maps()
        self.t1score = QSpinBox(); self.t1score.setRange(0, 200)
        self.t2score = QSpinBox(); self.t2score.setRange(0, 200)

        self.pick = QComboBox()
        self.pick.addItems(["—", "T1", "T2"])

        self.completed = QCheckBox("Completed")

        self.t1ban = QComboBox(); self.refresh_hero_list(self.t1ban)
        self.t2ban = QComboBox(); self.refresh_hero_list(self.t2ban)

        row.addWidget(self.label)
        row.addWidget(self.map_combo, 2)
        row.addWidget(self.t1score)
        row.addWidget(QLabel("-"))
        row.addWidget(self.t2score)
        row.addWidget(QLabel("Pick"))
        row.addWidget(self.pick)
        row.addWidget(QLabel("T1 Ban"))
        row.addWidget(self.t1ban, 2)
        row.addWidget(QLabel("T2 Ban"))
        row.addWidget(self.t2ban, 2)
        row.addWidget(self.completed)

    def refresh_maps(self):
        current = self.map_combo.currentText() if hasattr(self, "map_combo") else ""
        self.map_combo.clear()
        self.map_combo.addItem("")
        for name in sorted(self.get_map_names() or []):
            self.map_combo.addItem(name)
        if current:
            ix = self.map_combo.findText(current)
            self.map_combo.setCurrentIndex(ix if ix >= 0 else 0)

    def refresh_hero_list(self, combo: QComboBox):
        current = combo.currentText() if combo.count() > 0 else ""
        combo.clear()
        combo.addItem("— Hero —")
        for h in sorted(self.get_hero_names() or []):
            combo.addItem(h)
        if current:
            ix = combo.findText(current)
            combo.setCurrentIndex(ix if ix >= 0 else 0)


    def reset(self):
        self.map_combo.setCurrentIndex(0)
        self.t1score.setValue(0)
        self.t2score.setValue(0)
        self.completed.setChecked(False)
        self.pick.setCurrentIndex(0)
        self.t1ban.setCurrentIndex(0)
        self.t2ban.setCurrentIndex(0)

        
class GeneralTab(QWidget):
    updated = pyqtSignal()
    COLOR_FIELDS = [
        ("primary",    "Primary – Background color behind all text"),
        ("secondary",  "Secondary – Color of most text"),
        ("tertiary",   "Tertiary – Color of accents, score, and behind “vs” in non in-game scenes"),
        ("quaternary", "Quaternary – Background behind scores for non in-game scenes"),
        ("quinary",    "Quinary – Text color for matchup labels in bracket scenes, “Playoffs” text, Away Screen match labels, and social media font color"),
        ("senary",     "Senary – Font color for the Message on the Away Screen and Bracket Scenes"),
        ("septenary",  "Septenary – Primary background color during the stinger transitions"),
        ("octonary",   "Octonary – Secondary (trailing) background color during the stinger transitions"),
    ]
    
    def _emit_update(self):
        self.updated.emit()

    def __init__(self):
        super().__init__()
        root = QVBoxLayout(self)

        bo_box = QGroupBox("Number of Maps")
        bo_lay = QHBoxLayout(bo_box)
        self.maps_count = QSpinBox()
        self.maps_count.setRange(1, 7)
        self.maps_count.setValue(3)
        bo_lay.addWidget(QLabel("Maps:"))
        bo_lay.addWidget(self.maps_count)
        bo_lay.addStretch(1)
        root.addWidget(bo_box)

        people_box = QGroupBox("Casters & Host")
        people = QGridLayout(people_box)
        self.host = QLineEdit()
        self.caster1 = QLineEdit()
        self.caster2 = QLineEdit()
        people.addWidget(QLabel("Host name:"), 0, 0);     people.addWidget(self.host,   0, 1)
        people.addWidget(QLabel("Caster 1:"), 1, 0);     people.addWidget(self.caster1,1, 1)
        people.addWidget(QLabel("Caster 2:"), 2, 0);     people.addWidget(self.caster2,2, 1)
        root.addWidget(people_box)

        logo_box = QGroupBox("Logos")
        logo = QGridLayout(logo_box)
        self.overlay_logo_path = None
        self.overlay_logo_preview = QLabel("Overlay logo")
        self.overlay_logo_preview.setAlignment(Qt.AlignCenter)
        self.overlay_logo_preview.setFixedSize(200, 80)
        self.overlay_logo_preview.setStyleSheet("QLabel{border:1px solid #CCC;background:#FAFAFA;}")
        btn_overlay = QPushButton("Load overlay logo…")
        btn_overlay.clicked.connect(self._pick_overlay_logo)
        self.transition_logo_path = None
        self.transition_logo_preview = QLabel("Transition logo")
        self.transition_logo_preview.setAlignment(Qt.AlignCenter)
        self.transition_logo_preview.setFixedSize(200, 80)
        self.transition_logo_preview.setStyleSheet("QLabel{border:1px solid #CCC;background:#FAFAFA;}")
        btn_transition = QPushButton("Load transition logo…")
        btn_transition.clicked.connect(self._pick_transition_logo)

        logo.addWidget(QLabel("Overlay-logo:"),   0, 0); logo.addWidget(self.overlay_logo_preview,   0, 1); logo.addWidget(btn_overlay,   0, 2)
        logo.addWidget(QLabel("Transition-logo:"),1, 0); logo.addWidget(self.transition_logo_preview,1, 1); logo.addWidget(btn_transition,1, 2)
        root.addWidget(logo_box)

        color_box = QGroupBox("Overlay colours")
        colors = QVBoxLayout(color_box)
        self.color_btns: Dict[str, QPushButton] = {}

        status_box = QGroupBox("Status text")
        status_lay = QVBoxLayout(status_box)
        self.status_text = QLineEdit()
        self.status_text.setPlaceholderText("Tournament name here")
        status_lay.addWidget(self.status_text)
        root.addWidget(status_box)

        for key, label in self.COLOR_FIELDS:
            row = QHBoxLayout()
            row.addWidget(QLabel(label))
            btn = QPushButton("Select colour")
            btn.setFixedWidth(130)
            btn.clicked.connect(lambda _, k=key: self._pick_color(k))
            btn.setStyleSheet("QPushButton{border:1px solid #CCC; padding:6px; background:#FFFFFF;}")
            self.color_btns[key] = btn
            row.addStretch(1)
            row.addWidget(btn)
            colors.addLayout(row)

        root.addWidget(color_box)
        btn_row = QHBoxLayout()
        btn_row.addStretch(1)

        self.reset_btn = QPushButton("Reset this tab")
        self.reset_btn.clicked.connect(self.reset_tab)

        self.update_btn = QPushButton("Update (General)")
        self.update_btn.clicked.connect(self._emit_update)

        btn_row.addWidget(self.reset_btn)
        btn_row.addWidget(self.update_btn)
        root.addLayout(btn_row)

        root.addStretch(1)

        self._colors: Dict[str, str] = {}

    def _pick_overlay_logo(self):
        base = os.environ.get("SOWB_ROOT") or _app_base()
        start_dir = os.path.join(base, "Scoreboard", "Temp", "Broadcast Logos")
        os.makedirs(start_dir, exist_ok=True)

        path, _ = QFileDialog.getOpenFileName(
            self,
            "Valitse overlay-logo",
            start_dir,
            "Images (*.png *.jpg *.jpeg *.webp *.svg)"
        )
        if path:
            self.overlay_logo_path = path
            pix = QPixmap(path)
            self.overlay_logo_preview.setPixmap(
                pix.scaled(self.overlay_logo_preview.size(), Qt.KeepAspectRatio, Qt.SmoothTransformation)
            )

    def _pick_transition_logo(self):
        base = os.environ.get("SOWB_ROOT") or _app_base()
        start_dir = os.path.join(base, "Scoreboard", "Temp", "Broadcast Logos")
        os.makedirs(start_dir, exist_ok=True)

        path, _ = QFileDialog.getOpenFileName(
            self,
            "Valitse transition-logo",
            start_dir,
            "Images (*.png *.jpg *.jpeg *.webp *.svg)"
        )
        if path:
            self.transition_logo_path = path
            pix = QPixmap(path)
            self.transition_logo_preview.setPixmap(
                pix.scaled(self.transition_logo_preview.size(), Qt.KeepAspectRatio, Qt.SmoothTransformation)
            )

    def _pick_color(self, key: str):
        start = QColor(self._colors.get(key, "#FFFFFF"))
        color = QColorDialog.getColor(start, self, "Valitse väri")
        if color.isValid():
            hexv = color.name(QColor.HexRgb)
            self._colors[key] = hexv
            self.color_btns[key].setStyleSheet(f"QPushButton{{border:1px solid #CCC; padding:6px; background:{hexv};}}")

    def to_settings(self) -> GeneralSettings:
        return GeneralSettings(
            first_to=int(self.maps_count.value()),
            host=self.host.text().strip(),
            caster1=self.caster1.text().strip(),
            caster2=self.caster2.text().strip(),
            status_text=self.status_text.text().strip(),
            overlay_logo_path=self.overlay_logo_path,
            transition_logo_path=self.transition_logo_path,
            colors=dict(self._colors),
        )


    def from_settings(self, s: GeneralSettings):
        try:
            n = int(getattr(s, "first_to", 3) or 3)
        except ValueError:
            n = 3
        n = max(1, min(7, n))
        self.maps_count.setValue(n)

        self.host.setText(s.host or "")
        self.caster1.setText(s.caster1 or "")
        self.caster2.setText(s.caster2 or "")
        self.status_text.setText(getattr(s, "status_text", "") or "")

        self.overlay_logo_path = s.overlay_logo_path
        if s.overlay_logo_path:
            pix = QPixmap(s.overlay_logo_path)
            if not pix.isNull():
                self.overlay_logo_preview.setPixmap(
                    pix.scaled(self.overlay_logo_preview.size(), Qt.KeepAspectRatio, Qt.SmoothTransformation)
                )
        else:
            self.overlay_logo_preview.clear()
            self.overlay_logo_preview.setText("Overlay-logo")

        self.transition_logo_path = s.transition_logo_path
        if s.transition_logo_path:
            pix = QPixmap(s.transition_logo_path)
            if not pix.isNull():
                self.transition_logo_preview.setPixmap(
                    pix.scaled(self.transition_logo_preview.size(), Qt.KeepAspectRatio, Qt.SmoothTransformation)
                )
        else:
            self.transition_logo_preview.clear()
            self.transition_logo_preview.setText("Transition-logo")

        self._colors = dict(s.colors or {})
        for k, btn in self.color_btns.items():
            hexv = self._colors.get(k, "#FFFFFF")
            self._set_color_button_bg(k, hexv)


    
    def reset_tab(self):
        defaults = GeneralSettings()
        self.from_settings(defaults)
        self.status_text.clear()

    def _set_color_button_bg(self, key: str, hexv: str):
        self.color_btns[key].setStyleSheet(
            f"QPushButton{{border:1px solid #CCC; padding:6px; background:{hexv};}}"
        )

def _app_base():
    return os.path.dirname(sys.executable) if getattr(sys, "frozen", False) else os.path.dirname(__file__)

def _ensure_scoreboard_tree(root):
    subdirs = [
        "General", "Match", "Heroes", "Maps", "Gametypes",
        "Replay", "Replay\\Playlist", "Roles", "Teams", "Temp", "Waiting",
        "Brackets", "Brackets\\Logos"
    ]
    os.makedirs(root, exist_ok=True)
    for d in subdirs:
        os.makedirs(os.path.join(root, d), exist_ok=True)

def _norm_rel(path: str, root: str) -> str:
    """Palauta rootista suhteellinen polku forward slasheilla."""
    rel = os.path.relpath(path, root)
    return rel.replace("\\", "/")

class WaitingTab(QWidget):
    updated = pyqtSignal()

    def __init__(self):
        super().__init__()
        root = QVBoxLayout(self)

        base = os.environ.get("SOWB_ROOT") or _app_base()
        cand1 = os.path.join(base, "SOWBroadcast", "Highlights")
        cand2 = os.path.join(base, "Highlights")
        self.default_videos_dir = cand1 if os.path.isdir(cand1) else cand2

        box_v = QGroupBox("Waiting videos (folder)")
        lay_v = QHBoxLayout(box_v)
        self.videos_dir = QLineEdit()
        self.videos_dir.setReadOnly(True)
        self.videos_dir.setPlaceholderText(self.default_videos_dir or "Select a folder that contains videos")
        self.use_default_chk = QCheckBox("Use default (SOWBroadcast\\Highlights)")
        self.use_default_chk.setChecked(True if self.default_videos_dir else False)
        self.use_default_chk.toggled.connect(self._on_use_default_toggled)
        btn_pick = QPushButton("Browse…")
        btn_pick.clicked.connect(self._pick_folder)
        lay_v.addWidget(self.videos_dir, 1)
        lay_v.addWidget(btn_pick)
        lay_v.addWidget(self.use_default_chk)
        root.addWidget(box_v)

        box_t = QGroupBox("Countdown timer")
        lay_t = QHBoxLayout(box_t)
        self.min_spin = QSpinBox(); self.min_spin.setRange(0, 999); self.min_spin.setValue(0)
        self.sec_spin = QSpinBox(); self.sec_spin.setRange(0, 59);  self.sec_spin.setValue(0)
        lay_t.addWidget(QLabel("Minutes:")); lay_t.addWidget(self.min_spin)
        lay_t.addSpacing(16)
        lay_t.addWidget(QLabel("Seconds:")); lay_t.addWidget(self.sec_spin)
        lay_t.addStretch(1)

        self.live_label = QLabel("00:00")
        self.live_label.setStyleSheet("QLabel{font: 900 26px 'Segoe UI';}")

        self.btn_start = QPushButton("Start")
        self.btn_pause = QPushButton("Pause")
        self.btn_reset = QPushButton("Reset")

        self.btn_start.clicked.connect(self._start_timer)
        self.btn_pause.clicked.connect(self._pause_timer)
        self.btn_reset.clicked.connect(self._reset_timer_clicked)

        lay_t.addWidget(self.live_label)
        lay_t.addWidget(self.btn_start)
        lay_t.addWidget(self.btn_pause)
        lay_t.addWidget(self.btn_reset)


        root.addWidget(box_t)

        from PyQt5.QtCore import QTimer
        self._qtimer = QTimer(self)
        self._qtimer.setInterval(1000)
        self._qtimer.timeout.connect(self._tick)
        self._preset_seconds = 0
        self._remaining_seconds = 0

        for w in (self.min_spin, self.sec_spin):
            w.valueChanged.connect(self._on_preset_changed)

        box_x = QGroupBox("On-screen texts")
        grid = QFormLayout(box_x)
        self.text_starting = QLineEdit("STARTING SOON!")
        self.text_brb      = QLineEdit("BE RIGHT BACK!")
        self.text_end      = QLineEdit("THANK YOU FOR WATCHING")
        grid.addRow("StartingSoon.html:", self.text_starting)
        grid.addRow("BeRightBack.html:",  self.text_brb)
        grid.addRow("EndScreen.html:",    self.text_end)
        root.addWidget(box_x)
        
        box_o = QGroupBox("Ticker override")
        lay_o = QHBoxLayout(box_o)
        self.ticker_override_edit = QLineEdit()
        self.ticker_override_edit.setPlaceholderText("Custom ticker text…")
        self.ticker_override_chk = QCheckBox("Overwrite default ticker text")
        self.ticker_override_chk.toggled.connect(self._on_ticker_override_toggled)
        lay_o.addWidget(self.ticker_override_edit, 1)
        lay_o.addWidget(self.ticker_override_chk)
        root.addWidget(box_o)

        box_s = QGroupBox("Socials")
        form_s = QFormLayout(box_s)

        self.s_twitch  = QLineEdit(); self.s_twitch.setPlaceholderText("twitch.tv/")
        self.s_twitter = QLineEdit(); self.s_twitter.setPlaceholderText("@user or x.com/…")
        self.s_youtube = QLineEdit(); self.s_youtube.setPlaceholderText("@user")
        self.s_instagram = QLineEdit(); self.s_instagram.setPlaceholderText("@user")
        self.s_discord = QLineEdit(); self.s_discord.setPlaceholderText("/invite")
        self.s_website = QLineEdit(); self.s_website.setPlaceholderText("domain.com")

        form_s.addRow("Twitch",   self.s_twitch)
        form_s.addRow("Twitter/X",self.s_twitter)
        form_s.addRow("YouTube",  self.s_youtube)
        form_s.addRow("Instagram",self.s_instagram)
        form_s.addRow("Discord",  self.s_discord)
        form_s.addRow("Website",  self.s_website)

        root.addWidget(box_s)

        btns = QHBoxLayout()
        btns.addStretch(1)
        btn_reset = QPushButton("Reset this tab")
        btn_reset.clicked.connect(self._reset_tab)
        btn_update = QPushButton("Update (Waiting)")
        btn_update.clicked.connect(lambda *_: self.updated.emit())
        btns.addWidget(btn_reset); btns.addWidget(btn_update)
        root.addLayout(btns)
        root.addStretch(1)
        
        self._on_use_default_toggled(self.use_default_chk.isChecked())
        self._on_preset_changed()
        self._on_ticker_override_toggled(False)

    def _fmt(self, s:int)->str:
        s=max(0,int(s)); return f"{s//60:02d}:{s%60:02d}"

    def _on_preset_changed(self, *_):
        self._preset_seconds = int(self.min_spin.value())*60 + int(self.sec_spin.value())
        if not self._qtimer.isActive():
            self._remaining_seconds = self._preset_seconds
            self.live_label.setText(self._fmt(self._remaining_seconds))
        self.updated.emit()
        
    def _start_timer(self):
        if self._remaining_seconds <= 0:
            self._remaining_seconds = self._preset_seconds
        self._qtimer.start()
        self.updated.emit()

    def _pause_timer(self):
        if self._qtimer.isActive():
            self._qtimer.stop()
            self.updated.emit()

    def _on_ticker_override_toggled(self, checked: bool):
        self.ticker_override_edit.setEnabled(checked)

    def _reset_timer_clicked(self):
        self._qtimer.stop()
        self._remaining_seconds = self._preset_seconds
        self.live_label.setText(self._fmt(self._remaining_seconds))
        self.updated.emit()

    def _tick(self):
        self._remaining_seconds = max(0, self._remaining_seconds - 1)
        self.live_label.setText(self._fmt(self._remaining_seconds))
        if self._remaining_seconds <= 0:
            self._qtimer.stop()
        self.updated.emit()


    def _on_use_default_toggled(self, checked: bool):
        self.videos_dir.setEnabled(not checked)
        if checked:
            self.videos_dir.setText("")
            self.videos_dir.setPlaceholderText(self.default_videos_dir or "")
        self.updated.emit()

    def _pick_folder(self):
        d = QFileDialog.getExistingDirectory(self, "Select videos folder", self.default_videos_dir or "")
        if d:
            self.videos_dir.setText(d)
            self.use_default_chk.setChecked(False)
            self.updated.emit()

    def _reset_tab(self):
        self.use_default_chk.setChecked(True if self.default_videos_dir else False)
        self.videos_dir.clear()
        self.min_spin.setValue(0); self.sec_spin.setValue(0)
        self.text_starting.setText("STARTING SOON!")
        self.text_brb.setText("BE RIGHT BACK!")
        self.text_end.setText("THANK YOU FOR WATCHING!")
        self._reset_timer_clicked()
        self.ticker_override_chk.setChecked(False)
        self.ticker_override_edit.clear()
        self._reset_timer_clicked()

    def to_settings(self) -> WaitingSettings:
        secs = int(self._remaining_seconds)
        vdir = "" if self.use_default_chk.isChecked() else self.videos_dir.text().strip()
        socials = {
            "twitch":   self._normalize_handle("twitch",   self.s_twitch.text()),
            "twitter":  self._normalize_handle("twitter",  self.s_twitter.text()),
            "youtube":  self._normalize_handle("youtube",  self.s_youtube.text()),
            "instagram":self._normalize_handle("instagram",self.s_instagram.text()),
            "discord":  self._normalize_handle("discord",  self.s_discord.text()),
            "web":      self._normalize_handle("web",      self.s_website.text()),
        }
        socials = {k:v for k,v in socials.items() if v}
        return WaitingSettings(
            videos_dir=vdir,
            timer_seconds=max(0, secs),
            text_starting=self.text_starting.text().strip() or "STARTING SOON!",
            text_brb=self.text_brb.text().strip() or "BE RIGHT BACK!",
            text_end=self.text_end.text().strip() or "THANK YOU FOR WATCHING",
            timer_running=bool(self._qtimer.isActive()),
            socials=socials,
            ticker_override=self.ticker_override_edit.text().strip(),
            ticker_override_enabled=bool(self.ticker_override_chk.isChecked()),
        )


    def from_settings(self, s: WaitingSettings):
        secs = int(getattr(s, "timer_seconds", 0) or 0)
        self._remaining_seconds = max(0, secs)
        self._preset_seconds = self._remaining_seconds
        self.min_spin.setValue(self._preset_seconds // 60)
        self.sec_spin.setValue(self._preset_seconds % 60)
        self.live_label.setText(self._fmt(self._remaining_seconds))
        vdir = getattr(s, "videos_dir", "") or ""
        if vdir:
            self.videos_dir.setText(vdir)
            self.use_default_chk.setChecked(False)
        else:
            self.use_default_chk.setChecked(True if self.default_videos_dir else False)
            self.videos_dir.clear()
        self.text_starting.setText(getattr(s, "text_starting", "STARTING SOON!"))
        self.text_brb.setText(getattr(s, "text_brb", "BE RIGHT BACK!"))
        self.text_end.setText(getattr(s, "text_end", "THANK YOU FOR WATCHING"))
        soc = getattr(s, "socials", {}) or {}
        self.s_twitch.setText(soc.get("twitch",""))
        self.s_twitter.setText(soc.get("twitter",""))
        self.s_youtube.setText(soc.get("youtube",""))
        self.s_instagram.setText(soc.get("instagram",""))
        self.s_discord.setText(soc.get("discord",""))
        self.s_website.setText(soc.get("web",""))
        self.ticker_override_edit.setText(getattr(s, "ticker_override", ""))
        self.ticker_override_chk.setChecked(bool(getattr(s, "ticker_override_enabled", False)))
        self.ticker_override_edit.setEnabled(self.ticker_override_chk.isChecked())
    
    @staticmethod
    def _normalize_handle(kind: str, text: str) -> str:
        s = (text or "").strip()
        s = re.sub(r"^https?://", "", s, flags=re.I)
        if kind == "twitch":
            s = re.sub(r"^[^/]*twitch\.tv/", "", s, flags=re.I)
        elif kind == "twitter":
            s = re.sub(r"^[^/]*(twitter\.com|x\.com)/", "", s, flags=re.I)
        elif kind == "youtube":
            s = re.sub(r"^[^/]*(youtube\.com|youtu\.be)/", "", s, flags=re.I)
        elif kind == "instagram":
            s = re.sub(r"^[^/]*instagram\.com/", "", s, flags=re.I)
        elif kind == "discord":
            s = re.sub(r"^[^/]*(discord\.gg/|discord\.com/invite/)", "", s, flags=re.I)
        s = re.sub(r"^@+", "", s)
        s = re.sub(r"^/+", "", s)
        s = re.split(r"[/?#]", s)[0]
        return s





class DraftTab(QWidget):
    updated = pyqtSignal()

    def __init__(self, get_maps_by_mode):
        super().__init__()
        self.get_maps_by_mode = get_maps_by_mode
        root = QVBoxLayout(self)

        row = QHBoxLayout()
        self.btn_all = QPushButton("Select All")
        self.btn_none = QPushButton("Select None")
        row.addWidget(self.btn_all)
        row.addWidget(self.btn_none)
        row.addStretch(1)
        root.addLayout(row)

        self.tree = QTreeWidget()
        self.tree.setHeaderHidden(True)
        self.tree.setSelectionMode(QTreeWidget.NoSelection)
        root.addWidget(self.tree, 1)

        self.btn_all.clicked.connect(self.select_all)
        self.btn_none.clicked.connect(self.select_none)
        self.tree.itemChanged.connect(lambda *_: self.updated.emit())

        self.update_btn = QPushButton("Update")
        self.update_btn.clicked.connect(lambda *_: self.updated.emit())
        root.addWidget(self.update_btn)

        self.reload()

    def _iter_map_items(self):
        """Iteroi vain kartta-childit (ei moodiotsikoita)."""
        top_count = self.tree.topLevelItemCount()
        for i in range(top_count):
            parent = self.tree.topLevelItem(i)
            for j in range(parent.childCount()):
                yield parent.child(j)

    def reload(self):
        """Lataa kartat ryhmiteltynä pelimuodoittain. Säilyttää aiemmat valinnat."""
        old_selected = set(self.get_pool())
        self.tree.blockSignals(True)
        self.tree.clear()

        data = self.get_maps_by_mode() or {}
        for mode_name, maps in data.items():
            if not maps:
                continue
            mode_item = QTreeWidgetItem([mode_name or "Unspecified"])
            mode_item.setFlags(mode_item.flags() & ~Qt.ItemIsUserCheckable)
            self.tree.addTopLevelItem(mode_item)
            for name in sorted(maps):
                it = QTreeWidgetItem([name])
                it.setFlags(it.flags() | Qt.ItemIsUserCheckable)
                checked = (name in old_selected) or (not old_selected)
                it.setCheckState(0, Qt.Checked if checked else Qt.Unchecked)
                mode_item.addChild(it)

        self.tree.expandAll()
        self.tree.blockSignals(False)

    def get_pool(self) -> list:
        """Palauttaa valitun map poolin nimilistan."""
        pool = []
        for it in self._iter_map_items():
            if it.checkState(0) == Qt.Checked:
                pool.append(it.text(0))
        return pool

    def set_pool(self, names: list):
        """Aseta valinnat annetun nimilistan mukaan."""
        wanted = set(names or [])
        self.tree.blockSignals(True)
        for it in self._iter_map_items():
            it.setCheckState(0, Qt.Checked if it.text(0) in wanted else Qt.Unchecked)
        self.tree.blockSignals(False)
        self.updated.emit()

    def select_all(self):
        self.tree.blockSignals(True)
        for it in self._iter_map_items():
            it.setCheckState(0, Qt.Checked)
        self.tree.blockSignals(False)
        self.updated.emit()

    def select_none(self):
        self.tree.blockSignals(True)
        for it in self._iter_map_items():
            it.setCheckState(0, Qt.Unchecked)
        self.tree.blockSignals(False)
        self.updated.emit()


class BracketTeamRow(QWidget):
    changed = pyqtSignal()
    move_up = pyqtSignal(object)
    move_down = pyqtSignal(object)
    remove_row = pyqtSignal(object)

    def __init__(self, team: Optional[BracketTeam] = None):
        super().__init__()
        row = QHBoxLayout(self)
        row.setContentsMargins(0, 0, 0, 0)

        self.name_edit = QLineEdit()
        self.abbr_edit = QLineEdit()
        self.abbr_edit.setFixedWidth(80)

        self.logo_edit = QLineEdit()
        self.logo_edit.setReadOnly(True)
        self.logo_btn = QPushButton("Browse…")
        self.logo_btn.setFixedWidth(90)

        self.group_edit = QLineEdit()
        self.group_edit.setFixedWidth(80)

        self.up_btn = QPushButton("↑")
        self.down_btn = QPushButton("↓")
        self.remove_btn = QPushButton("Remove")
        self.up_btn.setFixedWidth(30)
        self.down_btn.setFixedWidth(30)

        row.addWidget(QLabel("Name:"), 0)
        row.addWidget(self.name_edit, 2)
        row.addWidget(QLabel("Abbr:"), 0)
        row.addWidget(self.abbr_edit, 0)
        row.addWidget(QLabel("Logo:"), 0)
        row.addWidget(self.logo_edit, 2)
        row.addWidget(self.logo_btn, 0)
        row.addWidget(QLabel("Group:"), 0)
        row.addWidget(self.group_edit, 0)
        row.addWidget(self.up_btn, 0)
        row.addWidget(self.down_btn, 0)
        row.addWidget(self.remove_btn, 0)

        self.logo_btn.clicked.connect(self._pick_logo)
        self.up_btn.clicked.connect(lambda *_: self.move_up.emit(self))
        self.down_btn.clicked.connect(lambda *_: self.move_down.emit(self))
        self.remove_btn.clicked.connect(lambda *_: self.remove_row.emit(self))

        self.name_edit.textChanged.connect(lambda *_: self.changed.emit())
        self.abbr_edit.textChanged.connect(lambda *_: self.changed.emit())
        self.group_edit.textChanged.connect(lambda *_: self.changed.emit())

        self._logo_path = None
        if team:
            self.from_team(team)

    def _pick_logo(self):
        base = os.environ.get("SOWB_ROOT") or _app_base()
        start_dir = os.path.join(base, "Scoreboard", "Teams")
        os.makedirs(start_dir, exist_ok=True)
        path, _ = QFileDialog.getOpenFileName(
            self,
            "Select team logo",
            start_dir,
            "Images (*.png *.jpg *.jpeg *.webp *.svg)"
        )
        if path:
            self._logo_path = path
            self.logo_edit.setText(path)
            self.changed.emit()

    def to_team(self) -> BracketTeam:
        return BracketTeam(
            name=self.name_edit.text().strip(),
            abbr=self.abbr_edit.text().strip(),
            logo_path=self._logo_path,
            group=self.group_edit.text().strip()
        )

    def from_team(self, team: BracketTeam):
        self.name_edit.setText(team.name or "")
        self.abbr_edit.setText(team.abbr or "")
        self._logo_path = team.logo_path
        self.logo_edit.setText(team.logo_path or "")
        self.group_edit.setText(team.group or "")


class BracketMatchRow(QWidget):
    score_changed = pyqtSignal(int, int)

    def __init__(self, match: BracketMatch, label: str):
        super().__init__()
        self.match_id = match.id
        row = QHBoxLayout(self)
        row.setContentsMargins(0, 0, 0, 0)

        self.label = QLabel(label)
        self.label.setMinimumWidth(160)
        self.teamA_label = QLabel("TBD")
        self.teamB_label = QLabel("TBD")
        self.teamA_label.setMinimumWidth(180)
        self.teamB_label.setMinimumWidth(180)

        self.scoreA = QSpinBox()
        self.scoreB = QSpinBox()
        self.scoreA.setRange(0, 99)
        self.scoreB.setRange(0, 99)
        self.scoreA.setFixedWidth(60)
        self.scoreB.setFixedWidth(60)

        self.winner_label = QLabel("Winner: —")

        row.addWidget(self.label, 0)
        row.addWidget(self.teamA_label, 1)
        row.addWidget(self.scoreA, 0)
        row.addWidget(QLabel("vs"), 0)
        row.addWidget(self.scoreB, 0)
        row.addWidget(self.teamB_label, 1)
        row.addWidget(self.winner_label, 1)

        self.scoreA.valueChanged.connect(self._emit_score)
        self.scoreB.valueChanged.connect(self._emit_score)

    def _emit_score(self, *_):
        self.score_changed.emit(self.scoreA.value(), self.scoreB.value())

    def set_team_labels(self, name_a: str, name_b: str):
        self.teamA_label.setText(name_a)
        self.teamB_label.setText(name_b)
        enable = bool(name_a) and bool(name_b) and name_a != "TBD" and name_b != "TBD"
        self.scoreA.setEnabled(enable)
        self.scoreB.setEnabled(enable)

    def set_scores(self, a: int, b: int):
        self.scoreA.blockSignals(True)
        self.scoreB.blockSignals(True)
        self.scoreA.setValue(a)
        self.scoreB.setValue(b)
        self.scoreA.blockSignals(False)
        self.scoreB.blockSignals(False)

    def set_winner(self, name: str):
        self.winner_label.setText(f"Winner: {name or '—'}")


class BracketTab(QWidget):
    updated = pyqtSignal()

    BRACKET_TYPES = [
        "4 team single elimination",
        "4 team double elimination",
        "6 team single elimination",
        "6 team double elimination",
        "8 team single elimination",
        "8 team double elimination",
        "16 team single elimination",
        "16 team double elimination",
        "swiss",
        "round robin group stage",
    ]

    def __init__(self):
        super().__init__()
        self.matches: List[BracketMatch] = []
        self.match_rows: Dict[int, BracketMatchRow] = {}
        self._loading_state = False

        root = QVBoxLayout(self)

        settings_box = QGroupBox("Bracket Settings")
        settings_layout = QHBoxLayout(settings_box)
        self.bracket_type_combo = QComboBox()
        for t in self.BRACKET_TYPES:
            self.bracket_type_combo.addItem(t)
        settings_layout.addWidget(QLabel("Bracket type:"))
        settings_layout.addWidget(self.bracket_type_combo, 1)

        self.team_count_spin = QSpinBox()
        self.team_count_spin.setRange(2, 32)
        self.team_count_spin.setValue(4)
        settings_layout.addWidget(QLabel("Team count:"))
        settings_layout.addWidget(self.team_count_spin)

        self.display_stage_combo = QComboBox()
        self.display_stage_combo.addItems(["All", "Groups only", "Playoffs only"])
        settings_layout.addWidget(QLabel("Display:"))
        settings_layout.addWidget(self.display_stage_combo)

        self.display_group_combo = QComboBox()
        self.display_group_combo.addItems(["All"])
        settings_layout.addWidget(QLabel("Group:"))
        settings_layout.addWidget(self.display_group_combo)

        self.display_round_combo = QComboBox()
        self.display_round_combo.addItems(["All"])
        settings_layout.addWidget(QLabel("Round:"))
        settings_layout.addWidget(self.display_round_combo)

        settings_layout.addStretch(1)
        root.addWidget(settings_box)

        team_box = QGroupBox("Teams")
        team_layout = QVBoxLayout(team_box)

        self.team_container = QVBoxLayout()
        team_scroll_root = QWidget()
        team_scroll_root.setLayout(self.team_container)
        team_scroll = QScrollArea()
        team_scroll.setWidgetResizable(True)
        team_scroll.setWidget(team_scroll_root)

        team_layout.addWidget(team_scroll, 1)

        team_buttons = QHBoxLayout()
        self.add_team_btn = QPushButton("Add Team")
        self.generate_btn = QPushButton("Generate Bracket")
        team_buttons.addWidget(self.add_team_btn)
        team_buttons.addStretch(1)
        team_buttons.addWidget(self.generate_btn)
        team_layout.addLayout(team_buttons)
        root.addWidget(team_box, 3)

        matches_box = QGroupBox("Matches")
        matches_layout = QVBoxLayout(matches_box)

        self.matches_container = QVBoxLayout()
        matches_scroll_root = QWidget()
        matches_scroll_root.setLayout(self.matches_container)
        matches_scroll = QScrollArea()
        matches_scroll.setWidgetResizable(True)
        matches_scroll.setWidget(matches_scroll_root)
        matches_layout.addWidget(matches_scroll, 1)
        root.addWidget(matches_box, 5)

        btns = QHBoxLayout()
        btns.addStretch(1)
        self.update_btn = QPushButton("Update (Brackets)")
        btns.addWidget(self.update_btn)
        root.addLayout(btns)

        self.add_team_btn.clicked.connect(self._add_team_row)
        self.generate_btn.clicked.connect(self._generate_bracket)
        self.update_btn.clicked.connect(lambda *_: self.updated.emit())
        self.bracket_type_combo.currentTextChanged.connect(self._on_bracket_type_changed)
        self.team_count_spin.valueChanged.connect(self._on_team_count_changed)
        self.display_stage_combo.currentTextChanged.connect(self._on_display_changed)
        self.display_group_combo.currentTextChanged.connect(lambda *_: self.updated.emit())
        self.display_round_combo.currentTextChanged.connect(lambda *_: self.updated.emit())

        self._add_team_row()
        self._last_bracket_type = self.bracket_type_combo.currentText()
        self._last_team_count = len(self._collect_teams())
        self._update_team_count_state()

    def _add_team_row(self, team: Optional[BracketTeam] = None):
        row = BracketTeamRow(team)
        row.changed.connect(self._on_teams_changed)
        row.move_up.connect(self._move_team_up)
        row.move_down.connect(self._move_team_down)
        row.remove_row.connect(self._remove_team_row)
        self.team_container.addWidget(row)
        self._on_teams_changed()

    def _iter_team_rows(self) -> List[BracketTeamRow]:
        rows = []
        for i in range(self.team_container.count()):
            w = self.team_container.itemAt(i).widget()
            if isinstance(w, BracketTeamRow):
                rows.append(w)
        return rows

    def _remove_team_row(self, row: BracketTeamRow):
        row.setParent(None)
        row.deleteLater()
        self._on_teams_changed()

    def _move_team_up(self, row: BracketTeamRow):
        rows = self._iter_team_rows()
        idx = rows.index(row)
        if idx <= 0:
            return
        self.team_container.insertWidget(idx - 1, row)
        self._on_teams_changed()

    def _move_team_down(self, row: BracketTeamRow):
        rows = self._iter_team_rows()
        idx = rows.index(row)
        if idx >= len(rows) - 1:
            return
        self.team_container.insertWidget(idx + 1, row)
        self._on_teams_changed()

    def _on_teams_changed(self):
        if self._loading_state:
            return
        current_count = len(self._collect_teams())
        if self.matches and current_count != self._last_team_count:
            QMessageBox.information(
                self,
                "Teams changed",
                "Team count changed. Please regenerate the bracket to update matches."
            )
            self.matches = []
            self._rebuild_matches_ui()
        self._last_team_count = current_count
        self._update_display_filters()
        self._refresh_match_rows()
        self.updated.emit()

    def _on_bracket_type_changed(self, *_):
        if self.matches:
            ans = QMessageBox.question(
                self,
                "Regenerate bracket?",
                "Changing bracket type will regenerate matches and reset scores. Continue?",
                QMessageBox.Yes | QMessageBox.No
            )
            if ans != QMessageBox.Yes:
                self.bracket_type_combo.blockSignals(True)
                self.bracket_type_combo.setCurrentText(self._last_bracket_type)
                self.bracket_type_combo.blockSignals(False)
                return
        self._generate_bracket()
        self._last_bracket_type = self.bracket_type_combo.currentText()
        self._update_team_count_state()

    def _validate_team_count(self, count: int) -> bool:
        btype = self.bracket_type_combo.currentText()
        fixed_counts = {
            "4 team single elimination": 4,
            "4 team double elimination": 4,
            "6 team single elimination": 6,
            "6 team double elimination": 6,
            "8 team single elimination": 8,
            "8 team double elimination": 8,
            "16 team single elimination": 16,
            "16 team double elimination": 16,
        }
        if btype in fixed_counts:
            required = fixed_counts[btype]
            if count != required:
                QMessageBox.warning(self, "Team count mismatch",
                                    f"{btype} requires exactly {required} teams.")
                return False
            return True
        if btype == "swiss":
            if count < 2:
                QMessageBox.warning(self, "Team count mismatch",
                                    "Swiss requires at least 2 teams.")
                return False
            return True
        if btype == "round robin group stage":
            if count < 2 or count > 32:
                QMessageBox.warning(self, "Team count mismatch",
                                    "Round robin group stage supports 2 to 32 teams.")
                return False
            return True
        return True

    def _update_team_count_state(self):
        btype = self.bracket_type_combo.currentText()
        fixed_counts = {
            "4 team single elimination": 4,
            "4 team double elimination": 4,
            "6 team single elimination": 6,
            "6 team double elimination": 6,
            "8 team single elimination": 8,
            "8 team double elimination": 8,
            "16 team single elimination": 16,
            "16 team double elimination": 16,
        }
        if btype in fixed_counts:
            required = fixed_counts[btype]
            self.team_count_spin.blockSignals(True)
            self.team_count_spin.setValue(required)
            self.team_count_spin.blockSignals(False)
            self.team_count_spin.setEnabled(False)
        else:
            self.team_count_spin.setEnabled(True)
            if self.team_count_spin.value() < 2:
                self.team_count_spin.setValue(max(2, self._last_team_count))
        self._sync_team_rows_with_count()
        allow_groups = btype in {"swiss", "round robin group stage"}
        for row in self._iter_team_rows():
            row.group_edit.setEnabled(allow_groups)
        self._update_display_filters()

    def _on_team_count_changed(self, *_):
        if self._loading_state:
            return
        self._sync_team_rows_with_count()
        self.updated.emit()

    def _sync_team_rows_with_count(self):
        desired = self.team_count_spin.value()
        rows = self._iter_team_rows()
        if len(rows) < desired:
            for _ in range(desired - len(rows)):
                self._add_team_row()
        elif len(rows) > desired:
            for row in rows[desired:]:
                row.setParent(None)
                row.deleteLater()
        self._last_team_count = desired

    def _on_display_changed(self, *_):
        self._update_display_filters()
        self.updated.emit()

    def _update_display_filters(self):
        teams = self._collect_teams()
        groups = sorted({(t.group or "").strip() for t in teams if (t.group or "").strip()})
        self.display_group_combo.blockSignals(True)
        current = self.display_group_combo.currentText()
        self.display_group_combo.clear()
        self.display_group_combo.addItem("All")
        for g in groups:
            self.display_group_combo.addItem(f"Group {g}")
        if current in [f"Group {g}" for g in groups]:
            self.display_group_combo.setCurrentText(current)
        self.display_group_combo.blockSignals(False)

        round_labels = ["All"]
        if self.matches:
            rounds = sorted({m.round for m in self.matches})
            round_labels += [f"Round {r}" for r in rounds]
        self.display_round_combo.blockSignals(True)
        current_round = self.display_round_combo.currentText()
        self.display_round_combo.clear()
        self.display_round_combo.addItems(round_labels)
        if current_round in round_labels:
            self.display_round_combo.setCurrentText(current_round)
        self.display_round_combo.blockSignals(False)

    def _generate_bracket(self):
        teams = self._collect_teams()
        if not self._validate_team_count(len(teams)):
            return
        self._last_team_count = len(teams)
        btype = self.bracket_type_combo.currentText()
        if btype.endswith("single elimination"):
            matches = self._generate_single_elimination(teams)
        elif btype.endswith("double elimination"):
            matches = self._generate_double_elimination(teams)
        elif btype == "swiss":
            matches = self._generate_swiss(teams)
        else:
            matches = self._generate_round_robin(teams)
        self.matches = matches
        self._rebuild_matches_ui()
        self._apply_progression()
        self._update_display_filters()
        self.updated.emit()

    def _collect_teams(self) -> List[BracketTeam]:
        return [row.to_team() for row in self._iter_team_rows()]

    def _rebuild_matches_ui(self):
        while self.matches_container.count():
            item = self.matches_container.takeAt(0)
            widget = item.widget()
            if widget:
                widget.setParent(None)
                widget.deleteLater()
        self.match_rows.clear()
        for m in self.matches:
            label = self._match_label(m)
            row = BracketMatchRow(m, label)
            row.score_changed.connect(lambda a, b, mid=m.id: self._on_score_changed(mid, a, b))
            self.matches_container.addWidget(row)
            self.match_rows[m.id] = row
        self._refresh_match_rows()

    def _refresh_match_rows(self):
        teams = self._collect_teams()
        for match in self.matches:
            row = self.match_rows.get(match.id)
            if not row:
                continue
            name_a = self._team_label(match.teamA, teams)
            name_b = self._team_label(match.teamB, teams)
            row.set_team_labels(name_a, name_b)
            row.set_scores(match.scoreA, match.scoreB)
            winner = self._winner_team_id(match)
            row.set_winner(self._team_label(winner, teams) if winner is not None else "")

    def _on_score_changed(self, match_id: int, score_a: int, score_b: int):
        match = next((m for m in self.matches if m.id == match_id), None)
        if not match:
            return
        match.scoreA = score_a
        match.scoreB = score_b
        self._apply_progression()
        self.updated.emit()

    def _apply_progression(self):
        if not self.matches:
            return
        teams = self._collect_teams()
        for m in self.matches:
            m.teamA = m.seedA
            m.teamB = m.seedB

        match_map = {m.id: m for m in self.matches}
        ordered = sorted(self.matches, key=lambda m: (m.round, m.id))
        for m in ordered:
            winner = self._winner_team_id(m)
            loser = self._loser_team_id(m)
            if m.winnerToMatchId and m.winnerToSlot:
                target = match_map.get(m.winnerToMatchId)
                if target:
                    self._assign_team(target, m.winnerToSlot, winner)
            if m.loserToMatchId and m.loserToSlot:
                target = match_map.get(m.loserToMatchId)
                if target:
                    self._assign_team(target, m.loserToSlot, loser)

        for m in self.matches:
            row = self.match_rows.get(m.id)
            if not row:
                continue
            row.set_team_labels(self._team_label(m.teamA, teams),
                                self._team_label(m.teamB, teams))
            row.set_scores(m.scoreA, m.scoreB)
            winner = self._winner_team_id(m)
            row.set_winner(self._team_label(winner, teams) if winner is not None else "")

    def _assign_team(self, match: BracketMatch, slot: str, team_id: Optional[int]):
        if slot == "A":
            if match.teamA != team_id:
                match.teamA = team_id
                match.scoreA = 0
                match.scoreB = 0
        elif slot == "B":
            if match.teamB != team_id:
                match.teamB = team_id
                match.scoreA = 0
                match.scoreB = 0

    def _team_label(self, team_id: Optional[int], teams: List[BracketTeam]) -> str:
        if team_id is None:
            return "TBD"
        if team_id < 0 or team_id >= len(teams):
            return "TBD"
        team = teams[team_id]
        return team.name or team.abbr or f"Team {team_id + 1}"

    def _winner_team_id(self, match: BracketMatch) -> Optional[int]:
        if match.teamA is None and match.teamB is None:
            return None
        if match.teamA is None:
            return match.teamB
        if match.teamB is None:
            return match.teamA
        if match.scoreA > match.scoreB:
            return match.teamA
        if match.scoreB > match.scoreA:
            return match.teamB
        return None

    def _loser_team_id(self, match: BracketMatch) -> Optional[int]:
        if match.teamA is None or match.teamB is None:
            return None
        if match.scoreA > match.scoreB:
            return match.teamB
        if match.scoreB > match.scoreA:
            return match.teamA
        return None

    def _match_label(self, match: BracketMatch) -> str:
        if match.bracket == "Swiss":
            return f"Swiss R{match.round} M{match.slot}"
        if match.bracket == "RR":
            group = f"{match.group} " if match.group else ""
            return f"{group}R{match.round} M{match.slot}"
        prefix = match.bracket or "Bracket"
        return f"{prefix} R{match.round} M{match.slot}"

    def _next_power_of_two(self, n: int) -> int:
        p = 1
        while p < n:
            p *= 2
        return p

    def _generate_single_elimination(self, teams: List[BracketTeam]) -> List[BracketMatch]:
        n = len(teams)
        slots = self._next_power_of_two(n)
        seeds = list(range(n)) + [None] * (slots - n)
        matches: List[BracketMatch] = []
        match_id = 1
        round_matches: List[List[BracketMatch]] = []

        round_count = int(round(slots.bit_length() - 1))
        current_count = slots // 2
        for r in range(1, round_count + 1):
            round_list = []
            for i in range(current_count):
                team_a = seeds[2 * i] if r == 1 else None
                team_b = seeds[2 * i + 1] if r == 1 else None
                match = BracketMatch(
                    id=match_id,
                    round=r,
                    slot=i + 1,
                    teamA=team_a,
                    teamB=team_b,
                    seedA=team_a,
                    seedB=team_b,
                    bracket="WB"
                )
                round_list.append(match)
                matches.append(match)
                match_id += 1
            round_matches.append(round_list)
            current_count //= 2

        for r, round_list in enumerate(round_matches[:-1], start=1):
            next_round = round_matches[r]
            for i, match in enumerate(round_list):
                target = next_round[i // 2]
                match.winnerToMatchId = target.id
                match.winnerToSlot = "A" if i % 2 == 0 else "B"

        return matches

    def _generate_double_elimination(self, teams: List[BracketTeam]) -> List[BracketMatch]:
        n = len(teams)
        slots = self._next_power_of_two(n)
        seeds = list(range(n)) + [None] * (slots - n)
        match_id = 1
        matches: List[BracketMatch] = []

        wb_rounds: List[List[BracketMatch]] = []
        round_count = int(round(slots.bit_length() - 1))
        current_count = slots // 2
        for r in range(1, round_count + 1):
            round_list = []
            for i in range(current_count):
                team_a = seeds[2 * i] if r == 1 else None
                team_b = seeds[2 * i + 1] if r == 1 else None
                match = BracketMatch(
                    id=match_id,
                    round=r,
                    slot=i + 1,
                    teamA=team_a,
                    teamB=team_b,
                    seedA=team_a,
                    seedB=team_b,
                    bracket="WB"
                )
                round_list.append(match)
                matches.append(match)
                match_id += 1
            wb_rounds.append(round_list)
            current_count //= 2

        for r, round_list in enumerate(wb_rounds[:-1], start=1):
            next_round = wb_rounds[r]
            for i, match in enumerate(round_list):
                target = next_round[i // 2]
                match.winnerToMatchId = target.id
                match.winnerToSlot = "A" if i % 2 == 0 else "B"

        lb_rounds: List[List[BracketMatch]] = []
        lb_round_count = max(1, 2 * (round_count - 1))
        for r in range(1, lb_round_count + 1):
            if r == 1:
                count = max(1, len(wb_rounds[0]) // 2)
            elif r % 2 == 0:
                count = len(wb_rounds[r // 2])
            else:
                count = max(1, len(lb_rounds[-1]) // 2)
            round_list = []
            for i in range(count):
                match = BracketMatch(
                    id=match_id,
                    round=r,
                    slot=i + 1,
                    bracket="LB"
                )
                round_list.append(match)
                matches.append(match)
                match_id += 1
            lb_rounds.append(round_list)

        if wb_rounds:
            for i, wb_match in enumerate(wb_rounds[0]):
                lb_match = lb_rounds[0][i // 2]
                wb_match.loserToMatchId = lb_match.id
                wb_match.loserToSlot = "A" if i % 2 == 0 else "B"

        for r in range(2, lb_round_count + 1, 2):
            wb_round = wb_rounds[r // 2]
            lb_round = lb_rounds[r - 1]
            for i, wb_match in enumerate(wb_round):
                lb_match = lb_round[i]
                wb_match.loserToMatchId = lb_match.id
                wb_match.loserToSlot = "B"
                prev_lb_match = lb_rounds[r - 2][i]
                prev_lb_match.winnerToMatchId = lb_match.id
                prev_lb_match.winnerToSlot = "A"

        for r in range(2, lb_round_count, 2):
            lb_round = lb_rounds[r - 1]
            next_round = lb_rounds[r]
            for i, lb_match in enumerate(lb_round):
                target = next_round[i // 2]
                lb_match.winnerToMatchId = target.id
                lb_match.winnerToSlot = "A" if i % 2 == 0 else "B"

        grand_final = BracketMatch(
            id=match_id,
            round=round_count + 1,
            slot=1,
            bracket="GF"
        )
        matches.append(grand_final)
        match_id += 1

        wb_final = wb_rounds[-1][0]
        wb_final.winnerToMatchId = grand_final.id
        wb_final.winnerToSlot = "A"
        if lb_rounds:
            lb_final = lb_rounds[-1][0]
            lb_final.winnerToMatchId = grand_final.id
            lb_final.winnerToSlot = "B"
            wb_final.loserToMatchId = lb_final.id
            wb_final.loserToSlot = "B"

        return matches

    def _generate_swiss(self, teams: List[BracketTeam]) -> List[BracketMatch]:
        n = len(teams)
        if n <= 8:
            rounds = 3
        elif n <= 16:
            rounds = 4
        else:
            rounds = 5
        matches: List[BracketMatch] = []
        match_id = 1
        for r in range(1, rounds + 1):
            slots = list(range(n))
            for i in range(0, len(slots), 2):
                team_a = slots[i]
                team_b = slots[i + 1] if i + 1 < len(slots) else None
                match = BracketMatch(
                    id=match_id,
                    round=r,
                    slot=(i // 2) + 1,
                    teamA=team_a,
                    teamB=team_b,
                    scoreA=0,
                    scoreB=0,
                    seedA=team_a,
                    seedB=team_b,
                    bracket="Swiss"
                )
                matches.append(match)
                match_id += 1
        return matches

    def _generate_round_robin(self, teams: List[BracketTeam]) -> List[BracketMatch]:
        matches: List[BracketMatch] = []
        match_id = 1
        groups: Dict[str, List[int]] = {}
        for idx, team in enumerate(teams):
            group = (team.group or "").strip() or "A"
            groups.setdefault(group, []).append(idx)

        for group_name in sorted(groups.keys()):
            group_team_ids = groups[group_name]
            group_matches = self._round_robin_pairings(group_team_ids)
            for round_num, round_pairs in enumerate(group_matches, start=1):
                for slot, (team_a, team_b) in enumerate(round_pairs, start=1):
                    match = BracketMatch(
                        id=match_id,
                        round=round_num,
                        slot=slot,
                        teamA=team_a,
                        teamB=team_b,
                        seedA=team_a,
                        seedB=team_b,
                        bracket="RR",
                        group=group_name
                    )
                    matches.append(match)
                    match_id += 1
        return matches

    def _round_robin_pairings(self, team_ids: List[int]) -> List[List[tuple]]:
        ids = team_ids[:]
        if len(ids) % 2 == 1:
            ids.append(None)
        n = len(ids)
        rounds = n - 1
        schedule = []
        for r in range(rounds):
            pairs = []
            for i in range(n // 2):
                a = ids[i]
                b = ids[n - 1 - i]
                if a is None or b is None:
                    continue
                pairs.append((a, b))
            schedule.append(pairs)
            ids = [ids[0]] + [ids[-1]] + ids[1:-1]
        return schedule

    def to_state(self) -> BracketState:
        return BracketState(
            settings=BracketSettings(
                bracket_type=self.bracket_type_combo.currentText(),
                team_count=int(self.team_count_spin.value()),
                display_stage=self.display_stage_combo.currentText(),
                display_group=self.display_group_combo.currentText(),
                display_round=self.display_round_combo.currentText()
            ),
            teams=self._collect_teams(),
            matches=self.matches[:]
        )

    def from_state(self, state: BracketState):
        self._loading_state = True
        self.bracket_type_combo.setCurrentText(state.settings.bracket_type or self.BRACKET_TYPES[0])
        self._last_bracket_type = self.bracket_type_combo.currentText()
        self.team_count_spin.setValue(int(getattr(state.settings, "team_count", 0) or 0) or len(state.teams) or 2)
        self.display_stage_combo.setCurrentText(getattr(state.settings, "display_stage", "All") or "All")
        for row in self._iter_team_rows():
            row.setParent(None)
            row.deleteLater()
        for team in state.teams:
            self._add_team_row(team)
        self._last_team_count = len(state.teams)
        self.matches = state.matches[:]
        for m in self.matches:
            if m.seedA is None:
                m.seedA = m.teamA
            if m.seedB is None:
                m.seedB = m.teamB
        self._rebuild_matches_ui()
        self._apply_progression()
        self._update_team_count_state()
        self._update_display_filters()
        self.display_group_combo.setCurrentText(getattr(state.settings, "display_group", "All") or "All")
        self.display_round_combo.setCurrentText(getattr(state.settings, "display_round", "All") or "All")
        self._loading_state = False


class BulkImportRow(QWidget):
    """Yksi rivi import-listassa."""
    def __init__(self, kind: str, file_path: str, name_guess: str, mode_names=None):
        super().__init__()
        self.kind = kind
        self.file_path = file_path

        row = QHBoxLayout(self)
        row.setContentsMargins(0, 0, 0, 0)

        self.chk = QCheckBox()
        self.chk.setChecked(True)
        self.fn_label = QLabel(os.path.basename(file_path))
        self.name_edit = QLineEdit(name_guess)

        row.addWidget(self.chk)
        row.addWidget(QLabel(kind), 0)
        row.addWidget(self.fn_label, 2)
        row.addWidget(QLabel("Name:"), 0)
        row.addWidget(self.name_edit, 2)

        self.mode_combo = None
        if kind == "Map":
            self.mode_combo = QComboBox()
            self.mode_combo.addItem("")
            for m in sorted(mode_names or []):
                self.mode_combo.addItem(m)
            row.addWidget(QLabel("Mode:"))
            row.addWidget(self.mode_combo, 1)

    def to_result(self):
        return {
            "enabled": self.chk.isChecked(),
            "kind": self.kind,
            "file_path": self.file_path,
            "name": self.name_edit.text().strip(),
            "mode": self.mode_combo.currentText().strip() if self.mode_combo else None,
        }


class BulkImportDialog(QDialog):
    """Listaa kansioista löytyneet kuvat. Nimet ja (karttojen) moodit voi muokata ennen tallennusta."""
    def __init__(self, parent, heroes_files: list, maps_files: list, mode_names: list):
        super().__init__(parent)
        self.setWindowTitle("Bulk Import from Folders")
        self.resize(820, 520)

        root = QVBoxLayout(self)

        info = QLabel("Review detected assets. Edit names (and modes for maps) before importing.")
        root.addWidget(info)

        self.container = QVBoxLayout()
        scroll_root = QWidget(); scroll_root.setLayout(self.container)
        scroll_area = QScrollArea(); scroll_area.setWidgetResizable(True); scroll_area.setWidget(scroll_root)
        root.addWidget(scroll_area, 1)

        self.rows: list[BulkImportRow] = []

        if heroes_files:
            self.container.addWidget(QLabel("Heroes"))
            for p, name_guess in heroes_files:
                r = BulkImportRow("Hero", p, name_guess)
                self.rows.append(r); self.container.addWidget(r)

        if maps_files:
            self.container.addWidget(QLabel("Maps"))
            for p, name_guess in maps_files:
                r = BulkImportRow("Map", p, name_guess, mode_names=mode_names)
                self.rows.append(r); self.container.addWidget(r)

        btns = QHBoxLayout()
        btns.addStretch(1)
        ok = QPushButton("Import"); cancel = QPushButton("Cancel")
        ok.clicked.connect(self.accept); cancel.clicked.connect(self.reject)
        btns.addWidget(cancel); btns.addWidget(ok)
        root.addLayout(btns)

    def results(self):
        return [r.to_result() for r in self.rows]

# -----------------------------
# Main Window
# -----------------------------
class TournamentApp(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("SOW Broadcast")
        self.resize(1400, 860)

        app_dir = QStandardPaths.writableLocation(QStandardPaths.AppDataLocation)
        if not app_dir:
            app_dir = os.path.join(os.path.expanduser("~"), ".ow_tournament_manager")
        self.app_dir = app_dir
        os.makedirs(self.app_dir, exist_ok=True)
        base_root = os.environ.get("SOWB_ROOT") or _app_base()
        self.autosave_path = os.path.join(base_root, "autosave.json")
        self.current_save_path: Optional[str] = None
        self.export_dir = os.path.join(self.app_dir, "exports")
        os.makedirs(self.export_dir, exist_ok=True)

        self.heroes: Dict[str, Asset] = {}
        self.maps: Dict[str, Asset] = {}
        self.modes: Dict[str, Asset] = {}

        self._build_menubar()

        central = QWidget(); self.setCentralWidget(central)
        root = QVBoxLayout(central)

        tabs = QTabWidget()
        root.addWidget(tabs)

        match_tab = QWidget()
        match_root = QVBoxLayout(match_tab)

        splitter = QSplitter()
        self.team1_panel = TeamPanel("Team 1", self._hero_names, default_color="#55aaff")
        self.team2_panel = TeamPanel("Team 2", self._hero_names, default_color="#ff557f")
        splitter.addWidget(self.team1_panel)
        splitter.addWidget(self.team2_panel)
        splitter.setSizes([700, 700])
        match_root.addWidget(splitter, 6)

        maps_box = QGroupBox("Maps")
        maps_layout = QVBoxLayout(maps_box)

        self.map_rows: List[MapRow] = []
        for i in range(1, 8):
            mr = MapRow(i, self._map_names, self._hero_names)
            self.map_rows.append(mr)
            maps_layout.addWidget(mr)

        current_row = QHBoxLayout()
        current_row.addWidget(QLabel("Current:"))
        self.current_map_buttons: List[QRadioButton] = []
        for i in range(1, 8):
            rb = QRadioButton(str(i))
            self.current_map_buttons.append(rb)
            current_row.addWidget(rb)
        current_row.addStretch()
        maps_layout.addLayout(current_row)

        match_root.addWidget(maps_box, 4)

        bottom = QHBoxLayout()
        self.reset_btn = QPushButton("Reset")
        self.reset_btn.clicked.connect(self._reset_all)
        self.swap_btn = QPushButton("Swap Teams")
        self.swap_btn.clicked.connect(self._swap_teams)
        self.update_btn = QPushButton("Update")
        self.update_btn.clicked.connect(self._update)
        bottom.addWidget(self.reset_btn)
        bottom.addWidget(self.swap_btn)
        bottom.addStretch(1)
        bottom.addWidget(self.update_btn)
        match_root.addLayout(bottom)

        tabs.addTab(match_tab, "Match")

        # --- GENERAL TAB ---
        self.general_tab = GeneralTab()
        self.general_tab.updated.connect(self._update_general_only)
        tabs.addTab(self.general_tab, "General")
        self.general_tab.from_settings(GeneralSettings())
        
        # --- WAITING TAB ---
        self.waiting_tab = WaitingTab()
        self.waiting_tab.updated.connect(self._update_waiting_only)
        tabs.addTab(self.waiting_tab, "Waiting Screen")

        
        # --- DRAFT TAB (map pool) ---
        self.draft_tab = DraftTab(self._maps_by_mode)
        self.draft_tab.updated.connect(self._update)
        tabs.addTab(self.draft_tab, "Draft")

        # --- BRACKETS TAB ---
        self.brackets_tab = BracketTab()
        self.brackets_tab.updated.connect(self._update_brackets_only)
        tabs.addTab(self.brackets_tab, "Brackets")
        
        self._ensure_default_assets_installed()
        self._auto_discover_assets()  

        self._load_autosave()
        self._last_state_for_diff = None
        self._start_replay_watcher()
        self._update()

    # ---------------------
    # Menubar and handlers
    # ---------------------
    
    def _ensure_default_assets_installed(self):
        """
        Ensimmäisellä käynnillä kopioi bundle-sisällöt käyttäjän Scoreboardiin.
        Jos käyttäjän kansiossa on index.json, oletetaan että lista on kuratoitu
        -> ei kopioida mitään takaisin (estää "zombie"-assetit).
        """
        user_root = self._scoreboard_root()
        bundled = _bundled_scoreboard_dir()
        if not bundled:
            return
        for sub in ("Maps", "Gametypes", "Heroes"):
            user_sub = os.path.join(user_root, sub)
            if os.path.isfile(os.path.join(user_sub, "index.json")):
                continue
            _copy_tree_if_missing(os.path.join(bundled, sub), user_sub)


    def _auto_discover_assets(self):
        import os
        import re

        def pick_dir(kind: str) -> str:
            dev = DEV_ASSET_DIRS.get(kind)
            if dev and os.path.isdir(dev):
                return dev

            b = _bundled_scoreboard_dir()
            sub = "Gametypes" if kind == "gametypes" else kind.capitalize()
            if b:
                cand = os.path.join(b, sub)
                if os.path.isdir(cand):
                    return cand

            user = os.path.join(self._scoreboard_root(), sub)
            return user

        loaded_maps   = self._load_assets_from_index("Maps", self.maps)
        loaded_heroes = self._load_assets_from_index("Heroes", self.heroes)
        loaded_modes  = self._load_assets_from_index("Gametypes", self.modes)

        if not loaded_heroes:
            heroes_dir = pick_dir("heroes")
            heroes_files = self._scan_image_files(heroes_dir)
            self.heroes.clear()
            for p, name in heroes_files:
                self.heroes[name] = Asset(
                    name=name,
                    image_path=os.path.join("Scoreboard", "Heroes", f"{self._slugify(name)}.png"),
                    source_path=p
                )

        if not loaded_maps:
            maps_dir = pick_dir("maps")
            maps_files = self._scan_image_files(maps_dir)
            self.maps.clear()
            for p, name in maps_files:
                self.maps[name] = Asset(
                    name=name,
                    image_path=os.path.join("Scoreboard", "Maps", f"{self._slugify(name)}.png"),
                    mode=None,
                    source_path=p
                )

        if not loaded_modes:
            modes_dir = pick_dir("gametypes")
            mode_files = self._scan_image_files(modes_dir)
            self.modes.clear()
            if mode_files:
                for p, name in mode_files:
                    self.modes[name] = Asset(
                        name=name,
                        image_path=os.path.join("Scoreboard", "Gametypes", f"{self._slugify(name)}.png"),
                        source_path=p
                    )
            else:
                if os.path.isdir(modes_dir):
                    for fn in sorted(os.listdir(modes_dir)):
                        stem, ext = os.path.splitext(fn)
                        if ext.lower() in {".txt", ".json"}:
                            name = re.sub(r"[-_]+", " ", stem).strip().title()
                            if name:
                                self.modes[name] = Asset(name=name)

        self._on_assets_changed()

        self._export_assets_category("Heroes", self.heroes)
        self._export_assets_category("Maps", self.maps)
        self._export_assets_category("Gametypes", self.modes)

    
    def _export_waiting(self, state: dict):
        root = self._scoreboard_root()
        wdir = os.path.join(root, "Waiting")
        pldir = os.path.join(wdir, "Playlist")
        os.makedirs(wdir, exist_ok=True)
        os.makedirs(pldir, exist_ok=True)

        w: dict = state.get("waiting") or {}
        ws = WaitingSettings(**w) if isinstance(w, dict) else WaitingSettings()

        self._write_txt(os.path.join(wdir, "text_starting.txt"), ws.text_starting or "STARTING SOON!")
        self._write_txt(os.path.join(wdir, "text_brb.txt"),      ws.text_brb      or "BE RIGHT BACK!")
        self._write_txt(os.path.join(wdir, "text_end.txt"),      ws.text_end      or "THANK YOU FOR WATCHING")
        self._write_txt(os.path.join(wdir, "timer_seconds.txt"), str(int(ws.timer_seconds or 0)))
        
        ticker_override = getattr(ws, "ticker_override", "") or ""
        use_override = bool(getattr(ws, "ticker_override_enabled", False) and ticker_override.strip())
        self._write_txt(os.path.join(wdir, "ticker_override.txt"), ticker_override)
        self._write_txt(
            os.path.join(wdir, "ticker_use_override.txt"),
            "1" if use_override else "0"
        )
        
        import json as _json
        soc = getattr(ws, "socials", {}) or {}
        try:
            with open(os.path.join(wdir, "socials.json"), "w", encoding="utf-8") as f:
                _json.dump(soc, f, ensure_ascii=False)
        except Exception:
            pass

        base = os.environ.get("SOWB_ROOT") or _app_base()
        def_dir1 = os.path.join(base, "SOWBroadcast", "Highlights")
        def_dir2 = os.path.join(base, "Highlights")
        default_dir = def_dir1 if os.path.isdir(def_dir1) else def_dir2 if os.path.isdir(def_dir2) else ""
        src_dir = (ws.videos_dir or "").strip() or default_dir

        exts = {".mp4", ".mov", ".webm", ".mkv"}
        filenames = []

        if src_dir and os.path.isdir(src_dir):
            for fn in sorted(os.listdir(src_dir)):
                if os.path.splitext(fn)[1].lower() in exts:
                    filenames.append(fn)
                    src = os.path.join(src_dir, fn)
                    dst = os.path.join(pldir, fn)
                    try:
                        if (not os.path.exists(dst)) or (os.path.getmtime(src) > os.path.getmtime(dst)):
                            shutil.copy2(src, dst)
                    except Exception:
                        pass

        for old in os.listdir(pldir):
            if old not in filenames:
                try: os.remove(os.path.join(pldir, old))
                except Exception: pass

        self._write_txt(os.path.join(wdir, "videos.txt"), "\n".join(filenames) + ("\n" if filenames else ""))
        self._write_txt(os.path.join(wdir, "videos_dir.txt"), "")
        self._write_txt(os.path.join(wdir, "timer_running.txt"),
                "1" if bool(ws.timer_running) else "0")

    def _name_from_filename(self, path: str) -> str:
        """Ei kovakoodattuja korjauksia: vain väliviivat/alikulkevat -> välilyönti, ja title case."""
        stem = os.path.splitext(os.path.basename(path))[0]
        raw = re.sub(r"[-_]+", " ", stem).strip()
        return raw.title()

    def _scan_image_files(self, folder: str) -> list[tuple[str, str]]:
        """Palauttaa [(abspath, name_guess)] folderista, ilman mitään poikkeuslistoja."""
        out = []
        if not os.path.isdir(folder):
            return out
        for fn in os.listdir(folder):
            ext = os.path.splitext(fn)[1].lower()
            if ext in {".png", ".jpg", ".jpeg", ".webp"}:
                p = os.path.join(folder, fn)
                out.append((p, self._name_from_filename(p)))
        return out

    def _bulk_import_wizard(self):
        base = os.environ.get("SOWB_ROOT") or _app_base()
        heroes_dir = os.path.join(base, "Scoreboard", "Heroes")
        maps_dir   = os.path.join(base, "Scoreboard", "Maps")

        heroes_files = self._scan_image_files(heroes_dir)
        maps_files   = self._scan_image_files(maps_dir)

        existing_hero_names = set(self.heroes.keys())
        heroes_files = [(p, n if n not in existing_hero_names else n) for (p, n) in heroes_files]

        existing_map_names = set(self.maps.keys())
        maps_files = [(p, n if n not in existing_map_names else n) for (p, n) in maps_files]

        mode_names = list(self.modes.keys())

        dlg = BulkImportDialog(self, heroes_files, maps_files, mode_names)
        if dlg.exec_() != QDialog.Accepted:
            return

        results = dlg.results()
        added_h = added_m = 0
        for r in results:
            if not r["enabled"]:
                continue
            name = r["name"]
            if not name:
                continue
            if r["kind"] == "Hero":
                if name in self.heroes:
                    continue
                self.heroes[name] = Asset(
                    name=name,
                    image_path=os.path.join("Scoreboard", "Heroes", f"{self._slugify(name)}.png"),
                    source_path=r["file_path"]
                )
                added_h += 1
            else:
                if name in self.maps:
                    continue
                mode = (r.get("mode") or "").strip() or None
                self.maps[name] = Asset(
                    name=name,
                    image_path=os.path.join("Scoreboard", "Maps", f"{self._slugify(name)}.png"),
                    mode=mode,
                    source_path=r["file_path"]
                )
                added_m += 1

        self._on_assets_changed()
        QMessageBox.information(self, "Bulk Import",
                                f"Imported {added_h} heroes and {added_m} maps.\n"
                                "You can still edit them anytime in the Managers.")


    def _build_menubar(self):
        mb = self.menuBar()
        filem = mb.addMenu("File")
        customm = mb.addMenu("Customize")
        teamsm = mb.addMenu("Teams")

        act_hero = QAction("Manage Heroes…", self)
        act_hero.triggered.connect(lambda: self._open_asset_manager("Heroes", self.heroes, self._on_assets_changed))
        act_map = QAction("Manage Maps…", self)
        act_map.triggered.connect(lambda: self._open_asset_manager("Maps", self.maps, self._on_assets_changed))
        act_mode = QAction("Manage Game Modes…", self)
        act_mode.triggered.connect(lambda: self._open_asset_manager("Game Modes", self.modes, self._on_assets_changed))
        
        act_bulk_import = QAction("Bulk Import from Folders…", self)
        act_bulk_import.triggered.connect(self._bulk_import_wizard)

        customm.addAction(act_hero)
        customm.addAction(act_map)
        customm.addAction(act_mode)
        customm.addSeparator()
        customm.addAction(act_bulk_import)        

        act_load = QAction("Load…", self); act_load.triggered.connect(self._load_from_file)
        act_save = QAction("Save", self); act_save.triggered.connect(self._save)
        act_saveas = QAction("Save As…", self); act_saveas.triggered.connect(self._save_as)
        filem.addAction(act_load)
        filem.addAction(act_save)
        filem.addAction(act_saveas)
        filem.addSeparator()
        act_quit = QAction("Quit", self)
        act_quit.triggered.connect(self.close)
        filem.addAction(act_quit)
        
        act_ex_home = QAction("Export Home…", self)
        act_ex_home.triggered.connect(lambda: self._export_team_dialog(self.team1_panel))
        act_im_home = QAction("Import Home…", self)
        act_im_home.triggered.connect(lambda: self._import_team_dialog(self.team1_panel))

        act_ex_away = QAction("Export Away…", self)
        act_ex_away.triggered.connect(lambda: self._export_team_dialog(self.team2_panel))
        act_im_away = QAction("Import Away…", self)
        act_im_away.triggered.connect(lambda: self._import_team_dialog(self.team2_panel))

        teamsm.addAction(act_ex_home)
        teamsm.addAction(act_im_home)
        teamsm.addSeparator()
        teamsm.addAction(act_ex_away)
        teamsm.addAction(act_im_away)

    def _open_asset_manager(self, title: str, store: Dict[str, Asset], on_close):
        mode_names = None
        if title == "Maps":
            mode_names = list(self.modes.keys())
        dlg = AssetManagerDialog(self, title, store, mode_names=mode_names)
        dlg.exec_()
        on_close()

    def _maps_by_mode(self) -> dict:
        """
        Palauta OrderedDict/dict: mode -> [map-names].
        Jos kartalla ei ole asetettua modea, laitetaan 'Unspecified' alle.
        Moodien järjestys otetaan self.modes-assetlistasta, lopuksi lisätään Unspecified jos tarpeen.
        """
        from collections import OrderedDict
        by_mode = OrderedDict()
        for m in self.modes.keys():
            by_mode[m] = []
        by_mode.setdefault("Unspecified", [])
        for name, asset in self.maps.items():
            mode = (asset.mode or "").strip() or "Unspecified"
            by_mode.setdefault(mode, [])
            by_mode[mode].append(name)
        cleaned = OrderedDict((k, v) for k, v in by_mode.items() if v)
        return cleaned


    def _on_assets_changed(self):
        self.team1_panel.refresh_hero_lists()
        self.team2_panel.refresh_hero_lists()
        for mr in self.map_rows:
            mr.refresh_maps()
            mr.refresh_hero_list(mr.t1ban)
            mr.refresh_hero_list(mr.t2ban)
        if hasattr(self, "draft_tab"):
            self.draft_tab.reload()

    def _hero_names(self) -> List[str]:
        return sorted(self.heroes.keys())

    def _map_names(self) -> List[str]:
        return sorted(self.maps.keys())
    
    def _write_txt(self, path: str, text: str) -> bool:
        """
        Kirjoittaa tiedoston vain, jos sisältö oikeasti muuttuisi.
        Palauttaa True jos kirjoitettiin, False jos ohitettiin.
        """
        os.makedirs(os.path.dirname(path), exist_ok=True)
        new = text or ""
        try:
            with open(path, "r", encoding="utf-8") as f:
                old = f.read()
            if old == new:
                return False
        except FileNotFoundError:
            pass
        with open(path, "w", encoding="utf-8") as f:
            f.write(new)
        return True

            
    def build_match_text(self, state: dict) -> str:
        """
        Muoto: Team1 (t1Total - t2Total) Team2    -    Map1 (x - y)    -    Map2 (...) ...
        Kirjoittaa kaikki GUIhin syötetyt kartat järjestyksessä. Ottaa map-nimen vain jos se on annettu.
        """
        t1 = state.get("team1", {}) or {}
        t2 = state.get("team2", {}) or {}
        maps = state.get("maps", []) or []

        t1_name = (t1.get("name") or "").strip()
        t2_name = (t2.get("name") or "").strip()
        t1_total = str(t1.get("score", 0))
        t2_total = str(t2.get("score", 0))

        parts = [f"{t1_name} ({t1_total} - {t2_total}) {t2_name}"]

        for item in maps:
            if not item:
                continue
            name = (item.get("map") or "").strip()
            m1 = int(item.get("t1") or 0)
            m2 = int(item.get("t2") or 0)
            if name:
                parts.append(f"{name} ({m1} - {m2})")

        sep = "      •      "
        return sep.join(parts).strip() + "                              "

    def _teams_dir(self) -> str:
        """Scoreboard/Teams kansio."""
        root = self._scoreboard_root()
        d = os.path.join(root, "Teams")
        os.makedirs(d, exist_ok=True)
        return d

    def _export_team_dialog(self, panel: 'TeamPanel'):
        """Exporttaa yhden tiimin JSON + logon PNG:nä. Logon tiedostonimi = tiimin nimi (slug)."""
        t = panel.to_team()

        t.score = 0
        t.banned_hero = ""

        base_slug = self._slugify(t.name or "team")
        default_json = os.path.join(self._teams_dir(), f"{base_slug}.sowteam.json")

        path, _ = QFileDialog.getSaveFileName(
            self, "Export Team", default_json,
            "SOW Team (*.sowteam.json);;JSON (*.json)"
        )
        if not path:
            return
        if not path.lower().endswith(".json"):
            path += ".sowteam.json"

        out_dir = os.path.dirname(path)

        logo_name = f"{base_slug}.png"
        logo_path_out = os.path.join(out_dir, logo_name)

        data = {
            "version": 1,
            "name": t.name,
            "abbr": t.abbr,
            "logo_png": logo_name if t.logo_path else None,
            "players": [
                {"name": p.name, "hero": p.hero, "role": p.role}
                for p in (t.players or [])
            ],
        }

        with open(path, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=2)

        if t.logo_path:
            try:
                self._save_pixmap_as_png(t.logo_path, logo_path_out)
            except Exception as e:
                QMessageBox.warning(self, "Export", f"Logo export failed:\n{e}")


    def _import_team_dialog(self, panel: 'TeamPanel'):
        """Lataa yhden tiimin. Ei ylikirjoita scorea eikä bännättyä hero a."""
        start = self._teams_dir()
        path, _ = QFileDialog.getOpenFileName(self, "Import Team", start,
                                              "SOW Team (*.json *.json);;All files (*.*)")
        if not path:
            return
        try:
            with open(path, "r", encoding="utf-8") as f:
                data = json.load(f)
        except Exception as e:
            QMessageBox.warning(self, "Import failed", str(e))
            return

        keep_score = panel.score.value()

        players = []
        for p in data.get("players", []):
            players.append(Player(name=p.get("name",""), hero=p.get("hero",""), role=p.get("role","")))
        while len(players) < 8:
            players.append(Player())

        t = Team(
            name=data.get("name",""),
            abbr=data.get("abbr",""),
            logo_path=None,
            score=keep_score,
            players=players,
            banned_hero=""
        )

        logo_rel = data.get("logo_png")
        cand = None
        if logo_rel:
            cand = os.path.join(os.path.dirname(path), logo_rel)
            if not os.path.exists(cand):
                cand = None

        if not cand:
            base_noext = os.path.splitext(os.path.basename(path))[0]
            try_candidates = [
                os.path.join(os.path.dirname(path), f"{base_noext}.png"),
                os.path.join(os.path.dirname(path), "Logo.png"),
            ]
            for c in try_candidates:
                if os.path.exists(c):
                    cand = c
                    break

        if cand:
            t.logo_path = cand
            t.color_hex = getattr(panel, "default_color", "#FFFFFF")
            panel.from_team(t)
            self._autosave()

    def _export_status_text(self, state: dict):
        """Kirjoita käyttäjän asettama status-teksti Scoreboard/Match/status.txt"""
        match_dir = os.path.join(self._scoreboard_root(), "Match")
        os.makedirs(match_dir, exist_ok=True)
        general = state.get("general", {}) or {}
        text = general.get("status_text", "").strip()
        self._write_txt(os.path.join(match_dir, "status.txt"), text)


    def _replay_dirs(self):
        """Palauttaa (replay_dir, playlist_dir) ja varmistaa, että ne ovat olemassa."""
        root = os.path.join(self._scoreboard_root(), "Replay")
        playlist = os.path.join(root, "Playlist")
        os.makedirs(playlist, exist_ok=True)
        return root, playlist

    def _write_replay_pointer(self, fname: str):
        """Kirjoita viimeisin toistettava filename (vain nimi, ei polkua)."""
        replay_dir, _ = self._replay_dirs()
        path = os.path.join(replay_dir, "replaypath.txt")
        os.makedirs(replay_dir, exist_ok=True)
        with open(path, "w", encoding="utf-8") as f:
            f.write(fname.strip())

    def _next_replay_number(self, playlist_dir: str) -> int:
        """Etsi suurin n- prefiksistä ja palauta n+1."""
        nmax = 0
        for name in os.listdir(playlist_dir):
            m = re.match(r"^(\d+)-", name)
            if m:
                try:
                    n = int(m.group(1))
                    if n > nmax:
                        nmax = n
                except ValueError:
                    pass
        return nmax + 1

    def _start_replay_watcher(self):
        """Käynnistä taustasäie, joka vahtii Replay Replay.mp4 -tiedostoa."""
        if getattr(self, "_replay_thread_started", False):
            return
        self._replay_thread_started = True
        t = threading.Thread(target=self._replay_watcher, name="ReplayWatcher", daemon=True)
        t.start()

    def _replay_watcher(self):
        """Pollaa 0.5s välein Scoreboard/Replay/Replay Replay.mp4.
        Kopioi Playlistiin numeroprefiksillä *vain kerran* jokaista tiedoston (mtime,size) -sisältöä kohden.
        """
        replay_dir, playlist_dir = self._replay_dirs()
        src = os.path.join(replay_dir, "Replay Replay.mp4")

        observed_sig = None
        last_copied_sig = None
        stable_hits = 0

        while True:
            try:
                if os.path.isfile(src):
                    st = os.stat(src)
                    sig = (int(st.st_mtime), st.st_size)

                    if sig != observed_sig:
                        observed_sig = sig
                        stable_hits = 1
                    else:
                        stable_hits += 1

                    if stable_hits >= 3 and sig != last_copied_sig:
                        n = self._next_replay_number(playlist_dir)
                        base_hyphen = os.path.basename(src).replace(" ", "-")
                        dst_name = f"{n}-{base_hyphen}"
                        dst = os.path.join(playlist_dir, dst_name)
                        try:
                            shutil.copy2(src, dst)
                            self._write_replay_pointer(dst_name)
                            last_copied_sig = sig
                        except Exception:
                            pass
                else:
                    observed_sig = None
                    stable_hits = 0
            except Exception:
                pass

            time.sleep(0.5)

    def _diff_for_scoreboard(self, old: dict, new: dict):
        keys = []
        if not old:
            return [
                "assets.heroes", "assets.maps", "assets.modes",
                "general.colors",
                "t1.name","t1.score","t1.color","t1.logo","t1.ban","t1.abbr","t1.players",
                "t2.name","t2.score","t2.color","t2.logo","t2.ban","t2.abbr","t2.players",
                "general.caster1","general.caster2","general.host",
                "waiting.texts","waiting.timer","waiting.videos","waiting.socials",
                "maps",
                "brackets"
            ]

        o1, n1 = old.get("team1", {}), new.get("team1", {})
        o2, n2 = old.get("team2", {}), new.get("team2", {})
        og, ng = old.get("general", {}), new.get("general", {})

        oa, na = old.get("assets", {}) or {}, new.get("assets", {}) or {}
        for cat in ("heroes", "maps", "modes"):
            od = oa.get(cat) or {}
            nd = na.get(cat) or {}
            changed = False
            if od.keys() != nd.keys():
                changed = True
            else:
                for k in od.keys():
                    a, b = od.get(k) or {}, nd.get(k) or {}
                    if (a.get("name") != b.get("name")) \
                       or (a.get("image_path") != b.get("image_path")) \
                       or (a.get("mode") != b.get("mode")):
                        changed = True

                        break
            if changed:
                keys.append(f"assets.{cat}")

        if (og.get("colors") or {}) != (ng.get("colors") or {}):
            keys.append("general.colors")

        ow, nw = old.get("waiting", {}) or {}, new.get("waiting", {}) or {}

        if ((ow.get("text_starting") or "").strip() != (nw.get("text_starting") or "").strip() or
            (ow.get("text_brb")      or "").strip() != (nw.get("text_brb")      or "").strip() or
            (ow.get("text_end")      or "").strip() != (nw.get("text_end")      or "").strip()):
            keys.append("waiting.texts")

        if ((ow.get("timer_seconds") or 0) != (nw.get("timer_seconds") or 0) or
            bool(ow.get("timer_running")) != bool(nw.get("timer_running"))):
            keys.append("waiting.timer")

        if (ow.get("videos_dir") or "").strip() != (nw.get("videos_dir") or "").strip():
            keys.append("waiting.videos")
            
        if (ow.get("socials") or {}) != (nw.get("socials") or {}):
            keys.append("waiting.socials")

        def cmp_team(prefix, o, n):
            if o.get("name") != n.get("name"): keys.append(f"{prefix}.name")
            if o.get("score") != n.get("score"): keys.append(f"{prefix}.score")
            if o.get("color_hex") != n.get("color_hex"): keys.append(f"{prefix}.color")
            if o.get("logo_path") != n.get("logo_path"): keys.append(f"{prefix}.logo")
            if o.get("banned_hero") != n.get("banned_hero"): keys.append(f"{prefix}.ban")
            if o.get("abbr") != n.get("abbr"): keys.append(f"{prefix}.abbr")

        def _players_changed(o_team: dict, n_team: dict) -> bool:
            ol = o_team.get("players") or []
            nl = n_team.get("players") or []
            if len(ol) != len(nl):
                return True
            for a, b in zip(ol, nl):
                if ((a.get("name") or "").strip() != (b.get("name") or "").strip() or
                    (a.get("role") or "").strip() != (b.get("role") or "").strip() or
                    (a.get("hero") or "").strip() != (b.get("hero") or "").strip()):
                    return True
            return False

        cmp_team("t1", o1, n1)
        cmp_team("t2", o2, n2)
        if _players_changed(o1, n1): keys.append("t1.players")
        if _players_changed(o2, n2): keys.append("t2.players")

        go, gn = og, ng
        if (go.get("caster1") or "").strip() != (gn.get("caster1") or "").strip():
            keys.append("general.caster1")
        if (go.get("caster2") or "").strip() != (gn.get("caster2") or "").strip():
            keys.append("general.caster2")
        if (go.get("host") or "").strip() != (gn.get("host") or "").strip():
            keys.append("general.host")

        if old.get("current_map") != new.get("current_map"):
            keys.append("maps")
        om, nm = old.get("maps") or [], new.get("maps") or []
        if len(om) != len(nm):
            keys.append("maps")
        else:
            for a, b in zip(om, nm):
                if (a.get("map"), a.get("t1"), a.get("t2"), a.get("completed"), a.get("pick"),
                    a.get("t1_ban"), a.get("t2_ban")) != \
                   (b.get("map"), b.get("t1"), b.get("t2"), b.get("completed"), b.get("pick"),
                    b.get("t1_ban"), b.get("t2_ban")):
                    keys.append("maps"); break

        ob = old.get("brackets") or {}
        nb = new.get("brackets") or {}
        if ob != nb:
            keys.append("brackets")

        return keys

    def _load_assets_from_index(self, category: str, target_dict: dict) -> bool:
        """
        Lataa Scoreboard/<category>/index.json ja täyttää target_dict:
        - category: "Maps" | "Heroes" | "Gametypes"
        Palauttaa True jos lataus onnistui, muuten False.
        """
        root = self._scoreboard_root()
        cat_dir = os.path.join(root, category)
        p = os.path.join(cat_dir, "index.json")
        if not os.path.isfile(p):
            return False

        try:
            with open(p, "r", encoding="utf-8") as f:
                data = json.load(f)

            key = "maps" if category == "Maps" else "heroes" if category == "Heroes" else "modes"
            rows = data.get(key, [])
            target_dict.clear()

            for it in rows:
                name = (it.get("name") or "").strip()
                img_rel = (it.get("image") or "").replace("/", os.sep)
                img_abs = os.path.join(root, img_rel) if img_rel else None
                mode = it.get("mode") if category == "Maps" else None

                if not name:
                    continue

                target_dict[name] = Asset(
                    name=name,
                    image_path=img_rel if img_rel else None,
                    mode=mode,
                    source_path=img_abs if (img_abs and os.path.isfile(img_abs)) else None
                )
            return True
        except Exception:
            return False



    def _export_map_pool_to_match(self, state: dict):
        """
        Kirjoita Scoreboard/Match/maps.txt poolin perusteella.
        Jos pool on tyhjä -> käytä kaikkia nykyisiä kartta-asset-nimiä.
        Tiedoston rivit ovat kuvatiedostojen nimiä (slug + .png),
        jotka vastaavat _export_assets_category('Maps', ...) -outputteja.
        """
        root = self._scoreboard_root()
        out_path = os.path.join(root, "Match", "maps.txt")
        os.makedirs(os.path.dirname(out_path), exist_ok=True)

        pool = state.get("map_pool") or []
        names = pool if pool else sorted(self.maps.keys())

        files = [f"{self._slugify(name)}.png" for name in names if name in self.maps]

        txt = "\n".join(files) + ("\n" if files else "")
        self._write_txt(out_path, txt)

        names_txt = os.path.join(root, "Match", "map_pool.txt")
        self._write_txt(names_txt, "\n".join(names) + ("\n" if names else ""))


    def _notify_overlays(self, changed_keys: list):
        """POST /notify -> paikallispalvelin pushaa SSE-viestin."""
        if not changed_keys:
            return
        try:
            import urllib.request, json
            data = json.dumps({"changed": changed_keys}).encode("utf-8")
            req = urllib.request.Request(
                "http://127.0.0.1:8324/notify",
                data=data,
                headers={"Content-Type":"application/json"}
            )
            urllib.request.urlopen(req, timeout=0.5).read()
        except Exception:
            pass

    def _scoreboard_root(self):
        base = os.environ.get("SOWB_ROOT") or _app_base()
        root = os.path.join(base, "Scoreboard")
        _ensure_scoreboard_tree(root)
        try:
            with open(os.path.join(root, "__last_gui_touch.txt"), "w", encoding="utf-8") as f:
                f.write("ok")
        except Exception:
            pass
        return root

    @staticmethod
    def _slugify(name: str) -> str:
        s = unicodedata.normalize("NFKD", (name or "").strip().lower())
        s = "".join(ch for ch in s if not unicodedata.combining(ch))
        s = re.sub(r"[^a-z0-9]+", "-", s)
        s = re.sub(r"-{2,}", "-", s).strip("-")
        return s or "item"

    @staticmethod
    def _ensure_dir(p: str):
        os.makedirs(p, exist_ok=True)
   
    def _save_pixmap_as_png(self, src_path: Optional[str], dst_path: str, *, force: bool = False):
        if not src_path:
            return
        if not force:
            try:
                if os.path.exists(dst_path) and os.path.getmtime(dst_path) >= os.path.getmtime(src_path):
                    return
            except OSError:
                pass
        pix = QPixmap(src_path)
        if not pix.isNull():
            pix.save(dst_path, "PNG")

    def _export_assets_category(self, category_name: str, assets: Dict[str, Asset]):
        """
        Kirjoittaa Scoreboard/<Category>/index.txt ja kuvat PNG:nä:
        Scoreboard/<Category>/<slug>.png
        """
        root = self._scoreboard_root()
        cat_dir = os.path.join(root, category_name)
        self._ensure_dir(cat_dir)

        index_path = os.path.join(cat_dir, "index.txt")
        with open(index_path, "w", encoding="utf-8") as f:
            for name in sorted(assets.keys()):
                f.write(name + "\n")

        for name, asset in assets.items():
            slug = self._slugify(name)
            out_png = os.path.join(cat_dir, f"{slug}.png")

            src = None
            if asset.source_path and os.path.exists(asset.source_path):
                src = asset.source_path
            elif asset.image_path and os.path.exists(asset.image_path) \
                 and os.path.abspath(asset.image_path) != os.path.abspath(out_png):
                src = asset.image_path

            if not src:
                continue

            try:
                need_copy = True
                try:
                    st_src = os.stat(src)
                    st_dst = os.stat(out_png)
                    if st_dst.st_size == st_src.st_size and int(st_dst.st_mtime) >= int(st_src.st_mtime):
                        need_copy = False
                except FileNotFoundError:
                    pass

                if need_copy:
                    shutil.copy2(src, out_png)
            except Exception as e:
                print(f"[Maps] copy failed {src} -> {out_png}: {e}")

        if category_name in {"Maps", "Heroes", "Gametypes"}:
            items = []
            for name, asset in assets.items():
                slug = self._slugify(name)
                out_png = os.path.join(cat_dir, f"{slug}.png")
                img_rel = _norm_rel(out_png, root)

                item = {"name": name, "slug": slug, "image": img_rel}
                if category_name == "Maps":
                    item["mode"] = (asset.mode or "")
                items.append(item)

            index_json_path = os.path.join(cat_dir, "index.json")
            payload = (
                {"maps": items} if category_name == "Maps" else
                {"heroes": items} if category_name == "Heroes" else
                {"modes": items}
            )
            with open(index_json_path, "w", encoding="utf-8") as f:
                json.dump(payload, f, ensure_ascii=False, indent=2)


    def _export_general(self, settings: 'GeneralSettings'):
        root = self._scoreboard_root()
        gen_dir = os.path.join(root, "General")
        self._ensure_dir(gen_dir)

        self._write_txt(os.path.join(gen_dir, "host.txt"),     settings.host or "")
        self._write_txt(os.path.join(gen_dir, "caster1.txt"),  settings.caster1 or "")
        self._write_txt(os.path.join(gen_dir, "caster2.txt"),  settings.caster2 or "")
        self._write_txt(os.path.join(gen_dir, "first_to.txt"), str(settings.first_to))

        with open(os.path.join(gen_dir, "colors.txt"), "w", encoding="utf-8") as f:
            for k, v in (settings.colors or {}).items():
                f.write(f"{k}={v}\n")

        self._save_pixmap_as_png(settings.overlay_logo_path,    os.path.join(gen_dir, "OverlayLogo.png"),    force=True)
        self._save_pixmap_as_png(settings.transition_logo_path, os.path.join(gen_dir, "TransitionLogo.png"), force=True)

    def _export_scoreboard(self, state: dict):
        self._export_assets_category("Heroes", self.heroes)
        self._export_assets_category("Maps", self.maps)
        self._export_assets_category("Gametypes", self.modes)

        g = state.get("general") or {}
        settings = GeneralSettings(**g) if isinstance(g, dict) else GeneralSettings()
        self._export_general(settings)
        
        self._export_waiting(state)
    
    def _export_match(self, state: dict):
        root = self._scoreboard_root()
        match_dir = os.path.join(root, "Match")
        self._ensure_dir(match_dir)

        def write_team_flat(prefix: str, team: dict):
            self._write_txt(os.path.join(match_dir, f"{prefix}Name.txt"),  team.get("name", "") or "")
            self._write_txt(os.path.join(match_dir, f"{prefix}Score.txt"), str(team.get("score", 0)))
            self._write_txt(os.path.join(match_dir, f"{prefix}Color.txt"), team.get("color_hex", "") or "")
            self._write_txt(os.path.join(match_dir, f"{prefix}Abbr.txt"),  team.get("abbr", "") or "")

            lines = []
            for i, p in enumerate(team.get("players") or [], start=1):
                name = (p.get("name") or "").replace("\t", " ")
                hero = (p.get("hero") or "").replace("\t", " ")
                role = (p.get("role") or "").replace("\t", " ")
                lines.append(f"{i}\t{name}\t{hero}\t{role}")
            self._write_txt(os.path.join(match_dir, f"{prefix}Players.txt"), "\n".join(lines) + ("\n" if lines else ""))

            logo_src = team.get("logo_path")
            self._save_pixmap_as_png(logo_src, os.path.join(match_dir, f"{prefix}Logo.png"), force=True)

        t1 = state.get("team1") or {}
        t2 = state.get("team2") or {}
        write_team_flat("T1", t1)
        write_team_flat("T2", t2)

        cur = state.get("current_map")
        self._write_txt(os.path.join(match_dir, "CurrentMap.txt"), "" if cur is None else str(cur))

        maps = state.get("maps") or []
        for m in maps:
            idx = int(m.get("index", 0)) or 0
            if idx <= 0:
                continue
            name = (m.get("map") or "").replace("\n", " ").strip()
            t1s = int(m.get("t1", 0)) if str(m.get("t1", "")).isdigit() else 0
            t2s = int(m.get("t2", 0)) if str(m.get("t2", "")).isdigit() else 0
            comp = 1 if m.get("completed", False) else 0
            map_name = (m.get("map") or "").replace("\n", " ").strip()
            body = (
                f"Name={map_name}\n"
                f"T1={t1s}\n"
                f"T2={t2s}\n"
                f"Completed={comp}\n"
                f"Pick={(m.get('pick') or '')}\n"
                f"T1Ban={(m.get('t1_ban') or '')}\n"
                f"T2Ban={(m.get('t2_ban') or '')}\n"
            )
            self._write_txt(os.path.join(match_dir, f"Map{idx}.txt"), body)
        self._write_txt(os.path.join(match_dir, "matchtext.txt"), self.build_match_text(state))

        with open(os.path.join(match_dir, "match.json"), "w", encoding="utf-8") as f:
            json.dump({k: v for k, v in state.items() if k != "assets"}, f, ensure_ascii=False, indent=2)

        t1_players = (state.get("team1") or {}).get("players") or []
        for i in range(8):
            p = t1_players[i] if i < len(t1_players) else {}
            self._write_txt(os.path.join(match_dir, f"T1P{i+1}Name.txt"), (p.get("name") or "").strip())
            self._write_txt(os.path.join(match_dir, f"T1P{i+1}Role.txt"), (p.get("role") or "").strip())
            self._write_txt(os.path.join(match_dir, f"T1P{i+1}Hero.txt"), (p.get("hero") or "").strip())

        t2_players = (state.get("team2") or {}).get("players") or []
        for i in range(8):
            p = t2_players[i] if i < len(t2_players) else {}
            self._write_txt(os.path.join(match_dir, f"T2P{i+1}Name.txt"), (p.get("name") or "").strip())
            self._write_txt(os.path.join(match_dir, f"T2P{i+1}Role.txt"), (p.get("role") or "").strip())
            self._write_txt(os.path.join(match_dir, f"T2P{i+1}Hero.txt"), (p.get("hero") or "").strip())
            
        self._export_map_pool_to_match(state)

    def _export_brackets(self, state: dict):
        root = self._scoreboard_root()
        bdir = os.path.join(root, "Brackets")
        logos_dir = os.path.join(bdir, "Logos")
        os.makedirs(bdir, exist_ok=True)
        os.makedirs(logos_dir, exist_ok=True)

        bracket = state.get("brackets") or {}
        settings = bracket.get("settings") or {}
        teams = bracket.get("teams") or []
        matches = bracket.get("matches") or []

        out_teams = []
        for idx, t in enumerate(teams):
            name = (t.get("name") or "").strip()
            abbr = (t.get("abbr") or "").strip()
            group = (t.get("group") or "").strip()
            logo_path = t.get("logo_path")
            logo_rel = None
            if logo_path:
                slug = self._slugify(name or abbr or f"team-{idx+1}")
                out_png = os.path.join(logos_dir, f"{slug}.png")
                self._save_pixmap_as_png(logo_path, out_png, force=True)
                logo_rel = f"Logos/{slug}.png"
            out_teams.append({
                "name": name,
                "abbr": abbr,
                "group": group,
                "logo": logo_rel
            })

        payload = {
            "settings": settings,
            "teams": out_teams,
            "matches": matches,
        }

        json_path = os.path.join(bdir, "bracket.json")
        with open(json_path, "w", encoding="utf-8") as f:
            json.dump(payload, f, ensure_ascii=False, indent=2)

        html_path = os.path.join(bdir, "brackets.html")
        html = self._brackets_html_template(payload)
        self._write_txt(html_path, html)

    def _brackets_html_template(self, payload: dict) -> str:
        base = os.environ.get("SOWB_ROOT") or _app_base()
        template_path = os.path.join(base, "HTML", "brackets.html")
        try:
            with open(template_path, "r", encoding="utf-8") as f:
                return f.read()
        except Exception:
            inline_json = json.dumps(payload, ensure_ascii=False).replace("</", "<\\/")
            fallback = (
                "<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>"
                "<title>Brackets</title></head><body>"
                "<pre id=\"inline\"></pre>"
                "<script>document.getElementById('inline').textContent="
                f"{json.dumps(inline_json)};</script></body></html>"
            )
            return fallback

    # ---------------------
    # Actions: Reset & Swap
    # ---------------------
    def _reset_all(self):
        self.team1_panel.reset()
        self.team2_panel.reset()
        self.team1_panel.color_hex = "#55aaff"; self.team1_panel._apply_color_style()
        self.team2_panel.color_hex = "#ff557f"; self.team2_panel._apply_color_style()
        for i, rb in enumerate(self.current_map_buttons, start=1):
            rb.setChecked(i == 1)
        for mr in self.map_rows:
            mr.reset()
        self._autosave(self._collect_state())

    def _swap_teams(self):
        t1 = self.team1_panel.to_team()
        t2 = self.team2_panel.to_team()

        def _norm(s: str) -> str:
            return (s or "").strip().lower()

        T1_DEF = "#55aaff"
        T2_DEF = "#ff557f"
        t1_custom = _norm(t1.color_hex) != _norm(T1_DEF)
        t2_custom = _norm(t2.color_hex) != _norm(T2_DEF)

        self.team1_panel.from_team(t2)
        self.team2_panel.from_team(t1)

        if t2_custom:
            self.team1_panel.color_hex = t2.color_hex
        else:
            self.team1_panel.color_hex = T1_DEF
        if t1_custom:
            self.team2_panel.color_hex = t1.color_hex
        else:
            self.team2_panel.color_hex = T2_DEF

        self.team1_panel._apply_color_style()
        self.team2_panel._apply_color_style()

        for mr in self.map_rows:
            a = mr.t1score.value()
            mr.t1score.setValue(mr.t2score.value())
            mr.t2score.setValue(a)
            idx = mr.pick.currentIndex()
            if idx == 1:
                mr.pick.setCurrentIndex(2)
            elif idx == 2:
                mr.pick.setCurrentIndex(1)
            t1b_ix = mr.t1ban.currentIndex()
            t2b_ix = mr.t2ban.currentIndex()
            mr.t1ban.setCurrentIndex(t2b_ix)
            mr.t2ban.setCurrentIndex(t1b_ix)

        self._autosave()



    # ---------------------
    # Update: export JSON for persistence/OBS & autosave
    # ---------------------
    def _collect_state(self):
        t1 = self.team1_panel.to_team()
        t2 = self.team2_panel.to_team()

        current_ix = None
        for i, rb in enumerate(self.current_map_buttons, start=1):
            if rb.isChecked():
                current_ix = i
                break

        maps = []
        for idx, mr in enumerate(self.map_rows, start=1):
            maps.append({
                "index": idx,
                "map": mr.map_combo.currentText(),
                "t1": mr.t1score.value(),
                "t2": mr.t2score.value(),
                "completed": mr.completed.isChecked(),
                "pick": mr.pick.currentText(),
                "t1_ban": ("" if mr.t1ban.currentText() == "— Hero —" else mr.t1ban.currentText()),
                "t2_ban": ("" if mr.t2ban.currentText() == "— Hero —" else mr.t2ban.currentText()),
                "winner": ("t1" if mr.t1score.value() > mr.t2score.value()
                           else "t2" if mr.t2score.value() > mr.t1score.value()
                           else "")
            })

        state = {
            "team1": asdict(t1),
            "team2": asdict(t2),
            "maps": maps,
            "current_map": current_ix,
            "map_pool": self.draft_tab.get_pool() if hasattr(self, "draft_tab") else [],
            "assets": {
                "heroes": {k: asdict(v) for k, v in self.heroes.items()},
                "maps": {k: asdict(v) for k, v in self.maps.items()},
                "modes": {k: asdict(v) for k, v in self.modes.items()},
            }
        }
        general = self.general_tab.to_settings()
        state["general"] = asdict(general)
        waiting = self.waiting_tab.to_settings()
        state["waiting"] = asdict(waiting)
        if hasattr(self, "brackets_tab"):
            state["brackets"] = asdict(self.brackets_tab.to_state())
        return state


    def _apply_state(self, state: dict):
        assets = state.get("assets", {})
        self.heroes = {k: Asset(**v) for k, v in assets.get("heroes", {}).items()}
        self.maps = {k: Asset(**v) for k, v in assets.get("maps", {}).items()}
        self.modes = {k: Asset(**v) for k, v in assets.get("modes", {}).items()}
        self._on_assets_changed()

        t1 = Team(**{k: v for k, v in state.get("team1", {}).items() if k != "players"})
        t1.players = [Player(**p) for p in state.get("team1", {}).get("players", [])]
        t2 = Team(**{k: v for k, v in state.get("team2", {}).items() if k != "players"})
        t2.players = [Player(**p) for p in state.get("team2", {}).get("players", [])]
        self.team1_panel.from_team(t1)
        self.team2_panel.from_team(t2)

        for mr in self.map_rows:
            mr.reset()

        for item in state.get("maps", []):
            idx = int(item.get("index", 0))
            if 1 <= idx <= len(self.map_rows):
                mr = self.map_rows[idx - 1]
                name = item.get("map", "")
                if name:
                    ix = mr.map_combo.findText(name)
                    mr.map_combo.setCurrentIndex(ix if ix >= 0 else 0)
                mr.t1score.setValue(int(item.get("t1", 0)))
                mr.t2score.setValue(int(item.get("t2", 0)))
                mr.completed.setChecked(bool(item.get("completed", False)))

                txt = (item.get("pick") or "")
                if txt == "T1": mr.pick.setCurrentIndex(1)
                elif txt == "T2": mr.pick.setCurrentIndex(2)
                else: mr.pick.setCurrentIndex(0)


                t1b = (item.get("t1_ban") or "")
                t2b = (item.get("t2_ban") or "")
                if t1b:
                    ix = mr.t1ban.findText(t1b)
                    mr.t1ban.setCurrentIndex(ix if ix >= 0 else 0)
                else:
                    mr.t1ban.setCurrentIndex(0)
                if t2b:
                    ix = mr.t2ban.findText(t2b)
                    mr.t2ban.setCurrentIndex(ix if ix >= 0 else 0)
                else:
                    mr.t2ban.setCurrentIndex(0)

        cur = state.get("current_map")
        for i, rb in enumerate(self.current_map_buttons, start=1):
            rb.setChecked(i == cur)
        
        gdata = state.get("general", {})
        self.general_tab.from_settings(GeneralSettings(**gdata))
        
        wdata = state.get("waiting", {}) or {}
        self.waiting_tab.from_settings(WaitingSettings(**wdata))
            
        pool = state.get("map_pool") or []
        if hasattr(self, "draft_tab"):
            self.draft_tab.set_pool(pool)

        bdata = state.get("brackets") or {}
        if hasattr(self, "brackets_tab") and bdata:
            settings = bdata.get("settings") or {}
            teams = [BracketTeam(**t) for t in bdata.get("teams", [])]
            matches = []
            for m in bdata.get("matches", []):
                match = BracketMatch(
                    id=int(m.get("id", 0)),
                    round=int(m.get("round", 1)),
                    slot=int(m.get("slot", 1)),
                    teamA=m.get("teamA"),
                    teamB=m.get("teamB"),
                    scoreA=int(m.get("scoreA", 0)),
                    scoreB=int(m.get("scoreB", 0)),
                    winnerToMatchId=m.get("winnerToMatchId"),
                    loserToMatchId=m.get("loserToMatchId"),
                    winnerToSlot=m.get("winnerToSlot"),
                    loserToSlot=m.get("loserToSlot"),
                    bracket=m.get("bracket", ""),
                    group=m.get("group"),
                    seedA=m.get("seedA", m.get("teamA")),
                    seedB=m.get("seedB", m.get("teamB"))
                )
                matches.append(match)
            self.brackets_tab.from_state(BracketState(
                settings=BracketSettings(bracket_type=settings.get("bracket_type", "swiss")),
                teams=teams,
                matches=matches
            ))

    def _update(self):
        state = self._collect_state()

        old = getattr(self, "_last_state_for_diff", None)
        changed = self._diff_for_scoreboard(old, state)

        if "assets.heroes" in changed:
            self._export_assets_category("Heroes", self.heroes)
        if "assets.maps" in changed:
            self._export_assets_category("Maps", self.maps)
        if "assets.modes" in changed:
            self._export_assets_category("Gametypes", self.modes)

        g = state.get("general") or {}
        settings = GeneralSettings(**g) if isinstance(g, dict) else GeneralSettings()
        self._export_general(settings)

        self._export_match(state)
        
        self._export_waiting(state)
        self._export_brackets(state)

        self._export_status_text(state)
        self._notify_overlays(changed)

        base_root = os.environ.get("SOWB_ROOT") or _app_base()

        match_path = os.path.join(base_root, "match.json")
        with open(match_path, "w", encoding="utf-8") as f:
            json.dump({k: v for k, v in state.items() if k != "assets"}, f, ensure_ascii=False, indent=2)

        assets_path = os.path.join(base_root, "assets.json")
        with open(assets_path, "w", encoding="utf-8") as f:
            json.dump(state.get("assets", {}), f, ensure_ascii=False, indent=2)

        self._autosave(state)
        self._last_state_for_diff = state
        
    def _update_general_only(self):
        g = asdict(self.general_tab.to_settings())
        self._export_general(GeneralSettings(**g))
        self._export_status_text({"general": g})
        full = {
            "team1": {}, "team2": {}, "maps": [],
            "current_map": None,
            "general": g,
            "assets": {"heroes":{}, "maps":{}, "modes":{}},
        }
        old = getattr(self, "_last_state_for_diff", None)
        changed = self._diff_for_scoreboard(old, full)
        self._last_state_for_diff = full
        self._notify_overlays(changed)
        
    def _update_waiting_only(self):
        w = asdict(self.waiting_tab.to_settings())
        self._export_waiting({"waiting": w})
        g = asdict(self.general_tab.to_settings())
        self._export_status_text({"general": g})
        self._autosave(self._collect_state())
        full = {
            "team1": {}, "team2": {}, "maps": [],
            "current_map": None,
            "general": g,
            "waiting": w,
            "assets": {"heroes":{}, "maps":{}, "modes":{}},
        }
        old = getattr(self, "_last_state_for_diff", None)
        changed = self._diff_for_scoreboard(old, full)
        self._last_state_for_diff = full
        self._notify_overlays(changed)

    def _update_brackets_only(self):
        b = asdict(self.brackets_tab.to_state()) if hasattr(self, "brackets_tab") else {}
        self._export_brackets({"brackets": b})
        self._autosave(self._collect_state())
        full = {
            "team1": {}, "team2": {}, "maps": [],
            "current_map": None,
            "general": {},
            "waiting": {},
            "brackets": b,
            "assets": {"heroes":{}, "maps":{}, "modes":{}},
        }
        old = getattr(self, "_last_state_for_diff", None)
        changed = self._diff_for_scoreboard(old, full)
        self._last_state_for_diff = full
        self._notify_overlays(changed)


    # ---------------------
    # Save/Load helpers
    # ---------------------
    def _autosave(self, state: Optional[dict] = None):
        if state is None:
            state = self._collect_state()
        try:
            with open(self.autosave_path, "w", encoding="utf-8") as f:
                json.dump(state, f, ensure_ascii=False, indent=2)
            print(f"[autosave] wrote {self.autosave_path}")
        except Exception as e:
            print(f"[autosave] failed: {e}")

    def _load_autosave(self):
        if os.path.exists(self.autosave_path):
            try:
                with open(self.autosave_path, "r", encoding="utf-8") as f:
                    state = json.load(f)
                self._apply_state(state)
                print(f"[autosave] loaded {self.autosave_path}")
            except Exception as e:
                print(f"[autosave] load failed: {e}")


    def _save(self):
        if self.current_save_path and self.current_save_path != self.autosave_path:
            path = self.current_save_path
        else:
            return self._save_as()
        state = self._collect_state()
        with open(path, "w", encoding="utf-8") as f:
            json.dump(state, f, ensure_ascii=False, indent=2)
        self._autosave(state)

    def _save_as(self):
        path, _ = QFileDialog.getSaveFileName(self, "Save As", os.path.join(self.app_dir, "match_state.json"), "JSON Files (*.json)")
        if not path:
            return
        self.current_save_path = path
        self._save()

    def _load_from_file(self):
        start = self.export_dir if os.path.isdir(self.export_dir) else self.app_dir
        path, _ = QFileDialog.getOpenFileName(
            self, "Load state",
            start,
            "SOW Broadcast (*.sowbroadcast.json);;JSON (*.json);;All files (*.*)"
        )
        if not path:
            return
        try:
            with open(path, "r", encoding="utf-8") as f:
                state = json.load(f)
            self._apply_state(state)
            self.current_save_path = path
            self._autosave(state)
        except Exception as e:
            QMessageBox.critical(self, "Load failed", str(e))

    def closeEvent(self, event):
        self._autosave()
        super().closeEvent(event)
def _start_http_server(bind="127.0.0.1", port=8324):
    import http.server, threading, atexit
    from server import PushHandler

    base = os.path.dirname(sys.executable) if getattr(sys, "frozen", False) else os.path.dirname(__file__)
    os.chdir(base)

    httpd = http.server.ThreadingHTTPServer((bind, port), PushHandler)
    t = threading.Thread(target=httpd.serve_forever, daemon=True)
    t.start()
    atexit.register(httpd.shutdown)
    return httpd

# -----------------------------
# Entrypoint
# -----------------------------
if __name__ == "__main__":
    from PyQt5.QtCore import QCoreApplication

    QCoreApplication.setOrganizationName("SOWBroadcast")
    QCoreApplication.setApplicationName("SOWBroadcast")
    
    _start_http_server()

    app = QApplication(sys.argv)
    win = TournamentApp()
    win.show()
    sys.exit(app.exec_())
