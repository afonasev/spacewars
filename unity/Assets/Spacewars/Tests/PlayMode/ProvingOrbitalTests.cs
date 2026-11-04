using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object=UnityEngine.Object;

public sealed class ProvingOrbitalTests
{
    [UnityTest] public IEnumerator LiveSandboxPublishesAllAbilitiesAndIndependentTimersToHud()
    {
        const BindingFlags f=BindingFlags.Instance|BindingFlags.NonPublic;
        var setup=new ProvingGroundSetup(PlayableProfile.Default);
        var runtime=new PlayableRuntime(setup.Configuration(),109,startPaused:true);
        var host=new GameObject("Proving orbital HUD");var hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
        var type=typeof(PlayableBootstrap);
        void Set(string n,object v)=>type.GetField(n,f).SetValue(hud,v);
        object Call(string n,params object[] a)=>type.GetMethods(f).Single(m=>m.Name==n&&m.GetParameters().Length==a.Length).Invoke(hud,a);
        RenderTexture target=null;
        try{
            float deadline=Time.realtimeSinceStartup+30;
            while(!runtime.IsPrepared&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.True(runtime.IsPrepared,runtime.Latest?.Failure);
            foreach(string owner in new[]{"player-1","enemy-1"}){
                var state=runtime.ParticipantView(owner).Orbital;
                CollectionAssert.AreEqual(new[]{1,2,8,3},state.Abilities.Select(a=>a.Id));
                Assert.IsEmpty(state.Options);Assert.Zero(state.PendingLevel);Assert.True(state.Abilities.All(a=>a.Remaining==0));
            }
            runtime.RequestPause(false);
            Assert.True(runtime.TrySubmit(new PlayableCommand(109,1,"player-1",PlayableCommandKind.UseOrbital,null,new NavPoint(-15,0),targetId:1)).Accepted);
            while(!runtime.ReceiptsAfter(0).Any(r=>r.Sequence==1)&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.AreEqual(PlayableCommandStatus.Applied,runtime.ReceiptsAfter(0).Single(r=>r.Sequence==1).Status);
            // Receipts and presentation snapshots are published independently by the threaded host.
            while(runtime.Latest.Orbital.Abilities.Single(a=>a.Id==1).Remaining<=0&&Time.realtimeSinceStartup<deadline)yield return null;
            var scan=runtime.Latest.Orbital.Abilities.Single(a=>a.Id==1);Assert.Greater(scan.Remaining,0);Assert.LessOrEqual(scan.Remaining,5);Assert.AreEqual(5,scan.Duration);
            Assert.True(runtime.Latest.Orbital.Abilities.Where(a=>a.Id!=1).All(a=>a.Remaining==0));Assert.Zero(runtime.Latest.Orbital.CommonRemaining);
            Assert.True(runtime.TrySubmit(new PlayableCommand(109,2,"player-1",PlayableCommandKind.UseOrbital,null,new NavPoint(-15,0),targetId:2)).Accepted);
            while(!runtime.ReceiptsAfter(0).Any(r=>r.Sequence==2)&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.AreEqual(PlayableCommandStatus.Applied,runtime.ReceiptsAfter(0).Single(r=>r.Sequence==2).Status);
            runtime.RequestPause(true);while(!runtime.Latest.Paused&&Time.realtimeSinceStartup<deadline)yield return null;Assert.True(runtime.Latest.Paused);
            Set("profile",setup.Profile);Call("CreateHud");Set("runtime",runtime);Set("provingGround",setup);Set("view",runtime.Latest);
            var root=(VisualElement)type.GetField("root",f).GetValue(hud);var panel=host.GetComponent<UIDocument>().panelSettings;
            target=new RenderTexture(1280,800,24);target.Create();panel.targetTexture=target;
            root.style.width=1280/panel.scale;root.style.height=800/panel.scale;root.style.backgroundColor=OrbitalTheme.Background;
            yield return null;yield return null;Call("UpdateOrbitalHud");yield return null;yield return null;
            foreach(int id in new[]{1,2,8,3})Assert.NotNull(root.Q<Button>("orbital-slot-"+id));
            foreach(int id in new[]{4,5,6,7})Assert.IsNull(root.Q<Button>("orbital-slot-"+id));
            var old=RenderTexture.active;RenderTexture.active=target;var pixels=new Texture2D(1280,800,TextureFormat.RGBA32,false);
            pixels.ReadPixels(new Rect(0,0,1280,800),0,0);pixels.Apply();RenderTexture.active=old;
            var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/qa/proving-orbital"));Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output,"abilities.png"),pixels.EncodeToPNG());Object.Destroy(pixels);
        }finally{runtime.RequestStop();Set("runtime",null);if(target){target.Release();Object.Destroy(target);}Object.Destroy(host);}
    }
    [UnityTest] public IEnumerator CapturedActorsRepaintInProvingGround()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var setup=new ProvingGroundSetup(PlayableProfile.Default);var rules=setup.Profile;
        var host=new GameObject("Proving capture owner paint");var hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
        void Set(string n,object v)=>typeof(PlayableBootstrap).GetField(n,flags).SetValue(hud,v);
        object Call(string n)=>typeof(PlayableBootstrap).GetMethods(flags).Single(m=>m.Name==n&&m.GetParameters().Length==0).Invoke(hud,null);
        PlayableSnapshot Frame(PlayableOwner owner,int tick){
            var kinds=new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Shkval,PlayableEntityKind.Hawk};
            var units=kinds.Select((kind,i)=>new PlayableEntitySnapshot(i+1,owner,kind,new NavPoint(-9+i*5,0),20,false,0,0,0)).ToArray();
            var building=new PlayableBuildingSnapshot(10,owner,PlayableBuildingKind.Mine,new NavPoint(0,8),80,1,0,0,default(NavPoint));
            return new PlayableSnapshot(rules.ProfileId,rules.Revision,1,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,500,new NavGeometry(32,Array.Empty<NavObstacle>(),1),units,new[]{building},Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null);
        }
        try{
            Set("profile",rules);Set("provingGround",setup);Set("view",Frame(PlayableOwner.Enemy,1));Call("CreateWorld");Call("CreateHud");Call("Render");yield return null;
            var world=(PlayableWorld)typeof(PlayableBootstrap).GetField("world",flags).GetValue(hud);var original=world.Actors.ToDictionary(k=>k.Key,v=>v.Value);
            void Capture(string name){
                var output=Environment.GetEnvironmentVariable("SPACEWARS_CAPTURE_PAINT_OUTPUT");if(string.IsNullOrWhiteSpace(output))return;
                var camera=(Camera)typeof(PlayableBootstrap).GetField("cameraView",flags).GetValue(hud);var target=RenderTexture.GetTemporary(1000,700,24);var previous=RenderTexture.active;
                var pixels=new Texture2D(1000,700,TextureFormat.RGBA32,false);
                try{camera.transform.position=new Vector3(0,28,-22);camera.transform.LookAt(new Vector3(0,0,3));camera.orthographicSize=16;camera.targetTexture=target;camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1000,700),0,0);pixels.Apply();Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,name+".png"),pixels.EncodeToPNG());}
                finally{camera.targetTexture=null;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);Object.DestroyImmediate(pixels);}
            }
            Capture("before-capture");
            foreach(var owner in new[]{PlayableOwner.Player,PlayableOwner.Enemy}){
                Set("view",Frame(owner,owner==PlayableOwner.Player?2:3));Call("Render");yield return null;
                Capture(owner==PlayableOwner.Player?"after-capture":"after-recapture");
                var expected=owner==PlayableOwner.Player?new Color(.22f,.53f,.94f):new Color(.93f,.34f,.23f);
                foreach(int id in new[]{1,2,3,4,10}){
                    var actor=world.Actors[id];Assert.AreSame(original[id],actor,"Capture reuses the actor; cached paint must refresh.");int slots=0;
                    foreach(var renderer in actor.Hull.GetComponentsInChildren<Renderer>(true))for(int slot=0;slot<renderer.sharedMaterials.Length;slot++){
                        var role=renderer.sharedMaterials[slot].name;if(!role.EndsWith("team-primary")&&!role.EndsWith("team-emissive"))continue;
                        var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,slot);var actual=block.GetColor("_BaseColor");
                        Assert.AreEqual(expected.r,actual.r,.0001f,"owner red id="+id);Assert.AreEqual(expected.g,actual.g,.0001f,"owner green id="+id);Assert.AreEqual(expected.b,actual.b,.0001f,"owner blue id="+id);slots++;
                    }
                    Assert.Greater(slots,0,"Real unit/mine prefab has team-color materials id="+id);
                }
            }
        }finally{Object.DestroyImmediate(host);}
    }
}
