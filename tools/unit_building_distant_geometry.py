"""Reproducible REVIEW candidates, not a runtime prefab substitution.

Blender 5.2.1: --background --python tools/unit_building_distant_geometry.py --
  --output design/unit-building-lod/candidates --renders .local/unit-building-lod/renders
The source registry pins the active source assets. The original near assets are
never written. Small meshes retain their full geometry, as in the tank workflow.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import struct
import sys

import bpy
import bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[1]
REGISTRY = ROOT / 'design/unit-building-lod/sources.json'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def glb(path):
    data = path.read_bytes()
    assert data[:4] == b'glTF' and struct.unpack_from('<I', data, 4)[0] == 2
    size, kind = struct.unpack_from('<II', data, 12)
    assert kind == 0x4E4F534A
    return json.loads(data[20:20 + size])


def metrics(path):
    doc = glb(path)
    primitives = [p for m in doc['meshes'] for p in m['primitives']]
    assert all(p.get('mode', 4) == 4 for p in primitives)
    return dict(triangles=sum(doc['accessors'][p['indices']]['count'] // 3 for p in primitives),
                vertices=sum(doc['accessors'][p['attributes']['POSITION']]['count'] for p in primitives),
                meshes=len(doc['meshes']), primitives=len(primitives),
                materials=len(doc.get('materials', [])), skins=len(doc.get('skins', [])))


def restore_material_role_names(source, output):
    # glTF permits duplicate material names; Blender datablocks do not. Resolve
    # through each unchanged node/primitive slot, never by stripping a suffix.
    original, exported = glb(source), glb(output)
    nodes = {n['name']: n for n in original['nodes']}
    mapping = {}
    for node in exported['nodes']:
        if 'mesh' not in node:
            continue
        a = original['meshes'][nodes[node['name']]['mesh']]['primitives']
        b = exported['meshes'][node['mesh']]['primitives']
        assert len(a) == len(b), 'Material primitive count changed'
        for near, far in zip(a, b):
            role = original['materials'][near['material']]['name']
            index = far['material']
            assert index not in mapping or mapping[index] == role
            mapping[index] = role
    assert len(mapping) == len(exported['materials'])
    for index, role in mapping.items():
        exported['materials'][index]['name'] = role
    data = output.read_bytes()
    old_size = struct.unpack_from('<I', data, 12)[0]
    chunk = json.dumps(exported, separators=(',', ':'), ensure_ascii=False).encode('utf-8')
    chunk += b' ' * (-len(chunk) % 4)
    tail = data[20 + old_size:]
    output.write_bytes(struct.pack('<4sIIII', b'glTF', 2, 20 + len(chunk) + len(tail), len(chunk), 0x4E4F534A) + chunk + tail)


def geometry_triangles():
    return sum(len(p.vertices) - 2 for o in bpy.data.objects if o.type == 'MESH' for p in o.data.polygons)


def snapshot():
    # Names, parent frames, material slots and sockets are presentation contracts.
    return {o.name: dict(parent=o.parent.name if o.parent else None,
                        matrix=[v for row in o.matrix_local for v in row],
                        materials=[m.name if m else None for m in o.data.materials] if o.type == 'MESH' else [])
            for o in bpy.data.objects}


def restore_surface_normals(mesh, source):
    """Reproject per corner with target-face orientation to keep hard seams.

    Nearest vertex/loop transfer can select a bevel normal on a broad panel at
    the same position. Query source triangles and resolve those surface ties
    by the destination geometric face, then interpolate its original normals.
    Position/UV/topology stay untouched.
    """
    source.calc_loop_triangles()
    triangles = list(source.loop_triangles)
    corners = [n.vector.copy() for n in source.corner_normals]
    tree = BVHTree.FromPolygons([v.co for v in source.vertices],
                               [t.vertices for t in triangles], all_triangles=True)
    extent = max(max(v.co[i] for v in source.vertices) - min(v.co[i] for v in source.vertices) for i in range(3))
    tolerance = max(extent * .05, 1e-6)
    normals = [None] * len(mesh.loops)
    for polygon in mesh.polygons:
        for loop_index in polygon.loop_indices:
            point = mesh.vertices[mesh.loops[loop_index].vertex_index].co
            nearest = tree.find_nearest(point)
            options = tree.find_nearest_range(point, nearest[3] + tolerance)
            hit, _, index, _ = min(options, key=lambda r: r[3] / tolerance + 2 * (1 - r[1].dot(polygon.normal)))
            triangle = triangles[index]
            a, b, c = [source.vertices[v].co for v in triangle.vertices]
            v0, v1, v2 = b - a, c - a, hit - a
            d00, d01, d11 = v0.dot(v0), v0.dot(v1), v1.dot(v1)
            denominator = d00 * d11 - d01 * d01
            if abs(denominator) < 1e-20:
                weights = (1, 0, 0)
            else:
                v = (d11 * v2.dot(v0) - d01 * v2.dot(v1)) / denominator
                w = (d00 * v2.dot(v1) - d01 * v2.dot(v0)) / denominator
                weights = (1 - v - w, v, w)
            normal = sum((corners[i] * w for i, w in zip(triangle.loops, weights)), Vector())
            normals[loop_index] = normal.normalized()
    mesh.normals_split_custom_set(normals)


def decimate(preserve, ratio=.5, minimum=100, weld=False):
    seen = set()
    for obj in list(bpy.data.objects):
        if obj.type != 'MESH' or obj.data.as_pointer() in seen:
            continue
        seen.add(obj.data.as_pointer())
        if obj.name in preserve:
            continue
        if sum(len(p.vertices) - 2 for p in obj.data.polygons) < minimum:
            continue
        # Keep the authored corner normals before edit-mode weld/collapse.
        # glTF hard edges are split vertices; merging them otherwise averages
        # broad armor panels and creates a wet/grey surface under unchanged light.
        surface = obj.copy()
        surface.data = obj.data.copy()
        bpy.context.collection.objects.link(surface)
        low = [min(v.co[i] for v in obj.data.vertices) for i in range(3)]
        high = [max(v.co[i] for v in obj.data.vertices) for i in range(3)]
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        if weld:
            # GLB splits vertices along hard-normal/UV seams. Aggressive collapse
            # on those disconnected faces makes floating shards. Reconnect only
            # coincident positions; per-loop UV data remains on the mesh.
            bpy.ops.mesh.remove_doubles(threshold=.000001)
            # Each disconnected solid keeps a minimum surface budget. A global
            # ratio may erase a small solid while spending triangles on a larger
            # neighbor in the same material mesh (notably the science building).
            bm = bmesh.from_edit_mesh(obj.data)
            bm.verts.index_update()
            tag = bm.verts.layers.int.new('lod_island')
            remaining = set(bm.verts)
            counts = []
            while remaining:
                island = len(counts) + 1
                first = min(remaining, key=lambda v: v.index)
                remaining.remove(first)
                stack = [first]
                points = []
                while stack:
                    vertex = stack.pop()
                    points.append(vertex)
                    vertex[tag] = island
                    for edge in vertex.link_edges:
                        other = edge.other_vert(vertex)
                        if other in remaining:
                            remaining.remove(other)
                            stack.append(other)
                faces = {face for vertex in points for face in vertex.link_faces}
                counts.append(sum(len(face.verts) - 2 for face in faces))
            bmesh.update_edit_mesh(obj.data)
            for island, count in enumerate(counts, 1):
                if count < minimum:
                    continue
                bpy.ops.mesh.select_all(action='DESELECT')
                bm = bmesh.from_edit_mesh(obj.data)
                tag = bm.verts.layers.int['lod_island']
                for vertex in bm.verts:
                    vertex.select_set(vertex[tag] == island)
                bm.select_flush(True)
                bmesh.update_edit_mesh(obj.data)
                bpy.ops.mesh.decimate(ratio=max(ratio, min(1, 12 / count)))
            bm = bmesh.from_edit_mesh(obj.data)
            bm.verts.layers.int.remove(bm.verts.layers.int['lod_island'])
            bmesh.update_edit_mesh(obj.data)
        else:
            bpy.ops.mesh.decimate(ratio=ratio)
        bpy.ops.object.mode_set(mode='OBJECT')
        if weld:
            # A coarse approximation must fit the unchanged source culling box.
            # QEM may otherwise move a boundary vertex outside the source envelope.
            for vertex in obj.data.vertices:
                for i in range(3):
                    vertex.co[i] = max(low[i], min(high[i], vertex.co[i]))
        restore_surface_normals(obj.data, surface.data)
        mesh = surface.data
        bpy.data.objects.remove(surface, do_unlink=True)
        bpy.data.meshes.remove(mesh)
        obj.select_set(False)


def set_hawk_variant(upgraded):
    # The production prefab toggles these exact groups. Preserve both in GLB;
    # render them separately so overlapping weapons cannot hide differences.
    for obj in bpy.data.objects:
        parent = obj
        while parent:
            if parent.name in ('baseOnly', 'upgradedOnly'):
                obj.hide_render = (parent.name == 'upgradedOnly') != upgraded
                break
            parent = parent.parent


def setup_render():
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 24
    scene.cycles.use_denoising = True
    scene.render.resolution_x = 480
    scene.render.resolution_y = 400
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.world = bpy.data.worlds.new('LOD review world')
    scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value = (.12, .16, .20, 1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value = .55
    scene.view_settings.view_transform = 'AgX'
    # Diagnostic owner paint only, after exporting unchanged material roles.
    # Unity's actual finish/lighting still requires separate runtime verification.
    for material in bpy.data.materials:
        if material.name == 'team-primary' and material.use_nodes:
            shader = material.node_tree.nodes.get('Principled BSDF')
            if shader:
                shader.inputs['Base Color'].default_value = (.19, .72, .77, 1)
    points = [o.matrix_world @ Vector(p) for o in bpy.data.objects if o.type == 'MESH' for p in o.bound_box]
    low = Vector([min(v[i] for v in points) for i in range(3)])
    high = Vector([max(v[i] for v in points) for i in range(3)])
    center = (low + high) / 2
    extent = max(high - low)
    camera_data = bpy.data.cameras.new('LOD review camera')
    camera = bpy.data.objects.new('LOD review camera', camera_data)
    scene.collection.objects.link(camera)
    camera.location = center + Vector((1.2, -1.65, 1.25)) * extent
    camera.rotation_euler = (center - camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera_data.type = 'ORTHO'
    camera_data.ortho_scale = extent * 1.65
    scene.camera = camera
    for name, position, power, size in [('Key', (1, -2, 4), 1200, 4), ('Fill', (-3, 0, 2), 700, 5)]:
        data = bpy.data.lights.new(name, 'AREA')
        data.energy = power * extent * extent
        data.shape = 'DISK'
        data.size = size * extent
        light = bpy.data.objects.new(name, data)
        scene.collection.objects.link(light)
        light.location = center + Vector(position) * extent
        light.rotation_euler = (center - light.location).to_track_quat('-Z', 'Y').to_euler()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, default=ROOT / 'design/unit-building-lod/candidates')
    parser.add_argument('--renders', type=Path)
    parser.add_argument('--keys', nargs='*')
    parser.add_argument('--registry', type=Path, default=REGISTRY)
    parser.add_argument('--ratio', type=float, default=.5)
    parser.add_argument('--minimum', type=int, default=100)
    parser.add_argument('--weld', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    assert bpy.app.version_string == '5.2.1 LTS', bpy.app.version_string
    assert 0 < args.ratio < 1 and args.minimum >= 20
    args.output.mkdir(parents=True, exist_ok=True)
    if args.renders:
        args.renders.mkdir(parents=True, exist_ok=True)
    registry = json.loads(args.registry.read_text())['assets']
    assert not args.keys or set(args.keys) <= {a['key'] for a in registry}, 'Unknown source key'
    manifest = args.output / 'manifest.json'
    previous = json.loads(manifest.read_text())['assets'] if args.keys and manifest.exists() else []
    rows = {row['key']: row for row in previous}
    for asset in registry:
        key = asset['key']
        if args.keys and key not in args.keys:
            continue
        source = ROOT / asset['source']
        assert sha(source) == asset['source_sha256'], 'Source changed; review ' + key
        assert not glb(source).get('animations'), 'Animated source needs explicit clip preservation'
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.gltf(filepath=str(source))
        bpy.ops.object.select_all(action='DESELECT')
        before = snapshot()
        full_count = geometry_triangles()
        original_meshes = {o.name: o.data.copy() for o in bpy.data.objects if o.type == 'MESH'}
        preserved = asset.get('preserve_full_mesh_nodes', [])
        assert set(preserved) <= set(original_meshes), 'Missing protected source assembly'
        ratio = asset.get('ratio_override', args.ratio)
        decimate(preserved, ratio, args.minimum, args.weld)
        assert before == snapshot(), 'Hierarchy/material/frame contract changed: ' + key
        distant_count = geometry_triangles()
        assert 0 < distant_count < full_count
        output = args.output / (key + '-distant.glb')
        bpy.ops.export_scene.gltf(filepath=str(output), export_format='GLB', export_texcoords=True,
                                 export_normals=True, export_materials='EXPORT', export_animations=False,
                                 export_skins=True, export_cameras=False, export_lights=False)
        restore_material_role_names(source, output)
        row = dict(key=key, source=asset['source'], source_sha256=sha(source),
                   candidate=output.name, candidate_sha256=sha(output),
                   before=metrics(source), after=metrics(output),
                   source_object_triangles=full_count, candidate_object_triangles=distant_count,
                   blender=bpy.app.version_string, ratio=ratio, minimum_mesh_triangles=args.minimum,
                   preserve_full_mesh_nodes=preserved,
                   corner_normals='SOURCE_TRIANGLE_INTERPOLATED_FACE_AWARE', uv='SOURCE_CORNER_UV_RETAINED_BY_COLLAPSE',
                   weld_before_decimation=args.weld, constrain_to_source_bounds=args.weld,
                   preserve_disconnected_solids=args.weld,
                   hierarchy_material_frames_unchanged=True, status='REVIEW_CANDIDATE_NOT_RUNTIME')
        assert row['before']['materials'] == row['after']['materials']
        (args.output / (key + '.json')).write_text(json.dumps(row, indent=2) + '\n')
        if args.renders:
            reduced_meshes = {o.name: o.data for o in bpy.data.objects if o.type == 'MESH'}
            setup_render()  # One camera from unchanged bounds for both representations.
            for variant in (['base', 'upgraded'] if key == 'hawk' else ['default']):
                if key == 'hawk':
                    set_hawk_variant(variant == 'upgraded')
                for detail, meshes in [('full', original_meshes), ('distant', reduced_meshes)]:
                    for name, mesh in meshes.items():
                        bpy.data.objects[name].data = mesh
                    bpy.context.scene.render.filepath = str(args.renders / f'{key}-{variant}-{detail}.png')
                    bpy.ops.render.render(write_still=True)
        rows[key] = row
        print('DISTANT_CANDIDATE', key, full_count, distant_count, flush=True)
    manifest.write_text(json.dumps(dict(classification='BLENDER_REVIEW_CONCEPT_NOT_UNITY_RUNTIME',
                                      assets=[rows[a['key']] for a in registry if a['key'] in rows]), indent=2) + '\n')


if __name__ == '__main__':
    main()
