using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.EditMode
{
    public sealed class AiReviewBudgetTests
    {
        private static AiBudgetEntry Entry(string id,int amount,bool payment=false,double settled=0)
        {
            var p=PlayableProfile.Default;
            var a=new PlayableAiAction(1,"player-1",p.ProfileId,p.Revision,71,1,payment?PlayableCommandKind.StartBuildingRepair:PlayableCommandKind.QueueTank,new[]{4});
            return new AiBudgetEntry(id,"test",a,amount,1,0,90,"terminal",new[]{id},"terms",payment?new AiReceiptIdentity(71,"player-1",1,1):null,payment,settled);
        }
        private static AiBudgetState State(int liquid=100,AiBudgetEntry[] reserved=null,AiBudgetEntry[] unpaid=null,AiBudgetEntry[] paid=null,double settled=0,long paidTotal=0)=>
            new AiBudgetState("player-1",71,liquid,0,paidTotal,reserved??Array.Empty<AiBudgetEntry>(),unpaid??Array.Empty<AiBudgetEntry>(),paid??Array.Empty<AiBudgetEntry>(),admittedDecision:1,admittedAction:1,repairPaidTotal:settled);
        [Test] public void ReviewCloneOwnsIndependentLedgerAndUsesCurrentTunedProfile()
        {
            var data=AiProfile.Initial.CopyData();data.fields.Single(x=>x.path=="decision.repeatOrderSeconds").value+=1;
            var tuned=new AiProfile(data);var p=PlayableProfile.Default;
            var owner=new PlayableAiOwnerLoop(p,PlayableAiOpeningComposition.Initialize(7,"player-1",aiProfile:tuned),71,null,tuned,AiDifficulty.Fighter);
            owner.Budget.ObserveBank(100);
            var clone=owner.CreateReviewClone(0);
            Assert.AreNotSame(owner,clone);Assert.AreNotSame(owner.Budget,clone.Budget);Assert.AreSame(tuned,clone.TeamProfile);
            clone.Budget.ObserveBank(25);clone.Budget.SetSafetyReserve(7);
            Assert.AreEqual(100,owner.Budget.Liquid);Assert.Zero(owner.Budget.SafetyReserve);
        }
        [Test] public void FrozenReviewInputRestoresOffThreadWithoutReadingChangedAuthorityBudget()
        {
            var profile=PlayableProfile.Default;var owner=new PlayableAiOwnerLoop(profile,PlayableAiOpeningComposition.Initialize(7,"player-1"),71);
            owner.Budget.ObserveBank(100);var input=owner.CaptureReviewInput(0);owner.Budget.ObserveBank(17);
            PlayableAiOwnerLoop copy=null;Exception failure=null;
            var thread=new System.Threading.Thread(()=>{try{copy=input.Restore();}catch(Exception e){failure=e;}}){IsBackground=true};thread.Start();Assert.True(thread.Join(5000));Assert.IsNull(failure);
            Assert.AreEqual(100,copy.Budget.Liquid);Assert.AreEqual(17,owner.Budget.Liquid);Assert.AreNotSame(owner.Budget,copy.Budget);
        }
        [Test] public void ReviewCursorRejectsValuesOutsideOwnerRoster()
        {
            var scheduler=new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>());
            scheduler.RestoreReviewCursor(0);Assert.Throws<ArgumentException>(()=>scheduler.RestoreReviewCursor(1));Assert.Throws<ArgumentException>(()=>scheduler.RestoreReviewCursor(-1));
        }
        [TestCase(false)][TestCase(true)]
        public void BackgroundCompletionAtReviewPhaseYieldsDeliveryBeforeAnotherReview(bool invalidate)
        {
            var config=OfflineParticipantAuthorityTests.Config(2,false);
            var domain=new PlayableDomain(config.Profile,71,config);
            var owner=new PlayableAiOwnerLoop(config.Profile,PlayableAiOpeningComposition.Initialize(config.Seed,config.Roster[0].Id),71);
            var scheduler=new AiAuthorityScheduler(new[]{owner});scheduler.EnableBackgroundReviews();
            using(var entered=new System.Threading.ManualResetEventSlim())using(var release=new System.Threading.ManualResetEventSlim()){
                domain.BeforeOwnerReviewForTests=()=>{entered.Set();if(!release.Wait(5000))throw new TimeoutException("Review phase control was not released.");};
                void Review()=>scheduler.Review(domain,0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),config.Seed,_=>0);
                try{
                    Review();Assert.True(entered.Wait(5000));
                    // Work is incomplete at delivery, then settles before this tick's review.
                    scheduler.Deliver(domain,_=>0,false);if(invalidate)scheduler.Rebind(config.Profile);
                    release.Set();Assert.True(System.Threading.SpinWait.SpinUntil(()=>scheduler.ReviewWorkerStopped,5000));
                    Review();
                    var work=typeof(AiAuthorityScheduler).GetField("reviewWork",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                    Assert.IsNull(work.GetValue(scheduler),"Completion at the review phase must leave one authority delivery opportunity before launching another detached review.");
                    if(invalidate)Assert.AreSame(owner,scheduler.Owners.Single());else Assert.AreNotSame(owner,scheduler.Owners.Single());
                    scheduler.Deliver(domain,_=>0,false);
                    Review();Assert.NotNull(work.GetValue(scheduler),"The delivery opportunity does not stop subsequent ordinary reviews.");
                }finally{
                    release.Set();Assert.True(System.Threading.SpinWait.SpinUntil(()=>scheduler.ReviewWorkerStopped,5000));
                    domain.BeforeOwnerReviewForTests=null;scheduler.Stop(domain.Tick);domain.StopObservedPaths();
                    Assert.True(System.Threading.SpinWait.SpinUntil(()=>domain.ObservedPathWorkerStopped,5000));
                    domain.Navigation.DestroyNavigationAuthority();
                }
            }
        }
        [Test] public void ReviewKeepsExactLiveRepairSettlementAndNewReservation()
        {
            var invoice=Entry("repair",40,true,0.1);var initial=State(unpaid:new[]{invoice},settled:0.1);
            var planned=State(reserved:new[]{Entry("new",20)},unpaid:new[]{invoice},settled:0.1);
            var current=State(83,unpaid:new[]{Entry("repair",23,true,17.123456789)},settled:17.123456789);
            Assert.True(AiBudgetLedger.TryMergeReview(initial,planned,current,30,out var result));
            Assert.AreEqual(current.RepairPaidTotal,result.RepairPaidTotal);Assert.AreEqual(83,result.Liquid);
            Assert.AreSame(current.Unpaid.Single(),result.Capture().Unpaid.Single());Assert.AreEqual(20,result.Reserved);
        }
        [Test] public void CompletedPaymentHistoryIsTakenFromAuthorityWithoutReplayingIt()
        {
            var invoice=Entry("repair",40,true);var initial=State(unpaid:new[]{invoice});
            var closed=invoice.With(0,"terms",invoice.Receipt,false,40.123456789);
            var current=State(60,paid:new[]{closed},settled:40.123456789);
            Assert.True(AiBudgetLedger.TryMergeReview(initial,initial,current,30,out var result));
            Assert.IsEmpty(result.Capture().Unpaid);Assert.AreSame(closed,result.Capture().Paid.Single());Assert.AreEqual(current.RepairPaidTotal,result.RepairPaidTotal);
        }
        [Test] public void StaleDecisionCannotFundNewWorkFromAlreadySpentMoney()
        {
            var initial=State();var planned=State(reserved:new[]{Entry("new",80)});
            Assert.False(AiBudgetLedger.TryMergeReview(initial,planned,State(20),30,out var result));Assert.IsNull(result);
        }
        [Test] public void ChangedAuthorityReservationInvalidatesDetachedDecision()
        {
            Assert.False(AiBudgetLedger.TryMergeReview(State(),State(),State(reserved:new[]{Entry("foreign",1)}),30,out _));
        }
        [Test] public void WorkerCannotForgeGameplayPaymentHistory()
        {
            Assert.False(AiBudgetLedger.TryMergeReview(State(),State(paidTotal:1),State(),30,out _));
        }
        [Test] public void ExistingUnfundedObligationSurvivesWithoutInventingNewCash()
        {
            var initial=State(0,unpaid:new[]{Entry("repair",40,true)});
            Assert.True(AiBudgetLedger.TryMergeReview(initial,initial,initial,30,out var result));Assert.AreEqual(40,result.Unpaid);Assert.Zero(result.Available);
        }
    }
}
