"""Select reviewed tooling contracts from a committed PR diff; never launch tests."""
import argparse
import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
QA_TESTS = {'tools.test_native_qa', 'tools.test_check_qa', 'tools.test_qa_project_settings'}
RELEASE_TESTS = {'tools.test_github_publication', 'tools.test_release_pipeline', 'tools.test_game_release_notes'}
BUILD_TESTS = {'tools.test_release_pipeline', 'tools.test_client_resources', 'tools.test_test_match_logging_build'}
PACKAGE_TESTS = {'tools.test_native_distribution', 'tools.test_windows_dpi', 'tools.test_desktop_launcher'}
PLUGIN_TESTS = {'tools.test_native_distribution', 'tools.test_client_resources', 'tools.test_test_match_logging_build'}
DEPENDENCIES = {
    'tools/native-release/publish.py': RELEASE_TESTS,
    'tools/native-release/publish_state.py': RELEASE_TESTS,
    'tools/native-release/build.py': BUILD_TESTS,
    'tools/native-release/build_state.py': BUILD_TESTS,
    'tools/native-release/package.py': PACKAGE_TESTS,
    'tools/native_plugins.py': PLUGIN_TESTS,
    'tools/build_recast.py': PLUGIN_TESTS,
    'tools/native_qa.py': QA_TESTS,
    'tools/check_qa.py': QA_TESTS,
    'tools/qa/split_scale_report.py': {'tools.test_split_scale_report'},
    'tools/qa_project_settings.py': QA_TESTS,
    'tools/unity.sh': {'tools.test_native_qa', 'tools.test_unity_quiet'},
    'Makefile': {'tools.test_local_workflow'},
    'tools/select_tooling_tests.py': {'tools.test_select_tooling_tests'},
    '.github/workflows/tooling.yml': QA_TESTS | {
        'tools.test_select_tooling_tests', 'tools.test_route_performance_qa'},
}


def select(paths, root=ROOT):
    modules = set()
    decision_lab = False
    recast_native = False
    for name in paths:
        modules.update(DEPENDENCIES.get(name, ()))
        if name.startswith('unity/Assets/Spacewars/Plugins/'):
            modules.update(PLUGIN_TESTS)
        if (name.startswith('native/recast/') or name in (
                'tools/build_recast.py', 'tools/test_recast_native.py',
                '.github/workflows/tooling.yml', 'tools/select_tooling_tests.py')):
            recast_native = True
        path = Path(name)
        # The C ABI module requires a freshly built macOS library, not Ubuntu discovery.
        if path.parent == Path('tools') and path.suffix == '.py' and name != 'tools/test_recast_native.py':
            test_name = path.stem if path.stem.startswith('test_') else 'test_' + path.stem
            if (root / 'tools' / (test_name + '.py')).is_file():
                modules.add('tools.' + test_name)
        if (name.startswith(('tools/ai-decision-lab/', 'unity/Assets/Spacewars/Runtime/',
                             'unity/Assets/Spacewars/Simulation/')) or
                name in ('.github/workflows/tooling.yml', 'tools/select_tooling_tests.py')):
            decision_lab = True
    return {'modules': sorted(modules), 'decision_lab': decision_lab, 'recast_native': recast_native}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base', required=True)
    parser.add_argument('--github-output', type=Path)
    args = parser.parse_args()
    paths = subprocess.check_output(
        ['git', 'diff', '--name-only', '-z', args.base, 'HEAD', '--'], cwd=ROOT).decode().split('\0')
    result = select(paths)
    if args.github_output:
        with args.github_output.open('a') as output:
            output.write('modules=' + ' '.join(result['modules']) + '\n')
            output.write('decision_lab=' + str(result['decision_lab']).lower() + '\n')
            output.write('recast_native=' + str(result['recast_native']).lower() + '\n')
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
