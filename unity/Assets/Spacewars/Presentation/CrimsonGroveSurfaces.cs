using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    // Cosmetic geometry derived from unchanged authored blockers and construction slots.
    public static partial class CrimsonGroveSurfaces
    {
        public static float CrestHeight(NavObstacle shape,CrimsonGroveArtProfile art,float x,float z){
            float n=Mathf.PerlinNoise(x/12+31,z/12+71);
            return (float)shape.Top-Mathf.Min(art.erosion,(float)shape.Top*.4f)*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.2f,.8f,n));
        }
        public static Mesh LayeredRock(NavObstacle shape,CrimsonGroveArtProfile art,out TerrainMesh.RockCap cap)
        {
            double bottom=0,top=shape.Top;float erosion=art.erosion;
            var vertices=new List<Vector3>();var indices=new List<int>();
            var polygon=shape.Footprint;
            // A validated irregular inset gives sloping fractured faces, not vertical
            // extrusions. Narrow concave masses reduce the inset until it fits.
            NavPolygon roofPolygon=polygon;
            for(float inset=erosion;inset>.001f;inset*=.5f)
            {
                var ring=new List<NavPoint>();var v=polygon.Vertices;
                for(int i=0;i<v.Count;i++)
                {
                    var a=v[(i+v.Count-1)%v.Count];var b=v[i];var c=v[(i+1)%v.Count];
                    var incoming=new Vector2((float)(b.X-a.X),(float)(b.Z-a.Z)).normalized;
                    var outgoing=new Vector2((float)(c.X-b.X),(float)(c.Z-b.Z)).normalized;
                    var normal=new Vector2(-incoming.y-outgoing.y,incoming.x+outgoing.x);
                    float distance=inset*(.45f+.55f*Mathf.PerlinNoise((float)b.X*.31f+17,(float)b.Z*.31f+23));
                    normal*=distance/Mathf.Max(.1f,1+Vector2.Dot(incoming,outgoing));
                    ring.Add(new NavPoint(b.X+normal.x,b.Z+normal.y));
                }
                try{var candidate=new NavPolygon(ring);if(polygon.StrictlyContains(candidate)){roofPolygon=candidate;break;}}
                catch(System.ArgumentException){ /* Retry a smaller, safe inset. */ }
            }
            float Roof(float x,float z)
            {
                return CrestHeight(shape,art,x,z);
            }
            Vector3 P(NavPoint p)=>new Vector3((float)p.X,Roof((float)p.X,(float)p.Z),(float)p.Z);
            void Tri(Vector3 a,Vector3 b,Vector3 c)
            {int i=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);indices.Add(i);indices.Add(i+1);indices.Add(i+2);}
            void Cap(Vector3 a,Vector3 b,Vector3 c,int depth)
            {
                if(depth<4)
                {
                    Vector3 Mid(Vector3 u,Vector3 v){var m=(u+v)*.5f;m.y=Roof(m.x,m.z);return m;}
                    var ab=Mid(a,b);var bc=Mid(b,c);var ca=Mid(c,a);
                    Cap(a,ab,ca,depth+1);Cap(ab,b,bc,depth+1);Cap(ca,bc,c,depth+1);Cap(ab,bc,ca,depth+1);
                }
                else Tri(a,c,b);
            }
            for(int i=0;i<roofPolygon.Triangles.Count;i+=3)
                Cap(P(roofPolygon.Vertices[roofPolygon.Triangles[i]]),P(roofPolygon.Vertices[roofPolygon.Triangles[i+1]]),P(roofPolygon.Vertices[roofPolygon.Triangles[i+2]]),0);
            cap=new TerrainMesh.RockCap(roofPolygon,vertices);
            for(int i=0;i<polygon.Triangles.Count;i+=3)
            {
                var a=P(polygon.Vertices[polygon.Triangles[i]]);var b=P(polygon.Vertices[polygon.Triangles[i+1]]);var c=P(polygon.Vertices[polygon.Triangles[i+2]]);
                a.y=b.y=c.y=(float)bottom;Tri(a,b,c);
            }
            for(int i=0;i<polygon.Vertices.Count;i++)
            {
                var a=P(polygon.Vertices[i]);var b=P(polygon.Vertices[(i+1)%polygon.Vertices.Count]);
                var ra=P(roofPolygon.Vertices[i]);var rb=P(roofPolygon.Vertices[(i+1)%polygon.Vertices.Count]);
                int steps=16; // Matches four cap subdivisions, avoiding T-junction cracks.
                int layers=Mathf.Max(8,Mathf.CeilToInt((float)(top-bottom)/art.benchHeight)*3);
                var inward=new Vector3(-(b.z-a.z),0,b.x-a.x).normalized;
                Vector3 Wall(float along,float level)
                {
                    var crest=Vector3.Lerp(ra,rb,along);float roof=Roof(crest.x,crest.z);
                    // Continuous aperiodic horizontal benches, cut inward only. The base and
                    // roof rings remain exact, and no dressing crosses the simulation footprint.
                    var p=Vector3.Lerp(Vector3.Lerp(a,b,along),crest,level);
                    float altitude=Mathf.Lerp((float)bottom,roof,level);
                    float warp=(Mathf.PerlinNoise(p.x*.11f+17,p.z*.11f+41)-.5f)*.6f;
                    float bed=(altitude+warp)/art.benchHeight;
                    float lip=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.08f,.42f,Mathf.Repeat(bed,1)));
                    float block=Mathf.PerlinNoise(p.x*.6f+Mathf.Floor(bed)*13,p.z*.6f+7);
                    float cut=Mathf.Sin(level*Mathf.PI)*Mathf.Sin(along*Mathf.PI)*art.benchDepth*(.25f+.55f*lip+.65f*block);
                    var shifted=p+inward*cut;
                    if(shape.Contains(new NavPoint(shifted.x,shifted.z))){p.x=shifted.x;p.z=shifted.z;}
                    p.y=Mathf.Lerp((float)bottom,roof,level);return p;
                }
                for(int j=0;j<steps;j++)for(int k=0;k<layers;k++)
                {
                    float u=(float)j/steps,v=(float)(j+1)/steps,lo=(float)k/layers,hi=(float)(k+1)/layers;
                    Tri(Wall(u,lo),Wall(u,hi),Wall(v,hi));Tri(Wall(u,lo),Wall(v,hi),Wall(v,lo));
                }
            }
            var mesh=new Mesh{name="Eroded geological solid",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
            mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        public static NavObstacle TrimBorder(CrimsonGroveMap map,NavObstacle border,CrimsonGroveArtProfile art){
            double inner=border.Footprint.Vertices.Min(p=>Math.Sqrt(p.X*p.X+p.Z*p.Z));
            var points=border.Footprint.Vertices.Select(p=>{double radius=Math.Sqrt(p.X*p.X+p.Z*p.Z),scale=Math.Min(1,(inner+art.outerRimWidth)/radius);return new NavPoint(p.X*scale,p.Z*scale);});
            return new NavObstacle(new NavPolygon(points),border.Bottom,border.Top);
        }
        public static NavObstacle Pad(TerritorySite site,PlayableProfile profile,CrimsonGroveArtProfile art){
            var points=new List<NavPoint>();
            void Reserve(NavPoint p,double radius){
                // Circumscribed octagons contain the whole square footprint at any slot heading.
                double r=(radius*Math.Sqrt(2)+art.padMargin)/Math.Cos(Math.PI/8);
                for(int i=0;i<8;i++){double a=(i+.5)*Math.PI/4;points.Add(new NavPoint(p.X+Math.Cos(a)*r,p.Z+Math.Sin(a)*r));}
            }
            Reserve(site.Position,TerritoryRules.Radius(profile,site.Kind));foreach(var slot in site.Slots)Reserve(slot.Position,profile.OrdinaryPadRadius);
            var sorted=points.OrderBy(p=>p.X).ThenBy(p=>p.Z).Distinct().ToArray();var hull=new List<NavPoint>();
            double Cross(NavPoint a,NavPoint b,NavPoint c)=>(b.X-a.X)*(c.Z-a.Z)-(b.Z-a.Z)*(c.X-a.X);
            foreach(var p in sorted){while(hull.Count>=2&&Cross(hull[hull.Count-2],hull[hull.Count-1],p)<=0)hull.RemoveAt(hull.Count-1);hull.Add(p);}
            int lower=hull.Count;for(int i=sorted.Length-2;i>=0;i--){var p=sorted[i];while(hull.Count>lower&&Cross(hull[hull.Count-2],hull[hull.Count-1],p)<=0)hull.RemoveAt(hull.Count-1);hull.Add(p);}hull.RemoveAt(hull.Count-1);
            return new NavObstacle(new NavPolygon(hull));
        }
        public static List<NavPoint[]> Routes(CrimsonGroveMap map){
            var paths=new List<NavPoint[]>();
            for(int i=0;i<8;i++){
                double a=22.5+45*i;paths.Add(new[]{map.Polar(a,128),map.Polar(a-6,145)});
                paths.Add(new[]{map.Polar(a,128),map.Polar(a+20,112)});
                paths.Add(new[]{map.Polar(a,128),map.Polar(a,78)});paths.Add(new[]{map.Polar(a+20,112),map.Polar(a+20,78)});
            }
            // Cosmetic remnants of the old perimeter connection, through the existing pockets.
            for(int i=0;i<8;i++){double a=22.5+45*i;paths.Add(new[]{map.Polar(a,128),map.Polar(a+10,121),map.Polar(a+20,112),map.Polar(a+32,117),map.Polar(a+45,128)});}
            // The approved inner and transverse central passages, repeated around the basin.
            for(int i=0;i<4;i++){
                double a=90*i;paths.Add(new[]{map.Polar(a,54),map.Polar(a+10,62),map.Polar(a+30,62),map.Polar(a+45,62)});
                paths.Add(new[]{map.Polar(a,54),map.Polar(a,30),map.Polar(a+7,19),map.Polar(a+46,19),map.Polar(a+45,34),map.Polar(a+45,62)});
            }
            return paths;
        }
        public static double RouteDistance(NavPoint p,List<NavPoint[]> routes){
            double best=double.MaxValue;foreach(var line in routes)for(int i=1;i<line.Length;i++){
                var a=line[i-1];var b=line[i];double dx=b.X-a.X,dz=b.Z-a.Z;double t=Math.Max(0,Math.Min(1,((p.X-a.X)*dx+(p.Z-a.Z)*dz)/(dx*dx+dz*dz)));
                double x=p.X-a.X-dx*t,z=p.Z-a.Z-dz*t;best=Math.Min(best,Math.Sqrt(x*x+z*z));
            }return best;
        }
    }
}
