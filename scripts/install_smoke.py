"""Native single-file installation, PATH ownership and detached uninstall acceptance."""
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import time


def main(executable):
    source = Path(executable).resolve()
    if os.name == 'nt' and os.environ.get('SIDEPULSE_INSTALL_SYSTEM_TESTS') != '1':
        raise RuntimeError('Windows user-PATH smoke requires an isolated CI user and SIDEPULSE_INSTALL_SYSTEM_TESTS=1')
    with tempfile.TemporaryDirectory(prefix='SidePulse install ü ') as directory:
        root = Path(directory).resolve()
        download = root / 'Downloads'
        download.mkdir()
        portable = download / source.name
        shutil.copy2(source, portable)  # Deliberately no loose runtime, docs or manifest.
        target = root / 'Programs' / 'SidePulse ü' / 'app'
        config = root / 'configuration'
        env = dict(os.environ, SIDEPULSE_NET_HOME=str(config), DOTNET_BUNDLE_EXTRACT_BASE_DIR=str(root / 'runtime-cache'))
        original_profile = b'# User settings \xc3\xbc\nexport KEEP=value\n'
        if os.name != 'nt':
            env.update(HOME=str(root), SHELL='/bin/bash')
            profile = root / '.bashrc'
            profile.write_bytes(original_profile)
        else:
            import winreg

            def user_path():
                with winreg.OpenKey(winreg.HKEY_CURRENT_USER, 'Environment') as key:
                    try:
                        return winreg.QueryValueEx(key, 'Path')
                    except FileNotFoundError:
                        return None

            original_path = user_path()

        def run(exe, *args, success=True, extra_env=None):
            result = subprocess.run([str(exe), *args], text=True, capture_output=True, env=extra_env or env, timeout=45, cwd=root)
            assert (result.returncode == 0) == success, (args, result.returncode, result.stdout, result.stderr)
            return result

        expected_version = run(portable, '--version').stdout.strip()
        docs = json.loads(run(portable, 'docs', '--offline', '--json').stdout)
        assert any('Beta.md' in name for name in docs)
        licenses = run(portable, 'about', 'licenses', '--full', '--json').stdout
        assert '.dotnet.LICENSE.txt' in licenses and '.dotnet.ThirdPartyNotices.txt' in licenses
        arguments = ['install', '--directory', str(target), '--add-to-path', '--shell', 'bash', '--json']
        preview = json.loads(run(portable, *arguments, '--dry-run').stdout)
        assert preview['plan']['Executable'] == str(target / source.name)
        assert not target.exists() and not config.exists()
        if os.name != 'nt':
            assert preview['path']['Profile'] == str(root / '.bashrc')
            assert profile.read_bytes() == original_profile
        collision = root / 'conflicting command'
        collision.mkdir()
        (collision / source.name).write_text('unrelated application')
        conflicting_env = dict(env, PATH=str(collision) + os.pathsep + env['PATH'])
        assert 'Another spn' in run(portable, *arguments, '--dry-run', success=False, extra_env=conflicting_env).stderr
        assert not target.exists() and not config.exists()
        run(portable, *arguments)
        run(portable, *arguments)  # Idempotent binary/PATH/profile installation.
        installed = target / source.name
        record = json.loads((config / 'installation.json').read_text())
        assert record['Directory'] == str(target)
        assert set(path.name for path in target.iterdir()) == {source.name, 'sidepulse-install.json'}
        manifest = json.loads((target / 'sidepulse-install.json').read_text())
        assert manifest['Format'] == 2 and len(manifest['Files']) == 1
        if os.name != 'nt':
            registration = next(item for item in record['PathEntries'] if item['Kind'] == 'profile')
            assert Path(registration['Backup']).read_bytes() == original_profile
            assert profile.read_text().count('# >>> SidePulse.NET PATH >>>') == 1
            profile.write_bytes(profile.read_bytes() + b'# Added after installation\n')
        shutil.rmtree(download)
        unrelated = root / 'unrelated working directory'
        unrelated.mkdir()
        fresh_env = dict(env)
        if os.name == 'nt':
            with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r'SYSTEM\CurrentControlSet\Control\Session Manager\Environment') as key:
                machine_path = winreg.QueryValueEx(key, 'Path')[0]
            fresh_env['PATH'] = os.path.expandvars(machine_path + ';' + user_path()[0])
            invocation = [os.environ.get('COMSPEC', r'C:\Windows\System32\cmd.exe'), '/d', '/c', 'spn', '--version']
        else:
            invocation = ['/bin/bash', '--noprofile', '--rcfile', str(profile), '-i', '-c', 'spn --version']
        fresh = subprocess.run(invocation, stdin=subprocess.DEVNULL, capture_output=True, text=True, env=fresh_env, cwd=unrelated, timeout=45)
        assert fresh.returncode == 0 and fresh.stdout.strip() == expected_version, (fresh.returncode, fresh.stdout, fresh.stderr)
        run(installed, '--version')
        assert json.loads(run(installed, 'install', '--status', '--json').stdout)['mode'] == 'installed'
        run(installed, 'state', 'working', '--dry-run', '--json')
        plugin = root / 'opencode.js'
        run(installed, 'agents', 'install', 'opencode', '--config', str(plugin))
        match = re.search(r'const executable =\s*(".*?");', plugin.read_text())
        assert json.loads(match.group(1)) == str(installed)
        run(installed, 'uninstall', success=False)  # Hook paths must be removed explicitly.
        run(installed, 'agents', 'uninstall', 'opencode', '--config', str(plugin))
        # Unowned files, settings and profiles must survive application removal.
        (target / 'personal.txt').write_text('keep')
        (config / 'personal-settings.json').write_text('{"keep":true}')
        run(installed, 'uninstall', '--dry-run', '--json')
        scheduled = json.loads(run(installed, 'uninstall', '--json').stdout)
        job = Path(scheduled['workspace'])
        deadline = time.monotonic() + 20
        while time.monotonic() < deadline and not (job / 'result.json').exists():
            time.sleep(0.1)
        assert (job / 'result.json').exists(), 'Detached uninstall did not complete'
        assert not installed.exists() and not (config / 'installation.json').exists()
        assert (target / 'personal.txt').read_text() == 'keep'
        assert (config / 'personal-settings.json').exists()
        if os.name != 'nt':
            assert profile.read_bytes() == original_profile + b'# Added after installation\n'
            assert not (root / '.local/bin/spn').exists()
            assert Path(registration['Backup']).exists()
        else:
            assert user_path() == original_path, 'User PATH or its registry type was not restored exactly'
        # Give the detached Windows helper time to close its executable before cleanup.
        for attempt in range(100):
            try:
                shutil.rmtree(job)
                break
            except PermissionError:
                time.sleep(0.1)
        print('Single-file offline resources, install/PATH preview, conflicts, idempotence, stable hooks and owned uninstall passed.')


if __name__ == '__main__':
    main(sys.argv[1])
