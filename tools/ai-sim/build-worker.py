#!/usr/bin/env python3
"""Explicit minimal QA worker build. Always use the host Unity wrapper."""
import argparse, json, os, pathlib, subprocess, sys
root = pathlib.Path(__file__).resolve().parents[2]
p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--output', required=True, type=pathlib.Path)
a = p.parse_args()
output = a.output.resolve()
if output.exists() and any(output.iterdir()):
    sys.exit('Build output must be empty; preserve exact existing workers.')
if subprocess.check_output(['git', 'status', '--porcelain'], cwd=root):
    sys.exit('Commit source before QA worker build; exact clean revision required.')
revision = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip()
output.mkdir(parents=True, exist_ok=True)
env = dict(os.environ, SPACEWARS_AI_BUILD_OUTPUT=str(output), SPACEWARS_AI_CODE_REVISION=revision,
           SPACEWARS_AI_LAUNCHER=str(root / 'tools/unity.sh'))
command = [str(root / 'tools/unity.sh'), 'shared', '-batchmode', '-nographics', '-executeMethod',
           'Spacewars.Editor.HeadlessProject.Build', '-quit', '-logFile', str(output / 'build.log')]
(output / 'build-request.json').write_text(json.dumps({'revision': revision, 'command': command}, indent=2) + '\n')
with (output / 'host.log').open('w') as log:
    result = subprocess.run(command, cwd=root, env=env, stdout=log, stderr=subprocess.STDOUT)
(output / 'build-exit.json').write_text(json.dumps({'exit': result.returncode, 'revision': revision}) + '\n')
sys.exit(result.returncode)
