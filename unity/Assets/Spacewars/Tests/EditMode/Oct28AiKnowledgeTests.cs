using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NativeAiInitiativeTests
    {
        private static PlayableAiObservation Oct28Objectives(long tick,bool ambiguous=false,bool fullEmpty=false,bool completed=false,bool pending=false)
        {
            var original=ObjectiveObservation(tick);var point=new NavPoint(8,0);
            var units=original.Entities.Select(u=>{
                if(!completed)return u;
                var position=new NavPoint(8,(u.Id-2)*2);
                var stamp=new PlayableOrderStamp(u.Id,u.Owner,71,1,1,10,PlayableOrderOrigin.Ai,PlayableCommandKind.AttackMove,PlayableAiOpeningComposition.SourceIdentity,1,1);
                var completion=new PlayableSpatialCompletion(stamp,1,u.Id,new NavLocation(point,NavLocation.FlatSurface),new NavLocation(position,NavLocation.FlatSurface),11,tick);
                var queue=pending?new PlayableUnitQueueSnapshot(null,new PlayableQueuedOrderSnapshot(2,2,2,2,PlayableCommandKind.AttackMove,new NavLocation(point,NavLocation.FlatSurface),0,77,2),Array.Empty<PlayableQueuedOrderSnapshot>(),null,true):null;
                return new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,position,100,false,0,Math.PI/2,Math.PI/2,navigationOutcome:NavigationOutcome.Arrived,orderStamp:stamp,completion:completion,queue:queue);
            }).ToArray();
            var objectives=original.PublicScoutObjectives.Concat(ambiguous?new[]{new PlayablePublicScoutObjective(78,new NavPoint(16,0),PlayablePublicScoutObjectiveRole.PossibleEnemyStart,true)}:Array.Empty<PlayablePublicScoutObjective>()).ToArray();
            var routes=units.SelectMany(u=>objectives.Select(g=>new PlayableRouteProof(u.Id,u.Owner,71,tick,1,u.Position,P.TankCollisionRadius,PlayableRouteTargetKind.PublicObjective,g.SiteId,g.Approach,g.Approach,new[]{g.Approach}))).ToArray();
            var vision=new PlayableVision(0,50,50,1,1);vision.Refresh(fullEmpty?new[]{new VisionSource(point,4)}:Array.Empty<VisionSource>(),Array.Empty<KnownBuilding>());
            return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,original.Seed,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units,original.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot(),publicScoutObjectives:objectives,routeProofs:routes,intelEnvelopes:new[]{new AiIntelEnvelope(77,new[]{new VisionSource(point,2)})}));
        }
        [Test] public void Oct28AmbiguousPublicStartsRequireScoutingBeforeArmyPressure()
        {
            var planner=new AiArmyPlanner("player-1",71);var action=Plan(planner,Registry(),Oct28Objectives(10,ambiguous:true));
            Assert.False(action?.Kind==PlayableCommandKind.AttackMove,"Two unknown possible starts are scout objectives, not confirmed army targets");
        }
        [Test] public void Oct28FullyCoveredEmptyStartReleasesActiveAttackMission()
        {
            var planner=new AiArmyPlanner("player-1",71);var registry=Registry();var initial=Oct28Objectives(10);var action=Plan(planner,registry,initial);Assert.NotNull(action);planner.Commit(initial,action,registry);planner.ObserveReceipt(Receipt(action,PlayableAiDeliveryStatus.Applied,11));
            var seen=Oct28Objectives(12,fullEmpty:true);Assert.True(seen.Intel.Areas.Single().Full);planner.Observe(seen,registry,AiProfile.Initial);
            Assert.False(planner.Active,"Genuine full empty-area coverage must release a pointless attack without waiting for a stall deadline");
        }
        [Test] public void Oct28IndividualArrivalReceiptsCompletePublicObjectiveWithoutCenterPileup()
        {
            var planner=new AiArmyPlanner("player-1",71);var registry=Registry();var initial=Oct28Objectives(10);var action=Plan(planner,registry,initial);Assert.NotNull(action);planner.Commit(initial,action,registry);planner.ObserveReceipt(Receipt(action,PlayableAiDeliveryStatus.Applied,11));
            var arrived=Oct28Objectives(12,completed:true);Assert.True(arrived.Entities.All(u=>u.Completion!=null));Assert.True(arrived.Entities.Any(u=>Math.Abs(u.Position.Z)>P.TankCollisionRadius));
            planner.Observe(arrived,registry,AiProfile.Initial);Assert.False(planner.Active,"Separate assigned formation slots have real completion receipts; bodies must not pile into the same center");
        }
        private static void Oct28BindKnowledge(AiArmyPlanner planner,AiKnowledgeState knowledge)=>typeof(AiArmyPlanner).GetProperty("Knowledge",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(planner,knowledge);
        [Test] public void Oct28PreviousArrivalCannotCompleteAnAcceptedPendingReplacement()
        {
            var planner=new AiArmyPlanner("player-1",71);var registry=Registry();var initial=Oct28Objectives(10);var action=Plan(planner,registry,initial);planner.Commit(initial,action,registry);planner.ObserveReceipt(Receipt(action,PlayableAiDeliveryStatus.Applied,11));
            var pending=Oct28Objectives(12,completed:true,pending:true);Assert.True(pending.Entities.All(u=>u.Completion!=null&&u.Queue.Pending!=null));
            planner.Observe(pending,registry,AiProfile.Initial);Assert.True(planner.Active,"Prior genuine completion must not certify a replacement still waiting for installation");
        }
        [Test] public void Oct28RememberedEmptyStartRemainsExcludedAfterScoutLeaves()
        {
            var planner=new AiArmyPlanner("player-1",71);var o=Oct28Objectives(20);Assert.False(o.Intel.Areas.Single().Full);
            Oct28BindKnowledge(planner,new AiKnowledgeState(o.OwnerId,o.Generation,20,Array.Empty<AiKnownContact>(),new[]{new AiVisitedArea(77,10,true,false)}));
            Assert.IsNull(Plan(planner,Registry(),o),"Leaving vision cannot turn genuine known emptiness into an attack target again");
        }
        [Test] public void Oct28KnownEmptyAlternativeMakesRemainingPublicStartInferable()
        {
            var planner=new AiArmyPlanner("player-1",71);var o=Oct28Objectives(20,ambiguous:true);
            Oct28BindKnowledge(planner,new AiKnowledgeState(o.OwnerId,o.Generation,20,Array.Empty<AiKnownContact>(),new[]{new AiVisitedArea(77,10,true,false)}));
            var action=Plan(planner,Registry(),o);Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.AttackMove,action.Kind);Assert.AreEqual(78,action.SiteId,"Only remaining public zone follows from actual exclusion of the alternative");
        }
        [TestCase("owner")][TestCase("generation")][TestCase("future")]
        public void Oct28PublicTargetRejectsForeignOrFutureKnowledge(string invalid)
        {
            var planner=new AiArmyPlanner("player-1",71);var o=Oct28Objectives(20);
            Oct28BindKnowledge(planner,new AiKnowledgeState(invalid=="owner"?"enemy-1":o.OwnerId,invalid=="generation"?72:o.Generation,invalid=="future"?21:20,Array.Empty<AiKnownContact>(),Array.Empty<AiVisitedArea>()));
            Assert.Throws<ArgumentException>(()=>Plan(planner,Registry(),o));
        }
    }
}
