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
    public sealed class NativeAiActivityPolicyTests
    {
        private static readonly PlayableProfile P=PlayableProfile.Default;
        private static PlayableEntitySnapshot Unit(int id,PlayableOwner owner,PlayableEntityKind kind,NavPoint point,int? health=null)=>
            new PlayableEntitySnapshot(id,owner,kind,point,health??PlayableUnitRules.Health(P,kind),false,0,0,0);
        private static PlayableBuildingSnapshot Building(int id,PlayableOwner owner,PlayableBuildingKind kind,NavPoint point,int site=0,int slot=0,ConstructionPhase phase=ConstructionPhase.Ready)=>
            new PlayableBuildingSnapshot(id,owner,kind,point,200,phase==ConstructionPhase.Ready?1:.4,0,0,default,siteId:site,slotId:slot,phase:phase);
        private static PlayableAiObservation View(PlayableEntitySnapshot[] units,PlayableBuildingSnapshot[] buildings,TerritorySiteSnapshot[] sites=null,
            PlayableRouteProof[] proofs=null,PlayablePublicScoutObjective[] objectives=null,long tick=45,double settled=0)
        {
            var vision=new PlayableVision(0,200,200,1,1);vision.Refresh(new[]{new VisionSource(default,200)},Array.Empty<KnownBuilding>());
            return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,41,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,10000,null,
                units,buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:sites,vision:vision.Snapshot(),
                routeProofs:proofs,publicScoutObjectives:objectives,researchAvailability:AiResearchCatalog.All.Select(d=>new PlayableResearchAvailabilitySnapshot(d.Kind,d.Cost(P),true)).ToArray(),
                incomePerSecond:10,settledIncome:settled,homeSiteId:1));
        }
        private static AiBudgetLedger Budget(PlayableAiObservation o){var b=new AiBudgetLedger(o.OwnerId,o.Generation);b.ObserveBank(o.Credits);return b;}
        private static AiArmyRegistry Registry()=>new AiArmyRegistry("player-1",71,AiProfile.Initial,AiDifficulty.Fighter);
        private static PlayableRouteProof Proof(PlayableEntitySnapshot u,PlayableRouteTargetKind kind,int id,NavPoint target,NavPoint goal,long tick)=>
            new PlayableRouteProof(u.Id,u.Owner,71,tick,1,u.Position,PlayableUnitRules.Radius(P,u.Kind),kind,id,target,goal,new[]{goal});
        private static void Applied(AiArmyPlanner p,PlayableAiObservation o,PlayableAiAction a)=>p.ObserveReceipt(
            new PlayableAiTraceRecord(o.Identity,a.ActionId,o.Tick,1,o.Tick,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"applied",ownerId:o.OwnerId,
                sourceIdentity:PlayableAiOpeningComposition.SourceIdentity,receiptIdentity:new AiReceiptIdentity(71,o.OwnerId,1,1)));
        private static PlayableAiObservation Science(int living,bool center=false,ConstructionPhase phase=ConstructionPhase.Ready,params PlayableEntitySnapshot[] extra)
        {
            var units=Enumerable.Range(1,living).Select(i=>Unit(i,PlayableOwner.Player,i==1?PlayableEntityKind.Tank:PlayableEntityKind.Explorer,default)).Concat(extra).ToArray();
            var buildings=new[]{Building(70,PlayableOwner.Player,PlayableBuildingKind.Factory,default),Building(80,PlayableOwner.Player,PlayableBuildingKind.Headquarters,new NavPoint(-20,0))};
            if(center)buildings=buildings.Concat(new[]{Building(90,PlayableOwner.Player,PlayableBuildingKind.ScientificCenter,new NavPoint(-10,0),phase:phase)}).ToArray();
            return View(units,buildings);
        }
        [TestCase(9,false)][TestCase(10,true)]
        public void ScienceCenterAdmissionCountsEveryOwnLivingKindAtTheProfileFloor(int living,bool allowed)
        {
            Assert.AreEqual(10,AiProfile.Initial.Value("technology.minimumUnitsForScienceCenter"));
            var o=Science(living);var b=Budget(o);var before=b.Capture();var plan=new AiTechnologyPlanner().Plan(o,P,AiProfile.Initial,b.Capture());
            Assert.AreEqual(allowed,plan.Infrastructure!=null);Assert.AreEqual(allowed,plan.FundingGoal!=null);Assert.IsNull(plan.Action);
            Assert.AreEqual(before.Liquid,b.Liquid);Assert.Zero(b.Reserved);Assert.Zero(b.Unpaid);
        }
        [Test] public void ScienceFloorExcludesEnemyAlliedAndDeadBodies()
        {
            var o=Science(9,false,ConstructionPhase.Ready,Unit(100,PlayableOwner.Player,PlayableEntityKind.Explorer,default,0),
                Unit(101,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(90,90)),Unit(102,PlayableOwner.Third,PlayableEntityKind.Tank,new NavPoint(90,90)));
            var plan=new AiTechnologyPlanner().Plan(o,P,AiProfile.Initial,Budget(o).Capture());Assert.IsNull(plan.Infrastructure);Assert.IsNull(plan.FundingGoal);
        }
        [TestCase(ConstructionPhase.Ready)][TestCase(ConstructionPhase.Constructing)]
        public void ExistingScienceAndResearchRemainEligibleAfterForceLoss(ConstructionPhase phase)
        {
            var o=Science(1,true,phase);var plan=new AiTechnologyPlanner().Plan(o,P,AiProfile.Initial,Budget(o).Capture());
            Assert.IsNull(plan.Infrastructure);Assert.NotNull(plan.FundingGoal);
            if(phase==ConstructionPhase.Ready){Assert.NotNull(plan.Action);Assert.AreEqual(PlayableCommandKind.QueueResearch,plan.Action.Kind);}
            else Assert.IsNull(plan.Action);
        }
        private static PlayableAiObservation Territory(bool full,bool claimed=false,bool ready=false,long tick=45,double settled=0)
        {
            var home=TerritoryRules.Sites(P).First(s=>s.Kind==PlayableBuildingKind.Headquarters);
            var outpost=TerritoryRules.Sites(P).First(s=>s.Kind==PlayableBuildingKind.Outpost);
            var mine=TerritoryRules.Sites(P).First(s=>s.Kind==PlayableBuildingKind.Mine);
            var unit=Unit(13,PlayableOwner.Player,PlayableEntityKind.Explorer,claimed||ready?outpost.Position:home.Position);
            var buildings=new List<PlayableBuildingSnapshot>{Building(80,PlayableOwner.Player,PlayableBuildingKind.Headquarters,home.Position,home.Id)};
            if(full)buildings.AddRange(home.Slots.Select((slot,i)=>Building(90+i,PlayableOwner.Player,PlayableBuildingKind.Factory,slot.Position,home.Id,slot.Id)));
            if(ready)buildings.Add(Building(120,PlayableOwner.Player,PlayableBuildingKind.Outpost,outpost.Position,outpost.Id));
            var sites=new[]{new TerritorySiteSnapshot(home,PlayableOwner.Player,PlayableOwner.Player,1,false,80,true),
                new TerritorySiteSnapshot(outpost,claimant:claimed||ready?(PlayableOwner?)PlayableOwner.Player:null,owner:ready?(PlayableOwner?)PlayableOwner.Player:null,progress:claimed||ready?1:0,contested:false,centerId:ready?120:0,ready:ready),
                new TerritorySiteSnapshot(mine,null,null,0,false,0,false)};
            var objectives=new[]{new PlayablePublicScoutObjective(outpost.Id,outpost.Position,PlayablePublicScoutObjectiveRole.ExpansionIncome,true),
                new PlayablePublicScoutObjective(mine.Id,mine.Position,PlayablePublicScoutObjectiveRole.ExpansionIncome,true)};
            return View(new[]{unit},buildings.ToArray(),sites,new[]{Proof(unit,PlayableRouteTargetKind.PublicObjective,outpost.Id,outpost.Position,outpost.Position,tick)},objectives,tick,settled);
        }
        [Test] public void FullBuildingCapacityPrioritizesAnOutpostThatAddsSlots()
        {
            var o=Territory(true);Assert.True(AiIncomeSiteSelection.NeedsBuildingTerritory(o));
            var chosen=AiIncomeSiteSelection.Select(o,P,AiProfile.Initial,null,o.Sites.Where(s=>AiIncomeSiteSelection.Free(s,o,P)),false);
            Assert.NotNull(chosen);Assert.AreEqual(PlayableBuildingKind.Outpost,chosen.Site.Kind);Assert.Greater(chosen.Site.Slots.Count,0);
            Assert.True(AiIncomeSiteSelection.TerritoryAllowed(o,P,AiProfile.Initial,null,chosen));
            Assert.False(AiIncomeSiteSelection.NeedsBuildingTerritory(Territory(false)));
        }
        [Test] public void FullHomeOutpostUsesOrdinaryActorClaimBuildAndObservedIncomeCompletion()
        {
            var o=Territory(true);var planner=new AiExpansionPlanner(o.OwnerId);var move=planner.TryPlan(o,P,AiProfile.Initial);
            Assert.NotNull(move);Assert.AreEqual(PlayableCommandKind.Move,move.Kind);Assert.AreEqual(PlayableBuildingKind.Outpost,move.BuildingKind);Assert.True(planner.ClaimsActor(13));
            planner.ObserveReceipt(new PlayableAiTraceRecord(o.Identity,move.ActionId,o.Tick,1,o.Tick,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"move",ownerId:o.OwnerId,
                sourceIdentity:PlayableAiOpeningComposition.SourceIdentity,receiptIdentity:new AiReceiptIdentity(71,o.OwnerId,1,1),kind:move.Kind));
            var captured=Territory(true,true,tick:90);planner.Observe(captured,P,AiProfile.Initial);var build=planner.TryPlan(captured,P,AiProfile.Initial);
            Assert.NotNull(build);Assert.AreEqual(PlayableCommandKind.BuildAt,build.Kind);Assert.AreEqual(move.SiteId,build.SiteId);
            planner.ObserveReceipt(new PlayableAiTraceRecord(captured.Identity,build.ActionId,90,2,90,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"build",ownerId:o.OwnerId,
                sourceIdentity:PlayableAiOpeningComposition.SourceIdentity,receiptIdentity:new AiReceiptIdentity(71,o.OwnerId,2,1),kind:build.Kind));
            planner.Observe(Territory(true,true,true,120,100),P,AiProfile.Initial);Assert.AreEqual("income",planner.Capture().Phase);
            planner.Observe(Territory(true,true,true,150,120),P,AiProfile.Initial);Assert.AreEqual(0,planner.Capture().SiteId);
        }
        private static PlayableAiObservation Army(int count=6,bool enemy=false,bool possible=false,Dictionary<int,NavPoint> positions=null,long tick=45)
        {
            var units=Enumerable.Range(1,count).Select(i=>Unit(i,PlayableOwner.Player,PlayableEntityKind.Tank,positions!=null&&positions.TryGetValue(i,out var point)?point:new NavPoint(i,0))).ToArray();
            var own=new[]{Building(80,PlayableOwner.Player,PlayableBuildingKind.Headquarters,new NavPoint(0,-20),1),
                Building(81,PlayableOwner.Player,PlayableBuildingKind.Mine,new NavPoint(20,0),4),Building(82,PlayableOwner.Player,PlayableBuildingKind.Outpost,new NavPoint(40,20),3)};
            var hostile=enemy?new[]{Building(90,PlayableOwner.Enemy,PlayableBuildingKind.Headquarters,new NavPoint(90,0)),Building(91,PlayableOwner.Enemy,PlayableBuildingKind.Mine,new NavPoint(50,0))}:Array.Empty<PlayableBuildingSnapshot>();
            var objectives=possible?new[]{new PlayablePublicScoutObjective(2,new NavPoint(90,0),PlayablePublicScoutObjectiveRole.PossibleEnemyStart,true)}:Array.Empty<PlayablePublicScoutObjective>();
            var proofs=units.SelectMany(u=>own.Select(b=>Proof(u,PlayableRouteTargetKind.FriendlyAnchor,b.Id,b.Position,new NavPoint(b.Position.X-3,b.Position.Z),tick))
                .Concat(hostile.Select(b=>Proof(u,PlayableRouteTargetKind.VisibleEnemy,b.Id,b.Position,b.Position,tick)))
                .Concat(objectives.Select(g=>Proof(u,PlayableRouteTargetKind.PublicObjective,g.SiteId,g.Approach,g.Approach,tick)))).ToArray();
            return View(units,own.Concat(hostile).ToArray(),proofs:proofs,objectives:objectives,tick:tick);
        }
        [Test] public void TwoPlayerPressureRanksACloserEnemyMineBeforeLowerIdHeadquarters()
        {
            var o=Army(3,true);var registry=Registry();var planner=new AiArmyPlanner(o.OwnerId,o.Generation);planner.Rebind(P);
            var opening=PlayableAiOpeningComposition.Initialize(41,o.OwnerId,forcedOpening:PlayableAiOpening.BlindRush);
            var action=planner.Propose(o,opening,P,AiProfile.Initial,registry);Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.Attack,action.Kind);Assert.AreEqual(91,action.TargetId);
            var scores=AiOpponentSelector.Assess(o,P,AiProfile.Initial);Assert.Greater(scores.Single(s=>s.TargetId==91).Reachability,scores.Single(s=>s.TargetId==90).Reachability);
        }
        [Test] public void GreedyMayDefendAKnownLeadButUnknownEconomyDoesNotStopPressure()
        {
            var planner=new AiArmyPlanner("player-1",71);planner.Rebind(P);var o=Army(3,true);
            var opening=PlayableAiOpeningComposition.Initialize(41,o.OwnerId,forcedOpening:PlayableAiOpening.GreedySafe);
            Assert.True(AiOpponentSelector.KnownEconomicLead(o,P));var guard=planner.Propose(o,opening,P,AiProfile.Initial,Registry());Assert.NotNull(guard);Assert.AreEqual(PlayableCommandKind.Move,guard.Kind);
            var unknown=Army(3,possible:true);Assert.False(AiOpponentSelector.KnownEconomicLead(unknown,P));
            var attack=planner.Propose(unknown,opening,P,AiProfile.Initial,Registry());Assert.NotNull(attack);Assert.AreEqual(PlayableCommandKind.AttackMove,attack.Kind);
        }
        private static (AiArmyPlanner planner,AiArmyRegistry registry,Dictionary<int,NavPoint> positions) DistributedGuards()
        {
            var r=Registry();var p=new AiArmyPlanner("player-1",71);p.Rebind(P);var positions=new Dictionary<int,NavPoint>();
            for(int i=0;i<3;i++)
            {
                var o=Army(positions:positions,tick:45+i*90);var a=p.ProposePositioning(o,P,AiProfile.Initial,r);Assert.NotNull(a,"each uncovered asset gets a legal assignment");
                p.Commit(o,a,r);Applied(p,o,a);foreach(int id in a.EntityIds)positions[id]=a.Target;
                p.Observe(Army(positions:positions,tick:90+i*90),r,AiProfile.Initial,allowsPressure:false);
            }
            return (p,r,positions);
        }
        [Test] public void QuietDefenseCoversThreeAssetsWithinExistingCapAndRoundtrips()
        {
            var x=DistributedGuards();var active=x.registry.Capture().Armies.Where(a=>a.Phase!=AiArmyPhase.Disbanded).ToArray();
            Assert.AreEqual(3,active.Length);Assert.AreEqual(x.registry.MajorCap,active.Length);Assert.AreEqual(3,active.Select(a=>a.Objective).Distinct().Count());
            Assert.AreEqual(6,active.SelectMany(a=>a.Members).Distinct().Count());Assert.True(active.All(a=>a.Members.Length==2));x.registry.AssertInvariants();
            var restoredRegistry=AiArmyRegistry.Restore(x.registry.Capture(),AiProfile.Initial,AiDifficulty.Fighter,315);byte[] bytes;
            using(var stream=new MemoryStream()){using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))x.planner.WriteState(writer);bytes=stream.ToArray();}
            var restored=new AiArmyPlanner("player-1",71);restored.Rebind(P);using(var stream=new MemoryStream(bytes))using(var reader=new BinaryReader(stream))restored.ReadState(reader,315,restoredRegistry);
            using(var stream=new MemoryStream()){using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))restored.WriteState(writer);CollectionAssert.AreEqual(bytes,stream.ToArray());}
        }
        [Test] public void DistributedGuardsReleaseViablePressureWithoutDiscardingAllCoverage()
        {
            var x=DistributedGuards();var o=Army(enemy:true,positions:x.positions,tick:360);x.planner.Observe(o,x.registry,AiProfile.Initial);
            var opening=PlayableAiOpeningComposition.Initialize(41,o.OwnerId,forcedOpening:PlayableAiOpening.BlindRush);var action=x.planner.Propose(o,opening,P,AiProfile.Initial,x.registry);
            Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.Attack,action.Kind);Assert.AreEqual(91,action.TargetId);x.planner.Commit(o,action,x.registry);
            var groups=x.registry.Capture().Armies.Where(a=>a.Phase!=AiArmyPhase.Disbanded).ToArray();Assert.True(groups.Any(a=>a.Role==AiArmyRole.Attack));
            Assert.True(groups.Any(a=>a.Role==AiArmyRole.MobileDefense));Assert.LessOrEqual(groups.Length,x.registry.MajorCap);x.registry.AssertInvariants();
        }
    }
}
