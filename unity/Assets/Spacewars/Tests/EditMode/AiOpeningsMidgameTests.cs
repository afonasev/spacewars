using System;
using System.Linq;
using NUnit.Framework;
using System.IO;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
public sealed class AiOpeningsMidgameTests
{
    [Test] public void CatalogUsesAgreedUnmodifiedWeightsAndRealAirProducer()
    {
        var ai=AiProfile.Initial;
        Assert.AreEqual(5,AiOpeningCatalog.All.Count);
        CollectionAssert.AreEqual(new[]{40d,20d,20d,10d,10d},AiOpeningCatalog.All.Select(x=>ai.Value(x.WeightPath)).ToArray());
        Assert.AreEqual(AiMobilityClass.Aircraft,AiRosterCatalog.Initial.For(PlayableEntityKind.Hawk).mobility);
        Assert.AreEqual(PlayableBuildingKind.Hangar,AiRosterCatalog.Initial.Production(PlayableCommandKind.QueueHawk).producer);
        Assert.Greater(AiRosterCatalog.Initial.For(PlayableEntityKind.Explorer).lineWeight,0);
        Assert.AreEqual(PlayableAiOpening.GreedySafe,AiOpeningCatalog.For(PlayableAiOpening.GreedyMine).Opening);
    }
    [Test] public void OpeningPickRepeatsAndAirIsInOrdinarySelection()
    {
        int air=0;
        for(int seed=0;seed<200;seed++)
        {var a=PlayableAiOpeningComposition.Initialize(seed,"enemy-1");Assert.True(a.SemanticallyEquals(PlayableAiOpeningComposition.Initialize(seed,"enemy-1")));if(a.Opening==PlayableAiOpening.Air)air++;}
        Assert.Greater(air,0);Assert.Less(air,60);
    }

