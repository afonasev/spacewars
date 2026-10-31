using System;
using System.Linq;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
public sealed class ShkvalMonolithPresentationTests
{
 [Test] public void ArtMetadataRejectsNonfiniteAndOutOfRangeValues(){var p=ShkvalArtProfile.Load();foreach(var field in p.Parameters()){Assert.IsNotEmpty(field.Description);Assert.Greater(field.Step,0);float before=field.Read();field.Write(float.NaN);Assert.Throws<ArgumentException>(()=>p.Validate());field.Write(field.Maximum+1);Assert.Throws<ArgumentException>(()=>p.Validate());field.Write(before);}p.Validate();}
 [TestCase(false)][TestCase(true)] public void PhysicalGeometryMatchesRaisedPrefabAndTelescopingRams(bool upgraded)
 {
  var host=new GameObject("Shkval mechanical geometry");var p=PlayableProfile.Default;var world=new PlayableWorld(host.transform,p);var actor=world.Tank(1,true,PlayableEntityKind.Shkval);
  try{foreach(double pitch in new[]{ShkvalLaunchGeometry.NeutralPitch,.8,1.1,1.4})foreach(double heading in new[]{0d,.7}){
   var ground=new NavPoint(3,5);var e=new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Shkval,ground,100,false,2,.4,heading,upgraded:upgraded,weaponPitch:pitch);
   world.UpdateResearchModel(actor,e);actor.Root.position=new Vector3(3,2,5);var gradient=new NavPoint(.12,-.18);actor.Root.rotation=Quaternion.FromToRotation(Vector3.up,new Vector3(-.12f,1,.18f).normalized);actor.Hull.localRotation=Quaternion.Euler(0,90-.4f*Mathf.Rad2Deg,0);actor.Turret.localRotation=Quaternion.Euler(0,(float)(.4-heading)*Mathf.Rad2Deg,0);world.AimExplorer(actor,e);
   for(int cell=0;cell<4;cell++){
    var frame=ShkvalLaunchGeometry.At(new BallisticPoint(3,2,5),.4,heading,pitch,upgraded,cell,p.ShkvalModelRadius*p.ShkvalModelScale/2.05,p.ShkvalProjectileLength,gradient);var muzzle=actor.Launcher.Find("launchCarriage/muzzle"+cell);
    Assert.Less(Vector3.Distance(muzzle.position,new Vector3((float)frame.Mouth.X,(float)frame.Mouth.Y,(float)frame.Mouth.Z)),.0001f,"Pure authority geometry equals actual authored rig");Assert.Greater(Vector3.Dot(muzzle.forward,new Vector3((float)frame.Direction.X,(float)frame.Direction.Y,(float)frame.Direction.Z)),.999999f);
   }
   foreach(var housing in actor.Turret.GetComponentsInChildren<Transform>().Where(t=>t.name=="elevationRamLeft"||t.name=="elevationRamRight")){
    var rod=housing.Cast<Transform>().Single(t=>t.name.StartsWith("elevationRod"));float x=housing.name.EndsWith("Left")?.49f:-.49f;var upper=actor.Turret.TransformPoint(actor.Launcher.localPosition+actor.Launcher.localRotation*(Quaternion.Euler(32,0,0)*new Vector3(x,.16f,.13f)));
    Assert.Less(Vector3.Distance(rod.TransformPoint(Vector3.up*new Vector3(0,.18f,-.11f).magnitude),upper),.0001f,"Ram endpoint stays attached while extending");
    Assert.Less(Vector3.Distance(housing.TransformPoint(new Vector3(x,-.06f,.4f)),actor.Turret.TransformPoint(new Vector3(x,-.06f,.4f))),.0001f,"Ram base stays attached");
   }
  }}finally{world.Dispose();UnityEngine.Object.DestroyImmediate(host);}
 }
 [TestCase(false)][TestCase(true)] public void ModelHasSixWheelsAndFourChannelsWithIndependentCarriage(bool upgrade)
 {
  var prefab=Resources.Load<GameObject>("StyleA/"+(upgrade?"shkval-guidance-3":"shkval"));Assert.NotNull(prefab);
  var nodes=prefab.GetComponentsInChildren<Transform>(true);Assert.AreEqual(6,nodes.Count(t=>t.name.StartsWith("wheel")&&t.childCount>0));Assert.AreEqual(4,nodes.Count(t=>t.name.StartsWith("muzzle")));
  var pitch=nodes.Single(t=>t.name=="launcherPitch");var carriage=nodes.Single(t=>t.name=="launchCarriage");Assert.True(carriage.IsChildOf(pitch));Assert.IsEmpty(prefab.GetComponentsInChildren<Collider>(true));
 }
 [TestCase(false)][TestCase(true)] public void EveryMuzzleEmitsTheLoadedRocketAndReloadFreezes(bool upgraded)
 {
  const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
  var g=new GameObject("Shkval muzzle contract");var hud=g.AddComponent<PlayableBootstrap>();hud.enabled=false;var p=PlayableProfile.Default;var art=ShkvalArtProfile.Load();var world=new PlayableWorld(g.transform,p);hud.ConfigureShkvalEvidence(world,p);
  var actor=world.Tank(1,true,PlayableEntityKind.Shkval);
  var render=typeof(PlayableBootstrap).GetMethod("RenderProjectiles",flags);
  var field=typeof(PlayableBootstrap).GetField("view",flags);
  var flight=new BallisticFlight(new BallisticPoint(0,p.ShkvalLaunchHeight,0),new BallisticPoint(10,0,0),p.ShkvalArcHeight,p.ShkvalProjectileSpeed);
  try{
   for(int barrel=0;barrel<4;barrel++){
    ShkvalLaunchGeometry.Aim(default,0,upgraded,barrel,p.ShkvalModelRadius*p.ShkvalModelScale/2.05,p.ShkvalProjectileLength,default,new BallisticPoint(10,0,0),p.ShkvalArcHeight,out double aimYaw,out double aimPitch);
    var start=ShkvalLaunchGeometry.At(default,0,aimYaw,aimPitch,upgraded,barrel,p.ShkvalModelRadius*p.ShkvalModelScale/2.05,p.ShkvalProjectileLength).Center;
    flight=new BallisticFlight(start,new BallisticPoint(10,0,0),p.ShkvalArcHeight,p.ShkvalProjectileSpeed);
    var e=new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Shkval,default,100,false,2,0,aimYaw,upgraded:upgraded,weaponPitch:aimPitch,weaponSequence:barrel+1,weaponReload:p.ShkvalFireIntervalMs/1000);
    world.UpdateResearchModel(actor,e);actor.Hull.localRotation=Quaternion.Euler(0,90,0);actor.Turret.localRotation=Quaternion.Euler(0,-(float)aimYaw*Mathf.Rad2Deg,0);
    var launch=new ShkvalLaunchSnapshot(barrel,default,0,aimYaw,upgraded,flight.Tangent(0),flight.Duration,flight.Start.Y,aimPitch,flight.Start);
    var tangent=flight.Tangent(0);var shot=new PlayableProjectileSnapshot(100+barrel,1,2,start.Ground,PlayableEntityKind.Shkval,Math.Atan2(tangent.Z,tangent.X),start.Y,Math.Atan2(tangent.Y,Math.Sqrt(tangent.X*tangent.X+tangent.Z*tangent.Z)),age:0,launch:launch);
    PlayableSnapshot Snapshot(bool paused)=>new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,3,RuntimeStatus.Running,paused,PlayableMatchOutcome.Playing,0,null,new[]{e},Array.Empty<PlayableBuildingSnapshot>(),new[]{shot},new PlayableRuntimeMetrics(0,0,0,0,0),null);
    field.SetValue(hud,Snapshot(false));render.Invoke(hud,null);
    var shells=(IDictionary)typeof(PlayableBootstrap).GetField("shells",flags).GetValue(hud);var rocket=((GameObject)shells[shot.Id]).transform;var muzzle=actor.Launcher.Find("launchCarriage/muzzle"+barrel);
    var tip=rocket.position+rocket.right*(float)p.ShkvalProjectileLength*.64f*art.rocketScale;
    Assert.Less(Vector3.Distance(tip,muzzle.position+muzzle.forward*(float)ShkvalLaunchGeometry.LoadedProtrusion),.0001f,"Tip exits selected physical bore "+barrel);
    Assert.Greater(Vector3.Dot(rocket.right,muzzle.forward),.9999f);
    var pose=rocket.localToWorldMatrix;var loaded=actor.LoadedRockets.Select(t=>t.localToWorldMatrix).ToArray();
    field.SetValue(hud,Snapshot(true));render.Invoke(hud,null);Assert.AreEqual(pose,rocket.localToWorldMatrix);CollectionAssert.AreEqual(loaded,actor.LoadedRockets.Select(t=>t.localToWorldMatrix).ToArray());
    Assert.AreEqual(4,actor.LoadedRockets.Length);foreach(var cell in actor.LoadedRockets)Assert.False(cell.Find("rocket-exhaust").gameObject.activeSelf);
   }
  }finally{UnityEngine.Object.DestroyImmediate(g);}
 }
 [TestCase(false)][TestCase(true)] public void FourLoadedCellsHaveIndependentVisibilityFeedAndPause(bool upgraded)
 {
  const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
  var g=new GameObject("Four loaded channels");var hud=g.AddComponent<PlayableBootstrap>();hud.enabled=false;var p=PlayableProfile.Default;var world=new PlayableWorld(g.transform,p);hud.ConfigureShkvalEvidence(world,p);var actor=world.Tank(1,true,PlayableEntityKind.Shkval);
  var field=typeof(PlayableBootstrap).GetField("view",flags);var render=typeof(PlayableBootstrap).GetMethod("RenderProjectiles",flags);
  void Show(double[] cells,bool paused=false){var e=new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Shkval,default,100,false,0,0,0,upgraded:upgraded,shkvalCells:new ShkvalCellsSnapshot(cells));world.UpdateResearchModel(actor,e);field.SetValue(hud,new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,3,RuntimeStatus.Running,paused,PlayableMatchOutcome.Playing,0,null,new[]{e},Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));render.Invoke(hud,null);}
  try{
   Show(new double[4]);Assert.AreEqual(4,actor.LoadedRockets.Count(t=>t.gameObject.activeSelf));var ready=actor.LoadedRockets.Select(t=>t.position).ToArray();
   double interval=p.ShkvalFireIntervalMs/1000;
   Show(new[]{2*interval,0,0,0});Assert.False(actor.LoadedRockets[0].gameObject.activeSelf);Assert.AreEqual(3,actor.LoadedRockets.Count(t=>t.gameObject.activeSelf));
   var feeding=new[]{interval*.5,2*interval,0,0};Show(feeding);Assert.True(actor.LoadedRockets[0].gameObject.activeSelf);Assert.False(actor.LoadedRockets[1].gameObject.activeSelf);Assert.Greater(Vector3.Distance(ready[0],actor.LoadedRockets[0].position),.001f);
   var poses=actor.LoadedRockets.Select(t=>t.localToWorldMatrix).ToArray();Show(feeding,true);CollectionAssert.AreEqual(poses,actor.LoadedRockets.Select(t=>t.localToWorldMatrix).ToArray());
   Show(new double[4]);Assert.AreEqual(4,actor.LoadedRockets.Count(t=>t.gameObject.activeSelf));CollectionAssert.AreEqual(ready,actor.LoadedRockets.Select(t=>t.position).ToArray());
  }finally{UnityEngine.Object.DestroyImmediate(g);}
 }
 [Test] public void MeshTemplatesCannotRenderBeforeDeferredDestruction()
 {
  const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
  var host=new GameObject("Shkval mesh template contract");var hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
  var world=new PlayableWorld(host.transform,PlayableProfile.Default);hud.ConfigureShkvalEvidence(world,PlayableProfile.Default);
  var before=UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include,FindObjectsSortMode.None).Select(r=>r.GetInstanceID()).ToHashSet();
  var tracer=new PlayableProjectileSnapshot(21,1,2,new NavPoint(1,0),PlayableEntityKind.Explorer,0,1,.3,age:.1);
  var impact=new PlayableImpactSnapshot(22,PlayableOwner.Player,new BallisticPoint(2,0,0),1.4,30,1);
  var view=new PlayableSnapshot("template-test",1,1,7,1,31,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),new[]{tracer},new PlayableRuntimeMetrics(0,0,0,0,0),null,impacts:new[]{impact});
  try {
   typeof(PlayableBootstrap).GetField("view",flags).SetValue(hud,view);typeof(PlayableBootstrap).GetMethod("RenderProjectiles",flags).Invoke(hud,null);
   foreach(var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(r=>!before.Contains(r.GetInstanceID())))
    Assert.True(r.transform.IsChildOf(host.transform)||!r.gameObject.activeInHierarchy,"Visible mesh template escaped the effect hierarchy: "+r.name);
  }finally{UnityEngine.Object.DestroyImmediate(host);}
 }
 [Test] public void LaunchAndDetailedRocketFreezeAtUnchangedSnapshot()
 {
  const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
  var g=new GameObject("Shkval pause contract");var hud=g.AddComponent<PlayableBootstrap>();hud.enabled=false;var p=PlayableProfile.Default;var world=new PlayableWorld(g.transform,p);hud.ConfigureShkvalEvidence(world,p);
  var e=new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Shkval,default,100,false,2,0,0);var a=world.Tank(1,true,PlayableEntityKind.Shkval);
  var shot=new PlayableProjectileSnapshot(5,1,2,new NavPoint(1,0),PlayableEntityKind.Shkval,0,1,.3,age:.1);
  PlayableSnapshot Snapshot(bool paused)=>new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,3,RuntimeStatus.Running,paused,PlayableMatchOutcome.Playing,0,null,new[]{e},Array.Empty<PlayableBuildingSnapshot>(),new[]{shot},new PlayableRuntimeMetrics(0,0,0,0,0),null);
  try{
   typeof(PlayableBootstrap).GetField("view",flags).SetValue(hud,Snapshot(false));var render=typeof(PlayableBootstrap).GetMethod("RenderProjectiles",flags);render.Invoke(hud,null);
   var carriage=a.Launcher.Find("launchCarriage");Assert.Greater(carriage.localPosition.sqrMagnitude,0);
   var shells=(IDictionary)typeof(PlayableBootstrap).GetField("shells",flags).GetValue(hud);var rocket=(GameObject)shells[5];Assert.NotNull(rocket.transform.Find("rocket-stabilizers"));Assert.NotNull(rocket.transform.Find("rocket-nozzle-collar"));
   var transforms=rocket.GetComponentsInChildren<Transform>().Select(t=>t.localToWorldMatrix).ToArray();var pose=carriage.localToWorldMatrix;
   typeof(PlayableBootstrap).GetField("view",flags).SetValue(hud,Snapshot(true));render.Invoke(hud,null);Assert.AreEqual(pose,carriage.localToWorldMatrix);CollectionAssert.AreEqual(transforms,rocket.GetComponentsInChildren<Transform>().Select(t=>t.localToWorldMatrix).ToArray());
  }finally{UnityEngine.Object.DestroyImmediate(g);}
 }
 [Test] public void LoadedRocketSharesExactMaterialTrianglesAndRemovesHiddenComponents()
 {
  const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
  var host=new GameObject("Loaded rocket geometry");var hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
  var art=typeof(PlayableBootstrap).GetField("projectileVisuals",flags).GetValue(hud);var type=art.GetType();var p=PlayableProfile.Default;
  GameObject Make(string method,Color color)=> (GameObject)type.GetMethod(method).Invoke(art,method=="Rocket"?new object[]{host.transform,(float)p.ShkvalProjectileRadius,(float)p.ShkvalProjectileLength,color,true}:new object[]{host.transform,(float)p.ShkvalProjectileRadius,(float)p.ShkvalProjectileLength,color});
  try{
   var original=Make("Rocket",Color.cyan);var loaded=Make("LoadedRocket",Color.cyan);var other=Make("LoadedRocket",Color.red);
   var sources=original.GetComponentsInChildren<MeshFilter>().Where(f=>!f.transform.IsChildOf(original.transform.Find("rocket-exhaust"))).ToArray();
   var filter=loaded.GetComponent<MeshFilter>();var mesh=filter.sharedMesh;var r=loaded.GetComponent<MeshRenderer>();
   Assert.AreEqual(5,sources.Length);Assert.AreEqual(5,mesh.subMeshCount);Assert.AreEqual(1,loaded.GetComponentsInChildren<MeshRenderer>(true).Length);Assert.AreEqual(2,loaded.GetComponentsInChildren<Transform>(true).Length);
   Assert.AreSame(mesh,other.GetComponent<MeshFilter>().sharedMesh);Assert.AreNotSame(r.sharedMaterials[0],other.GetComponent<MeshRenderer>().sharedMaterials[0]);
   Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off,r.shadowCastingMode);Assert.False(r.receiveShadows);
   for(int sub=0;sub<sources.Length;sub++){
    var f=sources[sub];var source=f.sharedMesh;var a=source.triangles;var b=mesh.GetTriangles(sub);
    Assert.AreSame(f.GetComponent<MeshRenderer>().sharedMaterial,r.sharedMaterials[sub]);Assert.AreEqual(a.Length,b.Length);
    for(int i=0;i<a.Length;i++){
     Assert.Less(Vector3.Distance(f.transform.TransformPoint(source.vertices[a[i]]),loaded.transform.TransformPoint(mesh.vertices[b[i]])),.000001f,"Exact vertex/material semantics");
     Assert.Less(Vector3.Distance(f.transform.TransformDirection(source.normals[a[i]]),loaded.transform.TransformDirection(mesh.normals[b[i]])),.000001f,"Exact shading normals");
    }
   }
   host.transform.position=new Vector3(4,3,2);host.transform.rotation=Quaternion.Euler(12,35,8);
   var cached=Make("LoadedRocket",Color.cyan);Assert.AreSame(mesh,cached.GetComponent<MeshFilter>().sharedMesh);
   Assert.False(loaded.transform.Find("rocket-exhaust").gameObject.activeSelf);Assert.AreEqual(0,loaded.transform.Find("rocket-exhaust").childCount);
  }finally{UnityEngine.Object.DestroyImmediate(host);}
 }
 [TestCase(false)][TestCase(true)] public void HullRecoilUsesSavedCellsWithoutProjectilesAndNeverMovesAuthority(bool upgraded)
 {
  const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
  var g=new GameObject("Shkval hull recoil");var hud=g.AddComponent<PlayableBootstrap>();hud.enabled=false;var p=PlayableProfile.Default;var art=ShkvalArtProfile.Load();var world=new PlayableWorld(g.transform,p);hud.ConfigureShkvalEvidence(world,p);var actor=world.Tank(1,true,PlayableEntityKind.Shkval);
  var field=typeof(PlayableBootstrap).GetField("view",flags);var render=typeof(PlayableBootstrap).GetMethod("RenderProjectiles",flags);double interval=p.ShkvalFireIntervalMs/1000;
  void Show(double age,bool paused=false){var e=new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Shkval,default,100,false,0,0,0,upgraded:upgraded,shkvalCells:new ShkvalCellsSnapshot(new[]{2*interval-age,0,0,0}));world.UpdateResearchModel(actor,e);actor.Root.position=new Vector3(3,2,5);actor.Hull.localRotation=Quaternion.Euler(0,40,0);actor.Turret.localRotation=Quaternion.Euler(0,70,0);field.SetValue(hud,new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,3,RuntimeStatus.Running,paused,PlayableMatchOutcome.Playing,0,null,new[]{e},Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));render.Invoke(hud,null);}
  try{
   Show(0);Assert.AreEqual(Vector3.zero,actor.Hull.localPosition);var root=actor.Root.localToWorldMatrix;
   Show(art.hullRecoilAttackSeconds);var peak=actor.Hull.localPosition;Assert.AreEqual(art.hullRecoilTravel,peak.magnitude,.000001f);Assert.Less(Vector3.Dot(peak,actor.Root.InverseTransformDirection(actor.Turret.forward)),-art.hullRecoilTravel*.999f);Assert.AreEqual(root,actor.Root.localToWorldMatrix);
   var poses=actor.LoadedRockets.Select(t=>t.localToWorldMatrix).ToArray();Show(art.hullRecoilAttackSeconds,true);Assert.AreEqual(peak,actor.Hull.localPosition);CollectionAssert.AreEqual(poses,actor.LoadedRockets.Select(t=>t.localToWorldMatrix).ToArray());
   Show(art.hullRecoilAttackSeconds+art.hullRecoilReturnSeconds/2);Assert.AreEqual(art.hullRecoilTravel/2,actor.Hull.localPosition.magnitude,.000001f);
   Show(art.hullRecoilAttackSeconds);Assert.AreEqual(peak,actor.Hull.localPosition,"Restored timer reconstructs pose without accumulated frame state");
   Show(art.hullRecoilAttackSeconds+art.hullRecoilReturnSeconds+.01);Assert.AreEqual(Vector3.zero,actor.Hull.localPosition);Assert.AreEqual(root,actor.Root.localToWorldMatrix);
  }finally{UnityEngine.Object.DestroyImmediate(g);}
 }
 [TestCase(false,0d,0d,false)][TestCase(true,0d,0d,false)]
 [TestCase(false,.7d,1.9d,false)][TestCase(true,.7d,1.9d,false)]
 [TestCase(false,.7d,1.9d,true)][TestCase(true,.7d,1.9d,true)]
 [TestCase(false,-1.3d,-.2d,false)][TestCase(true,-1.3d,-.2d,false)]
 public void ActualLoadedInstanceLeavesEachBoreAndContinuesToTarget(bool upgraded,double hull,double turret,bool runtimeStepOrder)
 {
  const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
  var g=new GameObject("Actual loaded rocket flight");var hud=g.AddComponent<PlayableBootstrap>();hud.enabled=false;var p=PlayableProfile.Default;var art=ShkvalArtProfile.Load();var world=new PlayableWorld(g.transform,p);hud.ConfigureShkvalEvidence(world,p);var actor=world.Tank(1,true,PlayableEntityKind.Shkval);
  var field=typeof(PlayableBootstrap).GetField("view",flags);var render=typeof(PlayableBootstrap).GetMethod("RenderProjectiles",flags);var shells=(IDictionary)typeof(PlayableBootstrap).GetField("shells",flags).GetValue(hud);
  var ground=new NavPoint(3,5);double interval=p.ShkvalFireIntervalMs/1000d;
  var flight=new BallisticFlight(new BallisticPoint(3,p.ShkvalLaunchHeight,5),new BallisticPoint(3+10*Math.Cos(turret),0,5+10*Math.Sin(turret)),p.ShkvalArcHeight,p.ShkvalProjectileSpeed);
  int barrel=0;int shotId=100;double aimYaw=turret,aimPitch=0;
  void Show(double age,bool firing,bool paused=false){
   var cells=new double[4];if(firing)cells[barrel]=2*interval-Math.Max(0,age-(runtimeStepOrder?1d/30d:0));
   var e=new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Shkval,ground,100,false,2,hull,aimYaw,upgraded:upgraded,weaponPitch:aimPitch,shkvalCells:firing?new ShkvalCellsSnapshot(cells):null);
   world.UpdateResearchModel(actor,e);actor.Root.position=new Vector3(3,0,5);actor.Hull.localRotation=Quaternion.Euler(0,90-(float)hull*Mathf.Rad2Deg,0);actor.Turret.localRotation=Quaternion.Euler(0,(float)(hull-aimYaw)*Mathf.Rad2Deg,0);
   var at=flight.Point(age/flight.Duration);var tangent=flight.Tangent(age/flight.Duration);
   var launch=new ShkvalLaunchSnapshot(barrel,new BallisticPoint(3,0,5),hull,aimYaw,upgraded,flight.Tangent(0),flight.Duration,flight.Start.Y,aimPitch,flight.Start);
   var shot=new PlayableProjectileSnapshot(shotId,1,2,new NavPoint(at.X,at.Z),PlayableEntityKind.Shkval,Math.Atan2(tangent.Z,tangent.X),at.Y,Math.Atan2(tangent.Y,Math.Sqrt(tangent.X*tangent.X+tangent.Z*tangent.Z)),age:age,launch:launch);
   field.SetValue(hud,new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,3,RuntimeStatus.Running,paused,PlayableMatchOutcome.Playing,0,null,new[]{e},Array.Empty<PlayableBuildingSnapshot>(),firing?new[]{shot}:Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));render.Invoke(hud,null);
  }
  try{for(barrel=0;barrel<4;barrel++,shotId++){
   var end=new BallisticPoint(3+10*Math.Cos(turret),0,5+10*Math.Sin(turret));
   ShkvalLaunchGeometry.Aim(new BallisticPoint(3,0,5),hull,upgraded,barrel,p.ShkvalModelRadius*p.ShkvalModelScale/2.05,p.ShkvalProjectileLength,default,end,p.ShkvalArcHeight,out aimYaw,out aimPitch);
   flight=new BallisticFlight(ShkvalLaunchGeometry.At(new BallisticPoint(3,0,5),hull,aimYaw,aimPitch,upgraded,barrel,p.ShkvalModelRadius*p.ShkvalModelScale/2.05,p.ShkvalProjectileLength).Center,end,p.ShkvalArcHeight,p.ShkvalProjectileSpeed);
   Show(0,false);var loaded=actor.LoadedRockets[barrel];var origin=loaded.localToWorldMatrix;var mesh=loaded.GetComponent<MeshFilter>().sharedMesh;var materials=loaded.GetComponent<MeshRenderer>().sharedMaterials;var scale=loaded.localScale;var siblings=actor.LoadedRockets.ToArray();
   Show(0,true);var flying=((GameObject)shells[shotId]).transform;
   Assert.AreSame(loaded,flying,"The actual loaded Transform must become the projectile, not a lookalike copy");Assert.AreSame(mesh,flying.GetComponent<MeshFilter>().sharedMesh);CollectionAssert.AreEqual(materials,flying.GetComponent<MeshRenderer>().sharedMaterials);Assert.AreEqual(scale,flying.localScale);
   for(int i=0;i<16;i++)Assert.AreEqual(origin[i],flying.localToWorldMatrix[i],.0001f,"Continuous world pose at release");
   Assert.AreNotSame(loaded,actor.LoadedRockets[barrel]);Assert.False(actor.LoadedRockets[barrel].gameObject.activeSelf);for(int c=0;c<4;c++)if(c!=barrel)Assert.AreSame(siblings[c],actor.LoadedRockets[c]);
   Assert.True(flying.Find("rocket-exhaust").gameObject.activeSelf);foreach(var flame in flying.Find("rocket-exhaust").GetComponentsInChildren<Renderer>())Assert.False(flame.enabled,"No visible flame while the nozzle remains inside the bore");Assert.AreEqual(2,flying.Find("rocket-exhaust").childCount);Assert.AreEqual(0,actor.LoadedRockets[barrel].Find("rocket-exhaust").childCount);
   Show(.05,true);foreach(var flame in flying.Find("rocket-exhaust").GetComponentsInChildren<Renderer>())Assert.True(flame.enabled,"Exterior exhaust after nozzle exit");Assert.AreSame(flying,((GameObject)shells[shotId]).transform);Assert.Greater(Vector3.Distance(flying.position,origin.GetColumn(3)),.001f);var clipRenderer=flying.Find("rocket-exhaust").GetChild(0).GetComponent<Renderer>();var clipBlock=new MaterialPropertyBlock();clipRenderer.GetPropertyBlock(clipBlock);var clipPlane=clipBlock.GetVector("_LaunchClipPlane");Assert.AreEqual(1,clipBlock.GetFloat("_LaunchClipEnabled"));Assert.AreEqual("Spacewars/ShkvalExhaust",clipRenderer.sharedMaterial.shader.name);var pose=flying.localToWorldMatrix;var racks=actor.LoadedRockets.Select(t=>t.localToWorldMatrix).ToArray();Show(.05,true,true);clipRenderer.GetPropertyBlock(clipBlock);Assert.AreEqual(clipPlane,clipBlock.GetVector("_LaunchClipPlane"));Assert.True(clipRenderer.enabled);Assert.AreEqual(pose,flying.localToWorldMatrix);CollectionAssert.AreEqual(racks,actor.LoadedRockets.Select(t=>t.localToWorldMatrix).ToArray());
   BallisticPoint expected=default;
   foreach(double age in new[]{0,.0166666667,.0333333333,.1,.2,.3,.4}){Show(age,true);expected=flight.Point(age/flight.Duration);var tangent=flight.Tangent(age/flight.Duration);Assert.Less(Vector3.Distance(flying.position,new Vector3((float)expected.X,(float)expected.Y,(float)expected.Z)),.0001f,"Exact authority position at EVERY age");Assert.Greater(Vector3.Dot(flying.right,new Vector3((float)tangent.X,(float)tangent.Y,(float)tangent.Z).normalized),.999999f,"Exact ballistic tangent without in-air correction");}
   Show(flight.Duration*.9,true);expected=flight.Point(.9);Assert.AreSame(loaded,((GameObject)shells[shotId]).transform);Assert.Less(Vector3.Distance(flying.position,new Vector3((float)expected.X,(float)expected.Y,(float)expected.Z)),.0001f);Assert.AreSame(mesh,flying.GetComponent<MeshFilter>().sharedMesh);Assert.AreEqual(new Vector3(3,0,5),actor.Root.position);
  }}finally{UnityEngine.Object.DestroyImmediate(g);}
 }
 [TestCase(false)][TestCase(true)] public void LateFlightCannotConsumeTheNewReadyRocket(bool upgraded)
 {
  const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
  var g=new GameObject("Late observed flight");var hud=g.AddComponent<PlayableBootstrap>();hud.enabled=false;var p=PlayableProfile.Default;var world=new PlayableWorld(g.transform,p);hud.ConfigureShkvalEvidence(world,p);var actor=world.Tank(1,true,PlayableEntityKind.Shkval);var e=new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Shkval,default,100,false,0,0,0,upgraded:upgraded,shkvalCells:new ShkvalCellsSnapshot(new double[4]));world.UpdateResearchModel(actor,e);
  var field=typeof(PlayableBootstrap).GetField("view",flags);var render=typeof(PlayableBootstrap).GetMethod("RenderProjectiles",flags);
  void Show(bool oldShot){var launch=new ShkvalLaunchSnapshot(0,new BallisticPoint(0,0,0),0,0,upgraded,new BallisticPoint(10,5,0),2,1);var shot=new PlayableProjectileSnapshot(400,1,2,new NavPoint(5,0),PlayableEntityKind.Shkval,0,2,0,age:1,launch:launch);field.SetValue(hud,new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,3,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,new[]{e},Array.Empty<PlayableBuildingSnapshot>(),oldShot?new[]{shot}:Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));render.Invoke(hud,null);}
  try{Show(false);var ready=actor.LoadedRockets.ToArray();Show(true);CollectionAssert.AreEqual(ready,actor.LoadedRockets);Assert.True(ready.All(t=>t.gameObject.activeSelf));var shells=(IDictionary)typeof(PlayableBootstrap).GetField("shells",flags).GetValue(hud);Assert.AreNotSame(ready[0].gameObject,shells[400]);Assert.AreSame(ready[0].GetComponent<MeshFilter>().sharedMesh,((GameObject)shells[400]).GetComponent<MeshFilter>().sharedMesh);}finally{UnityEngine.Object.DestroyImmediate(g);}
 }
 [Test] public void ExhaustShaderClipsBehindMuzzlePlaneOnGpuAndPreservesExteriorColor()
 {
  var shader=Resources.Load<Shader>("ShkvalExhaust");Assert.NotNull(shader);Assert.True(shader.isSupported);
  var host=new GameObject("Exhaust GPU clipping");var quad=GameObject.CreatePrimitive(PrimitiveType.Quad);quad.transform.SetParent(host.transform,false);quad.layer=30;quad.transform.localScale=Vector3.one*2;
  var material=new Material(shader);material.SetColor("_BaseColor",Color.red);var renderer=quad.GetComponent<Renderer>();renderer.sharedMaterial=material;
  var camera=new GameObject("Exhaust clip camera").AddComponent<Camera>();camera.transform.SetParent(host.transform,false);camera.transform.position=new Vector3(0,0,-2);camera.orthographic=true;camera.orthographicSize=1;camera.nearClipPlane=.01f;camera.farClipPlane=10;camera.cullingMask=1<<30;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
  camera.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();var target=new RenderTexture(64,64,24);target.Create();camera.targetTexture=target;var pixels=new Texture2D(64,64,TextureFormat.RGB24,false);var before=RenderTexture.active;bool fog=RenderSettings.fog;
  void Capture(){camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,64,64),0,0);pixels.Apply();RenderTexture.active=before;}
  try{
   RenderSettings.fog=false;var block=new MaterialPropertyBlock();block.SetVector("_LaunchClipPlane",new Vector4(1,0,0,0));block.SetFloat("_LaunchClipEnabled",1);renderer.SetPropertyBlock(block);Capture();
   Assert.Less(pixels.GetPixel(16,32).r,.02f,"Flame fragments behind muzzle plane must not render");Assert.Greater(pixels.GetPixel(48,32).r,.95f,"Exterior flame must retain its color");
   block.SetFloat("_LaunchClipEnabled",0);renderer.SetPropertyBlock(block);Capture();Assert.Greater(pixels.GetPixel(16,32).r,.95f,"Unbound/exterior flight has the original unclipped flame");Assert.Greater(pixels.GetPixel(48,32).r,.95f);
  }finally{RenderSettings.fog=fog;RenderTexture.active=before;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(host);}
 }
}
