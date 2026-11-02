using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.EditMode
{
    public sealed class AiActiveStrategyTests
    {
        static readonly PlayableProfile P=PlayableProfile.Default;
        static readonly AiProfile AI=AiProfile.Initial;
        static PlayableAiObservation View(long tick=100,int count=6,PlayableEntityKind kind=PlayableEntityKind.Tank,PlayableBuildingSnapshot[] extra=null,PlayableResearchOrderSnapshot[] research=null,bool routeTower=false)
        {
            var own=Enumerable.Range(1,count).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,kind,new NavPoint(-10,id+15),(int)PlayableUnitRules.Health(P,kind),false,0,0,0)).ToArray();
            var target=new PlayableEntitySnapshot(101,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(20,18),P.TankHealth,false,0,0,0);
            var buildings=new[]{new PlayableBuildingSnapshot(201,PlayableOwner.Player,PlayableBuildingKind.Refinery,new NavPoint(-30,-20),TerritoryRules.Health(P,PlayableBuildingKind.Refinery),1,0,0,default,refineryUpgraded:true),new PlayableBuildingSnapshot(202,PlayableOwner.Enemy,PlayableBuildingKind.Mine,new NavPoint(30,-20),TerritoryRules.Health(P,PlayableBuildingKind.Mine),1,0,0,default)}.Concat(extra??Array.Empty<PlayableBuildingSnapshot>()).ToArray();
            var vision=new PlayableVision(0,50,50,1,1);vision.Refresh(new[]{new VisionSource(default,100)},Array.Empty<KnownBuilding>());
            var routes=own.Select(u=>new PlayableRouteProof(u.Id,u.Owner,107,tick,1,u.Position,PlayableUnitRules.Radius(P,kind),PlayableRouteTargetKind.VisibleEnemy,target.Id,target.Position,target.Position,new[]{target.Position})).ToArray();
            if(routeTower)routes=routes.Concat(own.SelectMany(u=>buildings.Where(b=>b.Id==401).Select(b=>new PlayableRouteProof(u.Id,u.Owner,107,tick,1,u.Position,PlayableUnitRules.Radius(P,kind),PlayableRouteTargetKind.VisibleEnemy,b.Id,b.Position,b.Position,new[]{b.Position})))).ToArray();
                return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,107,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,own.Concat(new[]{target}).ToArray(),buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot(),routeProofs:routes,ownerResearch:research,population:new PlayablePopulationSnapshot(own.Sum(u=>PlayableUnitRules.Population(P,u.Kind)),0,P.ArmyCapacity)));
        }
        static PlayableBuildingSnapshot Construction(ConstructionPhase phase=ConstructionPhase.Constructing,int id=301)=>new PlayableBuildingSnapshot(id,PlayableOwner.Player,PlayableBuildingKind.Factory,new NavPoint(-25,-10),P.FactoryHealth,.2,0,0,default,phase:phase);
        static PlayableAiOpeningCompositionState Opening()=>PlayableAiOpeningComposition.Initialize(7,"player-1",aiProfile:AI).WithPhase(PlayableAiOpeningPhase.Complete);
        [TestCase(AiPersonality.Defensive)][TestCase(AiPersonality.Aggressive)][TestCase(AiPersonality.Harassing)]
        public void NominalKnownIncomeLeadNeverJustifiesIdleArmy(AiPersonality personality)
        {
            var o=View();Assert.True(AiOpponentSelector.KnownEconomicLead(o,P));var planner=new AiArmyPlanner(o.OwnerId,o.Generation){Personality=personality};var registry=new AiArmyRegistry(o.OwnerId,o.Generation,AI,AiDifficulty.Veteran);
            planner.Observe(o,registry,AI,replanQuietGuards:false);Assert.AreEqual(PlayableCommandKind.Attack,planner.Propose(o,Opening(),P,AI,registry)?.Kind);
        }
        [TestCase(ConstructionPhase.Pending,false)][TestCase(ConstructionPhase.Ready,false)][TestCase(ConstructionPhase.Constructing,true)]
        public void OnlyActualPaidUsefulDevelopmentQualifies(ConstructionPhase phase,bool expected)
        {Assert.AreEqual(expected,AiActiveStrategyPolicy.MaterialDevelopment(View(extra:new[]{Construction(phase)}),P,AI));}
        [TestCase(false,false,false)][TestCase(true,false,true)][TestCase(true,true,false)]
        public void DeferredOrIrrelevantResearchCannotHoldArmy(bool active,bool hawks,bool expected)
        {
            var research=new PlayableResearchOrderSnapshot(1,PlayableResearchKind.TankChassis,10,300,1,30,active,false);
            var o=View(kind:hawks?PlayableEntityKind.Hawk:PlayableEntityKind.Tank,research:new[]{research});Assert.AreEqual(expected,AiActiveStrategyPolicy.MaterialDevelopment(o,P,AI));
        }
        [Test] public void DevelopmentDeadlineCannotRenewAndSavedPlannerRestoresItExactly()
        {
            var o=View(extra:new[]{Construction()});var planner=new AiArmyPlanner(o.OwnerId,o.Generation);var registry=new AiArmyRegistry(o.OwnerId,o.Generation,AI,AiDifficulty.Veteran);
            planner.Observe(o,registry,AI,replanQuietGuards:false);Assert.IsNull(planner.Propose(o,Opening(),P,AI,registry),"No offensive order while useful paid work is in its short window");
            byte[] Write(AiArmyPlanner a){using var s=new MemoryStream();using var w=new BinaryWriter(s);a.WriteState(w);return s.ToArray();}
            var bytes=Write(planner);var restored=new AiArmyPlanner(o.OwnerId,o.Generation);using(var reader=new BinaryReader(new MemoryStream(bytes)))restored.ReadState(reader,o.Tick,registry);CollectionAssert.AreEqual(bytes,Write(restored));
            long expiry=o.Tick+AiProfile.SecondsToTicks(AI.Value("activeStrategy.developmentSeconds"),30);
            var later=View(tick:expiry,extra:new[]{Construction(id:302)});restored.Observe(later,registry,AI,replanQuietGuards:false);Assert.AreEqual(PlayableCommandKind.Attack,restored.Propose(later,Opening(),P,AI,registry)?.Kind,"New purchase cannot renew expired development");
        }
        [Test] public void FirstExitAndFlankStayAvailableDuringMainInvestment()
        {
            var o=View(extra:new[]{Construction()});var registry=new AiArmyRegistry(o.OwnerId,o.Generation,AI,AiDifficulty.Veteran);
            foreach(bool flank in new[]{true,false})
            {
                var planner=new AiArmyPlanner(o.OwnerId,o.Generation,flank?AiArmyPlanner.FlankPolicy:AiArmyPlanner.Policy){OpeningMinimum=flank?0:3};planner.Observe(o,registry,AI,replanQuietGuards:false);
                Assert.AreEqual(PlayableCommandKind.Attack,planner.Propose(o,Opening(),P,AI,registry)?.Kind);
            }
        }
        static PlayableBuildingSnapshot Tower(PlayableBuildingKind kind,NavPoint position,ConstructionPhase phase=ConstructionPhase.Ready)=>new PlayableBuildingSnapshot(401,PlayableOwner.Enemy,kind,position,TerritoryRules.Health(P,kind),1,0,0,default,phase:phase);
        [Test] public void CompatibleLocalGaussMattersButAirRemoteAndPendingDoNot()
        {
            var gauss=Tower(PlayableBuildingKind.GaussTower,new NavPoint(20,18));
            double Risk(PlayableAiObservation o)=>AiActiveStrategyPolicy.FortificationPower(o,P,AI,o.Entities.Where(u=>u.Owner==o.Owner).ToArray(),101,new NavPoint(20,18));
            Assert.Greater(Risk(View(extra:new[]{gauss})),0);Assert.Zero(Risk(View(kind:PlayableEntityKind.Hawk,extra:new[]{gauss})));
            Assert.Zero(Risk(View(kind:PlayableEntityKind.Shkval,extra:new[]{gauss})),"Available ranged support stops outside Gauss coverage");
            Assert.Zero(Risk(View(extra:new[]{Tower(PlayableBuildingKind.GaussTower,new NavPoint(40,-30))})));
            Assert.Zero(Risk(View(extra:new[]{Tower(PlayableBuildingKind.GaussTower,new NavPoint(20,18),ConstructionPhase.Constructing)})));
            Assert.True(AiActiveStrategyPolicy.ViableApproach(View(kind:PlayableEntityKind.Hawk,extra:new[]{gauss}),P,AI,View(kind:PlayableEntityKind.Hawk).Entities.Where(u=>u.Owner==PlayableOwner.Player).ToArray(),101,new NavPoint(20,18),PlayableRouteTargetKind.VisibleEnemy));
        }
        [Test] public void RememberedConstructionCannotBecomeReadyFirepowerWithoutObservation()
        {
            var tower=Tower(PlayableBuildingKind.GaussTower,new NavPoint(20,18),ConstructionPhase.Constructing);var o=View(extra:new[]{tower});
            var knowledge=new AiKnowledgeState(o.OwnerId,o.Generation,o.Tick,new[]{new AiKnownContact(tower.Id,tower.Owner,true,(int)tower.Kind,tower.Position,tower.Health,o.Tick,1,false)},Array.Empty<AiVisitedArea>());
            var memo=new Dictionary<string,long>();AiActiveStrategyPolicy.ObserveFortifications(o,knowledge,memo);
            Assert.IsEmpty(AiActiveStrategyPolicy.KnownTowers(View(),knowledge,memo));
            var ready=View(extra:new[]{Tower(PlayableBuildingKind.GaussTower,tower.Position)});AiActiveStrategyPolicy.ObserveFortifications(ready,knowledge,memo);
            Assert.AreEqual(1,AiActiveStrategyPolicy.KnownTowers(View(),knowledge,memo).Length,"A previously observed ready tower remains known");
            var foreign=new AiKnowledgeState("foreign",o.Generation,o.Tick,knowledge.Contacts,knowledge.Areas);Assert.Throws<ArgumentException>(()=>AiActiveStrategyPolicy.KnownTowers(o,foreign,memo));
        }
        [Test] public void FortificationCreatesUsefulSupportThroughExistingCompositionOnlyAfterOpening()
        {
            var o=View(extra:new[]{Tower(PlayableBuildingKind.GaussTower,new NavPoint(20,18))},routeTower:true);
            var d=AiDoctrineTests.Policy(AiDoctrine.Tanks);var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(AiDoctrinePolicy).GetField("chosen",flags).SetValue(d,false);Assert.Zero(d.Targets(o,AI)[2],"First opening composition stays intact");
            typeof(AiDoctrinePolicy).GetField("chosen",flags).SetValue(d,true);Assert.Greater(d.Targets(o,AI)[2],0,"Existing producer mix requests siege support");
            Assert.Zero(d.Targets(View(extra:new[]{Tower(PlayableBuildingKind.GaussTower,new NavPoint(20,18))}),AI)[2],"No certified approach: no global tower deterrence");
        }
        [Test] public void ReadyFortificationChangesOnlyTheCoveredTargetScore()
        {
            var plain=AiOpponentSelector.Assess(View(),P,AI).Single();var local=AiOpponentSelector.Assess(View(extra:new[]{Tower(PlayableBuildingKind.GaussTower,new NavPoint(20,18))}),P,AI).Single();var remote=AiOpponentSelector.Assess(View(extra:new[]{Tower(PlayableBuildingKind.GaussTower,new NavPoint(40,-30))}),P,AI).Single();
            Assert.Less(local.Score,plain.Score);Assert.AreEqual(plain.Score,remote.Score);Assert.Greater(local.FortificationRisk,0);
        }
        [Test] public void ActiveStrategyFieldsAreEditableBoundedAndRoundTrip()
        {
            foreach(var field in NativeBalanceFields.AiOpeningFields.Where(f=>f.Path.StartsWith("activeStrategy.")))
            {Assert.IsNotEmpty(field.Label);Assert.IsNotEmpty(field.Description);Assert.IsNotEmpty(field.Unit);Assert.Greater(field.Step,0);Assert.Throws<ArgumentOutOfRangeException>(()=>field.Validate(field.Maximum+field.Step));var d=AI.CopyData();NativeBalanceFields.Write(field,d,field.Minimum);Assert.AreEqual(field.Minimum,new AiProfile(d).Value(field.Path));}
        }
        [Test] public void SiegeGoalUsesAvailableProductionUntilCurrentSciencePrerequisiteIsReady()
        {
            var factory=new PlayableBuildingSnapshot(303,PlayableOwner.Player,PlayableBuildingKind.Factory,new NavPoint(-25,-10),P.FactoryHealth,1,0,0,default);
            var center=new PlayableBuildingSnapshot(304,PlayableOwner.Player,PlayableBuildingKind.ScientificCenter,new NavPoint(-25,0),TerritoryRules.Health(P,PlayableBuildingKind.ScientificCenter),1,0,0,default);
            var tower=Tower(PlayableBuildingKind.GaussTower,new NavPoint(20,18));
            var before=View(extra:new[]{factory,tower},routeTower:true);var ready=View(extra:new[]{factory,center,tower},routeTower:true);var doctrine=AiDoctrineTests.Policy(AiDoctrine.Tanks);
            PlayableAiAction Request(PlayableAiObservation o)=>new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.QueueShkval,new[]{factory.Id},unitKind:PlayableEntityKind.Shkval);
            Assert.AreEqual("ready science prerequisite",AiEconomyAdmission.Reject(before,P,Request(before)));Assert.IsNull(AiEconomyAdmission.Reject(ready,P,Request(ready)));
            Assert.AreEqual(PlayableEntityKind.Tank,doctrine.GroundProductionKind(before,AI));Assert.AreEqual(PlayableEntityKind.Shkval,doctrine.GroundProductionKind(ready,AI));
            var budget=new AiBudgetLedger(before.OwnerId,before.Generation);budget.ObserveBank(before.Credits);
            Assert.True(doctrine.Assess(before,P,AI,budget.Capture()).Candidates.Any(c=>c.Legal&&c.Action.Kind==PlayableCommandKind.QueueTank),"Support prerequisite cannot idle an available line or prevent growing toward science eligibility");
        }
    }
}
