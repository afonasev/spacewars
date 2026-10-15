using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiSiteFinancialMechanismTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private static object Get(object o,string name)=>o.GetType().GetField(name,F).GetValue(o);
        private static object Call(object o,string name,params object[] args){try{return o.GetType().GetMethod(name,F).Invoke(o,args);}catch(TargetInvocationException e){throw e.InnerException;}}
        private static AiOpeningExecutor Opening()=>new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(41,"player-1",aiProfile:AiProfile.Initial,forcedOpening:PlayableAiOpening.GreedySafe),71);
        private static object Start()=>Activator.CreateInstance(typeof(AiBudgetLedger).Assembly.GetType("Spacewars.Runtime.AiExpansionFinancial"),F,null,new object[]{"player-1",71L},null);
        private static PlayableAiObservation View(long tick,int cash,int siteId=4,bool known=true,bool safe=true,double progress=0,PlayableProfile p=null,double actorOffset=6,bool homeReady=true,bool homeKnown=true)
        {
            p=p??PlayableProfile.Default;var s=TerritoryRules.Sites(p).Single(x=>x.Id==siteId);var actor=new PlayableEntitySnapshot(13,PlayableOwner.Player,PlayableEntityKind.Explorer,new NavPoint(s.Position.X+actorOffset,s.Position.Z),p.ExplorerHealth,false,0,0,0);
            var units=safe?new[]{actor}:new[]{actor,new PlayableEntitySnapshot(14,PlayableOwner.Enemy,PlayableEntityKind.Tank,actor.Position,p.TankHealth,false,0,0,0)};
            var role=s.Kind==PlayableBuildingKind.Mine?PlayablePublicScoutObjectiveRole.ExpansionIncome:PlayablePublicScoutObjectiveRole.ExpansionOutpost;
            var objectives=TerritoryRules.Sites(p).Where(x=>x.Kind==PlayableBuildingKind.Mine||x.Kind==PlayableBuildingKind.Outpost).Select(x=>new PlayablePublicScoutObjective(x.Id,x.Position,x.Kind==PlayableBuildingKind.Mine?PlayablePublicScoutObjectiveRole.ExpansionIncome:PlayablePublicScoutObjectiveRole.ExpansionOutpost,true)).ToArray();
            var proof=new PlayableRouteProof(13,PlayableOwner.Player,71,tick,1,actor.Position,p.ExplorerCollisionRadius,PlayableRouteTargetKind.PublicObjective,s.Id,s.Position,s.Position,new[]{s.Position});
            var home=TerritoryRules.Sites(p).Single(x=>x.Id==1);int homeId=homeKnown?home.Id:0;
            var buildings=new System.Collections.Generic.List<PlayableBuildingSnapshot>();var sites=new System.Collections.Generic.List<TerritorySiteSnapshot>();
            if(homeKnown)
            {
                int hq=1000;buildings.Add(new PlayableBuildingSnapshot(hq,PlayableOwner.Player,PlayableBuildingKind.Headquarters,home.Position,p.HeadquartersHealth,1,0,0,default,siteId:home.Id,phase:ConstructionPhase.Ready));
                int refineryCount=homeReady?Opening().RequiredHomeRefineryCount(AiProfile.Initial):0;int id=1001;
                for(int i=0;i<refineryCount;i++){var slot=home.Slots[i];buildings.Add(new PlayableBuildingSnapshot(id++,PlayableOwner.Player,PlayableBuildingKind.Refinery,slot.Position,p.RefineryHealth,1,0,0,default,siteId:home.Id,slotId:slot.Id,parentId:hq,phase:ConstructionPhase.Ready));}
                if(homeReady){var slot=home.Slots[refineryCount];buildings.Add(new PlayableBuildingSnapshot(id,PlayableOwner.Player,PlayableBuildingKind.Factory,slot.Position,p.FactoryHealth,1,0,0,default,siteId:home.Id,slotId:slot.Id,parentId:hq,phase:ConstructionPhase.Ready));}
                sites.Add(new TerritorySiteSnapshot(home,PlayableOwner.Player,PlayableOwner.Player,1,false,hq,true));
            }
            if(known)sites.Add(new TerritorySiteSnapshot(s,null,null,progress,false,0,false));
            return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,41,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,cash,null,units,buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:sites.ToArray(),publicScoutObjectives:objectives,routeProofs:new[]{proof},activeProfile:p,homeSiteId:homeId));
        }
        private static void Observe(object f,AiBudgetLedger b,AiDecisionArbiter a,AiExpansionPlanner ex,PlayableAiObservation o,int protect=0,bool allowed=true,PlayableProfile p=null)
        {p=p??PlayableProfile.Default;var opening=Opening();b.ObserveBank(o.Credits);Call(f,"Reconcile",o,p,AiProfile.Initial,opening,b,a,ex);var old=Call(f,"Detach",b);Call(f,"Observe",o,p,AiProfile.Initial,opening,b,a,ex,old,protect,allowed);}
        private static void Validate(object f,AiBudgetLedger b,AiDecisionArbiter a,long tick,PlayableProfile p=null)
        {p=p??PlayableProfile.Default;Call(f,"Validate",p,AiProfile.Initial,b,a,tick,41,TerritoryRules.Sites(p),2);}
        [Test]
        public void NewFinancialGoalAndExpiryWaitForObservedHomeReady()
        {
            var p=PlayableProfile.Default;var f=Start();var b=new AiBudgetLedger("player-1",71);var a=new AiDecisionArbiter(AiProfile.Initial);var ex=new AiExpansionPlanner("player-1");
            Call(f,"InitializeNewMatch",p,AiProfile.Initial,a);Assert.True((bool)Call(f,"BootstrapActive",a));
            foreach(long tick in new[]{45L,300L,899L})
            {
                Observe(f,b,a,ex,View(tick,500,p:p,homeReady:false),p:p);
                Assert.True((bool)Call(f,"BootstrapActive",a));Assert.AreEqual(0,(int)Call(f,"ActiveSite",a));Assert.IsEmpty(b.Capture().Reserved);
                var financialType=typeof(AiBudgetLedger).Assembly.GetType("Spacewars.Runtime.AiExpansionFinancial");
                var memo=financialType.GetMethod("Load",F).Invoke(null,new object[]{a});
                Assert.IsNull(Get(memo,"Active"));Assert.IsEmpty((System.Collections.IEnumerable)Get(memo,"Expired"));Assert.IsEmpty((System.Collections.IEnumerable)Get(memo,"Dormant"));
                Validate(f,b,a,tick,p);
            }
            const long homeReadyTick=900;
            Observe(f,b,a,ex,View(homeReadyTick,500,p:p,homeReady:true),p:p);
            Assert.False((bool)Call(f,"BootstrapActive",a),"only the observed actual home READY facts open new finance-goal creation");
            Assert.Greater((int)Call(f,"ActiveSite",a),0);
            var created=b.Capture().Reserved.Single();Assert.AreEqual("expansion-financial",created.Purpose);
            Assert.GreaterOrEqual(created.CreatedTick,homeReadyTick);Assert.AreEqual(homeReadyTick,created.CreatedTick);
            Assert.AreEqual(b.ReservationLifetimeTicks,created.ExpiresTick-created.CreatedTick,"the first post-home goal receives the existing reservation lifetime");
            Validate(f,b,a,homeReadyTick,p);
        }
        [Test] public void AllocationProtectsHigherBandsAndGrowthAloneRenewsSerializedClock()
        {
            var f=Start();var b=new AiBudgetLedger("player-1",71);var a=new AiDecisionArbiter(AiProfile.Initial);var ex=new AiExpansionPlanner("player-1");
            Observe(f,b,a,ex,View(0,64),40);var e=b.Capture().Reserved.Single();Assert.AreEqual(24,e.Amount);Assert.IsEmpty(e.Claims);Assert.IsEmpty(e.Action.EntityIds);Assert.AreEqual(40,b.Available);
            Observe(f,b,a,ex,View(300,64),40);Assert.AreEqual(e.ExpiresTick,b.Capture().Reserved.Single().ExpiresTick);
            Observe(f,b,a,ex,View(600,84),40);e=b.Capture().Reserved.Single();Assert.AreEqual(44,e.Amount);Assert.AreEqual(600,e.CreatedTick);
            Observe(f,b,a,ex,View(900,84),70);var shrunk=b.Capture().Reserved.Single();Assert.AreEqual(14,shrunk.Amount);Assert.AreEqual(e.CreatedTick,shrunk.CreatedTick);Assert.AreEqual(e.ExpiresTick,shrunk.ExpiresTick);
            Observe(f,b,a,ex,View(1000,84),84);Assert.Zero(b.Reserved);Assert.AreEqual(e.ExpiresTick,b.Capture().Reserved.Single().ExpiresTick);Validate(f,b,a,1000);
        }
        [Test] public void ExpiredFingerprintsSurviveAlternativeSiteMetadataUnknownAndHistoryChurn()
        {
            var f=Start();var b=new AiBudgetLedger("player-1",71);var a=new AiDecisionArbiter(AiProfile.Initial);var ex=new AiExpansionPlanner("player-1");
            Observe(f,b,a,ex,View(0,64));long expiry=b.Capture().Reserved.Single().ExpiresTick;
            Observe(f,b,a,ex,View(expiry,64));Assert.IsEmpty(b.Capture().Reserved);
            Observe(f,b,a,ex,View(expiry+1,64,3));Assert.AreEqual(3,b.Capture().Reserved.Single().Action.SiteId);long second=b.Capture().Reserved.Single().ExpiresTick;
            Observe(f,b,a,ex,View(second,64,3));Assert.IsEmpty(b.Capture().Reserved);
            var data=PlayableProfile.Default.CopyData();data.revision++;var next=PlayableProfile.Create(data);Call(f,"Rebind",next,AiProfile.Initial,second+1,b,a,null);
            for(int n=0;n<160;n++)
            {long t=second+2+n;var v=View(t,164,n%2==0?4:3,p:next);var action=new PlayableAiAction(n+1,v.OwnerId,v.ProfileId,v.ProfileRevision,71,t,PlayableCommandKind.Move,new[]{13});a.Select(new[]{new AiIntent("churn"+n,"scout",action,t+1,0,0,Array.Empty<string>())},t,164,10,1,b);Observe(f,b,a,ex,v,p:next);Assert.IsEmpty(b.Capture().Reserved);}
            Observe(f,b,a,ex,View(second+200,164,known:false,p:next),p:next);Observe(f,b,a,ex,View(second+201,164,p:next),p:next);Assert.IsEmpty(b.Capture().Reserved);Validate(f,b,a,second+201,next);
            Observe(f,b,a,ex,View(second+202,164,progress:.1,p:next),p:next);Assert.AreEqual(4,b.Capture().Reserved.Single().Action.SiteId,"Only material current capture progress reopens expiry");Validate(f,b,a,second+202,next);
        }
        [Test] public void AnotherExpansionKeepsOriginalFinancialClockAtZeroAndExpiryTombstonesOwnerClaim()
        {
            var f=Start();var b=new AiBudgetLedger("player-1",71);var a=new AiDecisionArbiter(AiProfile.Initial);var ex=new AiExpansionPlanner("player-1");
            Observe(f,b,a,ex,View(0,64));var original=b.Capture().Reserved.Single();
            var other=View(100,500,siteId:3,actorOffset:20);var claim=ex.TryPlan(other,PlayableProfile.Default,AiProfile.Initial,0,_=>true,_=>true);Assert.NotNull(claim);
            Observe(f,b,a,ex,other);var zero=b.Capture().Reserved.Single(x=>x.Purpose=="expansion-financial");
            Assert.Zero(zero.Amount);Assert.AreEqual(original.CreatedTick,zero.CreatedTick);Assert.AreEqual(original.ExpiresTick,zero.ExpiresTick);Validate(f,b,a,100);
            var detached=Call(f,"Detach",b);Call(f,"Observe",other,PlayableProfile.Default,AiProfile.Initial,Opening(),b,a,ex,detached,0,true);
            zero=b.Capture().Reserved.Single(x=>x.Purpose=="expansion-financial");Assert.Zero(zero.Amount);Assert.AreEqual(original.CreatedTick,zero.CreatedTick);Assert.AreEqual(original.ExpiresTick,zero.ExpiresTick);
            var atExpiry=View(original.ExpiresTick,64,siteId:3);Observe(f,b,a,ex,atExpiry);Assert.IsEmpty(b.Capture().Reserved);
            var unchanged=View(original.ExpiresTick,500,siteId:4,actorOffset:20);Observe(f,b,a,ex,unchanged);Assert.True((bool)Call(f,"IsExpiredFingerprint",unchanged.Sites.Single(s=>s.Site.Id==4),a));
            var cleanPlanner=new AiExpansionPlanner("player-1");var denied=cleanPlanner.TryPlan(unchanged,PlayableProfile.Default,AiProfile.Initial,0,_=>true,id=>!(bool)Call(f,"IsExpiredFingerprint",unchanged.Sites.FirstOrDefault(s=>s.Site.Id==id),a));Assert.IsNull(denied);
            var changed=View(original.ExpiresTick+1,500,siteId:4,progress:.1,actorOffset:20);Assert.False((bool)Call(f,"IsExpiredFingerprint",changed.Sites.Single(s=>s.Site.Id==4),a));
            var allowed=new AiExpansionPlanner("player-1").TryPlan(changed,PlayableProfile.Default,AiProfile.Initial,0,_=>true,id=>!(bool)Call(f,"IsExpiredFingerprint",changed.Sites.FirstOrDefault(s=>s.Site.Id==id),a));Assert.NotNull(allowed);
        }
        [Test] public void OrdinaryFallbackMustFitEveryHoldSafetyAndProtectedFundsWithoutReleasingFinance()
        {
            var f=Start();var b=new AiBudgetLedger("player-1",71);var a=new AiDecisionArbiter(AiProfile.Initial);var ex=new AiExpansionPlanner("player-1");var o=View(0,500);
            var action=ex.TryPlan(o,PlayableProfile.Default,AiProfile.Initial);Assert.NotNull(action);b.ObserveBank(500);b.SetSafetyReserve(10);
            foreach(var hold in new[]{("finance",100), ("technology",75), ("startup",50)})
                Assert.True(b.Reserve(new AiBudgetEntry(hold.Item1,hold.Item1,action,hold.Item2,1,0,1000,"test",Array.Empty<string>(),"test")));
            var fallback=new AiIntent("fallback","expansion",action,1000,0,0,new[]{"recipient:13"},1,startupFund:200);
            Assert.AreEqual(265,b.Available);Assert.True((bool)Call(f,"FullyFundable",b,65,fallback));Assert.False((bool)Call(f,"FullyFundable",b,66,fallback));
            Assert.False((bool)Call(f,"FullyFundable",b,0,new AiIntent("too-much","expansion",action,1000,0,0,new[]{"recipient:13"},1,startupFund:266)));
            Assert.AreEqual(225,b.Reserved);Assert.AreEqual(265,b.Available);Assert.AreEqual(3,b.Capture().Reserved.Count);
        }
        [Test] public void UnknownUnsafeAndSurvivalReleaseMoneyWithoutRenewingOrCommandeering()
        {
            var f=Start();var b=new AiBudgetLedger("player-1",71);var a=new AiDecisionArbiter(AiProfile.Initial);var ex=new AiExpansionPlanner("player-1");
            Observe(f,b,a,ex,View(0,64));var initial=b.Capture().Reserved.Single();Observe(f,b,a,ex,View(100,64,known:false));Assert.Zero(b.Reserved);Assert.AreEqual(initial.ExpiresTick,b.Capture().Reserved.Single().ExpiresTick);
            Observe(f,b,a,ex,View(200,64,safe:false));Assert.Zero(b.Reserved);Observe(f,b,a,ex,View(300,64),allowed:false);Assert.Zero(b.Reserved);Assert.AreEqual(initial.ExpiresTick,b.Capture().Reserved.Single().ExpiresTick);Assert.Zero(ex.Capture().SiteId);Validate(f,b,a,300);
        }
        [TestCase(false)][TestCase(true)] public void FullMoveOrBuildInvoiceUsesOneRealHoldAndRemainsOrdinaryAged(bool build)
        {
            var f=Start();var b=new AiBudgetLedger("player-1",71);var a=new AiDecisionArbiter(AiProfile.Initial);var ex=new AiExpansionPlanner("player-1");var o=View(0,300);
            Observe(f,b,a,ex,o);var e=b.Capture().Reserved.Single();int price=TerritoryRules.Cost(PlayableProfile.Default,e.Action.BuildingKind);
            var action=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,71,0,build?PlayableCommandKind.BuildAt:PlayableCommandKind.Move,new[]{13},siteId:4,buildingKind:e.Action.BuildingKind,seed:41,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            var old=new AiIntent("aged","production",action,10000,0,0,Array.Empty<string>(),0);a.Select(new[]{old},0,300,10,1,b);
            var funded=new AiIntent("funded","expansion",action,10000,build?price:0,0,new[]{"recipient:13"},1,startupFund:build?0:price,reservationId:e.Id);
            long age=AiProfile.SecondsToTicks(AiProfile.Initial.Value("decision.intentAgingSeconds"),30);
            Assert.AreEqual(old,a.Select(new[]{old,funded},age,300,10,1,b,gameplayProfile:PlayableProfile.Default).Single(),"Financial full hold cannot spoof accepted-startup band above ordinary aging");
            var extra=new AiIntent("extra","economy",action,10000,100,0,new[]{"other"});Assert.AreEqual(2,a.Select(new[]{funded,extra},age+1,300,10,2,b,gameplayProfile:PlayableProfile.Default).Count,"Full financial addback must not subtract its own startup twice");
            var ordinary=(AiIntent)Call(f,"Consume",funded,b,a,PlayableProfile.Default);Assert.IsNull(ordinary.ReservationId);Assert.AreEqual(funded.Credits,ordinary.Credits);Assert.AreEqual(funded.StartupFund,ordinary.StartupFund);Assert.Zero(b.Reserved);Assert.AreEqual(300,b.Available);Validate(f,b,a,age+1);
        }
        [Test] public void ZeroPriceRebindUsesOrdinaryFreeAdmissionWithoutRenewalOrFinancialIdentity()
        {
            var f=Start();var b=new AiBudgetLedger("player-1",71);var a=new AiDecisionArbiter(AiProfile.Initial);var ex=new AiExpansionPlanner("player-1");Observe(f,b,a,ex,View(0,64));var first=b.Capture().Reserved.Single();
            var data=PlayableProfile.Default.CopyData();data.revision++;data.mineCreditCost=0;var p=PlayableProfile.Create(data);var previous=Call(f,"Detach",b);Call(f,"Rebind",p,AiProfile.Initial,300L,b,a,previous);
            var zero=b.Capture().Reserved.Single();Assert.Zero(zero.Amount);Assert.AreEqual(first.CreatedTick,zero.CreatedTick);Assert.AreEqual(first.ExpiresTick,zero.ExpiresTick);
            var o=View(300,64,p:p);var action=ex.TryPlan(o,p,AiProfile.Initial);Assert.NotNull(action);Assert.IsNull(Call(f,"ReservationFor",action,o,p,b,a),"A free real command never borrows a zero-amount financial startup identity");
            var intent=new AiIntent("free","expansion",action,400,0,0,new[]{"recipient:13"},1);Assert.AreEqual(intent,a.Select(new[]{intent},300,64,10,1,b,gameplayProfile:p).Single());Assert.Zero(b.PaidTotal);Assert.True((bool)Call(f,"ShouldConsume",intent,b,a,p));var ordinary=(AiIntent)Call(f,"Consume",intent,b,a,p);Assert.IsNull(ordinary.ReservationId);Assert.IsEmpty(b.Capture().Reserved);Assert.AreEqual(0,(int)Call(f,"ActiveSite",a));Validate(f,b,a,300,p);
        }
        [Test] public void MalformedLedgerAndExpiredFingerprintBindingsAreRejected()
        {
            var f=Start();var b=new AiBudgetLedger("player-1",71);var a=new AiDecisionArbiter(AiProfile.Initial);var ex=new AiExpansionPlanner("player-1");Observe(f,b,a,ex,View(0,64));Validate(f,b,a,0);var e=b.Capture().Reserved.Single();b.Release(e.Id);
            var wrong=new AiBudgetEntry(e.Id,e.Purpose,new PlayableAiAction(1,"player-1",e.Action.ProfileId,e.Action.ProfileRevision,71,0,PlayableCommandKind.BuildAt,new[]{13},siteId:4,buildingKind:e.Action.BuildingKind,seed:41,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity),e.Amount,1,e.CreatedTick,e.ExpiresTick,e.ReleaseCondition,Array.Empty<string>(),e.Terms);Assert.True(b.Reserve(wrong));Assert.Throws<ArgumentException>(()=>Validate(f,b,a,0));
            Call(a,"SaveSiteFundingMemory","AAAA");Assert.Throws<ArgumentException>(()=>Validate(f,b,a,0));Call(a,"SaveSiteFundingMemory",new object[]{null});Assert.Throws<ArgumentException>(()=>Validate(f,b,a,0));
        }
        private static string Root(){var p=new DirectoryInfo(Directory.GetCurrentDirectory());while(p!=null&&!File.Exists(Path.Combine(p.FullName,"workflow/project.json")))p=p.Parent;Assert.NotNull(p);return p.FullName;}
        private static OfflineMatchConfiguration EarnedConfig(bool captured)
        {
            var data=PlayableProfile.Default.CopyData();data.startingCredits=captured?64:190;var p=PlayableProfile.Create(data);
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-24,0),new NavPoint(-24,6),pin:1),new OfflineStart("east",2,new NavPoint(24,24),new NavPoint(18,24),pin:2)};
            var sites=new[]{new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,Array.Empty<TerritorySlot>()),new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,Array.Empty<TerritorySlot>()),new TerritorySite(3,PlayableBuildingKind.Mine,new NavPoint(-11,0),p)};
            var roster=new[]{new OfflineParticipant("west-owner",1,1,OfflineControl.Ai),new OfflineParticipant("east-owner",2,1,OfflineControl.Human)};
            var near=new NavPoint(sites[2].Position.X+TerritoryRules.CaptureRadius(p,PlayableBuildingKind.Mine)-p.ExplorerCollisionRadius,sites[2].Position.Z);
            var config=new OfflineMatchConfiguration(p,"site-finance-earned-mechanism","site-finance-earned-flat-v1","UnityHostRouteService",19092026,roster,starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,captured?near:new NavPoint(-20,7))});
            return config;
        }
        private static OfflineMatchConfiguration PostOpeningEarnedConfig(bool captured)
            =>NativeAiPostOpeningFixtures.WithReadyHome(EarnedConfig(captured),captured?"earned-captured":"earned-move");
        private static void Step(PlayableAuthorityTick a,UnityHostRouteService host)
        {for(int n=0;n<512;n++){if(a.TryAdvance())return;host.Service(a,64);}Assert.Fail("Ordinary authority route barrier");}
        [TestCase("null")][TestCase("empty")][TestCase("wrong-site")][TestCase("duplicate-expiry")][TestCase("missing-hold")][TestCase("actor-bound")]
        public void WholeC9RejectsPresentCorruptMemoAndLedger(string corruption)
        {
            var c=PostOpeningEarnedConfig(true);var a=new PlayableAuthorityTick(c,71);
            try
            {
                using(var host=new UnityHostRouteService())for(int n=0;n<45;n++)Step(a,host);
                var loop=((IEnumerable)Get(Get(a,"scheduler"),"owners")).Cast<object>().Single();var b=(AiBudgetLedger)Get(loop,"budget");var arbiter=(AiDecisionArbiter)Get(loop,"arbiter");
                var original=a.CaptureBytes();var good=PlayableAuthorityTick.RestoreBytes(original,c);try{CollectionAssert.AreEqual(original,good.CaptureBytes());}finally{good.Stop();}
                var history=(IDictionary)Get(arbiter,"history");var memo=history["expansion-financial-lifecycle-v1"];Assert.NotNull(memo);
                if(corruption=="null"||corruption=="empty")memo.GetType().GetField("Conditions",F).SetValue(memo,corruption=="null"?null:"");
                else if(corruption=="missing-hold")b.Release(b.Capture().Reserved.Single(x=>x.Purpose=="expansion-financial").Id);
                else if(corruption=="actor-bound")
                {var e=b.Capture().Reserved.Single(x=>x.Purpose=="expansion-financial");b.Release(e.Id);var action=new PlayableAiAction(1,e.Action.PlayerId,e.Action.ProfileId,e.Action.ProfileRevision,71,e.Action.SnapshotSequence,PlayableCommandKind.BuildAt,new[]{13},siteId:3,buildingKind:PlayableBuildingKind.Mine,seed:c.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);Assert.True(b.Reserve(new AiBudgetEntry(e.Id,e.Purpose,action,e.Amount,1,e.CreatedTick,e.ExpiresTick,e.ReleaseCondition,e.Claims,e.Terms)));}
                else
                {
                    using(var stream=new MemoryStream()){using(var w=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))
                    {w.Write(0x45464d31);w.Write("west-owner");w.Write(71L);w.Write(c.Profile.ProfileId+"@"+c.Profile.Revision+":"+AiProfile.Initial.Hash);w.Write(true);
                     Action<int> finger=id=>{w.Write(id);w.Write((int)PlayableBuildingKind.Mine);w.Write(-1);w.Write(-1);w.Write(0);w.Write(false);w.Write(0d);};finger(corruption=="wrong-site"?999:3);w.Write(corruption=="duplicate-expiry"?2:0);if(corruption=="duplicate-expiry"){finger(3);finger(3);}}
                    memo.GetType().GetField("Conditions",F).SetValue(memo,Convert.ToBase64String(stream.ToArray()));}
                }
                var bad=a.CaptureBytes();var error=Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(bad,c));
                var folder=Path.Combine(Root(),".local/o6-site-financial-correctness");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"malformed-"+corruption+".json"),JsonConvert.SerializeObject(new{corruption,error.Message,Tick=a.Tick}));
            }
            finally{a.Stop();}
        }
        [Test] public void ActiveWholeC9RebindPreservesAmountAndClockAcrossGameplayAndAiMetadata()
        {
            var c=PostOpeningEarnedConfig(true);var a=new PlayableAuthorityTick(c,71);
            try
            {
                using(var host=new UnityHostRouteService())for(int n=0;n<45;n++)Step(a,host);
                var loop=((IEnumerable)Get(Get(a,"scheduler"),"owners")).Cast<object>().Single();var b=(AiBudgetLedger)Get(loop,"budget");var first=b.Capture().Reserved.Single(x=>x.Purpose=="expansion-financial");
                var data=c.Profile.CopyData();data.revision++;data.mineCreditCost+=50;var p=PlayableProfile.Create(data);var aiData=AiProfile.Initial.CopyData();aiData.revision++;var ai=new AiProfile(aiData);Assert.IsNull(a.ApplyProfile(p,ai));
                var after=b.Capture().Reserved.Single(x=>x.Purpose=="expansion-financial");Assert.AreEqual(first.Amount,after.Amount);Assert.AreEqual(first.CreatedTick,after.CreatedTick);Assert.AreEqual(first.ExpiresTick,after.ExpiresTick);Assert.AreEqual(p.Revision,after.Action.ProfileRevision);
                var routeCosts=new double[c.Starts.Count,c.Starts.Count];for(int i=0;i<c.Starts.Count;i++)for(int j=0;j<c.Starts.Count;j++)routeCosts[i,j]=c.RouteCost(i,j);
                var revised=new OfflineMatchConfiguration(p,c.SourceIdentity,c.MapIdentity,c.RouteProvenance,c.Seed,c.Roster.ToArray(),c.Starts.ToArray(),c.Sites.ToArray(),c.Obstacles.ToArray(),routeCosts,
                    spectators:c.Spectators.ToArray(),scenario:c.ScenarioUnits.ToArray(),terrain:c.Profile.AuthoredMap==null?"flat-ground-v1":c.Profile.AuthoredMap.Id,scenarioBuildings:c.ScenarioBuildings.ToArray());
                var bytes=a.CaptureBytes();var restore=PlayableAuthorityTick.RestoreBytes(bytes,revised,ai);try{CollectionAssert.AreEqual(bytes,restore.CaptureBytes());}finally{restore.Stop();}
            }
            finally{a.Stop();}
        }
        [TestCase(false)][TestCase(true)] public void RealEarnedMoneyFundsFreshOrdinaryMoveOrCapturedBuildAndPaidIncome(bool captured)
        {
            // Explicit new mechanism world, unchanged gameplay prices/income/durations.
            // Explicit initial cash makes one partial hold before income completes the full price.
            // Additional scenario actors are authored at genesis, never spawned/reset during execution.
            var config=PostOpeningEarnedConfig(captured);var p=config.Profile;
            int mineQuote=TerritoryRules.Cost(p,PlayableBuildingKind.Mine);var a=new PlayableAuthorityTick(config,71);bool partial=false,ready=false,move=false,paid=false;long firstAdmission=0;int maxAmount=0;var folder=Path.Combine(Root(),".local/o6-site-financial-correctness");Directory.CreateDirectory(folder);
            try
            {
                var loop=((IEnumerable)Get(Get(a,"scheduler"),"owners")).Cast<object>().Single();var b=(AiBudgetLedger)Get(loop,"budget");
                using(var host=new UnityHostRouteService())for(int n=0;n<1800;n++)
                {
                    bool advanced=false;for(int attempt=0;attempt<512;attempt++){if(a.TryAdvance()){advanced=true;break;}host.Service(a,64);}Assert.True(advanced,"Original route barrier");
                    var view=a.ParticipantView("west-owner");var held=b.Capture().Reserved.SingleOrDefault(x=>x.Purpose=="expansion-financial");if(held!=null){partial|=held.Amount>0&&held.Amount<mineQuote;maxAmount=Math.Max(maxAmount,held.Amount);Assert.IsEmpty(held.Action.EntityIds);Assert.IsEmpty(held.Claims);}
                    var pending=((IEnumerable)Get(loop,"pendingItems")).Cast<object>().Where(x=>(string)Get(x,"PolicyName")=="expansion").ToArray();
                    if(pending.Length>0&&firstAdmission==0)
                    {firstAdmission=a.Tick;Assert.GreaterOrEqual(view.SettledIncome,mineQuote-p.StartingCredits);Assert.GreaterOrEqual(view.Credits,mineQuote);Assert.IsNull(((AiIntent)Get(pending[0],"Intent")).ReservationId,"Consumed site finance is not a fake pending startup identity");}
                    var records=(System.Collections.Generic.List<PlayableAiTraceRecord>)Get(loop,"records");move|=records.Any(x=>x.Policy=="expansion"&&x.Kind==PlayableCommandKind.Move&&x.Status==PlayableAiDeliveryStatus.Applied);paid|=b.Capture().Paid.Any(x=>x.Purpose=="expansion"&&x.Amount==mineQuote);
                    ready|=view.Buildings.Any(x=>x.SiteId==3&&x.SlotId==0&&x.Phase==ConstructionPhase.Ready);
                    if(a.Tick%45==0||pending.Length>0){var bytes=a.CaptureBytes();var clone=PlayableAuthorityTick.RestoreBytes(bytes,config);try{CollectionAssert.AreEqual(bytes,clone.CaptureBytes(),"Exact wholeC9 including financial, full Move/Build pending and real payment states");}finally{clone.Stop();}}
                    if(ready&&paid)break;
                }
                File.WriteAllBytes(Path.Combine(folder,captured?"captured-paid.world":"move-paid.world"),a.CaptureBytes());File.WriteAllText(Path.Combine(folder,captured?"captured-paid.json":"move-paid.json"),JsonConvert.SerializeObject(new{a.Tick,partial,maxAmount,firstAdmission,move,paid,ready,Budget=b.Capture(),View=a.ParticipantView("west-owner")}));
                Assert.True(partial);Assert.Greater(firstAdmission,0);Assert.True(paid);Assert.True(ready);if(!captured)Assert.True(move);else Assert.False(move,"Already captured ordinary BuildAt should not manufacture a Move invoice");
            }
            finally{a.Stop();}
        }
    }
}
