using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        // Only authority tick boundaries issue/consume this single durable grant.
        // Physical Pump and SetBudget cannot replenish it.
        private bool tickQuantaEnabled;
        internal bool TickQuantaEnabled
        {
            get{using(EnterTransport())return tickQuantaEnabled||committedHostRoot?.TickQuantum==true;}
            set{using(EnterTransport()){
                if(committedHostRoot?.TickQuantum==true&&(!value||committedHostRoot.QuantumGeneration!=Generation))throw new ArgumentException("Saved quantum differs from the authority policy/generation.");
                tickQuantaEnabled=value;
            }}
        }
        internal bool QuantumRunnable {get{using(EnterTransport())return committedHostRoot?.QuantumRunnable==true;}}
        private bool QuantumIngressAllowed(NavigationRequest request)
            =>!TickQuantaEnabled||committedHostRoot?.QuantumRunnable==true&&request.Request<=committedHostRoot.AdmissionPrefix;
        internal void ValidateTickQuantumBoundary(long worldTick,bool asynchronous)
        {
            using(EnterTransport()){
                var root=committedHostRoot;if(root?.TickQuantum!=true)return;
                if(!tickQuantaEnabled||root.QuantumGeneration!=Generation||root.AdmissionPrefix>requestSequence||root.QuantumTick>worldTick+1||root.QuantumSerial==root.ConsumedQuantum&&root.QuantumTick>worldTick||!asynchronous&&root.QuantumSerial>root.ConsumedQuantum&&root.QuantumTick!=worldTick+1)
                    throw new ArgumentException("Saved quantum does not bind to the world tick/request prefix.");
            }
        }
        internal bool PrepareTickQuantum(long upcomingTick,int budget,bool asynchronous)
        {
            InitializePhysicalHostForCapture();
            NavigationHostRoot source=PinCommittedHostRoot(),next=null;
            NavigationRetentionBudget.Scope scope=null;bool changed=false;
            try{
                if(source==null)return false;
                long prefix;bool work;
                using(EnterTransport()){
                    prefix=requestSequence;
                    work=pending.Count!=0||allocating.Count!=0||probes.Count!=0||Requests.Count!=0||source.Rows.Count!=0||source.ArrivalWork.Count!=0||source.Delivered.Count!=0;
                    if(source.TickQuantum&&source.QuantumGeneration!=Generation)throw new InvalidOperationException("Quantum generation differs from authority.");
                    if(source.QuantumRunnable)return asynchronous;
                    // Fixed polls inspect the SAME completed quantum until the world
                    // step consumes it; they never mint another grant.
                    if(!asynchronous&&source.TickQuantum&&source.QuantumTick==upcomingTick&&source.QuantumSerial>source.ConsumedQuantum)return true;
                    if(!work&&(!source.TickQuantum||source.QuantumSerial==source.ConsumedQuantum))return true;
                }
                if(!TryOpenPhysicalHostScope(NavigationPreparationKind.Admission,Generation,Admission.PublicationIdentity,out scope))return false;
                next=source.ForkOwned(scope.OwnedReservation());
                if(next.TickQuantum&&next.QuantumSerial>next.ConsumedQuantum)next.ConsumeQuantum();
                if(work)next.IssueQuantum(Generation,upcomingTick,budget,prefix);
                next.SealPrepared();scope.Attach(next);
                PublishPhysicalHostRoot(scope,source,next,()=>{});changed=true;
                return asynchronous||!work;
            }catch(PhysicalIngressRefused){return asynchronous;}
            finally{next?.ReleaseStorage();scope?.Discard();source?.Release();if(changed)SignalHost();}
        }
        internal bool ConsumeFixedTickQuantum(long upcomingTick)
        {
            var source=PinCommittedHostRoot();NavigationHostRoot next=null;NavigationRetentionBudget.Scope scope=null;
            try{
                if(source==null||!source.TickQuantum||source.QuantumSerial==source.ConsumedQuantum)return true;
                if(source.QuantumTick!=upcomingTick||!source.QuantumCompleted)return false;
                if(!TryOpenPhysicalHostScope(NavigationPreparationKind.Admission,Generation,Admission.PublicationIdentity,out scope))return false;
                next=source.ForkOwned(scope.OwnedReservation());next.ConsumeQuantum();next.SealPrepared();scope.Attach(next);PublishPhysicalHostRoot(scope,source,next,()=>{});return true;
            }catch(PhysicalIngressRefused){return false;}
            finally{next?.ReleaseStorage();scope?.Discard();source?.Release();}
        }
    }
}
