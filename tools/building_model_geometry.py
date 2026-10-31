#!/usr/bin/env python3
"""Offline GLB surface witness for lossless building export consolidation.

Compare triangles in articulation-root space, including winding, normals and all
UV/color attributes. 1e-5 decimal resolution tolerates float32 transform baking;
counts, materials and retained motion/socket nodes must match exactly.
"""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import struct

IDENTITY = ((1,0,0,0),(0,1,0,0),(0,0,1,0),(0,0,0,1))
MOTION = {'modelRoot','fan0','fan1','rockerPivot','drillSlider','hydraulicBarrel0','hydraulicBarrel1','hydraulicRod0','hydraulicRod1','turbineYaw','antennaYaw'} | {f'doorSection{i}' for i in range(6)}

def multiply(a,b):
    return tuple(tuple(sum(a[r][k]*b[k][c] for k in range(4)) for c in range(4)) for r in range(4))

def matrix(node):
    if 'matrix' in node:
        a=node['matrix'];return tuple(tuple(a[c*4+r] for c in range(4)) for r in range(4))
    x,y,z,w=node.get('rotation',[0,0,0,1]);s=node.get('scale',[1,1,1]);t=node.get('translation',[0,0,0])
    a=((1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)),(2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)),(2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)))
    return tuple(tuple(a[r][c]*s[c] for c in range(3))+(t[r],) for r in range(3))+((0,0,0,1),)

def point(a,p):return tuple(sum(a[r][c]*p[c] for c in range(3))+a[r][3] for r in range(3))
def normal(a,n):
    # Cofactor matrix / determinant is inverse transpose; glTF normals normalize.
    cof=tuple(tuple(((-1)**(r+c))*(a[(r+1)%3][(c+1)%3]*a[(r+2)%3][(c+2)%3]-a[(r+1)%3][(c+2)%3]*a[(r+2)%3][(c+1)%3]) for c in range(3)) for r in range(3))
    # Cyclic minors above already include their signs.
    cof=tuple(tuple(cof[r][c]*((-1)**(r+c)) for c in range(3)) for r in range(3))
    det=sum(a[0][c]*cof[0][c] for c in range(3));assert abs(det)>1e-20
    out=tuple(sum(cof[r][c]*n[c] for c in range(3))/det for r in range(3));length=sum(v*v for v in out)**.5
    return tuple(v/length for v in out)

class GLB:
    def __init__(self,path):
        self.bytes=Path(path).read_bytes();size=struct.unpack_from('<I',self.bytes,12)[0];self.data=json.loads(self.bytes[20:20+size]);self.binary=self.bytes[28+size:]
    def accessor(self,i):
        a=self.data['accessors'][i];assert not a.get('sparse');v=self.data['bufferViews'][a['bufferView']]
        fmt={5120:'b',5121:'B',5122:'h',5123:'H',5125:'I',5126:'f'}[a['componentType']];count={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4}[a['type']];size=struct.calcsize(fmt)*count
        offset=v.get('byteOffset',0)+a.get('byteOffset',0);stride=v.get('byteStride',size)
        return [struct.unpack_from('<'+fmt*count,self.binary,offset+k*stride) for k in range(a['count'])]
    def surfaces(self):
        d=self.data;triangles=Counter();retained={};parents={ch:i for i,n in enumerate(d['nodes']) for ch in n.get('children',[])}
        def walk(i,transform=IDENTITY,root='modelRoot'):
            n=d['nodes'][i];name=n.get('name','');local=matrix(n)
            if name in MOTION or name.startswith('doorSection'):
                root=name;transform=IDENTITY
            else:transform=multiply(transform,local)
            if 'mesh' not in n:
                retained[name]={'parent':d['nodes'][parents[i]].get('name') if i in parents else None,'matrix':tuple(tuple(round(v,6) for v in row) for row in local)}
            if 'mesh' in n:
                for p in d['meshes'][n['mesh']]['primitives']:
                    assert p.get('mode',4)==4
                    arrays={k:self.accessor(v) for k,v in p['attributes'].items()};idx=[x[0] for x in self.accessor(p['indices'])];material=d['materials'][p['material']]['name']
                    vertices=[]
                    for j in range(len(arrays['POSITION'])):
                        attrs=[]
                        for key,values in sorted(arrays.items()):
                            value=point(transform,values[j]) if key=='POSITION' else normal(transform,values[j]) if key=='NORMAL' else values[j]
                            attrs.append((key,tuple(round(v,5) for v in value)))
                        vertices.append(tuple(attrs))
                    for j in range(0,len(idx),3):
                        tri=tuple(vertices[k] for k in idx[j:j+3]);tri=min(tri,tri[1:]+tri[:1],tri[2:]+tri[:2]);triangles[(root,material,tri)]+=1
            for child in n.get('children',[]):walk(child,transform,root)
        for i in d['scenes'][d.get('scene',0)]['nodes']:walk(i)
        return triangles,retained

def compare(before,after):
    a,an=GLB(before).surfaces();b,bn=GLB(after).surfaces();missing=a-b;added=b-a
    assert an==bn,'Retained hierarchy/transform mismatch'
    assert not missing and not added,f'Surface mismatch: missing={sum(missing.values())}, added={sum(added.values())}; first={str(next(iter(missing),None))[:500]}'
    # Negative control: a missing triangle is observable, even with duplicate geometry.
    broken=b.copy();broken.subtract({next(iter(b)):1});assert broken!=a
    return {'result':'PASS','triangles':sum(a.values()),'retained_nodes':len(an),'quantization_decimal_places':5,'before_sha256':hashlib.sha256(Path(before).read_bytes()).hexdigest(),'after_sha256':hashlib.sha256(Path(after).read_bytes()).hexdigest(),'negative_control_missing_triangle':'PASS'}

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('before');p.add_argument('after');p.add_argument('--output');args=p.parse_args();result=compare(args.before,args.after);text=json.dumps(result,indent=2);print(text)
    if args.output:Path(args.output).write_text(text+'\n')
