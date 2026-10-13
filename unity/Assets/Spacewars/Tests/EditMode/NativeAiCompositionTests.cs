using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.IO;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    // O4 descriptor, phase, composition and ordinary authority integration contracts.
    public sealed class NativeAiCompositionTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        private static PlayableAiObservation Observation(PlayableProfile p,int credits=10000,int free=100,PlayableProductionOrderSnapshot[] orders=null,
            PlayableResearchOrderSnapshot[] research=null,PlayableBuildingKind producer=PlayableBuildingKind.Factory,bool selling=false,
            long tick=45,double income=0,PlayableEntitySnapshot[] units=null,bool factory=true)
        {
            var lifecycle=selling?new PlayableBuildingLifecycleSnapshot(true,0,false,false,0,0,null,null,1,0,false):null;
            var own=new PlayableBuildingSnapshot(4,PlayableOwner.Player,producer,default(NavPoint),100,1,orders?.Length??0,0,default(NavPoint),orders:orders,lifecycle:lifecycle);
            return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,41,1,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,credits,null,
                units??Array.Empty<PlayableEntitySnapshot>(),factory?new[]{own}:Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,
                population:new PlayablePopulationSnapshot(0,0,free),ownerResearch:research,incomePerSecond:income));
        }
        private static AiRosterCatalog RemapLine(PlayableEntityKind kind)
        {
            var data=AiRosterCatalog.Initial.CopyData();
            foreach(var d in data){d.reconWeight=0;d.lineWeight=d.kind==kind?1:0;d.supportWeight=d.kind==kind?0:1;}
            return new AiRosterCatalog(data);
        }
        [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Explorer)][TestCase(PlayableEntityKind.Shkval)]
        public void CatalogRoleRemapUsesRealGenericProductionAndGameplayTerms(PlayableEntityKind kind)
        {
            var config=OfflineParticipantAuthorityTests.Config(3,false,seed:41);
            var authority=new PlayableAuthorityTick(config,71);
            var domain=typeof(PlayableAuthorityTick).GetField("domain",F).GetValue(authority);
            object Call(string method,params object[] args)=>domain.GetType().GetMethod(method,F).Invoke(domain,args);
            var p=config.Profile;var ownerId=config.Roster[0].Id;var owner=(PlayableOwner)Call("OwnerFor",ownerId);
            Call("AddCredits",owner,10000d);
            PlayableSnapshot Snapshot()=>(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,config.Seed,owner);
            var home=Snapshot().Sites.First(s=>s.Owner==owner&&s.Site.Kind==PlayableBuildingKind.Headquarters);
            Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(71,1,ownerId,PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:home.Site.Id,slotId:home.Site.Slots[0].Id,parentId:home.CenterId,buildingKind:PlayableBuildingKind.Factory)).Status);
            Call("AdvanceFoundations");Call("AdvanceBuildings",p.FactoryBuildSeconds+1);
            var o=PlayableAiObservation.From(Snapshot());var catalog=RemapLine(kind);
            // The caller supplies its role demand; the adapter has no strategy/kind branches.
            var selected=catalog.CopyData().Single(d=>d.lineWeight>0);
            var planner=new AiEconomyPlanner(catalog);var demand=new AiProductionDemand();demand.Observe(o,AiProfile.Initial);
            var phasePlanner=new AiPhaseCompositionPlanner(ownerId,71,catalog);phasePlanner.Observe(o,p,AiProfile.Initial);
            var composition=phasePlanner.Assess(o,p,AiProfile.Initial,reconCap:0);
            var candidate=composition.Candidates.Single(c=>c.Policy=="production"&&c.Legal);
            var plan=planner.Plan(o,p,demand,AiProfile.Initial,productionKind:candidate.Action.UnitKind);
            Assert.AreEqual(selected.productionCommand,candidate.Action.Kind);Assert.AreEqual(kind,candidate.Action.UnitKind);
            Assert.AreEqual(PlayableUnitRules.Cost(p,kind)/PlayableUnitRules.Duration(p,kind),plan.Capacity.SustainedSpendPerSecond);
            var policy=new PlayableAiProductionLivenessPolicy(profile:p,ownerId:ownerId,catalog:catalog);
            var action=(PlayableAiAction)typeof(PlayableAiProductionLivenessPolicy).GetMethod("Admit",F).Invoke(policy,new object[]{o,candidate.Action});
            Assert.True(policy.HasPendingObligation);Assert.IsNull(policy.TryPlan(o,kind));
            int before=o.Credits;var receipt=authority.Apply(new PlayableCommand(71,2,ownerId,action.Kind,action.CopyEntityIds(),unitKind:action.UnitKind,origin:PlayableOrderOrigin.Ai,source:action.SourceIdentity,jobId:1,actionId:action.ActionId));
            Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status);
            var paid=Snapshot();var waiting=paid.Buildings.Single(b=>b.Id==action.EntityIds.Single()).PrivateState.Orders.Single();
            Assert.False(waiting.Active,"Enqueue pays before ordinary production activation.");
            Assert.AreEqual(kind,waiting.Kind);Assert.AreEqual(PlayableUnitRules.Cost(p,kind),waiting.PaidCost);
            Assert.AreEqual(before-waiting.PaidCost,paid.Credits);Assert.AreEqual(o.Population.Reserved,paid.Population.Reserved);
            Call("AdvanceProduction",0d); // Execute ordinary head admission; retain gameplay duration and all suffix contracts.
            var after=Snapshot();var queue=after.Buildings.Single(b=>b.Id==action.EntityIds.Single()).PrivateState.Orders.Single();
            Assert.AreEqual(kind,queue.Kind);Assert.True(queue.Active);Assert.AreEqual(PlayableUnitRules.Cost(p,kind),queue.PaidCost);
            Assert.AreEqual(PlayableUnitRules.Population(p,kind),queue.PopulationCost);Assert.AreEqual(PlayableUnitRules.Duration(p,kind),queue.Duration);
            Assert.AreEqual(before-queue.PaidCost,after.Credits);
            Assert.AreEqual(o.Population.Reserved+queue.PopulationCost,after.Population.Reserved);
            policy.ObserveReceipt(new PlayableAiTraceRecord(o.Identity,action.ActionId,0,2,0,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"applied",ownerId:ownerId,sourceIdentity:action.SourceIdentity));
            Assert.False(policy.HasPendingObligation);
        }
        [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Explorer)][TestCase(PlayableEntityKind.Shkval)]
        public void GenericAdmissionEnforcesGameplayCreditsPopulationAndProducer(PlayableEntityKind kind)
        {
            var p=PlayableProfile.Default;var catalog=RemapLine(kind);var descriptor=catalog.For(kind);
            PlayableAiAction Action()=>new PlayableAiAction(1,"player-1",p.ProfileId,p.Revision,71,1,descriptor.productionCommand,new[]{4},unitKind:kind);
            Assert.AreEqual("insufficient credits",AiEconomyAdmission.Reject(Observation(p,credits:PlayableUnitRules.Cost(p,kind)-1),p,Action(),true,catalog));
            Assert.AreEqual("population capacity",AiEconomyAdmission.Reject(Observation(p,free:PlayableUnitRules.Population(p,kind)-1),p,Action(),true,catalog));
            Assert.AreEqual("ready live producer prerequisite",AiEconomyAdmission.Reject(Observation(p,producer:PlayableBuildingKind.ScientificCenter),p,Action(),true,catalog));
            Assert.AreEqual("ready live producer prerequisite",AiEconomyAdmission.Reject(Observation(p,selling:true),p,Action(),true,catalog));
            var busy=new[]{new PlayableProductionOrderSnapshot(1,kind,PlayableUnitRules.Cost(p,kind),PlayableUnitRules.Population(p,kind),PlayableUnitRules.Duration(p,kind),1,true)};
            Assert.AreEqual("producer queue busy",AiEconomyAdmission.Reject(Observation(p,orders:busy),p,Action(),true,catalog));
            Assert.IsNull(AiEconomyAdmission.Reject(Observation(p,credits:PlayableUnitRules.Cost(p,kind),free:PlayableUnitRules.Population(p,kind)),p,Action(),true,catalog));
        }
        [TestCase(false,false)][TestCase(true,false)][TestCase(false,true)]
        public void UnlockRequiresCompletedOwnResearchNotAvailabilityOrQueuedResearch(bool active,bool complete)
        {
            var p=PlayableProfile.Default;var data=AiRosterCatalog.Initial.CopyData();data.Single(d=>d.kind==PlayableEntityKind.Shkval).unlockDependencies=new[]{PlayableResearchKind.ShkvalGuidance};
            var catalog=new AiRosterCatalog(data);var research=new[]{new PlayableResearchOrderSnapshot(1,PlayableResearchKind.ShkvalGuidance,9,p.ShkvalGuidanceCost,complete?p.ShkvalGuidanceSeconds:0,p.ShkvalGuidanceSeconds,active,complete)};
            var o=Observation(p,research:research);var planner=new AiEconomyPlanner(catalog);var demand=new AiProductionDemand();demand.Observe(o,AiProfile.Initial);
            var plan=planner.Plan(o,p,demand,AiProfile.Initial,productionKind:PlayableEntityKind.Shkval);var candidate=plan.Candidates.Single(c=>c.Policy=="production");
            Assert.AreEqual(complete,candidate.Legal);Assert.AreEqual(complete?null:"production unlock prerequisite",candidate.Reason);
            Assert.AreEqual(complete?p.ShkvalCreditCost/p.ShkvalProductionDurationSec:0,plan.Capacity.SustainedSpendPerSecond);
            var policy=new PlayableAiProductionLivenessPolicy(profile:p,catalog:catalog);
            Assert.AreEqual(complete,policy.TryPlan(o,PlayableEntityKind.Shkval)!=null);
            research[0]=new PlayableResearchOrderSnapshot(2,PlayableResearchKind.ShkvalGuidance,9,0,0,0,false,!complete);
            Assert.AreEqual(complete,o.OwnerResearch.Single().Complete,"Detached projection must not alias the caller array.");
        }
        [Test] public void PaidQueueCapacityKeepsOriginalTermsWhileIdleLineUsesCurrentGameplay()
        {
            var p=PlayableProfile.Default;var nextData=p.CopyData();nextData.shkvalCreditCost=p.ShkvalCreditCost+10;var next=PlayableProfile.Create(nextData);
            var paid=new[]{new PlayableProductionOrderSnapshot(1,PlayableEntityKind.Shkval,p.ShkvalCreditCost,p.ShkvalPopulationCost,p.ShkvalProductionDurationSec,1,true)};
            var capacity=new AiProductionDemand().Assess(Observation(next,orders:paid),next,AiProfile.Initial,10000,AiRosterCatalog.Initial,PlayableEntityKind.Shkval);
            Assert.AreEqual(p.ShkvalCreditCost/p.ShkvalProductionDurationSec,capacity.SustainedSpendPerSecond);
            Assert.AreEqual(next.FactoryCreditCost+AiProfile.Initial.Value("economy.factoryLaunchCycles")*next.TankCreditCost,capacity.StartupFund,"Existing startup commitment remains Tank-funded.");
            Assert.AreEqual(next.ShkvalCreditCost/next.ShkvalProductionDurationSec,new AiProductionDemand().Assess(Observation(next),next,AiProfile.Initial,10000,AiRosterCatalog.Initial,PlayableEntityKind.Shkval).SustainedSpendPerSecond);
        }
        [Test] public void DocumentedExclusionCannotEnterProductionThroughExistingAdapter()
        {
            var p=PlayableProfile.Default;var data=AiRosterCatalog.Initial.CopyData();data.Single(d=>d.kind==PlayableEntityKind.Shkval).exclusionReason="Explicit diagnostic exclusion";
            var catalog=new AiRosterCatalog(data);var o=Observation(p);var plan=new AiEconomyPlanner(catalog).Plan(o,p,productionKind:PlayableEntityKind.Shkval);
            Assert.AreEqual("unsupported production descriptor",plan.Candidates.Single(c=>c.Policy=="production").Reason);
            Assert.IsNull(new PlayableAiProductionLivenessPolicy(profile:p,catalog:catalog).TryPlan(o,PlayableEntityKind.Shkval));
        }

        private static PlayableEntitySnapshot[] Cover(PlayableProfile p,int count=3)=>Enumerable.Range(0,count).Select(i=>new PlayableEntitySnapshot(10+i,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(i,0),p.TankHealth,false,0,0,0)).ToArray();
        private static AiProfile Profile(params (string path,double value)[] edits)
        {var d=AiProfile.Initial.CopyData();d.revision++;foreach(var e in edits)d.fields.Single(f=>f.path==e.path).value=e.value;return new AiProfile(d);}
        [Test] public void PhasesRequireResidenceAndLateConjunctionAndNeverRegress()
        {
            var p=PlayableProfile.Default;var ai=AiProfile.Initial;var c=new AiPhaseCompositionPlanner("player-1",71);
            c.Observe(Observation(p,tick:0,income:4),p,ai);
            c.Observe(Observation(p,tick:899,income:8),p,ai);Assert.AreEqual(AiGamePhase.Early,c.Capture().Phase);
            c.Observe(Observation(p,tick:900,income:8),p,ai);Assert.AreEqual(AiGamePhase.Mid,c.Capture().Phase);Assert.AreEqual(900,c.Capture().EnteredTick);
            var research=new[]{new PlayableResearchOrderSnapshot(1,PlayableResearchKind.TankChassis,1,300,30,30,false,true)};
            c.Observe(Observation(p,tick:1799,income:16,units:Cover(p),research:research),p,ai);Assert.AreEqual(AiGamePhase.Mid,c.Capture().Phase);
            c.Observe(Observation(p,tick:1800,income:16,units:Cover(p)),p,ai);Assert.AreEqual(AiGamePhase.Mid,c.Capture().Phase,"Completed research required for income route.");
            c.Observe(Observation(p,tick:1801,income:16,research:research),p,ai);Assert.AreEqual(AiGamePhase.Mid,c.Capture().Phase,"Viable living combat cover required for income route.");
            c.Observe(Observation(p,tick:1802,income:16,units:Cover(p),research:research),p,ai);Assert.AreEqual(AiGamePhase.Late,c.Capture().Phase);
            c.Observe(Observation(p,tick:1803,income:0),p,ai);Assert.AreEqual(AiGamePhase.Late,c.Capture().Phase);Assert.AreEqual(4,c.Capture().StartingIncome);
        }
        [TestCase(0d)][TestCase(4d)] public void PoorIncomeUsesAbsoluteFallbackAndEachPhaseResidence(double income)
        {
            var p=PlayableProfile.Default;var ai=AiProfile.Initial;var c=new AiPhaseCompositionPlanner("player-1",71);
            c.Observe(Observation(p,tick:0,income:income),p,ai);c.Observe(Observation(p,tick:7199,income:income),p,ai);Assert.AreEqual(AiGamePhase.Early,c.Capture().Phase);
            c.Observe(Observation(p,tick:7200,income:income),p,ai);Assert.AreEqual(AiGamePhase.Mid,c.Capture().Phase);
            c.Observe(Observation(p,tick:17999,income:income),p,ai);Assert.AreEqual(AiGamePhase.Mid,c.Capture().Phase);
            c.Observe(Observation(p,tick:18000,income:income),p,ai);Assert.AreEqual(AiGamePhase.Late,c.Capture().Phase);
            var lateStart=new AiPhaseCompositionPlanner("player-1",71);lateStart.Observe(Observation(p,tick:18000),p,ai);Assert.AreEqual(AiGamePhase.Mid,lateStart.Capture().Phase);
            lateStart.Observe(Observation(p,tick:18899),p,ai);Assert.AreEqual(AiGamePhase.Mid,lateStart.Capture().Phase);lateStart.Observe(Observation(p,tick:18900),p,ai);Assert.AreEqual(AiGamePhase.Late,lateStart.Capture().Phase);
        }
        [Test] public void ZeroStartingIncomeNeverInventsRatioFloor()
        {
            var p=PlayableProfile.Default;var c=new AiPhaseCompositionPlanner("player-1",71);c.Observe(Observation(p,tick:0),p,AiProfile.Initial);c.Observe(Observation(p,tick:900,income:1000),p,AiProfile.Initial);
            Assert.AreEqual(AiGamePhase.Early,c.Capture().Phase);Assert.Zero(c.Capture().StartingIncome);
        }
        [Test] public void SupportMinimumNeedsLivingCoverFundsPopulationAndProducerAndCountsPaidWaiting()
        {
            var p=PlayableProfile.Default;var ai=AiProfile.Initial;var c=new AiPhaseCompositionPlanner("player-1",71);c.Observe(Observation(p,tick:0,income:4),p,ai);c.Observe(Observation(p,tick:900,income:8),p,ai);
            var selected=c.Assess(Observation(p,tick:900,units:Cover(p)),p,ai,reconCap:0);Assert.AreEqual("minimum support deficit",selected.Reason);Assert.AreEqual(PlayableEntityKind.Shkval,selected.Candidates.Single().Action.UnitKind);
            Assert.False(c.Assess(Observation(p,tick:900),p,ai,reconCap:0).Candidates.Any(a=>a.Action.UnitKind==PlayableEntityKind.Shkval));
            Assert.False(c.Assess(Observation(p,tick:900,units:Cover(p),credits:p.ShkvalCreditCost-1),p,ai,reconCap:0).Candidates.Any(a=>a.Action.UnitKind==PlayableEntityKind.Shkval));
            Assert.False(c.Assess(Observation(p,tick:900,units:Cover(p),free:p.ShkvalPopulationCost-1),p,ai,reconCap:0).Candidates.Any(a=>a.Action.UnitKind==PlayableEntityKind.Shkval));
            Assert.IsEmpty(c.Assess(Observation(p,tick:900,units:Cover(p),factory:false),p,ai).Candidates);Assert.IsEmpty(c.Assess(Observation(p,tick:900,units:Cover(p),selling:true),p,ai).Candidates);
            var waiting=new[]{new PlayableProductionOrderSnapshot(1,PlayableEntityKind.Shkval,71,p.ShkvalPopulationCost,77,77,false)};
            var paid=c.Assess(Observation(p,tick:900,units:Cover(p),orders:waiting),p,ai);Assert.AreEqual(1,paid.SupportCount);Assert.AreEqual(71,paid.CommittedRoleCredits[2]);Assert.IsEmpty(paid.Candidates);
            Assert.Throws<ArgumentException>(()=>c.Assess(PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,72,41,1,45,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null)),p,ai));
        }
        [Test] public void CreditSharesRetainPaidTermsAndCountAcceptedUnpaidOnceNotPaidHistory()
        {
            var p=PlayableProfile.Default;var c=new AiPhaseCompositionPlanner("player-1",71);var o=Observation(p,units:Cover(p));var a=new PlayableAiAction(1,"player-1",p.ProfileId,p.Revision,71,1,PlayableCommandKind.QueueTank,new[]{4},unitKind:PlayableEntityKind.Tank);
            var identity=new AiReceiptIdentity(71,"player-1",1,1);var e=new AiBudgetEntry("accepted","production",a,17,0,0,90,"terminal",Array.Empty<string>(),"paid terms",identity);
            var state=new AiBudgetState("player-1",71,10000,0,99,Array.Empty<AiBudgetEntry>(),new[]{e},new[]{e});
            var assessment=c.Assess(o,p,AiProfile.Initial,state,reconCap:0);Assert.AreEqual(3*p.TankCreditCost+17,assessment.CommittedRoleCredits[1]);Assert.Zero(assessment.CommittedRoleCredits[2]);
            var nextData=p.CopyData();nextData.shkvalCreditCost+=10;var next=PlayableProfile.Create(nextData);
            var queue=new[]{new PlayableProductionOrderSnapshot(1,PlayableEntityKind.Shkval,123,3,22,22,false)};var nextAssessment=c.Assess(Observation(next,orders:queue),next,AiProfile.Initial);Assert.AreEqual(123,nextAssessment.CommittedRoleCredits[2]);
        }
        [Test] public void VisibleCounterMovesAtMostTotalApprovedMassAndIncompatibleTargetDoesNotAttractBudget()
        {
            var p=PlayableProfile.Default;var c=new AiPhaseCompositionPlanner("player-1",71);var ai=AiProfile.Initial;var own=Cover(p);
            var ordinary=c.Assess(Observation(p,units:own),p,ai,reconCap:1);var enemy=new PlayableEntitySnapshot(50,PlayableOwner.Enemy,PlayableEntityKind.Tank,default(NavPoint),p.TankHealth,false,0,0,0);
            var response=c.Assess(Observation(p,units:own.Concat(new[]{enemy}).ToArray()),p,ai,reconCap:1);
            double moved=Enumerable.Range(0,3).Sum(i=>Math.Max(0,response.BudgetShares[i]-ordinary.BudgetShares[i]));Assert.Greater(moved,0);Assert.LessOrEqual(moved,.15+1e-12);Assert.AreEqual(1,response.BudgetShares.Sum(),1e-12);
            var capped=Profile(("composition.counterMaximumBudgetTransfer",.05));var limited=c.Assess(Observation(p,units:own.Concat(new[]{enemy}).ToArray()),p,capped,reconCap:1);Assert.LessOrEqual(Enumerable.Range(0,3).Sum(i=>Math.Max(0,limited.BudgetShares[i]-ordinary.BudgetShares[i])),.05+1e-12);
            var data=AiRosterCatalog.Initial.CopyData();data.Single(d=>d.kind==PlayableEntityKind.Shkval).targetTags=new[]{"building"};var restricted=new AiPhaseCompositionPlanner("player-1",71,new AiRosterCatalog(data));
            var baseRestricted=restricted.Assess(Observation(p,units:own),p,ai);var ground=restricted.Assess(Observation(p,units:own.Concat(new[]{enemy}).ToArray()),p,ai);Assert.Less(ground.BudgetShares[2],baseRestricted.BudgetShares[2]);
        }
        [Test] public void HiddenWorldCounterfactualLeavesCompositionUnchanged()
        {
            var config=OfflineParticipantAuthorityTests.Config(3,false,seed:41);var a=new PlayableAuthorityTick(config,71);var domain=typeof(PlayableAuthorityTick).GetField("domain",F).GetValue(a);
            object Call(string method,params object[] args)=>domain.GetType().GetMethod(method,F).Invoke(domain,args);
            var id=config.Roster[0].Id;var owner=(PlayableOwner)Call("OwnerFor",id);PlayableAiObservation View()=>PlayableAiObservation.From((PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,config.Seed,owner));
            var p=config.Profile;var c=new AiPhaseCompositionPlanner(id,71);var before=c.Assess(View(),p,AiProfile.Initial);var enemy=(PlayableOwner)Call("OwnerFor",config.Roster[1].Id);var pos=config.Starts[config.Assignments[1]].Position;
            var all=((PlayableSnapshot)Call("Snapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,config.Seed)).Entities;
            double radius=PlayableUnitRules.Radius(p,PlayableEntityKind.Shkval);
            var place=new[]{new NavPoint(pos.X+6,pos.Z-6),new NavPoint(pos.X-6,pos.Z+6),new NavPoint(pos.X+6,pos.Z+6),new NavPoint(pos.X-6,pos.Z-6)}.First(point=>!View().Vision.IsVisible(point)&&a.NavigationGeometry.IsFree(point,radius)&&all.All(u=>(u.Position.X-point.X)*(u.Position.X-point.X)+(u.Position.Z-point.Z)*(u.Position.Z-point.Z)>Math.Pow(radius+PlayableUnitRules.Radius(p,u.Kind),2)));
            var hidden=(int)Call("SpawnUnit",place,enemy,PlayableEntityKind.Shkval);Assert.False(View().Entities.Any(u=>u.Id==hidden));Call("AddCredits",enemy,10000d);
            var after=c.Assess(View(),p,AiProfile.Initial);CollectionAssert.AreEqual(before.BudgetShares,after.BudgetShares);CollectionAssert.AreEqual(before.CommittedRoleCredits,after.CommittedRoleCredits);CollectionAssert.AreEqual(before.Candidates.Select(x=>x.Action.Kind),after.Candidates.Select(x=>x.Action.Kind));Assert.AreEqual(before.Reason,after.Reason);
        }
        [Test] public void UnavailableRoleHasCheapestLegalCombatFallbackOrExplicitBlocker()
        {
            var p=PlayableProfile.Default;var data=AiRosterCatalog.Initial.CopyData();foreach(var d in data){d.reconWeight=0;d.lineWeight=1;d.supportWeight=0;}var c=new AiPhaseCompositionPlanner("player-1",71,new AiRosterCatalog(data));
            var ai=Profile(("composition.early.reconBudgetShare",1),("composition.early.lineBudgetShare",0),("composition.early.supportBudgetShare",0));var chosen=c.Assess(Observation(p),p,ai,reconCap:0);Assert.AreEqual(PlayableEntityKind.Explorer,chosen.Candidates.Single().Action.UnitKind);
            var blocked=c.Assess(Observation(p,credits:0),p,ai);Assert.IsEmpty(blocked.Candidates);StringAssert.Contains("no affordable legal combat",blocked.Reason);
            Assert.Throws<ArgumentException>(()=>new AiRosterCatalog(AiRosterCatalog.Initial.CopyData().Where(d=>d.kind!=PlayableEntityKind.Shkval)));
        }
        [Test] public void StartupTankObligationsRemainAvailableDespiteOtherCompositionAndReservedFunds()
        {
            var p=PlayableProfile.Default;var c=new AiPhaseCompositionPlanner("player-1",71);var o=Observation(p,credits:p.TankCreditCost);var a=new PlayableAiAction(1,"player-1",p.ProfileId,p.Revision,71,1,PlayableCommandKind.QueueTank,new[]{4},unitKind:PlayableEntityKind.Tank);
            var e=new AiBudgetEntry("factory-startup:1:1","factory-startup",a,p.TankCreditCost,1,0,90,"cycle",Array.Empty<string>(),"terms");var state=new AiBudgetState("player-1",71,p.TankCreditCost,0,0,new[]{e},Array.Empty<AiBudgetEntry>(),Array.Empty<AiBudgetEntry>());
            var chosen=c.Assess(o,p,AiProfile.Initial,state,reconCap:0,startupProducers:new[]{4});Assert.AreEqual(PlayableEntityKind.Tank,chosen.Candidates.Single().Action.UnitKind);Assert.AreEqual(1,chosen.Candidates.Single().Priority);
        }
        [Test] public void RealOwnerPhaseChangesPaidProductionAndPreservesPendingReplayAndProfileApply()
        {
            var original=(OfflineMatchConfiguration)typeof(NativeAiOpeningTests).GetMethod("Config",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{19092026});
            var ai=Profile(("composition.early.reconBudgetShare",0),("composition.early.lineBudgetShare",1),("composition.early.supportBudgetShare",0),("composition.mid.reconBudgetShare",0),("composition.mid.lineBudgetShare",0),("composition.mid.supportBudgetShare",1));
            var a=new PlayableAuthorityTick(original,71,aiProfile:ai);var domain=typeof(PlayableAuthorityTick).GetField("domain",F).GetValue(a);
            object Call(string method,params object[] args)=>domain.GetType().GetMethod(method,F).Invoke(domain,args);
            object Owner(PlayableAuthorityTick t)=>((IEnumerable)t.GetType().GetField("scheduler",F).GetValue(t).GetType().GetField("owners",F).GetValue(t.GetType().GetField("scheduler",F).GetValue(t))).Cast<object>().Single();
            var composition=(AiPhaseCompositionPlanner)Owner(a).GetType().GetField("composition",F).GetValue(Owner(a));var id=original.Roster[0].Id;var own=(PlayableOwner)Call("OwnerFor",id);
            composition.Observe(PlayableAiObservation.From(a.ParticipantView(id)),original.Profile,ai);var home=a.ParticipantView(id).Sites.Single(s=>s.Owner==own&&s.Site.Kind==PlayableBuildingKind.Headquarters);
            for(int i=0;i<3;i++){Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,i+1,id,PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:home.Site.Id,slotId:home.Site.Slots[i].Id,parentId:home.CenterId,buildingKind:i==0?PlayableBuildingKind.Factory:PlayableBuildingKind.Refinery)).Status);}
            Call("AdvanceFoundations");Call("AdvanceBuildings",original.Profile.FactoryBuildSeconds+original.Profile.RefineryBuildSeconds);for(int i=0;i<3;i++)Call("SpawnUnit",new NavPoint(home.Site.Position.X+i*2,home.Site.Position.Z+4),own,PlayableEntityKind.Tank);
            void Step(PlayableAuthorityTick t){while(t.Requests.TryDequeue(out var request))Assert.True(t.Answers.TryEnqueue(new NavigationAnswer(request,Array.Empty<NavPoint>())));if(!t.TryAdvance()){while(t.Requests.TryDequeue(out var request))Assert.True(t.Answers.TryEnqueue(new NavigationAnswer(request,Array.Empty<NavPoint>())));Assert.True(t.TryAdvance());}}
            for(int i=0;i<899;i++)Step(a);Assert.AreEqual(AiGamePhase.Early,composition.Capture().Phase);
            var ledger=(AiBudgetLedger)Owner(a).GetType().GetField("budget",F).GetValue(Owner(a));Assert.False(ledger.Capture().Paid.Any(e=>e.Action.Kind==PlayableCommandKind.QueueShkval));
            for(int i=0;i<6;i++)Step(a);Assert.AreEqual(AiGamePhase.Mid,composition.Capture().Phase);var bytes=a.CaptureBytes();var b=PlayableAuthorityTick.RestoreBytes(bytes,original,ai);CollectionAssert.AreEqual(bytes,b.CaptureBytes());
            for(int i=0;i<90;i++){Step(a);Step(b);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes(),"phase composition suffix "+i);}
            // Phase changes never cancel/reprice a paid head. Let its actual retained cycle
            // finish plus the existing decision/reaction cadence before demanding new spend.
            long cycle=AiProfile.SecondsToTicks(original.Profile.TankProductionSeconds+ai.DifficultyValue(AiDifficulty.Fighter,"decisionSeconds")+ai.DifficultyValue(AiDifficulty.Fighter,"reactionSeconds"),30);
            for(int i=0;i<cycle;i++){Step(a);Step(b);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes(),"paid-head composition suffix "+i);}
            Assert.True(ledger.Capture().Paid.Any(e=>e.Action.Kind==PlayableCommandKind.QueueShkval&&e.Amount==original.Profile.ShkvalCreditCost));
            Assert.True(a.ParticipantView(id).Buildings.Any(q=>q.PrivateState!=null&&q.PrivateState.Orders.Any(order=>order.Kind==PlayableEntityKind.Shkval&&order.PaidCost==original.Profile.ShkvalCreditCost))||a.ParticipantView(id).Entities.Any(u=>u.Owner==own&&u.Kind==PlayableEntityKind.Shkval));
            var before=composition.Capture();var updated=ai.CopyData();updated.revision++;updated.fields.Single(f=>f.path=="composition.mid.minimumSupport").value=2;Assert.IsNull(a.ApplyProfile(original.Profile,new AiProfile(updated)));Assert.AreEqual(before.StartingIncome,composition.Capture().StartingIncome);Assert.AreEqual(before.EnteredTick,composition.Capture().EnteredTick);Assert.AreEqual(before.Phase,composition.Capture().Phase);
        }
        [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Explorer)][TestCase(PlayableEntityKind.Shkval)]
        public void RegisteredCommandKindOwnsDiagnosticCostAndPopulationEvenIfUnitArgumentDisagrees(PlayableEntityKind kind)
        {
            var p=PlayableProfile.Default;var command=PlayableUnitRules.ProductionCommand(kind);Assert.AreEqual(kind,PlayableUnitRules.ProductionKind(command));Assert.True(AiRosterCatalog.IsProduction(command));
            Assert.False(AiRosterCatalog.IsProduction(PlayableCommandKind.BuildAt));Assert.IsNull(PlayableUnitRules.ProductionKind(PlayableCommandKind.Move));
            var wrong=kind==PlayableEntityKind.Tank?PlayableEntityKind.Shkval:PlayableEntityKind.Tank;
            var action=new PlayableAiAction(1,"player-1",p.ProfileId,p.Revision,71,1,command,new[]{4},unitKind:wrong);
            var candidate=new AiEconomyCandidate("production",action,null);
            var diagnostic=(AiEconomyCandidateDecision)Activator.CreateInstance(typeof(AiEconomyCandidateDecision),F,null,new object[]{candidate,p,Array.Empty<AiIntent>(),new System.Collections.Generic.Dictionary<string,string>()},null);
            Assert.AreEqual(kind,diagnostic.UnitKind);Assert.AreEqual(PlayableUnitRules.Cost(p,kind),diagnostic.Credits);Assert.AreEqual(PlayableUnitRules.Population(p,kind),diagnostic.Population);
            var data=AiRosterCatalog.Initial.CopyData();data.Single(d=>d.kind==kind).exclusionReason="Explicit exclusion";data.Single(d=>d.kind==kind).productionCommand=PlayableCommandKind.BuildAt;var excluded=new AiRosterCatalog(data);
            Assert.IsNull(excluded.Production(command));Assert.IsNull(excluded.Production(PlayableCommandKind.BuildAt));Assert.AreEqual("unsupported production descriptor",AiEconomyAdmission.Reject(Observation(p),p,action,true,excluded));
        }
    }
}
