using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public static class BasaltCrownDetails
    {
        public enum Kind { Kerb=1, Rubble=2, Ore=3, BorderRock=4 }
        public sealed class Placement
        {
            public Kind Type;
            public NavPoint Center;
            public float HalfX,HalfZ,Height,Heading;
            public double Radius=>Math.Sqrt(HalfX*HalfX+HalfZ*HalfZ);
        }
        public static TerritorySite[] HillMines(BasaltCrownMap map,PlayableProfile profile)=>map.Sites(profile)
            .Where(s=>s.Kind==PlayableBuildingKind.Mine&&map.SurfaceHeight(s.Position)>0).ToArray();

        // Bounded shader layout for the four base sites.
        public const int MaxPads=32;
        public sealed class SitePad
        {
            public NavPoint Center,Hub;
            public float HalfX,HalfZ,Corner;
            public bool IsOutpost;
        }
        public static TerritorySite[] Mines(BasaltCrownMap map,PlayableProfile profile)=>map.Sites(profile).Where(s=>s.Kind==PlayableBuildingKind.Mine).ToArray();
        public static SitePad[] SitePads(BasaltCrownMap map,PlayableProfile profile,BasaltCrownArtProfile art)
        {
            float slotRadius=(float)Math.Max(profile.ScienceFootprintRadius,Math.Max(profile.FactoryFootprintRadius,profile.RefineryFootprintRadius));
            float defenseRadius=Mathf.Max((float)(Math.Sqrt(2)*profile.TurretFootprintRadius),
                ConstructionApronMesh.DefenseSupportHalf(profile,EnvironmentArtProfile.Load()).magnitude);
            var pads=map.Sites(profile).Where(site=>TerritoryRules.Center(site.Kind)).Select(site=>{
                bool outpost=site.Kind==PlayableBuildingKind.Outpost;
                // A compact outpost needs only the feather and the same width of solid paving beyond it.
                float margin=outpost?Math.Min(art.siteApronMargin,2*art.siteApronFeather):art.siteApronMargin;
                var hub=(Center:site.Position,Radius:(float)TerritoryRules.Radius(profile,site.Kind)+margin);
                var slots=site.Slots.Select(slot=>(Center:slot.Position,Radius:slotRadius+margin));
                var defenses=site.DefenseSlots(profile).Select(slot=>(Center:slot.Position,Radius:defenseRadius+margin));
                return CompoundApron(hub.Center,slots.Prepend(hub).Concat(defenses).ToArray(),margin,outpost);
            }).ToArray();
            if(pads.Length>MaxPads)throw new InvalidOperationException("Basalt site pad shader capacity exceeded");
            return pads;
        }
        private static SitePad CompoundApron(NavPoint hub,(NavPoint Center,float Radius)[] footprints,float margin,bool outpost)
        {
            double minX=footprints.Min(p=>p.Center.X-p.Radius),maxX=footprints.Max(p=>p.Center.X+p.Radius);
            double minZ=footprints.Min(p=>p.Center.Z-p.Radius),maxZ=footprints.Max(p=>p.Center.Z+p.Radius);
            var center=new NavPoint((minX+maxX)*.5,(minZ+maxZ)*.5);
            double halfX=(maxX-minX)*.5,halfZ=(maxZ-minZ)*.5;
            // Projection onto a chamfer at 45 degrees costs sqrt(2) times the footprint radius.
            double cornerLimit=footprints.Min(p=>halfX+halfZ-Math.Abs(p.Center.X-center.X)-Math.Abs(p.Center.Z-center.Z)-Math.Sqrt(2)*p.Radius);
            return new SitePad{Center=center,Hub=hub,HalfX=(float)halfX,HalfZ=(float)halfZ,
                Corner=(float)Math.Min(margin*2,Math.Min(Math.Min(halfX,halfZ),cornerLimit)),IsOutpost=outpost};
        }
        public static float PadDistance(NavPoint point,SitePad pad)
        {
            double edgeX=Math.Abs(point.X-pad.Center.X)-pad.HalfX;
            double edgeZ=Math.Abs(point.Z-pad.Center.Z)-pad.HalfZ;
            return (float)Math.Max(Math.Max(edgeX,edgeZ),(edgeX+edgeZ+pad.Corner)*.70710678);
        }
        public static bool Clear(Placement p,BasaltCrownMap map,PlayableProfile profile,BasaltCrownArtProfile art)
            =>Clear(p,map,profile,art,BasaltCrownRoads.Plaza(map,profile));
        private static bool Clear(Placement p,BasaltCrownMap map,PlayableProfile profile,BasaltCrownArtProfile art,BasaltCrownRoads.RelayPlaza plaza)
        {
            if(p.Type==Kind.Kerb){
                double cos=Math.Cos(p.Heading),sin=Math.Sin(p.Heading);
                double dx=plaza.Center.x-p.Center.X,dz=plaza.Center.y-p.Center.Z;
                double localX=Math.Max(0,Math.Abs(dx*cos+dz*sin)-p.HalfX);
                double localZ=Math.Max(0,Math.Abs(-dx*sin+dz*cos)-p.HalfZ);
                if(localX*localX+localZ*localZ<plaza.Radius*plaza.Radius)return false;
                foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1}){
                    float cornerX=(float)(p.Center.X+x*p.HalfX*cos-z*p.HalfZ*sin);
                    float cornerZ=(float)(p.Center.Z+x*p.HalfX*sin+z*p.HalfZ*cos);
                    if(BasaltCrownRoads.SignedDistance(cornerX,cornerZ,art)<0)return false;
                }
            }
            // Full circumscribed footprint is conservative even for rotated chips.
            // Leave a real visual gate through the kerb towards each relocated flank mine.
            if(p.Type==Kind.Kerb&&Math.Abs(p.Center.Z)<art.siteLinkWidth*.5f+p.HalfZ&&Math.Abs(p.Center.X)>BasaltCrownRoads.RingX&&Mines(map,profile).Any(m=>map.SurfaceHeight(m.Position)==0&&Math.Abs(p.Center.X)<Math.Abs(m.Position.X)))return false;
            double r=p.Radius+art.detailClearance;
            if(!map.SupportsFootprint(p.Center,r))return false;
            foreach(var site in map.Sites(profile)){
                if(Distance(p.Center,site.Position)<r+TerritoryRules.Radius(profile,site.Kind)+profile.TankCollisionRadius)return false;
                foreach(var slot in site.Slots){
                    double reserve=Math.Max(profile.ScienceFootprintRadius,Math.Max(profile.FactoryFootprintRadius,profile.RefineryFootprintRadius));
                    if(Distance(p.Center,slot.Position)<r+reserve+profile.TankCollisionRadius)return false;
                }
            }
            return true;
        }
        private static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
        private static float Hash(float x,float z)=>Mathf.Repeat(Mathf.Sin(x*127.1f+z*311.7f)*43758.5453f,1);
        public static List<Placement> Plan(BasaltCrownMap map,PlayableProfile profile,BasaltCrownArtProfile art)
        {
            art.Validate();var result=new List<Placement>();
            var compounds=SitePads(map,profile,art);
            var outposts=compounds.Where(pad=>pad.IsOutpost).ToArray();
            var plaza=BasaltCrownRoads.Plaza(map,profile);
            void Add(Placement p){
                if(p.Type==Kind.Kerb&&compounds.Any(pad=>PadDistance(p.Center,pad)<p.Radius))return;
                if(p.Type==Kind.Rubble&&outposts.Any(pad=>PadDistance(p.Center,pad)<p.Radius))return;
                if(Clear(p,map,profile,art,plaza))result.Add(p);
            }
            void Edge(bool horizontal,float fixedAxis,float lo,float hi){
                float half=art.kerbLength*.5f;
                for(float a=lo+half;a+half<=hi;a+=art.kerbLength+art.kerbGap){
                    float x=horizontal?a:fixedAxis,z=horizontal?fixedAxis:a;
                    // Preserve open intersections: edging only follows the union's outer edge.
                    if(BasaltCrownRoads.SignedDistance(x,z,art,plaza)<art.kerbWidth*.45f)continue;
                    Add(new Placement{Type=Kind.Kerb,Center=new NavPoint(x,z),HalfX=horizontal?half:art.kerbWidth*.5f,HalfZ=horizontal?art.kerbWidth*.5f:half,Height=art.kerbHeight});
                }
            }
            float offset=art.kerbWidth*.5f+art.detailClearance;
            foreach(int side in new[]{-1,1}){
                Edge(true,side*(BasaltCrownRoads.CausewayHalfWidth+offset),-BasaltCrownRoads.RingX,BasaltCrownRoads.RingX);
                foreach(int edge in new[]{-1,1}){
                    Edge(true,side*(BasaltCrownRoads.RingZ+edge*(art.bypassWidth*.5f+offset)),-BasaltCrownRoads.RingX,BasaltCrownRoads.RingX);
                    Edge(false,side*(BasaltCrownRoads.RingX+edge*(art.bypassWidth*.5f+offset)),-BasaltCrownRoads.RingZ,BasaltCrownRoads.RingZ);
                }
            }
            // Tangent blocks follow only the exposed arcs. Clear rejects both road openings.
            float rim=plaza.Radius+offset;
            int blocks=Mathf.FloorToInt(2*Mathf.PI*rim/(art.kerbLength+art.kerbGap));
            for(int i=0;i<blocks;i++){
                float angle=2*Mathf.PI*i/blocks;
                var center=new NavPoint(plaza.Center.x+rim*Mathf.Cos(angle),plaza.Center.y+rim*Mathf.Sin(angle));
                Add(new Placement{Type=Kind.Kerb,Center=center,HalfX=art.kerbLength*.5f,HalfZ=art.kerbWidth*.5f,
                    Height=art.kerbHeight,Heading=angle+Mathf.PI*.5f});
            }
            var rocks=map.Solids.Where(s=>!double.IsNaN(s.Top)).ToArray();float step=art.rubbleSize*2.3f;
            // Deterministic world grid with irregular jitter, not random global state.
            for(float z=-46;z<=46;z+=step)for(float x=-54;x<=54;x+=step){
                float h=Hash(x,z);if(h>art.rubbleDensity)continue;
                var q=new NavPoint(x+(Hash(x+7,z)-.5f)*step*.6f,z+(Hash(x,z+11)-.5f)*step*.6f);
                if(!rocks.Any(b=>b.BoundaryDistanceSquared(q)<art.rubbleBand*art.rubbleBand))continue;
                float size=art.rubbleSize*(.45f+.55f*h);
                if(BasaltCrownRoads.SignedDistance((float)q.X,(float)q.Z,art,plaza)<size+art.detailClearance)continue;
                Add(new Placement{Type=Kind.Rubble,Center=q,HalfX=size*.5f,HalfZ=size*(.3f+.15f*Hash(x+13,z)),Height=art.rubbleHeight*(.5f+.5f*h),Heading=h*Mathf.PI});
            }
            foreach(var site in Mines(map,profile))for(int i=0;i<12;i++){
                // Hill approaches face the causeway; flank approaches face the ring road.
                double angle=i*Math.PI/6;if((site.Id>=7?Math.Abs(Math.Sin(angle)):Math.Abs(Math.Cos(angle)))<.45)continue;
                double radius=art.mineApronRadius*.83;
                var q=new NavPoint(site.Position.X+Math.Cos(angle)*radius,site.Position.Z+Math.Sin(angle)*radius);
                Add(new Placement{Type=Kind.Ore,Center=q,HalfX=art.rubbleSize*.55f,HalfZ=art.rubbleSize*.35f,Height=art.rubbleHeight,Heading=(float)angle});
            }
            return result;
        }
        public static Mesh Build(IEnumerable<Placement> placements,BasaltCrownMap map)
        {
            var vertices=new List<Vector3>();var indices=new List<int>();
            void Tri(Vector3 a,Vector3 b,Vector3 c){int n=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);indices.Add(n);indices.Add(n+1);indices.Add(n+2);}
            foreach(var p in placements){
                // Eight-corner chamfered blocks / irregular faceted rubble. Ring count is mesh topology.
                var lower=new Vector3[8];var upper=new Vector3[8];
                var footprint=new[]{new Vector2(-1,-.7f),new Vector2(-.7f,-1),new Vector2(.7f,-1),new Vector2(1,-.7f),new Vector2(1,.7f),new Vector2(.7f,1),new Vector2(-.7f,1),new Vector2(-1,.7f)};
                for(int i=0;i<8;i++){
                    var f=footprint[i];float irregular=p.Type==Kind.Kerb?1:.8f+.2f*Hash((float)p.Center.X+i,(float)p.Center.Z);
                    float x=f.x*p.HalfX*irregular,z=f.y*p.HalfZ*irregular;
                    Vector3 At(float scale,float height){double xx=p.Center.X+(x*Math.Cos(p.Heading)-z*Math.Sin(p.Heading))*scale,zz=p.Center.Z+(x*Math.Sin(p.Heading)+z*Math.Cos(p.Heading))*scale;return new Vector3((float)xx,(float)map.SurfaceHeight(new NavPoint(xx,zz))+height,(float)zz);}
                    lower[i]=At(1,.003f);upper[i]=At(p.Type==Kind.Kerb?.88f:.6f,p.Height*(p.Type==Kind.Kerb?1:.65f+.35f*Hash(i,(float)p.Center.X)));
                }
                var center=new Vector3((float)p.Center.X,(float)map.SurfaceHeight(p.Center)+p.Height,(float)p.Center.Z);
                for(int i=0;i<8;i++){int j=(i+1)%8;Tri(lower[i],upper[i],upper[j]);Tri(lower[i],upper[j],lower[j]);Tri(upper[i],center,upper[j]);}
            }
            var mesh=new Mesh{name="Basalt combined presentation details",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
}
