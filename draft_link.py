# draft_link.py
# Live yhteys SOWDraft-draft-huoneeseen: kuuntelee samaa julkista "stream"-roolia
# Socket.io-kanavaa jota SOWDraftin oma OBS-overlay (public/stream.html) käyttää.
# Ei vaadi mitään muutoksia SOWDraftiin - "stream"-rooli on jo tokenitonta ja julkista.
import threading
from typing import Optional
from urllib.parse import urlparse, parse_qs

import socketio
from PyQt5.QtCore import QObject, pyqtSignal


class DraftLinkSignals(QObject):
    # Kaikki emit()-kutsut voivat tulla socketio:n taustasäikeeltä - PyQt jonottaa
    # ne automaattisesti pääsäikeelle, koska tämä QObject luodaan pääsäikeellä.
    state_received = pyqtSignal(dict)
    status_changed = pyqtSignal(str)  # "connecting" | "connected" | "disconnected" | "error:<msg>"


class DraftLinkClient:
    """Yhdistää SOWDraftin draft-huoneeseen 'stream'-roolilla (julkinen, ei tokenia)."""

    def __init__(self):
        self.signals = DraftLinkSignals()
        self.match_id: Optional[str] = None
        self.stream_url: Optional[str] = None
        self._origin: Optional[str] = None
        self._sio = socketio.Client(reconnection=True, logger=False, engineio_logger=False)
        self._sio.on('connect', self._on_connect)
        self._sio.on('disconnect', self._on_disconnect)
        self._sio.on('connect_error', self._on_connect_error)
        self._sio.on('update-state', self._on_update_state)
        self._sio.on('error', self._on_server_error)

    @staticmethod
    def parse_stream_url(stream_url: str):
        """Palauttaa (origin, matchId) tai nostaa ValueErrorin jos linkki on virheellinen."""
        parsed = urlparse(stream_url.strip())
        if not parsed.scheme or not parsed.netloc:
            raise ValueError("Linkki ei ole kelvollinen URL.")
        qs = parse_qs(parsed.query)
        match_id = (qs.get('matchId') or [None])[0]
        if not match_id:
            raise ValueError("Linkistä ei löytynyt matchId-parametria.")
        return f"{parsed.scheme}://{parsed.netloc}", match_id

    def connect(self, stream_url: str):
        origin, match_id = self.parse_stream_url(stream_url)
        self.stream_url = stream_url.strip()
        self._origin = origin
        self.match_id = match_id

        if self._sio.connected:
            try:
                self._sio.disconnect()
            except Exception:
                pass

        self.signals.status_changed.emit("connecting")
        threading.Thread(target=self._connect_thread, args=(origin,), daemon=True).start()

    def _connect_thread(self, origin: str):
        try:
            self._sio.connect(origin, transports=["websocket", "polling"], wait_timeout=10)
        except Exception as e:
            self.signals.status_changed.emit(f"error:{e}")

    def disconnect(self):
        self.match_id = None
        try:
            if self._sio.connected:
                self._sio.disconnect()
        except Exception:
            pass
        self.signals.status_changed.emit("disconnected")

    def _on_connect(self):
        self._sio.emit('join-match', {'matchId': self.match_id, 'role': 'stream'})
        self.signals.status_changed.emit("connected")

    def _on_disconnect(self):
        self.signals.status_changed.emit("disconnected")

    def _on_connect_error(self, data=None):
        self.signals.status_changed.emit(f"error:{data or 'connection failed'}")

    def _on_server_error(self, data):
        self.signals.status_changed.emit(f"error:{data}")

    def _on_update_state(self, data):
        if isinstance(data, dict):
            self.signals.state_received.emit(data)
