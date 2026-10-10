using System;
using System.Threading;

namespace Spacewars.Runtime
{
    // Lifetime ownership across replacement generations of one host. Independent
    // hosts use independent lanes. This is not a ledger for retained old worlds.
    public sealed class CertifiedRouteLane
    {
        private readonly object gate=new object();
        private readonly ManualResetEvent available=new ManualResetEvent(true);
        private bool occupied;
        private Exception failure;
        internal bool Acquire(WaitHandle wake,Func<bool> stopping)
        {
            var signals=new[]{wake,available};
            while(!stopping()){
                lock(gate){
                    if(failure!=null)throw new InvalidOperationException("Certified route lane cleanup failed.",failure);
                    if(!occupied){occupied=true;available.Reset();return true;}
                }
                WaitHandle.WaitAny(signals);
            }
            return false;
        }
        internal void Release(Exception cleanupFailure)
        {
            lock(gate){failure=cleanupFailure;occupied=false;available.Set();}
        }
    }
}
