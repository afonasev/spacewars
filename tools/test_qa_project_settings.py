import io
import json
from contextlib import redirect_stdout
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

import check_qa
import native_qa
from qa_project_settings import ProjectSettingsGuard


class SettingsEvidenceTests(unittest.TestCase):
    def test_evidence_inside_settings_is_rejected_before_writing(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); evidence = root / 'unity/ProjectSettings/evidence'
            with self.assertRaises(ValueError):
                ProjectSettingsGuard(root, evidence)
            self.assertFalse(evidence.exists())

    def test_dirty_baseline_is_preserved_and_is_not_drift(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            settings = root / 'unity/ProjectSettings'
            settings.mkdir(parents=True)
            original = b'already dirty user content\n\xff'
            (settings / 'QualitySettings.asset').write_bytes(original)
            guard = ProjectSettingsGuard(root, root / 'evidence')
            result = guard.finish()
            self.assertTrue(result['unchanged'])
            self.assertFalse(result['automatic_restore'])
            self.assertEqual(original, (root / 'evidence/before/QualitySettings.asset').read_bytes())
            self.assertEqual(original, (settings / 'QualitySettings.asset').read_bytes())

    def test_added_removed_and_changed_files_keep_exact_bytes_and_hashes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); settings = root / 'unity/ProjectSettings'
            settings.mkdir(parents=True)
            (settings / 'QualitySettings.asset').write_bytes(b'msaa: 2\n')
            (settings / 'removed.asset').write_bytes(b'old\x00')
            guard = ProjectSettingsGuard(root, root / 'evidence')
            (settings / 'QualitySettings.asset').write_bytes(b'msaa: 4\n')
            (settings / 'removed.asset').unlink()
            (settings / 'new.asset').write_bytes(b'new\xff')
            result = guard.finish()
            self.assertEqual(['QualitySettings.asset', 'new.asset', 'removed.asset'], result['changed'])
            self.assertNotEqual(result['before']['QualitySettings.asset'], result['after']['QualitySettings.asset'])
            self.assertEqual(b'new\xff', (root / 'evidence/after/new.asset').read_bytes())
            self.assertEqual(b'old\x00', (root / 'evidence/before/removed.asset').read_bytes())
            self.assertEqual(b'msaa: 4\n', (settings / 'QualitySettings.asset').read_bytes())
            self.assertFalse((settings / 'removed.asset').exists())
            self.assertIn('+msaa: 4', (root / 'evidence/settings.diff').read_text())

    def test_creation_of_missing_settings_directory_is_detected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); guard = ProjectSettingsGuard(root, root / 'evidence')
            settings = root / 'unity/ProjectSettings'; settings.mkdir(parents=True)
            (settings / 'new.asset').write_text('new')
            self.assertEqual(['new.asset'], guard.finish()['changed'])


class RunnerSettingsTests(unittest.TestCase):
    def run_fixture(self, runner, drift, failed=False):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); (root / 'tools').mkdir()
            settings = root / 'unity/ProjectSettings'; settings.mkdir(parents=True)
            target = settings / 'QualitySettings.asset'; target.write_text('dirty before\n')
            wrapper = root / 'tools/unity.sh'
            # This is a tiny fixture producer, never Unity or a Player executable.
            xml = ('<test-run result="' + ('Failed' if failed else 'Passed') +
                   '" total="1" passed="' + ('0' if failed else '1') +
                   '" failed="' + ('1' if failed else '0') + '" skipped="0" duration="0.01">' +
                   '<test-case fullname="A.Fixture.Case" result="' +
                   ('Failed' if failed else 'Passed') + '"/></test-run>')
            wrapper.write_text('#!' + sys.executable + '\nimport pathlib, sys\n' +
                (f'pathlib.Path({str(target)!r}).write_text("changed after\\n")\n' if drift else '') +
                f'pathlib.Path(sys.argv[sys.argv.index("-testResults")+1]).write_text({xml!r})\n' +
                'print("unity-run: started fixture", flush=True)\n')
            wrapper.chmod(0o755)
            output = root / 'output'
            argv = (['native_qa.py', '--suite', 'editmode', '--fixture', 'A.Fixture', '--output', str(output)]
                    if runner is native_qa else
                    ['check_qa.py', '--scope', 'focused', '--platform', 'EditMode', '--fixture', 'A.Fixture'])
            with patch.object(runner, 'ROOT', root), patch('sys.argv', argv), \
                    patch.object(runner.subprocess, 'check_output', side_effect=lambda *a, **k: 'fixture' if k.get('text') else b''), \
                    patch.object(check_qa, 'provenance', return_value={'revision': 'fixture'}), \
                    redirect_stdout(io.StringIO()):
                code = runner.main() if runner is native_qa else runner.run()
            if runner is native_qa:
                result = json.loads((output / 'manifest.json').read_text())
                row = result['steps'][0]
                self.assertEqual(not failed, row['tests_passed'])
                evidence = row['project_settings']
            else:
                result = json.loads(next((root / '.local/qa').glob('*/result.json')).read_text())
                self.assertEqual('Failed' if drift or failed else 'Passed', result['result'])
                if drift and not failed:
                    self.assertEqual(1, result['checks'][0]['passed'], 'NUnit PASS retained separately from settings gate')
                evidence = result['project_settings']
            self.assertEqual(int(drift or failed), code)
            self.assertEqual(not drift, evidence['unchanged'])
            self.assertEqual(['QualitySettings.asset'] if drift else [], evidence['changed'])
            self.assertEqual('changed after\n' if drift else 'dirty before\n', target.read_text())
            self.assertEqual(b'dirty before\n', (Path(evidence['evidence']) / 'before/QualitySettings.asset').read_bytes())

    def test_both_runners_keep_nunit_result_and_reject_settings_drift_without_restoring(self):
        for runner in (native_qa, check_qa):
            for drift, failed in ((False, False), (True, False), (True, True), (False, True)):
                with self.subTest(runner=runner.__name__, drift=drift, failed=failed):
                    self.run_fixture(runner, drift, failed)


if __name__ == '__main__':
    unittest.main()
