"""Verify the six release archives against their embedded installation manifests."""
import hashlib
import json
from pathlib import Path, PurePosixPath
import sys
import tarfile
import zipfile

from release_checksums import write


def verify(directory, version):
    root = Path(directory)
    # Enforce the exact runtime set before checking any contents.
    write(root)
    for platform in ('win', 'linux', 'osx'):
        for architecture in ('x64', 'arm64'):
            runtime = f'{platform}-{architecture}'
            archive = root / (f'spn-{runtime}' + ('.zip' if platform == 'win' else '.tar.gz'))
            files = {}
            if platform == 'win':
                with zipfile.ZipFile(archive) as package:
                    for item in package.infolist():
                        if item.is_dir():
                            continue
                        if (item.external_attr >> 16) & 0o170000 not in (0, 0o100000):
                            raise ValueError('Release contains a link or special file')
                        add(files, item.filename, package.read(item))
            else:
                with tarfile.open(archive, 'r:gz') as package:
                    for item in package:
                        if item.isdir():
                            continue
                        if not item.isfile():
                            raise ValueError('Release contains a link or special file')
                        add(files, item.name.removeprefix('./'), package.extractfile(item).read())
            manifest = json.loads(files.pop('sidepulse-install.json'))
            if manifest['Format'] != 1 or manifest['Version'] != version or manifest['Runtime'] != runtime:
                raise ValueError('Release manifest version, runtime or format mismatch')
            entries = manifest['Files']
            if len({entry['Path'] for entry in entries}) != len(entries):
                raise ValueError('Duplicate manifest file')
            if set(files) != {entry['Path'] for entry in entries}:
                raise ValueError('Release manifest does not describe the complete archive')
            required = {'spn.exe' if platform == 'win' else 'spn', 'spn.dll',
                        'hostfxr.dll' if platform == 'win' else 'libhostfxr.dylib' if platform == 'osx' else 'libhostfxr.so'}
            if not required.issubset(files) or any(name in files for name in ('sidepulse-net', 'sidepulse-net.exe', 'sidepulse-net.dll')):
                raise ValueError('Release executable/runtime layout is incomplete or contains the removed command')
            for entry in entries:
                if hashlib.sha256(files[entry['Path']]).hexdigest().lower() != entry['Sha256'].lower():
                    raise ValueError('Release file hash mismatch: ' + entry['Path'])
            print(f'Verified {archive.name}: {len(files)} files, version {version}')


def add(files, name, data):
    path = PurePosixPath(name)
    if path.is_absolute() or any(part in ('', '.', '..') for part in name.split('/')) or '\\' in name or ':' in name or name in files:
        raise ValueError('Unsafe or duplicate archive path')
    files[name] = data


if __name__ == '__main__':
    verify(*sys.argv[1:])
