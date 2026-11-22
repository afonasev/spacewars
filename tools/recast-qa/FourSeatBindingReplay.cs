using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;

internal static class FourSeatBindingReplay
{
    internal static void RunMovedOrigin()
    {
        System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(typeof(PlayableRuntime).Assembly,
            (name,assembly,path)=>name=="spacewars_recast"?System.Runtime.InteropServices.NativeLibrary.Load(Path.GetFullPath("unity/Assets/Spacewars/Plugins/macOS/libspacewars_recast.dylib")):IntPtr.Zero);
        var input=Environment.GetEnvironmentVariable("FOUR_SEAT_REPLAY_INPUT")??throw new ArgumentException("Explicit input required");
        var captured=JObject.Parse(File.ReadAllText(input));
        var mapData=JsonConvert.DeserializeObject<ThreeCrossingsProfileData>(File.ReadAllText("unity/Assets/Spacewars/Content/Resources/ThreeCrossingsProfile.json"));
        var profile=PlayableProfile.Create(captured["manifest"]["Profile"].ToObject<PlayableProfileData>(),new ThreeCrossingsMap(mapData));
        Spacewars.Tests.EditMode.RecastMovedReadyFixture.Run(profile);
    }
    // Component diagnostic using the production source, map/profile and captured
    // human goals/applied ticks. Not an exact replay of worker scheduling or Player QA.
    internal static void Run()
    {
#if DEVELOPMENT_BUILD
        System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(typeof(PlayableRuntime).Assembly,
            (name,assembly,path)=>name=="spacewars_recast"?System.Runtime.InteropServices.NativeLibrary.Load(Path.GetFullPath("unity/Assets/Spacewars/Plugins/macOS/libspacewars_recast.dylib")):IntPtr.Zero);
        var input=Environment.GetEnvironmentVariable("FOUR_SEAT_REPLAY_INPUT")??throw new ArgumentException("Explicit input required");
        var output=Environment.GetEnvironmentVariable("FOUR_SEAT_REPLAY_OUTPUT")??throw new ArgumentException("Unique output required");
        if(Directory.Exists(output))throw new ArgumentException("Output exists");Directory.CreateDirectory(output);
        var captured=JObject.Parse(File.ReadAllText(input));
        var mapData=JsonConvert.DeserializeObject<ThreeCrossingsProfileData>(File.ReadAllText("unity/Assets/Spacewars/Content/Resources/ThreeCrossingsProfile.json"));
        var profile=PlayableProfile.Create(captured["manifest"]["Profile"].ToObject<PlayableProfileData>(),new ThreeCrossingsMap(mapData));
        var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=7};setup.InitializeParticipants();
        while(setup.Participants.Count<4)setup.AddParticipant(false,0,4);
        for(int i=0;i<4;i++){var p=setup.Participants[i];p.Human=true;p.Team=i/2+1;p.DeviceId=i;p.Name="Binding replay "+i;}
        OfflineMatchConfiguration config;
        using(var genesis=PlayableRuntime.CreateSplitScreenPerformanceMatch(profile,1,setup,startPaused:true,unitsPerSeat:20)){
            config=(OfflineMatchConfiguration)typeof(PlayableRuntime).GetField("offlineConfiguration",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(genesis);
        }
        var world=new PlayableAuthorityTick(config,1,asynchronousRoutes:true);world.EnableAccruedCredits();
        CertifiedRouteWorker worker=null;int next=0;var commands=(JArray)captured["commands"];var clock=Stopwatch.StartNew();
        try{
            worker=new CertifiedRouteWorker(world.NavigationBinding,profile.NavigationRequestsPerFrame);
            while(world.Tick<2000&&clock.Elapsed.TotalSeconds<180){
                while(next<commands.Count&&(long)commands[next]["tick"]<=world.Tick){
                    var c=commands[next]["command"];var p=c["Target"];
                    var command=new PlayableCommand(1,(long)c["Sequence"],(string)c["PlayerId"],Enum.Parse<PlayableCommandKind>((string)c["Kind"]),c["EntityIds"].ToObject<int[]>(),new NavPoint((double)p["X"],(double)p["Z"]),origin:PlayableOrderOrigin.Human);
                    PlayableCommandReceipt receipt;using(world.NavigationBinding.RoutePort.AcquireCapture(()=>clock.Elapsed.TotalSeconds>=180))receipt=world.Apply(command);if(receipt.Status!=PlayableCommandStatus.Applied)throw new Exception("Captured command refused "+receipt.Status+" at "+world.Tick+" message="+receipt.Message);
                    Console.WriteLine("REPLAY command tick="+world.Tick+" owner="+command.PlayerId+" seq="+command.Sequence);next++;
                }
                if(!world.TryAdvance()){Thread.Sleep(1);continue;}
                if(worker.Publication.Failure!=null)throw new Exception(worker.Publication.Failure);
                if(world.Tick%120==0)Console.WriteLine("REPLAY tick="+world.Tick+" wall="+clock.Elapsed.TotalSeconds);
            }
            if(world.Tick<2000)throw new TimeoutException("Component horizon2000 not reached");
            File.WriteAllText(Path.Combine(output,"result.json"),JsonConvert.SerializeObject(new{status="completed component diagnostic",tick=world.Tick,commands=next,seconds=clock.Elapsed.TotalSeconds},Formatting.Indented));
        }catch(Exception error){File.WriteAllText(Path.Combine(output,"failure.txt"),"tick="+world.Tick+" commands="+next+"\n"+error);throw;}
        finally{worker?.Dispose();if(worker!=null&&!SpinWait.SpinUntil(()=>worker.IsStopped,5000))throw new Exception("Worker did not stop");world.Domain.Navigation.ReleaseCommittedHostRoot();world.Domain.DestroyOwnedNavigation();}
#else
        throw new InvalidOperationException("Component replay requires DEVELOPMENT_BUILD source, never an implicit full suite");
#endif
    }
}
