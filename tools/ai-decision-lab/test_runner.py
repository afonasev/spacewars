"""Focused evidence/CLI failure contracts; uses the actual already-built executable."""
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest import mock

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('decision_runner', HERE / 'run.py')
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)
DOTNET = runner.shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
DLL = HERE / 'bin/Release/net8.0/ai-decision-lab.dll'


class RunnerContracts(unittest.TestCase):
    def test_existing_output_is_never_overwritten(self):
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory); marker = target / 'keep'; marker.write_text('original')
            with self.assertRaisesRegex(ValueError, 'already exists'):
                runner.run(target, HERE / 'cases/science-production.json', DOTNET)
            self.assertEqual(marker.read_text(), 'original')

    def test_source_drift_fails_before_launch_and_preserves_input(self):
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory) / 'evidence'
            with mock.patch.object(runner, 'sources', side_effect=[{'a': 'old'}, {'a': 'new'}, {'a': 'new'}]), \
                 mock.patch.object(runner.subprocess, 'check_output', return_value='test'), \
                 mock.patch.object(runner.subprocess, 'run') as launch:
                report = runner.run(target, HERE / 'cases/science-production.json', DOTNET)
                launch.assert_not_called()
            self.assertEqual(report['status'], 'FAIL')
            self.assertFalse(report['source_unchanged'])
            self.assertTrue((target / 'scenario.json').exists())
            self.assertTrue((target / 'manifest.json').exists())

    def test_actual_cli_rejects_unknown_missing_and_negative_inputs(self):
        self.assertTrue(DLL.is_file(), 'Build Release runner before these tests; no silent skip.')
        original = json.loads((HERE / 'cases/science-production.json').read_text())
        missing = dict(original); del missing['Seed']
        for name, value in [('unknown', dict(original, Typo=1)), ('missing', missing), ('negative', dict(original, Cash=-1))]:
            with self.subTest(name=name), tempfile.TemporaryDirectory() as directory:
                p = Path(directory) / 'case.json'; p.write_text(json.dumps(value))
                result = subprocess.run([DOTNET, str(DLL), 'inspect', str(p)], capture_output=True, text=True, timeout=10)
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(json.loads(result.stderr)['status'], 'FAIL')
                self.assertEqual(result.stdout, '')

    def test_failed_build_is_not_a_passing_empty_run(self):
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory) / 'evidence'
            with mock.patch.object(runner, 'sources', return_value={'a': 'same'}), \
                 mock.patch.object(runner.subprocess, 'check_output', return_value='test'), \
                 mock.patch.object(runner.subprocess, 'run', return_value=subprocess.CompletedProcess([], 42)):
                report = runner.run(target, HERE / 'cases/science-production.json', DOTNET)
            self.assertEqual(report['status'], 'FAIL')
            self.assertEqual(len(report['steps']), 1)
            self.assertEqual(report['steps'][0]['exit_code'], 42)
            self.assertEqual(report['gameplay_acceptance'], 'NotRun')


if __name__ == '__main__':
    unittest.main()
