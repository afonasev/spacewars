using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
public sealed class MapRockOwnershipRuntimeTests
{
    static string Geometry(Mesh mesh)
    {
        using var stream=new MemoryStream();using var w=new BinaryWriter(stream);
        using(var data=Mesh.AcquireReadOnlyMeshData(mesh))for(int s=0;s<mesh.vertexBufferCount;s++)w.Write(data[0].GetVertexData<byte>(s).ToArray());
        for(int s=0;s<mesh.subMeshCount;s++)foreach(int i in mesh.GetIndices(s,true))w.Write(i);
        w.Flush();using var sha=SHA256.Create();return Convert.ToBase64String(sha.ComputeHash(stream.ToArray()));
    }
    [UnityTest] public IEnumerator FinalMapMeshesRemainPrivateThroughDisposal()
    {
        Assert.True(Application.isPlaying);
        foreach(IPlayableTerrain map in new IPlayableTerrain[]{new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)),new FoundryMap(JsonUtility.FromJson<FoundryProfileData>(Resources.Load<TextAsset>("FoundryProfile").text))}){
            var a=new GameObject("First rock world");var b=new GameObject("Second rock world");PlayableWorld aw=null,bw=null;
            try{
                var p=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);aw=new PlayableWorld(a.transform,p);bw=new PlayableWorld(b.transform,p);
                Mesh[] Owned(PlayableWorld world)=>((List<Mesh>)typeof(PlayableWorld).GetField("terrainMeshes",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(world)).ToArray();
                var am=Owned(aw);var bm=Owned(bw);Assert.Greater(am.Length,0);Assert.AreEqual(am.Length,bm.Length);Assert.IsEmpty(am.Intersect(bm));
                var before=bm.Select(Geometry).ToArray();aw.Dispose();aw=null;yield return null;
                Assert.True(am.All(m=>!m),"First world releases owned meshes after actual PlayerLoop frame");Assert.True(bm.All(m=>m),"Second world remains alive");CollectionAssert.AreEqual(before,bm.Select(Geometry));Assert.IsEmpty(b.GetComponentsInChildren<Collider>());
            }finally{aw?.Dispose();bw?.Dispose();Object.DestroyImmediate(a);Object.DestroyImmediate(b);}
        }
    }
}
