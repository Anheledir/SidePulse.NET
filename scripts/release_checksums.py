"""Write the release checksum list after collecting all six platform archives."""
import hashlib
from pathlib import Path
import sys


def write(directory):
    root = Path(directory)
    expected = {f'sidepulse-net-{platform}-{arch}' + ('.zip' if platform == 'win' else '.tar.gz')
                for platform in ('win', 'linux', 'osx') for arch in ('x64', 'arm64')}
    archives = {p.name: p for p in root.iterdir() if p.is_file() and (p.name.endswith('.zip') or p.name.endswith('.tar.gz'))}
    if set(archives) != expected:
        raise ValueError('Release must contain exactly the six expected runtime archives')
    lines = [hashlib.sha256(archives[name].read_bytes()).hexdigest() + '  ' + name for name in sorted(archives)]
    (root / 'SHA256SUMS').write_text('\n'.join(lines) + '\n', encoding='utf-8')


if __name__ == '__main__':
    write(sys.argv[1])
