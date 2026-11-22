using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using Object = UnityEngine.Object;
namespace Spacewars.Tests.EditMode
{
 public sealed class TankMechanismTests
 {
  static PlayableSnapshot Frame(long tick,bool paused=false,bool shot=false)=>new PlayableSnapshot("tank-test",1,1,7319,tick+1,tick,RuntimeStatus.Running,paused,PlayableMatchOutcome.Playing,0,new NavGeometry(20,Array.Empty<NavObstacle>(),1),Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sounds:shot?new[]{new PlayableSoundEvent(1,3,PlayableSoundKind.Shot,new NavPoint(0,0),PlayableEntityKind.Tank,unitId:3)}:Array.Empty<PlayableSoundEvent>());

  [TestCase("tankHybrid")][TestCase("tankHybridUpgraded")]
  public void SharedShotIndexPreservesRecoilAndMuzzleAcrossPublications(string key)
  {
   var prefab=Resources.Load<GameObject>("StyleA/"+key);
   var reference=Object.Instantiate(prefab).AddComponent<TankMechanismPresentation>();
   var candidate=Object.Instantiate(prefab).AddComponent<TankMechanismPresentation>();
   var binding=BindingFlags.NonPublic|BindingFlags.Instance;
   var indexType=typeof(TankMechanismPresentation).Assembly.GetType("Spacewars.Presentation.TankShotIndex",true);
   var index=Activator.CreateInstance(indexType,true);
   var drive=typeof(TankMechanismPresentation).GetMethod("DriveWithShotIndex",binding);
   var stateFields=new[]{"lastShot","shotTime","renderedRecoil","renderedFlashPhase","flash"}
    .Select(name=>typeof(TankMechanismPresentation).GetField(name,binding)).ToArray();
   try
   {
    var initial=new[]{Shot(1,0,3)};
    var first=new[]{Shot(2,1,3),Shot(3,1,7),Shot(2,1,3)};
    var paused=new[]{Shot(4,2,3),Shot(5,2,3,PlayableSoundKind.Impact)};
    var reordered=new[]{Shot(8,4,7),Shot(7,4,3),Shot(9,3,3),Shot(9,4,3),Shot(10,4,3,weapon:PlayableEntityKind.Hawk)};
    var frames=new[]{MakeFrame(0,1,false,initial),MakeFrame(1,1,false,first),MakeFrame(1,1,false,first),
     MakeFrame(2,1,true,paused),MakeFrame(3,1,false,paused),MakeFrame(4,1,false,reordered),
     MakeFrame(0,1,false,initial),MakeFrame(0,2,false,reordered),MakeFrame(1,2,false,Array.Empty<PlayableSoundEvent>())};
    bool flashed=false;
    for(int i=0;i<frames.Length;i++)
    {
     var pose=new PlayableDisplayPose(new NavPoint(i*.14,i*.07),i*.04,i*.02);
     reference.Drive(pose,frames[i],3);drive.Invoke(candidate,new object[]{pose,frames[i],3,index});
     foreach(var field in stateFields)
     {
      var expected=field.GetValue(reference);var actual=field.GetValue(candidate);
      if(expected is GameObject flash){flashed|=flash.activeSelf;Assert.AreEqual(flash.activeSelf,((GameObject)actual).activeSelf,field.Name);}
      else Assert.AreEqual(expected,actual,field.Name+" at frame "+i);
     }
     Vector3 Barrel(Component model)=>model.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="barrelRecoil").localPosition;
     Assert.AreEqual(Barrel(reference),Barrel(candidate),"barrel at frame "+i);
    }
    Assert.True(flashed,"An observed fresh shot must actually exercise the flash");
   }
   finally{Object.DestroyImmediate(reference.gameObject);Object.DestroyImmediate(candidate.gameObject);}
  }

  private static PlayableSoundEvent Shot(long id,long tick,int unit,PlayableSoundKind kind=PlayableSoundKind.Shot,
   PlayableEntityKind weapon=PlayableEntityKind.Tank)=>new PlayableSoundEvent(id,tick,kind,default,weapon,unitId:unit);

