# tournament_link.py
# Live yhteys SOWDraftin turnaussivuun: hakee joukkueet/standingsit/bracketin
# julkisesta REST-rajapinnasta (GET /api/tournament-system/:id) ja pysyy
# ajan tasalla kuuntelemalla samaa julkista "tournament-update"-Socket.io-
# tapahtumaa jota sowdraftin oma turnaussivu käyttää. Ei vaadi mitään
# muutoksia SOWDraftiin - rajapinta ja tapahtuma ovat jo julkisia.
import json
import threading
import urllib.request
from typing import Optional
from urllib.parse import urlparse, parse_qs, urljoin

import socketio
from PyQt5.QtCore import QObject, pyqtSignal


class TournamentLinkSignals(QObject):
    # Kaikki emit()-kutsut voivat tulla socketio:n tai HTTP-hakusäikeen
    # taustalta - PyQt jonottaa ne automaattisesti pääsäikeelle, koska tämä
    # QObject luodaan pääsäikeellä.
    data_received = pyqtSignal(dict)
    status_changed = pyqtSignal(str)  # "connecting" | "connected" | "disconnected" | "error:<msg>"


class TournamentLinkClient:
    """Yhdistää SOWDraftin julkiseen turnaussivuun: hakee tiedot REST-rajapinnasta
    ja päivittää ne live 'tournament-update'-tapahtuman perusteella."""

    def __init__(self):
        self.signals = TournamentLinkSignals()
        self.tournament_id: Optional[str] = None
        self.tournament_url: Optional[str] = None
        self._origin: Optional[str] = None
        self._sio = socketio.Client(reconnection=True, logger=False, engineio_logger=False)
        self._sio.on('connect', self._on_connect)
        self._sio.on('disconnect', self._on_disconnect)
        self._sio.on('connect_error', self._on_connect_error)
        self._sio.on('tournament-update', self._on_tournament_update)

    @staticmethod
    def parse_tournament_url(tournament_url: str):
        """Palauttaa (origin, tournamentId) tai nostaa ValueErrorin jos linkki on
        virheellinen. Tukee sekä sowdraftin oikeaa polkumuotoa
        (/tournament/<id-tai-slug>) että ?id=-kyselyparametria."""
        parsed = urlparse(tournament_url.strip())
        if not parsed.scheme or not parsed.netloc:
            raise ValueError("Linkki ei ole kelvollinen URL.")
        origin = f"{parsed.scheme}://{parsed.netloc}"

        segments = [s for s in parsed.path.split('/') if s]
        tournament_id = None
        for i, seg in enumerate(segments):
            if seg == 'tournament' and i + 1 < len(segments):
                tournament_id = segments[i + 1]
                break
        if not tournament_id:
            qs = parse_qs(parsed.query)
            tournament_id = (qs.get('id') or [None])[0]
        if not tournament_id:
            raise ValueError("Linkistä ei löytynyt turnauksen tunnistetta.")
        return origin, tournament_id

    def connect(self, tournament_url: str):
        origin, tournament_id = self.parse_tournament_url(tournament_url)
        self.tournament_url = tournament_url.strip()
        self._origin = origin
        self.tournament_id = tournament_id

        if self._sio.connected:
            try:
                self._sio.disconnect()
            except Exception:
                pass

        self.signals.status_changed.emit("connecting")
        threading.Thread(target=self._fetch_thread, daemon=True).start()
        threading.Thread(target=self._connect_thread, args=(origin,), daemon=True).start()

    def disconnect(self):
        self.tournament_id = None
        try:
            if self._sio.connected:
                self._sio.disconnect()
        except Exception:
            pass
        self.signals.status_changed.emit("disconnected")

    def _connect_thread(self, origin: str):
        try:
            self._sio.connect(origin, transports=["websocket", "polling"], wait_timeout=10)
        except Exception as e:
            self.signals.status_changed.emit(f"error:{e}")

    def _fetch_thread(self, retry: bool = True):
        origin = self._origin
        tournament_id = self.tournament_id
        try:
            url = f"{origin}/api/tournament-system/{tournament_id}"
            # Ilman selaimenkaltaista User-Agentia moni CDN/WAF (mm. sowdraft.fi:n
            # edessä oleva) palauttaa 403:n Python-urllibin oletus-UA:lle vaikka
            # rajapinta itsessään on julkinen ja toimiva.
            req = urllib.request.Request(url, headers={
                "User-Agent": "Mozilla/5.0 (compatible; SOWBroadcast)",
                "Accept": "application/json",
            })
            with urllib.request.urlopen(req, timeout=10) as resp:
                data = json.loads(resp.read().decode("utf-8"))
            if not isinstance(data, dict) or not data.get("tournament"):
                raise ValueError("Turnausta ei löytynyt.")
            # Vain oman connect()-kutsun tulos huomioidaan, jos toinen linkitys
            # on ehtinyt korvata sen sillä välin.
            if self.tournament_id != tournament_id:
                return
            data["_origin"] = origin
            self.signals.data_received.emit(data)
        except Exception as e:
            self.signals.status_changed.emit(f"error:{e}")
            # Yksi automaattinen uusintayritys - moni virhe (hetkellinen 403/timeout
            # yhteyden muodostuessa) korjautuu itsestään parin sekunnin kuluttua.
            if retry and self.tournament_id == tournament_id:
                threading.Timer(4.0, lambda: self._fetch_thread(retry=False)).start()

    def _on_connect(self):
        self.signals.status_changed.emit("connected")

    def _on_disconnect(self):
        self.signals.status_changed.emit("disconnected")

    def _on_connect_error(self, data=None):
        self.signals.status_changed.emit(f"error:{data or 'connection failed'}")

    def _on_tournament_update(self, tournament_id):
        if not self.tournament_id or tournament_id != self.tournament_id:
            return
        threading.Thread(target=self._fetch_thread, daemon=True).start()
