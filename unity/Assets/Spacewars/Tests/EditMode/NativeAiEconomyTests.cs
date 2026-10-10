using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiEconomyTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        private static AiReceiptIdentity Receipt(long decision=1,long generation=71,string owner="a")=>new AiReceiptIdentity(generation,owner,decision,1);
        private static PlayableAiAction Action(PlayableCommandKind kind=PlayableCommandKind.BuildAt,string owner="a",long generation=71)=>
            new PlayableAiAction(1,owner,"p",1,generation,1,kind,buildingKind:PlayableBuildingKind.Factory);
        private static AiBudgetEntry Entry(string id,int amount=60,string claim="slot:1",AiReceiptIdentity receipt=null,long expires=20)=>
            new AiBudgetEntry(id,"economy",Action(),amount,0,1,expires,"producer/site lost or expiry",new[]{claim},"p@1",receipt);
        private static AiBudgetLedger Ledger(int bank=100){var x=new AiBudgetLedger("a",71);x.ObserveBank(bank);return x;}
        private static AiIntent Intent(string id,int cost,string claim)=>new AiIntent(id,"economy",Action(),100,cost,0,new[]{claim});

        private static PlayableAiObservation EconomyObservation(int credits,bool full=false,bool populationFull=false,bool selling=false,bool busy=false,bool reachable=true)
        {
            var p=PlayableProfile.Default;var site=TerritoryRules.Sites(p).First(x=>x.Kind==PlayableBuildingKind.Headquarters);
            var buildings=new System.Collections.Generic.List<PlayableBuildingSnapshot>{new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Headquarters,site.Position,100,1,0,0,default,siteId:site.Id)};
            var lifecycle=selling?new PlayableBuildingLifecycleSnapshot(true,0,false,false,0,0,null,null,1,0,false):null;
            buildings.Add(new PlayableBuildingSnapshot(2,PlayableOwner.Player,PlayableBuildingKind.Factory,site.Slots[0].Position,100,1,busy?1:0,0,default,siteId:site.Id,slotId:site.Slots[0].Id,parentId:1,lifecycle:lifecycle));
            if(full)foreach(var slot in site.Slots.Skip(1))buildings.Add(new PlayableBuildingSnapshot(10+slot.Id,PlayableOwner.Player,PlayableBuildingKind.ScientificCenter,slot.Position,100,1,0,0,default,siteId:site.Id,slotId:slot.Id,parentId:1));
            var snapshot=new PlayableSnapshot(p.ProfileId,p.Revision,71,19092026,1,45,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,credits,null,Array.Empty<PlayableEntitySnapshot>(),buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,
                sites:new[]{new TerritorySiteSnapshot(site,PlayableOwner.Player,PlayableOwner.Player,1,false,1,true)},population:new PlayablePopulationSnapshot(populationFull?p.ArmyCapacity:0,0,p.ArmyCapacity),publicScoutObjectives:new[]{new PlayablePublicScoutObjective(99,new NavPoint(5,5),PlayablePublicScoutObjectiveRole.PossibleEnemyStart,reachable)});
            return PlayableAiObservation.From(snapshot);
        }
        [Test] public void AffordableFallbackReportsExpensiveBuildAndKeepsUnit()
        {
            var p=PlayableProfile.Default;Assert.Greater(p.RefineryCreditCost,p.TankCreditCost);
            var o=EconomyObservation(p.TankCreditCost);var planner=new AiEconomyPlanner();var plan=planner.Plan(o,p);
            Assert.True(plan.Candidates.Any(c=>c.Action.Kind==PlayableCommandKind.BuildAt&&c.Reason=="insufficient credits"));
            Assert.AreEqual(PlayableCommandKind.QueueTank,plan.Candidates.Single(c=>c.Legal).Action.Kind);
            CollectionAssert.AreEqual(plan.Candidates.Select(c=>c.Reason),planner.Plan(o,p).Candidates.Select(c=>c.Reason));
        }
        [Test] public void FullSlotsKeepsProductionAndDetachedExpansionRequest()
        {
            var plan=new AiEconomyPlanner().Plan(EconomyObservation(10000,full:true),PlayableProfile.Default);
            Assert.True(plan.Candidates.Where(c=>c.Policy=="economy").All(c=>c.Reason=="slot occupied"));
            Assert.AreEqual("production",plan.Candidates.Single(c=>c.Legal).Policy);
            CollectionAssert.AreEqual(new[]{99},plan.ExpansionSiteIds);StringAssert.Contains("unverified",plan.ExpansionReason);
        }
        [Test] public void GenuineBlockReportsPopulationSlotsAndUnreachableExpansionWithoutSpend()
        {
            var o=EconomyObservation(10000,full:true,populationFull:true,reachable:false);var plan=new AiEconomyPlanner().Plan(o,PlayableProfile.Default);
            Assert.False(plan.Candidates.Any(c=>c.Legal));Assert.AreEqual("population capacity",plan.Candidates.Single(c=>c.Policy=="production").Reason);
            Assert.IsEmpty(plan.ExpansionSiteIds);StringAssert.Contains("no reachable",plan.ExpansionReason);
            var ledger=Ledger(10000);Assert.AreEqual(10000,ledger.Available);Assert.Zero(ledger.Unpaid);Assert.Zero(ledger.PaidTotal);
        }
        [TestCase(true,false,"ready live producer prerequisite")][TestCase(false,true,"producer queue busy")]
        public void AdmissionRejectsKnownUnavailableProducer(bool selling,bool busy,string reason)
        {
            var plan=new AiEconomyPlanner().Plan(EconomyObservation(10000,selling:selling,busy:busy),PlayableProfile.Default);
            Assert.AreEqual(reason,plan.Candidates.Single(c=>c.Policy=="production").Reason);
        }
        [Test] public void AffordableFallbackUsesOrdinaryAuthorityAndRestoresSelectedPolicy()
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);for(int i=0;i<80;i++)Step(a);
            var domain=Get(a,"domain");var d=domain.GetType();
            d.GetMethod("AdvanceFoundations",F).Invoke(domain,null);
            d.GetMethod("AdvanceBuildings",F).Invoke(domain,new object[]{(double)c.Profile.FactoryBuildSeconds+1});
            foreach(var owner in Owners(a))
            {
                var logical=d.GetMethod("OwnerFor",F).Invoke(domain,new object[]{Budget(owner).OwnerId});
                var bank=(double)d.GetMethod("Balance",F).Invoke(domain,new[]{logical});
                d.GetMethod("AddCredits",F).Invoke(domain,new[]{logical,(object)(c.Profile.TankCreditCost-bank)});
            }
            for(int i=0;i<10;i++)Step(a);
            Assert.True(Owners(a).All(o=>((AiEconomyPlan)o.GetType().GetProperty("EconomyPlan",F).GetValue(o)).Candidates.Any(x=>x.Policy=="production"&&x.Legal)));
            foreach(var row in Journal(a))
            {
                var candidates=row["Record"]["Candidates"].ToArray();
                Assert.True(candidates.Any(x=>(string)x["Reason"]=="insufficient credits"));
                Assert.True(candidates.Any(x=>(bool)x["Selected"]&&(string)x["Policy"]=="production"));
                Assert.True(candidates.Where(x=>!(bool)x["Selected"]).All(x=>!string.IsNullOrEmpty((string)x["Reason"])));
            }
            var restored=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);
            for(int i=0;i<35;i++){Step(a);Step(restored);CollectionAssert.AreEqual(a.CaptureBytes(),restored.CaptureBytes());}
            Assert.True(Owners(a).All(o=>Budget(o).Capture().Paid.Any(e=>e.Purpose=="production"&&e.Action.Kind==PlayableCommandKind.QueueTank)));
        }

        [TestCase(false)][TestCase(true)] public void FullSlotsOrdinaryAuthorityUsesProductionOrReportsRealPopulation(bool populationFull)
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);var domain=Get(a,"domain");var d=domain.GetType();
            object Call(string name,params object[] args)=>d.GetMethod(name,F).Invoke(domain,args);
            foreach(var owner in Owners(a))
            {
                var logical=(PlayableOwner)Call("OwnerFor",Budget(owner).OwnerId);Call("AddCredits",logical,10000d);
                var snapshot=(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed,logical);
                var site=snapshot.Sites.Single(x=>x.Owner==logical&&x.Site.Kind==PlayableBuildingKind.Headquarters);
                foreach(var slot in site.Site.Slots)
                    Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",site.Site.Id,slot.Id,slot.Id==site.Site.Slots[0].Id?PlayableBuildingKind.Factory:PlayableBuildingKind.ScientificCenter,site.CenterId,logical,null));
            }
            Call("AdvanceFoundations");Call("AdvanceBuildings",(double)Math.Max(c.Profile.FactoryBuildSeconds,TerritoryRules.Duration(c.Profile,PlayableBuildingKind.ScientificCenter))+1);
            if(populationFull)foreach(var owner in Owners(a))
            {
                var logical=(PlayableOwner)Call("OwnerFor",Budget(owner).OwnerId);
                var snapshot=(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed,logical);
                var home=snapshot.Sites.Single(x=>x.Owner==logical&&x.Site.Kind==PlayableBuildingKind.Headquarters);
                var nav=(NavigationSession)Get(domain,"navigation");int placed=0;
                int needed=(snapshot.Population.Capacity-snapshot.Population.Living-snapshot.Population.Reserved)/c.Profile.TankPopulationCost;
                // Populate only genuinely placeable positions in the real fixture geometry.
                for(int x=-90;x<=90&&placed<needed;x+=3)
                    for(int z=-90;z<=90&&placed<needed;z+=3)
                        if(nav.Crowd.CanPlace(new NavPoint(x,z),c.Profile.TankCollisionRadius))
                        {Call("SpawnProduced",new NavPoint(x,z),null,logical,PlayableEntityKind.Tank);placed++;}
                Assert.AreEqual(needed,placed);
                snapshot=(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed,logical);
                Assert.Less(snapshot.Population.Capacity-snapshot.Population.Living-snapshot.Population.Reserved,c.Profile.TankPopulationCost);
                Assert.LessOrEqual(snapshot.Population.Living+snapshot.Population.Reserved,snapshot.Population.Capacity);
            }
            for(int i=0;i<45;i++)Step(a);
            foreach(var owner in Owners(a))
            {
                var plan=(AiEconomyPlan)owner.GetType().GetProperty("EconomyPlan",F).GetValue(owner);
                Assert.NotNull(plan);Assert.True(plan.Candidates.Where(x=>x.Policy=="economy").All(x=>x.Reason=="slot occupied"));
                Assert.AreEqual(populationFull?"population capacity":null,plan.Candidates.Single(x=>x.Policy=="production").Reason);
                Assert.AreEqual(populationFull?0:c.Profile.TankCreditCost,Budget(owner).Unpaid);
            }
            foreach(var row in Journal(a))
            {
                var candidates=row["Record"]["Candidates"].ToArray();
                Assert.True(candidates.Any(x=>(string)x["Reason"]=="slot occupied"));
                if(populationFull)
                {
                    Assert.True(candidates.All(x=>!(bool)x["Selected"]));Assert.True(candidates.Any(x=>(string)x["Reason"]=="population capacity"));
                    Assert.AreEqual(0,(long)row["Record"]["Unpaid"]);Assert.GreaterOrEqual((long)row["Record"]["Available"],0);
                }
            }
            var restored=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);
            for(int i=0;i<35;i++){Step(a);Step(restored);CollectionAssert.AreEqual(a.CaptureBytes(),restored.CaptureBytes());}
            foreach(var owner in Owners(a))Assert.AreEqual(populationFull?0:c.Profile.TankCreditCost,Budget(owner).PaidTotal);
        }

        [Test] public void AdmissionIncludesFullyCapturedUnbuiltMineAndRejectsIncompleteOrForeignCapture()
        {
            var p=PlayableProfile.Default;var mine=TerritoryRules.Sites(p).First(x=>x.Kind==PlayableBuildingKind.Mine);
            PlayableAiObservation View(double progress,PlayableOwner claimant)=>PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,7,1,45,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,10000,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:new[]{new TerritorySiteSnapshot(mine,claimant,null,progress,false,0,false)}));
            var plan=new AiEconomyPlanner().Plan(View(1,PlayableOwner.Player),p);
            Assert.True(plan.Candidates.Single().Legal);Assert.AreEqual(PlayableBuildingKind.Mine,plan.Candidates.Single().Action.BuildingKind);
            var partial=new AiEconomyPlanner().Plan(View(0.5,PlayableOwner.Player),p);Assert.AreEqual("site capture/center prerequisite",partial.Candidates.Single().Reason);
            Assert.IsEmpty(new AiEconomyPlanner().Plan(View(1,PlayableOwner.Enemy),p).Candidates);
        }

        private static Newtonsoft.Json.Linq.JObject[] Journal(PlayableAuthorityTick authority)
        {
            var bytes=authority.CaptureBytes();var checkpoints=authority.CaptureDiagnosticCheckpoints();
            var seen=new System.Collections.Generic.HashSet<string>();var writer=new System.IO.StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            Assert.AreEqual(checkpoints.Length,Spacewars.Headless.AiEconomyDecisionJournal.Write(writer,checkpoints,seen,checkpoints.Length));
            Assert.Zero(Spacewars.Headless.AiEconomyDecisionJournal.Write(writer,checkpoints,seen,checkpoints.Length),"Same decision is not logged again every tick.");
            CollectionAssert.AreEqual(bytes,authority.CaptureBytes(),"Reporting never changes world, ledger, ordinals or claims.");
            var reordered=new System.IO.StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            Spacewars.Headless.AiEconomyDecisionJournal.Write(reordered,checkpoints.Reverse(),new System.Collections.Generic.HashSet<string>(),checkpoints.Length);
            Assert.AreEqual(writer.ToString(),reordered.ToString());
            var bounded=new System.IO.StringWriter();Assert.AreEqual(1,Spacewars.Headless.AiEconomyDecisionJournal.Write(bounded,checkpoints,new System.Collections.Generic.HashSet<string>(),1));
            TestContext.WriteLine("E2_ACTUAL_WORKER_JOURNAL\n"+writer);
            var rows=writer.ToString().Split(new[]{'\n'},StringSplitOptions.RemoveEmptyEntries).Select(Newtonsoft.Json.Linq.JObject.Parse).ToArray();
            Assert.True(rows.All(r=>(string)r["Type"]=="ai-economy-decision"&&r["Receipt"]==null&&r["Record"]["Status"]==null));
            return rows;
        }
        [Test] public void ArbitrationReportsBudgetClaimsCooldownAndAttentionWithoutExtraCommands()
        {
            var arbiter=new AiDecisionArbiter(AiProfile.Initial);var a=Intent("a",60,"first");var b=Intent("b",60,"second");
            Assert.AreEqual("a",arbiter.Select(new[]{a,b},1,100,0,2).Single().Id);Assert.AreEqual("ledger budget unavailable",arbiter.Rejections["b"]);
            Assert.AreEqual("a",arbiter.Select(new[]{Intent("a",0,"same"),Intent("b",0,"same")},2,100,0,2).Single().Id);Assert.AreEqual("selected action claim conflict",arbiter.Rejections["b"]);
            arbiter.Terminal(a,2,PlayableAiDeliveryStatus.Rejected);
            Assert.AreEqual("b",arbiter.Select(new[]{a,b},3,100,0,2).Single().Id);Assert.AreEqual("rejection cooldown",arbiter.Rejections["a"]);
            arbiter=new AiDecisionArbiter(AiProfile.Initial);Assert.AreEqual("a",arbiter.Select(new[]{Intent("a",0,"a"),Intent("b",0,"b")},1,100,0,1).Single().Id);Assert.AreEqual("decision attention limit",arbiter.Rejections["b"]);
        }

        private static PlayableSnapshot EconomyView(PlayableAuthorityTick a,OfflineMatchConfiguration c,PlayableOwner owner=PlayableOwner.Player)
        {
            var d=Get(a,"domain");return (PlayableSnapshot)d.GetType().GetMethod("PlayerSnapshot",F).Invoke(d,new object[]{1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed,owner});
        }
        private static int PrepareLine(PlayableAuthorityTick a,OfflineMatchConfiguration c,bool rich=false)
        {
            var d=Get(a,"domain");object Call(string name,params object[] args)=>d.GetType().GetMethod(name,F).Invoke(d,args);
            Call("AddCredits",PlayableOwner.Player,10000d);
            var home=EconomyView(a,c).Sites.Single(x=>x.Site.Id==EconomyView(a,c).HomeSiteId);
            foreach(var slot in home.Site.Slots.Take(rich?home.Site.Slots.Count:1))
                Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",home.Site.Id,slot.Id,slot.Id==1?PlayableBuildingKind.Factory:PlayableBuildingKind.Refinery,home.CenterId,PlayableOwner.Player,null));
            Call("AdvanceFoundations");Call("AdvanceBuildings",(double)Math.Max(c.Profile.FactoryBuildSeconds,c.Profile.RefineryBuildSeconds)+1);
            return EconomyView(a,c).Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory).Id;
        }
        private static OfflineMatchConfiguration RichEconomyConfig()
        {
            var baseConfig=Config();var routes=new double[baseConfig.Starts.Count,baseConfig.Starts.Count];
            for(int i=0;i<baseConfig.Starts.Count;i++)for(int j=0;j<baseConfig.Starts.Count;j++)routes[i,j]=i==j?0:1;
            var site=baseConfig.Sites.Single(x=>x.Id==9);
            return new OfflineMatchConfiguration(baseConfig.Profile,baseConfig.SourceIdentity,baseConfig.MapIdentity,baseConfig.RouteProvenance,baseConfig.Seed,baseConfig.Roster.ToArray(),baseConfig.Starts.ToArray(),baseConfig.Sites.ToArray(),baseConfig.Obstacles.ToArray(),routes,
                scenarioBuildings:new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.Outpost,site.Id,0,site.Position)});
        }
        [Test] public void ScaleAndUseIncomeOverflowBuildsThenActuallyProducesOnNewLine()
        {
            var c=RichEconomyConfig();
            var a=new PlayableAuthorityTick(c,71);int original=PrepareLine(a,c,true);
            var initial=EconomyView(a,c);Assert.Greater(initial.IncomePerSecond,PlayableUnitRules.Cost(c.Profile,PlayableEntityKind.Tank)/PlayableUnitRules.Duration(c.Profile,PlayableEntityKind.Tank));
            var d=Get(a,"domain");double settled=initial.SettledIncome;
            // External fixture bank funding/refunds are deliberately excluded from settled income.
            Assert.Zero(settled);
            int newId=0;long queued=0;int livingAtQueue=0;bool complete=false;
            for(int i=0;i<3000;i++)
            {
                Step(a);var view=EconomyView(a,c);
                var extra=view.Buildings.FirstOrDefault(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.Id!=original);
                if(extra!=null)newId=extra.Id;
                if(extra?.PrivateState.Orders.Count>0&&queued==0){queued=extra.PrivateState.Orders[0].Id;livingAtQueue=view.Entities.Count(e=>e.Owner==view.Owner&&e.Kind==PlayableEntityKind.Tank);}
                if(queued>0&&!extra.PrivateState.Orders.Any(q=>q.Id==queued)&&view.Entities.Count(e=>e.Owner==view.Owner&&e.Kind==PlayableEntityKind.Tank)>livingAtQueue){complete=true;break;}
            }
            Assert.Greater(newId,0,"E2-04 must construct an additional known legal factory");Assert.Greater(queued,0,"ordinary authority must pay a production order on that new line");Assert.True(complete,"new paid order must settle into a living unit");
            Assert.True(Budget(Owners(a).Single(o=>Budget(o).OwnerId==c.Roster[0].Id)).Capture().Paid.Any(e=>e.Action.EntityIds.Contains(newId)&&e.Action.Kind==PlayableCommandKind.QueueTank));
            Assert.Greater(EconomyView(a,c).SettledIncome,0);Journal(a);
            var restored=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);
            for(int i=0;i<35;i++){Step(a);Step(restored);CollectionAssert.AreEqual(a.CaptureBytes(),restored.CaptureBytes(),"income/utilization history must resume identically");}
        }
        [Test] public void ScaleAndUseIdleLineReceivesPaidOrderWithinProfileAndFrozenDeadline()
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);int factory=PrepareLine(a,c);long start=EconomyView(a,c).Tick;
            int ticks=0;for(;ticks<450;ticks++){Step(a);if(EconomyView(a,c).Buildings.Single(b=>b.Id==factory).PrivateState.Orders.Count>0)break;}
            Assert.LessOrEqual(EconomyView(a,c).Tick-start,AiProfile.SecondsToTicks(AiProfile.Initial.Value("economy.idleLineDeadlineSeconds"),30));
            Assert.Less(ticks,450,"frozen evaluation maximum 15s is independent of candidate deadlines");
            Assert.AreEqual(1,EconomyView(a,c).Buildings.Count(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory),"idle capacity must not demand another factory");Journal(a);
        }
        [Test] public void ScaleAndUseBlockedExitKeepsPaidHeadAndBoundsFactoryConstruction()
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);int factory=PrepareLine(a,c);var d=Get(a,"domain");
            for(int i=0;i<240&&EconomyView(a,c).Buildings.Single(b=>b.Id==factory).PrivateState.Orders.Count==0;i++)Step(a);
            var paid=EconomyView(a,c).Buildings.Single(b=>b.Id==factory).PrivateState.Orders.Single();
            var buildings=(IDictionary)Get(d,"buildings");var nav=(NavigationSession)Get(d,"navigation");
            var exits=(NavPoint[])d.GetType().GetMethod("FactoryExitCandidates",F).Invoke(d,new[]{buildings[factory],(object)PlayableEntityKind.Tank});
            int blocker=10000;foreach(var point in exits)if(nav.Crowd.CanPlace(point,c.Profile.TankCollisionRadius))nav.Crowd.Add(blocker++,point,c.Profile.TankCollisionRadius,c.Profile.TankSpeed,c.Profile.TankTurnSpeed);
            for(int i=0;i<1800;i++)Step(a);
            var view=EconomyView(a,c);var head=view.Buildings.Single(b=>b.Id==factory).PrivateState.Orders.Single();
            Assert.AreEqual(paid.Id,head.Id);Assert.AreEqual(paid.PaidCost,head.PaidCost);Assert.True(head.Active);Assert.Zero(head.Remaining);
            Assert.AreEqual(1,view.Buildings.Count(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Factory));
            var rows=Journal(a);var row=rows.Single(x=>(string)x["Record"]["OwnerId"]==c.Roster[0].Id);
            Assert.AreEqual("producer exit blocked",(string)row["Record"]["ProductionCapacity"]["ScalingReason"]);Assert.AreEqual(1,(int)row["Record"]["ProductionCapacity"]["BlockedExits"]);
        }
        private static object EconomyOwner(PlayableAuthorityTick a,OfflineMatchConfiguration c)=>Owners(a).Single(o=>Budget(o).OwnerId==c.Roster[0].Id);
        private static PlayableBuildingSnapshot AwaitAdditionalFactory(PlayableAuthorityTick a,OfflineMatchConfiguration c,int original)
        {
            for(int i=0;i<3000;i++){Step(a);var b=EconomyView(a,c).Buildings.FirstOrDefault(x=>x.Owner==PlayableOwner.Player&&x.Kind==PlayableBuildingKind.Factory&&x.Id!=original);if(b!=null)return b;}
            Assert.Fail("Expected real additional factory");return null;
        }
        private static OfflineMatchConfiguration WithEconomyProfile(OfflineMatchConfiguration c,PlayableProfile p)
        {
            var costs=new double[c.Starts.Count,c.Starts.Count];for(int i=0;i<c.Starts.Count;i++)for(int j=0;j<c.Starts.Count;j++)costs[i,j]=c.RouteCost(i,j);
            return new OfflineMatchConfiguration(p,c.SourceIdentity,c.MapIdentity,c.RouteProvenance,c.Seed,c.Roster.ToArray(),c.Starts.ToArray(),c.Sites.ToArray(),c.Obstacles.ToArray(),costs,scenarioBuildings:c.ScenarioBuildings.ToArray());
        }
        [Test] public void ScaleAndUseStartupSurvivesCompetingOldProductionResearchRestoreAndProfile()
        {
            var c=RichEconomyConfig();var a=new PlayableAuthorityTick(c,71);int original=PrepareLine(a,c,true);var d=Get(a,"domain");
            object Call(string name,params object[] args)=>d.GetType().GetMethod(name,F).Invoke(d,args);
            for(int i=0;i<360;i++)Step(a);
            // Staged fixture funds this ordinary infrastructure purchase independently;
            // the gameplay catalog price is still charged and no income sample is fabricated.
            Call("AddCredits",PlayableOwner.Player,(double)c.Profile.ScienceCreditCost);
            var site=EconomyView(a,c).Sites.Single(x=>x.Site.Id==9);
            Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,(long)Call("AllocateAiSequence"),c.Roster[0].Id,PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:9,slotId:2,parentId:site.CenterId,buildingKind:PlayableBuildingKind.ScientificCenter)).Status);
            var extra=AwaitAdditionalFactory(a,c,original);Assert.AreNotEqual(ConstructionPhase.Ready,extra.Phase);
            var owner=EconomyOwner(a,c);var ledger=Budget(owner);var hold=ledger.Capture().Reserved.Single(x=>x.Purpose=="factory-startup");
            int launch=checked((int)AiProfile.Initial.Value("economy.factoryLaunchCycles")*c.Profile.TankCreditCost);Assert.AreEqual(launch,hold.Amount);
            double bank=(double)Call("Balance",PlayableOwner.Player);Call("AddCredits",PlayableOwner.Player,launch+c.Profile.TankCreditCost-bank);
            long oldPaid=ledger.PaidTotal;bool oldLinePaid=false,researchBlocked=false;
            int elapsed=0;
            for(;elapsed<150;elapsed++)
            {
                Step(a);Assert.AreEqual(launch,ledger.Capture().Reserved.Single(x=>x.Id==hold.Id).Amount);
                oldLinePaid|=ledger.PaidTotal>oldPaid&&ledger.Capture().Paid.Any(e=>e.CreatedTick>hold.CreatedTick&&e.Action.EntityIds.Contains(original)&&e.Action.Kind==PlayableCommandKind.QueueTank);
                var rejects=(System.Collections.Generic.IReadOnlyDictionary<string,string>)Get(Get(owner,"arbiter"),"rejections");researchBlocked|=rejects.Any(x=>x.Key.StartsWith("research:QueueResearch:")&&x.Value=="ledger budget unavailable");
            }
            Assert.True(oldLinePaid,"useful old-line orders may spend outside the held startup amount");Assert.True(researchBlocked,"ordinary eligible research must encounter the real held-fund budget blocker");
            Journal(a);var before=a.CaptureBytes();var restored=PlayableAuthorityTick.RestoreBytes(before,c);
            for(int i=0;i<20;i++){Step(a);Step(restored);CollectionAssert.AreEqual(a.CaptureBytes(),restored.CaptureBytes());}
            var profileData=c.Profile.CopyData();profileData.revision++;var next=PlayableProfile.Create(profileData);var aiData=AiProfile.Initial.CopyData();aiData.revision++;var ai=new AiProfile(aiData);
            Assert.IsNull(a.ApplyProfile(next,ai));c=WithEconomyProfile(c,next);ledger=Budget(EconomyOwner(a,c));
            var rebound=ledger.Capture().Reserved.Single(x=>x.Id==hold.Id);Assert.AreEqual(launch,rebound.Amount);Assert.AreEqual(next.ProfileId+"@"+next.Revision,rebound.Terms);
            Assert.True(ledger.Capture().Paid.Any(x=>x.Action.Kind==PlayableCommandKind.BuildAt&&x.Action.SiteId==9&&x.Action.SlotId==extra.SlotId&&x.Terms==hold.Terms),"paid factory terms remain from the accepted catalog revision");
            restored=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c,ai);long firstOrder=0;bool produced=false;int living=0;long previousHold=ledger.ReservationAmount(hold.Id);
            for(int i=0;i<1800;i++)
            {
                Step(a);Step(restored);CollectionAssert.AreEqual(a.CaptureBytes(),restored.CaptureBytes());var v=EconomyView(a,c);var b=v.Buildings.Single(x=>x.Id==extra.Id);
                long nowHeld=ledger.ReservationAmount(hold.Id);if(nowHeld!=previousHold){TestContext.WriteLine("E3_STARTUP_TRANSITION "+Newtonsoft.Json.JsonConvert.SerializeObject(new{Tick=a.Tick,PreviousHeld=previousHold,Held=nowHeld,Budget=ledger.Capture(),NewLine=b.PrivateState}));previousHold=nowHeld;}
                if(firstOrder==0&&b.PrivateState.Orders.Count>0){firstOrder=b.PrivateState.Orders[0].Id;living=v.Entities.Count(x=>x.Owner==v.Owner&&x.Kind==PlayableEntityKind.Tank);}
                var progress=ledger.Capture();int terminalCycles=progress.Paid.Count(x=>x.Action.EntityIds.Contains(extra.Id)&&x.Action.Kind==PlayableCommandKind.QueueTank);
                bool startupPending=progress.Unpaid.Any(x=>x.Action.EntityIds.Contains(extra.Id)&&x.Action.Kind==PlayableCommandKind.QueueTank);
                if(firstOrder>0&&!b.PrivateState.Orders.Any(x=>x.Id==firstOrder)&&v.Entities.Count(x=>x.Owner==v.Owner&&x.Kind==PlayableEntityKind.Tank)>living&&terminalCycles>=(int)AiProfile.Initial.Value("economy.factoryLaunchCycles")&&!startupPending&&progress.Reserved.All(x=>x.Id!=hold.Id)){produced=true;break;}
            }
            Journal(a);TestContext.WriteLine("E3_STARTUP_FINAL "+Newtonsoft.Json.JsonConvert.SerializeObject(new{Tick=a.Tick,Budget=ledger.Capture(),World=EconomyView(a,c).Population}));
            Assert.True(produced,"accepted startup survives competition and pays its planned cycles on the new line through ordinary receipts");
            Assert.GreaterOrEqual(ledger.Capture().Paid.Count(x=>x.Action.EntityIds.Contains(extra.Id)&&x.Action.Kind==PlayableCommandKind.QueueTank),(int)AiProfile.Initial.Value("economy.factoryLaunchCycles"));
        }
        [TestCase(false)][TestCase(true)] public void ScaleAndUseStartupReleasesOnDestroyedFactoryOrBoundedExpiry(bool expire)
        {
            var c=RichEconomyConfig();var a=new PlayableAuthorityTick(c,71);int original=PrepareLine(a,c,true);var extra=AwaitAdditionalFactory(a,c,original);
            var ledger=Budget(EconomyOwner(a,c));var hold=ledger.Capture().Reserved.Single(x=>x.Purpose=="factory-startup");
            if(expire)
            {
                var data=AiProfile.Initial.CopyData();data.revision++;data.fields.Single(x=>x.path=="economy.reservationExpirySeconds").value=AiProfileMetadata.Field("economy.reservationExpirySeconds").Minimum;var ai=new AiProfile(data);
                Assert.IsNull(a.ApplyProfile(c.Profile,ai));var restored=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c,ai);
                for(int i=0;i<180;i++){Step(a);Step(restored);CollectionAssert.AreEqual(a.CaptureBytes(),restored.CaptureBytes());}
                Assert.AreNotEqual(ConstructionPhase.Ready,EconomyView(a,c).Buildings.Single(x=>x.Id==extra.Id).Phase,"expiry occurs during real construction, without changing build duration");
            }
            else
            {
                Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,(long)Get(a,"domain").GetType().GetMethod("AllocateAiSequence",F).Invoke(Get(a,"domain"),null),c.Roster[0].Id,PlayableCommandKind.CancelBuilding,new[]{extra.Id})).Status);Step(a);
                Assert.False(EconomyView(a,c).Buildings.Any(x=>x.Id==extra.Id));
            }
            Assert.False(ledger.Capture().Reserved.Any(x=>x.Id==hold.Id&&x.CreatedTick==hold.CreatedTick),"original startup must not hold money forever");
            Assert.False(ledger.Capture().Paid.Any(x=>x.Action.EntityIds.Contains(extra.Id)&&x.Action.Kind==PlayableCommandKind.QueueTank));
        }
        [Test] public void ScaleAndUseColdThreeOwnerRestoreDoesNotCreateResearchOrIncomeRows()
        {
            var basic=OfflineParticipantAuthorityTests.Config(3,false,seed:4103);var costs=new double[basic.Starts.Count,basic.Starts.Count];
            for(int i=0;i<basic.Starts.Count;i++)for(int j=0;j<basic.Starts.Count;j++)costs[i,j]=basic.RouteCost(i,j);
            var c=new OfflineMatchConfiguration(basic.Profile,basic.SourceIdentity,basic.MapIdentity,basic.RouteProvenance,basic.Seed,basic.Roster.Select(p=>new OfflineParticipant(p.Id,p.LogicalPlayer,p.Team,OfflineControl.Ai)).ToArray(),basic.Starts.ToArray(),basic.Sites.ToArray(),basic.Obstacles.ToArray(),costs);
            var a=new PlayableAuthorityTick(c,71);var original=a.CaptureBytes();var before=PlayableWorldState.Decode(original);
            int researchRows=((IDictionary)Get(Get(a,"domain"),"research")).Count;int incomeRows=((IDictionary)Get(Get(a,"domain"),"settledIncome")).Count;
            var b=PlayableAuthorityTick.RestoreBytes(original,c);var after=PlayableWorldState.Decode(b.CaptureBytes());
            Assert.AreEqual(researchRows,((IDictionary)Get(Get(b,"domain"),"research")).Count,"restore validation is a read, not a PlayerSnapshot query that materializes owner research");
            Assert.AreEqual(incomeRows,((IDictionary)Get(Get(b,"domain"),"settledIncome")).Count);
            CollectionAssert.AreEqual(before.Domain,after.Domain);CollectionAssert.AreEqual(before.AiAuthority,after.AiAuthority);CollectionAssert.AreEqual(original,b.CaptureBytes());
        }
        [Test] public void ScaleAndUseHistoryHonorsPauseProfileRestartAndRejectsCorruptClocks()
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);PrepareLine(a,c);for(int i=0;i<65;i++)Step(a);
            var owner=Owners(a).First();var demand=Get(owner,"demand");var samples=(IList)Get(demand,"samples");
            int count=samples.Count;double income=EconomyView(a,c).SettledIncome;long tick=a.Tick;
            for(int i=0;i<10;i++)Assert.True(a.TryAdvance(true));
            Assert.AreEqual(tick,a.Tick);Assert.AreEqual(count,samples.Count);Assert.AreEqual(income,EconomyView(a,c).SettledIncome);
            var data=AiProfile.Initial.CopyData();data.revision++;data.fields.Single(x=>x.path=="economy.incomeWindowSeconds").value=45;var ai=new AiProfile(data);
            Assert.IsNull(a.ApplyProfile(c.Profile,ai));var b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c,ai);
            for(int i=0;i<35;i++){Step(a);Step(b);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());}
            var fresh=new PlayableAuthorityTick(c,72,ai);Assert.Zero(EconomyView(fresh,c).SettledIncome);
            Assert.Zero(((IList)Get(Get(Owners(fresh).First(),"demand"),"samples")).Count);
            var latest=samples[samples.Count-1];latest.GetType().GetField("Tick",F).SetValue(latest,a.Tick+1);
            Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c,ai));
        }
        [Test] public void ScaleAndUseStartupReservationCannotBeSpentByAnotherPolicyOrProducer()
        {
            var ledger=Ledger(300);var action=new PlayableAiAction(1,"a","p",1,71,1,PlayableCommandKind.QueueTank,new[]{2},unitKind:PlayableEntityKind.Tank);
            Assert.True(ledger.Reserve(new AiBudgetEntry("startup","factory-startup",action,300,1,1,20,"new line or expiry",new[]{"startup"},"p@1")));
            var wrong=new AiIntent("wrong","economy",Action(),10,150,0,new[]{"slot:3"},utility:2,reservationId:"startup");
            var other=new AiIntent("other","production",new PlayableAiAction(1,"a","p",1,71,1,PlayableCommandKind.QueueTank,new[]{3}),10,150,0,new[]{"recipient:3"},reservationId:"startup");
            var right=new AiIntent("right","production",action,10,150,0,new[]{"recipient:2"},reservationId:"startup");
            var arbiter=new AiDecisionArbiter(AiProfile.Initial);var chosen=arbiter.Select(new[]{wrong,other,right},2,300,10,3,ledger);
            Assert.AreEqual("right",chosen.Single().Id);Assert.AreEqual("startup reservation target mismatch",arbiter.Rejections["wrong"]);Assert.AreEqual("startup reservation target mismatch",arbiter.Rejections["other"]);
            Assert.AreEqual(300,ledger.Reserved,"proposal evaluation does not consume a live held fund");Assert.Zero(ledger.Available);
        }
        [Test] public void ScaleAndUseStartupFundProtectedAcrossSelectedAlternatives()
        {
            var a=new AiDecisionArbiter(AiProfile.Initial);
            var scale=new AiIntent("scale","economy",Action(),100,300,0,new[]{"slot:1"},utility:2,startupFund:300);
            var other=new AiIntent("other","research",Action(),100,150,0,new[]{"recipient:2"});
            Assert.AreEqual(0,a.Select(new[]{scale},1,599,10,2).Count);
            var selected=a.Select(new[]{other,scale},2,600,10,2);Assert.AreEqual("scale",selected.Single().Id);Assert.AreEqual("ledger budget unavailable",a.Rejections["other"]);
        }

        [Test] public void LedgerAffordabilityAndDoubleSpend()
        {
            var ledger=Ledger();ledger.SetSafetyReserve(10);
            var arbiter=new AiDecisionArbiter(AiProfile.Initial);
            var selected=arbiter.Select(new[]{Intent("b",60,"slot:2"),Intent("a",60,"slot:1")},1,100,0,2,ledger);
            Assert.AreEqual("a",selected.Single().Id);
            Assert.True(ledger.Accept(Entry("a",receipt:Receipt())));Assert.False(ledger.Accept(Entry("b",claim:"slot:2",receipt:Receipt(2))));
            Assert.AreEqual(100,ledger.Liquid);Assert.AreEqual(60,ledger.Unpaid);Assert.AreEqual(30,ledger.Available);Assert.Zero(ledger.PaidTotal);
            Assert.IsEmpty(arbiter.Select(new[]{Intent("a",0,"different"),Intent("b",40,"slot:2")},2,100,0,2,ledger));
        }
        [Test] public void LedgerReservationExpiryAndLostTargetRelease()
        {
            var ledger=Ledger();Assert.True(ledger.Reserve(Entry("expired")));ledger.Expire(20);Assert.AreEqual(60,ledger.Reserved);
            ledger.Expire(21);Assert.Zero(ledger.Reserved);Assert.AreEqual(100,ledger.Available);Assert.False(ledger.Release("expired"));
            Assert.True(ledger.Reserve(Entry("lost")));ledger.Expire(5,e=>false);Assert.Zero(ledger.Reserved);
            Assert.Throws<ArgumentException>(()=>ledger.Reserve(Entry("unbounded",expires:ledger.ReservationLifetimeTicks+2)));
        }
        [Test] public void LedgerStableIntentConsumesOwnReservation()
        {
            var ledger=Ledger();Assert.True(ledger.Reserve(Entry("a",70)));Assert.True(ledger.Reserve(Entry("a",70)));
            var arbiter=new AiDecisionArbiter(AiProfile.Initial);
            var chosen=arbiter.Select(new[]{Intent("a",60,"slot:1"),Intent("b",40,"slot:2")},2,100,0,2,ledger);
            CollectionAssert.AreEqual(new[]{"a","b"},chosen.Select(x=>x.Id));
            Assert.True(ledger.Accept(Entry("a",receipt:Receipt())));Assert.True(ledger.Accept(Entry("b",40,"slot:2",Receipt(2))));
            Assert.Zero(ledger.Reserved);Assert.AreEqual(100,ledger.Unpaid);Assert.Zero(ledger.Available);
        }
        [Test] public void LedgerReservationFundingAfterExternalBankDropProtectsOthers()
        {
            var ledger=Ledger();Assert.True(ledger.Reserve(Entry("a",60)));Assert.True(ledger.Reserve(Entry("b",40,"slot:2")));
            ledger.ObserveBank(50);var arbiter=new AiDecisionArbiter(AiProfile.Initial);
            Assert.IsEmpty(arbiter.Select(new[]{Intent("a",30,"slot:1")},2,50,0,1,ledger));
            Assert.AreEqual("a",arbiter.Select(new[]{Intent("a",10,"slot:1")},2,50,0,1,ledger).Single().Id);
            Assert.True(ledger.Accept(Entry("a",10,receipt:Receipt())));Assert.AreEqual(40,ledger.Reserved);Assert.AreEqual(10,ledger.Unpaid);Assert.Zero(ledger.Available);
        }
        [Test] public void LedgerAppliedRestoreAndLateReceiptAreIdempotent()
        {
            var ledger=Ledger();var first=Receipt();Assert.True(ledger.Accept(Entry("stable",receipt:first)));
            Assert.True(ledger.Reconcile("stable",first,PlayableAiDeliveryStatus.Applied));
            Assert.AreEqual(100,ledger.Liquid,"Gameplay owns the debit; ledger never debits bank.");ledger.ObserveBank(40);
            ledger=AiBudgetLedger.Restore(ledger.Capture(),10);
            Assert.False(ledger.Reconcile("stable",first,PlayableAiDeliveryStatus.Rejected));Assert.False(ledger.Reconcile("stable",first,PlayableAiDeliveryStatus.Applied));
            Assert.AreEqual(60,ledger.PaidTotal);Assert.AreEqual(40,ledger.Available);
            ledger.ObserveBank(100);var second=Receipt(2);Assert.True(ledger.Accept(Entry("stable",receipt:second)));
            Assert.False(ledger.Reconcile("stable",first,PlayableAiDeliveryStatus.Stale));Assert.AreEqual(60,ledger.Unpaid);
            Assert.False(ledger.Reconcile("stable",Receipt(2,72),PlayableAiDeliveryStatus.Applied));
            Assert.False(ledger.Reconcile("stable",Receipt(2,71,"other"),PlayableAiDeliveryStatus.Applied));
            Assert.True(ledger.Reconcile("stable",second,PlayableAiDeliveryStatus.Rejected));Assert.Zero(ledger.Unpaid);Assert.AreEqual(60,ledger.PaidTotal);
            Assert.False(ledger.Accept(Entry("stable",receipt:first)),"Settled receipt cannot be admitted twice.");
        }
        [TestCase(PlayableAiDeliveryStatus.Rejected)][TestCase(PlayableAiDeliveryStatus.Stale)]
        [TestCase(PlayableAiDeliveryStatus.Cancelled)][TestCase(PlayableAiDeliveryStatus.Stopped)]
        [TestCase(PlayableAiDeliveryStatus.InvalidOwner)][TestCase(PlayableAiDeliveryStatus.InvalidAction)]
        public void LedgerTerminalFailureReleasesOnce(PlayableAiDeliveryStatus status)
        {
            var ledger=Ledger();var receipt=Receipt();Assert.True(ledger.Accept(Entry("a",receipt:receipt)));
            Assert.False(ledger.Release("a"));Assert.False(ledger.Reconcile("a",receipt,PlayableAiDeliveryStatus.Accepted));
            Assert.True(ledger.Reconcile("a",receipt,status));Assert.False(ledger.Reconcile("a",receipt,status));
            Assert.Zero(ledger.Unpaid);Assert.Zero(ledger.PaidTotal);Assert.AreEqual(100,ledger.Available);
        }
        [Test] public void LedgerPositiveAndNonoverlapInvariants()
        {
            var ledger=Ledger();Assert.Throws<ArgumentException>(()=>Entry("bad",-1));Assert.Throws<ArgumentException>(()=>ledger.ObserveBank(-1));
            Assert.Throws<ArgumentException>(()=>ledger.SetSafetyReserve(-1));
            Assert.True(ledger.Reserve(Entry("a",30)));Assert.False(ledger.Reserve(Entry("b",30)));
            var overlap=new AiBudgetState("a",71,100,0,0,new[]{Entry("a",30),Entry("b",30)},Array.Empty<AiBudgetEntry>(),Array.Empty<AiBudgetEntry>());
            Assert.Throws<ArgumentException>(()=>AiBudgetLedger.Restore(overlap,10));
            Assert.Throws<ArgumentException>(()=>new AiBudgetEntry("a","x",Action(),1,0,1,2,"expiry",new[]{"same","same"},"p@1"));
            ledger.ObserveBank(10);Assert.Zero(ledger.Available,"External legitimate spend does not create negative available money.");
            Assert.AreEqual(30,ledger.Reserved);Assert.False(ledger.Accept(Entry("a",30,receipt:Receipt())));
        }
        [Test] public void LedgerRebindOnlyFutureTermsAndReleaseUnaffordableReserve()
        {
            var ledger=Ledger(300);var receipt=Receipt();Assert.True(ledger.Accept(Entry("paid",receipt:receipt)));
            Assert.True(ledger.Reconcile("paid",receipt,PlayableAiDeliveryStatus.Applied));ledger.ObserveBank(240);
            Assert.True(ledger.Reserve(Entry("future",80)));Assert.True(ledger.Reserve(Entry("lower",80,"slot:2")));
            ledger.RepriceReservations(e=>150,"p@2");var state=ledger.Capture();Assert.AreEqual(150,ledger.Reserved);
            Assert.AreEqual("future",state.Reserved.Single().Id);Assert.AreEqual("p@2",state.Reserved.Single().Terms);
            Assert.AreEqual(60,state.Paid.Single().Amount);Assert.AreEqual("p@1",state.Paid.Single().Terms);Assert.AreEqual(240,ledger.Liquid);
            ledger.RebindLifetime(5);ledger.Expire(7);Assert.Zero(ledger.Reserved);
        }
        [Test] public void LedgerDeferredPaymentKeepsUnpaidThroughTerminalAndRestore()
        {
            var ledger=Ledger();var receipt=Receipt();var entry=new AiBudgetEntry("research","research",Action(PlayableCommandKind.QueueResearch),60,0,1,20,"terminal",Array.Empty<string>(),"p@1",receipt);
            Assert.True(ledger.Accept(entry));Assert.True(ledger.Reconcile("research",receipt,PlayableAiDeliveryStatus.Applied,false));
            ledger=AiBudgetLedger.Restore(ledger.Capture(),10);Assert.AreEqual(60,ledger.Unpaid);Assert.Zero(ledger.PaidTotal);
            Assert.False(ledger.Reconcile("research",receipt,PlayableAiDeliveryStatus.Rejected));
            Assert.True(ledger.SettlePayment("research",receipt,true,0,70,"p@2"));Assert.AreEqual(70,ledger.Unpaid);
            Assert.True(ledger.SettlePayment("research",receipt,true,70,70,"p@2"));Assert.False(ledger.SettlePayment("research",receipt,true,70,70,"p@2"));
            Assert.AreEqual(70,ledger.PaidTotal);Assert.Zero(ledger.Unpaid);Assert.AreEqual("p@2",ledger.Capture().Paid.Single().Terms);
        }
        [Test] public void LedgerOldAdmissionCannotReplayAfterBoundedHistoryOrRejectedRestore()
        {
            var ledger=Ledger(10000);
            for(int i=1;i<=140;i++){Assert.True(ledger.Accept(Entry("stable",1,receipt:Receipt(i))));Assert.True(ledger.Reconcile("stable",Receipt(i),PlayableAiDeliveryStatus.Applied));}
            Assert.AreEqual(128,ledger.Capture().Paid.Count);ledger=AiBudgetLedger.Restore(ledger.Capture(),10);
            Assert.False(ledger.Accept(Entry("stable",1,receipt:Receipt(1))));Assert.AreEqual(140,ledger.PaidTotal);
            Assert.True(ledger.Accept(Entry("stable",1,receipt:Receipt(141))));Assert.True(ledger.Reconcile("stable",Receipt(141),PlayableAiDeliveryStatus.Rejected));
            ledger=AiBudgetLedger.Restore(ledger.Capture(),10);Assert.False(ledger.Accept(Entry("stable",1,receipt:Receipt(141))));
            Assert.True(ledger.Accept(Entry("stable",1,receipt:Receipt(142))));
        }
        private static object Get(object o,string field)=>o.GetType().GetField(field,F).GetValue(o);
        private static object[] Owners(PlayableAuthorityTick t)=>((IEnumerable)Get(Get(t,"scheduler"),"owners")).Cast<object>().ToArray();
        private static AiBudgetLedger Budget(object o)=>(AiBudgetLedger)Get(o,"budget");
        private static OfflineMatchConfiguration Config()
        {
            var c=OfflineParticipantAuthorityTests.Config(2,false);var routes=new double[c.Starts.Count,c.Starts.Count];
            for(int i=0;i<c.Starts.Count;i++)for(int j=0;j<c.Starts.Count;j++)routes[i,j]=i==j?0:1;
            return new OfflineMatchConfiguration(c.Profile,c.SourceIdentity,c.MapIdentity,c.RouteProvenance,c.Seed,
                c.Roster.Select(p=>new OfflineParticipant(p.Id,p.LogicalPlayer,p.Team,OfflineControl.Ai)).ToArray(),c.Starts.ToArray(),c.Sites.ToArray(),c.Obstacles.ToArray(),routes);
        }
        private static void Step(PlayableAuthorityTick t)
        {while(t.Requests.TryDequeue(out var r))Assert.True(t.Answers.TryEnqueue(new NavigationAnswer(r,Array.Empty<NavPoint>())));if(!t.TryAdvance()){while(t.Requests.TryDequeue(out var r))Assert.True(t.Answers.TryEnqueue(new NavigationAnswer(r,Array.Empty<NavPoint>())));Assert.True(t.TryAdvance());}}
        [Test] public void LedgerRealAuthoritySaveRestorePendingAndPaidSuffix()
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);for(int i=0;i<45;i++)Step(a);
            TestContext.WriteLine("E1_IDENTITY gameplay="+c.Profile.ProfileId+"@"+c.Profile.Revision+" ai="+AiProfile.Initial.Id+"@"+AiProfile.Initial.Revision+" aiHash="+AiProfile.Initial.Hash+" seed="+c.Seed+" map="+c.MapIdentity+" source="+PlayableAiOpeningComposition.SourceIdentity);
            Assert.True(Owners(a).All(o=>Budget(o).Unpaid>0));
            var b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);
            for(int i=0;i<120;i++){Step(a);Step(b);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());}
            foreach(var o in Owners(b))
            {
                var budget=Budget(o);Assert.Greater(budget.PaidTotal,0);Assert.True(budget.Capture().Paid.Any(e=>e.Action.Kind==PlayableCommandKind.BuildAt));
                var domain=Get(b,"domain");Assert.AreEqual(domain.GetType().GetMethod("AiLiquidCredits",F).Invoke(domain,new object[]{budget.OwnerId}),budget.Liquid);
            }
        }
        [Test] public void LedgerRealDestroyedProducerReleasesReservation()
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);for(int i=0;i<80;i++)Step(a);
            var owner=Owners(a)[0];var ledger=Budget(owner);var domain=Get(a,"domain");
            var logical=domain.GetType().GetMethod("OwnerFor",F).Invoke(domain,new object[]{ledger.OwnerId});
            var snapshot=(PlayableSnapshot)domain.GetType().GetMethod("PlayerSnapshot",F).Invoke(domain,new object[]{1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed,logical});
            var factory=snapshot.Buildings.Single(x=>x.Kind==PlayableBuildingKind.Factory&&x.Owner.Equals(logical));
            var action=new PlayableAiAction(1,ledger.OwnerId,c.Profile.ProfileId,c.Profile.Revision,71,1,PlayableCommandKind.QueueTank,new[]{factory.Id},seed:c.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            Assert.True(ledger.Reserve(new AiBudgetEntry("producer-reserve","production",action,10,0,80,300,"producer destroyed",new[]{"recipient:"+factory.Id},c.Profile.ProfileId+"@"+c.Profile.Revision)));
            domain.GetType().GetMethod("Damage",F).Invoke(domain,new object[]{factory.Id,1000000});
            for(int i=0;i<20;i++)Step(a);
            Assert.IsEmpty(ledger.Capture().Reserved.Where(x=>x.Id=="producer-reserve"));
        }
        [Test] public void LedgerRealProfileBarrierPreservesPaidAndRepricesReserve()
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);for(int i=0;i<80;i++)Step(a);
            var owner=Owners(a)[0];var ledger=Budget(owner);var paid=ledger.Capture().Paid.First(e=>e.Action.Kind==PlayableCommandKind.BuildAt);
            var domain=Get(a,"domain");var logical=domain.GetType().GetMethod("OwnerFor",F).Invoke(domain,new object[]{ledger.OwnerId});
            domain.GetType().GetMethod("AddCredits",F).Invoke(domain,new[]{logical,(object)10000d});
            ledger.ObserveBank((int)domain.GetType().GetMethod("AiLiquidCredits",F).Invoke(domain,new object[]{ledger.OwnerId}));
            Assert.True(ledger.Reserve(new AiBudgetEntry("future-factory","economy",paid.Action,c.Profile.FactoryCreditCost,0,80,300,"expiry",new[]{"future-slot"},paid.Terms)));
            var data=c.Profile.CopyData();data.revision++;data.factoryCreditCost+=50;var next=PlayableProfile.Create(data,c.Profile.AuthoredMap,"E1 revised");
            Assert.IsNull(a.ApplyProfile(next));var state=ledger.Capture();
            Assert.AreEqual(c.Profile.FactoryCreditCost,state.Paid.First(e=>e.Receipt.Id==paid.Receipt.Id).Amount);
            Assert.AreEqual(paid.Terms,state.Paid.First(e=>e.Receipt.Id==paid.Receipt.Id).Terms);
            Assert.AreEqual(next.FactoryCreditCost,state.Reserved.Single().Amount);Assert.AreEqual(next.ProfileId+"@"+next.Revision,state.Reserved.Single().Terms);
            var corrupt=PlayableWorldState.Decode(a.CaptureBytes());corrupt.AiAuthority[4]=1;Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(corrupt.Encode(),c));
        }
        [Test] public void LedgerRealAiProfileCanReduceActionLimitAndRestorePastAdmission()
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71,difficulty:AiDifficulty.Veteran);for(int i=0;i<15;i++)Step(a);
            Assert.True(Owners(a).Any(o=>Budget(o).Capture().AdmittedAction>1));
            var data=AiProfile.Initial.CopyData();data.revision++;
            foreach(var field in data.fields.Where(f=>f.path.EndsWith(".actionsPerDecision",StringComparison.Ordinal)))field.value=1;
            var ai=new AiProfile(data);Assert.IsNull(a.ApplyProfile(c.Profile,ai));
            var b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c,ai);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
            for(int i=0;i<80;i++){Step(a);Step(b);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());}
        }
        [TestCase(false)][TestCase(true)] public void LedgerRealQueuedResearchSettlesOnlyWhenGameplayPays(bool free)
        {
            var d=OfflineParticipantAuthorityTests.D;var profile=PlayableProfile.Default;
            if(free){var data=profile.CopyData();data.explorerAssaultCost=0;profile=PlayableProfile.Create(data,profile.AuthoredMap,"E1 zero price fixture");}
            var domain=Activator.CreateInstance(d,F,null,new object[]{profile,71L},null);
            object Call(string name,params object[] args)=>d.GetMethod(name,F).Invoke(domain,args);
            Call("AddCredits",PlayableOwner.Player,10000d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,2,PlayableBuildingKind.ScientificCenter,1,PlayableOwner.Player,null));
            Call("AdvanceFoundations");Call("AdvanceBuildings",TerritoryRules.Duration(profile,PlayableBuildingKind.ScientificCenter)+1);
            var snapshot=(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19092026,PlayableOwner.Player);
            int center=snapshot.Buildings.Single(x=>x.Kind==PlayableBuildingKind.ScientificCenter).Id;
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("QueueResearch",center,PlayableOwner.Player,PlayableResearchKind.TankChassis,null));
            var ledger=new AiBudgetLedger("player-1",71);ledger.ObserveBank((int)Call("AiLiquidCredits","player-1"));
            var action=new PlayableAiAction(1,"player-1",profile.ProfileId,profile.Revision,71,1,PlayableCommandKind.QueueResearch,new[]{center},researchKind:PlayableResearchKind.ExplorerAssaultGuns);
            var receipt=Receipt(1,71,"player-1");var entry=new AiBudgetEntry("waiting-research","research",action,(int)Math.Ceiling(profile.ExplorerAssaultCost),0,1,200,"terminal",new[]{"producer:"+center},profile.ProfileId+"@"+profile.Revision,receipt);
            Assert.True(ledger.Accept(entry));int before=ledger.Liquid;
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("QueueResearch",center,PlayableOwner.Player,PlayableResearchKind.ExplorerAssaultGuns,null));
            bool paid=(bool)Call("AiCommandPaid",action);Assert.False(paid);Assert.True(ledger.Reconcile(entry.Id,receipt,PlayableAiDeliveryStatus.Applied,paid));
            Call("ReconcileAiPayments",ledger);Assert.AreEqual(before,ledger.Liquid);Assert.AreEqual(1,ledger.Capture().Unpaid.Count);Assert.Zero(ledger.PaidTotal);
            Call("AdvanceResearch",profile.TankChassisSeconds);Call("ReconcileAiPayments",ledger);
            Assert.Zero(ledger.Unpaid);Assert.AreEqual((long)Math.Ceiling(profile.ExplorerAssaultCost),ledger.PaidTotal);Assert.AreEqual(before-(int)Math.Ceiling(profile.ExplorerAssaultCost),ledger.Liquid);
            Assert.IsEmpty(ledger.Capture().Unpaid);Assert.AreEqual(1,ledger.Capture().Paid.Count);
            Call("ReconcileAiPayments",ledger);Assert.AreEqual((long)Math.Ceiling(profile.ExplorerAssaultCost),ledger.PaidTotal);
        }
    }
}
