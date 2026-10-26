"""Isolated publishing experiment. Diagnostic failures are reported, never release assets."""
import argparse
import collections
import gzip
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

from benchmark_startup import benchmark

MODES = {
    'full': [],
    'raw-trim': ['PublishTrimmed=true'],
    'partial-strict': ['PublishTrimmed=true', 'TrimMode=partial', 'JsonSerializerIsReflectionEnabledByDefault=true'],
    'partial': ['PublishTrimmed=true', 'TrimMode=partial', 'JsonSerializerIsReflectionEnabledByDefault=true'],
    'partial-r2r': ['PublishTrimmed=true', 'TrimMode=partial', 'JsonSerializerIsReflectionEnabledByDefault=true', 'PublishReadyToRun=true'],
    'aot': ['PublishAot=true', 'PublishSingleFile=false', 'DebugType=None', 'StripSymbols=true'],
}


def run_logged(arguments, path, cwd, environment=None, timeout=600):
    with path.open('w', encoding='utf-8') as log:
        try:
            result = subprocess.run(arguments, cwd=cwd, env=environment, stdout=log,
                                    stderr=subprocess.STDOUT, timeout=timeout)
            return result.returncode
        except subprocess.TimeoutExpired:
            log.write('\nExperiment timed out.\n')
            return 124


def probe(executable, arguments, environment, expected=None, json_field=None):
    try:
        result = subprocess.run([str(executable), *arguments], env=environment,
                                capture_output=True, text=True, timeout=20)
    except subprocess.TimeoutExpired:
        return {'arguments': arguments, 'exit_code': 124, 'passed': False, 'stderr_excerpt': 'Probe timed out.'}
    passed = result.returncode == 0
    if passed and expected is not None:
        passed = result.stdout.strip() == expected
    if passed and json_field is not None:
        try:
            passed = json_field in json.loads(result.stdout)
        except (ValueError, TypeError):
            passed = False
    return {'arguments': arguments, 'exit_code': result.returncode, 'passed': passed,
            'stdout_bytes': len(result.stdout.encode()), 'stderr_excerpt': result.stderr[:1200]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--modes', nargs='+', choices=list(MODES), default=list(MODES))
    parser.add_argument('--samples', type=int, default=7)
    args = parser.parse_args()
    if not 1 <= args.samples <= 100:
        parser.error('--samples must be 1..100')
    if os.name != 'nt':
        import resource
        resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    source = Path(__file__).resolve().parents[1]
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    system = {'Windows': 'win', 'Darwin': 'osx', 'Linux': 'linux'}[platform.system()]
    architecture = {'amd64': 'x64', 'x86_64': 'x64', 'arm64': 'arm64', 'aarch64': 'arm64'}[platform.machine().lower()]
    rid = system + '-' + architecture
    version = ET.parse(source / 'src/SidePulse.Cli/SidePulse.Cli.csproj').findtext('./PropertyGroup/Version')
    report = {'schema_version': 1, 'runtime': rid, 'platform': platform.platform(), 'version': version,
              'decision': 'Experimental evidence only. Ordinary release settings are not changed by this script.', 'variants': {}}
    for mode in args.modes:
        destination = output / 'builds' / mode
        if destination.exists():
            raise SystemExit('Use a fresh output directory; refusing stale experiment binaries: ' + str(destination))
        destination.mkdir(parents=True)
        properties = list(MODES[mode])
        diagnostic = mode not in ('full', 'partial-strict')
        if diagnostic:
            # Intentionally broken prototypes must reach execution so warnings can
            # be correlated with failures. Never use these overrides for release.
            properties += ['TreatWarningsAsErrors=false', 'ILLinkTreatWarningsAsErrors=false', 'IlcTreatWarningsAsErrors=false']
        if mode == 'aot' and system == 'linux' and not shutil.which('clang') and shutil.which('gcc'):
            properties += ['CppCompilerAndLinker=gcc']
        publish_log = output / (mode + '-publish.log')
        command = ['dotnet', 'publish', 'src/SidePulse.Cli', '-t:Rebuild', '-c', 'Release', '-r', rid,
                   '--self-contained', 'true', '-p:NuGetLockFilePath=obj/packages.experiment.' + mode + '.lock.json',
                   '-o', str(destination), *['-p:' + p for p in properties]]
        code = run_logged(command, publish_log, source)
        text = publish_log.read_text(encoding='utf-8', errors='replace')
        warnings = dict(collections.Counter(re.findall(r'(?:warning|error) (IL\d+)', text)))
        entry = {'properties': properties, 'diagnostic_warnings_allowed': diagnostic,
                 'publish_exit_code': code, 'analysis_diagnostics': warnings, 'probes': [], 'smokes': {}}
        report['variants'][mode] = entry
        executable = destination / ('spn.exe' if system == 'win' else 'spn')
        if code == 0 and executable.exists():
            data = executable.read_bytes()
            entry.update(executable_bytes=len(data), executable_gzip_bytes=len(gzip.compress(data, compresslevel=6, mtime=0)))
            with tempfile.TemporaryDirectory(prefix='spn deployment probe ') as temporary:
                environment = dict(os.environ, SIDEPULSE_NET_HOME=temporary)
                for arguments, expected, field in [
                    (['--version'], version, None),
                    (['--version', '--no-interactive'], version, None),
                    (['state', 'working', '--dry-run', '--json'], None, 'program'),
                    (['settings', 'show', '--json'], None, 'preferences'),
                    (['docs', '--offline'], None, None),
                    (['about', 'licenses', '--full'], None, None),
                ]:
                    entry['probes'].append(probe(executable, arguments, environment, expected, field))
            if all(p['passed'] for p in entry['probes']):
                manifest = run_logged([sys.executable, str(source / 'scripts/package_manifest.py'), str(destination), rid, version], output / (mode + '-manifest.log'), source)
                entry['manifest_exit_code'] = manifest
                if manifest == 0:
                    for script in ['cli_smoke', 'terminal_smoke', 'animation_smoke', 'install_smoke', 'update_smoke', 'agent_smoke', 'opencode_smoke']:
                        status = run_logged([sys.executable, str(source / 'scripts' / (script + '.py')), str(executable)], output / (mode + '-' + script + '.log'), source, timeout=180)
                        entry['smokes'][script] = status
                        if status != 0:
                            break
                    if all(v == 0 for v in entry['smokes'].values()):
                        entry['benchmark'] = benchmark(executable, args.samples, mode)
            entry['functional_pass'] = bool(entry['smokes']) and all(v == 0 for v in entry['smokes'].values())
        entry['eligible_for_review'] = code == 0 and entry.get('functional_pass', False) and not warnings and not diagnostic
        (output / 'report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
        print(mode, json.dumps({k: entry.get(k) for k in ['publish_exit_code', 'executable_bytes', 'analysis_diagnostics', 'functional_pass', 'eligible_for_review']}), flush=True)
    baseline = report['variants'].get('full')
    if baseline is not None and not baseline['eligible_for_review']:
        raise SystemExit('Unmodified baseline failed; experiment environment/results need investigation.')


if __name__ == '__main__':
    main()
