"""Editor-only supervisor. Owns a private process group, never scans/kills other servers."""
import json
import os
from pathlib import Path
import signal
import select
import subprocess
import sys
import threading
import time


def supervise(uvx, directory, token, owner):
    directory = Path(directory)
    state = {"token": token, "supervisorPid": os.getpid(), "serverPid": 0,
             "status": "starting", "message": ""}

    def publish(status, message=""):
        state.update(status=status, message=message)
        temporary = directory / "status.tmp"
        temporary.write_text(json.dumps(state))
        temporary.replace(directory / "status.json")

    child = None
    watcher = None
    stopping = threading.Event()
    for sig in (signal.SIGTERM, signal.SIGINT):
        signal.signal(sig, lambda *_: stopping.set())
    try:
        publish("starting")
        child = subprocess.Popen([uvx, "styly-netsync-server@0.17.4"],
                                 start_new_session=True, cwd=str(directory),
                                 stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        state["serverPid"] = child.pid
        if hasattr(select, "kqueue"):
            watcher = select.kqueue()
            watcher.control([select.kevent(child.pid, filter=select.KQ_FILTER_PROC,
                            flags=select.KQ_EV_ADD, fflags=select.KQ_NOTE_EXIT)], 0, 0)

        def drain():
            path = directory / "server.log"
            with path.open("ab", buffering=0) as output:
                while True:
                    chunk = child.stdout.readline(8192)
                    if not chunk:
                        break
                    if output.tell() + len(chunk) > 2 * 1024 * 1024:
                        output.seek(0)
                        output.truncate()
                    output.write(chunk)

        reader = threading.Thread(target=drain, daemon=True)
        reader.start()
        publish("running")
        while not stopping.is_set():
            # Observe exit without reaping: reserve the leader PID until group cleanup.
            exited = (bool(watcher.control(None, 1, 0)) if watcher is not None else
                      os.waitid(os.P_PID, child.pid, os.WEXITED | os.WNOHANG | os.WNOWAIT) is not None)
            if exited:
                state["message"] = "Server exited unexpectedly. See server.log."
                break
            if os.getppid() != owner:
                stopping.set()
                break
            request = directory / "stop.request"
            if request.exists() and request.read_text().strip() == token:
                stopping.set()
                break
            time.sleep(0.2)
        publish("stopping")
    except Exception as error:
        state["message"] = str(error)
    finally:
        if child is not None:
            # All descendants inherit this private group. Keep the group leader
            # unreaped while terminating descendants, avoiding PID reuse.
            try:
                os.killpg(child.pid, signal.SIGTERM)
                time.sleep(1)
                os.killpg(child.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            except PermissionError as error:
                # macOS may return EPERM when only zombies remain in the group.
                listing = subprocess.run(["/bin/ps", "-axo", "pgid=,stat="],
                                         capture_output=True, text=True)
                alive = any(len(parts) == 2 and parts[0] == str(child.pid)
                            and not parts[1].startswith("Z")
                            for parts in (line.split() for line in listing.stdout.splitlines()))
                if listing.returncode != 0 or alive:
                    state["message"] = "Could not stop owned process group: " + str(error)
            try:
                child.wait(timeout=2)
            except subprocess.TimeoutExpired:
                state["message"] = "Owned server did not stop; see server.log."
        if watcher is not None:
            watcher.close()
        publish("failed" if state["message"] else "stopped", state["message"])


if __name__ == "__main__":
    supervise(sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4]))
