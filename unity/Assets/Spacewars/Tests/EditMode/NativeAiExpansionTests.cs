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
    public sealed class NativeAiExpansionTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        private static object Get(object o,string n)=>o.GetType().GetField(n,F).GetValue(o);
        private static object Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,F).Invoke(o,a);
        private static OfflineMatchConfiguration Config(PlayableBuildingKind kind=PlayableBuildingKind.Mine,bool hidden=false)
        {
            var p=PlayableProfile.Default;
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-24,0),new NavPoint(-24,6),pin:1),new OfflineStart("east",2,new NavPoint(24,24),new NavPoint(18,24),pin:2)};
            var sites=new[]{new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,Array.Empty<TerritorySlot>()),new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,Array.Empty<TerritorySlot>()),new TerritorySite(3,kind,new NavPoint(-11,0),p),new TerritorySite(4,PlayableBuildingKind.Outpost,new NavPoint(20,-20),p)};
            var roster=new[]{new OfflineParticipant("west-owner",1,1,OfflineControl.Ai),new OfflineParticipant("east-owner",2,2,OfflineControl.Human)};
            return new OfflineMatchConfiguration(p,"e4-safe-expansion","e4-public-flat-v1","UnityHostRouteService",19092026,roster,starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},scenarioBuildings:hidden?new[]{new OfflineScenarioBuilding(2,PlayableBuildingKind.Outpost,4,0,new NavPoint(20,-20))}:null);
        }
        private static object Owner(PlayableAuthorityTick a)=>((System.Collections.IEnumerable)Get(Get(a,"scheduler"),"owners")).Cast<object>().Single();
        private static AiExpansionPlanner Planner(PlayableAuthorityTick a)=>(AiExpansionPlanner)Get(Owner(a),"expansion");
        private static PlayableSnapshot View(PlayableAuthorityTick a,OfflineMatchConfiguration c)=>(PlayableSnapshot)Call(Get(a,"domain"),"PlayerSnapshot",a.Tick,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed,PlayableOwner.Player);
        private static void Step(PlayableAuthorityTick a,UnityHostRouteService host)
        {for(int i=0;i<512;i++){if(a.TryAdvance())return;host.Service(a,64);}Assert.Fail("route barrier did not complete");}
        [Test] public void LayoutConstructorPreflight()
        {
            foreach(var kind in new[]{PlayableBuildingKind.Mine,PlayableBuildingKind.Outpost})foreach(bool hidden in new[]{false,true})
            {
                var c=Config(kind,hidden);var p=c.Profile;
                var envelopes=c.Sites.SelectMany(s=>new[]{Tuple.Create(s.Position,TerritoryRules.Radius(p,s.Kind),s.Id,true)}.Concat(s.Slots.Select(t=>Tuple.Create(t.Position,Math.Max(p.ScienceFootprintRadius,Math.Max(p.FactoryFootprintRadius,p.RefineryFootprintRadius)),s.Id,false)))).ToArray();
                foreach(var e in envelopes)Assert.Less(Math.Max(Math.Abs(e.Item1.X),Math.Abs(e.Item1.Z))+e.Item2*Math.Sqrt(2)+p.ExplorerCollisionRadius,p.ArenaHalfExtent);
                foreach(var start in c.Starts)foreach(var e in envelopes)Assert.Greater(Math.Sqrt(Math.Pow(start.ExplorerAnchor.X-e.Item1.X,2)+Math.Pow(start.ExplorerAnchor.Z-e.Item1.Z,2)),e.Item2+p.ExplorerCollisionRadius);
                TestContext.WriteLine("preflight kind="+kind+" hidden="+hidden+" envelopes="+envelopes.Length);
            }
        }
        [TestCase(PlayableBuildingKind.Mine)][TestCase(PlayableBuildingKind.Outpost)]
        public void CaptureToIncomeUsesRealTravelCaptureBuildPaymentAndSettlement(PlayableBuildingKind kind)
        {
            var c=Config(kind);var a=new PlayableAuthorityTick(c,71);int actor=View(a,c).Entities.Single(e=>e.Owner==PlayableOwner.Player).Id;
            double readyBaseline=0,readyBank=0;double before=View(a,c).SettledIncome;bool claimed=false,captured=false,paid=false,ready=false;int initial=View(a,c).Credits;long incomeReady=0;
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<1800;i++)
                {
                    Step(a,host);var state=Planner(a).Capture();var v=View(a,c);var s=v.Sites.Single(x=>x.Site.Id==3);
                    if(state.SiteId==3){claimed=true;Assert.AreEqual(actor,state.ActorId);Assert.LessOrEqual(state.CreatedTick,state.ProgressTick);}
                    captured|=s.Progress>=1&&s.Claimant==PlayableOwner.Player;
                    var b=v.Buildings.FirstOrDefault(x=>x.SiteId==3&&x.SlotId==0&&x.Owner==PlayableOwner.Player);
                    if(b!=null){Assert.True(captured);paid=true;Assert.Less(v.Credits,initial+v.SettledIncome);}
                    if(b?.Phase==ConstructionPhase.Ready){ready=true;if(incomeReady==0){incomeReady=a.Tick;readyBaseline=v.SettledIncome;readyBank=v.ExactCredits;}Assert.AreEqual(PlayableOwner.Player,s.Owner);}
                    if(ready&&state.SiteId==0){Assert.Greater(a.Tick,incomeReady);Assert.Greater(v.SettledIncome,before);StringAssert.Contains("settled",state.Reason);
                        double otherRate=v.Buildings.Where(x=>x.Owner==PlayableOwner.Player&&x.Phase==ConstructionPhase.Ready&&x.SiteId!=3&&x.PrivateState?.Lifecycle?.Selling!=true)
                            .Sum(x=>x.RefineryUpgraded?c.Profile.RefineryUpgradedIncome:TerritoryRules.Income(c.Profile,x.Kind))/c.Profile.IncomePeriodSeconds;
                        double expansionRate=TerritoryRules.Income(c.Profile,kind)/c.Profile.IncomePeriodSeconds;
                        Assert.AreEqual(otherRate+expansionRate,v.SettledIncome-readyBaseline,1e-8,"actual first full payout includes the new income center");
                        Assert.AreEqual(otherRate+expansionRate,v.ExactCredits-readyBank,1e-8,"bank receives actual expansion payout alongside existing sources");
                        TestContext.WriteLine("E2-07 kind="+kind+" readyTick="+incomeReady+" settlementTick="+a.Tick+" bank="+readyBank+"->"+v.ExactCredits+" settled="+readyBaseline+"->"+v.SettledIncome+" otherRate="+otherRate+" expansionRate="+expansionRate);
                        break;}
                }
            }
            Assert.True(claimed);Assert.True(captured);Assert.True(paid);Assert.True(ready);Assert.Zero(Planner(a).Capture().SiteId);
            var checkpoint=(PlayableAiOwnerCheckpoint)Owner(a).GetType().GetProperty("Checkpoint",F).GetValue(Owner(a));
            Assert.True(checkpoint.Records.Any(r=>r.Policy=="expansion"&&r.Kind==PlayableCommandKind.Move&&r.Status==PlayableAiDeliveryStatus.Applied));
            Assert.True(checkpoint.Records.Any(r=>r.Policy=="expansion"&&r.Kind==PlayableCommandKind.BuildAt&&r.Status==PlayableAiDeliveryStatus.Applied));
            var ledger=(AiBudgetLedger)Get(Owner(a),"budget");Assert.True(ledger.Capture().Paid.Any(e=>e.Purpose=="expansion"&&e.Amount==TerritoryRules.Cost(c.Profile,kind)));
        }
        private static OfflineMatchConfiguration CompetingSpendConfig()
        {
            var c=Config();var p=c.Profile;
            var starts=new[]{new OfflineStart("west",1,c.Starts[0].Position,new NavPoint(-24,7),pin:1),c.Starts[1]};
            var sites=c.Sites.Select(x=>x.Id==1?new TerritorySite(1,PlayableBuildingKind.Headquarters,x.Position,p):x).ToArray();
            return new OfflineMatchConfiguration(p,c.SourceIdentity,"e4-competing-spend-v1",c.RouteProvenance,c.Seed,c.Roster.ToArray(),starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}});
        }
        [Test] public void AcceptedExpansionProtectsFutureBuildFromCompetingSpendThenReleasesToScout()
        {
            var c=CompetingSpendConfig();var a=new PlayableAuthorityTick(c,71);var ledger=(AiBudgetLedger)Get(Owner(a),"budget");
            bool held=false,paid=false,ready=false,completed=false;double readyIncome=0,readyBank=0;long readyTick=0,releaseTick=0;int actor=View(a,c).Entities.Single(e=>e.Owner==PlayableOwner.Player).Id;
            using(var host=new UnityHostRouteService())
            {
                long expiry=AiProfile.SecondsToTicks(AiProfile.Initial.Value("economy.reservationExpirySeconds"),30);
                for(long i=0;i<expiry+90&&!completed;i++)
                {
                    Step(a,host);var v=View(a,c);var state=Planner(a).Capture();var fund=ledger.Capture().Reserved.FirstOrDefault(e=>e.Purpose=="expansion-build");
                    if(state.SiteId==3&&!paid&&!v.Buildings.Any(b=>b.SiteId==3&&b.Owner==PlayableOwner.Player))
                    {
                        var unpaid=ledger.Capture().Unpaid.FirstOrDefault(e=>e.Purpose=="expansion"&&e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.SiteId==3);
                        Assert.AreEqual(1,(fund!=null?1:0)+(unpaid!=null?1:0),"one protected held or actually admitted unpaid build, never double hold");
                        var protection=fund??unpaid;Assert.AreEqual(c.Profile.MineCreditCost,protection.Amount);Assert.AreEqual(3,protection.Action.SiteId);CollectionAssert.AreEqual(new[]{actor},protection.Action.EntityIds);
                        if(fund!=null)held=true;
                        else{Assert.True(held);Assert.NotNull(unpaid.Receipt);Assert.True(Checkpoint(a).Records.Any(r=>r.Policy=="expansion"&&r.Kind==PlayableCommandKind.BuildAt&&r.Status==PlayableAiDeliveryStatus.Scheduled&&r.ReceiptIdentity.Id==unpaid.Receipt.Id));}
                        Assert.GreaterOrEqual(ledger.Liquid-ledger.Unpaid,ledger.Reserved);
                        Assert.True(v.Sites.Single(x=>x.Site.Id==1).Site.Slots.Count>0);
                    }
                    var mine=v.Buildings.FirstOrDefault(b=>b.SiteId==3&&b.Owner==PlayableOwner.Player);
                    if(mine!=null)
                    {paid=true;Assert.True(held);Assert.False(ledger.Capture().Reserved.Any(e=>e.Purpose=="expansion-build"));Assert.False(ledger.Capture().Unpaid.Any(e=>e.Purpose=="expansion"&&e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.SiteId==3));Assert.AreEqual(1,ledger.Capture().Paid.Count(e=>e.Purpose=="expansion"&&e.Action.Kind==PlayableCommandKind.BuildAt));}
                    if(mine?.Phase==ConstructionPhase.Ready)
                    {
                        ready=true;if(readyTick==0){readyTick=a.Tick;readyIncome=v.SettledIncome;readyBank=v.ExactCredits;}
                        if(state.SiteId==0)
                        {
                            double expected=v.Buildings.Where(b=>b.Owner==PlayableOwner.Player&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.Lifecycle?.Selling!=true).Sum(b=>b.RefineryUpgraded?c.Profile.RefineryUpgradedIncome:TerritoryRules.Income(c.Profile,b.Kind))/c.Profile.IncomePeriodSeconds;
                            Assert.AreEqual(expected,v.SettledIncome-readyIncome,1e-8);Assert.GreaterOrEqual(v.ExactCredits-readyBank,expected-1e-8);
                            StringAssert.Contains("settled",state.Reason);completed=true;releaseTick=a.Tick;
                            TestContext.WriteLine("competing expansion ready="+readyTick+" release="+releaseTick+" bank="+readyBank+"->"+v.ExactCredits+" settled="+readyIncome+"->"+v.SettledIncome+" rate="+expected);
                        }
                    }
                }
                Assert.True(held);Assert.True(paid);Assert.True(ready);Assert.True(completed);Assert.False(Planner(a).ClaimsActor(actor));Assert.False(Planner(a).ClaimsSite(3));
                Assert.True(Checkpoint(a).Records.Any(r=>r.Policy=="economy"&&r.Kind==PlayableCommandKind.BuildAt&&r.Status==PlayableAiDeliveryStatus.Applied));
                long deadline=AiProfile.SecondsToTicks(AiProfile.Initial.DifficultyValue(AiDifficulty.Fighter,"decisionSeconds")+AiProfile.Initial.DifficultyValue(AiDifficulty.Fighter,"reactionSeconds"),30)+1;
                for(long i=0;i<deadline&&!Checkpoint(a).Records.Any(r=>r.Policy=="scout"&&r.Status==PlayableAiDeliveryStatus.Applied&&r.ApplicationTick>=releaseTick);i++)Step(a,host);
                Assert.True(Checkpoint(a).Records.Any(r=>r.Policy=="scout"&&r.Kind==PlayableCommandKind.Move&&r.Status==PlayableAiDeliveryStatus.Applied&&r.ApplicationTick>=releaseTick&&r.ApplicationTick-releaseTick<=deadline));
                var restored=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);CollectionAssert.AreEqual(a.CaptureBytes(),restored.CaptureBytes());
            }
        }
        private static OfflineMatchConfiguration ProfileConfig(OfflineMatchConfiguration c,PlayableProfile p)=>new OfflineMatchConfiguration(p,c.SourceIdentity,c.MapIdentity,c.RouteProvenance,c.Seed,c.Roster.ToArray(),c.Starts.ToArray(),c.Sites.ToArray(),c.Obstacles.ToArray(),new double[,]{{0,c.RouteCost(0,1)},{c.RouteCost(1,0),0}},scenarioBuildings:c.ScenarioBuildings.ToArray());
        [Test] public void LiveFundingHonorsPauseCancelledBuildRepriceRestoreAndPaidTerms()
        {
            var c=CompetingSpendConfig();var a=new PlayableAuthorityTick(c,71);var ledger=(AiBudgetLedger)Get(Owner(a),"budget");
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<100&&Planner(a).Capture().SiteId==0;i++)Step(a,host);
                var first=ledger.Capture().Reserved.Single(e=>e.Purpose=="expansion-build");Assert.AreEqual(c.Profile.MineCreditCost,first.Amount);
                long tick=a.Tick;Assert.True(a.TryAdvance(true));Assert.AreEqual(tick,a.Tick);Assert.AreEqual(first.Amount,ledger.Capture().Reserved.Single(e=>e.Purpose=="expansion-build").Amount);
                var b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
                for(int i=0;i<80;i++){Step(a,host);Step(b,host);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());}
                Assert.True(Checkpoint(a).Records.Any(r=>r.Policy=="expansion"&&r.Kind==PlayableCommandKind.Move&&r.Status==PlayableAiDeliveryStatus.Applied));
                var data=c.Profile.CopyData();data.revision++;data.mineCreditCost=150;var next=PlayableProfile.Create(data);double bank=View(a,c).ExactCredits;long paid=ledger.PaidTotal;
                Assert.IsNull(a.ApplyProfile(next));c=ProfileConfig(c,next);Assert.AreEqual(bank,View(a,c).ExactCredits);Assert.AreEqual(paid,ledger.PaidTotal);
                Assert.AreEqual(150,ledger.Capture().Reserved.Single(e=>e.Purpose=="expansion-build").Amount);
                Assert.True(ledger.Capture().Paid.Where(e=>e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.BuildingKind==PlayableBuildingKind.Factory).All(e=>e.Amount==PlayableProfile.Default.FactoryCreditCost));
                b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
                for(int i=0;i<900&&!ledger.Capture().Unpaid.Any(e=>e.Purpose=="expansion"&&e.Action.Kind==PlayableCommandKind.BuildAt);i++)Step(a,host);
                Assert.True(ledger.Capture().Unpaid.Any(e=>e.Purpose=="expansion"&&e.Action.Kind==PlayableCommandKind.BuildAt));
                Assert.False(ledger.Capture().Reserved.Any(e=>e.Purpose=="expansion-build"));
                Assert.True(a.TryAdvance(true));Assert.AreEqual(150,ledger.Capture().Reserved.Single(e=>e.Purpose=="expansion-build").Amount,"actual unpaid cancellation reprotects the valid commitment");
                b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
                for(int i=0;i<900&&!ledger.Capture().Paid.Any(e=>e.Purpose=="expansion"&&e.Action.Kind==PlayableCommandKind.BuildAt);i++)Step(a,host);
                var settled=ledger.Capture().Paid.Single(e=>e.Purpose=="expansion"&&e.Action.Kind==PlayableCommandKind.BuildAt);Assert.AreEqual(150,settled.Amount);
                data=next.CopyData();data.revision++;data.mineCreditCost=250;var later=PlayableProfile.Create(data);Assert.IsNull(a.ApplyProfile(later));c=ProfileConfig(c,later);
                Assert.AreEqual(150,ledger.Capture().Paid.Single(e=>e.Purpose=="expansion"&&e.Action.Kind==PlayableCommandKind.BuildAt).Amount);Assert.False(ledger.Capture().Reserved.Any(e=>e.Purpose=="expansion-build"));
                b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
                var broken=PlayableWorldState.Decode(a.CaptureBytes());broken.AiAuthority[4]=4;Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(broken.Encode(),c),"older live schema explicitly refused");
            }
        }
        [Test] public void LiveFundingReleasesOnStopWithoutBankRefund()
        {
            var c=CompetingSpendConfig();var a=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<100&&Planner(a).Capture().SiteId==0;i++)Step(a,host);
                var ledger=(AiBudgetLedger)Get(Owner(a),"budget");Assert.True(ledger.Capture().Reserved.Any(e=>e.Purpose=="expansion-build"));double bank=View(a,c).ExactCredits;
                a.Stop();Assert.AreEqual(bank,View(a,c).ExactCredits);Assert.False(ledger.Capture().Reserved.Any(e=>e.Purpose=="expansion-build"));Assert.Zero(Planner(a).Capture().SiteId);Assert.Zero((long)Get(Planner(a),"pendingId"));
            }
        }
        [Test] public void ClaimPauseRestoreProfileAndRestartUseRealAuthority()
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<100&&Planner(a).Capture().SiteId==0;i++)Step(a,host);
                Assert.AreEqual(3,Planner(a).Capture().SiteId);var bytes=a.CaptureBytes();var b=PlayableAuthorityTick.RestoreBytes(bytes,c);CollectionAssert.AreEqual(bytes,b.CaptureBytes());
                var state=Planner(a).Capture();long tick=a.Tick;for(int i=0;i<10;i++)Assert.True(a.TryAdvance(true));Assert.AreEqual(tick,a.Tick);Assert.AreEqual(state.ExpiresTick,Planner(a).Capture().ExpiresTick);
                b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);for(int i=0;i<80;i++){Step(a,host);Step(b,host);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());}
                var data=AiProfile.Initial.CopyData();data.revision++;data.fields.Single(x=>x.path=="economy.expansionRiskRatio").value=0.75;var ai=new AiProfile(data);Assert.IsNull(a.ApplyProfile(c.Profile,ai));
                Assert.AreEqual(3,Planner(a).Capture().SiteId);b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c,ai);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
                var fresh=new PlayableAuthorityTick(c,72,ai);Assert.Zero(Planner(fresh).Capture().SiteId);Assert.AreEqual("idle",Planner(fresh).Capture().Phase);
            }
        }
        [TestCase(false)][TestCase(true)] public void ActorLossAndExpiryReleaseClaimWithoutFakeIncome(bool expiry)
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<100&&Planner(a).Capture().SiteId==0;i++)Step(a,host);var state=Planner(a).Capture();Assert.AreEqual(3,state.SiteId);
                if(expiry)
                {
                    var o=PlayableAiObservation.From(View(a,c));var field=typeof(AiExpansionPlanner).GetField("expires",F);field.SetValue(Planner(a),a.Tick);
                    Step(a,host);StringAssert.Contains("expired",Planner(a).Capture().Reason);
                }
                else{Call(Get(a,"domain"),"Damage",state.ActorId,10000);Step(a,host);StringAssert.Contains("actor lost",Planner(a).Capture().Reason);}
                Assert.False(((AiBudgetLedger)Get(Owner(a),"budget")).Capture().Reserved.Any(e=>e.Purpose=="expansion-build"));Assert.Zero(Planner(a).Capture().ActorId);Assert.Zero(Planner(a).Capture().SiteId);Assert.False(View(a,c).Buildings.Any(b=>b.SiteId==3));
                var b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
            }
        }
        private static PlayableAiObservation RoutedObservation(PlayableAuthorityTick a,OfflineMatchConfiguration c)
        {
            var first=PlayableAiObservation.From(View(a,c));
            return PlayableAiObservation.From((PlayableSnapshot)Call(Get(a,"domain"),"PlayerSnapshotWithRoutes",a.Tick,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed,PlayableOwner.Player,AiExpansionPlanner.RouteRequests(first,c.Profile)));
        }
        private static PlayableAiOwnerCheckpoint Checkpoint(PlayableAuthorityTick a)=>(PlayableAiOwnerCheckpoint)Owner(a).GetType().GetProperty("Checkpoint",F).GetValue(Owner(a));
        [TestCase(PlayableBuildingKind.Mine)][TestCase(PlayableBuildingKind.Outpost)]
        public void SingleProjectionPreservesFullObservationProofsAndActualPlans(PlayableBuildingKind kind)
        {
            var c=Config(kind);var a=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                foreach(int targetTick in new[]{0,45,120,300,800,1000})
                {
                    while(a.Tick<targetTick)Step(a,host);
                    foreach(bool paused in new[]{false,true})
                    {
                        var first=(PlayableSnapshot)Call(Get(a,"domain"),"PlayerSnapshot",a.Tick,paused?RuntimeStatus.Paused:RuntimeStatus.Running,paused,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed,PlayableOwner.Player);
                        var requests=AiExpansionPlanner.RouteRequests(PlayableAiObservation.From(first),c.Profile).Concat(AiScoutPlanner.RouteRequests(first.Entities,first.PublicScoutObjectives,first.Owner,first.Generation,first.Tick,c.Profile)).GroupBy(r=>new {r.UnitId,r.Kind,r.TargetId}).Select(g=>g.First()).ToArray();
                        var legacy=(PlayableSnapshot)Call(Get(a,"domain"),"PlayerSnapshotWithRoutes",a.Tick,first.Status,paused,first.Metrics,null,c.Seed,PlayableOwner.Player,requests);
                        var one=(PlayableSnapshot)Call(Get(a,"domain"),"PlayerSnapshotForAi",a.Tick,first.Status,paused,first.Metrics,null,c.Seed,PlayableOwner.Player,true);
                        // AI snapshots intentionally omit presentation sound events. Preserve
                        // exact comparison of every gameplay/public field and check audio separately.
                        Assert.AreEqual(PlayableAiCanonical.Encode(first.Sounds),PlayableAiCanonical.Encode(legacy.Sounds));Assert.IsEmpty(one.Sounds);
                        legacy=new PlayableSnapshot(legacy.ProfileId,legacy.ProfileRevision,legacy.Generation,legacy.Seed,legacy.Sequence,legacy.Tick,legacy.Status,legacy.Paused,legacy.Outcome,legacy.Credits,legacy.Geometry,legacy.Entities.ToArray(),legacy.Buildings.ToArray(),legacy.Projectiles.ToArray(),legacy.Metrics,legacy.Failure,
                            sites:legacy.Sites.ToArray(),incomePerSecond:legacy.IncomePerSecond,vision:legacy.Vision,discoveredSites:legacy.DiscoveredSites.ToArray(),population:legacy.Population,impacts:legacy.Impacts.ToArray(),researchAvailability:legacy.ResearchAvailability.ToArray(),ownerResearch:legacy.OwnerResearch.ToArray(),publicScoutObjectives:legacy.PublicScoutObjectives.ToArray(),ownCenterDamage:legacy.OwnCenterDamage.ToArray(),routeProofs:legacy.RouteProofs.ToArray(),artillerySupport:legacy.ArtillerySupport.ToArray(),owner:legacy.Owner,exactCredits:legacy.ExactCredits,ownerId:legacy.OwnerId,team:legacy.Team,participants:legacy.Participants.ToArray(),activeProfile:legacy.ActiveProfile,homeSiteId:legacy.HomeSiteId,ownerEliminated:legacy.OwnerEliminated,settledIncome:legacy.SettledIncome,intelEnvelopes:legacy.IntelEnvelopes.ToArray());
                        Assert.AreEqual(PlayableAiCanonical.Encode(legacy),PlayableAiCanonical.Encode(one),"all AI public fields and real route proofs");
                        var lo=PlayableAiObservation.From(legacy);var ro=PlayableAiObservation.From(one);Assert.AreEqual(PlayableAiCanonical.Encode(lo),PlayableAiCanonical.Encode(ro));
                        var lp=(AiExpansionPlanner)typeof(AiExpansionPlanner).GetMethod("Fork",F).Invoke(Planner(a),null);
                        var rp=(AiExpansionPlanner)typeof(AiExpansionPlanner).GetMethod("Fork",F).Invoke(Planner(a),null);
                        Assert.AreEqual(PlayableAiCanonical.Encode(lp.TryPlan(lo,c.Profile,AiProfile.Initial)),PlayableAiCanonical.Encode(rp.TryPlan(ro,c.Profile,AiProfile.Initial)));
                        Assert.AreEqual(PlayableAiCanonical.Encode(lp.Capture()),PlayableAiCanonical.Encode(rp.Capture()));
                        Assert.AreEqual(PlayableAiCanonical.Encode(new AiEconomyPlanner().Plan(lo,c.Profile)),PlayableAiCanonical.Encode(new AiEconomyPlanner().Plan(ro,c.Profile)));
                    }
                }
                var restored=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);CollectionAssert.AreEqual(a.CaptureBytes(),restored.CaptureBytes());
            }
        }
        [Test] public void HiddenOccupancyDoesNotChangePublicGeometryObservationOrPlan()
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);var b=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                // Same genesis and actual human capture prefix: own IDs, inputs, memory,
                // native profile and RNG are byte-identical before the hidden mutation.
                int enemy=View(a,c).Entities.FirstOrDefault(e=>e.Owner==PlayableOwner.Enemy)?.Id??((PlayableSnapshot)Call(Get(a,"domain"),"Snapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed)).Entities.Single(e=>e.Owner==PlayableOwner.Enemy).Id;
                foreach(var cycle in new[]{a,b})Assert.AreEqual(PlayableCommandStatus.Applied,cycle.Apply(new PlayableCommand(71,1,"east-owner",PlayableCommandKind.Move,new[]{enemy},new NavPoint(20,-20))).Status);
                for(int i=0;i<1800;i++)
                {
                    Step(a,host);Step(b,host);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
                    var truth=(PlayableSnapshot)Call(Get(a,"domain"),"Snapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed);
                    if(truth.Sites.Single(x=>x.Site.Id==4).Progress>=1&&truth.Buildings.Any(x=>x.SiteId==3&&x.Owner==PlayableOwner.Player))break;
                }
                Assert.AreEqual(PlayableCommandStatus.Applied,b.Apply(new PlayableCommand(71,2,"east-owner",PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:4,buildingKind:PlayableBuildingKind.Outpost)).Status);
                for(int i=0;i<80;i++)
                {
                    var ao=RoutedObservation(a,c);var bo=RoutedObservation(b,c);
                    Assert.AreEqual(PlayableAiCanonical.Encode(ao),PlayableAiCanonical.Encode(bo),"every detached field, including route proofs, remains identical");
                    Assert.AreEqual(PlayableAiCanonical.Encode(new AiEconomyPlanner().Plan(ao,c.Profile)),PlayableAiCanonical.Encode(new AiEconomyPlanner().Plan(bo,c.Profile)));
                    Assert.AreEqual(PlayableAiCanonical.Encode(Planner(a).Capture()),PlayableAiCanonical.Encode(Planner(b).Capture()));
                    Assert.AreEqual(PlayableAiCanonical.Encode(Checkpoint(a)),PlayableAiCanonical.Encode(Checkpoint(b)),"actual plans, receipts, state, opening RNG");
                    Step(a,host);Step(b,host);
                }
                var hidden=(PlayableSnapshot)Call(Get(b,"domain"),"Snapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed);
                Assert.True(hidden.Buildings.Any(x=>x.SiteId==4&&x.Owner==PlayableOwner.Enemy));
                Assert.True(View(b,c).PublicScoutObjectives.Any(g=>g.SiteId==4));Assert.False(View(b,c).Sites.Any(s=>s.Site.Id==4));Assert.False(View(b,c).Buildings.Any(x=>x.SiteId==4));
            }
        }
        [TestCase(false)][TestCase(true)] public void ChangedKnownThreatReleasesPendingClaimAndAllowsReplanOrEmergency(bool emergency)
        {
            var c=Config();
            // This existing injected-danger ownership diagnostic is separate from A3's
            // unassisted native defense acceptance. Scouts never substitute for a major army.
            if(emergency)c=new OfflineMatchConfiguration(c.Profile,c.SourceIdentity,c.MapIdentity,c.RouteProvenance,c.Seed,c.Roster.ToArray(),c.Starts.ToArray(),c.Sites.ToArray(),c.Obstacles.ToArray(),new double[,]{{0,100},{100,0}},scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-12,2)),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-10,2)),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-8,2))});
            var a=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<100&&Planner(a).Capture().SiteId==0;i++)Step(a,host);
                int actor=Planner(a).Capture().ActorId;Assert.AreEqual(3,Planner(a).Capture().SiteId);
                Assert.Greater((long)Get(Planner(a),"pendingId"),0,"actual pending command before terminal cancellation");
                Call(Get(a,"domain"),"SpawnProduced",new NavPoint(-12,8),null,PlayableOwner.Enemy,PlayableEntityKind.Tank);
                int threat=View(a,c).Entities.Single(e=>e.Owner==PlayableOwner.Enemy&&e.Kind==PlayableEntityKind.Tank).Id;
                Step(a,host);Assert.False(((AiBudgetLedger)Get(Owner(a),"budget")).Capture().Reserved.Any(e=>e.Purpose=="expansion-build"));Assert.Zero(Planner(a).Capture().SiteId);Assert.Zero((long)Get(Planner(a),"pendingId"));Assert.False(Planner(a).ClaimsActor(actor));Assert.False(Planner(a).ClaimsSite(3));
                Assert.True(Checkpoint(a).Records.Any(r=>r.Policy=="expansion"&&r.Status==PlayableAiDeliveryStatus.Cancelled));
                if(!emergency)
                {
                    Call(Get(a,"domain"),"Damage",threat,10000);
                    for(int i=0;i<100&&Planner(a).Capture().SiteId==0;i++)Step(a,host);
                    Assert.AreEqual(3,Planner(a).Capture().SiteId,"changed allowed danger input reopens the site");Assert.AreEqual(actor,Planner(a).Capture().ActorId);
                }
                else
                {
                    for(int i=0;i<100&&!Checkpoint(a).Records.Any(r=>r.Policy==AiDefensePlanner.Policy&&r.Status==PlayableAiDeliveryStatus.Applied);i++)Step(a,host);
                    Assert.True(Checkpoint(a).Records.Any(r=>r.Policy==AiDefensePlanner.Policy&&r.Status==PlayableAiDeliveryStatus.Applied));Assert.False(Planner(a).ClaimsActor(actor));
                    var unpaid=((AiBudgetLedger)Get(Owner(a),"budget")).Capture().Unpaid;
                    Assert.LessOrEqual(unpaid.Count(e=>e.Claims.Contains("recipient:"+actor)),1,"one tactical recipient through C3 arbitration");
                }
            }
        }
        [Test] public void NoRouteProofCannotBecomeStraightDistanceCapture()
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);var o=PlayableAiObservation.From(View(a,c));
            Assert.True(o.Sites.Any(s=>s.Site.Id==3));Assert.True(o.PublicScoutObjectives.Any(g=>g.SiteId==3));Assert.IsEmpty(o.RouteProofs);
            var planner=new AiExpansionPlanner(o.OwnerId);Assert.Null(planner.TryPlan(o,c.Profile,AiProfile.Initial));Assert.Zero(planner.Capture().SiteId);
        }
        [Test] public void LostHomeWithLowerIdMineStillUsesAppropriateOutpost()
        {
            var baseConfig=Config(PlayableBuildingKind.Outpost);var p=baseConfig.Profile;
            var sites=baseConfig.Sites.Where(s=>s.Id<=2).Concat(new[]{new TerritorySite(3,PlayableBuildingKind.Mine,new NavPoint(20,-20),p),new TerritorySite(4,PlayableBuildingKind.Outpost,new NavPoint(-11,0),p)}).ToArray();
            var buildings=new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.Mine,3,0,sites.Single(s=>s.Id==3).Position),new OfflineScenarioBuilding(1,PlayableBuildingKind.Outpost,4,0,sites.Single(s=>s.Id==4).Position)};
            var c=new OfflineMatchConfiguration(p,baseConfig.SourceIdentity,baseConfig.MapIdentity,baseConfig.RouteProvenance,baseConfig.Seed,baseConfig.Roster.ToArray(),baseConfig.Starts.ToArray(),sites,Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},scenarioBuildings:buildings);
            var a=new PlayableAuthorityTick(c,71);int home=View(a,c).Buildings.Single(b=>b.Kind==PlayableBuildingKind.Headquarters&&b.Owner==PlayableOwner.Player).Id;
            Call(Get(a,"domain"),"Damage",home,10000);
            using(var host=new UnityHostRouteService())
            {
                long deadline=AiProfile.SecondsToTicks(AiProfile.Initial.DifficultyValue(AiDifficulty.Fighter,"decisionSeconds")+AiProfile.Initial.DifficultyValue(AiDifficulty.Fighter,"reactionSeconds"),30)+30;
                for(long i=0;i<deadline&&!View(a,c).Buildings.Any(b=>b.Kind==PlayableBuildingKind.Factory&&b.SiteId==4);i++)Step(a,host);
                var factory=View(a,c).Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory&&b.SiteId==4);
                Assert.AreEqual(View(a,c).Sites.Single(x=>x.Site.Id==4).CenterId,factory.ParentId);Assert.Greater(factory.SlotId,0);
                Assert.True(View(a,c).Buildings.Any(b=>b.Kind==PlayableBuildingKind.Mine&&b.SiteId==3));
                Assert.False(new AiEconomyPlanner().Plan(PlayableAiObservation.From(View(a,c)),p).Candidates.Any(x=>x.Policy=="economy"&&(x.Action.SiteId==1||x.Action.SiteId==3)));
            }
        }
        [Test] public void LostHomeUsesLiveOutpostSlotsAndOrdinaryFactoryConstruction()
        {
            var c=Config(PlayableBuildingKind.Outpost);var a=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<1800&&!View(a,c).Sites.Single(s=>s.Site.Id==3).Ready;i++)Step(a,host);
                var outpost=View(a,c).Sites.Single(s=>s.Site.Id==3);Assert.True(outpost.Ready);
                int home=View(a,c).Buildings.Single(b=>b.Kind==PlayableBuildingKind.Headquarters&&b.Owner==PlayableOwner.Player).Id;
                Call(Get(a,"domain"),"Damage",home,10000);
                // With original prices and only the surviving outpost, wait for the real
                // affordable threshold, then enforce the profile admission deadline.
                var budget=(AiBudgetLedger)Get(Owner(a),"budget");
                long decisionTicks=AiProfile.SecondsToTicks(AiProfile.Initial.DifficultyValue(AiDifficulty.Fighter,"decisionSeconds")+AiProfile.Initial.DifficultyValue(AiDifficulty.Fighter,"reactionSeconds"),30);
                // Conservative bound pays both ordinary live-center investments from the
                // slowest guaranteed outpost rate, including the refinery build delay.
                double needed=Math.Max(0,c.Profile.FactoryCreditCost+c.Profile.RefineryCreditCost-View(a,c).ExactCredits);
                long recoveryTicks=(long)Math.Ceiling(needed/(c.Profile.OutpostIncomePerPeriod/c.Profile.IncomePeriodSeconds)+c.Profile.RefineryBuildSeconds)*30+2*decisionTicks+30;
                long affordable=-1;bool refineryPaid=false,refineryReady=false;double previousBank=View(a,c).ExactCredits;long previousPaid=budget.PaidTotal;
                for(long i=0;i<recoveryTicks&&!View(a,c).Buildings.Any(b=>b.Kind==PlayableBuildingKind.Factory&&b.SiteId==3);i++)
                {
                    Step(a,host);var v=View(a,c);var r=v.Buildings.FirstOrDefault(b=>b.Kind==PlayableBuildingKind.Refinery&&b.SiteId==3);
                    refineryPaid|=r!=null;refineryReady|=r?.Phase==ConstructionPhase.Ready;
                    if(budget.PaidTotal!=previousPaid||v.ExactCredits-previousBank>0)
                    {TestContext.WriteLine("E2-08 tick="+a.Tick+" bank="+v.ExactCredits+" settled="+v.SettledIncome+" income="+v.IncomePerSecond+" available="+budget.Available+" paid="+budget.PaidTotal+" refinery="+r?.Phase);previousPaid=budget.PaidTotal;}
                    previousBank=v.ExactCredits;
                    if(affordable<0&&budget.Available>=c.Profile.FactoryCreditCost&&new AiEconomyPlanner().Plan(PlayableAiObservation.From(v),c.Profile).Candidates.Any(x=>x.Legal&&x.Action.Kind==PlayableCommandKind.BuildAt&&x.Action.BuildingKind==PlayableBuildingKind.Factory))affordable=a.Tick;
                    if(affordable>=0)Assert.LessOrEqual(a.Tick-affordable,decisionTicks+30,"legal affordable factory cannot starve past actual C3/C4 admission window");
                }
                Assert.True(refineryPaid);Assert.True(refineryReady);Assert.GreaterOrEqual(affordable,0);
                var factory=View(a,c).Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory&&b.SiteId==3);
                Assert.AreEqual(outpost.CenterId,factory.ParentId);Assert.Greater(factory.SlotId,0);Assert.AreEqual(PlayableMatchOutcome.Playing,View(a,c).Outcome);
                Assert.False(new AiEconomyPlanner().Plan(PlayableAiObservation.From(View(a,c)),c.Profile).Candidates.Any(x=>x.Action.SiteId==1));
            }
        }
    }
}
