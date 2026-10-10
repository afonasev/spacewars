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
        private static object Owner(PlayableAuthorityTick a)=>((IEnumerable)Get(Get(a,"scheduler"),"owners")).Cast<object>().Single();
        private static AiOpeningExecutor Executor(PlayableAuthorityTick a)=>(AiOpeningExecutor)Get(Owner(a),"openingExecutor");
        private static OfflineMatchConfiguration Config(int seed=19092026)
        {
            var data=PlayableProfile.Default.CopyData();data.startingCredits=20000;var p=PlayableProfile.Create(data);
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-24,0),new NavPoint(-24,8),pin:1),new OfflineStart("east",2,new NavPoint(24,0),new NavPoint(24,8),pin:2)};
            // Authored slots stay inside the default arena including the validator's swept envelope.
            var slots=new[]{new TerritorySlot(1,new NavPoint(-24,-9),0),new TerritorySlot(2,new NavPoint(-14,0),0),new TerritorySlot(3,new NavPoint(-14,-9),0),new TerritorySlot(4,new NavPoint(-24,-18),0),new TerritorySlot(5,new NavPoint(-14,-18),0)};
            var sites=new[]{new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,slots),new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,Array.Empty<TerritorySlot>()),new TerritorySite(3,PlayableBuildingKind.Mine,new NavPoint(-10,20),p)};
            return new OfflineMatchConfiguration(p,"o3-diagnostic-funded","o3-flat-two-starts","UnityHostRouteService",seed,new[]{new OfflineParticipant("west-owner",1,1,OfflineControl.Ai),new OfflineParticipant("east-owner",2,2,OfflineControl.Human)},starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,48},{48,0}});
        }
        private static void Force(PlayableAuthorityTick a,PlayableAiOpening opening)
        {
            var owner=Owner(a);var state=PlayableAiOpeningComposition.Initialize(19092026,"west-owner",forcedOpening:opening);
            owner.GetType().GetField("opening",F).SetValue(owner,state);
            owner.GetType().GetField("openingExecutor",F).SetValue(owner,new AiOpeningExecutor(state,71));
        }
        private static void Step(PlayableAuthorityTick a,UnityHostRouteService host)
        {for(int i=0;i<512;i++){if(a.TryAdvance())return;host.Service(a,64);}Assert.Fail("native route barrier at tick "+a.Tick);}
        [TestCase(PlayableAiOpening.Safe)][TestCase(PlayableAiOpening.GreedySafe)][TestCase(PlayableAiOpening.GreedyMine)][TestCase(PlayableAiOpening.BlindRush)][TestCase(PlayableAiOpening.ExplorerAllIn)]
        public void AllTemplates_ExecuteOrdinaryMacroAndArmyRequests(PlayableAiOpening opening)
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);Force(a,opening);
            var ledger=(AiBudgetLedger)Get(Owner(a),"budget");
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<3650&&Executor(a).Capture().Phase==PlayableAiOpeningPhase.Active;i++)Step(a,host);
                var state=Executor(a).Capture();var paid=ledger.Capture().Paid;
                Assert.AreEqual(PlayableAiOpeningPhase.Complete,state.Phase,state.Template+": "+state.Reason+" completed="+string.Join(",",state.Completed));
                CollectionAssert.AreEquivalent(AiOpeningCatalog.For(opening).Milestones.Select(m=>m.Id),state.Completed);
                Assert.True(paid.Any(e=>e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.BuildingKind==PlayableBuildingKind.Factory));
                Assert.True(paid.Any(e=>e.Action.Kind==PlayableCommandKind.QueueTank));
                var view=PlayableAiObservation.From(a.ParticipantView("west-owner"));
                if(opening==PlayableAiOpening.GreedySafe)Assert.GreaterOrEqual(view.Buildings.Count(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Refinery&&b.Phase==ConstructionPhase.Ready),2);
                if(opening==PlayableAiOpening.GreedyMine)Assert.True(view.Sites.Any(s=>s.Site.Id==3&&s.Owner==view.Owner&&s.Ready));
                if(opening==PlayableAiOpening.BlindRush||opening==PlayableAiOpening.ExplorerAllIn)
                {
                    var army=(AiArmyPlanner)Get(Owner(a),"armyPlanner");var registry=(AiArmyRegistry)Get(Owner(a),"armies");
                    Assert.NotNull(army.Deployment(view,registry),"receipt plus effective Registry-owned order required");registry.AssertInvariants();
                }
                var bytes=a.CaptureBytes();var restored=PlayableAuthorityTick.RestoreBytes(bytes,c);CollectionAssert.AreEqual(bytes,restored.CaptureBytes());
                for(int i=0;i<90;i++){Step(a,host);Step(restored,host);CollectionAssert.AreEqual(a.CaptureBytes(),restored.CaptureBytes());}
            }
        }
        [Test] public void NormalSeeds_FixedBankCoversFiveAndProfileWeightsBindChoice()
        {
            var bank=Enumerable.Range(0,512).ToArray();var a=bank.Select(seed=>PlayableAiOpeningComposition.Initialize(seed,"owner-a")).ToArray();
            CollectionAssert.AreEquivalent(AiOpeningCatalog.All.Select(d=>d.Opening),a.Select(s=>s.Opening).Distinct());
            foreach(var state in a)Assert.True(state.SemanticallyEquals(PlayableAiOpeningComposition.Initialize(state.MatchSeed,state.OwnerId)));
            var data=AiProfile.Initial.CopyData();data.revision++;data.fields.Single(v=>v.path=="opening.weights.greedy").value=5;var ai=new AiProfile(data);
            var b=bank.Select(seed=>PlayableAiOpeningComposition.Initialize(seed,"owner-a",aiProfile:ai)).ToArray();
            Assert.Greater(b.Count(s=>s.Opening==PlayableAiOpening.GreedySafe),a.Count(s=>s.Opening==PlayableAiOpening.GreedySafe));
        }
        private static PlayableAiObservation Observation(long tick,bool factory=false,int tanks=0)
        {
            var p=PlayableProfile.Default;var site=TerritoryRules.Sites(p).First(s=>s.Kind==PlayableBuildingKind.Headquarters);
            var buildings=new System.Collections.Generic.List<PlayableBuildingSnapshot>{new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Headquarters,site.Position,100,1,0,0,default,siteId:site.Id)};
            if(factory)buildings.Add(new PlayableBuildingSnapshot(2,PlayableOwner.Player,PlayableBuildingKind.Factory,site.Slots[0].Position,100,1,0,0,default,siteId:site.Id,slotId:site.Slots[0].Id,parentId:1));
            var units=Enumerable.Range(10,tanks).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(id,0),100,false,0,0,0)).ToArray();
            return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,10000,null,units,buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites:new[]{new TerritorySiteSnapshot(site,PlayableOwner.Player,PlayableOwner.Player,1,false,1,true)},population:new PlayablePopulationSnapshot(tanks,0,p.ArmyCapacity)));
        }
        [Test] public void LostPrerequisite_ReplansThenDeadlineEnablesLiveFallback()
        {
            var e=new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(7,"player-1",forcedOpening:PlayableAiOpening.BlindRush),71);var ai=AiProfile.Initial;
            e.Observe(Observation(1,true),null,null,ai);CollectionAssert.Contains(e.Capture().Completed,"factory");
            e.Observe(Observation(2),null,null,ai);Assert.AreEqual(1,e.Capture().Replans);Assert.IsEmpty(e.Capture().Completed);StringAssert.Contains("producer lost",e.Capture().Reason);
            e.Observe(Observation(3601),null,null,ai);Assert.AreEqual(PlayableAiOpeningPhase.Aborted,e.Capture().Phase);StringAssert.Contains("deadline",e.Capture().Reason);Assert.True(e.AllowsPressure&&e.AllowsExpansion);Assert.AreEqual(AiScoutPlan.Standard,e.ScoutPlan);
            var o=Observation(3602,true);var normal=new AiEconomyPlanner().Plan(o,PlayableProfile.Default).Candidates;
            CollectionAssert.AreEqual(normal,e.MacroRequests(o,PlayableProfile.Default,ai,normal));Assert.True(normal.Any(c=>c.Legal&&c.Policy=="production"),"fallback remains a real affordable command request");
        }
        [Test] public void ReceiptsAloneCannotCompleteAndBudgetsRemainBound()
        {
            var ai=AiProfile.Initial;var state=PlayableAiOpeningComposition.Initialize(7,"player-1",forcedOpening:PlayableAiOpening.BlindRush);var e=new AiOpeningExecutor(state,71);var o=Observation(1,true,3);e.Observe(o,null,null,ai);
            var action=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,71,1,PlayableCommandKind.AttackMove,new[]{10,11,12});
            e.ObserveReceipt(action,new PlayableAiTraceRecord(o.Identity,1,1,1,1,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"applied",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(71,o.OwnerId,1,1),AiArmyPlanner.Policy,PlayableCommandKind.AttackMove));
            e.Observe(Observation(2,true,3),null,null,ai);Assert.AreEqual(PlayableAiOpeningPhase.Active,e.Capture().Phase);Assert.False(e.Capture().Completed.Contains("pressure"));
            var scout=new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(7,"player-1",forcedOpening:PlayableAiOpening.ExplorerAllIn),71);Assert.AreEqual(AiScoutPlan.ScoutLed,scout.ScoutPlan);
            var planner=new AiScoutPlanner();Assert.Greater(planner.Budget(ai,AiDifficulty.Fighter,state.PersonalitySeed,scout.ScoutPlan),planner.Budget(ai,AiDifficulty.Fighter,state.PersonalitySeed,e.ScoutPlan));
        }
        [TestCase("owner")][TestCase("generation")][TestCase("tick")]
        public void IntelRejectsForeignOrStaleKnowledgeWithoutProgress(string corrupt)
        {
            var e=new AiOpeningExecutor(PlayableAiOpeningComposition.Initialize(7,"player-1",forcedOpening:PlayableAiOpening.ExplorerAllIn),71);
            var o=Observation(10,true,3);var before=e.Capture();
            var knowledge=new AiKnowledgeState(corrupt=="owner"?"foreign":o.OwnerId,corrupt=="generation"?72:71,corrupt=="tick"?9:10,Array.Empty<AiKnownContact>(),new[]{new AiVisitedArea(2,10,false,false)});
            StringAssert.Contains("knowledge binding",Assert.Throws<ArgumentException>(()=>e.Observe(o,knowledge,null,AiProfile.Initial)).Message);
            Assert.AreEqual(before.StartedTick,e.Capture().StartedTick);Assert.AreEqual(before.ProgressTick,e.Capture().ProgressTick);CollectionAssert.AreEqual(before.Completed,e.Capture().Completed);
            e.Observe(o,new AiKnowledgeState(o.OwnerId,71,10,Array.Empty<AiKnownContact>(),Array.Empty<AiVisitedArea>()),null,AiProfile.Initial);
        }
        [Test] public void NormalSeeds_RepresentativeBankExecutesDistinctFirstCommandsWithoutForce()
        {
            var selected=Enumerable.Range(0,512).GroupBy(seed=>PlayableAiOpeningComposition.Initialize(seed,"west-owner").Opening).ToArray();
            CollectionAssert.AreEquivalent(AiOpeningCatalog.All.Select(d=>d.Opening),selected.Select(g=>g.Key));
            var signatures=new System.Collections.Generic.Dictionary<PlayableAiOpening,string>();
            using(var host=new UnityHostRouteService())foreach(var group in selected)
            {
                var c=Config(group.First());var a=new PlayableAuthorityTick(c,71);
                for(int i=0;i<120;i++)Step(a,host);
                var state=Executor(a).Capture();Assert.AreEqual(AiOpeningCatalog.For(group.Key).Id,state.Template);
                var ledger=(AiBudgetLedger)Get(Owner(a),"budget");var paid=ledger.Capture().Paid;
                Assert.True(paid.Any(e=>e.Action.Kind==PlayableCommandKind.BuildAt),state.Template);
                if(group.Key==PlayableAiOpening.GreedySafe){Assert.True(paid.Any(e=>e.Action.BuildingKind==PlayableBuildingKind.Refinery));Assert.False(paid.Any(e=>e.Action.BuildingKind==PlayableBuildingKind.Factory));}
                else Assert.True(paid.Any(e=>e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.BuildingKind==PlayableBuildingKind.Factory));
                var check=a.CaptureDiagnosticCheckpoints().Single();
                if(group.Key==PlayableAiOpening.BlindRush)Assert.False(check.Records.Any(r=>r.Policy=="scout"&&r.Status==PlayableAiDeliveryStatus.Applied));
                if(group.Key==PlayableAiOpening.ExplorerAllIn)Assert.True(check.Records.Any(r=>r.Policy=="scout"&&r.Status==PlayableAiDeliveryStatus.Applied));
                signatures[group.Key]=string.Join(",",check.Records.Where(r=>r.Status==PlayableAiDeliveryStatus.Applied).Select(r=>r.Policy+":"+r.Kind));
                a.Stop();
            }
            Assert.Greater(signatures.Values.Distinct().Count(),1,"actual first commands differ in the normal fixed bank");
        }
        [Test] public void DefinitionRejectsCyclesAndUnknownDependencies()
        {Assert.Throws<ArgumentException>(()=>new AiOpeningDefinition("bad",PlayableAiOpening.Safe,"opening.weights.safe",new[]{new AiOpeningMilestone("a",AiOpeningGoal.Factory,"b"),new AiOpeningMilestone("b",AiOpeningGoal.Force,"a")}));}
    }
}
