"""Exercise real Git worktrees, index checks and non-overwriting hooks."""
import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest

SOURCE = Path(__file__).with_name('worktree_storage.py')
spec = importlib.util.spec_from_file_location('worktree_storage', SOURCE)
storage = importlib.util.module_from_spec(spec)
spec.loader.exec_module(storage)


class WorktreeStorageTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.base = Path(self.tmp.name)
        self.root = self.base / 'main'
        self.root.mkdir()
        self.git('init', '-b', 'main')
        self.git('config', 'user.name', 'Storage test')
        self.git('config', 'user.email', 'storage@example.invalid')
        self.git('config', 'commit.gpgsign', 'false')
        self.write('tools/worktree_storage.py', SOURCE.read_bytes())
        self.write('unity/Assets/code.cs', b'class Kept {}\n')
        self.write('future-module/needed.txt', b'Future source directories remain included\n')
        self.write('docs/GAME_SPEC.md', b'# Required specification\n')
        self.write('docs/tasks/handoff.md', b'Needed handoff\n')
        self.write('docs/native-ai/old.zip', b'legacy\0binary')
        self.write('artifacts/playtest/old.png', b'legacy\0image')
        self.write('.gitignore', b'*.ignored\n.local/\n')
        self.git('add', '.')
        self.git('commit', '-m', 'fixture')
        self.initial = self.git('rev-parse', 'HEAD').strip()

    def git(self, *args, root=None, check=True):
        r = subprocess.run(['git', '-C', str(root or self.root), *args],
                           capture_output=True, text=True, check=check)
        return r.stdout if check else r

    def write(self, name, data, root=None):
        p = (root or self.root) / name
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(data)
        return p

    def linked(self, name='work'):
        p = self.base / name
        self.git('worktree', 'add', '-b', name, str(p), 'main')
        return p

    def test_create_never_materializes_evidence_and_retains_required_docs(self):
        p = self.base / 'lean'
        storage.create(self.root, p, 'lean', 'main')
        self.assertFalse((p / 'docs/native-ai').exists())
        self.assertFalse((p / 'artifacts').exists())
        for name in ['docs/GAME_SPEC.md', 'docs/tasks/handoff.md', 'unity/Assets/code.cs',
                     'future-module/needed.txt']:
            self.assertTrue((p / name).is_file(), name)
        self.assertEqual('', self.git('status', '--porcelain', root=p))
        self.assertTrue((self.root / 'docs/native-ai/old.zip').is_file())
        self.assertEqual('', self.git('status', '--porcelain'))

    def test_primary_and_dirty_worktrees_are_not_changed(self):
        with self.assertRaisesRegex(ValueError, 'Primary'):
            storage.prepare(self.root)
        p = self.linked()
        self.write('unity/Assets/code.cs', b'unsaved source', p)
        with self.assertRaisesRegex(ValueError, 'tracked changes'):
            storage.prepare(p)
        self.assertEqual(b'unsaved source', (p / 'unity/Assets/code.cs').read_bytes())
        self.assertTrue((p / 'artifacts/playtest/old.png').is_file())

    def test_local_only_evidence_is_preserved_even_when_ignored(self):
        for name in ['docs/local.ignored', 'artifacts/unknown.txt']:
            p = self.linked('work' + str(len(name)))
            f = self.write(name, b'unique local data', p)
            with self.assertRaisesRegex(ValueError, 'Local-only'):
                storage.prepare(p)
            self.assertEqual(b'unique local data', f.read_bytes())
            self.assertTrue((p / 'artifacts/playtest/old.png').exists())

    def test_custom_sparse_selection_is_not_overwritten(self):
        p = self.linked()
        self.git('sparse-checkout', 'set', '--cone', 'unity', root=p)
        before = self.git('sparse-checkout', 'list', root=p)
        with self.assertRaisesRegex(ValueError, 'custom sparse'):
            storage.prepare(p)
        self.assertEqual(before, self.git('sparse-checkout', 'list', root=p))

    def test_guard_uses_staged_blob_and_keeps_unchanged_legacy(self):
        self.assertEqual([], storage.violations(self.root))
        self.write('docs/result.md', b'# Compact report\n')
        self.git('add', 'docs/result.md')
        self.write('docs/result.md', b'\0unstaged binary is not committed')
        self.assertEqual([], storage.violations(self.root))
        self.write('docs/new.png', b'PNG')
        self.git('add', 'docs/new.png')
        self.assertEqual(['docs/new.png'], [v['path'] for v in storage.violations(self.root)])

    def test_guard_rejects_raw_large_and_unknown_binary_payloads(self):
        for name, data in [('docs/run.log', b'raw log'),
                           ('artifacts/telemetry.json', b'x' * (1024 * 1024 + 1)),
                           ('docs/payload.dat', b'a\0b'),
                           ('artifacts/Game.app/Contents/settings.txt', b'packaged app')]:
            self.write(name, data)
            self.git('add', name)
        self.assertEqual(4, len(storage.violations(self.root)))

    def test_committed_diff_guard_ignores_deletion_and_checks_rename_destination(self):
        self.git('rm', 'docs/native-ai/old.zip')
        self.write('docs/renamed.dat', b'\0binary')
        self.git('add', 'docs/renamed.dat')
        self.git('commit', '-m', 'candidate')
        errors = storage.violations(self.root, self.initial)
        self.assertEqual(['docs/renamed.dat'], [v['path'] for v in errors])

    def test_hooks_prepare_only_new_worktrees_and_reject_artifact_commit(self):
        storage.install_hooks(self.root)
        p = self.linked()
        self.assertFalse((p / 'artifacts').exists())
        self.assertEqual('', self.git('status', '--porcelain', root=p))
        self.write('docs/new.log', b'not a report', p)
        self.git('add', 'docs/new.log', root=p)
        r = self.git('commit', '-m', 'reject raw log', root=p, check=False)
        self.assertNotEqual(0, r.returncode)
        self.assertIn('shared', r.stderr)
        self.assertTrue((self.root / 'artifacts/playtest/old.png').exists())

    def test_install_does_not_overwrite_foreign_hook_or_config(self):
        h = self.root / '.git/hooks/pre-commit'
        h.write_text('#!/bin/sh\n# foreign hook\n')
        with self.assertRaisesRegex(ValueError, 'Existing hook'):
            storage.install_hooks(self.root)
        self.assertEqual('#!/bin/sh\n# foreign hook\n', h.read_text())
        self.assertFalse((h.parent / 'post-checkout').exists())
        self.git('config', 'core.hooksPath', '/some/other/hooks')
        with self.assertRaisesRegex(ValueError, 'core.hooksPath'):
            storage.install_hooks(self.root)


if __name__ == '__main__':
    unittest.main()
