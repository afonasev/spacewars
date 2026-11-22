"""Bake the Sea of Silence district albedo (RGB) and hard-cover footprint (A).

World X runs east and Z north. Each pixel covers the same distance on both axes.
The lunar shader adds its repeatable rock grain, impacts and actual hill relief.
"""
from pathlib import Path
import math
import random

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / 'unity/Assets/Spacewars/Content/Resources/Environment/MirrorDistricts.png'
WIDTH, HEIGHT = 2048, 1024
SCALE = WIDTH / 448
rng = np.random.default_rng(21112026)
placement = random.Random(21112026)
x, z = np.meshgrid((np.arange(WIDTH) + .5) / SCALE - 224,
                   112 - (np.arange(HEIGHT) + .5) / SCALE)


def noise(cells):
    source = Image.fromarray(rng.integers(0, 256, (cells, cells*2), dtype=np.uint8))
    return np.asarray(source.resize((WIDTH, HEIGHT), Image.Resampling.BICUBIC)) / 255. - .5


def fade(lo, hi, v):
    t = np.clip((v-lo)/(hi-lo), 0, 1)
    return t*t*(3-2*t)


def mix(a, b, weight):
    return a + (b-a)*weight[..., None]


broad, fold, grit = noise(8), noise(32), noise(180)
warped_z = z + 9*broad + 4*np.sin(x/26)
side = fade(27, 129, abs(x) + 22*broad + 8*fold)
north = fade(27, 66, warped_z)
south = fade(26, 62, -warped_z)
west = x < 0
ground = np.zeros((HEIGHT, WIDTH, 3)) + (149, 146, 147)
# Warm fan, blue basalt and terracotta dust on the west; graphite, mauve and ochre on the east.
def banks(left, right):
    return np.where(west[..., None], left, right).astype(float)


district = mix(banks((110, 129, 152), (160, 136, 151)),
               banks((203, 191, 169), (103, 103, 116)), north)
district = mix(district, banks((174, 121, 99), (190, 160, 108)), south)
ground = mix(ground, district, side)

# Long mineral beds follow different directions on each bank. The fan's rays
# originate outside the playable corner, and the old crater has a broken rim.
rays = np.arctan2(z-107, x+219)
fan = np.cos(rays*31 + 1.5*fold)
flow = np.sin(z*.36 + np.sin(x*.056)*4 + np.sin(x*.13+z*.04) + 3*fold)
beds = np.sin((z-.35*x)*.44 + 2.4*broad + 2.5*fold)
ring = np.hypot((x-132)/1.18, z+2)
ghost = np.exp(-((ring-29-4*broad)/1.6)**2) * fade(-.36,.22,fold)
detail = np.where(west, north*fan*7+(1-north)*flow*8,
                  north*np.maximum(0,beds-.58)*29 + south*beds*8 + (1-north-south)*ghost*20)
# Narrow streaks run inside the broad beds, breaking up where later dust buried them.
streaks = np.sin(z*1.13 + np.sin(x*.058)*11 + np.sin(x*.135+z*.06)*2.4 + 9*fold + grit)
west_lamina = fade(.55,.98,streaks)*fade(-.2,.22,fold)*(1-north)
east_lamina = fade(.72,.99,np.sin((z-.35*x)*.92 + 6*broad+6*fold+grit))
detail += np.where(west,west_lamina*19, east_lamina*(14*north+15*south))
ground += (side*detail + broad*19 + fold*10 + grit*5)[..., None]

# Indexed pavement makes a single footprint for rendering, minimap and vehicle trails.
materials = [(0,0,0),(131,129,125),(157,151,142),(109,111,116),(89,96,103),
             (151,131,115),(149,143,135),(172,161,139)]
paint = Image.new('L', (WIDTH, HEIGHT))
draw = ImageDraw.Draw(paint)


def tile(points, material):
    draw.polygon([((px+224)*SCALE,(112-pz)*SCALE) for px,pz in points], fill=material)


def polygon(cx, cz, radius, count, material, rotation=0):
    tile([(cx+radius*math.cos(rotation+i*2*math.pi/count),
           cz+radius*math.sin(rotation+i*2*math.pi/count)) for i in range(count)], material)


