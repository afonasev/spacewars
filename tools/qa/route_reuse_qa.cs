using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Spacewars.Runtime;
using Spacewars.Simulation;

internal static class RouteReuseQa
{
    internal static readonly long TargetTicks=long.Parse(Environment.GetEnvironmentVariable("ROUTE_REUSE_TICKS")??"5400");
    public static int Main(string[] args)
    {
#if ROUTE_REUSE_FROZEN
        return RouteReuseFrozen.Run(args[0],int.Parse(args[1]));
#elif ROUTE_REUSE_TESTS
        return Spacewars.Tests.EditMode.ObserverGeometryTileTests.RunSelected();
#else
        string output=args[0];int seed=int.Parse(args[1]);Directory.CreateDirectory(output);
        var data=JsonConvert.DeserializeObject<PlayableProfileData>(File.ReadAllText("unity/Assets/Spacewars/Content/Resources/PlayableProfile.json"));
        var map=JsonConvert.DeserializeObject<CrimsonGroveProfileData>(File.ReadAllText("unity/Assets/Spacewars/Content/Resources/CrimsonGroveProfile.json"));
        var profile=PlayableProfile.Create(data,new CrimsonGroveMap(map));
        var setup=new NativeLobbyConfiguration{CrimsonGrove=true,HumanPresent=false,HasExplicitSeed=true,ExplicitSeed=seed};setup.InitializeParticipants();
        var clock=Stopwatch.StartNew();var cpu0=Process.GetCurrentProcess().TotalProcessorTime;
        using(var samples=new StreamWriter(Path.Combine(output,"samples.jsonl")))
        using(var runtime=PlayableRuntime.CreateLobbyMatch(profile,1,setup,true))
        using(var routes=new CertifiedRouteWorker(runtime.NavigationBinding,profile.NavigationRequestsPerFrame,()=>runtime.IsStopRequested,runtime.ReportRouteFailure))
        {
            if(runtime.Latest.Participants.Count!=8)throw new Exception("Expected eight participants");
            var genesis=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Overview.Entities.Where(u=>u.Kind==PlayableEntityKind.Explorer).ToDictionary(u=>u.Id,u=>u.Position);
            var moved=new System.Collections.Generic.HashSet<int>();long firstMotion=-1,lastSample=-1,lastObserved=-1;bool timeout=false;
            runtime.RequestPause(false);
            while(!runtime.IsStopped){
                var view=runtime.Latest;var overview=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Overview;
                if(overview.Tick!=lastObserved){lastObserved=overview.Tick;
                    foreach(var u in overview.Entities)if(genesis.TryGetValue(u.Id,out var origin)&&Math.Pow(u.Position.X-origin.X,2)+Math.Pow(u.Position.Z-origin.Z,2)>.0625){moved.Add(u.Id);if(firstMotion<0)firstMotion=overview.Tick;}}
                if(view.Tick/900!=lastSample){lastSample=view.Tick/900;samples.WriteLine(JsonConvert.SerializeObject(new{seconds=clock.Elapsed.TotalSeconds,cpuSeconds=(Process.GetCurrentProcess().TotalProcessorTime-cpu0).TotalSeconds,tick=view.Tick,units=runtime.DiagnosticGlobalUnits,buildings=runtime.DiagnosticGlobalBuildings,moving=overview.Entities.Count(u=>u.Moving),firstMotion,movedExplorers=moved.Count,view.Failure}));samples.Flush();}
                if(view.Failure!=null)break;if(clock.Elapsed.TotalSeconds>900){timeout=true;break;}Thread.Sleep(10);
            }
            runtime.RequestStop();if(!SpinWait.SpinUntil(()=>runtime.IsStopped,30000))throw new Exception("Authority cleanup timeout");
            var final=runtime.Latest;var worker=routes.Publication;
            routes.Dispose();if(!SpinWait.SpinUntil(()=>routes.IsStopped,30000))throw new Exception("Route cleanup timeout");
            var result=new{seed,map=profile.AuthoredMap.Id,profile=profile.ProfileId,profile.Revision,targetTicks=TargetTicks,tick=final.Tick,simulationSeconds=final.Tick/30d,wallSeconds=clock.Elapsed.TotalSeconds,processCpuSeconds=(Process.GetCurrentProcess().TotalProcessorTime-cpu0).TotalSeconds,units=runtime.DiagnosticGlobalUnits,buildings=runtime.DiagnosticGlobalBuildings,firstMotion,movedExplorers=moved.Count,final.Failure,timeout,worker};
            File.WriteAllText(Path.Combine(output,"result.json"),JsonConvert.SerializeObject(result,Formatting.Indented));
            var state=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Overview;File.WriteAllText(Path.Combine(output,"final-state.json"),JsonConvert.SerializeObject(new{state.Tick,state.Entities,state.Buildings}));
            Console.WriteLine("ROUTE_REUSE_RESULT "+JsonConvert.SerializeObject(result));
            return timeout||final.Failure!=null||worker.Failure!=null||final.Tick!=TargetTicks?1:0;
        }
#endif
    }
}
