"""Preserve actual ProjectSettings before/after QA; never restore user files."""
import difflib
import hashlib
import json
from pathlib import Path


class ProjectSettingsGuard:
    def __init__(self, root, evidence):
        self.settings = Path(root) / 'unity/ProjectSettings'
        self.evidence = Path(evidence)
        if self.evidence.resolve().is_relative_to(self.settings.resolve()):
            raise ValueError('ProjectSettings evidence must be outside ProjectSettings')
        self.before = self.snapshot()
        self.save('before', self.before)

    def snapshot(self):
        return {str(p.relative_to(self.settings)): p.read_bytes()
                for p in sorted(self.settings.rglob('*')) if p.is_file()}

    def save(self, stage, files):
        for name, data in files.items():
            path = self.evidence / stage / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)

    def finish(self):
        after = self.snapshot()
        self.save('after', after)
        changed = sorted(name for name in self.before.keys() | after.keys()
                         if self.before.get(name) != after.get(name))
        diff = []
        for name in changed:
            diff.extend(difflib.unified_diff(
                self.before.get(name, b'').decode('utf-8', errors='replace').splitlines(True),
                after.get(name, b'').decode('utf-8', errors='replace').splitlines(True),
                fromfile='before/' + name, tofile='after/' + name))
        result = {'unchanged': not changed, 'changed': changed,
                  'before': {n: hashlib.sha256(b).hexdigest() for n, b in self.before.items()},
                  'after': {n: hashlib.sha256(b).hexdigest() for n, b in after.items()},
                  'evidence': str(self.evidence), 'automatic_restore': False}
        self.evidence.mkdir(parents=True, exist_ok=True)
        (self.evidence / 'settings.diff').write_text(''.join(diff))
        (self.evidence / 'manifest.json').write_text(json.dumps(result, indent=2) + '\n')
        return result
