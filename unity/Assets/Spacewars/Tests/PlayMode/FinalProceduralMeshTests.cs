using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

public sealed class FinalProceduralMeshTests
{
    static Mesh Example(string seam="")
    {
        var m=new Mesh{indexFormat=IndexFormat.UInt32};
        m.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up,Vector3.zero,Vector3.right,Vector3.up};
        var normals=Enumerable.Repeat(Vector3.forward,6).ToArray();if(seam=="normal")normals[3]=Vector3.back;m.normals=normals;
        var tangents=Enumerable.Repeat(new Vector4(1,0,0,1),6).ToArray();if(seam=="tangent")tangents[3].w=-1;m.tangents=tangents;
        var colors=Enumerable.Repeat(Color.white,6).ToArray();if(seam=="color")colors[3]=Color.red;m.colors=colors;
        var uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.zero,Vector2.right,Vector2.up};if(seam=="uv")uv[3]=Vector2.one;m.uv=uv;
        var uv1=(Vector2[])uv.Clone();if(seam=="uv1")uv1[3]=Vector2.one;m.uv2=uv1;
        m.subMeshCount=2;m.SetIndices(new[]{0,1,2},MeshTopology.Triangles,0);m.SetIndices(new[]{3,4,5},MeshTopology.Triangles,1);
        // Deliberately loose authored bounds must survive without recalculation.
        m.bounds=new Bounds(Vector3.one,new Vector3(5,6,7));return m;
    }
    static byte[] Corners(Mesh m)
    {
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        var positions=m.vertices;var normals=m.normals;var tangents=m.tangents;var colors=m.colors;var uv=m.uv;var uv1=m.uv2;
        void V(float value)=>writer.Write(BitConverter.SingleToInt32Bits(value));
        for(int sub=0;sub<m.subMeshCount;sub++){
            writer.Write((int)m.GetTopology(sub));var ids=m.GetIndices(sub,true);writer.Write(ids.Length);
            foreach(int i in ids){V(positions[i].x);V(positions[i].y);V(positions[i].z);V(normals[i].x);V(normals[i].y);V(normals[i].z);V(tangents[i].x);V(tangents[i].y);V(tangents[i].z);V(tangents[i].w);V(colors[i].r);V(colors[i].g);V(colors[i].b);V(colors[i].a);V(uv[i].x);V(uv[i].y);V(uv1[i].x);V(uv1[i].y);}
        }
        writer.Flush();return stream.ToArray();
    }
    [TestCase("")][TestCase("normal")][TestCase("tangent")][TestCase("color")][TestCase("uv")][TestCase("uv1")]
    public void CompleteAttributeSeamsTriangleOrderSubmeshesAndAuthoredBoundsSurvive(string seam)
    {
        var m=Example(seam);try{
            var before=Corners(m);var bounds=m.bounds;var subBounds=Enumerable.Range(0,2).Select(s=>m.GetSubMesh(s).bounds).ToArray();
            FinalProceduralMesh.IndexOwned(m);Assert.AreEqual(seam==""?3:4,m.vertexCount);Assert.AreEqual(IndexFormat.UInt16,m.indexFormat);
            CollectionAssert.AreEqual(before,Corners(m));Assert.AreEqual(bounds,m.bounds);Assert.AreEqual(2,m.subMeshCount);
            for(int s=0;s<2;s++)Assert.AreEqual(subBounds[s],m.GetSubMesh(s).bounds);
            var changed=m.vertices;changed[0].x=BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(changed[0].x)+1);m.vertices=changed;
            CollectionAssert.AreNotEqual(before,Corners(m),"One-bit attribute negative control must be observable");
        }finally{Object.DestroyImmediate(m);}
    }
    [Test] public void UInt32IsRetainedWhenDistinctRecordsDoNotFitUInt16()
    {
        var m=new Mesh{indexFormat=IndexFormat.UInt32};try{
            var vertices=Enumerable.Range(0,65536).Select(i=>new Vector3(i,0,0)).ToArray();m.vertices=vertices;m.SetIndices(new[]{0,65534,65535},MeshTopology.Triangles,0);
            FinalProceduralMesh.IndexOwned(m);Assert.AreEqual(IndexFormat.UInt32,m.indexFormat);CollectionAssert.AreEqual(vertices,m.vertices);CollectionAssert.AreEqual(new[]{0,65534,65535},m.GetIndices(0));
        }finally{Object.DestroyImmediate(m);}
    }
    [Test] public void HashCollisionStillComparesCompleteBytesAndRetainsFirstOccurrenceOrder()
    {
        // These distinct Float32 position records share FNV1a hash 3085470341.
        float F(uint bits)=>BitConverter.Int32BitsToSingle(unchecked((int)bits));
        var a=new Vector3(F(1117355678),F(3267379123),F(1098373885));var b=new Vector3(F(1111253830),F(3260151604),F(1119142354));
        var m=new Mesh();try{
            m.vertices=new[]{a,b,a,b};m.triangles=new[]{0,1,2,1,2,3};
            FinalProceduralMesh.IndexOwned(m);Assert.AreEqual(2,m.vertexCount);CollectionAssert.AreEqual(new[]{a,b},m.vertices);CollectionAssert.AreEqual(new[]{0,1,0,1,0,1},m.triangles);
        }finally{Object.DestroyImmediate(m);}
    }
    [UnityTest] public IEnumerator CompletedWorldsKeepIndependentOwnedPropMeshesThroughDisposal()
    {
        var a=new GameObject("First prop world");var b=new GameObject("Second prop world");PlayableWorld aw=null,bw=null;
        try{
            var map=new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text));
            var profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);
            aw=new PlayableWorld(a.transform,profile);bw=new PlayableWorld(b.transform,profile);
            Mesh[] Props(GameObject root)=>root.GetComponentsInChildren<MeshFilter>().Where(f=>f.name.StartsWith("Detail —")&&!f.name.Contains("geological")).Select(f=>f.sharedMesh).ToArray();
            var am=Props(a);var bm=Props(b);Assert.AreEqual(5,am.Length);Assert.AreEqual(5,bm.Length);Assert.IsEmpty(am.Intersect(bm));
            Assert.AreEqual(44251,am.Sum(m=>m.vertexCount),"All five final prop meshes use the indexed runtime path");
            Assert.AreEqual(18851,am.Sum(m=>m.triangles.Length/3),"Approved prop triangles remain complete");
            var source=bm.Select(m=>m.vertexCount).ToArray();aw.Dispose();aw=null;yield return null;
            Assert.True(am.All(m=>!m),"First world must release owned meshes");Assert.True(bm.All(m=>m));CollectionAssert.AreEqual(source,bm.Select(m=>m.vertexCount));
            Assert.IsEmpty(b.GetComponentsInChildren<Collider>());
        }finally{aw?.Dispose();bw?.Dispose();Object.DestroyImmediate(a);Object.DestroyImmediate(b);}
    }
}
