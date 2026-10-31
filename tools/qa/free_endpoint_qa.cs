using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
#if !CPU_CLOCK_ONLY
using Spacewars.Runtime;
using Spacewars.Simulation;
#endif
internal static class FreeEndpointQa
{
#if !CPU_CLOCK_ONLY
    internal static long TargetTicks;
    internal sealed class MatchRow {public bool Measured;public long Tick,FirstMotion;public int MovedExplorers,Units,Buildings;public double WallSeconds;public RouteCpu.Interval Cpu;public object Worker;}
    private static MatchRow Match(string output,int seed,long ticks,bool measured)
    {
        Directory.CreateDirectory(output);TargetTicks=ticks;
        var profile=PlayableProfile.Create(JsonConvert.DeserializeObject<PlayableProfileData>(File.ReadAllText("unity/Assets/Spacewars/Content/Resources/PlayableProfile.json")),new CrimsonGroveMap(JsonConvert.DeserializeObject<CrimsonGroveProfileData>(File.ReadAllText("unity/Assets/Spacewars/Content/Resources/CrimsonGroveProfile.json"))));
        var setup=new NativeLobbyConfiguration{CrimsonGrove=true,HumanPresent=false,HasExplicitSeed=true,ExplicitSeed=seed};setup.InitializeParticipants();
        var cpu0=RouteCpu.Read();var clock=Stopwatch.StartNew();
        using(var samples=new StreamWriter(Path.Combine(output,"samples.jsonl")))
        using(var runtime=PlayableRuntime.CreateLobbyMatch(profile,1,setup,true))
        using(var routes=new CertifiedRouteWorker(runtime.NavigationBinding,profile.NavigationRequestsPerFrame,()=>runtime.IsStopRequested,runtime.ReportRouteFailure)){
            if(runtime.Latest.Participants.Count!=8)throw new Exception("Expected8participants");
            var genesis=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Overview.Entities.Where(u=>u.Kind==PlayableEntityKind.Explorer).ToDictionary(u=>u.Id,u=>u.Position);
            var moved=new System.Collections.Generic.HashSet<int>();long firstMotion=-1,lastSample=-1,lastObserved=-1;bool timeout=false;runtime.RequestPause(false);
            while(!runtime.IsStopped){
                var view=runtime.Latest;var overview=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Overview;
                if(overview.Tick!=lastObserved){lastObserved=overview.Tick;foreach(var u in overview.Entities)if(genesis.TryGetValue(u.Id,out var origin)&&Math.Pow(u.Position.X-origin.X,2)+Math.Pow(u.Position.Z-origin.Z,2)>.0625){moved.Add(u.Id);if(firstMotion<0)firstMotion=overview.Tick;}}
                if(view.Tick/900!=lastSample){lastSample=view.Tick/900;var now=RouteCpu.Read();samples.WriteLine(JsonConvert.SerializeObject(new{seconds=clock.Elapsed.TotalSeconds,cpu=now,interval=RouteCpu.Delta(cpu0,now),tick=view.Tick,firstMotion,movedExplorers=moved.Count,view.Failure}));samples.Flush();}
                if(view.Failure!=null)break;if(clock.Elapsed.TotalSeconds>900){timeout=true;break;}Thread.Sleep(10);
            }
            runtime.RequestStop();if(!SpinWait.SpinUntil(()=>runtime.IsStopped,30000))throw new Exception("Authority cleanup timeout");var final=runtime.Latest;var worker=routes.Publication;routes.Dispose();if(!SpinWait.SpinUntil(()=>routes.IsStopped,30000))throw new Exception("Worker cleanup timeout");
            var end=RouteCpu.Read();var row=new MatchRow{Measured=measured,Tick=final.Tick,FirstMotion=firstMotion,MovedExplorers=moved.Count,Units=runtime.DiagnosticGlobalUnits,Buildings=runtime.DiagnosticGlobalBuildings,WallSeconds=clock.Elapsed.TotalSeconds,Cpu=RouteCpu.Delta(cpu0,end),Worker=worker};
            File.WriteAllText(Path.Combine(output,"result.json"),JsonConvert.SerializeObject(new{seed,targetTicks=ticks,profile=profile.ProfileId,map=profile.AuthoredMap.Id,row,cpu0,end,final.Failure,timeout},Formatting.Indented));Console.WriteLine("MATCH_RESULT "+JsonConvert.SerializeObject(row));
            if(timeout||final.Failure!=null||worker.Failure!=null||final.Tick!=ticks||moved.Count!=7||firstMotion<0)throw new Exception("Ordinary witness failed; no unchanged replay");return row;
        }
    }
#endif
    public static int Main(string[] args)
    {
#if CPU_CLOCK_ONLY
        return RouteCpu.Probe(args[0]);
#elif FREE_ENDPOINT_CHECKS
        return FreeEndpointChecks.RunSelected();
#elif FREE_ENDPOINT_FROZEN
        return FreeEndpointFrozen.Run(args[0],int.Parse(args[1]),args.Contains("--calibrate"));
#else
        string output=args[0];int seed=int.Parse(args[1]);var warm=Match(Path.Combine(output,"warmup"),seed,900,false);GC.Collect();GC.WaitForPendingFinalizers();var measured=Match(Path.Combine(output,"measured"),seed,5400,true);
        File.WriteAllText(Path.Combine(output,"result.json"),JsonConvert.SerializeObject(new{warmup=warm,measured,primaryCpu="native process clock, cross-checked with getrusage",warmupExcluded=true},Formatting.Indented));return 0;
#endif
    }
}