  private static PlayableSnapshot MakeFrame(long tick,long generation,bool paused,PlayableSoundEvent[] sounds)=>
   new PlayableSnapshot("tank-test",1,generation,7319,tick+1,tick,RuntimeStatus.Running,paused,PlayableMatchOutcome.Playing,
    0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),
    new PlayableRuntimeMetrics(0,0,0,0,0),null,sounds:sounds);

  [Test] public void DefaultWorldUsesAcceptedTankAndKeepsLegacyComparisonAvailable()
  {
   var host=new GameObject("Tank default integration");
   try {
    var world=new PlayableWorld(host.transform,PlayableProfile.Default);
    Assert.IsTrue(world.HybridTanks);
    var actor=world.Tank(1,true);
    Assert.NotNull(actor.TankMechanism);
    Assert.IsTrue(actor.Hull.name.StartsWith("tankHybrid",StringComparison.Ordinal));
    world.HybridTanks=false;
    var legacy=world.Tank(2,true);
    Assert.IsNull(legacy.TankMechanism);
   }finally{UnityEngine.Object.DestroyImmediate(host);}
  }
  [TestCase("tankHybrid")][TestCase("tankHybridUpgraded")]
  public void FastTrackSamplingMatchesSignedWrapAndAuthoredMechanism(string key)
  {
   var prefab=Resources.Load<GameObject>("StyleA/"+key);
   var stock=Object.Instantiate(prefab).AddComponent<TankMechanismPresentation>();
   var fast=Object.Instantiate(prefab).AddComponent<TankMechanismPresentation>();
   try
   {
    stock.Initialize();fast.Initialize();
    var binding=BindingFlags.NonPublic|BindingFlags.Instance;
    var mode=typeof(TankMechanismPresentation).GetProperty("FastSamplingForQa",binding);
    mode.SetValue(stock,false);
    var wrap=(Func<float,float>)typeof(TankMechanismPresentation).GetMethod("WrapLinkDistance",binding)
     .CreateDelegate(typeof(Func<float,float>),fast);
    float length=fast.TrackLoopLength;
    var random=new System.Random(19092026);
    for(int i=0;i<65536;i++)Check((float)((3*random.NextDouble()-1)*length));
    foreach(float value in new[]{float.Epsilon,-float.Epsilon,-0f,0f,-length,length,2*length})
    {
     int bits=BitConverter.SingleToInt32Bits(value);
     for(int offset=-4;offset<=4;offset++)Check(BitConverter.Int32BitsToSingle(unchecked(bits+offset)));
    }

    var reference=Tracked(stock);var actual=Tracked(fast);
    Assert.AreEqual(reference.Length,actual.Length);
    Assert.GreaterOrEqual(actual.Length,85);
    for(int frame=0;frame<96;frame++)
    {
     long tick=frame<56?frame:frame-56;
     var view=Frame(tick,paused:frame%13==0,shot:frame%19==0);
     var pose=new PlayableDisplayPose(new NavPoint(Math.Sin(frame*.27)*2.5,Math.Cos(frame*.11)*1.7),Math.Sin(frame*.21)*Math.PI,0);
     stock.Drive(pose,view,3);fast.Drive(pose,view,3);
     for(int i=0;i<reference.Length;i++)
     {
      Assert.AreEqual(reference[i].name,actual[i].name);
      Assert.True(reference[i].localPosition.Equals(actual[i].localPosition),$"{frame} {actual[i].name} position");
      Assert.True(reference[i].localRotation.Equals(actual[i].localRotation),$"{frame} {actual[i].name} rotation");
      Assert.True(reference[i].localToWorldMatrix.Equals(actual[i].localToWorldMatrix),$"{frame} {actual[i].name} matrix");
     }
    }

    void Check(float distance)
    {
     float expected=Mathf.Repeat(distance,length),measured=wrap(distance);
     Assert.AreEqual(BitConverter.SingleToInt32Bits(expected),BitConverter.SingleToInt32Bits(measured),$"wrapped {distance:R}");
    }
    Transform[] Tracked(Component mechanism)=>mechanism.GetComponentsInChildren<Transform>(true)
     .Where(node=>node.name.StartsWith("tread",StringComparison.Ordinal)||node.name.StartsWith("wheel",StringComparison.Ordinal)||
      node.name.StartsWith("engineRotor",StringComparison.Ordinal)||node.name=="barrelRecoil")
     .OrderBy(node=>node.name,StringComparer.Ordinal).ToArray();
   }
   finally{Object.DestroyImmediate(stock.gameObject);Object.DestroyImmediate(fast.gameObject);}
  }
  [TestCase("tankHybrid")][TestCase("tankHybridUpgraded")]
  public void AnimatedLoopMatchesAuthoredTrackAndClosesWithoutJump(string key)
  {
   var go=UnityEngine.Object.Instantiate(Resources.Load<GameObject>("StyleA/"+key));
   try
   {
    var mechanic=go.AddComponent<TankMechanismPresentation>();mechanic.Initialize();
    var nodes=go.GetComponentsInChildren<Transform>();
    for(int i=0;i<36;i++)
    {
     mechanic.SampleTrack(i*mechanic.TrackLoopLength/36,out float z,out float y,out float angle);
     foreach(var side in new[]{"L","R"})
     {
      var tread=nodes.Single(t=>t.name=="tread"+side+i.ToString("D2"));
      Assert.Less(Vector2.Distance(new Vector2(z,y),new Vector2(tread.localPosition.z,tread.localPosition.y)),.0001f,"authored/animated loop mismatch");
      Assert.Less(Quaternion.Angle(Quaternion.Euler(angle*Mathf.Rad2Deg,0,0),tread.localRotation),.05f);
     }
    }
    mechanic.SampleTrack(0,out float z0,out float y0,out float a0);
    mechanic.SampleTrack(mechanic.TrackLoopLength-.00001f,out float z1,out float y1,out float a1);
    Assert.Less(Vector2.Distance(new Vector2(z0,y0),new Vector2(z1,y1)),.0001f);
    Assert.Less(Quaternion.Angle(Quaternion.Euler(a0*Mathf.Rad2Deg,0,0),Quaternion.Euler(a1*Mathf.Rad2Deg,0,0)),.05f);
    mechanic.SampleTrack(.8f,out _,out float forwardHeight,out _);
    if(key.EndsWith("Upgraded"))Assert.Less(forwardHeight,y0-.08f,"upgraded upper run must descend toward nose");
    else Assert.AreEqual(y0,forwardHeight,.0001f);
   }finally{UnityEngine.Object.DestroyImmediate(go);}
  }
  [TestCase("tankHybrid")][TestCase("tankHybridUpgraded")]
  public void PackedTrackAssetsHaveConsistentVertexIndexAndSkinBuffers(string key)
  {
   var prefab=Resources.Load<GameObject>("StyleA/"+key);
   var tracks=prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
   Assert.AreEqual(2,tracks.Length);
   foreach(var track in tracks)
   {
    var mesh=track.sharedMesh;
    Assert.AreEqual(42,track.bones.Length);
    Assert.AreEqual(mesh.vertexCount,mesh.normals.Length);
    Assert.AreEqual(mesh.vertexCount,mesh.boneWeights.Length);
    Assert.Greater(mesh.vertexCount,0);
    foreach(var index in mesh.triangles)Assert.That(index,Is.InRange(0,mesh.vertexCount-1));
    for(int sub=0;sub<mesh.subMeshCount;sub++)
    {
     var range=mesh.GetSubMesh(sub);
     Assert.LessOrEqual(range.firstVertex+range.vertexCount,mesh.vertexCount,"stale vertex stream after asset update");
    }
    foreach(var weight in mesh.boneWeights)
    {Assert.That(weight.boneIndex0,Is.InRange(0,41));Assert.AreEqual(1,weight.weight0);}
   }
  }
  [TestCase("tankHybrid")][TestCase("tankHybridUpgraded")]
  public void UnchangedTrackPoseDoesNotDirtyBonesAndRewindStillReappliesThem(string key)
  {
   var go=UnityEngine.Object.Instantiate(Resources.Load<GameObject>("StyleA/"+key));
   try{
    var mechanic=go.AddComponent<TankMechanismPresentation>();mechanic.Initialize();
    var pose=new PlayableDisplayPose(new NavPoint(0,0),0,0);
    mechanic.Drive(pose,Frame(0),3);
    var bones=go.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("treadL",StringComparison.Ordinal)||t.name.StartsWith("treadR",StringComparison.Ordinal)||t.name.StartsWith("wheelL",StringComparison.Ordinal)||t.name.StartsWith("wheelR",StringComparison.Ordinal)).ToArray();
    Assert.AreEqual(84,bones.Length);
    var barrel=go.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="barrelRecoil");
    foreach(var bone in bones)bone.hasChanged=false;
    barrel.hasChanged=false;
    mechanic.Drive(pose,Frame(1),3);
    Assert.False(bones.Any(b=>b.hasChanged),"An idle unit must not dirty 84 skinning bones on every rendered frame");
    Assert.False(barrel.hasChanged,"An idle barrel should keep its transform clean");
    var original=bones[0].localPosition;
    bones[0].localPosition=original+Vector3.one;
    mechanic.Drive(pose,Frame(0),3);
    Assert.AreEqual(original,bones[0].localPosition,"A rewound presentation reapplies its authoritative track pose");
   }finally{UnityEngine.Object.DestroyImmediate(go);}
  }
  [Test] public void WorldReusesStationaryTankPoseAndStillAppliesMotion()
  {
   var host=new GameObject("Tank pose reuse");
   try{
    var world=new PlayableWorld(host.transform,PlayableProfile.Default);
    var actor=world.Tank(3,true);
    var pose=new PlayableDisplayPose(new NavPoint(2,1),.2,-.1);
    world.ApplyUnitPose(actor,PlayableEntityKind.Tank,pose);
    var rootPosition=actor.Root.position;
    var hullRotation=actor.Hull.localRotation;
    var turretRotation=actor.Turret.localRotation;
    actor.Root.hasChanged=actor.Hull.hasChanged=actor.Turret.hasChanged=false;
    world.ApplyUnitPose(actor,PlayableEntityKind.Tank,pose);
    Assert.False(actor.Root.hasChanged||actor.Hull.hasChanged||actor.Turret.hasChanged);

    var moved=new PlayableDisplayPose(new NavPoint(3,1),pose.Hull,pose.Turret);
    world.ApplyUnitPose(actor,PlayableEntityKind.Tank,moved);
    Assert.AreNotEqual(rootPosition,actor.Root.position);
    Assert.AreEqual(hullRotation,actor.Hull.localRotation);
    Assert.AreEqual(turretRotation,actor.Turret.localRotation);
    var turned=new PlayableDisplayPose(moved.Position,moved.Hull+.3,moved.Turret+.1);
    world.ApplyUnitPose(actor,PlayableEntityKind.Tank,turned);
    Assert.AreNotEqual(hullRotation,actor.Hull.localRotation);
    Assert.AreNotEqual(turretRotation,actor.Turret.localRotation);
   }finally{UnityEngine.Object.DestroyImmediate(host);}
  }
  [TestCase("tankHybrid")][TestCase("tankHybridUpgraded")]
  public void TracksFollowSignedTranslationAndPivotPauseFreezesAndShotsDeduplicate(string key)
  {
   var go=UnityEngine.Object.Instantiate(Resources.Load<GameObject>("StyleA/"+key));
   try{
    var mechanic=go.AddComponent<TankMechanismPresentation>();mechanic.Initialize();Assert.AreEqual(Vector3.one*TankMechanismProfile.Load().modelScale,go.transform.localScale);var nodes=go.GetComponentsInChildren<Transform>();var left=nodes.Single(t=>t.name=="treadL00");var right=nodes.Single(t=>t.name=="treadR00");var barrel=nodes.Single(t=>t.name=="barrelRecoil");
    mechanic.Drive(new PlayableDisplayPose(new NavPoint(0,0),0,0),Frame(0),3);float start=left.localPosition.z;
    mechanic.Drive(new PlayableDisplayPose(new NavPoint(.1,0),0,0),Frame(1),3);Assert.Greater(left.localPosition.z,start);float l=left.localPosition.z,r=right.localPosition.z;
    mechanic.Drive(new PlayableDisplayPose(new NavPoint(.1,0),.1,0),Frame(2),3);Assert.Greater(left.localPosition.z,l);Assert.Less(right.localPosition.z,r);var frozen=left.localPosition;
    mechanic.Drive(new PlayableDisplayPose(new NavPoint(.1,0),.1,0),Frame(2,true),3);Assert.AreEqual(frozen,left.localPosition);
    float rest=barrel.localPosition.z;mechanic.Drive(new PlayableDisplayPose(new NavPoint(.1,0),.1,0),Frame(3,shot:true),3);Assert.Less(barrel.localPosition.z,rest);float recoil=barrel.localPosition.z;
    mechanic.Drive(new PlayableDisplayPose(new NavPoint(.1,0),.1,0),Frame(4,shot:true),3);Assert.Greater(barrel.localPosition.z,recoil,"same observed event must not restart recoil");
    mechanic.Drive(new PlayableDisplayPose(new NavPoint(0,0),0,0),Frame(100,shot:true),3);Assert.AreEqual(rest,barrel.localPosition.z);Assert.False(nodes.Single(t=>t.name=="muzzle").GetComponentInChildren<MeshRenderer>(true).gameObject.activeSelf);
   }finally{UnityEngine.Object.DestroyImmediate(go);}
  }
 }
}
