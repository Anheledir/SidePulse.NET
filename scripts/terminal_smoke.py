"""Exercise real cursor menus and editor/preview controls on Unix pseudo terminals."""
import os
from pathlib import Path
import select
import struct
import subprocess
import sys
import tempfile
import time


def main(executable):
    if os.name == 'nt':
        print('PTY smoke uses Unix APIs; Windows runs the redirected CLI smoke.')
        return
    import fcntl
    import pty
    import termios
    executable = str(Path(executable).resolve())
    with tempfile.TemporaryDirectory(prefix='sidepulse terminal ') as temporary:
        root = Path(temporary)
        env = dict(os.environ, TERM='xterm-256color', COLORTERM='truecolor', SIDEPULSE_NET_HOME=str(root / 'config'))
        env.pop('NO_COLOR', None)

        class Terminal:
            def __init__(self, args):
                self.master, slave = pty.openpty()
                fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack('HHHH', 30, 100, 0, 0))
                self.process = subprocess.Popen([executable, *args], stdin=slave, stdout=slave, stderr=slave, env=env)
                os.close(slave)
                self.output = b''

            def until(self, text):
                needle = text.encode('utf-8')
                deadline = time.monotonic() + 10
                while time.monotonic() < deadline:
                    if needle in self.output:
                        self.output = self.output[self.output.index(needle) + len(needle):]
                        return
                    if select.select([self.master], [], [], 0.1)[0]:
                        try:
                            data = os.read(self.master, 65536)
                        except OSError:
                            break
                        self.output += data
                        if needle in self.output:
                            self.output = self.output[self.output.index(needle) + len(needle):]
                            return
                    if self.process.poll() is not None:
                        break
                raise AssertionError((text, self.process.poll(), self.output[-3000:].decode('utf-8', errors='replace')))

            def send(self, keys):
                os.write(self.master, keys)

            def finish(self):
                try:
                    # Keep consuming terminal output while the app clears/redraws on exit.
                    # macOS PTYs have small buffers; waiting without reading can block it.
                    deadline = time.monotonic() + 10
                    while self.process.poll() is None and time.monotonic() < deadline:
                        if select.select([self.master], [], [], 0.1)[0]:
                            try:
                                self.output += os.read(self.master, 65536)
                            except OSError:
                                break
                    assert self.process.wait(timeout=1) == 0, self.output[-3000:]
                finally:
                    if self.process.poll() is None:
                        self.process.kill()
                    os.close(self.master)

        menu = Terminal([])
        try:
            menu.until('Host: not connected')
            menu.until('Quit')
            menu.send(b'\x1b')  # Esc at Home never exits.
            menu.until('Quit')
            menu.send(b'/')
            menu.until('Actions')
            menu.send(b'Help\r')  # searchable action palette
            for _ in range(4):
                menu.until('Manufacturer website')
                if _ == 0:
                    menu.send(b'\x1b[B' * 4)
                menu.send(b'\r')
                menu.until('Peter Kuhar')
            menu.until('Manufacturer website')
            menu.send(b'\x1b')
            menu.until('Quit')
            menu.send(b'/')
            menu.until('Actions')
            menu.send(b'Send\r')
            menu.until('Preview mode: off')
            menu.send(b'\r')  # Live send fails; remain in the same workspace.
            menu.until('No output found')
            menu.until('Preview mode: off')
            fcntl.ioctl(menu.master, termios.TIOCSWINSZ, struct.pack('HHHH', 20, 60, 0, 0))
            menu.send(b'\x1b[B' * 6 + b'\r')  # Enable Preview once.
            for _ in range(5):
                menu.until('Preview mode: on')
                menu.send(b'\r')
                menu.until('LED preview')
            menu.until('Preview mode: on')
            menu.send(b'\x1b')
            menu.until('Quit')
            menu.send(b'\x1b[B' * 2 + b'\r')  # Devices & phones
            menu.until('Mounted devices')
            menu.send(b'\r')
            menu.until('Completed: devices')
            menu.until('Mounted devices')
            menu.send(b'\x1b')
            menu.until('Quit')
            menu.send(b'\x1b[A' * 3 + b'\r')  # explicit Quit
            menu.finish()
        finally:
            if menu.process.poll() is None:
                menu.process.kill()

        settings = Terminal([])
        try:
            settings.until('Last action:')
            settings.until('Quit')
            settings.send(b'\x1b[B' * 4 + b'\r')
            settings.until('Preferences')
            settings.send(b'\r')
            settings.until('Show settings')
            settings.send(b'\x1b[B' * 2 + b'\r')
            settings.until('input retained')
            settings.send(b'\x15' + b'42\x03')
            settings.until('Show settings')
            settings.send(b'\r')
            settings.until('input retained')
            settings.send(b'\r')
            settings.until('Show settings')
            settings.send(b'\x1b')
            settings.until('Diagnostics')
            settings.send(b'\x1b')
            settings.until('Brightness: 42%')
            settings.until('Quit')
            settings.send(b'\x1b[B' * 2 + b'\r')
            settings.finish()
        finally:
            if settings.process.poll() is None:
                settings.process.kill()
        import json
        assert json.loads((root / 'config' / 'settings.json').read_text())['Brightness'] == 42
        restarted = Terminal([])
        try:
            restarted.until('Brightness: 42%')
            restarted.until('Quit')
            restarted.send(b'\x03')
            restarted.finish()
        finally:
            if restarted.process.poll() is None:
                restarted.process.kill()

        host = subprocess.Popen([executable, 'host', 'run', '--dry-run', '--plain'],
                                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, env=env)
        connected = None
        try:
            for _ in range(20):
                status = subprocess.run([executable, 'host', 'status', '--json'], capture_output=True,
                                        text=True, env=env, timeout=5)
                if status.returncode == 0 and json.loads(status.stdout)['Running']:
                    break
                assert host.poll() is None, 'Dry-run host exited before readiness'
            else:
                raise AssertionError('Dry-run host did not become ready')
            connected = Terminal([])
            connected.until('Host: running')
            connected.until('Quit')
            connected.send(b'\x1b[A\r')
            connected.finish()
        finally:
            if connected is not None and connected.process.poll() is None:
                connected.process.kill()
            host.terminate()
            host.wait(timeout=5)

        file = root / 'custom.LED'
        editor = Terminal(['animations', 'edit', '--file', str(file), '--model', 'dot'])
        try:
            editor.until('Save to library')
            editor.send(b'\r')  # preview
            editor.until('Playing')
            editor.send(b' ')
            editor.until('Paused')
            editor.send(b'\x1b[C')  # frame step
            time.sleep(0.15)
            editor.send(b'r')
            time.sleep(0.15)
            editor.send(b'\x1b')  # back to editor
            editor.until('Save to library')
            editor.send(b'\x1b[B' * 5 + b'\r')  # save
            editor.until('Pattern saved:')
            assert file.exists() and '#00e5ff' in file.read_text(encoding='utf-8')
            editor.send(b'\x1b')
            editor.finish()
        finally:
            if editor.process.poll() is None:
                editor.process.kill()
        print('Persistent navigation, ten actions across three sections, failed-send recovery, narrow preview and editor save passed.')


if __name__ == '__main__':
    main(sys.argv[1])
