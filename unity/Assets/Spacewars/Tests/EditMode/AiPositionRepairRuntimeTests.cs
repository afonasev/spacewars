using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NativeAiArmyTests
    {
        [Test,Timeout(120000)]
        public void PositionRepair_ActualCombatWithdrawalHealingAndSafeJoin()=>RunPositionRepair(2);

        // Fresh current-profile recipe: pure Initialize selects Safe at seed1.
        // The historical seed2 case above stays runnable with every assertion.
        [Test,Timeout(120000)]
        public void PositionRepair_CurrentProfileActualCombatWithdrawalHealingAndSafeJoin()=>RunPositionRepair(1);

        private void RunPositionRepair(int seed)
        {
            // Preserve the original repair roster, home anchor and five
            // legal slots. The shared reinforcement helper now has a third
            // income slot and a different wave roster; neither belongs here.
            var basis=A4NativeConfig();var p=basis.Profile;var ai=AiProfile.Initial;
            var added=new[]{new TerritorySlot(3,new NavPoint(-24,9),0),new TerritorySlot(4,new NavPoint(-14,9),0),new TerritorySlot(5,new NavPoint(-14,-9),0)};
            var sites=basis.Sites.Select(s=>s.Id==1?new TerritorySite(s.Id,s.Kind,s.Position,s.Slots.Where(slot=>slot.Id<=2).Concat(added).ToArray()):s).ToArray();
            var victimAt=new NavPoint(1,-8);double repairRange=AiPositionDevelopmentPlanner.Range(p,PlayableBuildingKind.RepairTower);
            var pad=sites.Single(s=>s.Id==1).DefenseSlots(p).Where(s=>AiPositionDevelopmentPlanner.Distance(s.Position,victimAt)>repairRange+PlayableUnitRules.Radius(p,PlayableEntityKind.Tank))
                .OrderBy(s=>AiPositionDevelopmentPlanner.Distance(s.Position,victimAt)).ThenBy(s=>s.Id).First();
            var buildings=basis.ScenarioBuildings.Where(b=>b.SiteId!=1||b.SlotId<=2).Concat(new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.ScientificCenter,1,3,added[0].Position),
                new OfflineScenarioBuilding(1,PlayableBuildingKind.Refinery,1,4,added[1].Position),new OfflineScenarioBuilding(1,PlayableBuildingKind.Refinery,1,5,added[2].Position),
                new OfflineScenarioBuilding(1,PlayableBuildingKind.RepairTower,1,pad.Id,pad.Position,pad.Heading)}).ToArray();
            var units=new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-7,-8)),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-5,-8)),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-3,-8))}.Concat(new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-1,-8)),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,victimAt),
                new OfflineScenarioUnit(2,PlayableEntityKind.Hawk,new NavPoint(1,-2))}).ToArray();
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-24,0),new NavPoint(-24,6),pin:1),new OfflineStart("east",2,new NavPoint(24,24),new NavPoint(18,24),pin:2)};
            var config=new OfflineMatchConfiguration(p,"ai-position-repair-runtime-v1",basis.MapIdentity,basis.RouteProvenance,seed,basis.Roster.ToArray(),starts,sites,basis.Obstacles.ToArray(),new double[,]{{0,100},{100,0}},
                spectators:new[]{PlayableRuntime.LocalSpectatorId},scenario:units,scenarioBuildings:buildings);
            Assert.AreEqual(PlayableAiOpening.Safe,PlayableAiOpeningComposition.Initialize(config.Seed,"player-1",aiProfile:ai).Opening);
            var source=Environment.GetEnvironmentVariable("AI_POSITION_SOURCE_SHA");Assert.IsNotEmpty(source,"fixed candidate source must be supplied by the recorded invocation");
            var output=Path.GetFullPath(Path.Combine(".local/ai-position-ready","repair-"+source+"-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(output);
            string logDirectory=null;
            try
            {
                Assert.IsNull(TestMatchLogSession.CurrentDirectory,"Fixture owns an isolated logging session");
                TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));logDirectory=TestMatchLogSession.CurrentDirectory;
                Assert.IsNotEmpty(logDirectory);Assert.IsNotNull(PlayableRuntime.JournalFactory);
                File.WriteAllText(Path.Combine(output,"source-binding.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{source,config.SourceIdentity,config.MapIdentity,config.Seed,p.ProfileId,p.Revision,ai.Id,ai.Hash,logDirectory,editorBuildIdentity="Preserve session provenance; exact QA revision is bound by plan.json and this source record."}));
            var runtime=new PlayableRuntime(config,107,aiDifficulty:AiDifficulty.Veteran,spectator:false,aiProfile:ai);var routes=new UnityHostRouteService();
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;bool released=false;
            void Release()
            {
                if(released)return;var worker=(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",flags).GetValue(routes);runtime.RequestStop();routes.Dispose();runtime.Dispose();
                Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&(worker==null||worker.IsStopped),5000));
                var a=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(runtime);a.Domain.Navigation.ReleaseCommittedHostRoot();a.Domain.DestroyOwnedNavigation();released=true;
            }
            var releases=new System.Collections.Concurrent.ConcurrentQueue<object>();
            PlayableAuthorityTick liveAuthority=null;long lastNativeReadback=-100;int nativeReadbacks=0,beforeRecoveryReadbacks=0;
            object PendingNative(PlayableAiObservation o,PlayableEntitySnapshot[] members,AiArmyMissionState mission)
            {
                if(liveAuthority==null||nativeReadbacks>=8||mission.Reason!="recovered"&&beforeRecoveryReadbacks>=4||o.Tick-lastNativeReadback<100||!members.Any(u=>u.Queue?.PendingAiRouteViable==true))return null;
                lastNativeReadback=o.Tick;nativeReadbacks++;if(mission.Reason!="recovered")beforeRecoveryReadbacks++;var nav=liveAuthority.Domain.Navigation;
                var attempts=(System.Collections.IEnumerable)typeof(NavigationSession).GetField("arrivalAttempts",flags).GetValue(nav);var rows=new System.Collections.Generic.List<object>();
                foreach(var pair in attempts)
                {
                    var a=pair.GetType().GetProperty("Value").GetValue(pair);object Field(string name)=>a.GetType().GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).GetValue(a);
                    var intents=(NavigationRequest[])Field("Intents");if(!intents.Any(q=>members.Any(u=>u.Id==q.Entity)))continue;
                    var work=(NavigationArrivalWork)Field("Work");
                    rows.Add(new{header=PendingFields(a,1),intents=intents.Select(q=>new{q.Entity,q.Request,q.Order,q.Start,q.Goal}).ToArray(),
                        work=work==null?null:new{work.LogicalEpoch,work.MemberCursor,work.TerminalPageCount,work.CandidateCursor,work.OccupancyCursor,work.WorkPhase,work.AllocationQuotaRemaining,work.AllocationWorkConsumed,work.CandidateWorkConsumed,work.OccupancyWorkConsumed,work.DirectWorkConsumed,work.RouteCounters}});
                }
                var root=(NavigationHostRoot)typeof(NavigationSession).GetField("committedHostRoot",flags).GetValue(nav);
                return new{steps=0,queries=0,rows,host=root==null?null:new{root.Epoch,root.Progress,root.QuantumTick,root.QuantumSerial,root.ConsumedQuantum,root.QuantumBudget,root.QuantumCompleted,operation=PendingFields(root.Operation,2),frontier=PendingFields(root.Frontier,2)}};
            }
            AiArmyPlanner.RepairReleaseForQa=(army,tick,reason,clients)=>releases.Enqueue(new{army,tick,reason,clients});
            AiArmyPlanner.RepairStateForQa=(stage,o,army,mission,memory,records)=>{
                var members=o.Entities.Where(u=>army.Members.Contains(u.Id)).ToArray();var clients=AiRepairUsePolicy.Clients(memory);
                releases.Enqueue(new{stage,o.Tick,army=army.Copy(),mission=mission.Copy(),memory=memory.ToDictionary(x=>x.Key,x=>x.Value),members,
                    routes=o.RouteProofs.Where(r=>members.Any(u=>u.Id==r.UnitId)&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor).ToArray(),pendingNative=PendingNative(o,members,mission),
                    receipts=(records??Array.Empty<PlayableAiTraceRecord>()).Where(r=>members.Any(u=>u.OrderStamp?.ActionId==r.ActionId)).ToArray()});
            };
            try
            {
                liveAuthority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(runtime);
                var initial=runtime.ParticipantView("player-1");int victim=initial.Entities.Single(u=>u.Owner==initial.Owner&&u.Kind==PlayableEntityKind.Tank&&u.Position.Equals(victimAt)).Id;
                int tower=initial.Buildings.Single(b=>b.Owner==initial.Owner&&b.Kind==PlayableBuildingKind.RepairTower).Id;
                var enemy=runtime.ParticipantView("enemy-1");int hawk=enemy.Entities.Single(u=>u.Owner==enemy.Owner&&u.Kind==PlayableEntityKind.Hawk).Id;
                Assert.True(enemy.Entities.Any(u=>u.Id==victim),"scripted human attack uses real visibility");
                Assert.True(runtime.TrySubmit(new PlayableCommand(107,1,"enemy-1",PlayableCommandKind.Attack,new[]{hawk},targetId:victim).AsHuman()).Accepted);
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);var clock=Stopwatch.StartNew();
                bool wounded=false,enemyRetired=false,repairApplied=false,moved=false,healed=false,sawReinforcement=false,joined=false;double lowHealth=p.TankHealth;NavPoint moveStart=default;long repairSequence=0,repairAction=0,repairTick=-1,healedTick=-1,joinedTick=-1;
                var events=new System.Collections.Generic.List<object>();long lastTick=-1;
                while(runtime.Latest.Tick<2700&&clock.Elapsed.TotalSeconds<90&&!joined)
                {
                    routes.Service(runtime,p.NavigationRequestsPerFrame);var view=runtime.ParticipantView("player-1");var cp=liveAuthority.CaptureDiagnosticCheckpoints().Single(c=>c.OwnerId=="player-1");
                    Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);Assert.GreaterOrEqual(view.Credits,0);
                    if(view.Outcome!=PlayableMatchOutcome.Playing){events.Add(new{stage="match-ended-before-repair-complete",view.Tick,view.Outcome});break;}
                    if(view.Tick==lastTick){Thread.Sleep(1);continue;}lastTick=view.Tick;
                    var unit=view.Entities.SingleOrDefault(u=>u.Id==victim&&u.Health>0);if(unit==null)break;
                    var repair=view.Buildings.Single(b=>b.Id==tower);lowHealth=Math.Min(lowHealth,unit.Health);
                    if(!wounded&&PlayableUnitRules.HealthRatio(p,unit)<ai.Value("positionDevelopment.repairHealth"))
                    {wounded=true;events.Add(new{stage="actual-combat-injury",view.Tick,unit.Id,unit.Health});}
                    if(wounded&&!enemyRetired)
                    {
                        Assert.True(runtime.TrySubmit(new PlayableCommand(107,2,"enemy-1",PlayableCommandKind.Move,new[]{hawk},basis.Starts[1].ExplorerAnchor).AsHuman()).Accepted);
                        enemyRetired=true;events.Add(new{stage="scripted-enemy-retirement-accepted",view.Tick});
                    }
                    var stamp=unit.OrderStamp;var order=unit.CurrentOrder;
                    var receipt=cp?.Records.LastOrDefault(r=>AiArmyPlanner.IsArmyPolicy(r.Policy)&&r.Kind==PlayableCommandKind.Move&&r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&
                        stamp!=null&&r.CommandSequence==stamp.Sequence&&r.ActionId==stamp.ActionId&&r.ApplicationTick==stamp.Tick&&r.ReceiptIdentity?.OwnerId=="player-1"&&r.ReceiptIdentity.Generation==107);
                    bool memo=cp?.ArmyMission?.RepairClients?.Keys.Any(key=>AiRepairUsePolicy.Parse(key,out var client,out var repairTower,out _)&&client==victim&&repairTower==tower&&key.Split(':').Length==5&&long.TryParse(key.Split(':')[4],out var sequence)&&sequence==stamp?.Sequence)==true;
                    bool currentRepair=wounded&&memo&&receipt!=null&&order?.Kind==PlayableTacticalOrderKind.Move&&order.CommandSequence==stamp.Sequence&&order.Generation==107&&stamp.Owner==view.Owner&&stamp.UnitId==victim&&stamp.Origin==PlayableOrderOrigin.Ai&&stamp.Source==PlayableAiOpeningComposition.SourceIdentity&&
                        stamp.JobId==stamp.ActionId&&AiPositionDevelopmentPlanner.Distance(order.Destination,repair.Position)<=repairRange;
                    if(currentRepair&&!repairApplied)
                    {repairApplied=true;moveStart=unit.Position;repairSequence=stamp.Sequence;repairAction=stamp.ActionId;repairTick=view.Tick;events.Add(new{stage="repair-effective-command",view.Tick,unit.Id,unit.Health,unit.Position,order,stamp,receipt});}
                    if(repairApplied&&!moved&&view.Tick>repairTick&&AiPositionDevelopmentPlanner.Distance(moveStart,unit.Position)>PlayableUnitRules.Radius(p,unit.Kind)&&AiPositionDevelopmentPlanner.Distance(unit.Position,repair.Position)<AiPositionDevelopmentPlanner.Distance(moveStart,repair.Position))
                    {moved=true;events.Add(new{stage="actual-movement",view.Tick,unit.Position,repairSequence,repairAction});}
                    if(repairApplied&&moved&&repair.Healing?.TargetId==victim&&unit.Health>lowHealth)
                    {if(!healed)events.Add(new{stage="actual-healing",view.Tick,unit.Health,lowHealth,repair.Healing});healed=true;healedTick=view.Tick;}
                    var army=cp?.Armies?.Armies.FirstOrDefault(a=>a.Members.Contains(victim)&&a.Phase!=AiArmyPhase.Disbanded);
                    sawReinforcement|=repairApplied&&army!=null&&army.Reinforcements.Contains(victim);
                    if(healed&&sawReinforcement&&army!=null&&!army.Reinforcements.Contains(victim)&&army.LeaderId!=victim&&
                        (army.Phase==AiArmyPhase.Advancing||army.Phase==AiArmyPhase.Engaging)&&PlayableUnitRules.HealthRatio(p,unit)>=ai.Value("positionDevelopment.repairReturnHealth"))
                    {
                        var leader=view.Entities.FirstOrDefault(u=>u.Id==army.LeaderId&&u.Owner==view.Owner&&u.Health>0);
                        bool physical=leader!=null&&AiPositionDevelopmentPlanner.Distance(unit.Position,leader.Position)<=p.FollowDistance+p.FollowArrivalTolerance+PlayableUnitRules.Radius(p,unit.Kind)+PlayableUnitRules.Radius(p,leader.Kind);
                        bool follow=AiArmyPlanner.AssemblyFollow(PlayableAiObservation.From(view),army,unit,cp.Records);
                        if(physical&&follow){joined=true;joinedTick=view.Tick;events.Add(new{stage="physical-safe-join",view.Tick,unit.Health,unit.Position,leaderId=leader.Id,leaderPosition=leader.Position,armyId=army.Id,army.Phase});}
                    }
                    if(events.Count>32)Assert.Fail("bounded event capture overflow");Thread.Sleep(1);
                }
                var request=runtime.RequestCaptureBytes();var captureClock=Stopwatch.StartNew();while(!request.IsCompleted&&captureClock.Elapsed.TotalSeconds<10){routes.Service(runtime,p.NavigationRequestsPerFrame);Thread.Sleep(1);}
                Assert.True(request.IsCompleted);Assert.False(request.IsFaulted,request.Exception?.ToString());var bytes=request.Result;File.WriteAllBytes(Path.Combine(output,"current.bin"),bytes);var finalCheckpoint=liveAuthority.CaptureDiagnosticCheckpoints().Single(c=>c.OwnerId=="player-1");var humanReceipts=runtime.ReceiptsAfter(0).Where(r=>r.OwnerId=="enemy-1").ToArray();
                // Preserve observed gameplay even if the independent exact-restore
                // gate throws. This file makes no restore or acceptance claim.
                File.WriteAllText(Path.Combine(output,"progress-before-restore.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{source,config.Seed,generation=107,victim,tower,wounded,enemyRetired,repairApplied,moved,healed,sawReinforcement,joined,repairTick,healedTick,joinedTick,lowHealth,events,repairReleases=releases.ToArray(),humanReceipts,finalCheckpoint,restore="NotRun"},Newtonsoft.Json.Formatting.Indented));
                Release();
                PlayableRuntime restored=null;PlayableAuthorityTick authority=null;Exception error=null;bool exact=false;
                try
                {
                    restored=PlayableRuntime.RestoreBytesForTests(bytes,config,ai,r=>{try{authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(r);exact=bytes.SequenceEqual(r.CaptureBeforeStartForTests());}catch(Exception e){error=e;}finally{r.RequestStop();}});
                    Assert.True(SpinWait.SpinUntil(()=>restored.IsStopped,3000));if(error!=null)Assert.Fail(error.ToString());
                }
                finally{if(restored!=null){restored.RequestStop();Assert.True(SpinWait.SpinUntil(()=>restored.IsStopped,3000));restored.Dispose();}if(authority!=null){authority.Domain.Navigation.ReleaseCommittedHostRoot();authority.Domain.DestroyOwnedNavigation();}}
                File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{source,config.Seed,generation=107,ai.Id,ai.Hash,victim,tower,pad,hawk,wounded,enemyRetired,repairApplied,moved,healed,sawReinforcement,joined,repairTick,healedTick,joinedTick,lowHealth,exact,events,repairReleases=releases.ToArray(),humanReceipts,finalCheckpoint,limitTicks=2700,wallLimitSeconds=90,wallSeconds=clock.Elapsed.TotalSeconds},Newtonsoft.Json.Formatting.Indented));
                Assert.True(humanReceipts.Any(r=>r.Sequence==1&&r.Status==PlayableCommandStatus.Applied),"scripted attack must actually apply");Assert.True(humanReceipts.Any(r=>r.Sequence==2&&r.Status==PlayableCommandStatus.Applied),"scripted retirement must actually apply");
                Assert.True(wounded,"real combat must damage the selected nonleader");Assert.True(repairApplied,"AI must apply an exact receipt-bound repair Move");Assert.True(moved,"real host must move that unit toward repair");
                Assert.True(healed,"repair tower must restore actual health on selected unit");Assert.True(joined,"healed unit must physically safely rejoin a live original army core with exact Follow");Assert.True(exact);
            }
            finally{AiArmyPlanner.RepairReleaseForQa=null;AiArmyPlanner.RepairStateForQa=null;Release();}
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
                        Assert.AreEqual(107,(long)manifest["Generation"]);Assert.AreEqual(config.Seed,(int)manifest["Seed"]);
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
