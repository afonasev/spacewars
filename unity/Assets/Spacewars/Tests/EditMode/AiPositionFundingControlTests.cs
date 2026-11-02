using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.EditMode
{
    public sealed class AiPositionFundingControlTests
    {
        static readonly PlayableProfile P=PlayableProfile.Default;
        static readonly AiProfile AI=AiProfile.Initial;
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static byte[] Wire(AiDecisionArbiter a)
        {using(var s=new MemoryStream()){using(var w=new BinaryWriter(s,System.Text.Encoding.UTF8,true))typeof(AiDecisionArbiter).GetMethod("WriteState",Flags).Invoke(a,new object[]{w});return s.ToArray();}}
        static AiDecisionArbiter Restore(byte[] bytes,long tick,string owner)
        {var a=new AiDecisionArbiter(AI);using(var s=new MemoryStream(bytes))using(var r=new BinaryReader(s))typeof(AiDecisionArbiter).GetMethod("ReadState",Flags).Invoke(a,new object[]{r,tick,owner,107L,P,AI});return a;}
        [Test] public void UnderfundedPositionSurvivesAdmissionAndSharesRealMoneyWithBoundedControls()
        {
            var site=TerritoryRules.Sites(P)[0];
            var factory=new PlayableBuildingSnapshot(9,PlayableOwner.Player,PlayableBuildingKind.Factory,site.Slots[2].Position,TerritoryRules.Health(P,PlayableBuildingKind.Factory),1,0,0,default,siteId:site.Id,slotId:site.Slots[2].Id,parentId:1);
            var units=Enumerable.Range(30,Math.Max(10,(int)AI.Value("technology.minimumUnitsForScienceCenter"))).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-20,id-40),P.TankHealth,false,0,0,0)).ToArray();
            PlayableAiObservation View(int cash,long tick=9000,PlayableBuildingSnapshot tower=null)=>AiPositionDevelopmentTests.View(extra:tower==null?new[]{factory}:new[]{factory,tower},units:units,credits:cash,tick:tick);
            var opening=AiPositionDevelopmentTests.Opening(PlayableAiOpening.GreedySafe);var o=View(74);var a=new AiDecisionArbiter(AI);var b=new AiBudgetLedger(o.OwnerId,o.Generation);b.ObserveBank(o.Credits);
            var choices=AiPositionDevelopmentPlanner.Plan(o,P,AI,opening,null,true);Assert.IsNotEmpty(choices,"legal useful goal must reach allocation while underfunded");
            Assert.True(choices.All(c=>AiEconomyAdmission.Reject(o,P,c.Action)=="insufficient credits"));
            int protect=PlayableUnitRules.Cost(P,PlayableEntityKind.Tank);var quote=AiPositionDevelopmentFunding.Quote(o,P,AI,a,choices,protect);Assert.NotNull(quote);Assert.AreEqual(P.TurretCreditCost,quote.Credits);
            var goal=choices.Single(c=>AiEconomyAdmission.IntentId("economy",c.Action)==AiEconomyAdmission.IntentId("economy",quote.Candidate.Action));
            Assert.False(quote.AllowsProduction(74,protect));Assert.True(quote.AllowsProduction(74,protect,acceptedStartup:true),"accepted startup remains outside discretionary withholding");
            Assert.False(AiExpansionFinancial.FullyFundable(b,0,new AiIntent("tower","economy",goal.Action,o.Tick+30,quote.Credits,0,Array.Empty<string>())));Assert.Zero(b.PaidTotal);
            // A current research hold can yield only through its ordinary allocator.
            var funding=new AiTechnologyFunding(o.OwnerId,o.Generation);var plan=new AiTechnologyPlanner().Plan(o,P,AI,b.Capture());Assert.NotNull(plan.FundingGoal);
            funding.Observe(o,P,AI,b,a,plan,true,3);Assert.AreEqual(71,b.Reserved);
            int cash=quote.Credits+protect+71;var funded=View(cash,9001);b.ObserveBank(cash);var nextPlan=new AiTechnologyPlanner().Plan(funded,P,AI,b.Capture(),preferredFunding:funding.Preferred(a));
            funding.Observe(funded,P,AI,b,a,nextPlan,true,protect,quote.Credits);
            Assert.AreEqual(71,b.Reserved);Assert.AreEqual(quote.Credits+protect,b.Available);Assert.Zero(b.Unpaid);Assert.Zero(b.PaidTotal);
            var intent=new AiIntent("tower","economy",goal.Action,funded.Tick+30,quote.Credits,0,new[]{"policy:economy"},1,goal.Utility);
            Assert.True(AiExpansionFinancial.FullyFundable(b,protect,intent));
            var chosen=a.Select(new[]{intent},funded.Tick,cash,P.ArmyCapacity,1,b,gameplayProfile:P);Assert.AreSame(intent,chosen.Single());
            // Accepted startup and invoices are real ledger claims, not extra promises.
            var production=new PlayableAiAction(8,o.OwnerId,P.ProfileId,P.Revision,o.Generation,funded.SnapshotSequence,PlayableCommandKind.QueueTank,new[]{9},unitKind:PlayableEntityKind.Tank,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            Assert.True(b.Reserve(new AiBudgetEntry("startup","factory-startup",production,protect,1,9001,9031,"terminal receipt",new[]{"producer:9"},P.ProfileId+"@"+P.Revision)));
            Assert.False(AiExpansionFinancial.FullyFundable(b,protect,intent));
            Assert.True(b.Accept(new AiBudgetEntry("invoice","production",production,protect,1,9001,9031,"terminal receipt",new[]{"recipient:9"},P.ProfileId+"@"+P.Revision,new AiReceiptIdentity(o.Generation,o.OwnerId,1,1))));
            Assert.Less(b.Available,intent.Credits);Assert.AreEqual(protect,b.Unpaid);Assert.Zero(b.PaidTotal);
            // A temporary higher funding band withholds savings without deleting the
            // valid target's original deadline (including an expired tombstone).
            Assert.IsNull(AiPositionDevelopmentFunding.Quote(funded,P,AI,a,AiPositionDevelopmentPlanner.Plan(funded,P,AI,opening,null,true),protect,fundingAllowed:false));
            // The target's clock and existing shared ledger retain exact wire state.
            var ledgerType=typeof(AiBudgetLedger).Assembly.GetType("Spacewars.Runtime.AiBudgetStateWire",true);
            byte[] LedgerBytes(AiBudgetLedger value){using(var s=new MemoryStream()){using(var w=new BinaryWriter(s,System.Text.Encoding.UTF8,true))ledgerType.GetMethod("Write",Flags|BindingFlags.Static).Invoke(null,new object[]{w,value});return s.ToArray();}}
            var ledgerBytes=LedgerBytes(b);AiBudgetLedger ledgerCopy;using(var stream=new MemoryStream(ledgerBytes))using(var reader=new BinaryReader(stream))ledgerCopy=(AiBudgetLedger)ledgerType.GetMethod("Read",Flags|BindingFlags.Static).Invoke(null,new object[]{reader,9001L});
            ExactByteAssert.AreEqual(ledgerBytes,LedgerBytes(ledgerCopy),"shared holds and unpaid production exact restoration");
            var bytes=Wire(a);var restored=Restore(bytes,9001,o.OwnerId);ExactByteAssert.AreEqual(bytes,Wire(restored),"position target and research memo exact restoration");
            long after=9000+AiProfile.SecondsToTicks(AI.Value("economy.reservationExpirySeconds"),30)+1;
            var expired=View(10000,after);var expiredChoices=AiPositionDevelopmentPlanner.Plan(expired,P,AI,opening,null,true);
            Assert.IsNull(AiPositionDevelopmentFunding.Quote(expired,P,AI,restored,expiredChoices,protect),"unchanged target must not renew saving after its finite window");
            Assert.IsNull(AiEconomyAdmission.Reject(expired,P,goal.Action),"expiry does not prohibit an affordable direct purchase");
            Assert.IsNull(AiPositionDevelopmentFunding.Quote(expired,P,AI,restored,expiredChoices,protect,fundingAllowed:false));
            Assert.IsNull(AiPositionDevelopmentFunding.Quote(expired,P,AI,restored,expiredChoices,protect),"temporary suppression cannot remove the expired tombstone");
            // Covered/invalid goals release their clock and impose no new savings band.
            var pad=site.DefenseSlots(P).Single(p=>p.Id==goal.Action.SlotId);var tower=new PlayableBuildingSnapshot(40,PlayableOwner.Player,PlayableBuildingKind.SentryTurret,pad.Position,TerritoryRules.Health(P,PlayableBuildingKind.SentryTurret),1,0,0,default,siteId:site.Id,slotId:pad.Id,parentId:1);
            var covered=View(74,after+1,tower);var coveredChoices=AiPositionDevelopmentPlanner.Plan(covered,P,AI,opening,null,true);Assert.IsEmpty(coveredChoices);
            Assert.IsNull(AiPositionDevelopmentFunding.Quote(covered,P,AI,restored,coveredChoices,protect));
            Assert.IsEmpty(AiPositionDevelopmentPlanner.Plan(o,P,AI,opening,null,false));
            var uncovered=View(10000,after+2);Assert.NotNull(AiPositionDevelopmentFunding.Quote(uncovered,P,AI,restored,AiPositionDevelopmentPlanner.Plan(uncovered,P,AI,opening,null,true),protect),"a genuinely uncovered goal after coverage change is a new bounded target");
        }
    }
}
