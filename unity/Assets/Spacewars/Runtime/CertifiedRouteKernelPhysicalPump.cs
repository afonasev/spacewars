using System;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public sealed partial class CertifiedRouteKernel
    {
        // Called before any private projection delta is prepared. Capture sees
        // the committed prefix, including the unconsumed operation grant.
        internal Action BeforePhysicalProjectionStep;
        private static PumpFrontier WithCounters(PumpFrontier f,PumpStage? stage=null,
            int? scans=null,int? transfers=null,int? ready=null,int? projection=null,
            int? phase=null,int? scanLimit=null,long? floor=null,int? solveLimit=null,int? consumed=null)
            =>new PumpFrontier(stage??f.Stage,f.OriginalBudget,scans??f.ScansUsed,
                transfers??f.TransfersUsed,ready??f.ReadyUsed,projection??f.ProjectionUsed,
                f.ProjectionAllowance,solveLimit??f.SolveWorkLimit,consumed??f.SolveConsumed,
                floor??f.AllocationFloor,phase??f.PhaseUsed,scanLimit??f.ScanAllowance);
        private void AdoptPhysicalFrontier(NavigationHostRoot root)
        {
            var f=root.Frontier;
            lock(pumpFrameGate){pumpStage=f.Stage;pumpBudget=f.OriginalBudget;
                scansUsed=f.ScansUsed;transfersUsed=f.TransfersUsed;readyUsed=f.ReadyUsed;
                projectionUsed=f.ProjectionUsed;projectionAllowance=f.ProjectionAllowance;
                solveWorkLimit=f.SolveWorkLimit;solveConsumed=f.SolveConsumed;
                pumpAllocationFloor=f.AllocationFloor;phaseUsed=f.PhaseUsed;scanAllowance=f.ScanAllowance;}
            epoch=root.Epoch;ProgressSequence=root.Progress;
        }
        private void RefreshPhysicalFrontier(NavigationRoutePort port)
        {var root=port.PinCommittedHostRoot();try{if(root!=null)AdoptPhysicalFrontier(root);}finally{root?.Release();}}
        private bool PublishPhysicalFrame(NavigationRoutePort port,PumpFrontier frame)
        {
            var source=port.PinCommittedHostRoot();NavigationHostRoot next=null;
            NavigationRetentionBudget.Scope scope=null;
            try{
                if(!port.TryOpenPhysicalHostScope(NavigationPreparationKind.Solve,epoch,
                    port.Admission.PublicationIdentity,out scope))return false;
                next=source==null?port.CreatePhysicalHostRoot(scope.OwnedReservation()):source.ForkOwned(scope.OwnedReservation());
                if(source==null)port.StageRetainedPhysicalArrivals(next,scope.OwnedReservation());
                next.SetFrontier(frame,epoch,ProgressSequence);
                if(next.QuantumRunnable&&frame.Stage==PumpStage.Idle)next.CompleteQuantum();
                next.SetOperation(new NavigationHostRoot.OperationDescriptor(frame.Stage,frame.PhaseUsed,false,false,default));
                next.SealPrepared();scope.Attach(next);port.PublishPhysicalHostRoot(scope,source,next);
                AdoptPhysicalFrontier(next);return true;
            }finally{next?.ReleaseStorage();scope?.Discard();source?.Release();}
        }
        private bool RetirePhysicalIdleCacheForArrival(NavigationRoutePort port)
        {
            // Arrival owns a separate shared graph/field. An idle ordinary
            // derivative cache must not exhaust its outer Session COW headroom.
            // Use the existing retirement grant and cache maintenance operation;
            // never discard an active ordinary job or an arrival artifact.
            if(!port.TickQuantaEnabled||port.AllocationCount==0)return false;
            var source=port.PinCommittedHostRoot();NavigationHostRoot next=null;NavigationRetentionBudget.Scope scope=null;
            try{
                if(source==null||source.Scheduler.PendingCount!=0||source.Scheduler.ReadyCount!=0||source.Scheduler.Counters.CacheBytes==0)return false;
                if(!port.TryOpenPhysicalHostScope(NavigationPreparationKind.Solve,epoch,port.Admission.PublicationIdentity,out scope))return false;
                next=source.ForkOwned(scope.OwnedReservation());long before=next.Scheduler.Counters.MaintenanceWork;
                next.Scheduler.TryMakeRoomForExternal(LayeredNavigationScheduler.MaxRetainedBytes,1);
                if(next.Scheduler.Counters.MaintenanceWork==before)return false;
                var f=source.Frontier;next.SetFrontier(WithCounters(f,phase:checked(f.PhaseUsed+1)),epoch,checked(source.Progress+1));
                next.SetOperation(new NavigationHostRoot.OperationDescriptor(PumpStage.Retirement,f.PhaseUsed,true,true,default));next.SealPrepared();scope.Attach(next);port.PublishPhysicalHostRoot(scope,source,next);AdoptPhysicalFrontier(next);return true;
            }catch(NavigationSession.PhysicalIngressRefused){return false;}
            finally{next?.ReleaseStorage();scope?.Discard();source?.Release();}
        }
        private bool? TryDropPhysicalStale(NavigationRoutePort port)
        {
            if(!port.TryObservePhysicalStale(out var request))return null;
            var source=port.PinCommittedHostRoot();NavigationHostRoot next=null;NavigationRetentionBudget.Scope scope=null;
            try{
                if(!port.TryOpenPhysicalHostScope(NavigationPreparationKind.Admission,epoch,port.Admission.PublicationIdentity,out scope))return false;
                next=source.ForkOwned(scope.OwnedReservation());var f=CapturePumpFrontier();next.SetFrontier(WithCounters(f,transfers:checked(f.TransfersUsed+1)),epoch,checked(ProgressSequence+1));next.SetOperation(new NavigationHostRoot.OperationDescriptor(PumpStage.Transfer,f.TransfersUsed,true,false,default));next.SealPrepared();
                return port.PublishOwnedStaleIngress(scope,source,next,request);
            }finally{next?.ReleaseStorage();scope?.Discard();source?.Release();}
        }
        private bool ScanPhysical(NavigationRoutePort port)
        {
            var source=port.PinCommittedHostRoot();NavigationHostRoot next=null;
            NavigationRetentionBudget.Scope scope=null;
            try{
                if(source==null||source.Scan.Count==0)return false;
                if(!port.TryOpenPhysicalHostScope(NavigationPreparationKind.Solve,epoch,
                    port.Admission.PublicationIdentity,out scope))return false;
                next=source.ForkOwned(scope.OwnedReservation());var key=next.Scan.First;
                bool current=next.Rows.TryGetValue(key,out var entry)&&entry is NavigationHostRow row&&port.ContainsCurrent(row.Request);
                next.StageScan(current,scope.OwnedReservation());
                next.SetFrontier(WithCounters(source.Frontier,scans:source.Frontier.ScansUsed+1),epoch,ProgressSequence);
                next.SetOperation(new NavigationHostRoot.OperationDescriptor(PumpStage.Scan,source.Frontier.ScansUsed,true,false,key.Reference));
                next.SealPrepared();scope.Attach(next);port.PublishPhysicalHostRoot(scope,source,next);AdoptPhysicalFrontier(next);return true;
            }finally{next?.ReleaseStorage();scope?.Discard();source?.Release();}
        }
        private bool TryRegisterPhysicalArrival(PlayableRouteBinding binding)
        {
            var port=binding.RoutePort;NavigationHostRoot source=null,next=null;NavigationRetentionBudget.Scope scope=null;
            try{
                if(!port.TryOpenPhysicalHostScope(NavigationPreparationKind.Admission,epoch,port.Admission.PublicationIdentity,out scope))return false;
                if(!port.TryObservePhysicalArrivalIngress(out var ingress,out var admissionPin,out var input,out var proposal,out var token,out var attempt))return false;
                using var observedPin=admissionPin;var observed=observedPin.Value;
                source=port.PinCommittedHostRoot();next=source.ForkOwned(scope.OwnedReservation());
                NavNameKey key;
                using(NavStorageScratch.Reserve(scope.OwnedReservation(),16384)){
                    var recipe=proposal.Proof.PhysicalRecipe??throw new InvalidOperationException("Actual arrival proof has no owned recipe.");
                    if(!ReferenceEquals(recipe.Subscription,proposal.Route.Subscription))throw new InvalidOperationException("Actual arrival route lost its original recipe owner.");
                    key=next.RegisterArrival(ingress,recipe,proposal.Route,token,observed.SurfaceProviderId,scope.OwnedReservation());
                }
                var f=CapturePumpFrontier();next.SetFrontier(WithCounters(f,transfers:f.TransfersUsed+1),epoch,checked(ProgressSequence+1));
                next.SetOperation(new NavigationHostRoot.OperationDescriptor(PumpStage.Transfer,f.TransfersUsed,true,true,key.Reference));
                next.SealPrepared();port.PublishRegisteredPhysicalArrival(scope,source,next,observed,ingress,key,attempt);AdoptPhysicalFrontier(next);return true;
            }catch(NavigationSession.PhysicalIngressRefused){return false;}finally{next?.ReleaseStorage();scope?.Discard();source?.Release();}
        }
        internal Func<bool> PauseAtPhysicalBoundary;
        internal int PhysicalOperationsPerPump=int.MaxValue;
        private int pumpExecutions;
        internal bool PumpExecuting=>System.Threading.Volatile.Read(ref pumpExecutions)!=0;
        public void Pump(PlayableRouteBinding binding,int budget)
        {
            System.Threading.Interlocked.Increment(ref pumpExecutions);
            try{PumpCore(binding,budget);}finally{System.Threading.Interlocked.Decrement(ref pumpExecutions);}
        }
        private void PumpCore(PlayableRouteBinding binding,int budget)
        {
            if(budget<0||budget>int.MaxValue/WorkUnitsPerRequestBudget)throw new ArgumentOutOfRangeException(nameof(budget));
            var port=binding.RoutePort??throw new ArgumentException("Missing route authority facade.");
            Attach(port);if(port.UseLegacyHost){PumpLegacy(binding,budget);return;}RefreshPhysicalFrontier(port);
            if(port.TickQuantaEnabled&&!port.QuantumRunnable){if(port.TryPromoteAccruedCredit()!=NavigationCreditPromotion.Promoted)return;RefreshPhysicalFrontier(port);}
            if(pumpStage==PumpStage.Idle){
                var quantum=port.PinCommittedHostRoot();try{if(quantum?.TickQuantum==true)budget=quantum.QuantumBudget;}finally{quantum?.Release();}
                var root=port.PinCommittedHostRoot();int count;try{count=root?.Scan.Count??0;}finally{root?.Release();}
                var initial=new PumpFrontier(PumpStage.Retirement,budget,0,0,0,0,checked(budget*16),0,0,0,0,Math.Min(budget,count));
                if(!PublishPhysicalFrame(port,initial))return;
            }
            // A resumed suffix always uses its saved budget. The caller's new
            // budget becomes applicable only after this suffix reaches Idle.
            if(pumpStage>=PumpStage.Scan&&pumpStage<=PumpStage.EndAllocation&&!allocating){
                reservedCorridorBytes=Math.Max(pumpAllocationFloor,BeginAllocation(port));allocating=true;
            }
            int operations=PhysicalOperationsPerPump;
            try{while(true){
                if(operations--<=0)return;
                if(PauseAtPhysicalBoundary?.Invoke()==true)return;
                var f=CapturePumpFrontier();int grant=f.OriginalBudget;
                switch(f.Stage){
                    case PumpStage.Retirement:
                        if(f.PhaseUsed<grant&&RetirePhysicalIdleCacheForArrival(port)){RefreshPhysicalFrontier(port);break;}
                        if(f.PhaseUsed<grant&&port.RetirePhysicalArrivalOwner()){RefreshPhysicalFrontier(port);break;}
                        if(f.PhaseUsed<grant&&RetirePhysicalDelivered(binding)){
                            RefreshPhysicalFrontier(port);
                            if(!PublishPhysicalFrame(port,WithCounters(CapturePumpFrontier(),phase:f.PhaseUsed+1)))return;break;
                        }
                        if(!PublishPhysicalFrame(port,WithCounters(f,PumpStage.ArrivalAdvance,phase:0)))return;break;
                    case PumpStage.ArrivalAdvance:
                        var arrivalRoot=port.PinCommittedHostRoot();bool fairArrival;try{fairArrival=arrivalRoot.FairArrivals;}finally{arrivalRoot.Release();}
                        if(port.AdvanceArrivals(grant)||fairArrival){RefreshPhysicalFrontier(port);f=CapturePumpFrontier();}
                        // New-policy work/cursor and its spent grant publish
                        // together. Refused publication leaves the same quantum
                        // resumable; never overwrite it with a stale close frame.
                        if(fairArrival){if(f.Stage==PumpStage.ArrivalAdvance)return;break;}
                        if(!PublishPhysicalFrame(port,WithCounters(f,PumpStage.PreFlush,phase:0)))return;break;
                    case PumpStage.PreFlush:
                    case PumpStage.PostFlush:
                        if(f.PhaseUsed<grant&&FlushPhysicalOrdinary(binding)){RefreshPhysicalFrontier(port);break;}
                        if(!PublishPhysicalFrame(port,WithCounters(f,f.Stage==PumpStage.PreFlush?PumpStage.Allocation:PumpStage.Idle,phase:0)))return;
                        if(f.Stage==PumpStage.PostFlush)return;break;
                    case PumpStage.Allocation:
                        // Publication can refuse while the allocation token remains
                        // ours. Resume that phase without acquiring it a second time.
                        if(!allocating){reservedCorridorBytes=BeginAllocation(port);allocating=true;}
                        if(!PublishPhysicalFrame(port,WithCounters(f,PumpStage.Scan,floor:reservedCorridorBytes)))return;
                        break;
                    case PumpStage.Scan:
                        if(f.ScansUsed<f.ScanAllowance&&ScanPhysical(port))break;
                        if(!PublishPhysicalFrame(port,WithCounters(f,PumpStage.Transfer)))return;break;
                    case PumpStage.Transfer:
                        if(f.TransfersUsed<grant&&Continue?.Invoke()!=false){
                            var stale=TryDropPhysicalStale(port);
                            if(stale.HasValue){if(!stale.Value)return;RefreshPhysicalFrontier(port);break;}
                            if(TryRegisterPhysicalArrival(binding)||TryRegisterPhysicalOrdinary(binding)){RefreshPhysicalFrontier(port);break;}
                        }
                        if(!PublishPhysicalFrame(port,WithCounters(f,PumpStage.Solve,solveLimit:checked(grant*WorkUnitsPerRequestBudget))))return;break;
                    case PumpStage.Solve:
                        AdvancePhysicalOrdinary(binding,f.SolveWorkLimit-f.SolveConsumed);RefreshPhysicalFrontier(port);
                        if(pumpStage==PumpStage.Solve&&!PublishPhysicalFrame(port,WithCounters(CapturePumpFrontier(),PumpStage.Ready)))return;break;
                    case PumpStage.Ready:
                        if(f.ReadyUsed<grant&&TransferPhysicalReady(binding,grant-f.ReadyUsed)>0){RefreshPhysicalFrontier(port);break;}
                        if(!PublishPhysicalFrame(port,WithCounters(CapturePumpFrontier(),PumpStage.Projection)))return;break;
                    case PumpStage.Projection:
                        if(f.ProjectionUsed<f.ProjectionAllowance){
                            BeforePhysicalProjectionStep?.Invoke();
                            if(Continue?.Invoke()!=false&&ProjectPhysicalOrdinary(binding,f.ProjectionAllowance-f.ProjectionUsed)>0){RefreshPhysicalFrontier(port);break;}
                        }
                        if(!PublishPhysicalFrame(port,WithCounters(f,PumpStage.EndAllocation)))return;break;
                    case PumpStage.EndAllocation:
                        var retained=port.PhysicalRetainedBytes??0;allocating=false;reservedCorridorBytes=0;port.EndHostAllocation(retained);
                        if(!PublishPhysicalFrame(port,WithCounters(f,PumpStage.PostFlush,phase:0)))return;break;
                    case PumpStage.Idle:return;
                    default:throw new InvalidOperationException("Unknown physical host pump phase.");
                }
            }}catch{
                if(allocating){var retained=port.PhysicalRetainedBytes??0;allocating=false;reservedCorridorBytes=0;port.EndHostAllocation(retained);}
                throw;
            }
        }
    }
}
