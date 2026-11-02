using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
public sealed class AiDoctrineTests
{
    private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly PlayableProfile P=PlayableProfile.Default;
    internal static PlayableAiObservation View(IEnumerable<PlayableEntityKind> army=null,bool hangar=true,bool enemyAir=false,PlayableResearchOrderSnapshot[] research=null,PlayableProductionOrderSnapshot[] queue=null)
    {
        var bs=new[]{new PlayableBuildingSnapshot(4,PlayableOwner.Player,PlayableBuildingKind.Factory,default,100,1,queue?.Length??0,0,default,orders:queue),new PlayableBuildingSnapshot(5,PlayableOwner.Player,PlayableBuildingKind.ScientificCenter,new NavPoint(20,0),100,1,0,0,default)}.Concat(hangar?new[]{new PlayableBuildingSnapshot(6,PlayableOwner.Player,PlayableBuildingKind.Hangar,new NavPoint(40,0),100,1,0,0,default)}:Array.Empty<PlayableBuildingSnapshot>()).ToArray();
        var units=(army??Array.Empty<PlayableEntityKind>()).Select((k,i)=>new PlayableEntitySnapshot(100+i,PlayableOwner.Player,k,default,PlayableUnitRules.Health(P,k),false,0,0,0)).Concat(enemyAir?new[]{new PlayableEntitySnapshot(999,PlayableOwner.Enemy,PlayableEntityKind.Hawk,new NavPoint(80,0),PlayableUnitRules.Health(P,PlayableEntityKind.Hawk),false,0,0,0)}:Array.Empty<PlayableEntitySnapshot>()).ToArray();
        return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,41,1,100000,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,100000,null,units,bs,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,population:new PlayablePopulationSnapshot(units.Length,0,1000),ownerId:"owner",researchAvailability:AiResearchCatalog.All.Select(d=>new PlayableResearchAvailabilitySnapshot(d.Kind,d.Cost(P),true)).ToArray(),ownerResearch:research));
    }
    internal static AiDoctrinePolicy Policy(AiDoctrine doctrine)
    {
        var d=new AiDoctrinePolicy(PlayableAiOpeningComposition.Initialize(41,"owner"),71);
        typeof(AiDoctrinePolicy).GetField("chosen",F).SetValue(d,true);typeof(AiDoctrinePolicy).GetField("doctrine",F).SetValue(d,doctrine);typeof(AiDoctrinePolicy).GetField("mixed",F).SetValue(d,(int)doctrine>=4&&doctrine!=AiDoctrine.Air);return d;
    }
    private static AiBudgetState Budget(PlayableAiObservation o){var b=new AiBudgetLedger(o.OwnerId,o.Generation);b.ObserveBank(o.Credits);return b.Capture();}
    [TestCase(AiDoctrine.Balanced)][TestCase(AiDoctrine.Tanks)][TestCase(AiDoctrine.Heavy)][TestCase(AiDoctrine.Explorers)]
    [TestCase(AiDoctrine.HawksTanks)][TestCase(AiDoctrine.HawksExplorers)][TestCase(AiDoctrine.BalancedSmallAir)][TestCase(AiDoctrine.HeavySmallAir)]
    public void ActualProductionCandidatesConvergeToHeadcountsWithoutReconCap(AiDoctrine kind)
    {
        var d=Policy(kind);var ai=AiProfile.Initial;var army=new List<PlayableEntityKind>();
        for(int i=0;i<56;i++)
        {var o=View(army);var c=d.Assess(o,P,ai,Budget(o)).Candidates.Single();Assert.IsNull(AiEconomyAdmission.Reject(o,P,c.Action,true));army.Add(c.Action.UnitKind);}
        var counts=new[]{army.Count(k=>k==PlayableEntityKind.Tank),army.Count(k=>k==PlayableEntityKind.Explorer),army.Count(k=>k==PlayableEntityKind.Shkval),army.Count(k=>k==PlayableEntityKind.Hawk)};var target=d.Targets(View(),ai);double total=target.Sum();
        for(int i=0;i<4;i++)Assert.That(counts[i],Is.InRange((int)Math.Floor(56*target[i]/total)-1,(int)Math.Ceiling(56*target[i]/total)+1),"target headcount role "+i);
        if(kind==AiDoctrine.Explorers||kind==AiDoctrine.HawksExplorers)Assert.Greater(counts[1],1,"combat Explorers cannot be capped at one scout");
    }
    [Test] public void MixedDoctrineUsesAvailableFactoryWithoutWaitingForHangar()
    {
        var d=Policy(AiDoctrine.HawksExplorers);var o=View(hangar:false);var c=d.Assess(o,P,AiProfile.Initial,Budget(o)).Candidates.Single();Assert.AreEqual(PlayableCommandKind.QueueExplorer,c.Action.Kind);
    }
    [Test] public void VisibleAirAddsCombatExplorersAndMissingVisionDoesNotEraseObservation()
    {
        var d=Policy(AiDoctrine.Heavy);var ai=AiProfile.Initial;var opening=PlayableAiOpeningComposition.Initialize(41,"owner");var executor=new AiOpeningExecutor(opening,71);
        Assert.Zero(d.Targets(View(),ai)[1]);d.Observe(View(enemyAir:true),P,ai,executor,AiDifficulty.Fighter);Assert.GreaterOrEqual(d.Targets(View(),ai)[1]/d.Targets(View(),ai)[0],.5);
        d.Observe(View(),P,ai,executor,AiDifficulty.Fighter);var o=View(Enumerable.Repeat(PlayableEntityKind.Tank,4));Assert.AreEqual(PlayableEntityKind.Explorer,d.Assess(o,P,ai,Budget(o)).Candidates.Single().Action.UnitKind);
    }
    [TestCase(PlayableEntityKind.Tank,PlayableResearchKind.TankChassis)][TestCase(PlayableEntityKind.Explorer,PlayableResearchKind.ExplorerAssaultGuns)]
    [TestCase(PlayableEntityKind.Shkval,PlayableResearchKind.ShkvalGuidance)][TestCase(PlayableEntityKind.Hawk,PlayableResearchKind.HawkSalvoSystems)]
    public void SpecializedResearchRequiresFiveLivingMatchingUnits(PlayableEntityKind kind,PlayableResearchKind research)
    {
        var queue=Enumerable.Range(1,5).Select(i=>new PlayableProductionOrderSnapshot(i,kind,PlayableUnitRules.Cost(P,kind),PlayableUnitRules.Population(P,kind),PlayableUnitRules.Duration(P,kind),0,i==1)).ToArray();
        Assert.False(AiTechnologyPlanner.ArmySizeAllows(View(Enumerable.Repeat(kind,4),queue:queue),P,research));Assert.True(AiTechnologyPlanner.ArmySizeAllows(View(Enumerable.Repeat(kind,5)),P,research));
    }
    [Test] public void HeavyResearchIssuesArmorThenAttackThenChassisThroughActualPlan()
    {
        var army=Enumerable.Repeat(PlayableEntityKind.Tank,10).ToArray();var complete=new List<PlayableResearchOrderSnapshot>();var d=Policy(AiDoctrine.Heavy);
        foreach(var expected in new[]{PlayableResearchKind.ImprovedDefensiveSystems,PlayableResearchKind.ImprovedWeaponSystems,PlayableResearchKind.TankChassis})
        {
            var o=View(army,research:complete.ToArray());var ai=AiProfile.Initial;var rules=AiArmyResearchPolicy.Observe(o,P,AiDifficulty.Fighter,AiGamePhase.Mid,new AiDecisionArbiter(ai));
            var plan=new AiTechnologyPlanner().Plan(o,P,ai,Budget(o),armyPolicy:rules,doctrine:d);Assert.NotNull(plan.Action,plan.Reason);Assert.AreEqual(expected,plan.Action.ResearchKind);
            complete.Add(new PlayableResearchOrderSnapshot(complete.Count+1,expected,5,PlayableResearchRules.Cost(P,expected),PlayableResearchRules.Duration(P,expected),PlayableResearchRules.Duration(P,expected),false,true));
        }
    }
    [TestCase(AiDoctrine.HawksExplorers)][TestCase(AiDoctrine.HawksTanks)]
    public void ReadyAirCoreSavesItsActualCostBeforeMoreCheapUnits(AiDoctrine kind)
    {
        var d=Policy(kind);var o=View(Enumerable.Repeat(kind==AiDoctrine.HawksExplorers?PlayableEntityKind.Explorer:PlayableEntityKind.Tank,4));
        var budget=new AiBudgetLedger(o.OwnerId,o.Generation);budget.ObserveBank(o.Credits);
        budget.SetSafetyReserve(o.Credits-PlayableUnitRules.Cost(P,PlayableEntityKind.Tank));
        var waiting=d.Assess(o,P,AiProfile.Initial,budget.Capture());
        Assert.False(waiting.Candidates.Any(c=>c.Legal),"Do not repeatedly consume the air-core deficit savings on a cheaper unit");
        budget.SetSafetyReserve(o.Credits-PlayableUnitRules.Cost(P,PlayableEntityKind.Hawk));
        var funded=d.Assess(o,P,AiProfile.Initial,budget.Capture());
        Assert.AreEqual(PlayableCommandKind.QueueHawk,funded.Candidates.Single(c=>c.Legal).Action.Kind);
    }
    [Test] public void AbsentAirProducerDoesNotFreezeTheReadyGroundLine()
    {
        var d=Policy(AiDoctrine.HawksExplorers);var o=View(Enumerable.Repeat(PlayableEntityKind.Explorer,4),hangar:false);
        var budget=new AiBudgetLedger(o.OwnerId,o.Generation);budget.ObserveBank(o.Credits);budget.SetSafetyReserve(o.Credits-P.ExplorerCreditCost);
        Assert.AreEqual(PlayableCommandKind.QueueExplorer,d.Assess(o,P,AiProfile.Initial,budget.Capture()).Candidates.Single(c=>c.Legal).Action.Kind);
    }
    [Test] public void DoctrineCurrentWirePreservesCommittedChoice()
    {
        var a=Policy(AiDoctrine.HawksExplorers);byte[] Save(AiDoctrinePolicy p){using var m=new MemoryStream();using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true))p.WriteState(w);return m.ToArray();}
        var bytes=Save(a);var b=Policy(AiDoctrine.Tanks);using(var m=new MemoryStream(bytes))using(var r=new BinaryReader(m))b.ReadState(r,100000);CollectionAssert.AreEqual(bytes,Save(b));
        var o=View(new[]{PlayableEntityKind.Tank});Assert.AreEqual(a.Assess(o,P,AiProfile.Initial,Budget(o)).Candidates.Single().Action.Kind,b.Assess(o,P,AiProfile.Initial,Budget(o)).Candidates.Single().Action.Kind);
    }

    [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Explorer)][TestCase(PlayableEntityKind.Shkval)]
    public void AcceptedFactoryStartupPaysTypedCyclesThroughLedgerArbiterAndCurrentWire(PlayableEntityKind kind)
    {
        var ai=AiProfile.Initial;int cycles=(int)ai.Value("economy.factoryLaunchCycles"),cost=PlayableUnitRules.Cost(P,kind);var o=View();var site=TerritoryRules.Sites(P).First(s=>s.Kind==PlayableBuildingKind.Headquarters);
        var factory=new PlayableBuildingSnapshot(4,PlayableOwner.Player,PlayableBuildingKind.Factory,site.Slots[0].Position,100,1,0,0,default,siteId:site.Id,slotId:site.Slots[0].Id,parentId:1);
        // The current ordinary Shkval contract requires a ready own science
        // center; this control concerns the accepted typed startup earmark.
        var buildings=new[]{factory}.Concat(kind==PlayableEntityKind.Shkval?new[]{new PlayableBuildingSnapshot(5,PlayableOwner.Player,PlayableBuildingKind.ScientificCenter,site.Slots[1].Position,100,1,0,0,default)}:Array.Empty<PlayableBuildingSnapshot>()).ToArray();
        o=PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,41,1,100000,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,100000,null,Array.Empty<PlayableEntitySnapshot>(),buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:new[]{new TerritorySiteSnapshot(site,PlayableOwner.Player,PlayableOwner.Player,1,false,1,true)},population:new PlayablePopulationSnapshot(0,0,100),ownerId:"owner"));
        var budget=new AiBudgetLedger(o.OwnerId,71);budget.ObserveBank(o.Credits);budget.SetSafetyReserve(o.Credits-cycles*cost);
        var build=new PlayableAiAction(1,o.OwnerId,P.ProfileId,P.Revision,71,1,PlayableCommandKind.BuildAt,siteId:site.Id,slotId:site.Slots[0].Id,parentId:1,buildingKind:PlayableBuildingKind.Factory,unitKind:kind,seed:41,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        var intent=new AiIntent("build","economy",build,100500,0,0,Array.Empty<string>(),startupFund:cycles*cost);var receipt=new AiReceiptIdentity(71,o.OwnerId,1,1);var launches=new AiFactoryLaunchCommitments();launches.Begin(intent,receipt,o.Tick,budget,P,ai);launches.Terminal(intent,receipt,PlayableAiDeliveryStatus.Applied,true,budget,P);launches.Observe(o,budget,P);
        Assert.Zero(budget.Available);var d=Policy(AiDoctrine.Explorers);var action=d.Assess(o,P,ai,budget.Capture()).Candidates.Single().Action;Assert.AreEqual(PlayableUnitRules.ProductionCommand(kind),action.Kind,"accepted launch kind survives a later doctrine choice");
        string id=launches.ReservationFor(action);Assert.NotNull(id);var q=new AiIntent("queue","production",action,100500,cost,PlayableUnitRules.Population(P,kind),Array.Empty<string>(),reservationId:id);
        Assert.AreEqual(1,new AiDecisionArbiter(ai).Select(new[]{q},o.Tick,o.Credits,100,1,budget).Count,"typed earmark is admitted even when all unheld cash is protected");
        using var bytes=new MemoryStream();using(var w=new BinaryWriter(bytes,System.Text.Encoding.UTF8,true))launches.WriteState(w);bytes.Position=0;AiFactoryLaunchCommitments restored;using(var reader=new BinaryReader(bytes,System.Text.Encoding.UTF8,true))restored=AiFactoryLaunchCommitments.ReadState(reader,o.Tick);Assert.AreEqual(id,restored.ReservationFor(action));
        var paid=new AiReceiptIdentity(71,o.OwnerId,2,1);restored.Use(q,paid,budget,P);restored.Terminal(q,paid,PlayableAiDeliveryStatus.Applied,true,budget,P);Assert.AreEqual((cycles-1)*cost,budget.ReservationAmount(id));
    }
}