def lane(a, b, width, steel=False):
    dx,dz = b[0]-a[0], b[1]-a[1]
    length = math.hypot(dx,dz)
    dx,dz = dx/length,dz/length
    def point(t, off):
        return (a[0]+dx*t-dz*off, a[1]+dz*t+dx*off)
    def slab(start,end,lo,hi,mat):
        tile([point(start,lo),point(end,lo),point(end,hi),point(start,hi)],mat)
    slab(0,length,-width/2,width/2,3)
    count=max(1,round(length/5))
    for i in range(count):
        start,end=length*i/count+.15,length*(i+1)/count-.15
        slab(start,end,-width/2+.24,width/2-.24,4 if steel else placement.choice([1,2,6]))
        if steel or i%4==2:
            for k in range(4):
                t=start+(end-start)*(.26+.15*k)
                slab(t,t+.2,-width*.29,width*.29,3 if steel else 4)


def arc(cx,cz,radius,start,end,width,pieces):
    for i in range(pieces):
        a=math.radians(start+(end-start)*i/pieces+.45)
        b=math.radians(start+(end-start)*(i+1)/pieces-.45)
        tile([(cx+r*math.cos(t),cz+r*math.sin(t))
              for r,t in [(radius-width/2,a),(radius+width/2,a),
                          (radius+width/2,b),(radius-width/2,b)]],placement.choice([2,6,7]))


# Aprons stay outside the working octagons. Joined concrete slabs break at expansion seams.
for bank in [-1,1]:
    for row in [82.8,20,-20,-82.8]:
        polygon(bank*203.2,row,17.8,8,3,math.pi/8)
        polygon(bank*203.2,row,17.1,8,2,math.pi/8)
    for top,bottom in [(82.8,20),(20,-20),(-20,-82.8)]:
        lane((bank*203.2,top),(bank*203.2,bottom),6.8)
    for row in [55.6,-55.6]:
        lane((bank*198,np.sign(row)*74),(bank*173,row),7)
        lane((bank*173,row),(bank*156,row),7)

for radius in [37,48,61]:
    arc(-213,103,radius,-81,-5,2.0,14)
for angle in [-8,-21,-36,-53,-71]:
    t=math.radians(angle)
    lane((-213+24*math.cos(t),103+24*math.sin(t)),
         (-213+49*math.cos(t),103+49*math.sin(t)),1.0)
lane((-203,20),(-174,6),7)
lane((-174,6),(-151,-12),7)
lane((-151,-12),(-119,-17),7)
lane((-119,-17),(-90,-25.2),6.5)
lane((-90,-25.2),(-66,-25.2),6)
for radius in [29,48]:
    arc(-205,-103,radius,4,84,1.6,11)
for deg in [12,32,52,71]:
    t=math.radians(deg)
    lane((-205+15*math.cos(t),-103+15*math.sin(t)),
         (-205+54*math.cos(t),-103+54*math.sin(t)),1.35)
for px in [108,127,146]:
    lane((px,66),(px+34,102),5.8,True)
for px in [111,134]:
    lane((px,-103),(px+39,-74),4.6,True)

# Small islands of hexagonal repair slabs leave much of the ancient crater exposed.
yard_cover = Image.new('L',paint.size)
yard_draw = ImageDraw.Draw(yard_cover)
for ix in range(6):
    for iz in range(-2,3):
        if (ix,iz) in [(0,2),(0,-2),(1,-2),(5,2),(3,2)]: continue
        cx=150+ix*7.5
        cz=iz*8.66+(ix%2)*4.33
        broken = (ix,iz) in {(0,-1),(1,1),(2,-2),(2,2),(4,-2)}
        def slab(radius):
            pts=[(cx+radius*math.cos(j*math.pi/3),cz+radius*math.sin(j*math.pi/3)) for j in range(6)]
            if broken:
                # Irregular fracture face; surviving half points back to the yard.
                pts=[pts[0],pts[1],(cx+.55,cz+1.4),(cx-.65,cz+.3),(cx+.2,cz-1.3),pts[5]]
            return pts
        tile(slab(4.96),3)
        tile(slab(4.71),placement.choice([1,5,6]))
        yard_draw.polygon([((px+224)*SCALE,(112-pz)*SCALE) for px,pz in slab(4.96)],fill=255)

