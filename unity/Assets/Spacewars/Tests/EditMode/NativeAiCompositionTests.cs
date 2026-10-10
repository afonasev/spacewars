using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    // O4 independent descriptor prerequisite. Phase/composition qualification remains pending.
    public sealed class NativeAiCompositionTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        private static PlayableAiObservation Observation(PlayableProfile p,int credits=10000,int free=100,PlayableProductionOrderSnapshot[] orders=null,
            PlayableResearchOrderSnapshot[] research=null,PlayableBuildingKind producer=PlayableBuildingKind.Factory,bool selling=false)
        {
            var lifecycle=selling?new PlayableBuildingLifecycleSnapshot(true,0,false,false,0,0,null,null,1,0,false):null;
            var own=new PlayableBuildingSnapshot(4,PlayableOwner.Player,producer,default(NavPoint),100,1,orders?.Length??0,0,default(NavPoint),orders:orders,lifecycle:lifecycle);
            return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,41,1,45,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,credits,null,
                Array.Empty<PlayableEntitySnapshot>(),new[]{own},Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,
                population:new PlayablePopulationSnapshot(0,0,free),ownerResearch:research));
        }
        private static AiRosterCatalog RemapLine(PlayableEntityKind kind)
        {
            var data=AiRosterCatalog.Initial.CopyData();
            foreach(var d in data){d.lineWeight=d.kind==kind?1:0;d.supportWeight=1;}
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
            var plan=planner.Plan(o,p,demand,AiProfile.Initial,productionKind:selected.kind);
            var candidate=plan.Candidates.Single(c=>c.Policy=="production"&&c.Legal);
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
    }
}
