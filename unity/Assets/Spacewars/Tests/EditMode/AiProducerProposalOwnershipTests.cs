using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
public sealed class AiProducerProposalOwnershipTests
{
    private static readonly PlayableProfile P=PlayableProfile.Default;
    private static PlayableSnapshot View(long tick,long sequence,bool hangar=true)
    {
        var slots=Enumerable.Range(1,5).Select(i=>new TerritorySlot(i,new NavPoint(i*9,10),0)).ToArray();var site=new TerritorySite(1,PlayableBuildingKind.Headquarters,default,slots);
        var buildings=new List<PlayableBuildingSnapshot>{new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Headquarters,default,P.HeadquartersHealth,1,0,0,default,siteId:1,phase:ConstructionPhase.Ready)};
        for(int i=1;i<=3;i++)buildings.Add(new PlayableBuildingSnapshot(i+1,PlayableOwner.Player,PlayableBuildingKind.Refinery,slots[i-1].Position,P.RefineryHealth,1,0,0,default,siteId:1,slotId:i,parentId:1,phase:ConstructionPhase.Ready));
        if(hangar)buildings.Add(new PlayableBuildingSnapshot(10,PlayableOwner.Player,PlayableBuildingKind.Hangar,slots[3].Position,P.HangarHealth,1,0,0,default,siteId:1,slotId:4,parentId:1,phase:ConstructionPhase.Ready));
        var units=Enumerable.Range(0,4).Select(i=>new PlayableEntitySnapshot(100+i,PlayableOwner.Player,PlayableEntityKind.Hawk,default,PlayableUnitRules.Health(P,PlayableEntityKind.Hawk),false,0,0,0)).ToArray();
        return new PlayableSnapshot(P.ProfileId,P.Revision,1,881549,sequence,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,300,null,units,buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:new[]{new TerritorySiteSnapshot(site,PlayableOwner.Player,PlayableOwner.Player,1,false,1,true)},population:new PlayablePopulationSnapshot(4,0,100),owner:PlayableOwner.Player,ownerId:"air-owner",homeSiteId:1,team:1);
    }
    [Test] public void OpeningRecoveryAndDoctrinePublishOneFactoryObligation()
    {
        var ai=NativeBalanceFields.InitialAiOpeningProfile();var opening=PlayableAiOpeningComposition.Initialize(881549,"air-owner",aiProfile:ai);Assert.AreEqual(PlayableAiOpening.Air,opening.Opening);
        var loop=new PlayableAiOwnerLoop(P,opening,1,null,ai,AiDifficulty.Veteran);var rows=new List<JObject>();
        var previous=PlayableAiOwnerLoop.CurrentSafeFundingReadbackForTests;var owner=PlayableAiOwnerLoop.CurrentSafeFundingOwnerFilterForTests;var filter=PlayableAiOwnerLoop.CurrentSafeFundingStageFilterForTests;Assert.IsNull(previous);
        try{
            PlayableAiOwnerLoop.CurrentSafeFundingOwnerFilterForTests="air-owner";PlayableAiOwnerLoop.CurrentSafeFundingStageFilterForTests=(_,stage)=>stage=="ActualCandidatesBeforeSelection";PlayableAiOwnerLoop.CurrentSafeFundingReadbackForTests=json=>rows.Add(JObject.Parse(json));
            const BindingFlags f=BindingFlags.Instance|BindingFlags.NonPublic;
            // The incident already had a fulfilled opening force before this doctrine decision.
            var prior=(AiOpeningExecutor)typeof(PlayableAiOwnerLoop).GetField("openingExecutor",f).GetValue(loop);prior.Observe(PlayableAiObservation.From(View(5955,1)),null,null,ai);
            loop.Review(View(6000,2),0);
            var executor=(AiOpeningExecutor)typeof(PlayableAiOwnerLoop).GetField("openingExecutor",f).GetValue(loop);var doctrine=(AiDoctrinePolicy)typeof(PlayableAiOwnerLoop).GetField("doctrine",f).GetValue(loop);var o=PlayableAiObservation.From(View(6000,2));
            Assert.True(executor.Capture().InitialForceFulfilled);Assert.True(doctrine.Ready);Assert.AreEqual(AiDoctrine.HawksExplorers,doctrine.Capture().Doctrine);Assert.False(o.Entities.Any(u=>u.Kind==PlayableEntityKind.Explorer));
            var macro=executor.MacroRequests(o,P,ai,Array.Empty<AiEconomyCandidate>()).Where(c=>c.Legal&&c.Action.BuildingKind==PlayableBuildingKind.Factory).ToArray();var raw=doctrine.ProducerRequests(o,P).Where(c=>c.Legal&&c.Action.BuildingKind==PlayableBuildingKind.Factory).ToArray();Assert.AreEqual(1,macro.Length);Assert.AreEqual(1,raw.Length);Assert.AreEqual(AiEconomyAdmission.IntentId(macro[0].Policy,macro[0].Action),AiEconomyAdmission.IntentId(raw[0].Policy,raw[0].Action),"the two actual planners request the same Factory slot");
            Assert.AreEqual(PlayableEntityKind.Explorer,doctrine.GroundProductionKind(o,ai),"Factory Proposal uses the live doctrine production kind");const string expected="economy:BuildAt::1:5:0:0:0:Factory:Explorer:TankChassis";TestContext.WriteLine("Q2_EXPECTED_RAW "+AiEconomyAdmission.IntentId(macro[0].Policy,macro[0].Action));TestContext.WriteLine("Q2_OWNER_CANDIDATES "+rows.Last());var actual=rows.Last()["Data"]["Proposals"].ToArray();Assert.AreEqual(actual.Length,actual.Select(x=>(string)x["Id"]).Distinct().Count(),"actual owner-loop arbitration keeps strict unique intents");Assert.AreEqual(1,actual.Count(x=>(string)x["Id"]==expected),"one actionable Factory obligation reaches Select");Assert.AreEqual(300,(int)actual.Single(x=>(string)x["Id"]==expected)["Credits"]);
        }finally{PlayableAiOwnerLoop.CurrentSafeFundingReadbackForTests=previous;PlayableAiOwnerLoop.CurrentSafeFundingOwnerFilterForTests=owner;PlayableAiOwnerLoop.CurrentSafeFundingStageFilterForTests=filter;loop.Stop();}
    }
    [Test] public void SharedProducerAdmissionRetainsDistinctKindsAndSlots()
    {
        var ai=NativeBalanceFields.InitialAiOpeningProfile();var opening=PlayableAiOpeningComposition.Initialize(881549,"air-owner",aiProfile:ai);var o=PlayableAiObservation.From(View(6000,2,hangar:false));
        var executor=new AiOpeningExecutor(opening,1){PlayableProfileForObservation=P};executor.Observe(PlayableAiObservation.From(View(5955,1)),null,null,ai);executor.Observe(o,null,null,ai);var doctrine=new AiDoctrinePolicy(opening,1);doctrine.Observe(o,P,ai,executor,AiDifficulty.Veteran);
        Assert.True(doctrine.Ready);var raw=doctrine.ProducerRequests(o,P).ToArray();Assert.True(raw.Any(c=>c.Legal&&c.Action.BuildingKind==PlayableBuildingKind.Factory));Assert.True(raw.Any(c=>c.Legal&&c.Action.BuildingKind==PlayableBuildingKind.Hangar));
        var merged=executor.MacroRequests(o,P,ai,Array.Empty<AiEconomyCandidate>(),doctrineProducers:raw).Where(c=>c.Legal&&c.Action.Kind==PlayableCommandKind.BuildAt&&(c.Action.BuildingKind==PlayableBuildingKind.Factory||c.Action.BuildingKind==PlayableBuildingKind.Hangar)).ToArray();
        Assert.AreEqual(4,merged.Length,"both real producer kinds retain both vacant slots");Assert.AreEqual(4,merged.Select(c=>AiEconomyAdmission.IntentId(c.Policy,c.Action)).Distinct().Count());
        foreach(var kind in new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Hangar})CollectionAssert.AreEquivalent(new[]{4,5},merged.Where(c=>c.Action.BuildingKind==kind).Select(c=>c.Action.SlotId).ToArray());
        Assert.True(merged.All(c=>c.Priority==1),"opening producer priority remains unchanged");
    }

}
