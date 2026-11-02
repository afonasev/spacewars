using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
public sealed class PreparedMapSceneTests
{
    const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
    static Type Authoring=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Spacewars.Editor.PreparedMapsAuthoring")).First(t=>t!=null);
    static void Author(string method)=>Authoring.GetMethod(method).Invoke(null,null);
    static readonly string[] Maps={"ThreeCrossings","Foundry","MossHighland","BasaltCrown","WhiteMeridian","CrimsonGrove","AntennaCity","NullDock","AiTest","Campaign","ProvingGround","Diagnostic","Nereida"};
    static IPlayableTerrain Map(string key)=>(IPlayableTerrain)Authoring.GetMethod("Map").Invoke(null,new object[]{key});
    static PlayableProfile Profile(string key)=>(PlayableProfile)Authoring.GetMethod("Profile").Invoke(null,new object[]{key});
    static string Output=>Environment.GetEnvironmentVariable("SPACEWARS_PREPARED_MAP_QA")??Path.GetFullPath("../.local/prepared-map-cache/qa");
    [SetUp] public void Setup(){Directory.CreateDirectory(Output);PreparedMapCache.DirectoryOverride=Path.Combine(Output,"variants",Guid.NewGuid().ToString("N"));PreparedMapCache.BypassReads=false;}
    [TearDown] public void Cleanup(){PreparedMapCache.DirectoryOverride=null;PreparedMapCache.BypassReads=false;}
    static string Fingerprint(Transform root,PlayableWorld world=null)
    {
        var material=world==null?null:(Material)typeof(PlayableWorld).GetField("material",Flags).GetValue(world);
        var scene=PreparedSceneCodec.Capture(root,material,world?.Fog.Texture);if(world!=null){var lods=(List<ModelDetailLod>)typeof(PlayableWorld).GetField("sceneryDetail",Flags).GetValue(world);var nodes=root.GetComponentsInChildren<Transform>(true);scene.worldLods=lods.Select(l=>Array.FindIndex(nodes,t=>t==l.transform)).ToArray();}var n=scene.nodes[0];n.name="$root";n.position=Vector3.zero;n.rotation=Quaternion.identity;n.scale=Vector3.one;n.layer=0;n.active=true;
        Dump(Path.Combine(Output,root.name.Replace("/","_")+"-"+Guid.NewGuid().ToString("N")+"-scene.json"),scene);return PreparedMapCache.Hash(PreparedSceneCodec.Encode("exact-scene",scene));
    }
    [UnityTest,Timeout(900000)] public IEnumerator PrepareDefaultsAndRestoreAllMapsExactly()
    {
        var fingerprints=new Dictionary<string,string>();var minimaps=new Dictionary<string,byte[]>();var timings=new Dictionary<string,double>();
        bool bakeComplete=false;Author("BeginBake");
        try{foreach(string key in Maps){var host=new GameObject("Generate "+key);PlayableWorld world=null;PlayableMapTerrain mini=null;var clock=Stopwatch.StartNew();
            try{if(key=="Nereida"){host.AddComponent<NereidaPreview>().Build(NereidaPreview.LoadProfile());clock.Stop();fingerprints[key]=Fingerprint(host.transform);}
                else{var p=Profile(key);world=new PlayableWorld(host.transform,p,staged:true);yield return world.Preparation;mini=new PlayableMapTerrain(p,staged:true);yield return mini.Preparation;clock.Stop();Assert.False(world.PreparationCacheHit,key);mini.ShowPublicTerrain();minimaps[key]=mini.Texture.GetRawTextureData();fingerprints[key]=Fingerprint(world.GraphicsRoot,world);}
                timings[key]=clock.Elapsed.TotalSeconds;UnityEngine.Debug.Log("MAP_GENERATED "+key+" "+timings[key]);
            }finally{mini?.Dispose();world?.Dispose();Object.Destroy(host);}yield return null;
        }bakeComplete=true;}finally{Author(bakeComplete?"EndBake":"AbortBake");}
        foreach(string key in Maps){var host=new GameObject("Restore "+key);PlayableWorld world=null;PlayableMapTerrain mini=null;var clock=Stopwatch.StartNew();
            try{string actual;
                if(key=="Nereida"){var preview=host.AddComponent<NereidaPreview>();preview.Build(NereidaPreview.LoadProfile());clock.Stop();Assert.True(preview.PreparationCacheHit,key);actual=Fingerprint(host.transform);}
                else{var p=Profile(key);world=new PlayableWorld(host.transform,p,staged:true);yield return world.Preparation;mini=new PlayableMapTerrain(p,staged:true);yield return mini.Preparation;clock.Stop();Assert.True(world.PreparationCacheHit,key);Assert.True(mini.PreparationCacheHit,key+" minimap");mini.ShowPublicTerrain();CollectionAssert.AreEqual(minimaps[key],mini.Texture.GetRawTextureData(),key+" minimap pixels");actual=Fingerprint(world.GraphicsRoot,world);}
                Assert.AreEqual(fingerprints[key],actual,key+" complete scene packet");File.WriteAllText(Path.Combine(Output,key+".json"),JsonUtility.ToJson(new Result{map=key,generatedSeconds=timings[key],cachedSeconds=clock.Elapsed.TotalSeconds,fingerprint=actual},true));UnityEngine.Debug.Log("MAP_CACHED "+key+" "+clock.Elapsed.TotalSeconds);
            }finally{mini?.Dispose();world?.Dispose();Object.Destroy(host);}yield return null;
        }
    }
    static void Dump(string path,PreparedScene scene)
    {
        File.WriteAllText(path,PreparedSceneCodec.Json(scene));
        File.WriteAllText(path+".buffers",string.Join("\n",scene.meshes.Select((m,i)=>i+" "+m.name+" "+string.Join(" ",m.streams.Select(PreparedMapCache.Hash))+" "+PreparedMapCache.Hash(m.indices)).Concat(scene.textures.Select((t,i)=>"tex "+i+" "+t.name+" "+(t.pixels==null?t.path:PreparedMapCache.Hash(t.pixels))))));
    }
    [UnityTest,Timeout(180000)] public IEnumerator PackagedDefaultsSurviveProcessRestart()
    {
        Assert.True((bool)Authoring.GetMethod("Fresh").Invoke(null,null),"Default assets must match current sources");
        foreach(string key in Maps){var clock=Stopwatch.StartNew();var host=new GameObject("restart "+key);PlayableWorld world=null;PlayableMapTerrain mini=null;
            try{string actual;if(key=="Nereida"){var preview=host.AddComponent<NereidaPreview>();preview.Build(NereidaPreview.LoadProfile());Assert.True(preview.PreparationCacheHit,key);clock.Stop();actual=Fingerprint(host.transform);}
                else{var p=Profile(key);world=new PlayableWorld(host.transform,p,staged:true);yield return world.Preparation;mini=new PlayableMapTerrain(p,staged:true);yield return mini.Preparation;Assert.True(world.PreparationCacheHit,key);Assert.True(mini.PreparationCacheHit,key+" minimap");clock.Stop();actual=Fingerprint(world.GraphicsRoot,world);}
                var expected=JsonUtility.FromJson<Result>(File.ReadAllText(Path.Combine(Output,key+".json")));Assert.AreEqual(expected.fingerprint,actual,key);File.WriteAllText(Path.Combine(Output,"Restart-"+key+".json"),JsonUtility.ToJson(new Result{map=key,fingerprint=actual,cachedSeconds=clock.Elapsed.TotalSeconds},true));UnityEngine.Debug.Log("MAP_RESTART "+key+" "+clock.Elapsed.TotalSeconds);
            }finally{mini?.Dispose();world?.Dispose();Object.Destroy(host);}yield return null;
        }
    }
    [UnityTest,Timeout(180000)] public IEnumerator CorruptVariantRegeneratesAndCancelledRestoreReleasesResources()
    {
        var data=Profile("Campaign").CopyData();data.fogTintG+=.021;var profile=PlayableProfile.Create(data,Map("Campaign"));var host=new GameObject("corrupt source");PlayableWorld world=null;string expected;
        try{world=new PlayableWorld(host.transform,profile,staged:true);yield return world.Preparation;Assert.False(world.PreparationCacheHit);expected=Fingerprint(world.GraphicsRoot,world);}finally{world?.Dispose();Object.Destroy(host);}yield return null;
        var file=Directory.GetFiles(PreparedMapCache.DirectoryOverride,"World-Campaign-*.swmap").Single();File.WriteAllBytes(file,new byte[]{1,2,3});host=new GameObject("regenerate");world=null;
        try{world=new PlayableWorld(host.transform,profile,staged:true);yield return world.Preparation;Assert.False(world.PreparationCacheHit);Assert.AreEqual(expected,Fingerprint(world.GraphicsRoot,world));}finally{world?.Dispose();Object.Destroy(host);}yield return null;
        host=new GameObject("repaired hit");world=null;
        try{world=new PlayableWorld(host.transform,profile,staged:true);yield return world.Preparation;Assert.True(world.PreparationCacheHit);Assert.AreEqual(expected,Fingerprint(world.GraphicsRoot,world));}finally{world?.Dispose();Object.Destroy(host);}yield return null;
        var resource=Resources.Load<TextAsset>("PreparedMaps/World-ThreeCrossings");var open=typeof(PreparedMapCache).GetMethod("Open",BindingFlags.Static|BindingFlags.NonPublic);var scene=PreparedSceneCodec.Decode((byte[])open.Invoke(null,new object[]{resource.bytes,"",true}));
        // A bounded stress fixture guarantees a mid-restore cancellation opportunity
        // without relying on the production scene exceeding a wall-clock budget.
        scene.nodes=scene.nodes.Concat(Enumerable.Range(0,65536).Select(i=>new PreparedNode{name="cancel node "+i,parent=0,scale=Vector3.one,rotation=Quaternion.identity,active=true})).ToArray();
        host=new GameObject("cancel restore");var instance=new PreparedSceneInstance();var work=instance.Restore(scene,host.transform);
        try{Assert.True(work.MoveNext(),"Large prepared scene must yield before completion");var meshes=instance.Meshes.Where(m=>m).ToArray();Assert.IsNotEmpty(meshes);((IDisposable)work).Dispose();yield return null;Assert.True(meshes.All(m=>!m));Assert.AreEqual(0,host.transform.childCount);}finally{instance.Dispose();Object.Destroy(host);}yield return null;
    }
    [UnityTest,Timeout(180000)] public IEnumerator CachedLoadingLifecycleWithHeadlessDevices()
    {
        var keyboard=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();var mouse=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();var fixture=new LoadingScreenTests();
        try{bool sawPrepared=false,configured=false;var work=fixture.StartupAndMatchCoverInitializationUntilReady();
            try{while(work.MoveNext()){
                var host=(GameObject)typeof(LoadingScreenTests).GetField("host",Flags).GetValue(fixture);
                var game=host?host.GetComponent<PlayableBootstrap>():null;
                if(game&&!configured){
                    var baseline=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text));
                    var store=NativeBalanceStore.ForApplication(baseline,Path.Combine(PreparedMapCache.DirectoryOverride,"native-balance.json"));
                    typeof(PlayableBootstrap).GetField("balanceStore",Flags).SetValue(game,store);configured=true;
                }
                var world=game?(PlayableWorld)typeof(PlayableBootstrap).GetField("world",Flags).GetValue(game):null;
                sawPrepared|=world?.PreparationCacheHit==true;yield return work.Current;
            }}finally{(work as IDisposable)?.Dispose();}
            Assert.True(sawPrepared,"The ordinary new-match profile must use a packaged prepared world");
        }
        finally{fixture.Cleanup();UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse);}
    }
    [Serializable] class Result{public string map,fingerprint;public double generatedSeconds,cachedSeconds;}
    [UnityTest,Timeout(180000)] public IEnumerator ChangedProfileRebuildsThenPersistsAndWorldsOwnTheirFogMeshesAndGate()
    {
        var baseProfile=Profile("Campaign");var data=baseProfile.CopyData();data.fogTintR+=.013;var profile=PlayableProfile.Create(data,baseProfile.AuthoredMap);
        var first=new GameObject("variant first");var second=new GameObject("variant second");PlayableWorld a=null,b=null;
        try{a=new PlayableWorld(first.transform,profile,staged:true);yield return a.Preparation;Assert.False(a.PreparationCacheHit,"Changed effective profile must generate");
            b=new PlayableWorld(second.transform,profile,staged:true);yield return b.Preparation;Assert.True(b.PreparationCacheHit,"Second identical profile must restore persistent data");
            Assert.AreEqual(Fingerprint(a.GraphicsRoot,a),Fingerprint(b.GraphicsRoot,b));Assert.AreNotSame(a.Fog.Texture,b.Fog.Texture);
            var am=first.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh).Distinct().ToArray();var bm=second.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh).Distinct().ToArray();Assert.IsEmpty(am.Intersect(bm));
            b.SetCampaignGate(false);Assert.True(first.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Canyon gate").gameObject.activeSelf);Assert.False(second.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Canyon gate").gameObject.activeSelf);
            a.Dispose();a=null;yield return null;Assert.True(bm.All(m=>m));Assert.True(b.Fog.Texture);
        }finally{a?.Dispose();b?.Dispose();Object.Destroy(first);Object.Destroy(second);}yield return null;
    }
    [UnityTest,Timeout(180000)] public IEnumerator ArtOverrideInvalidatesAndRuntimeResourceIndexCanPersistNewVariant()
    {
        var profile=Profile("NullDock");var art=NullDockArtProfile.Load();art.panelSize+=.125f;art.Validate();var resolver=PreparedMapCache.EditorResourcePath;
        var first=new GameObject("art first");var second=new GameObject("art second");PlayableWorld a=null,b=null;
        try{PreparedMapCache.EditorResourcePath=null;a=new PlayableWorld(first.transform,profile,staged:true,dockArt:art);yield return a.Preparation;Assert.False(a.PreparationCacheHit);
            b=new PlayableWorld(second.transform,profile,staged:true,dockArt:art);yield return b.Preparation;Assert.True(b.PreparationCacheHit);Assert.AreEqual(Fingerprint(a.GraphicsRoot,a),Fingerprint(b.GraphicsRoot,b));
        }finally{PreparedMapCache.EditorResourcePath=resolver;a?.Dispose();b?.Dispose();Object.Destroy(first);Object.Destroy(second);}yield return null;
    }
}
