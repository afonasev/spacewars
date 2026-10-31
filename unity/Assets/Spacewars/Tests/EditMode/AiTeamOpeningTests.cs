using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
public sealed class AiTeamOpeningTests
{
    private static readonly PlayableProfile P=PlayableProfile.Default;
    private static OfflineParticipant[] Team(bool three=false,bool otherBot=false)=>three?new[]{new OfflineParticipant("owner",1,1,OfflineControl.Ai),new OfflineParticipant("ally1",2,1,otherBot?OfflineControl.Ai:OfflineControl.Human),new OfflineParticipant("ally2",3,1,OfflineControl.Human)}:new[]{new OfflineParticipant("owner",1,1,OfflineControl.Ai),new OfflineParticipant("ally1",2,1,otherBot?OfflineControl.Ai:OfflineControl.Human)};
    private static PlayableAiObservation View(int seed,int income,OfflineParticipant[] participants,string support,long tick=100)
    {
        var site=TerritoryRules.Sites(P).First(s=>s.Kind==PlayableBuildingKind.Headquarters);var bs=new System.Collections.Generic.List<PlayableBuildingSnapshot>{new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Headquarters,site.Position,P.HeadquartersHealth,1,0,0,default,siteId:site.Id)};
        for(int i=0;i<income;i++)bs.Add(new PlayableBuildingSnapshot(10+i,PlayableOwner.Player,PlayableBuildingKind.Refinery,site.Slots[i].Position,100,1,0,0,default,siteId:site.Id,slotId:site.Slots[i].Id,parentId:1));
        void Ally(int index,PlayableBuildingKind kind,int id,ConstructionPhase phase=ConstructionPhase.Ready)=>bs.Add(new PlayableBuildingSnapshot(id,(PlayableOwner)index,kind,new NavPoint(40,index*10),phase==ConstructionPhase.Ready?100:0,phase==ConstructionPhase.Ready?1:0,0,0,default,phase:phase,includePrivateState:false));
        if(support.Contains("factory"))Ally(1,PlayableBuildingKind.Factory,100,ConstructionPhase.Pending);
        if(support.Contains("hangar"))Ally(support.Contains("bot-hangar")?1:participants.Length-1,PlayableBuildingKind.Hangar,200,ConstructionPhase.Pending);
        if(support.Contains("greedy"))for(int i=0;i<3;i++)Ally(1,PlayableBuildingKind.Refinery,110+i,i==2?ConstructionPhase.Constructing:ConstructionPhase.Ready);
        if(support.Contains("all-greedy")&&participants.Length==3)for(int i=0;i<3;i++)Ally(2,PlayableBuildingKind.Refinery,120+i,i==2?ConstructionPhase.Pending:ConstructionPhase.Ready);
        var unit=new PlayableEntitySnapshot(50,PlayableOwner.Player,PlayableEntityKind.Explorer,site.Position,P.ExplorerHealth,false,0,0,0);
        return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,seed,1,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,10000,null,new[]{unit},bs.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:new[]{new TerritorySiteSnapshot(site,PlayableOwner.Player,PlayableOwner.Player,1,false,1,true)},population:new PlayablePopulationSnapshot(1,0,100),ownerId:"owner",homeSiteId:site.Id,team:1,participants:participants));
    }
    private static int Seed(OfflineParticipant[] team)=>Enumerable.Range(0,1000).First(seed=>AiOpeningExecutor.TeamAirOwner(View(seed,0,team,""),AiProfile.Initial)=="owner");
    [TestCase(PlayableAiOpening.Safe,2)][TestCase(PlayableAiOpening.BlindRush,2)][TestCase(PlayableAiOpening.ExplorerAllIn,2)][TestCase(PlayableAiOpening.Air,2)][TestCase(PlayableAiOpening.GreedySafe,3)]
    public void EveryTeamWinnerWaitsForItsRequiredReadyIncomeBeforeRealHangarCandidate(PlayableAiOpening opening,int required)
    {
        var team=Team();int seed=Seed(team);var ai=AiProfile.Initial;var e=new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(seed,"owner",forcedOpening:opening),71){PlayableProfileForObservation=P};var o=View(seed,required-1,team,"factory");e.Observe(o,null,null,ai);Assert.False(e.Air);Assert.False(e.ProducerChosen);Assert.False(e.MacroRequests(o,P,ai,Array.Empty<AiEconomyCandidate>()).Any(c=>c.Legal&&c.Action.BuildingKind==PlayableBuildingKind.Hangar));
        o=View(seed,required,team,"factory");e.Observe(o,null,null,ai);Assert.True(e.Air);Assert.True(e.MacroRequests(o,P,ai,Array.Empty<AiEconomyCandidate>()).Any(c=>c.Legal&&c.Action.BuildingKind==PlayableBuildingKind.Hangar));
    }
    [TestCase("all-greedy",true)][TestCase("greedy",false)][TestCase("factory-hangar",true)][TestCase("hangar",false)]
    public void HumanObservedIncomeAndProductionFollowGroundPriorityAndBoundedWait(string support,bool air)
    {
        var team=Team(three:true);int seed=Seed(team);var ai=AiProfile.Initial;var e=new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(seed,"owner",forcedOpening:PlayableAiOpening.Safe),71){PlayableProfileForObservation=P};var o=View(seed,2,team,support);e.Observe(o,null,null,ai);
        if(!e.ProducerChosen){o=View(seed,2,team,support,250);e.Observe(o,null,null,ai);}Assert.True(e.ProducerChosen);Assert.AreEqual(air,e.Air);
        var expected=air?PlayableBuildingKind.Hangar:PlayableBuildingKind.Factory;Assert.True(e.MacroRequests(o,P,ai,Array.Empty<AiEconomyCandidate>()).Any(c=>c.Legal&&c.Action.Kind==PlayableCommandKind.BuildAt&&c.Action.BuildingKind==expected));
    }
    [Test] public void AnotherPaidBotHangarPreventsSecondAiAirEvenWithGroundHalf()
    {
        var team=Team(three:true,otherBot:true);int seed=Seed(team);var ai=AiProfile.Initial;var e=new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(seed,"owner",forcedOpening:PlayableAiOpening.Safe),71){PlayableProfileForObservation=P};e.Observe(View(seed,2,team,"factory-bot-hangar"),null,null,ai);Assert.True(e.ProducerChosen);Assert.False(e.Air);
    }
    [Test] public void GreedyAiSupportUsesActualDurablePublication()
    {
        AiTeamPublication P(string id,PlayableAiOpening opening)=>new AiTeamPublication{Owner=id,Team=1,Opening=opening,Tick=100,Mission=new AiArmyMissionState(),Targets=Array.Empty<AiOpponentAssessment>(),RequestedSites=Array.Empty<int>()};
        var board=AiTeamCoordinator.Resolve(new[]{P("owner",PlayableAiOpening.Safe),P("ally1",PlayableAiOpening.GreedySafe)},AiProfile.Initial);Assert.True(board["owner"].GreedyAllies.Contains("ally1"));Assert.False(board["ally1"].GreedyAllies.Contains("owner"));
    }
}
