import json
from pathlib import Path
import struct
import tempfile
import unittest

from tools.building_model_geometry import compare, matrix, normal


def fixture(path, *, material='steel', normal_y=1., translation=0.):
    payload=struct.pack('<9f',0,0,0,1,0,0,0,0,1)+struct.pack('<9f',0,normal_y,0,0,normal_y,0,0,normal_y,0)+struct.pack('<6f',0,0,1,0,0,1)+struct.pack('<3H',0,1,2)
    data={'asset':{'version':'2.0'},'scene':0,'scenes':[{'nodes':[0]}],'nodes':[{'name':'modelRoot','children':[1]},{'name':'surface','mesh':0,'translation':[translation,0,0]}],'meshes':[{'primitives':[{'attributes':{'POSITION':0,'NORMAL':1,'TEXCOORD_0':2},'indices':3,'material':0}]}],'materials':[{'name':material}],'buffers':[{'byteLength':len(payload)}],'bufferViews':[{'buffer':0,'byteOffset':offset,'byteLength':length} for offset,length in [(0,36),(36,36),(72,24),(96,6)]],'accessors':[{'bufferView':i,'componentType':5126 if i<3 else 5123,'type':kind,'count':3} for i,kind in enumerate(['VEC3','VEC3','VEC2','SCALAR'])]}
    js=json.dumps(data).encode();js+=b' '*((-len(js))%4);payload+=b'\0'*((-len(payload))%4)
    path.write_bytes(struct.pack('<III',0x46546c67,2,12+8+len(js)+8+len(payload))+struct.pack('<II',len(js),0x4e4f534a)+js+struct.pack('<II',len(payload),0x004e4942)+payload)


class BuildingModelGeometryTests(unittest.TestCase):
    def test_unchanged_surface_passes_and_negative_control_runs(self):
        with tempfile.TemporaryDirectory() as folder:
            a=Path(folder)/'a.glb';fixture(a)
            self.assertEqual('PASS',compare(a,a)['negative_control_missing_triangle'])

    def test_same_triangle_count_does_not_hide_material_or_position_change(self):
        with tempfile.TemporaryDirectory() as folder:
            a=Path(folder)/'a.glb';b=Path(folder)/'b.glb';fixture(a)
            for options in [{'material':'paint'},{'translation':.1},{'normal_y':-1.}]:
                fixture(b,**options)
                with self.assertRaisesRegex(AssertionError,'Surface mismatch'):compare(a,b)

    def test_normal_uses_inverse_transpose_for_nonuniform_scale(self):
        transform=matrix({'scale':[2,1,3]});n=normal(transform,(1,1,0))
        # Transformed tangent (2,-1,0) must remain perpendicular.
        self.assertAlmostEqual(0,2*n[0]-n[1],places=12)

    def test_root_transform_is_a_preserved_articulation_contract(self):
        # Named pivot binds are checked separately from their local surface frame.
        with tempfile.TemporaryDirectory() as folder:
            a=Path(folder)/'a.glb';b=Path(folder)/'b.glb';fixture(a);fixture(b)
            raw=b.read_bytes();size=struct.unpack_from('<I',raw,12)[0];data=json.loads(raw[20:20+size]);data['nodes'][0]['translation']=[1,0,0]
            js=json.dumps(data).encode();js+=b' '*((-len(js))%4);tail=raw[20+size:]
            b.write_bytes(struct.pack('<III',0x46546c67,2,20+len(js)+len(tail))+struct.pack('<II',len(js),0x4e4f534a)+js+tail)
            with self.assertRaisesRegex(AssertionError,'hierarchy/transform'):compare(a,b)
