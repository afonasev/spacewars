using System;
using System.Threading;
using Spacewars.Runtime;
using Spacewars.Simulation;

// Actual production command and physical kernel. Barriers choose order only;
// they never alter authority state, inject failures, or run world/AI ticks.
internal static class NavigationCommandInterleavingScenario
{
    internal static object Run(bool overlap)
    {
        var timer=System.Diagnostics.Stopwatch.StartNew();
        var profile=PlayableProfile.Default;
        var domain=new PlayableDomain(profile,71,false);
        var nav=domain.Navigation;var kernel=new CertifiedRouteKernel();
        using var start=new ManualResetEventSlim();
        using var allocated=new ManualResetEventSlim();
        using var finish=new ManualResetEventSlim();
        Thread worker=null;Exception workerError=null;
        var trace=new System.Collections.Generic.List<string>();
        try
        {
            int id=(int)typeof(PlayableDomain).GetMethod("SpawnUnit",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(domain,new object[]{new NavPoint(-18,-14),PlayableOwner.Player,PlayableEntityKind.Tank});
            var binding=new PlayableRouteBinding(domain.Geometry,profile,nav.Admission,nav.RoutePort);
            kernel.Attach(nav.RoutePort);using(var warm=domain.CaptureWorldState(7,"command-interleaving")){}
            if(domain.Apply(new PlayableCommand(71,1,"player-1",PlayableCommandKind.Move,new[]{id},new NavPoint(-10,-14)).AsHuman(),out _)!=PlayableCommandStatus.Applied)throw new Exception("Setup command refused");
            kernel.PauseAtPhysicalBoundary=()=>{
                if(kernel.CapturePumpFrontier().Stage==CertifiedRouteKernel.PumpStage.Scan){
                    allocated.Set();if(!finish.Wait(3000))throw new TimeoutException("Worker allocation release");
                }
                return false;
            };
            worker=new Thread(()=>{try{if(!start.Wait(3000))throw new TimeoutException("Worker start");kernel.Pump(binding,4);}catch(Exception e){workerError=e;}});
            worker.Start();
            nav.AfterMovementSourcePinsForTests=()=>{
                trace.Add("command source pinned");
                if(overlap){start.Set();if(!allocated.Wait(3000))throw new TimeoutException("Worker Scan boundary");trace.Add("worker allocation active at Scan");}
            };
            var status=domain.Apply(new PlayableCommand(71,2,"player-1",PlayableCommandKind.Move,new[]{id},new NavPoint(-8,-14)).AsHuman(),out var message);
            trace.Add("command returned "+status);
            finish.Set();start.Set();if(!worker.Join(3000))throw new TimeoutException("Worker join");
            if(workerError!=null)throw new Exception("Worker failure",workerError);
            if(domain.Tick!=0)throw new Exception("Unexpected world stepping");
            if(status!=PlayableCommandStatus.Applied||message!="Order accepted.")throw new Exception("Command refused: "+status+" / "+message);
            return new{schedule=overlap?"pin-command/start-worker/resume-command":"command/worker",status=status.ToString(),numericStatus=(int)status,message,trace,tick=domain.Tick,elapsedMs=timer.Elapsed.TotalMilliseconds};
        }
        finally
        {
            finish.Set();start.Set();worker?.Join(3000);
            nav.AfterMovementSourcePinsForTests=null;
            kernel.PauseAtPhysicalBoundary=null;kernel.Clear();domain.DestroyOwnedNavigation();
        }
    }
}
