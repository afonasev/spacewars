using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
namespace Spacewars.Tests.PlayMode
{
    public sealed class CrossingsHydroworksThreadedTests
    {
        [UnityTest,Timeout(120000)] public IEnumerator CenterGroupArrivesWhileNativeAiLeavesHome()
        {
            const int seed=7;
            var p=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),
                new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));
            var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=seed};setup.InitializeParticipants();
            Assert.IsNull(setup.AddParticipant(false,0,4));Assert.IsNull(setup.AddParticipant(false,0,4));
            Assert.IsNull(setup.Validate(p,true,true));
            var basis=NativeLobbyMatch.Create(p,setup);
            var origins=new[]{new NavPoint(-18,-3),new NavPoint(-18,0),new NavPoint(-18,3)};
            var scenario=origins.Select((v,i)=>new OfflineScenarioUnit(1,i==0?PlayableEntityKind.Explorer:i==1?PlayableEntityKind.Tank:PlayableEntityKind.Shkval,v)).ToArray();
            var config=new OfflineMatchConfiguration(p,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,seed,basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),new double[4,4],scenario:scenario,terrain:p.AuthoredMap.Id);
            using(var runtime=new PlayableRuntime(config,81))using(var routes=new UnityHostRouteService())
            {
                var group=runtime.ParticipantView("player-1").Entities.Where(e=>e.Owner==PlayableOwner.Player&&origins.Contains(e.Position)).Select(e=>e.Id).ToArray();
                Assert.AreEqual(3,group.Length);
                var aiStarts=config.Roster.Where(r=>r.Control==OfflineControl.Ai).ToDictionary(r=>r.Id,r=>runtime.ParticipantView(r.Id).Entities.Single(e=>e.Owner==runtime.ParticipantView(r.Id).Owner).Position);
                var goal=new NavPoint(18,0);bool moved=false,arrived=false,aiMoved=false;long? firstAllArrivedTick=null,firstAiMotionTick=null;PlayableEntitySnapshot[] arrivalWitness=null;float deadline=Time.realtimeSinceStartup+110;
                // Bounded observer: retain immutable published snapshots on state transitions;
                // no synchronous logging or authority mutation inside the observed match loop.
                var events=new List<object>(128);int dropped=0;
                var states=new Dictionary<int,(int health,bool moving,NavigationOutcome outcome,long stamp,long active,long pending,long complete,bool east)>();
                object UnitState(int id,PlayableEntitySnapshot e)=>new{id,alive=e!=null,kind=e?.Kind,position=e?.Position,health=e?.Health,moving=e?.Moving,outcome=e?.NavigationOutcome,location=e?.Location,currentOrder=e?.CurrentOrder,completion=e?.Completion,queue=e?.Queue,orderStamp=e?.OrderStamp};
                void Observe(PlayableSnapshot own)
                {
                    foreach(int id in group){
                        var e=own.Entities.FirstOrDefault(v=>v.Id==id);
                        var key=(e?.Health??-1,e?.Moving??false,e?.NavigationOutcome??NavigationOutcome.Idle,e?.OrderStamp?.Revision??0,e?.Queue?.Active?.IssuanceId??0,e?.Queue?.Pending?.IssuanceId??0,e?.Queue?.Completion?.CompletionTick??-1,(e?.Position.X??-18)>0);
                        if(states.TryGetValue(id,out var old)&&old.Equals(key))continue;states[id]=key;
                        if(events.Count<128)events.Add(new{own.Tick,unit=UnitState(id,e),navigationPending=own.Metrics.NavigationPending,routeProof=own.RouteProofs.FirstOrDefault(v=>v.UnitId==id),hostPumps=routes.CertifiedPublication?.Pumps});else dropped++;
                    }
                }
                Observe(runtime.ParticipantView("player-1"));
                Assert.AreEqual(PlayableCommandStatus.Accepted,runtime.TrySubmit(new PlayableCommand(81,1,"player-1",PlayableCommandKind.Move,group,goal)).Status);
                while(runtime.Latest.Tick<1800&&Time.realtimeSinceStartup<deadline&&!runtime.IsStopped)
                {
                    routes.Service(runtime,p.NavigationRequestsPerFrame);
                    var own=runtime.ParticipantView("player-1");var units=own.Entities.Where(e=>group.Contains(e.Id)).ToArray();
                    Observe(own);
                    moved|=units.Any(e=>!origins.Contains(e.Position));
                    // Arrival is a witnessed event, independent of when AI starts moving.
                    arrived|=units.Length==3&&units.All(e=>e.NavigationOutcome==NavigationOutcome.Arrived&&e.Position.X>0);
                    if(arrived&&!firstAllArrivedTick.HasValue){firstAllArrivedTick=own.Tick;arrivalWitness=units;}
                    foreach(var ai in aiStarts){var view=runtime.ParticipantView(ai.Key);bool observed=view.Entities.Any(e=>e.Owner==view.Owner&&e.Kind==PlayableEntityKind.Explorer&&!e.Position.Equals(ai.Value)&&e.OrderStamp?.Origin==PlayableOrderOrigin.Ai);aiMoved|=observed;if(observed&&!firstAiMotionTick.HasValue)firstAiMotionTick=view.Tick;}
                    Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);
                    if(moved&&arrived&&aiMoved)break;yield return null;
                }
                TestContext.WriteLine($"CROSSINGS_WITNESS seed={seed} tick={runtime.Latest.Tick} motion={moved} arrived={arrived} nativeAiMotion={aiMoved} threadedPumps={routes.CertifiedPublication?.Pumps} failure={runtime.Latest.Failure}");
                var terminal=runtime.OfflineFrame;var terminalOwn=terminal.Views["player-1"];
                var terminalStates=group.Select(id=>UnitState(id,terminalOwn.Entities.FirstOrDefault(e=>e.Id==id))).ToArray();
                var terminalHost=routes.CertifiedPublication;
                // Capture is optional diagnostics after the observed result, within the
                // original wall deadline. No pause, speed, roster or horizon change.
                byte[] captured=null;string captureStatus="NotRun: original wall budget exhausted";
                if(Time.realtimeSinceStartup<deadline&&!runtime.IsStopped){
                    var capture=runtime.RequestCaptureBytes();
                    while(!capture.IsCompleted&&Time.realtimeSinceStartup<deadline&&!runtime.IsStopped){routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}
                    if(capture.IsCompleted&&!capture.IsFaulted&&!capture.IsCanceled){captured=capture.Result;captureStatus="Captured current format";}
                    else captureStatus=capture.IsFaulted?capture.Exception.ToString():"NotRun: capture did not finish inside original wall budget";
                }
                var output=Environment.GetEnvironmentVariable("CROSSINGS_DIAGNOSTIC_OUTPUT")??Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/crossings-layout/diagnostic"));Directory.CreateDirectory(output);
                if(captured!=null)File.WriteAllBytes(Path.Combine(output,"terminal-current.bin"),captured);
                var report=new{producer=Environment.GetEnvironmentVariable("CROSSINGS_PRODUCER"),seed,horizon=1800,wallSeconds=110,nunitTimeoutMs=120000,terminal.Tick,moved,arrived,aiMoved,firstAllArrivedTick,firstAiMotionTick,group,origins,goal,terminalStates,events,dropped,receipts=terminal.Receipts,routeProofs=terminalOwn.RouteProofs.Where(v=>group.Contains(v.UnitId)).ToArray(),host=terminalHost,runtimeFailure=terminalOwn.Failure,projectionFailure=routes.LastProjectionFailure,captureStatus,profileResource=Resources.Load<TextAsset>("PlayableProfile").text,mapResource=Resources.Load<TextAsset>("ThreeCrossingsProfile").text,config.Starts,config.Assignments};
                File.WriteAllText(Path.Combine(output,"observer.json"),Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
                TestContext.WriteLine("CROSSINGS_OBSERVER "+output+" "+captureStatus);
                Assert.True(moved);Assert.True(arrived,"Mixed group must arrive on the opposite bank through center");Assert.True(aiMoved,"Native AI scout must move after an AI command");
                Assert.NotNull(arrivalWitness);Assert.AreEqual(3,arrivalWitness.Length);
                foreach(var unit in arrivalWitness){
                    var receipt=unit.Completion;Assert.NotNull(receipt,"Arrival must retain its exact spatial completion");
                    Assert.AreEqual(1,receipt.GroupId);Assert.AreEqual(1,receipt.Order.Sequence);Assert.AreEqual(1,receipt.Order.Revision);
                    Assert.AreEqual(81,receipt.Order.Generation);Assert.AreEqual(unit.Id,receipt.Order.UnitId);
                    Assert.AreEqual(PlayableOrderOrigin.Human,receipt.Order.Origin);Assert.AreEqual(PlayableCommandKind.Move,receipt.Order.Kind);
                    Assert.AreEqual(goal,receipt.OriginalTarget);Assert.AreEqual("bank/east",receipt.OriginalLocation.SurfaceId);Assert.AreEqual("bank/east",receipt.AssignedLocation.SurfaceId);
                    Assert.AreEqual(unit.OrderStamp.Revision,receipt.Order.Revision);Assert.LessOrEqual(receipt.CompletionTick,firstAllArrivedTick.Value);
                }
                Assert.NotNull(routes.CertifiedPublication);Assert.Greater(routes.CertifiedPublication.Pumps,0);
                runtime.RequestStop();Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,3000));
            }
        }
    }
}