# A flush cable trench and a single maintenance hatch connect each relay to its ground equipment.
for rz in [81.6,0,-81.6]:
    side=-1 if rz<0 else 1
    route=[(0,rz),(-7*side,rz),(-9*side,rz+2*side),(-12*side,rz+2*side)]
    for a,b in zip(route,route[1:]):
        dx,dz=b[0]-a[0],b[1]-a[1]
        length=math.hypot(dx,dz)
        for half,material in ((.39,3),(.19,6)):
            u,v=-dz*half/length,dx*half/length
            tile([(a[0]+u,a[1]+v),(b[0]+u,b[1]+v),(b[0]-u,b[1]-v),(a[0]-u,a[1]-v)],material)
    hx,hz=route[-1]
    for w,h,mat in [(1.38,1.06,3),(1.15,.83,4)]:
        tile([(hx-w,hz-h),(hx+w,hz-h),(hx+w,hz+h),(hx-w,hz+h)],mat)
    for off in [-.62,0,.62]:
        tile([(hx+off-.08,hz-.58),(hx+off+.08,hz-.58),(hx+off+.08,hz+.58),(hx+off-.08,hz+.58)],2)
# The deck spans plan columns 397–472. Each sill follows its skewed
# bank exactly; the compressed dust beyond it remains a soft trail surface.
for turn in (-1, 1):
    local_x, local_z = turn*x, turn*z
    left, right = -81.2, -51.2
    across = (local_x-left)/(right-left)
    for direction, start, end in [(1,63.2,60.4),(-1,48.0,46.0)]:
        bank_z = start+(end-start)*across
        distance = direction*(local_z-bank_z)
        def at(t, outward):
            return turn*(left+(right-left)*t), turn*(start+(end-start)*t+direction*outward)
        for j in range(5):
            lo,hi=j/5+.002,(j+1)/5-.002
            tile([at(lo,-.10),at(hi,-.10),at(hi,1.75),at(lo,1.75)],6)
        lateral = local_x-(left+right)/2
        bend = distance*.07 + fold*.75
        tracks = np.exp(-((abs(lateral-bend)-4.6)/(1.17+.04*np.clip(distance,0,18)))**2)
        wear = tracks*fade(.9,2.5,distance)*(1-fade(4,18+4*fold,distance))
        ground *= (1-wear*(.14+.05*grit))[...,None]

# Dust collects around the separate footings rather than outlining a rectangle.
for center_x in (-211.6,211.6):
    for center_z in (-100,100):
        dx, dz = x-center_x,z-center_z
        radius = np.hypot(dx/6.2,dz/3.8)
        dust = (1-fade(.66,1.42,radius+.23*fold+.10*grit))
        ground += (dust*(6+8*grit))[...,None]

codes = np.asarray(paint)
hard = codes != 0
paving = np.array(materials)[codes].astype(float)
# Subtle metre-scale slab joints and dust tie the aprons into their surroundings.
joint = np.minimum(abs(np.remainder(x+2.5,5)-2.5),abs(np.remainder(z+2.5,5)-2.5))
paving += (fold*15+grit*9-8*(1-fade(.06,.30,joint)))[...,None]
paving = mix(paving,ground,.12+.18*fade(-.15,.41,fold))
# Loose soil covers the broken field edge; buried patches take wheel prints.
outer = (np.asarray(yard_cover)>0)*(1-fade(150,181,x)+.55*fade(9,19,abs(z)))
burial = np.clip(outer*(.32+.69*fold+.78*grit),0,1)
paving = mix(paving,ground,fade(.06,.67,burial))
hard = hard & (burial < .64)
rgb = np.where(hard[...,None],paving,ground)
rgba = np.dstack((np.clip(rgb,0,255).astype('uint8'),hard.astype('uint8')*255))
Image.fromarray(rgba).save(OUTPUT)
