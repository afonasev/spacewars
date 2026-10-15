using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public static class BasaltCrownDetails
    {
        public enum Kind { Kerb=1, Rubble=2, Ore=3 }
        public sealed class Placement
        {
            public Kind Type;
            public NavPoint Center;
            public float HalfX,HalfZ,Height,Heading;
            public double Radius=>Math.Sqrt(HalfX*HalfX+HalfZ*HalfZ);
        }
        public static TerritorySite[] HillMines(BasaltCrownMap map,PlayableProfile profile)=>map.Sites(profile)
            .Where(s=>s.Kind==PlayableBuildingKind.Mine&&map.SurfaceHeight(s.Position)>0).ToArray();

        // Bounded shader layout: four central sites plus their authored construction slots.
        public const int MaxPads=32;
        public sealed class SitePad
        {
            public NavPoint Center,Hub;
            public float Radius;
        }
        public static TerritorySite[] Mines(BasaltCrownMap map,PlayableProfile profile)=>map.Sites(profile).Where(s=>s.Kind==PlayableBuildingKind.Mine).ToArray();
        public static SitePad[] SitePads(BasaltCrownMap map,PlayableProfile profile,BasaltCrownArtProfile art)
        {
            var result=new List<SitePad>();
            float slotRadius=(float)Math.Max(profile.ScienceFootprintRadius,Math.Max(profile.FactoryFootprintRadius,profile.RefineryFootprintRadius));
            foreach(var site in map.Sites(profile).Where(s=>s.Kind!=PlayableBuildingKind.Mine)){
                result.Add(new SitePad{Center=site.Position,Hub=site.Position,Radius=(float)TerritoryRules.Radius(profile,site.Kind)+art.siteApronMargin});
                foreach(var slot in site.Slots)result.Add(new SitePad{Center=slot.Position,Hub=site.Position,Radius=slotRadius+art.siteApronMargin});
            }
            if(result.Count>MaxPads)throw new InvalidOperationException("Basalt site pad shader capacity exceeded");
            return result.ToArray();
        }
        public static float PadDistance(NavPoint point,SitePad pad,float linkWidth)
        {
            double x=Math.Abs(point.X-pad.Center.X),z=Math.Abs(point.Z-pad.Center.Z);
            double octagon=Math.Max(Math.Max(x,z),(x+z)*.70710678)-pad.Radius;
            double dx=pad.Center.X-pad.Hub.X,dz=pad.Center.Z-pad.Hub.Z,l=dx*dx+dz*dz;
            double t=l>0?Math.Max(0,Math.Min(1,((point.X-pad.Hub.X)*dx+(point.Z-pad.Hub.Z)*dz)/l)):0;
            double ax=point.X-pad.Hub.X-t*dx,az=point.Z-pad.Hub.Z-t*dz;
            return (float)Math.Min(octagon,Math.Sqrt(ax*ax+az*az)-linkWidth*.5);
        }
        public static bool Clear(Placement p,BasaltCrownMap map,PlayableProfile profile,BasaltCrownArtProfile art)
        {
            if(p.Type==Kind.Kerb)foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})
                if(BasaltCrownRoads.SignedDistance((float)p.Center.X+x*p.HalfX,(float)p.Center.Z+z*p.HalfZ,art)<0)return false;
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
            void Add(Placement p){if(Clear(p,map,profile,art))result.Add(p);}
            void Edge(bool horizontal,float fixedAxis,float lo,float hi){
                float half=art.kerbLength*.5f;
                for(float a=lo+half;a+half<=hi;a+=art.kerbLength+art.kerbGap){
                    float x=horizontal?a:fixedAxis,z=horizontal?fixedAxis:a;
                    // Preserve open intersections: edging only follows the union's outer edge.
                    if(BasaltCrownRoads.SignedDistance(x,z,art)<art.kerbWidth*.45f)continue;
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
            var rocks=map.Solids.Where(s=>!double.IsNaN(s.Top)).ToArray();float step=art.rubbleSize*2.3f;
            // Deterministic world grid with irregular jitter, not random global state.
            for(float z=-46;z<=46;z+=step)for(float x=-54;x<=54;x+=step){
                float h=Hash(x,z);if(h>art.rubbleDensity)continue;
                var q=new NavPoint(x+(Hash(x+7,z)-.5f)*step*.6f,z+(Hash(x,z+11)-.5f)*step*.6f);
                if(!rocks.Any(b=>b.BoundaryDistanceSquared(q)<art.rubbleBand*art.rubbleBand))continue;
                float size=art.rubbleSize*(.45f+.55f*h);
                if(BasaltCrownRoads.SignedDistance((float)q.X,(float)q.Z,art)<size+art.detailClearance)continue;
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
