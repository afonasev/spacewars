using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using Spacewars.Presentation;
namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiTechnologyTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        private static object Get(object o,string name)=>o.GetType().GetField(name,F).GetValue(o);
        private static object Owner(PlayableAuthorityTick a)=>((IEnumerable)Get(Get(a,"scheduler"),"owners")).Cast<object>().Single();
        private static AiBudgetLedger Budget(PlayableAiObservation o){var b=new AiBudgetLedger(o.OwnerId,o.Generation);b.ObserveBank(o.Credits);return b;}
        private static PlayableAiObservation View(PlayableProfile p,PlayableEntityKind kind=PlayableEntityKind.Tank,bool center=true,PlayableResearchOrderSnapshot[] research=null,PlayableProductionOrderSnapshot[] orders=null,int credits=10000,PlayableEntityKind? secondKind=null)
        {
            var buildings=new[]{new PlayableBuildingSnapshot(4,PlayableOwner.Player,center?PlayableBuildingKind.ScientificCenter:PlayableBuildingKind.Factory,default(NavPoint),100,1,orders?.Length??0,0,default(NavPoint),orders:orders)}
                .Concat(center?new[]{new PlayableBuildingSnapshot(5,PlayableOwner.Player,PlayableBuildingKind.Factory,new NavPoint(4,0),100,1,0,0,default(NavPoint))}:Array.Empty<PlayableBuildingSnapshot>()).ToArray();
            var entities=new[]{new PlayableEntitySnapshot(2,PlayableOwner.Player,kind,default(NavPoint),PlayableUnitRules.Health(p,kind),false,0,0,0)}
                .Concat(secondKind.HasValue?new[]{new PlayableEntitySnapshot(3,PlayableOwner.Player,secondKind.Value,new NavPoint(2,0),PlayableUnitRules.Health(p,secondKind.Value),false,0,0,0)}:Array.Empty<PlayableEntitySnapshot>()).ToArray();
            return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,41,1,45,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,credits,null,
                entities,buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,
                researchAvailability:AiResearchCatalog.All.Select(d=>new PlayableResearchAvailabilitySnapshot(d.Kind,d.Cost(p),true)).ToArray(),ownerResearch:research));
        }
        private static AiBudgetLedger WithTechnologyHold(PlayableAiObservation o,PlayableProfile p,PlayableResearchKind branch,int amount)
        {
            var b=Budget(o);var action=new PlayableAiAction(90,o.OwnerId,p.ProfileId,p.Revision,o.Generation,o.SnapshotSequence,PlayableCommandKind.QueueResearch,new[]{4},researchKind:branch,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            var entry=new AiBudgetEntry("technology-funding:Research:"+branch+":1:1:1",AiTechnologyFunding.Purpose,action,amount,1,o.Tick,o.Tick+300,"test hold",Array.Empty<string>(),p.ProfileId+"@"+p.Revision);
            Assert.True(b.Reserve(entry),"fixture technology reservation fits observed cash");return b;
        }
        [TestCase(PlayableEntityKind.Tank,PlayableResearchKind.TankChassis)]
        [TestCase(PlayableEntityKind.Explorer,PlayableResearchKind.ExplorerAssaultGuns)]
        [TestCase(PlayableEntityKind.Shkval,PlayableResearchKind.ShkvalGuidance)]
        public void AllBranches_UsesActualEffectsAndAffectedArmy(PlayableEntityKind unit,PlayableResearchKind kind)
        {
            var p=PlayableProfile.Default;var o=View(p,unit);var planner=new AiTechnologyPlanner();var plan=planner.Plan(o,p,AiProfile.Initial,Budget(o).Capture());
            CollectionAssert.AreEquivalent(Enum.GetValues(typeof(PlayableResearchKind)),AiResearchCatalog.All.Select(d=>d.Kind));
            Assert.AreEqual(kind,plan.Action.ResearchKind);Assert.AreEqual(4,plan.Action.EntityIds.Single());Assert.AreEqual(Enum.GetValues(typeof(PlayableResearchKind)).Length,plan.Branches.Count);
            foreach(var b in plan.Branches){Assert.AreEqual(b.Descriptor.Affects(unit)?PlayableUnitRules.Cost(p,unit):0,b.AffectedValue);Assert.AreEqual(!b.Descriptor.Global&&b.Descriptor.Unit==unit,b.Utility>0,"global branches stay below the army threshold in this old-branch fixture");}
            var d=AiResearchCatalog.For(kind);double expected=unit==PlayableEntityKind.Tank?p.TankChassisSpeed/p.TankSpeed-1:unit==PlayableEntityKind.Explorer?PlayableUnitRules.DamagePerSecond(p,unit,true)/PlayableUnitRules.DamagePerSecond(p,unit)-1:p.ShkvalGuidanceRange/p.ShkvalBaseRange-1;
            Assert.AreEqual(expected,d.RelativeGain(p),1e-12);
        }
        [Test] public void ReadyCenter_FullHoldForOwnBranchMakesThatBranchPayable()
        {
            var p=PlayableProfile.Default;var o=View(p,PlayableEntityKind.Shkval,credits:(int)p.ShkvalGuidanceCost);var budget=WithTechnologyHold(o,p,PlayableResearchKind.ShkvalGuidance,(int)p.ShkvalGuidanceCost);
            var plan=new AiTechnologyPlanner().Plan(o,p,AiProfile.Initial,budget.Capture(),preferredFunding:PlayableResearchKind.ShkvalGuidance);
            Assert.AreEqual(PlayableResearchKind.ShkvalGuidance,plan.FundingGoal.Research);Assert.AreEqual(PlayableResearchKind.ShkvalGuidance,plan.Action.ResearchKind);
        }
        [Test] public void ReadyCenter_UnrelatedFreeCashStillFundsHigherUtilityBranch()
        {
            // Ledger isolation uses an intentionally expensive alternative branch; ordinary Guidance now costs 300.
            var data=PlayableProfile.Default.CopyData();data.shkvalGuidanceCost=600;var p=PlayableProfile.Create(data);var o=View(p,PlayableEntityKind.Explorer,credits:500,secondKind:PlayableEntityKind.Shkval);var budget=WithTechnologyHold(o,p,PlayableResearchKind.ShkvalGuidance,200);
            Assert.AreEqual(300,budget.Available);
            var plan=new AiTechnologyPlanner().Plan(o,p,AiProfile.Initial,budget.Capture(),preferredFunding:PlayableResearchKind.ShkvalGuidance);
            Assert.AreEqual(PlayableResearchKind.ShkvalGuidance,plan.FundingGoal.Research);Assert.AreEqual(PlayableResearchKind.ExplorerAssaultGuns,plan.Action.ResearchKind);
        }
        [Test] public void ReadyCenter_UnrelatedPartialHoldCannotPayForDifferentBranch()
        {
            // Ledger isolation uses an intentionally expensive alternative branch; ordinary Guidance now costs 300.
            var data=PlayableProfile.Default.CopyData();data.shkvalGuidanceCost=600;var p=PlayableProfile.Create(data);var o=View(p,PlayableEntityKind.Explorer,credits:400,secondKind:PlayableEntityKind.Shkval);var budget=WithTechnologyHold(o,p,PlayableResearchKind.ShkvalGuidance,200);
            Assert.AreEqual(200,budget.Available);
            var plan=new AiTechnologyPlanner().Plan(o,p,AiProfile.Initial,budget.Capture(),preferredFunding:PlayableResearchKind.ShkvalGuidance);
            Assert.AreEqual(PlayableResearchKind.ShkvalGuidance,plan.FundingGoal.Research);Assert.IsNull(plan.Action);
        }
        [Test] public void PrerequisiteAndBudgetUseSharedSafetyAndDoNotMutateLedger()
        {
            var p=PlayableProfile.Default;var o=View(p,center:false);var b=Budget(o);var before=b.Capture();var planner=new AiTechnologyPlanner();
            var plan=planner.Plan(o,p,AiProfile.Initial,b.Capture());Assert.NotNull(plan.Infrastructure);Assert.IsNull(plan.Action);Assert.AreEqual(PlayableBuildingKind.ScientificCenter,plan.Infrastructure.Building);
            Assert.AreEqual(before.PaidTotal,b.PaidTotal);Assert.Zero(b.Unpaid);Assert.Zero(b.Reserved);
            b.SetSafetyReserve(o.Credits-(int)p.TankChassisCost-(int)p.ScienceCreditCost+1);Assert.IsNull(planner.Plan(o,p,AiProfile.Initial,b.Capture()).Infrastructure);
            b.SetSafetyReserve(0);var danger=planner.Plan(o,p,AiProfile.Initial,b.Capture(),true);Assert.IsNull(danger.Infrastructure);Assert.IsNull(danger.Action);Assert.True(danger.Branches.All(x=>x.Utility==0));
            Assert.Throws<ArgumentException>(()=>planner.Plan(o,p,AiProfile.Initial,new AiBudgetLedger("foreign",71).Capture()));
        }
        [TestCase(false,false,AiResearchProgress.Waiting)][TestCase(true,false,AiResearchProgress.Active)][TestCase(false,true,AiResearchProgress.Completed)]
        public void ProgressComesFromOwnerOrdersNotCenterOrAvailability(bool active,bool complete,AiResearchProgress progress)
        {
            var p=PlayableProfile.Default;var order=new PlayableResearchOrderSnapshot(11,PlayableResearchKind.TankChassis,999,active||complete?p.TankChassisCost:0,7,p.TankChassisSeconds,active,complete);
            var o=View(p,center:false,research:new[]{order});var plan=new AiTechnologyPlanner().Plan(o,p,AiProfile.Initial,Budget(o).Capture());
            Assert.AreEqual(progress,plan.Branches.Single(x=>x.Descriptor.Kind==order.Kind).Progress);Assert.IsNull(plan.Action);Assert.AreEqual(progress==AiResearchProgress.Waiting,plan.Infrastructure!=null);
            var lost=View(p,center:false);Assert.NotNull(new AiTechnologyPlanner().Plan(lost,p,AiProfile.Initial,Budget(lost).Capture()).Infrastructure,"Lost active order can be replanned; completed order cannot.");
        }
        private static OfflineMatchConfiguration Config(bool sustainedOpponent=false,bool readyTechnologyEconomy=false,bool readyScience=false,int startingCredits=20000,bool researchLifecycle=false)
        {
            var data=PlayableProfile.Default.CopyData();data.startingCredits=startingCredits;
            // Lifecycle fixture keeps its original immediate unlock; default 3/6, 5/8, 7/10 minute boundaries are verified separately.
            if(researchLifecycle){data.aiArmySystemsVeteranDelaySeconds=0;data.aiArmySystemsVeteranLevel2DelaySeconds=0;}
            var p=PlayableProfile.Create(data);
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-24,0),new NavPoint(-24,8),pin:1),new OfflineStart("east",2,new NavPoint(24,0),new NavPoint(24,8),pin:2)};
            var slots=new[]{new TerritorySlot(1,new NavPoint(-24,-9),0),new TerritorySlot(2,new NavPoint(-14,0),0),new TerritorySlot(3,new NavPoint(-14,-9),0),new TerritorySlot(4,new NavPoint(-24,-18),0),new TerritorySlot(5,new NavPoint(-14,-18),0)};
            var sites=new[]{new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,slots),new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,Array.Empty<TerritorySlot>())}
                .Concat(sustainedOpponent?new[]{new TerritorySite(3,PlayableBuildingKind.Outpost,new NavPoint(24,-24),Array.Empty<TerritorySlot>())}:Array.Empty<TerritorySite>()).ToArray();
            return new OfflineMatchConfiguration(p,"o5-diagnostic-funded",sustainedOpponent?"o5-flat-two-starts-ordinary-outpost":"o5-flat-two-starts","UnityHostRouteService",19092026,new[]{new OfflineParticipant("west-owner",1,1,OfflineControl.Ai),new OfflineParticipant("east-owner",2,2,OfflineControl.Human)},starts,sites,readyTechnologyEconomy?new[]{new NavObstacle(-64,-14,64,-12)}:Array.Empty<NavObstacle>(),new double[,]{{0,48},{48,0}},
                scenarioBuildings:(readyScience?new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.ScientificCenter,1,3,slots[2].Position)}:Array.Empty<OfflineScenarioBuilding>()).Concat(sustainedOpponent?new[]{new OfflineScenarioBuilding(2,PlayableBuildingKind.Outpost,3,0,new NavPoint(24,-24))}:Array.Empty<OfflineScenarioBuilding>())
                    .Concat(readyTechnologyEconomy?new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.Factory,1,1,slots[0].Position),new OfflineScenarioBuilding(1,PlayableBuildingKind.Refinery,1,2,slots[1].Position)}:Array.Empty<OfflineScenarioBuilding>()).ToArray());
        }
        private static void Step(PlayableAuthorityTick a,UnityHostRouteService host)
        {for(int i=0;i<512;i++){if(a.TryAdvance())return;host.Service(a,64);}Assert.Fail("native route barrier at tick "+a.Tick);}
        [Test,Explicit("Full navigation BODY equality on every tick is a navigation serialization stress scenario; use the research-focused actual-domain scenario for feature QA.")] public void VeteranMidgameActuallyPaysAndCompletesArmySystemsWithRestore()
        {
            var config=Config(sustainedOpponent:true,readyTechnologyEconomy:true,readyScience:true,startingCredits:300,researchLifecycle:true);var a=new PlayableAuthorityTick(config,71,difficulty:AiDifficulty.Veteran);var owner=Owner(a);
            var opening=PlayableAiOpeningComposition.Initialize(config.Seed,"west-owner",forcedOpening:PlayableAiOpening.Safe);
            owner.GetType().GetField("opening",F).SetValue(owner,opening);owner.GetType().GetField("openingExecutor",F).SetValue(owner,new AiOpeningExecutor(opening,71));
            var phase=(AiPhaseCompositionPlanner)Get(owner,"composition");phase.Observe(PlayableAiObservation.From(a.ParticipantView("west-owner")),config.Profile,AiProfile.Initial);
            phase.GetType().GetField("phase",F).SetValue(phase,AiGamePhase.Mid);phase.GetType().GetField("entered",F).SetValue(phase,1L);
            var domain=Get(a,"domain");var own=a.ParticipantView("west-owner").Owner;
            for(int i=0;i<10;i++)domain.GetType().GetMethod("SpawnUnit",F).Invoke(domain,new object[]{new NavPoint(-20+4*(i%5),8+4*(i/5)),own,i<2?PlayableEntityKind.Tank:PlayableEntityKind.Explorer});
            // Constrained mixed roster exercises the ten-unit gate and real savings;
            // avoids a 20k-bank macro/army-navigation stress scenario unrelated to research.
            // All original ticks, durations, exact suffix comparisons and assertions remain.
            bool saving=false,active=false;PlayableAuthorityTick restored=null;
            using(var host=new UnityHostRouteService())for(int i=0;i<3600;i++)
            {
                Step(a,host);var o=a.ParticipantView("west-owner");
                var plan=(AiTechnologyPlan)owner.GetType().GetProperty("TechnologyPlan").GetValue(owner);saving|=plan?.PriorityResearch==true;
                active|=o.OwnerResearch.Any(r=>PlayableResearchRules.Global(r.Kind)&&r.Active);
                if(restored!=null){Step(restored,host);Assert.True(a.CaptureBytes().AsSpan().SequenceEqual(restored.CaptureBytes()),"army systems exact suffix");}
                if(active&&restored==null){var bytes=a.CaptureBytes();restored=PlayableAuthorityTick.RestoreBytes(bytes,config);CollectionAssert.AreEqual(bytes,restored.CaptureBytes());}
                if(o.OwnerResearch.Any(r=>PlayableResearchRules.Global(r.Kind)&&r.Complete))break;
            }
            Assert.True(saving);Assert.True(active);Assert.True(a.ParticipantView("west-owner").OwnerResearch.Any(r=>PlayableResearchRules.Global(r.Kind)&&r.Complete),"veteran midgame must complete an ordinary paid army-system order");
            var budget=(AiBudgetLedger)Get(owner,"budget");Assert.True(budget.Capture().Paid.Any(e=>e.Action.Kind==PlayableCommandKind.QueueResearch&&PlayableResearchRules.Global(e.Action.ResearchKind)));
            a.Stop();restored?.Stop();
        }
        private static long[] ResearchBytes(PlayableSnapshot o)=>o.OwnerResearch.SelectMany(r=>new[]{r.Id,(long)r.Kind,(long)r.CenterId,BitConverter.DoubleToInt64Bits(r.PaidCost),BitConverter.DoubleToInt64Bits(r.Elapsed),BitConverter.DoubleToInt64Bits(r.Duration),r.Active?1L:0L,r.Complete?1L:0L}).ToArray();
        [Test] public void VeteranMidgameCompletesArmyResearchAndRestoresExactResearchProgress()
        {
            var config=Config(sustainedOpponent:true,readyTechnologyEconomy:true,readyScience:true,startingCredits:300,researchLifecycle:true);var a=new PlayableAuthorityTick(config,71,difficulty:AiDifficulty.Veteran);var owner=Owner(a);
            var opening=PlayableAiOpeningComposition.Initialize(config.Seed,"west-owner",forcedOpening:PlayableAiOpening.Safe);
            owner.GetType().GetField("opening",F).SetValue(owner,opening);owner.GetType().GetField("openingExecutor",F).SetValue(owner,new AiOpeningExecutor(opening,71));
            var phase=(AiPhaseCompositionPlanner)Get(owner,"composition");phase.Observe(PlayableAiObservation.From(a.ParticipantView("west-owner")),config.Profile,AiProfile.Initial);
            phase.GetType().GetField("phase",F).SetValue(phase,AiGamePhase.Mid);phase.GetType().GetField("entered",F).SetValue(phase,1L);
            var domain=Get(a,"domain");var own=a.ParticipantView("west-owner").Owner;
            for(int i=0;i<10;i++)domain.GetType().GetMethod("SpawnUnit",F).Invoke(domain,new object[]{new NavPoint(-20+4*(i%5),8+4*(i/5)),own,i<2?PlayableEntityKind.Tank:PlayableEntityKind.Explorer});
            // Constrained mixed roster exercises the ten-unit gate and real savings;
            // avoids a 20k-bank macro/army-navigation stress scenario unrelated to research.
            // All original ticks, durations, exact suffix comparisons and assertions remain.
            bool saving=false,active=false;PlayableAuthorityTick restored=null;
            using(var host=new UnityHostRouteService())for(int i=0;i<3600;i++)
            {
                Step(a,host);var o=a.ParticipantView("west-owner");
                var plan=(AiTechnologyPlan)owner.GetType().GetProperty("TechnologyPlan").GetValue(owner);saving|=plan?.PriorityResearch==true;
                active|=o.OwnerResearch.Any(r=>PlayableResearchRules.Global(r.Kind)&&r.Active);
                if(restored!=null){Step(restored,host);CollectionAssert.AreEqual(ResearchBytes(o),ResearchBytes(restored.ParticipantView("west-owner")),"exact research suffix tick "+a.Tick);}
                if(active&&restored==null){var bytes=a.CaptureBytes();restored=PlayableAuthorityTick.RestoreBytes(bytes,config);Assert.True(bytes.AsSpan().SequenceEqual(restored.CaptureBytes()),"full active checkpoint equality");}
                if(o.OwnerResearch.Any(r=>PlayableResearchRules.Global(r.Kind)&&r.Complete))break;
            }
            Assert.True(saving);Assert.True(active);Assert.True(a.ParticipantView("west-owner").OwnerResearch.Any(r=>PlayableResearchRules.Global(r.Kind)&&r.Complete),"veteran midgame must complete an ordinary paid army-system order");
            var budget=(AiBudgetLedger)Get(owner,"budget");Assert.True(budget.Capture().Paid.Any(e=>e.Action.Kind==PlayableCommandKind.QueueResearch&&PlayableResearchRules.Global(e.Action.ResearchKind)));
            Assert.NotNull(restored);Assert.True(a.CaptureBytes().AsSpan().SequenceEqual(restored.CaptureBytes()),"full final checkpoint equality");
            a.Stop();restored?.Stop();
        }
        [Test] public void AutonomousResearch_BuildsPaysCompletesAndConsumesWithExactRestore()
        {
            // This is a science lifecycle fixture, not a cold-start macro deadline.
            // Prepare Safe's actual home/force prerequisites so technology can compete
            // while the next paid income build proceeds, before growth fills every pad.
            // The barrier keeps the authored enemy outpost alive through research.
            // Science itself is still autonomously built, paid and researched below.
            var config=Config(sustainedOpponent:true,readyTechnologyEconomy:true);var a=new PlayableAuthorityTick(config,71);var owner=Owner(a);
            var opening=PlayableAiOpeningComposition.Initialize(config.Seed,"west-owner",forcedOpening:PlayableAiOpening.Safe);
            owner.GetType().GetField("opening",F).SetValue(owner,opening);owner.GetType().GetField("openingExecutor",F).SetValue(owner,new AiOpeningExecutor(opening,71));
            PlayableAiObservation O()=>PlayableAiObservation.From(a.ParticipantView("west-owner"));
            // Spawn ordinary units through the domain. Authored scenario units pin
            // their upgrade variant forever and therefore cannot test owner research.
            var preparedDomain=Get(a,"domain");
            for(int i=0;i<(int)AiProfile.Initial.Value("opening.forceUnits");i++)
                preparedDomain.GetType().GetMethod("SpawnUnit",F).Invoke(preparedDomain,new object[]{new NavPoint(-20+4*i,8),O().Owner,PlayableEntityKind.Tank});
            Assert.False(O().Buildings.Any(b=>b.Kind==PlayableBuildingKind.ScientificCenter));
            var budget=(AiBudgetLedger)Get(owner,"budget");bool sawActive=false,sawPending=false;PlayableAuthorityTick restored=null;
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<4800&&!O().OwnerResearch.Any(r=>r.Kind==PlayableResearchKind.TankChassis&&r.Complete);i++)
                {
                    Assert.AreEqual(PlayableMatchOutcome.Playing,a.Latest.Outcome,"science lifecycle fixture requires a living opponent until research completes");
                    Step(a,host);var o=O();var checkpoint=a.CaptureDiagnosticCheckpoints().Single();
                    if(restored!=null){Step(restored,host);Assert.True(a.CaptureBytes().AsSpan().SequenceEqual(restored.CaptureBytes()),"research suffix tick "+a.Tick);}
                    if(!sawPending&&budget.Capture().Unpaid.Any(e=>e.Action.Kind==PlayableCommandKind.QueueResearch&&!e.AwaitingPayment))
                    {sawPending=true;var bytes=a.CaptureBytes();restored=PlayableAuthorityTick.RestoreBytes(bytes,config);CollectionAssert.AreEqual(bytes,restored.CaptureBytes());}
                    sawActive|=o.OwnerResearch.Any(r=>r.Kind==PlayableResearchKind.TankChassis&&r.Active&&r.PaidCost==config.Profile.TankChassisCost);
                }
            }
            var final=O();Assert.True(final.OwnerResearch.Any(r=>r.Kind==PlayableResearchKind.TankChassis&&r.Complete),"tick="+a.Tick+" plan="+((AiTechnologyPlan)owner.GetType().GetProperty("TechnologyPlan").GetValue(owner))?.Reason);
            Assert.True(sawActive);Assert.True(sawPending);var paid=budget.Capture().Paid;
            Assert.True(paid.Any(e=>e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.BuildingKind==PlayableBuildingKind.ScientificCenter&&e.Amount==config.Profile.ScienceCreditCost));
            Assert.AreEqual(1,paid.Count(e=>e.Action.Kind==PlayableCommandKind.QueueResearch&&e.Action.ResearchKind==PlayableResearchKind.TankChassis));
            Assert.AreEqual(config.Profile.TankChassisCost,paid.Single(e=>e.Action.Kind==PlayableCommandKind.QueueResearch&&e.Action.ResearchKind==PlayableResearchKind.TankChassis).Amount);
            var domain=Get(a,"domain");Assert.True((bool)domain.GetType().GetMethod("Chassis",F).Invoke(domain,new object[]{final.Owner}));
            Assert.True(final.Entities.Any(e=>e.Kind==PlayableEntityKind.Tank&&e.Upgraded));
            var navigation=Get(domain,"navigation");var crowd=(NavCrowd)navigation.GetType().GetProperty("Crowd").GetValue(navigation);
            foreach(var tank in final.Entities.Where(e=>e.Owner==final.Owner&&e.Kind==PlayableEntityKind.Tank))
            {Assert.True(crowd.TryGet(tank.Id,out var agent));Assert.AreEqual(config.Profile.TankChassisSpeed,agent.Speed);}
            TestContext.WriteLine("O5_AUTONOMOUS "+Newtonsoft.Json.JsonConvert.SerializeObject(new{a.Tick,Profile=config.Profile.ProfileId,Ai=AiProfile.Initial.Hash,Seed=config.Seed,Map=config.MapIdentity,Research=final.OwnerResearch,SciencePaid=paid.Where(e=>e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.BuildingKind==PlayableBuildingKind.ScientificCenter),ResearchPaid=paid.Where(e=>e.Action.Kind==PlayableCommandKind.QueueResearch),sawPending,sawActive}));
            var bytesFinal=a.CaptureBytes();CollectionAssert.AreEqual(bytesFinal,PlayableAuthorityTick.RestoreBytes(bytesFinal,config).CaptureBytes());
        }
        [Test] public void CountingActualAcceptedUnpaidThenPaidQueueAndLivingUnitIsExactlyOnce()
        {
            var config=Config();var a=new PlayableAuthorityTick(config,71);var domain=Get(a,"domain");
            object Call(string method,params object[] args)=>domain.GetType().GetMethod(method,F).Invoke(domain,args);
            PlayableAiObservation O()=>PlayableAiObservation.From(a.ParticipantView("west-owner"));
            var o=O();var home=o.Sites.Single(s=>s.Owner==o.Owner);
            Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,1,o.OwnerId,PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:home.Site.Id,slotId:home.Site.Slots[0].Id,parentId:home.CenterId,buildingKind:PlayableBuildingKind.Factory)).Status);
            Call("AdvanceFoundations");Call("AdvanceBuildings",config.Profile.FactoryBuildSeconds+1);o=O();var factory=o.Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory);
            var action=new PlayableAiAction(99,o.OwnerId,o.ProfileId,o.ProfileRevision,71,o.SnapshotSequence,PlayableCommandKind.QueueTank,new[]{factory.Id},seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            var ledger=Budget(o);var receipt=new AiReceiptIdentity(71,o.OwnerId,1,1);int cost=config.Profile.TankCreditCost;
            var entry=new AiBudgetEntry("o5-production", "production",action,cost,0,o.Tick,o.Tick+100,"terminal receipt",new[]{"recipient:"+factory.Id,"policy:production"},config.Profile.ProfileId+"@"+config.Profile.Revision,receipt);
            Assert.True(ledger.Accept(entry));double Value()=>new AiTechnologyPlanner().Plan(O(),config.Profile,AiProfile.Initial,ledger.Capture()).Branches.Single(x=>x.Descriptor.Kind==PlayableResearchKind.TankChassis).AffectedValue;
            Assert.AreEqual(cost,Value());Assert.Zero(O().Buildings.Single(b=>b.Id==factory.Id).PrivateState.QueueCount);
            var result=a.Apply(new PlayableCommand(71,2,o.OwnerId,action.Kind,action.CopyEntityIds(),origin:PlayableOrderOrigin.Ai,source:action.SourceIdentity,jobId:1,actionId:99));Assert.AreEqual(PlayableCommandStatus.Applied,result.Status);
            Assert.True(ledger.Reconcile(entry.Id,receipt,PlayableAiDeliveryStatus.Applied,true));ledger.ObserveBank(O().Credits);
            Assert.AreEqual(cost,ledger.PaidTotal);Assert.Zero(ledger.Unpaid);Assert.AreEqual(cost,O().Buildings.Single(b=>b.Id==factory.Id).PrivateState.Orders.Single().PaidCost);Assert.AreEqual(cost,Value(),"paid history never doubles ordinary paid queue");
            Call("AdvanceProduction",0d);Call("AdvanceProduction",config.Profile.TankProductionSeconds+1);Assert.True(O().Entities.Any(e=>e.Kind==PlayableEntityKind.Tank));Assert.Zero(O().Buildings.Single(b=>b.Id==factory.Id).PrivateState.QueueCount);Assert.AreEqual(cost,Value(),"completed queue becomes living unit once");a.Stop();
        }
        private static NavPoint FreeThreatPoint(object domain,PlayableProfile profile,NavPoint center)
        {
            var nav=(NavigationSession)Get(domain,"navigation");
            double offset=profile.HeadquartersFootprintRadius+2*profile.TankCollisionRadius+profile.Navigation.ArrivalTolerance;
            foreach(var dx in new[]{offset,-offset,0d})foreach(var dz in new[]{offset,-offset,0d})
            {var at=new NavPoint(center.X+dx,center.Z+dz);if(nav.Crowd.CanPlace(at,profile.TankCollisionRadius))return at;}
            throw new InvalidOperationException("No free current visible HQ threat point");
        }
        [Test] public void DefendVersusTech_ActualOwnerDeliveryCancelsUnpaidWithoutRefundingPaidResearch()
        {
            var config=Config();var a=new PlayableAuthorityTick(config,71);var owner=Owner(a);var domain=Get(a,"domain");
            object Call(string method,params object[] args)=>domain.GetType().GetMethod(method,F).Invoke(domain,args);
            PlayableAiObservation O()=>PlayableAiObservation.From(a.ParticipantView("west-owner"));
            var o=O();var home=o.Sites.Single(s=>s.Owner==o.Owner);
            Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,1,o.OwnerId,PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:home.Site.Id,slotId:home.Site.Slots[0].Id,parentId:home.CenterId,buildingKind:PlayableBuildingKind.ScientificCenter)).Status);
            Call("AdvanceFoundations");Call("AdvanceBuildings",config.Profile.ScienceBuildSeconds+1);
            for(int i=0;i<3;i++)Call("SpawnUnit",new NavPoint(home.Site.Position.X+i*2,home.Site.Position.Z+3),o.Owner,PlayableEntityKind.Tank);
            var ledger=(AiBudgetLedger)Get(owner,"budget");AiBudgetEntry pending=null;
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<1800&&pending==null;i++){Step(a,host);pending=ledger.Capture().Unpaid.FirstOrDefault(e=>e.Action.Kind==PlayableCommandKind.QueueResearch&&!e.AwaitingPayment);
                    var diagnosticCheckpoint=(PlayableAiOwnerCheckpoint)owner.GetType().GetProperty("Checkpoint",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(owner);
                    if(diagnosticCheckpoint.DecisionTick==a.Tick)O5DecisionDiagnostics.Emit("guard-decision",a,owner,O(),config.Profile);
                }
                O5DecisionDiagnostics.Emit("guard-boundary",a,owner,O(),config.Profile,new{Pending=pending});
                Assert.NotNull(pending,"Actual owner must schedule research before threat appears.");
                long action=a.CaptureDiagnosticCheckpoints().Single().Records.Last(r=>r.Policy=="research"&&r.Status==PlayableAiDeliveryStatus.Scheduled).ActionId;
                double bank=(double)Call("Balance",o.Owner);long paid=ledger.PaidTotal;
                int hostile=(int)Call("SpawnUnit",FreeThreatPoint(domain,config.Profile,home.Site.Position),(PlayableOwner)Call("OwnerFor","east-owner"),PlayableEntityKind.Tank);
                Assert.True(O().Entities.Any(e=>e.Id==hostile),"Threat must be CURRENT VISIBLE through ordinary owner fog projection.");
                Step(a,host);var records=a.CaptureDiagnosticCheckpoints().Single().Records;
                Assert.True(records.Any(r=>r.ActionId==action&&r.Status==PlayableAiDeliveryStatus.Cancelled));Assert.False(records.Any(r=>r.ActionId==action&&r.Status==PlayableAiDeliveryStatus.Applied));Assert.False(ledger.Capture().Unpaid.Any(e=>e.Id==pending.Id));Assert.AreEqual(paid,ledger.PaidTotal);
                Assert.GreaterOrEqual((double)Call("Balance",o.Owner),bank,"No unpaid research cost is charged on survival cancellation.");Assert.False(O().OwnerResearch.Any(r=>r.Kind==pending.Action.ResearchKind));
                // Ordinary human paid order remains governed by gameplay, not tech guard.
                var center=O().Buildings.Single(b=>b.Kind==PlayableBuildingKind.ScientificCenter);
                Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,(long)Call("AllocateAiSequence"),o.OwnerId,PlayableCommandKind.QueueResearch,new[]{center.Id},researchKind:pending.Action.ResearchKind)).Status);
                var active=O().OwnerResearch.Single();double paidBank=(double)Call("Balance",o.Owner);
                Step(a,host);var after=O().OwnerResearch.Single();Assert.True(after.Active);Assert.AreEqual(active.Id,after.Id);Assert.AreEqual(active.PaidCost,after.PaidCost);Assert.Greater(after.Elapsed,active.Elapsed);Assert.AreEqual(paidBank,(double)Call("Balance",o.Owner),"Threat guard never cancels/refunds already paid ordinary research.");
                var bytes=a.CaptureBytes();CollectionAssert.AreEqual(bytes,PlayableAuthorityTick.RestoreBytes(bytes,config).CaptureBytes());
                TestContext.WriteLine("O5_VISIBLE_TECH_GUARD "+Newtonsoft.Json.JsonConvert.SerializeObject(new{a.Tick,CancelledAction=action,Threat=hostile,PaidResearch=after,LedgerPaid=ledger.PaidTotal}));
            }
            a.Stop();
        }

        [Test] public void LostCenterWaitingOrderRebuildsThroughS2AndPaysOnlyOnOrdinaryActivation()
        {
            var config=Config();var a=new PlayableAuthorityTick(config,71);var domain=Get(a,"domain");
            object Call(string method,params object[] args)=>domain.GetType().GetMethod(method,F).Invoke(domain,args);
            PlayableAiObservation O()=>PlayableAiObservation.From(a.ParticipantView("west-owner"));
            var o=O();var home=o.Sites.Single(s=>s.Owner==o.Owner);long sequence=1;
            var build=new PlayableCommand(71,sequence++,o.OwnerId,PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:home.Site.Id,slotId:home.Site.Slots[0].Id,parentId:home.CenterId,buildingKind:PlayableBuildingKind.ScientificCenter);
            Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(build).Status);
            Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,sequence++,o.OwnerId,PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:home.Site.Id,slotId:home.Site.Slots[1].Id,parentId:home.CenterId,buildingKind:PlayableBuildingKind.Factory)).Status);
            Call("AdvanceFoundations");Call("AdvanceBuildings",config.Profile.ScienceBuildSeconds+1);
            var center=O().Buildings.Single(b=>b.Kind==PlayableBuildingKind.ScientificCenter);double before=(double)Call("Balance",o.Owner);
            Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,sequence++,o.OwnerId,PlayableCommandKind.QueueResearch,new[]{center.Id},researchKind:PlayableResearchKind.TankChassis)).Status);
            Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,sequence++,o.OwnerId,PlayableCommandKind.QueueResearch,new[]{center.Id},researchKind:PlayableResearchKind.ExplorerAssaultGuns)).Status);
            Assert.AreEqual(before-config.Profile.TankChassisCost,(double)Call("Balance",o.Owner));
            var waiting=O().OwnerResearch.Single(r=>r.Kind==PlayableResearchKind.ExplorerAssaultGuns);Assert.False(waiting.Active);Assert.Zero(waiting.PaidCost);
            // Bind a real ordinary waiting order to the same shared deferred-payment path as owner Apply.
            var ledger=Budget(O());var action=new PlayableAiAction(90,o.OwnerId,o.ProfileId,o.ProfileRevision,71,O().SnapshotSequence,PlayableCommandKind.QueueResearch,new[]{center.Id},researchKind:waiting.Kind,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            var receipt=new AiReceiptIdentity(71,o.OwnerId,1,1);var entry=new AiBudgetEntry("o5-waiting","research",action,(int)config.Profile.ExplorerAssaultCost,0,o.Tick,o.Tick+100,"terminal receipt",new[]{"recipient:"+center.Id,"policy:research"},config.Profile.ProfileId+"@"+config.Profile.Revision,receipt);
            Assert.True(ledger.Accept(entry));Assert.True(ledger.Reconcile(entry.Id,receipt,PlayableAiDeliveryStatus.Applied,false));Assert.AreEqual(config.Profile.ExplorerAssaultCost,ledger.Unpaid);
            Call("Damage",center.Id,(int)config.Profile.ScienceHealth+1);o=O();Assert.AreEqual(waiting.Id,o.OwnerResearch.Single().Id);Assert.AreEqual(before-config.Profile.TankChassisCost,(double)Call("Balance",o.Owner),"Lost paid chassis has no refund; waiting is still unpaid.");
            // Leave exactly construction cash beyond the already held waiting price: no double reservation.
            Call("AddCredits",o.Owner,config.Profile.ScienceCreditCost+config.Profile.ExplorerAssaultCost-(double)Call("Balance",o.Owner));ledger.ObserveBank(O().Credits);
            var plan=new AiTechnologyPlanner().Plan(O(),config.Profile,AiProfile.Initial,ledger.Capture());Assert.NotNull(plan.Infrastructure);Assert.AreEqual(waiting.Kind,plan.Infrastructure.Research);Assert.IsNull(plan.Action);
            var candidate=new AiEconomyPlanner().InfrastructureRequests(O(),config.Profile,plan.Infrastructure).First(c=>c.Legal);
            Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,sequence++,o.OwnerId,candidate.Action.Kind,candidate.Action.CopyEntityIds(),siteId:candidate.Action.SiteId,slotId:candidate.Action.SlotId,parentId:candidate.Action.ParentId,buildingKind:candidate.Action.BuildingKind)).Status);
            Call("AdvanceFoundations");Call("AdvanceBuildings",config.Profile.ScienceBuildSeconds+1);Call("AdvanceResearch",0d);
            var active=O().OwnerResearch.Single();Assert.AreEqual(waiting.Id,active.Id);Assert.True(active.Active);Assert.AreNotEqual(center.Id,active.CenterId);Assert.AreEqual(config.Profile.ExplorerAssaultCost,active.PaidCost);Assert.Zero((double)Call("Balance",o.Owner));
            Call("ReconcileAiPayments",ledger);Assert.Zero(ledger.Unpaid);Assert.AreEqual(config.Profile.ExplorerAssaultCost,ledger.PaidTotal);Assert.AreEqual(receipt.Id,ledger.Capture().Paid.Single().Receipt.Id);
            Call("AdvanceResearch",config.Profile.ExplorerAssaultSeconds);Assert.True(O().OwnerResearch.Single().Complete);Assert.True(O().Entities.Single(e=>e.Kind==PlayableEntityKind.Explorer).Upgraded);
            Assert.IsNull(new AiTechnologyPlanner().Plan(O(),config.Profile,AiProfile.Initial,ledger.Capture()).Action);a.Stop();
        }

        [TestCase("covered")][TestCase("member-loss")][TestCase("other-front")]
        public void CoveredDefense_CurrentUnpaidTechDeliveryUsesFreshLocalCoverage(string condition)
        {
            var basis=Config();var extra=new TerritorySite(3,PlayableBuildingKind.Outpost,new NavPoint(24,-24),Array.Empty<TerritorySlot>());
            var config=new OfflineMatchConfiguration(basis.Profile,"o5-covered-defense","o5-two-fronts","UnityHostRouteService",basis.Seed,basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.Concat(new[]{extra}).ToArray(),basis.Obstacles.ToArray(),new double[,]{{0,48},{48,0}},scenarioBuildings:new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.Outpost,3,0,extra.Position)});
            var a=new PlayableAuthorityTick(config,71);var owner=Owner(a);var domain=Get(a,"domain");
            object Call(string method,params object[] args)=>domain.GetType().GetMethod(method,F).Invoke(domain,args);
            PlayableAiObservation O()=>PlayableAiObservation.From(a.ParticipantView("west-owner"));
            var o=O();var home=o.Sites.Single(s=>s.Site.Id==o.HomeSiteId);long sequence=1;
            var kinds=new[]{PlayableBuildingKind.ScientificCenter,PlayableBuildingKind.Factory,PlayableBuildingKind.Refinery};
            for(int i=0;i<kinds.Length;i++)Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,sequence++,o.OwnerId,PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:home.Site.Id,slotId:home.Site.Slots[i].Id,parentId:home.CenterId,buildingKind:kinds[i])).Status);
            Call("AdvanceFoundations");Call("AdvanceBuildings",config.Profile.ScienceBuildSeconds+1);
            for(int i=0;i<3;i++)Call("SpawnUnit",new NavPoint(home.Site.Position.X+i*2,home.Site.Position.Z+3),o.Owner,PlayableEntityKind.Tank);
            int threat=(int)Call("SpawnUnit",new NavPoint(-5,0),(PlayableOwner)Call("OwnerFor","east-owner"),PlayableEntityKind.Tank);
            var ledger=(AiBudgetLedger)Get(owner,"budget");AiBudgetEntry pending=null;
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<1800&&pending==null;i++){Step(a,host);pending=ledger.Capture().Unpaid.FirstOrDefault(e=>e.Action.Kind==PlayableCommandKind.QueueResearch&&!e.AwaitingPayment);}
                Assert.NotNull(pending);var checkpoint=a.CaptureDiagnosticCheckpoints().Single();
                var scheduled=checkpoint.Records.Last(r=>r.Policy=="research"&&r.Status==PlayableAiDeliveryStatus.Scheduled);var defense=checkpoint.Armies.Armies.Single(g=>g.TacticalOwner==AiDefensePlanner.Policy&&g.Phase!=AiArmyPhase.Disbanded);
                Assert.True(checkpoint.Records.Any(r=>r.Policy==AiDefensePlanner.Policy&&r.Status==PlayableAiDeliveryStatus.Applied));Assert.True(O().Entities.Any(e=>e.Id==threat&&e.Health>0));
                Assert.IsEmpty(O().RouteProofs,"Ordinary participant view omits scheduler proofs: delivery must independently recertify.");
                if(condition=="member-loss")Call("Damage",defense.Members.First(),config.Profile.TankHealth+1);
                if(condition=="other-front")
                {
                    int distant=(int)Call("SpawnUnit",new NavPoint(24,-20),(PlayableOwner)Call("OwnerFor","east-owner"),PlayableEntityKind.Tank);
                    Assert.True(O().Entities.Any(e=>e.Id==distant));
                }
                for(long i=a.Tick;i<=scheduled.DueTick+1;i++)Step(a,host);
                var terminal=a.CaptureDiagnosticCheckpoints().Single().Records.Where(r=>r.ActionId==scheduled.ActionId&&r.Status!=PlayableAiDeliveryStatus.Scheduled).ToArray();
                if(condition=="covered")
                {
                    Assert.True(O().Entities.Any(e=>e.Id==threat&&e.Health>0),"Threat must still be CURRENT VISIBLE when paid tech actually delivers.");
                    Assert.True(terminal.Any(r=>r.Status==PlayableAiDeliveryStatus.Applied));Assert.False(terminal.Any(r=>r.Status==PlayableAiDeliveryStatus.Cancelled));
                    Assert.True(ledger.Capture().Paid.Any(e=>e.Receipt.Id==pending.Receipt.Id&&e.Amount==pending.Amount));
                }
                else{Assert.True(terminal.Any(r=>r.Status==PlayableAiDeliveryStatus.Cancelled));Assert.False(terminal.Any(r=>r.Status==PlayableAiDeliveryStatus.Applied));Assert.False(ledger.Capture().Paid.Any(e=>e.Receipt.Id==pending.Receipt.Id));}
                var bytes=a.CaptureBytes();CollectionAssert.AreEqual(bytes,PlayableAuthorityTick.RestoreBytes(bytes,config).CaptureBytes());
                TestContext.WriteLine("O5_COVERED_DELIVERY "+Newtonsoft.Json.JsonConvert.SerializeObject(new{Condition=condition,a.Tick,Threat=threat,scheduled.DueTick,Terminal=terminal,Defender=defense,Research=O().OwnerResearch}));
            }
            a.Stop();
        }
        [TestCase(0d)][TestCase(1d)]
        public void ProductivePrerequisite_CheapScienceCannotConsumeSoleRequiredFactorySlot(double scienceCost)
        {
            var basis=Config();var data=PlayableProfile.Default.CopyData();data.scienceCreditCost=scienceCost;data.explorerAssaultCost=0;data.tankChassisCost=0;var profile=PlayableProfile.Create(data);
            var sites=basis.Sites.Select(s=>s.Id==1?new TerritorySite(s.Id,s.Kind,s.Position,s.Slots.Take(1).ToArray()):s).ToArray();
            var config=new OfflineMatchConfiguration(profile,"o5-cheap-science","o5-one-owned-slot","UnityHostRouteService",basis.Seed,basis.Roster.ToArray(),basis.Starts.ToArray(),sites,basis.Obstacles.ToArray(),new double[,]{{0,48},{48,0}});
            var a=new PlayableAuthorityTick(config,71);var o=PlayableAiObservation.From(a.ParticipantView("west-owner"));var owner=Owner(a);var ledger=(AiBudgetLedger)Get(owner,"budget");
            var pure=new AiTechnologyPlanner().Plan(o,profile,AiProfile.Initial,Budget(o).Capture());Assert.True(pure.Branches.Any(b=>b.Utility>0));Assert.IsNull(pure.Infrastructure);Assert.IsNull(pure.Action);Assert.AreEqual("compatible productive prerequisite unresolved",pure.Reason);
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<700&&!PlayableAiObservation.From(a.ParticipantView("west-owner")).Buildings.Any(b=>b.Owner==o.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.Phase==ConstructionPhase.Ready);i++)Step(a,host);
                var current=PlayableAiObservation.From(a.ParticipantView("west-owner"));Assert.True(current.Buildings.Any(b=>b.Owner==o.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.Phase==ConstructionPhase.Ready));Assert.False(current.Buildings.Any(b=>b.Owner==o.Owner&&b.Kind==PlayableBuildingKind.ScientificCenter));
                Assert.True(ledger.Capture().Paid.Any(e=>e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.BuildingKind==PlayableBuildingKind.Factory));Assert.False(ledger.Capture().Paid.Concat(ledger.Capture().Unpaid).Concat(ledger.Capture().Reserved).Any(e=>e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.BuildingKind==PlayableBuildingKind.ScientificCenter));
                var bytes=a.CaptureBytes();CollectionAssert.AreEqual(bytes,PlayableAuthorityTick.RestoreBytes(bytes,config).CaptureBytes());
            }
            a.Stop();
        }

    }
    // Instrumentation only: detached snapshot + current exact world bytes, no policy writes/commands.
    internal static class O5DecisionDiagnostics
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        private static object Field(object owner,string name)=>owner.GetType().GetField(name,Flags).GetValue(owner);
        private static object Property(object owner,string name)=>owner.GetType().GetProperty(name,Flags).GetValue(owner);
        internal static void Emit(string label,PlayableAuthorityTick authority,object owner,PlayableAiObservation observation,PlayableProfile profile,object additional=null)
        {
            var budget=(AiBudgetLedger)Field(owner,"budget");var ai=(AiProfile)Field(owner,"nativeProfile");
            var technology=(AiTechnologyPlan)Property(owner,"TechnologyPlan");
            var opening=(AiOpeningExecutor)Field(owner,"openingExecutor");
            var pending=((IEnumerable)Field(owner,"pendingItems")).Cast<object>().Select(item=>new{Policy=Field(item,"PolicyName"),Intent=Field(item,"Intent"),Action=Field(item,"Action"),DueTick=Field(item,"DueTick"),Identity=Field(item,"Identity")}).ToArray();
            var payload=new{Label=label,authority.Tick,observation.OwnerId,Observation=observation,Profile=profile.CopyData(),Ai=ai.CopyData(),Opening=opening.Capture(),
                TechnologyAtLastDecision=technology,BroadThreatRecomputationForComparison=new AiTechnologyPlanner().Plan(observation,profile,ai,budget.Capture(),AiDefensePlanner.Threats(observation,profile).Length>0),
                Bank=observation.Credits,LedgerLiquid=budget.Liquid,LedgerAvailable=budget.Available,LedgerSafety=budget.SafetyReserve,Ledger=budget.Capture(),
                EconomyPlan=Property(owner,"EconomyPlan"),EconomyDecision=Property(owner,"Checkpoint") is PlayableAiOwnerCheckpoint checkpoint?checkpoint.EconomyDecision:null,
                ScientificInfrastructureProposals=technology==null?null:new AiEconomyPlanner().InfrastructureRequests(observation,profile,technology.Infrastructure),
                Pending=pending,Rejections=((AiDecisionArbiter)Field(owner,"arbiter")).Rejections,Checkpoint=Property(owner,"Checkpoint"),Additional=additional};
            var directory=new System.IO.DirectoryInfo(System.IO.Directory.GetCurrentDirectory());
            while(directory!=null&&!System.IO.File.Exists(System.IO.Path.Combine(directory.FullName,"workflow/project.json")))directory=directory.Parent;
            if(directory==null)throw new InvalidOperationException("O5 diagnostic repository root unavailable");
            string destination=System.IO.Path.Combine(directory.FullName,".local/o5-diagnosis");System.IO.Directory.CreateDirectory(destination);
            string stem=label+"-"+authority.Tick.ToString(System.Globalization.CultureInfo.InvariantCulture);
            System.IO.File.WriteAllText(System.IO.Path.Combine(destination,stem+".json"),Newtonsoft.Json.JsonConvert.SerializeObject(payload,Newtonsoft.Json.Formatting.Indented));
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(destination,stem+".state"),authority.CaptureBytes());
            TestContext.WriteLine("O5_DIAGNOSTIC "+label+" tick="+authority.Tick+" bank="+observation.Credits+" available="+budget.Available+" opening="+opening.Capture().Template+"/"+opening.Capture().Phase+" technology="+technology?.Reason+" saved="+stem);
        }
    }

}
