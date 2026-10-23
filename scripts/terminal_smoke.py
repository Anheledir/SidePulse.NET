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
                fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack('HHHH', 40, 120, 0, 0))
                self.process = subprocess.Popen([executable, *args], stdin=slave, stdout=slave, stderr=slave, env=env)
                os.close(slave)
                self.output = b''

            def until(self, text):
                needle = text.encode('utf-8')
                deadline = time.monotonic() + 10
                while time.monotonic() < deadline:
                    if select.select([self.master], [], [], 0.1)[0]:
                        try:
                            data = os.read(self.master, 65536)
                        except OSError:
                            break
                        self.output += data
                        if needle in self.output:
                            self.output = b''
                            return
                    if self.process.poll() is not None:
                        break
                raise AssertionError((text, self.process.poll(), self.output[-3000:].decode('utf-8', errors='replace')))

            def send(self, keys):
                os.write(self.master, keys)

            def finish(self):
                try:
                    assert self.process.wait(timeout=10) == 0
                finally:
                    if self.process.poll() is None:
                        self.process.kill()
                    os.close(self.master)

        menu = Terminal([])
        try:
            menu.until('About')
            menu.send(b'\x1b[A\r')  # wrap from Animations to About
            menu.until('Manufacturer website')
            menu.send(b'\x1b[B' * 4 + b'\r')
            menu.until('Peter Kuhar')
            menu.finish()
        finally:
            if menu.process.poll() is None:
                menu.process.kill()

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
        print('Cursor menus, Spectre preview controls and editor save passed.')


if __name__ == '__main__':
    main(sys.argv[1])
