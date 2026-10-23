from pathlib import Path
import json
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from check_docs import check
from sync_wiki import MANIFEST, stage


class DocumentationTests(unittest.TestCase):
    def test_checker_detects_missing_relative_link(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'README.md').write_text('[Missing](missing.md)\n')
            self.assertTrue(any('missing local link' in error for error in check(root)[1]))

    def test_checker_resolves_wiki_links_and_reports_missing_page(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            pages = root / 'docs/wiki'
            pages.mkdir(parents=True)
            (pages / 'Home.md').write_text('[[Architecture]]\n')
            self.assertTrue(check(root)[1])
            (pages / 'Architecture.md').write_text('# Architecture\n')
            self.assertFalse(check(root)[1])

    def test_sync_preserves_unmanaged_page_and_removes_owned_obsolete_page(self):
        with tempfile.TemporaryDirectory() as directory:
            source, target = Path(directory) / 'source', Path(directory) / 'target'
            source.mkdir()
            target.mkdir()
            (source / 'Home.md').write_text('New home\n')
            (target / 'Unmanaged.md').write_text('Keep\n')
            (target / 'Obsolete.md').write_text('Remove\n')
            (target / MANIFEST).write_text(json.dumps(['Home.md', 'Obsolete.md']))
            stage(source, target)
            self.assertEqual((target / 'Unmanaged.md').read_text(), 'Keep\n')
            self.assertFalse((target / 'Obsolete.md').exists())
            self.assertEqual((target / 'Home.md').read_text(), 'New home\n')

    def test_sync_refuses_path_traversal_in_manifest(self):
        with tempfile.TemporaryDirectory() as directory:
            source, target = Path(directory) / 'source', Path(directory) / 'target'
            source.mkdir()
            target.mkdir()
            (source / 'Home.md').write_text('Home\n')
            (target / MANIFEST).write_text('["../outside.md"]')
            with self.assertRaises(ValueError):
                stage(source, target)

    def test_sync_is_repeatable(self):
        with tempfile.TemporaryDirectory() as directory:
            source, target = Path(directory) / 'source', Path(directory) / 'target'
            source.mkdir()
            target.mkdir()
            (source / 'Home.md').write_text('Home\n')
            stage(source, target)
            original = (target / MANIFEST).read_bytes()
            stage(source, target)
            self.assertEqual(original, (target / MANIFEST).read_bytes())


if __name__ == '__main__':
    unittest.main()
