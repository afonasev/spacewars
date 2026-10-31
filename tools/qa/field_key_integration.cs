// Current-main functional witness. Historical free_endpoint_qa.cs benchmark stays unchanged.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json;
using Spacewars.Runtime;
using Spacewars.Simulation;
internal static class FreeEndpointQa
{
    internal static long TargetTicks;
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    sealed class OrderEvidence {public int Unit;public PlayableOwner Owner;public long Revision,Sequence,Action,IssuedTick,ObservedTick,ProgressTick=-1;public NavPoint Start,Target;public PlayableCommandKind Kind;public double Progress;}
    static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));
    // Read only after authority and host stop: no observers in AI decisions or live mutable state.
    static object[] OpeningStates(PlayableRuntime runtime){var scheduler=(AiAuthorityScheduler)typeof(PlayableRuntime).GetField("aiScheduler",Private).GetValue(runtime);return scheduler.Owners.Select(owner=>{
        var opening=(AiOpeningExecutor)typeof(PlayableAiOwnerLoop).GetField("openingExecutor",Private).GetValue(owner);
        return (object)new{owner.OwnerId,State=opening.Capture(),opening.Air,ScoutBudget=(int)typeof(PlayableAiOwnerLoop).GetProperty("ScoutBudget",Private).GetValue(owner),owner.Checkpoint.LastScoutTick,owner.Checkpoint.LastPolicy};}).ToArray();}
    static object Match(string output,int seed,long ticks,bool requireMotion,bool diagnose){
        Directory.CreateDirectory(output);TargetTicks=ticks;
        var profile=PlayableProfile.Create(JsonConvert.DeserializeObject<PlayableProfileData>(File.ReadAllText("unity/Assets/Spacewars/Content/Resources/PlayableProfile.json")),new CrimsonGroveMap(JsonConvert.DeserializeObject<CrimsonGroveProfileData>(File.ReadAllText("unity/Assets/Spacewars/Content/Resources/CrimsonGroveProfile.json"))));
        var setup=new NativeLobbyConfiguration{CrimsonGrove=true,HumanPresent=false,HasExplicitSeed=true,ExplicitSeed=seed};setup.InitializeParticipants();
        var clock=Stopwatch.StartNew();var orders=new Dictionary<string,OrderEvidence>();var initialMoved=new HashSet<int>();long lastTick=-1;bool timeout=false;
        using(var samples=new StreamWriter(Path.Combine(output,"samples.jsonl")))
        using(var runtime=PlayableRuntime.CreateLobbyMatch(profile,1,setup,true))
        using(var routes=new CertifiedRouteWorker(runtime.NavigationBinding,profile.NavigationRequestsPerFrame,()=>runtime.IsStopRequested,runtime.ReportRouteFailure)){
            if(runtime.Latest.Participants.Count!=8||runtime.Latest.Participants.Any(p=>p.Control!=OfflineControl.Ai))throw new Exception("Expected8AIparticipants");
            var genesis=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Overview.Entities.Where(u=>u.Kind==PlayableEntityKind.Explorer).ToDictionary(u=>u.Id,u=>u.Position);runtime.RequestPause(false);
            while(!runtime.IsStopped){var view=runtime.Latest;var frame=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);
                if(frame.Overview.Tick!=lastTick){lastTick=frame.Overview.Tick;
                    foreach(var u in frame.Overview.Entities)if(genesis.TryGetValue(u.Id,out var origin)&&Distance(u.Position,origin)>.25)initialMoved.Add(u.Id);
                    foreach(var ownerView in frame.Players)foreach(var u in ownerView.Entities.Where(u=>u.Owner==ownerView.Owner)){
                        var s=u.OrderStamp;var o=u.CurrentOrder;if(s==null||o==null||s.Origin!=PlayableOrderOrigin.Ai||s.UnitId!=u.Id||s.Owner!=u.Owner||s.Generation!=runtime.Generation||o.UnitId!=u.Id||o.Owner!=u.Owner||o.Generation!=s.Generation||o.CommandSequence!=s.Sequence||u.Departing||(s.Kind!=PlayableCommandKind.Move&&s.Kind!=PlayableCommandKind.AttackMove))continue;
                        string key=u.Id+":"+s.Revision+":"+s.Sequence;
                        if(!orders.TryGetValue(key,out var e)){e=new OrderEvidence{Unit=u.Id,Owner=u.Owner,Revision=s.Revision,Sequence=s.Sequence,Action=s.ActionId,IssuedTick=s.Tick,ObservedTick=lastTick,Start=u.Position,Target=o.Destination,Kind=s.Kind};orders.Add(key,e);}
                        double progress=Distance(e.Start,e.Target)-Distance(u.Position,e.Target);e.Progress=Math.Max(e.Progress,progress);if(e.ProgressTick<0&&lastTick>e.ObservedTick&&progress>.25&&Distance(u.Position,e.Start)>.25)e.ProgressTick=lastTick;
                    }
                    if(lastTick%900==0){samples.WriteLine(JsonConvert.SerializeObject(new{tick=lastTick,initialExplorersMoved=initialMoved.Count,aiOrders=orders.Count,progressedOrders=orders.Values.Count(e=>e.ProgressTick>=0),view.Failure}));samples.Flush();}
                }
                if(view.Failure!=null)break;if(clock.Elapsed.TotalSeconds>900){timeout=true;break;}Thread.Sleep(10);
            }
            runtime.RequestStop();if(!SpinWait.SpinUntil(()=>runtime.IsStopped,30000))throw new Exception("Authority cleanup timeout");var final=runtime.Latest;var worker=routes.Publication;routes.Dispose();if(!SpinWait.SpinUntil(()=>routes.IsStopped,30000))throw new Exception("Worker cleanup timeout");
            var states=OpeningStates(runtime);var result=new{seed,targetTicks=ticks,tick=final.Tick,requireMotion,diagnose,initialExplorersMoved=initialMoved.Count,legacyWarmMovementGuardPassed=initialMoved.Count==7,orders=orders.Values.ToArray(),openingStates=states,final.Failure,worker,timeout,wallSeconds=clock.Elapsed.TotalSeconds};
            File.WriteAllText(Path.Combine(output,"result.json"),JsonConvert.SerializeObject(result,Formatting.Indented));Console.WriteLine("INTEGRATION_RESULT "+JsonConvert.SerializeObject(new{tick=final.Tick,initialExplorersMoved=initialMoved.Count,orders=orders.Count,progressedOrders=orders.Values.Count(e=>e.ProgressTick>=0),final.Failure,WorkerFailure=worker.Failure,timeout}));
            if(timeout||final.Failure!=null||worker.Failure!=null||worker.ProjectionFailure!=null||final.Tick!=ticks)throw new Exception("Current runtime witness failed; no unchanged replay");
            if(requireMotion&&!orders.Values.Any(e=>e.ProgressTick>=0))throw new Exception("No observed AI order with subsequent progress toward its bound target");
            return result;
        }
    }
    public static int Main(string[] args){string output=args[0];int seed=int.Parse(args[1]);if(args.Contains("--diagnose-opening")){Match(output,seed,900,false,true);return 0;}
        var warm=Match(Path.Combine(output,"warmup"),seed,900,false,false);GC.Collect();GC.WaitForPendingFinalizers();var witness=Match(Path.Combine(output,"witness"),seed,5400,true,false);File.WriteAllText(Path.Combine(output,"result.json"),JsonConvert.SerializeObject(new{warmup=warm,witness,currentFunctionalWitness=true,historicalBenchmarkUnchanged=true,performanceClaim=false},Formatting.Indented));return 0;}
}
