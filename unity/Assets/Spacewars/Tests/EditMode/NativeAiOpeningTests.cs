using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using Spacewars.Runtime;
using Spacewars.Presentation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiOpeningTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        private static object Get(object o,string name)=>o.GetType().GetField(name,F).GetValue(o);
        private static object Call(object o,string name,params object[] a)=>o.GetType().GetMethod(name,F).Invoke(o,a);
        private static object Owner(PlayableAuthorityTick a)=>((IEnumerable)Get(Get(a,"scheduler"),"owners")).Cast<object>().Single();
        private static AiOpeningExecutor Executor(PlayableAuthorityTick a)=>(AiOpeningExecutor)Get(Owner(a),"openingExecutor");
        private static OfflineMatchConfiguration Config(int seed=19092026,bool noFactorySlot=false)
        {
            var data=PlayableProfile.Default.CopyData();data.startingCredits=20000;var p=PlayableProfile.Create(data);
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-24,0),new NavPoint(-24,8),pin:1),new OfflineStart("east",2,new NavPoint(24,0),new NavPoint(24,8),pin:2)};
            // Authored slots stay inside the default arena including the validator's swept envelope.
            var slots=new[]{new TerritorySlot(1,new NavPoint(-24,-9),0),new TerritorySlot(2,new NavPoint(-14,0),0),new TerritorySlot(3,new NavPoint(-14,-9),0),new TerritorySlot(4,new NavPoint(-24,-18),0),new TerritorySlot(5,new NavPoint(-14,-18),0)};
            var sites=new[]{new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,slots),new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,Array.Empty<TerritorySlot>()),new TerritorySite(3,PlayableBuildingKind.Mine,new NavPoint(-10,20),p)};
            var scenario=noFactorySlot?slots.Select((s,i)=>new OfflineScenarioBuilding(1,i==0?PlayableBuildingKind.Refinery:PlayableBuildingKind.ScientificCenter,1,s.Id,s.Position,s.Heading)).ToArray():Array.Empty<OfflineScenarioBuilding>();
            return new OfflineMatchConfiguration(p,"o3-diagnostic-funded","o3-flat-two-starts","UnityHostRouteService",seed,new[]{new OfflineParticipant("west-owner",1,1,OfflineControl.Ai),new OfflineParticipant("east-owner",2,2,OfflineControl.Human)},starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,48},{48,0}},scenarioBuildings:scenario);
        }
        private static void Force(PlayableAuthorityTick a,PlayableAiOpening opening)
        {
            var owner=Owner(a);var state=PlayableAiOpeningComposition.Initialize(19092026,"west-owner",forcedOpening:opening);
            owner.GetType().GetField("opening",F).SetValue(owner,state);
            owner.GetType().GetField("openingExecutor",F).SetValue(owner,new AiOpeningExecutor(state,71));
        }
        private static void Step(PlayableAuthorityTick a,UnityHostRouteService host)
        {for(int i=0;i<512;i++){if(a.TryAdvance())return;host.Service(a,64);}Assert.Fail("native route barrier at tick "+a.Tick);}
        [Test] public void InitialForceFundingProtectsOnlyTheUncoveredCurrentRoster()
        {
            var p=PlayableProfile.Default;var ai=AiProfile.Initial;
            var e=new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(7,"player-1",forcedOpening:PlayableAiOpening.GreedySafe),71);
            var o=Observation(1,true);e.Observe(o,null,null,ai);var target=e.DesiredForce(ai,e.RequiredForce);
            var ledger=new AiBudgetLedger(o.OwnerId,o.Generation);ledger.ObserveBank(1000);
            int total=target.Sum(x=>x.Value*PlayableUnitRules.Cost(p,x.Key));
            Assert.Greater(total,0);Assert.AreEqual(total,e.InitialForceFundingProtection(o,p,ai,ledger.Capture()));
            foreach(var x in target)
            {
                var action=new PlayableAiAction(1,o.OwnerId,p.ProfileId,p.Revision,o.Generation,0,PlayableUnitRules.ProductionCommand(x.Key),new[]{2},unitKind:x.Key,seed:7,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
                string id="factory-startup:"+x.Key;
                Assert.True(ledger.Reserve(new AiBudgetEntry(id,"factory-startup",action,(x.Value-1)*PlayableUnitRules.Cost(p,x.Key),1,0,100,"paid launch cycles",new[]{id},p.ProfileId+"@"+p.Revision)));
            }
            int uncovered=target.Sum(x=>PlayableUnitRules.Cost(p,x.Key));
            Assert.AreEqual(uncovered,e.InitialForceFundingProtection(o,p,ai,ledger.Capture()),"startup holds cover only matching current unit kinds");
            var expense=new AiIntent("remote-growth","expansion",ledger.Capture().Reserved.First().Action,100,(int)ledger.Available,0,Array.Empty<string>());
            Assert.True(AiExpansionFinancial.FullyFundable(ledger,0,expense));
            Assert.False(AiExpansionFinancial.FullyFundable(ledger,uncovered,expense),"discretionary expansion cannot consume the uncovered force quote");
            var orders=target.SelectMany(x=>Enumerable.Range(1,x.Value).Select(i=>new PlayableProductionOrderSnapshot((int)x.Key*100+i,x.Key,PlayableUnitRules.Cost(p,x.Key),PlayableUnitRules.Population(p,x.Key),PlayableUnitRules.Duration(p,x.Key),PlayableUnitRules.Duration(p,x.Key),i==1))).ToArray();
            var buildings=o.Buildings.Select(b=>b.Kind!=PlayableBuildingKind.Factory?b:new PlayableBuildingSnapshot(b.Id,b.Owner,b.Kind,b.Position,b.Health,1,orders.Length,0,default,siteId:b.SiteId,slotId:b.SlotId,parentId:1,phase:ConstructionPhase.Ready,orders:orders)).ToArray();
            var queued=PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,Array.Empty<PlayableEntitySnapshot>(),buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:o.Sites.ToArray(),population:new PlayablePopulationSnapshot(0,orders.Sum(q=>q.PopulationCost),p.ArmyCapacity)));
            Assert.Zero(e.InitialForceFundingProtection(queued,p,ai,ledger.Capture()),"paid production covers the actual roster");
            foreach(var x in target)ledger.Release("factory-startup:"+x.Key);
            Assert.AreEqual(total,e.InitialForceFundingProtection(o,p,ai,ledger.Capture()));
            var fulfilled=WithForce(Observation(2,true),e,ai);e.Observe(fulfilled,null,null,ai);
            Assert.Zero(e.InitialForceFundingProtection(fulfilled,p,ai,ledger.Capture()),"actual living force ends the hold");
        }
        [TestCase(PlayableAiOpening.Safe)][TestCase(PlayableAiOpening.GreedySafe)][TestCase(PlayableAiOpening.Air)][TestCase(PlayableAiOpening.BlindRush)][TestCase(PlayableAiOpening.ExplorerAllIn)]
        public void AllTemplates_ExecuteOrdinaryMacroAndArmyRequests(PlayableAiOpening opening)
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);Force(a,opening);
            var ledger=(AiBudgetLedger)Get(Owner(a),"budget");
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<3650&&Executor(a).Capture().Phase==PlayableAiOpeningPhase.Active;i++)Step(a,host);
                var state=Executor(a).Capture();var paid=ledger.Capture().Paid;
                bool boundedAir=opening==PlayableAiOpening.Air&&state.Phase==PlayableAiOpeningPhase.Aborted;
                if(boundedAir)
                {
                    Assert.AreEqual(AiProfile.SecondsToTicks(AiProfile.Initial.Value("opening.deadlineSeconds"),30),a.Tick-state.StartedTick);
                    StringAssert.Contains("deadline",state.Reason);Assert.False(state.InitialForceFulfilled);
                    Assert.False(Executor(a).AllowsPressure,"fallback cannot shrink the initial air group");
                    Assert.True(Executor(a).InitialAttackPending);
                    var airView=PlayableAiObservation.From(a.ParticipantView("west-owner"));
                    Assert.True(airView.Buildings.Any(x=>x.Kind==PlayableBuildingKind.Hangar&&x.QueueCount>0),"the existing paid fourth Hawk remains in ordinary production");
                }
                else
                {
                    Assert.AreEqual(PlayableAiOpeningPhase.Complete,state.Phase,state.Template+": "+state.Reason+" completed="+string.Join(",",state.Completed));Assert.True(state.InitialForceFulfilled,"opening completion records its actual live initial force");
                    CollectionAssert.AreEquivalent(AiOpeningCatalog.For(opening).Milestones.Select(m=>m.Id),state.Completed);
                }
                Assert.True(paid.Any(e=>e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.BuildingKind==(opening==PlayableAiOpening.Air?PlayableBuildingKind.Hangar:PlayableBuildingKind.Factory)));
                Assert.True(paid.Any(e=>e.Action.Kind==PlayableCommandKind.QueueTank||e.Action.Kind==PlayableCommandKind.QueueExplorer||e.Action.Kind==PlayableCommandKind.QueueHawk));
                var view=PlayableAiObservation.From(a.ParticipantView("west-owner"));
                if(opening==PlayableAiOpening.GreedySafe)Assert.GreaterOrEqual(view.Buildings.Count(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Refinery&&b.Phase==ConstructionPhase.Ready),2);
                if(opening==PlayableAiOpening.Air)Assert.True(view.Entities.Any(u=>u.Owner==view.Owner&&u.Kind==PlayableEntityKind.Hawk));
                if(opening==PlayableAiOpening.BlindRush||opening==PlayableAiOpening.ExplorerAllIn)
                {
                    var army=(AiArmyPlanner)Get(Owner(a),"armyPlanner");var registry=(AiArmyRegistry)Get(Owner(a),"armies");
                    Assert.NotNull(army.Deployment(view,registry),"receipt plus effective Registry-owned order required");registry.AssertInvariants();
                }
                var bytes=a.CaptureBytes();var restored=PlayableAuthorityTick.RestoreBytes(bytes,c);CollectionAssert.AreEqual(bytes,restored.CaptureBytes());Assert.AreEqual(state.InitialForceFulfilled,((PlayableAiOwnerLoop)Owner(restored)).Checkpoint.OpeningExecution.InitialForceFulfilled,"the actual one-time latch survives exact whole-world restore");
                for(int i=0;i<90;i++){Step(a,host);Step(restored,host);CollectionAssert.AreEqual(a.CaptureBytes(),restored.CaptureBytes());}
            }
        }
        [Test] public void NormalSeeds_FixedBankCoversFiveAndProfileWeightsBindChoice()
        {
            var bank=Enumerable.Range(0,512).ToArray();var a=bank.Select(seed=>PlayableAiOpeningComposition.Initialize(seed,"owner-a")).ToArray();
            CollectionAssert.AreEquivalent(AiOpeningCatalog.All.Select(d=>d.Opening),a.Select(s=>s.Opening).Distinct());
            foreach(var state in a)Assert.True(state.SemanticallyEquals(PlayableAiOpeningComposition.Initialize(state.MatchSeed,state.OwnerId)));
            var data=AiProfile.Initial.CopyData();data.revision++;data.fields.Single(v=>v.path=="openingsMidgame.opening.greedy").value*=5;var ai=new AiProfile(data);
            var b=bank.Select(seed=>PlayableAiOpeningComposition.Initialize(seed,"owner-a",aiProfile:ai)).ToArray();
            Assert.Greater(b.Count(s=>s.Opening==PlayableAiOpening.GreedySafe),a.Count(s=>s.Opening==PlayableAiOpening.GreedySafe));
        }
        [Test] public void ExplorerAllIn_DefaultWeightRaisesItsShareAndKeepsEveryOpening()
        {
            const string weight="openingsMidgame.opening.allIn";
            var current=NativeBalanceFields.InitialAiOpeningProfile();
            var previous=current.CopyData();
            previous.fields.Single(f=>f.path==weight).value=10;
            previous.fields.Single(f=>f.path=="openingsMidgame.allInMax").value=5;
            var baseline=new AiProfile(previous);
            Assert.True(NativeBalanceFields.AiOpeningFields.Any(f=>f.Path==weight));
            double Share(AiProfile ai)=>ai.Value(weight)/AiOpeningCatalog.All.Sum(d=>ai.Value(d.WeightPath));
            Assert.AreEqual(0.1,Share(baseline),1e-10);
            Assert.AreEqual(1d/6d,Share(current),1e-10);
            var seats=new[]{"player-1","foundry-2","foundry-3","foundry-4"};
            foreach(var ai in new[]{baseline,current})
            {
                var choices=from seed in Enumerable.Range(-1024,2048) from seat in seats
                    select PlayableAiOpeningComposition.Initialize(seed,seat,aiProfile:ai).Opening;
                var counts=choices.GroupBy(x=>x).ToDictionary(g=>g.Key,g=>g.Count());
                CollectionAssert.AreEquivalent(AiOpeningCatalog.All.Select(d=>d.Opening),counts.Keys);
                double observed=counts[PlayableAiOpening.ExplorerAllIn]/8192d;
                Assert.AreEqual(Share(ai),observed,.02);
                TestContext.WriteLine($"{ai.Hash} "+string.Join(", ",counts.OrderBy(c=>c.Key).Select(c=>$"{c.Key}={c.Value}")));
            }
        }

        private static PlayableAiObservation Observation(long tick,bool factory=false,int tanks=0)
        {
            var p=PlayableProfile.Default;var site=TerritoryRules.Sites(p).First(s=>s.Kind==PlayableBuildingKind.Headquarters);
            var buildings=new System.Collections.Generic.List<PlayableBuildingSnapshot>{new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Headquarters,site.Position,100,1,0,0,default,siteId:site.Id)};
            for(int i=1;i<=3;i++){var slot=site.Slots[i];buildings.Add(new PlayableBuildingSnapshot(30+i,PlayableOwner.Player,PlayableBuildingKind.Refinery,slot.Position,p.RefineryHealth,1,0,0,default,siteId:site.Id,slotId:slot.Id,parentId:1,phase:ConstructionPhase.Ready));}
            if(factory)buildings.Add(new PlayableBuildingSnapshot(2,PlayableOwner.Player,PlayableBuildingKind.Factory,site.Slots[0].Position,100,1,0,0,default,siteId:site.Id,slotId:site.Slots[0].Id,parentId:1));
            var units=Enumerable.Range(10,tanks).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(id,0),100,false,0,0,0)).ToArray();
            return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,10000,null,units,buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:new[]{new TerritorySiteSnapshot(site,PlayableOwner.Player,PlayableOwner.Player,1,false,1,true)},population:new PlayablePopulationSnapshot(tanks,0,p.ArmyCapacity)));
        }
        private static PlayableAiObservation HomeEconomyObservation(int tick,int readyHomeRefineries,bool readyHomeFactory,bool readyNonhomeEconomy,bool publicEnemyStart=false,int tanks=0)
        {
            var p=PlayableProfile.Default;var home=TerritoryRules.Sites(p).First(s=>s.Kind==PlayableBuildingKind.Headquarters);
            var nonhome=new TerritorySite(99,PlayableBuildingKind.Outpost,new NavPoint(-10,20),p);
            var buildings=new System.Collections.Generic.List<PlayableBuildingSnapshot>{new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Headquarters,home.Position,100,1,0,0,default,siteId:home.Id)};
            int id=2;
            for(int i=0;i<readyHomeRefineries;i++){var slot=home.Slots[i];buildings.Add(new PlayableBuildingSnapshot(id++,PlayableOwner.Player,PlayableBuildingKind.Refinery,slot.Position,100,1,0,0,default,siteId:home.Id,slotId:slot.Id,parentId:1));}
            if(readyHomeFactory){var slot=home.Slots[readyHomeRefineries];buildings.Add(new PlayableBuildingSnapshot(id++,PlayableOwner.Player,PlayableBuildingKind.Factory,slot.Position,100,1,0,0,default,siteId:home.Id,slotId:slot.Id,parentId:1));}
            var sites=new System.Collections.Generic.List<TerritorySiteSnapshot>{new TerritorySiteSnapshot(home,PlayableOwner.Player,PlayableOwner.Player,1,false,1,true)};
            if(readyNonhomeEconomy)
            {
                buildings.Add(new PlayableBuildingSnapshot(50,PlayableOwner.Player,PlayableBuildingKind.Outpost,nonhome.Position,100,1,0,0,default,siteId:nonhome.Id));
                buildings.Add(new PlayableBuildingSnapshot(51,PlayableOwner.Player,PlayableBuildingKind.Refinery,nonhome.Slots[0].Position,100,1,0,0,default,siteId:nonhome.Id,slotId:nonhome.Slots[0].Id,parentId:50));
                buildings.Add(new PlayableBuildingSnapshot(52,PlayableOwner.Player,PlayableBuildingKind.Factory,nonhome.Slots[1].Position,100,1,0,0,default,siteId:nonhome.Id,slotId:nonhome.Slots[1].Id,parentId:50));
                sites.Add(new TerritorySiteSnapshot(nonhome,PlayableOwner.Player,PlayableOwner.Player,1,false,50,true));
            }
            var objectives=publicEnemyStart?new[]{new PlayablePublicScoutObjective(77,new NavPoint(24,24),PlayablePublicScoutObjectiveRole.PossibleEnemyStart,true)}:Array.Empty<PlayablePublicScoutObjective>();
            var units=Enumerable.Range(100,tanks).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(id,0),100,false,0,0,0)).ToArray();
            return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,10000,null,units,buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:sites.ToArray(),homeSiteId:home.Id,publicScoutObjectives:objectives,population:new PlayablePopulationSnapshot(tanks,0,p.ArmyCapacity)));
        }
        private static PlayableAiObservation WithForce(PlayableAiObservation o,AiOpeningExecutor e,AiProfile ai)
        {
            int id=100;var p=PlayableProfile.Default;
            var units=e.DesiredForce(ai,e.RequiredForce).SelectMany(x=>Enumerable.Range(0,x.Value).Select(_=>new PlayableEntitySnapshot(id++,o.Owner,x.Key,default,PlayableUnitRules.Health(p,x.Key),false,0,0,0))).ToArray();
            return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,o.Generation,o.Seed,o.SnapshotSequence,o.Tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,o.Credits,null,units,o.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:o.Sites.ToArray(),owner:o.Owner,ownerId:o.OwnerId,homeSiteId:o.HomeSiteId,population:new PlayablePopulationSnapshot(units.Sum(u=>PlayableUnitRules.Population(p,u.Kind)),0,p.ArmyCapacity)));
        }
        [Test] public void InitialForceLatchRequiresLiveOwnedTanksAndSurvivesLossReplan()
        {
            var ai=AiProfile.Initial;var e=new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(7,"player-1",forcedOpening:PlayableAiOpening.BlindRush),71);int required=(int)ai.Value("opening.forceUnits");
            e.Observe(Observation(1,true),null,null,ai);Assert.False(e.Capture().InitialForceFulfilled,"a ready factory is not a live force");
            e.Observe(WithForce(Observation(2,true),e,ai),null,null,ai);Assert.True(e.Capture().InitialForceFulfilled);CollectionAssert.Contains(e.Capture().Completed,"force");
            e.Observe(Observation(3,false),null,null,ai);Assert.False(e.Capture().Completed.Contains("factory"),"lost producer requires actual readiness again");Assert.True(e.Capture().InitialForceFulfilled,"later loss must not reset the one-time latch");
        }
        [Test] public void InitialForceLatchCanCompleteAfterOpeningAbort()
        {
            var ai=AiProfile.Initial;var e=new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(7,"player-1",forcedOpening:PlayableAiOpening.BlindRush),71);int required=(int)ai.Value("opening.forceUnits");
            e.Observe(Observation(1,true),null,null,ai);e.Observe(Observation(3601,true),null,null,ai);Assert.AreEqual(PlayableAiOpeningPhase.Aborted,e.Capture().Phase);Assert.False(e.Capture().InitialForceFulfilled);
            e.Observe(WithForce(Observation(3602,true),e,ai),null,null,ai);Assert.True(e.Capture().InitialForceFulfilled,"post-abort observations still latch actual living force");
        }
        [Test] public void FirstLiveForceReleasesGrowthWithoutAStandingArmyOrFreshReconGate()
        {
            var ai=AiProfile.Initial;var e=new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(7,"player-1",forcedOpening:PlayableAiOpening.Safe),71);int required=(int)ai.Value("opening.forceUnits");
            var first=HomeEconomyObservation(10,e.RequiredHomeRefineryCount(ai),true,false,true);e.Observe(first,null,null,ai);
            Assert.True(e.InitialGrowthCommitmentPending(first,ai),"the initial live force is still missing");
            var force=WithForce(HomeEconomyObservation(11,e.RequiredHomeRefineryCount(ai),true,false,true),e,ai);e.Observe(force,null,null,ai);Assert.True(e.Capture().InitialForceFulfilled);
            Assert.False(e.InitialGrowthCommitmentPending(force,ai),"actual live force ends the one-time growth protection without waiting for an intel freshness clock");
            var laterLoss=HomeEconomyObservation(12,e.RequiredHomeRefineryCount(ai),true,false,true);e.Observe(laterLoss,null,null,ai);
            Assert.True(e.Capture().InitialForceFulfilled);Assert.False(e.InitialGrowthCommitmentPending(laterLoss,ai),"later force loss cannot reopen the fulfilled initial gate");
        }
        [Test] public void ProfileRebindPreservesInitialForceLatch()
        {
            var ai=AiProfile.Initial;var opening=PlayableAiOpeningComposition.Initialize(7,"player-1",forcedOpening:PlayableAiOpening.BlindRush);
            var owner=new PlayableAiOwnerLoop(PlayableProfile.Default,opening,71,null,ai,AiDifficulty.Fighter);var executor=(AiOpeningExecutor)Get(owner,"openingExecutor");
            executor.Observe(Observation(1,true),null,null,ai);executor.Observe(WithForce(Observation(2,true),executor,ai),null,null,ai);Assert.True(executor.Capture().InitialForceFulfilled);
            var data=ai.CopyData();data.revision++;owner.Rebind(new AiProfile(data));Assert.True(executor.Capture().InitialForceFulfilled,"AI profile rebind preserves the historical actual-force fact");
        }
        [Test] public void AbortedOpeningDoesNotLeakOrdinaryGrowthBuildingsPastInitialForce()
        {
            var ai=AiProfile.Initial;var p=PlayableProfile.Default;var e=new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(7,"player-1",forcedOpening:PlayableAiOpening.BlindRush),71);
            e.Observe(Observation(1,true),null,null,ai);e.Observe(Observation(3601,true),null,null,ai);Assert.AreEqual(PlayableAiOpeningPhase.Aborted,e.Capture().Phase);
            var o=HomeEconomyObservation(3601,1,true,false);int producer=o.Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory).Id;var slot=o.Sites.Single(s=>s.Site.Id==o.HomeSiteId).Site.Slots.Last();
            var grow=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.BuildAt,siteId:o.HomeSiteId,slotId:slot.Id,parentId:o.Sites.Single(s=>s.Site.Id==o.HomeSiteId).CenterId,buildingKind:PlayableBuildingKind.Factory,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            var queue=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.QueueTank,new[]{producer},unitKind:PlayableEntityKind.Tank,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            var projected=e.MacroRequests(o,p,ai,new[]{new AiEconomyCandidate("economy",grow,null,1),new AiEconomyCandidate("production",queue,null,1)},true);
            Assert.True(projected.Any(c=>c.Policy=="production"&&c.Legal),"fallback retains actual production");Assert.False(projected.Any(c=>c.Action.Kind==PlayableCommandKind.BuildAt),"ordinary growth remains suppressed until actual initial force");
        }
        [Test] public void PrivateAiSchemaRejectsLegacyThirteenExplicitly()
        {
            using(var stream=new System.IO.MemoryStream())using(var writer=new System.IO.BinaryWriter(stream))
            {
                writer.Write(13);writer.Flush();stream.Position=0;using(var reader=new System.IO.BinaryReader(stream))
                    StringAssert.Contains("unsupported scheduler schema",Assert.Throws<ArgumentException>(()=>AiAuthorityScheduler.ReadState(reader,PlayableProfile.Default,AiProfile.Initial,71,7,0,Array.Empty<string>())).Message);
            }
        }
        [Test] public void InitialForceLatchRejectsMalformedBooleanTag()
        {
            var opening=PlayableAiOpeningComposition.Initialize(7,"player-1",forcedOpening:PlayableAiOpening.BlindRush);var source=new AiOpeningExecutor(opening,71);byte[] bytes;
            using(var stream=new System.IO.MemoryStream())using(var writer=new System.IO.BinaryWriter(stream)){source.WriteState(writer);writer.Flush();bytes=stream.ToArray();}
            using(var stream=new System.IO.MemoryStream(bytes))using(var reader=new System.IO.BinaryReader(stream))
            {
                WorldWire.Boolean(reader);WorldWire.String(reader);
                for(int i=0;i<5;i++)WorldWire.Boolean(reader);reader.ReadInt32();reader.ReadInt32();WorldWire.Boolean(reader);
                for(int i=0;i<3;i++)reader.ReadInt64();WorldWire.Boolean(reader);WorldWire.Boolean(reader);bytes[stream.Position]=2;
            }
            var restored=new AiOpeningExecutor(opening,71);using(var stream=new System.IO.MemoryStream(bytes))using(var reader=new System.IO.BinaryReader(stream))
                Assert.Throws<ArgumentException>(()=>restored.ReadState(reader,0),"corrupted boolean tags remain rejected");
        }
        [Test] public void HomeEconomyGateRequiresActualReadyBuildingsAtHomeAndProtectsCurrentOpeningQuote()
        {
            var ai=AiProfile.Initial;var profile=PlayableProfile.Default;var state=PlayableAiOpeningComposition.Initialize(7,"player-1",forcedOpening:PlayableAiOpening.GreedySafe);
            var executor=new AiOpeningExecutor(state,71);int required=executor.RequiredHomeRefineryCount(ai);
            var foreignOnly=HomeEconomyObservation(1,0,false,true);
            Assert.False(executor.HomeEconomyReady(foreignOnly,ai),"nonhome READY income and factory do not satisfy home opening facts");
            var readyHome=HomeEconomyObservation(2,required,true,true);executor.Observe(readyHome,null,null,ai);
            Assert.True(executor.HomeEconomyReady(readyHome,ai),"the required home refineries and first home factory satisfy the gate by READY world facts");
            var beforeHome=HomeEconomyObservation(3,0,false,false);var normal=new AiEconomyPlanner().Plan(beforeHome,profile).Candidates;
            Assert.AreEqual(TerritoryRules.Cost(profile,PlayableBuildingKind.Refinery),executor.HomeEconomyQuote(beforeHome,profile,ai,normal),"protect the current legal home opening quote from discretionary technology");
            var requested=executor.MacroRequests(beforeHome,profile,ai,normal).Where(c=>c.Legal&&c.Policy=="economy"&&c.Action.Kind==PlayableCommandKind.BuildAt&&
                (c.Action.BuildingKind==PlayableBuildingKind.Refinery||c.Action.BuildingKind==PlayableBuildingKind.Factory)).ToArray();
            Assert.True(requested.Length>0);Assert.True(requested.All(c=>c.Action.SiteId==beforeHome.HomeSiteId),"opening income/factory requests stay at the home site");
        }
        [Test] public void AbortedOpeningQuotesLegalHomeFactoryAfterOrdinarySaleFreesTheLastSlot()
        {
            var c=Config(noFactorySlot:true);var a=new PlayableAuthorityTick(c,71);Force(a,PlayableAiOpening.BlindRush);var ai=AiProfile.Initial;
            try
            {
                using(var host=new UnityHostRouteService())
                {
                    var before=PlayableAiObservation.From(a.ParticipantView("west-owner"));var executor=Executor(a);
                    var owner=Owner(a);var finance=Get(owner,"expansionFinancial");var arbiter=Get(owner,"arbiter");
                    Assert.True(executor.MissingEssentialEconomy(before,ai));Assert.True((bool)Call(finance,"BootstrapActive",arbiter));
                    Assert.Zero(executor.HomeEconomyQuote(before,c.Profile,ai,new AiEconomyPlanner().Plan(before,c.Profile).Candidates),"no open home slot means no quote");
                    Step(a,host);var aborted=PlayableAiObservation.From(a.ParticipantView("west-owner"));
                    Assert.AreEqual(PlayableAiOpeningPhase.Aborted,executor.Capture().Phase);Assert.True(executor.MissingEssentialEconomy(aborted,ai),"fallback must retain the missing producer obligation");
                    int science=aborted.Buildings.First(b=>b.Kind==PlayableBuildingKind.ScientificCenter).Id;
                    Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,(long)Get(Get(a,"domain"),"nextAiSequence"),"west-owner",PlayableCommandKind.SellBuilding,new[]{science})).Status);
                    for(int i=0;i<=(int)Math.Ceiling(c.Profile.BuildingSaleDemolitionSec*30)+2;i++)Step(a,host);
                    var free=PlayableAiObservation.From(a.ParticipantView("west-owner"));var normal=new AiEconomyPlanner().Plan(free,c.Profile).Candidates;
                    Assert.True(normal.Any(x=>x.Legal&&x.Action.Kind==PlayableCommandKind.BuildAt&&x.Action.BuildingKind==PlayableBuildingKind.Factory&&x.Action.SiteId==free.HomeSiteId),"ordinary economy has a legal home factory request after demolition");
                    Assert.AreEqual(TerritoryRules.Cost(c.Profile,PlayableBuildingKind.Factory),executor.HomeEconomyQuote(free,c.Profile,ai,normal));
                    Assert.True((bool)Call(finance,"BootstrapActive",arbiter));
                }
            }
            finally{a.Stop();}
        }
        [Test] public void LostPrerequisite_ReplansThenDeadlineEnablesLiveFallback()
        {
            var e=new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(7,"player-1",forcedOpening:PlayableAiOpening.BlindRush),71);var ai=AiProfile.Initial;
            e.Observe(Observation(1,true),null,null,ai);CollectionAssert.Contains(e.Capture().Completed,"factory");
            e.Observe(Observation(2),null,null,ai);Assert.False(e.Capture().Completed.Contains("factory"));CollectionAssert.Contains(e.Capture().Completed,"income");StringAssert.Contains("producer lost",e.Capture().Reason);
            var recovery=e.MacroRequests(Observation(2),PlayableProfile.Default,ai,Array.Empty<AiEconomyCandidate>());Assert.True(recovery.Any(c=>c.Legal&&c.Action.BuildingKind==PlayableBuildingKind.Factory),"loss immediately requests a legal replacement");
            e.Observe(Observation(3601),null,null,ai);Assert.AreEqual(PlayableAiOpeningPhase.Aborted,e.Capture().Phase);StringAssert.Contains("deadline",e.Capture().Reason);Assert.False(e.AllowsPressure,"fallback cannot skip the actual first force");Assert.True(e.AllowsExpansion);Assert.AreEqual(AiScoutPlan.Standard,e.ScoutPlan);
            var o=Observation(3602,true);var normal=new AiEconomyPlanner().Plan(o,PlayableProfile.Default).Candidates;
            Assert.True(e.MacroRequests(o,PlayableProfile.Default,ai,normal).Any(c=>c.Legal&&c.Policy=="production"),"fallback remains a real affordable command request");
        }
        [Test] public void ReceiptsAloneCannotCompleteAndBudgetsRemainBound()
        {
            var ai=AiProfile.Initial;var state=PlayableAiOpeningComposition.Initialize(7,"player-1",forcedOpening:PlayableAiOpening.BlindRush);var e=new AiOpeningExecutor(state,71);var o=Observation(1,true,3);e.Observe(o,null,null,ai);
            var action=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,71,1,PlayableCommandKind.AttackMove,new[]{10,11,12});
            e.ObserveReceipt(action,new PlayableAiTraceRecord(o.Identity,1,1,1,1,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"applied",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(71,o.OwnerId,1,1),AiArmyPlanner.Policy,PlayableCommandKind.AttackMove));
            e.Observe(Observation(2,true,3),null,null,ai);Assert.AreEqual(PlayableAiOpeningPhase.Active,e.Capture().Phase);Assert.False(e.Capture().Completed.Contains("pressure"));
            var scout=new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(7,"player-1",forcedOpening:PlayableAiOpening.ExplorerAllIn),71);Assert.AreEqual(AiScoutPlan.Standard,scout.ScoutPlan);
            var planner=new AiScoutPlanner();Assert.AreEqual(planner.Budget(ai,AiDifficulty.Fighter,state.PersonalitySeed,scout.ScoutPlan),planner.Budget(ai,AiDifficulty.Fighter,state.PersonalitySeed,e.ScoutPlan),"all-in is a force choice, not a separate scout-led budget");
        }
        [TestCase("owner")][TestCase("generation")][TestCase("tick")]
        public void IntelRejectsForeignOrStaleKnowledgeWithoutProgress(string corrupt)
        {
            var e=new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(7,"player-1",forcedOpening:PlayableAiOpening.ExplorerAllIn),71);
            var o=Observation(10,true,3);var before=e.Capture();var wire=Bytes(e);
            var knowledge=new AiKnowledgeState(corrupt=="owner"?"foreign":o.OwnerId,corrupt=="generation"?72:71,corrupt=="tick"?9:10,Array.Empty<AiKnownContact>(),new[]{new AiVisitedArea(2,10,false,false)});
            StringAssert.Contains("knowledge binding",Assert.Throws<ArgumentException>(()=>e.Observe(o,knowledge,null,AiProfile.Initial)).Message);
            Assert.AreEqual(before.StartedTick,e.Capture().StartedTick);Assert.AreEqual(before.ProgressTick,e.Capture().ProgressTick);CollectionAssert.AreEqual(before.Completed,e.Capture().Completed);
            CollectionAssert.AreEqual(wire,Bytes(e),"every durable field remains unchanged on rejection");
            e.Observe(o,new AiKnowledgeState(o.OwnerId,71,10,Array.Empty<AiKnownContact>(),Array.Empty<AiVisitedArea>()),null,AiProfile.Initial);
        }
        private static byte[] Bytes(AiOpeningExecutor e)
        {
            using(var s=new System.IO.MemoryStream())using(var w=new System.IO.BinaryWriter(s))
            {e.WriteState(w);w.Flush();return s.ToArray();}
        }
        [TestCase("tick")][TestCase("future-start")][TestCase("army")]
        public void DeploymentBindingRejectsInvalidInputAtomically(string corrupt)
        {
            var e=new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(7,"player-1",forcedOpening:PlayableAiOpening.BlindRush),71);
            var o=Observation(10,true);var wire=Bytes(e);
            var deployment=new AiArmyDeployment(corrupt=="tick"?Observation(9,true):o,corrupt=="army"?0:1,corrupt=="future-start"?11:9);
            StringAssert.Contains("deployment binding",Assert.Throws<ArgumentException>(()=>e.Observe(o,null,deployment,AiProfile.Initial)).Message);
            CollectionAssert.AreEqual(wire,Bytes(e));
            Assert.DoesNotThrow(()=>e.Observe(o,null,new AiArmyDeployment(o,1,9),AiProfile.Initial));
            Assert.False(e.Capture().InitialForceFulfilled,"a valid deployment receipt cannot fabricate the initial live force");
        }
        [Test] public void CurrentRestorePreservesTheOriginalOpeningDeadline()
        {
            var opening=PlayableAiOpeningComposition.Initialize(7,"player-1",forcedOpening:PlayableAiOpening.BlindRush);
            var e=new AiOpeningExecutor(opening,71);e.Observe(Observation(1,true),null,null,AiProfile.Initial);var wire=Bytes(e);
            var restored=new AiOpeningExecutor(opening,71);
            using(var s=new System.IO.MemoryStream(wire))using(var r=new System.IO.BinaryReader(s))restored.ReadState(r,1);
            CollectionAssert.AreEqual(wire,Bytes(restored));
            long deadline=1+AiProfile.SecondsToTicks(AiProfile.Initial.Value("opening.deadlineSeconds"),30);
            e.Observe(Observation(deadline,true),null,null,AiProfile.Initial);restored.Observe(Observation(deadline,true),null,null,AiProfile.Initial);
            Assert.AreEqual(PlayableAiOpeningPhase.Aborted,restored.Capture().Phase);CollectionAssert.AreEqual(Bytes(e),Bytes(restored));
        }
        [Test] public void AbortedAirRetainsItsFirstGroupUntilActualDeployment()
        {
            var p=PlayableProfile.Default;var ai=AiProfile.Initial;
            var opening=PlayableAiOpeningComposition.Initialize(7,"player-1",forcedOpening:PlayableAiOpening.Air);
            var e=new AiOpeningExecutor(opening,71);
            PlayableAiObservation View(int tick,int count)
            {
                var ground=HomeEconomyObservation(tick,3,true,false);
                var buildings=ground.Buildings.Select(b=>b.Kind!=PlayableBuildingKind.Factory?b:new PlayableBuildingSnapshot(b.Id,b.Owner,PlayableBuildingKind.Hangar,b.Position,p.HangarHealth,1,0,0,default,siteId:b.SiteId,slotId:b.SlotId,parentId:1,phase:ConstructionPhase.Ready)).ToArray();
                var units=Enumerable.Range(100,count).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Hawk,default,PlayableUnitRules.Health(p,PlayableEntityKind.Hawk),false,0,0,0)).ToArray();
                return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,10000,null,units,buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:ground.Sites.ToArray(),population:new PlayablePopulationSnapshot(count*(int)p.HawkPopulationCost,0,p.ArmyCapacity)));
            }
            e.Observe(View(1,0),null,null,ai);
            e.Observe(View(3601,3),null,null,ai);Assert.AreEqual(PlayableAiOpeningPhase.Aborted,e.Capture().Phase);
            Assert.False(e.AllowsPressure);Assert.True(e.InitialAttackPending);Assert.AreEqual(3,e.InitialMembers(View(3601,3),ai).Length);
            var ready=View(3602,4);e.Observe(ready,null,null,ai);
            Assert.True(e.AllowsPressure);Assert.True(e.InitialAttackPending);Assert.AreEqual(4,e.InitialMembers(ready,ai).Length);
            e.Observe(ready,null,new AiArmyDeployment(ready,1,3602),ai);
            Assert.False(e.InitialAttackPending);Assert.IsNull(e.InitialMembers(ready,ai));
            var bytes=Bytes(e);var restored=new AiOpeningExecutor(opening,71);
            using(var stream=new System.IO.MemoryStream(bytes))using(var reader=new System.IO.BinaryReader(stream))restored.ReadState(reader,3602);
            CollectionAssert.AreEqual(bytes,Bytes(restored));Assert.False(restored.InitialAttackPending);
        }
        [TestCase(PlayableAiOpening.GreedyMine)][TestCase(PlayableAiOpening.DoubleMineExplorerRush)]
        public void RetiredAliasesSelectTheCurrentGreedyPlan(PlayableAiOpening alias)
        {
            var oldName=PlayableAiOpeningComposition.Initialize(7,"player-1",forcedOpening:alias);
            var current=PlayableAiOpeningComposition.Initialize(7,"player-1",forcedOpening:PlayableAiOpening.GreedySafe);
            Assert.True(oldName.SemanticallyEquals(current));
            Assert.False(AiOpeningCatalog.For(alias).Milestones.Any(x=>x.Goal==AiOpeningGoal.Expansion||x.Goal==AiOpeningGoal.Intel));
        }
        [Test] public void NormalSeeds_RepresentativeBankExecutesIncomeFirstWithoutForce()
        {
            var selected=Enumerable.Range(0,512).GroupBy(seed=>PlayableAiOpeningComposition.Initialize(seed,"west-owner").Opening).ToArray();
            CollectionAssert.AreEquivalent(AiOpeningCatalog.All.Select(d=>d.Opening),selected.Select(g=>g.Key));
            using(var host=new UnityHostRouteService())foreach(var group in selected)
            {
                var c=Config(group.First());var a=new PlayableAuthorityTick(c,71);
                for(int i=0;i<120;i++)Step(a,host);
                var state=Executor(a).Capture();Assert.AreEqual(AiOpeningCatalog.For(group.Key).Id,state.Template);
                var ledger=(AiBudgetLedger)Get(Owner(a),"budget");var paid=ledger.Capture().Paid;
                Assert.True(paid.Any(e=>e.Action.Kind==PlayableCommandKind.BuildAt),state.Template);
                Assert.True(paid.Any(e=>e.Action.BuildingKind==PlayableBuildingKind.Refinery),"all current openings start real income");
                Assert.AreEqual(PlayableBuildingKind.Refinery,paid.Where(e=>e.Action.Kind==PlayableCommandKind.BuildAt).OrderBy(e=>e.CreatedTick).First().Action.BuildingKind,"current openings all begin with actual income; their later counts/producers are covered by AllTemplates");
                Assert.AreEqual(group.Key,((PlayableAiOwnerLoop)Owner(a)).Checkpoint.Opening.Opening,"normal selection reaches the owner without Force");
                a.Stop();
            }
        }
        [Test] public void DefinitionRejectsCyclesAndUnknownDependencies()
        {Assert.Throws<ArgumentException>(()=>new AiOpeningDefinition("bad",PlayableAiOpening.Safe,"opening.weights.safe",new[]{new AiOpeningMilestone("a",AiOpeningGoal.Factory,"b"),new AiOpeningMilestone("b",AiOpeningGoal.Force,"a")}));}
    }
}
