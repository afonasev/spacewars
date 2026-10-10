using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    public static class TerrainMesh
    {
        // Geological dressing stays entirely inside the simulation solid. Tessellation
        // lengths/depth limits are mesh quality bounds, not gameplay dimensions.
        public static Mesh ErodedRock(NavObstacle shape,double bottom,double top,float erosion)
        {
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
                float n=Mathf.PerlinNoise(x*.27f+31,z*.27f+71)*.65f+Mathf.PerlinNoise(x*.79f+11,z*.79f+17)*.35f;
                return (float)top-Mathf.Min(erosion,(float)(top-bottom)*.4f)*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.2f,.8f,n));
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
                int layers=Mathf.Max(2,Mathf.CeilToInt((float)(top-bottom)/.65f));
                var inward=new Vector3(-(b.z-a.z),0,b.x-a.x).normalized;
                Vector3 Wall(float along,float level)
                {
                    var crest=Vector3.Lerp(ra,rb,along);float roof=Roof(crest.x,crest.z);
                    var p=Vector3.Lerp(Vector3.Lerp(a,b,along),crest,level);
                    float cut=Mathf.Sin(level*Mathf.PI)*Mathf.Sin(along*Mathf.PI)*erosion*.28f*(.2f+.8f*Mathf.PerlinNoise(p.x*.9f+level*7,p.z*.9f));
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
        // Same validated polygon triangulation for renderer and route-provider bake.
        public static Mesh Prism(NavObstacle shape,double bottom,double top,System.Func<NavPoint,double> height=null)
        {
            var polygon=shape.Footprint;var vertices=new List<Vector3>();var indices=new List<int>();
            void Triangle(Vector3 a,Vector3 b,Vector3 c){int start=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);indices.Add(start);indices.Add(start+1);indices.Add(start+2);}
            Vector3 P(NavPoint p,bool upper)=>new Vector3((float)p.X,(float)(upper?(height==null?top:height(p)):bottom),(float)p.Z);
            for(int i=0;i<polygon.Triangles.Count;i+=3){var a=polygon.Vertices[polygon.Triangles[i]];var b=polygon.Vertices[polygon.Triangles[i+1]];var c=polygon.Vertices[polygon.Triangles[i+2]];Triangle(P(a,true),P(c,true),P(b,true));Triangle(P(a,false),P(b,false),P(c,false));}
            for(int i=0;i<polygon.Vertices.Count;i++){var a=polygon.Vertices[i];var b=polygon.Vertices[(i+1)%polygon.Vertices.Count];Triangle(P(a,false),P(a,true),P(b,true));Triangle(P(a,false),P(b,true),P(b,false));}
            var mesh=new Mesh{name="Shared terrain prism"};mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
}
