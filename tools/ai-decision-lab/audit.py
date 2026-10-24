#!/usr/bin/env python3
"""Read-only inventory: ancestry/patch equality do not establish semantic readiness."""
import argparse
import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[2]


def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT, text=True).strip()


def blob(ref, path):
    result = subprocess.run(['git', 'rev-parse', '--verify', f'{ref}:{path}'], cwd=ROOT,
                            capture_output=True, text=True)
    return result.stdout.strip() if result.returncode == 0 else None


def audit(main, candidate):
    main, candidate = git('rev-parse', main), git('rev-parse', candidate)
    base = git('merge-base', main, candidate)
    cherry = {line[2:]: line[0] for line in git('cherry', main, candidate).splitlines()}
    commits = []
    for sha in git('rev-list', '--reverse', '--no-merges', f'{main}..{candidate}').splitlines():
        paths = git('diff-tree', '--no-commit-id', '--name-only', '-r', sha).splitlines()
        commits.append({'sha': sha, 'subject': git('show', '-s', '--format=%s', sha),
                        'patch_equal_in_main': cherry.get(sha) == '-', 'paths': paths})
    paths = sorted(set(git('diff', '--name-only', base, candidate).splitlines()))
    files = [{'path': p, 'main_blob': blob(main, p), 'candidate_blob': blob(candidate, p),
              'same_at_tips': blob(main, p) == blob(candidate, p),
              'main_changed_since_fork': blob(main, p) != blob(base, p)} for p in paths]
    return {'schema': 'ai-candidate-audit-v1', 'main': main, 'candidate': candidate, 'fork': base,
            'candidate_is_ancestor': subprocess.run(['git', 'merge-base', '--is-ancestor', candidate, main], cwd=ROOT).returncode == 0,
            'nonmerge_commits': commits, 'files': files,
            'scope': 'Git inventory only; same behavior under different source needs semantic review; no QA or merge performed.'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--main', default='main')
    parser.add_argument('--candidate', default='codex/ai-methodology-cycle-20261022')
    args = parser.parse_args()
    print(json.dumps(audit(args.main, args.candidate), indent=2))
