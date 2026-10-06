using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableExplorerTests
    {
        private static readonly Type Domain=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private object domain;
        private PlayableProfile profile;
        private long sequence;
        private object Call(string name,params object[] args)=>Domain.GetMethod(name,Flags).Invoke(domain,args);
        private PlayableSnapshot View(PlayableOwner owner=PlayableOwner.Player)=>(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);
        private NavigationSession Nav=>(NavigationSession)Domain.GetProperty("Navigation",Flags).GetValue(domain);
        private IDictionary Units=>(IDictionary)Domain.GetField("units",Flags).GetValue(domain);
        [SetUp] public void Create(){profile=PlayableProfile.Default;domain=Activator.CreateInstance(Domain,Flags,null,new object[]{profile,1L},null);sequence=0;}
        private void Clear(){foreach(int id in Units.Keys.Cast<int>().ToArray()){Nav.Remove(id);Units.Remove(id);}}
        private int Spawn(PlayableOwner owner,PlayableEntityKind kind,double x,double z)=>(int)Call("SpawnUnit",new NavPoint(x,z),owner,kind);
        private PlayableCommandStatus Send(PlayableCommandKind kind,int id,PlayableEntityKind unitKind=PlayableEntityKind.Tank,long order=0,NavPoint target=default(NavPoint))=>(PlayableCommandStatus)Call("Apply",new PlayableCommand(1,++sequence,"player-1",kind,new[]{id},target,productionOrderId:order,unitKind:unitKind),null);
        private int Factory(int slot=1){Call("AddCredits",PlayableOwner.Player,10000d);Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,slot,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));Call("AdvanceFoundations");Call("AdvanceBuildings",100d);return View().Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory&&b.SlotId==slot).Id;}
        private PlayableBuildingPrivateState State(int id)=>View().Buildings.Single(b=>b.Id==id).PrivateState;
        private void Tick(int count){for(int i=0;i<count;i++){while(Nav.Requests.TryDequeue(out var r))Nav.Answers.TryEnqueue(new NavigationAnswer(r,new SharedFlowRouter(r.Geometry,r.Profile).FindPath(r.Start,r.Goal)));Nav.ApplyResults();Call("Step",1d/30);}}
        [Test] public void StartsHaveFreeTypedScoutAndOwnPopulation()
        {
            var p=View();var e=View(PlayableOwner.Enemy);Assert.AreEqual(1,p.Entities.Count);Assert.AreEqual(PlayableEntityKind.Explorer,p.Entities.Single().Kind);Assert.AreEqual(profile.ExplorerHealth,p.Entities.Single().Health);
            Assert.AreEqual(profile.ExplorerPopulationCost,p.Population.Living);Assert.Zero(p.Population.Reserved);Assert.AreEqual(profile.StartingCredits,p.Credits);
            Assert.AreEqual(1,e.Entities.Count(u=>u.Kind==PlayableEntityKind.Explorer));Assert.AreEqual(2,e.Entities.Count(u=>u.Kind==PlayableEntityKind.Tank));Assert.AreEqual(3,e.Entities.Count);
        }
        [Test] public void MixedQueueFreezesCostsAndCancelsOnlyAddressedOrder()
        {
            int f=Factory();int before=View().Credits;Send(PlayableCommandKind.QueueExplorer,f);Send(PlayableCommandKind.QueueTank,f);Call("AdvanceProduction",1d);
            var q=State(f).Orders;Assert.AreEqual(PlayableEntityKind.Explorer,q[0].Kind);Assert.AreEqual(profile.ExplorerProductionDurationSec,q[0].Duration);Assert.AreEqual(profile.ExplorerCreditCost,q[0].PaidCost);Assert.AreEqual(profile.ExplorerPopulationCost,View().Population.Reserved);
            Assert.True(q[0].Active);Assert.False(q[1].Active);Send(PlayableCommandKind.CancelProductionOrder,f,order:q[1].Id);Assert.AreEqual(before-profile.ExplorerCreditCost,View().Credits);Assert.AreEqual(q[0].Id,State(f).Orders.Single().Id);
            Call("AdvanceProduction",100d);Assert.IsEmpty(State(f).Orders);Assert.AreEqual(2*profile.ExplorerPopulationCost,View().Population.Living);Assert.Zero(View().Population.Reserved);Assert.AreEqual(2,View().Entities.Count(u=>u.Kind==PlayableEntityKind.Explorer));
        }
        [Test] public void RepeatCanSwitchKindsAndManualOrderTurnsItOff()
        {
            int f=Factory();Send(PlayableCommandKind.ToggleRepeatProduction,f,PlayableEntityKind.Explorer);Call("AdvanceProduction",1d);Assert.AreEqual(PlayableEntityKind.Explorer,State(f).RepeatKind);Assert.AreEqual(PlayableEntityKind.Explorer,State(f).Orders.Single().Kind);
            Send(PlayableCommandKind.ToggleRepeatProduction,f,PlayableEntityKind.Tank);Assert.True(State(f).Repeat);Assert.IsEmpty(State(f).Orders);Call("AdvanceProduction",1d);Assert.AreEqual(PlayableEntityKind.Tank,State(f).Orders.Single().Kind);
            Send(PlayableCommandKind.QueueExplorer,f);Assert.False(State(f).Repeat);Assert.AreEqual(2,State(f).Orders.Count);
        }
        [Test] public void MixedCrowdUsesPhysicalRadiiAndSpeed()
        {
            var geometry=new NavGeometry(30,Array.Empty<NavObstacle>(),1);var crowd=new NavCrowd(geometry,profile.Navigation);
            var scout=crowd.Add(1,new NavPoint(-10,-5),profile.ExplorerCollisionRadius,profile.ExplorerSpeed,profile.ExplorerTurnSpeed);var tank=crowd.Add(2,new NavPoint(-10,5));
            crowd.SetRoute(1,new NavPoint(10,-5),new[]{new NavPoint(10,-5)});crowd.SetRoute(2,new NavPoint(10,5),new[]{new NavPoint(10,5)});for(int i=0;i<30;i++)crowd.Step(1d/30);
            Assert.AreEqual(-10+profile.ExplorerSpeed,scout.Position.X,1e-8);Assert.AreEqual(-10+profile.TankSpeed,tank.Position.X,1e-8);
            Assert.True(crowd.CanPlace(new NavPoint(tank.Position.X+profile.TankCollisionRadius+profile.ExplorerCollisionRadius+.01,5),profile.ExplorerCollisionRadius));Assert.False(crowd.CanPlace(new NavPoint(tank.Position.X+profile.TankCollisionRadius+profile.ExplorerCollisionRadius-.01,5),profile.ExplorerCollisionRadius));
        }
        [Test] public void ScoutRoutesUseItsActualFootprint()
        {
            Clear();int id=Spawn(PlayableOwner.Player,PlayableEntityKind.Explorer,-10,10);Assert.True(Nav.Move(id,new NavPoint(-5,10)));Assert.True(Nav.Requests.TryDequeue(out var r));Assert.AreEqual(profile.ExplorerCollisionRadius,r.Profile.Radius);Assert.AreEqual(profile.ExplorerSpeed,r.Profile.TankSpeed);
        }
        [Test] public void MovingScoutFiresWithoutAbandoningMoveAndStopCancelsBurst()
        {
            Clear();int scout=Spawn(PlayableOwner.Player,PlayableEntityKind.Explorer,-10,0);int enemy=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-4,0);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.Move,scout,target:new NavPoint(-8.5,0)));Tick(2);
            Assert.True(Nav.Crowd.TryGet(scout,out var actor));Assert.True(actor.Moving);Assert.Greater(actor.Position.X,-10);Assert.True(View().Projectiles.Any(p=>p.Kind==PlayableEntityKind.Explorer));
            var unit=Units[scout];Assert.Greater((int)unit.GetType().GetField("BurstRemaining").GetValue(unit),0);Send(PlayableCommandKind.Stop,scout);Assert.Zero((int)unit.GetType().GetField("BurstRemaining").GetValue(unit));Assert.True(View().Projectiles.Any(),"Already emitted projectiles survive command cancellation.");
        }
        [Test] public void BurstStopsWhenTargetLeavesRange()
        {
            Clear();int scout=Spawn(PlayableOwner.Player,PlayableEntityKind.Explorer,-10,0);int enemy=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-4,0);Tick(1);var unit=Units[scout];Assert.Greater((int)unit.GetType().GetField("BurstRemaining").GetValue(unit),0);
            Nav.Remove(enemy);Units.Remove(enemy);Tick(1);Assert.Zero((int)unit.GetType().GetField("BurstRemaining").GetValue(unit));
        }
        [Test] public void PointTracerHitsNearestEnemyAndIgnoresFriend()
        {
            Clear();int near=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Explorer,-6,0);Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-2,0);Spawn(PlayableOwner.Player,PlayableEntityKind.Tank,-9,0);
            Assert.AreEqual(near,Call("FirstHit",PlayableOwner.Player,new NavPoint(-12,0),new NavPoint(-5,0),0d));
        }
        [Test] public void PhysicalBurstDamagesEnemyWithPerTracerPayload()
        {
            Clear();Spawn(PlayableOwner.Player,PlayableEntityKind.Explorer,-10,0);int enemy=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-7,0);Tick(20);
            var target=View().Entities.Single(e=>e.Id==enemy);Assert.Less(target.Health,profile.TankHealth);Assert.AreEqual(0,(profile.TankHealth-target.Health)%profile.ExplorerDamage);
        }
        [Test] public void SmallerPaidHeadCanUseCapacityThatTankCannot()
        {
            var d=(PlayableProfileData)typeof(PlayableProfile).GetMethod("DefaultData",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);d.armyCapacity=2*d.explorerPopulationCost;profile=PlayableProfile.Create(d);domain=Activator.CreateInstance(Domain,Flags,null,new object[]{profile,1L},null);
            int tank=Factory(1),scout=Factory(2);Send(PlayableCommandKind.QueueTank,tank);Send(PlayableCommandKind.QueueExplorer,scout);Call("AdvanceProduction",1d);
            Assert.False(State(tank).Orders[0].Active);Assert.True(State(scout).Orders[0].Active);Assert.AreEqual(profile.ExplorerPopulationCost,View().Population.Reserved);
        }
        [Test] public void SpreadAndLeadAreDeterministicAndTrackMovingTarget()
        {
            Clear();int scout=Spawn(PlayableOwner.Player,PlayableEntityKind.Explorer,-10,0);int enemy=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-4,0);var u=Units[scout];var v=Units[enemy];
            u.GetType().GetField("Target").SetValue(u,enemy);u.GetType().GetField("BurstRemaining").SetValue(u,profile.ExplorerBurstSize);
            v.GetType().GetField("Velocity").SetValue(v,new NavPoint(0,profile.TankSpeed));
            Call("FireTracer",u,new NavPoint(-10,0),new NavPoint(-4,0));Call("FireTracer",u,new NavPoint(-10,0),new NavPoint(-4,0));var shots=View().Projectiles;
            Assert.AreEqual(2,shots.Count);Assert.AreEqual(shots[0].Heading,shots[1].Heading);Assert.Greater(shots[0].Heading,-profile.ExplorerSpreadDeg*Math.PI/180);Assert.Less(shots[0].Heading,Math.Asin(profile.TankSpeed/profile.ExplorerProjectileSpeed)+profile.ExplorerSpreadDeg*Math.PI/180+1e-8);
        }
        [Test] public void ExplorerMetadataRejectsNonFiniteAndZeroBurst()
        {
            var d=(PlayableProfileData)typeof(PlayableProfile).GetMethod("DefaultData",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);d.explorerSpeed=double.NaN;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(d));d.explorerSpeed=profile.ExplorerSpeed;d.explorerBurstSize=0;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(d));
        }
    }
}
