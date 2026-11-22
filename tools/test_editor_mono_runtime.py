"""Exercise native identity/cache boundaries without starting an Editor."""
import os
from pathlib import Path
import tempfile
import subprocess
import unittest
from unittest.mock import patch

import editor_mono_runtime as runtime


class EditorMonoRuntimeTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.library = self.root / 'candidate.dylib'
        self.library.write_bytes(b'qualified native fixture')
        spec = dict(runtime.SPEC, id='fixture-runtime', runtime_sha256=runtime.digest(self.library))
        self.spec = patch.object(runtime, 'SPEC', spec)
        self.spec.start(); self.addCleanup(self.spec.stop)
        self.environment = patch.dict(os.environ, {'SPACEWARS_EDITOR_MONO_CACHE_ROOT': str(self.root / 'cache')})
        self.environment.start(); self.addCleanup(self.environment.stop)

    def test_prepare_preserves_source_and_refuses_to_replace_corrupt_cache(self):
        before = self.library.read_bytes()
        cache = runtime.prepare(self.library)
        self.assertEqual(before, self.library.read_bytes())
        self.assertEqual(cache, runtime.prepare(self.library))
        (cache / runtime.LIBRARY).write_bytes(b'foreign or corrupt bytes')
        with self.assertRaises(ValueError):
            runtime.prepare(self.library)
        self.assertEqual(b'foreign or corrupt bytes', (cache / runtime.LIBRARY).read_bytes())

    def test_wrong_library_is_rejected_before_cache_creation(self):
        self.library.write_bytes(b'wrong runtime')
        with self.assertRaises(ValueError):
            runtime.prepare(self.library)
        self.assertFalse(runtime.cache_root().exists())

    def test_launch_retains_arguments_and_limits_override_to_child_environment(self):
        editor = self.root / 'Unity.app/Contents/MacOS/Unity'
        editor.parent.mkdir(parents=True); editor.write_bytes(b'editor')
        stock = editor.parent.parent / 'Frameworks' / runtime.LIBRARY
        stock.parent.mkdir(); stock.write_bytes(b'stock native runtime')
        runtime.SPEC.update(stock_editor_sha256=runtime.digest(editor), stock_mono_sha256=runtime.digest(stock))
        cache = runtime.prepare(self.library)
        args = ['-projectPath', '/owned/project', '-batchmode', '-noaudio', '-muted']
        with patch.dict(os.environ, {'DYLD_LIBRARY_PATH': '/prior', 'SPACEWARS_EDITOR_MONO_STOCK': '0'}):
            with patch.object(os, 'execve') as execute:
                runtime.launch(str(editor), args)
                command, actual_args, environment = execute.call_args.args
            self.assertEqual(str(editor.resolve()), command)
            self.assertEqual([str(editor.resolve()), *args], actual_args)
            self.assertEqual(str(cache) + ':/prior', environment['DYLD_LIBRARY_PATH'])
            self.assertEqual('/prior', os.environ['DYLD_LIBRARY_PATH'])
        editor.write_bytes(b'other engine')
        with self.assertRaises(ValueError):
            runtime.validate_stock(editor)

    def test_explicit_stock_route_needs_no_cache(self):
        with patch.dict(os.environ, {'SPACEWARS_EDITOR_MONO_STOCK': '1'}), patch.object(os, 'execv') as execute:
            runtime.launch('/explicit/Unity', ['-muted'])
        execute.assert_called_once_with('/explicit/Unity', ['/explicit/Unity', '-muted'])

    def test_source_rebuild_preserves_an_existing_workspace(self):
        existing = self.root / 'existing-build'
        existing.mkdir(); sentinel = existing / 'keep.txt'; sentinel.write_text('keep')
        result = subprocess.run(['sh', str(runtime.ROOT / 'tools/editor-mono/rebuild.sh'), str(existing)],
                                capture_output=True, text=True)
        self.assertNotEqual(0, result.returncode)
        self.assertEqual('keep', sentinel.read_text())
        self.assertEqual([sentinel], list(existing.iterdir()))


if __name__ == '__main__':
    unittest.main()
