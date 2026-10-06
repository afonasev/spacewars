using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableAiAdapterTests
    {
        private static bool Until(Func<bool> predicate)=>SpinWait.SpinUntil(predicate,3000);
        private static PlayableProfile FastFactoryProfile()
        {
            var data=(PlayableProfileData)typeof(PlayableProfile).GetMethod("DefaultData",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            data.factoryBuildSeconds=1;data.tankProductionSeconds=30;data.startingCredits=1000;
            return PlayableProfile.Create(data);
        }
        private static PlayableAiAction Move(PlayableAiObservation observation,long id=1)=>new PlayableAiAction(id,"player-1",observation.ProfileId,observation.ProfileRevision,observation.Generation,observation.SnapshotSequence,PlayableCommandKind.Move,observation.Entities.Where(e=>e.Owner==PlayableOwner.Player).Select(e=>e.Id).Take(1).ToArray(),new NavPoint(-6,3));

        [Test] public void ObservationCopiesPublishedViewWithoutTrustedGeometryOrEnemyPrivateState()
        {
            var research=new[]{new PlayableResearchOrderSnapshot(4,PlayableResearchKind.TankChassis,2,300,4,30,true,false)};
            var own=new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.ScientificCenter,new NavPoint(0,0),100,1,0,0,default(NavPoint),research:research);
            var enemy=new PlayableBuildingSnapshot(2,PlayableOwner.Enemy,PlayableBuildingKind.ScientificCenter,new NavPoint(3,0),100,1,0,0,default(NavPoint),includePrivateState:false);
            var snapshot=new PlayableSnapshot("native",9,4,7,8,9,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,500,null,Array.Empty<PlayableEntitySnapshot>(),new[]{own,enemy},Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null);
            var observation=PlayableAiObservation.From(snapshot);
            Assert.AreEqual("observation-14:native@9:player-1:4:8:9:7",observation.Identity);
            Assert.AreEqual(1,observation.Buildings.Single(b=>b.Owner==PlayableOwner.Player).PrivateState.Research.Count);
            Assert.IsNull(observation.Buildings.Single(b=>b.Owner==PlayableOwner.Enemy).PrivateState);
            Assert.False(typeof(PlayableAiObservation).GetProperties().Any(p=>p.Name.Contains("Geometry")));
        }

        [Test] public void AdapterBindsDelayedActionAndRecordsAdmissionAndTerminalReceipt()
        {
            using(var runtime=new PlayableRuntime(PlayableProfile.Default,17,8,false))
            {
                Assert.True(Until(()=>runtime.Latest.Tick>1));var observation=PlayableAiObservation.From(runtime.Latest);var adapter=new PlayableAiAdapter(runtime);
                Assert.AreEqual(PlayableAiDeliveryStatus.Scheduled,adapter.Schedule(observation,Move(observation),0));adapter.Pump();
                Assert.True(Until(()=>runtime.ReceiptsAfter(0).Any(r=>r.Sequence==1)));adapter.Pump();
                Assert.True(adapter.Trace.Any(r=>r.ActionId==1&&r.Status==PlayableAiDeliveryStatus.Accepted));
                Assert.True(adapter.Trace.Any(r=>r.ActionId==1&&r.Status==PlayableAiDeliveryStatus.Applied));
                runtime.RequestStop();Assert.True(Until(()=>runtime.IsStopped));
            }
        }

        [Test] public void StalePauseAndRestartActionsNeverApply()
        {
            using(var runtime=new PlayableRuntime(PlayableProfile.Default,21,9,false))
            {
                Assert.True(Until(()=>runtime.Latest.Tick>1));var observation=PlayableAiObservation.From(runtime.Latest);var adapter=new PlayableAiAdapter(runtime);
                var wrongProfile=new PlayableAiAction(1,"player-1",observation.ProfileId,observation.ProfileRevision+1,observation.Generation,observation.SnapshotSequence,PlayableCommandKind.Move);
                Assert.AreEqual(PlayableAiDeliveryStatus.Stale,adapter.Schedule(observation,wrongProfile,0));
                runtime.RequestPause(true);Assert.True(Until(()=>runtime.Latest.Paused));Assert.AreEqual(PlayableAiDeliveryStatus.Scheduled,adapter.Schedule(observation,Move(observation,2),0));adapter.Pump();
                Assert.True(adapter.Trace.Any(r=>r.ActionId==2&&r.Status==PlayableAiDeliveryStatus.Rejected));runtime.RequestPause(false);
                runtime.RequestStop();Assert.True(Until(()=>runtime.IsStopped));
            }
            using(var fresh=new PlayableRuntime(PlayableProfile.Default,22,9,false))
            {
                Assert.True(Until(()=>fresh.Latest.Tick>1));var observation=PlayableAiObservation.From(fresh.Latest);var adapter=new PlayableAiAdapter(fresh);
                var old=new PlayableAiAction(3,"player-1",observation.ProfileId,observation.ProfileRevision,21,observation.SnapshotSequence,PlayableCommandKind.Move);
                Assert.AreEqual(PlayableAiDeliveryStatus.Stale,adapter.Schedule(observation,old,0));fresh.RequestStop();Assert.True(Until(()=>fresh.IsStopped));
            }
        }

        [Test] public void EquivalentInvalidDecisionRecordsAreDeterministic()
        {
            var snapshot=new PlayableSnapshot("native",9,4,7,8,9,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null);
            var observation=PlayableAiObservation.From(snapshot);using(var runtime=new PlayableRuntime(PlayableProfile.Default,4,7,false))
            {
                var first=new PlayableAiAdapter(runtime);var second=new PlayableAiAdapter(runtime);
                var invalid=new PlayableAiAction(7,"enemy-1",observation.ProfileId,observation.ProfileRevision,observation.Generation,observation.SnapshotSequence,PlayableCommandKind.Move);
                Assert.AreEqual(first.Schedule(observation,invalid,0),second.Schedule(observation,invalid,0));
                Assert.AreEqual(first.Trace.Single().ObservationIdentity,second.Trace.Single().ObservationIdentity);Assert.AreEqual(first.Trace.Single().Status,second.Trace.Single().Status);
                runtime.RequestStop();Assert.True(Until(()=>runtime.IsStopped));
            }
        }

        [Test] public void DiagnosticRunFreezesScenarioSourceAndInitialObservationIdentity()
        {
            using(var runtime=new PlayableRuntime(PlayableProfile.Default,31,11,false))
            {
                Assert.True(Until(()=>runtime.Latest.Tick>1));var run=new PlayableAiDiagnosticRun(runtime,"sandbox-u6","source:dc888524");
                Assert.AreEqual("sandbox-u6",run.Identity.ScenarioId);Assert.AreEqual("source:dc888524",run.Identity.SourceIdentity);
                Assert.AreEqual(run.Identity.ObservationIdentity,run.Observe().Identity);
                runtime.RequestStop();Assert.True(Until(()=>runtime.IsStopped));
            }
        }

        [Test] public void EvaluatorFixtureBankRejectsForeignIdentityWithoutMutatingTheEngine()
        {
            using(var runtime=new PlayableRuntime(PlayableProfile.Default,41,12,false))
            {
                Assert.True(Until(()=>runtime.Latest.Tick>1));var run=new PlayableAiDiagnosticRun(runtime,"native-flat-sandbox-u6","source:dc888524","adapter-u6-fixtures-v1");var before=runtime.Latest.Tick;
                var foreign=PlayableAiObservation.From(new PlayableSnapshot("foreign",9,41,12,1,before,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));
                var action=new PlayableAiAction(101,"player-1",foreign.ProfileId,foreign.ProfileRevision,foreign.Generation,foreign.SnapshotSequence,PlayableCommandKind.Move);
                Assert.AreEqual(PlayableAiDeliveryStatus.Stale,run.Schedule(foreign,action,1));Assert.AreEqual(PlayableAiDeliveryStatus.Stale,run.Records.Single().Status);Assert.Zero(run.Records.Single().CommandSequence);
                Assert.GreaterOrEqual(runtime.Latest.Tick,before);runtime.RequestStop();Assert.True(Until(()=>runtime.IsStopped));
            }
        }

        [Test] public void FixedFixturesCoverDelayedDeliveryCancellationPauseRestartAndLiveness()
        {
            using(var runtime=new PlayableRuntime(PlayableProfile.Default,42,13,false))
            {
                Assert.True(Until(()=>runtime.Latest.Tick>1));var adapter=new PlayableAiAdapter(runtime);var legal=adapter.Observe();
                Assert.AreEqual(PlayableAiDeliveryStatus.Scheduled,adapter.Schedule(legal,Move(legal,201),2));
                var cancelled=adapter.Observe();Assert.AreEqual(PlayableAiDeliveryStatus.Scheduled,adapter.Schedule(cancelled,Move(cancelled,202),5));Assert.AreEqual(PlayableAiDeliveryStatus.Cancelled,adapter.Cancel(202));
                Assert.True(Until(()=>runtime.Latest.Tick>=legal.Tick+2));adapter.Pump();Assert.True(Until(()=>runtime.ReceiptsAfter(0).Any(r=>r.Sequence==1)));adapter.Pump();
                Assert.True(adapter.Trace.Any(r=>r.ActionId==201&&r.Status==PlayableAiDeliveryStatus.Applied&&r.ApplicationTick>=legal.Tick+2));Assert.False(adapter.Trace.Any(r=>r.ActionId==202&&r.Status==PlayableAiDeliveryStatus.Applied));
                var paused=adapter.Observe();Assert.AreEqual(PlayableAiDeliveryStatus.Scheduled,adapter.Schedule(paused,Move(paused,203),0));runtime.RequestPause(true);Assert.True(Until(()=>runtime.Latest.Paused));adapter.Pump();Assert.True(adapter.Trace.Any(r=>r.ActionId==203&&r.Status==PlayableAiDeliveryStatus.Rejected));
                runtime.RequestPause(false);runtime.RequestStop();Assert.True(Until(()=>runtime.IsStopped));adapter.Pump();Assert.True(adapter.Trace.All(r=>r.ActionId!=203||r.Status!=PlayableAiDeliveryStatus.Applied));
            }
        }

        [Test] public void TimelineCheckpointIsSerializableDiagnosticTraceNotPlayerReplay()
        {
            using(var runtime=new PlayableRuntime(PlayableProfile.Default,43,14,false))
            {
                Assert.True(Until(()=>runtime.Latest.Tick>1));var run=new PlayableAiDiagnosticRun(runtime,"native-flat-sandbox-u6","source:dc888524","adapter-u6-fixtures-v1");var observation=run.Observe();
                Assert.AreEqual(PlayableAiDeliveryStatus.Scheduled,run.Schedule(observation,Move(observation,301),0));Assert.True(Until(()=>runtime.Latest.Tick>observation.Tick));run.Pump();Assert.True(Until(()=>runtime.ReceiptsAfter(0).Any(r=>r.Sequence==1)));run.Pump();
                var checkpoint=run.CaptureTimeline();var reloaded=new PlayableAiDiagnosticTimeline(checkpoint.Identity,checkpoint.Records);
                Assert.True(checkpoint.SemanticallyEquals(reloaded));Assert.True(checkpoint.Records.Any(r=>r.ActionId==301&&r.Status==PlayableAiDeliveryStatus.Applied&&r.ApplicationTick>0));
                runtime.RequestStop();Assert.True(Until(()=>runtime.IsStopped));
            }
        }

        [Test] public void EconomicPolicyUsesAdapterReceiptBeforeAdvancingTheDurablePriority()
        {
            using(var runtime=new PlayableRuntime(PlayableProfile.Default,51,15,false))
            {
                Assert.True(Until(()=>runtime.Latest.Tick>1));var run=new PlayableAiDiagnosticRun(runtime,"native-flat-sandbox-u6","source:0f24351a","economic-liveness-u6-fixtures-v1");var policy=new PlayableAiEconomicLivenessPolicy();var observation=run.Observe();
                var factory=policy.TryPlan(observation);Assert.NotNull(factory);Assert.AreEqual(PlayableCommandKind.BuildAt,factory.Kind);Assert.AreEqual(PlayableBuildingKind.Factory,factory.BuildingKind);
                Assert.AreEqual(PlayableAiDeliveryStatus.Scheduled,run.Schedule(observation,factory,0));Assert.True(Until(()=>runtime.Latest.Tick>observation.Tick));run.Pump();Assert.True(Until(()=>runtime.ReceiptsAfter(0).Any(r=>r.Sequence==1)));run.Pump();
                foreach(var record in run.Records.Where(x=>x.ActionId==factory.ActionId))policy.ObserveReceipt(record);Assert.True(Until(()=>runtime.Latest.Buildings.Any(x=>x.Kind==PlayableBuildingKind.Factory&&x.Owner==PlayableOwner.Player)));
                Assert.False(policy.HasPendingObligation);var refinery=policy.TryPlan(run.Observe());Assert.NotNull(refinery);Assert.AreEqual(PlayableBuildingKind.Refinery,refinery.BuildingKind);Assert.AreEqual("economic-liveness-u6-fixtures-v1",run.Identity.FixtureBankIdentity);
                runtime.RequestStop();Assert.True(Until(()=>runtime.IsStopped));
            }
        }

        [Test] public void EconomicPolicyRetriesOnlyAfterRejectedOrStoppedReceipts()
        {
            using(var runtime=new PlayableRuntime(PlayableProfile.Default,52,16,false))
            {
                Assert.True(Until(()=>runtime.Latest.Tick>1));var adapter=new PlayableAiAdapter(runtime);var policy=new PlayableAiEconomicLivenessPolicy();var observation=adapter.Observe();var first=policy.TryPlan(observation);
                Assert.AreEqual(PlayableAiDeliveryStatus.Scheduled,adapter.Schedule(observation,first,0));runtime.RequestPause(true);Assert.True(Until(()=>runtime.Latest.Paused));adapter.Pump();
                foreach(var record in adapter.Trace.Where(x=>x.ActionId==first.ActionId))policy.ObserveReceipt(record);
                Assert.False(policy.HasPendingObligation);runtime.RequestPause(false);Assert.True(Until(()=>!runtime.Latest.Paused));var retryObservation=adapter.Observe();var retry=policy.TryPlan(retryObservation);Assert.NotNull(retry);Assert.AreNotEqual(first.ActionId,retry.ActionId);Assert.AreEqual(PlayableBuildingKind.Factory,retry.BuildingKind);
                Assert.AreEqual(PlayableAiDeliveryStatus.Scheduled,adapter.Schedule(retryObservation,retry,0));runtime.RequestStop();Assert.True(Until(()=>runtime.IsStopped));adapter.Pump();
                foreach(var record in adapter.Trace.Where(x=>x.ActionId==retry.ActionId))policy.ObserveReceipt(record);
                Assert.True(adapter.Trace.Any(x=>x.ActionId==retry.ActionId&&x.Status==PlayableAiDeliveryStatus.Stopped));Assert.False(policy.HasPendingObligation);
            }
        }

        [Test] public void EconomicPolicyFallsBackToAnOwnedCapturedMineWithoutEnemyState()
        {
            var profile=PlayableProfile.Default;var sites=TerritoryRules.Sites(profile);var buildings=new[]{
                new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Headquarters,sites[0].Position,100,1,0,0,default(NavPoint),siteId:1),
                new PlayableBuildingSnapshot(2,PlayableOwner.Player,PlayableBuildingKind.Factory,sites[0].Slots[0].Position,100,1,0,0,default(NavPoint),siteId:1,slotId:1,parentId:1),
                new PlayableBuildingSnapshot(3,PlayableOwner.Player,PlayableBuildingKind.Refinery,sites[0].Slots[1].Position,100,1,0,0,default(NavPoint),siteId:1,slotId:2,parentId:1)};
            var snapshots=new[]{new TerritorySiteSnapshot(sites[0],PlayableOwner.Player,PlayableOwner.Player,1,false,1,true),new TerritorySiteSnapshot(sites[3],PlayableOwner.Player,PlayableOwner.Player,1,false,0,false)};
            var observation=PlayableAiObservation.From(new PlayableSnapshot(profile.ProfileId,profile.Revision,53,17,1,2,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,9999,null,Array.Empty<PlayableEntitySnapshot>(),buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,snapshots));
            var action=new PlayableAiEconomicLivenessPolicy().TryPlan(observation);
            Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.BuildAt,action.Kind);Assert.AreEqual(PlayableBuildingKind.Mine,action.BuildingKind);Assert.AreEqual(4,action.SiteId);Assert.Zero(action.SlotId);Assert.Zero(action.ParentId);
        }

        [Test] public void ProductionPolicyEmitsOneTankActionForTheLowestOwnedIdleFactory()
        {
            var profile=PlayableProfile.Default;var buildings=new[]{
                new PlayableBuildingSnapshot(8,PlayableOwner.Enemy,PlayableBuildingKind.Factory,new NavPoint(2,0),250,1,0,0,default(NavPoint),includePrivateState:false),
                new PlayableBuildingSnapshot(4,PlayableOwner.Player,PlayableBuildingKind.Factory,new NavPoint(0,0),250,1,0,0,default(NavPoint)),
                new PlayableBuildingSnapshot(6,PlayableOwner.Player,PlayableBuildingKind.Factory,new NavPoint(1,0),250,1,0,0,default(NavPoint))};
            var observation=PlayableAiObservation.From(new PlayableSnapshot(profile.ProfileId,profile.Revision,61,18,4,9,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,9999,null,Array.Empty<PlayableEntitySnapshot>(),buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));
            var policy=new PlayableAiProductionLivenessPolicy();var action=policy.TryPlan(observation);
            Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.QueueTank,action.Kind);CollectionAssert.AreEqual(new[]{4},action.EntityIds);Assert.AreEqual(PlayableEntityKind.Tank,action.UnitKind);Assert.IsNull(policy.TryPlan(observation));
        }

        [Test] public void ProductionPolicyWaitsForObservedTankCredits()
        {
            var profile=PlayableProfile.Default;
            var factory=new PlayableBuildingSnapshot(4,PlayableOwner.Player,PlayableBuildingKind.Factory,new NavPoint(0,0),250,1,0,0,default(NavPoint));
            PlayableAiObservation Observe(int credits)=>PlayableAiObservation.From(new PlayableSnapshot(profile.ProfileId,profile.Revision,61,18,4,9,
                RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,credits,null,Array.Empty<PlayableEntitySnapshot>(),new[]{factory},
                Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));
            var policy=new PlayableAiProductionLivenessPolicy(profile:profile);
            Assert.IsNull(policy.TryPlan(Observe(profile.TankCreditCost-1)));
            Assert.False(policy.HasPendingObligation);
            Assert.AreEqual(PlayableCommandKind.QueueTank,policy.TryPlan(Observe(profile.TankCreditCost))?.Kind);
        }

        [Test] public void ProductionPolicyDoesNotInspectForeignOrBusyFactoryQueues()
        {
            var profile=PlayableProfile.Default;var order=new[]{new PlayableProductionOrderSnapshot(1,PlayableEntityKind.Tank,150,3,15,15,false)};var buildings=new[]{
                new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Factory,new NavPoint(0,0),250,1,1,0,default(NavPoint),orders:order),
                new PlayableBuildingSnapshot(2,PlayableOwner.Enemy,PlayableBuildingKind.Factory,new NavPoint(1,0),250,1,0,0,default(NavPoint),includePrivateState:false)};
            var observation=PlayableAiObservation.From(new PlayableSnapshot(profile.ProfileId,profile.Revision,62,19,4,9,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,9999,null,Array.Empty<PlayableEntitySnapshot>(),buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));
            Assert.IsNull(new PlayableAiProductionLivenessPolicy().TryPlan(observation));
        }

        [Test] public void ProductionPolicyUsesAppliedReceiptBeforeTheNextEligibleObservation()
        {
            using(var runtime=new PlayableRuntime(FastFactoryProfile(),63,20,false))
            {
                Assert.True(runtime.TrySubmit(new PlayableCommand(63,1,"player-1",PlayableCommandKind.BuildFactory,Array.Empty<int>())).Accepted);
                Assert.True(Until(()=>runtime.Latest.Buildings.Any(x=>x.Owner==PlayableOwner.Player&&x.Kind==PlayableBuildingKind.Factory&&x.Phase==ConstructionPhase.Ready)));
                var run=new PlayableAiDiagnosticRun(runtime,"native-flat-sandbox-u6","source:production-liveness-u6","production-liveness-u6-fixtures-v1",2);var policy=new PlayableAiProductionLivenessPolicy();var observation=run.Observe();var action=policy.TryPlan(observation);
                Assert.NotNull(action);Assert.AreEqual(PlayableAiDeliveryStatus.Scheduled,run.Schedule(observation,action,0));Assert.True(Until(()=>runtime.Latest.Tick>observation.Tick));var applied=Until(()=>{run.Pump();return run.Records.Any(x=>x.ActionId==action.ActionId&&x.Status==PlayableAiDeliveryStatus.Applied&&x.ApplicationTick>0);});Assert.True(applied,String.Join(",",run.Records.Where(x=>x.ActionId==action.ActionId).Select(x=>x.Status+":"+x.Message)));
                foreach(var record in run.Records.Where(x=>x.ActionId==action.ActionId))policy.ObserveReceipt(record);
                Assert.True(run.Records.Any(x=>x.ActionId==action.ActionId&&x.Status==PlayableAiDeliveryStatus.Applied&&x.ApplicationTick>0));Assert.False(policy.HasPendingObligation);Assert.True(Until(()=>runtime.Latest.Buildings.Single(x=>x.Id==action.EntityIds.Single()).PrivateState.QueueCount>0));Assert.AreEqual("production-liveness-u6-fixtures-v1",run.Identity.FixtureBankIdentity);
                runtime.RequestStop();Assert.True(Until(()=>runtime.IsStopped));
            }
        }

        [Test] public void ProductionPolicyRetriesOnlyAfterTerminalNonAppliedReceiptAndGenerationChange()
        {
            var profile=PlayableProfile.Default;var factory=new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Factory,default(NavPoint),250,1,0,0,default(NavPoint));
            var firstObservation=PlayableAiObservation.From(new PlayableSnapshot(profile.ProfileId,profile.Revision,64,21,1,2,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,9999,null,Array.Empty<PlayableEntitySnapshot>(),new[]{factory},Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));
            var policy=new PlayableAiProductionLivenessPolicy();var first=policy.TryPlan(firstObservation);policy.ObserveReceipt(new PlayableAiTraceRecord(firstObservation.Identity,first.ActionId,0,0,0,PlayableAiDeliveryStatus.Rejected,PlayableCommandStatus.Rejected,"paused",sourceIdentity:PlayableAiOpeningComposition.SourceIdentity));
            Assert.False(policy.HasPendingObligation);var retry=policy.TryPlan(firstObservation);Assert.NotNull(retry);Assert.AreNotEqual(first.ActionId,retry.ActionId);
            var freshObservation=PlayableAiObservation.From(new PlayableSnapshot(profile.ProfileId,profile.Revision,65,21,2,3,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,9999,null,Array.Empty<PlayableEntitySnapshot>(),new[]{factory},Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));
            var fresh=policy.TryPlan(freshObservation);Assert.NotNull(fresh);Assert.AreNotEqual(retry.ActionId,fresh.ActionId);Assert.True(policy.HasPendingObligation);
            policy.ObserveReceipt(new PlayableAiTraceRecord(firstObservation.Identity,retry.ActionId,0,0,0,PlayableAiDeliveryStatus.Stopped,PlayableCommandStatus.Stopped,"obsolete",sourceIdentity:PlayableAiOpeningComposition.SourceIdentity));Assert.True(policy.HasPendingObligation);
            policy.ObserveReceipt(new PlayableAiTraceRecord(freshObservation.Identity,fresh.ActionId,0,0,1,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"applied",sourceIdentity:PlayableAiOpeningComposition.SourceIdentity));Assert.False(policy.HasPendingObligation);
        }

        [Test] public void ProductionPolicyClearsEachTerminalNonAppliedDelivery()
        {
            var profile=PlayableProfile.Default;var factory=new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Factory,default(NavPoint),250,1,0,0,default(NavPoint));
            var observation=PlayableAiObservation.From(new PlayableSnapshot(profile.ProfileId,profile.Revision,66,22,1,2,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,9999,null,Array.Empty<PlayableEntitySnapshot>(),new[]{factory},Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));
            foreach(var status in new[]{PlayableAiDeliveryStatus.Rejected,PlayableAiDeliveryStatus.Stale,PlayableAiDeliveryStatus.Cancelled,PlayableAiDeliveryStatus.Stopped,PlayableAiDeliveryStatus.InvalidAction,PlayableAiDeliveryStatus.InvalidOwner})
            {
                var policy=new PlayableAiProductionLivenessPolicy();var action=policy.TryPlan(observation);policy.ObserveReceipt(new PlayableAiTraceRecord(observation.Identity,action.ActionId,0,0,0,status,null,status==PlayableAiDeliveryStatus.Rejected?"paused":"terminal",sourceIdentity:PlayableAiOpeningComposition.SourceIdentity));Assert.False(policy.HasPendingObligation,status.ToString());Assert.NotNull(policy.TryPlan(observation),status.ToString());
            }
        }
    }
}
