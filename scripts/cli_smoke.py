"""Exercise published CLI, local hooks and a simulated device without hardware."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time


def main():
    exe = str(Path(sys.argv[1]).resolve())

    child_env = os.environ.copy()

    def run(*args, input=None):
        result = subprocess.run([exe, *args], input=input, text=True, capture_output=True, timeout=10, env=child_env)
        assert result.returncode == 0, (args, result.stdout, result.stderr)
        return result

    preview = json.loads(run('state', 'working', '--dry-run', '--json').stdout)
    assert preview['program'] == 'off\n#00e5ff 1.6s pulse\nrepeat'
    malformed = run('hook', 'preToolUse', input='invalid')
    assert not malformed.stdout and not malformed.stderr
    disconnected = run('hook', 'agentStop', input=json.dumps({'sessionId': 'a', 'timestamp': 1, 'stopReason': 'end_turn'}))
    assert not disconnected.stdout and not disconnected.stderr
    with tempfile.TemporaryDirectory(prefix='SidePulse test ü ') as directory:
        child_env['SIDEPULSE_NET_HOME'] = str(Path(directory) / 'config')
        checks = json.loads(run('doctor', '--json').stdout)
        assert all(check['Status'] != 'error' for check in checks)
        if os.name == 'nt' or os.environ.get('SIDEPULSE_NATIVE_VAULT_TESTS') == '1':
            token = 'a' * 64 + '_TestKey'
            child_env['SIDEPULSE_TEST_TOKEN'] = token
            run('link', 'import', 'iphone', '--token-env', 'SIDEPULSE_TEST_TOKEN')
            phones = run('link', 'list', '--json').stdout
            assert token not in phones
            assert json.loads(phones)[0]['IsDefault']
            blob = (Path(child_env['SIDEPULSE_NET_HOME']) / ('links.dpapi' if os.name == 'nt' else 'links.encrypted')).read_bytes()
            assert token.encode() not in blob
            run('link', 'rename', 'iphone', 'personal')
            run('link', 'default', 'personal')
            run('state', 'done', '--phone', 'default', '--dry-run')
            run('link', 'remove', 'personal')
            assert json.loads(run('link', 'list', '--json').stdout) == []
        target = Path(directory) / 'LEDS.LED'
        target.write_text('off')
        run('state', 'waiting', '--device', directory)
        assert '#ff9900' in target.read_text()
        config = Path(directory) / 'hooks.json'
        run('hook-config', '--exe', exe, '--output', str(config))
        hooks = json.loads(config.read_text())['hooks']
        assert 'permissionRequest' not in hooks and 'subagentStop' not in hooks
        assert 'exit 0' in hooks['preToolUse'][0]['powershell']
        assert 'exit 0' in hooks['preToolUse'][0]['bash']
        refused = subprocess.run([exe, 'hook-config', '--exe', exe, '--output', str(config)], capture_output=True)
        assert refused.returncode != 0
        with open(Path(directory) / 'host.log', 'w') as log:
            host = subprocess.Popen([exe, 'host', '--device', directory, '--plain'], stdout=log, stderr=log)
            try:
                # Repeated idempotent input accommodates host startup without a timing-only assertion.
                for attempt in range(30):
                    assert host.poll() is None, 'Host exited: ' + (Path(directory) / 'host.log').read_text()
                    payload = json.dumps({'sessionId': 'smoke', 'timestamp': attempt + 1})
                    if os.name == 'nt':
                        result = run('hook', 'userPromptSubmitted', input=payload)
                    else:
                        result = subprocess.run(['bash', '-c', hooks['userPromptSubmitted'][0]['bash']],
                                                input=payload, text=True, capture_output=True, timeout=10, env=child_env)
                        assert result.returncode == 0
                    assert not result.stdout and not result.stderr
                    if '#00e5ff' in target.read_text():
                        break
                    time.sleep(0.1)
                else:
                    raise AssertionError('Host did not deliver the working state')
                competing = subprocess.run([exe, 'host', '--device', directory, '--plain'],
                                           text=True, capture_output=True, timeout=10, env=child_env)
                assert competing.returncode != 0, 'A second host must not take ownership'
                run('hook', 'agentStop', input=json.dumps({'sessionId': 'smoke', 'timestamp': 100, 'stopReason': 'end_turn'}))
                for _ in range(30):
                    if '#00ff66' in target.read_text():
                        break
                    time.sleep(0.1)
                else:
                    raise AssertionError('Host did not deliver the completed state')
            finally:
                host.terminate()
                host.wait(timeout=10)
            # A forcibly terminated host must be restartable without manual socket cleanup.
            restarted = subprocess.Popen([exe, 'host', '--device', directory, '--plain'], stdout=log, stderr=log)
            try:
                for attempt in range(30):
                    assert restarted.poll() is None, 'Restarted host exited: ' + (Path(directory) / 'host.log').read_text()
                    run('hook', 'userPromptSubmitted', input=json.dumps({'sessionId': 'restart', 'timestamp': 200 + attempt}))
                    if '#00e5ff' in target.read_text():
                        break
                    time.sleep(0.1)
                else:
                    raise AssertionError('Restarted host did not accept hook input')
            finally:
                restarted.terminate()
                restarted.wait(timeout=10)
    print('CLI, hook isolation, configuration and host/device smoke checks passed.')


if __name__ == '__main__':
    main()
