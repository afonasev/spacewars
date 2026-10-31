using System;
using System.Linq;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
public sealed class AiPersonalityTests
{
    private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly PlayableProfile P=PlayableProfile.Default;
    private static PlayableAiObservation At(long tick,bool threat=false)
    {
        var basis=AiDoctrineTests.View(Enumerable.Repeat(PlayableEntityKind.Tank,10));var units=basis.Entities.Concat(threat?new[]{new PlayableEntitySnapshot(999,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(1,0),P.TankHealth,false,0,0,0)}:Array.Empty<PlayableEntitySnapshot>()).ToArray();
        return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,41,1,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,100000,null,units,basis.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,population:new PlayablePopulationSnapshot(10,0,1000),ownerId:"owner"));
    }
    private static AiProfile ScheduledProfile()
    {
        var data=AiProfile.Initial.CopyData();data.revision++;
        foreach(var f in data.fields)
        {if(f.path=="openingsMidgame.adaptation.probability"||f.path=="openingsMidgame.adaptation.secondProbability")f.value=1;if(f.path=="openingsMidgame.adaptation.firstMaxSeconds")f.value=180;}
        return new AiProfile(data);
    }
    private static AiDoctrinePolicy Scheduled(AiProfile ai)
    {
        var d=new AiDoctrinePolicy(PlayableAiOpeningComposition.Initialize(41,"owner",aiProfile:ai),71){SpendableCredits=100000};typeof(AiDoctrinePolicy).GetField("chosen",F).SetValue(d,true);typeof(AiDoctrinePolicy).GetField("chosenTick",F).SetValue(d,100L);typeof(AiDoctrinePolicy).GetField("doctrine",F).SetValue(d,AiDoctrine.Tanks);return d;
    }
    private static byte[] Save(AiDoctrinePolicy d){using var m=new MemoryStream();using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true))d.WriteState(w);return m.ToArray();}
    [Test] public void EasyNeverHarassesEvenWithAirDoctrineAndOtherDifficultiesCanHarass()
    {
        var ai=AiProfile.Initial;bool normalHarass=false;
        for(int seed=0;seed<500;seed++)
        {var opening=PlayableAiOpeningComposition.Initialize(seed,"owner");Assert.AreNotEqual(AiPersonality.Harassing,AiDoctrinePolicy.InitialPersonality(opening,ai,AiDifficulty.Recruit));normalHarass|=AiDoctrinePolicy.InitialPersonality(opening,ai,AiDifficulty.Fighter)==AiPersonality.Harassing;}
        Assert.True(normalHarass);var d=AiDoctrineTests.Policy(AiDoctrine.Air);typeof(AiDoctrinePolicy).GetField("personality",F).SetValue(d,AiPersonality.Harassing);Assert.False(d.HarassAllowed(AiDifficulty.Recruit));Assert.True(d.HarassAllowed(AiDifficulty.Fighter));
        var scouts=new AiScoutPlanner();Assert.Greater(scouts.Budget(ai,AiDifficulty.Fighter,0,AiScoutPlan.Standard),scouts.Budget(ai,AiDifficulty.Recruit,uint.MaxValue,AiScoutPlan.Standard));Assert.GreaterOrEqual(scouts.Budget(ai,AiDifficulty.Veteran,0,AiScoutPlan.Standard),2);
    }
    [Test] public void RareChangesWaitForDefenseAndAffordabilityKeepClocksAndStopAfterTwo()
    {
        var ai=ScheduledProfile();var d=Scheduled(ai);var executor=new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(41,"owner",aiProfile:ai),71);
        d.Observe(At(100),P,ai,executor,AiDifficulty.Fighter);var first=d.Capture();Assert.AreEqual(5500,first.NextCompositionChangeTick);var pending=first.PendingDoctrine;
        for(long t=101;t<120;t++)d.Observe(At(t),P,ai,executor,AiDifficulty.Fighter);Assert.AreEqual(first.NextCompositionChangeTick,d.Capture().NextCompositionChangeTick);Assert.AreEqual(pending,d.Capture().PendingDoctrine);
        d.Observe(At(5500,true),P,ai,executor,AiDifficulty.Fighter);Assert.Zero(d.Capture().CompositionChanges);d.SpendableCredits=0;d.Observe(At(5501),P,ai,executor,AiDifficulty.Fighter);Assert.Zero(d.Capture().CompositionChanges,"gross credits cannot make an unaffordable change");
        d.SpendableCredits=100000;d.Observe(At(5502),P,ai,executor,AiDifficulty.Fighter);Assert.AreEqual(1,d.Capture().CompositionChanges);Assert.AreEqual(12702,d.Capture().NextCompositionChangeTick);
        var bytes=Save(d);var restored=Scheduled(ai);using(var m=new MemoryStream(bytes))using(var r=new BinaryReader(m))restored.ReadState(r,5502);CollectionAssert.AreEqual(bytes,Save(restored));
        foreach(long t in new[]{12701L,12702L,100000L}){d.Observe(At(t),P,ai,executor,AiDifficulty.Fighter);restored.Observe(At(t),P,ai,executor,AiDifficulty.Fighter);CollectionAssert.AreEqual(Save(d),Save(restored));}
        Assert.AreEqual(2,d.Capture().CompositionChanges);Assert.AreEqual(-1,d.Capture().NextCompositionChangeTick);Assert.LessOrEqual(d.Capture().PersonalityChanges,2);
    }
    [Test] public void TwoMissionChannelsKeepDistinctTargetsOwnershipAndCurrentState()
    {
        var ai=AiProfile.Initial;var units=Enumerable.Range(100,6).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Tank,default,P.TankHealth,false,0,0,0)).ToArray();
        var targets=new[]{new PlayableBuildingSnapshot(500,PlayableOwner.Enemy,PlayableBuildingKind.Refinery,new NavPoint(30,0),100,1,0,0,default),new PlayableBuildingSnapshot(501,PlayableOwner.Enemy,PlayableBuildingKind.Refinery,new NavPoint(-30,0),100,1,0,0,default)};
        var routes=units.SelectMany(u=>targets.Select(t=>new PlayableRouteProof(u.Id,u.Owner,71,100000,1,u.Position,PlayableUnitRules.Radius(P,u.Kind),PlayableRouteTargetKind.VisibleEnemy,t.Id,t.Position,t.Position,new[]{u.Position,t.Position}))).ToArray();
        var vision=new PlayableVision(0,50,50,1,1);vision.Refresh(new[]{new VisionSource(default,50)},Array.Empty<KnownBuilding>());
        var o=PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,41,1,100000,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,10000,null,units,targets,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot(),routeProofs:routes,ownerId:"owner"));
        var opening=PlayableAiOpeningComposition.Initialize(41,"owner",forcedOpening:PlayableAiOpening.Air).WithPhase(PlayableAiOpeningPhase.Complete);var registry=new AiArmyRegistry("owner",71,ai,AiDifficulty.Fighter);
        var main=new AiArmyPlanner("owner",71){SplitLargeArmies=true,Personality=AiPersonality.Aggressive};main.Rebind(P);var a=main.Propose(o,opening,P,ai,registry);Assert.NotNull(a);Assert.AreEqual(3,a.EntityIds.Count);main.Commit(o,a,registry);
        var flank=new AiArmyPlanner("owner",71,AiArmyPlanner.FlankPolicy){SplitLargeArmies=true,HarassAllowed=true,Personality=AiPersonality.Harassing,OtherTargetId=a.TargetId};flank.Rebind(P);var b=flank.Propose(o,opening,P,ai,registry);Assert.NotNull(b);Assert.AreNotEqual(a.TargetId,b.TargetId);Assert.False(a.EntityIds.Intersect(b.EntityIds).Any());flank.Commit(o,b,registry);
        Assert.AreEqual(2,registry.Capture().Armies.Count(x=>x.Major));Assert.AreEqual(AiArmyRole.Raid,registry.Capture().Armies.Single(x=>x.TacticalOwner==AiArmyPlanner.FlankPolicy).Role);Assert.DoesNotThrow(()=>registry.AssertInvariants());
        using var stream=new MemoryStream();using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))flank.WriteState(writer);stream.Position=0;var copy=new AiArmyPlanner("owner",71,AiArmyPlanner.FlankPolicy);using(var reader=new BinaryReader(stream,System.Text.Encoding.UTF8,true))copy.ReadState(reader,o.Tick,registry);Assert.AreEqual(flank.Capture().ArmyId,copy.Capture().ArmyId);Assert.AreEqual(flank.PendingId,copy.PendingId);
    }
    [Test] public void PendingProducersOccupyTheirIntentWithoutDuplicateBuildOrders()
    {
        var site=TerritoryRules.Sites(P).First(s=>s.Kind==PlayableBuildingKind.Headquarters);
        PlayableAiObservation View(bool deadReadyFactory=false)
        {
            var bs=new[]{new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Headquarters,site.Position,P.HeadquartersHealth,1,0,0,default,siteId:site.Id),new PlayableBuildingSnapshot(4,PlayableOwner.Player,PlayableBuildingKind.Factory,site.Slots[0].Position,0,0,0,0,default,siteId:site.Id,slotId:site.Slots[0].Id,parentId:1,phase:deadReadyFactory?ConstructionPhase.Ready:ConstructionPhase.Pending),new PlayableBuildingSnapshot(5,PlayableOwner.Player,PlayableBuildingKind.Hangar,site.Slots[1].Position,0,0,0,0,default,siteId:site.Id,slotId:site.Slots[1].Id,parentId:1,phase:ConstructionPhase.Constructing)};
            return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,41,1,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,10000,null,Array.Empty<PlayableEntitySnapshot>(),bs,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:new[]{new TerritorySiteSnapshot(site,PlayableOwner.Player,PlayableOwner.Player,1,false,1,true)},ownerId:"owner"));
        }
        var d=AiDoctrineTests.Policy(AiDoctrine.HawksExplorers);for(int i=0;i<3;i++)Assert.IsEmpty(d.ProducerRequests(View(),P));Assert.True(d.ProducerRequests(View(true),P).Any(c=>c.Legal&&c.Action.BuildingKind==PlayableBuildingKind.Factory));Assert.False(d.ProducerRequests(View(true),P).Any(c=>c.Action.BuildingKind==PlayableBuildingKind.Hangar));
    }

    [TestCase("opening.")][TestCase("ground.")][TestCase("combined.")][TestCase("personality.")]
    public void EditableWeightGroupsRejectEmptyButAllowIndividualZero(string group)
    {
        var d=AiProfile.Initial.CopyData();foreach(var f in d.fields.Where(f=>f.path.StartsWith("openingsMidgame."+group)&&f.path!= "openingsMidgame.personality.maximumChanges"&&f.path!="openingsMidgame.personality.holdSeconds"&&f.path!="openingsMidgame.personality.harassingScoutScale"&&f.path!="openingsMidgame.personality.harassingRetreatRatio"))f.value=0;
        Assert.Throws<ArgumentException>(()=>new AiProfile(d));
        d=AiProfile.Initial.CopyData();d.fields.Single(f=>f.path=="openingsMidgame."+(group=="personality."?"personality.harassing":group=="opening."?"opening.air":group=="ground."?"ground.explorers":"combined.hawksExplorers")).value=0;Assert.DoesNotThrow(()=>new AiProfile(d));
    }
    [TestCase("reinforcedMin","reinforcedMax")][TestCase("allInMin","allInMax")][TestCase("outpostMin","outpostMax")][TestCase("adaptation.firstMinSeconds","adaptation.firstMaxSeconds")][TestCase("airFirst","airCalm")][TestCase("airFactoryThreat","airFactoryCalm")]
    public void EditableRangesRejectInversion(string minimum,string maximum)
    {
        var d=AiProfile.Initial.CopyData();var lo=d.fields.Single(f=>f.path=="openingsMidgame."+minimum);var hi=d.fields.Single(f=>f.path=="openingsMidgame."+maximum);lo.value=hi.value+(minimum.EndsWith("Seconds")?10:1);Assert.Throws<ArgumentException>(()=>new AiProfile(d));
    }

    [Test] public void DisabledAlternativeWeightsDoNotForceOrSpendRandomTransition()
    {
        var data=ScheduledProfile().CopyData();foreach(var f in data.fields.Where(f=>f.path.StartsWith("openingsMidgame.ground.")))f.value=f.path.EndsWith("tanks")?1:0;var ai=new AiProfile(data);var d=Scheduled(ai);var executor=new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(41,"owner",aiProfile:ai),71);
        d.Observe(At(100),P,ai,executor,AiDifficulty.Fighter);d.Observe(At(100000),P,ai,executor,AiDifficulty.Fighter);Assert.AreEqual(AiDoctrine.Tanks,d.Capture().Doctrine);Assert.Zero(d.Capture().CompositionChanges);Assert.AreEqual(-1,d.Capture().NextCompositionChangeTick);
    }

    [Test] public void UsefulScoutProductionIsBoundedAndDedicatedScoutsDoNotFillCombatRatio()
    {
        var ai=AiProfile.Initial;var d=AiDoctrineTests.Policy(AiDoctrine.HawksExplorers);var army=Enumerable.Repeat(PlayableEntityKind.Hawk,4).Concat(Enumerable.Repeat(PlayableEntityKind.Explorer,3)).ToArray();var o=AiDoctrineTests.View(army);d.UtilityAvailable=id=>id>=104;d.CombatCountable=id=>id<104;
        var b=new AiBudgetLedger(o.OwnerId,o.Generation);b.ObserveBank(o.Credits);var a=d.Assess(o,P,ai,b.Capture(),3,true).Candidates.Single().Action;Assert.AreEqual(PlayableCommandKind.QueueExplorer,a.Kind,"three dedicated scouts cannot stand in for the combat Explorer component");
        d=AiDoctrineTests.Policy(AiDoctrine.Tanks);o=AiDoctrineTests.View(Enumerable.Repeat(PlayableEntityKind.Tank,3));b=new AiBudgetLedger(o.OwnerId,o.Generation);b.ObserveBank(o.Credits);Assert.AreEqual(PlayableCommandKind.QueueExplorer,d.Assess(o,P,ai,b.Capture(),2,true).Candidates.Single().Action.Kind,"non-easy reconnaissance has a real replenishment path");
        o=AiDoctrineTests.View(Enumerable.Repeat(PlayableEntityKind.Tank,3).Concat(Enumerable.Repeat(PlayableEntityKind.Explorer,2)));b=new AiBudgetLedger(o.OwnerId,o.Generation);b.ObserveBank(o.Credits);Assert.AreEqual(PlayableCommandKind.QueueTank,d.Assess(o,P,ai,b.Capture(),2,true).Candidates.Single().Action.Kind,"once useful reconnaissance is funded, ordinary combat production resumes");
    }
    [Test] public void DuePureAirAdaptationRequestsAffordableFactoryAndWaitsForRealReadyProducer()
    {
        var ai=AiProfile.Initial;var d=AiDoctrineTests.Policy(AiDoctrine.Air);d.SpendableCredits=100000;
        foreach(var pair in new[]{("airOpening",(object)true),("scheduleChosen",(object)true),("personalityChosen",(object)true),("nextCompositionChange",(object)100L),("pendingDoctrine",(object)AiDoctrine.HawksExplorers)})typeof(AiDoctrinePolicy).GetField(pair.Item1,F).SetValue(d,pair.Item2);
        var site=TerritoryRules.Sites(P).First(s=>s.Kind==PlayableBuildingKind.Headquarters);
        PlayableAiObservation View(long tick,ConstructionPhase? factory=null,bool threat=false)
        {
            var bs=new[]{new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Headquarters,site.Position,P.HeadquartersHealth,1,0,0,default,siteId:site.Id),new PlayableBuildingSnapshot(5,PlayableOwner.Player,PlayableBuildingKind.Hangar,site.Slots[0].Position,100,1,0,0,default,siteId:site.Id,slotId:site.Slots[0].Id,parentId:1)}.Concat(factory.HasValue?new[]{new PlayableBuildingSnapshot(4,PlayableOwner.Player,PlayableBuildingKind.Factory,site.Slots[1].Position,factory==ConstructionPhase.Ready?100:0,factory==ConstructionPhase.Ready?1:0,0,0,default,siteId:site.Id,slotId:site.Slots[1].Id,parentId:1,phase:factory.Value)}:Array.Empty<PlayableBuildingSnapshot>()).ToArray();
            var units=threat?new[]{new PlayableEntitySnapshot(999,PlayableOwner.Enemy,PlayableEntityKind.Tank,site.Position,P.TankHealth,false,0,0,0)}:Array.Empty<PlayableEntitySnapshot>();
            return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,41,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,100000,null,units,bs,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:new[]{new TerritorySiteSnapshot(site,PlayableOwner.Player,PlayableOwner.Player,1,false,1,true)},ownerId:"owner",population:new PlayablePopulationSnapshot(0,0,1000)));
        }
        var executor=new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(41,"owner"),71);
        Assert.IsEmpty(d.ProducerRequests(View(99),P));d.SpendableCredits=0;Assert.IsEmpty(d.ProducerRequests(View(100),P));d.SpendableCredits=100000;Assert.IsEmpty(d.ProducerRequests(View(100,threat:true),P));
        var request=d.ProducerRequests(View(100),P).First(c=>c.Legal);Assert.AreEqual(PlayableBuildingKind.Factory,request.Action.BuildingKind);Assert.IsNull(AiEconomyAdmission.Reject(View(100),P,request.Action,true));Assert.AreEqual(AiDoctrine.Air,d.Capture().Doctrine);Assert.Zero(d.Capture().CompositionChanges);
        foreach(var phase in new[]{ConstructionPhase.Pending,ConstructionPhase.Constructing}){Assert.IsEmpty(d.ProducerRequests(View(101,phase),P));d.Observe(View(101,phase),P,ai,executor,AiDifficulty.Fighter);Assert.AreEqual(AiDoctrine.Air,d.Capture().Doctrine);Assert.Zero(d.Capture().CompositionChanges);}
        d.Observe(View(102,ConstructionPhase.Ready),P,ai,executor,AiDifficulty.Fighter);Assert.AreEqual(AiDoctrine.HawksExplorers,d.Capture().Doctrine);Assert.AreEqual(1,d.Capture().CompositionChanges);
    }
    [Test] public void OwnerLoopArbiterCommitsPrimaryBeforeSecondaryAndKeepsDifferentTargets()
    {
        var ai=AiProfile.Initial;var opening=PlayableAiOpeningComposition.Initialize(41,"owner");var loop=new PlayableAiOwnerLoop(P,opening,71,null,ai,AiDifficulty.Veteran);
        var executor=(AiOpeningExecutor)typeof(PlayableAiOwnerLoop).GetField("openingExecutor",F).GetValue(loop);typeof(AiOpeningExecutor).GetField("phase",F).SetValue(executor,PlayableAiOpeningPhase.Complete);
        var units=Enumerable.Range(100,6).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Tank,default,P.TankHealth,false,0,0,0)).ToArray();
        var targets=new[]{new PlayableBuildingSnapshot(500,PlayableOwner.Enemy,PlayableBuildingKind.Refinery,new NavPoint(30,0),100,1,0,0,default),new PlayableBuildingSnapshot(501,PlayableOwner.Enemy,PlayableBuildingKind.Refinery,new NavPoint(-30,0),100,1,0,0,default)};
        var vision=new PlayableVision(0,50,50,1,1);vision.Refresh(new[]{new VisionSource(default,50)},Array.Empty<KnownBuilding>());
        PlayableSnapshot View(long tick)
        {
            var routes=units.SelectMany(u=>targets.Select(t=>new PlayableRouteProof(u.Id,u.Owner,71,tick,1,u.Position,PlayableUnitRules.Radius(P,u.Kind),PlayableRouteTargetKind.VisibleEnemy,t.Id,t.Position,t.Position,new[]{u.Position,t.Position}))).ToArray();
            return new PlayableSnapshot(P.ProfileId,P.Revision,71,41,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,10000,null,units,targets,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot(),routeProofs:routes,ownerId:"owner");
        }
        loop.Review(View(100000),0);var first=(PlayableAiOwnerCheckpoint)typeof(PlayableAiOwnerLoop).GetMethod("Capture",F).Invoke(loop,null);Assert.AreEqual(1,first.Armies.Armies.Count(a=>a.Major));Assert.AreEqual(AiArmyPlanner.Policy,first.Armies.Armies.Single(a=>a.Major).TacticalOwner);
        loop.Review(View(100045),0);var second=(PlayableAiOwnerCheckpoint)typeof(PlayableAiOwnerLoop).GetMethod("Capture",F).Invoke(loop,null);var armies=second.Armies.Armies.Where(a=>a.Major).ToArray();Assert.AreEqual(2,armies.Length);Assert.AreEqual(2,armies.Select(a=>a.Objective).Distinct().Count());Assert.False(armies[0].Members.Intersect(armies[1].Members).Any());Assert.True(second.Records.Any(r=>r.Policy==AiArmyPlanner.FlankPolicy&&r.Status==PlayableAiDeliveryStatus.Scheduled));
        loop.Stop(100045);
    }
}
