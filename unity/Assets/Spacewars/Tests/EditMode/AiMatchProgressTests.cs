using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class AiMatchProgressTests
    {
        private static AiIntent Intent(string id,string policy="production",int priority=0,bool overdue=false,string conditions="ready")=>
            new AiIntent(id,policy,new PlayableAiAction(1,"player-1","p",1,1,1,policy=="production"?PlayableCommandKind.QueueTank:PlayableCommandKind.Move,new[]{21}),long.MaxValue,policy=="production"?150:0,0,new[]{id},priority,conditions:conditions,overdue:overdue);

        [TestCase(false)][TestCase(true)] public void AffordableProductionGetsFirstAttentionWithoutWaitingForAging(bool aged)
        {
            var ai=AiProfile.Initial;var arbiter=new AiDecisionArbiter(ai);var production=Intent("production");
            if(aged)Assert.IsEmpty(arbiter.Select(new[]{production},0,0,10,1));
            long tick=aged?AiProfile.SecondsToTicks(ai.Value("decision.intentAgingSeconds"),30):0;
            var newer=Intent("army-1","army-mission",1,true);
            Assert.AreEqual(production,arbiter.Select(new[]{production,newer},tick,622,10,1).Single(),"Useful affordable production receives the first ordinary attention slot immediately");
        }

        [Test] public void UnaffordableOrReservedProductionLeavesAttentionForOtherUsefulWork()
        {
            var arbiter=new AiDecisionArbiter(AiProfile.Initial);var production=Intent("production");var move=Intent("move","scout");
            Assert.AreEqual(move,arbiter.Select(new[]{production,move},0,149,10,1).Single());
            var budget=new AiBudgetLedger("player-1",1);budget.ObserveBank(200);
            Assert.True(budget.Reserve(new AiBudgetEntry("committed","economy",move.Action,100,0,0,100,"target lost or expiry",new[]{"site:2"},"p@1")));
            Assert.AreEqual(move,arbiter.Select(new[]{production,move},1,200,10,1,budget).Single());
            Assert.AreEqual(100,budget.Reserved,"Attention priority cannot spend accepted funding");
            Assert.AreEqual("ledger budget unavailable",arbiter.Rejections[production.Id]);
        }

        [Test] public void ProductionPriorityRequiresARealQueueAndKeepsRemainingAttentionAvailable()
        {
            var arbiter=new AiDecisionArbiter(AiProfile.Initial);var production=Intent("production");var scout=Intent("scout","scout",1,true);
            var fake=new AiIntent("fake","production",scout.Action,100,0,0,new[]{"fake"});
            Assert.AreEqual(scout,arbiter.Select(new[]{fake,scout},0,622,10,1).Single());
            CollectionAssert.AreEqual(new[]{production,scout},arbiter.Select(new[]{production,scout},1,622,10,2));
        }

        [Test] public void SurvivalStillPreemptsAgedProductionAndBudgetStillApplies()
        {
            var ai=AiProfile.Initial;var arbiter=new AiDecisionArbiter(ai);var production=Intent("production");
            arbiter.Select(new[]{production},0,0,10,1);
            long aged=AiProfile.SecondsToTicks(ai.Value("decision.intentAgingSeconds"),30);
            var survival=Intent("survival","defense",2);
            Assert.AreEqual(survival,arbiter.Select(new[]{production,survival},aged,622,10,1).Single());
            Assert.IsEmpty(arbiter.Select(new[]{production},aged+1,149,10,1));Assert.AreEqual("ledger budget unavailable",arbiter.Rejections[production.Id]);
        }

        [Test] public void RejectedIntentRetriesAreBoundedAndRelevantChangeReopensThem()
        {
            var ai=AiProfile.Initial;var arbiter=new AiDecisionArbiter(ai);var action=Intent("move","army-mission");
            long retry=AiProfile.SecondsToTicks(ai.Value("decision.retrySeconds"),30),tick=0;
            int limit=(int)ai.Value("decision.retryLimit");
            for(int i=0;i<limit;i++){
                Assert.AreEqual(action,arbiter.Select(new[]{action},tick,622,10,1).Single());
                arbiter.Terminal(action,tick,PlayableAiDeliveryStatus.Rejected);
                Assert.IsEmpty(arbiter.Select(new[]{action},tick+1,622,10,1));tick+=retry;
            }
            Assert.IsEmpty(arbiter.Select(new[]{action},tick,622,10,1));Assert.AreEqual("unchanged rejection retry limit",arbiter.Rejections[action.Id]);
            var changed=Intent("move","army-mission",conditions:"changed-relevant-route");
            Assert.AreEqual(changed,arbiter.Select(new[]{changed},tick,622,10,1).Single());
        }

        [Test] public void StagingRefusalReportsAdmissionAndRetainsAcceptedIntentThenRecovers()
        {
            var d=new PlayableDomain(PlayableProfile.Default,1,false);var nav=d.Navigation;
            try{
                Assert.AreEqual(PlayableCommandStatus.Applied,d.Apply(new PlayableCommand(1,1,"player-1",PlayableCommandKind.Hold,new[]{3}),out _));
                nav.Crowd.TryGet(3,out var actor);nav.Crowd.TryGet(4,out var peer);bool held=peer.Held;
                var before=d.PlayerSnapshot(1,RuntimeStatus.Running,false,null,null,7).Entities.Single(e=>e.Id==3);
                nav.AfterMovementObservationForQa=()=>peer.Held=!held;
                var move=new PlayableCommand(1,2,"player-1",PlayableCommandKind.Move,new[]{3},new NavPoint(actor.Position.X+2,actor.Position.Z+3));
                Assert.AreEqual(PlayableCommandStatus.Overflow,d.Apply(move,out var message));
                StringAssert.StartsWith("Navigation admission refused:",message);
                Assert.True(actor.Held);Assert.AreEqual(before.OrderStamp.Revision,d.PlayerSnapshot(1,RuntimeStatus.Running,false,null,null,7).Entities.Single(e=>e.Id==3).OrderStamp.Revision);
                peer.Held=held;nav.AfterMovementObservationForQa=null;
                Assert.AreEqual(PlayableCommandStatus.Applied,d.Apply(new PlayableCommand(1,3,"player-1",PlayableCommandKind.Move,new[]{3},move.Target),out var recovered),recovered);
                // The accepted replacement intentionally retains HOLD while
                // its route is pending. Release it only after real installation.
                var kernel=new CertifiedRouteKernel();
                try{for(int tick=0;tick<120&&actor.Held;tick++){kernel.Pump(new PlayableRouteBinding(d.Geometry,d.RoutingProfile,nav.Admission,nav.RoutePort),4);d.Step(1d/30);nav.Crowd.TryGet(3,out actor);}}
                finally{kernel.Clear();}
                Assert.False(actor.Held,"A valid installed replacement releases the retained HOLD");
            }finally{nav.AfterMovementObservationForQa=null;nav.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(nav.PhysicalHostBudget.HeldBytes);}
        }

        [Test] public void MovementRetryConditionsIgnoreTimeIncomeAndDistantPeersButObserveProgressAndClearance()
        {
            var p=PlayableProfile.Default;
            PlayableAiObservation Observation(long tick,int credits,double ownX,double allyX)=>PlayableAiObservation.From(
                new PlayableSnapshot(p.ProfileId,p.Revision,1,7,tick+1,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,credits,null,
                    new[]{new PlayableEntitySnapshot(21,PlayableOwner.Player,PlayableEntityKind.Explorer,new NavPoint(ownX,.1),70,false,0,0,0),new PlayableEntitySnapshot(22,PlayableOwner.Enemy,PlayableEntityKind.Explorer,new NavPoint(allyX,.1),70,false,0,0,0)},
                    Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),null,null));
            var action=Intent("move","army-mission").Action;
            string initial=PlayableAiOwnerLoop.MovementRetryConditions(Observation(0,622,.1,10),action,p);
            Assert.AreEqual(initial,PlayableAiOwnerLoop.MovementRetryConditions(Observation(900,900,.100000001,20),action,p));
            Assert.AreNotEqual(initial,PlayableAiOwnerLoop.MovementRetryConditions(Observation(900,900,.1+p.Navigation.LocalGridCell,20),action,p));
            Assert.AreNotEqual(initial,PlayableAiOwnerLoop.MovementRetryConditions(Observation(900,900,.1,.5),action,p),"A known endpoint blocker entering/leaving is a relevant admission change");
        }
    }
}
