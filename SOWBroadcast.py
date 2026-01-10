import sys, os, json, re, shutil, time, threading, unicodedata, shutil
import server as _sb__force_include
from dataclasses import dataclass, asdict
from typing import Callable, Dict, List, Optional

from PyQt5.QtCore import Qt, QStandardPaths, pyqtSignal
from PyQt5.QtGui import QPixmap, QColor
from PyQt5.QtWidgets import (
    QApplication, QMainWindow, QWidget, QVBoxLayout, QHBoxLayout,
    QLabel, QLineEdit, QPushButton, QComboBox, QSpinBox, QCheckBox,
    QAction, QFileDialog, QRadioButton, QGroupBox, QGridLayout, QDialog,
    QFormLayout, QListWidget, QListWidgetItem, QMessageBox, QSplitter,
    QSizePolicy, QColorDialog, QTabWidget, QTreeWidget, QTreeWidgetItem, QScrollArea,
    QTableWidget, QHeaderView, QDialogButtonBox
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

@dataclass
class StandingsRow:
    team_name: str = ""
    abbr: str = ""
    logo_path: Optional[str] = None
    wins: int = 0
    losses: int = 0
    map_diff: int = 0
    points: int = 0
    status: str = ""
    rank: int = 0

@dataclass
class StandingsSettings:
    title: str = ""
    subtitle: str = ""
    columns: Dict[str, str] = None
    rows: List[StandingsRow] = None
    groups: List["StandingsGroup"] = None
    display_group: str = ""

    def __post_init__(self):
        if self.columns is None:
            self.columns = {"mode": "map_diff"}
        if self.rows is None:
            self.rows = []
        if self.groups is None:
            self.groups = []

@dataclass
class StandingsGroup:
    key: str = ""
    name: str = ""
    rows: List[StandingsRow] = None

    def __post_init__(self):
        if self.rows is None:
            self.rows = []

def _standings_row_from_dict(data: dict) -> StandingsRow:
    payload = dict(data or {})
    payload.setdefault("points", 0)
    payload.pop("maps_for", None)
    payload.pop("maps_against", None)
    return StandingsRow(**payload)

def _standings_group_from_dict(data: dict, fallback_key: str = "") -> StandingsGroup:
    payload = dict(data or {})
    rows = [_standings_row_from_dict(r) for r in (payload.get("rows") or [])]
    key = (payload.get("key") or payload.get("id") or "").strip() or fallback_key
    return StandingsGroup(
        key=key,
        name=payload.get("name", "") or payload.get("title", "") or "",
        rows=rows,
    )

def _apply_standings_ranks(rows: List[StandingsRow]) -> None:
    if not rows:
        return
    used_ranks = {r.rank for r in rows if int(r.rank or 0) > 0}
    def sort_key(r: StandingsRow):
        return (-int(r.points or 0), -int(r.map_diff or 0), r.team_name.lower())
    remaining = [r for r in rows if int(r.rank or 0) <= 0]
    remaining.sort(key=sort_key)
    next_rank = 1
    for r in remaining:
        while next_rank in used_ranks:
            next_rank += 1
        r.rank = next_rank
        used_ranks.add(next_rank)
        next_rank += 1

@dataclass
class TeamRef:
    name: str = ""
    abbr: str = ""
    logo_path: Optional[str] = None

@dataclass
class BracketMatch:
    id: str = ""
    bo_label: str = ""
    team1: TeamRef = None
    team2: TeamRef = None
    score1: int = 0
    score2: int = 0
    status: str = ""

    def __post_init__(self):
        if self.team1 is None:
            self.team1 = TeamRef()
        if self.team2 is None:
            self.team2 = TeamRef()

@dataclass
class BracketRound:
    name: str = ""
    side: str = ""
    matches: List[BracketMatch] = None

    def __post_init__(self):
        if self.matches is None:
            self.matches = []

@dataclass
class BracketSettings:
    title: str = ""
    stage: str = ""
    rounds: List[BracketRound] = None
    double_elim_view: str = ""
    teams: List[TeamRef] = None

    def __post_init__(self):
        if self.rounds is None:
            self.rounds = []
        if self.teams is None:
            self.teams = []


class BracketMatchWidget(QWidget):
    selected = pyqtSignal(object)
    updated = pyqtSignal()

    def __init__(self, match: BracketMatch, team_options: Optional[List[TeamRef]] = None):
        super().__init__()
        self.match = match
        self._team_options = team_options or []
        self._team_map = {t.name: t for t in self._team_options if t.name}

        root = QVBoxLayout(self)
        root.setContentsMargins(2, 2, 2, 2)
        root.setSpacing(4)

        self.team1_combo = self._build_team_combo(match.team1.name)
        self.team1_score = QSpinBox()
        self.team1_score.setRange(0, 99)
        self.team1_score.setValue(int(match.score1 or 0))

        self.team2_combo = self._build_team_combo(match.team2.name)
        self.team2_score = QSpinBox()
        self.team2_score.setRange(0, 99)
        self.team2_score.setValue(int(match.score2 or 0))

        row1 = QHBoxLayout()
        row1.addWidget(self.team1_combo, 1)
        row1.addWidget(self.team1_score)
        row2 = QHBoxLayout()
        row2.addWidget(self.team2_combo, 1)
        row2.addWidget(self.team2_score)

        root.addLayout(row1)
        root.addLayout(row2)

        self.team1_combo.currentTextChanged.connect(lambda *_: self._on_team_changed(self.team1_combo, 1))
        self.team2_combo.currentTextChanged.connect(lambda *_: self._on_team_changed(self.team2_combo, 2))
        self.team1_score.valueChanged.connect(self._on_score_changed)
        self.team2_score.valueChanged.connect(self._on_score_changed)

    def _build_team_combo(self, initial: str) -> QComboBox:
        combo = QComboBox()
        combo.setEditable(False)
        combo.setMinimumWidth(140)
        self._populate_combo(combo, initial)
        return combo

    def _populate_combo(self, combo: QComboBox, initial: str):
        combo.blockSignals(True)
        combo.clear()
        combo.addItem("")
        for t in self._team_options:
            if t.name:
                combo.addItem(t.name)
        if initial and combo.findText(initial) >= 0:
            combo.setCurrentText(initial)
        else:
            combo.setCurrentIndex(0)
        combo.blockSignals(False)

    def set_team_options(self, team_options: List[TeamRef]):
        self._team_options = team_options or []
        self._team_map = {t.name: t for t in self._team_options if t.name}
        self._populate_combo(self.team1_combo, self.team1_combo.currentText())
        self._populate_combo(self.team2_combo, self.team2_combo.currentText())

    def set_team(self, slot: int, team: Optional[TeamRef]):
        target = self.team1_combo if slot == 1 else self.team2_combo
        if team and team.name:
            target.setCurrentText(team.name)
        else:
            target.setCurrentIndex(0)
        self._apply_team_ref(slot, team)

    def _on_team_changed(self, combo: QComboBox, slot: int):
        name = combo.currentText().strip()
        team = self._team_map.get(name, TeamRef(name=name))
        self._apply_team_ref(slot, team)
        self.updated.emit()

    def _apply_team_ref(self, slot: int, team: Optional[TeamRef]):
        team = team or TeamRef()
        if slot == 1:
            self.match.team1 = TeamRef(name=team.name, abbr=team.abbr, logo_path=team.logo_path)
        else:
            self.match.team2 = TeamRef(name=team.name, abbr=team.abbr, logo_path=team.logo_path)

    def _on_score_changed(self, *_):
        self.match.score1 = int(self.team1_score.value())
        self.match.score2 = int(self.team2_score.value())
        self.updated.emit()

    def mousePressEvent(self, event):
        self.selected.emit(self)
        super().mousePressEvent(event)


class BracketTeamRow(QWidget):
    changed = pyqtSignal()

    def __init__(self):
        super().__init__()
        self._logo_path = ""

        layout = QHBoxLayout(self)
        layout.setContentsMargins(2, 2, 2, 2)
        layout.setSpacing(4)

        self.name_edit = QLineEdit()
        self.browse_btn = QPushButton("...")
        self.browse_btn.setFixedWidth(28)

        layout.addWidget(self.name_edit, 1)
        layout.addWidget(self.browse_btn)

        self.name_edit.textChanged.connect(self.changed)
        self.browse_btn.clicked.connect(self._pick_logo)

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
            self.changed.emit()

    def set_team(self, team: Optional[TeamRef]):
        self.name_edit.setText(team.name if team else "")
        self._logo_path = team.logo_path if team else ""

    def team_ref(self) -> TeamRef:
        return TeamRef(
            name=self.name_edit.text().strip(),
            abbr="",
            logo_path=self._logo_path or None,
        )

    def clear(self):
        self.name_edit.clear()
        self._logo_path = ""


class BracketTeamsImportDialog(QDialog):
    def __init__(self, parent, teams: List[TeamRef]):
        super().__init__(parent)
        self.setWindowTitle("Import Teams from Standings")
        self.resize(360, 420)

        root = QVBoxLayout(self)
        root.addWidget(QLabel("Select teams to import."))

        self.listw = QListWidget()
        for team in teams:
            label = team.name or "Unnamed team"
            item = QListWidgetItem(label)
            item.setData(Qt.UserRole, team)
            item.setCheckState(Qt.Checked)
            self.listw.addItem(item)
        root.addWidget(self.listw, 1)

        btns = QDialogButtonBox(QDialogButtonBox.Ok | QDialogButtonBox.Cancel)
        btns.accepted.connect(self.accept)
        btns.rejected.connect(self.reject)
        root.addWidget(btns)

    def selected_teams(self) -> List[TeamRef]:
        teams = []
        for i in range(self.listw.count()):
            item = self.listw.item(i)
            if item.checkState() == Qt.Checked:
                teams.append(item.data(Qt.UserRole))
        return teams

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
        "Standings", "Bracket"
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


class StandingsTab(QWidget):
    updated = pyqtSignal()

    HEADERS = [
        "Rank", "Team", "Abbr", "W", "L", "+/-", "Points", "Status", "Logo"
    ]

    def __init__(self):
        super().__init__()
        root = QVBoxLayout(self)

        self._groups: List[StandingsGroup] = []
        self._group_widgets: List[dict] = []
        self._group_counter = 1
        self._loading_groups = False

        group_box = QGroupBox("Standings Groups")
        group_layout = QGridLayout(group_box)
        self.add_group_btn = QPushButton("Add Group")
        self.display_combo = QComboBox()
        self.display_combo.addItem("All groups", "__all__")
        group_layout.addWidget(self.add_group_btn, 0, 0)
        group_layout.addWidget(QLabel("Show in standings.html"), 0, 1)
        group_layout.addWidget(self.display_combo, 0, 2)
        group_layout.setColumnStretch(2, 1)
        root.addWidget(group_box)

        self.groups_area = QScrollArea()
        self.groups_area.setWidgetResizable(True)
        self.groups_container = QWidget()
        self.groups_layout = QVBoxLayout(self.groups_container)
        self.groups_layout.setContentsMargins(0, 0, 0, 0)
        self.groups_layout.setSpacing(12)
        self.groups_area.setWidget(self.groups_container)
        root.addWidget(self.groups_area, 2)

        title_box = QGroupBox("Standings Title")
        title_form = QFormLayout(title_box)
        self.title_edit = QLineEdit()
        self.subtitle_edit = QLineEdit()
        title_form.addRow("Title", self.title_edit)
        title_form.addRow("Subtitle / Group", self.subtitle_edit)
        root.addWidget(title_box)

        action_row = QHBoxLayout()
        action_row.addStretch(1)
        self.reset_btn = QPushButton("Reset this tab")
        self.update_btn = QPushButton("Update + sort")
        action_row.addWidget(self.reset_btn)
        action_row.addWidget(self.update_btn)
        root.addLayout(action_row)

        self.reset_btn.clicked.connect(self.reset_tab)
        self.update_btn.clicked.connect(self._update_and_sort)
        self.add_group_btn.clicked.connect(self._add_group)

        self._add_group(initial=True)

    def _make_spin(self, minv: int, maxv: int) -> QSpinBox:
        s = QSpinBox()
        s.setRange(minv, maxv)
        return s

    def _make_logo_cell(self, value: str = "") -> QWidget:
        wrap = QWidget()
        lay = QHBoxLayout(wrap)
        lay.setContentsMargins(0, 0, 0, 0)
        edit = QLineEdit()
        edit.setText(value or "")
        browse = QPushButton("Browse…")
        browse.setFixedWidth(80)
        browse.clicked.connect(lambda *_: self._pick_logo_for(edit))
        lay.addWidget(edit, 1)
        lay.addWidget(browse)
        wrap.setLayout(lay)
        wrap._edit = edit
        return wrap

    def _pick_logo_for(self, edit: QLineEdit):
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
            edit.setText(path)

    def _build_group_widget(self, group: StandingsGroup) -> dict:
        box = QGroupBox()
        box_layout = QVBoxLayout(box)

        header_row = QHBoxLayout()
        name_label = QLabel("Name")
        name_edit = QLineEdit(group.name or "")
        remove_btn = QPushButton("Remove")
        header_row.addWidget(name_label)
        header_row.addWidget(name_edit, 1)
        header_row.addWidget(remove_btn)
        box_layout.addLayout(header_row)

        table = QTableWidget(0, len(self.HEADERS))
        table.setHorizontalHeaderLabels(self.HEADERS)
        table.verticalHeader().setVisible(False)
        table.setSelectionBehavior(QTableWidget.SelectRows)
        table.setSelectionMode(QTableWidget.SingleSelection)
        table.setVerticalScrollBarPolicy(Qt.ScrollBarAlwaysOff)
        table.setHorizontalScrollBarPolicy(Qt.ScrollBarAsNeeded)
        table.setSizePolicy(QSizePolicy.Expanding, QSizePolicy.Fixed)
        header = table.horizontalHeader()
        for i in range(len(self.HEADERS)):
            if self.HEADERS[i] in {"Team", "Logo"}:
                header.setSectionResizeMode(i, QHeaderView.Stretch)
            else:
                header.setSectionResizeMode(i, QHeaderView.ResizeToContents)
        box_layout.addWidget(table, 1)

        row_btns = QHBoxLayout()
        add_btn = QPushButton("Add Row")
        remove_btn_row = QPushButton("Remove Selected")
        row_btns.addWidget(add_btn)
        row_btns.addWidget(remove_btn_row)
        row_btns.addStretch(1)
        box_layout.addLayout(row_btns)

        widget = {
            "box": box,
            "group": group,
            "name_edit": name_edit,
            "table": table,
            "remove_btn": remove_btn,
        }
        add_btn.clicked.connect(lambda: self._add_row(table))
        remove_btn_row.clicked.connect(lambda: self._remove_selected(table))
        remove_btn.clicked.connect(lambda _=False, w=widget: self._remove_group(w))
        name_edit.textChanged.connect(self._on_group_name_changed)
        return widget

    def _update_group_controls(self):
        allow_remove = len(self._group_widgets) > 1
        for widget in self._group_widgets:
            widget["remove_btn"].setEnabled(allow_remove)

    def _new_group(self, name: str = "", rows: Optional[List[StandingsRow]] = None, key: str = "") -> StandingsGroup:
        group_count = len(self._groups) + 1
        if not key:
            key = f"group_{self._group_counter}"
            self._group_counter += 1
        default_name = name or f"Group {group_count}"
        group = StandingsGroup(key=key, name=default_name)
        if rows is not None:
            group.rows = rows
        return group

    def _refresh_display_combo(self, selected_key: Optional[str] = None):
        self.display_combo.blockSignals(True)
        current_key = selected_key or self.display_combo.currentData()
        self.display_combo.clear()
        self.display_combo.addItem("All groups", "__all__")
        for widget in self._group_widgets:
            label = (widget["name_edit"].text().strip()
                     or widget["group"].name
                     or widget["group"].key
                     or "Group")
            self.display_combo.addItem(label, widget["group"].key or label)
        if current_key:
            idx = self.display_combo.findData(current_key)
            if idx >= 0:
                self.display_combo.setCurrentIndex(idx)
        self.display_combo.blockSignals(False)

    def _load_group(self, table: QTableWidget, group: StandingsGroup, name_edit: QLineEdit):
        self._loading_groups = True
        name_edit.setText(group.name or "")
        self._load_rows(table, group.rows or [])
        if table.rowCount() == 0:
            self._add_row(table)
        self._loading_groups = False

    def _on_group_name_changed(self):
        if self._loading_groups:
            return
        self._refresh_display_combo(selected_key=self.display_combo.currentData())

    def _add_group(self, initial: bool = False):
        group = self._new_group()
        self._groups.append(group)
        widget = self._build_group_widget(group)
        self._group_widgets.append(widget)
        self.groups_layout.addWidget(widget["box"])
        if initial:
            self._add_row(widget["table"])
        self._refresh_display_combo(selected_key=self.display_combo.currentData())
        self._update_group_controls()
        if not initial:
            self.updated.emit()

    def _remove_group(self, widget: dict):
        if len(self._group_widgets) <= 1:
            return
        if widget in self._group_widgets:
            self._group_widgets.remove(widget)
        if widget["group"] in self._groups:
            self._groups.remove(widget["group"])
        widget["box"].setParent(None)
        widget["box"].deleteLater()
        self._refresh_display_combo(selected_key=self.display_combo.currentData())
        self._update_group_controls()
        self.updated.emit()

    def _update_table_height(self, table: QTableWidget):
        header_height = table.horizontalHeader().height()
        rows_height = sum(table.rowHeight(i) for i in range(table.rowCount()))
        frame = table.frameWidth() * 2
        height = header_height + rows_height + frame
        table.setMinimumHeight(height)
        table.setMaximumHeight(height)

    def _add_row(self, table: QTableWidget, row_data: Optional[StandingsRow] = None):
        row = table.rowCount()
        table.insertRow(row)

        rank_spin = self._make_spin(0, 99)
        rank_spin.setValue(int(getattr(row_data, "rank", 0) or 0))
        table.setCellWidget(row, 0, rank_spin)

        team_edit = QLineEdit(getattr(row_data, "team_name", "") or "")
        table.setCellWidget(row, 1, team_edit)

        abbr_edit = QLineEdit(getattr(row_data, "abbr", "") or "")
        table.setCellWidget(row, 2, abbr_edit)

        wins_spin = self._make_spin(0, 99)
        wins_spin.setValue(int(getattr(row_data, "wins", 0) or 0))
        table.setCellWidget(row, 3, wins_spin)

        loss_spin = self._make_spin(0, 99)
        loss_spin.setValue(int(getattr(row_data, "losses", 0) or 0))
        table.setCellWidget(row, 4, loss_spin)

        diff_spin = self._make_spin(-99, 99)
        diff_spin.setValue(int(getattr(row_data, "map_diff", 0) or 0))
        table.setCellWidget(row, 5, diff_spin)

        points_spin = self._make_spin(0, 999)
        points_spin.setValue(int(getattr(row_data, "points", 0) or 0))
        table.setCellWidget(row, 6, points_spin)

        status_combo = QComboBox()
        status_combo.addItem("")
        status_combo.addItems(["qualified", "eliminated"])
        status = (getattr(row_data, "status", "") or "").strip().lower()
        ix = status_combo.findText(status, Qt.MatchExactly)
        status_combo.setCurrentIndex(ix if ix >= 0 else 0)
        table.setCellWidget(row, 7, status_combo)

        logo_widget = self._make_logo_cell(getattr(row_data, "logo_path", "") or "")
        table.setCellWidget(row, 8, logo_widget)
        self._update_table_height(table)

    def _remove_selected(self, table: QTableWidget):
        selected = table.selectionModel().selectedRows()
        if not selected:
            return
        for idx in sorted([s.row() for s in selected], reverse=True):
            table.removeRow(idx)
        self._update_table_height(table)

    def _row_data(self, table: QTableWidget, row: int) -> StandingsRow:
        rank = int(table.cellWidget(row, 0).value())
        team_name = table.cellWidget(row, 1).text().strip()
        abbr = table.cellWidget(row, 2).text().strip()
        wins = int(table.cellWidget(row, 3).value())
        losses = int(table.cellWidget(row, 4).value())
        map_diff = int(table.cellWidget(row, 5).value())
        points = int(table.cellWidget(row, 6).value())
        status = table.cellWidget(row, 7).currentText().strip()
        logo_widget = table.cellWidget(row, 8)
        logo_path = logo_widget._edit.text().strip() if logo_widget else ""
        return StandingsRow(
            team_name=team_name,
            abbr=abbr,
            logo_path=logo_path or None,
            wins=wins,
            losses=losses,
            map_diff=map_diff,
            points=points,
            status=status,
            rank=rank,
        )

    def _rows(self, table: QTableWidget) -> List[StandingsRow]:
        return [row for _, row in self._indexed_rows(table)]

    def _indexed_rows(self, table: QTableWidget) -> List[tuple[int, StandingsRow]]:
        rows = []
        for i in range(table.rowCount()):
            row = self._row_data(table, i)
            has_identity = bool(row.team_name or row.abbr or row.logo_path)
            has_stats = any([row.wins, row.losses, row.map_diff, row.points])
            if has_identity or has_stats:
                rows.append((i, row))
        return rows

    def _sort_rows(self):
        for widget in self._group_widgets:
            table = widget["table"]
            rows = self._rows(table)
            def sort_key(r: StandingsRow):
                diff = r.map_diff
                return (-r.points, -diff, r.team_name.lower())
            rows.sort(key=sort_key)
            for rank, row in enumerate(rows, start=1):
                row.rank = rank
            self._load_rows(table, rows)

    def _update_and_sort(self):
        self._sort_rows()
        self.updated.emit()

    def _load_rows(self, table: QTableWidget, rows: List[StandingsRow]):
        table.setRowCount(0)
        for row in rows:
            self._add_row(table, row)
        if not rows:
            self._update_table_height(table)

    def reset_tab(self):
        self.title_edit.clear()
        self.subtitle_edit.clear()
        self._groups = []
        for widget in self._group_widgets:
            widget["box"].setParent(None)
            widget["box"].deleteLater()
        self._group_widgets = []
        self._group_counter = 1
        self._add_group(initial=True)

    def to_settings(self) -> StandingsSettings:
        indexed_rows = []
        if self._group_widgets:
            indexed_rows = self._indexed_rows(self._group_widgets[0]["table"])
        rows = [row for _, row in indexed_rows]
        columns = {"mode": "map_diff"}
        _apply_standings_ranks(rows)
        for idx, row in indexed_rows:
            rank_widget = self._group_widgets[0]["table"].cellWidget(idx, 0) if self._group_widgets else None
            if rank_widget and int(rank_widget.value()) <= 0:
                rank_widget.setValue(int(row.rank or 0))

        display_group = self.display_combo.currentData() or "__all__"
        groups = []
        for widget in self._group_widgets:
            table = widget["table"]
            group = widget["group"]
            group.name = widget["name_edit"].text().strip()
            indexed = self._indexed_rows(table)
            group.rows = [row for _, row in indexed]
            _apply_standings_ranks(group.rows or [])
            for idx, row in indexed:
                rank_widget = table.cellWidget(idx, 0)
                if rank_widget and int(rank_widget.value()) <= 0:
                    rank_widget.setValue(int(row.rank or 0))
            groups.append(group)
        if display_group != "__all__" and not any(group.key == display_group for group in groups):
            display_group = "__all__"
        if groups:
            selected = None
            if display_group != "__all__":
                selected = next((g for g in groups if g.key == display_group), None)
            rows = list(selected.rows) if selected else list(groups[0].rows)

        return StandingsSettings(
            title=self.title_edit.text().strip(),
            subtitle=self.subtitle_edit.text().strip(),
            columns=columns,
            rows=rows,
            groups=groups,
            display_group=display_group,
        )

    def from_settings(self, settings: StandingsSettings):
        self.title_edit.setText(settings.title or "")
        self.subtitle_edit.setText(settings.subtitle or "")
        self._groups = []
        for widget in self._group_widgets:
            widget["box"].setParent(None)
            widget["box"].deleteLater()
        self._group_widgets = []
        self._group_counter = 1
        groups = settings.groups or []
        if not groups:
            groups = [StandingsGroup(key="group_1", name="Group 1", rows=settings.rows or [])]
        for idx, group in enumerate(groups, start=1):
            if not group.key:
                group.key = f"group_{idx}"
            if not group.name:
                group.name = f"Group {idx}"
            self._groups.append(group)
            self._group_counter = max(self._group_counter, idx + 1)
            widget = self._build_group_widget(group)
            self._group_widgets.append(widget)
            self.groups_layout.addWidget(widget["box"])
        self._refresh_display_combo(selected_key=settings.display_group or "__all__")
        for widget in self._group_widgets:
            self._load_group(widget["table"], widget["group"], widget["name_edit"])
        self._update_group_controls()


class BracketTab(QWidget):
    updated = pyqtSignal()

    def __init__(self):
        super().__init__()
        self._match_id_counter = 1
        self._rounds: List[BracketRound] = []
        self._match_widgets: List[BracketMatchWidget] = []
        self._team_options: List[TeamRef] = []
        self._qualified_provider: Optional[Callable[[], List[TeamRef]]] = None

        root = QVBoxLayout(self)

        header = QGroupBox("Bracket Title")
        header_form = QFormLayout(header)
        self.title_edit = QLineEdit()
        self.stage_edit = QLineEdit()
        header_form.addRow("Title", self.title_edit)
        header_form.addRow("Stage", self.stage_edit)
        root.addWidget(header)

        controls = QGroupBox("Bracket Controls")
        controls_layout = QHBoxLayout(controls)
        self.template_combo = QComboBox()
        self.template_combo.addItems([
            "4 team single elimination",
            "6 team single elimination (top 2 start round 2)",
            "8 team single elimination",
            "4 team double elimination",
            "6 team double elimination",
            "8 team double elimination",
        ])
        self._single_elim_mode = False
        self.load_template_btn = QPushButton("Load Template")
        controls_layout.addWidget(QLabel("Template"))
        controls_layout.addWidget(self.template_combo, 1)
        controls_layout.addWidget(self.load_template_btn)
        self.double_elim_view_label = QLabel("Double elim view")
        self.double_elim_view_combo = QComboBox()
        self.double_elim_view_combo.addItems([
            "Upper Bracket",
            "Lower Bracket",
        ])
        self.double_elim_view_label.setVisible(False)
        self.double_elim_view_combo.setVisible(False)
        controls_layout.addWidget(self.double_elim_view_label)
        controls_layout.addWidget(self.double_elim_view_combo)
        self.bronze_match_check = QCheckBox("Bronze match")
        self.bronze_match_check.setVisible(False)
        controls_layout.addWidget(self.bronze_match_check)
        root.addWidget(controls)

        main_split = QSplitter()
        root.addWidget(main_split, 1)

        team_panel = QGroupBox("Teams")
        team_panel_layout = QVBoxLayout(team_panel)
        team_panel_layout.setContentsMargins(6, 6, 6, 6)
        team_panel_layout.setSpacing(6)

        self.team_list_scroll = QScrollArea()
        self.team_list_scroll.setWidgetResizable(True)
        self.team_list_container = QWidget()
        self.team_list_layout = QVBoxLayout(self.team_list_container)
        self.team_list_layout.setContentsMargins(2, 2, 2, 2)
        self.team_list_layout.setSpacing(4)
        self.team_list_scroll.setWidget(self.team_list_container)
        team_panel_layout.addWidget(self.team_list_scroll, 1)

        self.team_rows: List[BracketTeamRow] = []
        for _ in range(16):
            row = BracketTeamRow()
            row.changed.connect(self._refresh_team_options)
            self.team_rows.append(row)
            self.team_list_layout.addWidget(row)
        self.team_list_layout.addStretch(1)

        team_btns = QHBoxLayout()
        team_btns.addStretch(1)
        self.import_qualified_btn = QPushButton("Import from Standings")
        self.clear_teams_btn = QPushButton("Clear")
        team_btns.addWidget(self.import_qualified_btn)
        team_btns.addWidget(self.clear_teams_btn)
        team_panel_layout.addLayout(team_btns)

        main_split.addWidget(team_panel)

        self.bracket_scroll = QScrollArea()
        self.bracket_scroll.setWidgetResizable(True)
        self.bracket_container = QWidget()
        self.bracket_layout = QVBoxLayout(self.bracket_container)
        self.bracket_layout.setContentsMargins(4, 4, 4, 4)
        self.bracket_layout.setSpacing(10)
        self.bracket_scroll.setWidget(self.bracket_container)
        main_split.addWidget(self.bracket_scroll)
        main_split.setSizes([300, 900])

        action_row = QHBoxLayout()
        action_row.addStretch(1)
        self.reset_btn = QPushButton("Reset this tab")
        self.update_btn = QPushButton("Update (Bracket)")
        action_row.addWidget(self.reset_btn)
        action_row.addWidget(self.update_btn)
        root.addLayout(action_row)

        self.load_template_btn.clicked.connect(self._load_template_from_combo)
        self.import_qualified_btn.clicked.connect(self._import_teams_from_standings)
        self.clear_teams_btn.clicked.connect(self._clear_team_list)
        self.reset_btn.clicked.connect(self.reset_tab)
        self.update_btn.clicked.connect(lambda *_: self.updated.emit())
        self.double_elim_view_combo.currentTextChanged.connect(lambda *_: self.updated.emit())
        self.bronze_match_check.stateChanged.connect(self._on_bronze_match_changed)
        self.title_edit.textChanged.connect(lambda *_: self.updated.emit())
        self.stage_edit.textChanged.connect(lambda *_: self.updated.emit())

    def set_qualified_provider(self, provider: Callable[[], List[TeamRef]]):
        self._qualified_provider = provider

    def _new_match_id(self) -> str:
        stamp = int(time.time())
        self._match_id_counter += 1
        return f"m{stamp}-{self._match_id_counter}"

    def _make_match(self) -> BracketMatch:
        return BracketMatch(
            id=self._new_match_id(),
            bo_label="",
            status="",
            team1=TeamRef(),
            team2=TeamRef(),
            score1=0,
            score2=0,
        )

    def _set_double_elim_view_controls(self, visible: bool, value: Optional[str] = None):
        self.double_elim_view_label.setVisible(visible)
        self.double_elim_view_combo.setVisible(visible)
        if value:
            normalized = value.lower()
            if normalized == "lower":
                self.double_elim_view_combo.setCurrentText("Lower Bracket")
            else:
                self.double_elim_view_combo.setCurrentText("Upper Bracket")

    def _set_bronze_match_controls(self, visible: bool, enabled: bool = False):
        self.bronze_match_check.blockSignals(True)
        self.bronze_match_check.setVisible(visible)
        self.bronze_match_check.setChecked(enabled)
        self.bronze_match_check.blockSignals(False)

    def _double_elim_view_value(self) -> str:
        if not self.double_elim_view_combo.isVisible():
            return ""
        return "lower" if self.double_elim_view_combo.currentText() == "Lower Bracket" else "upper"

    def _find_bronze_round_index(self) -> Optional[int]:
        for idx, rnd in enumerate(self._rounds):
            if (rnd.side or "").lower() != "lower":
                continue
            if (rnd.name or "").strip().lower() != "bronze match":
                continue
            if len(rnd.matches or []) != 1:
                continue
            return idx
        return None

    def _apply_bronze_match(self, enabled: bool):
        idx = self._find_bronze_round_index()
        if enabled:
            if idx is None:
                self._rounds.append(BracketRound(
                    name="Bronze Match",
                    side="lower",
                    matches=[self._make_match()],
                ))
        elif idx is not None:
            self._rounds.pop(idx)

    def _supports_double_elim_view(self, rounds: List[BracketRound]) -> bool:
        upper = [r for r in rounds if (r.side or "").lower() == "upper"]
        lower = [r for r in rounds if (r.side or "").lower() == "lower"]
        upper_counts = [len(r.matches or []) for r in upper]
        lower_counts = [len(r.matches or []) for r in lower]
        if upper_counts == [4, 2, 1] and lower_counts == [2, 2, 1]:
            return True
        return upper_counts == [2, 2, 1] and lower_counts == [2, 1, 1]

    def _is_single_elim_rounds(self, rounds: List[BracketRound]) -> bool:
        if any((r.side or "").lower() == "grand" for r in rounds):
            return False
        lower_rounds = [r for r in rounds if (r.side or "").lower() == "lower"]
        if len(lower_rounds) > 1:
            return False
        if lower_rounds:
            rnd = lower_rounds[0]
            if (rnd.name or "").strip().lower() != "bronze match":
                return False
            if len(rnd.matches or []) != 1:
                return False
        return bool(rounds)

    def _on_bronze_match_changed(self):
        if not self._single_elim_mode:
            return
        self._apply_bronze_match(self.bronze_match_check.isChecked())
        self._build_bracket_view()
        self.updated.emit()

    def _load_template_from_combo(self):
        name = self.template_combo.currentText()
        if name == "4 team single elimination":
            rounds = self._template_single_elim(4)
        elif name == "6 team single elimination (top 2 start round 2)":
            rounds = self._template_single_elim(6)
        elif name == "8 team single elimination":
            rounds = self._template_single_elim(8)
        elif name == "4 team double elimination":
            rounds = self._template_double_elim(4)
        elif name == "6 team double elimination":
            rounds = self._template_double_elim(6)
        else:
            rounds = self._template_double_elim(8)
        self._single_elim_mode = name in (
            "4 team single elimination",
            "6 team single elimination (top 2 start round 2)",
            "8 team single elimination",
        )
        self._set_double_elim_view_controls(
            name in ("6 team double elimination", "8 team double elimination"),
            "upper",
        )
        self._set_bronze_match_controls(self._single_elim_mode, False)
        self._rounds = rounds
        if self._single_elim_mode:
            self._apply_bronze_match(self.bronze_match_check.isChecked())
        self._build_bracket_view()
        self.updated.emit()

    def _template_single_elim(self, team_count: int) -> List[BracketRound]:
        if team_count == 4:
            round_defs = [("Semifinals", 2), ("Final", 1)]
        elif team_count == 6:
            round_defs = [("Round 1", 2), ("Semifinals", 2), ("Final", 1)]
        else:
            round_defs = [("Quarterfinals", 4), ("Semifinals", 2), ("Final", 1)]
        rounds = []
        for name, match_count in round_defs:
            rounds.append(BracketRound(
                name=name,
                side="upper",
                matches=[self._make_match() for _ in range(match_count)],
            ))
        return rounds

    def _template_double_elim(self, team_count: int) -> List[BracketRound]:
        if team_count == 4:
            upper = [("Upper Semifinals", 2), ("Upper Final", 1)]
            lower = [("Lower Semifinal", 1), ("Lower Final", 1)]
        elif team_count == 6:
            upper = [("Upper Round 1", 2), ("Upper Semifinals", 2), ("Upper Final", 1)]
            lower = [("Lower Round 1", 2), ("Lower Semifinal", 1), ("Lower Final", 1)]
        else:
            upper = [("Upper Round 1", 4), ("Upper Semifinals", 2), ("Upper Final", 1)]
            lower = [("Lower Round 1", 2), ("Lower Round 2", 2), ("Lower Final", 1)]
        rounds = []
        for name, match_count in upper:
            rounds.append(BracketRound(
                name=name,
                side="upper",
                matches=[self._make_match() for _ in range(match_count)],
            ))
        for name, match_count in lower:
            rounds.append(BracketRound(
                name=name,
                side="lower",
                matches=[self._make_match() for _ in range(match_count)],
            ))
        rounds.append(BracketRound(
            name="Grand Final",
            side="grand",
            matches=[self._make_match()],
        ))
        return rounds

    def _clear_bracket(self):
        while self.bracket_layout.count():
            item = self.bracket_layout.takeAt(0)
            widget = item.widget()
            if widget:
                widget.setParent(None)
                widget.deleteLater()
        self._match_widgets = []

    def _build_bracket_view(self):
        self._clear_bracket()
        if not self._rounds:
            self.bracket_layout.addWidget(QLabel("No bracket loaded. Select a template to begin."))
            return

        sections = {
            "upper": ("Upper Bracket", []),
            "lower": ("Lower Bracket", []),
            "grand": ("Grand Final", []),
        }
        for rnd in self._rounds:
            key = rnd.side or "upper"
            if key not in sections:
                key = "upper"
            sections[key][1].append(rnd)

        for key in ("upper", "lower", "grand"):
            title, rounds = sections[key]
            if not rounds:
                continue
            box = QGroupBox(title)
            box_layout = QHBoxLayout(box)
            box_layout.setSpacing(20)
            for col_idx, rnd in enumerate(rounds):
                col = QWidget()
                col_layout = QVBoxLayout(col)
                col_layout.setSpacing(10)
                col_layout.addWidget(QLabel(rnd.name or "Round"))
                spacing = max(6, 10 * (col_idx + 1))
                for match in rnd.matches:
                    widget = BracketMatchWidget(match, team_options=self._team_options)
                    widget.updated.connect(self._on_match_updated)
                    col_layout.addWidget(widget)
                    col_layout.addSpacing(spacing)
                    self._match_widgets.append(widget)
                col_layout.addStretch(1)
                box_layout.addWidget(col)
            self.bracket_layout.addWidget(box)
        self.bracket_layout.addStretch(1)

    def _on_match_updated(self):
        self.updated.emit()

    def _refresh_team_options(self):
        teams = []
        for row in self.team_rows:
            team = row.team_ref()
            if team.name:
                teams.append(team)
        self._team_options = teams
        for widget in self._match_widgets:
            widget.set_team_options(self._team_options)
        self.updated.emit()

    def _clear_team_list(self):
        for row in self.team_rows:
            row.clear()
        self._refresh_team_options()

    def _import_teams_from_standings(self):
        if not self._qualified_provider:
            QMessageBox.warning(self, "Import Teams", "No standings data available.")
            return
        teams = self._qualified_provider() or []
        if not teams:
            QMessageBox.information(self, "Import Teams", "No teams found in standings.")
            return
        dlg = BracketTeamsImportDialog(self, teams)
        if dlg.exec_() != QDialog.Accepted:
            return
        selected = dlg.selected_teams()
        for idx, row in enumerate(self.team_rows):
            row.set_team(selected[idx] if idx < len(selected) else None)
        self._refresh_team_options()

    def reset_tab(self):
        self.title_edit.clear()
        self.stage_edit.clear()
        self._rounds = []
        self._team_options = []
        self._clear_team_list()
        self._set_double_elim_view_controls(False)
        self._single_elim_mode = False
        self._set_bronze_match_controls(False)
        self._build_bracket_view()
        self.updated.emit()

    def to_settings(self) -> BracketSettings:
        teams = [row.team_ref() for row in self.team_rows]
        return BracketSettings(
            title=self.title_edit.text().strip(),
            stage=self.stage_edit.text().strip(),
            rounds=self._rounds or [],
            double_elim_view=self._double_elim_view_value(),
            teams=teams,
        )

    def from_settings(self, settings: BracketSettings):
        self.title_edit.setText(settings.title or "")
        self.stage_edit.setText(settings.stage or "")
        teams = settings.teams or []
        for idx, row in enumerate(self.team_rows):
            row.set_team(teams[idx] if idx < len(teams) else None)
        self._refresh_team_options()
        self._rounds = []
        for rnd in settings.rounds or []:
            matches = []
            for match in rnd.matches or []:
                matches.append(BracketMatch(
                    id=match.id or self._new_match_id(),
                    bo_label=match.bo_label,
                    status=match.status,
                    team1=TeamRef(name=match.team1.name, abbr=match.team1.abbr, logo_path=match.team1.logo_path),
                    team2=TeamRef(name=match.team2.name, abbr=match.team2.abbr, logo_path=match.team2.logo_path),
                    score1=match.score1,
                    score2=match.score2,
                ))
            self._rounds.append(BracketRound(
                name=rnd.name,
                side=rnd.side,
                matches=matches,
            ))
        if self._supports_double_elim_view(self._rounds):
            self._set_double_elim_view_controls(True, settings.double_elim_view or "upper")
        else:
            self._set_double_elim_view_controls(False)
        self._single_elim_mode = self._is_single_elim_rounds(self._rounds)
        if self._single_elim_mode:
            self._set_bronze_match_controls(True, self._find_bronze_round_index() is not None)
        else:
            self._set_bronze_match_controls(False)
        self._build_bracket_view()


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

        # --- STANDINGS TAB ---
        self.standings_tab = StandingsTab()
        self.standings_tab.updated.connect(self._update)
        tabs.addTab(self.standings_tab, "Standings")

        # --- BRACKET TAB ---
        self.bracket_tab = BracketTab()
        self.bracket_tab.updated.connect(self._update)
        self.bracket_tab.set_qualified_provider(self._teams_from_standings)
        tabs.addTab(self.bracket_tab, "Bracket")
        
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

    def _normalize_logo_path(self, path: Optional[str]) -> Optional[str]:
        if not path:
            return None
        p = path.strip()
        if not p:
            return None
        if not os.path.isabs(p):
            return p.replace("\\", "/")
        root = self._scoreboard_root()
        try:
            rel = os.path.relpath(p, root)
            if not rel.startswith(".."):
                return rel.replace("\\", "/")
        except Exception:
            pass
        return p.replace("\\", "/")

    def _export_standings(self, settings: StandingsSettings):
        root = self._scoreboard_root()
        out_dir = os.path.join(root, "Standings")
        os.makedirs(out_dir, exist_ok=True)

        groups = settings.groups or []
        if not groups:
            groups = [StandingsGroup(key="group_1", name="Group 1", rows=settings.rows or [])]

        for group in groups:
            _apply_standings_ranks(group.rows or [])

        def build_rows(source_rows: List[StandingsRow]) -> List[dict]:
            payload_rows = []
            for r in source_rows or []:
                diff = int(r.map_diff or 0)
                payload_rows.append({
                    "team": r.team_name,
                    "abbr": r.abbr,
                    "logo": self._normalize_logo_path(r.logo_path),
                    "wins": int(r.wins or 0),
                    "losses": int(r.losses or 0),
                    "map_diff": diff,
                    "points": int(r.points or 0),
                    "status": r.status or "",
                    "rank": int(r.rank or 0),
                })
            return payload_rows

        groups_payload = []
        for group in groups:
            groups_payload.append({
                "key": group.key,
                "name": group.name or "",
                "rows": build_rows(group.rows or []),
            })

        display_group = settings.display_group or "__all__"
        selected_rows = []
        if display_group != "__all__":
            for group in groups:
                if group.key == display_group:
                    selected_rows = build_rows(group.rows or [])
                    break
        elif len(groups) == 1:
            selected_rows = build_rows(groups[0].rows or [])

        payload = {
            "title": settings.title or "",
            "subtitle": settings.subtitle or "",
            "columns": settings.columns or {"mode": "map_diff"},
            "rows": selected_rows,
            "groups": groups_payload,
            "display_group": display_group,
        }
        json_path = os.path.join(out_dir, "standings.json")
        changed = self._write_json(json_path, payload)
        if changed:
            stamp = time.strftime("%Y-%m-%d %H:%M:%S")
            self._write_txt(os.path.join(out_dir, "updated_at.txt"), stamp)

    def _export_bracket(self, settings: BracketSettings):
        root = self._scoreboard_root()
        out_dir = os.path.join(root, "Bracket")
        os.makedirs(out_dir, exist_ok=True)

        rounds = []
        for r in settings.rounds or []:
            matches = []
            for m in r.matches or []:
                matches.append({
                    "id": m.id,
                    "bo_label": m.bo_label,
                    "team1": {
                        "name": m.team1.name,
                        "abbr": m.team1.abbr,
                        "logo": self._normalize_logo_path(m.team1.logo_path),
                    },
                    "team2": {
                        "name": m.team2.name,
                        "abbr": m.team2.abbr,
                        "logo": self._normalize_logo_path(m.team2.logo_path),
                    },
                    "score1": int(m.score1 or 0),
                    "score2": int(m.score2 or 0),
                    "status": m.status or "",
                })
            rounds.append({
                "name": r.name,
                "side": r.side or "",
                "matches": matches,
            })

        payload = {
            "title": settings.title or "",
            "stage": settings.stage or "",
            "rounds": rounds,
            "double_elim_view": settings.double_elim_view or "",
        }
        json_path = os.path.join(out_dir, "bracket.json")
        changed = self._write_json(json_path, payload)
        if changed:
            stamp = time.strftime("%Y-%m-%d %H:%M:%S")
            self._write_txt(os.path.join(out_dir, "updated_at.txt"), stamp)

    def _teams_from_standings(self) -> List[TeamRef]:
        if not hasattr(self, "standings_tab"):
            return []
        settings = self.standings_tab.to_settings()
        rows: List[StandingsRow] = []
        if settings.groups:
            for group in settings.groups:
                rows.extend(group.rows or [])
        else:
            rows = list(settings.rows or [])
        def sort_key(row: StandingsRow):
            rank = int(row.rank or 0)
            return (rank if rank > 0 else 9999, row.team_name.lower())
        rows.sort(key=sort_key)
        teams = []
        for row in rows:
            name = (row.team_name or "").strip()
            if not name:
                continue
            teams.append(TeamRef(name=name, abbr=row.abbr, logo_path=row.logo_path))
        return teams

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

    def _write_json(self, path: str, payload: dict) -> bool:
        body = json.dumps(payload, ensure_ascii=False, indent=2)
        return self._write_txt(path, body)

            
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
        self._notify_overlays(["replay"])

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
                "maps", "standings", "bracket"
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

        if (old.get("standings") or {}) != (new.get("standings") or {}):
            keys.append("standings")
        if (old.get("bracket") or {}) != (new.get("bracket") or {}):
            keys.append("bracket")


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
            body = (
                f"Name={(m.get('map') or '').replace('\n', ' ').strip()}\n"
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
        if hasattr(self, "standings_tab"):
            standings = self.standings_tab.to_settings()
            state["standings"] = asdict(standings)
        if hasattr(self, "bracket_tab"):
            bracket = self.bracket_tab.to_settings()
            state["bracket"] = asdict(bracket)
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

        sdata = state.get("standings", {}) or {}
        rows = [_standings_row_from_dict(r) for r in (sdata.get("rows") or [])]
        groups_data = sdata.get("groups") or []
        groups = []
        for idx, g in enumerate(groups_data, start=1):
            groups.append(_standings_group_from_dict(g, fallback_key=f"group_{idx}"))
        if not groups and rows:
            groups = [StandingsGroup(key="group_1", name="Group 1", rows=rows)]
        s_settings = StandingsSettings(
            title=sdata.get("title", ""),
            subtitle=sdata.get("subtitle", ""),
            columns=sdata.get("columns") or {"mode": "map_diff"},
            rows=rows,
            groups=groups,
            display_group=sdata.get("display_group", ""),
        )
        if hasattr(self, "standings_tab"):
            self.standings_tab.from_settings(s_settings)

        bdata = state.get("bracket", {}) or {}
        rounds = []
        for r in bdata.get("rounds") or []:
            matches = []
            for m in r.get("matches") or []:
                team1 = TeamRef(**(m.get("team1") or {}))
                team2 = TeamRef(**(m.get("team2") or {}))
                matches.append(BracketMatch(
                    id=m.get("id", ""),
                    bo_label=m.get("bo_label", ""),
                    team1=team1,
                    team2=team2,
                    score1=int(m.get("score1", 0) or 0),
                    score2=int(m.get("score2", 0) or 0),
                    status=m.get("status", ""),
                ))
            rounds.append(BracketRound(
                name=r.get("name", ""),
                side=r.get("side", ""),
                matches=matches,
            ))
        b_settings = BracketSettings(
            title=bdata.get("title", ""),
            stage=bdata.get("stage", ""),
            rounds=rounds,
            double_elim_view=bdata.get("double_elim_view", ""),
            teams=[TeamRef(**(t or {})) for t in (bdata.get("teams") or [])],
        )
        if hasattr(self, "bracket_tab"):
            self.bracket_tab.from_settings(b_settings)
            
        pool = state.get("map_pool") or []
        if hasattr(self, "draft_tab"):
            self.draft_tab.set_pool(pool)

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
        if "standings" in state:
            s = state.get("standings") or {}
            s_settings = StandingsSettings(
                title=s.get("title", ""),
                subtitle=s.get("subtitle", ""),
                columns=s.get("columns") or {"mode": "map_diff"},
                rows=[_standings_row_from_dict(r) for r in (s.get("rows") or [])],
                groups=[_standings_group_from_dict(g, fallback_key=f"group_{idx}") for idx, g in enumerate(s.get("groups") or [], start=1)],
                display_group=s.get("display_group", ""),
            )
            self._export_standings(s_settings)
        if "bracket" in state:
            b = state.get("bracket") or {}
            rounds = []
            for r in b.get("rounds") or []:
                matches = []
                for m in r.get("matches") or []:
                    matches.append(BracketMatch(
                        id=m.get("id", ""),
                        bo_label=m.get("bo_label", ""),
                        team1=TeamRef(**(m.get("team1") or {})),
                        team2=TeamRef(**(m.get("team2") or {})),
                        score1=int(m.get("score1", 0) or 0),
                        score2=int(m.get("score2", 0) or 0),
                        status=m.get("status", ""),
                    ))
                rounds.append(BracketRound(
                    name=r.get("name", ""),
                    side=r.get("side", ""),
                    matches=matches,
                ))
            b_settings = BracketSettings(
                title=b.get("title", ""),
                stage=b.get("stage", ""),
                rounds=rounds,
                double_elim_view=b.get("double_elim_view", ""),
                teams=[TeamRef(**(t or {})) for t in (b.get("teams") or [])],
            )
            self._export_bracket(b_settings)

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
