from PIL import Image,ImageDraw
from pathlib import Path
import math
R=Path(__file__).parent
S=2
INK='#e3f0f7';MUTED='#87a3b3';CYAN='#45d9f0';LINE='#3a5463'
F='/System/Library/Fonts/Supplemental/Arial.ttf';B='/System/Library/Fonts/Supplemental/Arial Bold.ttf'
def line(d,p,c,w=1):d.line([(int(x*S),int(y*S)) for x,y in p],fill=c,width=max(1,int(w*S)))
def el(d,xy,fill=None,out=None,w=1):d.ellipse(tuple(int(v*S) for v in xy),fill=fill,outline=out,width=max(1,int(w*S)))
def poly(d,p,c):d.polygon([(int(x*S),int(y*S)) for x,y in p],fill=c)
def arc(d,xy,a,b,c,w=1):d.arc(tuple(int(v*S) for v in xy),a,b,fill=c,width=max(1,int(w*S)))
def radar(n):
 # Draw large enough that the small HUD versions retain a material silhouette.
 im=Image.new('RGBA',(n*S,n*S));d=ImageDraw.Draw(im);cx=cy=n/2;r=n*.37
 # Machined dark body with a lower lip and highlight on the upper edge.
 for dy,rr,fill in [(7,r+6,'#060d12'),(5,r+4,'#17232c'),(2,r+4,'#526978'),(0,r+3,'#263f4c'),(-1,r,'#0b1720')]:el(d,(cx-rr,cy-rr+dy,cx+rr,cy+rr+dy),fill)
 arc(d,(cx-r-3,cy-r-4,cx+r+3,cy+r+2),195,333,'#90a5af',1.5)
 arc(d,(cx-r-2,cy-r-1,cx+r+2,cy+r+3),18,150,'#10242d',3)
 # Solid radial glass shading.
 for rr in range(int(r)-1,0,-1):
  k=1-rr/r;col=(int(12+10*k),int(31+25*k),int(40+30*k));el(d,(cx-rr,cy-rr,cx+rr,cy+rr),col)
 ir=r*.83
 # A few contour traces, kept low contrast.
 for j in range(4):
  p=[]
  for i in range(27):
   x=-ir+i*ir*2/26;y=(j-1.5)*ir*.34+math.sin(i*.37+j)*ir*.09
   if x*x+y*y<ir*ir:p.append((cx+x,cy+y))
  if len(p)>1:line(d,p,'#224a55',.8)
 for a in range(0,360,15):
  t=math.radians(a);r1=r*.9;r2=r*(.99 if a%45==0 else .955)
  line(d,[(cx+math.cos(t)*r1,cy+math.sin(t)*r1),(cx+math.cos(t)*r2,cy+math.sin(t)*r2)],'#7896a3' if a%45==0 else '#3d5c6a',1)
 # Illuminated sweep fades across its angular width.
 for a in range(276,327):
  k=(a-276)/50;col=(int(20+17*k),int(54+51*k),int(64+55*k))
  d.pieslice(tuple(int(v*S) for v in (cx-ir,cy-ir,cx+ir,cy+ir)),a,a+1,fill=col)
 for k in [.34,.67,1]:el(d,(cx-ir*k,cy-ir*k,cx+ir*k,cy+ir*k),out='#397c89' if k<1 else '#4b9aa8',w=.8)
 line(d,[(cx-ir,cy),(cx+ir,cy)],'#2b505d',.7);line(d,[(cx,cy-ir),(cx,cy+ir)],'#2b505d',.7)
 a=math.radians(327);line(d,[(cx,cy),(cx+math.cos(a)*ir,cy+math.sin(a)*ir)],'#7be5ed',2)
 # Observed contacts.
 for dx,dy,sz in [(-.38,-.21,.045),(.28,-.42,.04),(.42,.3,.035)]:
  px=cx+ir*dx;py=cy+ir*dy;s=ir*sz;poly(d,[(px,py-s),(px+s,py),(px,py+s),(px-s,py)],'#c2edf1')
 el(d,(cx-2.5,cy-2.5,cx+2.5,cy+2.5),'#b3f3f5')
 # Upper glass reflection, restrained like the radial wheel sheen.
 arc(d,(cx-ir+3,cy-ir+3,cx+ir-3,cy+ir-3),208,267,'#58848e',1.5)
 return im

if __name__ == "__main__":
 import argparse
 p=argparse.ArgumentParser();p.add_argument("--output",required=True);a=p.parse_args()
 radar(256).resize((256,256),Image.Resampling.LANCZOS).save(a.output)
