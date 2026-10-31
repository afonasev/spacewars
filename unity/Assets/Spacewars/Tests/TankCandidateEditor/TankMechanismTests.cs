using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Tests.EditMode
{
 public sealed class TankMechanismTests
 {
  static PlayableSnapshot Frame(long tick,bool paused=false,bool shot=false)=>new PlayableSnapshot("tank-test",1,1,7319,tick+1,tick,RuntimeStatus.Running,paused,PlayableMatchOutcome.Playing,0,new NavGeometry(20,Array.Empty<NavObstacle>(),1),Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sounds:shot?new[]{new PlayableSoundEvent(1,3,PlayableSoundKind.Shot,new NavPoint(0,0),PlayableEntityKind.Tank,unitId:3)}:Array.Empty<PlayableSoundEvent>());
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
    foreach(var bone in bones)bone.hasChanged=false;
    mechanic.Drive(pose,Frame(1),3);
    Assert.False(bones.Any(b=>b.hasChanged),"An idle unit must not dirty 84 skinning bones on every rendered frame");
    var original=bones[0].localPosition;
    bones[0].localPosition=original+Vector3.one;
    mechanic.Drive(pose,Frame(0),3);
    Assert.AreEqual(original,bones[0].localPosition,"A rewound presentation reapplies its authoritative track pose");
   }finally{UnityEngine.Object.DestroyImmediate(go);}
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
