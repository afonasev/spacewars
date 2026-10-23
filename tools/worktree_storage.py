#!/usr/bin/env python3
"""Keep historical evidence out of code worktrees and new code commits."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys

PATTERNS = ('/*', '!/artifacts/', '!/docs/*/', '/docs/tasks/')
MAX_DOCUMENT_BYTES = 1024 * 1024
RAW_SUFFIXES = {'.log', '.csv', '.jsonl', '.xml', '.zip', '.gz', '.zst',
                '.tar', '.mp4', '.mov', '.wav', '.flac', '.mp3', '.png',
                '.jpg', '.jpeg', '.webp', '.gif', '.dll', '.pdb', '.exe',
                '.dmg', '.pkg', '.nupkg', '.world'}
HOOK_MARKER = '# spacewars-worktree-storage-v1'


def git(root, *args, data=None):
    return subprocess.run(['git', '-C', str(root), *args], input=data,
                          stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                          check=True).stdout


def location(root):
    root = Path(git(root, 'rev-parse', '--show-toplevel').decode().strip())
    gitdir = Path(git(root, 'rev-parse', '--absolute-git-dir').decode().strip())
    common = Path(git(root, 'rev-parse', '--git-common-dir').decode().strip())
    if not common.is_absolute():
        common = root / common
    return root, gitdir.resolve(), common.resolve()


def prepare(root, automatic=False, fresh=False):
    root, gitdir, common = location(root)

    def refuse(reason):
        if automatic:
            return {'skipped': reason, 'root': str(root)}
        raise ValueError(reason)

    if gitdir == common:
        return refuse('Primary checkout is preserved; prepare only linked worktrees')
    if not fresh and git(root, 'status', '--porcelain', '--untracked-files=no'):
        return refuse('Worktree has tracked changes; preserve it unchanged')
    # Sparse checkout may remove ignored files in excluded directories. Reject
    # both kinds of local-only data, even if normal git status hides them.
    for options in [('--others', '--exclude-standard'),
                    ('--others', '--ignored', '--exclude-standard')]:
        if git(root, 'ls-files', *options, '--', 'docs', 'artifacts'):
            return refuse('Local-only docs/artifacts exist; preserve them unchanged')
    sparse = gitdir / 'info/sparse-checkout'
    if sparse.exists() and tuple(sparse.read_text().splitlines()) != PATTERNS:
        return refuse('Existing custom sparse selection is preserved')
    git(root, 'sparse-checkout', 'set', '--no-cone', '--no-sparse-index',
        '--stdin', data=('\n'.join(PATTERNS) + '\n').encode())
    if fresh:
        # worktree add --no-checkout leaves no populated index. Merely setting
        # sparse patterns is insufficient: initialize HEAD with those patterns.
        git(root, 'read-tree', '-mu', 'HEAD')
    return {'prepared': str(root), 'patterns': list(PATTERNS)}


def create(root, path, branch, ref):
    root, _, _ = location(root)
    path = Path(path).resolve()
    if path.exists():
        raise ValueError('Destination already exists; it will not be reused or reset')
    git(root, 'worktree', 'add', '--no-checkout', '-b', branch, str(path), ref)
    try:
        return prepare(path, fresh=True)
    except Exception:
        print('Creation stopped; retained worktree for inspection: ' + str(path),
              file=sys.stderr)
        raise


def violations(root, base=None):
    root, _, _ = location(root)
    if base:
        git(root, 'rev-parse', '--verify', base + '^{commit}')
        names = git(root, 'diff', '--name-only', '-z', '--diff-filter=ACMRT',
                    base, 'HEAD', '--', 'docs', 'artifacts')
        revision = 'HEAD'
    else:
        names = git(root, 'diff', '--cached', '--name-only', '-z',
                    '--diff-filter=ACMRT', '--', 'docs', 'artifacts')
        revision = ''
    found = []
    for raw in names.split(b'\0'):
        if not raw:
            continue
        name = os.fsdecode(raw)
        blob = revision + ':' + name
        size = int(git(root, 'cat-file', '-s', blob))
        reason = None
        if size > MAX_DOCUMENT_BYTES:
            reason = 'document exceeds 1 MiB'
        elif Path(name).suffix.lower() in RAW_SUFFIXES or any(
                part.lower().endswith('.app') for part in Path(name).parts):
            reason = 'generated/binary evidence belongs in shared evidence_root'
        else:
            content = git(root, 'cat-file', 'blob', blob)
            try:
                content.decode('utf-8')
                if b'\0' in content:
                    reason = 'binary data'
            except UnicodeDecodeError:
                reason = 'binary data'
        if reason:
            found.append({'path': name, 'bytes': size, 'reason': reason})
    return found


def install_hooks(root):
    root, _, common = location(root)
    configured = subprocess.run(['git', '-C', str(root), 'config', '--get',
                                 'core.hooksPath'], capture_output=True, text=True)
    if configured.stdout.strip():
        raise ValueError('Existing core.hooksPath is preserved; chain these checks explicitly')
    hooks = common / 'hooks'
    primary_tool = common.parent / 'tools/worktree_storage.py'
    if not primary_tool.is_file():
        raise ValueError('Integrate tools/worktree_storage.py into the primary checkout first')
    # New managed worktrees from older refs can still use the current helper.
    # Active worktrees and ordinary branch switches are never sparsified.
    quoted_tool = "'" + str(primary_tool).replace("'", "'\\''") + "'"
    scripts = {
        'post-checkout': '#!/bin/sh\n' + HOOK_MARKER + '\n'
            'case "$1:$3" in\n'
            '  0000000000000000000000000000000000000000:1)\n'
            '    root=$(git rev-parse --show-toplevel) || exit $?\n'
            '    python3 ' + quoted_tool + ' --root "$root" prepare --automatic\n'
            '    ;;\n'
            'esac\n',
        'pre-commit': '#!/bin/sh\n' + HOOK_MARKER + '\n'
            'root=$(git rev-parse --show-toplevel) || exit $?\n'
            '# Old active branches retain their existing commit behavior.\n'
            '[ -f "$root/tools/worktree_storage.py" ] || exit 0\n'
            'exec python3 "$root/tools/worktree_storage.py" --root "$root" check\n',
    }
    for name in scripts:
        p = hooks / name
        if p.is_symlink() or (p.exists() and HOOK_MARKER not in p.read_text()):
            raise ValueError('Existing hook is preserved: ' + str(p))
    hooks.mkdir(exist_ok=True)
    for name, content in scripts.items():
        p = hooks / name
        p.write_text(content)
        p.chmod(0o755)
    return {'installed': [str(hooks / name) for name in scripts],
            'managed_checkout_peak': 'post-checkout hook runs after initial checkout'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[1])
    sub = parser.add_subparsers(dest='command', required=True)
    p = sub.add_parser('prepare', help='Sparsify a clean owned linked worktree')
    p.add_argument('--automatic', action='store_true')
    p = sub.add_parser('create', help='Create without first materializing historical evidence')
    p.add_argument('path', type=Path)
    p.add_argument('--branch', required=True)
    p.add_argument('--ref', default='main')
    p = sub.add_parser('check', help='Check staged docs/artifacts, or committed diff from --base')
    p.add_argument('--base')
    sub.add_parser('install-hooks', help='Install non-overwriting repository hooks after integration')
    args = parser.parse_args()
    try:
        if args.command == 'create':
            result = create(args.root, args.path, args.branch, args.ref)
        elif args.command == 'prepare':
            result = prepare(args.root, args.automatic)
        elif args.command == 'install-hooks':
            result = install_hooks(args.root)
        else:
            errors = violations(args.root, args.base)
            result = {'violations': errors}
            if errors:
                print(json.dumps(result, indent=2))
                print('Keep compact reports in docs; use .local for scratch and '
                      'workflow/project.json evidence_root for durable evidence.', file=sys.stderr)
                return 1
        print(json.dumps(result, indent=2))
        return 0
    except (ValueError, subprocess.CalledProcessError) as error:
        print(str(error), file=sys.stderr)
        if isinstance(error, subprocess.CalledProcessError):
            print(error.stderr.decode(errors='replace'), file=sys.stderr)
        return 2


if __name__ == '__main__':
    sys.exit(main())
