"""Measure isolated single-file launches; cold means empty bundle cache, not cold OS cache."""
import argparse
import gzip
import hashlib
import json
import os
from pathlib import Path
import platform
import statistics
import subprocess
import tempfile
import time


def launch(executable, arguments, environment, home=False):
    start = time.perf_counter()
    if not home:
        result = subprocess.run([str(executable), *arguments], env=environment,
                                capture_output=True, timeout=15)
        if result.returncode:
            raise RuntimeError(f'Benchmark command failed: {arguments}; exit {result.returncode}')
        return (time.perf_counter() - start) * 1000
    import fcntl
    import pty
    import select
    import struct
    import termios
    master, slave = pty.openpty()
    fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack('HHHH', 30, 100, 0, 0))
    child = subprocess.Popen([str(executable)], stdin=slave, stdout=slave, stderr=slave, env=environment)
    os.close(slave)
    output = b''
    try:
        deadline = time.monotonic() + 15
        while time.monotonic() < deadline:
            if select.select([master], [], [], 0.05)[0]:
                try:
                    output += os.read(master, 65536)
                except OSError:
                    break
                if b'Esc back' in output and b'Last action:' in output:
                    elapsed = (time.perf_counter() - start) * 1000
                    # A new PTY is not necessarily the controlling terminal; use the
                    # explicit menu action rather than relying on its signal handling.
                    os.write(master, b'\x1b[A\r')
                    while child.poll() is None and time.monotonic() < deadline:
                        if select.select([master], [], [], 0.05)[0]:
                            try:
                                os.read(master, 65536)
                            except OSError:
                                break
                    if child.wait(timeout=2) != 0:
                        raise RuntimeError('Home failed to exit cleanly')
                    return elapsed
            if child.poll() is not None:
                break
        raise RuntimeError('Home readiness marker not observed')
    finally:
        if child.poll() is None:
            child.kill()
        child.wait(timeout=5)
        os.close(master)


def summary(values):
    ordered = sorted(values)
    return {'samples_ms': [round(v, 2) for v in values],
            'median_ms': round(statistics.median(values), 2),
            'min_ms': round(ordered[0], 2), 'max_ms': round(ordered[-1], 2)}


def benchmark(executable, samples=7, label=None):
    executable = Path(executable).resolve()
    with tempfile.TemporaryDirectory(prefix='spn startup ') as temporary:
        root = Path(temporary)
        environment = dict(os.environ, SIDEPULSE_NET_HOME=str(root / 'config'),
                           TERM='xterm-256color', COLORTERM='truecolor')
        environment.pop('NO_COLOR', None)
        modes = [('version', ['--version']), ('state_preview', ['state', 'working', '--dry-run', '--json'])]
        if os.name != 'nt':
            modes.append(('home', []))
        results = {}
        cache_files = []
        for name, arguments in modes:
            cold, warm = [], []
            for index in range(samples):
                cache = root / f'{name}-{index}'
                environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = str(cache)
                cold.append(launch(executable, arguments, environment, home=name == 'home'))
                warm.append(launch(executable, arguments, environment, home=name == 'home'))
                if not cache_files:
                    cache_files = [{'name': p.name, 'bytes': p.stat().st_size}
                                   for p in sorted(cache.rglob('*')) if p.is_file()]
            results[name] = {'empty_bundle_cache': summary(cold), 'cached_bundle': summary(warm)}
        if (root / 'config').exists():
            raise RuntimeError('Read-only benchmark unexpectedly created application configuration')
    # gzip is measured outside launch timings, at the release tar compression level.
    data = executable.read_bytes()
    return {'schema_version': 1, 'label': label, 'platform': platform.platform(),
            'architecture': platform.machine(), 'processor': platform.processor(),
            'logical_cpus': os.cpu_count(), 'python': platform.python_version(),
            'executable_sha256': hashlib.sha256(data).hexdigest(), 'executable_bytes': len(data),
            'executable_gzip_bytes': len(gzip.compress(data, compresslevel=6, mtime=0)),
            'extracted_bytes': sum(f['bytes'] for f in cache_files), 'extracted_files': cache_files,
            'samples_per_mode': samples, 'results': results,
            'home_measurement': 'Not measured: Windows requires a real console/ConPTY measurement' if os.name == 'nt' else 'Unix PTY at 100x30, first complete Home input guide',
            'limits': 'Empty bundle cache is not cold OS page cache. Alternating cold/warm runs; no hardware or bridge delivery. Times include process startup, not just managed Main. gzip bytes are executable-only, not the complete release archive.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('executable')
    parser.add_argument('--samples', type=int, default=7)
    parser.add_argument('--label')
    parser.add_argument('--compare', type=Path, help='Compare against a prior report from the same machine')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    if not 1 <= args.samples <= 100:
        parser.error('--samples must be 1..100')
    result = benchmark(args.executable, args.samples, args.label)
    if args.compare:
        baseline = json.loads(args.compare.read_text(encoding='utf-8'))
        comparison = {}
        regressions = []
        for mode, measurements in result['results'].items():
            for cache, values in measurements.items():
                previous = baseline['results'][mode][cache]['median_ms']
                current = values['median_ms']
                comparison[f'{mode}/{cache}'] = {'before_ms': previous, 'after_ms': current,
                                                 'change_percent': round((current / previous - 1) * 100, 1)}
                # Shared CI runners are noisy. Catch severe regressions, not small jitter.
                if current > previous * 1.5 + 100:
                    regressions.append(f'{mode}/{cache}')
        if result['executable_bytes'] > baseline['executable_bytes'] * 1.1:
            regressions.append('executable growth >10%')
        result['comparison'] = comparison
        result['regression_budget'] = 'median <= baseline * 1.5 + 100ms; executable <= baseline * 1.1'
        result['regressions'] = regressions
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(f"{args.label or 'spn'}: {result['executable_bytes']} executable bytes; {result['extracted_bytes']} extracted bytes")
    for name, measurements in result['results'].items():
        print(name, {cache: values['median_ms'] for cache, values in measurements.items()})
    if result.get('regressions'):
        raise SystemExit('Startup regression budget exceeded: ' + ', '.join(result['regressions']))


if __name__ == '__main__':
    main()
