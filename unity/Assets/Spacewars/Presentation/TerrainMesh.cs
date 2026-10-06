using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    internal static class TerrainMesh
    {
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
