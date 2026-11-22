using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine;
using UnityEngine.TestTools;
namespace Spacewars.Tests.PlayMode
{
    public sealed class AiOpeningFallbackThreadedTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        [UnityTest,Timeout(150000)] public IEnumerator DeadlineKeepsPaidAirProductionAndReleasesTheActualFirstGroup()
        {
            const int seed=19092026;const long generation=141;const string owner="west-owner";
            var data=PlayableProfile.Default.CopyData();data.startingCredits=20000;var p=PlayableProfile.Create(data);var ai=AiProfile.Initial;
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-24,0),new NavPoint(-24,8),pin:1),new OfflineStart("east",2,new NavPoint(24,0),new NavPoint(24,8),pin:2)};
            var slots=new[]{new TerritorySlot(1,new NavPoint(-24,-9),0),new TerritorySlot(2,new NavPoint(-14,0),0),new TerritorySlot(3,new NavPoint(-14,-9),0),new TerritorySlot(4,new NavPoint(-24,-18),0),new TerritorySlot(5,new NavPoint(-14,-18),0)};
            var sites=new[]{new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,slots),new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,Array.Empty<TerritorySlot>()),new TerritorySite(3,PlayableBuildingKind.Mine,new NavPoint(-10,20),p)};
            var config=new OfflineMatchConfiguration(p,"opening-fallback-v1","o3-flat-two-starts","UnityHostRouteService",seed,new[]{new OfflineParticipant(owner,1,1,OfflineControl.Ai),new OfflineParticipant("human-owner",2,2,OfflineControl.Human)},starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,48},{48,0}},spectators:new[]{PlayableRuntime.LocalSpectatorId});
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/repair-native-opening-contracts/ordinary-air-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(output);
            Assert.IsNull(TestMatchLogSession.CurrentDirectory);TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));
            var runtime=new PlayableRuntime(config,generation,startPaused:true,aiProfile:ai);var routes=new UnityHostRouteService();
            var history=new List<object>();bool aborted=false,fulfilled=false,pressure=false,moved=false;long firstMotion=0;
            try
            {
                // Choose the opening precondition while paused. No orders, production,
                // receipts, routes, timing or completed milestones are injected.
                var scheduler=typeof(PlayableRuntime).GetField("aiScheduler",F).GetValue(runtime);
                var loop=((IEnumerable)scheduler.GetType().GetField("owners",F).GetValue(scheduler)).Cast<PlayableAiOwnerLoop>().Single();
                var opening=PlayableAiOpeningComposition.Initialize(seed,owner,forcedOpening:PlayableAiOpening.Air);
                var executor=new AiOpeningExecutor(opening,generation);
                typeof(PlayableAiOwnerLoop).GetField("opening",F).SetValue(loop,opening);typeof(PlayableAiOwnerLoop).GetField("openingExecutor",F).SetValue(loop,executor);
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);runtime.RequestPause(false);
                float until=Time.realtimeSinceStartup+120;var positions=new Dictionary<int,NavPoint>();
                while(runtime.Latest.Tick<5400&&!runtime.IsStopped&&runtime.Latest.Outcome==PlayableMatchOutcome.Playing&&Time.realtimeSinceStartup<until)
                {
                    routes.Service(runtime,p.NavigationRequestsPerFrame);Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);
                    var own=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Players.Single(v=>v.Owner==PlayableOwner.Player);
                    if(own.Tick>AiProfile.SecondsToTicks(ai.Value("opening.deadlineSeconds"),30)&&own.Entities.Count(u=>u.Owner==PlayableOwner.Player&&u.Kind==PlayableEntityKind.Hawk&&u.Health>0&&!u.Departing)>=4)foreach(var u in own.Entities.Where(u=>u.Owner==PlayableOwner.Player&&u.Kind==PlayableEntityKind.Hawk&&!u.Departing&&u.OrderStamp?.Origin==PlayableOrderOrigin.Ai))
                    {if(positions.TryGetValue(u.Id,out var before)&&AiOpponentSelector.Distance(before,u.Position)>p.HawkRadius){moved=true;if(firstMotion==0)firstMotion=own.Tick;}else if(!positions.ContainsKey(u.Id))positions.Add(u.Id,u.Position);}
                    yield return null;
                }
            }
            finally
            {
                runtime.RequestStop();routes.Dispose();runtime.Dispose();Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,5000));
                var authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",F).GetValue(runtime);authority.Domain.Navigation.ReleaseCommittedHostRoot();authority.Domain.DestroyOwnedNavigation();TestMatchLogSession.Shutdown();
            }
            // The scheduler may replace owner loops after background review. Read
            // immutable ordinary journal checkpoints, never a captured executor object.
            string previous=null;
            foreach(var file in Directory.GetFiles(Path.Combine(output,"TestLogs"),"events-*.jsonl",SearchOption.AllDirectories).OrderBy(x=>x))
            foreach(var line in File.ReadLines(file))
            {
                var row=Newtonsoft.Json.Linq.JObject.Parse(line);
                if((string)row["Type"]!="ai-checkpoint"||(string)row["OwnerId"]!=owner)continue;
                var opening=row["OpeningExecution"];if(opening==null)continue;
                bool actualForce=(bool)opening["InitialForceFulfilled"];
                aborted|=(string)opening["Phase"]=="Aborted";fulfilled|=actualForce;pressure|=opening["Completed"].Select(x=>(string)x).Contains("pressure");
                var armies=row["Armies"]["Armies"];
                if(!actualForce)Assert.False(armies.Any(a=>(string)a["Phase"]!="Disbanded"&&((string)a["Role"]=="Attack"||(string)a["Role"]=="AlliedSupport")),"deadline cannot launch an undersized air group");
                string key=opening.ToString(Newtonsoft.Json.Formatting.None);
                if(key!=previous&&history.Count<128){history.Add(new{Tick=(long)row["Tick"],Opening=opening,Armies=armies});previous=key;}
            }
            File.WriteAllText(Path.Combine(output,"result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{seed,generation,runtime.Latest.Tick,runtime.Latest.Outcome,aborted,fulfilled,pressure,moved,firstMotion,history},Newtonsoft.Json.Formatting.Indented));TestContext.WriteLine("AIR_FALLBACK_RESULT "+output);
            Assert.True(aborted,"frozen paid production crosses the original deadline");Assert.True(fulfilled,"actual paid first group must finish after deadline");Assert.True(pressure,"real effective army deployment must release the one-time opening restriction");Assert.True(moved,"AI-issued first army order must physically move Hawks through the threaded host");
        }
    }
}
