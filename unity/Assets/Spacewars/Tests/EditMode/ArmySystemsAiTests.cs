using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class ArmySystemsAiTests
    {
        static PlayableAiObservation Observe(int ownCount,bool center=true,bool firstCompleted=false,int allies=0,int queued=0,bool specialized=true)
        {
            var p=PlayableProfile.Default;
            var own=Enumerable.Range(1,ownCount).Select(i=>new PlayableEntitySnapshot(i,PlayableOwner.Player,i==ownCount?PlayableEntityKind.Explorer:PlayableEntityKind.Tank,default,100,false,0,0,0,upgraded:specialized));
            var other=Enumerable.Range(100,allies).Select(i=>new PlayableEntitySnapshot(i,PlayableOwner.Third,PlayableEntityKind.Tank,default,100,false,0,0,0));
            var orders=Enumerable.Range(1,queued).Select(i=>new PlayableProductionOrderSnapshot(i,PlayableEntityKind.Tank,p.TankCreditCost,p.TankPopulationCost,p.TankProductionSeconds,p.TankProductionSeconds,false)).ToArray();
            var buildings=new[]{new PlayableBuildingSnapshot(200,PlayableOwner.Player,PlayableBuildingKind.Factory,default,250,1,queued,0,default,orders:orders)}.Concat(center?new[]{new PlayableBuildingSnapshot(201,PlayableOwner.Player,PlayableBuildingKind.ScientificCenter,default,250,1,0,0,default)}:Array.Empty<PlayableBuildingSnapshot>());
            var research=(specialized?new[]{PlayableResearchKind.TankChassis,PlayableResearchKind.ExplorerAssaultGuns,PlayableResearchKind.ShkvalGuidance}:Array.Empty<PlayableResearchKind>()).Concat(firstCompleted?new[]{PlayableResearchKind.ImprovedWeaponSystems,PlayableResearchKind.ImprovedDefensiveSystems}:Array.Empty<PlayableResearchKind>()).Select((k,i)=>new PlayableResearchOrderSnapshot(i+1,k,201,PlayableResearchRules.Cost(p,k),PlayableResearchRules.Duration(p,k),PlayableResearchRules.Duration(p,k),false,true)).ToArray();
            return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,10000,null,own.Concat(other).ToArray(),buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),null,null,ownerResearch:research,researchAvailability:AiResearchCatalog.All.Select(d=>new PlayableResearchAvailabilitySnapshot(d.Kind,d.Cost(p),!research.Any(r=>r.Kind==d.Kind)&&PlayableResearchRules.Prerequisite(d.Kind,research))).ToArray()));
        }
        static AiTechnologyPlan Plan(PlayableAiObservation o)
        {var budget=new AiBudgetLedger(o.OwnerId,o.Generation);budget.ObserveBank(o.Credits);return new AiTechnologyPlanner().Plan(o,PlayableProfile.Default,AiProfile.Initial,budget.Capture());}
        [TestCase(9,false)][TestCase(10,true)] public void BoundaryCountsLivingOwnIncludingExplorer(int count,bool allowed)
        {
            var o=Observe(count);var plan=Plan(o);
            Assert.AreEqual(count,o.Entities.Count(u=>u.Owner==o.Owner));
            foreach(var branch in plan.Branches.Where(b=>b.Descriptor.Global&&!PlayableResearchRules.Previous(b.Descriptor.Kind).HasValue))Assert.AreEqual(allowed,branch.Utility>0);
            Assert.AreEqual(allowed,plan.Action!=null);if(allowed)Assert.True(PlayableResearchRules.Global(plan.Action.ResearchKind));
        }
        [Test] public void AlliesAndQueuedUnitsDoNotOpenThresholdOrScienceFunding()
        {
            var o=Observe(9,center:false,allies:20,queued:10);var plan=Plan(o);
            Assert.IsNull(plan.Action);Assert.IsNull(plan.FundingGoal);Assert.IsNull(plan.Infrastructure);
            Assert.False(AiTechnologyPlanner.ArmySizeAllows(o,PlayableProfile.Default,PlayableResearchKind.ImprovedWeaponSystems));
        }
        [Test] public void SecondLevelUsesIncrementalGainAndIncludesSpecializedArmy()
        {
            var o=Observe(10,firstCompleted:true);var plan=Plan(o);Assert.NotNull(plan.Action);Assert.True(PlayableResearchRules.Previous(plan.Action.ResearchKind).HasValue);
            foreach(var b in plan.Branches.Where(b=>b.Descriptor.Global&&PlayableResearchRules.Previous(b.Descriptor.Kind).HasValue)){
                Assert.Greater(b.AffectedValue,0,"specialized upgrades do not remove globally affected units");
                Assert.AreEqual(1.30/1.15-1,b.Descriptor.RelativeGain(PlayableProfile.Default),1e-12);
                Assert.Greater(b.Utility,0);
            }
        }
        [Test] public void OldTechnologiesRemainAvailableBelowThreshold()
        {
            var plan=Plan(Observe(1,specialized:false));Assert.NotNull(plan.Action);Assert.False(PlayableResearchRules.Global(plan.Action.ResearchKind));
        }
        [Test] public void CurrentObservationRechecksThresholdAndMetadataCanTuneIt()
        {
            var p=PlayableProfile.Default;var action=Plan(Observe(10)).Action;Assert.NotNull(action);
            Assert.False(AiTechnologyPlanner.ArmySizeAllows(Observe(9),p,action.ResearchKind));
            var data=p.CopyData();data.armySystemsMinimumUnits=9;Assert.True(AiTechnologyPlanner.ArmySizeAllows(Observe(9),PlayableProfile.Create(data),action.ResearchKind));
        }
    }
}
