#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using Unity.Profiling;
namespace Spacewars.Presentation
{
 // Presentation fixture in a native Player. Snapshot poses are authored for visual review;
 // gameplay is independently qualified by the domain and ordinary threaded-host tests.
 public sealed class SentryTurretReviewCapture:MonoBehaviour
 {
  PlayableWorld world;GameObject root;Camera cam;RenderTexture target;Texture2D pixels;string output;ProfilerRecorder draws,passes;
  [Serializable]sealed class Costs{public long beforeDraws,beforePasses,afterDraws,afterPasses;public string source,scope="Fixed presentation fixture, same camera/1600x1000/lighting; 6 buildings before, 11 after (5 added turrets), one active laser. -1 unavailable (including zero samples while explicit Camera.Render produced pixels). No FPS claim.";}
  static long Count(ProfilerRecorder r)=>r.Valid&&r.Count>0&&r.LastValue>0?r.LastValue:-1;
  IEnumerator Start()
  {
   output=Environment.GetEnvironmentVariable("SPACEWARS_SENTRY_REVIEW");if(string.IsNullOrEmpty(output)){Application.Quit(1);yield break;}
   Directory.CreateDirectory(output);draws=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Draw Calls Count",1);passes=ProfilerRecorder.StartNew(ProfilerCategory.Render,"SetPass Calls Count",1);var run=Run();
   while(true){bool more;object next;try{more=run.MoveNext();next=more?run.Current:null;}catch(Exception e){File.WriteAllText(Path.Combine(output,"FAIL.txt"),e.ToString());Debug.LogException(e);Application.Quit(1);yield break;}if(!more)break;yield return next;}
   File.WriteAllText(Path.Combine(output,"PASS.txt"),"Native Player presentation fixture; authored snapshot poses; ordinary gameplay proof stored separately. Silent startup; no human/device acceptance.\n");Application.Quit(0);
  }
  IEnumerator Run()
  {
   var p=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text));
   var home=TerritoryRules.Sites(p).First(s=>s.Kind==PlayableBuildingKind.Headquarters);var center=home.Position;
   root=new GameObject("Sentry native presentation review");world=new PlayableWorld(root.transform,p,staged:true);yield return world.Preparation;world.Fog.ShowPublic();
   var lighting=IndustrialLightingProfile.Load("three-crossings");var light=new GameObject("Review sun").AddComponent<Light>();light.transform.SetParent(root.transform);lighting.ApplyScene(light);
   cam=new GameObject("Review camera").AddComponent<Camera>();cam.transform.SetParent(root.transform);cam.orthographic=true;cam.nearClipPlane=.1f;cam.farClipPlane=500;cam.backgroundColor=new Color(.1f,.12f,.14f);cam.clearFlags=CameraClearFlags.SolidColor;
   cam.gameObject.AddComponent<VisibleShadowCoverage>().Configure(root.transform,lighting);cam.gameObject.AddComponent<ContactOcclusionCamera>().Configure(ContactOcclusionProfile.Load());cam.gameObject.AddComponent<ActorGlowCamera>().Configure(ActorMaterialProfile.Load());cam.gameObject.AddComponent<CameraAntialiasing>().Configure(CameraAntialiasingProfile.Load());
   target=new RenderTexture(1600,1000,24){antiAliasing=CameraAntialiasingProfile.Load().Samples};target.Create();cam.targetTexture=target;pixels=new Texture2D(1600,1000,TextureFormat.RGB24,false);
   var buildings=new List<PlayableBuildingSnapshot>();
   PlayableBuildingSnapshot Building(int id,PlayableBuildingKind kind,NavPoint at,int slot=0,PlayableTurretSnapshot state=null)=>new PlayableBuildingSnapshot(id,PlayableOwner.Player,kind,at,TerritoryRules.Health(p,kind),1,0,0,default,siteId:home.Id,slotId:slot,parentId:slot==0?0:1,turret:state);
   void Place(PlayableBuildingSnapshot b){var a=world.Building(b.Id,b.Kind.ToString(),true);a.Root.position=world.Point(b.Position);world.FaceBuilding(a,cam);a.Selection.SetActive(false);buildings.Add(b);}
   Place(Building(1,PlayableBuildingKind.Headquarters,center));
   var kinds=new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Refinery,PlayableBuildingKind.ScientificCenter,PlayableBuildingKind.Hangar,PlayableBuildingKind.Factory};
   foreach(var slot in home.Slots)Place(Building(10+slot.Id,kinds[(slot.Id-1)%kinds.Length],slot.Position,slot.Id));
   void CameraAt(NavPoint focus,float size){var point=world.Point(focus);cam.orthographicSize=size;cam.transform.position=point+new Vector3(16,26,-23);cam.transform.LookAt(point+Vector3.up*.8f);}
   void Capture(string name){cam.Render();var old=RenderTexture.active;RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();RenderTexture.active=old;File.WriteAllBytes(Path.Combine(output,name),pixels.EncodeToPNG());}
   var siteState=new TerritorySiteSnapshot(home,PlayableOwner.Player,PlayableOwner.Player,1,false,1,true);
   var pads=new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,0,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),null,null,sites:new[]{siteState});world.RenderSites(pads,cam);
   CameraAt(center,13);for(int frame=0;frame<5;frame++)yield return null;var costs=new Costs{beforeDraws=Count(draws),beforePasses=Count(passes),source=Environment.GetEnvironmentVariable("SPACEWARS_SENTRY_REVIEW_SOURCE")};Capture("01-before.png");
   foreach(var slot in home.DefenseSlots(p))Place(Building(100+slot.Id,PlayableBuildingKind.SentryTurret,slot.Position,slot.Id,new PlayableTurretSnapshot(0,Math.PI/2,0,false,0,0)));
   var selected=buildings.First(b=>b.Kind==PlayableBuildingKind.SentryTurret);var pad=selected.Position;var targetAt=new NavPoint(pad.X+5,pad.Z+3);var tank=world.Tank(900,false);tank.Root.position=world.Point(targetAt);tank.Selection.SetActive(false);
   PlayableSnapshot Snapshot(PlayableBuildingSnapshot b,PlayableEntitySnapshot enemy)=>new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,0,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,new[]{enemy},buildings.Where(x=>x.Id!=b.Id).Concat(new[]{b}).ToArray(),Array.Empty<PlayableProjectileSnapshot>(),null,null,activeProfile:p);
   var ground=new PlayableEntitySnapshot(900,PlayableOwner.Enemy,PlayableEntityKind.Tank,targetAt,100,false,0,0,0);
   var active=Building(selected.Id,selected.Kind,pad,selected.SlotId,new PlayableTurretSnapshot(900,Math.Atan2(3,5),0,false,1,0));
   foreach(var b in buildings.Where(b=>b.Turret!=null))world.Actors[b.Id].Sentry.Drive(b.Id==active.Id?active:b,Snapshot(active,ground),p);
   for(int frame=0;frame<5;frame++)yield return null;costs.afterDraws=Count(draws);costs.afterPasses=Count(passes);File.WriteAllText(Path.Combine(output,"render-costs.json"),JsonUtility.ToJson(costs,true));Capture("02-after.png");CameraAt(new NavPoint(pad.X+2,pad.Z+1),5);yield return null;Capture("03-laser-close.png");
   var air=world.Tank(901,false,PlayableEntityKind.Hawk);air.Root.position=world.Point(pad)+Vector3.up*7;air.Selection.SetActive(false);
   for(int i=0;i<36;i++){
    bool overhead=i>=24;double yaw=overhead?Math.Atan2(3,5):-Math.PI+(Math.PI+Math.Atan2(3,5))*Math.Min(1,i/18d),pitch=overhead?Math.PI/2*Math.Min(1,(i-23)/8d):0;
    var enemy=overhead?new PlayableEntitySnapshot(901,PlayableOwner.Enemy,PlayableEntityKind.Hawk,pad,100,false,0,0,0,altitude:7):ground;
    var b=Building(selected.Id,selected.Kind,pad,selected.SlotId,new PlayableTurretSnapshot(enemy.Id,yaw,pitch,true,i,0));world.Actors[b.Id].Sentry.Drive(b,Snapshot(b,enemy),p);yield return null;Capture("turn-"+i.ToString("D2")+".png");
   }
   Capture("04-vertical.png");
   var renderers=world.Actors[selected.Id].Hull.GetComponentsInChildren<MeshRenderer>();var filters=world.Actors[selected.Id].Hull.GetComponentsInChildren<MeshFilter>();
   File.WriteAllText(Path.Combine(output,"costs.txt"),"renderers="+renderers.Length+"; triangles="+filters.Sum(f=>f.sharedMesh.triangles.Length/3)+"; vertices="+filters.Sum(f=>f.sharedMesh.vertexCount)+"; sharedMaterials="+renderers.SelectMany(r=>r.sharedMaterials).Distinct().Count()+"; laser points=2, one shared material, no shadows\n");
  }
  void OnDestroy(){draws.Dispose();passes.Dispose();world?.Dispose();if(target){target.Release();Destroy(target);}if(pixels)Destroy(pixels);if(root)Destroy(root);}
 }
}
#endif
