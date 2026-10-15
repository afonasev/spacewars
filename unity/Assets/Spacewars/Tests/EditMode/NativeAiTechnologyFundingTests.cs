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
    public sealed class NativeAiTechnologyFundingTests
    {
        private static PlayableAiObservation View(long tick,int cash,double income=0,bool center=false,double progress=1,PlayableProfile p=null,bool prerequisite=true,bool occupied=false,PlayableEntityKind unit=PlayableEntityKind.Tank,bool secondCenter=false)
        {
            p=p??PlayableProfile.Default;var site=TerritoryRules.Sites(p).First(s=>s.Kind==PlayableBuildingKind.Headquarters);
            var bs=new[]{new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Headquarters,site.Position,400,1,0,0,default(NavPoint),siteId:site.Id),new PlayableBuildingSnapshot(2,PlayableOwner.Player,PlayableBuildingKind.Factory,site.Slots[0].Position,250,1,0,0,default(NavPoint),siteId:site.Id,slotId:site.Slots[0].Id,parentId:1)};
            if(!prerequisite)bs=bs.Where(b=>b.Kind!=PlayableBuildingKind.Factory).ToArray();
            if(occupied)bs=bs.Concat(new[]{new PlayableBuildingSnapshot(3,PlayableOwner.Player,PlayableBuildingKind.Refinery,site.Slots[1].Position,p.RefineryHealth,1,0,0,default(NavPoint),siteId:site.Id,slotId:site.Slots[1].Id,parentId:1)}).ToArray();
            if(center)bs=bs.Concat(new[]{new PlayableBuildingSnapshot(3,PlayableOwner.Player,PlayableBuildingKind.ScientificCenter,site.Slots[1].Position,250,progress,0,0,default(NavPoint),siteId:site.Id,slotId:site.Slots[1].Id,parentId:1,phase:progress<1?ConstructionPhase.Constructing:ConstructionPhase.Ready)}).ToArray();
            if(secondCenter)bs=bs.Concat(new[]{new PlayableBuildingSnapshot(4,PlayableOwner.Player,PlayableBuildingKind.ScientificCenter,site.Slots[2].Position,250,1,0,0,default(NavPoint),siteId:site.Id,slotId:site.Slots[2].Id,parentId:1,phase:ConstructionPhase.Ready)}).ToArray();
            var units=Enumerable.Range(10,3).Select(i=>new PlayableEntitySnapshot(i,PlayableOwner.Player,unit,new NavPoint(i,0),PlayableUnitRules.Health(p,unit),false,0,0,0)).ToArray();
            return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,41,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,cash,null,units,bs,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:new[]{new TerritorySiteSnapshot(site,PlayableOwner.Player,PlayableOwner.Player,1,false,1,true)},population:new PlayablePopulationSnapshot(9,0,p.ArmyCapacity),researchAvailability:AiResearchCatalog.All.Select(d=>new PlayableResearchAvailabilitySnapshot(d.Kind,d.Cost(p),true)).ToArray(),activeProfile:p,homeSiteId:site.Id,settledIncome:income));
        }
        private static void Observe(AiTechnologyFunding f,AiBudgetLedger b,AiDecisionArbiter a,PlayableAiObservation o,int protect=0,bool allowed=true,PlayableProfile p=null)
        {p=p??PlayableProfile.Default;b.ObserveBank(o.Credits);b.Expire(o.Tick,e=>!AiTechnologyFunding.IsFunding(e)||f.Valid(e,o,p,b,a));f.Reconcile(o,b,a);var plan=new AiTechnologyPlanner().Plan(o,p,AiProfile.Initial,b.Capture(),preferredFunding:f.Preferred(a));f.Observe(o,p,AiProfile.Initial,b,a,plan,allowed,protect);}
        private static (AiTechnologyFunding f,AiBudgetLedger b,AiDecisionArbiter a) Start(int cash=176)
        {var o=View(0,cash);var b=new AiBudgetLedger(o.OwnerId,71);return(new AiTechnologyFunding(o.OwnerId,71),b,new AiDecisionArbiter(AiProfile.Initial));}
        [Test] public void PartialCurrentMoney_ProtectsScoutAndRenewsOnlyActualGrowth()
        {
            var x=Start();Observe(x.f,x.b,x.a,View(0,176),100);var e=x.b.Capture().Reserved.Single();Assert.AreEqual(76,e.Amount);Assert.AreEqual(100,x.b.Available);Assert.AreEqual(0,x.b.PaidTotal);
            Observe(x.f,x.b,x.a,View(300,176),100);Assert.AreEqual(e.ExpiresTick,x.b.Capture().Reserved.Single().ExpiresTick);
            Observe(x.f,x.b,x.a,View(600,231,55),100);var grown=x.b.Capture().Reserved.Single();Assert.AreEqual(131,grown.Amount);Assert.AreEqual(600,grown.CreatedTick);Assert.AreEqual(600+x.b.ReservationLifetimeTicks,grown.ExpiresTick);
        }
        [Test] public void ExpiryReleasedAvailable_IsNotIncomeOrNewCash_EvenAfterHistoryChurn()
        {
            var x=Start();Observe(x.f,x.b,x.a,View(0,176));long expiry=x.b.Capture().Reserved.Single().ExpiresTick;
            Observe(x.f,x.b,x.a,View(expiry+1,176));Assert.IsEmpty(x.b.Capture().Reserved);Assert.AreEqual(176,x.b.Available);
            for(int n=0;n<160;n++)
            {var o=View(expiry+2+n,176);var action=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,71,o.SnapshotSequence,PlayableCommandKind.Move,new[]{10});x.a.Select(new[]{new AiIntent("churn-"+n,"scout",action,o.Tick+1,0,0,Array.Empty<string>())},o.Tick,176,10,1,x.b);Observe(x.f,x.b,x.a,o);Assert.IsEmpty(x.b.Capture().Reserved);}
            Observe(x.f,x.b,x.a,View(expiry*3,176));Assert.IsEmpty(x.b.Capture().Reserved);
            Observe(x.f,x.b,x.a,View(expiry*3+1,196,20));Assert.AreEqual(196,x.b.Capture().Reserved.Single().Amount);
        }
        [Test] public void AtomicScienceInvoice_RetainsResearchRemainderAndRejectsForeignConsumption()
        {
            var x=Start(600);var o=View(0,600);Observe(x.f,x.b,x.a,o);var held=x.b.Capture().Reserved.Single();
            var action=held.Action;var intent=new AiIntent("science","economy",action,30,300,0,new[]{"slot:"+action.SiteId+":"+action.SlotId},1,reservationId:held.Id);
            var bad=new AiIntent("foreign","economy",new PlayableAiAction(1,"foreign",o.ProfileId,o.ProfileRevision,71,0,PlayableCommandKind.BuildAt,siteId:action.SiteId,slotId:action.SlotId,parentId:1,buildingKind:PlayableBuildingKind.ScientificCenter),30,300,0,Array.Empty<string>(),reservationId:held.Id);
            Assert.IsEmpty(x.a.Select(new[]{bad},0,600,10,1,x.b));Assert.AreEqual(600,x.b.Reserved);
            Assert.AreEqual(intent,x.a.Select(new[]{intent},0,600,10,1,x.b).Single());var receipt=new AiReceiptIdentity(71,o.OwnerId,1,1);
            Assert.True(x.f.Accept(intent,receipt,o,PlayableProfile.Default,x.b,x.a));Assert.AreEqual(300,x.b.Unpaid);Assert.AreEqual(300,x.b.Reserved);Assert.AreEqual(0,x.b.Available);Assert.AreEqual(0,x.b.PaidTotal);
            Assert.False(x.f.Accept(intent,receipt,o,PlayableProfile.Default,x.b,x.a));Assert.AreEqual(600,x.b.Unpaid+x.b.Reserved);
            x.b.Reconcile(intent.Id,receipt,PlayableAiDeliveryStatus.Applied);Observe(x.f,x.b,x.a,View(1,300,center:true,progress:.1));Assert.AreEqual(300,x.b.PaidTotal);Assert.AreEqual(300,x.b.Reserved);Assert.AreEqual(0,x.b.Available);
            Observe(x.f,x.b,x.a,View(601,300,center:true));var remaining=x.b.Capture().Reserved.Single();Assert.AreEqual(PlayableCommandKind.QueueResearch,remaining.Action.Kind);Assert.AreEqual(3,remaining.Action.EntityIds.Single());
            var research=new AiIntent("research","research",remaining.Action,630,300,0,new[]{"recipient:3"},reservationId:remaining.Id);
            Assert.True(x.f.Accept(research,new AiReceiptIdentity(71,o.OwnerId,2,1),View(601,300,center:true),PlayableProfile.Default,x.b,x.a));Assert.AreEqual(0,x.b.Reserved);Assert.AreEqual(300,x.b.Unpaid);Assert.AreEqual(300,x.b.PaidTotal);
        }
        [TestCase(false)][TestCase(true)] public void SurvivalOrLostPrerequisite_ReleasesGoalWithoutSpend(bool lost)
        {
            var x=Start();Observe(x.f,x.b,x.a,View(0,176));if(lost)Observe(x.f,x.b,x.a,View(1,176,prerequisite:false));else Observe(x.f,x.b,x.a,View(1,176),allowed:false);
            Assert.IsEmpty(x.b.Capture().Reserved);Assert.AreEqual(176,x.b.Available);Assert.AreEqual(0,x.b.PaidTotal);
        }
        [Test] public void RejectedScience_ReleasesRemainder_NoRecycledCashRenewal()
        {
            var x=Start(600);var o=View(0,600);Observe(x.f,x.b,x.a,o);var e=x.b.Capture().Reserved.Single();var i=new AiIntent("science","economy",e.Action,30,300,0,Array.Empty<string>(),reservationId:e.Id);var r=new AiReceiptIdentity(71,o.OwnerId,1,1);
            Assert.True(x.f.Accept(i,r,o,PlayableProfile.Default,x.b,x.a));x.b.Reconcile(i.Id,r,PlayableAiDeliveryStatus.Rejected);x.f.Terminal(i,PlayableAiDeliveryStatus.Rejected,x.b,x.a);
            Observe(x.f,x.b,x.a,View(31,600));Assert.IsEmpty(x.b.Capture().Reserved);Assert.AreEqual(0,x.b.PaidTotal);
        }
        [Test] public void ProfileMetadata_RebindsTermsWithoutRenewingDeadline()
        {
            var x=Start();Observe(x.f,x.b,x.a,View(0,176));var original=x.b.Capture().Reserved.Single();var data=PlayableProfile.Default.CopyData();data.revision++;var next=PlayableProfile.Create(data);
            x.b.RepriceReservations(e=>x.f.Reprice(e,next),next.ProfileId+"@"+next.Revision);x.f.Rebind(next,AiProfile.Initial,x.b,x.a);
            Observe(x.f,x.b,x.a,View(600,176,p:next),p:next);var repriced=x.b.Capture().Reserved.Single();Assert.AreEqual(original.ExpiresTick,repriced.ExpiresTick);Assert.AreEqual(next.Revision,repriced.Action.ProfileRevision);
        }
        private const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static;
        private static byte[] Wire(AiDecisionArbiter a)
        {using(var s=new MemoryStream()){using(var w=new BinaryWriter(s,System.Text.Encoding.UTF8,true))typeof(AiDecisionArbiter).GetMethod("WriteState",Private).Invoke(a,new object[]{w});return s.ToArray();}}
        private static AiDecisionArbiter Restore(byte[] data,long tick,string owner)
        {var a=new AiDecisionArbiter(AiProfile.Initial);using(var s=new MemoryStream(data))using(var r=new BinaryReader(s))typeof(AiDecisionArbiter).GetMethod("ReadState",Private).Invoke(a,new object[]{r,tick,owner,71L,PlayableProfile.Default,AiProfile.Initial});return a;}
        private static object Memo(AiDecisionArbiter a)=>typeof(AiTechnologyFunding).GetMethod("Load",Private).Invoke(null,new object[]{a});
        private static void Save(AiDecisionArbiter a,object m)=>typeof(AiTechnologyFunding).GetMethod("Save",Private).Invoke(null,new[]{(object)a,m});
        private static void Validate(AiTechnologyFunding f,PlayableAiObservation o,AiBudgetLedger b,AiDecisionArbiter a)=>typeof(AiTechnologyFunding).GetMethod("Validate",Private).Invoke(f,new object[]{o,PlayableProfile.Default,b,a,Array.Empty<AiIntent>()});
        [Test] public void CurrentC9PartialExpiryForeignAndMalformedMemo_RejectBeforeArbiterMutation()
        {
            var x=Start();var o=View(0,176);Observe(x.f,x.b,x.a,o);var data=Wire(x.a);var restored=Restore(data,0,o.OwnerId);CollectionAssert.AreEqual(data,Wire(restored));Validate(x.f,o,x.b,restored);
            Assert.Throws<TargetInvocationException>(()=>Restore(data,0,"foreign"));
            var memo=Memo(x.a);memo.GetType().GetField("Expired",Private).SetValue(memo,true);Save(x.a,memo);
            var target=new AiDecisionArbiter(AiProfile.Initial);var before=Wire(target);var bad=Wire(x.a);
            using(var stream=new MemoryStream(bad))using(var reader=new BinaryReader(stream))Assert.Throws<TargetInvocationException>(()=>typeof(AiDecisionArbiter).GetMethod("ReadState",Private).Invoke(target,new object[]{reader,0L,o.OwnerId,71L,PlayableProfile.Default,AiProfile.Initial}));
            CollectionAssert.AreEqual(before,Wire(target),"malformed expired positive amount must reject before adding any history");
        }
        private static AiBudgetLedger LedgerRoundTrip(AiBudgetLedger b,long tick)
        {
            var type=typeof(AiBudgetLedger).Assembly.GetType("Spacewars.Runtime.AiBudgetStateWire");using(var stream=new MemoryStream())
            {using(var w=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))type.GetMethod("Write",Private).Invoke(null,new object[]{w,b});stream.Position=0;using(var r=new BinaryReader(stream))return(AiBudgetLedger)type.GetMethod("Read",Private).Invoke(null,new object[]{r,tick});}
        }
        [Test] public void SameCashOtherHoldRelease_GrowsPartialAmountWithoutRenewingSerializedDeadline()
        {
            var x=Start();var o=View(0,176);x.b.ObserveBank(176);var other=new AiBudgetEntry("other","other",new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,71,0,PlayableCommandKind.Stop),100,1,0,x.b.ReservationLifetimeTicks,"expiry",Array.Empty<string>(),o.ProfileId+"@"+o.ProfileRevision);Assert.True(x.b.Reserve(other));
            Observe(x.f,x.b,x.a,o);var original=x.b.Capture().Reserved.Single(AiTechnologyFunding.IsFunding);Assert.AreEqual(76,original.Amount);
            x.b=LedgerRoundTrip(x.b,0);x.a=Restore(Wire(x.a),0,o.OwnerId);Assert.True(x.b.Release(other.Id));Observe(x.f,x.b,x.a,View(600,176));var increased=x.b.Capture().Reserved.Single();Assert.AreEqual(176,increased.Amount);Assert.AreEqual(original.CreatedTick,increased.CreatedTick);Assert.AreEqual(original.ExpiresTick,increased.ExpiresTick);
            x.b=LedgerRoundTrip(x.b,600);x.a=Restore(Wire(x.a),600,o.OwnerId);
            for(long tick=original.ExpiresTick+1;tick<=original.ExpiresTick*3+1;tick+=300)
            {Observe(x.f,x.b,x.a,View(tick,176));Assert.IsEmpty(x.b.Capture().Reserved);x.b=LedgerRoundTrip(x.b,tick);x.a=Restore(Wire(x.a),tick,o.OwnerId);}
        }
        [Test] public void HeldSavings_RequestFullRoutesOnlyOnDecisionOrActualVisibleThreat()
        {
            var x=Start();var o=View(1,176);Observe(x.f,x.b,x.a,o);var type=typeof(AiBudgetLedger).Assembly.GetType("Spacewars.Runtime.PlayableAiOwnerLoop");var opening=PlayableAiOpeningComposition.Initialize(41,o.OwnerId,aiProfile:AiProfile.Initial);
            var loop=Activator.CreateInstance(type,Private,null,new object[]{PlayableProfile.Default,opening,71L,null},null);type.GetField("budget",Private).SetValue(loop,x.b);type.GetField("arbiter",Private).SetValue(loop,x.a);type.GetField("lastDecisionTick",Private).SetValue(loop,1L);
            bool Army(long tick)=>(bool)type.GetMethod("NeedsArmyRoutes",Private).Invoke(loop,new object[]{tick});
            PlayableSnapshot Snapshot(bool threat)=>new PlayableSnapshot(o.ProfileId,o.ProfileRevision,71,41,2,2,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,176,null,o.Entities.Concat(threat?new[]{new PlayableEntitySnapshot(101,PlayableOwner.Enemy,PlayableEntityKind.Tank,o.Buildings.Single(b=>b.Kind==PlayableBuildingKind.Headquarters).Position,100,false,0,0,0)}:Array.Empty<PlayableEntitySnapshot>()).ToArray(),o.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null);
            bool Defense(PlayableSnapshot snapshot)=>(bool)type.GetMethod("NeedsDefenseRoutes",Private).Invoke(loop,new object[]{snapshot});
            Assert.False(Army(2),"partial savings do not force unrelated all-army routes each clear tick");Assert.False(Defense(Snapshot(false)));Assert.True(Defense(Snapshot(true)));
            var defense=(AiDefensePlanner)type.GetField("defense",Private).GetValue(loop);var armies=(AiArmyRegistry)type.GetField("armies",Private).GetValue(loop);defense.Observe(PlayableAiObservation.From(Snapshot(true)),armies,PlayableProfile.Default);Assert.True(Defense(Snapshot(true)),"already observed defense still needs fresh coverage while savings held");
            Assert.True(Army(1+AiProfile.SecondsToTicks(AiProfile.Initial.DifficultyValue(AiDifficulty.Fighter,"decisionSeconds"),30)));
            x.f.Suppress(o,x.b,x.a);Assert.False(Defense(Snapshot(true)),"after release original observed-threat cadence remains");
        }
        [Test] public void SerializedMemoAndHoldWithOwnFactoryParent_RejectPhysicalS2Binding()
        {
            var x=Start();var o=View(0,176);Observe(x.f,x.b,x.a,o);var old=x.b.Capture().Reserved.Single();var memo=Memo(x.a);memo.GetType().GetField("Parent",Private).SetValue(memo,2);Save(x.a,memo);var id=(string)typeof(AiTechnologyFunding).GetMethod("Id",Private).Invoke(null,new[]{memo});var action=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,71,0,PlayableCommandKind.BuildAt,siteId:old.Action.SiteId,slotId:old.Action.SlotId,parentId:2,buildingKind:PlayableBuildingKind.ScientificCenter,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            x.b.Release(old.Id);Assert.True(x.b.Reserve(new AiBudgetEntry(id,old.Purpose,action,old.Amount,old.Priority,old.CreatedTick,old.ExpiresTick,old.ReleaseCondition,old.Claims,old.Terms)));var restored=Restore(Wire(x.a),0,o.OwnerId);x.b=LedgerRoundTrip(x.b,0);var before=Wire(restored);
            Assert.Throws<TargetInvocationException>(()=>Validate(x.f,o,x.b,restored));CollectionAssert.AreEqual(before,Wire(restored));Assert.AreEqual(176,x.b.Reserved,"reject does not consume/mutate foreign serialized state");
        }
        [Test] public void UrgentCostedRepair_ActualOwnerReviewReleasesSavingsBeforeAllocation()
        {
            var p=PlayableProfile.Default;var baseView=View(60,176);var factory=baseView.Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory);int health=Math.Max(1,(int)(p.FactoryHealth*AiProfile.Initial.Value("economy.repairPriorityHealthRatio"))/2);
            var life=new PlayableBuildingLifecycleSnapshot(false,0,false,false,0,0,null,null,1,0,false,p.FactoryCreditCost*p.BuildingRepairCostRatio*(1-health/(double)p.FactoryHealth),p.Revision);
            var bs=baseView.Buildings.Where(b=>b.Id!=factory.Id).Concat(new[]{new PlayableBuildingSnapshot(factory.Id,factory.Owner,factory.Kind,factory.Position,health,1,0,0,default(NavPoint),factory.SiteId,factory.SlotId,factory.ParentId,lifecycle:life)}).Concat(new[]{new PlayableBuildingSnapshot(4,PlayableOwner.Player,PlayableBuildingKind.Refinery,default(NavPoint),p.RefineryHealth,1,0,0,default(NavPoint),factory.SiteId,3,1),new PlayableBuildingSnapshot(5,PlayableOwner.Player,PlayableBuildingKind.Refinery,default(NavPoint),p.RefineryHealth,1,0,0,default(NavPoint),factory.SiteId,4,1)}).ToArray();
            var units=Enumerable.Range(10,(int)AiProfile.Initial.Value("opening.forceUnits")+2).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(id,0),p.TankHealth,false,0,0,0)).ToArray();
            var opening=Enumerable.Range(1,10000).Select(seed=>PlayableAiOpeningComposition.Initialize(seed,baseView.OwnerId,aiProfile:AiProfile.Initial)).First(v=>v.Opening==PlayableAiOpening.Safe);
            var snapshot=new PlayableSnapshot(p.ProfileId,p.Revision,71,opening.MatchSeed,60,60,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,176,null,units,bs,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:baseView.Sites.ToArray(),population:new PlayablePopulationSnapshot(units.Length*PlayableUnitRules.Population(p,PlayableEntityKind.Tank),0,p.ArmyCapacity),researchAvailability:baseView.ResearchAvailability.ToArray(),activeProfile:p,homeSiteId:baseView.HomeSiteId);
            var o=PlayableAiObservation.From(snapshot);var x=Start();Observe(x.f,x.b,x.a,o);Assert.Greater(x.b.Reserved,0);
            var type=typeof(AiBudgetLedger).Assembly.GetType("Spacewars.Runtime.PlayableAiOwnerLoop");var loop=Activator.CreateInstance(type,Private,null,new object[]{p,opening,71L,null},null);type.GetField("budget",Private).SetValue(loop,x.b);type.GetField("arbiter",Private).SetValue(loop,x.a);
            Assert.True(((AiPhaseCompositionPlanner)type.GetField("composition",Private).GetValue(loop)).ViableCover(o,p,AiProfile.Initial));Assert.False(((AiOpeningExecutor)type.GetField("openingExecutor",Private).GetValue(loop)).MissingEssentialEconomy(o,AiProfile.Initial));
            var repair=new AiInfrastructurePlanner(o.OwnerId).Plan(o,p,AiProfile.Initial).Single(c=>c.Legal&&c.Action.Kind==PlayableCommandKind.StartBuildingRepair);Assert.AreEqual(1,repair.Priority,"existing configured urgent health band");
            type.GetMethod("Review",Private).Invoke(loop,new object[]{snapshot,0L});Assert.IsEmpty(x.b.Capture().Reserved.Where(AiTechnologyFunding.IsFunding));Assert.True(x.b.Capture().Unpaid.Any(e=>e.Action.Kind==PlayableCommandKind.StartBuildingRepair&&e.Amount==(int)Math.Ceiling(life.RepairAllocation)),"actual owner arbitration admits costed allocation after savings release");Assert.Zero(x.b.PaidTotal);
        }
        [TestCase(false)][TestCase(true)] public void InvalidOrRejectedGoal_RealIncomeRecoversLegalTarget_NoCashReleaseRenewal(bool rejected)
        {
            int cash=rejected?600:176;var x=Start(cash);var o=View(0,cash);Observe(x.f,x.b,x.a,o);var original=x.b.Capture().Reserved.Single();
            if(rejected)
            {var i=new AiIntent("recovery-science","economy",original.Action,30,300,0,Array.Empty<string>(),reservationId:original.Id);var receipt=new AiReceiptIdentity(71,o.OwnerId,1,1);Assert.True(x.f.Accept(i,receipt,o,PlayableProfile.Default,x.b,x.a));x.b.Reconcile(i.Id,receipt,PlayableAiDeliveryStatus.Rejected);x.f.Terminal(i,PlayableAiDeliveryStatus.Rejected,x.b,x.a);}
            Observe(x.f,x.b,x.a,View(31,cash,occupied:!rejected));Assert.IsEmpty(x.b.Capture().Reserved);Assert.AreEqual(cash,x.b.Available);
            Observe(x.f,x.b,x.a,View(600,cash,occupied:!rejected));Assert.IsEmpty(x.b.Capture().Reserved,"reselection/free cash alone cannot renew at later cadence");x.b=LedgerRoundTrip(x.b,600);x.a=Restore(Wire(x.a),600,o.OwnerId);
            var fresh=View(601,cash+20,20,occupied:!rejected);Observe(x.f,x.b,x.a,fresh);Assert.IsNotEmpty(x.b.Capture().Reserved,"genuine observed new income must recover a legal goal after loss/rejection");var replacement=x.b.Capture().Reserved.Single();Assert.AreEqual(Math.Min(cash+20,600),replacement.Amount);Assert.AreEqual(601,replacement.CreatedTick);Assert.AreEqual(601+x.b.ReservationLifetimeTicks,replacement.ExpiresTick);if(!rejected)Assert.AreNotEqual(original.Action.SlotId,replacement.Action.SlotId);Assert.True(x.f.Valid(replacement,fresh,PlayableProfile.Default,x.b,x.a));Validate(x.f,fresh,x.b,x.a);
        }
        [Test] public void LostUsefulBranch_OnlyActualIncomePermitsReplacement()
        {
            var x=Start();Observe(x.f,x.b,x.a,View(0,176));var original=x.b.Capture().Reserved.Single();
            Observe(x.f,x.b,x.a,View(31,176,unit:PlayableEntityKind.Explorer));Assert.IsEmpty(x.b.Capture().Reserved);
            Observe(x.f,x.b,x.a,View(600,176,unit:PlayableEntityKind.Explorer));Assert.IsEmpty(x.b.Capture().Reserved);
            var fresh=View(601,196,20,unit:PlayableEntityKind.Explorer);Observe(x.f,x.b,x.a,fresh);var replacement=x.b.Capture().Reserved.Single();
            Assert.AreEqual(PlayableResearchKind.ExplorerAssaultGuns,x.f.Preferred(x.a));Assert.AreEqual(601,replacement.CreatedTick);Assert.AreEqual(196,replacement.Amount);Validate(x.f,fresh,x.b,x.a);
        }
        [TestCase(false)][TestCase(true)] public void ExpiredScience_BranchPivotRequiresExactPaidReceiptBeforePhysicalProgress(bool matched)
        {
            var x=Start(600);var o=View(0,600);Observe(x.f,x.b,x.a,o);var held=x.b.Capture().Reserved.Single();
            var old=new AiIntent("old-science","economy",held.Action,30,300,0,Array.Empty<string>(),reservationId:held.Id);var oldReceipt=new AiReceiptIdentity(71,o.OwnerId,1,1);
            Assert.True(x.f.Accept(old,oldReceipt,o,PlayableProfile.Default,x.b,x.a));
            var status=matched?PlayableAiDeliveryStatus.Applied:PlayableAiDeliveryStatus.Rejected;Assert.True(x.b.Reconcile(old.Id,oldReceipt,status));x.f.Terminal(old,status,x.b,x.a);
            if(!matched)
            {
                var otherReceipt=new AiReceiptIdentity(71,o.OwnerId,2,1);
                Assert.True(x.b.Accept(new AiBudgetEntry("other-science","economy",held.Action,300,1,1,31,"terminal",Array.Empty<string>(),held.Terms,otherReceipt)));
                Assert.True(x.b.Reconcile("other-science",otherReceipt,PlayableAiDeliveryStatus.Applied));
                Assert.AreNotEqual(oldReceipt.Id,x.b.Capture().Paid.Single().Receipt.Id,"same owner, site, slot, parent and amount still belong to a different receipt");
            }
            long tick=held.ExpiresTick+1;var pivot=View(tick,600,center:true,unit:PlayableEntityKind.Explorer);
            var plan=new AiTechnologyPlanner().Plan(pivot,PlayableProfile.Default,AiProfile.Initial,x.b.Capture());Assert.AreEqual(PlayableResearchKind.ExplorerAssaultGuns,plan.FundingGoal.Research,"old tank branch unavailable, new explorer branch useful");
            Observe(x.f,x.b,x.a,pivot);Assert.AreEqual(600,x.b.Liquid);Assert.AreEqual(0,pivot.SettledIncome);Assert.AreEqual(300,x.b.PaidTotal);Assert.IsEmpty(x.b.Capture().Unpaid);
            if(matched)
            {var replacement=x.b.Capture().Reserved.Single();Assert.AreEqual(tick,replacement.CreatedTick);Assert.AreEqual(PlayableResearchKind.ExplorerAssaultGuns,x.f.Preferred(x.a));Validate(x.f,pivot,x.b,x.a);}
            else
            {Assert.IsEmpty(x.b.Capture().Reserved,"unmatched physical center must not revive an expired goal before branch replacement clears the old receipt");Observe(x.f,x.b,x.a,View(tick+300,600,center:true,unit:PlayableEntityKind.Explorer));Assert.IsEmpty(x.b.Capture().Reserved);Validate(x.f,pivot,x.b,x.a);}
        }
        [Test] public void ExistingCenterWithoutScienceInvoice_RealConstructionProgressRenewsExpiredGoal()
        {
            var x=Start(600);Observe(x.f,x.b,x.a,View(0,600,center:true,progress:.1));var held=x.b.Capture().Reserved.Single();
            long tick=held.ExpiresTick+1;var grown=View(tick,600,center:true);Observe(x.f,x.b,x.a,grown);var renewed=x.b.Capture().Reserved.Single();
            Assert.AreEqual(held.Action.ResearchKind,renewed.Action.ResearchKind);Assert.AreEqual(held.Action.EntityIds.Single(),renewed.Action.EntityIds.Single());Assert.AreEqual(tick,renewed.CreatedTick);Assert.Zero(x.b.PaidTotal);Validate(x.f,grown,x.b,x.a);
        }
        [Test] public void AcceptedResearchInvoice_NewIncomeCannotPromiseSecondPayment()
        {
            var x=Start(600);var o=View(0,600,center:true);Observe(x.f,x.b,x.a,o);var held=x.b.Capture().Reserved.Single();
            var intent=new AiIntent("pending-research","research",held.Action,30,300,0,Array.Empty<string>(),reservationId:held.Id);var receipt=new AiReceiptIdentity(71,o.OwnerId,1,1);
            Assert.True(x.f.Accept(intent,receipt,o,PlayableProfile.Default,x.b,x.a));Observe(x.f,x.b,x.a,View(1,620,20,center:true));
            Assert.IsEmpty(x.b.Capture().Reserved);Assert.AreEqual(300,x.b.Unpaid);Assert.AreEqual(0,x.b.PaidTotal);
        }
        [Test] public void LiveResearchInvoiceC9_RequiresExactMemoCenterBeforeMutation()
        {
            var x=Start(600);var o=View(0,600,center:true,secondCenter:true);Observe(x.f,x.b,x.a,o);var held=x.b.Capture().Reserved.Single();Assert.AreEqual(3,held.Action.EntityIds.Single());
            var intent=new AiIntent("live-research","research",held.Action,30,300,0,Array.Empty<string>(),reservationId:held.Id);var receipt=new AiReceiptIdentity(71,o.OwnerId,1,1);Assert.True(x.f.Accept(intent,receipt,o,PlayableProfile.Default,x.b,x.a));
            var validate=typeof(AiTechnologyFunding).GetMethod("Validate",Private);validate.Invoke(x.f,new object[]{o,PlayableProfile.Default,x.b,x.a,new[]{intent}});var before=Wire(x.a);
            var forgedAction=new PlayableAiAction(held.Action.ActionId,o.OwnerId,o.ProfileId,o.ProfileRevision,71,o.SnapshotSequence,PlayableCommandKind.QueueResearch,new[]{4},researchKind:held.Action.ResearchKind,seed:o.Seed,sourceIdentity:held.Action.SourceIdentity);
            var forged=new AiIntent(intent.Id,"research",forgedAction,30,300,0,Array.Empty<string>(),reservationId:held.Id);
            Assert.Throws<TargetInvocationException>(()=>validate.Invoke(x.f,new object[]{o,PlayableProfile.Default,x.b,x.a,new[]{forged}}),"another own live center cannot replace exact accepted recipient");
            CollectionAssert.AreEqual(before,Wire(x.a));Assert.AreEqual(3,x.b.Capture().Unpaid.Single().Action.EntityIds.Single());Assert.AreEqual(300,x.b.Unpaid);Assert.IsEmpty(x.b.Capture().Reserved);
        }
        [Test] public void TypedMemoNoncanonicalBoolean_RejectsBeforeHistoryMutation()
        {
            var x=Start();var o=View(0,176);Observe(x.f,x.b,x.a,o);var args=new object[]{0L,0L};var payload=(string)typeof(AiDecisionArbiter).GetMethod("FundingMemory",Private,null,new[]{typeof(long).MakeByRefType(),typeof(long).MakeByRefType()},null).Invoke(x.a,args);var raw=Convert.FromBase64String(payload);
            using(var stream=new MemoryStream(raw))using(var reader=new BinaryReader(stream)){reader.ReadInt32();reader.ReadString();reader.ReadInt64();for(int n=0;n<10;n++)reader.ReadInt32();reader.ReadDouble();reader.ReadDouble();raw[stream.Position]=2;}
            typeof(AiDecisionArbiter).GetMethod("FundingMemory",Private,null,new[]{typeof(string),typeof(long),typeof(long)},null).Invoke(x.a,new object[]{Convert.ToBase64String(raw),args[0],args[1]});
            var target=new AiDecisionArbiter(AiProfile.Initial);var before=Wire(target);using(var stream=new MemoryStream(Wire(x.a)))using(var reader=new BinaryReader(stream))Assert.Throws<TargetInvocationException>(()=>typeof(AiDecisionArbiter).GetMethod("ReadState",Private).Invoke(target,new object[]{reader,0L,o.OwnerId,71L,PlayableProfile.Default,AiProfile.Initial}));CollectionAssert.AreEqual(before,Wire(target));
        }
        [Test] public void AwaitingScienceC9_RejectsUnboundPhysicalCenterAndWrongAcceptedReceipt()
        {
            var x=Start(600);var o=View(0,600);Observe(x.f,x.b,x.a,o);var e=x.b.Capture().Reserved.Single();var i=new AiIntent("science","economy",e.Action,30,300,0,Array.Empty<string>(),reservationId:e.Id);var r=new AiReceiptIdentity(71,o.OwnerId,1,1);
            Assert.True(x.f.Accept(i,r,o,PlayableProfile.Default,x.b,x.a));Validate(x.f,o,x.b,x.a);
            var data=Wire(x.a);var restored=Restore(data,0,o.OwnerId);CollectionAssert.AreEqual(data,Wire(restored));
            var m=Memo(restored);m.GetType().GetField("BuildReceipt",Private).SetValue(m,new AiReceiptIdentity(71,o.OwnerId,1,2));Save(restored,m);
            Assert.Throws<TargetInvocationException>(()=>Validate(x.f,View(0,300,center:true),x.b,restored),"same slot physical center cannot replace exact receipt");
            x.b.Reconcile(i.Id,r,PlayableAiDeliveryStatus.Applied);Observe(x.f,x.b,x.a,View(1,300,center:true));Validate(x.f,View(1,300,center:true),x.b,x.a);
        }
        [Test] public void AtomicAccept_RejectsOverlappingInvoiceAndRemainderClaimsWithoutMutation()
        {
            var x=Start(600);var o=View(0,600);Observe(x.f,x.b,x.a,o);var held=x.b.Capture().Reserved.Single();
            var invoice=new AiBudgetEntry("invoice","economy",held.Action,300,1,0,30,"terminal",new[]{"same"},held.Terms,new AiReceiptIdentity(71,o.OwnerId,1,1));
            var remainder=new AiBudgetEntry("remainder","research",held.Action,300,1,0,held.ExpiresTick,"expiry",new[]{"same"},held.Terms);
            Assert.False(x.b.AcceptReserved(invoice,held.Id,remainder));Assert.AreEqual(600,x.b.Reserved);Assert.AreEqual(0,x.b.Unpaid);Assert.AreEqual(0,x.b.Capture().AdmittedDecision);
        }
        [Test] public void MemoOutsidePruning_RetainsFull128OrdinaryHistories()
        {
            var x=Start();Observe(x.f,x.b,x.a,View(0,176));
            for(int n=0;n<160;n++){var o=View(n+1,176);x.a.Select(new[]{new AiIntent("ordinary-"+n,"scout",new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,71,0,PlayableCommandKind.Stop),2000,0,0,Array.Empty<string>())},o.Tick,176,0,1,x.b);}
            var history=(System.Collections.IDictionary)typeof(AiDecisionArbiter).GetField("history",Private).GetValue(x.a);Assert.AreEqual(129,history.Count);Assert.NotNull(Memo(x.a));
        }
    }
}
