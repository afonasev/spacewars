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
    public sealed class SingleFlatPaidPageRuntimeTests
    {
        [TestCase(false)][TestCase(true)][Timeout(90000)]
        public void ActualFlatMoveResumesPaidPagesAndArrivesWithoutReissue(bool laterPage)
        {
            // This witness targets the certified-flat scheduler, including its
            // paid lazy N1 candidate pages. The production default now selects
            // Recast; copying its terms retains the original flat test backend.
            var terms=PlayableProfile.Default.CopyData();terms.groundRouteBackend=0;
            var p=PlayableProfile.Create(terms);Assert.False(p.UsesRecastGroundRoutes);
            double radius=PlayableUnitRules.Radius(p,PlayableEntityKind.Tank);
            double spacing=Math.Max(p.Navigation.ArrivalSlotSpacing,4*radius+2*p.Navigation.ArrivalTolerance+2*p.Navigation.LocalGridCell);
            var obstacles=new List<NavObstacle>();
            if(laterPage){
                // Native spiral centers stay free at radius0, but clearance rejects
                // the inner grid. Only the late outer top corners fit a Tank.
                for(int k=-8;k<=8;k++){
                    double x=k*spacing+.5,top=7.5*spacing;
                    bool pocket=Math.Abs(x-28)<4.5||Math.Abs(x+28)<4.5;double bottom=pocket?-23.5:-31.9;
                    if(k==0){obstacles.Add(new NavObstacle(x,-31.9,x+.01,-1));obstacles.Add(new NavObstacle(x,1,x+.01,top));}
                    else obstacles.Add(new NavObstacle(x,bottom,x+.01,top));
                }
                obstacles.Add(new NavObstacle(-31.9,.5,-1,.51));obstacles.Add(new NavObstacle(1,.5,31.9,.51));
            }
            var from=new NavPoint(7.5*spacing+.5,30.5);var goal=laterPage?new NavPoint(0,0):new NavPoint(-22,30);
            var sites=new[]{new TerritorySite(1,PlayableBuildingKind.Headquarters,new NavPoint(-28,-28),Array.Empty<TerritorySlot>()),new TerritorySite(2,PlayableBuildingKind.Headquarters,new NavPoint(28,-28),Array.Empty<TerritorySlot>())};
            var starts=new[]{new OfflineStart("west",1,sites[0].Position,new NavPoint(-25,-25),pin:1),new OfflineStart("east",2,sites[1].Position,new NavPoint(25,-25),pin:2)};
            var config=new OfflineMatchConfiguration(p,"single-flat-paidpage-runtime-v1","single-flat-pages","flat-explicit-lines",2,
                new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("enemy-1",2,2,OfflineControl.Human)},starts,sites,obstacles.ToArray(),new double[,]{{0,0},{0,0}},
                spectators:new[]{PlayableRuntime.LocalSpectatorId},scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,from),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(0,0))});
            var output=Path.GetFullPath(Path.Combine(".local/ai-position-ready","single-flat-pages-"+laterPage+"-"+(Environment.GetEnvironmentVariable("AI_POSITION_SOURCE_SHA")??"runner-bound")));Directory.CreateDirectory(output);
            PlayableRuntime runtime=null;var host=new UnityHostRouteService();var watch=Stopwatch.StartNew();
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;PlayableAuthorityTick authority=null;NavigationSession nav=null;NavigationRetentionBudget budget=null;
            var receipts=new List<PlayableCommandReceipt>();var trace=new List<object>();long originalRequest=0,firstMotion=-1,arrival=-1;bool crossed=false,epochResumed=false,moved=false,arrived=false,exact=false;long maxCandidates=0,maxEpoch=-1;string lastTrace=null;
            void Service(){host.Service(runtime,p.NavigationRequestsPerFrame);receipts.AddRange(runtime.DrainReceipts());Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(host.LastProjectionFailure);}
            void Observe(){var root=nav.PinCommittedHostRoot();try{if(root==null)return;foreach(var pair in root.ArrivalWork){var work=(NavigationArrivalWork)pair.Value;
                maxCandidates=Math.Max(maxCandidates,work.CandidateWorkConsumed);maxEpoch=Math.Max(maxEpoch,work.LogicalEpoch);crossed|=work.CandidateWorkConsumed>=256;epochResumed|=work.LogicalEpoch>0&&work.CandidateWorkConsumed>=256;
                string mark=work.WorkPhase+":"+work.LogicalEpoch+":"+(work.CandidateWorkConsumed/64)+":"+root.LastServedArrival+":"+root.ArrivalRoundEnd;
                if(mark!=lastTrace&&trace.Count<64){lastTrace=mark;trace.Add(new{tick=runtime.Latest.Tick,work.WorkPhase,work.LogicalEpoch,work.CandidateWorkConsumed,work.OccupancyWorkConsumed,work.AllocationQuotaRemaining,work.RouteCounters,root.LastServedArrival,root.ArrivalRoundEnd,root.Progress});}
            }}finally{root?.Release();}}
            CertifiedRouteWorker worker=null;bool released=false;string logDirectory=null;
            void Release(){if(released)return;worker=(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",flags).GetValue(host)??worker;runtime?.RequestStop();host.Dispose();runtime?.Dispose();Assert.True(SpinWait.SpinUntil(()=>(runtime==null||runtime.IsStopped)&&(worker==null||worker.IsStopped),5000));nav?.ReleaseCommittedHostRoot();authority?.Domain.DestroyOwnedNavigation();released=true;}
            Assert.IsNull(TestMatchLogSession.CurrentDirectory,"Preserve any unrelated active recording session");
            TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));logDirectory=TestMatchLogSession.CurrentDirectory;
            try{
                Assert.IsNotEmpty(logDirectory);Assert.NotNull(PlayableRuntime.JournalFactory);Assert.True(Path.GetFullPath(logDirectory).StartsWith(output+Path.DirectorySeparatorChar,StringComparison.Ordinal));
                File.WriteAllText(Path.Combine(output,"source-binding.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{source=Environment.GetEnvironmentVariable("AI_POSITION_SOURCE_SHA"),config.SourceIdentity,config.MapIdentity,config.Seed,p.ProfileId,p.Revision,p.UsesRecastGroundRoutes,logDirectory,editorBuildIdentity="Preserve session provenance; exact QA revision is bound by plan.json and this source record."}));
                runtime=new PlayableRuntime(config,107,startPaused:true,aiProfile:AiProfile.Initial);authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(runtime);nav=authority.Domain.Navigation;budget=nav.PhysicalHostBudget;
                while(!(runtime.IsPrepared&&runtime.Latest.Paused)&&watch.Elapsed.TotalSeconds<10){Service();Thread.Sleep(1);}Assert.True(runtime.IsPrepared&&runtime.Latest.Paused);
                Assert.AreEqual(4,p.NavigationRequestsPerFrame,"Same ordinary host grant as original repair");var initial=runtime.ParticipantView("player-1");int actor=initial.Entities.Single(u=>u.Position.Equals(from)).Id;
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);runtime.RequestPause(false);
                var command=new PlayableCommand(107,1,"player-1",PlayableCommandKind.Move,new[]{actor},goal).AsHuman();var submitted=runtime.TrySubmit(command);Assert.True(submitted.Accepted,submitted.Status.ToString());
                while(runtime.Latest.Tick<900&&watch.Elapsed.TotalSeconds<45&&!arrived){
                    Service();Observe();var current=runtime.ParticipantView("player-1").Entities.Single(u=>u.Id==actor);var request=nav.PendingRequestFor(actor);
                    if(request!=null){if(originalRequest==0)originalRequest=request.Request;else Assert.AreEqual(originalRequest,request.Request,"One original command/request, no reissue");}
                    if(!moved&&AiPositionDevelopmentPlanner.Distance(current.Position,from)>radius){moved=true;firstMotion=runtime.Latest.Tick;}
                    if(current.NavigationOutcome==NavigationOutcome.Arrived&&moved){arrived=true;arrival=runtime.Latest.Tick;}
                    Thread.Sleep(1);
                }
                var capture=runtime.RequestCaptureBytes();double captureLimit=watch.Elapsed.TotalSeconds+10;while(!capture.IsCompleted&&watch.Elapsed.TotalSeconds<captureLimit){Service();Observe();Thread.Sleep(1);}Assert.True(capture.IsCompleted&&!capture.IsFaulted,capture.Exception?.ToString());var bytes=capture.Result;File.WriteAllBytes(Path.Combine(output,"current.bin"),bytes);
                File.WriteAllText(Path.Combine(output,"progress.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{laterPage,source=Environment.GetEnvironmentVariable("AI_POSITION_SOURCE_SHA"),p.ProfileId,p.Revision,p.Navigation.GridCell,p.NavigationRequestsPerFrame,seed=2,generation=107,actor,from,goal,originalRequest,moved,arrived,firstMotion,arrival,crossed,epochResumed,maxCandidates,maxEpoch,limitTicks=900,wallLimitSeconds=45,seconds=watch.Elapsed.TotalSeconds,receipts,trace},Newtonsoft.Json.Formatting.Indented));
                Release();
                PlayableRuntime restored=null;PlayableAuthorityTick restoredAuthority=null;Exception error=null;
                try{restored=PlayableRuntime.RestoreBytesForTests(bytes,config,AiProfile.Initial,r=>{try{restoredAuthority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(r);exact=bytes.SequenceEqual(r.CaptureBeforeStartForTests());}catch(Exception e){error=e;}finally{r.RequestStop();}});Assert.True(SpinWait.SpinUntil(()=>restored.IsStopped,3000));if(error!=null)Assert.Fail(error.ToString());}
                finally{restored?.Dispose();if(restoredAuthority!=null){restoredAuthority.Domain.Navigation.ReleaseCommittedHostRoot();restoredAuthority.Domain.DestroyOwnedNavigation();}}
                Assert.True(receipts.Any(r=>r.Sequence==1&&r.Status==PlayableCommandStatus.Applied));Assert.True(moved);Assert.True(arrived);Assert.True(exact);
                if(laterPage){Assert.True(crossed&&epochResumed,"Later legal page must resume naturally after the initial256 paid grant; no manual epoch/Pump or reissue");Assert.Greater(maxCandidates,256);}
                File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{result="PASS",laterPage,moved,arrived,firstMotion,arrival,crossed,epochResumed,maxCandidates,maxEpoch,exact,manualSteps=0,manualPump=0,manualEpoch=0,receipts},Newtonsoft.Json.Formatting.Indented));
            }finally{
                try{Release();if(budget!=null){File.WriteAllText(Path.Combine(output,"cleanup.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{runtime.IsStopped,workerStopped=worker==null||worker.IsStopped,budget.PhysicalBytes,budget.HeldBytes,budget.ActiveScopes}));Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}}
                finally{TestMatchLogSession.Shutdown();}
                if(logDirectory!=null){
                    var summaries=Directory.GetFiles(logDirectory,"summary.json",SearchOption.AllDirectories);Assert.GreaterOrEqual(summaries.Length,1,"Live runtime must have a completed journal even when its gameplay assertion fails");
                    foreach(var file in summaries){var summary=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));Assert.AreEqual("complete",(string)summary["Status"]);Assert.Zero((long)summary["Dropped"]);Assert.Greater((long)summary["Written"],0);}
                    foreach(var file in Directory.GetFiles(logDirectory,"manifest.json",SearchOption.AllDirectories)){var manifest=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));Assert.AreEqual(107,(int)manifest["Generation"]);Assert.AreEqual(2,(int)manifest["Seed"]);Assert.AreEqual(config.MapIdentity,(string)manifest["Map"]);Assert.AreEqual(AiProfile.Initial.Hash,(string)manifest["Ai"]["Hash"]);}
                    Assert.AreEqual(0,new FileInfo(Path.Combine(logDirectory,"errors.jsonl")).Length,"No hidden recorder or runtime errors");Assert.True(File.Exists(Path.Combine(logDirectory,"session-end.json")));
                    File.WriteAllText(Path.Combine(output,"logging-verification.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{logDirectory,matches=summaries.Length,complete=true,dropped=0,errors=0,source=Environment.GetEnvironmentVariable("AI_POSITION_SOURCE_SHA")}));
                }
            }
        }
    }
}
