using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

public sealed class AiParallelConstructionTests
{
    private static PlayableAiObservation View(int paid,int ready=0,int credits=2000,bool producer=false)
    {
        var p=PlayableProfile.Default;var slots=Enumerable.Range(1,5).Select(i=>new TerritorySlot(i,new NavPoint(i*9,10),0)).ToArray();
        var site=new TerritorySite(1,PlayableBuildingKind.Headquarters,default,slots);
        var buildings=new System.Collections.Generic.List<PlayableBuildingSnapshot>{new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Headquarters,default,p.HeadquartersHealth,1,0,0,default,siteId:1,phase:ConstructionPhase.Ready)};
        for(int i=0;i<paid;i++)buildings.Add(new PlayableBuildingSnapshot(2+i,PlayableOwner.Player,PlayableBuildingKind.Refinery,slots[i].Position,i<ready?p.RefineryHealth:0,i<ready?1:.2,0,0,default,siteId:1,slotId:i+1,parentId:1,phase:i<ready?ConstructionPhase.Ready:i%2==0?ConstructionPhase.Pending:ConstructionPhase.Constructing));
        if(producer)buildings.Add(new PlayableBuildingSnapshot(10,PlayableOwner.Player,PlayableBuildingKind.Factory,slots[4].Position,p.FactoryHealth,1,0,0,default,siteId:1,slotId:5,parentId:1,phase:ConstructionPhase.Ready));
        return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,123,1,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,credits,null,Array.Empty<PlayableEntitySnapshot>(),buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:new[]{new TerritorySiteSnapshot(site,PlayableOwner.Player,PlayableOwner.Player,1,false,1,true)},population:new PlayablePopulationSnapshot(0,0,100),ownerId:"parallel-owner",homeSiteId:1,team:1));
    }
    private static AiOpeningExecutor Opening(PlayableAiOpening kind)=>new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(123,"parallel-owner",forcedOpening:kind),71){PlayableProfileForObservation=PlayableProfile.Default};
    [TestCase(PlayableAiOpening.Safe,2)] [TestCase(PlayableAiOpening.GreedySafe,3)]
    public void PendingIncomeDoesNotBlockNextRequiredIncomeOrOverfillTarget(PlayableAiOpening kind,int target)
    {
        var p=PlayableProfile.Default;var ai=AiProfile.Initial;var e=Opening(kind);var o=View(1);e.Observe(o,null,null,ai);
        Assert.True(e.MacroRequests(o,p,ai,Array.Empty<AiEconomyCandidate>()).Any(c=>c.Legal&&c.Action.BuildingKind==PlayableBuildingKind.Refinery));
        o=View(target);e.Observe(o,null,null,ai);var next=e.MacroRequests(o,p,ai,Array.Empty<AiEconomyCandidate>());
        Assert.False(next.Any(c=>c.Legal&&c.Action.BuildingKind==PlayableBuildingKind.Refinery),"paid targets must not be duplicated");
        Assert.True(next.Any(c=>c.Legal&&c.Action.BuildingKind==PlayableBuildingKind.Factory),"producer can overlap paid income foundations");
        Assert.False(e.HomeEconomyReady(o,ai));Assert.False(e.Capture().Completed.Contains("income"),"no future income readiness");
        Assert.True(next.Where(c=>c.Legal).All(c=>c.Action.SlotId>target),"occupied slots remain illegal");
    }
    [Test] public void AggressiveProducerOverlapsFirstIncomeAndAffordabilityStillApplies()
    {
        var p=PlayableProfile.Default;var ai=AiProfile.Initial;var e=Opening(PlayableAiOpening.BlindRush);var o=View(1,credits:p.FactoryCreditCost);e.Observe(o,null,null,ai);
        Assert.True(e.MacroRequests(o,p,ai,Array.Empty<AiEconomyCandidate>()).Any(c=>c.Legal&&c.Action.BuildingKind==PlayableBuildingKind.Factory));
        o=View(1,credits:p.FactoryCreditCost-1);e.Observe(o,null,null,ai);
        Assert.False(e.MacroRequests(o,p,ai,Array.Empty<AiEconomyCandidate>()).Any(c=>c.Legal&&c.Action.BuildingKind==PlayableBuildingKind.Factory));
    }
    [Test] public void EarlyReadyProducerRetainsMilestoneDependenciesAndExactCurrentWire()
    {
        var ai=AiProfile.Initial;var e=Opening(PlayableAiOpening.BlindRush);e.Observe(View(1,producer:true),null,null,ai);
        Assert.False(e.Capture().Completed.Contains("factory"),"ready producer alone cannot complete income milestone");
        byte[] Save(AiOpeningExecutor x){using var m=new MemoryStream();using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true))x.WriteState(w);return m.ToArray();}
        var bytes=Save(e);var restored=Opening(PlayableAiOpening.BlindRush);using(var m=new MemoryStream(bytes))using(var r=new BinaryReader(m))restored.ReadState(r,100);
        CollectionAssert.AreEqual(bytes,Save(restored));
        var complete=View(1,1,producer:true);e.Observe(complete,null,null,ai);restored.Observe(complete,null,null,ai);
        CollectionAssert.AreEqual(Save(e),Save(restored));CollectionAssert.Contains(e.Capture().Completed,"income");CollectionAssert.Contains(e.Capture().Completed,"factory");
    }
    [Test] public void RecordOrdinaryAggressiveSeedWithoutRunningGame()
    {
        var ai=NativeBalanceFields.InitialAiOpeningProfile();
        int seed=Enumerable.Range(1,1000).First(s=>PlayableAiOpeningComposition.Initialize(s,"parallel-owner",aiProfile:ai).Opening==PlayableAiOpening.BlindRush);
        TestContext.WriteLine("PARALLEL_PREFLIGHT seed="+seed+" profile="+ai.Id+"@"+ai.Revision+":"+ai.Hash);
    }
}
