"""Prevent implicit Player builds in local and full-test routes."""
from pathlib import Path
import subprocess
import unittest

ROOT = Path(__file__).resolve().parents[1]

class LocalWorkflowTests(unittest.TestCase):
    def route(self, target):
        return subprocess.check_output(['make', '-n', target], cwd=ROOT, text=True)

    def test_checks_and_editor_do_not_build_player(self):
        for target in ('check-local', 'check', 'check-full', 'unity-editor'):
            with self.subTest(target=target):
                route = self.route(target)
                for token in ('tools.sh build', '--suite build', '--suite player',
                              'project_checks.py build', 'PrototypeBuilder.BuildMac'):
                    self.assertNotIn(token, route)

    def test_check_is_local_without_full_match_suites(self):
        route = self.route('check')
        self.assertEqual(route, self.route('check-local'))
        for token in ('tools.sh test-edit', 'tools.sh test-play', '--suite all',
                      'check_qa.py full', 'RunWithFixtureEquivalence'):
            self.assertNotIn(token, route)

    def test_build_remains_explicit(self):
        route = self.route('build')
        self.assertTrue(any(token in route for token in ('tools.sh build', '--suite build',
                        'project_checks.py build', 'PrototypeBuilder.BuildMac')), route)

    def test_default_make_only_shows_help(self):
        route = subprocess.check_output(['make', '-n'], cwd=ROOT, text=True)
        self.assertNotIn('tools/unity.sh', route)
        self.assertNotIn('python3 tools/', route)
        self.assertNotIn('./unity/tools.sh', route)

if __name__ == '__main__':
    unittest.main()
