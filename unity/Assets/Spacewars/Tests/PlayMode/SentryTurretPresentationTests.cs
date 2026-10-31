using System;
using System.Linq;
using System.Collections;
using System.IO;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Tests.PlayMode
{
 public sealed class SentryTurretPresentationTests
 {
  const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
  static PlayableSnapshot View(PlayableProfile p,PlayableBuildingSnapshot[] buildings,PlayableEntitySnapshot[] entities=null,TerritorySiteSnapshot[] sites=null)=>new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,entities??Array.Empty<PlayableEntitySnapshot>(),buildings,Array.Empty<PlayableProjectileSnapshot>(),null,null,sites:sites,activeProfile:p);
  static PlayableBuildingSnapshot Turret(int target,double heading=0,double pitch=0,ConstructionPhase phase=ConstructionPhase.Ready)=>new PlayableBuildingSnapshot(10,PlayableOwner.Player,PlayableBuildingKind.SentryTurret,default,300,phase==ConstructionPhase.Ready?1:.5,0,0,default,phase:phase,turret:new PlayableTurretSnapshot(target,heading,pitch,true,1,0));
  [Test] public void LaserUsesSensorAndVisibleTargetAcrossTurningAndVerticalAimAndStopsWhenTargetMissing()
  {
   var root=new GameObject("Sentry presentation regression");var p=PlayableProfile.Default;
   try{using var world=new PlayableWorld(root.transform,p);var actor=world.Building(10,"SentryTurret",true);world.FaceBuilding(actor);
    var enemy=new PlayableEntitySnapshot(20,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(5,4),100,false,0,0,0);
    foreach(double yaw in new[]{0d,Math.PI/2,Math.PI})foreach(double pitch in new[]{0d,Math.PI/2}){
     var b=Turret(20,yaw,pitch);actor.Sentry.Drive(b,View(p,new[]{b},new[]{enemy}),p);Assert.True(actor.Sentry.LaserVisible);
     var rig=actor.Hull.GetComponent<SentryTurretRig>();var expected=new Vector3((float)(Math.Cos(yaw)*Math.Cos(pitch)),(float)Math.Sin(pitch),(float)(Math.Sin(yaw)*Math.Cos(pitch)));Assert.Greater(Vector3.Dot(rig.Forward,expected),.999f,"Rendered barrels must follow authority yaw/pitch");
     var muzzle=SentryTurretGeometry.Muzzle(default,BuildingCombatGeometry.ApronLift,p.TurretModelScale,yaw,pitch,0);var authored=rig.Left.position;Assert.Less(Math.Min(Vector3.Distance(authored,new Vector3((float)muzzle.X,(float)muzzle.Y,(float)muzzle.Z)),Vector3.Distance(rig.Right.position,new Vector3((float)muzzle.X,(float)muzzle.Y,(float)muzzle.Z))),.001f,"Physical shot starts at one authored muzzle");
     var sensor=actor.Hull.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="sensor-lens");Assert.Less(Vector3.Distance(sensor.position,actor.Sentry.Laser.GetPosition(0)),1e-5);
     Assert.AreEqual(5,actor.Sentry.Laser.GetPosition(1).x,1e-5);Assert.AreEqual(4,actor.Sentry.Laser.GetPosition(1).z,1e-5);
     var previous=actor.Sentry.Laser.GetPosition(1);actor.Sentry.Drive(b,View(p,new[]{b},new[]{enemy}),p);Assert.True(actor.Sentry.LaserVisible);Assert.AreEqual(previous,actor.Sentry.Laser.GetPosition(1));
    }
    var missing=Turret(20);actor.Sentry.Drive(missing,View(p,new[]{missing}),p);Assert.False(actor.Sentry.LaserVisible,"Hidden target cannot be resolved from authority");
    var idle=Turret(0);actor.Sentry.Drive(idle,View(p,new[]{idle},new[]{enemy}),p);Assert.False(actor.Sentry.LaserVisible);
    var construction=Turret(20,phase:ConstructionPhase.Constructing);actor.Sentry.Drive(construction,View(p,new[]{construction},new[]{enemy}),p);Assert.False(actor.Sentry.LaserVisible);
    Assert.NotNull(actor.Sentry.Laser.sharedMaterial);Assert.AreEqual(2,actor.Sentry.Laser.positionCount);
   }finally{UnityEngine.Object.DestroyImmediate(root);}
  }
  [UnityTest] public IEnumerator TurretLabControlsOpenUnderBuildingsAndPersistThroughRealUi()
  {
   var p=PlayableProfile.Default;var host=new GameObject("Sentry lab UI");var hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
   typeof(PlayableBootstrap).GetField("profile",F).SetValue(hud,p);typeof(PlayableBootstrap).GetMethod("CreateHud",F).Invoke(hud,null);
   var root=(VisualElement)typeof(PlayableBootstrap).GetField("root",F).GetValue(hud);root.Clear();root.style.width=1280;root.style.height=900;
   string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/sentry-combat/lab-ui/",Guid.NewGuid().ToString("N"),"balance.json"));Directory.CreateDirectory(Path.GetDirectoryName(path));
   var navigation=(NativeMenuNavigation)typeof(PlayableBootstrap).GetField("menuNavigation",F).GetValue(hud);
   try{
    var store=new NativeBalanceStore(p,path);store.Copy("Sentry UI");var lab=new NativeBalanceView(root,navigation,store,()=>{});yield return null;yield return null;
    navigation.ActivateElement(lab.Page.Q<Button>("lab-section-Здания"));navigation.ActivateElement(lab.Page.Q<Button>("lab-object-Пулемётная-турель"));yield return null;
    var fields=PlayableProfileMetadata.Fields.Where(f=>f.Path.StartsWith("turret.")&&NativeBalanceFields.Editable(f)).ToArray();Assert.AreEqual(11,fields.Length);
    foreach(var field in fields)Assert.NotNull(lab.Page.Q<TextField>("lab-field-"+field.FieldName),field.Path);
    lab.Page.Q<TextField>("lab-field-turretCreditCost").value="225";yield return null;
    Assert.True(lab.Page.Q<Button>("lab-save").enabledInHierarchy,lab.Page.Q<Label>("lab-status").text);
    navigation.ActivateElement(lab.Page.Q<Button>("lab-save"));yield return null;Assert.AreEqual(225,store.Active.TurretCreditCost,lab.Page.Q<Label>("lab-status").text);
    Assert.AreEqual(225,new NativeBalanceStore(p,path).Resolve(store.SelectedVersion).TurretCreditCost);
   }finally{navigation.Dispose();UnityEngine.Object.DestroyImmediate(host);}
  }
  [UnityTest] public IEnumerator ProvingPreparationPopupOffersTurretOnEveryDefensePlotAndOnlyOrdinaryKindsOnOrdinaryPlots()
  {
   var setup=new ProvingGroundSetup(PlayableProfile.Default);var p=setup.Profile;var authority=new OfflineParticipantAuthority(setup.Configuration(),93);
   var host=new GameObject("Proving turret popup");var hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
   void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,F).SetValue(hud,value);
   object Call(string name,params object[] args)=>typeof(PlayableBootstrap).GetMethods(F).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(hud,args);
   Set("profile",p);Call("CreateHud");Set("view",authority.View("player-1"));Set("provingGround",setup);
   var root=(VisualElement)typeof(PlayableBootstrap).GetField("root",F).GetValue(hud);root.Clear();root.style.width=1280;root.style.height=800;root.style.backgroundColor=new Color(.05f,.08f,.1f);
   var layer=new VisualElement();layer.style.position=Position.Absolute;layer.style.left=layer.style.right=layer.style.top=layer.style.bottom=0;root.Add(layer);Set("provingGroundLayer",layer);
   var camera=new GameObject("Proving popup camera").AddComponent<Camera>();camera.transform.SetParent(host.transform);camera.orthographic=true;camera.orthographicSize=70;camera.transform.position=new Vector3(0,80,-60);camera.transform.LookAt(Vector3.zero);Set("cameraView",camera);
   var panel=host.GetComponent<UIDocument>().panelSettings;var texture=new RenderTexture(1280,800,24);texture.Create();panel.targetTexture=texture;
   var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/sentry-proving-popup/images"));Directory.CreateDirectory(output);
   try{
    yield return null;yield return null;int checkedDefense=0,checkedOrdinary=0;
    foreach(var site in p.AuthoredMap.Sites(p).Where(x=>x.Slots.Count>0))
    {
     foreach(var slot in site.Slots.Concat(site.DefenseSlots(p)))
     {
      Assert.True((bool)Call("OpenProvingConstruction",slot.Position),"Open site/slot "+site.Id+"/"+slot.Id);
      var popup=layer.Q<VisualElement>("proving-construction");Assert.NotNull(popup);
      var actual=popup.Query<Button>().ToList().Where(b=>b.name.StartsWith("proving-build-")).Select(b=>b.name).ToArray();
      bool defense=TerritoryRules.DefenseSlot(slot.Id);
      var expected=defense?new[]{"proving-build-SentryTurret","proving-build-RepairTower"}:new[]{"proving-build-Factory","proving-build-Refinery","proving-build-ScientificCenter","proving-build-Hangar"};
      CollectionAssert.AreEquivalent(expected,actual,"Site/slot "+site.Id+"/"+slot.Id);if(defense)checkedDefense++;else checkedOrdinary++;
      if(defense&&slot.Id==site.DefenseSlots(p)[0].Id){yield return null;yield return null;
       var previous=RenderTexture.active;RenderTexture.active=texture;var pixels=new Texture2D(1280,800,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,1280,800),0,0);pixels.Apply();RenderTexture.active=previous;File.WriteAllBytes(Path.Combine(output,"proving-menu-site-"+site.Id+".png"),pixels.EncodeToPNG());UnityEngine.Object.DestroyImmediate(pixels);
      }
     }
    }
    Assert.AreEqual(16,checkedDefense,"Both HQ and outpost defense rings on both sides");Assert.AreEqual(16,checkedOrdinary);
   }finally{
    panel.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(host);
    authority.Navigation.ReleaseCommittedHostRoot();var domain=typeof(OfflineParticipantAuthority).GetField("domain",F).GetValue(authority);domain.GetType().GetMethod("DestroyOwnedNavigation",F).Invoke(domain,null);Assert.Zero(authority.Navigation.PhysicalHostBudget.PhysicalBytes);
   }
  }
  [Test] public void SmallPadOffersBothDefenseBuildingsAndRealHudRingOpensAtDefenseSlot()
  {
   var p=PlayableProfile.Default;var site=TerritoryRules.Sites(p).First(s=>s.Kind==PlayableBuildingKind.Headquarters);
   var center=new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Headquarters,site.Position,400,1,0,0,default,siteId:site.Id);
   var view=View(p,new[]{center},sites:new[]{new TerritorySiteSnapshot(site,PlayableOwner.Player,PlayableOwner.Player,1,false,1,true)});
   var actions=OfflinePadWorldActions.Build(view,p,site.Id,1001);CollectionAssert.AreEqual(new[]{PlayableBuildingKind.SentryTurret,PlayableBuildingKind.RepairTower},actions.Select(a=>a.Command.Building).ToArray());Assert.True(actions.All(a=>a.Command.Slot==1001));
   Assert.False(OfflinePadWorldActions.Build(view,p,site.Id,1).Any(a=>TerritoryRules.Defensive(a.Command.Building)));
   var host=new GameObject("Sentry real HUD");var hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
   void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,F).SetValue(hud,value);
   void Call(string name)=>typeof(PlayableBootstrap).GetMethod(name,F).Invoke(hud,null);
   try{Set("profile",p);Call("CreateHud");Set("view",view);Set("selectedSite",site.Id);Set("selectedSlot",1001);Call("UpdateHud");
    var actual=(OfflinePadAction[])typeof(PlayableBootstrap).GetField("battleActions",F).GetValue(hud);CollectionAssert.AreEqual(new[]{PlayableBuildingKind.SentryTurret,PlayableBuildingKind.RepairTower},actual.Select(a=>a.Command.Building).ToArray());Assert.True(actual.All(a=>a.Command.Slot==1001));
   }finally{UnityEngine.Object.DestroyImmediate(host);}
  }
 }
}
