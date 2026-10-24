using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Tests.PlayMode
{
 public sealed class GroundTrailPresentationTests
 {
  static PlayableSnapshot Frame(long tick,PlayableGroundTrail[] trails,long generation=1)=>new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,generation,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,groundTrails:trails);
  [Test] public void DistinctChassisRenderClearExpireAndToggleWithoutChangingFacts()
  {
   bool previous=NativeGraphicsSettings.GroundTrailsEnabled;var host=new GameObject("trail-test");var data=PlayableProfile.Default.CopyData();var p=PlayableProfile.Create(data,new AiTestMap());PlayableWorld world=null;
   try{
    NativeGraphicsSettings.GroundTrailsEnabled=true;world=new PlayableWorld(host.transform,p);world.SetPresentationLayer(12);world.Fog.ShowPublic();
    var facts=new[]{new PlayableGroundTrail(new NavPoint(0,0),new NavPoint(2,0),0,0,3,PlayableEntityKind.Tank),new PlayableGroundTrail(new NavPoint(0,4),new NavPoint(2,4),0,0,3,PlayableEntityKind.Explorer),new PlayableGroundTrail(new NavPoint(0,8),new NavPoint(2,8),0,0,3,PlayableEntityKind.Shkval)};
    world.RenderGroundTrails(Frame(4,facts));Assert.Greater(world.GroundTrailQuadCount,0);
    var obj=host.GetComponentsInChildren<Transform>().Single(t=>t.name=="Ground vehicle trails").gameObject;Assert.AreEqual(12,obj.layer);Assert.Zero(obj.GetComponentsInChildren<Collider>().Length);
    var mesh=obj.GetComponent<MeshFilter>().sharedMesh;var uv=new System.Collections.Generic.List<Vector4>();mesh.GetUVs(1,uv);Assert.AreEqual(3,uv.Select(x=>x.y).Distinct().Count());
    NativeGraphicsSettings.GroundTrailsEnabled=false;world.RenderGroundTrails(Frame(4,facts));Assert.False(obj.activeSelf);
    NativeGraphicsSettings.GroundTrailsEnabled=true;world.RenderGroundTrails(Frame(4,facts));Assert.True(obj.activeSelf);
    world.RenderGroundTrails(Frame(303,facts));Assert.Zero(world.GroundTrailQuadCount);world.RenderGroundTrails(Frame(0,Array.Empty<PlayableGroundTrail>(),2));Assert.Zero(world.GroundTrailQuadCount);
   }finally{world?.Dispose();UnityEngine.Object.DestroyImmediate(host);NativeGraphicsSettings.GroundTrailsEnabled=previous;}
  }
  [TestCase(1)] [TestCase(20)] public void ConfiguredLifetimeControlsMeshAndShader(int seconds)
  {
   bool old=NativeGraphicsSettings.GroundTrailsEnabled;var host=new GameObject("lifetime-test");var data=PlayableProfile.Default.CopyData();data.groundTrailLifetimeSeconds=seconds;var p=PlayableProfile.Create(data,new AiTestMap());PlayableWorld world=null;
   try{NativeGraphicsSettings.GroundTrailsEnabled=true;world=new PlayableWorld(host.transform,p);world.Fog.ShowPublic();
    var facts=new[]{new PlayableGroundTrail(new NavPoint(0,0),new NavPoint(2,0),0,0,1,PlayableEntityKind.Tank)};
    world.RenderGroundTrails(Frame(p.GroundTrailLifetimeTicks,facts));Assert.Greater(world.GroundTrailQuadCount,0);
    var material=host.GetComponentsInChildren<MeshRenderer>().Single(x=>x.name=="Ground vehicle trails").sharedMaterial;
    Assert.AreEqual(seconds,material.GetFloat("_Lifetime"));Assert.AreEqual(Math.Min(seconds,2.5f),material.GetFloat("_Fade"));
    world.RenderGroundTrails(Frame(p.GroundTrailLifetimeTicks+1,facts));Assert.Zero(world.GroundTrailQuadCount);
   }finally{world?.Dispose();UnityEngine.Object.DestroyImmediate(host);NativeGraphicsSettings.GroundTrailsEnabled=old;}
  }
  [Test] public void AuthoredMaterialsExcludeMetalRockAndIndustrialFoundry()
  {
   var moss=new MossHighlandMap();foreach(var patch in moss.Materials){var p=new NavPoint((patch.Bounds.MinX+patch.Bounds.MaxX)*.5,(patch.Bounds.MinZ+patch.Bounds.MaxZ)*.5);if(moss.Materials.Last(x=>x.Bounds.Contains(p)).Material!=patch.Material||moss.SupportAt(p)==null)continue;
    if(patch.Material=="metal-deck"||patch.Material.StartsWith("mountain."))Assert.AreEqual(GroundTrailSurfaceKind.Hard,GroundTrailSurface.At(moss,p));}
   var map=new ThreeCrossingsMap(new ThreeCrossingsProfileData());foreach(var support in map.Supports.Where(s=>s.IsBridge)){var b=support.Bounds;Assert.AreEqual(GroundTrailSurfaceKind.Hard,GroundTrailSurface.At(map,new NavPoint((b.MinX+b.MaxX)*.5,(b.MinZ+b.MaxZ)*.5)));}
  }
  [TestCase(1)] [TestCase(-1)] public void ShkvalImpressionsMatchWheelContactOnFirstDisplacement(int direction)
  {
   bool old=NativeGraphicsSettings.GroundTrailsEnabled;var host=new GameObject("contact-test");var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());PlayableWorld world=null;
   try{NativeGraphicsSettings.GroundTrailsEnabled=true;world=new PlayableWorld(host.transform,p);world.Fog.ShowPublic();
    world.RenderGroundTrails(Frame(1,new[]{new PlayableGroundTrail(new NavPoint(0,0),new NavPoint(direction*.01,0),0,0,1,PlayableEntityKind.Shkval)}));
    var mesh=host.GetComponentsInChildren<MeshFilter>().Single(f=>f.gameObject.name=="Ground vehicle trails").sharedMesh;var b=mesh.bounds;
    float scale=(float)(p.ShkvalModelRadius*p.ShkvalModelScale/2.05);float edge=direction>0?-1.329f:1.179f;
    Assert.That(b.center.x,Is.EqualTo(edge*scale+direction*.005f).Within(.00001));
    Assert.That(b.max.z,Is.EqualTo(.86f*scale).Within(.00001));Assert.That(b.min.z,Is.EqualTo(-.86f*scale).Within(.00001));
    Assert.That(mesh.vertices.Where(v=>v.z>0).Min(v=>v.z),Is.EqualTo(.58f*scale).Within(.00001));
   }finally{world?.Dispose();UnityEngine.Object.DestroyImmediate(host);NativeGraphicsSettings.GroundTrailsEnabled=old;}
  }
  [Test] public void EveryReadabilityValueHasValidatedLabMetadata()
  {var p=GroundTrailProfile.Load();Assert.AreEqual(20,p.Parameters().Count());foreach(var f in p.Parameters()){float old=f.Read();Assert.IsNotEmpty(f.Description);Assert.Greater(f.Step,0);f.Write(float.NaN);Assert.Throws<ArgumentException>(()=>p.Validate());f.Write(old);}p.Validate();}
 }
}
