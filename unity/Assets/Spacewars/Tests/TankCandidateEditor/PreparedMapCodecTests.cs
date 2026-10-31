using System;
using System.IO;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using Object=UnityEngine.Object;
public sealed class PreparedMapCodecTests
{
    string folder;Func<string> stamp;
    [SetUp] public void Setup(){folder=Path.GetFullPath("../.local/prepared-map-cache/unit-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);PreparedMapCache.DirectoryOverride=folder;PreparedMapCache.BypassReads=false;stamp=PreparedMapCache.EditorStamp;}
    [TearDown] public void Cleanup(){PreparedMapCache.EditorStamp=stamp;PreparedMapCache.DirectoryOverride=null;PreparedMapCache.BypassReads=false;Directory.Delete(folder,true);}
    [Test] public void KeyBindsFullProfileGeometryAndGeneratorStamp()
    {
        var p=PlayableProfile.Default;var map=ThreeCrossingsMap.Default;string a=PreparedMapCache.Key(map,p);Assert.AreEqual(a,PreparedMapCache.Key(ThreeCrossingsMap.Default,p));
        var data=p.CopyData();data.cameraHeight+=1;Assert.AreNotEqual(a,PreparedMapCache.Key(map,PlayableProfile.Create(data,map)));PreparedMapCache.EditorStamp=()=>"different-source";Assert.AreNotEqual(a,PreparedMapCache.Key(map,p));
    }
    [Test] public void TypedPacketRestoresRawMeshesMasksBlocksLodAndIndependentFog()
    {
        var source=new GameObject("source");var target=new GameObject("target");var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.transform.SetParent(source.transform,false);cube.transform.localPosition=new Vector3(2,3,4);Object.DestroyImmediate(cube.GetComponent<Collider>());
        var material=new Material(Resources.Load<Shader>("TerritoryFog"));var fog=new Texture2D(2,2);var otherFog=new Texture2D(2,2);PreparedSceneInstance instance=null;
        try{material.SetTexture("_FogMask",fog);cube.GetComponent<MeshRenderer>().sharedMaterial=material;var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",new Color(.1f,.2f,.3f,1));block.SetFloat("_SurfaceRole",3);cube.GetComponent<Renderer>().SetPropertyBlock(block);var expectedColor=block.GetVector("_BaseColor");
            var lod=cube.AddComponent<ModelDetailLod>();var mesh=cube.GetComponent<MeshFilter>().sharedMesh;lod.Configure(new Renderer[]{cube.GetComponent<MeshRenderer>()},new[]{mesh},new[]{mesh},new[]{mesh},mesh.bounds);lod.ConfigureSelectionBounds(new[]{mesh.bounds});lod.MarkCatalogBound();cube.AddComponent<CrossingSceneryDetail>().Bind(lod);
            var scene=PreparedSceneCodec.Capture(source.transform,material,fog);PreparedMapCache.SaveScene("Test","key",scene);Assert.True(PreparedMapCache.TryScene("Test","key",out var loaded));
            instance=new PreparedSceneInstance();var work=instance.Restore(loaded,target.transform,null,otherFog);while(work.MoveNext()){}
            var actual=target.GetComponentInChildren<MeshFilter>();Assert.AreEqual(ModelDetailCatalog.Identity(mesh),ModelDetailCatalog.Identity(actual.sharedMesh));Assert.AreEqual(cube.transform.localPosition,actual.transform.localPosition);Assert.AreNotSame(mesh,actual.sharedMesh);
            var renderer=actual.GetComponent<Renderer>();renderer.GetPropertyBlock(block);Assert.AreEqual(expectedColor,block.GetVector("_BaseColor"));Assert.AreEqual(3,block.GetFloat("_SurfaceRole"));Assert.AreSame(otherFog,renderer.sharedMaterial.GetTexture("_FogMask"));Assert.True(actual.GetComponent<ModelDetailLod>().IsCatalogBound);Assert.AreEqual(1,actual.GetComponent<ModelDetailLod>().SelectionBounds.Length);
            var restored=actual.sharedMesh;instance.Dispose();instance=null;Assert.False(restored);Assert.True(mesh);Assert.True(fog);Assert.True(otherFog);
        }finally{instance?.Dispose();Object.DestroyImmediate(source);Object.DestroyImmediate(target);Object.DestroyImmediate(material);Object.DestroyImmediate(fog);Object.DestroyImmediate(otherFog);}
    }
    [Test] public void PackagedMaterialVectorsRestoreExactlyAcrossNativeColorConversion()
    {
        var open=typeof(PreparedMapCache).GetMethod("Open",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
        var source=Resources.Load<TextAsset>("PreparedMaps/World-ThreeCrossings");var scene=PreparedSceneCodec.Decode((byte[])open.Invoke(null,new object[]{source.bytes,"",true}));
        var host=new GameObject("material restore");var fog=new Texture2D(2,2);var instance=new PreparedSceneInstance();
        try{var work=instance.Restore(scene,host.transform,null,fog);while(work.MoveNext()){}for(int i=0;i<scene.materials.Length;i++)foreach(var property in scene.materials[i].properties)if(property.kind==1)Assert.AreEqual(property.vector,instance.Materials[i].GetVector(property.name),property.name);}
        finally{instance.Dispose();Object.DestroyImmediate(host);Object.DestroyImmediate(fog);}
    }
    [Test] public void ColorPacketsAreExactCorruptionMissesAndVariantStorageIsBounded()
    {
        var colors=new[]{new Color(.1234567f,.2f,.3f,.4f),new Color(.5f,.6f,.7f,1)};for(int i=0;i<7;i++)PreparedMapCache.SaveColors("Map",i.ToString(),colors);Assert.AreEqual(4,Directory.GetFiles(folder,"*.swmap").Length);
        var actual=new Color[2];Assert.True(PreparedMapCache.TryColors("Map","6",actual));CollectionAssert.AreEqual(colors,actual);
        File.WriteAllBytes(PreparedMapCache.VariantPath("Map","6"),new byte[]{1,2,3});Assert.False(PreparedMapCache.TryColors("Map","6",actual));PreparedMapCache.SaveColors("Map","6",colors);Assert.True(PreparedMapCache.TryColors("Map","6",actual));
    }
}
