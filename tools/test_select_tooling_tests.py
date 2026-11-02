import unittest
import tempfile
import io
from contextlib import redirect_stdout
from unittest.mock import patch
from pathlib import Path

import select_tooling_tests as selector
from select_tooling_tests import select, ROOT, QA_TESTS


class AffectedSelectionTests(unittest.TestCase):
    def test_pr_base_and_github_outputs_use_the_reviewed_selection(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / 'outputs'
            with patch('sys.argv', ['select_tooling_tests.py', '--base', 'base-sha', '--github-output', str(output)]), \
                    patch.object(selector.subprocess, 'check_output', return_value=b'tools/route_performance_qa.py\0') as git, \
                    redirect_stdout(io.StringIO()):
                selector.main()
            git.assert_called_once_with(['git', 'diff', '--name-only', '-z', 'base-sha', 'HEAD', '--'], cwd=ROOT)
            self.assertEqual('modules=tools.test_route_performance_qa\ndecision_lab=false\n', output.read_text())

    def test_release_helpers_select_their_actual_regressions(self):
        for name,expected in [('publish.py',selector.RELEASE_TESTS),('publish_state.py',selector.RELEASE_TESTS),('build.py',selector.BUILD_TESTS),('build_state.py',selector.BUILD_TESTS)]:
            with self.subTest(name=name):
                result=select(['tools/native-release/'+name])
                self.assertEqual(result['modules'],sorted(expected))
                self.assertFalse(result['decision_lab'])

    def test_changed_qa_dependency_selects_both_runners_and_guard(self):
        result = select(['tools/qa_project_settings.py'])
        self.assertEqual(sorted(QA_TESTS), result['modules'])
        self.assertFalse(result['decision_lab'])

    def test_player_wrapper_change_selects_only_its_tooling_contracts(self):
        result = select(['tools/route_performance_qa.py'])
        self.assertEqual(['tools.test_route_performance_qa'], result['modules'])
        self.assertFalse(result['decision_lab'])

    def test_lab_and_its_production_dependencies_require_the_explicit_lab_route(self):
        for path in ['tools/ai-decision-lab/test_runner.py', 'tools/ai-decision-lab/Program.cs',
                     'unity/Assets/Spacewars/Runtime/AiTechnologyFunding.cs',
                     'unity/Assets/Spacewars/Simulation/Ai/AiProfile.cs']:
            with self.subTest(path=path):
                result = select([path])
                self.assertTrue(result['decision_lab'])
                self.assertEqual([], result['modules'])

    def test_docs_assets_and_unknown_filenames_never_expand_to_full_discovery(self):
        self.assertEqual({'modules': [], 'decision_lab': False}, select([
            'docs/README.md', 'unity/Assets/Spacewars/Content/model.prefab',
            'tools/unknown.py', 'tools/test_missing.py', 'tools/test_x.py\nmodules=unsafe']))

    def test_workflow_selection_is_bounded_and_every_module_exists(self):
        result = select(['.github/workflows/tooling.yml', 'tools/check_qa.py'])
        self.assertTrue(result['decision_lab'])
        self.assertEqual(5, len(result['modules']))
        for module in result['modules']:
            self.assertTrue((ROOT / Path(module.replace('.', '/') + '.py')).is_file())

    def test_existing_tooling_tests_are_selected_directly_and_deduplicated(self):
        result = select(['tools/test_route_performance_qa.py', 'tools/route_performance_qa.py'])
        self.assertEqual(['tools.test_route_performance_qa'], result['modules'])


if __name__ == '__main__':
    unittest.main()
