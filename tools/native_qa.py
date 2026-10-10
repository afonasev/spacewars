#!/usr/bin/env python3
"""Run current Unity prototype checks; no browser/SOURCE comparison gates."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET
from check_qa import validate_results

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--suite', choices=['editmode', 'playmode', 'build', 'all', 'player'], required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--dry-run', action='store_true')
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    steps = (['editmode', 'playmode'] if args.suite == 'all' else
             ['build', 'editmode', 'playmode'] if args.suite == 'player' else [args.suite])
    revision = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    diff = subprocess.check_output(['git', 'diff', 'HEAD', '--', 'unity', 'tools/native_qa.py'], cwd=ROOT)
    (output / 'tracked.diff').write_bytes(diff)
    manifest = {'commit': revision, 'tracked_diff_sha256': hashlib.sha256(diff).hexdigest(),
                'scope': 'current native runtime; retired comparisons and web suites excluded', 'steps': []}
    failed = False
    for step in steps:
        command = [str(ROOT / 'tools/unity.sh'), 'shared', '-batchmode']
        if step == 'build':
            command += ['-executeMethod', 'Spacewars.Editor.PlayableProject.Build', '-quit']
        else:
            command += ['-runTests', '-testPlatform', 'EditMode' if step == 'editmode' else 'PlayMode',
                        '-testResults', str(output / (step + '.xml'))]
        command += ['-logFile', str(output / (step + '.log'))]
        row = {'step': step, 'command': command, 'exit_code': None}
        manifest['steps'].append(row)
        if not args.dry_run:
            # Never accept a stale XML from an earlier run.
            xml_path = output / (step + '.xml')
            if step != 'build':
                xml_path.unlink(missing_ok=True)
            with (output / (step + '-host.log')).open('w') as log:
                row['exit_code'] = subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT).returncode
            if step != 'build' and xml_path.exists():
                result = ET.parse(xml_path).getroot()
                row['tests'] = {k: result.get(k) for k in ['result', 'total', 'passed', 'failed', 'skipped']}
                try:
                    row['executed'] = validate_results(xml_path)
                    row['passed'] = row['exit_code'] == 0
                except ValueError as error:
                    row['validation_error'] = str(error)
                    row['passed'] = False
            else:
                row['passed'] = step == 'build' and row['exit_code'] == 0 and 'PLAYABLE_BUILD_PASS' in (output / 'build.log').read_text(errors='replace')
            failed |= not row['passed']
        (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
        print(json.dumps(row), flush=True)
        if failed:
            break
    return int(failed)


if __name__ == '__main__':
    sys.exit(main())
