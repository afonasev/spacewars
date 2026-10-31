"""Run with Blender --background --python tools/tank_distant_geometry.py.

Reproduce the human-approved 0.5 concept from its unchanged full source.
Only meshes with >=100 triangles are simplified, exactly as in the preview.
"""
import hashlib
import json
from pathlib import Path
import bpy

root = Path(__file__).resolve().parents[1]
source = root / 'unity/Assets/Spacewars/Content/StyleA/tankHybrid.glb'
output = source.with_name('tankHybridDistant.glb')
expected_source = '0f98a2982baa964e953355e2d83d5a4d2bd975cf52991220feba97f9d8ab354f'
assert hashlib.sha256(source.read_bytes()).hexdigest() == expected_source, 'Re-review changed source'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(source))
bpy.ops.object.select_all(action='DESELECT')
before = sum(len(p.vertices)-2 for o in bpy.data.objects if o.type == 'MESH' for p in o.data.polygons)
seen = set()
for obj in list(bpy.data.objects):
    if obj.type != 'MESH' or obj.data.as_pointer() in seen:
        continue
    seen.add(obj.data.as_pointer())
    if sum(len(p.vertices)-2 for p in obj.data.polygons) < 100:
        continue
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.decimate(ratio=.5)
    bpy.ops.object.mode_set(mode='OBJECT')
    obj.select_set(False)
after = sum(len(p.vertices)-2 for o in bpy.data.objects if o.type == 'MESH' for p in o.data.polygons)
assert before == 51612 and after == 25802, (before, after)
bpy.ops.export_scene.gltf(filepath=str(output), export_format='GLB', export_texcoords=True,
    export_normals=True, export_materials='EXPORT', export_animations=False,
    export_skins=True, export_cameras=False, export_lights=False)
output.with_suffix('.generated.json').write_text(json.dumps({
    'generator': 'tools/tank_distant_geometry.py', 'blender': bpy.app.version_string,
    'source_sha256': expected_source, 'ratio_large_meshes': .5, 'source_triangles': before,
    'distant_triangles': after, 'sha256': hashlib.sha256(output.read_bytes()).hexdigest(),
    'approval': 'smooth-eight-player-400 Q1: distant only, full current model near',
}, indent=2)+'\n')
