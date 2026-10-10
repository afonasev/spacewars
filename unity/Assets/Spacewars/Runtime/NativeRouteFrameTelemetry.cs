using System;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Spacewars.Runtime
{
    public struct NativeRouteFrameSample
    {
        public long Frame,Generation,Tick,Sequence,BarrierPolls,Reuses,Builds,Pumps,Retained,PeakRetained,QueueAge,Heap,MainAllocated,WorkerAllocated;
        public int Units,Buildings,Pending,Ready,GC0;
        public double Elapsed,FrameMs,UpdateMs,PrepareMs,ProviderMs,SolveMs,ProjectionMs,TransportWaitMs,TransportHoldMs,PublishMs;
        internal string Csv()=>string.Join(",",new object[]{Elapsed,Frame,Generation,Tick,Sequence,FrameMs,UpdateMs,Units,Buildings,BarrierPolls,Reuses,Builds,Pumps,PrepareMs,ProviderMs,SolveMs,ProjectionMs,TransportWaitMs,TransportHoldMs,PublishMs,Pending,Ready,QueueAge,Retained,PeakRetained,Heap,GC0,MainAllocated,WorkerAllocated});
    }
    // Optional diagnostics: single render producer, single IO consumer. Numeric
    // value samples own their data; overflow is counted explicitly, never waits.
    public sealed class NativeRouteFrameTelemetry : IDisposable
    {
        private readonly NativeRouteFrameSample[] samples;
        private readonly string path;
        private readonly Action beforeOpen;
        private readonly AutoResetEvent wake=new AutoResetEvent(false);
        private long produced,consumed,dropped;
        private int stopping,stopped;
        private string failure;
        public NativeRouteFrameTelemetry(string path,int capacity=4096,Action beforeOpen=null)
        {
            if(string.IsNullOrEmpty(path)||capacity<1)throw new ArgumentException("Invalid frame telemetry sink.");
            this.path=path;this.beforeOpen=beforeOpen;samples=new NativeRouteFrameSample[capacity];
            new Thread(Write){IsBackground=true,Name="Spacewars.FrameTelemetry"}.Start();
        }
        public long Dropped=>Interlocked.Read(ref dropped);
        public bool IsStopped=>Volatile.Read(ref stopped)!=0;
        public string Failure=>Volatile.Read(ref failure);
        public bool TryWrite(NativeRouteFrameSample sample)
        {
            if(Volatile.Read(ref stopping)!=0)return false;
            long next=Volatile.Read(ref produced);
            if(next-Volatile.Read(ref consumed)>=samples.Length){Interlocked.Increment(ref dropped);return false;}
            samples[next%samples.Length]=sample;Volatile.Write(ref produced,next+1);Signal();return true;
        }
        private void Signal(){try{wake.Set();}catch(ObjectDisposedException){}}
        private void Write()
        {
            try{
                Thread.CurrentThread.CurrentCulture=CultureInfo.InvariantCulture;beforeOpen?.Invoke();
                using(var writer=new StreamWriter(path)){
                    writer.WriteLine("elapsed,render_frame,generation,tick,sequence,frame_ms,main_update_ms,global_units,global_buildings,barrier_polls,barrier_reuses,barrier_builds,route_pumps,prepare_elapsed_ms,provider_elapsed_ms,solve_elapsed_ms,projection_elapsed_ms,transport_wait_ms,transport_hold_ms,publish_elapsed_ms,pending,ready,queue_age,retained_bytes,peak_retained_bytes,heap_bytes,gc0_count,main_allocated_bytes,route_worker_allocated_bytes");
                    while(true){
                        long next=Volatile.Read(ref consumed);
                        while(next<Volatile.Read(ref produced)){writer.WriteLine(samples[next%samples.Length].Csv());next++;Volatile.Write(ref consumed,next);}
                        writer.Flush();
                        if(Volatile.Read(ref stopping)!=0&&next==Volatile.Read(ref produced))break;
                        wake.WaitOne();
                    }
                    writer.WriteLine("# samples="+Volatile.Read(ref consumed)+",dropped="+Dropped);
                }
            }catch(Exception ex){Volatile.Write(ref failure,ex.ToString());}
            finally{Volatile.Write(ref stopped,1);wake.Dispose();}
        }
        // Called by the same producer after its last sample. IO drains on owner.
        public void Dispose(){Volatile.Write(ref stopping,1);Signal();}
    }
}
