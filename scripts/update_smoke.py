"""Run the actual updater helper against isolated self-contained installs."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile


def main(executable):
    executable = Path(executable).resolve()
    source = executable.parent
    shipped = json.loads((source / 'sidepulse-install.json').read_text(encoding='utf-8'))
    with tempfile.TemporaryDirectory(prefix='SidePulse updater ü ') as temporary:
        root = Path(temporary).resolve()
        env = dict(os.environ, SIDEPULSE_NET_HOME=str(root / 'state'))
        for fail in (False, True):
            home = root / ('rollback' if fail else 'success')
            target = home / 'installation'
            job = home / '.sidepulse-update-smoke'
            stage = job / 'stage'
            backup = home / 'installation.backup-smoke'
            helper = job / 'helper'
            # A deliberately incorrect expected version exercises real startup rollback.
            for destination in (target, stage, helper):
                shutil.copytree(source, destination)
            expected = '99.0.0' if fail else shipped['Version']
            if fail:
                (stage / 'sidepulse-install.json').write_text(json.dumps(dict(shipped, Version=expected)), encoding='utf-8')
            initial = subprocess.run([str(target / executable.name), 'update', 'status', '--json'], env=env, capture_output=True, text=True, timeout=15)
            assert initial.returncode == 0, (initial.stdout, initial.stderr)
            receipt = Path(json.loads(initial.stdout)['receipt'])
            plan = dict(Target=str(target), Stage=str(stage), Backup=str(backup), Job=str(job), Receipt=str(receipt),
                        ExpectedManifestHash=hashlib.sha256((target / 'sidepulse-install.json').read_bytes()).hexdigest().upper(),
                        Version=expected, Runtime=shipped['Runtime'], ParentId=2147483647, ParentStartTicks=0)
            plan_path = job / 'plan.json'
            plan_path.write_text(json.dumps(plan), encoding='utf-8')
            result = subprocess.run([str(helper / executable.name), 'internal-update', '--plan', str(plan_path)],
                                    env=env, capture_output=True, text=True, timeout=75, cwd=job)
            assert result.returncode == (1 if fail else 0), (result.returncode, result.stdout, result.stderr, receipt.read_text(encoding='utf-8') if receipt.exists() else 'no receipt')
            state = json.loads(receipt.read_text(encoding='utf-8'))
            assert state['State'] == ('failed' if fail else 'installed'), state
            current = json.loads((target / 'sidepulse-install.json').read_text(encoding='utf-8'))
            assert current['Version'] == shipped['Version']
            version = subprocess.run([str(target / executable.name), '--version'], env=env, capture_output=True, text=True, timeout=15)
            assert version.returncode == 0 and version.stdout.strip() == shipped['Version']
            status = subprocess.run([str(target / executable.name), 'update', 'status', '--json'], env=env, capture_output=True, text=True, timeout=15)
            assert status.returncode == (1 if fail else 0) and json.loads(status.stdout)['State'] == state['State']
            assert (job / 'failed-package').exists() if fail else backup.exists()
            print('Native updater ' + ('startup failure rollback' if fail else 'replacement and backup') + ' passed.', flush=True)


if __name__ == '__main__':
    main(sys.argv[1])
