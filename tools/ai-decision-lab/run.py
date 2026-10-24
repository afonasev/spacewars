#!/usr/bin/env python3
"""Build actual production C# and preserve bounded, source-bound decision evidence."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def sources():
    paths = list((ROOT / 'unity/Assets/Spacewars/Runtime').rglob('*.cs'))
    paths += list((ROOT / 'unity/Assets/Spacewars/Simulation').rglob('*.cs'))
    paths += [HERE / name for name in ('Program.cs', 'ai-decision-lab.csproj', 'NuGet.Config', 'run.py')]
    return {str(p.relative_to(ROOT)): digest(p) for p in sorted(paths)}


def run(output, scenario, dotnet):
    # Never reuse an evidence directory or accidentally include our output in source inputs.
    output = output.resolve()
    if output.exists():
        raise ValueError('Output already exists; choose a new evidence directory.')
    if not scenario.is_file():
        raise ValueError('Scenario file is missing.')
    if not dotnet or not Path(dotnet).is_file():
        raise ValueError('Install .NET 8 SDK or pass --dotnet /absolute/path/to/dotnet.')
    before = sources()
    scenario_bytes = scenario.read_bytes()
    output.mkdir(parents=True)
    (output / 'scenario.json').write_bytes(scenario_bytes)
    report = {'schema': 'ai-decision-run-v1', 'status': 'NotRun', 'scope': 'decision-only',
              'source_files': before, 'scenario_sha256': hashlib.sha256(scenario_bytes).hexdigest(),
              'steps': [], 'gameplay_acceptance': 'NotRun', 'unity_equivalence': 'NotRun',
              'limitations': 'Authored observations, fresh owner review; no host, movement, full match, or replacement of NUnit coverage.'}
    try:
        report['revision'] = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
        report['dirty_paths'] = subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True).splitlines()
        report['sdk'] = subprocess.check_output([dotnet, '--version'], text=True).strip()
        env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1')
        dll = HERE / 'bin/Release/net8.0/ai-decision-lab.dll'
        steps = [
            ('build', [dotnet, 'build', str(HERE), '-c', 'Release', '--nologo', '-v:q', '-p:UseSharedCompilation=false']),
            ('checks', [dotnet, str(dll), 'check']),
            ('inspect', [dotnet, str(dll), 'inspect', str(output / 'scenario.json')]),
        ]
        for name, command in steps:
            if sources() != before:
                raise RuntimeError('Sources changed during run; candidate is not qualified.')
            started = time.monotonic()
            with (output / f'{name}.stdout').open('wb') as out, (output / f'{name}.stderr').open('wb') as err:
                completed = subprocess.run(command, cwd=ROOT, env=env, stdout=out, stderr=err, timeout=120)
            report['steps'].append({'name': name, 'command': command, 'exit_code': completed.returncode,
                                    'wall_seconds': time.monotonic() - started})
            if completed.returncode:
                raise RuntimeError(f'{name} failed; see preserved stdout/stderr.')
            if name != 'build':
                payload = json.loads((output / f'{name}.stdout').read_text())
                if name == 'checks' and (payload.get('status') != 'PASS' or payload.get('count', 0) <= 0):
                    raise RuntimeError('Missing nonempty passing check result.')
        if sources() != before:
            raise RuntimeError('Sources changed during run; candidate is not qualified.')
        report['status'] = 'PASS_SELECTED_DECISION_CHECKS'
    except Exception as error:
        report['status'] = 'FAIL'
        report['error'] = str(error)
    finally:
        report['source_unchanged'] = sources() == before
        report['artifacts'] = {p.name: digest(p) for p in sorted(output.iterdir()) if p.is_file()}
        (output / 'manifest.json').write_text(json.dumps(report, indent=2) + '\n')
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--scenario', type=Path, default=HERE / 'cases/science-production.json')
    parser.add_argument('--dotnet', default=shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet'))
    args = parser.parse_args()
    try:
        report = run(args.output, args.scenario, args.dotnet)
    except (ValueError, OSError) as error:
        parser.error(str(error))
    print(json.dumps({k: report[k] for k in ('status', 'revision', 'steps', 'source_unchanged') if k in report}, indent=2))
    return 0 if report['status'] == 'PASS_SELECTED_DECISION_CHECKS' else 1


if __name__ == '__main__':
    sys.exit(main())
