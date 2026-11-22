using System;
using System.Threading;

namespace Spacewars.Simulation
{
    internal interface INavMotionWork { void RunRows(); }

    // Execution infrastructure, owned by one match authority thread, like the
    // certified-route worker. No world, reservation or job survives Dispatch.
    // Each job owns/admit its own inputs, outputs and cursor before dispatch.
    internal sealed class NavMotionExecutor : IDisposable
    {
        [ThreadStatic] internal static NavMotionExecutor Current;
        private readonly object gate=new object();
        private readonly Thread[] helpers;
        private INavMotionWork work;
        private Exception infrastructureFailure;
        private long sequence;
        private int activeHelpers;
        private bool helperFinished;
        private long completedBatches;
        private bool running,stopping;
        internal int HelperCount=>helpers.Length;
        internal long CompletedBatches { get { lock(gate)return completedBatches; } }
        internal bool IsIdle { get { lock(gate)return !running&&work==null; } }
        internal NavMotionExecutor(int helperCount)
        {
            if(helperCount<1||helperCount>3)throw new ArgumentOutOfRangeException(nameof(helperCount));
            helpers=new Thread[helperCount];
            try{for(int i=0;i<helpers.Length;i++){
                helpers[i]=new Thread(Loop){IsBackground=true,Name="Spacewars.Motion."+i};helpers[i].Start();
            }}catch{Dispose();throw;}
        }
        internal void Dispatch(INavMotionWork next) => Dispatch(next,ownerParticipates:true);
        internal void DispatchWorkers(INavMotionWork next) => Dispatch(next,ownerParticipates:false);
        private void Dispatch(INavMotionWork next,bool ownerParticipates)
        {
            if(next==null)throw new ArgumentNullException(nameof(next));
            lock(gate){
                if(stopping)throw new ObjectDisposedException(nameof(NavMotionExecutor));
                if(running)throw new InvalidOperationException("Motion executor already has a job.");
                running=true;helperFinished=false;work=next;infrastructureFailure=null;sequence++;Monitor.PulseAll(gate);
            }
            Exception failure=null;
            try{if(ownerParticipates)next.RunRows();}catch(Exception error){failure=error;}
            finally{lock(gate){
                // One participant must drain the cursor before closing admission.
                // Worker-only batches keep the authority thread outside RunRows.
                while(!ownerParticipates&&!helperFinished)Monitor.Wait(gate);
                work=null;
                while(activeHelpers!=0)Monitor.Wait(gate);
                failure=failure??infrastructureFailure;infrastructureFailure=null;completedBatches++;running=false;Monitor.PulseAll(gate);
            }}
            if(failure!=null)throw failure;
        }
        private void Loop()
        {
            long observed=0;
            while(true){
                INavMotionWork job;
                lock(gate){while(!stopping&&(sequence==observed||work==null))Monitor.Wait(gate);
                    if(stopping)return;
                    observed=sequence;job=work;activeHelpers++;}
                try{job.RunRows();}
                catch(Exception error){lock(gate)if(infrastructureFailure==null)infrastructureFailure=error;}
                finally{job=null;lock(gate){helperFinished=true;activeHelpers--;if(activeHelpers==0)Monitor.PulseAll(gate);}}
            }
        }
        public void Dispose()
        {
            lock(gate){while(running)Monitor.Wait(gate);stopping=true;Monitor.PulseAll(gate);}
            foreach(var helper in helpers)if(helper!=null&&helper.IsAlive)helper.Join();
        }
    }
}
