import unittest
from upstream_watch import issue_body, MARKER


class UpstreamWatchTests(unittest.TestCase):
    def test_report_is_stable_and_retains_review_baseline(self):
        config = {'baseline': 'abc'}
        comparison = {'html_url': 'https://github.com/a/b/compare/abc...def',
                      'total_commits': 2, 'files': [{'filename': 'LEDS_FORMAT.md', 'status': 'modified'}]}
        body = issue_body(config, comparison)
        self.assertIn(MARKER, body)
        self.assertIn('`abc`', body)
        self.assertIn('LEDS_FORMAT.md', body)
        self.assertEqual(body, issue_body(config, comparison))
