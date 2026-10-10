using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableAiResearchLivenessPolicyTests
    {
        private static bool Until(Func<bool> predicate)=>SpinWait.SpinUntil(predicate,8000);
        private static PlayableProfile FastResearchProfile()
        {
            var data=(PlayableProfileData)typeof(PlayableProfile).GetMethod("DefaultData",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            data.startingCredits=5000;data.armyCapacity=20;data.factoryBuildSeconds=1;data.refineryBuildSeconds=1;data.scienceBuildSeconds=1;data.tankProductionSeconds=1;
            return PlayableProfile.Create(data);
        }
        private static PlayableCommand Build(long generation,long sequence,int centerId,int slot,PlayableBuildingKind kind)=>new PlayableCommand(generation,sequence,"player-1",PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:1,slotId:slot,buildingKind:kind,parentId:centerId);
        private static PlayableAiObservation Observation(long generation=1,bool researched=false,bool enemyVisible=true,int credits=900,bool chassisAvailable=true,bool ownCenter=true,bool secondCenter=false)
        {
            var research=researched?new[]{new PlayableResearchOrderSnapshot(1,PlayableResearchKind.TankChassis,8,300,1,30,true,false)}:Array.Empty<PlayableResearchOrderSnapshot>();
            var centers=new[]{new PlayableBuildingSnapshot(8,ownCenter?PlayableOwner.Player:PlayableOwner.Enemy,PlayableBuildingKind.ScientificCenter,new NavPoint(0,0),100,1,0,0,default(NavPoint),research:research)};if(secondCenter)centers=centers.Concat(new[]{new PlayableBuildingSnapshot(7,PlayableOwner.Player,PlayableBuildingKind.ScientificCenter,new NavPoint(3,0),100,1,0,0,default(NavPoint),research:research)}).ToArray();var buildings=centers.Concat(new[]{new PlayableBuildingSnapshot(3,PlayableOwner.Player,PlayableBuildingKind.Refinery,new NavPoint(1,0),100,1,0,0,default(NavPoint)),new PlayableBuildingSnapshot(4,PlayableOwner.Player,PlayableBuildingKind.Refinery,new NavPoint(2,0),100,1,0,0,default(NavPoint))}).ToArray();
            var entities=enemyVisible?new[]{new PlayableEntitySnapshot(2,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(0,1),100,false,0,0,0),new PlayableEntitySnapshot(9,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(8,1),100,false,0,0,0)}:new[]{new PlayableEntitySnapshot(2,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(0,1),100,false,0,0,0)};
            return PlayableAiObservation.From(new PlayableSnapshot("native",9,generation,7,11,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,credits,null,entities,buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,researchAvailability:new[]{new PlayableResearchAvailabilitySnapshot(PlayableResearchKind.TankChassis,300,chassisAvailable)}));
        }
        private static PlayableAiResearchReadiness Ready(PlayableAiObservation o)=>new PlayableAiResearchReadiness(o.Generation,o.Tick,o.Tick,false,1,0,0,2);

        [Test] public void ReleaseProfilePreservesSourceResearchThresholds()
        {var p=PlayableAiResearchPolicyProfile.Release;Assert.AreEqual("adaptive-strategic-ai-v1@release",p.Id);Assert.AreEqual(35,p.RescoutSeconds);Assert.AreEqual(.5,p.MaximumDanger);Assert.AreEqual(1.5,p.AdvantageRatio);Assert.AreEqual(2,p.MinimumRefineries);Assert.AreEqual(1,p.DefensiveReserveUnits);Assert.AreEqual(5,PlayableAiResearchPolicyMetadata.Fields.Length);Assert.True(PlayableAiResearchPolicyMetadata.Fields.All(f=>f.Step>0&&f.Minimum<=f.Maximum));Assert.Throws<ArgumentOutOfRangeException>(()=>new PlayableAiResearchPolicyProfile("",0,0,-1,0,0,-1));}

        [Test] public void FreshnessDoesNotTreatMissingEvidenceAsSafe()
        {var p=PlayableAiResearchPolicyProfile.Release;Assert.False(new PlayableAiResearchReadiness(1,100,-1,false,0,0,0,2).Fresh(p));Assert.True(new PlayableAiResearchReadiness(1,100,100,false,1,0,0,2).Fresh(p));Assert.False(new PlayableAiResearchReadiness(1,100,100-35*30-1,false,1,0,0,2).Fresh(p));}

        [Test] public void TrackerUsesOnlyPublishedEnemyEvidenceAndResetsGeneration()
        {var tracker=new PlayableAiResearchReadinessTracker();var visible=Observation();var intent=new PlayableAiResearchStrategicIntent(visible.Generation,visible.Tick,false,1);Assert.True(tracker.Observe(visible,intent).Fresh(PlayableAiResearchPolicyProfile.Release));var reset=Observation(generation:2,enemyVisible:false);Assert.False(tracker.Observe(reset,intent).Fresh(PlayableAiResearchPolicyProfile.Release));}

        [Test] public void MissingIntentAndIdenticalOwnerViewsCannotBecomeSafeFromHiddenState()
        {var observation=Observation(enemyVisible:false);var profile=PlayableAiResearchPolicyProfile.Release;Assert.False(new PlayableAiResearchReadinessTracker().Observe(observation,null).Fresh(profile));var intent=new PlayableAiResearchStrategicIntent(observation.Generation,observation.Tick,false,1);var left=new PlayableAiResearchReadinessTracker().Observe(observation,intent);var right=new PlayableAiResearchReadinessTracker().Observe(Observation(enemyVisible:false),intent);Assert.AreEqual(left.LastScoutTick,right.LastScoutTick);Assert.AreEqual(left.Danger,right.Danger);Assert.AreEqual(left.ForceAdvantage,right.ForceAdvantage);Assert.AreEqual(left.EnemyForce,right.EnemyForce);Assert.AreEqual(left.Fresh(profile),right.Fresh(profile));
            var properties=typeof(PlayableAiObservation).GetProperties();
            Assert.False(properties.Any(x=>x.Name.Contains("Geometry")||x.PropertyType.Name.Contains("Domain")||x.PropertyType==typeof(PlayableVision)||typeof(Delegate).IsAssignableFrom(x.PropertyType)),"No geometry, domain, live vision or delegate lookup may escape");
            Assert.AreEqual(typeof(TeamVisionSnapshot),properties.Single(x=>x.Name=="Vision").PropertyType);
            Assert.False(typeof(TeamVisionSnapshot).GetProperties().Any(x=>x.CanWrite));}

        [Test] public void EligibleObservationQueuesChassisAtLowestOwnCenter()
        {var o=Observation();var policy=new PlayableAiResearchLivenessPolicy();var action=policy.TryPlan(o,Ready(o));Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.QueueResearch,action.Kind);Assert.AreEqual(PlayableResearchKind.TankChassis,action.ResearchKind);CollectionAssert.AreEqual(new[]{8},action.EntityIds);Assert.IsNull(policy.TryPlan(o,Ready(o)));}

        [Test] public void EligibleObservationUsesTheLowestStableOwnCenter()
        {var o=Observation(secondCenter:true);var action=new PlayableAiResearchLivenessPolicy().TryPlan(o,Ready(o));Assert.NotNull(action);CollectionAssert.AreEqual(new[]{7},action.EntityIds);}

        [Test] public void EmergencyExistingResearchAndStaleGenerationBlockOrResetPolicy()
        {var policy=new PlayableAiResearchLivenessPolicy();var o=Observation();Assert.IsNull(policy.TryPlan(o,new PlayableAiResearchReadiness(o.Generation,o.Tick,o.Tick,true,1,0,0,2)));Assert.IsNull(policy.TryPlan(o,new PlayableAiResearchReadiness(o.Generation,o.Tick,o.Tick,false,0,0,0,2)));Assert.IsNull(policy.TryPlan(Observation(researched:true),Ready(Observation(researched:true))));var old=policy.TryPlan(o,Ready(o));Assert.NotNull(old);var later=Observation(generation:2);var next=policy.TryPlan(later,Ready(later));Assert.NotNull(next);policy.ObserveReceipt(new PlayableAiTraceRecord(o.Identity,old.ActionId,0,0,0,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"old",sourceIdentity:PlayableAiOpeningComposition.SourceIdentity));Assert.True(policy.HasPendingObligation);}

        [Test] public void CostAvailabilityAndForeignCenterBlockTheResearch()
        {var policy=new PlayableAiResearchLivenessPolicy();var poor=Observation(credits:299);Assert.IsNull(policy.TryPlan(poor,Ready(poor)));var unavailable=Observation(chassisAvailable:false);Assert.IsNull(policy.TryPlan(unavailable,Ready(unavailable)));var foreign=Observation(ownCenter:false);Assert.IsNull(policy.TryPlan(foreign,Ready(foreign)));}

        [Test] public void TerminalReceiptClearsPendingForRetry()
        {var policy=new PlayableAiResearchLivenessPolicy();var o=Observation();var first=policy.TryPlan(o,Ready(o));policy.ObserveReceipt(new PlayableAiTraceRecord(o.Identity,first.ActionId,0,0,0,PlayableAiDeliveryStatus.Rejected,PlayableCommandStatus.Rejected,"x",sourceIdentity:PlayableAiOpeningComposition.SourceIdentity));Assert.False(policy.HasPendingObligation);Assert.NotNull(policy.TryPlan(o,Ready(o)));}

        [Test] public void AppliedRuntimeResearchReceiptResolvesTheBoundObligation()
        {
            using(var runtime=new PlayableRuntime(FastResearchProfile(),81,31,false))
            {
                Assert.True(Until(()=>runtime.Latest.Tick>1));var first=runtime.Latest;var center=first.Sites.Single(x=>x.Site.Id==1).CenterId;
                Assert.True(runtime.TrySubmit(Build(first.Generation,1,center,1,PlayableBuildingKind.Factory)).Accepted);
                Assert.True(runtime.TrySubmit(Build(first.Generation,2,center,2,PlayableBuildingKind.Refinery)).Accepted);
                Assert.True(runtime.TrySubmit(Build(first.Generation,3,center,3,PlayableBuildingKind.Refinery)).Accepted);
                Assert.True(runtime.TrySubmit(Build(first.Generation,4,center,4,PlayableBuildingKind.ScientificCenter)).Accepted);
                Assert.True(Until(()=>runtime.Latest.Buildings.Count(x=>x.Owner==PlayableOwner.Player&&x.Phase==ConstructionPhase.Ready&&x.Kind==PlayableBuildingKind.Refinery)==2&&runtime.Latest.Buildings.Any(x=>x.Owner==PlayableOwner.Player&&x.Phase==ConstructionPhase.Ready&&x.Kind==PlayableBuildingKind.ScientificCenter)));
                var factory=runtime.Latest.Buildings.Single(x=>x.Owner==PlayableOwner.Player&&x.Kind==PlayableBuildingKind.Factory);Assert.True(runtime.TrySubmit(new PlayableCommand(runtime.Latest.Generation,5,"player-1",PlayableCommandKind.QueueTank,new[]{factory.Id})).Accepted);
                Assert.True(Until(()=>runtime.Latest.Entities.Any(x=>x.Owner==PlayableOwner.Player&&x.Kind==PlayableEntityKind.Tank)));
                var run=new PlayableAiDiagnosticRun(runtime,"native-flat-sandbox-u6","source:technology-liveness-u6","research-liveness-u6-fixtures-v1",6);var observation=run.Observe();var policy=new PlayableAiResearchLivenessPolicy();var readiness=new PlayableAiResearchReadiness(observation.Generation,observation.Tick,observation.Tick,false,1,0,0,1.25);var action=policy.TryPlan(observation,readiness);
                Assert.NotNull(action,"credits="+observation.Credits+" tanks="+observation.Entities.Count(x=>x.Owner==PlayableOwner.Player&&x.Kind==PlayableEntityKind.Tank)+" refineries="+observation.Buildings.Count(x=>x.Owner==PlayableOwner.Player&&x.Kind==PlayableBuildingKind.Refinery&&x.Phase==ConstructionPhase.Ready)+" science="+observation.Buildings.Count(x=>x.Owner==PlayableOwner.Player&&x.Kind==PlayableBuildingKind.ScientificCenter&&x.Phase==ConstructionPhase.Ready)+" availability="+observation.ResearchAvailability.Count);Assert.AreEqual(PlayableAiDeliveryStatus.Scheduled,run.Schedule(observation,action,0));runtime.RequestPause(true);Assert.True(Until(()=>runtime.Latest.Paused));run.Pump();foreach(var record in run.Records.Where(x=>x.ActionId==action.ActionId))policy.ObserveReceipt(record);Assert.True(run.Records.Any(x=>x.ActionId==action.ActionId&&x.Status==PlayableAiDeliveryStatus.Rejected));Assert.False(policy.HasPendingObligation);
                runtime.RequestPause(false);Assert.True(Until(()=>!runtime.Latest.Paused));var retryObservation=run.Observe();var retry=policy.TryPlan(retryObservation,new PlayableAiResearchReadiness(retryObservation.Generation,retryObservation.Tick,retryObservation.Tick,false,1,0,0,1.25));Assert.NotNull(retry);Assert.AreNotEqual(action.ActionId,retry.ActionId);Assert.AreEqual(PlayableAiDeliveryStatus.Scheduled,run.Schedule(retryObservation,retry,0));Assert.True(Until(()=>runtime.Latest.Tick>retryObservation.Tick));Assert.True(Until(()=>{run.Pump();return run.Records.Any(x=>x.ActionId==retry.ActionId&&x.Status==PlayableAiDeliveryStatus.Applied&&x.ApplicationTick>0);}));
                foreach(var record in run.Records.Where(x=>x.ActionId==retry.ActionId))policy.ObserveReceipt(record);
                Assert.False(policy.HasPendingObligation);Assert.True(Until(()=>runtime.Latest.Buildings.Where(x=>x.Kind==PlayableBuildingKind.ScientificCenter&&x.Owner==PlayableOwner.Player).SelectMany(x=>x.PrivateState.Research).Any(x=>x.Kind==PlayableResearchKind.TankChassis&&x.Active)));
                runtime.RequestStop();Assert.True(Until(()=>runtime.IsStopped));
            }
        }
    }
}
