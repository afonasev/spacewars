"""Exercise native runner planning without launching Unity."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]

class NativeQaRoutes(unittest.TestCase):
    def plan(self, suite):
        with tempfile.TemporaryDirectory() as directory:
            subprocess.run([sys.executable, 'tools/native_qa.py', '--suite', suite,
                            '--output', directory, '--dry-run'], cwd=ROOT, check=True,
                           stdout=subprocess.DEVNULL)
            return json.loads((Path(directory)/'manifest.json').read_text())['steps']

    def test_all_keeps_both_complete_test_suites_without_build(self):
        rows = self.plan('all')
        self.assertEqual([r['step'] for r in rows], ['editmode', 'playmode'])
        for row, platform in zip(rows, ['EditMode', 'PlayMode']):
            command = row['command']
            self.assertIn('-noaudio', command)
            self.assertIn('-muted', command)
            self.assertLess(command.index('-noaudio'), command.index('-runTests'))
            self.assertLess(command.index('-muted'), command.index('-runTests'))
            self.assertIn('-runTests', command)
            self.assertEqual(command[command.index('-testPlatform')+1], platform)
            self.assertNotIn('-testFilter', command)
            self.assertNotIn('-executeMethod', command)
            self.assertIsNone(row['exit_code'])
            self.assertNotIn('passed', row)

    def test_omitted_suite_cannot_implicitly_run_full_matches(self):
        with tempfile.TemporaryDirectory() as directory:
            result = subprocess.run([sys.executable, 'tools/native_qa.py', '--output', directory],
                                    cwd=ROOT, capture_output=True, text=True)
            self.assertEqual(result.returncode, 2)
            self.assertFalse((Path(directory)/'manifest.json').exists())

    def test_build_and_combined_route_remain_explicit(self):
        self.assertEqual([r['step'] for r in self.plan('build')], ['build'])
        self.assertEqual([r['step'] for r in self.plan('player')], ['build','editmode','playmode'])
        for suite in ('build', 'player'):
            for row in self.plan(suite):
                self.assertIn('-noaudio', row['command'])
                self.assertIn('-muted', row['command'])
                self.assertLess(row['command'].index('-noaudio'), row['command'].index('-executeMethod')
                                if '-executeMethod' in row['command'] else len(row['command']))
                self.assertLess(row['command'].index('-muted'), row['command'].index('-executeMethod')
                                if '-executeMethod' in row['command'] else len(row['command']))


class UnityLauncherForwardingTests(unittest.TestCase):
    def invoke_with_fake_runner(self, arguments, mode=None):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            runner = root / 'fake-unity-runner'
            capture = root / 'captured-argv.json'
            runner.write_text('#!/usr/bin/env python3\nimport json, os, sys\n'
                              'open(os.environ["CAPTURE_ARGV"], "w").write(json.dumps(sys.argv[1:]))\n')
            runner.chmod(0o755)
            environment = os.environ.copy()
            environment.pop('UNITY_RUN_MODE', None)
            environment.update({'UNITY_RUNNER': str(runner),
                                'UNITY_EDITOR': '/fake/Unity', 'CAPTURE_ARGV': str(capture)})
            if mode is not None:
                environment['UNITY_RUN_MODE'] = mode
            subprocess.run(['tools/unity.sh', *arguments], cwd=ROOT, env=environment, check=True)
            return json.loads(capture.read_text())

    def test_editor_launcher_forwards_both_silent_parameters_after_executable(self):
        command = self.invoke_with_fake_runner(['shared', '-noaudio', '-muted', '-batchmode'])
        editor_argv = command[command.index('--') + 1:]
        self.assertEqual('/fake/Unity', editor_argv[0])
        self.assertEqual(['-noaudio', '-muted', '-batchmode'], editor_argv[editor_argv.index('-noaudio'):])

    def test_player_launcher_forwards_both_silent_parameters(self):
        command = self.invoke_with_fake_runner(['player', '/fake/Spacewars', '-noaudio', '-muted'])
        player_argv = command[command.index('--') + 1:]
        self.assertEqual(['/fake/Spacewars', '-noaudio', '-muted'], player_argv)

    def test_player_defaults_to_shared_and_respects_explicit_exclusive(self):
        for override, expected in [(None, '--shared'), ('exclusive', '--exclusive')]:
            with self.subTest(override=override):
                command = self.invoke_with_fake_runner(['player', '/fake/Spacewars', '-muted'],
                                                       mode=override)
                self.assertEqual(expected, command[0])
                self.assertEqual(['/fake/Spacewars', '-muted', '-noaudio'],
                                 command[command.index('--') + 1:])


class FullRunApprovalTests(unittest.TestCase):
    def test_unapproved_unfiltered_routes_fail_before_evidence_or_unity(self):
        for suite in ('all', 'player', 'editmode', 'playmode'):
            with self.subTest(suite=suite), tempfile.TemporaryDirectory() as directory:
                result = subprocess.run([sys.executable, 'tools/native_qa.py', '--suite', suite,
                                         '--output', directory], cwd=ROOT, capture_output=True, text=True)
                self.assertEqual(2, result.returncode)
                self.assertIn('human approval', result.stderr)
                self.assertEqual([], list(Path(directory).iterdir()))

    def test_affected_route_batches_fixtures_without_build(self):
        with tempfile.TemporaryDirectory() as directory:
            subprocess.run([sys.executable, 'tools/native_qa.py', '--suite', 'editmode',
                            '--fixture', 'A.Tests', '--fixture', 'B.Tests',
                            '--output', directory, '--dry-run'], cwd=ROOT, check=True,
                           stdout=subprocess.DEVNULL)
            manifest = json.loads((Path(directory) / 'manifest.json').read_text())
            command = manifest['steps'][0]['command']
            self.assertEqual('A.Tests;B.Tests', command[command.index('-testFilter')+1])
            self.assertFalse(manifest['full_run_confirmed'])

if __name__ == '__main__':
    unittest.main()
