using System;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableAiScoutLivenessPolicyTests
    {
        private static bool Until(Func<bool> predicate)=>SpinWait.SpinUntil(predicate,8000);
        private static PlayableAiObservation Observation(long generation=1,bool objective=true,bool secondExplorer=false,bool moving=false)
        {
            var entities=new[]{new PlayableEntitySnapshot(8,PlayableOwner.Player,PlayableEntityKind.Explorer,new NavPoint(-8,0),100,moving,0,0,0)};
            if(secondExplorer)entities=entities.Concat(new[]{new PlayableEntitySnapshot(3,PlayableOwner.Player,PlayableEntityKind.Explorer,new NavPoint(-7,0),100,false,0,0,0)}).ToArray();
            var targets=objective?new[]{new PlayablePublicScoutObjective(2,new NavPoint(17,0),PlayablePublicScoutObjectiveRole.PossibleEnemyStart,true)}:Array.Empty<PlayablePublicScoutObjective>();
            return PlayableAiObservation.From(new PlayableSnapshot("native",9,generation,7,11,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,900,null,entities,Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,publicScoutObjectives:targets));
        }

        [Test] public void PublicTargetIsAuthoredAndIndependentOfHiddenEnemyState()
        {
            var profile=PlayableProfile.Default;var targets=TerritoryRules.PublicScoutObjectives(profile,PlayableOwner.Player);Assert.AreEqual(1,targets.Length);Assert.AreEqual(2,targets[0].SiteId);Assert.AreEqual(PlayablePublicScoutObjectiveRole.PossibleEnemyStart,targets[0].Role);Assert.True(targets[0].Reachable);
            var left=PlayableAiObservation.From(new PlayableSnapshot(profile.ProfileId,profile.Revision,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,10,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,publicScoutObjectives:targets));
            var right=PlayableAiObservation.From(new PlayableSnapshot(profile.ProfileId,profile.Revision,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,10,null,new[]{new PlayableEntitySnapshot(99,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(20,0),100,false,0,0,0)},Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,publicScoutObjectives:targets));
            Assert.AreEqual(left.PublicScoutObjectives.Single().Approach.X,right.PublicScoutObjectives.Single().Approach.X);Assert.AreEqual(left.PublicScoutObjectives.Single().Approach.Z,right.PublicScoutObjectives.Single().Approach.Z);Assert.False(typeof(PlayableAiObservation).GetProperties().Any(x=>x.Name.Contains("Vision")||x.Name.Contains("Geometry")));
        }

        [Test] public void BothAuthoredStartApproachesAvoidOccupiedHomeSlots()
        {
            var profile=PlayableProfile.Default;
            foreach(var observer in new[]{PlayableOwner.Player,PlayableOwner.Enemy})
            {
                var target=TerritoryRules.PublicScoutObjectives(profile,observer).Single();
                var home=TerritoryRules.Sites(profile).Single(site=>site.Id==target.SiteId);
                var solids=home.Slots.Take(2).Select(slot=>new NavObstacle(slot.Position.X-profile.FactoryFootprintRadius,
                    slot.Position.Z-profile.FactoryFootprintRadius,slot.Position.X+profile.FactoryFootprintRadius,
                    slot.Position.Z+profile.FactoryFootprintRadius)).ToArray();
                var geometry=new NavGeometry(profile.ArenaHalfExtent,solids,1);
                Assert.True(geometry.IsFree(target.Approach,profile.ExplorerCollisionRadius),observer+" scout approach intersects an authored build slot.");
            }
        }

        [Test] public void PolicyMovesOnlyLowestStableReadyExplorerToPublicStart()
        {
            var observation=Observation(secondExplorer:true);var policy=new PlayableAiScoutLivenessPolicy();var action=policy.TryPlan(observation);
            Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.Move,action.Kind);CollectionAssert.AreEqual(new[]{3},action.EntityIds);Assert.AreEqual(17,action.Target.X);Assert.IsNull(policy.TryPlan(observation));
        }

        [Test] public void PolicyDoesNotInventTargetOrUseMovingExplorer()
        {
            Assert.IsNull(new PlayableAiScoutLivenessPolicy().TryPlan(Observation(objective:false)));Assert.IsNull(new PlayableAiScoutLivenessPolicy().TryPlan(Observation(moving:true)));
        }

        [Test] public void RuntimeReceiptRetriesPausedMoveAndResolvesAppliedMove()
        {
            using(var runtime=new PlayableRuntime(PlayableProfile.Default,91,41,false))
            {
                Assert.True(Until(()=>runtime.Latest.Tick>1));var run=new PlayableAiDiagnosticRun(runtime,"native-flat-sandbox-u6","source:scouting-public-objectives-u6","scouting-public-objectives-u6-fixtures-v1");var policy=new PlayableAiScoutLivenessPolicy();var observation=run.Observe();var first=policy.TryPlan(observation);
                Assert.NotNull(first);Assert.AreEqual(PlayableAiDeliveryStatus.Scheduled,run.Schedule(observation,first,0));runtime.RequestPause(true);Assert.True(Until(()=>runtime.Latest.Paused));run.Pump();foreach(var record in run.Records.Where(x=>x.ActionId==first.ActionId))policy.ObserveReceipt(record);Assert.True(run.Records.Any(x=>x.ActionId==first.ActionId&&x.Status==PlayableAiDeliveryStatus.Rejected));Assert.False(policy.HasPendingObligation);
                runtime.RequestPause(false);
                Assert.True(Until(()=>!runtime.Latest.Paused),"resume");
                var retryObservation=run.Observe();var retry=policy.TryPlan(retryObservation);
                Assert.NotNull(retry,"retry action");Assert.AreNotEqual(first.ActionId,retry.ActionId);
                Assert.AreEqual(PlayableAiDeliveryStatus.Scheduled,run.Schedule(retryObservation,retry,0));
                Assert.True(Until(()=>runtime.Latest.Tick>retryObservation.Tick),"advance");
                Assert.True(Until(()=>{run.Pump();return run.Records.Any(x=>x.ActionId==retry.ActionId&&x.Status==PlayableAiDeliveryStatus.Applied&&x.ApplicationTick>0);}),String.Join(",",run.Records.Where(x=>x.ActionId==retry.ActionId).Select(x=>x.Status+":"+x.Message)));
                foreach(var record in run.Records.Where(x=>x.ActionId==retry.ActionId))policy.ObserveReceipt(record);Assert.False(policy.HasPendingObligation);
                runtime.RequestStop();Assert.True(Until(()=>runtime.IsStopped));
            }
        }

        [Test] public void ObsoleteReceiptDoesNotClearCurrentGenerationMove()
        {
            var policy=new PlayableAiScoutLivenessPolicy();var old=Observation();var oldAction=policy.TryPlan(old);var fresh=Observation(generation:2);var freshAction=policy.TryPlan(fresh);Assert.NotNull(freshAction);policy.ObserveReceipt(new PlayableAiTraceRecord(old.Identity,oldAction.ActionId,0,0,0,PlayableAiDeliveryStatus.Stopped,PlayableCommandStatus.Stopped,"obsolete",sourceIdentity:PlayableAiOpeningComposition.SourceIdentity));Assert.True(policy.HasPendingObligation);policy.ObserveReceipt(new PlayableAiTraceRecord(fresh.Identity,freshAction.ActionId,0,0,1,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"applied",sourceIdentity:PlayableAiOpeningComposition.SourceIdentity));Assert.False(policy.HasPendingObligation);
        }
    }
}
