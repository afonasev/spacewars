"""Source-grounded Blender art study. Not native terrain, collision or gameplay QA.
Run: blender --background --python tools/render_moss_highland_concepts.py
"""
import bpy,json,math,random,sys
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'docs/moss-highland/visual-options'
d=json.loads((OUT/'web-source.json').read_text());N=64;CELL=2;HALF=64
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=32
scene.cycles.use_denoising=True;scene.render.resolution_x=1500;scene.render.resolution_y=1125;scene.render.resolution_percentage=100
scene.world.color=(.35,.4,.45);scene.view_settings.view_transform='AgX'
# Fixed world coordinates shared by original and proposed variants.
def node(tree,kind,location):
 n=tree.nodes.new(kind);n.location=location;return n
def material(name,color,texture=None,wet=False):
 m=bpy.data.materials.new(name);m.use_nodes=True;t=m.node_tree;t.nodes.clear();bs=node(t,'ShaderNodeBsdfPrincipled',(240,0));output=node(t,'ShaderNodeOutputMaterial',(550,0));t.links.new(bs.outputs['BSDF'],output.inputs['Surface']);bs.inputs['Roughness'].default_value=.92
 texcoord=node(t,'ShaderNodeTexCoord',(-900,0));sep=node(t,'ShaderNodeVectorMath',(-710,0));sep.operation='SCALE';sep.inputs[3].default_value=.045;t.links.new(texcoord.outputs['Object'],sep.inputs[0])
 noise=node(t,'ShaderNodeTexNoise',(-490,-210));noise.inputs['Scale'].default_value=.085 if 'moss-ground' in name else .42;noise.inputs['Detail'].default_value=4;t.links.new(texcoord.outputs['Object'],noise.inputs['Vector'])
 ramp=node(t,'ShaderNodeValToRGB',(-260,-180));ramp.color_ramp.elements[0].position=.22;ramp.color_ramp.elements[0].color=((.17,.115,.045,1) if 'moss-ground' in name and not name.startswith('source') else (*[c*.60 for c in color],1));ramp.color_ramp.elements[1].position=.8;ramp.color_ramp.elements[1].color=(*[min(c*1.30,1) for c in color],1);t.links.new(noise.outputs['Fac'],ramp.inputs[0])
 if texture:
  image=node(t,'ShaderNodeTexImage',(-470,50));image.image=bpy.data.images.load(str(OUT/'source-textures'/texture),check_existing=True);t.links.new(sep.outputs['Vector'],image.inputs['Vector'])
  mix=node(t,'ShaderNodeMixRGB',(-30,70));mix.blend_type='MULTIPLY';mix.inputs[0].default_value=.65;t.links.new(ramp.outputs['Color'],mix.inputs[1]);t.links.new(image.outputs['Color'],mix.inputs[2]);t.links.new(mix.outputs[0],bs.inputs['Base Color'])
 else:t.links.new(ramp.outputs['Color'],bs.inputs['Base Color'])
 bump=node(t,'ShaderNodeBump',(-30,-120));bump.inputs['Strength'].default_value=.18;bump.inputs['Distance'].default_value=.09;t.links.new(noise.outputs['Fac'],bump.inputs['Height']);t.links.new(bump.outputs['Normal'],bs.inputs['Normal'])
 return m
# Full cell extrusion reproduces the source's height contours; heights are one metre per level.
vertices=[];faces=[];roles=[]
for z in range(N):
 for x in range(N):
  c=d['cells'][z*N+x];h=c['level'];xx=x*2-HALF;yy=z*2-HALF;idx=len(vertices)
  vertices.extend([(xx,yy,-.7),(xx+2,yy,-.7),(xx+2,yy+2,-.7),(xx,yy+2,-.7),(xx,yy,h),(xx+2,yy,h),(xx+2,yy+2,h),(xx,yy+2,h)])
  faces.append((idx+4,idx+5,idx+6,idx+7));roles.append(c['material'])
  # Only exposed vertical faces; no redundant internal grid walls.
  for dx,dz,ids in [(0,-1,(0,1,5,4)),(1,0,(1,2,6,5)),(0,1,(2,3,7,6)),(-1,0,(3,0,4,7))]:
   neighbor=d['cells'][(z+dz)*N+x+dx]['level'] if 0<=x+dx<N and 0<=z+dz<N else -.7
   if neighbor<h:
    side=len(vertices);a=vertices[idx+ids[2]];b=vertices[idx+ids[3]]
    vertices.extend([(a[0],a[1],neighbor),(b[0],b[1],neighbor),b,a]);faces.append(tuple(range(side,side+4)));roles.append('mountain.base-dark')
