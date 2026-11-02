using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.EditMode
{
    public sealed class CombatMicroAppliedPreemptionTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        const long Gen=901;
        static readonly PlayableProfile P=PlayableProfile.Default;
        static readonly AiProfile Ai=AiProfile.Initial;
        static object Get(object value,string name)=>value.GetType().GetField(name,F).GetValue(value);
        static void Set(object value,string name,object data)=>value.GetType().GetField(name,F).SetValue(value,data);
        sealed class World:IDisposable
        {
            internal readonly PlayableDomain D;
            internal readonly OfflineMatchConfiguration Config;
            internal readonly PlayableAiOwnerLoop Loop=new PlayableAiOwnerLoop(P,PlayableAiOpeningComposition.Initialize(7,"player-1",aiProfile:Ai),Gen,null,Ai,AiDifficulty.Veteran);
            internal readonly AiCombatMicroPolicy Micro;
            internal readonly AiArmyRegistry Registry;
            internal readonly int Patient,Enemy,Tower;
            internal int Receipts;
            internal World(bool selection=false,int damage=80)
            {
                var starts=new[]{new OfflineStart("west",1,new NavPoint(-20,10),new NavPoint(-18,-12),pin:1),new OfflineStart("east",2,new NavPoint(24,21),new NavPoint(25,27),pin:2)};
                var site=new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,P);var pad=site.DefenseSlots(P).Single(x=>x.Id==1003);
                var scenario=new List<OfflineScenarioUnit>{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-16,15)),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,selection?new NavPoint(-16,20):new NavPoint(-7,15))};
                if(selection){scenario.Add(new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-15,25)));scenario.Add(new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-11,25)));}
                Config=new OfflineMatchConfiguration(P,"applied-preemption-control","flat-seed7","current-native-path",7,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("enemy-1",2,2,OfflineControl.Human)},starts,
                    new[]{site,new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,Array.Empty<TerritorySlot>())},Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},scenario:scenario.ToArray(),scenarioBuildings:new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.RepairTower,1,pad.Id,pad.Position,pad.Heading)});
                D=new PlayableDomain(P,Gen,Config);
                var initial=View();Patient=initial.Entities.Where(u=>u.Owner==PlayableOwner.Player&&u.Kind==PlayableEntityKind.Tank).OrderBy(u=>u.Id).First().Id;Enemy=initial.Entities.Single(u=>u.Owner==PlayableOwner.Enemy&&u.Kind==PlayableEntityKind.Tank).Id;
                typeof(PlayableDomain).GetMethod("Damage",F).Invoke(D,new object[]{Patient,damage});Tower=initial.Buildings.Single(b=>b.Kind==PlayableBuildingKind.RepairTower).Id;
                var ids=initial.Entities.Where(u=>u.Owner==PlayableOwner.Player&&u.Kind==PlayableEntityKind.Tank).Select(u=>u.Id).ToArray();
                Assert.AreEqual(PlayableCommandStatus.Applied,D.Apply(new PlayableCommand(Gen,1,"player-1",PlayableCommandKind.Attack,ids,selection?new NavPoint(-16,20):new NavPoint(-7,15),targetId:Enemy,origin:PlayableOrderOrigin.Ai,source:PlayableAiOpeningComposition.SourceIdentity,jobId:9,actionId:9),out _));
                var command=new PlayableCommand(Gen,2,"player-1",PlayableCommandKind.Move,new[]{Patient},new NavPoint(-20,15),parentId:Tower,origin:PlayableOrderOrigin.Ai,source:PlayableAiOpeningComposition.SourceIdentity,jobId:10,actionId:10);
                Assert.AreEqual(PlayableCommandStatus.Applied,D.Apply(command,out var message),message);
                var o=PlayableAiObservation.From(View());Registry=(AiArmyRegistry)Get(Loop,"armies");
                Assert.True(Registry.TryCreate(o,AiArmyRole.MobileDefense,AiDefensePlanner.Policy,ids,out var army,requiredMinimum:selection?3:1));
                var defense=(AiDefensePlanner)Get(Loop,"defense");var prior=new PlayableAiAction(1,o.OwnerId,P.ProfileId,P.Revision,Gen,o.SnapshotSequence,PlayableCommandKind.Attack,ids,o.Entities.Single(u=>u.Id==Enemy).Position,targetId:Enemy,seed:7,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);defense.Commit(o,prior,Registry,P);defense.ObserveReceipt(new PlayableAiTraceRecord(o.Identity,1,0,1,0,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"prior defense",o.OwnerId,prior.SourceIdentity,new AiReceiptIdentity(Gen,o.OwnerId,1,1),AiDefensePlanner.Policy,prior.Kind));
                Micro=(AiCombatMicroPolicy)Get(Loop,"combatMicro");
                var local=Action(o,1,PlayableCommandKind.Move,new NavPoint(-20,15),tower:Tower);
                Micro.Commit(new AiCombatMicroPlan{Action=local,Phase=AiCombatMicroPhase.Repair,Army=army},Gen,0);
                var actual=Action(o,10,local.Kind,local.Target,tower:Tower);var identity=new AiReceiptIdentity(Gen,o.OwnerId,1,1);
                Micro.Applied(actual,2,0,identity);
                Micro.Receipt(new PlayableAiTraceRecord(o.Identity,1,0,2,0,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"accepted repair",o.OwnerId,local.SourceIdentity,identity,AiCombatMicroPolicy.Policy,local.Kind));
                ((IList)Get(Loop,"records")).Add(new PlayableAiTraceRecord(o.Identity,10,0,2,0,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"accepted repair",o.OwnerId,local.SourceIdentity,identity,AiCombatMicroPolicy.Policy,local.Kind));
                Set(Loop,"ordinal",1L);Set(Loop,"lastCommandSequence",2L);Set(Loop,"lastPolicy",AiCombatMicroPolicy.Policy);Set(Loop,"lastActionKind",PlayableCommandKind.Move);
                Loop.ReconcileBudget(D);Loop.CombatAttention.Charge(0,Ai,AiDifficulty.Veteran,true);Assert.True(Micro.Reserves(Patient));
            }
            internal PlayableSnapshot View()=>D.PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player);
            PlayableAiAction Action(PlayableAiObservation o,long id,PlayableCommandKind kind,NavPoint point,int target=0,int tower=0)=>new PlayableAiAction(id,o.OwnerId,P.ProfileId,P.Revision,Gen,o.SnapshotSequence,kind,new[]{Patient},point,targetId:target,parentId:tower,seed:7,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            internal void Stage(int priority=2,PlayableCommandKind kind=PlayableCommandKind.Attack,int target=0,long due=0,long expires=100,bool callbackFault=false)
            {
                var o=PlayableAiObservation.From(View());var action=Action(o,100,kind,new NavPoint(-20,16),target==0?Enemy:target);
                var defense=(AiDefensePlanner)Get(Loop,"defense");long local=2;if(kind==PlayableCommandKind.Attack)defense.Commit(o,Action(o,local,kind,action.Target,action.TargetId),Registry,P);
                StageItem(o,action,AiDefensePlanner.Policy,priority,1,local,due,expires,r=>{defense.ObserveReceipt(r);Receipts++;if(callbackFault)throw new InvalidOperationException("urgent-receipt");});
            }
            internal void StageMicroCallbackFault()
            {
                var o=PlayableAiObservation.From(View());long local=Micro.PendingId==0?2:Micro.PendingId;
                var action=Action(o,local,PlayableCommandKind.Move,new NavPoint(-20,15),tower:Tower);
                Micro.Commit(new AiCombatMicroPlan{Action=action,Phase=AiCombatMicroPhase.Repair,Army=Registry.ArmyFor(Patient)},Gen,D.Tick);
                StageItem(o,Action(o,101,action.Kind,action.Target,tower:Tower),AiCombatMicroPolicy.Policy,0,2,local,50,100,r=>{Micro.Receipt(r);throw new InvalidOperationException("micro-cancel-receipt");});
            }
            void StageItem(PlayableAiObservation o,PlayableAiAction action,string policy,int priority,int ordinal,long local,long due,long expires,Action<PlayableAiTraceRecord> receipt)
            {
                Set(Loop,"ordinal",2L);var identity=new AiReceiptIdentity(Gen,o.OwnerId,2,ordinal);var claims=new[]{"recipient:"+Patient,"policy:"+policy};
                var intent=new AiIntent("applied-preemption:"+policy,policy,action,expires,0,0,claims,priority:priority);
                Assert.True(Loop.Budget.Accept(new AiBudgetEntry(intent.Id,policy,action,0,priority,D.Tick,expires,"control",claims,P.ProfileId+"@"+P.Revision,identity)));
                var item=Activator.CreateInstance(typeof(PlayableAiOwnerLoop).GetNestedType("Pending",BindingFlags.NonPublic),true);
                var values=new Dictionary<string,object>{{"Observation",o},{"ObservationIdentity",o.Identity},{"ObservationTick",o.Tick},{"SnapshotSequence",o.SnapshotSequence},{"Action",action},{"DueTick",due},{"HumanSequence",0L},{"Ordinal",2L},{"LocalActionId",local},{"PolicyName",policy},{"Intent",intent},{"Identity",identity},{"Receipt",receipt}};
                foreach(var pair in values)Set(item,pair.Key,pair.Value);((IList)Get(Loop,"pendingItems")).Add(item);
            }
            public void Dispose(){D.BeforeArmySafetyPathForTests=null;D.AfterTacticalPreparationForQa=null;D.Navigation.AfterTacticalPublishForQa=null;D.Navigation.ReleaseCommittedHostRoot();D.DestroyOwnedNavigation();}
        }
        [TestCase("paused")][TestCase("stale")][TestCase("low")][TestCase("rejected")][TestCase("attention")][TestCase("path-pending")]
        public void UnappliedUrgentReplacementRetainsAcceptedRepair(string reason)
        {
            using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();using var w=new World();
            try{
                w.Stage(priority:reason=="low"?0:2,kind:reason=="path-pending"?PlayableCommandKind.Move:PlayableCommandKind.Attack,expires:reason=="stale"?1:100);
                if(reason=="rejected")w.D.Navigation.PhysicalHostBudget.AllocationAdmission=_=>false;
                if(reason=="stale"){w.D.Step(1d/30);w.D.Step(1d/30);}
                if(reason=="attention")while(w.Loop.CombatAttention.Allows(0,Ai,AiDifficulty.Veteran,true,false))w.Loop.CombatAttention.Charge(0,Ai,AiDifficulty.Veteran,true);
                if(reason=="path-pending"){w.D.EnableAsyncObservedPaths();w.D.BeforeArmySafetyPathForTests=(a,id)=>{entered.Set();if(!release.Wait(5000))throw new TimeoutException("Path control release");};}
                long before=w.Loop.CombatAttention.Applied;w.Loop.Deliver(w.D,0,reason=="paused");
                if(reason=="path-pending")Assert.True(entered.Wait(5000),"Reach the actual ordinary asynchronous path lane");
                Assert.True(w.Micro.Reserves(w.Patient));Assert.AreEqual(10,w.View().Entities.Single(u=>u.Id==w.Patient).OrderStamp.ActionId);
                Assert.AreEqual(before,w.Loop.CombatAttention.Applied);Assert.False(w.Loop.Checkpoint.Records.Any(r=>r.ActionId==100&&r.Status==PlayableAiDeliveryStatus.Applied));
                if(reason=="attention"||reason=="path-pending")Assert.AreEqual(1,((IList)Get(w.Loop,"pendingItems")).Count);
            }finally{release.Set();w.D.Navigation.PhysicalHostBudget.AllocationAdmission=null;}
        }
        [Test] public void AppliedUrgentReplacementRevokesOnceAndChargesOnce()
        {
            using var w=new World();w.Stage();long before=w.Loop.CombatAttention.Applied;w.Loop.Deliver(w.D,0,false);
            Assert.False(w.Micro.Reserves(w.Patient));Assert.AreEqual(100,w.View().Entities.Single(u=>u.Id==w.Patient).OrderStamp.ActionId);Assert.AreEqual(1,w.Receipts);Assert.AreEqual(before+1,w.Loop.CombatAttention.Applied);
            w.Loop.Deliver(w.D,0,false);Assert.AreEqual(1,w.Receipts);Assert.AreEqual(before+1,w.Loop.CombatAttention.Applied);
        }
        [Test] public void HumanReplacementKeepsPriorityAndRevokesObservedRepair()
        {
            using var w=new World();w.Stage();Assert.AreEqual(PlayableCommandStatus.Applied,w.D.Apply(new PlayableCommand(Gen,3,"player-1",PlayableCommandKind.Hold,new[]{w.Patient},default,origin:PlayableOrderOrigin.Human),out _));
            w.Loop.Deliver(w.D,1,false);w.Micro.Observe(PlayableAiObservation.From(w.View()),P,Ai,AiDifficulty.Veteran,w.Registry);
            Assert.False(w.Micro.Reserves(w.Patient));Assert.AreEqual(PlayableOrderOrigin.Human,w.View().Entities.Single(u=>u.Id==w.Patient).OrderStamp.Origin);Assert.False(w.Loop.Checkpoint.Records.Any(r=>r.ActionId==100&&r.Status==PlayableAiDeliveryStatus.Applied));
        }
        [TestCase(false)][TestCase(true)] public void TacticalFailureOnlyRevokesAfterTheCommittedBoundary(bool committed)
        {
            using var w=new World();w.Stage();
            if(committed)w.D.Navigation.AfterTacticalPublishForQa=()=>throw new InvalidOperationException("committed-control");else w.D.AfterTacticalPreparationForQa=_=>throw new InvalidOperationException("not-committed-control");
            var error=Assert.Catch(()=>w.Loop.Deliver(w.D,0,false));
            Assert.AreEqual(!committed,w.Micro.Reserves(w.Patient));Assert.AreEqual(committed?100:10,w.View().Entities.Single(u=>u.Id==w.Patient).OrderStamp.ActionId);
            if(committed){Assert.IsInstanceOf<PlayableDomain.TacticalCommitFailure>(error);Assert.AreEqual(PlayableDomain.TacticalCommitPhase.Committed,((PlayableDomain.TacticalCommitFailure)error).Phase);Assert.AreEqual("committed-control",((PlayableDomain.TacticalCommitFailure)error).Original.Message);}
        }
        [Test] public void FailedMicroCancellationAfterAcceptedCommandClearsTheSinglePendingClaim()
        {
            using var w=new World();w.StageMicroCallbackFault();var o=PlayableAiObservation.From(w.View());
            var action=new PlayableAiAction(100,o.OwnerId,P.ProfileId,P.Revision,Gen,o.SnapshotSequence,PlayableCommandKind.Attack,new[]{w.Patient},new NavPoint(-7,15),targetId:w.Enemy,seed:7,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            Assert.AreEqual(PlayableCommandStatus.Applied,w.D.Apply(new PlayableCommand(Gen,3,o.OwnerId,action.Kind,action.CopyEntityIds(),action.Target,targetId:w.Enemy,origin:PlayableOrderOrigin.Ai,source:action.SourceIdentity,jobId:100,actionId:100),out _));
            var error=(Exception)typeof(PlayableAiOwnerLoop).GetMethod("PreemptCombatMicro",F).Invoke(w.Loop,new object[]{action});Assert.AreEqual("micro-cancel-receipt",error.Message);
            Assert.False(w.Micro.Reserves(w.Patient));Assert.IsEmpty(((IList)Get(w.Loop,"pendingItems")));Assert.IsEmpty(w.Loop.Budget.Capture().Unpaid);
            Assert.AreEqual(100,w.View().Entities.Single(u=>u.Id==w.Patient).OrderStamp.ActionId);
        }
        [Test] public void FailedAcceptedReceiptClearsCustodyAndChargesExactlyOnce()
        {
            using var w=new World();w.Stage(callbackFault:true);long before=w.Loop.CombatAttention.Applied;
            var error=Assert.Throws<InvalidOperationException>(()=>w.Loop.Deliver(w.D,0,false));Assert.AreEqual("urgent-receipt",error.Message);
            Assert.False(w.Micro.Reserves(w.Patient));Assert.AreEqual(100,w.View().Entities.Single(u=>u.Id==w.Patient).OrderStamp.ActionId);Assert.AreEqual(before+1,w.Loop.CombatAttention.Applied);Assert.AreEqual(1,w.Receipts);
            Assert.IsEmpty(((IList)Get(w.Loop,"pendingItems")));Assert.IsEmpty(w.Loop.Budget.Capture().Unpaid);w.Loop.Deliver(w.D,0,false);Assert.AreEqual(before+1,w.Loop.CombatAttention.Applied);
        }
        [Test] public void DelayedPreemptionOwnerAndWorldColdRestoreAreExactBeforeAndAfterApplied()
        {
            using var w=new World();w.Stage(due:15);byte[] owner=WorldWire.Pack(w.Loop.WriteState);var clone=w.Loop.CreateReviewClone(0);CollectionAssert.AreEqual(owner,WorldWire.Pack(clone.WriteState));
            byte[] bytes=w.D.CaptureWorldBytes(7,"applied-preemption-control");var restored=PlayableDomain.RestoreConfiguredWorldBytes(bytes,P,7,"applied-preemption-control",w.Config);
            try{
                CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(7,"applied-preemption-control"));w.Loop.Deliver(w.D,0,false);clone.Deliver(restored,0,false);Assert.True(w.Micro.Reserves(w.Patient));
                for(int tick=0;tick<15;tick++){w.D.Step(1d/30);restored.Step(1d/30);}
                w.Loop.Deliver(w.D,0,false);clone.Deliver(restored,0,false);Assert.False(w.Micro.Reserves(w.Patient));
                CollectionAssert.AreEqual(WorldWire.Pack(w.Loop.WriteState),WorldWire.Pack(clone.WriteState));CollectionAssert.AreEqual(w.D.CaptureWorldBytes(7,"applied-preemption-control"),restored.CaptureWorldBytes(7,"applied-preemption-control"));
                CollectionAssert.AreEqual(WorldWire.Pack(w.Loop.WriteState),WorldWire.Pack(w.Loop.CreateReviewClone(15).WriteState));
            }finally{restored.Navigation.ReleaseCommittedHostRoot();restored.DestroyOwnedNavigation();}
        }
        [Test] public void SelectedUrgentDefenseDoesNotEraseRepairBeforeItsDelayedOrderIsApplied()
        {
            using var w=new World(true);
            var host=new CertifiedRouteKernel();
            try{for(int n=0;n<128&&w.D.Navigation.PendingCount>0;n++){host.Pump(new PlayableRouteBinding(w.D.Geometry,P,w.D.Navigation.Admission,w.D.Navigation.RoutePort),4);w.D.Navigation.ApplyResults();}}
            finally{host.Clear();}
            Assert.AreEqual(0,w.D.Navigation.PendingCount);Assert.AreEqual(PlayableTacticalOrderKind.Move,w.View().Entities.Single(u=>u.Id==w.Patient).CurrentOrder?.Kind,"The actual Repair must replace the prior Attack before testing a new defense proposal");
            var opening=Get(w.Loop,"openingExecutor");Set(opening,"phase",PlayableAiOpeningPhase.Complete);Set(opening,"initialForceFulfilled",true);foreach(var key in new[]{"income","factory","force","pressure"})((HashSet<string>)Get(opening,"completed")).Add(key);
            Set(w.Loop,"lastDecisionTick",-100L);
            var view=w.D.PlayerSnapshotForArmyAi(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player,true,w.Loop.ArmyState);w.Loop.Review(view,0);
            Assert.True(w.Loop.Checkpoint.Records.Any(r=>r.Policy==AiDefensePlanner.Policy&&r.Status==PlayableAiDeliveryStatus.Scheduled),"The ordinary owner must actually select urgent defense");Assert.True(w.Micro.Reserves(w.Patient));
            w.Loop.Deliver(w.D,0,true);Assert.True(w.Micro.Reserves(w.Patient));Assert.AreEqual(10,w.View().Entities.Single(u=>u.Id==w.Patient).OrderStamp.ActionId);
        }
        [TestCase(false)][TestCase(true)]
        public void RetiredRepairOwnerAndNativeWorldColdRestoreStayExactAcrossCustodyAndReadiness(bool healed)
        {
            using var w=new World(damage:healed?10:80);
            byte[] before=WorldWire.Pack(w.Loop.WriteState);CollectionAssert.AreEqual(before,WorldWire.Pack(w.Loop.CreateReviewClone(0).WriteState));
            w.Registry.Disband(w.Registry.ArmyFor(w.Patient));
            ((AiDefensePlanner)Get(w.Loop,"defense")).ReconcileRegistry(w.Registry);
            w.Micro.Observe(PlayableAiObservation.From(w.View()),P,Ai,AiDifficulty.Veteran,w.Registry);Assert.True(w.Micro.Reserves(w.Patient));
            byte[] saved=w.D.CaptureWorldBytes(7,"applied-preemption-control");var restored=PlayableDomain.RestoreConfiguredWorldBytes(saved,P,7,"applied-preemption-control",w.Config);
            var clone=w.Loop.CreateReviewClone(0);var micro=(AiCombatMicroPolicy)Get(clone,"combatMicro");var registry=(AiArmyRegistry)Get(clone,"armies");
            try{
                CollectionAssert.AreEqual(saved,restored.CaptureWorldBytes(7,"applied-preemption-control"));
                for(int tick=0;tick<15;tick++){
                    var hot=PlayableAiObservation.From(w.View());var cold=PlayableAiObservation.From(restored.PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player));
                    w.Micro.Observe(hot,P,Ai,AiDifficulty.Veteran,w.Registry);micro.Observe(cold,P,Ai,AiDifficulty.Veteran,registry);
                    Assert.IsNull(w.Micro.Plan(hot,P,Ai,AiDifficulty.Veteran,w.Registry));Assert.IsNull(micro.Plan(cold,P,Ai,AiDifficulty.Veteran,registry));
                    Assert.AreEqual(!healed,w.Micro.Reserves(w.Patient));Assert.AreEqual(!healed,micro.Reserves(w.Patient));
                    CollectionAssert.AreEqual(WorldWire.Pack(w.Loop.WriteState),WorldWire.Pack(clone.WriteState));
                    w.D.Step(1d/30);restored.Step(1d/30);
                    CollectionAssert.AreEqual(w.D.CaptureWorldBytes(7,"applied-preemption-control"),restored.CaptureWorldBytes(7,"applied-preemption-control"));
                }
                CollectionAssert.AreEqual(WorldWire.Pack(w.Loop.WriteState),WorldWire.Pack(w.Loop.CreateReviewClone(15).WriteState));
            }finally{restored.Navigation.ReleaseCommittedHostRoot();restored.DestroyOwnedNavigation();}
        }
    }
}
