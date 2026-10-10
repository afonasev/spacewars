using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Sculpted presentation only. The base is the complete authoritative footprint.
    public static class FoundryTerrainMesh
    {
        public static IEnumerable<NavObstacle> Ridges(FoundryMap map)
        {
            for(int i=0;i<map.Solids.Count;i++)
            {
                if(i>=6&&i<14){yield return Join(map.Solids[i],map.Solids[++i]);}
                else yield return map.Solids[i];
            }
        }
        public static NavObstacle Join(NavObstacle a,NavObstacle b)
        {
            var xs=new[]{a.MinX,a.MaxX,b.MinX,b.MaxX}.Distinct().OrderBy(x=>x).ToArray();
            var zs=new[]{a.MinZ,a.MaxZ,b.MinZ,b.MaxZ}.Distinct().OrderBy(z=>z).ToArray();
            var edges=new Dictionary<NavPoint,NavPoint>();
            bool Filled(int x,int z)=>x>=0&&z>=0&&x<xs.Length-1&&z<zs.Length-1&&(a.Contains(new NavPoint((xs[x]+xs[x+1])/2,(zs[z]+zs[z+1])/2))||b.Contains(new NavPoint((xs[x]+xs[x+1])/2,(zs[z]+zs[z+1])/2)));
            for(int x=0;x<xs.Length-1;x++)for(int z=0;z<zs.Length-1;z++)if(Filled(x,z))
            {
                var lo=new NavPoint(xs[x],zs[z]);var right=new NavPoint(xs[x+1],zs[z]);var hi=new NavPoint(xs[x+1],zs[z+1]);var left=new NavPoint(xs[x],zs[z+1]);
                if(!Filled(x,z-1))edges.Add(lo,right);if(!Filled(x+1,z))edges.Add(right,hi);
                if(!Filled(x,z+1))edges.Add(hi,left);if(!Filled(x-1,z))edges.Add(left,lo);
            }
            var start=edges.Keys.OrderBy(p=>p.X).ThenBy(p=>p.Z).First();var ring=new List<NavPoint>();var cursor=start;
            do{ring.Add(cursor);cursor=edges[cursor];if(ring.Count>edges.Count)throw new ArgumentException("Disconnected ridge union");}while(!cursor.Equals(start));
            if(ring.Count!=edges.Count)throw new ArgumentException("Multiple ridge boundaries");
            // Remove collinear grid junctions before triangulating the single L boundary.
            var corners=ring.Where((p,i)=>{var previous=ring[(i+ring.Count-1)%ring.Count];var next=ring[(i+1)%ring.Count];return Math.Abs((p.X-previous.X)*(next.Z-p.Z)-(p.Z-previous.Z)*(next.X-p.X))>1e-9;});
            return new NavObstacle(new NavPolygon(corners),Math.Min(a.Bottom,b.Bottom),Math.Min(a.Top,b.Top));
        }
        // A six-sided circumscribed outline contains the entire square envelope,
        // rather than trimming its corners or changing any construction rules.
        public static NavObstacle HomePad(NavPoint center,double envelope,FoundrySurfaceProfile settings)
        {
            double x=envelope*settings.homeHexExtent,z=x;
            return new NavObstacle(new NavPolygon(new[]{
                new NavPoint(center.X-x,center.Z),new NavPoint(center.X-x/2,center.Z-z),
                new NavPoint(center.X+x/2,center.Z-z),new NavPoint(center.X+x,center.Z),
                new NavPoint(center.X+x/2,center.Z+z),new NavPoint(center.X-x/2,center.Z+z)}));
        }
        public static float RoadCoverage(FoundryMap map,NavPoint point,FoundrySurfaceProfile settings)
        {
            double distance=0;
            foreach(var road in map.Roads)
            {
                bool horizontal=road.MaxX-road.MinX>road.MaxZ-road.MinZ;
                float along=(float)(horizontal?point.X:point.Z),cross=(float)(horizontal?point.Z:point.X);
                float offset=(Mathf.PerlinNoise(along/settings.roadBendScale+17,cross/settings.roadBendScale*.2f+53)-.5f)*2*settings.roadMeander;
                var q=horizontal?new NavPoint(point.X,point.Z+offset):new NavPoint(point.X+offset,point.Z);
                if(road.Contains(q))distance=Math.Max(distance,Math.Sqrt(road.BoundaryDistanceSquared(q)));
            }
            float width=.6f+.8f*Mathf.PerlinNoise((float)point.X/settings.roadBendScale+71,(float)point.Z/settings.roadBendScale+11);
            return Mathf.SmoothStep(0,1,(float)distance/(settings.roadFeather*width));
        }
        public static float Roof(float x,float z,double bottom,double top,FoundrySurfaceProfile settings)
        {
            float macro=Mathf.PerlinNoise(x/settings.crownScale+31,z/settings.crownScale+71);
            float layer=Mathf.PerlinNoise(x/settings.crownScale*2+11,z/settings.crownScale*2+17);
            return (float)top-Mathf.Min(settings.crownRelief,(float)(top-bottom)*.35f)*(.2f+.8f*(macro*.75f+layer*.25f));
        }
        public static Mesh Rock(NavObstacle shape,FoundrySurfaceProfile settings,System.Collections.Generic.IReadOnlyList<NavObstacle> lava=null,Func<NavPoint,double> supportHeight=null)
        {
            var polygon=shape.Footprint;var vertices=new List<Vector3>();var triangles=new List<int>();
            // Axis-aligned authored solids are tessellated into bounded quads, avoiding
            // long ear-clipped slivers and a different height interpolation on adjoining caps.
            double[] Grid(IEnumerable<double> source)
            {
                var knots=source.Distinct().OrderBy(v=>v).ToArray();var values=new List<double>();
                for(int i=0;i<knots.Length-1;i++)
                {int n=Math.Max(1,(int)Math.Ceiling((knots[i+1]-knots[i])/1.5));for(int j=0;j<n;j++)values.Add(knots[i]+(knots[i+1]-knots[i])*j/n);}
                values.Add(knots.Last());return values.ToArray();
            }
            var xs=Grid(polygon.Vertices.Select(p=>p.X));var zs=Grid(polygon.Vertices.Select(p=>p.Z));
            void Tri(Vector3 a,Vector3 b,Vector3 c){int i=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);triangles.Add(i);triangles.Add(i+1);triangles.Add(i+2);}
            float deformation=1;
            Vector3 Top(NavPoint p)
            {
                float boundary=(float)Math.Sqrt(polygon.BoundaryDistanceSquared(p));
                float h=Roof((float)p.X,(float)p.Z,shape.Bottom,shape.Top,settings);
                float fracture=Mathf.PerlinNoise((float)p.X/settings.crownScale+41,(float)p.Z/settings.crownScale+97);
                // A height field cannot fold its cap or open an L-corner slit.
                // The complete footprint meets a visible low basalt foot, while
                // variable shoulder widths make the high ridge meander inside it.
                float width=settings.shoulderWidth*(.4f+.6f*fracture);
                float foot=(float)(supportHeight?.Invoke(p)??shape.Bottom)+settings.footHeight;
                foot=Mathf.Min(foot,(float)shape.Top-settings.crownRelief);
                h=Mathf.Lerp(foot,h,Mathf.SmoothStep(0,1,boundary/width));
                var position=new Vector2((float)p.X,(float)p.Z);var inset=Vector2.zero;
                for(int edge=0;edge<polygon.Vertices.Count;edge++)
                {
                    var a=polygon.Vertices[edge];var b=polygon.Vertices[(edge+1)%polygon.Vertices.Count];
                    var direction=new Vector2((float)(b.X-a.X),(float)(b.Z-a.Z)).normalized;
                    var normal=new Vector2(-direction.y,direction.x);
                    var relative=position-new Vector2((float)a.X,(float)a.Z);
                    float along=Vector2.Dot(relative,direction),distance=Vector2.Dot(relative,normal);
                    float length=Vector2.Distance(new Vector2((float)a.X,(float)a.Z),new Vector2((float)b.X,(float)b.Z));
                    if(along>=-.001f&&along<=length+.001f&&distance>=-.001f&&distance<settings.shoulderWidth*2)
                        inset+=normal*settings.shoulderWidth*(.15f+.6f*fracture)*(1-Mathf.SmoothStep(0,1,distance/(settings.shoulderWidth*2)));
                }
                var candidate=position+inset*deformation;
                if(polygon.Contains(new NavPoint(candidate.x,candidate.y)))position=candidate;
                if(lava!=null)
                {
                    double distance=double.MaxValue;foreach(var channel in lava)distance=Math.Min(distance,Math.Sqrt(channel.Footprint.DistanceSquared(p)));
                    // Molten channels retain a continuous level surface; rock meets their cooled rim.
                    float rim=Mathf.SmoothStep(0,1,(float)distance/settings.lavaCrustWidth);
                    position=Vector2.Lerp(new Vector2((float)p.X,(float)p.Z),position,rim);
                    h=Mathf.Lerp((float)shape.Top,h,rim);
                }
                return new Vector3(position.x,Mathf.Clamp(h,(float)shape.Bottom+.001f,(float)shape.Top),position.y);
            }
            // Fit the deformation to the local ridge thickness: reject any
            // reversed projected cap triangle before producing the mesh. This
            // retains large bank variation without folding narrow L-corners.
            for(int attempt=0;attempt<12;attempt++)
            {
                bool folded=false;
                for(int x=0;x<xs.Length-1&&!folded;x++)for(int z=0;z<zs.Length-1;z++)
                {
                    if(!polygon.Contains(new NavPoint((xs[x]+xs[x+1])/2,(zs[z]+zs[z+1])/2)))continue;
                    var a=Top(new NavPoint(xs[x],zs[z]));var b=Top(new NavPoint(xs[x+1],zs[z]));
                    var c=Top(new NavPoint(xs[x+1],zs[z+1]));var d=Top(new NavPoint(xs[x],zs[z+1]));
                    if(Vector3.Cross(c-a,b-a).y<=.001f||Vector3.Cross(d-a,c-a).y<=.001f){folded=true;break;}
                }
                if(!folded)break;deformation*=.5f;
            }
            for(int x=0;x<xs.Length-1;x++)for(int z=0;z<zs.Length-1;z++)
            {
                if(!polygon.Contains(new NavPoint((xs[x]+xs[x+1])/2,(zs[z]+zs[z+1])/2)))continue;
                var a=Top(new NavPoint(xs[x],zs[z]));var b=Top(new NavPoint(xs[x+1],zs[z]));var c=Top(new NavPoint(xs[x+1],zs[z+1]));var d=Top(new NavPoint(xs[x],zs[z+1]));
                Tri(a,c,b);Tri(a,d,c);
            }
            for(int i=0;i<polygon.Vertices.Count;i++)
            {
                var a=polygon.Vertices[i];var b=polygon.Vertices[(i+1)%polygon.Vertices.Count];
                var knots=a.X==b.X?zs.Where(z=>z>=Math.Min(a.Z,b.Z)&&z<=Math.Max(a.Z,b.Z)).Select(z=>new NavPoint(a.X,z)).ToArray():xs.Where(x=>x>=Math.Min(a.X,b.X)&&x<=Math.Max(a.X,b.X)).Select(x=>new NavPoint(x,a.Z)).ToArray();
                if(a.X>b.X||a.Z>b.Z)Array.Reverse(knots);
                var inward=new Vector3((float)(a.Z-b.Z),0,(float)(b.X-a.X)).normalized;
                int layers=Mathf.Max(4,Mathf.CeilToInt((float)(shape.Top-shape.Bottom)/settings.strataSpacing));
                for(int j=0;j<knots.Length-1;j++)
                {
                    Vector3 Wall(NavPoint q,float level)
                    {
                        var crown=Top(q);var floor=new Vector3((float)q.X,(float)shape.Bottom,(float)q.Z);var p=Vector3.Lerp(floor,crown,level);
                        float strata=.5f+.5f*Mathf.Sin((p.y/settings.strataSpacing+Mathf.PerlinNoise(p.x/settings.crownScale,p.z/settings.crownScale))*6.283185f);
                        var cut=p+inward*(settings.edgeErosion*.25f*Mathf.Sin(level*Mathf.PI)*strata);
                        // Corners remain at the authored edge, so the neighboring side joins exactly.
                        if(!q.Equals(a)&&!q.Equals(b)&&polygon.Contains(new NavPoint(cut.x,cut.z))){p.x=cut.x;p.z=cut.z;}return p;
                    }
                    for(int k=0;k<layers;k++)
                    {float lo=(float)k/layers,hi=(float)(k+1)/layers;Tri(Wall(knots[j],lo),Wall(knots[j],hi),Wall(knots[j+1],hi));Tri(Wall(knots[j],lo),Wall(knots[j+1],hi),Wall(knots[j+1],lo));}
                }
            }
            var mesh=new Mesh{name="Foundry connected layered basalt",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();
            // Weld presentation normals across coincident quad vertices; preserve large
            // geometry without exposing the technical rectangular tessellation grid.
            var normals=mesh.normals;var sums=new Dictionary<Vector3,Vector3>();
            Vector3 Key(Vector3 p)=>new Vector3(Mathf.Round(p.x*10000),Mathf.Round(p.y*10000),Mathf.Round(p.z*10000));
            for(int i=0;i<vertices.Count;i++){var key=Key(vertices[i]);sums.TryGetValue(key,out var n);sums[key]=n+normals[i];}
            for(int i=0;i<vertices.Count;i++)normals[i]=sums[Key(vertices[i])].normalized;
            mesh.normals=normals;mesh.RecalculateBounds();return mesh;
        }
    }
}
