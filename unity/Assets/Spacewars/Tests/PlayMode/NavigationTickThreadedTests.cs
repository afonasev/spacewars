using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.PlayMode
{
    public sealed class NavigationTickThreadedTests
    {
        sealed class AllFields:Newtonsoft.Json.Serialization.DefaultContractResolver
        {
            protected override IList<Newtonsoft.Json.Serialization.JsonProperty> CreateProperties(Type type,Newtonsoft.Json.MemberSerialization serialization)
            {return type.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Select(f=>{var p=base.CreateProperty(f,serialization);p.Readable=true;p.Writable=false;if(f.Name.StartsWith("<"))p.PropertyName=f.Name.Substring(1,f.Name.IndexOf(">")-1);return p;}).ToList();}
        }
        [Test,Timeout(65000)] public void OrdinaryCommandMovesAndCapturesWithTemporalTrace()=>Run(false);
        [Test,Timeout(210000)] public void FiftyParticipantOrdinaryNarrowing()=>Run(true);
        private void Run(bool narrow)
        {
            int count=narrow?50:8;
            var data=PlayableProfile.Default.CopyData();IPlayableTerrain terrain=narrow?(IPlayableTerrain)new ThreeCrossingsMap(new ThreeCrossingsProfileData()):new AiTestMap();var profile=PlayableProfile.Create(data,terrain);
            var roster=new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,2,OfflineControl.Human)};
            var basis=narrow?((ThreeCrossingsMap)terrain).Configuration(profile,7,roster):((AiTestMap)terrain).Configuration(profile,7,roster);
            OfflineScenarioUnit[] scenario;
            if(!narrow)scenario=Enumerable.Range(0,count).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-38+i%4*2.5,-38+i/4*2.5))).ToArray();
            else {
                var placed=new List<OfflineScenarioUnit>();double radius=profile.TankCollisionRadius,spacing=2*radius+.25;
                double Distance(NavPoint x,NavPoint y)=>Math.Sqrt(Math.Pow(x.X-y.X,2)+Math.Pow(x.Z-y.Z,2));
                var home=basis.Starts[basis.Assignments[0]].ExplorerAnchor;
                for(int ring=1;ring<=60&&placed.Count<count;ring++)for(int z=-ring;z<=ring&&placed.Count<count;z++)for(int x=-ring;x<=ring&&placed.Count<count;x++){
                    if(Math.Max(Math.Abs(x),Math.Abs(z))!=ring)continue;var point=new NavPoint(home.X+x*spacing,home.Z+z*spacing);
                    if(!terrain.SupportsFootprint(point,radius)||basis.Starts.Any(t=>Distance(point,t.ExplorerAnchor)<radius+profile.ExplorerCollisionRadius)||placed.Any(t=>Distance(point,t.Position)<2*radius)||basis.Sites.Any(t=>Distance(point,t.Position)<TerritoryRules.Radius(profile,t.Kind)+radius||t.Slots.Any(slot=>Distance(point,slot.Position)<Math.Max(profile.ScienceFootprintRadius,Math.Max(profile.FactoryFootprintRadius,profile.RefineryFootprintRadius))+radius)))continue;
                    placed.Add(new OfflineScenarioUnit(1,PlayableEntityKind.Tank,point));
                }
                Assert.AreEqual(count,placed.Count);scenario=placed.ToArray();
            }
            var costs=new double[basis.Starts.Count,basis.Starts.Count];for(int a=0;a<basis.Starts.Count;a++)for(int b=0;b<basis.Starts.Count;b++)costs[a,b]=basis.RouteCost(a,b);
            var config=new OfflineMatchConfiguration(profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,7,basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs,scenario:scenario,terrain:profile.AuthoredMap.Id);
            // JournalFactory is bound by the constructor, so recording must
            // start before the runtime (batch Editor has no automatic session).
            TestMatchLogSession.StartDefault(Path.Combine(Environment.GetEnvironmentVariable("NAV_TICK_EVIDENCE"),narrow?"TestLogs50":"TestLogs8"));
            var runtime=new PlayableRuntime(config,71,startPaused:true);var host=new UnityHostRouteService();
            var domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
            var trace=new List<object>();int focusId=0;
            Action observe=()=>{if(trace.Count<8192)trace.Add(new{domain.Tick,Actors=domain.Navigation.Crowd.Units.Select(u=>new{u.Id,u.Position,u.Heading,u.Moving,u.Outcome,u.InstalledRequest,u.InstalledOrder,u.Goal}).ToArray()});};
            object executor=null;
            long Batches()=>executor==null?0:(long)executor.GetType().GetProperty("CompletedBatches",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(executor);
            bool Idle()=>executor==null||(bool)executor.GetType().GetProperty("IsIdle",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(executor);var clock=Stopwatch.StartNew();bool applied=false;Exception primaryFailure=null;int captures=0;
            var output=Environment.GetEnvironmentVariable("NAV_TICK_EVIDENCE");string stem=narrow?"ordinary50":"ordinary8";
            void Service(){host.Service(runtime,profile.NavigationRequestsPerFrame);foreach(var receipt in runtime.DrainReceipts())if(receipt.Sequence==1){Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status);applied=true;}Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(host.CertifiedPublication?.Failure);}
            void Wait(Func<bool> condition,string phase){while(!condition()&&clock.Elapsed.TotalSeconds<(narrow?180:50)){Service();Thread.Sleep(1);}Assert.True(condition(),phase);}
            void SaveCapture(byte[] bytes,string phase){
                if(string.IsNullOrEmpty(output))return;Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,stem+"-"+phase+".bin"),bytes);
                File.WriteAllText(Path.Combine(output,stem+"-"+phase+".producer.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{Candidate=Environment.GetEnvironmentVariable("NAV_TICK_CANDIDATE"),Phase=phase,Seed=7,Generation=71,Map=profile.AuthoredMap.Id,profile.ProfileId,profile.Revision,Tick=runtime.Latest.Tick,PausedACK=runtime.Latest.Paused,Wall=clock.Elapsed.TotalSeconds,Scenario=scenario,Meaning="Current capture for causal inspection; post-failure pause does not extend the measured arrival horizon"},Newtonsoft.Json.Formatting.Indented));
            }
            void RoundTrip(){
                var capture=runtime.RequestCaptureBytes();Wait(()=>capture.IsCompleted,"current-format capture");
                var bytes=capture.GetAwaiter().GetResult();SaveCapture(bytes,"capture"+(++captures));var restored=PlayableAuthorityTick.RestoreBytes(bytes,config);
                try{
                    // Authority restore owns the world/AI/navigation sections;
                    // the outer Runtime separately owns its paused receipt queue.
                    // Recompose that untouched transport section for exact envelope equality.
                    using(var saved=PlayableWorldState.Decode(bytes))using(var roundTrip=PlayableWorldState.Decode(restored.CaptureBytes())){
                        roundTrip.RuntimeTransport=saved.RuntimeTransport;CollectionAssert.AreEqual(bytes,roundTrip.Encode());
                    }
                }finally{restored.Stop();restored.Domain.DestroyOwnedNavigation();}
            }
            try{
                Wait(()=>runtime.IsPrepared&&runtime.Latest.Paused,"prepared paused ACK");
                executor=typeof(PlayableRuntime).GetField("motionExecutor",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);Assert.NotNull(executor);
                var initial=runtime.ParticipantView("player-1");var tanks=initial.Entities.Where(e=>e.Owner==initial.Owner&&e.Kind==PlayableEntityKind.Tank).ToArray();Assert.AreEqual(count,tanks.Length);
                focusId=tanks[0].Id;domain.CombatStepForQa+=observe;
                var goal=narrow?new NavPoint(basis.Starts[basis.Assignments[0]].ExplorerAnchor.X>0?-30:30,38):new NavPoint(-20,-35);runtime.RequestPause(false);
                Assert.True(runtime.TrySubmit(new PlayableCommand(71,1,"player-1",PlayableCommandKind.Move,tanks.Select(e=>e.Id).ToArray(),goal)).Accepted);
                bool Arrived(){var view=runtime.ParticipantView("player-1");return tanks.All(t=>view.Entities.Any(e=>e.Id==t.Id&&(narrow?e.NavigationOutcome==NavigationOutcome.Arrived:Math.Pow(e.Position.X-goal.X,2)+Math.Pow(e.Position.Z-goal.Z,2)<36)));}
                Wait(()=>applied&&runtime.ParticipantView("player-1").Entities.Any(e=>tanks.Any(t=>t.Id==e.Id&&!t.Position.Equals(e.Position))),"command to first actual motion");
                runtime.RequestPause(true);Wait(()=>runtime.Latest.Paused,"active maneuver pause boundary");
                
                long pausedTick=runtime.Latest.Tick;RoundTrip();Assert.AreEqual(pausedTick,runtime.Latest.Tick,"capture does not advance pause");
                runtime.RequestPause(false);
                Wait(()=>applied&&Batches()>0&&Arrived(),"ordinary group arrival after real parallel movement");
                Wait(()=>runtime.ParticipantView("player-1").Entities.Where(e=>tanks.Any(t=>t.Id==e.Id)).All(e=>e.NavigationOutcome==NavigationOutcome.Arrived),"all eight assigned-slot arrivals");
                Assert.Greater(host.CertifiedPublication.Pumps,0);Assert.Zero(runtime.Latest.Metrics.Errors);
                runtime.RequestPause(true);Wait(()=>runtime.Latest.Paused,"pause boundary");
                RoundTrip();
                Assert.True(Idle());TestContext.WriteLine("ORDINARY_MOTION_EXECUTOR seed=7 actors=10 moved=8 tick="+runtime.Latest.Tick+" batches="+Batches()+" pumps="+host.CertifiedPublication.Pumps+" seconds="+clock.Elapsed.TotalSeconds);
            }catch(Exception error){
                primaryFailure=error;
                // Preserve a current, attributed causal input before destroying
                // the host. The original assertion/horizon has already failed;
                // these control-only waits provide no extra arrival credit.
                try{
                    var control=Stopwatch.StartNew();runtime.RequestPause(true);
                    while(!runtime.Latest.Paused&&!runtime.IsStopped&&control.ElapsedMilliseconds<10000){host.Service(runtime,profile.NavigationRequestsPerFrame);Thread.Sleep(1);}
                    if(!runtime.Latest.Paused)throw new InvalidOperationException("Failure snapshot has no pause ACK");
                    var saved=runtime.RequestCaptureBytes();while(!saved.IsCompleted&&control.ElapsedMilliseconds<10000){host.Service(runtime,profile.NavigationRequestsPerFrame);Thread.Sleep(1);}
                    if(!saved.IsCompleted)throw new InvalidOperationException("Failure snapshot control budget exhausted");
                    SaveCapture(saved.GetAwaiter().GetResult(),"after-failure-pause");
                }catch(Exception snapshotError){TestContext.WriteLine("FAILURE_CAPTURE_NOTRUN="+snapshotError);}
                throw;
            }finally{
                runtime.RequestStop();host.Dispose();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,10000));Assert.True(executor==null||Idle());
                domain.CombatStepForQa-=observe;TestMatchLogSession.Shutdown();
                TestContext.WriteLine("MOTION_PROOF_CACHE_HITS="+domain.Navigation.MotionProofHits);
                if(!string.IsNullOrEmpty(output)){Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,(narrow?"ordinary50":"ordinary8")+"-temporal.json"),Newtonsoft.Json.JsonConvert.SerializeObject(trace,Newtonsoft.Json.Formatting.Indented,new Newtonsoft.Json.JsonSerializerSettings{ContractResolver=new AllFields()}));}
                try{domain.DestroyOwnedNavigation();Assert.Zero(domain.Navigation.PhysicalHostBudget.PhysicalBytes);}catch(Exception cleanup){if(primaryFailure==null)throw;TestContext.WriteLine("CLEANUP_SECONDARY="+cleanup);}
            }
        }
    }
}
