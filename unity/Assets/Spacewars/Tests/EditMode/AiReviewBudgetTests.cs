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
