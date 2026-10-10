import io
import json
from contextlib import redirect_stdout
import tempfile
import unittest
from unittest.mock import patch
from pathlib import Path
import xml.etree.ElementTree as ET
import check_qa
from check_qa import UI_METHODS, plan, validate_results


class QaContractTests(unittest.TestCase):
    def test_default_plan_is_ui_without_full_match_tests(self):
        output = io.StringIO()
        with patch('sys.argv', ['check_qa.py', '--plan']), redirect_stdout(output):
            check_qa.run()
        result = json.loads(output.getvalue())
        self.assertEqual(result['scope'], 'ui')
        self.assertFalse(result['full_gameplay_gate'])
        self.assertEqual(result['suites'], [['PlayMode', ['PlayableUiShellTests']]])

    def test_full_requires_prior_human_confirmation_before_launch(self):
        with patch('sys.argv', ['check_qa.py', '--scope', 'full']), \
             patch.object(check_qa.subprocess, 'Popen') as launch:
            with self.assertRaises(SystemExit) as error:
                check_qa.run()
            self.assertEqual(2, error.exception.code)
            launch.assert_not_called()

    def document(self):
        root = ET.Element('test-run', result='Passed', total='18', passed='18', failed='0', skipped='0', duration='2.5')
        for method, count in UI_METHODS.items():
            for i in range(count):
                ET.SubElement(root, 'test-case', fullname='PlayableUiShellTests.' + method + (f'({i})' if count > 1 else ''), result='Passed')
        return root

    def validate(self, root, ui=True, fixtures=('PlayableUiShellTests',)):
        with tempfile.TemporaryDirectory() as directory:
            file = Path(directory) / 'result.xml'
            ET.ElementTree(root).write(file)
            return validate_results(file, fixtures, ui)

    def test_complete_contract(self):
        self.assertEqual(18, self.validate(self.document())['passed'])

    def test_missing_current_research_layout_regression_fails(self):
        root = self.document()
        case = next(c for c in root if c.get('fullname').endswith('.PopulatedResearchStaysBesideMapWithoutCoveringHeaderAt800'))
        root.remove(case); root.set('total', '17'); root.set('passed', '17')
        with self.assertRaises(ValueError): self.validate(root)

    def test_empty_failed_skipped_duplicate_missing_and_unexpected_fail(self):
        for mode in ('empty', 'failed', 'skipped', 'duplicate', 'missing', 'unexpected', 'totals', 'root', 'inconclusive'):
            with self.subTest(mode=mode):
                root = self.document()
                cases = list(root)
                if mode == 'empty': root.clear()
                elif mode in ('failed', 'skipped', 'inconclusive'): cases[0].set('result', mode.title())
                elif mode == 'duplicate': cases[0].set('fullname', cases[1].get('fullname'))
                elif mode == 'missing':
                    root.remove(cases[0]); root.set('total', '17'); root.set('passed', '17')
                elif mode == 'unexpected': cases[0].set('fullname', 'OtherTests.Case')
                elif mode == 'totals': root.set('passed', '19')
                elif mode == 'root': root.set('result', 'Failed')
                with self.assertRaises(ValueError): self.validate(root)

    def test_missing_file_and_bad_xml(self):
        with tempfile.TemporaryDirectory() as directory:
            file = Path(directory) / 'result.xml'
            with self.assertRaises(OSError): validate_results(file)
            file.write_text('<broken')
            with self.assertRaises(ET.ParseError): validate_results(file)

    def test_focused_requires_every_exact_fixture(self):
        root = self.document()
        with self.assertRaises(ValueError): self.validate(root, False, ('MissingFixture',))
        self.assertEqual(18, self.validate(root, False)['passed'])

    def test_routes_and_filters(self):
        self.assertEqual([('PlayMode', ('PlayableUiShellTests',))], plan('ui'))
        self.assertEqual([('EditMode', ()), ('PlayMode', ())], plan('full'))
        self.assertEqual([('EditMode', ('Spacewars.Tests.Contract',))], plan('focused', 'EditMode', ['Spacewars.Tests.Contract']))
        for fixtures in ([], ['A;B'], ['.*'], ['A', 'A']):
            with self.assertRaises(ValueError): plan('focused', 'EditMode', fixtures)
        with self.assertRaises(ValueError): plan('focused', None, ['A'])

    def combined_document(self):
        root = self.document()
        for method in check_qa.UI_AFFECTED_METHODS['PlayableProductionHudTests']:
            ET.SubElement(root, 'test-case', fullname='PlayableProductionHudTests.' + method, result='Passed')
        root.set('total', '21'); root.set('passed', '21')
        return root

    def test_combined_ui_requires_complete_shell_and_affected_contract(self):
        fixtures = ('PlayableUiShellTests', 'PlayableProductionHudTests')
        self.assertEqual(21, self.validate(self.combined_document(), True, fixtures)['passed'])
        for mode in ('shell-missing', 'hud-missing', 'hud-replaced', 'hud-skipped', 'foreign'):
            with self.subTest(mode=mode):
                root = self.combined_document()
                if mode.endswith('missing'):
                    root.remove(list(root)[0 if mode.startswith('shell') else -1])
                    root.set('total', '20'); root.set('passed', '20')
                elif mode == 'hud-replaced': list(root)[-1].set('fullname', 'PlayableProductionHudTests.Unknown')
                elif mode == 'hud-skipped': list(root)[-1].set('result', 'Skipped')
                else: list(root)[-1].set('fullname', 'OtherTests.Case')
                with self.assertRaises(ValueError): self.validate(root, True, fixtures)

    def test_affected_contracts_are_required_for_each_selected_hud(self):
        fixtures = ('PlayableUiShellTests', *check_qa.UI_AFFECTED_METHODS)
        root = self.document()
        for fixture, methods in check_qa.UI_AFFECTED_METHODS.items():
            for method, count in methods.items():
                for index in range(count):
                    ET.SubElement(root, 'test-case', fullname=fixture + '.' + method + (f'({index})' if count > 1 else ''), result='Passed')
        root.set('total', str(len(root))); root.set('passed', str(len(root)))
        self.assertEqual(35, self.validate(root, True, fixtures)['passed'])
        root.remove(list(root)[-1]); root.set('total', '29'); root.set('passed', '29')
        with self.assertRaises(ValueError): self.validate(root, True, fixtures)

    def test_affected_selection_is_ui_only_and_exact(self):
        hud = ('PlayableProductionHudTests',)
        self.assertEqual([('PlayMode', ('PlayableUiShellTests', *hud))], plan('ui', affected=hud))
        for affected in (('Unknown',), ('PlayableInputTests',), hud * 2, ('PlayableUiShellTests',), ('PlayableProductionHudTests.Method',)):
            with self.subTest(affected=affected), self.assertRaises(ValueError): plan('ui', affected=affected)
        for scope in ('full', 'focused'):
            with self.assertRaises(ValueError): plan(scope, 'PlayMode', hud, affected=hud)

    def test_combined_runner_launches_once_and_preserves_exclusive_evidence(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); (root / 'tools').mkdir()
            source = root / 'fixture.xml'; ET.ElementTree(self.combined_document()).write(source)
            wrapper = root / 'tools/unity.sh'
            # Exercise actual launch/evidence plumbing with a deterministic fixture producer.
            wrapper.write_text('#!/usr/bin/env python3\nimport pathlib, sys\n'
                               f'root = pathlib.Path({str(root)!r})\n'
                               '(root / "arguments.json").write_text(__import__("json").dumps(sys.argv[1:]))\n'
                               '(root / "launches").open("a").write("launch\\n")\n'
                               'pathlib.Path(sys.argv[sys.argv.index("-testResults")+1]).write_bytes((root / "fixture.xml").read_bytes())\n'
                               'print("unity-run: started fixture", flush=True)\n')
            wrapper.chmod(0o755)
            with patch.object(check_qa, 'ROOT', root), patch.object(check_qa, 'provenance', return_value={'revision': 'fixture'}), \
                 patch('sys.argv', ['check_qa.py', '--scope', 'ui', '--affected-fixture', 'PlayableProductionHudTests', '--exclusive']), \
                 patch.object(check_qa.subprocess, 'check_output', return_value=b''):
                with redirect_stdout(io.StringIO()): self.assertEqual(0, check_qa.run())
            arguments = json.loads((root / 'arguments.json').read_text())
            self.assertEqual('exclusive', arguments[0])
            self.assertEqual('PlayableUiShellTests;PlayableProductionHudTests', arguments[arguments.index('-testFilter')+1])
            self.assertEqual(['launch'], (root / 'launches').read_text().splitlines())
            result = json.loads(next((root / '.local/qa').glob('*/result.json')).read_text())
            self.assertEqual('Passed', result['result']); self.assertFalse(result['full_gameplay_gate'])
            self.assertEqual('exclusive', result['host_mode']); self.assertEqual(21, result['checks'][0]['passed'])
            self.assertIn('host_admission_seconds', result['checks'][0])
            check = result['checks'][0]
            self.assertAlmostEqual(check['wall_seconds'] - check['host_admission_seconds'],
                                   check['editor_wall_seconds'])

    def test_runner_rejects_successful_exit_without_xml_and_failed_launcher(self):
        # Exercise real subprocess/evidence plumbing, without launching Unity.
        for exit_code in (0, 7):
            with self.subTest(exit_code=exit_code), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                (root / 'tools').mkdir()
                wrapper = root / 'tools/unity.sh'
                wrapper.write_text('#!/bin/sh\nexit ' + str(exit_code) + '\n')
                wrapper.chmod(0o755)
                with patch.object(check_qa, 'ROOT', root), patch.object(check_qa, 'provenance', return_value={'revision': 'fixture'}), \
                     patch('sys.argv', ['check_qa.py', '--scope', 'ui']), \
                     patch.object(check_qa.subprocess, 'check_output', return_value=b''):
                    with redirect_stdout(io.StringIO()):
                        self.assertEqual(1, check_qa.run())
                import json
                result = json.loads(next((root / '.local/qa').glob('*/result.json')).read_text())
                self.assertEqual('Failed', result['result'])
                self.assertEqual(exit_code, result['checks'][0]['exit_code'])


if __name__ == '__main__': unittest.main()
