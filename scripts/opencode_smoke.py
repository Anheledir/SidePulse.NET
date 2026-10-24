"""Execute the generated OpenCode plugin against the real host, without an agent account."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile


def main():
    exe = str(Path(sys.argv[1]).resolve())
    with tempfile.TemporaryDirectory(prefix="SidePulse OpenCode ü ") as directory:
        root = Path(directory)
        env = os.environ.copy()
        env['SIDEPULSE_NET_HOME'] = str(root / 'state')
        plugin = root / 'sidepulse-net.js'

        def run(*args):
            result = subprocess.run([exe, *args], capture_output=True, text=True, env=env, timeout=20)
            assert result.returncode == 0, (args, result.stdout, result.stderr)
            return result.stdout

        preview = json.loads(run('agents', 'install', 'opencode', '--config', str(plugin), '--dry-run', '--json'))
        assert not plugin.exists()
        assert 'plugin' in preview['managedHooks']
        run('agents', 'install', 'opencode', '--config', str(plugin))
        assert json.loads(run('agents', 'status', 'opencode', '--config', str(plugin), '--json'))['State'] == 'installed'
        run('host', 'start', '--exe', exe, '--dry-run', '--json')
        try:
            result = subprocess.run(['node', str(Path(__file__).with_name('opencode_smoke.mjs')), str(plugin), exe],
                                    capture_output=True, text=True, env=env, timeout=45)
            assert result.returncode == 0, (result.stdout, result.stderr)
            assert 'DO-NOT-FORWARD' not in run('host', 'logs', '--tail', '100')
        finally:
            run('host', 'stop', '--json')
        run('agents', 'uninstall', 'opencode', '--config', str(plugin))
        assert not plugin.exists()
        print('OpenCode generated plugin, lifecycle, multiple prompts, isolation and removal passed.')


if __name__ == '__main__':
    main()
