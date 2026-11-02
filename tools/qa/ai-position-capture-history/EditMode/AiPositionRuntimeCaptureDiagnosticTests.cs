using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class AiPositionRuntimeCaptureDiagnosticTests
    {
        [Test,Timeout(180000)] public void ExistingSnapshotFullRuntimeRoundtripAndGameplayCheckpoint()
        {
            string input=Environment.GetEnvironmentVariable("AI_POSITION_CAPTURE");Assert.IsNotEmpty(input);
            string output=Path.Combine(Directory.GetParent(Path.GetDirectoryName(Path.GetFullPath(input))).FullName,"runtime-capture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
            var clock=Stopwatch.StartNew();
            void Mark(string phase,object details=null)=>File.AppendAllText(Path.Combine(output,"phases.jsonl"),Newtonsoft.Json.JsonConvert.SerializeObject(new{phase,elapsedSeconds=clock.Elapsed.TotalSeconds,details})+"\n");
            Mark("read-before");var bytes=File.ReadAllBytes(input);using(var sha=SHA256.Create())Assert.AreEqual("8b03cb9013fb50abf9f2a6cbc08e7612fe6b576938b7dff25e925482774acfe4",BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant());Mark("read-after");
            const int seed=7;const long generation=107;
            var p=PlayableProfile.Default;var ai=NativeBalanceFields.InitialAiOpeningProfile();
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-20,-15),new NavPoint(-20,-27),pin:1),new OfflineStart("east",2,new NavPoint(20,15),new NavPoint(20,27),pin:2)};
            var home=new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,p);
            var enemy=new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,p);
            var front=new TerritorySite(3,PlayableBuildingKind.Outpost,new NavPoint(-20,15),p);
            var kinds=new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Refinery,PlayableBuildingKind.Refinery,PlayableBuildingKind.Refinery,PlayableBuildingKind.ScientificCenter};
            Assert.GreaterOrEqual(home.Slots.Count,kinds.Length);
            var buildings=kinds.Select((kind,i)=>new OfflineScenarioBuilding(1,kind,1,home.Slots[i].Id,home.Slots[i].Position,home.Slots[i].Heading)).Concat(new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.Outpost,3,0,front.Position)}).ToArray();
            var units=Enumerable.Range(0,5).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-25+i*3,-1))).Concat(new[]{new OfflineScenarioUnit(2,PlayableEntityKind.Hawk,new NavPoint(20,-24))}).ToArray();
            var config=new OfflineMatchConfiguration(p,"ai-position-development-first-witness-v1","flat-public-position-v1","UnityHostRouteService",seed,new[]{new OfflineParticipant("position-owner",1,1,OfflineControl.Ai),new OfflineParticipant("human-owner",2,2,OfflineControl.Human)},starts,new[]{home,enemy,front},PlayableMap.StaticObstacles(p),new double[,]{{0,100},{100,0}},scenario:units,scenarioBuildings:buildings,spectators:new[]{PlayableRuntime.LocalSpectatorId});

            Assert.AreEqual("8d2ab24c1989b3fa52b21f175a4fbfe9b29e423eae5328ae55e606b9566c64ff",ai.Hash);
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            PlayableRuntime clone=null;byte[] roundtrip=null;Exception prestartFailure=null;
            try
            {
                Mark("runtime-restore-before");
                clone=PlayableRuntime.RestoreBytesForTests(bytes,config,ai,r=>
                {
                    try
                    {
                    Mark("prestart-enter");
                    var authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(r);
                    var checkpoint=authority.CaptureDiagnosticCheckpoints().Single(c=>c.OwnerId=="position-owner");
                    var scheduler=typeof(PlayableAuthorityTick).GetField("scheduler",flags).GetValue(authority);
                    var owner=((System.Collections.IEnumerable)scheduler.GetType().GetField("owners",flags).GetValue(scheduler)).Cast<object>().Single();
                    var ledger=(AiBudgetLedger)owner.GetType().GetField("budget",flags).GetValue(owner);
                    var observation=PlayableAiObservation.From(authority.Latest);
                    var opening=(AiOpeningExecutor)owner.GetType().GetField("openingExecutor",flags).GetValue(owner);
                    var view=authority.Latest;
                    var facts=new{producer="3e7e2a89b516e987b59636cf5c3b5124e4875f4c",view.Tick,view.Outcome,view.Credits,checkpoint.OpeningExecution,checkpoint.ArmyMission,checkpoint.Doctrine,
                        defense=view.Buildings.Where(b=>b.Owner==view.Owner&&TerritoryRules.Defensive(b.Kind)).Select(b=>new{b.Id,b.Kind,b.SiteId,b.SlotId,b.Phase,b.Health,b.Progress,b.Turret,b.Healing}),
                        paid=ledger.Capture().Paid.Select(e=>new{e.Id,e.Amount,e.Action.Kind,e.Action.BuildingKind,e.Action.SiteId,e.Action.SlotId,e.Action.ResearchKind,Receipt=e.Receipt?.Id}),
                        budget=new{ledger.Liquid,ledger.Available,ledger.Reserved,ledger.Unpaid,ledger.SafetyReserve},
                        units=view.Entities.Select(u=>new{u.Id,u.Owner,u.Kind,u.Health,u.Position,u.CurrentOrder,u.OrderStamp}),
                        records=checkpoint.Records,
                        missingEssentialEconomy=opening.MissingEssentialEconomy(observation,ai),
                        currentPurePositionQuotes=AiPositionDevelopmentPlanner.Plan(observation,p,ai,checkpoint.Opening.WithPhase(checkpoint.OpeningExecution.Phase),checkpoint.Knowledge,true),
                        historicalMovementAndProjectileAttribution="unknown unless original per-frame evidence exists; snapshot alone is not PASS"};
                    File.WriteAllText(Path.Combine(output,"checkpoint.json"),Newtonsoft.Json.JsonConvert.SerializeObject(facts,Newtonsoft.Json.Formatting.Indented));Mark("checkpoint-written");
                    Mark("runtime-capture-before");roundtrip=r.CaptureBeforeStartForTests();Mark("runtime-capture-after",new{bytes=roundtrip.Length});
                    }
                    catch(Exception error){prestartFailure=error;Mark("prestart-failed",error.ToString());}
                    finally{r.RequestStop();}
                });
                Mark("runtime-restore-returned");Assert.True(SpinWait.SpinUntil(()=>clone.IsStopped,5000));if(prestartFailure!=null)Assert.Fail(prestartFailure.ToString());
                using(var before=PlayableWorldState.Decode(bytes))using(var after=PlayableWorldState.Decode(roundtrip)){Assert.Greater(before.RuntimeTransport.Length,0);Assert.AreEqual(before.RuntimeTransport.Length,after.RuntimeTransport.Length);Mark("envelopes",new{before.Tick,afterTick=after.Tick,beforeTransport=before.RuntimeTransport.Length,afterTransport=after.RuntimeTransport.Length});}
                Mark("exact-before");ExactByteAssert.AreEqual(bytes,roundtrip,"full runtime roundtrip including transport");Mark("exact-after");
            }
            finally{if(clone!=null){clone.RequestStop();Assert.True(SpinWait.SpinUntil(()=>clone.IsStopped,5000));clone.Dispose();}Mark("cleanup-after");}
        }
    }
}
