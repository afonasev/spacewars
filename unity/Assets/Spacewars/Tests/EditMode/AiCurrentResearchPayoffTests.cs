using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class AiCurrentResearchPayoffTests
    {
        private static PlayableAiObservation View(long tick=0,int cash=10000,bool factory=true,int center=0,bool guidance=false,int? count=null)
        {
            var p=PlayableProfile.Default;var site=TerritoryRules.Sites(p).First(s=>s.Kind==PlayableBuildingKind.Headquarters);
            var bs=new[]{new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Headquarters,site.Position,p.HeadquartersHealth,1,0,0,default,siteId:site.Id)};
            if(factory)bs=bs.Concat(new[]{new PlayableBuildingSnapshot(2,PlayableOwner.Player,PlayableBuildingKind.Factory,site.Slots[0].Position,p.FactoryHealth,1,0,0,default,siteId:site.Id,slotId:site.Slots[0].Id,parentId:1)}).ToArray();
            if(center>0)bs=bs.Concat(new[]{new PlayableBuildingSnapshot(3,PlayableOwner.Player,PlayableBuildingKind.ScientificCenter,site.Slots[1].Position,(int)p.ScienceHealth,center==1?.5:1,0,0,default,siteId:site.Id,slotId:site.Slots[1].Id,parentId:1,phase:center==1?ConstructionPhase.Constructing:ConstructionPhase.Ready)}).ToArray();
            var units=Enumerable.Range(10,count??(int)AiProfile.Initial.Value("technology.minimumUnitsForScienceCenter")).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(id,0),p.TankHealth,false,0,0,0,upgraded:true)).ToArray();
            var research=guidance?new[]{new PlayableResearchOrderSnapshot(1,PlayableResearchKind.ShkvalGuidance,99,p.ShkvalGuidanceCost,p.ShkvalGuidanceSeconds,p.ShkvalGuidanceSeconds,false,true)}:Array.Empty<PlayableResearchOrderSnapshot>();
            return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,41,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,cash,null,units,bs,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,
                sites:new[]{new TerritorySiteSnapshot(site,PlayableOwner.Player,PlayableOwner.Player,1,false,1,true)},population:new PlayablePopulationSnapshot(units.Length*p.TankPopulationCost,0,p.ArmyCapacity),ownerId:"owner",ownerResearch:research,researchAvailability:Array.Empty<PlayableResearchAvailabilitySnapshot>(),activeProfile:p,homeSiteId:site.Id));
        }
        private static AiBudgetLedger Budget(PlayableAiObservation o){var b=new AiBudgetLedger(o.OwnerId,o.Generation);b.ObserveBank(o.Credits);return b;}
        private static AiTechnologyPlan Plan(PlayableAiObservation o,AiBudgetLedger b,AiDoctrine kind=AiDoctrine.Heavy,bool emergency=false)=>new AiTechnologyPlanner().Plan(o,PlayableProfile.Default,AiProfile.Initial,b.Capture(),emergency,doctrine:global::AiDoctrineTests.Policy(kind));
        [Test] public void ArtilleryDeficitRequestsNormalScienceWithoutResearchBeneficiaries()
        {
            var o=View();var p=PlayableProfile.Default;var ai=AiProfile.Initial;
            var delayed=AiArmyResearchPolicy.Observe(o,p,AiDifficulty.Fighter,AiGamePhase.Early,new AiDecisionArbiter(ai));
            var plan=new AiTechnologyPlanner().Plan(o,p,ai,Budget(o).Capture(),armyPolicy:delayed,doctrine:global::AiDoctrineTests.Policy(AiDoctrine.Heavy));
            Assert.True(plan.Branches.All(b=>b.Utility==0));
            Assert.NotNull(plan.Infrastructure);Assert.AreEqual(PlayableBuildingKind.ScientificCenter,plan.Infrastructure.Building);Assert.NotNull(plan.FundingGoal);Assert.IsNull(plan.Action,"No Guidance unlock order");
            Assert.True(new AiEconomyPlanner().InfrastructureRequests(o,PlayableProfile.Default,plan.Infrastructure).Any(c=>c.Legal));
        }
        [TestCase(1)][TestCase(2)] public void ExistingScienceDoesNotDuplicateOrForceGuidance(int center)
        {var o=View(center:center);var plan=Plan(o,Budget(o));Assert.IsNull(plan.Infrastructure);Assert.IsNull(plan.FundingGoal);Assert.IsNull(plan.Action);}
        [TestCase(AiDoctrine.Tanks)][TestCase(AiDoctrine.Explorers)][TestCase(AiDoctrine.Air)] public void DoctrineWithoutArtilleryDoesNotBuyScience(AiDoctrine doctrine)
        {var o=View();Assert.IsNull(Plan(o,Budget(o),doctrine).FundingGoal);}
        [TestCase(true,true,true)][TestCase(false,false,true)][TestCase(false,true,false)]
        public void EmergencyMissingProducerAndInitialForceGuardsRemain(bool emergency,bool factory,bool sufficientForce)
        {var o=View(factory:factory,count:sufficientForce?(int?)null:0);var plan=Plan(o,Budget(o),emergency:emergency);Assert.IsNull(plan.Infrastructure);Assert.IsNull(plan.FundingGoal);}
        [TestCase(false)][TestCase(true)] public void LivingExplorerReplacementCannotStarveReadyDoctrineArtillery(bool affordable)
        {
            var p=PlayableProfile.Default;var ai=AiProfile.Initial;
            var o=global::AiDoctrineTests.View(Enumerable.Repeat(PlayableEntityKind.Tank,8).Concat(Enumerable.Repeat(PlayableEntityKind.Explorer,4)));
            var d=global::AiDoctrineTests.Policy(AiDoctrine.Balanced);
            // These Explorers are serving the army, matching the actual failed
            // witness. Useful reconnaissance can ask for a separate replacement.
            typeof(AiDoctrinePolicy).GetProperty("UtilityAvailable",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(d,new Func<int,bool>(_=>false));
            var b=Budget(o);b.SetSafetyReserve(o.Credits-(affordable?p.ShkvalCreditCost:p.ExplorerCreditCost));
            var plan=d.Assess(o,p,ai,b.Capture(),3,true);Assert.IsNotEmpty(plan.Candidates);
            Assert.True(plan.Candidates.All(c=>c.Action.Kind==PlayableCommandKind.QueueShkval),"Repeated cheaper scout orders cannot consume this ready artillery deficit");
            if(affordable)Assert.True(plan.Candidates.Any(c=>c.Legal));else Assert.True(plan.Candidates.All(c=>c.Reason=="saving for doctrine Shkval"));
        }
        [Test] public void LastExplorerReplacementStillPrecedesArtillery()
        {
            var p=PlayableProfile.Default;var o=global::AiDoctrineTests.View(Enumerable.Repeat(PlayableEntityKind.Tank,8));var d=global::AiDoctrineTests.Policy(AiDoctrine.Heavy);
            var plan=d.Assess(o,p,AiProfile.Initial,Budget(o).Capture(),3,true);Assert.AreEqual(PlayableCommandKind.QueueExplorer,plan.Candidates.Single().Action.Kind);
        }
        [TestCase(false)][TestCase(true)] public void ArtilleryScienceHoldRetainsExpiryAndProtectsProductionEvenAfterGuidance(bool ownedGuidance)
        {
            var p=PlayableProfile.Default;var o=View(cash:100,guidance:ownedGuidance);var b=Budget(o);var a=new AiDecisionArbiter(AiProfile.Initial);var f=new AiTechnologyFunding(o.OwnerId,o.Generation);
            var plan=Plan(o,b);Assert.IsNull(plan.Infrastructure,"Science is unaffordable");Assert.NotNull(plan.FundingGoal);
            f.Observe(o,p,AiProfile.Initial,b,a,plan,true,protectedFunds:40);var held=b.Capture().Reserved.Single();Assert.AreEqual(60,held.Amount);Assert.AreEqual(PlayableCommandKind.BuildAt,held.Action.Kind);Assert.True(f.Valid(held,o,p,b,a));
            var next=View(tick:1,cash:100,guidance:ownedGuidance);f.Observe(next,p,AiProfile.Initial,b,a,Plan(next,b),true,protectedFunds:40);var same=b.Capture().Reserved.Single();Assert.AreEqual(held.ExpiresTick,same.ExpiresTick);Assert.AreEqual(held.CreatedTick,same.CreatedTick);
            next=View(tick:held.ExpiresTick+1,cash:100,guidance:ownedGuidance);b.Expire(next.Tick,e=>f.Valid(e,next,p,b,a));f.Reconcile(next,b,a);f.Observe(next,p,AiProfile.Initial,b,a,Plan(next,b),true,protectedFunds:40);Assert.IsEmpty(b.Capture().Reserved,"Same cash cannot renew expired science savings");
        }
        [Test] public void ReadyScienceReleasesConstructionSavingsWithoutBuyingGuidance()
        {
            var p=PlayableProfile.Default;var o=View(cash:100);var b=Budget(o);var a=new AiDecisionArbiter(AiProfile.Initial);var f=new AiTechnologyFunding(o.OwnerId,o.Generation);f.Observe(o,p,AiProfile.Initial,b,a,Plan(o,b),true);
            Assert.IsNotEmpty(b.Capture().Reserved);var next=View(tick:1,cash:100,center:2);f.Observe(next,p,AiProfile.Initial,b,a,Plan(next,b),true);Assert.IsEmpty(b.Capture().Reserved);Assert.IsNull(Plan(next,b).Action);
        }
        [Test] public void ChassisValuesExistingHealthEvenWithoutExtraSpeed()
        {
            var data=PlayableProfile.Default.CopyData();data.tankChassisSpeed=data.tankSpeed;var p=PlayableProfile.Create(data);var d=AiResearchCatalog.For(PlayableResearchKind.TankChassis);
            Assert.That(d.RelativeGain(p),Is.EqualTo(p.TankChassisHealthBonusPercent/100).Within(1e-12));
            data.tankChassisHealthBonusPercent=0;Assert.Zero(d.RelativeGain(PlayableProfile.Create(data)));
        }
        [Test] public void GuidanceValuesExistingBlastAreaEvenWithoutExtraRange()
        {
            var data=PlayableProfile.Default.CopyData();data.shkvalGuidanceRange=data.shkvalBaseRange;var p=PlayableProfile.Create(data);var d=AiResearchCatalog.For(PlayableResearchKind.ShkvalGuidance);
            double baseRadius=PlayableUnitRules.ShkvalBlastRadius(p,false),upgradedRadius=PlayableUnitRules.ShkvalBlastRadius(p,true);
            Assert.That(d.RelativeGain(p),Is.EqualTo(upgradedRadius*upgradedRadius/(baseRadius*baseRadius)-1).Within(1e-12));
            Assert.Greater(d.RelativeGain(p),0);data.shkvalBaseBlastRadiusMultiplier=1;Assert.Zero(d.RelativeGain(PlayableProfile.Create(data)));
        }
        [Test] public void ChangedSpecializationsUseProfileRatiosAndLeaveOtherResearchEffectsIntact()
        {
            var p=PlayableProfile.Default;double speed=p.TankChassisSpeed/p.TankSpeed,health=1+p.TankChassisHealthBonusPercent/100;
            Assert.That(AiResearchCatalog.For(PlayableResearchKind.TankChassis).RelativeGain(p),Is.EqualTo(speed*health-1).Within(1e-12));
            Assert.That(AiResearchCatalog.For(PlayableResearchKind.ExplorerAssaultGuns).RelativeGain(p),Is.EqualTo(PlayableUnitRules.DamagePerSecond(p,PlayableEntityKind.Explorer,true)/PlayableUnitRules.DamagePerSecond(p,PlayableEntityKind.Explorer)-1).Within(1e-12));
        }
    }
}
