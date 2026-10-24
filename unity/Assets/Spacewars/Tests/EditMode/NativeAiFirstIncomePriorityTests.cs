using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using Spacewars.Presentation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiFirstIncomePriorityTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private static object Get(object o,string name)=>o.GetType().GetField(name,F).GetValue(o);
        private static object Call(object o,string name,params object[] args)
        {try{return (o is Type t?t:o.GetType()).GetMethod(name,F).Invoke(o is Type?null:o,args);}catch(TargetInvocationException e){throw e.InnerException;}}
        private static object Finance()=>Activator.CreateInstance(typeof(AiBudgetLedger).Assembly.GetType("Spacewars.Runtime.AiExpansionFinancial"),F,null,new object[]{"player-1",71L},null);
        private static PlayableAiObservation View(long tick,int cash,bool busy=false,bool safe=true,bool known=true,ConstructionPhase? phase=null,bool center=false,double income=0,PlayableProfile p=null)
        {
            p=p??PlayableProfile.Default;
            var basic=(PlayableAiObservation)Call(typeof(NativeAiTechnologyFundingTests),"View",tick,cash,income,center,1d,p,true,false,PlayableEntityKind.Tank,false);
            var site=TerritoryRules.Sites(p).Single(s=>s.Id==4);
            var explorer=new PlayableEntitySnapshot(13,PlayableOwner.Player,PlayableEntityKind.Explorer,new NavPoint(site.Position.X+20,site.Position.Z),p.ExplorerHealth,busy,0,0,0);
            var units=basic.Entities.Concat(new[]{explorer}).Concat(safe?Array.Empty<PlayableEntitySnapshot>():new[]{new PlayableEntitySnapshot(99,PlayableOwner.Enemy,PlayableEntityKind.Tank,site.Position,p.TankHealth,false,0,0,0)}).ToArray();
            var buildings=basic.Buildings.ToArray();
            if(phase.HasValue)buildings=buildings.Concat(new[]{new PlayableBuildingSnapshot(20,PlayableOwner.Player,site.Kind,site.Position,phase==ConstructionPhase.Pending?0:(int)p.MineHealth,phase==ConstructionPhase.Ready?1:.2,0,0,default(NavPoint),siteId:4,phase:phase.Value)}).ToArray();
            var sites=basic.Sites.Concat(known?new[]{new TerritorySiteSnapshot(site,phase.HasValue?(PlayableOwner?)PlayableOwner.Player:null,phase.HasValue?(PlayableOwner?)PlayableOwner.Player:null,phase.HasValue?1:0,false,phase.HasValue?20:0,phase==ConstructionPhase.Ready)}:Array.Empty<TerritorySiteSnapshot>()).ToArray();
            var proof=new PlayableRouteProof(13,PlayableOwner.Player,71,tick,1,explorer.Position,p.ExplorerCollisionRadius,PlayableRouteTargetKind.PublicObjective,4,site.Position,site.Position,new[]{site.Position});
            return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,41,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,cash,null,units,buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:sites,publicScoutObjectives:new[]{new PlayablePublicScoutObjective(4,site.Position,PlayablePublicScoutObjectiveRole.ExpansionIncome,true)},routeProofs:new[]{proof},population:basic.Population,researchAvailability:basic.ResearchAvailability.ToArray(),activeProfile:p,homeSiteId:basic.HomeSiteId,settledIncome:income));
        }
        private static void Allocate(object f,AiTechnologyFunding tech,AiBudgetLedger b,AiDecisionArbiter a,AiExpansionPlanner ex,PlayableAiObservation o,int protect=0,bool allowed=true,PlayableProfile p=null)
        {
            p=p??PlayableProfile.Default;b.ObserveBank(o.Credits);Call(f,"Reconcile",o,p,AiProfile.Initial,null,b,a,ex);
            b.Expire(o.Tick,e=>e.Purpose=="expansion-financial"||(e.Purpose!=AiTechnologyFunding.Purpose||tech.Valid(e,o,p,b,a)));tech.Reconcile(o,b,a);
            var old=Call(f,"Detach",b);int cost=(int)Call(f,"PriorityCost",o,p,AiProfile.Initial,a,ex,old,allowed);
            var plan=new AiTechnologyPlanner().Plan(o,p,AiProfile.Initial,b.Capture(),preferredFunding:tech.Preferred(a));
            tech.Observe(o,p,AiProfile.Initial,b,a,plan,true,protect,cost);
            Call(f,"Observe",o,p,AiProfile.Initial,null,b,a,ex,old,protect,allowed);
            Call(f,"Validate",p,AiProfile.Initial,b,a,o.Tick,41,TerritoryRules.Sites(p),2);
            Call(tech,"Validate",o,p,b,a,Array.Empty<AiIntent>());
        }
        private static (object f,AiTechnologyFunding tech,AiBudgetLedger b,AiDecisionArbiter a,AiExpansionPlanner ex) Start(bool bootstrap=true,bool homeReady=true)
        {
            var f=Finance();var a=new AiDecisionArbiter(AiProfile.Initial);if(bootstrap)Call(f,"InitializeNewMatch",PlayableProfile.Default,AiProfile.Initial,a);
            if(bootstrap&&homeReady)Reconcile(f,a,HomeState(0,true));
            return(f,new AiTechnologyFunding("player-1",71),new AiBudgetLedger("player-1",71),a,new AiExpansionPlanner("player-1"));
        }
        private static byte[] ArbiterBytes(AiDecisionArbiter a)
        {using(var s=new MemoryStream()){using(var w=new BinaryWriter(s,System.Text.Encoding.UTF8,true))Call(a,"WriteState",w);return s.ToArray();}}
        private static byte[] BudgetWire(AiBudgetState state,long tick)
        {var ledger=AiBudgetLedger.Restore(state,tick);using(var s=new MemoryStream())using(var w=new BinaryWriter(s,System.Text.Encoding.UTF8,true)){AiBudgetStateWire.Write(w,ledger);return s.ToArray();}}
        private static AiDecisionArbiter Restore(AiDecisionArbiter a,long tick)
        {var bytes=ArbiterBytes(a);var restored=new AiDecisionArbiter(AiProfile.Initial);using(var s=new MemoryStream(bytes))using(var r=new BinaryReader(s))Call(restored,"ReadState",r,tick,"player-1",71L,PlayableProfile.Default,AiProfile.Initial);CollectionAssert.AreEqual(bytes,ArbiterBytes(restored));return restored;}
        private static PlayableAiObservation HomeState(int tick,bool readyHome,bool readyNonhome=false)
        {
            var ai=AiProfile.Initial;var state=PlayableAiOpeningComposition.Initialize(7,"player-1",aiProfile:ai,forcedOpening:PlayableAiOpening.GreedySafe);
            var opening=new AiOpeningExecutor(state,71);int refineries=readyHome?opening.RequiredHomeRefineryCount(ai):0;
            return (PlayableAiObservation)Call(typeof(NativeAiOpeningTests),"HomeEconomyObservation",tick,refineries,readyHome,readyNonhome,false,0);
        }
        private static AiOpeningExecutor Opening()=>new AiOpeningExecutor(
            PlayableAiOpeningComposition.Initialize(7,"player-1",aiProfile:AiProfile.Initial,forcedOpening:PlayableAiOpening.GreedySafe),71);
        private static void Reconcile(object f,AiDecisionArbiter a,PlayableAiObservation o)
        {
            var b=new AiBudgetLedger("player-1",71);var ex=new AiExpansionPlanner("player-1");
            Call(f,"Reconcile",o,PlayableProfile.Default,AiProfile.Initial,Opening(),b,a,ex);
        }
        private static void InstallVersion1(AiDecisionArbiter a,bool firstIncomeReady,int homeSite=0)
        {
            using(var s=new MemoryStream())using(var w=new BinaryWriter(s,System.Text.Encoding.UTF8,true))
            {
                w.Write(0x45464d31);w.Write("player-1");w.Write(71L);w.Write(PlayableProfile.Default.ProfileId+"@"+PlayableProfile.Default.Revision+":"+AiProfile.Initial.Hash);
                w.Write(false);w.Write(0);w.Write(1);w.Write(homeSite);w.Write(firstIncomeReady);Call(a,"SaveSiteFundingMemory",Convert.ToBase64String(s.ToArray()));
            }
        }
        private static void InstallVersion0(AiDecisionArbiter a)
        {
            using(var s=new MemoryStream())using(var w=new BinaryWriter(s,System.Text.Encoding.UTF8,true))
            {
                w.Write(0x45464d31);w.Write("player-1");w.Write(71L);w.Write(PlayableProfile.Default.ProfileId+"@"+PlayableProfile.Default.Revision+":"+AiProfile.Initial.Hash);
                w.Write(false);w.Write(0);Call(a,"SaveSiteFundingMemory",Convert.ToBase64String(s.ToArray()));
            }
        }
        private static void InstallVersion2(AiDecisionArbiter a)
        {
            using(var s=new MemoryStream())using(var w=new BinaryWriter(s,System.Text.Encoding.UTF8,true))
            {
                w.Write(0x45464d31);w.Write("player-1");w.Write(71L);w.Write(PlayableProfile.Default.ProfileId+"@"+PlayableProfile.Default.Revision+":"+AiProfile.Initial.Hash);
                w.Write(false);w.Write(0);w.Write(2);w.Write(0);w.Write(false);Call(a,"SaveSiteFundingMemory",Convert.ToBase64String(s.ToArray()));
            }
        }
        [TestCase(false)][TestCase(true)] public void ExistingDiscretionaryHoldShrinksCoherentlyWithoutClockProgressOrActorClaim(bool newIncome)
        {
            var x=Start();var oldView=View(0,312,known:false);Allocate(x.f,x.tech,x.b,x.a,x.ex,oldView);
            var before=x.b.Capture().Reserved.Single(AiTechnologyFunding.IsFunding);Assert.AreEqual(312,before.Amount);
            var memo=Call(typeof(AiTechnologyFunding),"Load",x.a);var oldMemoFields=new[]{"Branch","Phase","Site","Slot","Parent","Center","BuildIntent","BuildTerms","BuildReceipt","Created","Expires"}.Select(n=>Get(memo,n)).ToArray();
            var o=View(60,newIncome?332:312,busy:true,income:newIncome?20:0);Allocate(x.f,x.tech,x.b,x.a,x.ex,o);
            var held=x.b.Capture().Reserved.Single(AiTechnologyFunding.IsFunding);var finance=x.b.Capture().Reserved.Single(e=>e.Purpose=="expansion-financial");
            Assert.AreEqual(200,finance.Amount);Assert.AreEqual(newIncome?132:112,held.Amount);Assert.AreEqual(before.Id,held.Id);Assert.AreEqual(before.CreatedTick,held.CreatedTick);Assert.AreEqual(before.ExpiresTick,held.ExpiresTick);
            var nextMemo=Call(typeof(AiTechnologyFunding),"Load",x.a);Assert.AreEqual(held.Amount,Get(nextMemo,"Amount"));CollectionAssert.AreEqual(oldMemoFields,new[]{"Branch","Phase","Site","Slot","Parent","Center","BuildIntent","BuildTerms","BuildReceipt","Created","Expires"}.Select(n=>Get(nextMemo,n)).ToArray());Assert.False((bool)Get(nextMemo,"Expired"));
            Assert.IsEmpty(finance.Claims);Assert.IsEmpty(finance.Action.EntityIds);Assert.Zero(x.ex.Capture().SiteId);Assert.IsNull(x.ex.TryPlan(o,PlayableProfile.Default,AiProfile.Initial,0,_=>true,_=>true),"Busy witness remains unavailable to actual Move/Build");
            x.a=Restore(x.a,60);x.b=AiBudgetLedger.Restore(x.b.Capture(),60);Allocate(x.f,x.tech,x.b,x.a,x.ex,View(120,o.Credits,busy:true,income:newIncome?20:0));Assert.AreEqual(held.ExpiresTick,x.b.Capture().Reserved.Single(AiTechnologyFunding.IsFunding).ExpiresTick);
        }
        [Test] public void OrdinaryOwnerReviewUsesQ4AllocationBeforeSelectionWithoutStealingBusyExplorer()
        {
            var x=Start();Allocate(x.f,x.tech,x.b,x.a,x.ex,View(0,312,known:false));
            var p=PlayableProfile.Default;var o=View(60,312,busy:true);var site=o.Sites.Single(s=>s.Site.Id==o.HomeSiteId).Site;
            var buildings=o.Buildings.Concat(new[]{
                new PlayableBuildingSnapshot(4,PlayableOwner.Player,PlayableBuildingKind.Refinery,site.Slots[2].Position,(int)p.RefineryHealth,1,0,0,default(NavPoint),site.Id,site.Slots[2].Id,1),
                new PlayableBuildingSnapshot(5,PlayableOwner.Player,PlayableBuildingKind.Refinery,site.Slots[3].Position,(int)p.RefineryHealth,1,0,0,default(NavPoint),site.Id,site.Slots[3].Id,1)}).ToArray();
            var snapshot=new PlayableSnapshot(o.ProfileId,o.ProfileRevision,71,41,60,60,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,312,null,o.Entities.ToArray(),buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:o.Sites.ToArray(),publicScoutObjectives:o.PublicScoutObjectives.ToArray(),routeProofs:o.RouteProofs.ToArray(),population:o.Population,researchAvailability:o.ResearchAvailability.ToArray(),activeProfile:p,homeSiteId:o.HomeSiteId);
            var type=typeof(AiBudgetLedger).Assembly.GetType("Spacewars.Runtime.PlayableAiOwnerLoop");var opening=PlayableAiOpeningComposition.Initialize(41,o.OwnerId,aiProfile:AiProfile.Initial,forcedOpening:PlayableAiOpening.Safe);
            var loop=Activator.CreateInstance(type,F,null,new object[]{p,opening,71L,null},null);type.GetField("budget",F).SetValue(loop,x.b);type.GetField("arbiter",F).SetValue(loop,x.a);
            CurrentExpansionDecisionReadback actual=null;PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests=o.OwnerId;PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=r=>actual=r;
            try
            {
                Call(loop,"Review",snapshot,0L);Assert.NotNull(actual);Assert.Zero(actual.ProtectedFunds);Assert.False(actual.Threatened);Assert.False(actual.UrgentRepair);Assert.False(actual.EssentialScale);
                Assert.AreEqual(200,actual.AfterFinancialAllocation.Reserved.Single(e=>e.Purpose=="expansion-financial").Amount);Assert.AreEqual(112,actual.AfterFinancialAllocation.Reserved.Single(AiTechnologyFunding.IsFunding).Amount);
                Assert.IsNull(actual.ExpansionAction);Assert.False(actual.Proposals.Any(i=>i.Policy=="expansion"&&i.Action.EntityIds.Contains(13)));Assert.Zero(actual.ExpansionState.SiteId);
                var firstFinance=x.b.Capture().Reserved.Single(e=>e.Purpose=="expansion-financial");var firstTechnology=x.b.Capture().Reserved.Single(AiTechnologyFunding.IsFunding);
                byte[] arbiterBytes=ArbiterBytes(x.a),budgetBytes=BudgetWire(x.b.Capture(),60);
                // This is an arbiter/ledger DTO roundtrip; full authority C9 remains covered by the integration fixture.
                var restoredArbiter=Restore(x.a,60);var restoredBudget=AiBudgetLedger.Restore(x.b.Capture(),60);
                CollectionAssert.AreEqual(arbiterBytes,ArbiterBytes(restoredArbiter),"AiDecisionArbiter state survives its exact capture/restore");
                CollectionAssert.AreEqual(budgetBytes,BudgetWire(restoredBudget.Capture(),60),"AiBudgetState DTO restore preserves exact wire bytes");
                Assert.AreEqual(60,(long)Get(loop,"lastDecisionTick"),"the first owner review established the current decision clock");
                type.GetField("budget",F).SetValue(loop,restoredBudget);type.GetField("arbiter",F).SetValue(loop,restoredArbiter);
                long nextDecisionTick=(long)Get(loop,"lastDecisionTick")+(long)Get(loop,"DecisionIntervalTicks");
                var secondObservation=View(nextDecisionTick,312,busy:true);
                var secondSnapshot=new PlayableSnapshot(secondObservation.ProfileId,secondObservation.ProfileRevision,secondObservation.Generation,secondObservation.Seed,nextDecisionTick,nextDecisionTick,
                    RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,312,null,secondObservation.Entities.ToArray(),snapshot.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),
                    new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:secondObservation.Sites.ToArray(),publicScoutObjectives:secondObservation.PublicScoutObjectives.ToArray(),
                    routeProofs:secondObservation.RouteProofs.ToArray(),population:secondObservation.Population,researchAvailability:secondObservation.ResearchAvailability.ToArray(),activeProfile:p,homeSiteId:secondObservation.HomeSiteId);
                var firstExplorer=o.Entities.Single(e=>e.Id==13);var secondExplorer=secondObservation.Entities.Single(e=>e.Id==13);
                Assert.AreEqual(312,secondObservation.Credits);Assert.Zero(secondObservation.SettledIncome);Assert.AreEqual(firstExplorer.Position,secondExplorer.Position);
                Assert.AreEqual(firstExplorer.Moving,secondExplorer.Moving);Assert.AreEqual(firstExplorer.Held,secondExplorer.Held);
                Assert.AreEqual(nextDecisionTick,secondObservation.RouteProofs.Single().Tick,"the route proof is regenerated for the next decision observation");
                actual=null;Call(loop,"Review",secondSnapshot,0L);Assert.NotNull(actual,"the unchanged busy-owner state receives a second ordinary decision at the next profile interval");
                var secondFinance=restoredBudget.Capture().Reserved.Single(e=>e.Purpose=="expansion-financial");var secondTechnology=restoredBudget.Capture().Reserved.Single(AiTechnologyFunding.IsFunding);
                Assert.AreEqual(200,secondFinance.Amount);Assert.AreEqual(firstFinance.CreatedTick,secondFinance.CreatedTick);Assert.AreEqual(firstFinance.ExpiresTick,secondFinance.ExpiresTick,"a no-income busy review does not renew the finance clock");
                Assert.AreEqual(firstTechnology.Amount,secondTechnology.Amount);Assert.AreEqual(firstTechnology.CreatedTick,secondTechnology.CreatedTick);Assert.AreEqual(firstTechnology.ExpiresTick,secondTechnology.ExpiresTick);
                Assert.IsNull(actual.ExpansionAction);Assert.False(actual.Proposals.Any(i=>i.Policy=="expansion"&&i.Action.EntityIds.Contains(13)));Assert.Zero(actual.ExpansionState.SiteId);
            }
            finally{PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests=null;PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=null;}
        }
        [TestCase("unsafe")][TestCase("unknown")][TestCase("guard")][TestCase("legacy")]
        public void NoCurrentWitnessOrLegacyKeepsTechnologyFirst(string blocker)
        {
            var x=Start(blocker!="legacy");var o=View(0,312,safe:blocker!="unsafe",known:blocker!="unknown");Allocate(x.f,x.tech,x.b,x.a,x.ex,o,allowed:blocker!="guard");
            Assert.AreEqual(312,x.b.Capture().Reserved.Single(AiTechnologyFunding.IsFunding).Amount);Assert.Zero(x.b.Capture().Reserved.Where(e=>e.Purpose=="expansion-financial").Sum(e=>e.Amount));Assert.Zero(x.ex.Capture().SiteId);
        }
        [TestCase(0)][TestCase(40)][TestCase(150)] public void SafetyUnpaidStartupAndUsefulProductionBandsStayPayable(int protect)
        {
            var x=Start();var o=View(0,500);x.b.ObserveBank(500);x.b.SetSafetyReserve(20);
            var action=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,71,0,PlayableCommandKind.Stop);
            Assert.True(x.b.Reserve(new AiBudgetEntry("startup","startup",action,70,1,0,1000,"test",Array.Empty<string>(),"test")));
            Assert.True(x.b.Accept(new AiBudgetEntry("unpaid","economy",action,80,1,0,1000,"test",Array.Empty<string>(),"test",new AiReceiptIdentity(71,o.OwnerId,1,1))));
            Allocate(x.f,x.tech,x.b,x.a,x.ex,o,protect);Assert.AreEqual(70,x.b.ReservationAmount("startup"));Assert.AreEqual(80,x.b.Unpaid);Assert.AreEqual(20,x.b.SafetyReserve);Assert.AreEqual(protect,x.b.Available);
            Assert.AreEqual(Math.Min(200,330-protect),x.b.Capture().Reserved.Single(e=>e.Purpose=="expansion-financial").Amount);Assert.AreEqual(Math.Max(0,130-protect),x.b.Capture().Reserved.Single(AiTechnologyFunding.IsFunding).Amount);
        }
        [TestCase(false)][TestCase(true)] public void AcceptedScienceRemainderHasPriorityOverFirstIncome(bool paid)
        {
            var x=Start();var o=View(0,600,known:false);Allocate(x.f,x.tech,x.b,x.a,x.ex,o);var held=x.b.Capture().Reserved.Single(AiTechnologyFunding.IsFunding);
            var i=new AiIntent("science","economy",held.Action,300,300,0,Array.Empty<string>(),reservationId:held.Id);var receipt=new AiReceiptIdentity(71,o.OwnerId,1,1);Assert.True(x.tech.Accept(i,receipt,o,PlayableProfile.Default,x.b,x.a));
            if(paid)Assert.True(x.b.Reconcile(i.Id,receipt,PlayableAiDeliveryStatus.Applied));
            Allocate(x.f,x.tech,x.b,x.a,x.ex,View(60,paid?312:612,center:paid,income:12));
            Assert.AreEqual(300,x.b.Capture().Reserved.Single(AiTechnologyFunding.IsFunding).Amount);Assert.AreEqual(12,x.b.Capture().Reserved.Single(e=>e.Purpose=="expansion-financial").Amount);Assert.AreEqual(paid?0:300,x.b.Unpaid);Assert.AreEqual(paid?300:0,x.b.PaidTotal);
            var memo=Call(typeof(AiTechnologyFunding),"Load",x.a);Assert.AreEqual(receipt.Id,((AiReceiptIdentity)Get(memo,"BuildReceipt")).Id);x.a=Restore(x.a,60);
        }
        [Test] public void PendingOrPaidResearchRemainsAnOrdinaryExactInvoice()
        {
            var x=Start();var o=View(0,600,known:false,center:true);Allocate(x.f,x.tech,x.b,x.a,x.ex,o);var held=x.b.Capture().Reserved.Single(AiTechnologyFunding.IsFunding);
            var intent=new AiIntent("research","research",held.Action,300,300,0,Array.Empty<string>(),reservationId:held.Id);var receipt=new AiReceiptIdentity(71,o.OwnerId,1,1);Assert.True(x.tech.Accept(intent,receipt,o,PlayableProfile.Default,x.b,x.a));
            Allocate(x.f,x.tech,x.b,x.a,x.ex,View(60,612,center:true,income:12));Assert.AreEqual(300,x.b.Unpaid);Assert.AreEqual(receipt.Id,x.b.Capture().Unpaid.Single().Receipt.Id);Assert.IsEmpty(x.b.Capture().Reserved.Where(AiTechnologyFunding.IsFunding));Assert.AreEqual(200,x.b.Capture().Reserved.Single(e=>e.Purpose=="expansion-financial").Amount);
            Assert.True(x.b.Reconcile(intent.Id,receipt,PlayableAiDeliveryStatus.Applied));Assert.AreEqual(300,x.b.PaidTotal);Assert.AreEqual(receipt.Id,x.b.Capture().Paid.Single().Receipt.Id);Assert.Zero(x.b.Unpaid);
        }
        [TestCase("version")][TestCase("boolean")][TestCase("home")][TestCase("foreign-home")][TestCase("income-home")][TestCase("ready-without-home")][TestCase("first-income-ready-without-home")]
        public void MalformedBootstrapTailCannotPublishRestoredWorld(string corruption)
        {
            var c=(OfflineMatchConfiguration)Call(typeof(NativeAiSiteFinancialMechanismTests),"EarnedConfig",true);var authority=new PlayableAuthorityTick(c,71);
            try
            {
                var loop=((IEnumerable)Get(Get(authority,"scheduler"),"owners")).Cast<object>().Single();var a=(AiDecisionArbiter)Get(loop,"arbiter");var raw=Convert.FromBase64String((string)Call(a,"SiteFundingMemory"));
                int tailOffset,homeSite;bool historyKnown,firstIncomeReady,homeEconomyReady;
                using(var stream=new MemoryStream(raw))using(var reader=new BinaryReader(stream))
                {
                    var aiParticipant=c.Roster.Single(x=>x.Control==OfflineControl.Ai);
                    Assert.AreEqual(0x45464d31,reader.ReadInt32());Assert.AreEqual(aiParticipant.Id,reader.ReadString());Assert.AreEqual(71L,reader.ReadInt64());
                    Assert.AreEqual(c.Profile.ProfileId+"@"+c.Profile.Revision+":"+AiProfile.Initial.Hash,reader.ReadString());
                    Assert.IsFalse(reader.ReadBoolean(),"the genesis memo has no active site fingerprint");Assert.AreEqual(0,reader.ReadInt32(),"the genesis memo has no expiry fingerprints");
                    tailOffset=checked((int)stream.Position);Assert.AreEqual(3,reader.ReadInt32(),"fixture carries the current v3 tail");homeSite=reader.ReadInt32();
                    historyKnown=reader.ReadBoolean();firstIncomeReady=reader.ReadBoolean();homeEconomyReady=reader.ReadBoolean();Assert.AreEqual(stream.Length,stream.Position,"v3 genesis tail has exactly three flags and no extension");
                }
                Assert.AreEqual(0,homeSite,"the malformed-memo fixture is unbound at genesis");Assert.True(historyKnown);Assert.False(firstIncomeReady);Assert.False(homeEconomyReady);
                if(corruption=="boolean")raw[tailOffset+10]=2;
                else if(corruption=="version")Array.Copy(BitConverter.GetBytes(4),0,raw,tailOffset,4);
                else if(corruption=="home"||corruption=="foreign-home"||corruption=="income-home")Array.Copy(BitConverter.GetBytes(corruption=="home"?999:corruption=="foreign-home"?2:3),0,raw,tailOffset+4,4);
                else if(corruption=="first-income-ready-without-home")raw[tailOffset+9]=1;
                else raw[tailOffset+10]=1;
                Call(a,"SaveSiteFundingMemory",Convert.ToBase64String(raw));var bad=authority.CaptureBytes();Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(bad,c));CollectionAssert.AreEqual(bad,authority.CaptureBytes());
            }
            finally{authority.Stop();}
        }
        [Test] public void UnchangedExpiredSiteDoesNotReopenWithCashOrMetadata()
        {
            var x=Start();Allocate(x.f,x.tech,x.b,x.a,x.ex,View(0,312,busy:true));long expiry=x.b.Capture().Reserved.Single(e=>e.Purpose=="expansion-financial").ExpiresTick;
            Allocate(x.f,x.tech,x.b,x.a,x.ex,View(expiry,312,busy:true));Assert.IsEmpty(x.b.Capture().Reserved.Where(e=>e.Purpose=="expansion-financial"));
            Allocate(x.f,x.tech,x.b,x.a,x.ex,View(expiry+1,400,busy:true,income:88));Assert.IsEmpty(x.b.Capture().Reserved.Where(e=>e.Purpose=="expansion-financial"));Assert.True((bool)Call(x.f,"IsExpiredFingerprint",View(expiry+1,400).Sites.Single(s=>s.Site.Id==4),x.a));
            var p=PlayableProfile.Default;var data=p.CopyData();data.revision++;var next=PlayableProfile.Create(data);Call(x.f,"Rebind",next,AiProfile.Initial,expiry+2,x.b,x.a,null);Assert.Zero((int)Call(x.f,"PriorityCost",View(expiry+2,400,p:next),next,AiProfile.Initial,x.a,x.ex,null,true));
        }
        [Test] public void HomeAndFirstNonhomeIncomeMilestonesRemainIndependentAndStickyAcrossRestoreRebindAndLoss()
        {
            var x=Start(homeReady:false);
            Assert.True((bool)Call(x.f,"BootstrapActive",x.a));
            Reconcile(x.f,x.a,HomeState(1,false));Assert.True((bool)Call(x.f,"BootstrapActive",x.a),"missing home READY prerequisites leave Q7 closed");
            Reconcile(x.f,x.a,HomeState(2,true));Assert.False((bool)Call(x.f,"BootstrapActive",x.a),"home READY facts open Q7");
            Assert.True((bool)Call(x.f,"FirstIncomePriorityActive",x.a),"known Q4 history remains pending after home readiness");
            x.a=Restore(x.a,2);Assert.False((bool)Call(x.f,"BootstrapActive",x.a));Assert.True((bool)Call(x.f,"FirstIncomePriorityActive",x.a));
            Call(x.f,"Rebind",PlayableProfile.Default,AiProfile.Initial,2L,x.b,x.a,null);
            Reconcile(x.f,x.a,HomeState(3,true,true));Assert.False((bool)Call(x.f,"FirstIncomePriorityActive",x.a),"actual READY nonhome income closes Q4");
            x.a=Restore(x.a,3);Assert.False((bool)Call(x.f,"FirstIncomePriorityActive",x.a));
            Reconcile(x.f,x.a,HomeState(4,true,false));Assert.False((bool)Call(x.f,"FirstIncomePriorityActive",x.a),"loss of the nonhome center does not reset Q4 history");
            Reconcile(x.f,x.a,HomeState(5,false,false));Assert.False((bool)Call(x.f,"BootstrapActive",x.a),"loss of home buildings does not reset Q7 history");
        }
        [Test] public void AcceptedNonhomeReadyIncomeCanSetQ4BeforeHomeGateOpens()
        {
            var f=Finance();var a=new AiDecisionArbiter(AiProfile.Initial);Call(f,"InitializeNewMatch",PlayableProfile.Default,AiProfile.Initial,a);
            Reconcile(f,a,HomeState(1,false,true));Assert.True((bool)Call(f,"BootstrapActive",a));
            Assert.False((bool)Call(f,"FirstIncomePriorityActive",a),"Q4 first-income milestone is already complete even while Q7 remains closed");
            Reconcile(f,a,HomeState(2,true,true));Assert.False((bool)Call(f,"BootstrapActive",a));
            Assert.False((bool)Call(f,"FirstIncomePriorityActive",a));
        }
        [TestCase(false)][TestCase(true)] public void Version1HistoryMigratesWithoutConflatingHomeGate(bool firstIncomeReady)
        {
            var f=Finance();var a=new AiDecisionArbiter(AiProfile.Initial);
            int homeSite=TerritoryRules.Sites(PlayableProfile.Default).Single(s=>s.Id==1).Id;
            InstallVersion1(a,firstIncomeReady,homeSite);
            Assert.True((bool)Call(f,"BootstrapActive",a),"v1 cannot open Q7 without an independently observed home-ready fact");a=Restore(a,0);
            Reconcile(f,a,HomeState(1,false));Assert.True((bool)Call(f,"BootstrapActive",a));
            var beforeReady=typeof(AiBudgetLedger).Assembly.GetType("Spacewars.Runtime.AiExpansionFinancial").GetMethod("Load",F).Invoke(null,new object[]{a});
            Assert.AreEqual(1,(int)Get(beforeReady,"PolicyVersion"),"a bound v1 can remain v1 while the home is not READY");Assert.AreEqual(firstIncomeReady,(bool)Get(beforeReady,"FirstIncomeReady"));
            Reconcile(f,a,HomeState(2,true));
            Assert.False((bool)Call(f,"BootstrapActive",a));
            Assert.AreEqual(!firstIncomeReady,(bool)Call(f,"FirstIncomePriorityActive",a));
            var memo=typeof(AiBudgetLedger).Assembly.GetType("Spacewars.Runtime.AiExpansionFinancial").GetMethod("Load",F).Invoke(null,new object[]{a});
            Assert.AreEqual(3,(int)Get(memo,"PolicyVersion"));Assert.True((bool)Get(memo,"FirstIncomeHistoryKnown"));Assert.AreEqual(firstIncomeReady,(bool)Get(memo,"FirstIncomeReady"));
        }
        [Test] public void Version1GenesisWithUnknownHomeBindingRestoresAndMigratesOnFirstObservation()
        {
            var f=Finance();var a=new AiDecisionArbiter(AiProfile.Initial);InstallVersion1(a,false);
            Call(f,"Validate",PlayableProfile.Default,AiProfile.Initial,new AiBudgetLedger("player-1",71),a,0,7,TerritoryRules.Sites(PlayableProfile.Default),2);
            Assert.False((bool)Call(f,"FirstIncomePriorityActive",a));
            a=Restore(a,0);Reconcile(f,a,HomeState(1,true));
            Assert.False((bool)Call(f,"BootstrapActive",a));Assert.True((bool)Call(f,"FirstIncomePriorityActive",a));
        }
        [Test] public void Version0AbsentMilestonesRemainUnknownAfterHomeReadiness()
        {
            var f=Finance();var a=new AiDecisionArbiter(AiProfile.Initial);InstallVersion0(a);
            Assert.True((bool)Call(f,"BootstrapActive",a),"v0 has no evidence that the home gate was reached");
            Reconcile(f,a,HomeState(1,true,true));Assert.False((bool)Call(f,"BootstrapActive",a));
            Assert.False((bool)Call(f,"FirstIncomePriorityActive",a),"v0 must not infer Q4 history from an already-ready nonhome income site");
            var memo=typeof(AiBudgetLedger).Assembly.GetType("Spacewars.Runtime.AiExpansionFinancial").GetMethod("Load",F).Invoke(null,new object[]{a});
            Assert.False((bool)Get(memo,"FirstIncomeHistoryKnown"));Assert.False((bool)Get(memo,"FirstIncomeReady"));
        }
        [Test] public void InterimVersion2HomeGateOpensOnlyOnObservedReadyFacts()
        {
            var f=Finance();var a=new AiDecisionArbiter(AiProfile.Initial);InstallVersion2(a);
            Assert.True((bool)Call(f,"BootstrapActive",a));Reconcile(f,a,HomeState(1,false));Assert.True((bool)Call(f,"BootstrapActive",a));
            Reconcile(f,a,HomeState(2,true));Assert.False((bool)Call(f,"BootstrapActive",a));Assert.False((bool)Call(f,"FirstIncomePriorityActive",a));
            var memo=typeof(AiBudgetLedger).Assembly.GetType("Spacewars.Runtime.AiExpansionFinancial").GetMethod("Load",F).Invoke(null,new object[]{a});
            Assert.False((bool)Get(memo,"FirstIncomeHistoryKnown"));Assert.False((bool)Get(memo,"FirstIncomeReady"));
        }
        [Test] public void NullAndMalformedLegacyStatesKeepTheirDistinctSemantics()
        {
            var f=Finance();var a=new AiDecisionArbiter(AiProfile.Initial);Assert.True((bool)Call(f,"BootstrapActive",a),"null memo has no evidence that the home gate was reached");
            Reconcile(f,a,HomeState(1,false));Assert.True((bool)Call(f,"BootstrapActive",a));Reconcile(f,a,HomeState(2,true));Assert.False((bool)Call(f,"BootstrapActive",a));Assert.False((bool)Call(f,"FirstIncomePriorityActive",a));
            a=new AiDecisionArbiter(AiProfile.Initial);InstallVersion1(a,true,0);var b=new AiBudgetLedger("player-1",71);
            Assert.Throws<ArgumentException>(()=>Call(f,"Validate",PlayableProfile.Default,AiProfile.Initial,b,a,0,7,TerritoryRules.Sites(PlayableProfile.Default),2),"completed v1 history requires its original home binding");
        }
        [Test] public void NewGenesisAndReadyWholeWorldC9RemainExactWhileLegacyAbsentTailStaysUnknown()
        {
            var c=(OfflineMatchConfiguration)Call(typeof(NativeAiSiteFinancialMechanismTests),"PostOpeningEarnedConfig",true);var authority=new PlayableAuthorityTick(c,71);
            try
            {
                var loop=((IEnumerable)Get(Get(authority,"scheduler"),"owners")).Cast<object>().Single();var f=Get(loop,"expansionFinancial");var a=(AiDecisionArbiter)Get(loop,"arbiter");
                Assert.True((bool)Call(f,"BootstrapActive",a));var genesis=authority.CaptureBytes();var initial=PlayableAuthorityTick.RestoreBytes(genesis,c);try{CollectionAssert.AreEqual(genesis,initial.CaptureBytes());}finally{initial.Stop();}
                using(var host=new UnityHostRouteService())for(int t=0;t<1800&&(bool)Call(f,"BootstrapActive",a);t++)Call(typeof(NativeAiSiteFinancialMechanismTests),"Step",authority,host);
                Assert.False((bool)Call(f,"BootstrapActive",a));var bytes=authority.CaptureBytes();var clone=PlayableAuthorityTick.RestoreBytes(bytes,c);
                try{CollectionAssert.AreEqual(bytes,clone.CaptureBytes());var coldLoop=((IEnumerable)Get(Get(clone,"scheduler"),"owners")).Cast<object>().Single();Assert.False((bool)Call(Get(coldLoop,"expansionFinancial"),"BootstrapActive",Get(coldLoop,"arbiter")));}finally{clone.Stop();}
                var legacy=Start(false);Allocate(legacy.f,legacy.tech,legacy.b,legacy.a,legacy.ex,View(0,312));var payload=(string)Call(legacy.a,"SiteFundingMemory");legacy.a=Restore(legacy.a,0);Assert.AreEqual(payload,Call(legacy.a,"SiteFundingMemory"));Assert.True((bool)Call(legacy.f,"BootstrapActive",legacy.a));Assert.False((bool)Call(legacy.f,"FirstIncomePriorityActive",legacy.a));
            }
            finally{authority.Stop();}
        }
    }
}
