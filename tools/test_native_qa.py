"""Exercise native runner planning without launching Unity."""
import json
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

if __name__ == '__main__':
    unittest.main()
