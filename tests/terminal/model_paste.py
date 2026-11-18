"""Exercise the actual CLI in Unix pseudoterminals after a Debug build."""
from contextlib import contextmanager
import errno
import fcntl
import os
from pathlib import Path
import pty
import re
import select
import signal
import socket
import struct
import subprocess
import tempfile
import termios
import time

DLL = Path(__file__).resolve().parents[2] / "src/MafPlayground.CLI/bin/Debug/net10.0/MafPlayground.CLI.dll"
CSI = re.compile(rb"\x1b\[[0-?]*[ -/]*[@-~]")


class Terminal:
    def __init__(self, args, env):
        self.pid, self.fd = pty.fork()
        if self.pid == 0:
            self.resize(120, 0)
            os.execvpe("dotnet", ["dotnet", str(DLL), "tui", *args], env)

    def resize(self, columns, fd=None):
        fcntl.ioctl(self.fd if fd is None else fd, termios.TIOCSWINSZ, struct.pack("HHHH", 40, columns, 0, 0))

    def send(self, keys):
        os.write(self.fd, keys)

    def expect(self, label):
        output = bytearray()
        end = time.monotonic() + 8
        while time.monotonic() < end:
            if select.select([self.fd], [], [], 0.1)[0]:
                try:
                    data = os.read(self.fd, 65536)
                except OSError as error:
                    if error.errno != errno.EIO:
                        raise
                    break
                if not data:
                    break
                output.extend(data)
                if label in CSI.sub(b"", output):
                    # A repaint can span several writes. Consume the rest of this
                    # frame so it cannot satisfy the next key's expectation.
                    frame_end = time.monotonic() + 0.25
                    while time.monotonic() < frame_end and select.select([self.fd], [], [], 0.05)[0]:
                        if not os.read(self.fd, 65536):
                            break
                    return
        words = re.findall(rb"[A-Za-z0-9_{}:.,\x22 -]{3,}", CSI.sub(b"", output))
        raise AssertionError(f"Expected {label!r}, last screen text: {b' '.join(w.strip() for w in words if w.strip())[-1800:]!r}")

    def expect_exit(self, code):
        end = time.monotonic() + 6
        while time.monotonic() < end:
            pid, status = os.waitpid(self.pid, os.WNOHANG)
            if pid:
                self.pid = None
                assert os.waitstatus_to_exitcode(status) == code, status
                return
            if select.select([self.fd], [], [], 0.1)[0]:
                try:
                    os.read(self.fd, 65536)
                except OSError as error:
                    if error.errno != errno.EIO:
                        raise
        raise AssertionError("TUI did not exit")

    def close(self):
        os.close(self.fd)
        if self.pid:
            os.kill(self.pid, signal.SIGKILL)
            os.waitpid(self.pid, 0)


@contextmanager
def serve(env, directory):
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        host = f"http://127.0.0.1:{sock.getsockname()[1]}"
    with tempfile.TemporaryFile() as log:
        proc = subprocess.Popen(["dotnet", str(DLL), "serve", "--url", host, "--model", "ollama:fixture"],
                                env=env, cwd=directory, stdout=log, stderr=subprocess.STDOUT)
        try:
            end = time.monotonic() + 10
            while proc.poll() is None and time.monotonic() < end:
                match = re.search(rb"Local API bearer token: (\S+)", os.pread(log.fileno(), 65536, 0))
                if match:
                    yield host, match[1].decode()
                    return
                time.sleep(0.05)
            raise AssertionError("Studio did not start")
        finally:
            proc.terminate()
            try:
                proc.wait(5)
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.wait()


def check(env, empty_input, host=None):
    args = ["--host", host, "--input", str(empty_input)] if host else []
    inspector = b"Draft  translation-workflow" if host else b"studio / translation-workflow"
    editor = b"Input JSON" if host else b"Local model"
    text = b'{"text":"paste-test"}' if host else b"ollama:paste-test"
    terminal = Terminal(args, env)
    try:
        terminal.expect(b"EXAMPLE INPUT")
        terminal.send(b"\x1b[B" * 3 + b"\r")
        terminal.expect(inspector)
        for i in range(5):
            terminal.send(b"c")
            terminal.expect(b"EXAMPLE INPUT")
            terminal.resize(118 + i % 3)
            terminal.send(b"\x1b[A\x1b[B\r")
            terminal.expect(inspector)
        open_editor = b"i" if host else b"m"
        terminal.send(open_editor)
        terminal.expect(editor)
        terminal.send(b"\x1b[200~" + text + b"\x1b[201~")
        terminal.expect(text)
        terminal.send(b"\t\r" if host else b"\r")
        terminal.expect(b"Workflow topology" if host else inspector)
        terminal.send(open_editor)
        terminal.expect(text)
        terminal.send(b"\x1b")
        terminal.expect(b"Workflow topology" if host else inspector)
        terminal.send(b"\x10")
        terminal.expect(b"Find an action")
        terminal.send(b"\x1b")
        terminal.expect(b"Workflow topology" if host else inspector)
        terminal.send(open_editor)
        terminal.expect(text)
        terminal.send(b"\x03")
        terminal.expect_exit(130)
    finally:
        terminal.close()


if __name__ == "__main__":
    with tempfile.TemporaryDirectory() as directory:
        empty = Path(directory, "input.json")
        empty.write_text("")
        env = {k: v for k, v in os.environ.items() if k in ("PATH", "HOME", "DOTNET_ROOT", "LANG", "TMPDIR")}
        env.update(TERM="xterm-256color", AI_MODEL="", OTEL_SDK_DISABLED="true", MAF_TUI_CASES_DIR=directory)
        check(env, empty)
        print("Local catalog Enter, bracketed model paste, Apply and Ctrl+C passed.", flush=True)
        with serve(env, directory) as (host, token):
            check(dict(env, MAF_STUDIO_TOKEN=token), empty, host)
        print("Remote catalog Enter, bracketed JSON paste, Apply and Ctrl+C passed.")
