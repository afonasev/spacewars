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
        private int Factory(){Call("AddCredits",PlayableOwner.Player,10000d);Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,1,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));Call("AdvanceFoundations");Call("AdvanceBuildings",100d);return View().Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory).Id;}
        [Test]public void MixedProductionKeepsShkvalPayloadAndTypedRepeat()
        {
            int f=Factory(),before=View().Credits;Send(PlayableCommandKind.QueueShkval,f);Send(PlayableCommandKind.QueueExplorer,f);Send(PlayableCommandKind.QueueTank,f);Call("AdvanceProduction",1d);
            var q=View().Buildings.Single(b=>b.Id==f).PrivateState.Orders;Assert.AreEqual(3,q.Count);Assert.AreEqual(PlayableEntityKind.Shkval,q[0].Kind);Assert.AreEqual(p.ShkvalCreditCost,q[0].PaidCost);Assert.AreEqual(p.ShkvalProductionDurationSec,q[0].Duration);Assert.AreEqual(p.ShkvalPopulationCost,View().Population.Reserved);
            Send(PlayableCommandKind.CancelProductionOrder,f,order:q[1].Id);Assert.AreEqual(before-p.ShkvalCreditCost-p.TankCreditCost,View().Credits);
            Call("AdvanceProduction",100d);var unit=View().Entities.Single(e=>e.Kind==PlayableEntityKind.Shkval);Assert.AreEqual(p.ShkvalHealth,unit.Health);Assert.AreEqual(p.ShkvalPopulationCost,View().Population.Living);Assert.True(Nav.Crowd.TryGet(unit.Id,out var body));Assert.AreEqual(p.ShkvalCollisionRadius,body.Radius);
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
            Assert.AreEqual(p.TankHealth-p.ShkvalDamage,Get<int>(friend,"Health"));Assert.AreEqual(p.TankHealth-p.ShkvalDamage,Get<int>(target,"Health"));
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
