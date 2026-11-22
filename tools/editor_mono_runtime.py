#!/usr/bin/env python3
"""Install/verify a pinned, process-local Editor runtime; never edit Unity itself."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
SPEC = json.loads((ROOT / 'tools/editor-mono/runtime.json').read_text())
LIBRARY = 'libmonobdwgc-2.0.dylib'


def digest(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def cache_root():
    parent = Path(os.environ.get('SPACEWARS_EDITOR_MONO_CACHE_ROOT',
                                str(Path.home() / '.local/share/spacewars/editor-mono')))
    return parent / SPEC['id']


def validate_stock(editor):
    editor = Path(editor).resolve()
    library = editor.parent.parent / 'Frameworks' / LIBRARY
    if digest(editor) != SPEC['stock_editor_sha256'] or digest(library) != SPEC['stock_mono_sha256']:
        raise ValueError('Editor/Mono identity differs from the qualified 6000.3.23f1 ARM64 build')
    return editor


def validate_cache(path):
    path = Path(path)
    metadata = json.loads((path / 'manifest.json').read_text())
    if metadata != SPEC or digest(path / LIBRARY) != SPEC['runtime_sha256']:
        raise ValueError('Editor Mono cache does not match its pinned runtime identity')
    return path


def prepare(library=None):
    destination = cache_root()
    if destination.exists():
        return validate_cache(destination)
    if library is None:
        profile = json.loads((ROOT / 'workflow/project.json').read_text())
        library = Path(profile['evidence_root']) / SPEC['artifact']
    library = Path(library).resolve()
    if digest(library) != SPEC['runtime_sha256']:
        raise ValueError('Provided Mono library is not the pinned allocator candidate')
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = Path(tempfile.mkdtemp(prefix='.prepare-', dir=destination.parent))
    try:
        shutil.copy2(library, temporary / LIBRARY)
        (temporary / 'manifest.json').write_text(json.dumps(SPEC, indent=2) + '\n')
        validate_cache(temporary)
        os.rename(temporary, destination)
    finally:
        if temporary.exists():
            shutil.rmtree(temporary)
    return destination


def launch(editor, arguments):
    if os.environ.get('SPACEWARS_EDITOR_MONO_STOCK') == '1':
        return os.execv(editor, [editor, *arguments])
    editor = validate_stock(editor)
    runtime = validate_cache(cache_root())
    environment = dict(os.environ)
    previous = environment.get('DYLD_LIBRARY_PATH')
    environment['DYLD_LIBRARY_PATH'] = str(runtime) + (':' + previous if previous else '')
    os.execve(str(editor), [str(editor), *arguments], environment)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    prepare_parser = commands.add_parser('prepare', help='Copy a pinned library into the local cache; no build')
    prepare_parser.add_argument('--library', type=Path)
    commands.add_parser('verify', help='Verify cache bytes without launching anything')
    launch_parser = commands.add_parser('launch')
    launch_parser.add_argument('--editor', required=True)
    launch_parser.add_argument('arguments', nargs=argparse.REMAINDER)
    args = parser.parse_args()
    try:
        if args.command == 'prepare':
            print(prepare(args.library))
        elif args.command == 'verify':
            print(validate_cache(cache_root()))
        else:
            if platform.system() != 'Darwin' or platform.machine() != 'arm64':
                raise ValueError('This pinned Mono runtime is qualified only for macOS ARM64')
            launch(args.editor, args.arguments[1:] if args.arguments[:1] == ['--'] else args.arguments)
    except (OSError, ValueError, KeyError) as error:
        parser.exit(2, f'{error}\nPrepare the pinned runtime with: python3 tools/editor_mono_runtime.py prepare\n')


if __name__ == '__main__':
    main()
