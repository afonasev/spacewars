"""Validate build-owned tracked outputs before restoring their exact baseline bytes."""
import hashlib
import json
import pathlib
import re
import subprocess

SCENE = 'unity/Assets/Spacewars/Content/Scenes/Playable.unity'
SETTINGS = 'unity/ProjectSettings/ProjectSettings.asset'
PIPELINE = 'unity/Assets/Spacewars/Content/FoundationPipeline.asset'
GLOBAL = 'unity/Assets/UniversalRenderPipelineGlobalSettings.asset'
LAB = 'unity/Assets/Spacewars/Resources/NativeLabCatalog.json'
MAPS = 'unity/Assets/Spacewars/Content/Resources/PreparedMaps/'


def release_profile(root, version):
    """Reject authoring histories before issuing a publishable build identity."""
    raw = (pathlib.Path(root) / LAB).read_bytes()
    catalog = json.loads(raw)
    if catalog.get('schema') != 2 or catalog.get('releaseVersion') != version:
        raise ValueError('release profile is not bound to the build version')
    profiles = catalog.get('profiles', [])
    if len(profiles) != 1 or profiles[0].get('id') != catalog.get('defaultId') or catalog.get('defaultId') != 'spacewars-default':
        raise ValueError('shipping catalog requires exactly one Default profile')
    profile = profiles[0]
    rows = profile.get('versions', [])
    if len(rows) != 1:
        raise ValueError('intermediate profile versions cannot ship')
    row = rows[0]
    revision = row.get('revision')
    ai = row.get('aiProfile', {})
    if (type(revision) is not int or revision < 1 or profile.get('release') != [revision]
            or catalog.get('profileDefaultsVersion') != revision or row.get('releaseVersion') != version
            or row.get('data', {}).get('revision') != revision
            or ai.get('id') != profile['id'] or ai.get('revision') != revision):
        raise ValueError('balance and AI must share the release profile identity')
    if any(not isinstance(row.get(key), dict) or not row[key] for key in ('data', 'audio', 'aiProfile', 'modelDetail')) or not ai.get('fields'):
        raise ValueError('release profile requires complete balance, AI, audio and detail snapshots')
    return {'id': profile['id'], 'revision': revision, 'release_version': version,
            'catalog_sha256': hashlib.sha256(raw).hexdigest()}


def generated(path):
    return (path in (SCENE, SETTINGS, PIPELINE, GLOBAL, LAB)
            or re.fullmatch(re.escape(MAPS) + r'(?:World|Minimap|TacticalMinimap|OverheadBattle|Preview)-[A-Za-z0-9]+\.bytes', path) is not None
            or path in (MAPS + 'schema.json', MAPS + 'stamp.txt', MAPS + 'overhead-stamp.txt')
            or path.startswith('unity/Assets/Spacewars/Content/Resources/StyleA/') and path.endswith('-foundation-concrete.mat'))


def validate_change(path, before, after):
    if not generated(path):
        raise ValueError('unexpected source change during build: ' + path)
    # Unity maintains shader-strip caches here. Rendering/quality values must not drift.
    pattern = (r'^  m_Prefilter[^\n]*\n?' if path == PIPELINE
               else r'^      - rid: [0-9]+\n?' if path == GLOBAL else None)
    if pattern:
        strip = lambda raw: re.sub(pattern, '', raw.decode(), flags=re.MULTILINE)
        if strip(before) != strip(after):
            raise ValueError('unexpected render setting change during build: ' + path)


def capture(root, runner=None):
    root = pathlib.Path(root)
    runner = runner or (lambda *args: subprocess.check_output(['git', *args], cwd=root))
    names = runner('ls-files', '-z').decode().split('\0')
    names += runner('ls-files', '--others', '--exclude-standard', '-z').decode().split('\0')
    dirty = set(runner('diff', '--name-only', 'HEAD', '-z').decode().split('\0'))
    untracked = set(runner('ls-files', '--others', '--exclude-standard', '-z').decode().split('\0'))
    return {name: (root/name).read_bytes() for name in names if (generated(name) or name in dirty or name in untracked) and (root/name).is_file()}


def restore(root, baseline, evidence, runner=None):
    root = pathlib.Path(root)
    runner = runner or (lambda *args: subprocess.check_output(['git', *args], cwd=root))
    # Include new/deleted tracked paths and any unexpected untracked source, not ignored outputs.
    changed = runner('diff', '--name-only', 'HEAD', '-z').decode().split('\0')
    untracked = runner('ls-files', '--others', '--exclude-standard', '-z').decode().split('\0')
    changes = []
    for name in filter(None, changed + untracked):
        before = baseline.get(name)
        after = (root/name).read_bytes() if (root/name).is_file() else None
        if before == after:
            continue
        changes.append((name, before, after))
    evidence = pathlib.Path(evidence)
    evidence.mkdir(parents=True, exist_ok=True)
    diff = runner('diff', '--binary', 'HEAD')
    (evidence/'generated.diff').write_bytes(diff)
    # Validate all changes first: a single unexpected change preserves everything.
    for name, before, after in changes:
        if before is None or after is None:
            raise ValueError('unexpected added/deleted build output; preserving source: ' + name)
        validate_change(name, before, after)
    for name, before, _ in changes:
        (root/name).write_bytes(before)
    (evidence/'restored.json').write_text(json.dumps({'files': [n for n, _, _ in changes], 'status': 'restored exact pre-build bytes'}, indent=2)+'\n')
