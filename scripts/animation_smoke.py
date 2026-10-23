"""Exercise animation, script output and offline credits against built/published CLI."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile


def main(executable):
    executable = str(Path(executable).resolve())
    with tempfile.TemporaryDirectory(prefix='sidepulse animations ü ') as temporary:
        root = Path(temporary)
        env = dict(os.environ, SIDEPULSE_NET_HOME=str(root / 'config'), NO_COLOR='1')

        def run(*args, code=0):
            result = subprocess.run([executable, *args], env=env, capture_output=True, text=True, timeout=10)
            assert result.returncode == code, (args, result.returncode, result.stdout, result.stderr)
            assert '\x1b' not in result.stdout + result.stderr, args
            if code == 0:
                assert not result.stderr, (args, result.stderr)
            return result

        patterns = json.loads(run('--json', 'animations', 'list').stdout)
        assert patterns == json.loads(run('animations', 'list', '--json').stdout)
        assert any(p['Name'] == 'cyan-roll' and p['Models'] == ['Dot', 'Pro'] for p in patterns)
        frames = json.loads(run('animations', 'preview', 'cyan-roll', '--both', '--at', '500', '--json').stdout)
        assert [len(f['colors']) for f in frames] == [2, 8]
        assert len(run('animations', 'preview', 'cyan-roll', '--seconds', '600').stdout.splitlines()) == 1
        source = root / "Ned's custom.LED"
        source.write_text('off\n0:#ff0000 1s pulse; 1:#00ff00 1s\nrepeat\n', encoding='utf-8')
        assert run('animations', 'validate', str(source), '--quiet').stdout == ''
        error = run('animations', 'validate', '--file', str(root / 'missing.LED'), '--json', code=1)
        assert not error.stdout and 'error' in json.loads(error.stderr)
        run('animations', 'import', 'personal', '--file', str(source), '--model', 'dot', '--quiet')
        run('animations', 'import', 'personal', '--file', str(source), '--model', 'dot', code=1)
        exported = root / 'exported.LED'
        run('animations', 'export', 'personal', '--output', str(exported), '--quiet')
        assert exported.read_text(encoding='utf-8') == source.read_text(encoding='utf-8')
        run('animations', 'export', 'personal', '--output', str(exported), code=1)
        device = root / 'LEDS.LED'
        device.write_text('old data', encoding='utf-8')
        assert not run('animations', 'apply', 'personal', '--model', 'dot', '--device', str(device), '--brightness', '40', '--quiet').stdout
        assert device.read_text(encoding='utf-8').startswith('brightness 102\n')
        run('animations', 'apply', 'personal', '--model', 'pro', '--device', str(device), code=1)
        run('profiles', 'set', 'mine', '--state', 'Working', '--pattern', 'personal', '--brightness', '40', '--quiet')
        profile = json.loads(run('profiles', 'show', 'mine', '--json').stdout)
        assert profile['Brightness'] == 40 and profile['States']['Working'] == 'personal'
        assert not run('state', 'Done', '--dry-run', '--quiet').stdout
        assert json.loads(run('state', 'Done', '--dry-run', '--quiet', '--json').stdout)['dryRun']
        run('animations', 'edit', '--file', str(source), code=1)
        run('state', code=1)
        assert 'COMMANDS' in run('about').stdout
        assert 'COMMANDS' in run('host').stdout
        assert 'USAGE' in run().stdout
        assert json.loads(run('about', 'info', '--json').stdout)['unofficial']
        assert run('about', 'website').stdout.strip() == 'https://sidepulse.io/'
        credits = json.loads(run('about', 'credits', '--json').stdout)
        assert len(credits['dependencies']) == 7
        licenses = json.loads(run('about', 'licenses', '--full', '--json').stdout)
        assert len(licenses['notices']) >= 10 and all(n['text'] for n in licenses['notices'])
        assert len(json.loads(run('docs', '--offline', '--json').stdout)) >= 2
        print('Animation studio, script output and offline credits smoke passed.')


if __name__ == '__main__':
    main(sys.argv[1])
