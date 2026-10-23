"""Exercise native generated wrappers, owned hook setup and detached host control, without agent accounts."""
import json
import os
from pathlib import Path
import shlex
import subprocess
import sys
import tempfile
import time


def main():
    exe = str(Path(sys.argv[1]).resolve())
    with tempfile.TemporaryDirectory(prefix="SidePulse agents ü ") as directory:
        root = Path(directory)
        env = os.environ.copy()
        env['SIDEPULSE_NET_HOME'] = str(root / 'state')

        def run(*args, timeout=15):
            result = subprocess.run([exe, *args], text=True, capture_output=True, timeout=timeout, env=env)
            assert result.returncode == 0, (args, result.stdout, result.stderr)
            return result.stdout

        def status():
            return json.loads(run('host', 'status', '--json'))

        target = root / 'LEDS.LED'
        target.write_text('off')
        configs = {}
        originals = {}
        for provider in ('copilot', 'codex', 'claude', 'kiro'):
            path = root / (provider + '.json')
            original = '{"unrelated":"private-config-key","hooks":{}}\n' if provider in ('codex', 'claude') else None
            if original:
                path.write_text(original)
            originals[provider] = original
            preview = run('agents', 'install', provider, '--exe', exe, '--config', str(path), '--dry-run', '--json')
            assert 'private-config-key' not in preview
            assert (path.read_text() if path.exists() else None) == original
            run('agents', 'install', provider, '--exe', exe, '--config', str(path))
            assert json.loads(run('agents', 'status', provider, '--config', str(path), '--json'))['State'] == 'installed'
            configs[provider] = (path, json.loads(path.read_text()))

        started = False
        try:
            first = json.loads(run('host', 'start', '--exe', exe, '--device', directory, '--json', timeout=25))
            started = True
            assert first['Running']
            assert status()['Running']
            second = json.loads(run('host', 'start', '--exe', exe, '--device', directory, '--json'))
            assert second['ProcessId'] == first['ProcessId']

            def send(provider, event):
                configuration = configs[provider][1]
                if provider == 'copilot':
                    entry = configuration['hooks'][event][0]
                    command = entry['powershell'] if os.name == 'nt' else entry['bash']
                elif provider == 'kiro':
                    command = next(h['action']['command'] for h in configuration['hooks'] if h['trigger'] == event)
                else:
                    command = configuration['hooks'][event][0]['hooks'][0]['command']
                payload = {'session_id': 'same-id', 'prompt': 'DO-NOT-LOG-THIS', 'stopReason': 'end_turn'}
                if provider == 'copilot':
                    payload.update(sessionId='same-id', timestamp=time.time_ns() // 1000000)
                if os.name == 'nt':
                    arguments = ['pwsh', '-NoLogo', '-NoProfile', '-NonInteractive', '-Command', command] if provider == 'copilot' else shlex.split(command)
                else:
                    arguments = ['bash', '-c', command]
                result = subprocess.run(arguments, input=json.dumps(payload), text=True, capture_output=True, timeout=10, env=env)
                assert result.returncode == 0 and not result.stdout and not result.stderr, (provider, result.stdout, result.stderr)

            for provider in configs:
                send(provider, 'userPromptSubmitted' if provider == 'copilot' else 'UserPromptSubmit')
            for _ in range(30):
                snapshot = status()
                if len(snapshot['Sessions']) == 4:
                    break
                time.sleep(0.1)
            assert len(snapshot['Sessions']) == 4, snapshot
            assert {s['Provider'] for s in snapshot['Sessions']} == {'Copilot', 'Codex', 'Claude', 'Kiro'}
            send('claude', 'Stop')
            assert status()['State'] == 'Working'
            for provider in ('copilot', 'codex', 'kiro'):
                send(provider, 'agentStop' if provider == 'copilot' else 'Stop')
            for _ in range(30):
                if status()['State'] == 'Done':
                    break
                time.sleep(0.1)
            assert status()['State'] == 'Done'
            run('host', 'stop', '--json')
            started = False
            assert not status()['Running']
            logs = run('host', 'logs', '--tail', '10')
            assert 'DO-NOT-LOG-THIS' not in logs
        finally:
            if started:
                run('host', 'stop', '--json')
        for provider, (path, _) in configs.items():
            run('agents', 'uninstall', provider, '--config', str(path))
            assert (path.read_text() if path.exists() else None) == originals[provider]
        print('Agent wrappers, mixed-session isolation, hook setup/backups and detached host lifecycle passed.')


if __name__ == '__main__':
    main()
