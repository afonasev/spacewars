using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Tests.PlayMode
{
 public sealed class GroundTrailPresentationTests
 {
        static PlayableGroundTrail GroupTrailSample(int step,int actor)=>new PlayableGroundTrail(new NavPoint(-40+step*.08,-24+actor*.9),new NavPoint(-39.92+step*.08,-24+actor*.9),0,step*.08,step*3,PlayableEntityKind.Tank);
        static GameObject GeometryTestPad(PlayableWorld world)
        {
            var bounds=((Mesh)typeof(PlayableWorld).GetField("trailMesh",Hidden).GetValue(world)).bounds;
            var root=new GameObject("live memo pad");root.transform.SetParent((Transform)typeof(PlayableWorld).GetField("root",Hidden).GetValue(world));root.transform.position=world.Point(new NavPoint(bounds.center.x,bounds.center.z));
            var type=typeof(PlayableWorld).GetNestedType("PadView",BindingFlags.NonPublic);var pad=Activator.CreateInstance(type,true);type.GetField("Root").SetValue(pad,root);type.GetField("Surface").SetValue(pad,root.AddComponent<MeshFilter>());type.GetField("Half").SetValue(pad,new Vector2(bounds.extents.x+1,bounds.extents.z+1));
            ((IDictionary)typeof(PlayableWorld).GetField("territoryPads",Hidden).GetValue(world)).Add("geometry-test",pad);return root;
        }
        [Test] public void GeometryMemoKeepsMovingPadsAndArtEditsExact()
        {
            var p=PlayableProfile.ThreeCrossingsDefault;var host=new GameObject("geometry live input test");PlayableWorld original=null,memo=null;GameObject a=null,b=null;bool previous=NativeGraphicsSettings.GroundTrailsEnabled;
            try{
                NativeGraphicsSettings.GroundTrailsEnabled=true;original=new PlayableWorld(host.transform,p,staged:true);memo=new PlayableWorld(host.transform,p,staged:true);original.Fog.ShowPublic();memo.Fog.ShowPublic();typeof(PlayableWorld).GetField("DisableTrailTerrainReuseForQa",Hidden).SetValue(original,true);
                var facts=Enumerable.Range(0,24).Select(i=>{var s=GroupTrailSample(50+i,i%3);return new PlayableGroundTrail(s.From,s.To,s.Heading,s.Distance,s.Tick,(PlayableEntityKind)(i%3));}).ToArray();
                byte[] beforeArt=null;
                for(int step=0;step<6;step++){
                    if(step==1){a=GeometryTestPad(original);b=GeometryTestPad(memo);}
                    if(step==2)foreach(var pad in new[]{a,b})pad.transform.position+=Vector3.right*100;
                    if(step==3)foreach(var pad in new[]{a,b}){pad.transform.position=original.Point(new NavPoint(-36,-23));pad.transform.SetParent(host.transform,true);pad.transform.rotation=Quaternion.Euler(0,37,0);pad.transform.localScale=new Vector3(.4f,1,1.8f);}
                    if(step==4){foreach(var world in new[]{original,memo})world.ClearPads();a=b=null;}
                    if(step==5)foreach(var world in new[]{original,memo}){world.TrailArt.tankGauge+=.031f;world.TrailArt.explorerRearContact+=.027f;world.TrailArt.shkvalPitch+=.013f;}
                    var last=facts[23];facts[23]=new PlayableGroundTrail(last.From,last.To,last.Heading,last.Distance,last.Tick,last.Kind);
                    var view=Frame(240+step,facts);original.RenderGroundTrails(view);memo.RenderGroundTrails(view);
                    CollectionAssert.AreEqual(TrailMeshBytes(original),TrailMeshBytes(memo),"moving/rotating/scaling/removing pads and changed chassis art step="+step);
                    if(step==4)beforeArt=TrailMeshBytes(original);
                    if(step==5)Assert.False(beforeArt.SequenceEqual(TrailMeshBytes(original)),"the actual world profile edits must change the rendered mesh");
                    if(step==0||step==2||step==5)Assert.Greater(memo.GroundTrailQuadCount,0);
                    if(step==1)Assert.Zero(memo.GroundTrailQuadCount,"new pad must clip already cached geometry");
                    if(step==1||step==2||step==3)
                        Assert.AreEqual(step!=3,(bool)typeof(PlayableWorld).GetField("trailPadMemoActive",Hidden).GetValue(memo),
                            "direct pads can memoize, external transforms must use live clipping");
                }
            }finally{NativeGraphicsSettings.GroundTrailsEnabled=previous;original?.Dispose();memo?.Dispose();UnityEngine.Object.DestroyImmediate(host);}
        }
        [Test] public void GeometryMemoReclipsFogWithoutRepeatingTerrainQueries()
        {
            var p=PlayableProfile.ThreeCrossingsDefault;var host=new GameObject("geometry moving fog test");PlayableWorld original=null,memo=null;bool previous=NativeGraphicsSettings.GroundTrailsEnabled;
            try{
                NativeGraphicsSettings.GroundTrailsEnabled=true;original=new PlayableWorld(host.transform,p,staged:true);memo=new PlayableWorld(host.transform,p,staged:true);original.Fog.ShowPublic();memo.Fog.ShowPublic();typeof(PlayableWorld).GetField("DisableTrailTerrainReuseForQa",Hidden).SetValue(original,true);
                var facts=Enumerable.Range(0,24).Select(i=>new PlayableGroundTrail(new NavPoint(-20+i*.25,i%3*2),new NavPoint(-19.75+i*.25,i%3*2),0,i*.25,100+i,(PlayableEntityKind)(i%3))).ToArray();
                var vision=new PlayableVision(0,p.ArenaHalfExtent,p.ArenaHalfExtent,p.VisionCellSize,p.FogEdgeFeather);long terrainCalls=-1;
                for(int step=0;step<8;step++){
                    vision.Refresh(new[]{new VisionSource(new NavPoint(-20+step,2),5)},Array.Empty<KnownBuilding>());
                    var view=new PlayableSnapshot(p.ProfileId,p.Revision,1,7,150+step,150+step,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,groundTrails:facts,vision:vision.Snapshot());
                    original.RenderGroundTrails(view);memo.RenderGroundTrails(view);CollectionAssert.AreEqual(TrailMeshBytes(original),TrailMeshBytes(memo),"cached geometry must clip to current fog step="+step);
                    long calls=(long)typeof(PlayableWorld).GetField("TrailTerrainQueriesForQa",Hidden).GetValue(memo)+(long)typeof(PlayableWorld).GetField("TrailTerrainHitsForQa",Hidden).GetValue(memo);
                    if(step==0)terrainCalls=calls;else Assert.AreEqual(terrainCalls,calls,"fog-only changes must reuse static geometry without surface queries");
                }
            }finally{NativeGraphicsSettings.GroundTrailsEnabled=previous;original?.Dispose();memo?.Dispose();UnityEngine.Object.DestroyImmediate(host);}
        }
        [Test] public void GeometryMemoFallsBackAtCapacityAndPrunesExpiredSamples()
        {
            var p=PlayableProfile.ThreeCrossingsDefault;var host=new GameObject("geometry budget test");PlayableWorld original=null,memo=null;bool previous=NativeGraphicsSettings.GroundTrailsEnabled;
            try{
                NativeGraphicsSettings.GroundTrailsEnabled=true;original=new PlayableWorld(host.transform,p,staged:true);memo=new PlayableWorld(host.transform,p,staged:true);original.Fog.ShowPublic();memo.Fog.ShowPublic();typeof(PlayableWorld).GetField("DisableTrailTerrainReuseForQa",Hidden).SetValue(original,true);
                var still=Enumerable.Range(0,8192).Select(i=>new PlayableGroundTrail(new NavPoint(-40,-24),new NavPoint(-40,-24),0,0,100,PlayableEntityKind.Tank));
                var active=Enumerable.Range(0,24).Select(i=>GroupTrailSample(50+i,i%3)).ToArray();var facts=still.Concat(active).ToArray();
                original.RenderGroundTrails(Frame(240,facts));memo.RenderGroundTrails(Frame(240,facts));Assert.Greater(memo.GroundTrailQuadCount,0);CollectionAssert.AreEqual(TrailMeshBytes(original),TrailMeshBytes(memo),"full memo must fall back to identical rendering");
                Assert.AreEqual(8192,((IDictionary)typeof(PlayableWorld).GetField("trailGeometry",Hidden).GetValue(memo)).Count);
                original.RenderGroundTrails(Frame(241,active));memo.RenderGroundTrails(Frame(241,active));CollectionAssert.AreEqual(TrailMeshBytes(original),TrailMeshBytes(memo));Assert.Zero(((IDictionary)typeof(PlayableWorld).GetField("trailGeometry",Hidden).GetValue(memo)).Count,"retired cache entries must release their sample references");
                memo.RenderGroundTrails(Frame(1000,active));Assert.Zero(memo.GroundTrailQuadCount);Assert.Zero((int)typeof(PlayableWorld).GetField("trailGeometryQuads",Hidden).GetValue(memo));Assert.Zero(((IList)typeof(PlayableWorld).GetField("trailGeometryRetired",Hidden).GetValue(memo)).Count);
            }finally{NativeGraphicsSettings.GroundTrailsEnabled=previous;original?.Dispose();memo?.Dispose();UnityEngine.Object.DestroyImmediate(host);}
        }
        [TestCase(30)][TestCase(50)]
        public void ScaledTrailMemoKeepsExactMeshAndReusesLiveWorkingSet(int count)
        {
            var p=PlayableProfile.ThreeCrossingsDefault;var host=new GameObject("scaled trail exact memo");PlayableWorld original=null,memo=null;bool previous=NativeGraphicsSettings.GroundTrailsEnabled;
            try{
                NativeGraphicsSettings.GroundTrailsEnabled=true;original=new PlayableWorld(host.transform,p,staged:true);memo=new PlayableWorld(host.transform,p,staged:true);original.Fog.ShowPublic();memo.Fog.ShowPublic();typeof(PlayableWorld).GetField("DisableTrailTerrainReuseForQa",Hidden).SetValue(original,true);
                var facts=new System.Collections.Generic.Queue<PlayableGroundTrail>();for(int step=0;step<100;step++)for(int actor=0;actor<count;actor++)facts.Enqueue(GroupTrailSample(step,actor));
                for(int step=100;step<103;step++){
                    for(int actor=0;actor<count;actor++){facts.Dequeue();facts.Enqueue(GroupTrailSample(step,actor));}
                    long queries=(long)typeof(PlayableWorld).GetField("TrailTerrainQueriesForQa",Hidden).GetValue(memo);
                    var view=Frame(step*3,facts.ToArray());original.RenderGroundTrails(view);memo.RenderGroundTrails(view);
                    CollectionAssert.AreEqual(TrailMeshBytes(original),TrailMeshBytes(memo),"scaled vertices/UVs/colors/triangles/bounds step="+step);
                    long added=(long)typeof(PlayableWorld).GetField("TrailTerrainQueriesForQa",Hidden).GetValue(memo)-queries;
                    if(step>100)Assert.Less(added,facts.Count,"old live terrain probes must survive the next ordered full scan");
                }
                Assert.LessOrEqual(((IDictionary)typeof(PlayableWorld).GetField("trailTerrainKinds",Hidden).GetValue(memo)).Count,131072);
            }finally{NativeGraphicsSettings.GroundTrailsEnabled=previous;original?.Dispose();memo?.Dispose();UnityEngine.Object.DestroyImmediate(host);}
        }
        [Test] public void ScaledTrailMemoCapacityIsCappedAndCleared()
        {
            var p=PlayableProfile.ThreeCrossingsDefault;var host=new GameObject("trail memo maximum");PlayableWorld world=null;bool previous=NativeGraphicsSettings.GroundTrailsEnabled;
            try{
                NativeGraphicsSettings.GroundTrailsEnabled=true;world=new PlayableWorld(host.transform,p,staged:true);world.Fog.ShowPublic();
                world.RenderGroundTrails(Frame(300,Enumerable.Repeat(GroupTrailSample(100,0),7000).ToArray()));
                Assert.AreEqual(131072,(int)typeof(PlayableWorld).GetField("trailTerrainCapacity",Hidden).GetValue(world));
                typeof(PlayableWorld).GetMethod("ClearGroundTrails",Hidden).Invoke(world,null);
                Assert.AreEqual(4096,(int)typeof(PlayableWorld).GetField("trailTerrainCapacity",Hidden).GetValue(world));Assert.Zero(((IDictionary)typeof(PlayableWorld).GetField("trailTerrainKinds",Hidden).GetValue(world)).Count);
            }finally{NativeGraphicsSettings.GroundTrailsEnabled=previous;world?.Dispose();UnityEngine.Object.DestroyImmediate(host);}
        }
        [TestCase(30)][TestCase(50)]
        public void ProfileTenSecondsOfMovingGroupTrails(int count)
        {
            var p=PlayableProfile.ThreeCrossingsDefault;var host=new GameObject("group trail scale probe");PlayableWorld world=null;bool previous=NativeGraphicsSettings.GroundTrailsEnabled;
            try{
                NativeGraphicsSettings.GroundTrailsEnabled=true;world=new PlayableWorld(host.transform,p,staged:true);world.Fog.ShowPublic();
                var facts=new System.Collections.Generic.Queue<PlayableGroundTrail>();
                for(int step=0;step<100;step++)for(int actor=0;actor<count;actor++)facts.Enqueue(GroupTrailSample(step,actor));
                var rows=new System.Collections.Generic.List<string>{"step,render_ms,terrain_queries,terrain_hits,quads"};
                for(int frame=-3;frame<30;frame++){
                    int step=103+frame;for(int actor=0;actor<count;actor++){facts.Dequeue();facts.Enqueue(GroupTrailSample(step,actor));}
                    long queries=(long)typeof(PlayableWorld).GetField("TrailTerrainQueriesForQa",Hidden).GetValue(world),hits=(long)typeof(PlayableWorld).GetField("TrailTerrainHitsForQa",Hidden).GetValue(world);
                    long start=Stopwatch.GetTimestamp();world.RenderGroundTrails(Frame(step*3,facts.ToArray()));double ms=(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency;
                    queries=(long)typeof(PlayableWorld).GetField("TrailTerrainQueriesForQa",Hidden).GetValue(world)-queries;hits=(long)typeof(PlayableWorld).GetField("TrailTerrainHitsForQa",Hidden).GetValue(world)-hits;
                    if(frame>=0)rows.Add(string.Format(CultureInfo.InvariantCulture,"{0},{1:R},{2},{3},{4}",frame,ms,queries,hits,world.GroundTrailQuadCount));
                }
                Assert.Greater(world.GroundTrailQuadCount,0);
                var output=Environment.GetEnvironmentVariable("SPACEWARS_SMALL_MOVEMENT_OUTPUT");if(output!=null){Directory.CreateDirectory(output);File.WriteAllLines(Path.Combine(output,"group-trails-"+count+".csv"),rows);}
            }finally{NativeGraphicsSettings.GroundTrailsEnabled=previous;world?.Dispose();UnityEngine.Object.DestroyImmediate(host);}
        }

        [Test]
        public void ProfileThreeCrossingsTrailRebuildCost()
        {
            var p=PlayableProfile.ThreeCrossingsDefault;var host=new GameObject("small movement trail CPU probe");PlayableWorld world=null;bool previous=NativeGraphicsSettings.GroundTrailsEnabled;
            try{
                NativeGraphicsSettings.GroundTrailsEnabled=true;world=new PlayableWorld(host.transform,p,staged:true);world.Fog.ShowPublic();
                var facts=new System.Collections.Generic.List<PlayableGroundTrail>();
                for(int step=0;step<60;step++)for(int unit=0;unit<3;unit++)facts.Add(new PlayableGroundTrail(new NavPoint(-22+step*.2,unit*2.5),new NavPoint(-21.8+step*.2,unit*2.5),0,step*.2,step,PlayableEntityKind.Tank));
                var samples=new double[60];
                for(int step=-3;step<samples.Length;step++){
                    int n=step+3;facts.RemoveAt(0);facts.Add(new PlayableGroundTrail(new NavPoint(-22+n*.2,-2.5),new NavPoint(-21.8+n*.2,-2.5),0,n*.2,100+n,PlayableEntityKind.Tank));
                    var view=new PlayableSnapshot(p.ProfileId,p.Revision,1,7319,200+n,200+n,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,groundTrails:facts.ToArray());
                    long start=Stopwatch.GetTimestamp();world.RenderGroundTrails(view);double ms=(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency;if(step>=0)samples[step]=ms;
                }
                Assert.Greater(world.GroundTrailQuadCount,0);var sorted=samples.OrderBy(x=>x).ToArray();TestContext.WriteLine(string.Format(CultureInfo.InvariantCulture,"TRAIL_REBUILD samples={0} quads={1} mean_ms={2:F6} p95_ms={3:F6} max_ms={4:F6}",samples.Length,world.GroundTrailQuadCount,samples.Average(),sorted[56],sorted[59]));
                var output=Environment.GetEnvironmentVariable("SPACEWARS_SMALL_MOVEMENT_OUTPUT");if(output!=null)File.WriteAllLines(Path.Combine(output,"trail-rebuild.csv"),new[]{"step,render_trails_ms"}.Concat(samples.Select((ms,i)=>i+","+ms.ToString("R",CultureInfo.InvariantCulture))));
            }finally{NativeGraphicsSettings.GroundTrailsEnabled=previous;world?.Dispose();UnityEngine.Object.DestroyImmediate(host);}
        }

  [Test] public void ProfileThreeMovingSourcesFogCpu()
  {
   var p=PlayableProfile.ThreeCrossingsDefault;var vision=new PlayableVision(0,p.ArenaHalfExtent,p.ArenaHalfExtent,p.VisionCellSize,p.FogEdgeFeather);var fog=new PlayableFogMask(p);
   try{
    var samples=new double[60];for(int step=-3;step<samples.Length;step++){
     int n=step+3;vision.Refresh(Enumerable.Range(0,3).Select(i=>new VisionSource(new NavPoint(-18+n*.12,i*2.5),PlayableUnitRules.Vision(p,PlayableEntityKind.Tank))).ToArray(),Array.Empty<KnownBuilding>());
     var v=vision.Snapshot();long start=Stopwatch.GetTimestamp();fog.Update(v,1f/30);if(step>=0)samples[step]=(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency;
    }
    var sorted=samples.OrderBy(x=>x).ToArray();TestContext.WriteLine(string.Format(CultureInfo.InvariantCulture,"FOG_REBUILD samples={0} mean_ms={1:F6} p95_ms={2:F6} max_ms={3:F6}",samples.Length,samples.Average(),sorted[56],sorted[59]));
   }finally{fog.Dispose();}
  }
  static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
  static Mesh DisplayedTrailMesh(PlayableWorld world)=>(Mesh)typeof(PlayableWorld).GetField("trailMesh",Hidden).GetValue(world);
  static void RenderSharedTrails(PlayableWorld world,PlayableSnapshot view,PlayableWorld source=null,Camera camera=null)
  {
   typeof(PlayableWorld).GetMethod("RenderGroundTrails",Hidden,null,
       new[]{typeof(PlayableSnapshot),typeof(Camera),typeof(bool),typeof(PlayableWorld)},null)
       .Invoke(world,new object[]{view,camera,true,source});
  }
  [Test] public void LocalAlliesShareExactTrailMeshesWithIndependentVisibilityAndLifetime()
  {
   var p=PlayableProfile.ThreeCrossingsDefault;
   var host=new GameObject("allied trail mesh ownership");
   bool previous=NativeGraphicsSettings.GroundTrailsEnabled;
   var donor=new PlayableWorld(host.transform,p,staged:true);
   var peer=new PlayableWorld(host.transform,p,staged:true);
   var reference=new PlayableWorld(host.transform,p,staged:true);
   try
   {
    NativeGraphicsSettings.GroundTrailsEnabled=true;
    donor.SetPresentationLayer(8);peer.SetPresentationLayer(11);
    var samples=Enumerable.Range(0,24).Select(i=>new PlayableGroundTrail(
        new NavPoint(-25+i*.25,1),new NavPoint(-24.75+i*.25,1),0,i*.25,100+i,PlayableEntityKind.Tank)).ToArray();
    TeamVisionSnapshot Vision(double x,double radius)
    {
     var vision=new PlayableVision(0,p.ArenaHalfExtent,p.ArenaHalfExtent,p.VisionCellSize,p.FogEdgeFeather);
     vision.Refresh(new[]{new VisionSource(new NavPoint(x,1),radius)},Array.Empty<KnownBuilding>());
     return vision.Snapshot();
    }
    var sharedVision=Vision(-22,14);
    PlayableSnapshot View(long tick,PlayableGroundTrail[] facts,TeamVisionSnapshot vision,PlayableOwner owner,int team=0)
        =>new PlayableSnapshot(p.ProfileId,p.Revision,1,7,tick,tick,RuntimeStatus.Running,false,
            PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),
            Array.Empty<PlayableProjectileSnapshot>(),null,null,groundTrails:facts,vision:vision,owner:owner,team:team);

    RenderSharedTrails(donor,View(150,samples,sharedVision,PlayableOwner.Player));
    var peerView=View(150,samples,sharedVision,PlayableOwner.Enemy);
    RenderSharedTrails(peer,peerView,donor);
    reference.RenderGroundTrails(peerView);
    var oldMesh=DisplayedTrailMesh(peer);
    var oldBytes=TrailMeshBytes(peer);
    Assert.Greater(peer.GroundTrailQuadCount,0);
    Assert.AreSame(DisplayedTrailMesh(donor),oldMesh);
    CollectionAssert.AreEqual(TrailMeshBytes(reference),oldBytes);
    var donorRenderer=donor.GraphicsRoot.Find("Ground vehicle trails").GetComponent<MeshRenderer>();
    var peerRenderer=peer.GraphicsRoot.Find("Ground vehicle trails").GetComponent<MeshRenderer>();
    Assert.AreNotSame(donorRenderer.sharedMaterial,peerRenderer.sharedMaterial);
    Assert.AreSame(peer.Fog.Texture,peerRenderer.sharedMaterial.GetTexture("_FogMask"));
    Assert.AreEqual(11,peerRenderer.gameObject.layer);

    var fewer=samples.Take(12).ToArray();
    var nextVision=Vision(-22,13);
    RenderSharedTrails(donor,View(151,fewer,nextVision,PlayableOwner.Player));
    Assert.AreNotSame(DisplayedTrailMesh(donor),oldMesh);
    CollectionAssert.AreEqual(oldBytes,TrailMeshBytes(peer),"donor update leaves the borrowed mesh untouched");
    peerView=View(151,fewer,nextVision,PlayableOwner.Enemy);
    RenderSharedTrails(peer,peerView,donor);reference.RenderGroundTrails(peerView);
    Assert.AreSame(DisplayedTrailMesh(donor),DisplayedTrailMesh(peer));
    CollectionAssert.AreEqual(TrailMeshBytes(reference),TrailMeshBytes(peer));

    var pad=GeometryTestPad(peer);GeometryTestPad(reference);
    var padVision=Vision(-22,12);
    RenderSharedTrails(donor,View(152,samples,padVision,PlayableOwner.Player));
    peerView=View(152,samples,padVision,PlayableOwner.Enemy);
    RenderSharedTrails(peer,peerView,donor);reference.RenderGroundTrails(peerView);
    Assert.AreNotSame(DisplayedTrailMesh(donor),DisplayedTrailMesh(peer),"independent construction pads");
    CollectionAssert.AreEqual(TrailMeshBytes(reference),TrailMeshBytes(peer));
    pad.SetActive(false); // Keep a differing pad and use a different immutable view too.
    var separateVision=Vision(25,5);
    peerView=View(153,fewer,separateVision,PlayableOwner.Enemy);
    RenderSharedTrails(donor,View(153,fewer,padVision,PlayableOwner.Player));
    RenderSharedTrails(peer,peerView,donor);reference.RenderGroundTrails(peerView);
    Assert.AreNotSame(DisplayedTrailMesh(donor),DisplayedTrailMesh(peer));
    CollectionAssert.AreEqual(TrailMeshBytes(reference),TrailMeshBytes(peer));
    Assert.Zero(peer.GroundTrailQuadCount);

    peer.Clear();reference.Clear();
    var finalVision=Vision(-22,15);
    RenderSharedTrails(donor,View(154,samples,finalVision,PlayableOwner.Player));
    peerView=View(154,samples,finalVision,PlayableOwner.Enemy);
    RenderSharedTrails(peer,peerView,donor);
    Assert.AreSame(DisplayedTrailMesh(donor),DisplayedTrailMesh(peer));
    var finalBytes=TrailMeshBytes(peer);
    donor.Rebind(p);
    CollectionAssert.AreEqual(finalBytes,TrailMeshBytes(peer),"a rebind cannot retire a borrowed mesh");
    var expired=View(123+p.GroundTrailLifetimeTicks,samples,finalVision,PlayableOwner.Enemy);
    RenderSharedTrails(peer,expired,donor);reference.RenderGroundTrails(expired);
    Assert.Zero(peer.GroundTrailQuadCount);
    CollectionAssert.AreEqual(TrailMeshBytes(reference),TrailMeshBytes(peer));
   }
   finally
   {
    NativeGraphicsSettings.GroundTrailsEnabled=previous;
    donor.Dispose();peer.Dispose();reference.Dispose();
    UnityEngine.Object.DestroyImmediate(host);
   }
  }
  static byte[] TrailMeshBytes(PlayableWorld world)
  {
   var mesh=(Mesh)typeof(PlayableWorld).GetField("trailMesh",Hidden).GetValue(world);using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){
    var vertices=mesh.vertices;writer.Write(vertices.Length);foreach(var v in vertices){writer.Write(v.x);writer.Write(v.y);writer.Write(v.z);}foreach(var i in mesh.triangles)writer.Write(i);
    for(int channel=0;channel<2;channel++){var uv=new System.Collections.Generic.List<Vector4>();mesh.GetUVs(channel,uv);foreach(var v in uv){writer.Write(v.x);writer.Write(v.y);writer.Write(v.z);writer.Write(v.w);}}
    foreach(var c in mesh.colors){writer.Write(c.r);writer.Write(c.g);writer.Write(c.b);writer.Write(c.a);}writer.Write(mesh.bounds.center.x);writer.Write(mesh.bounds.center.y);writer.Write(mesh.bounds.center.z);writer.Write(mesh.bounds.size.x);writer.Write(mesh.bounds.size.y);writer.Write(mesh.bounds.size.z);return stream.ToArray();
   }
  }
  [Test] public void ThreeCrossingsTrailMemoKeepsMeshFogExpiryAndDynamicPadsExact()
  {
   var p=PlayableProfile.ThreeCrossingsDefault;var host=new GameObject("trail exact memo test");PlayableWorld a=null,b=null;bool previous=NativeGraphicsSettings.GroundTrailsEnabled;
   try{
    NativeGraphicsSettings.GroundTrailsEnabled=true;a=new PlayableWorld(host.transform,p,staged:true);b=new PlayableWorld(host.transform,p,staged:true);a.Fog.ShowPublic();b.Fog.ShowPublic();typeof(PlayableWorld).GetField("DisableTrailTerrainReuseForQa",Hidden).SetValue(a,true);typeof(PlayableWorld).GetField("DisableTrailGeometryReuseForQa",Hidden).SetValue(b,true);
    var facts=Enumerable.Range(0,24).Select(i=>new PlayableGroundTrail(new NavPoint(-20+i*.25,i%3*2),new NavPoint(-19.75+i*.25,i%3*2),0,i*.25,100+i,(PlayableEntityKind)(i%3))).ToArray();
    var vision=new PlayableVision(0,p.ArenaHalfExtent,p.ArenaHalfExtent,p.VisionCellSize,p.FogEdgeFeather);
    for(int step=0;step<8;step++){
     vision.Refresh(new[]{new VisionSource(new NavPoint(-20+step,2),5)},Array.Empty<KnownBuilding>());
     var v=new PlayableSnapshot(p.ProfileId,p.Revision,1,7,150+step,150+step,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,groundTrails:facts,vision:vision.Snapshot());
     a.RenderGroundTrails(v);b.RenderGroundTrails(v);CollectionAssert.AreEqual(TrailMeshBytes(a),TrailMeshBytes(b),"all vertices/UVs/colors/indices/bounds and fog clipping step="+step);
    }
    Assert.Greater((long)typeof(PlayableWorld).GetField("TrailTerrainHitsForQa",Hidden).GetValue(b),0);
    Assert.Less((long)typeof(PlayableWorld).GetField("TrailTerrainQueriesForQa",Hidden).GetValue(b),(long)typeof(PlayableWorld).GetField("TrailTerrainQueriesForQa",Hidden).GetValue(a));
    var point=new NavPoint(-18,0);Assert.AreEqual(GroundTrailSurfaceKind.Soil,b.TrailSurface(point));
    var root=new GameObject("dynamic pad after cached ground");root.transform.SetParent(host.transform);root.transform.position=b.Point(point);
    var padType=typeof(PlayableWorld).GetNestedType("PadView",BindingFlags.NonPublic);var pad=Activator.CreateInstance(padType,true);padType.GetField("Root").SetValue(pad,root);padType.GetField("Surface").SetValue(pad,root.AddComponent<MeshFilter>());padType.GetField("Half").SetValue(pad,new Vector2(1,1));
    ((IDictionary)typeof(PlayableWorld).GetField("territoryPads",Hidden).GetValue(b)).Add("memo-test",pad);Assert.AreEqual(GroundTrailSurfaceKind.Hard,b.TrailSurface(point),"new pad still occludes previously cached soft terrain");
    foreach(var w in new[]{a,b})w.RenderGroundTrails(Frame(1000,facts));Assert.Zero(a.GroundTrailQuadCount);Assert.Zero(b.GroundTrailQuadCount);
    b.Rebind(p);Assert.Zero(((IDictionary)typeof(PlayableWorld).GetField("trailTerrainKinds",Hidden).GetValue(b)).Count);
   }finally{NativeGraphicsSettings.GroundTrailsEnabled=previous;a?.Dispose();b?.Dispose();UnityEngine.Object.DestroyImmediate(host);}
  }
  [Test] public void ThreeCrossingsTrailMemoIsBoundedAndOtherTerrainDoesNotOptIn()
  {
   var p=PlayableProfile.ThreeCrossingsDefault;var host=new GameObject("trail memo bound");PlayableWorld world=null,other=null;
   try{
    world=new PlayableWorld(host.transform,p,staged:true);for(int i=0;i<4100;i++)world.TrailSurface(new NavPoint(-18+i*.0001,-2));
    Assert.AreEqual(4096,((IDictionary)typeof(PlayableWorld).GetField("trailTerrainKinds",Hidden).GetValue(world)).Count);Assert.AreEqual(GroundTrailSurface.At(p.AuthoredMap,new NavPoint(-18,-2)),world.TrailSurface(new NavPoint(-18,-2)));
    other=new PlayableWorld(host.transform,PlayableProfile.Default,staged:true);other.TrailSurface(default(NavPoint));Assert.Zero(((IDictionary)typeof(PlayableWorld).GetField("trailTerrainKinds",Hidden).GetValue(other)).Count);
   }finally{world?.Dispose();other?.Dispose();UnityEngine.Object.DestroyImmediate(host);}
  }
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
  [Test] public void DistantCameraHidesAllTrailsAndNearReturnUsesCurrentAge()
  {
   bool old=NativeGraphicsSettings.GroundTrailsEnabled;var host=new GameObject("zoom-trail-test");PlayableWorld world=null;
   try{
    NativeGraphicsSettings.GroundTrailsEnabled=true;var detail=ModelDetailProfile.Load();ModelDetailProfile.UseSession(detail);
    var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());world=new PlayableWorld(host.transform,p);world.Fog.ShowPublic();
    var camera=new GameObject("trail camera").AddComponent<Camera>();camera.transform.SetParent(host.transform);camera.orthographic=true;camera.orthographicSize=detail.trailHideZoom-1;
    var facts=new[]{new PlayableGroundTrail(new NavPoint(0,0),new NavPoint(2,0),0,0,3,PlayableEntityKind.Tank)};var frame=Frame(4,facts);
    world.RenderGroundTrails(frame,camera);var obj=host.GetComponentsInChildren<Transform>().Single(t=>t.name=="Ground vehicle trails").gameObject;int count=world.GroundTrailQuadCount;Assert.Greater(count,0);
    camera.orthographicSize=detail.trailHideZoom;world.RenderGroundTrails(frame,camera);Assert.False(obj.activeSelf,"all trail rendering disabled at far threshold");
    camera.orthographicSize--;world.RenderGroundTrails(frame,camera);Assert.True(obj.activeSelf,"zoom works on the same paused snapshot");Assert.AreEqual(count,world.GroundTrailQuadCount);
    camera.orthographicSize=detail.trailHideZoom;world.RenderGroundTrails(Frame(303,facts),camera);Assert.False(obj.activeSelf);
    camera.orthographicSize--;world.RenderGroundTrails(Frame(303,facts),camera);Assert.Zero(world.GroundTrailQuadCount,"far zoom must not freeze trail age");
   }finally{world?.Dispose();UnityEngine.Object.DestroyImmediate(host);NativeGraphicsSettings.GroundTrailsEnabled=old;ModelDetailProfile.UseSession(null);}
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
  [Test] public void CrimsonGroveUsesRenderedCoverageAndKeepsSlabsHardAcrossRebind()
  {
   var map=new CrimsonGroveMap();
   var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
   var host=new GameObject("grove trail material test");
   bool previous=NativeGraphicsSettings.GroundTrailsEnabled;
   PlayableWorld world=null;
   try{
    NativeGraphicsSettings.GroundTrailsEnabled=true;
    world=new PlayableWorld(host.transform,profile);
    world.Fog.ShowPublic();
    var mask=(Texture2D)typeof(PlayableWorld).GetField("groveSurfaceMask",Hidden).GetValue(world);
    Assert.IsNotNull(mask);
    var sites=map.Sites(profile).Where(s=>TerritoryRules.Center(s.Kind)).ToArray();
    foreach(var site in sites)Assert.AreEqual(GroundTrailSurfaceKind.Hard,world.TrailSurface(site.Position),"complete concrete apron");
    Assert.AreEqual(GroundTrailSurfaceKind.Hard,world.TrailSurface(new NavPoint(double.NaN,0)));
    Assert.AreEqual(GroundTrailSurfaceKind.Hard,world.TrailSurface(new NavPoint(map.HalfExtent*2,0)));
    var art=CrimsonGroveArtProfile.Load();
    var aprons=sites.Select(s=>CrimsonGroveSurfaces.Pad(s,profile,art)).ToArray();
    var points=Enumerable.Range(0,180).SelectMany(i=>Enumerable.Range(0,40).Select(r=>map.Polar(i*2,20+r*3)))
        .Where(p=>map.SupportsFootprint(p,0)&&!aprons.Any(a=>a.Contains(p))).ToArray();
    Color Coverage(NavPoint p)=>mask.GetPixelBilinear((float)(p.X/(2*map.HalfExtent)+.5),(float)(p.Z/(2*map.HalfExtent)+.5));
    var bedrock=points.First(p=>Coverage(p).b>.95f);
    var exposedRoad=points.First(p=>Coverage(p).a>.55f&&Coverage(p).b<.3f);
    Assert.AreEqual(GroundTrailSurfaceKind.Hard,world.TrailSurface(bedrock),"exposed bedrock on a drivable support");
    Assert.AreEqual(GroundTrailSurfaceKind.Hard,world.TrailSurface(exposedRoad),"exposed road on a drivable support");
    var soil=points.First(p=>Enumerable.Range(-3,7).All(x=>Enumerable.Range(-2,5).All(z=>
        world.TrailSurface(new NavPoint(p.X+x,p.Z+z))==GroundTrailSurfaceKind.Soil)));
    var facts=new[]{new PlayableGroundTrail(soil,new NavPoint(soil.X+2,soil.Z),0,0,1,PlayableEntityKind.Tank)};
    world.RenderGroundTrails(Frame(2,facts));
    Assert.Greater(world.GroundTrailQuadCount,0,"loose ground produces impressions");
    var original=TrailMeshBytes(world);
    world.Rebind(PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap()));
    Assert.AreEqual(GroundTrailSurfaceKind.Sand,world.TrailSurface(sites[0].Position));
    world.Rebind(profile);world.Fog.ShowPublic();world.RenderGroundTrails(Frame(3,facts));
    CollectionAssert.AreEqual(original,TrailMeshBytes(world));
    Assert.AreEqual(GroundTrailSurfaceKind.Hard,world.TrailSurface(sites[0].Position));
   }finally{world?.Dispose();UnityEngine.Object.DestroyImmediate(host);NativeGraphicsSettings.GroundTrailsEnabled=previous;}
  }

  [Test] public void MossFormerDeckIsReceptiveOutsideTheActiveApron()
  {
   var map=new MossHighlandMap();
   var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
   var host=new GameObject("moss trail material test");
   PlayableWorld world=null;
   try{
    world=new PlayableWorld(host.transform,profile,staged:true);
    var formerDeck=new NavPoint(-36.5,0);
    Assert.That(map.Materials.Last(p=>p.Bounds.Contains(formerDeck)).Material,Is.EqualTo("metal-deck"));
    Assert.AreEqual(GroundTrailSurfaceKind.Grass,world.TrailSurface(formerDeck));
    var current=map.Sites(profile).Single(s=>s.Kind==PlayableBuildingKind.Outpost&&s.Position.X<0);
    Assert.AreEqual(GroundTrailSurfaceKind.Hard,world.TrailSurface(current.Position));
    var river=new NavPoint(-55,27);
    Assert.True(map.BorderRivers.Any(r=>r.Contains(river)));
    Assert.AreEqual(GroundTrailSurfaceKind.Hard,world.TrailSurface(river));
   }finally{world?.Dispose();UnityEngine.Object.DestroyImmediate(host);}
  }

  [Test] public void CrossingsMineConcreteRejectsTrailsOnTheBank()
  {
   var profile=PlayableProfile.ThreeCrossingsDefault;
   var map=(ThreeCrossingsMap)profile.AuthoredMap;
   var art=ThreeCrossingsSurfaceProfile.Load();
   var mines=ThreeCrossingsMaterials.PlanMineAccess(art,map,profile);
   var host=new GameObject("crossings mine trail material test");
   PlayableWorld world=null;
   try{
    world=new PlayableWorld(host.transform,profile,staged:true);
    foreach(var access in mines){
     var site=access.Site.Position;
     Assert.AreEqual(GroundTrailSurfaceKind.Soil,GroundTrailSurface.At(map,site),"base support is a bank");
     Assert.AreEqual(GroundTrailSurfaceKind.Hard,world.TrailSurface(site),"concrete overlay wins");
     var direction=new NavPoint(access.Connection.X-site.X,access.Connection.Z-site.Z);
     var length=Math.Sqrt(direction.X*direction.X+direction.Z*direction.Z);
     NavPoint At(double distance)=>new NavPoint(site.X+direction.X/length*distance,site.Z+direction.Z/length*distance);
     var edge=art.mineYardRadius-art.minePadContactWidth*.5f;
     Assert.AreEqual(GroundTrailSurfaceKind.Hard,world.TrailSurface(At(edge-.15)));
     Assert.AreEqual(GroundTrailSurfaceKind.Soil,world.TrailSurface(At(edge+.15)));
    }
   }finally{world?.Dispose();UnityEngine.Object.DestroyImmediate(host);}
  }

  [Test] public void AuthoredMaterialsExcludeMetalRockAndIndustrialFoundry()
  {
   var moss=new MossHighlandMap();foreach(var patch in moss.Materials){var p=new NavPoint((patch.Bounds.MinX+patch.Bounds.MaxX)*.5,(patch.Bounds.MinZ+patch.Bounds.MaxZ)*.5);if(moss.Materials.Last(x=>x.Bounds.Contains(p)).Material!=patch.Material||moss.SupportAt(p)==null)continue;
    if(patch.Material.StartsWith("mountain."))Assert.AreEqual(GroundTrailSurfaceKind.Hard,GroundTrailSurface.At(moss,p));}
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
  {var p=GroundTrailProfile.Load();Assert.AreEqual(30,p.Parameters().Count());foreach(var f in p.Parameters()){float old=f.Read();Assert.IsNotEmpty(f.Description);Assert.Greater(f.Step,0);f.Write(float.NaN);Assert.Throws<ArgumentException>(()=>p.Validate());f.Write(old);}p.Validate();}
 }
}
