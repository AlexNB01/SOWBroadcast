# launch.py
import os, sys, time, subprocess

PORT = 8324
SERVER_SCRIPT = "server.py"   # jos sinulla on nimi "serve.py", vaihda tähän

def start_server(base, py):
    try:
        p = subprocess.Popen([py, "-u", SERVER_SCRIPT, "--port", str(PORT)],
                             cwd=base, stdin=subprocess.DEVNULL)
        print(f"[LAUNCH] Local server on http://127.0.0.1:{PORT}/ (pid {p.pid})")
        time.sleep(0.8)  # pieni hetki serverille
        return p
    except Exception as e:
        print(f"[LAUNCH] Failed to start server: {e}")
        return None

def start_gui(base, py):
    try:
        p = subprocess.Popen([py, "-u", "SOWBroadcast.py"], cwd=base)
        print("[LAUNCH] GUI started. Close it to return to this menu.")
        return p
    except Exception as e:
        print(f"[LAUNCH] Failed to start GUI: {e}")
        return None

def stop_process(p, name):
    if not p:
        return
    try:
        if p.poll() is None:
            p.terminate()
            p.wait(timeout=5)
    except subprocess.TimeoutExpired:
        print(f"[LAUNCH] {name} didn't exit, killing…")
        try:
            p.kill()
        except Exception:
            pass
    except Exception as e:
        print(f"[LAUNCH] Error stopping {name}: {e}")

def pause_exit():
    try:
        input("\nPress Enter to close this window...")
    except EOFError:
        pass

def main():
    base = os.path.abspath(os.path.dirname(__file__))
    py   = sys.executable

    server = start_server(base, py)

    while True:
        gui = start_gui(base, py)
        if not gui:
            choice = input("[LAUNCH] (R)etry, (Q)uit: ").strip().lower()
            if choice.startswith("q"):
                break
            else:
                continue

        # odota GUI:n päättymistä, mutta jätä konsoli auki
        while True:
            try:
                rc = gui.wait(timeout=0.5)
                print(f"[LAUNCH] GUI exited with code {rc}.")
                break
            except subprocess.TimeoutExpired:
                continue
            except KeyboardInterrupt:
                print("\n[LAUNCH] Ctrl+C pressed. Stopping GUI…")
                stop_process(gui, "GUI")
                rc = gui.poll()
                print(f"[LAUNCH] GUI exited with code {rc}.")
                break

        # valikko: jatketaanko vai lopetetaanko
        choice = input("[LAUNCH] (R)estart GUI  (Q)uit: ").strip().lower()
        if choice.startswith("q"):
            break
        # muuten looppi käynnistää GUI:n uudelleen

    print("[LAUNCH] Shutting down…")
    stop_process(server, "server")
    pause_exit()

if __name__ == "__main__":
    main()
