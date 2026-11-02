"""Exact-identity publication checkpoints; no remote payload downloads."""
import hashlib
import json
import pathlib
import shutil
import time
import uuid
from contextlib import contextmanager


def hashfile(path):
    with pathlib.Path(path).open('rb') as f:
        return hashlib.file_digest(f, 'sha256').hexdigest()


def inventory(folder):
    return {p.name: {'size': p.stat().st_size, 'sha256': hashfile(p)}
            for p in sorted(pathlib.Path(folder).iterdir()) if p.is_file() and p.name != 'release-payload.json'}


def check_upload(release, expected, channel, complete=True):
    if release['prerelease'] != (channel == 'test'):
        raise ValueError('release channel rejected')
    names = [a['name'] for a in release['assets']]
    if len(names) != len(set(names)) or not set(names) <= set(expected) or complete and set(names) != set(expected):
        raise ValueError('uploaded asset inventory rejected')
    for asset in release['assets']:
        local = expected[asset['name']]
        if asset.get('state') != 'uploaded' or asset['size'] != local['size']:
            raise ValueError('incomplete uploaded asset: ' + asset['name'])
        if asset.get('digest') and asset['digest'] != 'sha256:' + local['sha256']:
            raise ValueError('uploaded asset digest rejected: ' + asset['name'])


def space_guard(path, needed, floor):
    free = shutil.disk_usage(path).free
    if free < needed + floor:
        raise ValueError(f'insufficient free space: {free} bytes; stage needs {needed} plus {floor} headroom; preserve output and resume after freeing space')


class State:
    def __init__(self, output, identity, resume=False):
        self.root = pathlib.Path(output).resolve()
        self.path = self.root/'release-state.json'
        if resume:
            self.data = json.loads(self.path.read_text())
            if self.data.get('schema') != 1 or self.data.get('identity') != identity:
                raise ValueError('resume identity mismatch (source/version/sequence/channel/notes/legacy)')
        else:
            if self.root.exists() and any(self.root.iterdir()):
                raise ValueError('release output must be empty; use --resume only for the same recorded release')
            self.root.mkdir(parents=True, exist_ok=True)
            self.data = {'schema': 1, 'identity': identity, 'steps': {}, 'attempts': [], 'owner_token': uuid.uuid4().hex}
            self.save()

    def save(self):
        temp = self.path.with_suffix('.tmp')
        temp.write_text(json.dumps(self.data, indent=2)+'\n')
        temp.replace(self.path)

    def record(self, key, files=(), **values):
        hashes = {}
        for path in files:
            path = pathlib.Path(path).resolve()
            name = str(path.relative_to(self.root))
            hashes[name] = {'size': path.stat().st_size, 'sha256': hashfile(path)}
        self.data['steps'][key] = dict(values, files=hashes)
        self.save()

    def get(self, key):
        step = self.data['steps'].get(key)
        if step is None:
            return None
        for name, expected in step['files'].items():
            path = (self.root/name).resolve()
            if not path.is_relative_to(self.root) or not path.is_file() or {'size': path.stat().st_size, 'sha256': hashfile(path)} != expected:
                raise ValueError('checkpoint file missing/changed: ' + name)
        return step

    @contextmanager
    def phase(self, name):
        start = time.monotonic()
        attempt = {'phase': name, 'started_utc': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()), 'status': 'running'}
        self.data['attempts'].append(attempt)
        self.save()
        try:
            yield
        except BaseException as error:
            attempt.update(status='FAIL', error=str(error))
            raise
        else:
            attempt['status'] = 'PASS'
        finally:
            attempt['seconds'] = round(time.monotonic()-start, 3)
            self.save()
