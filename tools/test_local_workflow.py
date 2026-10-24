"""Prevent implicit Player builds in local and full-test routes."""
from pathlib import Path
import os
import subprocess
import unittest

ROOT = Path(__file__).resolve().parents[1]

def make_environment():
    environment = os.environ.copy()
    for key in ('MAKEFLAGS', 'MAKEOVERRIDES', 'MFLAGS', 'MAKELEVEL', 'CHECKS',
                'TOOLING_TESTS', 'FIXTURES', 'FULL_TESTS_CONFIRMED', 'UNITY_RUN_MODE'):
        environment.pop(key, None)
    return environment

class LocalWorkflowTests(unittest.TestCase):
    def route(self, target):
        return subprocess.check_output(['make', '-n', target], cwd=ROOT, text=True,
                                       env=make_environment())

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
        route = subprocess.check_output(['make', '-n'], cwd=ROOT, text=True,
                                        env=make_environment())
        self.assertNotIn('tools/unity.sh', route)
        self.assertNotIn('python3 tools/', route)
        self.assertNotIn('./unity/tools.sh', route)

    def test_affected_selector_rejects_missing_or_invalid_scope_before_launch(self):
        cases = [([], 'Select affected CHECKS'),
                 (['CHECKS=tooling'], 'require TOOLING_TESTS'),
                 (['CHECKS=editmode'], 'require FIXTURES'),
                 (['CHECKS=tooling check-ui invalid', 'TOOLING_TESTS=does_not_exist'],
                  'Unknown affected check'),
                 (['CHECKS=tooling editmode', 'TOOLING_TESTS=does_not_exist'],
                  'require FIXTURES')]
        for arguments, message in cases:
            with self.subTest(arguments=arguments):
                result = subprocess.run(['make', '--no-print-directory', 'check', *arguments],
                                        cwd=ROOT, capture_output=True, text=True,
                                        env=make_environment())
                self.assertNotEqual(0, result.returncode)
                self.assertIn(message, result.stdout)
                self.assertNotIn('ModuleNotFoundError', result.stderr)
                self.assertNotIn('tools/check_qa.py', result.stdout)

    def test_affected_selector_plans_only_selected_contracts(self):
        for arguments, expected in [
                (['CHECKS=tooling', 'TOOLING_TESTS=tools.test_native_qa'],
                 '-m unittest tools.test_native_qa'),
                (['CHECKS=editmode', 'FIXTURES=A.Tests B.Tests'],
                 '--suite editmode --fixture A.Tests --fixture B.Tests'),
                (['CHECKS=check-ui'], 'tools/check_qa.py --scope ui')]:
            with self.subTest(arguments=arguments):
                route = subprocess.check_output(['make', '-n', 'check', *arguments],
                                                cwd=ROOT, text=True, env=make_environment())
                self.assertIn(expected, route)
                self.assertNotIn('--suite all', route)
                self.assertNotIn('--suite player', route)

    def test_editor_defaults_to_shared_with_explicit_exclusive_override(self):
        self.assertIn('./tools/unity.sh shared', self.route('unity-editor'))
        route = subprocess.check_output(['make', '-n', 'unity-editor', 'UNITY_RUN_MODE=exclusive'],
                                        cwd=ROOT, text=True, env=make_environment())
        self.assertIn('./tools/unity.sh exclusive', route)

if __name__ == '__main__':
    unittest.main()
