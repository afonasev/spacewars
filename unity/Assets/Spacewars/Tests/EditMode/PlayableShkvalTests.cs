using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableShkvalTests
    {
        private static readonly Type Domain=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private object domain;private PlayableProfile p;private long sequence;
        private object Call(string name,params object[] args)=>Domain.GetMethod(name,Flags).Invoke(domain,args);
        private NavigationSession Nav=>(NavigationSession)Domain.GetProperty("Navigation",Flags).GetValue(domain);
        private IDictionary Units=>(IDictionary)Domain.GetField("units",Flags).GetValue(domain);
        private PlayableSnapshot View(PlayableOwner owner=PlayableOwner.Player)=>(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);
        [SetUp]public void Setup(){p=PlayableProfile.Default;domain=Activator.CreateInstance(Domain,Flags,null,new object[]{p,1L},null);sequence=0;Clear();}
        private void Clear(){foreach(int id in Units.Keys.Cast<int>().ToArray()){Nav.Remove(id);Units.Remove(id);}}
        private int Spawn(PlayableOwner owner,PlayableEntityKind kind,double x,double z)=>(int)Call("SpawnUnit",new NavPoint(x,z),owner,kind);
        private void Combat(int count){for(int i=0;i<count;i++){Call("RefreshVision");Call("AdvanceCombat",1d/30);Call("AdvanceProjectiles",1d/30);}}
        private PlayableCommandStatus Send(PlayableCommandKind kind,int id,PlayableEntityKind unitKind=PlayableEntityKind.Tank,long order=0,int target=0)=>(PlayableCommandStatus)Call("Apply",new PlayableCommand(1,++sequence,"player-1",kind,new[]{id},unitKind:unitKind,productionOrderId:order,targetId:target),null);
        private void Set(int id,string field,object value)=>Units[id].GetType().GetField(field).SetValue(Units[id],value);
        private T Get<T>(int id,string field)=>(T)Units[id].GetType().GetField(field).GetValue(Units[id]);
        // Rapid-fire/cell fixtures start already aimed; preparation has its own contract below.
        private void Align(int gun,int target){
            var e=View().Entities.Single(x=>x.Id==gun);var t=View().Entities.Single(x=>x.Id==target);
            ShkvalLaunchGeometry.Aim(new BallisticPoint(e.Position.X,0,e.Position.Z),e.HullHeading,e.Upgraded,e.WeaponSequence,p.ShkvalModelRadius*p.ShkvalModelScale/2.05,p.ShkvalProjectileLength,default,new BallisticPoint(t.Position.X,0,t.Position.Z),p.ShkvalArcHeight,out double yaw,out double pitch);
            Set(gun,"Turret",yaw);Set(gun,"ShkvalPitch",pitch);
        }
        private int Factory(){Call("AddCredits",PlayableOwner.Player,10000d);Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,1,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));Call("AdvanceFoundations");Call("AdvanceBuildings",100d);return View().Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory).Id;}
        [TestCase(PlayableEntityKind.Shkval)]
        [TestCase(PlayableEntityKind.Tank)]
        [TestCase(PlayableEntityKind.Explorer)]
        public void IdleWeaponTracksHullAndReturnsAfterTargetLoss(PlayableEntityKind kind)
        {
            int gun=Spawn(PlayableOwner.Player,kind,-12,0);
            Nav.Crowd.TryGet(gun,out var actor);actor.Heading=1.2;
            double speed=kind==PlayableEntityKind.Shkval?p.ShkvalTurretTurnSpeed:kind==PlayableEntityKind.Explorer?p.ExplorerTurretTurnSpeed:p.TankTurretTurnSpeed;
            Combat(1);Assert.AreEqual(Math.Min(1.2,speed/30),Get<double>(gun,"Turret"),1e-10);
            Combat(120);Assert.AreEqual(actor.Heading,Get<double>(gun,"Turret"),1e-10);
            // A new chassis heading must carry the resting weapon along with it.
            actor.Heading=-.7;Combat(120);Assert.AreEqual(actor.Heading,Get<double>(gun,"Turret"),1e-10);
            int target=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-4,0);
            Set(target,"Reload",999d);Set(gun,"Target",target);Set(gun,"ExplicitTarget",true);Set(gun,"Reload",999d);
            Combat(90);Assert.Greater(Math.Abs(Get<double>(gun,"Turret")-actor.Heading),.2,"Combat must still aim away from the hull");
            Nav.Remove(target);Units.Remove(target);
            double before=Get<double>(gun,"Turret");Combat(1);
            Assert.LessOrEqual(Math.Abs(Get<double>(gun,"Turret")-before),speed/30+1e-10,"No return snap");
            Combat(120);var rest=View().Entities.Single(e=>e.Id==gun);
            Assert.AreEqual(actor.Heading,rest.TurretHeading,1e-10);
            Assert.AreEqual(kind==PlayableEntityKind.Shkval?ShkvalLaunchGeometry.NeutralPitch:0,rest.WeaponPitch,1e-10);
            Assert.IsEmpty(View().Projectiles);
        }
        [Test]public void IdleAcquisitionRaisesBeforeFiringWithoutMovingRoot()
        {
            int gun=Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,0),target=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-4,0);
            Set(target,"Reload",999d);Set(gun,"StoppedSeconds",10d);Set(gun,"Target",target);Set(gun,"ExplicitTarget",true);Call("RefreshVision");
            var before=View().Entities.Single(e=>e.Id==gun);Combat(1);var raised=View().Entities.Single(e=>e.Id==gun);
            Assert.IsEmpty(View().Projectiles);Assert.Greater(raised.WeaponPitch,before.WeaponPitch);Assert.LessOrEqual(raised.WeaponPitch-before.WeaponPitch,p.ShkvalTurretTurnSpeed/30+1e-12);Assert.AreEqual(before.Position,raised.Position);
            Combat(4);var shot=View().Projectiles.Single();Assert.NotNull(shot.Launch);Assert.Greater(shot.Launch.Pitch,ShkvalLaunchGeometry.NeutralPitch);Assert.AreEqual(before.Position,shot.Launch.Ground.Ground);
        }
        [Test]public void FourMuzzlesCyclePerUnitIndependentlyOfOtherWeapons()
        {
            int a=Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,0),b=Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,3),target=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-4,0);
            Set(target,"Reload",999d);Set(a,"StoppedSeconds",10d);Set(b,"StoppedSeconds",10d);
            Set(a,"Target",target);Set(a,"ExplicitTarget",true);Set(b,"Reload",999d);Call("RefreshVision");
            var seen=new System.Collections.Generic.List<int>();
            for(int i=0;i<5;i++){
                Set(a,"Reload",0d);Align(a,target);Call("AdvanceIndexedCombat",1d/30);
                var shot=View().Projectiles.Where(x=>x.OwnerId==a).OrderByDescending(x=>x.Id).First();Assert.NotNull(shot.Launch);seen.Add(shot.Launch.Barrel);
                Assert.AreEqual((i+1)%4,View().Entities.Single(x=>x.Id==a).WeaponSequence);
            }
            CollectionAssert.AreEqual(new[]{0,1,2,3,0},seen);Assert.Zero(View().Entities.Single(x=>x.Id==b).WeaponSequence);
        }
        [Test]public void FourLoadedCellsConsumeRefillOldestAndRecoverWithoutAmmoGate()
        {
            int gun=Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,0),target=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-4,0);
            Set(target,"Reload",999d);Set(gun,"StoppedSeconds",10d);Set(gun,"Target",target);Set(gun,"ExplicitTarget",true);Call("RefreshVision");
            Assert.Null(View().Entities.Single(x=>x.Id==gun).ShkvalCells,"Unfired unit has four ready cells");
            double interval=p.ShkvalFireIntervalMs/1000;
            Align(gun,target);Call("AdvanceIndexedCombat",0d);
            var first=View().Entities.Single(x=>x.Id==gun).ShkvalCells;
            Assert.AreEqual(2*interval,first.Remaining(0));for(int c=1;c<4;c++)Assert.Zero(first.Remaining(c));
            Call("AdvanceIndexedCombat",interval);
            var second=View().Entities.Single(x=>x.Id==gun).ShkvalCells;
            Assert.AreEqual(interval,second.Remaining(0),1e-9,"Oldest begins feeding when two remain");Assert.AreEqual(2*interval,second.Remaining(1));Assert.Zero(second.Remaining(2));Assert.Zero(second.Remaining(3));
            Assert.AreEqual(2*interval,first.Remaining(0),"Earlier snapshot remains immutable");
            for(int shot=2;shot<12;shot++){
                Call("AdvanceIndexedCombat",interval);
                var e=View().Entities.Single(x=>x.Id==gun);
                Assert.AreEqual((shot+1)%4,e.WeaponSequence,"Visual supply never prevents a shot");
                Assert.GreaterOrEqual(Enumerable.Range(0,4).Count(c=>e.ShkvalCells.Remaining(c)==0),2);
            }
            Set(gun,"Reload",999d);Call("AdvanceIndexedCombat",interval);
            Assert.AreEqual(3,Enumerable.Range(0,4).Count(c=>View().Entities.Single(x=>x.Id==gun).ShkvalCells.Remaining(c)==0));
            Call("AdvanceIndexedCombat",interval);
            for(int c=0;c<4;c++)Assert.Zero(View().Entities.Single(x=>x.Id==gun).ShkvalCells.Remaining(c));
        }
        [Test]public void MixedProductionKeepsShkvalPayloadAndTypedRepeat()
        {
            int f=Factory(),before=View().Credits;Send(PlayableCommandKind.QueueShkval,f);Send(PlayableCommandKind.QueueExplorer,f);Send(PlayableCommandKind.QueueTank,f);Call("AdvanceProduction",1d);
            var q=View().Buildings.Single(b=>b.Id==f).PrivateState.Orders;Assert.AreEqual(3,q.Count);Assert.AreEqual(PlayableEntityKind.Shkval,q[0].Kind);Assert.AreEqual(p.ShkvalCreditCost,q[0].PaidCost);Assert.AreEqual(p.ShkvalProductionDurationSec,q[0].Duration);Assert.AreEqual(p.ShkvalPopulationCost,View().Population.Reserved);
            Send(PlayableCommandKind.CancelProductionOrder,f,order:q[1].Id);Assert.AreEqual(before-p.ShkvalCreditCost-p.TankCreditCost,View().Credits);
            Call("AdvanceProduction",100d);Call("AdvanceProduction",p.FactoryGateSeconds);Call("AdvanceProduction",FactoryReleaseRules.Duration(p,PlayableEntityKind.Shkval));var unit=View().Entities.Single(e=>e.Kind==PlayableEntityKind.Shkval);Assert.AreEqual(p.ShkvalHealth,unit.Health);Assert.AreEqual(p.ShkvalPopulationCost,View().Population.Living);Assert.True(Nav.Crowd.TryGet(unit.Id,out var body));Assert.AreEqual(p.ShkvalCollisionRadius,body.Radius);
            Send(PlayableCommandKind.ToggleRepeatProduction,f,PlayableEntityKind.Shkval);Assert.AreEqual(PlayableEntityKind.Shkval,View().Buildings.Single(b=>b.Id==f).PrivateState.RepeatKind);
        }
        [Test]public void BaselineRangeAndPreparationAreIndependentFromTank()
        {
            int id=Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,0);Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-1,0);Call("RefreshVision");
            Combat(53);Assert.IsEmpty(View().Projectiles);Combat(2);Assert.True(View().Projectiles.Any(x=>x.Kind==PlayableEntityKind.Shkval));Assert.AreEqual(13,PlayableUnitRules.Range(p,PlayableEntityKind.Shkval));Assert.AreEqual(16,p.ShkvalRange);
            Set(id,"Velocity",new NavPoint(.01,0));Combat(1);Assert.Zero(Get<double>(id,"StoppedSeconds"));
        }
        [Test]public void MissileSurvivesSourceAndFrozenEndpointDoesNotHome()
        {
            int id=Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,0);int target=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-4,0);Set(target,"Reload",999d);Combat(55);
            var shot=View().Projectiles.Single(x=>x.Kind==PlayableEntityKind.Shkval);Assert.NotNull(shot.Marker);double endpoint=shot.Marker.Position.X;
            Call("Damage",id,999);Nav.Remove(target);Units.Remove(target);Combat(2);var later=View().Projectiles.Single(x=>x.Kind==PlayableEntityKind.Shkval);Assert.AreEqual(endpoint,later.Marker.Position.X);Assert.Greater(later.Age,shot.Age);Assert.AreNotEqual(later.Position.X,shot.Position.X);
            Combat(60);Assert.IsEmpty(View().Projectiles.Where(x=>x.Kind==PlayableEntityKind.Shkval));
        }
        [Test]public void ExplicitShotOverridesFriendlyPenaltyAndAoEDamagesBoth()
        {
            int id=Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,0),target=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-5,0),friend=Spawn(PlayableOwner.Player,PlayableEntityKind.Tank,-5,1.5);
            Set(friend,"Reload",999d);Set(target,"Reload",999d);Combat(60);Assert.IsEmpty(View().Projectiles);Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.Attack,id,target:target));Combat(45);
            Assert.AreEqual(p.TankHealth-p.ShkvalDamage,Get<double>(friend,"Health"));Assert.AreEqual(p.TankHealth-p.ShkvalDamage,Get<double>(target,"Health"));
        }
        [Test]public void HiddenEntitiesCannotChangeAutomaticChoice()
        {
            int id=Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,0),target=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-4,0);Call("RefreshVision");
            int before=(int)Call("ArtilleryTarget",Units[id],new NavPoint(-12,0));Assert.AreEqual(target,before);
            Spawn(PlayableOwner.Enemy,PlayableEntityKind.Shkval,20,20);Call("AddBuilding",PlayableOwner.Enemy,PlayableBuildingKind.Factory,new NavPoint(20,20),true);Call("RefreshVision");
            Assert.AreEqual(before,Call("ArtilleryTarget",Units[id],new NavPoint(-12,0)));
        }
        [Test]public void MissileAndMarkerVisibilityAreIndependentAndImmutable()
        {
            Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,0);int target=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-4,0);Set(target,"Reload",999d);Combat(55);
            var list=(IList)Domain.GetField("projectiles",Flags).GetValue(domain);var shot=list.Cast<object>().Single(x=>x.GetType().GetField("Rocket").GetValue(x)!=null);var rocket=shot.GetType().GetField("Rocket").GetValue(shot);
            var saved=View().Projectiles.Single();rocket.GetType().GetField("Predicted").SetValue(rocket,new BallisticContact(1,new BallisticPoint(25,0,25)));
            var enemy=View(PlayableOwner.Enemy).Projectiles.Single();Assert.True(enemy.Visible);Assert.Null(enemy.Marker);Assert.NotNull(saved.Marker);Assert.Less(saved.Marker.Position.X,0);
            rocket.GetType().GetField("Predicted").SetValue(rocket,new BallisticContact(1,new BallisticPoint(-4,0,0)));shot.GetType().GetField("Position").SetValue(shot,new NavPoint(25,25));
            enemy=View(PlayableOwner.Enemy).Projectiles.Single();Assert.False(enemy.Visible);Assert.NotNull(enemy.Marker);Assert.AreEqual(0,enemy.Position.X);Assert.AreEqual(0,enemy.TargetId);Assert.AreEqual(0,enemy.OwnerId);
        }
        [Test]public void BlockedShkvalExitRetainsReservationAndCancellationRefunds()
        {
            int f=Factory();var buildings=(IDictionary)Domain.GetField("buildings",Flags).GetValue(domain);
            // Preserve the all-exits blocked contract after introducing alternatives.
            var exits=(NavPoint[])Call("FactoryExitCandidates",buildings[f],PlayableEntityKind.Shkval);
            foreach(var exit in exits)if(Nav.Crowd.CanPlace(exit,p.ShkvalCollisionRadius))Spawn(PlayableOwner.Player,PlayableEntityKind.Explorer,exit.X,exit.Z);
            Assert.False(exits.Any(exit=>Nav.Crowd.CanPlace(exit,p.ShkvalCollisionRadius)));
            int credits=View().Credits;Send(PlayableCommandKind.QueueShkval,f);Call("AdvanceProduction",100d);var q=View().Buildings.Single(b=>b.Id==f).PrivateState.Orders.Single();Assert.True(q.Active);Assert.Zero(q.Remaining);Assert.AreEqual(p.ShkvalPopulationCost,View().Population.Reserved);Assert.False(View().Entities.Any(e=>e.Kind==PlayableEntityKind.Shkval));
            Send(PlayableCommandKind.CancelProductionOrder,f,order:q.Id);Assert.AreEqual(credits,View().Credits);Assert.Zero(View().Population.Reserved);
        }
        [Test]public void ShkvalRouteUsesItsOwnRadiusAndSpeed()
        {
            int id=Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,8);Assert.True(Nav.Move(id,new NavPoint(-8,8)));Assert.True(Nav.Requests.TryDequeue(out var r));Assert.AreEqual(p.ShkvalCollisionRadius,r.Profile.Radius);Assert.AreEqual(p.ShkvalSpeed,r.Profile.TankSpeed);
        }
    }
}
