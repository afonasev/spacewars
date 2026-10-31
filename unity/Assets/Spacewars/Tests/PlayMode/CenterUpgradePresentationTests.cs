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
using Object=UnityEngine.Object;

public sealed class CenterUpgradePresentationTests
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    [UnityTest] public IEnumerator ActualCenterQueueSwitchesApprovedModelAndRetainsAllDetailTiers()
    {
        var p=PlayableProfile.Default;var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        object domain=Activator.CreateInstance(type,F,null,new object[]{p,1L,false},null);long seq=0;
        object Call(string name,params object[] args)=>type.GetMethod(name,F).Invoke(domain,args);
        PlayableSnapshot View()=>(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player);
        Call("AddCredits",PlayableOwner.Player,10000d);
        Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,5,PlayableBuildingKind.Hangar,1,PlayableOwner.Player,null));Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
        Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,2,PlayableBuildingKind.ScientificCenter,1,PlayableOwner.Player,null));Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
        int center=View().Buildings.Single(b=>b.Kind==PlayableBuildingKind.ScientificCenter).Id;
        void Queue(PlayableResearchKind kind)=>Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++seq,"player-1",PlayableCommandKind.QueueResearch,new[]{center},researchKind:kind),null));
        var parent=new GameObject("Center upgrade actual domain preview");PlayableWorld world=null;RenderTexture target=null;Texture2D pixels=null;
        var ambient=RenderSettings.ambientLight;var ambientMode=RenderSettings.ambientMode;
        try{
            world=new PlayableWorld(parent.transform,p,staged:true);yield return world.Preparation;world.Fog.ShowPublic();
            var light=new GameObject("Center review sun").AddComponent<Light>();light.transform.SetParent(parent.transform);IndustrialLightingProfile.Load().ApplyScene(light);
            var camera=new GameObject("Center review camera").AddComponent<Camera>();camera.transform.SetParent(parent.transform);camera.orthographic=true;camera.orthographicSize=2.4f;camera.nearClipPlane=.1f;camera.farClipPlane=500;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.12f,.15f);
            var b=View().Buildings.Single(x=>x.Id==center);var point=world.Point(b.Position);camera.transform.position=point+new Vector3(4,5,-6);camera.transform.LookAt(point+Vector3.up*.65f);
            target=new RenderTexture(1280,900,24);target.Create();camera.targetTexture=target;pixels=new Texture2D(1280,900,TextureFormat.RGB24,false);
            var actor=world.Building(center,b.Kind.ToString(),true,b.ModelUpgraded);actor.Root.position=point;actor.Selection.SetActive(false);world.FaceBuilding(actor,camera);PlayableWorld.PaintOwner(actor,new Color(.19f,.72f,.77f));
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/qa/center-upgrade-images"));Directory.CreateDirectory(output);
            void Capture(string name){camera.Render();var old=RenderTexture.active;RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,900),0,0);pixels.Apply();RenderTexture.active=old;File.WriteAllBytes(Path.Combine(output,name+".png"),pixels.EncodeToPNG());}
            yield return null;Capture("center-before");
            Queue(PlayableResearchKind.ScientificCenterUpgrade);Queue(PlayableResearchKind.ImprovedWeaponSystems);Queue(PlayableResearchKind.ImprovedDefensiveSystems);
            Call("AdvanceResearch",15d);var pending=View().OwnerResearch;Assert.AreEqual(3,pending.Count);Assert.AreEqual(.5,pending.Single(o=>o.Active).Progress);Assert.AreEqual(2,pending.Count(o=>!o.Active&&!o.Complete));
            Assert.False(View().Buildings.Single(x=>x.Id==center).ScientificCenterUpgraded);Call("AdvanceResearch",15d);
            b=View().Buildings.Single(x=>x.Id==center);Assert.True(b.ScientificCenterUpgraded);world.UpdateRefineryModel(actor,b,View().Tick);yield return null;PlayableWorld.PaintOwner(actor,new Color(.19f,.72f,.77f));
            Assert.NotNull(actor.Hull.GetComponentsInChildren<Transform>(true).SingleOrDefault(t=>t.name=="scannerInner"));Assert.NotNull(actor.DetailLod);
            Assert.True(actor.DetailLod.Renderers.All(r=>r.sharedMaterials.All(m=>m&&m.shader&&m.shader.name!="Hidden/InternalErrorShader")));
            long Tri(Mesh[] meshes)=>meshes.Sum(m=>(long)m.triangles.Length/3);
            Assert.Greater(Tri(actor.DetailLod.OriginalMeshes),Tri(actor.DetailLod.NormalMeshes));Assert.Greater(Tri(actor.DetailLod.NormalMeshes),Tri(actor.DetailLod.FarMeshes));
            for(int level=0;level<3;level++){actor.DetailLod.SetDetail(level);Capture("center-upgraded-lod-"+level);}
            actor.DetailLod.SelectScreenRatio(float.PositiveInfinity,ModelDetailProfile.Current);Assert.AreEqual(0,actor.DetailLod.Level);
            actor.DetailLod.SelectScreenRatio(0,ModelDetailProfile.Current);Assert.AreEqual(2,actor.DetailLod.Level);
            TestContext.WriteLine("Actual domain seed7: center queue -> completion -> runtime model replacement. LOD triangles "+Tri(actor.DetailLod.OriginalMeshes)+"/"+Tri(actor.DetailLod.NormalMeshes)+"/"+Tri(actor.DetailLod.FarMeshes));
        }finally{world?.Dispose();RenderSettings.ambientLight=ambient;RenderSettings.ambientMode=ambientMode;if(target){target.Release();Object.Destroy(target);}if(pixels)Object.Destroy(pixels);Object.Destroy(parent);}
    }
}
