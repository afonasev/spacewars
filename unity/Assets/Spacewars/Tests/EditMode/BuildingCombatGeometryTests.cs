using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
 public sealed class BuildingCombatGeometryTests
 {
  [TestCase(PlayableBuildingKind.Headquarters,false)][TestCase(PlayableBuildingKind.Factory,false)][TestCase(PlayableBuildingKind.Mine,false)][TestCase(PlayableBuildingKind.Outpost,false)][TestCase(PlayableBuildingKind.ScientificCenter,false)][TestCase(PlayableBuildingKind.ScientificCenter,true)][TestCase(PlayableBuildingKind.Refinery,false)][TestCase(PlayableBuildingKind.Refinery,true)][TestCase(PlayableBuildingKind.Hangar,false)]
  public void EveryAuthoredBuildingHasARealRoofAndStableNavigationRadius(PlayableBuildingKind kind,bool upgraded)
  {
   var p=PlayableProfile.Default;double nav=TerritoryRules.Radius(p,kind);var pose=BuildingCombatGeometry.Pose(p,kind,upgraded,default,0);
   Assert.True(pose.Segment(new BallisticPoint(0,30,0),new BallisticPoint(0,-2,0),.06,out double t,out var hit));Assert.That(t,Is.InRange(0d,1d));
   var surface=pose.Closest(hit);Assert.Less(Math.Sqrt(Math.Pow(hit.X-surface.X,2)+Math.Pow(hit.Y-surface.Y,2)+Math.Pow(hit.Z-surface.Z,2)),.061);
   Assert.AreEqual(nav,TerritoryRules.Radius(p,kind));
  }
  [Test] public void EmptyHaloDoesNotHitAndFacadeContactIsInsideTheOldNavigationCylinder()
  {
   var p=PlayableProfile.Default;var pose=BuildingCombatGeometry.Pose(p,PlayableBuildingKind.Headquarters,false,default,0);
   Assert.False(pose.Segment(new BallisticPoint(1.9,1.9,-4),new BallisticPoint(1.9,1.9,4),.06,out _,out _));Assert.False(pose.Overlaps(new BallisticPoint(1.9,1.9,0),.06));
   Assert.True(pose.Segment(new BallisticPoint(-6,1,0),new BallisticPoint(6,1,0),0,out double thin,out var hit));Assert.That(hit.X,Is.InRange(-1.8,-.8));
   Assert.True(pose.Segment(new BallisticPoint(-6,1,0),new BallisticPoint(6,1,0),.3,out double thick,out _));Assert.Less(thick,thin);
   var body=new BallisticBody(1,PlayableOwner.Enemy,default,default,p.HeadquartersFootprintRadius,2.5,100,true,combat:pose);
   var flight=new BallisticFlight(new BallisticPoint(1.9,1.9,-4),new BallisticPoint(1.9,1.9,4),0,12);
   Assert.Zero(PlayableBallistics.FirstContact(flight,.06,Array.Empty<NavObstacle>(),1.74,new[]{body},0).Value.BuildingId);
  }
  [Test] public void MovingFactoryGateAndHangarIrisExposeTheActualOpening()
  {
   var p=PlayableProfile.Default;var closed=BuildingCombatGeometry.Pose(p,PlayableBuildingKind.Factory,false,default,0);var open=BuildingCombatGeometry.Pose(p,PlayableBuildingKind.Factory,false,default,0,opening:1);
   var a=new BallisticPoint(0,.7,-6);var b=new BallisticPoint(0,.7,0);Assert.True(closed.Segment(a,b,.01,out double before,out _));bool after=open.Segment(a,b,.01,out double later,out _);Assert.True(!after||later>before+.02,"Raised gate cannot leave its old closed contact plane");
   closed=BuildingCombatGeometry.Pose(p,PlayableBuildingKind.Hangar,false,default,0);open=BuildingCombatGeometry.Pose(p,PlayableBuildingKind.Hangar,false,default,0,opening:1);
   a=new BallisticPoint(0,10,0);b=new BallisticPoint(0,.6*BuildingCombatGeometry.ModelScale(p,PlayableBuildingKind.Hangar)+BuildingCombatGeometry.ApronLift,0);
   Assert.True(closed.Segment(a,b,.01,out _,out _));Assert.False(open.Segment(a,b,.01,out _,out _),"Open iris leaves the shaft open above the lowered platform");
  }
  static readonly Type Domain=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
  const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;object domain;
  object Call(string name,params object[] args)=>Domain.GetMethod(name,Flags).Invoke(domain,args);
  NavigationSession Nav=>(NavigationSession)Domain.GetProperty("Navigation",Flags).GetValue(domain);
  PlayableSnapshot View()=>(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player);
  void Dispose(object d){if(d==null)return;var nav=(NavigationSession)Domain.GetProperty("Navigation",Flags).GetValue(d);nav.ReleaseCommittedHostRoot();nav.DestroyNavigationAuthority();}
  [TearDown] public void Cleanup(){Dispose(domain);domain=null;}
  int SetupDomain()
  {
   domain=Activator.CreateInstance(Domain,Flags,null,new object[]{PlayableProfile.Default,1L},null);var units=(IDictionary)Domain.GetField("units",Flags).GetValue(domain);foreach(int id in units.Keys.Cast<int>().ToArray()){Nav.Remove(id);units.Remove(id);}
   Call("SpawnUnit",new NavPoint(-6,0),PlayableOwner.Player,PlayableEntityKind.Tank);Call("AddBuilding",PlayableOwner.Enemy,PlayableBuildingKind.Headquarters,default(NavPoint),true);Call("RefreshVision");return View().Buildings.Single(b=>b.Owner==PlayableOwner.Enemy&&b.Position.Equals(default(NavPoint))).Id;
  }
  object Inject(PlayableEntityKind kind,int target)
  {
   var p=PlayableProfile.Default;var type=Domain.GetNestedType("Projectile",BindingFlags.NonPublic);var shot=Activator.CreateInstance(type);void Set(string n,object v)=>type.GetField(n).SetValue(shot,v);
   Set("Id",101);Set("TermsRevision",p.Revision);Set("Owner",View().Entities.Single().Id);Set("Target",target);Set("Faction",PlayableOwner.Player);Set("Kind",kind);Set("Position",new NavPoint(-3,0));Set("Height",1d);Set("Speed",15d);Set("Remaining",10d);Set("Damage",1d);Set("DirectionX",1d);Set("Radius",kind==PlayableEntityKind.Explorer?0d:.06);
   if(kind==PlayableEntityKind.Explorer||kind==PlayableEntityKind.Hawk)Set("Spatial",true);
   if(kind==PlayableEntityKind.Shkval)
   {
    var rt=Domain.GetNestedType("Rocket",BindingFlags.NonPublic);var rocket=Activator.CreateInstance(rt);void R(string n,object v)=>rt.GetField(n).SetValue(rocket,v);
    var f=new BallisticFlight(new BallisticPoint(-3,1,0),new BallisticPoint(3,1,0),0,15);R("Flight",f);R("DirectDamage",1d);R("Predicted",new BallisticContact(.3,new BallisticPoint(-1.3,1,0),target));R("Radius",p.ShkvalProjectileRadius);R("BlastRadius",p.ShkvalBlastRadius);R("MarkerStartRadius",p.ShkvalMarkerStartRadius);R("MarkerOpacity",p.ShkvalMarkerOpacity);R("BuildingHeight",p.ShkvalBuildingCollisionHeight);R("BuildingTarget",true);Set("Rocket",rocket);
   }
   ((IList)Domain.GetField("projectiles",Flags).GetValue(domain)).Add(shot);Domain.GetField("nextProjectile",Flags).SetValue(domain,102);return shot;
  }
  [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Explorer)][TestCase(PlayableEntityKind.Shkval)][TestCase(PlayableEntityKind.Hawk)]
  public void EachProjectileFamilyUsesTheSameBuildingSurfaceAndKeepsNavigation(PlayableEntityKind kind)
  {
   int target=SetupDomain();double health=View().Buildings.Single(b=>b.Id==target).Health;long revision=View().Geometry.Revision;Inject(kind,target);Call("AdvanceProjectiles",.2d);var view=View();Assert.AreEqual(health-1,view.Buildings.Single(b=>b.Id==target).Health);Assert.IsEmpty(view.Projectiles);Assert.AreEqual(revision,view.Geometry.Revision);
   BallisticPoint hit=kind==PlayableEntityKind.Shkval?view.Impacts.Single().Point:view.Sounds.Single(s=>s.Kind==PlayableSoundKind.Impact&&s.Contact!=null).Contact.Impact.Value;
   Assert.That(hit.X,Is.InRange(-1.8,-.8));var pose=BuildingCombatGeometry.Pose(PlayableProfile.Default,PlayableBuildingKind.Headquarters,false,default,0);var closest=pose.Closest(hit);Assert.Less(Math.Abs(hit.X-closest.X)+Math.Abs(hit.Y-closest.Y)+Math.Abs(hit.Z-closest.Z),.0001);
  }
  [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Explorer)][TestCase(PlayableEntityKind.Shkval)][TestCase(PlayableEntityKind.Hawk)]
  public void EachProjectileFamilyPassesEmptySpaceInsideTheOldBuildingCylinder(PlayableEntityKind kind)
  {
   int target=SetupDomain();double health=View().Buildings.Single(b=>b.Id==target).Health;var shot=Inject(kind,target);var type=shot.GetType();
   type.GetField("Position").SetValue(shot,new NavPoint(1.9,-3));type.GetField("Height").SetValue(shot,1.9d);type.GetField("DirectionX").SetValue(shot,0d);type.GetField("DirectionZ").SetValue(shot,1d);
   var rocket=type.GetField("Rocket").GetValue(shot);if(rocket!=null)rocket.GetType().GetField("Flight").SetValue(rocket,new BallisticFlight(new BallisticPoint(1.9,1.9,-3),new BallisticPoint(1.9,1.9,3),0,15));
   Call("AdvanceProjectiles",.2d);Assert.AreEqual(health,View().Buildings.Single(b=>b.Id==target).Health);Assert.AreEqual(1,View().Projectiles.Count,"Empty halo must not consume the projectile");
  }
  [Test] public void ARealBuildingShotHasNoForecastEvenAfterItsTargetDisappears()
  {
   int target=SetupDomain();var units=(IDictionary)Domain.GetField("units",Flags).GetValue(domain);int source=units.Keys.Cast<int>().Single();Nav.Remove(source);units.Remove(source);
   source=(int)Call("SpawnUnit",new NavPoint(-6,0),PlayableOwner.Player,PlayableEntityKind.Shkval);
   var unit=units[source];unit.GetType().GetField("Target").SetValue(unit,target);unit.GetType().GetField("ExplicitTarget").SetValue(unit,true);
   for(int i=0;i<60&&!View().Projectiles.Any();i++){Call("RefreshVision");Call("AdvanceCombat",1d/30);}
   Assert.IsTrue(View().Projectiles.Any(p=>p.Kind==PlayableEntityKind.Shkval));Assert.IsNull(View().Projectiles.Single().Marker);
   ((IDictionary)Domain.GetField("buildings",Flags).GetValue(domain)).Remove(target);Call("RefreshVision");Assert.IsNull(View().Projectiles.Single().Marker);
  }
  [Test] public void BuildingForecastClassificationSurvivesCurrentSaveRestore()
  {
   domain=Activator.CreateInstance(Domain,Flags,null,new object[]{PlayableProfile.Default,1L},null);var units=(IDictionary)Domain.GetField("units",Flags).GetValue(domain);foreach(int id in units.Keys.Cast<int>().ToArray()){Nav.Remove(id);units.Remove(id);}
   var enemy=PlayableProfile.Default.Headquarters(PlayableOwner.Enemy);Call("SpawnUnit",new NavPoint(enemy.X-4,enemy.Z),PlayableOwner.Player,PlayableEntityKind.Shkval);Call("RefreshVision");int target=View().Buildings.Single(b=>b.Owner==PlayableOwner.Enemy&&b.Kind==PlayableBuildingKind.Headquarters).Id;
   Inject(PlayableEntityKind.Shkval,target);Assert.IsNull(View().Projectiles.Single().Marker);
   const string source="native-domain-world-v1:c77962dd:source:96a32f63:ai-release-c569e3a03045";var bytes=(byte[])Call("CaptureWorldBytes",7,source);object restored=null;
   try{restored=Domain.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,PlayableProfile.Default,7,source});var view=(PlayableSnapshot)Domain.GetMethod("PlayerSnapshot",Flags).Invoke(restored,new object[]{1L,RuntimeStatus.Running,true,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player});Assert.IsNull(view.Projectiles.Single().Marker);
    byte[] Capture(object d)=>(byte[])Domain.GetMethod("CaptureWorldBytes",Flags).Invoke(d,new object[]{7,source});
    CollectionAssert.AreEqual(bytes,Capture(restored),"Current in-flight classification must round trip byte for byte");
    for(int i=0;i<3;i++){Call("AdvanceProjectiles",1d/30);Domain.GetMethod("AdvanceProjectiles",Flags).Invoke(restored,new object[]{1d/30});CollectionAssert.AreEqual(Capture(domain),Capture(restored),"Restored projectile suffix must remain exact");}
   }
   finally{Dispose(restored);}
  }
 }
}
