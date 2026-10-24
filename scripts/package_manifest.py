"""Describe the owned files of a self-contained distribution, with SHA-256 and modes."""
import hashlib
import json
from pathlib import Path
import re
import sys


def write(directory, runtime, version):
    root = Path(directory)
    if not re.fullmatch(r'(win|osx|linux)-(x64|arm64)', runtime):
        raise ValueError('Unsupported runtime')
    manifest = root / 'sidepulse-install.json'
    files = []
    for path in sorted(root.rglob('*')):
        if path.is_symlink():
            raise ValueError('Distribution contains a symlink')
        if path == manifest or path.is_dir():
            continue
        files.append({'Path': path.relative_to(root).as_posix(),
                      'Sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                      'Mode': path.stat().st_mode & 0o777})
    manifest.write_text(json.dumps({'Format': 2 if len(files) == 1 and files[0]['Path'] == ('spn.exe' if runtime.startswith('win-') else 'spn') else 1, 'Version': version, 'Runtime': runtime, 'Files': files}, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    write(*sys.argv[1:])
