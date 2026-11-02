#!/usr/bin/env python3
"""Consolidate exact generated Resources copies, preserving importer settings.

Only generated masks and LOD meshes qualify. No visual transforms/compression.
Run with Unity closed for this checkout. The alias manifest keeps authoring paths.
"""
import argparse
import hashlib
import json
import pathlib
import re
from collections import defaultdict

ROOT = pathlib.Path(__file__).resolve().parents[1]
STYLE = 'Assets/Spacewars/Content/Resources/StyleA/'
MANIFEST = 'Assets/Spacewars/Editor/ExactResourceAliases.json'
GUID = re.compile(rb'^guid: ([0-9a-f]{32})$', re.M)
REFERENCE = re.compile(rb'guid: ([0-9a-f]{32})')


def digest(data):
    return hashlib.sha256(data).hexdigest()


def importer(data):
    return GUID.sub(b'guid: <identity>', data)


def consolidate(root, apply=False):
    unity = root / 'unity'
    folder = unity / STYLE
    candidates = sorted(list(folder.glob('*-mask.png')) + list((folder / 'Lod').rglob('mesh-*.asset')))
    groups = defaultdict(list)
    for path in candidates:
        meta = pathlib.Path(str(path) + '.meta').read_bytes()
        groups[(digest(path.read_bytes()), importer(meta))].append(path)
    aliases = []
    for (sha, _), paths in groups.items():
        for path in paths[1:]:
            canonical = paths[0]
            aliases.append(dict(source=str(path.relative_to(unity)), target=str(canonical.relative_to(unity)),
                                sha256=sha, bytes=path.stat().st_size,
                                importer_sha256=digest(importer(pathlib.Path(str(path)+'.meta').read_bytes())),
                                source_guid=GUID.search(pathlib.Path(str(path)+'.meta').read_bytes())[1].decode(),
                                target_guid=GUID.search(pathlib.Path(str(canonical)+'.meta').read_bytes())[1].decode()))
    replacements = {a['source_guid'].encode(): a['target_guid'].encode() for a in aliases}
    deleted = {unity/a['source'] for a in aliases}
    deleted.update(pathlib.Path(str(p)+'.meta') for p in list(deleted))
    changes = []
    for path in (unity / 'Assets').rglob('*'):
        if path in deleted or path.suffix not in ('.asset', '.prefab', '.mat', '.unity', '.meta'):
            continue
        data = path.read_bytes()
        # Preserve binary files; all modified references must be Unity YAML.
        if path.suffix != '.meta' and not data.startswith(b'%YAML'):
            continue
        updated = REFERENCE.sub(lambda m: b'guid: '+replacements.get(m[1], m[1]), data)
        if updated != data:
            changes.append(str(path.relative_to(unity)))
            if apply:
                path.write_bytes(updated)
    if apply and aliases:
        manifest = unity / MANIFEST
        prior = json.loads(manifest.read_text())['aliases'] if manifest.exists() else []
        manifest.write_text(json.dumps({'aliases': prior + aliases}, indent=2)+'\n')
        for alias in aliases:
            path = unity / alias['source']
            path.unlink()
            pathlib.Path(str(path)+'.meta').unlink()
    return dict(removed_files=len(aliases), saved_source_bytes=sum(a['bytes'] for a in aliases),
                changed_references=changes, aliases=aliases)


def verify(root):
    unity = root / 'unity'
    aliases = json.loads((unity / MANIFEST).read_text())['aliases']
    removed = set()
    for a in aliases:
        source, target = unity/a['source'], unity/a['target']
        assert not source.exists(), a['source']
        assert digest(target.read_bytes()) == a['sha256'], a['target']
        meta = pathlib.Path(str(target)+'.meta').read_bytes()
        assert digest(importer(meta)) == a['importer_sha256'], a['target']+' importer'
        assert GUID.search(meta)[1].decode() == a['target_guid']
        removed.add(a['source_guid'].encode())
    for path in (unity/'Assets').rglob('*'):
        if path.suffix in ('.asset', '.prefab', '.mat', '.unity', '.meta'):
            assert not removed.intersection(REFERENCE.findall(path.read_bytes())), str(path)
    return {'verified_aliases': len(aliases), 'saved_source_bytes': sum(a['bytes'] for a in aliases)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--verify', action='store_true')
    args = parser.parse_args()
    print(json.dumps(verify(ROOT) if args.verify else consolidate(ROOT, args.apply), indent=2))


if __name__ == '__main__':
    main()
