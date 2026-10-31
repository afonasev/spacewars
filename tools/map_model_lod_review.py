"""Offline Blender geometry candidates from the exact Unity runtime mesh export.

Review only: does not modify active prefabs or procedural generators. Protects
small disconnected solids and transfers source normals directly to the Unity
review output without calling Blender custom-normal setters.
"""
import argparse
import json
from pathlib import Path
import sys
import zipfile

import bpy
import bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree


def solid_count(row):
    keys, ids, parent = {}, [], []
    for vertex in row['vertices']:
        key = tuple(round(v, 6) for v in vertex)
        if key not in keys:
            keys[key] = len(parent)
            parent.append(len(parent))
        ids.append(keys[key])
    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i
    used = set()
    for indices in row['submeshes']:
        for i in range(0, len(indices), 3):
            triangle = [ids[v] for v in indices[i:i+3]]
            if len(set(triangle)) < 3:
                continue
            used.update(triangle)
            for v in triangle[1:]:
                parent[find(v)] = find(triangle[0])
    return len({find(i) for i in used})


def decimate_solid_groups(obj, ratio, row):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    remaining = set(bm.verts)
    protected = set()
    while remaining:
        first = remaining.pop()
        stack = [first]
        visited = {first}
        while stack:
            vertex = stack.pop()
            for edge in vertex.link_edges:
                other = edge.other_vert(vertex)
                if other in remaining:
                    remaining.remove(other)
                    visited.add(other)
                    stack.append(other)
        faces = {face for vertex in visited for face in vertex.link_faces}
        count = sum(len(face.verts) - 2 for face in faces)
        if count < 100:
            protected.update(visited)
    bm.verts.index_update()
    bm.verts.ensure_lookup_table()
    protected_indices = {v.index for v in protected}
    if protected_indices:
        small = bm.copy()
        small.verts.ensure_lookup_table()
        bmesh.ops.delete(small, geom=[v for v in small.verts if v.index not in protected_indices], context='VERTS')
        small_mesh = bpy.data.meshes.new('Exact small solids')
        small.to_mesh(small_mesh)
        small.free()
        for material in obj.data.materials:
            small_mesh.materials.append(material)
        small_obj = bpy.data.objects.new('Exact small solids', small_mesh)
        bpy.context.collection.objects.link(small_obj)
        bmesh.ops.delete(bm, geom=list(protected), context='VERTS')
    bm.to_mesh(obj.data)
    bm.free()
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    modifier = obj.modifiers.new('Review collapse', 'DECIMATE')
    modifier.ratio = ratio
    if len(obj.data.polygons) > 3:
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    else:
        obj.modifiers.remove(modifier)
    bpy.ops.object.select_all(action='SELECT')
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.join()
    mesh = obj.data
    points = [Vector(v) for v in row['vertices']]
    low = [min(v[i] for v in points) for i in range(3)]
    high = [max(v[i] for v in points) for i in range(3)]
    for v in mesh.vertices:
        for i in range(3):
            v.co[i] = max(low[i], min(high[i], v.co[i]))
    ts = [tuple(indices[i:i+3]) for indices in row['submeshes'] for i in range(0,len(indices),3)]
    ts = [t for t in ts if len(set(t)) == 3 and (points[t[1]]-points[t[0]]).cross(points[t[2]]-points[t[0]]).length_squared > 1e-20]
    tree = BVHTree.FromPolygons(points, ts, all_triangles=True)
    source_normals = [Vector(v) for v in row['normals']]
    normals = [None] * len(mesh.loops)
    for face in mesh.polygons:
        center_hit = tree.find_nearest(face.center)
        for index in face.loop_indices:
            point = mesh.vertices[mesh.loops[index].vertex_index].co
            hit = tree.find_nearest(point)
            # A face-center witness resolves bevel/panel normal ambiguity without
            # an unbounded range query on overlapping damaged geometry.
            position, _, ti, _ = center_hit if center_hit[1].dot(face.normal) > hit[1].dot(face.normal) + 1e-4 else hit
            triangle = ts[ti]
            a, b, c = [points[v] for v in triangle]
            v0, v1, v2 = b-a, c-a, position-a
            aa, ab, bb = v0.dot(v0), v0.dot(v1), v1.dot(v1)
            den = aa*bb-ab*ab
            if abs(den) < 1e-20:
                weights = (1, 0, 0)
            else:
                u = (bb*v2.dot(v0)-ab*v2.dot(v1))/den
                v = (aa*v2.dot(v1)-ab*v2.dot(v0))/den
                weights = [max(0, min(1, w)) for w in (1-u-v, u, v)]
                total = sum(weights)
                weights = [w/total for w in weights]
            normals[index] = sum((source_normals[i]*w for i,w in zip(triangle,weights)),Vector()).normalized() if source_normals else face.normal
    return normals


