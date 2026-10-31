#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
 // Two appearance passes of ONE projected authority view. Baseline visuals are not a second simulation.
 public sealed class TankComparisonEvidence:MonoBehaviour
 {
  PlayableRuntime runtime;UnityHostRouteService routes;PlayableWorld world;GameObject scene;Camera cam;
  RenderTexture target;Texture2D pixels;string output;StreamWriter rows;long sequence,lastCapture=-1;int frames;
  readonly Dictionary<int,long> moveSequences=new Dictionary<int,long>();
  readonly Dictionary<int,Transform> legacy=new Dictionary<int,Transform>();readonly Dictionary<int,GameObject> shells=new Dictionary<int,GameObject>();
  readonly WebProjectileVisuals projectiles=new WebProjectileVisuals();bool failed;float started;
  void Start(){started=Time.realtimeSinceStartup;output=Environment.GetEnvironmentVariable("SPACEWARS_TANK_VIDEO_OUTPUT");if(string.IsNullOrEmpty(output)){Debug.LogError("Missing tank evidence output");Application.Quit(1);return;}Directory.CreateDirectory(output);StartCoroutine(Guard());}
  void Fail(string text){if(failed)return;failed=true;File.WriteAllText(Path.Combine(output,"FAIL.txt"),text);Debug.LogError("TANK_WITNESS_FAIL "+text);Application.Quit(1);enabled=false;}
  void Update(){if(Time.realtimeSinceStartup-started>300)Fail("300s wall guard");}
  IEnumerator Guard(){var run=Run();while(true){bool more;object next;try{more=run.MoveNext();next=more?run.Current:null;}catch(Exception e){Fail(e.ToString());yield break;}if(!more)yield break;yield return next;}}
  static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
  static NavPoint Offset(NavPoint p,NavPoint a,double n)=>new NavPoint(p.X+a.X*n,p.Z+a.Z*n);
  bool Healthy(){if(runtime.Latest.Failure!=null||routes.LastProjectionFailure!=null){Fail(runtime.Latest.Failure+" "+routes.LastProjectionFailure);return false;}return true;}
  void Move(int[] ids,NavPoint[] goals){for(int i=0;i<ids.Length;i++){moveSequences[ids[i]]=++sequence;var result=runtime.TrySubmit(new PlayableCommand(runtime.Generation,sequence,"player-1",PlayableCommandKind.Move,new[]{ids[i]},goals[i]));Debug.Log("TANK_WITNESS_MOVE id="+ids[i]+" goal="+goals[i].X+","+goals[i].Z+" result="+result.Status);}}
  IEnumerator Run()
  {
   var config=CreateConfiguration(out var points,out var lateral);var p=config.Profile;var map=p.AuthoredMap;
   File.WriteAllText(Path.Combine(output,"scenario.txt"),"seed=7319; current envelope="+PlayableWorldState.CurrentVersion+"; profile="+p.ProfileId+"@"+p.Revision+"; map="+map.Id+"; one threaded host, two appearance passes; reverse=2m/.45speed/.4rad; first motion60s, completion120s; points="+string.Join(";",points.Select(q=>q.X+","+q.Z))+"\n");
   scene=new GameObject("Tank comparison");world=new PlayableWorld(scene.transform,p,staged:true){HybridTanks=true};yield return world.Preparation;
   var light=new GameObject("Map sun").AddComponent<Light>();light.transform.SetParent(scene.transform);var lighting=IndustrialLightingProfile.Load(map.Id);lighting.ApplyScene(light);
   cam=new GameObject("Comparison camera").AddComponent<Camera>();cam.orthographic=true;cam.orthographicSize=5.2f;cam.nearClipPlane=.1f;cam.farClipPlane=700;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.1f,.12f,.14f);
   cam.gameObject.AddComponent<VisibleShadowCoverage>().Configure(scene.transform,lighting);cam.gameObject.AddComponent<ContactOcclusionCamera>().Configure(ContactOcclusionProfile.Load());cam.gameObject.AddComponent<ActorGlowCamera>().Configure(ActorMaterialProfile.Load());cam.gameObject.AddComponent<CameraAntialiasing>().Configure(CameraAntialiasingProfile.Load());
   target=new RenderTexture(800,900,24){antiAliasing=4};target.Create();cam.targetTexture=target;pixels=new Texture2D(1600,900,TextureFormat.RGB24,false);
   routes=new UnityHostRouteService();runtime=new PlayableRuntime(config,1,startPaused:true);float deadline=Time.realtimeSinceStartup+60;
   while(!runtime.IsPrepared&&Time.realtimeSinceStartup<deadline){routes.Service(runtime,p.NavigationRequestsPerFrame);if(!Healthy())yield break;yield return null;}
   if(!runtime.IsPrepared){Fail("Preparation timeout60s");yield break;}
   var initial=runtime.ParticipantView("player-1");var tanks=initial.Entities.Where(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Tank).OrderBy(e=>e.Upgraded).ToArray();
   if(tanks.Length!=2){Fail("Setup: expected two scenario tanks");yield break;}var ids=tanks.Select(e=>e.Id).ToArray();var origins=tanks.Select(e=>e.Position).ToArray();
   runtime.RequestPause(false);while(runtime.Latest.Paused){routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}
   rows=new StreamWriter(Path.Combine(output,"motion.csv"));rows.WriteLine("frame,tick,stage,id,x,z,hull,moving,shots,projectiles,outcome,completionTick,originalX,originalZ,assignedX,assignedZ");
   int stage=1,shots=0;long firstMotion=-1,fireTick=-1;bool pivot=false,reverse=false;var seen=new HashSet<long>();float motionDeadline=Time.realtimeSinceStartup+60;deadline=Time.realtimeSinceStartup+120;
   var prior=tanks;Move(ids,new[]{points[1],Offset(points[1],lateral,3)});
   while(Time.realtimeSinceStartup<deadline){
    routes.Service(runtime,p.NavigationRequestsPerFrame);if(!Healthy())yield break;var view=runtime.ParticipantView("player-1");tanks=ids.Select(id=>view.Entities.SingleOrDefault(e=>e.Id==id)).ToArray();if(tanks.Any(e=>e==null)){Fail("Scenario tank lost");yield break;}
    if(firstMotion<0&&tanks.Where((e,i)=>Distance(e.Position,origins[i])>.25).Any())firstMotion=view.Tick;
    if(firstMotion<0&&Time.realtimeSinceStartup>motionDeadline){Fail("First motion60s");yield break;}
    for(int i=0;i<2;i++){double angle=Math.Atan2(Math.Sin(tanks[i].HullHeading-prior[i].HullHeading),Math.Cos(tanks[i].HullHeading-prior[i].HullHeading));if(stage==3&&Distance(tanks[i].Position,prior[i].Position)<1e-9&&Math.Abs(angle)>.01)pivot=true;if(stage==4){double dx=tanks[i].Position.X-prior[i].Position.X,dz=tanks[i].Position.Z-prior[i].Position.Z;if(dx*Math.Cos(tanks[i].HullHeading)+dz*Math.Sin(tanks[i].HullHeading)<-1e-5)reverse=true;}}
    foreach(var s in view.Sounds)if(s.Kind==PlayableSoundKind.Shot&&ids.Contains(s.UnitId)&&seen.Add(s.Id)){shots++;if(stage==5&&fireTick<0)fireTick=view.Tick;}
    if(stage<5&&tanks.Select((e,i)=>!e.Moving&&e.NavigationOutcome==NavigationOutcome.Arrived&&e.Completion!=null&&e.Completion.Order.Sequence==moveSequences[e.Id]&&e.Completion.OriginalTarget.Equals(Offset(points[stage],lateral,i*3))&&Distance(e.Position,e.Completion.OriginalTarget)<=p.TankCollisionRadius).All(x=>x)){
     if(stage==4){stage=5;foreach(int id in ids)runtime.TrySubmit(new PlayableCommand(runtime.Generation,++sequence,"player-1",PlayableCommandKind.Stop,new[]{id}));}
     else{stage++;Move(ids,new[]{points[stage],Offset(points[stage],lateral,3)});}
    }
    Render(view,tanks,stage);prior=tanks;
    if(view.Tick-lastCapture>=3){lastCapture=view.Tick;yield return new WaitForEndOfFrame();Capture(view,tanks,stage,shots);}
    if(stage==5&&fireTick>=0&&view.Tick>=fireTick+150){if(firstMotion<0||!pivot||!reverse||shots<2){Fail("Assertions: motion="+firstMotion+" pivot="+pivot+" reverse="+reverse+" shots="+shots);yield break;}File.WriteAllText(Path.Combine(output,"PASS.txt"),"firstMotion="+firstMotion+" pivot="+pivot+" reverse="+reverse+" shots="+shots+" frames="+frames+"\n");rows.Flush();Debug.Log("TANK_WITNESS_PASS");Application.Quit(0);yield break;}
    yield return null;
   }
   Fail("Scenario completion120s");
  }
  public static OfflineMatchConfiguration CreateConfiguration(out NavPoint[] points,out NavPoint lateral)
  {
   var map=ThreeCrossingsMap.Default;var data=JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text);data.tankReverseDistance=2;data.revision++;
   var p=PlayableProfile.Create(data,map);var roster=new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,2,OfflineControl.Human)};
   var sites=map.Sites(p);var starts=sites.Where(s=>s.Kind==PlayableBuildingKind.Headquarters).Select((s,i)=>new OfflineStart("start-"+i,s.Id,s.Position,new NavPoint(s.Position.X+10,s.Position.Z),0,i+1)).ToArray();
   var authored=new OfflineMatchConfiguration(p,"tank-comparison-genesis",map.Id,"authored-terrain",7319,roster,starts,sites,map.MovementBlockers.ToArray(),new double[starts.Length,starts.Length],terrain:map.Id);
   var enemyStart=starts[authored.Assignments[1]];var center=enemyStart.Position;points=null;lateral=default;
   var geometry=new NavGeometry(p.ArenaHalfExtent,map.MovementBlockers.ToArray(),1);var diagnostics=new List<string>();
   // Deterministic lane search is setup only: all four segments must already have full authored support and site clearance.
   for(int direction=0;direction<16&&points==null;direction++)for(int mirror=-1;mirror<=1&&points==null;mirror+=2)for(int shiftX=-20;shiftX<=20&&points==null;shiftX+=2)for(int shiftZ=-20;shiftZ<=20&&points==null;shiftZ+=2){
    double a=direction*Math.PI/8;var u=new NavPoint(Math.Cos(a),Math.Sin(a));var v=new NavPoint(-Math.Sin(a)*mirror,Math.Cos(a)*mirror);
    var laneCenter=new NavPoint(center.X+shiftX,center.Z+shiftZ);
    var candidate=new[]{Offset(Offset(laneCenter,u,18),v,6),Offset(Offset(laneCenter,u,12),v,6),Offset(laneCenter,u,12),Offset(laneCenter,u,8),Offset(laneCenter,u,9.2)};
    if(Enumerable.Range(0,2).Any(i=>Distance(Offset(candidate[4],v,i*3),center)>p.TankRange-.2||!geometry.SegmentFree(Offset(candidate[4],v,i*3),center,p.TankProjectileCollisionRadius)||sites.Any(t=>t.Slots.Any(slot=>Distance(Offset(candidate[0],v,i*3),slot.Position)<Math.Max(p.FactoryFootprintRadius,p.ScienceFootprintRadius)+p.TankCollisionRadius))))continue;
    bool safe=true;for(int unit=0;unit<2&&safe;unit++)for(int segment=0;segment<4&&safe;segment++){
     var from=Offset(candidate[segment],v,unit*3);var to=Offset(candidate[segment+1],v,unit*3);
     if(!geometry.SegmentFree(from,to,p.TankCollisionRadius)||!map.SupportsSweep(from,to,p.TankCollisionRadius)){if(diagnostics.Count<64)diagnostics.Add(direction+"/"+mirror+" unit"+unit+" seg"+segment+" geometry "+from.X+","+from.Z+" -> "+to.X+","+to.Z);safe=false;break;}
     for(int k=0;k<=12;k++){var q=new NavPoint(from.X+(to.X-from.X)*k/12,from.Z+(to.Z-from.Z)*k/12);if(starts.Any(s=>Distance(q,s.ExplorerAnchor)<p.ExplorerRange+p.ExplorerCollisionRadius+p.TankCollisionRadius+1)||sites.Any(s=>Distance(q,s.Position)<TerritoryRules.Radius(p,s.Kind)+p.TankCollisionRadius+1)){if(diagnostics.Count<64)diagnostics.Add(direction+"/"+mirror+" unit"+unit+" seg"+segment+" q="+q.X+","+q.Z+" anchor="+starts.Min(t=>Distance(q,t.ExplorerAnchor))+" site="+sites.Min(t=>Distance(q,t.Position)-TerritoryRules.Radius(p,t.Kind)));safe=false;break;}}
    }
    if(safe){points=candidate;lateral=v;break;}
   }
   if(points==null)throw new InvalidOperationException("Setup: no supported two-tank lane around enemy headquarters; center="+center.X+","+center.Z+" extent="+p.ArenaHalfExtent+"\n"+string.Join("\n",diagnostics));
   double heading=Math.Atan2(points[1].Z-points[0].Z,points[1].X-points[0].X);
   return new OfflineMatchConfiguration(p,"tank-maneuver-witness-v1",map.Id,"authored-terrain",7319,roster,starts,sites,map.MovementBlockers.ToArray(),new double[starts.Length,starts.Length],scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,points[0],heading),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,Offset(points[0],lateral,3),heading,upgraded:true)},terrain:map.Id);
  }
  void Render(PlayableSnapshot view,PlayableEntitySnapshot[] tanks,int stage)
  {
   world.Fog.Update(view.Vision,Time.unscaledDeltaTime);world.RenderSites(view,cam);
   foreach(var e in tanks){if(!world.Actors.TryGetValue(e.Id,out var a))a=world.Tank(e.Id,true,e.Kind);world.UpdateResearchModel(a,e);a.Root.position=world.Point(e.Position);a.Hull.localRotation=Quaternion.Euler(0,90-(float)e.HullHeading*Mathf.Rad2Deg,0);a.Turret.localRotation=Quaternion.Euler(0,(float)(e.HullHeading-e.TurretHeading)*Mathf.Rad2Deg,0);a.TankMechanism.Drive(new PlayableDisplayPose(e.Position,e.HullHeading,e.TurretHeading),view,e.Id);
    if(!legacy.TryGetValue(e.Id,out var old)){old=Instantiate(Resources.Load<GameObject>("StyleA/"+(e.Upgraded?"tankUpgraded":"tank")),a.Root).transform;old.localScale=a.Hull.localScale;legacy[e.Id]=old;foreach(var renderer in old.GetComponentsInChildren<Renderer>())for(int slot=0;slot<renderer.sharedMaterials.Length;slot++)ActorMaterialProfile.Load().Apply(renderer,slot,new Color(.19f,.72f,.77f));}
    old.localRotation=a.Hull.localRotation;old.GetComponentsInChildren<Transform>().Single(t=>t.name=="turretYaw").localRotation=a.Turret.localRotation;old.gameObject.SetActive(false);
   }
   var focus=(world.Point(tanks[0].Position)+world.Point(tanks[1].Position))*.5f;cam.transform.position=focus+new Vector3(5,9,-10);cam.transform.LookAt(focus+Vector3.up*.2f);
  }
  void Capture(PlayableSnapshot view,PlayableEntitySnapshot[] tanks,int stage,int shots)
  {
   for(int pass=0;pass<2;pass++){
    foreach(var e in tanks){world.Actors[e.Id].Hull.gameObject.SetActive(pass==1);legacy[e.Id].gameObject.SetActive(pass==0);}
    foreach(var old in shells.Values)Destroy(old);shells.Clear();
    foreach(var p in view.Projectiles.Where(p=>p.Kind==PlayableEntityKind.Tank&&p.Visible)){var g=projectiles.Tank(scene.transform,pass==1);g.transform.position=new Vector3((float)p.Position.X,(float)p.Height,(float)p.Position.Z);g.transform.rotation=Quaternion.Euler(0,-(float)p.Heading*Mathf.Rad2Deg,0)*Quaternion.Euler(0,0,(float)p.Pitch*Mathf.Rad2Deg);shells[p.Id]=g;}
    cam.Render();var oldTarget=RenderTexture.active;RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,800,900),pass*800,0);RenderTexture.active=oldTarget;
    foreach(var old in shells.Values)old.SetActive(false);
   }
   pixels.Apply();File.WriteAllBytes(Path.Combine(output,"frame-"+frames.ToString("D5")+".png"),pixels.EncodeToPNG());foreach(var e in tanks)rows.WriteLine(frames+","+view.Tick+","+stage+","+e.Id+","+e.Position.X+","+e.Position.Z+","+e.HullHeading+","+e.Moving+","+shots+","+view.Projectiles.Count+","+e.NavigationOutcome+","+e.Completion?.CompletionTick+","+e.Completion?.OriginalTarget.X+","+e.Completion?.OriginalTarget.Z+","+e.Completion?.AssignedLocation.Position.X+","+e.Completion?.AssignedLocation.Position.Z);rows.Flush();frames++;
  }
  void OnDestroy(){rows?.Dispose();runtime?.Dispose();routes?.Dispose();world?.Dispose();projectiles.Dispose();if(target){target.Release();Destroy(target);}if(pixels)Destroy(pixels);if(scene)Destroy(scene);}
 }
}
#endif
