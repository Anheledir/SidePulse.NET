"""Compare public JSON responses and their filesystem effects with a published baseline executable."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile

COMMANDS = [
    ['state', 'working', '--dry-run', '--json'],
    ['state', 'waiting', '--brightness', '42', '--dry-run', '--json'],
    ['settings', 'show', '--json'], ['install', '--status', '--json'],
    ['uninstall', '--dry-run', '--json'], ['update', 'status', '--json'],
    ['host', 'status', '--json'], ['devices', '--json'], ['dashboard', '--json'],
    ['doctor', '--json'], ['link', 'list', '--json'], ['agents', 'list', '--json'],
    ['animations', 'list', '--json'],
    ['animations', 'validate', 'solid-green', '--model', 'dot', '--json'],
    ['animations', 'preview', 'solid-green', '--model', 'dot', '--at', '100', '--json'],
    ['animations', 'export', 'solid-green', '--model', 'dot', '--json'],
    ['docs', '--offline', '--json'], ['about', 'info', '--json'],
    ['about', 'github', '--json'], ['about', 'credits', '--json'],
    ['about', 'licenses', '--full', '--json'],
    ['state', 'not-a-state', '--json'],
]


def normalize(value, executable):
    if isinstance(value, dict):
        return {key: normalize(item, executable) for key, item in value.items()}
    if isinstance(value, list):
        return [normalize(item, executable) for item in value]
    if isinstance(value, str):
        target = str(executable.parent).rstrip('/\\')
        if os.name == 'nt':
            target = target.upper()
        key = hashlib.sha256(target.encode('utf-8')).hexdigest().upper()[:24]
        return value.replace(str(executable.parent), '<published-directory>').replace(key, '<installation-key>')
    return value


def snapshot(root):
    if not root.exists():
        return {}
    entries = {'.': ('directory', root.stat().st_mode & 0o777)}
    for path in sorted(root.rglob('*')):
        name = str(path.relative_to(root))
        entries[name] = ('directory', path.stat().st_mode & 0o777) if path.is_dir() else ('file', hashlib.sha256(path.read_bytes()).hexdigest(), path.stat().st_mode & 0o777)
    return entries


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('baseline', type=Path)
    parser.add_argument('candidate', type=Path)
    args = parser.parse_args()
    baseline, candidate = args.baseline.resolve(), args.candidate.resolve()
    with tempfile.TemporaryDirectory(prefix='spn JSON compatibility ') as temporary:
        config = Path(temporary) / 'config'
        environment = dict(os.environ, SIDEPULSE_NET_HOME=str(config))
        for command in COMMANDS:
            results = []
            before = snapshot(config)
            baseline_files = None
            for executable in (baseline, candidate):
                response = subprocess.run([str(executable), *command], env=environment,
                                          capture_output=True, text=True, timeout=15)
                channel = response.stdout if response.returncode == 0 else response.stderr
                try:
                    value = normalize(json.loads(channel), executable)
                except ValueError as error:
                    raise AssertionError(f'Invalid JSON for {command}, exit {response.returncode}') from error
                results.append((response.returncode, value))
                files = snapshot(config)
                if executable == baseline:
                    baseline_files = files
                else:
                    assert files == baseline_files, f'Candidate changed configuration beyond baseline: {command}'
                if '--dry-run' in command:
                    assert files == before, f'Preview created or changed configuration: {command}'
            assert results[0] == results[1], (command, results)
        # Existing link-list behavior can create an empty store lock on native-vault
        # platforms. Compare those effects rather than asserting a false baseline.
    print(f'{len(COMMANDS)} public JSON command/error responses match baseline.')


if __name__ == '__main__':
    main()
