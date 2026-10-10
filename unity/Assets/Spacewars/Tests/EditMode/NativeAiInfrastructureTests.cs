using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using Spacewars.Presentation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiInfrastructureTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        private static object Get(object o,string n)=>o.GetType().GetField(n,F).GetValue(o);
        private static object Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,F).Invoke(o,a);
        private static object Owner(PlayableAuthorityTick a)=>((System.Collections.IEnumerable)Get(Get(a,"scheduler"),"owners")).Cast<object>().Single();
        private static AiBudgetLedger Ledger(PlayableAuthorityTick a)=>(AiBudgetLedger)Get(Owner(a),"budget");
        private static AiInfrastructurePlanner Planner(PlayableAuthorityTick a)=>(AiInfrastructurePlanner)Get(Owner(a),"infrastructure");
        private static PlayableAiOwnerCheckpoint Checkpoint(PlayableAuthorityTick a)=>(PlayableAiOwnerCheckpoint)Owner(a).GetType().GetProperty("Checkpoint",F).GetValue(Owner(a));
        private static PlayableSnapshot View(PlayableAuthorityTick a,OfflineMatchConfiguration c)=>(PlayableSnapshot)Call(Get(a,"domain"),"PlayerSnapshot",a.Tick,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed,PlayableOwner.Player);
        private static void Step(PlayableAuthorityTick a,UnityHostRouteService host)
        {for(int i=0;i<512;i++){if(a.TryAdvance())return;host.Service(a,64);}Assert.Fail("real route barrier incomplete");}
        private static OfflineMatchConfiguration Config(bool conversion=false)
        {
            var p=PlayableProfile.Default;
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-24,0),new NavPoint(-24,-6),pin:1),new OfflineStart("east",2,new NavPoint(24,24),new NavPoint(18,24),pin:2)};
            var home=new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,p);
            var slots=home.Slots.Take(2).ToArray();
            var sites=new[]{new TerritorySite(1,home.Kind,home.Position,slots),new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,Array.Empty<TerritorySlot>())};
            var buildings=slots.Select(s=>new OfflineScenarioBuilding(1,conversion?PlayableBuildingKind.ScientificCenter:s.Id==1?PlayableBuildingKind.Factory:PlayableBuildingKind.Refinery,1,s.Id,s.Position,s.Heading)).ToArray();
            return new OfflineMatchConfiguration(p,"e5-infrastructure","e5-public-flat-v1","UnityHostRouteService",19092026,new[]{new OfflineParticipant("west-owner",1,1,OfflineControl.Ai),new OfflineParticipant("east-owner",2,2,OfflineControl.Human)},starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},scenarioBuildings:buildings);
        }
        private static int Factory(PlayableAuthorityTick a,OfflineMatchConfiguration c)=>View(a,c).Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory).Id;
        private static void Damage(PlayableAuthorityTick a,int id,double amount)=>Call(Get(a,"domain"),"Damage",id,(int)amount);
        [Test] public void RepairActualSlicesProtectedAllocationAndCompletion()
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);int id=Factory(a,c);Damage(a,id,c.Profile.FactoryHealth*.55);
            double initial=View(a,c).ExactCredits;double income=View(a,c).SettledIncome;long paid=Ledger(a).PaidTotal;double expected=c.Profile.FactoryCreditCost*c.Profile.BuildingRepairCostRatio*((int)(c.Profile.FactoryHealth*.55)/(double)c.Profile.FactoryHealth);
            bool admitted=false,active=false,partial=false;long start=0;double lastRepairPaid=-1;
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<1200;i++)
                {
                    Step(a,host);var v=View(a,c);var b=v.Buildings.Single(x=>x.Id==id);var ledger=Ledger(a);
                    var entry=ledger.Capture().Unpaid.SingleOrDefault(e=>e.Action.Kind==PlayableCommandKind.StartBuildingRepair);
                    if(entry!=null&&!entry.AwaitingPayment){admitted=true;Assert.AreEqual((int)Math.Ceiling(expected),entry.Amount);Assert.Zero(ledger.RepairPaidTotal);}
                    if(entry?.AwaitingPayment==true)
                    {
                        active=true;if(start==0)start=a.Tick;
                        Assert.True(b.PrivateState.Lifecycle.Repairing);
                        Assert.AreEqual((int)Math.Ceiling(b.PrivateState.Lifecycle.RepairAllocation),entry.Amount);
                        Assert.LessOrEqual(ledger.Available,Math.Max(0,ledger.Liquid-entry.Amount));
                        partial|=entry.ExactSettled>0;
                    }
                    if(lastRepairPaid!=ledger.RepairPaidTotal)
                    {TestContext.WriteLine("repair slice tick="+a.Tick+" bank="+v.ExactCredits+" settledIncome="+v.SettledIncome+" commandPaid="+ledger.PaidTotal+" repairPaid="+ledger.RepairPaidTotal+" allocation="+entry?.Amount+" available="+ledger.Available+" receipt="+entry?.Receipt.Id);lastRepairPaid=ledger.RepairPaidTotal;}
                    Assert.AreEqual(initial+v.SettledIncome-income-(ledger.PaidTotal-paid)-ledger.RepairPaidTotal,v.ExactCredits,1e-7,"actual bank = settled income - command payments - exact repair slices");
                    if(start>0&&!b.PrivateState.Lifecycle.Repairing&&b.Health==c.Profile.FactoryHealth)break;
                }
            }
            Assert.True(admitted);Assert.True(active);Assert.True(partial);Assert.AreEqual(expected,Ledger(a).RepairPaidTotal,1e-7);
            Assert.False(Ledger(a).Capture().Unpaid.Any(e=>e.Action.Kind==PlayableCommandKind.StartBuildingRepair));
            Assert.AreEqual(1,Checkpoint(a).Records.Count(r=>r.Kind==PlayableCommandKind.StartBuildingRepair&&r.Status==PlayableAiDeliveryStatus.Applied));
            TestContext.WriteLine("E2-10 start="+start+" end="+a.Tick+" bank="+initial+"->"+View(a,c).ExactCredits+" exact repair="+Ledger(a).RepairPaidTotal+" expected="+expected);
            var b2=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);CollectionAssert.AreEqual(a.CaptureBytes(),b2.CaptureBytes());
        }
        [Test] public void RepairChangedDamageRejectsPendingThenBoundedReplans()
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);int id=Factory(a,c);Damage(a,id,c.Profile.FactoryHealth*.2);
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<400&&!Ledger(a).Capture().Unpaid.Any(e=>e.Action.Kind==PlayableCommandKind.StartBuildingRepair&&!e.AwaitingPayment);i++)Step(a,host);
                Assert.True(Ledger(a).Capture().Unpaid.Any(e=>e.Action.Kind==PlayableCommandKind.StartBuildingRepair));
                Damage(a,id,c.Profile.FactoryHealth*.1);
                for(int i=0;i<50;i++)Step(a,host);
                Assert.True(Checkpoint(a).Records.Any(r=>r.Kind==PlayableCommandKind.StartBuildingRepair&&r.Status==PlayableAiDeliveryStatus.Rejected));
                Assert.Zero(Ledger(a).RepairPaidTotal);Assert.False(Ledger(a).Capture().Unpaid.Any(e=>e.Action.Kind==PlayableCommandKind.StartBuildingRepair));
                for(int i=0;i<500&&!Checkpoint(a).Records.Any(r=>r.Kind==PlayableCommandKind.StartBuildingRepair&&r.Status==PlayableAiDeliveryStatus.Applied);i++)Step(a,host);
                Assert.AreEqual(1,Checkpoint(a).Records.Count(r=>r.Kind==PlayableCommandKind.StartBuildingRepair&&r.Status==PlayableAiDeliveryStatus.Rejected));
                Assert.True(Checkpoint(a).Records.Any(r=>r.Kind==PlayableCommandKind.StartBuildingRepair&&r.Status==PlayableAiDeliveryStatus.Applied));
            }
        }
        [TestCase(false)][TestCase(true)] public void RepairActiveLossOrHumanCancellationReleasesOnlyRemaining(bool loss)
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);int id=Factory(a,c);Damage(a,id,c.Profile.FactoryHealth*.55);
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<600&&Ledger(a).RepairPaidTotal==0;i++)Step(a,host);
                double settled=Ledger(a).RepairPaidTotal;Assert.Greater(settled,0);
                if(loss)Damage(a,id,10000);
                else Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,(long)Get(Get(a,"domain"),"nextAiSequence"),"west-owner",PlayableCommandKind.CancelBuildingRepair,new[]{id})).Status);
                Step(a,host);Assert.AreEqual(settled,Ledger(a).RepairPaidTotal);Assert.False(Ledger(a).Capture().Unpaid.Any(e=>e.Action.Kind==PlayableCommandKind.StartBuildingRepair));
                var b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
            }
        }
        [Test] public void RepairPauseRestoreAndProfilePreservePaidTerms()
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);int id=Factory(a,c);Damage(a,id,c.Profile.FactoryHealth*.55);
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<600&&Ledger(a).RepairPaidTotal==0;i++)Step(a,host);
                var bytes=a.CaptureBytes();var b=PlayableAuthorityTick.RestoreBytes(bytes,c);CollectionAssert.AreEqual(bytes,b.CaptureBytes());
                long tick=a.Tick;double paid=Ledger(a).RepairPaidTotal;Assert.True(a.TryAdvance(true));Assert.True(b.TryAdvance(true));Assert.AreEqual(tick,a.Tick);Assert.AreEqual(paid,Ledger(a).RepairPaidTotal);
                for(int i=0;i<90;i++){Step(a,host);Step(b,host);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());}
                var data=c.Profile.CopyData();data.revision++;data.buildingRepairCostRatio=.9;var next=PlayableProfile.Create(data);
                string terms=Ledger(a).Capture().Unpaid.Single(e=>e.Action.Kind==PlayableCommandKind.StartBuildingRepair).Terms;
                Assert.Null(a.ApplyProfile(next));Assert.AreEqual(terms,Ledger(a).Capture().Unpaid.Single(e=>e.Action.Kind==PlayableCommandKind.StartBuildingRepair).Terms);
                for(int i=0;i<900&&Ledger(a).Capture().Unpaid.Any(e=>e.Action.Kind==PlayableCommandKind.StartBuildingRepair);i++)Step(a,host);
                Assert.AreEqual(c.Profile.FactoryCreditCost*c.Profile.BuildingRepairCostRatio*((int)(c.Profile.FactoryHealth*.55)/(double)c.Profile.FactoryHealth),Ledger(a).RepairPaidTotal,1e-7);
                a.Stop();Assert.Zero(Ledger(a).Reserved);Assert.Zero(Planner(a).Capture().SiteId);
            }
        }
        [Test] public void SaleActualRefundDemolitionReplacementAndNoSamePurposeRebuild()
        {
            var c=Config(true);var a=new PlayableAuthorityTick(c,71);double initial=View(a,c).ExactCredits,initialIncome=View(a,c).SettledIncome;
            bool selected=false,sale=false,free=false,build=false;int sold=0,slot=0;double refund=0;long saleTick=0,buildTick=0;
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<900;i++)
                {
                    Step(a,host);var v=View(a,c);var state=Planner(a).Capture();var pending=Ledger(a).Capture().Unpaid.SingleOrDefault(e=>e.Action.Kind==PlayableCommandKind.SellBuilding);
                    if(pending!=null){selected=true;sold=state.VictimId;slot=state.SlotId;refund=v.Buildings.Single(x=>x.Id==sold).PrivateState.Lifecycle.Refund;Assert.AreEqual(initial+v.SettledIncome-initialIncome,v.ExactCredits,1e-7,"future refund excluded");}
                    if(!sale&&state.SaleSettled)
                    {sale=true;saleTick=a.Tick;Assert.AreEqual(initial+v.SettledIncome-initialIncome+refund,v.ExactCredits,1e-7);Assert.True(v.Buildings.Single(x=>x.Id==sold).PrivateState.Lifecycle.Selling);Assert.False(v.Buildings.Any(x=>x.Kind==PlayableBuildingKind.Factory));}
                    if(sale&&!v.Buildings.Any(x=>x.Id==sold))free=true;
                    var replacement=v.Buildings.FirstOrDefault(x=>x.SiteId==1&&x.SlotId==slot&&x.Kind==PlayableBuildingKind.Factory);
                    if(replacement!=null){build=true;buildTick=a.Tick;Assert.True(free);Assert.GreaterOrEqual(buildTick-saleTick,(long)Math.Ceiling(c.Profile.BuildingSaleDemolitionSec*30));Assert.AreEqual(c.Profile.FactoryCreditCost,Ledger(a).Capture().Paid.Single(e=>e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.SlotId==slot).Amount);break;}
                }
                Assert.True(selected);Assert.True(sale);Assert.True(free);Assert.True(build);
                Assert.AreNotEqual(PlayableBuildingKind.ScientificCenter,Planner(a).Capture().ReplacementKind);
                for(int i=0;i<300;i++)
                {Step(a,host);var plan=new AiEconomyPlanner().Plan(PlayableAiObservation.From(View(a,c)),c.Profile);Assert.True(Planner(a).ClaimsSlot(1,slot));Assert.False(plan.Candidates.Any(x=>x.Legal&&x.Action.SiteId==1&&x.Action.SlotId==slot));Assert.False(View(a,c).Buildings.Any(x=>x.SlotId==slot&&x.Kind==PlayableBuildingKind.ScientificCenter));}
                var b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
                TestContext.WriteLine("E2-11/12 sale="+saleTick+" build="+buildTick+" refund="+refund+" bank="+View(a,c).ExactCredits+" slot="+slot+" source="+Planner(a).Capture().SourceKind+" replacement="+Planner(a).Capture().ReplacementKind);
            }
        }
        [Test] public void SaleProposalIsDetachedAndSurvivalRepairAssetsAreExcluded()
        {
            var c=Config(true);var a=new PlayableAuthorityTick(c,71);var p=Planner(a);var o=PlayableAiObservation.From(View(a,c));var before=a.CaptureBytes();
            Assert.True(p.Plan(o,c.Profile,AiProfile.Initial).Any(x=>x.Legal&&x.Action.Kind==PlayableCommandKind.SellBuilding));
            foreach(var candidate in p.Plan(o,c.Profile,AiProfile.Initial).Where(x=>x.Legal))Call(Call(p,"Fork"),"Admit",o,candidate.Action,Ledger(a));
            CollectionAssert.AreEqual(before,a.CaptureBytes(),"discarded proposals leave pending, bank, slot/RNG unchanged");
            int id=o.Buildings.First(b=>b.Kind==PlayableBuildingKind.ScientificCenter).Id;Damage(a,id,1);
            var changed=PlayableAiObservation.From(View(a,c));Assert.False(p.Plan(changed,c.Profile,AiProfile.Initial).Any(x=>x.Legal&&x.Action.Kind==PlayableCommandKind.SellBuilding&&x.Action.EntityIds.Contains(id)));
            Assert.False(p.Plan(changed,c.Profile,AiProfile.Initial).Any(x=>x.Action.Kind==PlayableCommandKind.SellBuilding&&changed.Buildings.Any(b=>x.Action.EntityIds.Contains(b.Id)&&TerritoryRules.Center(b.Kind))));
        }
        [Test] public void SalePendingCancellationAndExpiryReleaseSlot()
        {
            var c=Config(true);var a=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<100&&!Ledger(a).Capture().Unpaid.Any(e=>e.Action.Kind==PlayableCommandKind.SellBuilding);i++)Step(a,host);
                var bytes=a.CaptureBytes();var b=PlayableAuthorityTick.RestoreBytes(bytes,c);CollectionAssert.AreEqual(bytes,b.CaptureBytes());
                Assert.True(a.TryAdvance(true));Assert.Zero(Planner(a).Capture().SiteId);Assert.Zero(Ledger(a).Reserved);
                for(int i=0;i<100&&!Planner(a).Capture().SaleSettled;i++)Step(a,host);
                long expires=Planner(a).Capture().ExpiresTick;Assert.Greater(expires,a.Tick);
                for(long i=a.Tick;i<=expires+1;i++)Step(a,host);
                Assert.Zero(Planner(a).Capture().SiteId);Assert.False(Ledger(a).Capture().Reserved.Any(e=>e.Purpose=="infrastructure-conversion"));
            }
        }

        [Test] public void ConversionAcceptedReplacementRestoresExactPendingAndPaidSuffix()
        {
            var c=Config(true);var a=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<500&&!Ledger(a).Capture().Unpaid.Any(e=>e.Action.Kind==PlayableCommandKind.BuildAt&&e.Purpose=="infrastructure");i++)Step(a,host);
                var payment=Ledger(a).Capture().Unpaid.Single(e=>e.Action.Kind==PlayableCommandKind.BuildAt&&e.Purpose=="infrastructure");
                Assert.True(Planner(a).Capture().SaleSettled);Assert.False(Ledger(a).Capture().Reserved.Any(e=>e.Purpose=="infrastructure-conversion"));
                var o=PlayableAiObservation.From(View(a,c));var state=Planner(a).Capture();
                var sourceRebuild=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.BuildAt,siteId:state.SiteId,slotId:state.SlotId,parentId:state.ParentId,buildingKind:state.SourceKind,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
                Assert.Null(AiEconomyAdmission.Reject(o,c.Profile,sourceRebuild),"same-purpose rebuild is individually legal/affordable after demolition");
                var competitor=new AiIntent("competing-source-rebuild","economy",sourceRebuild,o.Tick+100,TerritoryRules.Cost(c.Profile,state.SourceKind),0,new[]{"slot:"+state.SiteId+":"+state.SlotId});
                var arbiter=new AiDecisionArbiter(AiProfile.Initial);Assert.IsEmpty(arbiter.Select(new[]{competitor},o.Tick,o.Credits,0,1,Ledger(a)));Assert.AreEqual("reserved claim conflict",arbiter.Rejections[competitor.Id]);
                var bytes=a.CaptureBytes();var b=PlayableAuthorityTick.RestoreBytes(bytes,c);CollectionAssert.AreEqual(bytes,b.CaptureBytes());
                for(int i=0;i<120;i++){Step(a,host);Step(b,host);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());}
                Assert.True(Checkpoint(a).Records.Any(r=>r.ReceiptIdentity.Id==payment.Receipt.Id&&r.Status==PlayableAiDeliveryStatus.Applied));
                Assert.AreEqual(c.Profile.FactoryCreditCost,Ledger(a).Capture().Paid.Single(e=>e.Receipt.Id==payment.Receipt.Id).Amount);
                TestContext.WriteLine("conversion pending receipt="+payment.Receipt.Id+" amount="+payment.Amount+" slot="+payment.Action.SlotId+" actual suffix paid="+Ledger(a).PaidTotal);
            }
        }
        [Test] public void ConversionGameAndAiProfileRebindKeepsSaleTermsAndClipsHorizon()
        {
            var c=Config(true);var a=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<200&&!Planner(a).Capture().SaleSettled;i++)Step(a,host);
                var original=Planner(a).Capture();Assert.True(original.SaleSettled);double bank=View(a,c).ExactCredits;
                var data=c.Profile.CopyData();data.revision++;data.factoryCreditCost+=10;var p=PlayableProfile.Create(data);
                var aiData=AiProfile.Initial.CopyData();aiData.revision++;aiData.fields.Single(f=>f.path=="economy.reservationExpirySeconds").value=10;var ai=new AiProfile(aiData);
                Assert.Null(a.ApplyProfile(p,ai));Assert.AreEqual(bank,View(a,c).ExactCredits);
                Assert.AreEqual(original.CreatedTick+300,Planner(a).Capture().ExpiresTick);
                var rebound=new OfflineMatchConfiguration(p,c.SourceIdentity,c.MapIdentity,c.RouteProvenance,c.Seed,c.Roster.ToArray(),c.Starts.ToArray(),c.Sites.ToArray(),c.Obstacles.ToArray(),new double[,]{{0,100},{100,0}},scenarioBuildings:c.ScenarioBuildings.ToArray());
                var bytes=a.CaptureBytes();var b=PlayableAuthorityTick.RestoreBytes(bytes,rebound,ai);CollectionAssert.AreEqual(bytes,b.CaptureBytes());
                for(int i=0;i<120;i++){Step(a,host);Step(b,host);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());}
                Assert.True(Ledger(a).Capture().Paid.Any(e=>e.Purpose=="infrastructure"&&e.Action.Kind==PlayableCommandKind.BuildAt&&e.Amount==p.FactoryCreditCost));
            }
        }
        [TestCase("owner")][TestCase("source")][TestCase("generation")]
        public void ForeignInfrastructureCallbackCannotClearActualPending(string mutation)
        {
            var c=Config(true);var a=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<100&&!Ledger(a).Capture().Unpaid.Any(e=>e.Action.Kind==PlayableCommandKind.SellBuilding);i++)Step(a,host);
                var planner=Planner(a);long id=(long)planner.GetType().GetProperty("PendingId",F).GetValue(planner);var entry=Ledger(a).Capture().Unpaid.Single(e=>e.Action.Kind==PlayableCommandKind.SellBuilding);
                var before=a.CaptureBytes();var identity=new AiReceiptIdentity(mutation=="generation"?72:71,mutation=="owner"?"east-owner":"west-owner",entry.Receipt.DecisionOrdinal,entry.Receipt.ActionOrdinal);
                Call(planner,"ObserveReceipt",new PlayableAiTraceRecord("fixture",id,a.Tick,0,0,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"foreign",mutation=="owner"?"east-owner":"west-owner",mutation=="source"?"foreign":PlayableAiOpeningComposition.SourceIdentity,identity,"infrastructure",PlayableCommandKind.SellBuilding));
                CollectionAssert.AreEqual(before,a.CaptureBytes());Assert.False(planner.Capture().SaleSettled);
            }
        }
        [TestCase(AiDifficulty.Recruit)][TestCase(AiDifficulty.Fighter)][TestCase(AiDifficulty.Veteran)]
        public void EveryDifficultyUsesOrdinaryRepairAndSaleWithProfileCadence(AiDifficulty difficulty)
        {
            foreach(bool conversion in new[]{false,true})
            {
                var c=Config(conversion);var a=new PlayableAuthorityTick(c,71,difficulty:difficulty);
                if(!conversion)Damage(a,Factory(a,c),c.Profile.FactoryHealth*.55);
                using(var host=new UnityHostRouteService())
                {
                    var kind=conversion?PlayableCommandKind.SellBuilding:PlayableCommandKind.StartBuildingRepair;
                    for(int i=0;i<700&&!Checkpoint(a).Records.Any(r=>r.Kind==kind&&r.Status==PlayableAiDeliveryStatus.Applied);i++)Step(a,host);
                    var receipt=Checkpoint(a).Records.First(r=>r.Kind==kind&&r.Status==PlayableAiDeliveryStatus.Applied);
                    Assert.AreEqual(AiProfile.SecondsToTicks(AiProfile.Initial.DifficultyValue(difficulty,"reactionSeconds"),30),receipt.ApplicationTick-long.Parse(receipt.ObservationIdentity.Split(':')[5]));
                    Assert.AreEqual(difficulty,Checkpoint(a).Difficulty);TestContext.WriteLine("difficulty="+difficulty+" kind="+kind+" tick="+receipt.ApplicationTick+" due="+receipt.DueTick);
                }
            }
        }

        [TestCase("purpose")][TestCase("target")][TestCase("owner")][TestCase("reservation")][TestCase("unknown-policy")]
        public void CorruptConversionPendingBindingsAreRejected(string mutation)
        {
            var c=Config(true);var a=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<500&&!Ledger(a).Capture().Unpaid.Any(e=>e.Action.Kind==PlayableCommandKind.BuildAt&&e.Purpose=="infrastructure");i++)Step(a,host);
                var e=Ledger(a).Capture().Unpaid.Single(x=>x.Action.Kind==PlayableCommandKind.BuildAt&&x.Purpose=="infrastructure");
                var pending=((System.Collections.IEnumerable)Get(Owner(a),"pendingItems")).Cast<object>().Single(x=>((AiIntent)Get(x,"Intent")).Id==e.Id);
                var intent=(AiIntent)Get(pending,"Intent");
                if(mutation=="reservation"||mutation=="unknown-policy")
                {
                    string policy=mutation=="unknown-policy"?"economy":intent.Policy;
                    var changed=new AiIntent(intent.Id,policy,intent.Action,intent.ExpiresTick,intent.Credits,intent.Population,intent.Claims,intent.Priority,intent.Utility,intent.Conditions,intent.Reason,intent.StartupFund,mutation=="reservation"?"factory-startup:1:1":intent.ReservationId,intent.Overdue);
                    pending.GetType().GetField("Intent",F).SetValue(pending,changed);
                    if(mutation=="unknown-policy")pending.GetType().GetField("PolicyName",F).SetValue(pending,policy);
                }
                else
                {
                    var x=e.Action;
                    var action=new PlayableAiAction(x.ActionId,mutation=="owner"?"east-owner":x.PlayerId,x.ProfileId,x.ProfileRevision,x.Generation,x.SnapshotSequence,x.Kind,x.CopyEntityIds(),siteId:x.SiteId,slotId:mutation=="target"?2:x.SlotId,buildingKind:x.BuildingKind,parentId:x.ParentId,seed:x.Seed,sourceIdentity:x.SourceIdentity);
                    var changed=new AiBudgetEntry(e.Id,mutation=="purpose"?"production":e.Purpose,action,e.Amount,e.Priority,e.CreatedTick,e.ExpiresTick,e.ReleaseCondition,e.Claims,e.Terms,e.Receipt);
                    ((System.Collections.Generic.IDictionary<string,AiBudgetEntry>)Get(Ledger(a),"unpaid"))[e.Id]=changed;
                }
                if(mutation=="unknown-policy")
                {var exception=Assert.Throws<TargetInvocationException>(()=>Call(Owner(a),"ValidateBudget",Get(a,"domain")));Assert.IsInstanceOf<ArgumentException>(exception.InnerException);}
                else Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c));
            }
        }
        [Test] public void HumanCancelAndRestartRepairDoesNotInheritOldPaymentReceipt()
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);int id=Factory(a,c);Damage(a,id,c.Profile.FactoryHealth*.55);
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<600&&Ledger(a).RepairPaidTotal==0;i++)Step(a,host);
                double paid=Ledger(a).RepairPaidTotal;
                Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,(long)Get(Get(a,"domain"),"nextAiSequence"),"west-owner",PlayableCommandKind.CancelBuildingRepair,new[]{id})).Status);
                Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,(long)Get(Get(a,"domain"),"nextAiSequence"),"west-owner",PlayableCommandKind.StartBuildingRepair,new[]{id})).Status);
                Step(a,host);Assert.AreEqual(paid,Ledger(a).RepairPaidTotal);Assert.False(Ledger(a).Capture().Unpaid.Any(e=>e.Action.Kind==PlayableCommandKind.StartBuildingRepair));
                Assert.True(Ledger(a).Capture().Reserved.Any(e=>e.Purpose=="external-repair"&&e.Amount>0));
                var b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
            }
        }

        private static OfflineMatchConfiguration PartialConfig()
        {
            var c=Config(true);var p=c.Profile;var home=new TerritorySite(1,PlayableBuildingKind.Headquarters,c.Starts[0].Position,p);
            var slots=home.Slots.Take(3).ToArray();var sites=new[]{new TerritorySite(1,home.Kind,home.Position,slots),c.Sites[1]};
            var buildings=slots.Select(t=>new OfflineScenarioBuilding(1,t.Id==1?PlayableBuildingKind.Factory:PlayableBuildingKind.ScientificCenter,1,t.Id,t.Position,t.Heading)).ToArray();
            return new OfflineMatchConfiguration(p,c.SourceIdentity,c.MapIdentity,c.RouteProvenance,c.Seed,c.Roster.ToArray(),c.Starts.ToArray(),sites,c.Obstacles.ToArray(),new double[,]{{0,100},{100,0}},scenarioBuildings:buildings);
        }
        [Test] public void PartialActualRefundProtectedAgainstAffordableProductionUntilReplacementPaid()
        {
            var c=PartialConfig();var a=new PlayableAuthorityTick(c,71);var initial=View(a,c);int victim=initial.Buildings.Where(b=>b.Kind==PlayableBuildingKind.ScientificCenter).OrderBy(b=>b.Id).First().Id;
            // Frozen gameplay defaults, real damage/refund; fixture bank is an initial input.
            foreach(var science in initial.Buildings.Where(b=>b.Kind==PlayableBuildingKind.ScientificCenter))Damage(a,science.Id,(int)(c.Profile.ScienceHealth*.3));Call(Get(a,"domain"),"AddCredits",PlayableOwner.Player,-initial.ExactCredits);
            double refund=View(a,c).Buildings.Single(b=>b.Id==victim).PrivateState.Lifecycle.Refund;Assert.Less(refund,c.Profile.RefineryCreditCost);
            bool held=false,competitor=false,ready=false,restoredPartial=false;long saleTick=0,paidTick=0;double last=-1;double initialIncome=View(a,c).SettledIncome;
            using(var host=new UnityHostRouteService())
            {
                for(int n=0;n<1600;n++)
                {
                    Step(a,host);var v=View(a,c);var state=Planner(a).Capture();var ledger=Ledger(a);
                    if(state.SaleSettled&&saleTick==0)
                    {
                        saleTick=a.Tick;Assert.AreEqual(v.SettledIncome-initialIncome+refund,v.ExactCredits,1e-7);Assert.AreEqual(victim,state.VictimId);
                    }
                    if(saleTick==0)continue;
                    var fund=ledger.Capture().Reserved.SingleOrDefault(e=>e.Purpose=="infrastructure-conversion");
                    if(fund!=null&&fund.Amount<c.Profile.RefineryCreditCost)
                    {
                        held=true;TestContext.WriteLine("partial proof bank="+v.ExactCredits+" refund="+refund+" quote="+c.Profile.RefineryCreditCost+" held="+fund.Amount+" legal production="+new AiEconomyPlanner().Plan(PlayableAiObservation.From(v),c.Profile).Candidates.Any(x=>x.Legal&&x.Policy=="production"));Assert.Greater(fund.Amount,0,"actual partial settled money must be protected, not a zero placeholder");
                        Assert.AreEqual(Math.Min(v.Credits,c.Profile.RefineryCreditCost),fund.Amount);
                        if(!restoredPartial)
                        {var bytes=a.CaptureBytes();var restored=PlayableAuthorityTick.RestoreBytes(bytes,c);CollectionAssert.AreEqual(bytes,restored.CaptureBytes());restoredPartial=true;}
                        var production=new AiEconomyPlanner().Plan(PlayableAiObservation.From(v),c.Profile).Candidates.FirstOrDefault(x=>x.Legal&&x.Policy=="production");
                        if(production!=null)
                        {
                            competitor=true;var proposed=(AiIntent)Call(Owner(a),"Proposal","production",production.Action,PlayableAiObservation.From(v),0);
                            var arbiter=new AiDecisionArbiter(AiProfile.Initial);Assert.IsEmpty(arbiter.Select(new[]{proposed},v.Tick,v.Credits,Math.Max(0,v.Population.Capacity-v.Population.Living-v.Population.Reserved),1,ledger));Assert.AreEqual("ledger budget unavailable",arbiter.Rejections[proposed.Id]);
                        }
                        if(last!=fund.Amount){TestContext.WriteLine("partial conversion tick="+a.Tick+" bank="+v.ExactCredits+" actual refund="+refund+" held="+fund.Amount+" quote="+c.Profile.RefineryCreditCost+" income="+v.SettledIncome+" available="+ledger.Available);last=fund.Amount;}
                    }
                    var replacement=v.Buildings.FirstOrDefault(b=>b.SiteId==state.SiteId&&b.SlotId==state.SlotId&&b.Kind==PlayableBuildingKind.Refinery);
                    if(replacement!=null)
                    {
                        paidTick=a.Tick;Assert.AreEqual(c.Profile.RefineryCreditCost,ledger.Capture().Paid.Single(e=>e.Purpose=="infrastructure"&&e.Action.Kind==PlayableCommandKind.BuildAt).Amount);Assert.False(ledger.Capture().Reserved.Any(e=>e.Purpose=="infrastructure-conversion"));ready=true;break;
                    }
                }
                Assert.True(held);Assert.True(competitor,"real producer must have an individually affordable legal unit order while partial fund exists");Assert.True(ready);
                Assert.Less(paidTick,Planner(a).Capture().ExpiresTick);Assert.False(Checkpoint(a).Records.Any(r=>r.Policy=="production"&&r.Status==PlayableAiDeliveryStatus.Applied&&r.ApplicationTick<paidTick));
                TestContext.WriteLine("partial replacement sale="+saleTick+" paid="+paidTick+" final bank="+View(a,c).ExactCredits+" command paid="+Ledger(a).PaidTotal);
            }
        }

        private static void ReachPartial(PlayableAuthorityTick a,OfflineMatchConfiguration c,UnityHostRouteService host)
        {
            var v=View(a,c);foreach(var science in v.Buildings.Where(b=>b.Kind==PlayableBuildingKind.ScientificCenter))Damage(a,science.Id,(int)(c.Profile.ScienceHealth*.3));Call(Get(a,"domain"),"AddCredits",PlayableOwner.Player,-v.ExactCredits);
            for(int n=0;n<700&&!Ledger(a).Capture().Reserved.Any(e=>e.Purpose=="infrastructure-conversion"&&e.Amount>0&&e.Amount<c.Profile.RefineryCreditCost);n++)Step(a,host);
            Assert.True(Ledger(a).Capture().Reserved.Any(e=>e.Purpose=="infrastructure-conversion"&&e.Amount>0&&e.Amount<c.Profile.RefineryCreditCost));
        }
        [Test] public void PartialFundGameAiRepriceRestoreAndActualPaidSuffixStayCoherent()
        {
            var c=PartialConfig();var a=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                ReachPartial(a,c,host);var before=Ledger(a).Capture().Reserved.Single(e=>e.Purpose=="infrastructure-conversion");double bank=View(a,c).ExactCredits;
                var data=c.Profile.CopyData();data.revision++;data.refineryCreditCost+=10;var p=PlayableProfile.Create(data);
                var aiData=AiProfile.Initial.CopyData();aiData.revision++;aiData.fields.Single(f=>f.path=="economy.reservationExpirySeconds").value=30;var ai=new AiProfile(aiData);
                Assert.Null(a.ApplyProfile(p,ai));var held=Ledger(a).Capture().Reserved.Single(e=>e.Purpose=="infrastructure-conversion");
                Assert.AreEqual(before.Id,held.Id);Assert.AreEqual(before.Amount,held.Amount);Assert.AreEqual(before.CreatedTick,held.CreatedTick);Assert.AreEqual(before.CreatedTick+900,held.ExpiresTick);
                Assert.AreEqual(p.ProfileId+"@"+p.Revision,held.Terms);Assert.AreEqual(p.Revision,held.Action.ProfileRevision);Assert.AreEqual(bank,View(a,c).ExactCredits);
                var rebound=new OfflineMatchConfiguration(p,c.SourceIdentity,c.MapIdentity,c.RouteProvenance,c.Seed,c.Roster.ToArray(),c.Starts.ToArray(),c.Sites.ToArray(),c.Obstacles.ToArray(),new double[,]{{0,100},{100,0}},scenarioBuildings:c.ScenarioBuildings.ToArray());
                var bytes=a.CaptureBytes();var b=PlayableAuthorityTick.RestoreBytes(bytes,rebound,ai);CollectionAssert.AreEqual(bytes,b.CaptureBytes());
                for(int n=0;n<1200&&!Ledger(a).Capture().Paid.Any(e=>e.Purpose=="infrastructure"&&e.Action.Kind==PlayableCommandKind.BuildAt);n++)
                {Step(a,host);Step(b,host);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());}
                var payment=Ledger(a).Capture().Paid.Single(e=>e.Purpose=="infrastructure"&&e.Action.Kind==PlayableCommandKind.BuildAt);Assert.AreEqual(p.RefineryCreditCost,payment.Amount);
                Assert.True(Checkpoint(a).Records.Any(r=>r.ReceiptIdentity.Id==payment.Receipt.Id&&r.Status==PlayableAiDeliveryStatus.Applied));Assert.False(Ledger(a).Capture().Reserved.Any(e=>e.Purpose=="infrastructure-conversion"));
                TestContext.WriteLine("partial reprice="+held.Terms+" hold="+held.Amount+" expires="+held.ExpiresTick+" payment="+payment.Amount+" receipt="+payment.Receipt.Id+" bank="+View(a,c).ExactCredits);
            }
        }
        [TestCase("expiry")][TestCase("loss")][TestCase("stop")]
        public void PartialConversionFundReleasesForActualLifecycleTerminal(string terminal)
        {
            var c=PartialConfig();var a=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                ReachPartial(a,c,host);
                if(terminal=="expiry")
                {
                    var data=AiProfile.Initial.CopyData();data.revision++;data.fields.Single(f=>f.path=="economy.reservationExpirySeconds").value=5;var ai=new AiProfile(data);
                    Assert.Null(a.ApplyProfile(c.Profile,ai));long expires=Planner(a).Capture().ExpiresTick;
                    for(long n=a.Tick;n<=expires;n++)Step(a,host);
                    Assert.Zero(Planner(a).Capture().SiteId);Assert.False(Ledger(a).Capture().Reserved.Any(e=>e.Purpose=="infrastructure-conversion"));
                    var b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c,ai);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
                }
                else
                {
                    if(terminal=="loss"){Damage(a,Planner(a).Capture().ParentId,10000);Step(a,host);
                        Assert.True(a.ParticipantView("west-owner").OwnerEliminated);
                        Assert.True(a.ParticipantView("west-owner").Entities.Where(u=>u.Owner==PlayableOwner.Player).All(u=>u.Queue==null),"Elimination cancels every own active/pending/deferred queue before strict restore");
                    }else a.Stop();
                    Assert.Zero(Planner(a).Capture().SiteId);Assert.False(Ledger(a).Capture().Reserved.Any(e=>e.Purpose=="infrastructure-conversion"));
                    var b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
                }
            }
        }
    }
}
