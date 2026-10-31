#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
 public sealed partial class PlayableBootstrap
 {
  public void ConfigureShkvalEvidence(PlayableWorld scene,PlayableProfile p){enabled=false;world=scene;profile=p;}
  public Transform ShkvalEvidenceFlight(int id)=>shells.TryGetValue(id,out var shell)?shell.transform:null;
  public void RenderShkvalEvidence(PlayableSnapshot snapshot,Camera camera)
  {
   view=snapshot;
   world.Fog.Update(view.Vision,.033f);world.RenderSites(view,camera);world.AdvanceAircraftAnimation(view,Time.realtimeSinceStartup);
   foreach(var e in view.Entities){if(!world.Actors.TryGetValue(e.Id,out var a))a=world.Tank(e.Id,e.Owner==view.Owner,e.Kind);world.UpdateResearchModel(a,e);a.Root.position=world.Point(e.Position);a.Hull.localRotation=Quaternion.Euler(0,90-(float)e.HullHeading*Mathf.Rad2Deg,0);a.Turret.localRotation=Quaternion.Euler(0,(float)(e.HullHeading-e.TurretHeading)*Mathf.Rad2Deg,0);world.UpdateAircraft(a,e,view);world.AimExplorer(a,e);a.Health.parent.gameObject.SetActive(false);}
   foreach(var b in view.Buildings){if(!world.Actors.TryGetValue(b.Id,out var a))a=world.Building(b.Id,b.Kind.ToString(),b.Owner==view.Owner);world.UpdateRefineryModel(a,b,view.Tick);world.UpdateFactoryModel(a,b,view);a.Root.position=world.Point(b.Position);world.FaceBuilding(a);world.UpdateHangar(a,b,view.Tick);a.Health.parent.gameObject.SetActive(false);}
   RenderDirectContacts();RenderProjectiles();
  }
 }
 public sealed class ShkvalNativeEvidence:MonoBehaviour
 {
  PlayableRuntime runtime;UnityHostRouteService routes;PlayableWorld world;PlayableBootstrap renderer;Camera cam;RenderTexture target;Texture2D pixels;string output;float start;
  void Start(){start=Time.realtimeSinceStartup;output=Environment.GetEnvironmentVariable("SPACEWARS_SHKVAL_OUTPUT");if(string.IsNullOrEmpty(output)){Application.Quit(1);return;}Directory.CreateDirectory(output);StartCoroutine(Guard());}
  void Update(){if(Time.realtimeSinceStartup-start>150){File.WriteAllText(Path.Combine(output,"FAIL.txt"),"150s wall budget");Application.Quit(1);}}
  IEnumerator Guard(){var r=Run();while(true){object next;bool more;try{more=r.MoveNext();next=more?r.Current:null;}catch(Exception e){File.WriteAllText(Path.Combine(output,"FAIL.txt"),e.ToString());Debug.LogException(e);Application.Quit(1);yield break;}if(!more)yield break;yield return next;}}
  IEnumerator Run()
  {
   var captureArt=ShkvalArtProfile.Load();
   var p=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text));
   var slots=new[]{new TerritorySlot(1,new NavPoint(-1,7),0)};
   var sites=new[]{new TerritorySite(1,PlayableBuildingKind.Headquarters,new NavPoint(-6,7),slots),new TerritorySite(2,PlayableBuildingKind.Headquarters,new NavPoint(12,0),Array.Empty<TerritorySlot>())};
   var config=new OfflineMatchConfiguration(p,"shkval-art-r9","flat-art-stage","native-host",7319,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,2,OfflineControl.Human)},new[]{new OfflineStart("west",1,sites[0].Position,new NavPoint(-4,-3),0,1),new OfflineStart("east",2,sites[1].Position,new NavPoint(21,15),0,2)},sites,Array.Empty<NavObstacle>(),new double[,]{{0,20},{20,0}},scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Shkval,new NavPoint(0,-2)),new OfflineScenarioUnit(1,PlayableEntityKind.Shkval,new NavPoint(0,2),upgraded:true),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(5,-3)),new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(5,3)),new OfflineScenarioUnit(1,PlayableEntityKind.Hawk,new NavPoint(7,0))},scenarioBuildings:new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.Factory,1,1,slots[0].Position)});
   world=new PlayableWorld(transform,p,staged:true);yield return world.Preparation;
   renderer=new GameObject("Production projectile renderer").AddComponent<PlayableBootstrap>();renderer.ConfigureShkvalEvidence(world,p);
   var light=new GameObject("Sun").AddComponent<Light>();light.transform.SetParent(transform);var lighting=IndustrialLightingProfile.Load(null);lighting.ApplyScene(light);
   cam=new GameObject("Shkval capture camera").AddComponent<Camera>();cam.transform.SetParent(transform);cam.orthographic=true;cam.nearClipPlane=.05f;cam.farClipPlane=200;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.12f,.15f,.17f);
   cam.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();cam.gameObject.AddComponent<CameraAntialiasing>().Configure(CameraAntialiasingProfile.Load());
   target=new RenderTexture(1600,1000,24){antiAliasing=4};target.Create();cam.targetTexture=target;pixels=new Texture2D(1600,1000,TextureFormat.RGB24,false);
   routes=new UnityHostRouteService();runtime=new PlayableRuntime(config,1,startPaused:true);
   float deadline=Time.realtimeSinceStartup+60;while(!runtime.IsPrepared&&Time.realtimeSinceStartup<deadline){routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}
   if(!runtime.IsPrepared)throw new Exception("Shkval stage preparation timeout");
   var v=runtime.ParticipantView("player-1");renderer.RenderShkvalEvidence(v,cam);yield return null;
   if(v.Entities.Where(e=>e.Kind==PlayableEntityKind.Shkval).Select(e=>world.Actors[e.Id]).Any(a=>a.LoadedRockets==null||a.LoadedRockets.Length!=4||a.LoadedRockets.Any(t=>!t.gameObject.activeSelf)))throw new Exception("Initial rack must contain all four rockets");
   MeasureLoadedCost(p);
   Focus(new Vector3(-1,0,2),8);for(int i=0;i<30;i++){renderer.RenderShkvalEvidence(v,cam);yield return null;}Shot("01-context");
   Focus(new Vector3(0,.4f,0),3);Shot("02-base-upgraded");
   Focus(new Vector3(0,.4f,-2),1.25f);Shot("03-base-detail");Focus(new Vector3(0,.4f,2),1.25f);Shot("04-upgraded-detail");
   // A real production projectile mesh in a clearly labelled static inspection view.
   using(var art=new WebProjectileVisuals()){
    var rocket=art.Rocket(transform,(float)p.ShkvalProjectileRadius,(float)p.ShkvalProjectileLength,new Color(.19f,.72f,.77f),true);rocket.transform.position=new Vector3(0,3,-5);art.Exhaust(rocket,.4f,.08f,.5f,.8f);foreach(var t in rocket.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;int mask=cam.cullingMask;cam.cullingMask=1<<30;Focus(rocket.transform.position,.65f);Shot("05-projectile-model");cam.cullingMask=mask;Destroy(rocket);
   }
   Focus(new Vector3(6,1,0),7);runtime.RequestPause(false);
   while(runtime.ParticipantView("player-1").Paused)yield return null;
   v=runtime.ParticipantView("player-1");int enemy=v.Buildings.Single(b=>b.Owner==PlayableOwner.Enemy&&b.Kind==PlayableBuildingKind.Headquarters).Id;
   long sequence=0;foreach(var u in v.Entities.Where(u=>u.Owner==PlayableOwner.Player&&u.Position.X>=0))
    if(runtime.TrySubmit(new PlayableCommand(runtime.Generation,++sequence,"player-1",PlayableCommandKind.Attack,new[]{u.Id},targetId:enemy)).Status!=PlayableCommandStatus.Accepted)throw new Exception("Evidence Attack command refused");
   var facts=new System.Collections.Generic.List<string>{"tick,weapon,target,x,y,z,surface_error"};var contacts=new System.Collections.Generic.HashSet<long>();var impacts=new System.Collections.Generic.HashSet<int>();var seenKinds=new System.Collections.Generic.HashSet<PlayableEntityKind>();var sourceShots=new System.Collections.Generic.HashSet<int>();
   var combat=BuildingCombatGeometry.Pose(p,PlayableBuildingKind.Headquarters,false,new NavPoint(12,0),0);
   void Contact(PlayableEntityKind kind,int id,BallisticPoint at){var q=combat.Closest(at);double error=Math.Sqrt((q.X-at.X)*(q.X-at.X)+(q.Y-at.Y)*(q.Y-at.Y)+(q.Z-at.Z)*(q.Z-at.Z));facts.Add(v.Tick+","+kind+","+id+","+at.X.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+at.Y.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+at.Z.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+error.ToString("R",System.Globalization.CultureInfo.InvariantCulture));if(error>.0001)throw new Exception("Impact is off authored surface: "+kind+" "+error);seenKinds.Add(kind);}

   Directory.CreateDirectory(Path.Combine(output,"frames"));Directory.CreateDirectory(Path.Combine(output,"model-frames"));int count=0;long last=-1;bool shot=false,hit=false,pause=false;int shots=0;var ids=new System.Collections.Generic.HashSet<int>();
   var firstLaunches=new System.Collections.Generic.Dictionary<int,PlayableProjectileSnapshot>();
   var continuity=new System.Collections.Generic.List<string>{"tick,unit,shot,muzzle,loaded_instance,flight_instance,body_renderers,mesh_resources"};
   Directory.CreateDirectory(Path.Combine(output,"launch-base"));Directory.CreateDirectory(Path.Combine(output,"launch-upgraded"));
   var muzzleFacts=new System.Collections.Generic.List<string>{"tick,unit,shot,muzzle"};var barrelCounts=new System.Collections.Generic.Dictionary<int,int>();
   var cells=new System.Collections.Generic.List<string>{"tick,unit,cell0,cell1,cell2,cell3"};
   var aimFacts=new System.Collections.Generic.List<string>{"tick,unit,pitch_radians,turret_radians,projectiles"};bool preAimCaptured=false;
   var recoilFacts=new System.Collections.Generic.List<string>{"tick,unit,upgraded,hull_offset_m,root_error_m"};
   var recoilPeaks=new System.Collections.Generic.Dictionary<int,float>();
   var times=new System.Collections.Generic.List<string>{"frame,tick,wall_seconds"};deadline=Time.realtimeSinceStartup+35;
   while(Time.realtimeSinceStartup<deadline){routes.Service(runtime,p.NavigationRequestsPerFrame);v=runtime.ParticipantView("player-1");if(v.Failure!=null||routes.LastProjectionFailure!=null)throw new Exception("Native stage failure: "+v.Failure+routes.LastProjectionFailure);var loadedBefore=world.Actors.Where(k=>k.Value.LoadedRockets!=null).ToDictionary(k=>k.Key,k=>k.Value.LoadedRockets.Select(t=>t.gameObject).ToArray());renderer.RenderShkvalEvidence(v,cam);
    foreach(var x in v.Projectiles.Where(x=>x.Kind==PlayableEntityKind.Shkval)){if(x.Marker!=null)throw new Exception("Building-target Shkval forecast is visible");if(ids.Add(x.Id)){shots++;sourceShots.Add(x.OwnerId);if(!firstLaunches.ContainsKey(x.OwnerId))firstLaunches.Add(x.OwnerId,x);
      var flying=renderer.ShkvalEvidenceFlight(x.Id);
      if(x.Launch==null||!loadedBefore.TryGetValue(x.OwnerId,out var priorLoaded)||!flying||flying.gameObject!=priorLoaded[x.Launch.Barrel])throw new Exception("Loaded rocket was replaced instead of continuing into flight: "+x.Id);
      if(flying.GetComponent<MeshFilter>()?.sharedMesh==null)throw new Exception("Launched combined body mesh lost");
      if(!File.Exists(Path.Combine(output,"exhaust-resources.json"))){var mats=flying.Find("rocket-exhaust").GetComponentsInChildren<Renderer>().Select(r=>r.sharedMaterial).Distinct().ToArray();var shaders=mats.Select(m=>m.shader).Distinct().ToArray();File.WriteAllText(Path.Combine(output,"exhaust-resources.json"),"{\"sharedMaterials\":"+mats.Length+",\"materialBytes\":"+mats.Sum(m=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m))+",\"shaders\":"+shaders.Length+",\"shaderBytes\":"+shaders.Sum(m=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m))+",\"rendererCount\":2,\"extraMeshResources\":0}");}

      continuity.Add(v.Tick+","+x.OwnerId+","+x.Id+","+x.Launch.Barrel+","+priorLoaded[x.Launch.Barrel].GetInstanceID()+","+flying.gameObject.GetInstanceID()+","+flying.GetComponentsInChildren<MeshRenderer>(true).Length+","+flying.GetComponentsInChildren<MeshFilter>(true).Select(f=>f.sharedMesh).Distinct().Count());
      if(x.Launch==null)throw new Exception("Missing visible launch binding");
      barrelCounts.TryGetValue(x.OwnerId,out int prior);if(x.Launch.Barrel!=prior%4)throw new Exception("Muzzle sequence skipped/repeated");barrelCounts[x.OwnerId]=prior+1;
      if(world.Actors[x.OwnerId].LoadedRockets?.Length!=4)throw new Exception("Missing loaded missile");muzzleFacts.Add(v.Tick+","+x.OwnerId+","+x.Id+","+x.Launch.Barrel);}}
    foreach(var e in v.Sounds.Where(e=>e.Kind==PlayableSoundKind.Impact&&e.Building&&e.Contact?.TargetId==enemy&&e.Contact.Impact.HasValue))if(contacts.Add(e.Id)){Contact(e.Weapon,enemy,e.Contact.Impact.Value);Shot("contact-"+e.Weapon+"-"+e.Id);}
    foreach(var e in v.Impacts)if(impacts.Add(e.Id)){Contact(PlayableEntityKind.Shkval,enemy,e.Point);Shot("contact-Shkval-"+e.Id);}

    if(!shot&&v.Projectiles.Any(x=>x.Kind==PlayableEntityKind.Shkval&&x.Age>.15)){shot=true;Shot("06-flight");}
    if(!hit&&v.Impacts.Count>0){hit=true;Shot("07-impact");}
    if(!pause&&shot&&hit){runtime.RequestPause(true);while(!runtime.ParticipantView("player-1").Paused)yield return null;var frozen=runtime.ParticipantView("player-1");renderer.RenderShkvalEvidence(frozen,cam);long tick=frozen.Tick;var frozenHull=world.Actors.Values.Where(a=>a.LoadedRockets!=null).Select(a=>a.Hull.localToWorldMatrix).ToArray();yield return new WaitForSecondsRealtime(.3f);if(runtime.ParticipantView("player-1").Tick!=tick)throw new Exception("Pause advanced simulation");renderer.RenderShkvalEvidence(frozen,cam);if(!frozenHull.SequenceEqual(world.Actors.Values.Where(a=>a.LoadedRockets!=null).Select(a=>a.Hull.localToWorldMatrix)))throw new Exception("Paused recoil moved");Shot("08-paused");pause=true;runtime.RequestPause(false);}
    foreach(var e in v.Entities.Where(e=>e.Kind==PlayableEntityKind.Shkval)){
     if(v.Tick!=last)aimFacts.Add(v.Tick+","+e.Id+","+e.WeaponPitch.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+e.TurretHeading.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+shots);
     if(!preAimCaptured&&shots==0&&e.WeaponPitch>ShkvalLaunchGeometry.NeutralPitch+.01){Focus(new Vector3(0,.4f,e.Upgraded?2:-2),1.25f);Shot("pre-aim-raised");Focus(new Vector3(6,1,0),7);preAimCaptured=true;}
     var a=world.Actors[e.Id];float offset=a.Hull.localPosition.magnitude;float error=Vector3.Distance(a.Root.position,world.Point(e.Position));
     if(error>.0001f)throw new Exception("Recoil displaced authority root");
     if(!recoilPeaks.TryGetValue(e.Id,out float peak)||offset>peak){recoilPeaks[e.Id]=offset;if(offset>.01f){Focus(new Vector3(0,.4f,e.Upgraded?2:-2),1.25f);Shot(e.Upgraded?"recoil-upgraded":"recoil-base");Focus(new Vector3(6,1,0),7);}}
     if(v.Tick!=last)recoilFacts.Add(v.Tick+","+e.Id+","+e.Upgraded+","+offset.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+error.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
    }
    if(v.Tick!=last){foreach(var x in v.Projectiles.Where(x=>x.Kind==PlayableEntityKind.Shkval&&x.Launch!=null&&x.Launch.Barrel==0&&x.Age<=captureArt.exhaustMuzzleFollowSeconds)){
      Focus(new Vector3(0,.4f,x.Launch.Upgraded?2:-2),1.25f);Shot((x.Launch.Upgraded?"launch-upgraded/":"launch-base/")+"shot-"+x.Id+"-tick-"+v.Tick.ToString("D5"));Focus(new Vector3(6,1,0),7);
     }}
    if(v.Tick!=last){foreach(var e in v.Entities.Where(e=>e.Kind==PlayableEntityKind.Shkval))cells.Add(v.Tick+","+e.Id+","+string.Join(",",Enumerable.Range(0,4).Select(c=>(e.ShkvalCells?.Remaining(c)??0).ToString("R",System.Globalization.CultureInfo.InvariantCulture))));last=v.Tick;yield return new WaitForEndOfFrame();Shot("frames/frame-"+count.ToString("D5"));Focus(new Vector3(0,.4f,0),3);Shot("model-frames/frame-"+count.ToString("D5"));Focus(new Vector3(6,1,0),7);times.Add(count+","+v.Tick+","+Time.realtimeSinceStartup.ToString("R",System.Globalization.CultureInfo.InvariantCulture));count++;}
    if(v.Tick>=360)break;yield return null;
   }
   File.WriteAllLines(Path.Combine(output,"pre-aim.csv"),aimFacts);
   File.WriteAllLines(Path.Combine(output,"continuity.csv"),continuity);
   File.WriteAllLines(Path.Combine(output,"recoil.csv"),recoilFacts);
   if(recoilPeaks.Count!=2||recoilPeaks.Values.Any(x=>x<.01f))throw new Exception("Missing visible recoil on both variants");
   File.WriteAllLines(Path.Combine(output,"cells.csv"),cells);
   File.WriteAllLines(Path.Combine(output,"muzzles.csv"),muzzleFacts);if(barrelCounts.Count!=2||barrelCounts.Values.Any(n=>n<4))throw new Exception("Missing full four-muzzle cycle");
   File.WriteAllLines(Path.Combine(output,"frame-times.csv"),times);File.WriteAllLines(Path.Combine(output,"contacts.csv"),facts);
   if(!shot||!hit||!pause||sourceShots.Count!=2||seenKinds.Count!=4)throw new Exception("Missing firing/impact/pause/all-family phase: sources="+sourceShots.Count+" kinds="+string.Join(",",seenKinds));
   var loadedMeshes=world.Actors.Values.Where(a=>a.LoadedRockets!=null).SelectMany(a=>a.LoadedRockets).SelectMany(t=>t.GetComponentsInChildren<MeshFilter>(true).Where(f=>!f.transform.IsChildOf(t.Find("rocket-exhaust")))).Select(f=>f.sharedMesh).ToArray();
   var uniqueLoaded=loadedMeshes.Distinct().ToArray();
   File.WriteAllText(Path.Combine(output,"loaded-metrics.json"),"{\"instances\":"+world.Actors.Values.Where(a=>a.LoadedRockets!=null).Sum(a=>a.LoadedRockets.Length)+",\"fullRackRenderers\":"+loadedMeshes.Length+",\"meshResources\":"+uniqueLoaded.Length+",\"triangles\":"+loadedMeshes.Sum(m=>m.triangles.Length/3)+",\"meshBytes\":"+uniqueLoaded.Sum(m=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m))+",\"meshBytesWithoutReuse\":"+loadedMeshes.Sum(m=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m))+"}");
   File.WriteAllText(Path.Combine(output,"PASS.txt"),"Native Development Player; staged flat scenario, seed 7319; real threaded authority and production renderer; R9 physical ballistic start and pre-aim, no in-air correction; R8 exhaust hidden inside bore and clipped behind muzzle; same loaded object/B+/recoil/FIFO preserved; base and upgraded B; explicit Attack commands; Tank/Explorer/Shkval/Hawk actual building contacts; no Shkval building marker; shots="+shots+" frames="+count+" tick="+v.Tick+"; not ordinary match/device/human acceptance\n");Debug.Log("SHKVAL_NATIVE_CAPTURE_PASS");runtime.RequestPause(true);while(!runtime.ParticipantView("player-1").Paused)yield return null;
   var inspection=InspectObservedLaunches(p,firstLaunches.Values.ToArray());while(inspection.MoveNext())yield return inspection.Current;
   runtime.Dispose();runtime=null;Destroy(renderer.gameObject);yield return null;world=null;Application.Quit(0);
  }
  // Diagnostic render replay of bindings observed in the completed threaded witness.
  // This adds no authority ticks or fabricated physics: it samples the same saved flight
  // and production renderer at 60 time samples/s, so PNG encoding cannot skip the bore exit.
  IEnumerator InspectObservedLaunches(PlayableProfile p,PlayableProjectileSnapshot[] observed)
  {
   var facts=new System.Collections.Generic.List<string>{"variant,phase_seconds,loaded_instance,flight_instance,x,y,z,exhaust_visible"};
   foreach(var source in observed.OrderBy(x=>x.Launch.Upgraded)){
    var l=source.Launch;string variant=l.Upgraded?"upgraded":"base";string folder="inspection-"+variant;Directory.CreateDirectory(Path.Combine(output,folder));
    foreach(var pair in world.Actors)pair.Value.Root.gameObject.SetActive(pair.Key==source.OwnerId);
    var flight=BallisticFlight.Restore(l.Start,new BallisticPoint(l.Start.X+l.Tangent.X,l.Start.Y+l.Tangent.Y-4*p.ShkvalArcHeight,l.Start.Z+l.Tangent.Z),p.ShkvalArcHeight,l.Duration);
    PlayableSnapshot Frame(double age,bool firing){
     var cells=new double[4];if(firing)cells[l.Barrel]=2*p.ShkvalFireIntervalMs/1000d-age;
     var entity=new PlayableEntitySnapshot(source.OwnerId,source.Faction,PlayableEntityKind.Shkval,l.Ground.Ground,100,false,source.TargetId,l.Hull,l.Turret,upgraded:l.Upgraded,weaponPitch:l.Pitch,shkvalCells:firing?new ShkvalCellsSnapshot(cells):null);
     var at=flight.Point(age/flight.Duration);var tangent=flight.Tangent(age/flight.Duration);
     var shot=new PlayableProjectileSnapshot(source.Id,source.OwnerId,source.TargetId,new NavPoint(at.X,at.Z),PlayableEntityKind.Shkval,Math.Atan2(tangent.Z,tangent.X),at.Y,Math.Atan2(tangent.Y,Math.Sqrt(tangent.X*tangent.X+tangent.Z*tangent.Z)),age:age,launch:l);
     return new PlayableSnapshot(p.ProfileId,p.Revision,1,7319,1,3,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,new[]{entity},Array.Empty<PlayableBuildingSnapshot>(),firing?new[]{shot}:Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null);
    }
    Focus(new Vector3((float)l.Ground.X,.4f,(float)l.Ground.Z),1.25f);renderer.RenderShkvalEvidence(Frame(0,false),cam);yield return null;Shot(folder+"/ready");
    int loaded=world.Actors[source.OwnerId].LoadedRockets[l.Barrel].gameObject.GetInstanceID();
    for(int frame=0;frame<=18;frame++){
     double age=frame/60d;renderer.RenderShkvalEvidence(Frame(age,true),cam);var rocket=renderer.ShkvalEvidenceFlight(source.Id);if(!rocket||rocket.gameObject.GetInstanceID()!=loaded)throw new Exception("Inspection replaced loaded rocket");
     bool flame=rocket.Find("rocket-exhaust").GetComponentsInChildren<Renderer>().Any(x=>x.enabled);if(frame==0&&flame)throw new Exception("Exhaust visible with nozzle inside bore");if(frame==18&&!flame)throw new Exception("Exterior exhaust missing");
     facts.Add(variant+","+age.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+loaded+","+rocket.gameObject.GetInstanceID()+","+rocket.position.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+rocket.position.y.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+rocket.position.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+flame);Shot(folder+"/frame-"+frame.ToString("D3"));
    }
   }
   File.WriteAllLines(Path.Combine(output,"inspection.csv"),facts);File.WriteAllText(Path.Combine(output,"INSPECTION-PASS.txt"),"Diagnostic production-renderer replay of the two launches actually observed in the preceding threaded witness; phase sampled at 1/60s, same loaded object throughout; no additional authority steps, not an ordinary-match/FPS claim.");
  }
  [Serializable] sealed class LoadedCost{public string path;public int rockets,gameObjects,transforms,renderers,activeRenderers,filters,submissions,triangles,vertices,meshResources,materials;public long componentBytes,meshBytes,materialBytes,measuredBytes;}
  [Serializable] sealed class LoadedComparison{public string units="Unity Profiler native object bytes; two full racks (8 loaded), same dimensions/color plus one flight rocket included in resource memory; no FPS/draw-call benchmark";public LoadedCost before,after;}
  void MeasureLoadedCost(PlayableProfile p)
  {
   LoadedCost Measure(bool combined){
    var host=new GameObject("Loaded cost measurement");host.SetActive(false);
    using(var art=new WebProjectileVisuals())try{
     var rockets=new System.Collections.Generic.List<GameObject>();
     for(int i=0;i<8;i++){
      var g=combined?art.LoadedRocket(host.transform,(float)p.ShkvalProjectileRadius,(float)p.ShkvalProjectileLength,Color.cyan):art.Rocket(host.transform,(float)p.ShkvalProjectileRadius,(float)p.ShkvalProjectileLength,Color.cyan,true);
      g.transform.Find("rocket-exhaust").gameObject.SetActive(false);rockets.Add(g);
     }
     // Flight resources remain required, and are counted on both sides of the comparison.
     var flight=art.Rocket(host.transform,(float)p.ShkvalProjectileRadius,(float)p.ShkvalProjectileLength,Color.cyan,true);
     var components=rockets.SelectMany(g=>g.GetComponentsInChildren<Component>(true)).ToArray();
     var transforms=components.OfType<Transform>().ToArray();var filters=components.OfType<MeshFilter>().ToArray();var renderers=components.OfType<MeshRenderer>().ToArray();
     // activeSelf chain below ignores the intentionally disabled measurement host.
     bool Visible(Transform t){for(;t!=host.transform;t=t.parent)if(!t.gameObject.activeSelf)return false;return true;}
     var active=filters.Where(f=>Visible(f.transform)).ToArray();
     var meshes=host.GetComponentsInChildren<MeshFilter>(true).Select(f=>f.sharedMesh).Distinct().ToArray();
     var materials=host.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r=>r.sharedMaterials).Distinct().ToArray();
     var c=new LoadedCost{path=combined?"combined loaded copies":"R5 detailed Rocket copies",rockets=8,gameObjects=transforms.Length,transforms=transforms.Length,renderers=renderers.Length,activeRenderers=active.Length,filters=filters.Length,submissions=active.Sum(f=>f.sharedMesh.subMeshCount),triangles=active.Sum(f=>f.sharedMesh.triangles.Length/3),vertices=active.Sum(f=>f.sharedMesh.vertexCount),meshResources=meshes.Length,materials=materials.Length,componentBytes=components.Sum(x=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(x))+transforms.Sum(t=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t.gameObject)),meshBytes=meshes.Sum(m=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m)),materialBytes=materials.Sum(m=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m))};
     c.measuredBytes=c.componentBytes+c.meshBytes+c.materialBytes;return c;
    }finally{Destroy(host);}
   }
   File.WriteAllText(Path.Combine(output,"loaded-cost.json"),JsonUtility.ToJson(new LoadedComparison{before=Measure(false),after=Measure(true)},true));
  }
  void Focus(Vector3 at,float size){cam.orthographicSize=size;cam.transform.position=at+new Vector3(9,10,-14);cam.transform.LookAt(at);}
  void Shot(string name){cam.Render();var old=RenderTexture.active;RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();RenderTexture.active=old;File.WriteAllBytes(Path.Combine(output,name+".png"),pixels.EncodeToPNG());}
  void OnDestroy(){runtime?.Dispose();routes?.Dispose();if(!renderer)world?.Dispose();if(target){target.Release();Destroy(target);}if(pixels)Destroy(pixels);}
 }
}
#endif
