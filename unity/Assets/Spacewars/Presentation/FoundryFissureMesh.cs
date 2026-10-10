using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation {
// Exact presentation clipping, independent of authoritative support/solid volumes.
public static class FoundryFissureMesh {
readonly struct Vertex {
public readonly double x,y,z;
public Vertex(Vector3 p){x=p.x;y=p.y;z=p.z;}
Vertex(double x,double y,double z){this.x=x;this.y=y;this.z=z;}
public static Vertex Lerp(Vertex a,Vertex b,double t)=>new Vertex(a.x+(b.x-a.x)*t,a.y+(b.y-a.y)*t,a.z+(b.z-a.z)*t);
public Vector3 Vector=>new Vector3((float)x,(float)y,(float)z);
}
static double Side(Vertex p,NavPoint a,NavPoint b)=>(b.X-a.X)*(p.z-a.Z)-(b.Z-a.Z)*(p.x-a.X);
static List<Vertex> Clip(List<Vertex> polygon,NavPoint a,NavPoint b,bool inside) {
var result=new List<Vertex>();if(polygon.Count==0)return result;
var previous=polygon[polygon.Count-1];double pd=Side(previous,a,b);bool pin=inside?pd>=0:pd<=0;
foreach(var current in polygon) {double cd=Side(current,a,b);bool cin=inside?cd>=0:cd<=0;
if(pin!=cin)result.Add(Vertex.Lerp(previous,current,pd/(pd-cd)));
if(cin)result.Add(current);previous=current;pd=cd;pin=cin;
}return result;
}
public static IEnumerable<Vector3[]> Exclude(Vector3 a,Vector3 b,Vector3 c,IReadOnlyList<NavObstacle> channels) {
var pieces=new List<List<Vertex>>{new List<Vertex>{new Vertex(a),new Vertex(b),new Vertex(c)}};
foreach(var channel in channels) {
var polygon=channel.Footprint;
for(int i=0;i<polygon.Triangles.Count;i+=3) {
var corners=new[]{polygon.Vertices[polygon.Triangles[i]],polygon.Vertices[polygon.Triangles[i+1]],polygon.Vertices[polygon.Triangles[i+2]]};
// Normalize each cutter to CCW in XZ, regardless of triangulator winding.
if((corners[1].X-corners[0].X)*(corners[2].Z-corners[0].Z)-(corners[1].Z-corners[0].Z)*(corners[2].X-corners[0].X)<0){var t=corners[1];corners[1]=corners[2];corners[2]=t;}
var next=new List<List<Vertex>>();
foreach(var piece in pieces) {var remainder=piece;
for(int edge=0;edge<3&&remainder.Count>=3;edge++) {var outside=Clip(remainder,corners[edge],corners[(edge+1)%3],false);if(outside.Count>=3)next.Add(outside);remainder=Clip(remainder,corners[edge],corners[(edge+1)%3],true);}
}pieces=next;
}
}
foreach(var piece in pieces)for(int i=1;i<piece.Count-1;i++) {
// Fixed numerical area tolerance rejects clipping slivers, not art geometry.
var u=piece[0].Vector;var v=piece[i].Vector;var w=piece[i+1].Vector;
if(Mathf.Abs(Vector3.Cross(v-u,w-u).y)>1e-7f)yield return new[]{u,v,w};
}
}
public static NavObstacle RidgeFor(FoundryMap map,NavObstacle channel)=>FoundryTerrainMesh.Ridges(map).First(r=>channel.Footprint.Vertices.All(r.Contains));
public static float Level(FoundryMap map,NavObstacle channel,FoundrySurfaceProfile settings)=>(float)RidgeFor(map,channel).Top-settings.lavaDepth;
}}
