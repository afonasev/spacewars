using System;
using System.Diagnostics;
using System.Threading;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // A publication owns no mutable scheduler/provider structures. Main may read
    // it without consulting the transport or waiting for the solver.
    public sealed class CertifiedRoutePublication
    {
        internal CertifiedRoutePublication(CertifiedRouteKernel kernel,long pumps,double elapsed,string failure=null,long allocated=0)
        {RecastFoundationBuilds=kernel.RecastFoundationBuilds;RecastFoundationReuses=kernel.RecastFoundationReuses;RecastPrepareCount=kernel.RecastPrepareCount;RecastGeometryDigests=kernel.RecastGeometryDigests;RecastColdBuilds=kernel.RecastColdBuilds;RecastWarmHits=kernel.RecastWarmHits;RecastLongQueries=kernel.RecastLongQueries;RecastAttachments=kernel.RecastAttachments;RecastIterations=kernel.RecastIterations;Counters=kernel.Counters;Epoch=kernel.SolverEpoch;RetainedBytes=kernel.RetainedBytes;
            PeakRetainedBytes=kernel.PeakRetainedBytes;ProjectionFailure=kernel.LastProjectionFailure;
            Pumps=pumps;PumpElapsedMilliseconds=elapsed;Failure=failure;AllocatedBytes=allocated;
            PreparationElapsedMilliseconds=kernel.PreparationElapsedMilliseconds;ProviderElapsedMilliseconds=kernel.ProviderElapsedMilliseconds;
            SolveElapsedMilliseconds=kernel.SolveElapsedMilliseconds;ProjectionElapsedMilliseconds=kernel.ProjectionElapsedMilliseconds;
            TransportWaitMilliseconds=kernel.TransportWaitMilliseconds;TransportHoldMilliseconds=kernel.TransportHoldMilliseconds;}
        public long RecastFoundationBuilds{get;}public long RecastFoundationReuses{get;}public long RecastPrepareCount{get;}public long RecastGeometryDigests{get;}
        public long RecastColdBuilds{get;}public long RecastWarmHits{get;}
        public long RecastLongQueries{get;} public long RecastAttachments{get;} public long RecastIterations{get;}
        public NavSchedulerCounters Counters {get;}
        public long Epoch {get;} public long RetainedBytes {get;} public long PeakRetainedBytes {get;}
        public string ProjectionFailure {get;} public string Failure {get;}
        public long Pumps {get;} public double PumpElapsedMilliseconds {get;} public long AllocatedBytes {get;}
        public double PreparationElapsedMilliseconds {get;} public double ProviderElapsedMilliseconds {get;}
        public double SolveElapsedMilliseconds {get;} public double ProjectionElapsedMilliseconds {get;}
        public double TransportWaitMilliseconds {get;} public double TransportHoldMilliseconds {get;}
    }

    // Exactly one solver owner per generation. Logical work drives its cadence;
    // blocked mailbox/admission waits are woken by transport changes, never FPS.
    public sealed class CertifiedRouteWorker : IDisposable
    {
        private PlayableRouteBinding binding;
        private readonly CertifiedRouteKernel kernel;
        private Func<bool> stopRequested;
        private Action<Exception> failed;
        private readonly CertifiedRouteLane lane;
        private readonly AutoResetEvent wake=new AutoResetEvent(false);
        private readonly Thread thread;
        private CertifiedRoutePublication publication;
        private int stopping,stopped,waiting;
        public CertifiedRouteWorker(PlayableRouteBinding binding,int budget,Func<bool> stopRequested=null,Action<Exception> failed=null,CertifiedRouteLane lane=null)
            :this(binding,budget,new CertifiedRouteKernel(),stopRequested,failed,lane){}
        internal CertifiedRouteWorker(PlayableRouteBinding binding,int budget,CertifiedRouteKernel kernel,Func<bool> stopRequested=null,Action<Exception> failed=null,CertifiedRouteLane lane=null)
        {
            if(binding?.RoutePort==null||budget<1)throw new ArgumentException("Invalid certified worker binding.");
            this.lane=lane??new CertifiedRouteLane();this.binding=binding;this.kernel=kernel;this.stopRequested=stopRequested;this.failed=failed;this.budget=budget;
            publication=new CertifiedRoutePublication(kernel,0,0);
            kernel.Continue=()=>!ShouldStop;
            kernel.CapturePauseRequested=()=>binding.RoutePort.CaptureRequested;
            binding.RoutePort.Changed+=Signal;
            thread=new Thread(Loop){IsBackground=true,Name="Spacewars.CertifiedRoutes."+binding.Admission.Generation};
            thread.Start();
        }
        private int budget;
        public CertifiedRoutePublication Publication=>Volatile.Read(ref publication);
        public bool IsWaiting=>Volatile.Read(ref waiting)!=0;
        public bool IsStopped=>Volatile.Read(ref stopped)!=0;
        private bool ShouldStop=>Volatile.Read(ref stopping)!=0||stopRequested?.Invoke()==true;
        public void SetBudget(int value){if(value<1)throw new ArgumentOutOfRangeException(nameof(value));if(Interlocked.Exchange(ref budget,value)!=value)Signal();}
        private void Signal(){try{wake.Set();}catch(ObjectDisposedException){}}
        private void WaitForChange(){Volatile.Write(ref waiting,1);try{wake.WaitOne();}finally{Volatile.Write(ref waiting,0);}}
        private bool EnterHostOperation(bool cleanup=false)
        {
            while(cleanup||!ShouldStop){if(binding.RoutePort.TryBeginHostOperation(cleanup))return true;WaitForChange();}
            return false;
        }
        private bool PumpOnce(ref long pumps,ref double elapsed,long initialAllocated)
        {
            if(binding.RoutePort.TickQuantaEnabled&&!binding.RoutePort.QuantumRunnable&&binding.RoutePort.TryPromoteAccruedCredit()!=NavigationCreditPromotion.Promoted)return true;
            if(!binding.RoutePort.QuantumRunnable&&!kernel.HasWork&&binding.RoutePort.QueuedRequestCount==0&&binding.RoutePort.AllocationCount==0&&!binding.RoutePort.ArrivalReconstructing)return true;
            long before=kernel.ProgressSequence,at=Stopwatch.GetTimestamp();var beforeFrontier=kernel.CapturePumpFrontier();
            using(PerformanceProfile.Measure(ProfileStage.RouteWorker))kernel.Pump(binding,Volatile.Read(ref budget));pumps++;
            elapsed+=(Stopwatch.GetTimestamp()-at)*1000d/Stopwatch.Frequency;
            Volatile.Write(ref publication,new CertifiedRoutePublication(kernel,pumps,elapsed,allocated:GC.GetAllocatedBytesForCurrentThread()-initialAllocated));
            if(ShouldStop||binding.RoutePort.CaptureRequested)return false;
            if(kernel.ProgressSequence!=before||!kernel.CapturePumpFrontier().Same(beforeFrontier))return false;
            return !(binding.RoutePort.TickQuantaEnabled&&!binding.RoutePort.QuantumRunnable&&binding.RoutePort.TryPromoteAccruedCredit()==NavigationCreditPromotion.Promoted);
        }
        private void Loop()
        {
            long pumps=0,initialAllocated=GC.GetAllocatedBytesForCurrentThread();double elapsed=0;bool acquired=false;
            try {
                Volatile.Write(ref waiting,1);
                try{acquired=lane.Acquire(wake,()=>ShouldStop);}finally{Volatile.Write(ref waiting,0);}
                if(!acquired)return;
                if(!EnterHostOperation())return;
                try{kernel.Attach(binding.RoutePort);kernel.PrepareRecastStartup(binding);Volatile.Write(ref publication,new CertifiedRoutePublication(kernel,pumps,elapsed,allocated:GC.GetAllocatedBytesForCurrentThread()-initialAllocated));}
                catch(Exception ex){Volatile.Write(ref publication,new CertifiedRoutePublication(kernel,pumps,elapsed,ex.ToString()));binding.RoutePort.ReportHostFailure(ex);throw;}
                finally{binding.RoutePort.EndHostOperation();}
                while(!ShouldStop){
                    if(!EnterHostOperation())break;
                    bool sleep;
                    try{sleep=PumpOnce(ref pumps,ref elapsed,initialAllocated);}
                    catch(Exception ex){Volatile.Write(ref publication,new CertifiedRoutePublication(kernel,pumps,elapsed,ex.ToString()));binding.RoutePort.ReportHostFailure(ex);throw;}
                    finally{binding.RoutePort.EndHostOperation();}
                    if(ShouldStop)break;
                    if(sleep)WaitForChange();else Thread.Yield();
                }
            }catch(Exception ex){binding.RoutePort.ReportHostFailure(ex);ReportFailure(ex);}
            finally{
                PerformanceProfile.Flush();
                Exception cleanupFailure=null;
                binding.RoutePort.CancelResultPreparation();
                binding.RoutePort.CancelCommandPreparation();
                // Cleanup also mutates the host and must wait for capture's finally release.
                EnterHostOperation(cleanup:true);
                try{kernel.Clear();}catch(Exception ex){cleanupFailure=ex;binding.RoutePort.ReportHostFailure(ex);ReportFailure(ex);
                    Volatile.Write(ref publication,new CertifiedRoutePublication(kernel,pumps,elapsed,ex.ToString()));}
                finally{
                    binding.RoutePort.EndHostOperation();binding.RoutePort.Changed-=Signal;
                    if(acquired)lane.Release(cleanupFailure);
                    kernel.Continue=null;kernel.CapturePauseRequested=null;binding=null;stopRequested=null;failed=null;
                    Volatile.Write(ref stopped,1);wake.Dispose();
                }
            }
        }
        private void ReportFailure(Exception error){try{failed?.Invoke(error);}catch(Exception callbackError){
            Volatile.Write(ref publication,new CertifiedRoutePublication(kernel,Publication.Pumps,Publication.PumpElapsedMilliseconds,error+"\nFailure callback: "+callbackError));}}
        public void Dispose(){Volatile.Write(ref stopping,1);Signal();}
    }
}
