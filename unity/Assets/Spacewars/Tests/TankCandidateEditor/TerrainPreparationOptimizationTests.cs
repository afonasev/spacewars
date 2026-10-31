using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using Object=UnityEngine.Object;

public sealed class TerrainPreparationOptimizationTests
{
    [Test] public void PreparedDistancesMatchNaivePolygonQueriesIncludingConcaveEdges()
    {
        var shapes=new[]{new NavObstacle(-9,-2,-3,7),new NavObstacle(4,-8,9,-3),new NavObstacle(new NavPolygon(new[]{new NavPoint(-2,-3),new NavPoint(3,-3),new NavPoint(3,1),new NavPoint(0,1),new NavPoint(0,6),new NavPoint(-2,6)}))};
        var polygons=shapes.Select(s=>s.Footprint).ToArray();var query=new TerrainPolygonDistances(shapes);
        void Check(NavPoint p){double expected=polygons.Min(s=>Math.Sqrt(s.DistanceSquared(p)));Assert.AreEqual(BitConverter.DoubleToInt64Bits(expected),BitConverter.DoubleToInt64Bits(query.Minimum(p)),p.X+","+p.Z);}
        for(int z=-40;z<=40;z++)for(int x=-40;x<=40;x++)Check(new NavPoint(x*.31,z*.29));
        foreach(var polygon in polygons)foreach(var p in polygon.Vertices)foreach(double delta in new[]{-1e-10,0,1e-10})Check(new NavPoint(p.X+delta,p.Z-delta));
        Assert.AreEqual(double.MaxValue,new TerrainPolygonDistances(Array.Empty<NavObstacle>()).Minimum(new NavPoint(0,0)));
    }
    [TestCase(0f)][TestCase(1.75f)] public void RoadBroadPhaseRetainsEveryPotentiallyPaintedSample(float meander)
    {
        foreach(float width in new[]{4.2f,12f}){
            var p=new ThreeCrossingsSurfaceProfile{roadMeander=meander,arterialWidth=width};
            var roads=ThreeCrossingsMaterials.Segments(p).Concat(new[]{new ThreeCrossingsMaterials.Segment(new NavPoint(-19.7,-26.5),new NavPoint(39.2,31.7),135.7)});
            foreach(var road in roads){var bounds=new ThreeCrossingsMaterials.RoadPaintBounds(road,p);
                for(float wz=-65;wz<=65;wz+=1.7f)for(float wx=-65;wx<=65;wx+=1.9f){
                    if(bounds.Contains(wx,wz))continue;
                    double dx=road.B.X-road.A.X,dz=road.B.Z-road.A.Z,length=dx*dx+dz*dz;
                    double t=length>0?Math.Max(0,Math.Min(1,((wx-road.A.X)*dx+(wz-road.A.Z)*dz)/length)):0;
                    double cx=road.A.X+dx*t,cz=road.A.Z+dz*t;float alongLength=(float)Math.Sqrt(length);
                    float endFade=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,4,(float)Math.Min(t,1-t)*alongLength));
                    float along=(float)(road.StartDistance+t*alongLength),phase=(float)(road.A.X*.17+road.A.Z*.11);
                    float drift=(Mathf.Sin(along/13+phase)*.65f+(Mathf.PerlinNoise((float)((cx+143.7)/12),(float)((cz-87.2)/12))-.5f)*.7f)*p.roadMeander*endFade;
                    double lateral=length>0?drift/Math.Sqrt(length):0,ax=wx-(cx-dz*lateral),az=wz-(cz+dx*lateral);
                    Assert.LessOrEqual(p.arterialWidth/2-(float)Math.Sqrt(ax*ax+az*az),0,"Culled original road sample must paint zero");
                }
            }
        }
    }
    [TestCase(6)][TestCase(9000)] public void BufferedMeshIdentityPreservesCompleteExpandedByteStream(int count)
    {
        var mesh=new Mesh();try{
            mesh.vertices=Enumerable.Range(0,count).Select(i=>new Vector3(i*.013f,i%7,i%11*.03f)).ToArray();mesh.normals=Enumerable.Repeat(Vector3.up,count).ToArray();
            mesh.uv=Enumerable.Range(0,count).Select(i=>new Vector2(i%3*.1f,i%5*.2f)).ToArray();mesh.colors=Enumerable.Range(0,count).Select(i=>new Color(i%5*.2f,.2f,.7f,1)).ToArray();
            mesh.subMeshCount=2;mesh.SetIndices(Enumerable.Range(0,count/2).ToArray(),MeshTopology.Triangles,0);mesh.SetIndices(Enumerable.Range(count/2,count/2).Reverse().ToArray(),MeshTopology.Triangles,1);
            // Reference writes the original complete format into memory, independently
            // of buffering/CryptoStream. The large case crosses multiple buffer blocks.
            string Reference(){using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,System.Text.Encoding.UTF8,true)){
                foreach(var a in mesh.GetVertexAttributes()){writer.Write((int)a.attribute);writer.Write((int)a.format);writer.Write(a.dimension);writer.Write(a.stream);}
                using var data=Mesh.AcquireReadOnlyMeshData(mesh);var streams=new byte[mesh.vertexBufferCount][];
                for(int s=0;s<streams.Length;s++)streams[s]=data[0].GetVertexData<byte>(s).ToArray();
                for(int sub=0;sub<mesh.subMeshCount;sub++){writer.Write((int)mesh.GetTopology(sub));var indices=mesh.GetIndices(sub,true);writer.Write(indices.Length);foreach(int index in indices)for(int s=0;s<streams.Length;s++)writer.Write(streams[s],index*mesh.GetVertexBufferStride(s),mesh.GetVertexBufferStride(s));}}
                using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-","").ToLowerInvariant();}
            Assert.AreEqual(Reference(),ModelDetailCatalog.Identity(mesh));
            var changed=mesh.vertices;changed[0].x+=.001f;mesh.vertices=changed;Assert.AreEqual(Reference(),ModelDetailCatalog.Identity(mesh));
        }finally{Object.DestroyImmediate(mesh);}
    }
}
