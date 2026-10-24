using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    public static partial class CrimsonGroveSurfaces
    {
        // These render meshes never enter terrain authority or receive colliders.
        public static Mesh SupportTop(MapSupport support)
        {
            var polygon=support.Bounds.Footprint;var vertices=new List<Vector3>();var indices=new List<int>();
            foreach(var p in polygon.Vertices)vertices.Add(new Vector3((float)p.X,(float)support.HeightAt(p),(float)p.Z));
            for(int i=0;i<polygon.Triangles.Count;i+=3){indices.Add(polygon.Triangles[i]);indices.Add(polygon.Triangles[i+2]);indices.Add(polygon.Triangles[i+1]);}
            var mesh=new Mesh{name="Unchanged authored support surface"};mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        public static Mesh SupportSides(MapSupport support,CrimsonGroveArtProfile art)
        {
            var polygon=support.Bounds.Footprint;var vertices=new List<Vector3>();var indices=new List<int>();
            void Tri(Vector3 a,Vector3 b,Vector3 c){int n=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);indices.AddRange(new[]{n,n+1,n+2});}
            for(int edge=0;edge<polygon.Vertices.Count;edge++){
                var a=polygon.Vertices[edge];var b=polygon.Vertices[(edge+1)%polygon.Vertices.Count];
                var av=new Vector3((float)a.X,0,(float)a.Z);var bv=new Vector3((float)b.X,0,(float)b.Z);
                Vector3 CornerNormal(int index){
                    var p=polygon.Vertices[(index+polygon.Vertices.Count-1)%polygon.Vertices.Count];var q=polygon.Vertices[index];var r=polygon.Vertices[(index+1)%polygon.Vertices.Count];
                    var incoming=new Vector2((float)(q.X-p.X),(float)(q.Z-p.Z)).normalized;var outgoing=new Vector2((float)(r.X-q.X),(float)(r.Z-q.Z)).normalized;
                    return new Vector3(-incoming.y-outgoing.y,0,incoming.x+outgoing.x).normalized;
                }
                var startNormal=CornerNormal(edge);var endNormal=CornerNormal((edge+1)%polygon.Vertices.Count);
                int steps=Math.Max(1,Mathf.CeilToInt(Vector3.Distance(av,bv)/art.rampFacetWidth)*3);
                // Three samples per broad facet and twelve vertical bands are mesh quality,
                // not game tuning. Contact rings stay exactly on the original boundary.
                const int layers=12;
                Vector3 P(int j,int k){
                    float u=(float)j/steps,t=(float)k/layers;var v=Vector3.Lerp(av,bv,u);
                    float roof=(float)support.HeightAt(new NavPoint(v.x,v.z));v.y=Mathf.Lerp(-2,roof,t);
                    float broad=Mathf.PerlinNoise(v.x/art.rampFacetWidth+37,v.z/art.rampFacetWidth+53);
                    float strata=Mathf.Repeat((v.y+Mathf.PerlinNoise(v.x*.17f,v.z*.17f)*.3f)/art.benchHeight,1);
                    float cut=art.rampRecess*(.15f+.85f*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.25f,.7f,broad)))*(.3f+.7f*strata)*Mathf.Sin(t*Mathf.PI);
                    if(k==0||k==layers)cut=0;
                    var recessed=v+Vector3.Lerp(startNormal,endNormal,u).normalized*cut;
                    if(polygon.Contains(new NavPoint(recessed.x,recessed.z))&&recessed.y<=support.HeightAt(new NavPoint(recessed.x,recessed.z)))return recessed;
                    return v;
                }
                for(int j=0;j<steps;j++)for(int k=0;k<layers;k++){
                    Tri(P(j,k),P(j,k+1),P(j+1,k+1));Tri(P(j,k),P(j+1,k+1),P(j+1,k));
                }
            }
            var mesh=new Mesh{name="Recessed stratified support sides",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        private static List<Vector2> ClipPanel(List<Vector2> polygon,Vector2 normal,float limit)
        {
            var result=new List<Vector2>();if(polygon.Count==0)return result;
            var prev=polygon[polygon.Count-1];float pd=Vector2.Dot(prev,normal)-limit;
            foreach(var next in polygon){float nd=Vector2.Dot(next,normal)-limit;
                if((pd<=0)!=(nd<=0))result.Add(Vector2.Lerp(prev,next,pd/(pd-nd)));
                if(nd<=0)result.Add(next);prev=next;pd=nd;
            }return result;
        }
        private static List<Vector2> InsetPanel(List<Vector2> polygon,float margin)
        {
            var result=new List<Vector2>(polygon);
            for(int i=0;i<polygon.Count;i++){
                var a=polygon[i];var b=polygon[(i+1)%polygon.Count];var e=(b-a).normalized;
                var outward=new Vector2(e.y,-e.x);result=ClipPanel(result,outward,Vector2.Dot(a,outward)-margin);
            }return result;
        }
        public static Mesh PrefabApron(NavObstacle pad,TerritorySite site,CrimsonGroveMap map,CrimsonGroveArtProfile art)
        {
            // Convex pad is unchanged. Panel cells are clipped inside it; seams and trim
            // are flush decoration, never holes in the construction or movement support.
            var origin=new Vector2((float)site.Position.X,(float)site.Position.Z);
            var outline=pad.Footprint.Vertices.Select(p=>new Vector2((float)p.X,(float)p.Z)-origin).ToList();
            var vertices=new List<Vector3>();var indices=new List<int>();var colors=new List<Color>();
            float floor=(float)map.SurfaceHeight(site.Position)+.012f;
            void Face(List<Vector2> poly,float y,Color color){
                if(poly.Count<3)return;int start=vertices.Count;
                foreach(var p in poly){vertices.Add(new Vector3(p.x+origin.x,y,p.y+origin.y));colors.Add(color);}
                for(int i=1;i<poly.Count-1;i++)indices.AddRange(new[]{start,start+i+1,start+i});
            }
            void Disc(Vector2 centre,float radius,float y,Color color){
                var ring=new List<Vector2>();for(int i=0;i<12;i++){float a=i*Mathf.PI/6;ring.Add(centre+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius);}Face(ring,y,color);
            }
            Face(outline,floor,new Color(.51f,.57f,.63f,1));
            var inside=InsetPanel(outline,art.padBorder);
            var seeds=new List<Vector2>{Vector2.zero};seeds.AddRange(site.Slots.Select(s=>new Vector2((float)s.Position.X,(float)s.Position.Z)-origin));
            // Two extra edge panels break a radial wedge pattern without a repeated tile grid.
            foreach(int edge in new[]{0,outline.Count/2}){
                var point=(outline[edge]+outline[(edge+1)%outline.Count])*.38f;
                if(seeds.All(s=>Vector2.Distance(s,point)>art.padJointSize*.2f))seeds.Add(point);
            }
            for(int i=0;i<seeds.Count;i++){
                var cell=new List<Vector2>(inside);
                for(int j=0;j<seeds.Count;j++)if(i!=j){var n=seeds[j]-seeds[i];cell=ClipPanel(cell,n,(seeds[j].sqrMagnitude-seeds[i].sqrMagnitude)*.5f);}
                cell=InsetPanel(cell,art.padSeam*.5f);if(cell.Count<3)continue;
                float tone=1+(Mathf.PerlinNoise(seeds[i].x+17,seeds[i].y+29)-.5f)*art.padWear;
                Face(cell,floor+.006f,new Color(tone,tone,tone,1));
                var centre=cell.Aggregate(Vector2.zero,(sum,p)=>sum+p)/cell.Count;
                for(int corner=0;corner<cell.Count;corner+=Math.Max(2,cell.Count/2)){
                    var bolt=Vector2.MoveTowards(cell[corner],centre,art.padFastener*4);
                    bool fits=true;for(int k=0;k<cell.Count;k++){
                        var a=cell[k];var e=cell[(k+1)%cell.Count]-a;float cross=e.x*(bolt.y-a.y)-e.y*(bolt.x-a.x);
                        if(cross<art.padFastener*1.5f*e.magnitude){fits=false;break;}
                    }if(!fits)continue;
                    Disc(bolt,art.padFastener,floor+.009f,new Color(.24f,.28f,.32f,1));
                    Disc(bolt,art.padFastener*.65f,floor+.010f,new Color(.67f,.7f,.73f,1));
                    Disc(bolt,art.padFastener*.33f,floor+.011f,new Color(.28f,.3f,.32f,1));
                }
            }
            var mesh=new Mesh{name="Complete flush prefab apron"};mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
}