mesh=bpy.data.meshes.new('Original 128m authored highland');mesh.from_pydata(vertices,[],faces);mesh.update();ground=bpy.data.objects.new('Source terrain',mesh);scene.collection.objects.link(ground)
# Smooth ramps over the central 1m rise, exactly the source's four 8m-wide entrances.
ramps=[];half_ramp=.5/math.tan(math.radians(22))
for axis in ['x','y']:
 for side in [-1,1]:
  lo=12-half_ramp;hi=12+half_ramp
  if axis=='x':vs=[(side*lo,-4,1),(side*hi,-4,0),(side*hi,4,0),(side*lo,4,1)]
  else:vs=[(-4,side*lo,1),(-4,side*hi,0),(4,side*hi,0),(4,side*lo,1)]
  if side*(1 if axis=='x' else -1)<0:vs.reverse()
  me=bpy.data.meshes.new('Ramp');me.from_pydata([(a,b,c+.006) for a,b,c in vs],[],[(0,1,2,3)]);me.update();ob=bpy.data.objects.new('Source ramp',me);scene.collection.objects.link(ob);ramps.append(ob)
# Mark actual sites as abstract low pads; no invented final buildings.
site_objects=[]
site_colors={'start':(.24,.47,.52),'mine':(.65,.47,.15),'outpost':(.45,.49,.48)}
site_mats={kind:material(kind+' location',color) for kind,color in site_colors.items()}
for site in d['sites']:
 x,y=site['x'],site['z'];cell=d['cells'][int((y+64)//2)*64+int((x+64)//2)];h=cell['level'];radius=3.2 if site['kind']=='start' else 2.1
 bpy.ops.mesh.primitive_cylinder_add(vertices=8,radius=radius,depth=.18,location=(x,y,h+.10));pad=bpy.context.object;pad.name='Site '+site['id'];pad.data.materials.append(site_mats[site['kind']]);site_objects.append(pad)
 bpy.ops.mesh.primitive_cube_add(size=1,location=(x,y,h+.8));ob=bpy.context.object;ob.scale=(1.3,1.3,.7) if site['kind']=='start' else (.7,.7,.7);ob.data.materials.append(site_mats[site['kind']]);site_objects.append(ob)
# Camera/sun identical across alternatives; renders don't depend on desktop/input/audio.
bpy.ops.object.light_add(type='SUN',location=(-35,-60,100));sun=bpy.context.object;sun.rotation_euler=(math.radians(35),math.radians(-22),math.radians(-25));sun.data.energy=2.4;sun.data.angle=math.radians(12)
bpy.ops.object.camera_add(location=(105,-136,135));camera=bpy.context.object;camera.rotation_euler=(Vector((0,0,0))-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=181;scene.camera=camera
# Sparse raised-rock pockets, never on the level-0 routes or near site reserves.
rng=random.Random(7108);details=[]
for z in range(1,63):
 for x in range(1,63):
  c=d['cells'][z*64+x]
  if c['level']<2 or rng.random()>.24:continue
  px=x*2-63+rng.uniform(-.65,.65);py=z*2-63+rng.uniform(-.65,.65)
  if any((px-s['x'])**2+(py-s['z'])**2<13**2 for s in d['sites']):continue
  bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1,radius=1,location=(px,py,c['level']+.12));ob=bpy.context.object;ob.name='Rock pocket';ob.scale=(rng.uniform(.25,.9),rng.uniform(.25,.85),rng.uniform(.18,.6));ob.rotation_euler.z=rng.random()*6.28;details.append(ob)

def natural_material(name,colors):
 # Continuous normalized role weights feather source material boundaries in world coordinates.
 m=bpy.data.materials.new(name+' continuous natural terrain');m.use_nodes=True;t=m.node_tree;t.nodes.clear()
 bs=node(t,'ShaderNodeBsdfPrincipled',(900,0));out=node(t,'ShaderNodeOutputMaterial',(1150,0));t.links.new(bs.outputs['BSDF'],out.inputs['Surface']);bs.inputs['Roughness'].default_value=.94
 coord=node(t,'ShaderNodeTexCoord',(-1200,500));scale=node(t,'ShaderNodeVectorMath',(-1010,550));scale.operation='SCALE';scale.inputs[3].default_value=1/128;t.links.new(coord.outputs['Object'],scale.inputs[0])
 offset=node(t,'ShaderNodeVectorMath',(-830,550));offset.operation='ADD';offset.inputs[1].default_value=(.5,.5,0);t.links.new(scale.outputs['Vector'],offset.inputs[0])
 image=node(t,'ShaderNodeTexImage',(-640,600));image.image=bpy.data.images.load(str(OUT/'natural-role-mask.png'),check_existing=True);image.image.colorspace_settings.name='Non-Color';image.image.alpha_mode='CHANNEL_PACKED';image.extension='EXTEND';t.links.new(offset.outputs['Vector'],image.inputs['Vector'])
 separate=node(t,'ShaderNodeSeparateColor',(-410,600));t.links.new(image.outputs['Color'],separate.inputs['Color']);weights=[separate.outputs['Red'],separate.outputs['Green'],separate.outputs['Blue'],image.outputs['Alpha']]
 total=None
 for w in weights:
  if total is None:total=w;continue
  add=node(t,'ShaderNodeMath',(-200,550));add.operation='ADD';t.links.new(total,add.inputs[0]);t.links.new(w,add.inputs[1]);total=add.outputs[0]
 acc=None
 for index,(label,tex) in enumerate([('moss-ground','moss-ground.png'),('mountain.base-dark','base-dark.png'),('mountain.base-mid','base-mid.png'),('metal-deck',None)]):
  base=material(name+' family '+label,colors[label],tex);base_bs=next(n for n in base.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
  # Copy the family's nodes and links to a group for portable shader construction.
  group=bpy.data.node_groups.new(name+' '+label,'ShaderNodeTree');group.interface.new_socket(name='Color',in_out='OUTPUT',socket_type='NodeSocketColor');go=group.nodes.new('NodeGroupOutput')
  mapping={}
  for n in base.node_tree.nodes:
   if n.type in ['BSDF_PRINCIPLED','OUTPUT_MATERIAL','BUMP']:continue
   nn=group.nodes.new(n.bl_idname);mapping[n]=nn
   if hasattr(n,'operation'):nn.operation=n.operation
   if hasattr(n,'blend_type'):nn.blend_type=n.blend_type
   if n.type=='TEX_IMAGE':nn.image=n.image
   if n.type=='VALTORGB':
    for a,b in zip(nn.color_ramp.elements,n.color_ramp.elements):a.position=b.position;a.color=b.color
   for a,b in zip(nn.inputs,n.inputs):
    if hasattr(b,'default_value'):a.default_value=b.default_value
  for link in base.node_tree.links:
   if link.from_node in mapping and link.to_node in mapping:group.links.new(mapping[link.from_node].outputs[link.from_socket.identifier],mapping[link.to_node].inputs[link.to_socket.identifier])
   elif link.to_node==base_bs and link.to_socket.name=='Base Color':group.links.new(mapping[link.from_node].outputs[link.from_socket.identifier],go.inputs['Color'])
  gn=node(t,'ShaderNodeGroup',(0,300-index*170));gn.node_tree=group
  divide=node(t,'ShaderNodeMath',(200,300-index*170));divide.operation='DIVIDE';t.links.new(weights[index],divide.inputs[0]);t.links.new(total,divide.inputs[1])
  mul=node(t,'ShaderNodeMixRGB',(370,300-index*170));mul.blend_type='MULTIPLY';mul.inputs[0].default_value=1;t.links.new(gn.outputs[0],mul.inputs[1]);t.links.new(divide.outputs[0],mul.inputs[2]);color=mul.outputs[0]
  if acc is None:acc=color
  else:
   add=node(t,'ShaderNodeMixRGB',(600,300-index*170));add.blend_type='ADD';add.inputs[0].default_value=1;t.links.new(acc,add.inputs[1]);t.links.new(color,add.inputs[2]);acc=add.outputs[0]
 t.links.new(acc,bs.inputs['Base Color'])
 noise=node(t,'ShaderNodeTexNoise',(400,-500));noise.inputs['Scale'].default_value=7;noise.inputs['Detail'].default_value=3;t.links.new(coord.outputs['Object'],noise.inputs['Vector'])
 bump=node(t,'ShaderNodeBump',(680,-440));bump.inputs['Strength'].default_value=.24;bump.inputs['Distance'].default_value=.045;t.links.new(noise.outputs['Fac'],bump.inputs['Height']);t.links.new(bump.outputs[0],bs.inputs['Normal'])
 return m

original_vertices=[v.co.copy() for v in mesh.vertices]
def render(name,colors,details_enabled,texture_enabled=True):
 for v,original in zip(mesh.vertices,original_vertices):
  v.co=original.copy()
  if details_enabled and original.z>=1:
   x=int(round((original.x+64)/2));y=int(round((original.y+64)/2));near=[];low=False
   for cx,cy in [(x-1,y-1),(x,y-1),(x-1,y),(x,y)]:
    if 0<=cx<64 and 0<=cy<64:
     if d['cells'][cy*64+cx]['level']>=2:near.append(Vector((cx*2-63,cy*2-63,original.z)))
     else:low=True
   if near and low:
    delta=sum(near,Vector())/len(near)-original
    if delta.length>.01:v.co+=delta.normalized()*.28
 mesh.update()
 ground.data.materials.clear();mats=[]
 for label,texture in [('moss-ground','moss-ground.png'),('mountain.base-dark','base-dark.png'),('mountain.base-mid','base-mid.png'),('metal-deck',None)]:
  mats.append(material(name+' '+label,colors[label],texture if texture_enabled else None));ground.data.materials.append(mats[-1])
 indices={'moss-ground':0,'mountain.base-dark':1,'mountain.base-mid':2,'metal-deck':3}
 for poly,role in zip(mesh.polygons,roles):poly.material_index=indices[role]
 if details_enabled:
  natural=natural_material(name,colors);ground.data.materials.append(natural)
  for poly in mesh.polygons:
   if poly.normal.z>.5:poly.material_index=4
 for ob in ramps:ob.data.materials.clear();ob.data.materials.append(mats[1])
 for ob in details:ob.hide_render=not details_enabled;ob.data.materials.clear();ob.data.materials.append(mats[2])
 camera.location=(105,-136,135);camera.rotation_euler=(Vector((0,0,0))-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=181
 scene.render.filepath=str(OUT/(name+'-overview.png'));bpy.ops.render.render(write_still=True)
 camera.location=(6,-62,36);camera.rotation_euler=(Vector((-18,-20,1))-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=32
 scene.render.filepath=str(OUT/(name+'-close.png'));bpy.ops.render.render(write_still=True)
if '--proposals-only' not in sys.argv:render('source',{'moss-ground':(.28,.36,.15),'mountain.base-dark':(.28,.29,.29),'mountain.base-mid':(.44,.43,.37),'metal-deck':(.33,.36,.37)},False)
render('wet-moss',{'moss-ground':(.29,.43,.15),'mountain.base-dark':(.38,.42,.4),'mountain.base-mid':(.57,.58,.49),'metal-deck':(.32,.35,.36)},True)
render('rocky-moss',{'moss-ground':(.37,.38,.19),'mountain.base-dark':(.44,.43,.38),'mountain.base-mid':(.62,.58,.46),'metal-deck':(.32,.35,.36)},True)
(OUT/'render.json').write_text(json.dumps({'renderer':'Blender Cycles','samples':32,'seed':7108,'topology':'original source cells, 1m levels','sites':'original 4 starts, 4 mines, 5 outposts','scope':'source-grounded art concepts; not Unity/gameplay acceptance','commands':'blender --background --python tools/render_moss_highland_concepts.py'},indent=2)+'\n')
