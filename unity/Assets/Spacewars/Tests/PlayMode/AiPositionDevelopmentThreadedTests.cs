using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class AiPositionDevelopmentThreadedTests
    {
        // Fixed first cycle: ordinary bank/profile, seed7, 5400 ticks, 150s wall.
        // Authored economic position is legal scenario input, never mutated world state.
        [UnityTest,Timeout(180000)] public IEnumerator OrdinaryAiBuildsReadySentryAndFiresAfterActualMovement()=>RunSentry(false);
        // A legal developed position isolates the approved midgame obligation
        // from the separate opening-home-defense and first-repair investments.
        // The current stock mixed wave is sufficient without excess ground
        // groups. The human aircraft waits behind its own base, outside initial
        // AI vision, until the newly paid outpost tower is ready.
        // Earlier candidate scenarios and their failures remain in source history
        // and qualification-20261102 evidence; the original case stays unchanged.
        [UnityTest,Timeout(180000)] public IEnumerator CurrentMidgameAiPaysBuildsAndFiresOutpostSentry()=>RunSentry(true,true);
        private IEnumerator RunSentry(bool currentProfileForce,bool developedPosition=false)
        {
            const int seed=7;const long generation=107;
            var p=PlayableProfile.Default;var ai=NativeBalanceFields.InitialAiOpeningProfile();
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-20,-15),new NavPoint(-20,-27),pin:1),new OfflineStart("east",2,new NavPoint(20,15),new NavPoint(20,27),pin:2)};
            var home=new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,p);
            var enemy=new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,p);
            var front=new TerritorySite(3,PlayableBuildingKind.Outpost,new NavPoint(-20,15),p);
            var kinds=new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Refinery,PlayableBuildingKind.Refinery,PlayableBuildingKind.Refinery,PlayableBuildingKind.ScientificCenter};
            Assert.GreaterOrEqual(home.Slots.Count,kinds.Length);
            var buildings=kinds.Select((kind,i)=>new OfflineScenarioBuilding(1,kind,1,home.Slots[i].Id,home.Slots[i].Position,home.Slots[i].Heading)).Concat(new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.Outpost,3,0,front.Position)}).ToArray();
            var units=Enumerable.Range(0,developedPosition?2:5).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-25+i*3,-1))).Concat(new[]{new OfflineScenarioUnit(2,PlayableEntityKind.Hawk,developedPosition?new NavPoint(16,29):new NavPoint(20,-24))}).ToArray();
            if(currentProfileForce)units=units.Concat(new[]{
                new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(-25,-4)),
                new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(-22,-4))}).ToArray();
            if(developedPosition){
                var homePad=home.DefenseSlots(p).First();var frontPad=front.DefenseSlots(p).First();
                buildings=buildings.Concat(new[]{
                    new OfflineScenarioBuilding(1,PlayableBuildingKind.SentryTurret,1,homePad.Id,homePad.Position,homePad.Heading),
                    new OfflineScenarioBuilding(1,PlayableBuildingKind.RepairTower,3,frontPad.Id,frontPad.Position,frontPad.Heading)}).ToArray();
            }
            var config=new OfflineMatchConfiguration(p,developedPosition?"ai-position-development-current-midgame-witness-v1":currentProfileForce?"ai-position-development-current-profile-witness-v1":"ai-position-development-first-witness-v1","flat-public-position-v1","UnityHostRouteService",seed,new[]{new OfflineParticipant("position-owner",1,1,OfflineControl.Ai),new OfflineParticipant("human-owner",2,2,OfflineControl.Human)},starts,new[]{home,enemy,front},PlayableMap.StaticObstacles(p),new double[,]{{0,100},{100,0}},scenario:units,scenarioBuildings:buildings,spectators:new[]{PlayableRuntime.LocalSpectatorId});
            var source=Environment.GetEnvironmentVariable("AI_POSITION_SOURCE_SHA");Assert.IsNotEmpty(source,"fixed candidate source must be supplied by the recorded invocation");
            var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/ai-position-development/ordinary-first-"+source+"-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(output);
            string logDirectory=null;
            try
            {
                Assert.IsNull(TestMatchLogSession.CurrentDirectory,"Fixture owns an isolated logging session");
                TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));logDirectory=TestMatchLogSession.CurrentDirectory;
                Assert.IsNotEmpty(logDirectory);Assert.IsNotNull(PlayableRuntime.JournalFactory);
                File.WriteAllText(Path.Combine(output,"source-binding.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{source,config.SourceIdentity,config.MapIdentity,config.Seed,p.ProfileId,p.Revision,ai.Id,ai.Hash,logDirectory,editorBuildIdentity="Preserve session provenance; exact QA revision is bound by plan.json and this source record."}));
            var runtime=new PlayableRuntime(config,generation,participantDifficulties:new[]{AiDifficulty.Veteran,AiDifficulty.Fighter},spectator:false,aiProfile:ai);var routes=new UnityHostRouteService();
            var movement=new Dictionary<int,(NavPoint Point,long Tick,long Action)>();bool moved=false,ready=false,shot=false,damage=false;int towerId=0,towerSlot=0,targetId=0;long command=0;var towerProjectiles=new HashSet<int>();var contacts=new List<object>();var motionEvidence=new List<object>();var motionRecorded=new HashSet<(int,long)>();bool originalReleased=false;
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            CertifiedRouteWorker originalRouteWorker=null;bool originalStopRequested=false;
            void StopOriginal()
            {
                if(!originalStopRequested)
                {
                    originalRouteWorker=(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",flags).GetValue(routes);
                    runtime.RequestStop();routes.Dispose();runtime.Dispose();originalStopRequested=true;
                }
                Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped&&(originalRouteWorker==null||originalRouteWorker.IsStopped),5000),"runtime and route-worker detach ACK before navigation destruction");
                var stoppedAuthority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(runtime);
                stoppedAuthority.Domain.Navigation.ReleaseCommittedHostRoot();stoppedAuthority.Domain.DestroyOwnedNavigation();originalReleased=true;
            }
            var phaseClock=Stopwatch.StartNew();
            void Mark(string phase,object details=null)=>File.AppendAllText(Path.Combine(output,"phases.jsonl"),Newtonsoft.Json.JsonConvert.SerializeObject(new{phase,elapsedSeconds=phaseClock.Elapsed.TotalSeconds,details})+"\n");
            try
            {
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);float deadline=Time.realtimeSinceStartup+150;
                while(runtime.Latest.Tick<5400&&!runtime.IsStopped&&Time.realtimeSinceStartup<deadline)
                {
                    routes.Service(runtime,p.NavigationRequestsPerFrame);var frame=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);var own=frame.Players.Single(v=>v.Owner==PlayableOwner.Player);
                    Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);Assert.GreaterOrEqual(own.Credits,0);
                    foreach(var u in own.Entities.Where(u=>u.Owner==own.Owner&&u.Health>0&&u.CurrentOrder!=null&&u.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&u.OrderStamp.Source==PlayableAiOpeningComposition.SourceIdentity&&u.CurrentOrder.CommandSequence==u.OrderStamp.Sequence))
                    {
                        if(!movement.TryGetValue(u.Id,out var prior)||prior.Action!=u.OrderStamp.ActionId)movement[u.Id]=(u.Position,own.Tick,u.OrderStamp.ActionId);
                        else if(own.Tick>prior.Tick&&AiPositionDevelopmentPlanner.Distance(prior.Point,u.Position)>PlayableUnitRules.Radius(p,u.Kind))
                        {moved=true;if(motionRecorded.Add((u.Id,u.OrderStamp.ActionId)))motionEvidence.Add(new{tick=own.Tick,u.Id,u.Kind,start=prior.Point,position=u.Position,prior.Tick,u.OrderStamp.ActionId,u.OrderStamp.Sequence,u.OrderStamp.Generation,u.OrderStamp.Source});}
                    }
                    var tower=own.Buildings.FirstOrDefault(b=>b.Owner==own.Owner&&b.SiteId==3&&b.Kind==PlayableBuildingKind.SentryTurret&&b.Phase==ConstructionPhase.Ready);
                    if(tower!=null)
                    {
                        ready=true;towerId=tower.Id;towerSlot=tower.SlotId;shot|=tower.Turret?.ShotSequence>0;
                        foreach(var projectile in frame.Overview.Projectiles.Where(q=>q.OwnerId==towerId))towerProjectiles.Add(projectile.Id);
                        var target=frame.Overview.Entities.FirstOrDefault(u=>u.Owner==PlayableOwner.Enemy&&u.Kind==PlayableEntityKind.Hawk);
                        if(command==0&&target!=null)
                        {
                            targetId=target.Id;command=1;
                            var goal=new NavPoint(tower.Position.X+p.TankCollisionRadius+p.TurretFootprintRadius+p.Navigation.ArrivalTolerance,tower.Position.Z);
                            Assert.AreEqual(PlayableCommandStatus.Accepted,runtime.TrySubmit(new PlayableCommand(generation,command,"human-owner",PlayableCommandKind.Move,new[]{targetId},goal)).Status);
                        }
                        if(targetId>0)
                        foreach(var sound in frame.Overview.Sounds.Where(s=>s.Kind==PlayableSoundKind.Impact&&s.Contact!=null&&s.Contact.TargetId==targetId&&towerProjectiles.Contains(s.Contact.ProjectileId)))
                        {contacts.Add(new{sound.Tick,sound.Contact.ProjectileId,sound.Contact.TargetId,sound.Contact.Impact});damage|=target==null||target.Health<p.HawkHealth;}
                    }
                    if(moved&&ready&&shot&&damage)break;yield return null;
                }
                File.WriteAllText(Path.Combine(output,"gameplay-before-restore.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{source,seed,generation,loopTick=runtime.Latest.Tick,moved,ready,shot,damage,towerId,towerSlot,targetId,motionEvidence,contacts,towerProjectiles},Newtonsoft.Json.Formatting.Indented));
                Mark("input-capture-before");var capture=runtime.RequestCaptureBytes();while(!capture.IsCompleted){routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}
                Assert.False(capture.IsFaulted,capture.Exception?.ToString());File.WriteAllBytes(Path.Combine(output,"current.bin"),capture.Result);Mark("input-capture-after",new{bytes=capture.Result.Length});
                long capturedTick;int transportLength;using(var state=PlayableWorldState.Decode(capture.Result)){capturedTick=state.Tick;transportLength=state.RuntimeTransport.Length;}
                Mark("original-stop-before");StopOriginal();Mark("original-stop-after");
                PlayableAiOwnerCheckpoint checkpoint=null;AiBudgetEntry paidTower=null;byte[] roundtrip=null;Exception prestartFailure=null;PlayableRuntime restored=null;PlayableAuthorityTick authority=null;
                try
                {
                    Mark("runtime-restore-before");restored=PlayableRuntime.RestoreBytesForTests(capture.Result,config,ai,r=>
                    {
                        try
                        {
                            Mark("prestart-enter");authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(r);
                            checkpoint=authority.CaptureDiagnosticCheckpoints().Single(c=>c.OwnerId=="position-owner");
                            var scheduler=typeof(PlayableAuthorityTick).GetField("scheduler",flags).GetValue(authority);
                            var owner=((System.Collections.IEnumerable)scheduler.GetType().GetField("owners",flags).GetValue(scheduler)).Cast<object>().Single();
                            var ledger=(AiBudgetLedger)owner.GetType().GetField("budget",flags).GetValue(owner);
                            paidTower=ledger.Capture().Paid.SingleOrDefault(e=>e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.BuildingKind==PlayableBuildingKind.SentryTurret&&e.Action.SiteId==3&&e.Action.SlotId==towerSlot);
                            Mark("runtime-capture-before");roundtrip=r.CaptureBeforeStartForTests();Mark("runtime-capture-after",new{bytes=roundtrip.Length});
                        }
                        catch(Exception error){prestartFailure=error;Mark("prestart-failed",error.ToString());}
                        finally{r.RequestStop();}
                    });Mark("runtime-restore-returned");Assert.True(System.Threading.SpinWait.SpinUntil(()=>restored.IsStopped,3000));if(prestartFailure!=null)Assert.Fail(prestartFailure.ToString());
                    using(var state=PlayableWorldState.Decode(roundtrip)){Assert.Greater(state.RuntimeTransport.Length,0);Assert.AreEqual(transportLength,state.RuntimeTransport.Length);Assert.AreEqual(capturedTick,state.Tick);}
                    bool applied=paidTower!=null&&paidTower.Amount==p.TurretCreditCost&&checkpoint.Records.Any(r=>r.ReceiptIdentity?.Id==paidTower.Receipt?.Id&&r.Policy=="economy"&&r.Kind==PlayableCommandKind.BuildAt&&r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&r.ReceiptIdentity?.OwnerId=="position-owner"&&r.ReceiptIdentity.Generation==generation);
                    Mark("exact-before");bool exact=capture.Result.SequenceEqual(roundtrip);Mark("exact-after",new{exact});
                    File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{source,output,seed,currentProfileForce,developedPosition,p.ProfileId,p.Revision,ai.Id,ai.Hash,tick=capturedTick,moved,applied,ready,shot,damage,exact,towerId,towerSlot,targetId,paidTower,contacts,motionEvidence,records=checkpoint.Records,checkpoint.OpeningExecution,checkpoint.ArmyMission,failure=runtime.Latest.Failure},Newtonsoft.Json.Formatting.Indented));
                    Assert.True(exact,"current exact full runtime restoration including transport");
                Assert.True(moved,"AI command must produce actual physical movement");Assert.True(applied,"ordinary AI construction has Applied receipt");Assert.True(ready,"AI paid a legal sentry and completed construction at held outpost");Assert.True(shot&&damage,"that turret actually fires and damages the approaching enemy");
                }
                finally{if(restored!=null){restored.RequestStop();Assert.True(System.Threading.SpinWait.SpinUntil(()=>restored.IsStopped,3000));restored.Dispose();}authority?.Domain.Navigation.DestroyNavigationAuthority();Mark("restored-cleanup-after");}
            }
            finally{if(!originalReleased)StopOriginal();}
            }
            finally
            {
                if(logDirectory!=null)
                {
                    TestMatchLogSession.Shutdown();
                    var summaries=Directory.GetFiles(logDirectory,"summary.json",SearchOption.AllDirectories);
                    Assert.GreaterOrEqual(summaries.Length,1,"Live runtime must have a completed journal even when gameplay fails");
                    foreach(var file in summaries)
                    {
                        var summary=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));
                        Assert.AreEqual("complete",(string)summary["Status"]);Assert.Zero((long)summary["Dropped"]);Assert.Greater((long)summary["Written"],0);
                    }
                    foreach(var file in Directory.GetFiles(logDirectory,"manifest.json",SearchOption.AllDirectories))
                    {
                        var manifest=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));
                        Assert.AreEqual(generation,(long)manifest["Generation"]);Assert.AreEqual(config.Seed,(int)manifest["Seed"]);
                        Assert.AreEqual(config.MapIdentity,(string)manifest["Map"]);Assert.AreEqual(ai.Hash,(string)manifest["Ai"]["Hash"]);
                    }
                    Assert.Zero(new FileInfo(Path.Combine(logDirectory,"errors.jsonl")).Length,"No hidden recorder or runtime errors");
                    var end=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(logDirectory,"session-end.json")));
                    Assert.False((bool)end["RecorderFailure"]);
                    File.WriteAllText(Path.Combine(output,"logging-verification.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{source,logDirectory,matches=summaries.Length,complete=true,dropped=0,errors=0}));
                }
            }
        }
    }
}