    private static PlayableAiObservation View(int income=3,bool mine=false,bool factory=false,int queue=0,int seed=771205,PlayableEntityKind unitKind=PlayableEntityKind.Hawk,int unitCount=4,string owner="air-owner",OfflineParticipant[] participants=null)
    {
        var p=PlayableProfile.Default;var slots=Enumerable.Range(1,5).Select(i=>new TerritorySlot(i,new NavPoint(i*9,10),0)).ToArray();
        var site=new TerritorySite(1,PlayableBuildingKind.Headquarters,new NavPoint(0,0),slots);
        var bs=new System.Collections.Generic.List<PlayableBuildingSnapshot>{new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Headquarters,site.Position,p.HeadquartersHealth,1,0,0,default,siteId:1,phase:ConstructionPhase.Ready)};
        for(int i=1;i<=income;i++)bs.Add(new PlayableBuildingSnapshot(1+i,PlayableOwner.Player,PlayableBuildingKind.Refinery,slots[i-1].Position,p.RefineryHealth,1,0,0,default,siteId:1,slotId:i,parentId:1,phase:ConstructionPhase.Ready));
        var orders=Enumerable.Range(1,queue).Select(i=>new PlayableProductionOrderSnapshot(i,PlayableEntityKind.Explorer,p.ExplorerCreditCost,p.ExplorerPopulationCost,PlayableUnitRules.Duration(p,PlayableEntityKind.Explorer),PlayableUnitRules.Duration(p,PlayableEntityKind.Explorer),i==1)).ToArray();
        bs.Add(new PlayableBuildingSnapshot(10,PlayableOwner.Player,factory?PlayableBuildingKind.Factory:PlayableBuildingKind.Hangar,slots[3].Position,100,1,queue,0,default,siteId:1,slotId:4,parentId:1,phase:ConstructionPhase.Ready,orders:orders));
        if(mine)bs.Add(new PlayableBuildingSnapshot(11,PlayableOwner.Player,PlayableBuildingKind.Mine,new NavPoint(0,20),100,1,0,0,default,siteId:2,phase:ConstructionPhase.Ready));
        if(participants!=null)foreach(var participant in participants.Where(x=>x.Id!=owner))bs.Add(new PlayableBuildingSnapshot(20+Array.IndexOf(participants,participant),(PlayableOwner)Array.IndexOf(participants,participant),PlayableBuildingKind.Factory,new NavPoint(50,0),100,1,0,0,default,phase:ConstructionPhase.Ready));
        var units=Enumerable.Range(0,unitCount).Select(i=>new PlayableEntitySnapshot(100+i,PlayableOwner.Player,unitKind,new NavPoint(0,0),PlayableUnitRules.Health(p,unitKind),false,0,0,0)).Concat(unitKind==PlayableEntityKind.Hawk?new[]{new PlayableEntitySnapshot(50,PlayableOwner.Player,PlayableEntityKind.Explorer,default,p.ExplorerHealth,false,0,0,0)}:Array.Empty<PlayableEntitySnapshot>()).ToArray();
        return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,seed,1,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,10000,null,units,bs.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:new[]{new TerritorySiteSnapshot(site,PlayableOwner.Player,PlayableOwner.Player,1,false,1,true)},population:new PlayablePopulationSnapshot(4,0,100),owner:PlayableOwner.Player,ownerId:owner,homeSiteId:1,team:1,participants:participants));
    }
    [Test] public void CalmAirRetainsLastSlotAndFundsFactoryAfterFourthActualIncome()
    {
        var p=PlayableProfile.Default;var ai=AiProfile.Initial;var opening=PlayableAiOpeningComposition.Initialize(771205,"air-owner",forcedOpening:PlayableAiOpening.Air);
        var executor=new AiOpeningExecutor(opening,71){PlayableProfileForObservation=p};var o=View();executor.Observe(o,null,null,ai);
        var before=executor.MacroRequests(o,p,ai,Array.Empty<AiEconomyCandidate>());
        Assert.False(before.Any(c=>c.Legal&&c.Action.BuildingKind==PlayableBuildingKind.Refinery),"last legal slot belongs to mandatory Factory");
        Assert.False(before.Any(c=>c.Legal&&c.Policy=="production"),"do not spend mandatory transition funds on a fifth Hawk");
        o=View(mine:true);executor.Observe(o,null,null,ai);
        Assert.True(executor.MacroRequests(o,p,ai,Array.Empty<AiEconomyCandidate>()).Any(c=>c.Legal&&c.Action.Kind==PlayableCommandKind.BuildAt&&c.Action.BuildingKind==PlayableBuildingKind.Factory));
    }
    [TestCase(false)][TestCase(true)] public void ScoutKeepsTheSelectedApproachWhenPublicSiteHasTwoRoles(bool reversed)
    {
        var p=PlayableProfile.Default;var ai=AiProfile.Initial;
        var unit=new PlayableEntitySnapshot(13,PlayableOwner.Player,PlayableEntityKind.Explorer,default,p.ExplorerHealth,false,0,0,0);
        var targets=new[]{new PlayablePublicScoutObjective(2,new NavPoint(17,0),PlayablePublicScoutObjectiveRole.PossibleEnemyStart,true),
            new PlayablePublicScoutObjective(2,new NavPoint(20,0),PlayablePublicScoutObjectiveRole.ExpansionOutpost,true)};
        if(reversed)Array.Reverse(targets);
        var routes=targets.Select(t=>new PlayableRouteProof(unit.Id,unit.Owner,71,100,1,unit.Position,p.ExplorerCollisionRadius,PlayableRouteTargetKind.PublicObjective,t.SiteId,t.Approach,t.Approach,new[]{t.Approach})).ToArray();
        var o=PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,7,100,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,new[]{unit},Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,publicScoutObjectives:targets,routeProofs:routes,ownerId:"air-owner"));
        var knowledge=new AiKnowledgeState(o.OwnerId,o.Generation,o.Tick,Array.Empty<AiKnownContact>(),Array.Empty<AiVisitedArea>());
        var registry=new AiArmyRegistry(o.OwnerId,o.Generation,ai,AiDifficulty.Fighter);var planner=new AiScoutPlanner();
        var action=planner.Plan(o,knowledge,registry,p,ai,2);Assert.NotNull(action);
        Assert.AreEqual(new NavPoint(17,0),action.Target,"Use the proof for this selected approach, independent of role ordering.");
        planner.Commit(o,action,registry);planner.Observe(o,knowledge,registry,ai,2);
        Assert.AreEqual(action.Target,registry.Capture().Armies.Single().Rally,"The live claim keeps its accepted approach without assuming site IDs are unique objectives.");
        var covered=new AiKnowledgeState(o.OwnerId,o.Generation,o.Tick,Array.Empty<AiKnownContact>(),new[]{new AiVisitedArea(2,o.Tick,true,false)});
        planner.Observe(o,covered,registry,ai,2);Assert.Zero(registry.ArmyFor(unit.Id),"Genuine area coverage still releases scouting ownership.");
    }
    [Test] public void AirIncomeSavingsRequireTheChosenReadyHomeProducer()
    {
        var p=PlayableProfile.Default;var ai=AiProfile.Initial;
        var opening=PlayableAiOpeningComposition.Initialize(771205,"air-owner",forcedOpening:PlayableAiOpening.Air);
        var executor=new AiOpeningExecutor(opening,71){PlayableProfileForObservation=p};
        var early=View(income:1,unitCount:0);executor.Observe(early,null,null,ai);
        Assert.False(executor.CanFundIncomeCapture(early,ai),"One early income must not start a Mine expiry clock before the opening economy/producer is ready.");
        var ready=View(unitCount:0);executor.Observe(ready,null,null,ai);
        Assert.True(executor.CanFundIncomeCapture(ready,ai),"Actual Ready Hangar fulfills the Air producer gate; a ground Factory is not required.");
        Assert.False(executor.Capture().InitialForceFulfilled,"Capture eligibility does not fabricate the first wave or impose a blanket expansion ban.");
    }
    [Test] public void CapturerReleaseRequiresSpendableFundsAndOrdinaryQueueAdmission()
    {
        var p=PlayableProfile.Default;var o=View(factory:true);
        Assert.False(AiOpeningExecutor.ReplacementAvailable(o,p,0),"gross bank is reserved elsewhere");
        Assert.True(AiOpeningExecutor.ReplacementAvailable(o,p,p.ExplorerCreditCost));
        Assert.False(AiOpeningExecutor.ReplacementAvailable(View(factory:true,queue:PlayableDomain.MaximumProductionOrders),p,p.ExplorerCreditCost),"full Factory queue is not a replacement path");
    }
    [Test] public void TeamGreedyNeverAddsAnIndependentAirDraw()
    {
        var p=PlayableProfile.Default;var ai=AiProfile.Initial;
        for(int seed=0;seed<128;seed++)
        {
            var participants=new[]{new OfflineParticipant("a",1,1,OfflineControl.Ai),new OfflineParticipant("b",2,1,OfflineControl.Ai),new OfflineParticipant("c",3,1,OfflineControl.Ai)};
            int selected=0;
            foreach(string id in new[]{"a","b","c"})
            {var o=View(seed:seed,owner:id,participants:participants);var executor=new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(seed,id,forcedOpening:PlayableAiOpening.GreedySafe),71){PlayableProfileForObservation=p};executor.Observe(o,null,null,ai);if(executor.Air)selected++;}
            Assert.LessOrEqual(selected,1,"one team reservation, including greedy air");
        }
    }
    [Test] public void OpeningCurrentWirePreservesChoicesAndNextObservation()
    {
        var p=PlayableProfile.Default;var ai=AiProfile.Initial;var opening=PlayableAiOpeningComposition.Initialize(771205,"air-owner",forcedOpening:PlayableAiOpening.Air);var a=new AiOpeningExecutor(opening,71){PlayableProfileForObservation=p};a.Observe(View(),null,null,ai);
        byte[] Save(AiOpeningExecutor e){using var m=new MemoryStream();using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true))e.WriteState(w);return m.ToArray();}
        var bytes=Save(a);var b=new AiOpeningExecutor(opening,71){PlayableProfileForObservation=p};using(var m=new MemoryStream(bytes))using(var reader=new BinaryReader(m))b.ReadState(reader,100);
        CollectionAssert.AreEqual(bytes,Save(b));a.Observe(View(mine:true),null,null,ai);b.Observe(View(mine:true),null,null,ai);CollectionAssert.AreEqual(Save(a),Save(b));
    }

    [Test] public void CurrentAuthorityRoundtripKeepsExactContinuationThroughRealHostService()
    {
        var basis=Spacewars.Tests.EditMode.OfflineParticipantAuthorityTests.Config(2,false,seed:771205);
        var costs=new double[basis.Starts.Count,basis.Starts.Count];for(int i=0;i<basis.Starts.Count;i++)for(int j=0;j<basis.Starts.Count;j++)costs[i,j]=i==j?0:1;
        var config=new OfflineMatchConfiguration(basis.Profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,basis.Seed,basis.Roster.Select(p=>new OfflineParticipant(p.Id,p.LogicalPlayer,p.Team,OfflineControl.Ai)).ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs);
        var a=new PlayableAuthorityTick(config,71);PlayableAuthorityTick b=null;
        using var ra=new UnityHostRouteService();using var rb=new UnityHostRouteService();
        void Step(PlayableAuthorityTick t,UnityHostRouteService host){for(int tries=0;!t.TryAdvance();tries++){Assert.Less(tries,4096,"bounded real host admission");host.Service(t,64);}}
        try
        {
            for(int i=0;i<15;i++)Step(a,ra);
            var bytes=a.CaptureBytes();b=PlayableAuthorityTick.RestoreBytes(bytes,config);CollectionAssert.AreEqual(bytes,b.CaptureBytes());
            using(var old=PlayableWorldState.Decode(bytes)){BitConverter.GetBytes(AiStateWire.Version-1).CopyTo(old.AiAuthority,4);var rejected=Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(old.Encode(),config));StringAssert.Contains("unsupported authority schema",rejected.Message);}
            for(int i=0;i<30;i++){
                Step(a,ra);Step(b,rb);var left=a.CaptureBytes();var right=b.CaptureBytes();
                if(!left.SequenceEqual(right)){
                    string output=Environment.GetEnvironmentVariable("COMBAT_MICRO_MACRO_CURRENT_OUTPUT")??Path.GetFullPath(Path.Combine(".local/ai-combat-attention/macro-current-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff")));Directory.CreateDirectory(output);
                    File.WriteAllBytes(Path.Combine(output,"before.world"),bytes);File.WriteAllBytes(Path.Combine(output,"hot.world"),left);File.WriteAllBytes(Path.Combine(output,"cold.world"),right);
                    object Fields(byte[] value){using var state=PlayableWorldState.Decode(value);using var hash=System.Security.Cryptography.SHA256.Create();return typeof(PlayableWorldState).GetFields().Where(f=>f.FieldType==typeof(byte[])).Select(f=>{var data=(byte[])f.GetValue(state);return new{Name=f.Name,Length=data?.Length??0,Hash=data==null?null:BitConverter.ToString(hash.ComputeHash(data)).Replace("-","")};}).ToArray();}
                    string summary=Newtonsoft.Json.JsonConvert.SerializeObject(new{Tick=a.Tick,Index=i,Hot=Fields(left),Cold=Fields(right)});File.WriteAllText(Path.Combine(output,"difference.json"),summary);TestContext.WriteLine("CURRENT_DIVERGENCE "+summary);
                }
                CollectionAssert.AreEqual(left,right,"exact current continuation tick "+i);
            }
        }
        finally{a.Stop();ra.Dispose();rb.Dispose();a.Domain.Navigation.DestroyNavigationAuthority();if(b!=null){b.Stop();b.Domain.Navigation.DestroyNavigationAuthority();}}
    }

    [TestCase(false)][TestCase(true)] public void AggressiveContinuationKeepsActualCandidatesAndFundingSeparate(bool reinforce)
    {
        var p=PlayableProfile.Default;var ai=AiProfile.Initial;var o=View(income:1,factory:true,unitKind:PlayableEntityKind.Explorer,unitCount:2);
        var opening=PlayableAiOpeningComposition.Initialize(771205,"air-owner",forcedOpening:PlayableAiOpening.BlindRush);var e=new AiOpeningExecutor(opening,71){PlayableProfileForObservation=p,AggressivePersonality=true};e.Observe(o,null,null,ai);
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        typeof(AiOpeningExecutor).GetField("forceKind",flags).SetValue(e,PlayableEntityKind.Explorer);typeof(AiOpeningExecutor).GetField("mixedForce",flags).SetValue(e,false);e.Observe(o,null,null,ai);
        typeof(AiOpeningExecutor).GetField("reinforce",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(e,reinforce);
        var candidates=e.MacroRequests(o,p,ai,Array.Empty<AiEconomyCandidate>());
        Assert.AreEqual(reinforce,candidates.Any(c=>c.Legal&&c.Policy=="production"));
        Assert.AreEqual(!reinforce,candidates.Any(c=>c.Legal&&c.Action.Kind==PlayableCommandKind.BuildAt&&c.Action.BuildingKind==PlayableBuildingKind.Refinery));
        Assert.True(e.HasContinuationCommitment(ai));
        if(reinforce)Assert.Greater(e.InitialForceFundingProtection(o,p,ai,null),0);
        var loop=new PlayableAiOwnerLoop(p,opening,71,null,ai,AiDifficulty.Fighter);
        typeof(PlayableAiOwnerLoop).GetField("openingExecutor",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(loop,e);
        Assert.AreEqual(0,typeof(PlayableAiOwnerLoop).GetProperty("ScoutBudget",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(loop),"completed live force is still committed until real pressure deployment");
        var registry=new AiArmyRegistry(o.OwnerId,o.Generation,ai,AiDifficulty.Fighter);
        Assert.True(registry.TryCreate(o,AiArmyRole.Attack,AiArmyPlanner.Policy,o.Entities.Select(u=>u.Id),out var army,requiredMinimum:2));
        Assert.True(o.Entities.All(u=>registry.ArmyFor(u.Id)==army),"both combat Explorers transfer atomically to first attack");
    }

    [TestCase(PlayableEntityKind.Tank,false)][TestCase(PlayableEntityKind.Explorer,false)][TestCase(PlayableEntityKind.Tank,true)]
    public void FirstWaveUsesDesiredKindsInFundingProductionFulfilmentAndArmy(PlayableEntityKind kind,bool mixed)
    {
        var p=PlayableProfile.Default;var ai=AiProfile.Initial;var opening=PlayableAiOpeningComposition.Initialize(771205,"air-owner",forcedOpening:PlayableAiOpening.BlindRush);var e=new AiOpeningExecutor(opening,71){PlayableProfileForObservation=p};
        var empty=View(income:1,factory:true,unitKind:PlayableEntityKind.Explorer,unitCount:1);e.Observe(empty,null,null,ai);
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;typeof(AiOpeningExecutor).GetField("forceKind",flags).SetValue(e,kind);typeof(AiOpeningExecutor).GetField("mixedForce",flags).SetValue(e,mixed);
        var desired=e.DesiredForce(ai,2);Assert.AreEqual(2,desired.Values.Sum());
        var candidates=e.MacroRequests(empty,p,ai,Array.Empty<AiEconomyCandidate>()).Where(c=>c.Legal&&c.Policy=="production").ToArray();Assert.True(candidates.All(c=>desired.ContainsKey(c.Action.UnitKind)));
        if(kind==PlayableEntityKind.Tank&&!mixed){Assert.AreEqual(0,e.InitialMembers(empty,ai).Length);Assert.False(e.Capture().InitialForceFulfilled);Assert.True(candidates.Any(c=>c.Action.Kind==PlayableCommandKind.QueueTank));}
        var own=desired.SelectMany(x=>Enumerable.Range(0,x.Value).Select((n)=>new PlayableEntitySnapshot(200+(int)x.Key*20+n,PlayableOwner.Player,x.Key,default,PlayableUnitRules.Health(p,x.Key),false,0,0,0))).ToArray();
        var basis=View(income:1,factory:true,unitCount:0);var snapshot=new PlayableSnapshot(p.ProfileId,p.Revision,71,771205,2,101,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,10000,null,own,basis.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:basis.Sites.ToArray(),population:new PlayablePopulationSnapshot(2,0,100),ownerId:"air-owner",homeSiteId:1);
        var o=PlayableAiObservation.From(snapshot);e.Observe(o,null,null,ai);Assert.True(e.Capture().InitialForceFulfilled);
        var ids=e.InitialMembers(o,ai);var registry=new AiArmyRegistry(o.OwnerId,71,ai,AiDifficulty.Fighter);Assert.True(registry.TryCreate(o,AiArmyRole.Attack,AiArmyPlanner.Policy,ids,out var id,requiredMinimum:2));
        CollectionAssert.AreEquivalent(own.Select(u=>u.Id),registry.Capture().Armies.Single().Members);
        foreach(var k in desired)Assert.AreEqual(k.Value,own.Count(u=>u.Kind==k.Key));
    }

    [Test] public void DistinctFactoryAndAirWaveQuotesAddButHeldAndUnpaidFundsCountOnce()
    {
        var p=PlayableProfile.Default;var ai=AiProfile.Initial;var o=View(unitCount:0);var e=new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(771205,"air-owner",forcedOpening:PlayableAiOpening.Air),71){PlayableProfileForObservation=p};
        // Loss of the sole capturer also makes the recovery Factory mandatory alongside the air wave.
        var snapshot=new PlayableSnapshot(p.ProfileId,p.Revision,71,771205,1,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,10000,null,Array.Empty<PlayableEntitySnapshot>(),o.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:o.Sites.ToArray(),population:new PlayablePopulationSnapshot(0,0,100),ownerId:"air-owner",homeSiteId:1);
        o=PlayableAiObservation.From(snapshot);e.Observe(o,null,null,ai);var b=new AiBudgetLedger(o.OwnerId,71);b.ObserveBank(o.Credits);
        int units=e.InitialForceFundingProtection(o,p,ai,b.Capture()),building=e.HomeEconomyFundingProtection(o,p,ai,b.Capture());Assert.Greater(units,0);Assert.AreEqual(TerritoryRules.Cost(p,PlayableBuildingKind.Factory),building);Assert.Greater(units+building,Math.Max(units,building));
        var factory=e.MacroRequests(o,p,ai,Array.Empty<AiEconomyCandidate>()).First(c=>c.Legal&&c.Action.BuildingKind==PlayableBuildingKind.Factory&&c.Action.Kind==PlayableCommandKind.BuildAt).Action;
        Assert.True(b.Reserve(new AiBudgetEntry("factory-goal","building-goal",factory,building,1,100,400,"test goal",Array.Empty<string>(),p.ProfileId+"@"+p.Revision)));
        Assert.Zero(e.HomeEconomyFundingProtection(o,p,ai,b.Capture()));Assert.AreEqual(units,e.InitialForceFundingProtection(o,p,ai,b.Capture()));
        var hawk=new PlayableAiAction(1,o.OwnerId,p.ProfileId,p.Revision,71,1,PlayableCommandKind.QueueHawk,new[]{10},unitKind:PlayableEntityKind.Hawk,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);int cost=PlayableUnitRules.Cost(p,PlayableEntityKind.Hawk);
        Assert.True(b.Reserve(new AiBudgetEntry("launch-goal","factory-startup",hawk,cost,1,100,400,"test unit hold",Array.Empty<string>(),p.ProfileId+"@"+p.Revision)));
        Assert.AreEqual(units-cost,e.InitialForceFundingProtection(o,p,ai,b.Capture()),"the same funded cycle is not protected twice");
        var pending=new AiBudgetLedger(o.OwnerId,71);pending.ObserveBank(o.Credits);Assert.True(pending.Accept(new AiBudgetEntry("paid-later","economy",factory,building,1,100,400,"pending invoice",Array.Empty<string>(),p.ProfileId+"@"+p.Revision,receipt:new AiReceiptIdentity(71,o.OwnerId,1,1))));
        Assert.Zero(e.HomeEconomyFundingProtection(o,p,ai,pending.Capture()),"accepted unpaid Factory already covers its own quote");
    }
    [Test] public void TeamWinnerSurvivesProfileApplyBeforeProducerDecision()
    {
        var p=PlayableProfile.Default;var ai=AiProfile.Initial;var participants=new[]{new OfflineParticipant("a",1,1,OfflineControl.Ai),new OfflineParticipant("b",2,1,OfflineControl.Ai),new OfflineParticipant("c",3,1,OfflineControl.Ai)};
        int seed=Enumerable.Range(0,128).First(s=>AiOpeningExecutor.TeamAirOwner(View(seed:s,owner:"a",participants:participants),ai)=="a");
        var e=new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(seed,"a",forcedOpening:PlayableAiOpening.Safe),71){PlayableProfileForObservation=p};e.Observe(View(income:0,seed:seed,owner:"a",participants:participants),null,null,ai);
        var changed=ai.CopyData();changed.revision++;foreach(var f in changed.fields.Where(f=>f.path=="openingsMidgame.teamAir2"||f.path=="openingsMidgame.teamAir3"))f.value=0;
        e.Observe(View(seed:seed,owner:"a",participants:participants),null,null,new AiProfile(changed));Assert.True(e.Air,"genesis team winner is durable, not rerolled by supported profile apply");
    }
}
