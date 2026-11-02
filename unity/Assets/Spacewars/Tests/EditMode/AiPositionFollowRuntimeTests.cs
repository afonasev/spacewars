using System;
using System.Collections.Generic;
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
        // Isolate the actual repair's moving-leader Follow leg using its building
        // geometry and poses. No AI, combat, reissue, manual Pump or budget changes.
        [Test,Timeout(90000)] public void PositionRepair_ActualFollowTracksMovingLeaderAroundTower()
            =>ActualFollowAroundTower(true);
        [Test,Timeout(90000)] public void PositionRepair_ActualFollowReachesStationaryLeaderAroundTower()
            =>ActualFollowAroundTower(false);
        [Test,Timeout(90000)] public void PositionRepair_ActualLeaderWithdrawalHomeAroundTower()
            =>ActualFollowAroundTower(true,true);
        private void ActualFollowAroundTower(bool movingLeader,bool withdrawal=false)
        {
            var basis=PendingReadbackConfiguration();var p=basis.Profile;var ai=AiProfile.Initial;
            var from=new NavPoint(-9.14202072178326,2.033802542241749);
            var leaderFrom=movingLeader?new NavPoint(-11.378726304813966,6.917391112577249):new NavPoint(-11.073793140956237,8.332341400886904);
            var destination=new NavPoint(-14.99669103036344,3.6420231500973106);
            if(withdrawal){leaderFrom=new NavPoint(-0.143598124779393,7.579556381981167);destination=new NavPoint(-19.589094954248743,-1.8157138203379113);}
            var config=new OfflineMatchConfiguration(p,withdrawal?"repair-leader-home-isolation-v1":movingLeader?"repair-follow-tower-isolation-v1":"repair-stationary-follow-tower-isolation-v1",basis.MapIdentity,basis.RouteProvenance,basis.Seed,
                basis.Roster.Select(x=>new OfflineParticipant(x.Id,x.LogicalPlayer,x.Team,OfflineControl.Human)).ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),new double[,]{{0,100},{100,0}},
                spectators:new[]{PlayableRuntime.LocalSpectatorId},scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,from,2.36155819408366),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,leaderFrom,withdrawal?-2.420785037470848:movingLeader?-2.7924710170225993:-2.7578390146001324)},scenarioBuildings:basis.ScenarioBuildings.ToArray());
            var source=Environment.GetEnvironmentVariable("AI_POSITION_SOURCE_SHA");Assert.IsNotEmpty(source);
            var output=Path.GetFullPath(Path.Combine(".local/ai-position-ready",(withdrawal?"leader-home-":movingLeader?"follow-tower-":"follow-stationary-")+source+"-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(output);
            PlayableRuntime runtime=null;UnityHostRouteService host=null;PlayableAuthorityTick authority=null;CertifiedRouteWorker worker=null;NavigationRetentionBudget budget=null;string logDirectory=null;
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;bool released=false;
            void Release(){if(released||runtime==null)return;runtime.RequestStop();if(host!=null){worker=(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",flags).GetValue(host);host.Dispose();}runtime.Dispose();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&(worker==null||worker.IsStopped),5000));authority?.Domain.Navigation.ReleaseCommittedHostRoot();authority?.Domain.DestroyOwnedNavigation();released=true;}
            var receipts=new List<PlayableCommandReceipt>();var trace=new List<object>();var watch=Stopwatch.StartNew();bool joined=false,leaderMoved=false,followerMoved=false,exact=false;long last=-1,maxGraph=0,maxConnector=0;string lastMark=null;
            void Service(){host.Service(runtime,p.NavigationRequestsPerFrame);receipts.AddRange(runtime.DrainReceipts());Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(host.LastProjectionFailure);}
            try{
                Assert.IsNull(TestMatchLogSession.CurrentDirectory);TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));logDirectory=TestMatchLogSession.CurrentDirectory;Assert.IsNotEmpty(logDirectory);
                File.WriteAllText(Path.Combine(output,"source-binding.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{source,config.SourceIdentity,config.MapIdentity,config.Seed,p.ProfileId,p.Revision,logDirectory,editorBuildIdentity="Exact QA revision is bound by plan.json; preserve original session provenance."}));
                runtime=new PlayableRuntime(config,107,startPaused:true,aiProfile:ai);host=new UnityHostRouteService();authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(runtime);var nav=authority.Domain.Navigation;budget=nav.PhysicalHostBudget;
                while(!(runtime.IsPrepared&&runtime.Latest.Paused)&&watch.Elapsed.TotalSeconds<10){Service();Thread.Sleep(1);}Assert.True(runtime.IsPrepared&&runtime.Latest.Paused);
                var initial=runtime.ParticipantView("player-1");int follower=initial.Entities.Single(u=>u.Position.Equals(from)).Id,leader=initial.Entities.Single(u=>u.Position.Equals(leaderFrom)).Id;
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);runtime.RequestPause(false);
                if(!withdrawal)Assert.True(runtime.TrySubmit(new PlayableCommand(107,1,"player-1",PlayableCommandKind.Follow,new[]{follower},targetId:leader).AsHuman()).Accepted);
                if(movingLeader)Assert.True(runtime.TrySubmit(new PlayableCommand(107,2,"player-1",PlayableCommandKind.Move,new[]{leader},destination).AsHuman()).Accepted);
                while(runtime.Latest.Tick<900&&watch.Elapsed.TotalSeconds<45&&!joined){
                    Service();var view=runtime.ParticipantView("player-1");if(view.Tick==last){Thread.Sleep(1);continue;}last=view.Tick;
                    var u=view.Entities.Single(e=>e.Id==follower);var l=view.Entities.Single(e=>e.Id==leader);double distance=AiPositionDevelopmentPlanner.Distance(u.Position,l.Position);
                    leaderMoved|=AiPositionDevelopmentPlanner.Distance(l.Position,leaderFrom)>PlayableUnitRules.Radius(p,l.Kind);followerMoved|=AiPositionDevelopmentPlanner.Distance(u.Position,from)>PlayableUnitRules.Radius(p,u.Kind);
                    joined=(movingLeader?leaderMoved&&l.NavigationOutcome==NavigationOutcome.Arrived:!leaderMoved&&l.Position.Equals(leaderFrom))&&followerMoved&&!l.Moving&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow&&u.CurrentOrder.TargetId==leader&&distance<=p.FollowDistance+p.FollowArrivalTolerance+PlayableUnitRules.Radius(p,u.Kind)+PlayableUnitRules.Radius(p,l.Kind);
                    if(withdrawal)joined=leaderMoved&&!l.Moving&&l.NavigationOutcome==NavigationOutcome.Arrived;
                    var root=nav.PinCommittedHostRoot();try{foreach(var pair in root.ArrivalWork){var w=(NavigationArrivalWork)pair.Value;maxGraph=Math.Max(maxGraph,w.RouteCounters.LogicalGraphWork);maxConnector=Math.Max(maxConnector,w.RouteCounters.ConnectorWork);}}finally{root.Release();}
                    string mark=(view.Tick/30)+":"+u.NavigationOutcome+":"+nav.IsPending(follower);if(mark!=lastMark&&trace.Count<96){lastMark=mark;nav.Crowd.TryGet(follower,out var actor);trace.Add(new{view.Tick,u.Position,u.Moving,u.NavigationOutcome,actor.Goal,pending=nav.IsPending(follower),request=nav.PendingRequestFor(follower)?.Request,leaderPosition=l.Position,leaderHeading=l.HullHeading,leaderMoving=l.Moving,leaderOutcome=l.NavigationOutcome,leaderPending=nav.IsPending(leader),leaderRequest=nav.PendingRequestFor(leader)?.Request,distance,maxGraph,maxConnector});}
                    Thread.Sleep(1);
                }
                var capture=runtime.RequestCaptureBytes();double end=watch.Elapsed.TotalSeconds+10;while(!capture.IsCompleted&&watch.Elapsed.TotalSeconds<end){Service();Thread.Sleep(1);}Assert.True(capture.IsCompleted&&!capture.IsFaulted,capture.Exception?.ToString());var bytes=capture.Result;File.WriteAllBytes(Path.Combine(output,"current.bin"),bytes);Release();
                PlayableRuntime restored=null;PlayableAuthorityTick a=null;Exception error=null;
                try{restored=PlayableRuntime.RestoreBytesForTests(bytes,config,ai,r=>{try{a=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(r);exact=bytes.SequenceEqual(r.CaptureBeforeStartForTests());}catch(Exception e){error=e;}finally{r.RequestStop();}});Assert.True(SpinWait.SpinUntil(()=>restored.IsStopped,3000));if(error!=null)Assert.Fail(error.ToString());}
                finally{restored?.Dispose();a?.Domain.Navigation.ReleaseCommittedHostRoot();a?.Domain.DestroyOwnedNavigation();}
                File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{source,config.Seed,generation=107,follower,leader,joined,leaderMoved,followerMoved,exact,maxGraph,maxConnector,receipts,trace,limitTicks=900,wallLimitSeconds=45,wallSeconds=watch.Elapsed.TotalSeconds},Newtonsoft.Json.Formatting.Indented));
                if(!withdrawal)Assert.True(receipts.Any(r=>r.Sequence==1&&r.Status==PlayableCommandStatus.Applied));if(movingLeader)Assert.True(receipts.Any(r=>r.Sequence==2&&r.Status==PlayableCommandStatus.Applied));else Assert.False(leaderMoved);Assert.True(exact);Assert.True(joined,withdrawal?"Actual original leader must physically complete its full home Move around the repair tower":movingLeader?"Actual Follow must physically join the moved original leader around the repair tower":"Actual Follow must physically join the stationary original leader around the repair tower");
            }finally{
                try{Release();if(budget!=null){File.WriteAllText(Path.Combine(output,"cleanup.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{runtime.IsStopped,workerStopped=worker==null||worker.IsStopped,budget.PhysicalBytes,budget.HeldBytes,budget.ActiveScopes}));Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}}
                finally{if(logDirectory!=null)TestMatchLogSession.Shutdown();}
                if(logDirectory!=null){var summaries=Directory.GetFiles(logDirectory,"summary.json",SearchOption.AllDirectories);Assert.GreaterOrEqual(summaries.Length,1);foreach(var file in summaries){var s=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));Assert.AreEqual("complete",(string)s["Status"]);Assert.Zero((long)s["Dropped"]);Assert.Greater((long)s["Written"],0);}foreach(var file in Directory.GetFiles(logDirectory,"manifest.json",SearchOption.AllDirectories)){var m=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));Assert.AreEqual(107,(long)m["Generation"]);Assert.AreEqual(config.Seed,(int)m["Seed"]);Assert.AreEqual(config.MapIdentity,(string)m["Map"]);Assert.AreEqual(ai.Hash,(string)m["Ai"]["Hash"]);}Assert.Zero(new FileInfo(Path.Combine(logDirectory,"errors.jsonl")).Length);var end=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(logDirectory,"session-end.json")));Assert.False((bool)end["RecorderFailure"]);File.WriteAllText(Path.Combine(output,"logging-verification.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{source,logDirectory,matches=summaries.Length,complete=true,dropped=0,errors=0}));}
            }
        }
    }
}
