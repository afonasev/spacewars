#if DEVELOPMENT_BUILD || UNITY_EDITOR
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
 // Native Player evidence: supported scenario genesis, normal Move ingress and threaded
 // authority. No teleports, synthetic trail samples, synchronous Pump or hidden unit reveal.
 public sealed class GroundTrailVideoEvidence:MonoBehaviour
 {
  PlayableRuntime runtime;UnityHostRouteService routes;PlayableWorld world;Camera cam;GameObject scene;
  RenderTexture target;Texture2D pixels;string output;float started;long sequence;StreamWriter frames;
  bool profileOnly;NativeRouteFrameTelemetry profileFrames;NativeFrameTimingProbe frameProbe;long profileFrame;
  void Start(){profileOnly=Environment.GetEnvironmentVariable("SPACEWARS_TRAIL_PROFILE")=="1";if(profileOnly){Application.runInBackground=true;GetComponent<PlayableBootstrap>()?.BeginFrameProfileEvidence();}started=Time.realtimeSinceStartup;output=Environment.GetEnvironmentVariable("SPACEWARS_TRAIL_VIDEO_OUTPUT");if(string.IsNullOrEmpty(output)){Debug.LogError("Missing owned video output");Application.Quit(1);return;}Directory.CreateDirectory(output);StartCoroutine(GuardedRun());}
  void Update(){if(Time.realtimeSinceStartup-started>300)Fail("Capture exceeded 300s wall budget");}
  void Fail(string message){File.WriteAllText(Path.Combine(output,"FAIL.txt"),message);Debug.LogError("TRAIL_VIDEO_FAIL "+message);Application.Quit(1);enabled=false;}
  IEnumerator GuardedRun()
  {
   var run=Run();while(true){bool more;object next;try{more=run.MoveNext();next=more?run.Current:null;}catch(Exception e){Fail(e.ToString());yield break;}if(!more)yield break;yield return next;}
  }
  IEnumerator Run()
  {
   string name=Environment.GetEnvironmentVariable("SPACEWARS_TRAIL_VIDEO_MAP")??"crossings";
   IPlayableTerrain map=name=="moss"?(IPlayableTerrain)new MossHighlandMap():name=="meridian"?new WhiteMeridianMap():name=="basalt"?new BasaltCrownMap():name=="foundry"?new FoundryMap(new FoundryProfileData()):ThreeCrossingsMap.Default;
   var p=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);
   int actorCount=int.Parse(Environment.GetEnvironmentVariable("SPACEWARS_TRAIL_PROFILE_ACTORS")??"3");
   if(actorCount!=3&&(!profileOnly||!(map is ThreeCrossingsMap)||(actorCount!=30&&actorCount!=50)))throw new InvalidOperationException("Scaled profile requires ThreeCrossings, profile mode and30 or50 actors");
   var roster=map is WhiteMeridianMap?new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,1,OfflineControl.Human),new OfflineParticipant("player-3",3,2,OfflineControl.Human),new OfflineParticipant("player-4",4,2,OfflineControl.Human)}:new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,2,OfflineControl.Human)};
   OfflineMatchConfiguration authored;
   if(map is MossHighlandMap moss)authored=moss.Configuration(p,7319,roster);
   else if(map is WhiteMeridianMap meridian)authored=meridian.Configuration(p,7319,roster);
   else if(map is BasaltCrownMap basalt)authored=basalt.Configuration(p,7319,roster);
   else if(map is FoundryMap foundry){var a=foundry.Configuration(p,7319);authored=new OfflineMatchConfiguration(p,a.SourceIdentity,a.MapIdentity,a.RouteProvenance,7319,roster,a.Starts.ToArray(),a.Sites.ToArray(),a.Obstacles.ToArray(),Costs(a),terrain:map.Id);}
   else{var sites=map.Sites(p);var starts=sites.Where(s=>s.Kind==PlayableBuildingKind.Headquarters).Select((s,i)=>new OfflineStart("start-"+i,s.Id,s.Position,new NavPoint(s.Position.X+(i==0?10:-10),s.Position.Z),0,i+1)).ToArray();authored=new OfflineMatchConfiguration(p,"trail-video-genesis",map.Id,"authored-terrain",7319,roster,starts,sites,map.MovementBlockers.ToArray(),new double[starts.Length,starts.Length],terrain:map.Id);}
   var origin=FindLane(p,authored,name);if(double.IsNaN(origin.X)){Fail("No safe three-lane film location on "+name);yield break;}
   var origins=actorCount==3?new[]{new NavPoint(origin.X,origin.Z-5),origin,new NavPoint(origin.X,origin.Z+5)}:FindGroupLanes(p,authored,actorCount);
   if(origins==null){Fail("No safe separated group lanes for "+actorCount);yield break;}
   var kinds=new[]{PlayableEntityKind.Explorer,PlayableEntityKind.Tank,PlayableEntityKind.Shkval};
   var assigned=authored.Assignments[0];var starts2=authored.Starts.Select((s,i)=>new OfflineStart(s.Id,s.SiteId,s.Position,i==assigned?origins[0]:s.ExplorerAnchor,s.Heading,s.PinnedLogicalPlayer)).ToArray();
   var scenario=Enumerable.Range(1,actorCount-1).Select(i=>new OfflineScenarioUnit(1,kinds[i%3],origins[i])).ToArray();
   var config=new OfflineMatchConfiguration(p,"ground-trail-video-v1",map.Id,authored.RouteProvenance,7319,roster,starts2,authored.Sites.ToArray(),authored.Obstacles.ToArray(),Costs(authored),scenario:scenario,terrain:map.Id);
   File.WriteAllText(Path.Combine(output,"scenario.txt"),"Native Player; normal threaded Move; seed=7319; map="+map.Id+"; genesis="+origin.X+","+origin.Z+"; lanes Explorer,Tank,Shkval; distance=14m; expiry=300ticks; local review, not human/device acceptance\n");
   scene=new GameObject("Ground trail native video");world=new PlayableWorld(scene.transform,p,staged:true);yield return world.Preparation;
   var light=new GameObject("Map sun").AddComponent<Light>();light.transform.SetParent(scene.transform);var lighting=IndustrialLightingProfile.Load(map.Id);lighting.ApplyScene(light);
   cam=new GameObject("Native video camera").AddComponent<Camera>();cam.transform.SetParent(scene.transform);cam.orthographic=true;cam.orthographicSize=9;cam.nearClipPlane=.1f;cam.farClipPlane=700;
   var focus=world.Point(new NavPoint(origin.X+7,origin.Z));cam.transform.position=focus+new Vector3(9,25,-21);cam.transform.LookAt(focus);cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.10f,.12f,.14f);
   if(actorCount!=3){focus=world.Point(new NavPoint(origins.Average(v=>v.X)+7,origins.Average(v=>v.Z)));cam.orthographicSize=25;cam.transform.position=focus+new Vector3(9,25,-21);cam.transform.LookAt(focus);}
   File.AppendAllText(Path.Combine(output,"scenario.txt"),"actors="+actorCount+"; origins="+string.Join(";",origins.Select(v=>v.X+","+v.Z))+"\n");
   cam.gameObject.AddComponent<VisibleShadowCoverage>().Configure(scene.transform,lighting);cam.gameObject.AddComponent<ContactOcclusionCamera>().Configure(ContactOcclusionProfile.Load());cam.gameObject.AddComponent<ActorGlowCamera>().Configure(ActorMaterialProfile.Load());cam.gameObject.AddComponent<CameraAntialiasing>().Configure(CameraAntialiasingProfile.Load());
   if(!profileOnly){target=new RenderTexture(1600,900,24){antiAliasing=CameraAntialiasingProfile.Load().Samples};target.Create();cam.targetTexture=target;pixels=new Texture2D(1600,900,TextureFormat.RGB24,false);}
   else{frameProbe=new NativeFrameTimingProbe(output);profileFrames=new NativeRouteFrameTelemetry(Path.Combine(output,"profile-frames.csv"));File.WriteAllText(Path.Combine(output,"profile-mode.txt"),"Existing native three-class/14m scenario; fullscreen camera at actual window resolution; no screenshot readback, video IO or preference toggles. Movement and post-arrival frame intervals are same-app observations, not predecessor/candidate FPS equivalence.\n");}
   routes=new UnityHostRouteService();runtime=new PlayableRuntime(config,1,startPaused:true);
   float deadline=Time.realtimeSinceStartup+60;
   while(!runtime.IsPrepared&&Time.realtimeSinceStartup<deadline){routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}
   if(!runtime.IsPrepared){Fail("Preparation timeout");yield break;}
   runtime.RequestPause(false);while(runtime.Latest.Paused){routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}
   // Settle only the actual team's fog before filming movement, so reveal animation
   // cannot be mistaken for delayed track emission in this staged native witness.
   float settle=Time.realtimeSinceStartup+1;while(Time.realtimeSinceStartup<settle){world.Fog.Update(runtime.ParticipantView("player-1").Vision,Time.unscaledDeltaTime);yield return null;}
   var initial=runtime.ParticipantView("player-1");var movers=initial.Entities.Where(e=>e.Owner==PlayableOwner.Player).OrderBy(e=>(int)e.Kind).ToArray();
   if(movers.Length!=actorCount){Fail("Expected "+actorCount+" owned movers; got "+movers.Length);yield break;}
   if(actorCount!=3)foreach(var e in movers)foreach(double offset in new[]{0d,14d}){var v=cam.WorldToViewportPoint(world.Point(new NavPoint(e.Position.X+offset,e.Position.Z)));if(v.z<=0||v.x<.03f||v.x>.97f||v.y<.03f||v.y>.97f){Fail("Group movement outside measured camera");yield break;}}
   foreach(var e in movers){var submit=runtime.TrySubmit(new PlayableCommand(runtime.Generation,++sequence,"player-1",PlayableCommandKind.Move,new[]{e.Id},new NavPoint(e.Position.X+14,e.Position.Z)));Debug.Log("TRAIL_MOVE "+e.Kind+" "+submit.Status);}
   var initialPositions=movers.ToDictionary(e=>e.Id,e=>e.Position);long commandTick=initial.Tick,firstMotion=-1,arrivalTick=-1,lastCapture=-10;int count=0,maxQuads=0;bool toggleOff=false,toggleOn=false;
   var startedKinds=new HashSet<PlayableEntityKind>();
   deadline=Time.realtimeSinceStartup+120;frames=new StreamWriter(Path.Combine(output,"frames.csv"));frames.WriteLine("frame,tick,quads,enabled");
   while(Time.realtimeSinceStartup<deadline)
   {
    long frameStart=System.Diagnostics.Stopwatch.GetTimestamp();routes.Service(runtime,p.NavigationRequestsPerFrame);var view=runtime.ParticipantView("player-1");
    double routeMs=(System.Diagnostics.Stopwatch.GetTimestamp()-frameStart)*1000d/System.Diagnostics.Stopwatch.Frequency;long renderStart=System.Diagnostics.Stopwatch.GetTimestamp();
    if(view.Failure!=null||routes.LastProjectionFailure!=null){Fail("Runtime/route failure: "+view.Failure+" "+routes.LastProjectionFailure);yield break;}
    var selected=view.Entities.Where(e=>initialPositions.ContainsKey(e.Id)).ToArray();
    foreach(var e in selected)if(Distance(e.Position,initialPositions[e.Id])>1e-7&&startedKinds.Add(e.Kind)){
     bool marked=view.GroundTrails.Any(t=>t.Kind==e.Kind);Debug.Log("TRAIL_START kind="+e.Kind+" tick="+view.Tick+" marked="+marked+" displacement="+Distance(e.Position,initialPositions[e.Id]));
     if(!marked){Fail("First observed moving snapshot has no trail for "+e.Kind);yield break;}
    }
    if(firstMotion<0&&selected.Any(e=>Distance(e.Position,initialPositions[e.Id])>.25)){firstMotion=view.Tick;Debug.Log("TRAIL_FIRST_MOTION tick="+firstMotion);}
    if(firstMotion<0&&Time.realtimeSinceStartup>deadline-60){Fail("First motion failed at 60s");yield break;}
    if(arrivalTick<0&&selected.Length==actorCount&&selected.All(e=>Distance(e.Position,new NavPoint(initialPositions[e.Id].X+14,initialPositions[e.Id].Z))<1.1&&!e.Moving)){arrivalTick=view.Tick;Debug.Log("TRAIL_ARRIVED tick="+arrivalTick);}
    world.Fog.Update(view.Vision,Time.unscaledDeltaTime);world.RenderSites(view,cam);
    foreach(var e in view.Entities){if(!world.Actors.TryGetValue(e.Id,out var actor))actor=world.Tank(e.Id,true,e.Kind);actor.Health.parent.gameObject.SetActive(false);actor.Root.position=world.Point(e.Position);var slope=map.SurfaceGradient(e.Position);actor.Root.rotation=Quaternion.FromToRotation(Vector3.up,new Vector3(-(float)slope.X,1,-(float)slope.Z).normalized);actor.Hull.localRotation=Quaternion.Euler(0,90-(float)e.HullHeading*Mathf.Rad2Deg,0);actor.Turret.localRotation=Quaternion.Euler(0,(float)(e.HullHeading-e.TurretHeading)*Mathf.Rad2Deg,0);}
    // One short preference demonstration after arrival; normal enabled state returns.
    if(!profileOnly&&arrivalTick>=0&&view.Tick>=arrivalTick+30&&!toggleOff){NativeGraphicsSettings.GroundTrailsEnabled=false;toggleOff=true;}
    if(!profileOnly&&arrivalTick>=0&&view.Tick>=arrivalTick+60&&!toggleOn){NativeGraphicsSettings.GroundTrailsEnabled=true;toggleOn=true;}
    world.RenderGroundTrails(view);maxQuads=Math.Max(maxQuads,world.GroundTrailQuadCount);
    if(profileOnly){var sample=new NativeRouteFrameSample{Frame=++profileFrame,Generation=view.Generation,Tick=view.Tick,Sequence=view.Sequence,Elapsed=Time.realtimeSinceStartup-started,FrameMs=Time.unscaledDeltaTime*1000d,Units=view.Entities.Count,RouteServiceMs=routeMs,RenderMs=(System.Diagnostics.Stopwatch.GetTimestamp()-renderStart)*1000d/System.Diagnostics.Stopwatch.Frequency,UpdateMs=(System.Diagnostics.Stopwatch.GetTimestamp()-frameStart)*1000d/System.Diagnostics.Stopwatch.Frequency,MotionActor=selected.Any(e=>e.Moving)?selected[0].Id:0};frameProbe.Read(ref sample);profileFrames.TryWrite(sample);}
    if(!profileOnly&&view.Tick-lastCapture>=3)
    {
     lastCapture=view.Tick;yield return new WaitForEndOfFrame();cam.Render();var prior=RenderTexture.active;RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1600,900),0,0);pixels.Apply();RenderTexture.active=prior;
     File.WriteAllBytes(Path.Combine(output,"frame-"+count.ToString("D5")+".png"),pixels.EncodeToPNG());if(count==10)Debug.Log("TRAIL_NATIVE_AA samples="+CameraAntialiasingDiagnostics.Samples(cam));frames.WriteLine(count+","+view.Tick+","+world.GroundTrailQuadCount+","+NativeGraphicsSettings.GroundTrailsEnabled);frames.Flush();count++;
    }
    if(arrivalTick>=0&&view.Tick>=arrivalTick+330){
     if(name!="foundry"&&maxQuads==0){Fail("No visible soft-surface trails");yield break;}
     if(name=="foundry"&&maxQuads!=0){Fail("Hard Foundry acquired trails");yield break;}
     if(world.GroundTrailQuadCount!=0){Fail("Trails survived ten seconds after arrival");yield break;}
     File.WriteAllText(Path.Combine(output,"PASS.txt"),"firstMotion="+firstMotion+" arrival="+arrivalTick+" final="+view.Tick+" maxQuads="+maxQuads+" frames="+count+"\n");frames.Dispose();frames=null;
     if(profileOnly){profileFrames.Dispose();float flushUntil=Time.realtimeSinceStartup+2;while(!profileFrames.IsStopped&&Time.realtimeSinceStartup<flushUntil)yield return null;if(!profileFrames.IsStopped||profileFrames.Failure!=null||profileFrames.Dropped!=0){Fail("Frame telemetry did not flush cleanly");yield break;}}
     Application.Quit(0);yield break;
    }
    yield return null;
   }
   Fail("Arrival/expiry witness exceeded 120s");
  }
  static double[,] Costs(OfflineMatchConfiguration c){var a=new double[c.Starts.Count,c.Starts.Count];for(int i=0;i<c.Starts.Count;i++)for(int j=0;j<c.Starts.Count;j++)a[i,j]=c.RouteCost(i,j);return a;}
  static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
  static NavPoint[] FindGroupLanes(PlayableProfile p,OfflineMatchConfiguration config,int count)
  {
   var map=p.AuthoredMap;var geometry=new NavGeometry(p.ArenaHalfExtent,map.MovementBlockers.ToArray(),1);
   double radius=new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Shkval}.Max(k=>PlayableUnitRules.Radius(p,k));
   double gap=Math.Max(3,2*radius+.5);
   var obstacles=config.Sites.Select(s=>(s.Position,TerritoryRules.Radius(p,s.Kind)+radius+3)).Concat(config.Sites.SelectMany(s=>s.Slots.Select(t=>(t.Position,Math.Max(p.FactoryFootprintRadius,p.ScienceFootprintRadius)+radius+3)))).ToArray();
   for(double z=-map.HalfExtent+10;z<map.HalfExtent-10;z+=3)for(double x=-map.HalfExtent+10;x<map.HalfExtent-28;x+=3){
    var result=new NavPoint[count];bool valid=true;
    for(int i=0;i<count&&valid;i++){
     var a=new NavPoint(x+(i%5)*gap,z+(i/5)*gap);var b=new NavPoint(a.X+14,a.Z);result[i]=a;
     if(!geometry.SegmentFree(a,b,radius)||!map.SupportsSweep(a,b,radius)){valid=false;break;}
     for(int step=0;step<=14;step++){var point=new NavPoint(a.X+step,a.Z);if(obstacles.Any(o=>Distance(point,o.Item1)<o.Item2)||GroundTrailSurface.At(map,point)==GroundTrailSurfaceKind.Hard){valid=false;break;}}
    }
    if(valid)return result;
   }
   return null;
  }
  static NavPoint FindLane(PlayableProfile p,OfflineMatchConfiguration config,string name)
  {
   var map=p.AuthoredMap;var geometry=new NavGeometry(p.ArenaHalfExtent,map.MovementBlockers.ToArray(),1);double radius=new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Shkval}.Max(k=>PlayableUnitRules.Radius(p,k));
   var obstacles=config.Sites.Select(s=>(s.Position,TerritoryRules.Radius(p,s.Kind)+radius+3)).Concat(config.Sites.SelectMany(s=>s.Slots.Select(t=>(t.Position,Math.Max(p.FactoryFootprintRadius,p.ScienceFootprintRadius)+radius+3)))).ToArray();
   double best=double.NegativeInfinity;var result=new NavPoint(double.NaN,0);
   for(double z=-map.HalfExtent+12;z<map.HalfExtent-12;z+=3)for(double x=-map.HalfExtent+10;x<map.HalfExtent-24;x+=3)
   {
    bool valid=true;double score=0;
    foreach(int lane in new[]{-1,0,1}){
     var a=new NavPoint(x,z+lane*5);var b=new NavPoint(x+14,z+lane*5);
     if(!geometry.SegmentFree(a,b,radius)||!map.SupportsSweep(a,b,radius)){valid=false;break;}
     for(int s=0;s<=7;s++){var point=new NavPoint(x+s*2,z+lane*5);if(obstacles.Any(o=>Distance(point,o.Item1)<o.Item2)){valid=false;break;}var kind=GroundTrailSurface.At(map,point);score+=name=="foundry"?(kind==GroundTrailSurfaceKind.Hard?1:0):kind==GroundTrailSurfaceKind.Hard?0:kind==GroundTrailSurfaceKind.Grass?2:1;}
     if(!valid)break;
    }
    score-=Math.Sqrt(x*x+z*z)*.001;
    if(valid&&score>best){best=score;result=new NavPoint(x,z);}
   }
   return result;
  }
  void OnApplicationQuit()=>Cleanup();
  void OnDestroy()=>Cleanup();
  void Cleanup(){profileFrames?.Dispose();profileFrames=null;frameProbe?.Dispose();frameProbe=null;frames?.Dispose();frames=null;runtime?.RequestStop();routes?.Dispose();routes=null;runtime?.Dispose();runtime=null;if(scene)world?.Dispose();world=null;if(target){target.Release();Destroy(target);target=null;}if(pixels){Destroy(pixels);pixels=null;}if(scene){Destroy(scene);scene=null;}}
 }
 public sealed partial class PlayableBootstrap
 {
  // The explicit profiling entrypoint owns this bootstrap and its loading UI.
  internal void BeginFrameProfileEvidence(){StopAllCoroutines();if(startupLoadingHost)Destroy(startupLoadingHost);if(startupLoadingPanel)Destroy(startupLoadingPanel);startupLoadingHost=null;startupLoadingPanel=null;}
 }
}
#endif
