using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class GroupOrderProfileLifecycleTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        private static readonly Type D=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private object domain;private NavigationSession nav;private long sequence;
        private object Call(string name,params object[] args)=>D.GetMethod(name,F).Invoke(domain,args);
        [SetUp] public void Setup(){domain=Activator.CreateInstance(D,F,null,new object[]{PlayableProfile.Default,1L,false},null);nav=(NavigationSession)D.GetProperty("Navigation",F).GetValue(domain);sequence=0;}
        private int Spawn(double x,double z,PlayableOwner owner=PlayableOwner.Player,PlayableEntityKind kind=PlayableEntityKind.Tank)=>(int)Call("SpawnUnit",new NavPoint(x,z),owner,kind);
        private PlayableCommandStatus Send(PlayableCommandKind kind,int[] ids,NavPoint goal=default(NavPoint),int target=0)
            =>(PlayableCommandStatus)Call("Apply",new PlayableCommand(1,++sequence,"player-1",kind,ids,goal,targetId:target),null);
        private void Deliver(){var geometry=(NavGeometry)D.GetProperty("Geometry",F).GetValue(domain);using(var host=new UnityHostRouteService()){var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,nav.Admission,nav.RoutePort);for(int i=0;i<80&&nav.PendingCount>0;i++){host.Service(binding,8);nav.ApplyResults();}}}
        private void Contact(int id,bool value)=>typeof(NavigationSession).GetMethod("SetFormationContact",F).Invoke(nav,new object[]{id,value,0L});
        private object Unit(int id)=>((IDictionary)D.GetField("units",F).GetValue(domain))[id];
        private static object UnitField(object unit,string name)=>unit.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(unit);
        [Test] public void OrdinaryDomainDeathDoesNotBarrierRemainingInstalledRouteOrChangeItsBaseSpeed()
        {
            int dead=Spawn(-12,-12),survivor=Spawn(-14,-12);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.Move,new[]{dead,survivor},new NavPoint(-10,-12)));
            Deliver();Assert.NotNull(nav.InstalledExecutionFor(survivor));
            var actor=nav.Crowd.Units.Single(u=>u.Id==survivor);double baseSpeed=actor.Speed;
            Call("Step",1d/30);var group=nav.GroupFor(survivor);var before=actor.Position;double progress=group.Members.Single(x=>x.Entity==survivor).Progress;
            long tick=(long)D.GetProperty("Tick",F).GetValue(domain);
            Call("Damage",dead,10000);
            Assert.False(((IDictionary)D.GetField("units",F).GetValue(domain)).Contains(dead));
            Assert.False(nav.Crowd.TryGet(dead,out _));Assert.Null(nav.GroupFor(dead));
            for(int i=0;i<30&&actor.Position.Equals(before);i++){
                Call("Step",1d/30);tick++;Assert.AreEqual(tick,(long)D.GetProperty("Tick",F).GetValue(domain));
                D.GetMethod("PlayerSnapshot",F).Invoke(domain,new object[]{tick,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19092026,PlayableOwner.Player});
            }
            Assert.NotNull(nav.InstalledExecutionFor(survivor));Assert.True(actor.Moving);
            bool positionAdvanced=!before.Equals(actor.Position);
            bool progressAdvanced=nav.GroupFor(survivor).Members.Single(x=>x.Entity==survivor).Progress>progress;
            Assert.True(positionAdvanced||progressAdvanced,"At least the authoritative position or installed route progress advances; the member cannot remain at a stalled delivery barrier.");
            Assert.AreEqual(group.GroupId,nav.GroupFor(survivor).GroupId);Assert.AreEqual(group.CommandSequence,nav.GroupFor(survivor).CommandSequence);
            Assert.AreEqual(baseSpeed,actor.Speed,"Removing a peer does not mutate the survivor's authored/base speed.");
            Assert.AreEqual(PlayableCommandKind.Move,nav.GroupFor(survivor).Kind);
            var snapshot=(PlayableSnapshot)D.GetMethod("PlayerSnapshot",F).Invoke(domain,new object[]{1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19092026,PlayableOwner.Player});
            Assert.AreEqual(PlayableMatchOutcome.Playing,(PlayableMatchOutcome)D.GetProperty("Outcome",F).GetValue(domain));
            Assert.True(snapshot.Buildings.Any(b=>b.Kind==PlayableBuildingKind.Headquarters&&b.Health>0));
        }

        [Test] public void AttackMoveContactPreservesIntentAndTargetEligibilityThenResumesInstalledOrder()
        {
            int actor=Spawn(-12,-12),enemy=Spawn(-8,-12,PlayableOwner.Enemy);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.AttackMove,new[]{actor},new NavPoint(-10,-12)));
            Deliver();Assert.NotNull(nav.InstalledExecutionFor(actor));var group=nav.GroupFor(actor);
            long contactTick=(long)D.GetProperty("Tick",F).GetValue(domain);
            for(int i=0;i<3;i++){Call("Step",1d/30);contactTick++;Assert.AreEqual(contactTick,(long)D.GetProperty("Tick",F).GetValue(domain));}
            Assert.True(nav.GroupFor(actor).Members.Single().FormationReleased,"The post-movement visibility phase records real visible contact.");
            var during=(PlayableSnapshot)D.GetMethod("PlayerSnapshot",F).Invoke(domain,new object[]{contactTick,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19092026,PlayableOwner.Player});
            Assert.True(during.Entities.Any(e=>e.Id==enemy),"The opposing unit is visible to this owner.");
            var moving=during.Entities.Single(e=>e.Id==actor);Assert.AreEqual(enemy,moving.TargetId,"Visible contact acquires the valid in-range target.");
            Assert.AreEqual(enemy,UnitField(Unit(actor),"Target"));Assert.False((bool)UnitField(Unit(actor),"ExplicitTarget"));
            Assert.True((bool)UnitField(Unit(actor),"HasAttackMove"));Assert.AreEqual(PlayableTacticalOrderKind.AttackMove,((PlayableTacticalOrderSnapshot)UnitField(Unit(actor),"CurrentOrder")).Kind);
            var projectiles=(IEnumerable)D.GetField("projectiles",F).GetValue(domain);
            Assert.True(projectiles.Cast<object>().Any(p=>(int)UnitField(p,"Owner")==actor&&(int)UnitField(p,"Target")==enemy),"A visible, in-range AttackMove target receives fire.");
            Assert.AreEqual(PlayableCommandKind.AttackMove,group.Kind);Assert.AreEqual(group.CommandSequence,nav.GroupFor(actor).CommandSequence);
            Call("Damage",enemy,10000);
            var actorState=nav.Crowd.Units.Single(u=>u.Id==actor);var deathPosition=actorState.Position;
            double deathProgress=nav.GroupFor(actor).Members.Single().Progress;
            long tick=(long)D.GetProperty("Tick",F).GetValue(domain);PlayableSnapshot resumed=null;
            bool advanced=false;
            for(int i=0;i<30&&!advanced;i++){
                Deliver();Call("Step",1d/30);tick++;
                Assert.AreEqual(tick,(long)D.GetProperty("Tick",F).GetValue(domain),"Every post-contact authority step advances exactly one tick.");
                Assert.AreEqual(PlayableMatchOutcome.Playing,(PlayableMatchOutcome)D.GetProperty("Outcome",F).GetValue(domain));
                Assert.True(nav.CaptureState().Crowd.Units.Any(u=>u.Id==actor));
                Assert.AreEqual(group.GroupId,nav.GroupFor(actor).GroupId);Assert.AreEqual(group.CommandSequence,nav.GroupFor(actor).CommandSequence);
                Assert.AreEqual(PlayableCommandKind.AttackMove,nav.GroupFor(actor).Kind);
                resumed=(PlayableSnapshot)D.GetMethod("PlayerSnapshot",F).Invoke(domain,new object[]{tick,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19092026,PlayableOwner.Player});
                Assert.True(resumed.Buildings.Any(b=>b.Kind==PlayableBuildingKind.Headquarters&&b.Health>0));
                var current=nav.GroupFor(actor).Members.Single();
                advanced=!deathPosition.Equals(actorState.Position)||current.Progress>deathProgress;
            }
            Assert.True(advanced,"The resumed actor's position or installed route progress advances within 30 authority steps.");
            Assert.AreEqual(group.GroupId,nav.GroupFor(actor).GroupId);Assert.AreEqual(group.CommandSequence,nav.GroupFor(actor).CommandSequence);
            Assert.AreEqual(PlayableCommandKind.AttackMove,nav.GroupFor(actor).Kind);Assert.NotNull(nav.InstalledExecutionFor(actor));
            Assert.False(nav.GroupFor(actor).Members.Single().FormationReleased);Assert.AreEqual(0,UnitField(Unit(actor),"Target"));Assert.False((bool)UnitField(Unit(actor),"ExplicitTarget"));
            Assert.True((bool)UnitField(Unit(actor),"HasAttackMove"));Assert.AreEqual(PlayableTacticalOrderKind.AttackMove,((PlayableTacticalOrderSnapshot)UnitField(Unit(actor),"CurrentOrder")).Kind);
            Assert.AreEqual(0,resumed.Entities.Single(e=>e.Id==actor).TargetId);
            Assert.True(actorState.Moving);
        }
        [Test] public void VisibleOutOfRangeAttackMoveKeepsRouteAndDoesNotInventFireEligibility()
        {
            int actor=Spawn(-18,-16),enemy=Spawn(-8,-12,PlayableOwner.Enemy);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.AttackMove,new[]{actor},new NavPoint(-15,-16)));
            Deliver();var group=nav.GroupFor(actor);Assert.NotNull(nav.InstalledExecutionFor(actor));
            Call("Step",1d/30);Assert.False(nav.GroupFor(actor).Members.Single().FormationReleased,"Visible contact outside weapon range does not release formation pacing.");
            var visible=(PlayableSnapshot)D.GetMethod("PlayerSnapshot",F).Invoke(domain,new object[]{1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19092026,PlayableOwner.Player});
            Assert.True(visible.Entities.Any(e=>e.Id==enemy));Assert.AreEqual(0,visible.Entities.Single(e=>e.Id==actor).TargetId);
            Assert.AreEqual(0,UnitField(Unit(actor),"Target"));Assert.False((bool)UnitField(Unit(actor),"ExplicitTarget"));
            Assert.AreEqual(0,UnitField(Unit(actor),"BurstRemaining"));Assert.True((bool)UnitField(Unit(actor),"HasAttackMove"));
            Assert.AreEqual(PlayableTacticalOrderKind.AttackMove,((PlayableTacticalOrderSnapshot)UnitField(Unit(actor),"CurrentOrder")).Kind);
            Assert.NotNull(nav.InstalledExecutionFor(actor));Assert.True(nav.Crowd.Units.Single(u=>u.Id==actor).Moving);
            Call("Damage",enemy,10000);Call("Step",1d/30);
            Assert.AreEqual(group.GroupId,nav.GroupFor(actor).GroupId);Assert.AreEqual(group.CommandSequence,nav.GroupFor(actor).CommandSequence);
            Assert.AreEqual(PlayableCommandKind.AttackMove,nav.GroupFor(actor).Kind);Assert.NotNull(nav.InstalledExecutionFor(actor));
            Assert.False(nav.GroupFor(actor).Members.Single().FormationReleased);Assert.AreEqual(0,UnitField(Unit(actor),"Target"));
            Assert.True(nav.Crowd.Units.Single(u=>u.Id==actor).Moving);
        }
    }
}
