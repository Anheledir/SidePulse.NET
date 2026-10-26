"""Compare public read-only JSON responses with a published baseline executable."""
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


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('baseline', type=Path)
    parser.add_argument('candidate', type=Path)
    args = parser.parse_args()
    baseline, candidate = args.baseline.resolve(), args.candidate.resolve()
    with tempfile.TemporaryDirectory(prefix='spn JSON compatibility ') as temporary:
        environment = dict(os.environ, SIDEPULSE_NET_HOME=str(Path(temporary) / 'config'))
        for command in COMMANDS:
            results = []
            for executable in (baseline, candidate):
                response = subprocess.run([str(executable), *command], env=environment,
                                          capture_output=True, text=True, timeout=15)
                channel = response.stdout if response.returncode == 0 else response.stderr
                try:
                    value = normalize(json.loads(channel), executable)
                except ValueError as error:
                    raise AssertionError(f'Invalid JSON for {command}, exit {response.returncode}') from error
                results.append((response.returncode, value))
            assert results[0] == results[1], (command, results)
        assert not (Path(temporary) / 'config').exists(), 'Read-only compatibility probes created configuration'
    print(f'{len(COMMANDS)} public JSON command/error responses match baseline.')


if __name__ == '__main__':
    main()
