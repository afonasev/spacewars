using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableAiMissionDefenseTests
    {
        private static readonly PlayableProfile Profile=PlayableProfile.Default;
        private static bool Until(Func<bool> predicate)=>SpinWait.SpinUntil(predicate,8000);
        private static int RushSeed()
        {
            for(var seed=1;seed<10000;seed++)if(PlayableAiOpeningComposition.Initialize(seed,"player-1").Opening==PlayableAiOpening.BlindRush)return seed;
            throw new InvalidOperationException("No blind rush seed in frozen corpus.");
        }
        private static PlayableEntitySnapshot Unit(int id,PlayableOwner owner,PlayableEntityKind kind,double x,double z,int health=100)
            =>new PlayableEntitySnapshot(id,owner,kind,new NavPoint(x,z),health,false,0,0,0);
        private static PlayableAiObservation Observe(int seed,long tick,PlayableEntitySnapshot[] entities,bool visibleBuilding=false,bool publicStart=true,long generation=1)
        {
            var buildings=new[]{new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Headquarters,new NavPoint(-10,0),Profile.HeadquartersHealth,1,0,0,default(NavPoint))}
                .Concat(visibleBuilding?new[]{new PlayableBuildingSnapshot(20,PlayableOwner.Enemy,PlayableBuildingKind.Headquarters,new NavPoint(10,0),Profile.HeadquartersHealth,1,0,0,default(NavPoint),includePrivateState:false)}:Array.Empty<PlayableBuildingSnapshot>()).ToArray();
            var objectives=publicStart?new[]{new PlayablePublicScoutObjective(2,new NavPoint(10,0),PlayablePublicScoutObjectiveRole.PossibleEnemyStart,true)}:Array.Empty<PlayablePublicScoutObjective>();
            return PlayableAiObservation.From(new PlayableSnapshot(Profile.ProfileId,Profile.Revision,generation,seed,tick+1,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,entities,buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,publicScoutObjectives:objectives));
        }
        private static PlayableAiTraceRecord Receipt(PlayableAiAction action,PlayableAiDeliveryStatus status,long tick)
            =>new PlayableAiTraceRecord("fixture",action.ActionId,tick,1,tick,status,status==PlayableAiDeliveryStatus.Applied?PlayableCommandStatus.Applied:PlayableCommandStatus.Rejected,"fixture",ownerId:"player-1",sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);

        [Test] public void RushWaveUsesVisibleTargetAndExcludesArtillery()
        {
            var seed=RushSeed();var opening=PlayableAiOpeningComposition.Initialize(seed,"player-1");var policy=new PlayableAiMissionDefensePolicy(Profile);
            var observation=Observe(seed,1,new[]{Unit(2,PlayableOwner.Player,PlayableEntityKind.Explorer,-8,0,Profile.ExplorerHealth),Unit(3,PlayableOwner.Player,PlayableEntityKind.Tank,-7,0,Profile.TankHealth),Unit(4,PlayableOwner.Player,PlayableEntityKind.Shkval,-6,0,Profile.ShkvalHealth),Unit(8,PlayableOwner.Enemy,PlayableEntityKind.Tank,6,0,Profile.TankHealth)});
            var action=policy.TryPlan(observation,opening);
            Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.Attack,action.Kind);Assert.AreEqual(8,action.TargetId);
            CollectionAssert.AreEquivalent(new[]{2,3},action.EntityIds);CollectionAssert.AreEquivalent(new[]{2,3},policy.Mission.AssignedIds);
            Assert.IsFalse(policy.Mission.Deployed);
            policy.ObserveReceipt(Receipt(action,PlayableAiDeliveryStatus.Applied,2));Assert.IsTrue(policy.Mission.Deployed);
        }

        [Test] public void PublicTargetIsFogSafeAndRestoreRetainsAssignment()
        {
            var seed=RushSeed();var opening=PlayableAiOpeningComposition.Initialize(seed,"player-1");var policy=new PlayableAiMissionDefensePolicy(Profile);
            var observation=Observe(seed,1,new[]{Unit(2,PlayableOwner.Player,PlayableEntityKind.Explorer,-8,0,Profile.ExplorerHealth),Unit(3,PlayableOwner.Player,PlayableEntityKind.Tank,-7,0,Profile.TankHealth)});
            var action=policy.TryPlan(observation,opening);Assert.AreEqual(PlayableCommandKind.AttackMove,action.Kind);Assert.AreEqual(0,action.TargetId);Assert.AreEqual(10,action.Target.X);
            Assert.AreEqual(2,policy.Mission.PublicSiteId);Assert.IsFalse(policy.Mission.VisibleTarget);
            var restored=new PlayableAiMissionDefensePolicy(Profile,10);restored.Restore(policy.Mission);
            CollectionAssert.AreEquivalent(policy.Mission.AssignedIds,restored.Mission.AssignedIds);Assert.AreEqual(policy.Mission.Target.X,restored.Mission.Target.X);
            Assert.AreEqual("player-1",restored.Mission.OwnerId);
            var foreign=new PlayableAiCombatMission(1,1,1,1,0,2,new NavPoint(10,0),false,false,false,"",new[]{2},ownerId:"enemy-1");
            Assert.Throws<ArgumentException>(()=>restored.Restore(foreign));
        }

        [Test] public void RejectedDeliveryRetriesAndAppliedReinforcementStaysOnTheMission()
        {
            var seed=RushSeed();var opening=PlayableAiOpeningComposition.Initialize(seed,"player-1");var policy=new PlayableAiMissionDefensePolicy(Profile);
            var first=Observe(seed,1,new[]{Unit(2,PlayableOwner.Player,PlayableEntityKind.Explorer,-8,0,Profile.ExplorerHealth),Unit(3,PlayableOwner.Player,PlayableEntityKind.Tank,-7,0,Profile.TankHealth)});
            var action=policy.TryPlan(first,opening);policy.ObserveReceipt(Receipt(action,PlayableAiDeliveryStatus.Rejected,2));Assert.IsFalse(policy.HasPendingAction);
            var retry=policy.TryPlan(Observe(seed,2,first.Entities.ToArray()),opening);Assert.NotNull(retry);Assert.AreNotEqual(action.ActionId,retry.ActionId);
            policy.ObserveReceipt(Receipt(retry,PlayableAiDeliveryStatus.Applied,3));Assert.IsTrue(policy.Mission.Deployed);
            var wave=policy.TryPlan(Observe(seed,125,new[]{Unit(2,PlayableOwner.Player,PlayableEntityKind.Explorer,-6,0,Profile.ExplorerHealth),Unit(3,PlayableOwner.Player,PlayableEntityKind.Tank,-5,0,Profile.TankHealth),Unit(5,PlayableOwner.Player,PlayableEntityKind.Tank,-8,1,Profile.TankHealth),Unit(6,PlayableOwner.Player,PlayableEntityKind.Shkval,-8,2,Profile.ShkvalHealth)}),opening);
            Assert.NotNull(wave);Assert.AreEqual(PlayableCommandKind.AttackMove,wave.Kind);CollectionAssert.AreEqual(new[]{5},wave.EntityIds);
            policy.ObserveReceipt(Receipt(wave,PlayableAiDeliveryStatus.Applied,126));CollectionAssert.AreEquivalent(new[]{2,3,5},policy.Mission.AssignedIds);
        }

        [Test] public void VisibleCenterEmergencyPreemptsPendingAssaultWithoutRecruitingShkval()
        {
            var seed=RushSeed();var opening=PlayableAiOpeningComposition.Initialize(seed,"player-1");var policy=new PlayableAiMissionDefensePolicy(Profile);
            var initial=Observe(seed,1,new[]{Unit(2,PlayableOwner.Player,PlayableEntityKind.Explorer,-8,0,Profile.ExplorerHealth),Unit(3,PlayableOwner.Player,PlayableEntityKind.Tank,-7,0,Profile.TankHealth)});
            var assault=policy.TryPlan(initial,opening);Assert.NotNull(assault);
            var threatened=Observe(seed,2,new[]{Unit(2,PlayableOwner.Player,PlayableEntityKind.Explorer,-8,0,Profile.ExplorerHealth),Unit(3,PlayableOwner.Player,PlayableEntityKind.Tank,-7,0,Profile.TankHealth),Unit(4,PlayableOwner.Player,PlayableEntityKind.Shkval,-7,1,Profile.ShkvalHealth),Unit(8,PlayableOwner.Enemy,PlayableEntityKind.Tank,-9,1,Profile.TankHealth),Unit(9,PlayableOwner.Enemy,PlayableEntityKind.Tank,-8,2,Profile.TankHealth)});
            var defense=policy.TryPlan(threatened,opening);Assert.NotNull(defense);Assert.AreEqual(assault.ActionId,policy.PreemptedActionId);
            Assert.AreEqual(PlayableCommandKind.Attack,defense.Kind);Assert.AreEqual(8,defense.TargetId);CollectionAssert.AreEquivalent(new[]{2,3},defense.EntityIds);
            Assert.IsTrue(policy.Mission.Completed);Assert.AreEqual("emergency-preempted",policy.Mission.Reason);
        }

        [Test] public void HiddenThreatAndForeignOwnerNeverDriveAnOrder()
        {
            var seed=RushSeed();var opening=PlayableAiOpeningComposition.Initialize(seed,"player-1");var policy=new PlayableAiMissionDefensePolicy(Profile);
            var hidden=Observe(seed,1,new[]{Unit(2,PlayableOwner.Player,PlayableEntityKind.Explorer,-8,0,Profile.ExplorerHealth),Unit(3,PlayableOwner.Player,PlayableEntityKind.Tank,-7,0,Profile.TankHealth)},publicStart:false);
            Assert.IsNull(policy.TryPlan(hidden,opening));Assert.IsNull(policy.Mission);
            Assert.Throws<ArgumentException>(()=>policy.TryPlan(hidden,PlayableAiOpeningComposition.Initialize(seed,"foreign-owner")));
        }

        [Test] public void RestartClearsOldGenerationAndIgnoresItsLateReceipt()
        {
            var seed=RushSeed();var opening=PlayableAiOpeningComposition.Initialize(seed,"player-1");var policy=new PlayableAiMissionDefensePolicy(Profile);
            var units=new[]{Unit(2,PlayableOwner.Player,PlayableEntityKind.Explorer,-8,0,Profile.ExplorerHealth),Unit(3,PlayableOwner.Player,PlayableEntityKind.Tank,-7,0,Profile.TankHealth)};
            var old=policy.TryPlan(Observe(seed,1,units),opening);Assert.NotNull(old);
            Assert.IsNull(policy.TryPlan(Observe(seed,1,Array.Empty<PlayableEntitySnapshot>(),publicStart:false,generation:2),opening));
            Assert.IsNull(policy.Mission);Assert.IsFalse(policy.HasPendingAction);
            policy.ObserveReceipt(Receipt(old,PlayableAiDeliveryStatus.Applied,2));Assert.IsNull(policy.Mission);
            var current=policy.TryPlan(Observe(seed,2,units,generation:2),opening);
            Assert.NotNull(current);Assert.AreEqual(2,policy.Mission.Generation);
        }

        [Test] public void EngineAdapterAppliesPolicyMissionOrderAndReportsTerminalReceipt()
        {
            using(var runtime=new PlayableRuntime(Profile,211,72,false))
            {
                Assert.IsTrue(Until(()=>runtime.Latest.Tick>1));var adapter=new PlayableAiAdapter(runtime);var observation=adapter.Observe();
                var unit=observation.Entities.Single(x=>x.Owner==PlayableOwner.Player&&x.Kind==PlayableEntityKind.Explorer);
                var target=observation.PublicScoutObjectives.First(x=>x.Role==PlayablePublicScoutObjectiveRole.PossibleEnemyStart&&x.Reachable);
                var policy=new PlayableAiMissionDefensePolicy(Profile);
                policy.Restore(new PlayableAiCombatMission(observation.Generation,observation.Tick,observation.Tick,observation.Tick,0,target.SiteId,target.Approach,false,false,false,"",new[]{unit.Id}));
                var action=policy.TryPlan(observation,runtime.OpeningComposition);Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.AttackMove,action.Kind);
                Assert.AreEqual(PlayableAiDeliveryStatus.Scheduled,adapter.Schedule(observation,action,0));
                Assert.IsTrue(Until(()=>{adapter.Pump();return adapter.Trace.Any(x=>x.ActionId==action.ActionId&&x.Status==PlayableAiDeliveryStatus.Applied);}));
                foreach(var record in adapter.Trace.Where(x=>x.ActionId==action.ActionId))policy.ObserveReceipt(record);
                Assert.IsTrue(policy.Mission.Deployed);Assert.IsTrue(adapter.Trace.Any(x=>x.ActionId==action.ActionId&&x.ApplicationTick>0));
                runtime.RequestStop();Assert.IsTrue(Until(()=>runtime.IsStopped));
            }
        }

        [Test] public void TacticalObservationRejectsForeignFactsAndCopiesConfirmedVisibleDamage()
        {
            var ownOrder=new PlayableTacticalOrderSnapshot(2,PlayableOwner.Player,1,7,4,PlayableTacticalOrderKind.AttackMove,new NavPoint(8,2),0);
            var foreignOrder=new PlayableTacticalOrderSnapshot(8,PlayableOwner.Enemy,1,9,4,PlayableTacticalOrderKind.Attack,new NavPoint(0,0),2);
            var entities=new[]{new PlayableEntitySnapshot(2,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-5,0),100,true,0,0,0,ownOrder),new PlayableEntitySnapshot(8,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(-9,0),100,false,2,0,0,foreignOrder)};
            var buildings=new[]{new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Headquarters,new NavPoint(-10,0),Profile.HeadquartersHealth-10,1,0,0,default(NavPoint))};
            var snapshot=new PlayableSnapshot(Profile.ProfileId,Profile.Revision,1,33,5,5,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,entities,buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,ownCenterDamage:new[]{new PlayableCenterDamageSnapshot(1,PlayableOwner.Player,8,1,5,10),new PlayableCenterDamageSnapshot(1,PlayableOwner.Enemy,2,1,5,10)});
            var observation=PlayableAiObservation.From(snapshot);
            Assert.AreEqual(PlayableAiObservation.CurrentSchemaVersion,14);
            Assert.AreEqual(7,observation.Entities.Single(e=>e.Id==2).CurrentOrder.CommandSequence);
            Assert.IsNull(observation.Entities.Single(e=>e.Id==8).CurrentOrder);
            Assert.AreEqual(1,observation.OwnCenterDamage.Count);Assert.AreEqual(8,observation.OwnCenterDamage[0].AttackerId);
            var roundtrip=PlayableTacticalObservationState.Deserialize(new PlayableTacticalObservationState(1,5,new[]{ownOrder},new[]{observation.OwnCenterDamage[0]}).Serialize());
            Assert.AreEqual(7,roundtrip.Orders.Single().CommandSequence);Assert.AreEqual(8,roundtrip.Damage.Single().AttackerId);
            var withoutAttacker=new PlayableSnapshot(Profile.ProfileId,Profile.Revision,1,33,6,6,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,entities.Take(1).ToArray(),buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,ownCenterDamage:new[]{new PlayableCenterDamageSnapshot(1,PlayableOwner.Player,8,1,5,10)});
            Assert.IsEmpty(PlayableAiObservation.From(withoutAttacker).OwnCenterDamage);
        }

        [Test] public void AppliedReplacementOrderReleasesMissionWithoutInferringFromAbsentOrder()
        {
            var seed=RushSeed();var opening=PlayableAiOpeningComposition.Initialize(seed,"player-1");var policy=new PlayableAiMissionDefensePolicy(Profile);
            var initial=Observe(seed,1,new[]{Unit(2,PlayableOwner.Player,PlayableEntityKind.Explorer,-8,0,Profile.ExplorerHealth),Unit(3,PlayableOwner.Player,PlayableEntityKind.Tank,-7,0,Profile.TankHealth)});
            var action=policy.TryPlan(initial,opening);policy.ObserveReceipt(Receipt(action,PlayableAiDeliveryStatus.Applied,2));
            Assert.IsNull(policy.TryPlan(Observe(seed,2,initial.Entities.ToArray()),opening));Assert.AreEqual(2,policy.Mission.AssignedIds.Count);
            var replacement=new PlayableTacticalOrderSnapshot(2,PlayableOwner.Player,1,7,3,PlayableTacticalOrderKind.Move,new NavPoint(-2,5),0);
            var changed=new[]{new PlayableEntitySnapshot(2,PlayableOwner.Player,PlayableEntityKind.Explorer,new NavPoint(-8,0),Profile.ExplorerHealth,true,0,0,0,replacement),Unit(3,PlayableOwner.Player,PlayableEntityKind.Tank,-7,0,Profile.TankHealth)};
            Assert.IsNull(policy.TryPlan(Observe(seed,3,changed),opening));CollectionAssert.AreEqual(new[]{3},policy.Mission.AssignedIds);
        }

        [Test] public void EngineCurrentOrderTracksAppliedCommandAndStop()
        {
            using(var runtime=new PlayableRuntime(Profile,301,37,false))
            {
                Assert.IsTrue(Until(()=>runtime.Latest.Tick>1));var unit=runtime.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Explorer);
                var move=new PlayableCommand(runtime.Generation,1,"player-1",PlayableCommandKind.Move,new[]{unit.Id},new NavPoint(-6,3));
                Assert.IsTrue(runtime.TrySubmit(move).Accepted);
                Assert.IsTrue(Until(()=>runtime.ReceiptsAfter(0).Any(r=>r.Sequence==1&&r.Status==PlayableCommandStatus.Applied)));
                using(var routes=new UnityHostRouteService())Assert.IsTrue(Until(()=>{routes.Service(runtime,PlayableRuntime.MaxOutstandingCommands);return runtime.Latest.Entities.Single(e=>e.Id==unit.Id).CurrentOrder!=null;}));
                var active=runtime.Latest.Entities.Single(e=>e.Id==unit.Id).CurrentOrder;
                Assert.AreEqual(PlayableTacticalOrderKind.Move,active.Kind);Assert.AreEqual(1,active.CommandSequence);Assert.AreEqual(-6,active.Destination.X);Assert.AreEqual(runtime.Generation,active.Generation);
                // Complete the first ordinary route before the next ingress barrier.
                // The later request remains deliberately held for terminal-order testing.
                using(var routes=new UnityHostRouteService())routes.Service(runtime,PlayableRuntime.MaxOutstandingCommands);
                Assert.IsTrue(runtime.TrySubmit(new PlayableCommand(runtime.Generation,2,"player-1",PlayableCommandKind.Move,new[]{unit.Id},new NavPoint(9999,9999))).Accepted);
                Assert.IsTrue(Until(()=>runtime.ReceiptsAfter(1).Any(r=>r.Sequence==2&&r.Status==PlayableCommandStatus.InvalidTarget)));
                Assert.AreEqual(1,runtime.Latest.Entities.Single(e=>e.Id==unit.Id).CurrentOrder.CommandSequence);
                Assert.IsTrue(runtime.TrySubmit(new PlayableCommand(runtime.Generation,3,"player-1",PlayableCommandKind.Stop,new[]{unit.Id})).Accepted);
                Assert.IsTrue(Until(()=>runtime.ReceiptsAfter(2).Any(r=>r.Sequence==3&&r.Status==PlayableCommandStatus.Applied)));
                Assert.IsTrue(Until(()=>runtime.Latest.Entities.Single(e=>e.Id==unit.Id).CurrentOrder==null));
                var position=runtime.Latest.Entities.Single(e=>e.Id==unit.Id).Position;
                Assert.IsTrue(runtime.TrySubmit(new PlayableCommand(runtime.Generation,4,"player-1",PlayableCommandKind.Move,new[]{unit.Id},new NavPoint(position.X+2,position.Z))).Accepted);
                Assert.IsTrue(Until(()=>runtime.ReceiptsAfter(3).Any(r=>r.Sequence==4&&r.Status==PlayableCommandStatus.Applied)));
                Assert.IsTrue(Until(()=>runtime.Latest.Entities.Single(e=>e.Id==unit.Id).Queue?.Pending!=null));
                Assert.IsNull(runtime.Latest.Entities.Single(e=>e.Id==unit.Id).CurrentOrder,"The second route remains held at the ordinary pending barrier");
                NavigationRequest current=null;
                Assert.IsTrue(Until(()=>{NavigationRequest request;while(runtime.Requests.TryDequeue(out request))if(request.Entity==unit.Id&&request.Order>=3){current=request;return true;}return false;}));
                Assert.IsTrue(runtime.Answers.TryEnqueue(new NavigationAnswer(current,new[]{current.Goal})));
                Assert.IsTrue(Until(()=>runtime.Latest.Entities.Single(e=>e.Id==unit.Id).CurrentOrder!=null));
                Assert.IsTrue(Until(()=>runtime.Latest.Entities.Single(e=>e.Id==unit.Id).CurrentOrder==null));
                runtime.RequestStop();Assert.IsTrue(Until(()=>runtime.IsStopped));
            }
        }

        [Test] public void AuthorityCenterDamageCheckpointRejectsForeignGenerationAndHiddenAttackerStaysPrivate()
        {
            var domainType=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain");
            var domain=Activator.CreateInstance(domainType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{Profile,411L},null);
            var snapshot=(PlayableSnapshot)domainType.GetMethod("Snapshot",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(domain,new object[]{1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,41});
            var center=snapshot.Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Headquarters);
            var attacker=snapshot.Entities.First(e=>e.Owner==PlayableOwner.Enemy);
            var projectileType=domainType.GetNestedType("Projectile",BindingFlags.NonPublic);
            var projectile=Activator.CreateInstance(projectileType,true);
            void Set(string name,object value)=>projectileType.GetField(name,BindingFlags.Instance|BindingFlags.Public).SetValue(projectile,value);
            Set("Id",900);Set("Owner",attacker.Id);Set("Target",center.Id);Set("Faction",PlayableOwner.Enemy);
            Set("Kind",PlayableEntityKind.Tank);Set("Damage",10);Set("Speed",300d);Set("Radius",0d);Set("Remaining",12d);
            Set("Position",new NavPoint(center.Position.X+TerritoryRules.Radius(Profile,center.Kind)+2,center.Position.Z));
            Set("DirectionX",-1d);Set("DirectionZ",0d);
            ((IList)domainType.GetField("projectiles",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(domain)).Add(projectile);
            domainType.GetMethod("Step",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(domain,new object[]{1d/30d});
            var capture=domainType.GetMethod("CaptureTacticalObservationState",BindingFlags.Instance|BindingFlags.NonPublic);
            var state=(PlayableTacticalObservationState)capture.Invoke(domain,null);Assert.AreEqual(1,state.Damage.Count);
            Assert.AreEqual(center.Id,state.Damage[0].CenterId);Assert.AreEqual(attacker.Id,state.Damage[0].AttackerId);
            var restore=domainType.GetMethod("RestoreTacticalObservationState",BindingFlags.Instance|BindingFlags.NonPublic);
            var serialized=state.Serialize();
            restore.Invoke(domain,new object[]{PlayableTacticalObservationState.Deserialize(serialized)});
            Assert.AreEqual(state.Damage[0].Tick,((PlayableTacticalObservationState)capture.Invoke(domain,null)).Damage[0].Tick);
            Assert.Throws<FormatException>(()=>PlayableTacticalObservationState.Deserialize("tactical-observation-v1|411|0\nD|bad"));
            Assert.Throws<TargetInvocationException>(()=>restore.Invoke(domain,new object[]{new PlayableTacticalObservationState(412,state.Tick,Array.Empty<PlayableTacticalOrderSnapshot>(),Array.Empty<PlayableCenterDamageSnapshot>())}));
            Assert.Throws<TargetInvocationException>(()=>restore.Invoke(domain,new object[]{new PlayableTacticalObservationState(411,state.Tick,new[]{new PlayableTacticalOrderSnapshot(attacker.Id,PlayableOwner.Player,411,1,state.Tick,PlayableTacticalOrderKind.Attack,default(NavPoint),center.Id)},Array.Empty<PlayableCenterDamageSnapshot>())}));
            var player=(PlayableSnapshot)domainType.GetMethod("PlayerSnapshot",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(domain,new object[]{2L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,41,PlayableOwner.Player});
            Assert.IsEmpty(player.OwnCenterDamage);
        }
    }
}