def build(row):
    mesh = bpy.data.meshes.new(row['hash'])
    # Deterministic position-index weld avoids Blender's quadratic merge on
    # damaged meshes. Corner UV/color seams stay attached to original faces.
    positions, remap, lookup = [], [], {}
    for position in row['vertices']:
        key = tuple(round(v, 6) for v in position)
        if key not in lookup:
            lookup[key] = len(positions)
            positions.append(position)
        remap.append(lookup[key])
    faces, source_corners, slots = [], [], []
    for slot, indices in enumerate(row['submeshes']):
        for i in range(0,len(indices),3):
            original = indices[i:i+3]
            triangle = [remap[v] for v in original]
            if len(set(triangle)) < 3:
                continue
            faces.append(triangle)
            source_corners.append(original)
            slots.append(slot)
    mesh.from_pydata(positions, [], faces)
    mesh.update()
    for slot in range(len(row['submeshes'])):
        mesh.materials.append(bpy.data.materials.new('slot-' + str(slot)))
    uv = mesh.uv_layers.new(name='UVMap') if row['uv'] else None
    colors = mesh.color_attributes.new(name='SourceColor', type='FLOAT_COLOR', domain='CORNER') if row['colors'] else None
    for face, original, slot in zip(mesh.polygons, source_corners, slots):
        face.material_index = slot
        for index, source_index in zip(face.loop_indices, original):
            if uv:
                uv.data[index].uv = row['uv'][source_index]
            if colors:
                colors.data[index].color = row['colors'][source_index]
    obj = bpy.data.objects.new('source', mesh)
    bpy.context.collection.objects.link(obj)
    return obj


def export(mesh, source, transferred_normals):
    mesh.calc_loop_triangles()
    normals = transferred_normals
    uv = mesh.uv_layers.active
    colors = mesh.color_attributes.get('SourceColor')
    vertices, ns, uvs, cs = [], [], [], []
    submeshes = [[] for _ in source['submeshes']]
    for triangle in mesh.loop_triangles:
        for index in triangle.loops:
            loop = mesh.loops[index]
            submeshes[triangle.material_index].append(len(vertices))
            vertices.append(list(mesh.vertices[loop.vertex_index].co))
            ns.append(list(normals[index]))
            if uv:
                uvs.append(list(uv.data[index].uv))
            if colors:
                cs.append(list(colors.data[index].color))
    return dict(vertices=vertices, normals=ns, uv=uvs, colors=cs,
                recalculateTangents=bool(source['tangents']), submeshes=submeshes)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path, required=True)
    parser.add_argument('--keys', nargs='*')
    parser.add_argument('--reuse-candidates', type=Path)
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    assert bpy.app.version_string == '5.2.1 LTS', bpy.app.version_string
    reuse = zipfile.ZipFile(args.reuse_candidates) if args.reuse_candidates else None
    reused_names = set(reuse.namelist()) if reuse else set()
    output = args.root / 'reduced'
    output.mkdir(exist_ok=True)
    archive_path = args.root / 'source-complete.zip'
    archive = zipfile.ZipFile(archive_path) if archive_path.exists() else None
    sources = {Path(name).stem: name for name in archive.namelist()} if archive else {}
    sources.update({p.stem: p for p in (args.root / 'source').glob('*.json')})
    for key, location in sorted(sources.items()):
        path = Path(key + '.json')
        if args.keys and key not in args.keys:
            continue
        row = json.loads(location.read_text() if isinstance(location, Path) else archive.read(location))
        for tier, ratio in [('normal', .5), ('far', .2)]:
            destination = output / (path.stem + '-' + tier + '.json')
            if destination.name in reused_names:
                continue
            if destination.exists():
                try:
                    if json.loads(destination.read_text()).get('method') == 'POSITION_INDEX_SOLIDS_FACE_WITNESS_V5':
                        continue
                except json.JSONDecodeError:
                    pass
            bpy.ops.wm.read_factory_settings(use_empty=True)
            obj = build(row)
            before = sum(len(p.vertices) - 2 for p in obj.data.polygons)
            transferred = decimate_solid_groups(obj, ratio, row)
            result = export(obj.data, row, transferred)
            source_solids, candidate_solids = solid_count(row), solid_count(result)
            reason = 'reduced'
            unsafe_count = None
            if candidate_solids < source_solids:
                # An open damaged solid can collapse completely. Retain the
                # safer existing representation; never weaken the guard.
                unsafe_count = candidate_solids
                normal_path = output / (path.stem + '-normal.json')
                safe = json.loads(normal_path.read_text()) if tier == 'far' and normal_path.exists() else row
                assert solid_count(safe) >= source_solids
                result = {key: safe[key] for key in ('vertices', 'normals', 'uv', 'colors', 'submeshes')}
                result['recalculateTangents'] = bool(row['tangents'])
                reason = 'retained-normal-to-preserve-solids' if safe is not row else 'retained-source-to-preserve-solids'
            after = sum(len(indices) // 3 for indices in result['submeshes'])
            assert 0 < after <= before, (path.stem, before, after)
            result.update(hash=path.stem, tier=tier, before=before, after=after,
                          blender=bpy.app.version_string, ratio=ratio,
                          status='REVIEW_ONLY_NOT_ACTIVE', method='POSITION_INDEX_SOLIDS_FACE_WITNESS_V5',
                          reason=reason if after < before else 'small-disconnected-solids-retained',
                          source_solids=source_solids, attempted_solids=candidate_solids,
                          unsafe_count=unsafe_count)
            temporary = destination.with_suffix('.json.tmp')
            temporary.write_text(json.dumps(result, separators=(',', ':')) + '\n')
            temporary.replace(destination)
            print('MAP_LOD_CANDIDATE', row['name'], tier, before, after, flush=True)


if __name__ == '__main__':
    main()
