"""Bake local, straight-alpha ground paintings. The capture radius is one UV-space unit."""
from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1] / 'unity/Assets/Spacewars/Content/Resources'
SIZE = 512
z, x = np.mgrid[:SIZE, :SIZE]
x = (x + .5) * 4 / SIZE - 2
z = 2 - (z + .5) * 4 / SIZE
r = np.hypot(x, z)
angle = np.arctan2(z, x)
rng = np.random.default_rng(3112026)
grain = rng.normal(0, 1, r.shape)


def layer(color, detail=0):
    return np.asarray(color) + np.broadcast_to(detail, r.shape)[..., None]


def mix(source, target, coverage):
    a = np.clip(coverage, 0, 1)[..., None]
    return source * (1-a) + target * a


def art(name, tint, texture):
    photo = Image.open(ROOT / texture).convert('L').resize((SIZE, SIZE))
    raw = np.asarray(photo, dtype=float)
    noise = (raw - raw.mean()) / max(raw.std(), 4)
    tone = layer(tint, np.clip(noise, -2, 2)*8 + grain*2)
    u, v = x, z
    edge = 1-r
    opacity = 1.0
    rim = (r < 1) & (r > .958)
    arcs = rim & (np.cos(6*angle) > .38)
    if name == 'Highland':
        u, v = .92*x+.39*z, -.39*x+.92*z
        # Overlapping beds break off at different distances along the slope.
        bedding = v + .045*np.sin(3*u) + .014*noise
        edge = 1.02-r + .048*np.sin(9*angle) + noise*.024
        for offset, start, end, thickness in [
            (-.51,-1.13,.89,.16), (-.30,-1.61,1.22,.19),
            (-.07,-1.94,1.45,.17), (.13,-1.43,1.81,.15),
            (.32,-1.71,1.20,.14), (.50,-.94,1.46,.13)]:
            along = np.minimum(u-start,end-u)
            tip = np.clip(along/.40,0,1)
            bed = np.minimum(along,thickness*(.18+.82*tip)-abs(bedding-offset))
            edge = np.maximum(edge,bed)
        opacity = np.maximum(np.clip((edge+.045+noise*.025)/.14,0,1),arcs)
        # Long fractured sheets of slate follow a common bedding direction.
        sx, sz = u*2.8+.08*noise, v*8+.14*np.sin(4*u)+.05*noise
        ix, iz = np.floor(sx), np.floor(sz)
        nearest, second = np.full(r.shape, 10.), np.full(r.shape, 10.)
        slab = np.zeros(r.shape)
        for dx in (-1,0,1):
            for dz in (-1,0,1):
                gx, gz = ix+dx, iz+dz
                h = np.sin(gx*31.72+gz*11.37)
                px = gx+.5+.35*h
                pz = gz+.5+.34*np.cos(gx*17.2-gz*37.9)
                distance = (px-sx)**2+(pz-sz)**2
                slab = np.where(distance<nearest,h,slab)
                second = np.minimum(second,np.maximum(nearest,distance))
                nearest = np.minimum(nearest,distance)
        crack = second-nearest
        tone += (slab*14)[:,:,None]
        tone = mix(tone, layer((67,65,53),noise*3), np.clip((.11-crack)/.09,0,1))
        tone += ((crack>.11)&(crack<.17))[:,:,None]*7
    elif name == 'Observatory':
        # Survey marks divide the original capture radius into six sectors.
        tone += (6*np.cos(3*angle) * np.clip(r/.4,0,1))[...,None]
        joint = np.minimum(abs(np.sin(3*angle))*r, abs(r-.49))
        tone = mix(tone,layer((78,79,80),noise*4), np.clip((.015-joint)/.012,0,1))
        footing = np.maximum(abs(x),abs(z))+.40*np.minimum(abs(x),abs(z))
        tone = mix(tone,layer((96,100,101),noise*4),footing<.32)
        tone = mix(tone,layer((161,148,126),noise*4),(footing<.32)&(footing>.29))
        tone = mix(tone,layer((128,125,122),noise*6),rim)
    elif name == 'Meridian':
        edge = np.minimum(1.82-abs(z), 1.10-z*.22-abs(x)) + .055*noise
        u, v = x+.13*z, z-.13*x
    elif name == 'Foundry':
        deck = 1.08-r
        ash = np.clip((.21-deck+.11*np.sin(7*x+4*z)+noise*.07)/.38,0,1)
        cable_axis = abs(z-.17*np.tanh(5*(x+1.44)))
        cable = np.minimum(np.minimum(x+1.94,-x-.91), .077-cable_axis)
        edge = np.maximum(deck,cable)
        # Two sheaths disappear under slag at the outer end of the trench.
        burial = np.clip((x+1.90+.055*noise)/.26,0,1)
        opacity = np.maximum(np.clip(deck/.07+.5,0,1)*(1-ash*.93),
                             np.clip(cable/.014+.5,0,1)*burial)
        opacity = np.maximum(opacity,arcs)
    elif name == 'City':
        hexagon = np.maximum(abs(z), abs(x)*np.sqrt(3)/2+abs(z)/2)
        edge = 1-hexagon
        radial = np.mod(angle-np.pi/2+np.pi/3,2*np.pi/3)-np.pi/3
        along, across = r*np.cos(radial), abs(r*np.sin(radial))
        approach = np.minimum(np.minimum(along-.92,1.59-along),.116-across)
        edge = np.maximum(edge,approach)
        sector = np.floor((angle+np.pi)/(np.pi/3))*np.pi/3
        u,v = x*np.cos(sector)+z*np.sin(sector), -x*np.sin(sector)+z*np.cos(sector)
    elif name == 'Crossings':
        # The terrain road mask supplies the entire dirt finish, including both approaches.
        edge = np.where(arcs, 1., -1.)
    if name in ('Meridian','Foundry','City'):
        width, height = {'Meridian':(.47,.27),'Foundry':(.47,.42),'City':(.14,.093)}[name]
        row = np.floor(v/height)
        a = u/width + (row*.5 if name in ('City','Meridian') else 0)
        b = v/height
        seam = np.minimum(abs(a-np.round(a))*width, abs(b-np.round(b))*height)
        tone += (5*np.sin(np.floor(a)*13 + row*7))[...,None]
        tone *= (1-.35*np.clip((.011-seam)/.009,0,1))[...,None]
        if name == 'City':
            grate = layer((48,61,71),noise*3+12*np.cos(along*220))
            grate = mix(grate,layer((100,174,183)), (across>.088)&(across<.103)&(np.cos(along*19)>.1))
            tone = mix(tone,grate,(approach>0)&(hexagon>1))
        if name=='Foundry':
            tone = mix(tone,layer((70,60,50),noise*10),ash*.75)
            seam_shadow = np.clip((.028-seam)/.028,0,1)
            tone *= (1-.30*seam_shadow)[:,:,None]
            trench = (cable>0)&(x<-.98)
            tone = mix(tone,layer((45,39,36),noise*4),trench)
            sheath = np.exp(-((cable_axis-.039)/.015)**2)
            tone += (sheath*trench)[:,:,None]*np.array((72,54,35))
            # Flanged feed-through where the buried pair enters the steel floor.
            socket = np.maximum(abs(x+1.015)-.09,abs(z-.17)-.14)
            tone = mix(tone,layer((64,64,61),noise*4),socket<0)
            lip = (socket<0)&(socket>-.022)
            tone = mix(tone,layer((145,130,100),noise*4),lip)
            stud = np.hypot(abs(a-np.floor(a)-.11)*width,abs(b-np.floor(b)-.12)*height)
            tone = mix(tone,layer((146,137,122),noise*2),(stud<.015)&(x>-1.1))
    if name == 'Basin':
        sector = np.floor((angle+.1*np.sin(4*r)+np.pi)/(np.pi/3))
        seam = abs(np.sin(3*(angle+.1*np.sin(4*r)+noise*.012)))*r
        tone += (9*np.cos(sector*4))[...,None]
        tone = mix(tone,layer((82,72,63),noise*5),np.clip((.027-seam)/.014,0,1))
        tone = mix(tone,layer((105,59,66),noise*7),r>.92)
        fan = np.minimum(np.minimum(-x-.94,1.54+x), .08+.40*(1.55+x)-abs(z))
        fan += .05*noise
        edge = np.maximum(edge,fan)
        tone = mix(tone,layer((151,133,120),noise*17+grain*6),r>1)
    elif name != 'Test':
        color = {'Foundry':(205,155,71),'Highland':(172,161,125),'City':(174,196,205),
                 'Observatory':(188,173,143),'Crossings':(202,188,153),'Meridian':(120,104,84)}[name]
        tone = mix(tone,layer(color,noise*4),arcs)
    feather = .095 if name in ('Highland','Meridian','Crossings') else .01
    alpha = np.clip(edge/feather+.5,0,1)*opacity
    if name in ('Highland','Foundry','Crossings'):
        alpha = np.maximum(alpha, arcs.astype(float))
    rgba = np.dstack((np.clip(tone,0,255),alpha*255)).astype('uint8')
    Image.fromarray(rgba).save(ROOT / 'RelayPaving' / (name+'.png'))


if __name__ == '__main__':
    for style, tint, texture in [
        ('Highland',(113,115,98),'MossHighlandArt/RockyMoss.png'),
        ('Meridian',(193,171,137),'BasaltCrownArt/Stone.png'),
        ('Foundry',(100,89,78),'Environment/NaturalFrontier/Steel.png'),
        ('City',(68,84,100),'Environment/NaturalFrontier/Concrete.png'),
        ('Basin',(176,161,148),'Environment/NaturalFrontier/Rock.png'),
        ('Observatory',(150,150,148),'Environment/NaturalFrontier/Concrete.png'),
        ('Test',(165,161,144),'Environment/NaturalFrontier/Concrete.png'),
        ('Crossings',(147,138,119),'Environment/NaturalFrontier/Concrete.png')]:
        art(style,tint,texture)
