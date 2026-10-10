using System;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using NUnit.Framework;
using Spacewars.Headless;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using Spacewars.Presentation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeHeadlessEconomyTests
    {
        private static MatchManifest Manifest(string fixture)
        {var identity=HeadlessFixtures.EngineIdentity(new string('a',40));identity.BundleHash=identity.ExecutableHash=new string('b',64);return HeadlessFixtures.Template(fixture,identity,"/worker","/worker/game","/repo/tools/unity.sh");}
        [TestCase("economy-rich-v1")][TestCase("economy-low-v1")][TestCase("economy-full-slots-v1")][TestCase("economy-lost-hq-v1")][TestCase("economy-blocked-exit-v1")]
        public void FixturesFreezeGenesisIdentityWithoutMoneyOrProfileMutation(string fixture)
        {
            var m=Manifest(fixture);Wire.Validate(m,false);var c=HeadlessFixtures.Create(m);var a=new PlayableAuthorityTick(c,m.Generation);
            Assert.AreEqual(m.MapHash,HeadlessFixtures.MapHash(c));Assert.AreEqual(m.GeometryHash,HeadlessFixtures.GeometryHash(c));
            Assert.AreEqual(AiMatchIdentity.GameplayDigest(PlayableProfile.Default),m.Worker.GameplayHash);
            Assert.AreEqual(c.Profile.StartingCredits,a.ParticipantView("west-owner").Credits);Assert.Zero(a.ParticipantView("west-owner").SettledIncome);
            Assert.AreEqual("ai",m.Roster[0].Control);Assert.AreEqual("human",m.Roster[1].Control);
            if(fixture=="economy-rich-v1")
            {
                var view=a.ParticipantView("west-owner");
                Assert.Greater(view.IncomePerSecond,PlayableUnitRules.Cost(c.Profile,PlayableEntityKind.Tank)/PlayableUnitRules.Duration(c.Profile,PlayableEntityKind.Tank));
                Assert.True(view.Sites.Any(site=>site.Owner==view.Owner&&site.Site.Slots.Any(slot=>!view.Buildings.Any(b=>b.SiteId==site.Site.Id&&b.SlotId==slot.Id))),"real free scaling slot required");
            }
            Assert.True(m.Commands.All(x=>x.OwnerId=="east-owner"&&x.Kind=="AttackMove"));
            a.Stop();
        }
        private static byte[] CurrentCheckpoint(string fixture,long requested,MatchManifest manifest,out OfflineMatchConfiguration config)
        {
            var historical=Path.Combine(Directory.GetCurrentDirectory(),"Tests/Fixtures",fixture);var original=File.ReadAllBytes(Path.Combine(historical,"checkpoint-"+requested+".world"));
            Assert.AreEqual(manifest.Expected.States.Single(x=>x.Tick==requested).Hash,Wire.Hash(original),"Original schema-6 evidence must stay byte exact");
            var path=Path.Combine(Directory.GetCurrentDirectory(),"Tests/Fixtures/o1-certified-e6",fixture);var identity=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(path,"identity.json")));
            Assert.AreEqual("native-army-checkpoint-input-v1",(string)identity["Schema"]);Assert.AreEqual(11,(int)identity["AiStateSchema"]);Assert.AreEqual(requested,(long)identity["RequestedHistoricalTick"]);
            Assert.AreEqual(Wire.Hash(original),(string)identity["OriginalCheckpointHash"]);Assert.AreEqual(Wire.Hash(File.ReadAllBytes(Path.Combine(historical,"manifest.json"))),(string)identity["OriginalManifestHash"]);
            Assert.AreEqual(PlayableAuthorityTick.RouteDeliveryPolicy,(string)identity["RouteDeliveryPolicy"]);Assert.AreEqual(manifest.Generation,(long)identity["Generation"]);Assert.AreEqual(manifest.Seed,(int)identity["Seed"]);Assert.AreEqual(40,((string)identity["CodeRevision"]).Length);
            Assert.AreEqual("native-a3-e6-derived-v1",(string)identity["InputMode"]);
            var derivedBytes=File.ReadAllBytes(Path.Combine(path,(string)identity["DerivedManifest"]));Assert.AreEqual((string)identity["DerivedManifestHash"],Wire.Hash(derivedBytes));
            var input=JsonConvert.DeserializeObject<ArmyRecoveryManifest>(System.Text.Encoding.UTF8.GetString(derivedBytes));Assert.AreEqual((string)identity["OriginalManifestHash"],input.OriginalManifestHash);Assert.AreEqual(PlayableAiCanonical.Encode(manifest),PlayableAiCanonical.Encode(input.OriginalManifest));
            Assert.AreEqual(6,input.OriginalRaidIds.Length);Assert.False(input.OriginalRaidIds.Contains(input.GuardId));CollectionAssert.AreEquivalent(input.OriginalRaidIds,input.Commands[0].EntityIds);Assert.AreEqual("AttackMove",input.Commands[0].Kind);Assert.AreEqual("Hold",input.Commands[1].Kind);CollectionAssert.AreEqual(new[]{input.GuardId},input.Commands[1].EntityIds);
            Assert.AreEqual(manifest.Commands.Single().X,input.Commands[0].X);Assert.AreEqual(manifest.Commands.Single().Z,input.Commands[0].Z);Assert.AreEqual(0,input.Commands[0].Tick);Assert.AreEqual(0,input.Commands[1].Tick);
            Assert.AreEqual((string)identity["GenesisHash"],Wire.Hash(File.ReadAllBytes(Path.Combine(path,(string)identity["Genesis"]))));config=ArmyRecoveryFixture.Create(input);Assert.AreEqual((string)identity["MapHash"],HeadlessFixtures.MapHash(config));Assert.AreEqual((string)identity["GeometryHash"],HeadlessFixtures.GeometryHash(config));Assert.AreEqual((string)identity["GameplayHash"],AiMatchIdentity.GameplayDigest(config.Profile));
            var bytes=File.ReadAllBytes(Path.Combine(path,(string)identity["Checkpoint"]));Assert.AreEqual((string)identity["CheckpointHash"],Wire.Hash(bytes));Assert.AreEqual((long)identity["Tick"],PlayableWorldState.Decode(bytes).Tick);return bytes;
        }
        [TestCase("e6-lost-hq",1200)][TestCase("e6-lost-hq-recent-order",2400)]
        public void HistoricalA1SchemaSevenInputsRejectExplicitly(string fixture,long requested)
        {
            var path=Path.Combine(Directory.GetCurrentDirectory(),"Tests/Fixtures/a1-current-e6",fixture);var identity=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(path,"identity.json")));
            Assert.AreEqual(7,(int)identity["AiStateSchema"]);var bytes=File.ReadAllBytes(Path.Combine(path,(string)identity["Checkpoint"]));Assert.AreEqual((string)identity["CheckpointHash"],Wire.Hash(bytes));
            var manifest=JsonConvert.DeserializeObject<MatchManifest>(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),"Tests/Fixtures",fixture,"manifest.json")));
            var error=Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(bytes,HeadlessFixtures.Create(manifest)));StringAssert.Contains("Unsupported world envelope.",error.Message);
        }
        [TestCase("e6-lost-hq",1200)][TestCase("e6-lost-hq-recent-order",2400)]
        public void HistoricalA2SchemaEightInputsRejectExplicitly(string fixture,long requested)
        {
            var path=Path.Combine(Directory.GetCurrentDirectory(),"Tests/Fixtures/a2-current-e6",fixture);var identity=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(path,"identity.json")));
            Assert.AreEqual(8,(int)identity["AiStateSchema"]);var bytes=File.ReadAllBytes(Path.Combine(path,(string)identity["Checkpoint"]));Assert.AreEqual((string)identity["CheckpointHash"],Wire.Hash(bytes));
            var manifest=JsonConvert.DeserializeObject<MatchManifest>(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),"Tests/Fixtures",fixture,"manifest.json")));
            var error=Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(bytes,HeadlessFixtures.Create(manifest)));StringAssert.Contains("Unsupported world envelope.",error.Message);
        }
        [TestCase("e6-lost-hq",1200)][TestCase("e6-lost-hq-recent-order",2400)]
        public void HistoricalSchemaSixCheckpointsRejectExplicitly(string fixture,long tick)
        {
            var path=Path.Combine(Directory.GetCurrentDirectory(),"Tests/Fixtures",fixture);var manifest=JsonConvert.DeserializeObject<MatchManifest>(File.ReadAllText(Path.Combine(path,"manifest.json")));var bytes=File.ReadAllBytes(Path.Combine(path,"checkpoint-"+tick+".world"));Assert.AreEqual(manifest.Expected.States.Single(x=>x.Tick==tick).Hash,Wire.Hash(bytes));
            var error=Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(bytes,HeadlessFixtures.Create(manifest)));StringAssert.Contains("Unsupported world envelope.",error.Message);
        }
        [Test] public void LostHeadquartersActualCheckpointExplainsAffordableRecoveryDemand()
        {
            var path=Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(),"Tests/Fixtures/e6-lost-hq"));
            var manifest=JsonConvert.DeserializeObject<MatchManifest>(File.ReadAllText(Path.Combine(path,"manifest.json")));OfflineMatchConfiguration c;var bytes=CurrentCheckpoint("e6-lost-hq",1200,manifest,out c);
            var a=PlayableAuthorityTick.RestoreBytes(bytes,c);AssertInitialRecoveryOpportunity(a,c,0);var v=a.ParticipantView("west-owner");var o=PlayableAiObservation.From(v);
            var checkpoint=a.CaptureDiagnosticCheckpoints().Single();var budget=a.CaptureDiagnosticBudgets().Single();
            TestContext.WriteLine(JsonConvert.SerializeObject(new {v.Tick,v.Credits,v.HomeSiteId,v.IncomePerSecond,OwnUnits=v.Entities.Where(e=>e.Owner==v.Owner),v.PublicScoutObjectives,KnownSites=v.Sites,VisibleHostiles=v.Entities.Where(e=>v.IsHostile(e.Owner)),checkpoint.LastScoutOrderTick,checkpoint.Expansion,checkpoint.Opening,Available=budget.Liquid-budget.Unpaid.Sum(e=>e.Amount)-budget.Reserved.Sum(e=>e.Amount)}));
            Assert.False(v.Buildings.Any(b=>b.Owner==v.Owner&&b.Kind==PlayableBuildingKind.Headquarters));
            Assert.True(v.Buildings.Any(b=>b.Owner==v.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.SiteId==3));
            Assert.Less(v.Credits,c.Profile.TankCreditCost);Assert.GreaterOrEqual(v.Credits,c.Profile.ExplorerCreditCost);
            a.Stop();
        }
        private static PlayableAuthorityTick Repro(out OfflineMatchConfiguration config,string fixture="e6-lost-hq",long tick=1200)
        {
            var path=Path.Combine(Directory.GetCurrentDirectory(),"Tests/Fixtures/"+fixture);
            var manifest=JsonConvert.DeserializeObject<MatchManifest>(File.ReadAllText(Path.Combine(path,"manifest.json")));config=HeadlessFixtures.Create(manifest);
            var bytes=CurrentCheckpoint(fixture,tick,manifest,out config);var authority=PlayableAuthorityTick.RestoreBytes(bytes,config);AssertInitialRecoveryOpportunity(authority,config,fixture=="e6-lost-hq"?0:1);return authority;
        }
        private static object AuthorityOwner(PlayableAuthorityTick authority,string id)
        {
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
            var scheduler=authority.GetType().GetField("scheduler",flags).GetValue(authority);var owners=(System.Collections.IEnumerable)scheduler.GetType().GetField("owners",flags).GetValue(scheduler);
            return owners.Cast<object>().Single(o=>(string)o.GetType().GetProperty("OwnerId",flags).GetValue(o)==id);
        }
        private static AiArmyRegistry AuthorityRegistry(PlayableAuthorityTick authority,string id)
        {var owner=AuthorityOwner(authority,id);return (AiArmyRegistry)owner.GetType().GetField("armies",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(owner);}
        private static AiDecisionArbiter AuthorityArbiter(PlayableAuthorityTick authority,string id)
        {var owner=AuthorityOwner(authority,id);return (AiDecisionArbiter)owner.GetType().GetField("arbiter",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(owner);}
        private static void AssertInitialRecoveryOpportunity(PlayableAuthorityTick authority,OfflineMatchConfiguration config,int priorPaidScouts)
        {
            var view=authority.ParticipantView("west-owner");var budget=authority.CaptureDiagnosticBudgets().Single();var observation=PlayableAiObservation.From(view);
            Assert.False(view.Buildings.Any(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Headquarters));Assert.False(view.Entities.Any(e=>e.Owner==view.Owner&&e.Kind==PlayableEntityKind.Explorer));
            Assert.True(view.Buildings.Any(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.SiteId==3&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.PrivateState!=null&&b.PrivateState.QueueCount==0));
            Assert.False(budget.Unpaid.Any(e=>e.Action.Kind==PlayableCommandKind.QueueExplorer),"Input must test new role admission rather than restore an already accepted Scout");
            Assert.False(view.Buildings.Where(b=>b.Owner==view.Owner&&b.PrivateState!=null).Any(b=>b.PrivateState.Orders.Any(order=>order.Kind==PlayableEntityKind.Explorer)));
            long available=(long)budget.Liquid-budget.SafetyReserve-budget.Unpaid.Sum(e=>(long)e.Amount)-budget.Reserved.Sum(e=>(long)e.Amount);
            Assert.GreaterOrEqual(available,config.Profile.ExplorerCreditCost);Assert.Less(available,config.Profile.TankCreditCost);Assert.Less(view.Credits,config.Profile.TankCreditCost);Assert.AreEqual(priorPaidScouts,budget.Paid.Count(e=>e.Action.Kind==PlayableCommandKind.QueueExplorer));
            Assert.Greater(authority.CaptureDiagnosticCheckpoints().Single().Opening.Intent.Explorer,0,"Actual existing opening role must request Scout recovery");
            Assert.True(observation.PublicScoutObjectives.Any(g=>g.Reachable));Assert.True(new AiEconomyPlanner().Plan(observation,config.Profile,available:(int)available,recoveryExplorerDemand:true).Candidates.Any(x=>x.Legal&&x.Action.Kind==PlayableCommandKind.QueueExplorer));
            if(priorPaidScouts>0)Assert.Less(view.Tick-authority.CaptureDiagnosticCheckpoints().Single().LastScoutOrderTick,35*30);
        }
        private static void Step(PlayableAuthorityTick a,UnityHostRouteService host)
        {for(int attempt=0;!a.TryAdvance();attempt++){Assert.Less(attempt,512);host.Service(a,64);}}
        [Test] public void MissingScoutFallbackPreservesRolesAvailabilityAndNormalTankComposition()
        {
            var a=Repro(out var c);var o=PlayableAiObservation.From(a.ParticipantView("west-owner"));var planner=new AiEconomyPlanner();
            Assert.AreEqual(1,planner.Plan(o,c.Profile,available:116,recoveryExplorerDemand:true).Candidates.Count(x=>x.Legal&&x.Action.Kind==PlayableCommandKind.QueueExplorer));
            foreach(var plan in new[]{planner.Plan(o,c.Profile,available:116),planner.Plan(o,c.Profile,available:99,recoveryExplorerDemand:true),planner.Plan(o,c.Profile,available:150,recoveryExplorerDemand:true)})
                Assert.False(plan.Candidates.Any(x=>x.Action.Kind==PlayableCommandKind.QueueExplorer));
            // Counterfactuals change only detached policy observations; never the real world.
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var entities=typeof(PlayableAiObservation).GetField("entities",flags);var objectives=typeof(PlayableAiObservation).GetField("publicScoutObjectives",flags);
            var original=o.Entities.ToArray();entities.SetValue(o,original.Concat(new[]{new PlayableEntitySnapshot(999,o.Owner,PlayableEntityKind.Explorer,new NavPoint(-.5,3),100,false,0,0,0)}).ToArray());
            Assert.False(planner.Plan(o,c.Profile,available:116,recoveryExplorerDemand:true).Candidates.Any(x=>x.Action.Kind==PlayableCommandKind.QueueExplorer));
            entities.SetValue(o,original.Concat(new[]{new PlayableEntitySnapshot(999,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(-.5,3),100,false,0,0,0)}).ToArray());
            Assert.False(planner.Plan(o,c.Profile,available:116,recoveryExplorerDemand:true).Candidates.Any(x=>x.Action.Kind==PlayableCommandKind.QueueExplorer));
            entities.SetValue(o,original);objectives.SetValue(o,Array.Empty<PlayablePublicScoutObjective>());
            Assert.False(planner.Plan(o,c.Profile,available:116,recoveryExplorerDemand:true).Candidates.Any(x=>x.Action.Kind==PlayableCommandKind.QueueExplorer));a.Stop();
        }
        [Test] public void ActualLostHomeRecoveryScoutHasOneOrdinaryPaidOrderPendingRestoreAndScoutExecution()
        {
            var a=Repro(out var c);long checkpointTick=a.Tick;var b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);double initial=a.ParticipantView("west-owner").ExactCredits,initialIncome=a.ParticipantView("west-owner").SettledIncome;
            long initialPaid=a.CaptureDiagnosticBudgets().Single().PaidTotal;long initialScoutTick=a.CaptureDiagnosticCheckpoints().Single().LastScoutOrderTick;bool pending=false,paid=false,born=false,moved=false;long paidTick=0;
            using(var host=new UnityHostRouteService())for(int i=0;i<700;i++)
            {
                Step(a,host);Step(b,host);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());var v=a.ParticipantView("west-owner");var budget=a.CaptureDiagnosticBudgets().Single();var checkpoint=a.CaptureDiagnosticCheckpoints().Single();
                var entry=budget.Unpaid.SingleOrDefault(e=>e.Action.Kind==PlayableCommandKind.QueueExplorer);
                if(entry!=null){pending=true;Assert.AreEqual(c.Profile.ExplorerCreditCost,entry.Amount);var restored=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);CollectionAssert.AreEqual(a.CaptureBytes(),restored.CaptureBytes());restored.Stop();}
                if(budget.Paid.Any(e=>e.Action.Kind==PlayableCommandKind.QueueExplorer)){paid=true;if(paidTick==0)paidTick=a.Tick;Assert.AreEqual(1,budget.Paid.Count(e=>e.Action.Kind==PlayableCommandKind.QueueExplorer));}
                Assert.AreEqual(initial+v.SettledIncome-initialIncome-(budget.PaidTotal-initialPaid)-budget.RepairPaidTotal,v.ExactCredits,1e-7);
                born|=v.Entities.Any(e=>e.Owner==v.Owner&&e.Kind==PlayableEntityKind.Explorer);
                moved|=born&&checkpoint.LastScoutOrderTick>initialScoutTick&&checkpoint.Records.Any(r=>r.Policy=="scout"&&r.Kind==PlayableCommandKind.Move&&r.Status==PlayableAiDeliveryStatus.Applied);
                if(moved)break;
            }
            Assert.True(pending);Assert.True(paid);Assert.True(born);Assert.True(moved);Assert.LessOrEqual(paidTick-checkpointTick,AiProfile.SecondsToTicks(AiProfile.Initial.Value("economy.idleLineDeadlineSeconds"),30));a.Stop();b.Stop();
        }
        [Test] public void ActualRecentlyLostScoutRecoversPendingRestoreAndOrdinaryMoveWithoutAssignedOwner()
        {
            var a=Repro(out var c,"e6-lost-hq-recent-order",2400);long checkpointTick=a.Tick;var original=a.CaptureDiagnosticCheckpoints().Single();
            Assert.Less(a.Tick-original.LastScoutOrderTick,35*30);Assert.False(a.ParticipantView("west-owner").Entities.Any(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Explorer));
            var twin=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);long paidTick=0,moveTick=0;bool pending=false,born=false,moved=false;
            long idleDeadline=AiProfile.SecondsToTicks(AiProfile.Initial.Value("economy.idleLineDeadlineSeconds"),30);Assert.AreEqual(240,idleDeadline);
            long productionTicks=AiProfile.SecondsToTicks(PlayableUnitRules.Duration(c.Profile,PlayableEntityKind.Explorer),30);
            long decisionTicks=AiProfile.SecondsToTicks(AiProfile.Initial.DifficultyValue(original.Difficulty,"decisionSeconds"),30);
            long reactionTicks=AiProfile.SecondsToTicks(AiProfile.Initial.DifficultyValue(original.Difficulty,"reactionSeconds"),30);
            long observationEnd=Math.Max(checkpointTick+700,Math.Max(original.LastScoutOrderTick+35*30,checkpointTick+idleDeadline+productionTicks)+decisionTicks+reactionTicks+1);
            using(var host=new UnityHostRouteService())for(int i=0;a.Tick<observationEnd;i++)
            {
                Step(a,host);Step(twin,host);CollectionAssert.AreEqual(a.CaptureBytes(),twin.CaptureBytes());var view=a.ParticipantView("west-owner");var newborn=view.Entities.FirstOrDefault(e=>e.Owner==view.Owner&&e.Kind==PlayableEntityKind.Explorer);
                if(newborn!=null){born=true;Assert.Zero(AuthorityRegistry(a,"west-owner").ArmyFor(newborn.Id),"Recovery proof must not inject an assigned tactical owner");Assert.False(newborn.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack||newborn.CurrentOrder?.Kind==PlayableTacticalOrderKind.AttackMove,"Scout must not receive strategic combat raid commands");}
                var budget=a.CaptureDiagnosticBudgets().Single();if(budget.Unpaid.Any(e=>e.Action.Kind==PlayableCommandKind.QueueExplorer)){pending=true;var restored=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);CollectionAssert.AreEqual(a.CaptureBytes(),restored.CaptureBytes());restored.Stop();}
                if(paidTick==0&&budget.Paid.Count(e=>e.Action.Kind==PlayableCommandKind.QueueExplorer)>1)paidTick=a.Tick;
                var checkpoint=a.CaptureDiagnosticCheckpoints().Single();moved=born&&checkpoint.LastScoutOrderTick>original.LastScoutOrderTick&&checkpoint.Records.Any(r=>r.Policy=="scout"&&r.Kind==PlayableCommandKind.Move&&r.Status==PlayableAiDeliveryStatus.Applied&&r.ApplicationTick>checkpointTick);
                if(moved){moveTick=checkpoint.LastScoutOrderTick;break;}
            }
            TestContext.WriteLine(JsonConvert.SerializeObject(new{Input="A3-derived genesis, natural Scout combat loss, no assigned-owner setup",checkpointTick,original.LastScoutOrderTick,idleDeadline,productionTicks,decisionTicks,reactionTicks,observationEnd,pending,paidTick,born,moved,moveTick,FinalTick=a.Tick}));
            Assert.True(pending);Assert.Greater(paidTick,checkpointTick);Assert.LessOrEqual(paidTick-checkpointTick,idleDeadline);Assert.True(born);Assert.True(moved);Assert.GreaterOrEqual(moveTick,original.LastScoutOrderTick+35*30);twin.Stop();a.Stop();
        }
        [Test] public void LiveDiagnosticsAreDetachedAndDoNotMutateWorldOrAiBytes()
        {
            var m=Manifest("economy-rich-v1");var a=new PlayableAuthorityTick(HeadlessFixtures.Create(m),m.Generation);a.ParticipantView("west-owner");
            var metric=new HeadlessEconomyMetrics(a,AiProfile.Initial);var bytes=a.CaptureBytes();
            var budgets=a.CaptureDiagnosticBudgets();Assert.AreEqual(1,budgets.Length);int bank=budgets[0].Liquid;budgets[0]=null;
            Assert.AreEqual(bank,a.CaptureDiagnosticBudgets()[0].Liquid);
            foreach(var b in a.ParticipantView("west-owner").Buildings.Where(b=>b.Kind==PlayableBuildingKind.Factory))a.DiagnosticFactoryExitUsable(b.Id,PlayableEntityKind.Tank);
            metric.Observe(a);metric.Observe(a);CollectionAssert.AreEqual(bytes,a.CaptureBytes());Assert.Zero(metric.Reports.Single().SampledTicks);a.Stop();
        }
        [Test] public void ObstacleEnclosureBlocksRealFactorySpawnCandidates()
        {
            var m=Manifest("economy-blocked-exit-v1");var a=new PlayableAuthorityTick(HeadlessFixtures.Create(m),m.Generation);
            var factory=a.ParticipantView("west-owner").Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory);
            Assert.False(a.DiagnosticFactoryExitUsable(factory.Id,PlayableEntityKind.Tank));a.Stop();
        }
        [Test] public void MetricsSubtractActualAcceptedObligationsAndTrackSettledIncomeWithoutAgingOnReads()
        {
            var m=Manifest("economy-low-v1");var a=new PlayableAuthorityTick(HeadlessFixtures.Create(m),m.Generation);var metric=new HeadlessEconomyMetrics(a,AiProfile.Initial);bool accepted=false;
            using(var host=new UnityHostRouteService())for(int i=0;i<600;i++)
            {
                for(int attempt=0;!a.TryAdvance();attempt++){Assert.Less(attempt,512);host.Service(a,64);}
                var live=a.CaptureDiagnosticBudgets().Single();accepted|=live.Unpaid.Count>0||live.Reserved.Count>0;
                var before=a.CaptureBytes();metric.Observe(a);metric.Observe(a);CollectionAssert.AreEqual(before,a.CaptureBytes());
                var report=metric.Reports.Single();var actual=a.ParticipantView("west-owner");
                Assert.AreEqual(live.Unpaid.Sum(e=>(long)e.Amount),report.LatestUnpaid);Assert.AreEqual(live.Reserved.Sum(e=>(long)e.Amount),report.LatestReserved);
                Assert.AreEqual(Math.Max(0,(long)actual.Credits-live.SafetyReserve-report.LatestUnpaid-report.LatestReserved),report.LatestSpendableBank);
            }
            var r=metric.Reports.Single();Assert.True(accepted);Assert.AreEqual(600,r.SampledTicks);Assert.Greater(r.PeakUnpaid,0);Assert.Greater(r.LowBankTicks,0);Assert.Greater(r.SettledIncome,0);
            Assert.AreEqual(AiProfile.SecondsToTicks(AiProfile.Initial.Value("economy.excessBankDeadlineSeconds"),30),r.ExcessBankDeadlineTicks);
            Assert.AreEqual(AiProfile.SecondsToTicks(AiProfile.Initial.Value("economy.idleLineDeadlineSeconds"),30),r.IdleLineDeadlineTicks);a.Stop();
        }
    }
}
