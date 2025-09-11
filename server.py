# server.py  — Static + SSE push + /notify
import os, sys, json, time, argparse, http.server, threading, socketserver

def _default_base():
    """
    Kun paketoituna (--onefile), sys.executable voi osoittaa _MEI-temp-kansioon.
    Käytä silloin prosessin CWD:tä (launcher.bat tekee pushd {app})
    tai, varalla, alkuperäisen EXE:n kansiota sys.argv[0]:sta.
    """
    if getattr(sys, "frozen", False):
        try:
            return os.getcwd()
        except Exception:
            pass
        return os.path.dirname(os.path.abspath(sys.argv[0]))
    return os.path.dirname(os.path.abspath(__file__))

# ---- SSE -tila ----
_event_id = 0
_last_payload = ""          # JSON-merkkijono
_cv = threading.Condition() # ilmoitetaan kun uusi viesti on valmis

class PushHandler(http.server.SimpleHTTPRequestHandler):
    # estä välimuisti staattisillekin pyynnöille
    def end_headers(self):
        self.send_header("Cache-Control", "no-cache, no-store, must-revalidate")
        self.send_header("Pragma", "no-cache")
        self.send_header("Expires", "0")
        super().end_headers()

    def log_message(self, fmt, *args):
        return

    # SSE-virta
    def _handle_events(self):
        self.send_response(200)
        self.send_header("Content-Type", "text/event-stream")
        self.send_header("Cache-Control", "no-cache")
        self.send_header("Connection", "keep-alive")
        self.end_headers()

        # pieni tervehdys
        try:
            self.wfile.write(b": connected\n\n")
            self.wfile.flush()
        except Exception:
            return

        last_sent = -1
        while True:
            try:
                with _cv:
                    # jos uutta ei ole, odota ja lähetä heartbeat 15s välein
                    if _event_id == last_sent:
                        _cv.wait(timeout=15.0)
                # heartbeat
                if _event_id == last_sent:
                    self.wfile.write(b": keepalive\n\n")
                    self.wfile.flush()
                    continue

                # uusi viesti
                payload = _last_payload.encode("utf-8")
                msg = b"id: " + str(_event_id).encode() + b"\n" + b"data: " + payload + b"\n\n"
                self.wfile.write(msg)
                self.wfile.flush()
                last_sent = _event_id
            except Exception:
                # selain sulki yhteyden
                break

    # POST /notify  — GUI kutsuu tätä ja antaa {"changed":[...]}
    def _handle_notify(self):
        length = int(self.headers.get("Content-Length", "0"))
        body = self.rfile.read(length) if length > 0 else b"{}"
        try:
            data = json.loads(body.decode("utf-8"))
            if not isinstance(data, dict) or "changed" not in data:
                raise ValueError("missing 'changed'")
            # talteen ja ilmoitus
            global _event_id, _last_payload
            with _cv:
                _event_id += 1
                _last_payload = json.dumps({"changed": list(data["changed"])})
                _cv.notify_all()
            self.send_response(204)
            self.end_headers()
        except Exception as e:
            self.send_response(400)
            self.end_headers()
            self.wfile.write(str(e).encode("utf-8"))

    def do_GET(self):
        if self.path.startswith("/events"):
            return self._handle_events()
        return super().do_GET()

    def do_POST(self):
        if self.path.startswith("/notify"):
            return self._handle_notify()
        return super().do_POST()

def main():
    p = argparse.ArgumentParser()
    p.add_argument("--bind", default="127.0.0.1")
    p.add_argument("--port", type=int, default=8324)
    p.add_argument("--root", default=None, help="Serve files from this directory")
    args = p.parse_args()

    # *** TÄSSÄ MUUTOS: käytä --root tai _default_base()a ***
    base = os.path.abspath(args.root.strip('"')) if args.root else _default_base()
    os.chdir(base)

    httpd = http.server.ThreadingHTTPServer((args.bind, args.port), PushHandler)
    print("PLEASE KEEP THIS WINDOW OPEN")
    try:
        httpd.serve_forever()
    finally:
        httpd.server_close()

if __name__ == "__main__":
    main()
