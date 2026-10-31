"""Validate the generated GLB boundary against original source contracts, no Unity.

This does not qualify Unity import, packed track skins, animation or appearance.
"""
import hashlib
import json
import math
from pathlib import Path
import struct

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    data = path.read_bytes()
    assert data[:4] == b'glTF' and struct.unpack_from('<I', data, 8)[0] == len(data)
    size, kind = struct.unpack_from('<II', data, 12)
    assert kind == 0x4E4F534A
    return json.loads(data[20:20 + size])


def nodes(doc):
    parents = {child: parent['name'] for parent in doc['nodes'] for child in parent.get('children', [])}
    result = {}
    for i, node in enumerate(doc['nodes']):
        name = node['name']
        assert name not in result, 'Duplicate node name: ' + name
        result[name] = (node, parents.get(i))
    return result


def close(a, b):
    return len(a) == len(b) and all(math.isfinite(x) and math.isfinite(y) and abs(x - y) < 2e-5 for x, y in zip(a, b))


def roles(doc, node):
    if 'mesh' not in node:
        return None
    return [doc['materials'][p['material']]['name'] for p in doc['meshes'][node['mesh']]['primitives']]


def validate(source, candidate):
    a, b = nodes(source), nodes(candidate)
    assert a.keys() == b.keys(), 'Named node/socket set changed'
    assert not source.get('animations') and not candidate.get('animations')
    assert not source.get('skins') and not candidate.get('skins'), 'Skinned source requires its own binding validation'
    assert sorted(m['name'] for m in source['materials']) == sorted(m['name'] for m in candidate['materials'])
    for name, (original, parent) in a.items():
        reduced, new_parent = b[name]
        assert parent == new_parent, 'Parent changed: ' + name
        for field, default in [('translation', [0, 0, 0]), ('scale', [1, 1, 1]), ('matrix', [])]:
            assert close(original.get(field, default), reduced.get(field, default)), f'{name}/{field}'
        q = original.get('rotation', [0, 0, 0, 1]); r = reduced.get('rotation', [0, 0, 0, 1])
        assert close(q, r) or close(q, [-x for x in r]), 'Rotation changed: ' + name
        assert roles(source, original) == roles(candidate, reduced), 'Material slots changed: ' + name
        if 'mesh' in reduced:
            src = source['meshes'][original['mesh']]['primitives']
            dst = candidate['meshes'][reduced['mesh']]['primitives']
            for x, y in zip(src, dst):
                assert x.get('mode', 4) == y.get('mode', 4) == 4
                assert x['attributes'].keys() == y['attributes'].keys(), 'Vertex attribute channels changed: ' + name
                assert candidate['accessors'][y['indices']]['count'] > 0


def main():
    folder = ROOT / 'design/unit-building-lod/candidates'
    manifest = json.loads((folder / 'manifest.json').read_text())
    expected = json.loads((ROOT / 'design/unit-building-lod/sources.json').read_text())['assets']
    rows = manifest['assets']
    assert [x['key'] for x in rows] == [x['key'] for x in expected], 'Missing/duplicate candidate'
    total_before = total_after = 0
    for row, binding in zip(rows, expected):
        source = ROOT / row['source']; candidate = folder / row['candidate']
        assert hashlib.sha256(source.read_bytes()).hexdigest() == binding['source_sha256'] == row['source_sha256']
        assert hashlib.sha256(candidate.read_bytes()).hexdigest() == row['candidate_sha256']
        validate(read(source), read(candidate))
        original, reduced = read(source), read(candidate)
        near, far = nodes(original), nodes(reduced)
        for name in binding.get('preserve_full_mesh_nodes', []):
            a = original['meshes'][near[name][0]['mesh']]['primitives']
            b = reduced['meshes'][far[name][0]['mesh']]['primitives']
            assert [original['accessors'][p['indices']]['count'] for p in a] == [reduced['accessors'][p['indices']]['count'] for p in b], 'Protected assembly changed'
        assert 0 < row['after']['triangles'] < row['before']['triangles']
        total_before += row['before']['triangles']; total_after += row['after']['triangles']
        print('PASS', row['key'], row['before']['triangles'], '->', row['after']['triangles'])
    print(f'PASS {len(rows)} sources/candidates; {total_before} -> {total_after} source triangles')


if __name__ == '__main__':
    main()
