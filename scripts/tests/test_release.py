import hashlib
import io
import json
from pathlib import Path
import sys
import tarfile
import tempfile
import unittest
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from verify_release import verify


class ReleaseTests(unittest.TestCase):
    def packages(self, root, corrupt=False, wrong_version=False):
        for platform in ('win', 'linux', 'osx'):
            for arch in ('x64', 'arm64'):
                runtime = f'{platform}-{arch}'
                names = ['spn.exe' if platform == 'win' else 'spn', 'spn.dll',
                         'hostfxr.dll' if platform == 'win' else 'libhostfxr.dylib' if platform == 'osx' else 'libhostfxr.so']
                files = {name: name.encode() for name in names}
                manifest = {'Format': 1, 'Version': '0.1.0-beta.2' if wrong_version else '0.1.0-beta.1',
                            'Runtime': runtime, 'Files': [{'Path': name, 'Sha256': hashlib.sha256(data).hexdigest(), 'Mode': 493} for name, data in files.items()]}
                files['sidepulse-install.json'] = json.dumps(manifest).encode()
                if corrupt:
                    files[names[0]] = b'changed after manifest generation'
                if platform == 'win':
                    with zipfile.ZipFile(root / f'spn-{runtime}.zip', 'w') as package:
                        for name, data in files.items():
                            package.writestr(name, data)
                else:
                    with tarfile.open(root / f'spn-{runtime}.tar.gz', 'w:gz') as package:
                        for name, data in files.items():
                            info = tarfile.TarInfo('./' + name)
                            info.size = len(data)
                            package.addfile(info, io.BytesIO(data))

    def test_complete_six_runtime_set_is_verified(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.packages(root)
            verify(root, '0.1.0-beta.1')
            self.assertEqual(6, len((root / 'SHA256SUMS').read_text().splitlines()))
            (root / 'spn-win-arm64.zip').unlink()
            with self.assertRaises(ValueError):
                verify(root, '0.1.0-beta.1')

    def test_corrupt_package_cannot_become_a_release(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.packages(root, corrupt=True)
            with self.assertRaisesRegex(ValueError, 'hash mismatch'):
                verify(root, '0.1.0-beta.1')

    def test_project_and_package_version_must_agree(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.packages(root, wrong_version=True)
            with self.assertRaisesRegex(ValueError, 'version, runtime or format mismatch'):
                verify(root, '0.1.0-beta.1')


if __name__ == '__main__':
    unittest.main()
