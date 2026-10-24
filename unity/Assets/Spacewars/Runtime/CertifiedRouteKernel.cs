using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // One bounded host adapter for ordinary and headless entrypoints. Epochs are
    // solver allotments under the existing frozen gameplay barrier, not ticks.
    public sealed partial class CertifiedRouteKernel
    {
        private sealed class ProviderEntry
        {
            internal ILayeredNavigationProvider Provider;
            internal int References;
        }
        internal sealed class Registered
        {
            internal NavigationRequest Request;
            internal string SurfaceProviderId;
            internal NavScheduledRequest Scheduled;
            internal NavigationAnswer Completion;
            internal NavScheduledCompletion Solved;
            internal int LegCursor, ReplayCursor;
            internal NavLocation Expected, At;
            internal List<NavPoint> Points;
            internal List<string> TypedPortals, ObservedPortals; internal bool Arrival;
            internal NavigationRoutePort ArrivalOwnerPort;internal ArrivalProviderLease ArrivalOwner;
        }
        private readonly LayeredNavigationScheduler scheduler = new LayeredNavigationScheduler();
        private readonly Dictionary<Tuple<NavGeometry,string>,ProviderEntry> providers =
            new Dictionary<Tuple<NavGeometry,string>,ProviderEntry>();
        private long providerBytes;
        private long heldProjectionBytes,hostAnswerBytes,peakRetainedBytes;
        private readonly Dictionary<string, Registered> registered = new Dictionary<string, Registered>(StringComparer.Ordinal);
        private readonly Queue<string> scanQueue = new Queue<string>();
        private readonly Queue<string> projectionQueue = new Queue<string>();
        private readonly Queue<string> completionQueue = new Queue<string>();
        private long epoch;
        private NavigationRoutePort currentPort;
        internal enum PumpStage {Idle,Retirement,ArrivalAdvance,PreFlush,Allocation,Scan,Transfer,Solve,Ready,Projection,EndAllocation,PostFlush}
        // Phase arithmetic only. The durable host checkpoint must also retain
        // the scheduler, rows, queues and in-flight operation ownership.
        internal readonly struct PumpFrontier
        {
            internal readonly PumpStage Stage;
            internal readonly int OriginalBudget,ScansUsed,TransfersUsed,ReadyUsed,ProjectionUsed,ProjectionAllowance,SolveWorkLimit,SolveConsumed;
            internal readonly long AllocationFloor;
            internal readonly int PhaseUsed,ScanAllowance;
            internal bool Same(PumpFrontier other)=>Stage==other.Stage&&OriginalBudget==other.OriginalBudget&&ScansUsed==other.ScansUsed&&TransfersUsed==other.TransfersUsed&&ReadyUsed==other.ReadyUsed&&ProjectionUsed==other.ProjectionUsed&&ProjectionAllowance==other.ProjectionAllowance&&SolveWorkLimit==other.SolveWorkLimit&&SolveConsumed==other.SolveConsumed&&AllocationFloor==other.AllocationFloor&&PhaseUsed==other.PhaseUsed&&ScanAllowance==other.ScanAllowance;
            internal PumpFrontier(PumpStage stage,int originalBudget,int scansUsed,int transfersUsed,int readyUsed,int projectionUsed,int projectionAllowance,int solveWorkLimit,int solveConsumed,long allocationFloor,int phaseUsed=0,int scanAllowance=0)
            {Stage=stage;OriginalBudget=originalBudget;ScansUsed=scansUsed;TransfersUsed=transfersUsed;ReadyUsed=readyUsed;ProjectionUsed=projectionUsed;ProjectionAllowance=projectionAllowance;SolveWorkLimit=solveWorkLimit;SolveConsumed=solveConsumed;AllocationFloor=allocationFloor;PhaseUsed=phaseUsed;ScanAllowance=scanAllowance;}
        }
        private readonly object pumpFrameGate=new object();
        private PumpStage pumpStage;
        private int pumpBudget,scansUsed,transfersUsed,readyUsed,projectionUsed,projectionAllowance,solveWorkLimit,solveConsumed;
        private long pumpAllocationFloor;
        private int phaseUsed,scanAllowance;
        internal PumpFrontier CapturePumpFrontier(){lock(pumpFrameGate)return new PumpFrontier(pumpStage,pumpBudget,scansUsed,transfersUsed,readyUsed,projectionUsed,projectionAllowance,solveWorkLimit,solveConsumed,pumpAllocationFloor,phaseUsed,scanAllowance);}
        private void SetPumpStage(PumpStage value){lock(pumpFrameGate)pumpStage=value;}
        private long reservedCorridorBytes;
        private bool allocating;
        private int allocationOwnerThread;
        internal bool OwnsAllocationThread(int thread)=>allocating&&thread!=0&&allocationOwnerThread==thread;
        // The token remains recorded after EndHostAllocation; it is not authority
        // to reopen allocation. Used only with a pinned same-quantum completion.
        internal bool CompletedAllocationOnThread(int thread)=>!allocating&&thread!=0&&allocationOwnerThread==thread;
        private long BeginAllocation(NavigationRoutePort port)
        {
            var floor=port.BeginHostAllocation();
            allocationOwnerThread=System.Threading.Thread.CurrentThread.ManagedThreadId;
            return floor;
        }
        internal Action BeforePrepare;
        internal Action<ArrivalProjectionDelta,ArrivalProjection> BeforeArrivalProjectionCommit;
        internal Func<bool> Continue;
        public long ProgressSequence {get;private set;}
        private readonly Dictionary<long,Registered> arrivalOwnedRows=new Dictionary<long,Registered>();
        internal static Registered CreateArrivalProjection(NavigationRequest request,NavScheduledRequest scheduled,NavTypedRoute route,ArrivalProjection saved,ArrivalProviderLease lease,NavigationRoutePort port,string surface,
            int pointsCapacity=-1,int typedCapacity=-1,int observedCapacity=-1)
        {
            var row=new Registered{Request=request,Scheduled=scheduled,SurfaceProviderId=surface,Arrival=true,ArrivalOwnerPort=port,ArrivalOwner=lease,
                Solved=new NavScheduledCompletion(scheduled,NavSolveStatus.Ready,route,0,0),LegCursor=saved.Leg,ReplayCursor=saved.Replay,Expected=saved.Expected,At=saved.At,
                Points=new List<NavPoint>(pointsCapacity<0?saved.Points.Length:pointsCapacity),TypedPortals=new List<string>(typedCapacity<0?saved.Typed.Length:typedCapacity),ObservedPortals=new List<string>(observedCapacity<0?saved.Observed.Length:observedCapacity)};
            row.Points.AddRange(saved.Points);row.TypedPortals.AddRange(saved.Typed);row.ObservedPortals.AddRange(saved.Observed);
            lease.PointsCapacity=row.Points.Capacity;lease.TypedCapacity=row.TypedPortals.Capacity;lease.ObservedCapacity=row.ObservedPortals.Capacity;return row;
        }
        private void RetireArrivalOwners(NavigationRoutePort port,int budget)
        {
            for(int n=0;n<budget;n++){
                var lease=port.NextRetiringArrivalProjection();
                if(lease==null){if(port.RetireRestoredArrivalProjection()){ProgressSequence++;continue;}break;}
                if(lease.Attached){
                    if(!arrivalOwnedRows.TryGetValue(lease.Token,out var row))break;
                    registered.Remove(row.Scheduled.Subscription.StableKey);row.Solved?.Release();row.Solved=null;row.Scheduled=null;row.Points=null;row.TypedPortals=null;row.ObservedPortals=null;
                    ReleaseArrivalOwner(row,"retirement");ProgressSequence++;UpdateReservation();
                }else{if(!port.RetireRestoredArrivalProjection())break;ProgressSequence++;}
            }
        }
        private bool PhysicalHasWork {get{var root=currentPort?.PinCommittedHostRoot();try{return root!=null&&(root.Rows.Count!=0||root.Frontier.Stage!=PumpStage.Idle);}finally{root?.Release();}}}
        public bool HasWork => PhysicalHasWork || registered.Count!=0 || (currentPort?.QueuedRequestCount??0)!=0 || (currentPort?.AllocationCount??0)!=0 || (currentPort?.ArrivalReconstructing??false);
        private const int WorkAllotment = 16384;
        private const int WorkUnitsPerRequestBudget = 1024;
        private static readonly NavEpochQuota Quota = new NavEpochQuota(WorkAllotment, WorkAllotment,
            WorkAllotment, WorkAllotment * 3, 256);
        private static readonly NavGraphCompileLimits Limits = new NavGraphCompileLimits(1048576, 300000, 1200000);
        public NavSchedulerCounters Counters {get{var root=currentPort?.PinCommittedHostRoot();try{return root?.Scheduler.Counters??scheduler.Counters;}finally{root?.Release();}}}
        public long SolverEpoch => epoch;
        public string LastProjectionFailure { get; private set; }
        private long OwnedRetainedBytes=>scheduler.Counters.RetainedBytes+OwnedExternalBytes();
        private long ReservedRetainedBytes=>scheduler.Counters.RetainedBytes+ExternalBytes();
        public long RetainedBytes => currentPort?.PhysicalRetainedBytes??(OwnedRetainedBytes+(currentPort?.CorridorRetainedBytes??0));
        public double TransportWaitMilliseconds=>currentPort?.TransportWaitMilliseconds??0;
        public double TransportHoldMilliseconds=>currentPort?.TransportHoldMilliseconds??0;
        public double PreparationElapsedMilliseconds {get;private set;}
        public double ProviderElapsedMilliseconds {get;private set;}
        public double SolveElapsedMilliseconds {get;private set;}
        public double ProjectionElapsedMilliseconds {get;private set;}
        private enum Phase {Preparation,Provider,Solve,Projection}
        private readonly struct Timing : IDisposable
        {
            private readonly CertifiedRouteKernel owner;private readonly Phase phase;private readonly long at;
            internal Timing(CertifiedRouteKernel owner,Phase phase){this.owner=owner;this.phase=phase;at=Stopwatch.GetTimestamp();}
            public void Dispose(){double ms=(Stopwatch.GetTimestamp()-at)*1000d/Stopwatch.Frequency;
                switch(phase){case Phase.Preparation:owner.PreparationElapsedMilliseconds+=ms;break;
                    case Phase.Provider:owner.ProviderElapsedMilliseconds+=ms;break;
                    case Phase.Solve:owner.SolveElapsedMilliseconds+=ms;break;
                    case Phase.Projection:owner.ProjectionElapsedMilliseconds+=ms;break;}}
        }
        private Timing Measure(Phase phase)=>new Timing(this,phase);
        public long PeakRetainedBytes => peakRetainedBytes;
        private long OwnedExternalBytes()=>providerBytes-scheduler.ProviderRetainedBytes+
            heldProjectionBytes+hostAnswerBytes+128L*registered.Values.Count(r=>!r.Arrival);
        private long ExternalBytes()=>OwnedExternalBytes()+Math.Max(reservedCorridorBytes,currentPort?.CorridorRetainedBytes??0);
        private void UpdateReservation(long nextRegistration=0)
        {if(ExternalBytes()+nextRegistration>LayeredNavigationScheduler.MaxRetainedBytes)throw new InvalidOperationException("Route retention components owned="+OwnedExternalBytes()+" corridor="+(currentPort?.CorridorRetainedBytes??0)+" reserved="+reservedCorridorBytes+" ownsolver="+scheduler.Counters.RetainedBytes+" arrival="+currentPort?.ArrivalRetentionDiagnostic);scheduler.ReserveExternalBytes(ExternalBytes()+nextRegistration);
            peakRetainedBytes=Math.Max(peakRetainedBytes,RetainedBytes);
            if(!allocating)currentPort?.SetHostRetainedBytes(OwnedRetainedBytes);}
        public void ReleaseForWorldCheckpoint(NavigationRoutePort port)
        {
            if(port==null)throw new ArgumentNullException(nameof(port));
            if(currentPort!=null&&!ReferenceEquals(currentPort,port))
                throw new InvalidOperationException("Checkpoint host is bound to another session.");
            port.ReleaseHostForWorldCheckpoint(Clear);
            // ClearWorld releases owned caches/work but retains this scheduler's epoch
            // and remaining quotas. Rebinding must not manufacture a fresh allotment.
        }
        public void Clear()
        {
            var oldPort=currentPort;
            if(oldPort==null){ClearOwned();return;}
            if(allocating){var retained=oldPort.PhysicalRetainedBytes??OwnedRetainedBytes;allocating=false;reservedCorridorBytes=0;oldPort.EndHostAllocation(retained);}
            ClearOwned();oldPort.SetHostRetainedBytes(0);oldPort.DetachHost(this);
        }
        // Scheduler ClearWorld retains epoch and quota history across authority ports.
        private void ReleaseArrivalOwner(Registered row,string reason)
        {var owner=row.ArrivalOwner;var port=row.ArrivalOwnerPort;row.ArrivalOwner=null;row.ArrivalOwnerPort=null;if(owner!=null){arrivalOwnedRows.Remove(owner.Token);port.ReleaseArrivalProjection(owner,reason);}}
        private void ClearProjectionOwnership(Registered row,string reason)
        {
            void Clear(){row.Scheduled=null;row.Solved?.Release();row.Solved=null;row.Points=null;row.TypedPortals=null;row.ObservedPortals=null;ReleaseArrivalOwner(row,reason);}
            if(row.ArrivalOwnerPort!=null)row.ArrivalOwnerPort.RunHostStep(Clear);else Clear();
        }
        private void ClearOwned()
        {foreach(var row in registered.Values.OrderBy(r=>r.ArrivalOwner?.Retiring==true?0:1).ThenBy(r=>r.ArrivalOwner?.Retiring==true?r.ArrivalOwner.Retirement:r.ArrivalOwner?.Acquisition??long.MaxValue).ThenBy(r=>r.ArrivalOwner?.Token??long.MaxValue)){ClearProjectionOwnership(row,"Clear");}scheduler.ClearWorld();registered.Clear();arrivalOwnedRows.Clear();providers.Clear();providerBytes=heldProjectionBytes=hostAnswerBytes=peakRetainedBytes=0;scanQueue.Clear();projectionQueue.Clear();completionQueue.Clear();currentPort=null;lock(pumpFrameGate){pumpStage=PumpStage.Idle;pumpAllocationFloor=0;}}

        // The lane owner attaches before inspecting work. A restored host may
        // contain only a saved suffix and no ingress mailbox item to wake it.
        internal void Attach(NavigationRoutePort port)
        {
            if(port==null)throw new ArgumentNullException(nameof(port));
            if(ReferenceEquals(currentPort,port))return;
            Clear();port.AttachHost(this);currentPort=port;
        }

        private void PumpLegacy(PlayableRouteBinding binding, int budget)
        {
            var port = binding.RoutePort;
            if(port == null)throw new ArgumentException("Missing route authority facade.");
            Attach(port);
            lock(pumpFrameGate){pumpBudget=budget;scansUsed=transfersUsed=readyUsed=projectionUsed=solveWorkLimit=solveConsumed=0;
                projectionAllowance=checked(budget*16);pumpAllocationFloor=0;pumpStage=PumpStage.Retirement;}
            port.RunHostStep(()=>RetireArrivalOwners(port,budget));
            SetPumpStage(PumpStage.ArrivalAdvance);
            if(port.AdvanceArrivals(budget))ProgressSequence++;
            // Commit queued answers before reserving solver growth. No provider/solve/
            // projection operation executes under the authority gate.
            SetPumpStage(PumpStage.PreFlush);
            port.RunHostStep(()=>{UpdateReservation();Flush(port,budget);});
            SetPumpStage(PumpStage.Allocation);
            reservedCorridorBytes=BeginAllocation(port);allocating=true;
            lock(pumpFrameGate)pumpAllocationFloor=reservedCorridorBytes;
            try {ServiceOwned(port,budget);}
            finally {
                SetPumpStage(PumpStage.EndAllocation);
                try{port.EndHostAllocation(OwnedRetainedBytes);}
                finally{allocating=false;reservedCorridorBytes=0;}
            }
            SetPumpStage(PumpStage.PostFlush);
            if(Continue?.Invoke()!=false)port.RunHostStep(()=>{UpdateReservation();Flush(port,budget);});
            SetPumpStage(PumpStage.Idle);
        }

        private void ServiceOwned(NavigationRoutePort port,int budget)
        {
            UpdateReservation();
            using var admissionPin=port.PinAdmission();
            var admission=admissionPin.Value;
            if(admission==null)return;
            // Old accepted inputs are kept until their answer can enter the
            // mailbox. Discharge stale ownership before asking the solver to work.
            int scans=Math.Min(budget,scanQueue.Count);
            SetPumpStage(PumpStage.Scan);
            for(int i=0;i<scans;i++) {
                lock(pumpFrameGate)scansUsed=i;
                var key=scanQueue.Dequeue();
                if(!registered.TryGetValue(key,out var row))continue;
                if(port.ContainsCurrent(row.Request)){scanQueue.Enqueue(key);continue;}
                scheduler.Cancel(key);
                SetCompletion(key,row,new NavigationAnswer(row.Request,Array.Empty<NavPoint>(),NavSolveStatus.Stale));
            }
            lock(pumpFrameGate){scansUsed=scans;pumpStage=PumpStage.Transfer;}
            for(int i=0;i<budget && Continue?.Invoke()!=false && registered.Count<LayeredNavigationScheduler.MaxQueue;i++) {
                lock(pumpFrameGate)transfersUsed=i;
                if(ReservedRetainedBytes+128>LayeredNavigationScheduler.MaxRetainedBytes)break;
                UpdateReservation(128);
                if(port.TryDropStale(out _)){ProgressSequence++;continue;}
                if(port.TryTransferArrival(out var arrival,out var arrivalScheduled,out var arrivalRoute,out var restoredProjection,out var arrivalOwner)) {
                    var arrivalKey=arrivalScheduled.Subscription.StableKey;

                    var row=arrivalOwner.Projection;
                    arrivalOwnedRows.Add(arrivalOwner.Token,row);
                    registered.Add(arrivalKey,row);scanQueue.Enqueue(arrivalKey);projectionQueue.Enqueue(arrivalKey);
                    ProgressSequence++;UpdateReservation();continue;
                }
                if(port.QueuedArrivalHead)break;
                NavScheduledRequest prepared=null;
                NavigationRequest preparedInput=null;
                bool oversized=false;
                bool accepted;
                try{accepted=port.TryTransfer(scheduler,(r,observed)=>{
                        preparedInput=r;return prepared=Prepare(observed,r,out oversized);},out var transferredRequest);
                    if(!accepted){if(preparedInput!=null)DropUnusedProvider(preparedInput,prepared?.Provider);
                        if(oversized&&port.TryRejectUnserviceable(preparedInput,out _)){
                            UpdateReservation();continue;
                        }
                        UpdateReservation();break;}
                    var key=prepared.Subscription.StableKey;
                    registered.Add(key,new Registered{Request=transferredRequest,Scheduled=prepared,
                        SurfaceProviderId=admission.SurfaceProviderId});
                    ProgressSequence++;RetainProvider(transferredRequest,prepared.Provider);scanQueue.Enqueue(key);UpdateReservation();
                }catch{if(preparedInput!=null)DropUnusedProvider(preparedInput,prepared?.Provider);
                    UpdateReservation();throw;}
            }
            SetPumpStage(PumpStage.Solve);
            UpdateReservation();
            int workLimit=checked(budget*WorkUnitsPerRequestBudget);lock(pumpFrameGate)solveWorkLimit=workLimit;
            bool Current(NavScheduledIdentity id)
            {
                using var latestPin=port.PinAdmission();
                var latest=latestPin.Value;
                return latest!=null&&registered.TryGetValue(id.StableKey,out var row)&&latest.Contains(row.Request);
            }
            int consumed;
            using(Measure(Phase.Solve)){consumed=scheduler.Advance(epoch,Quota,Current,workLimit);
            if(consumed==0&&scheduler.NeedsNextEpoch) {
                epoch++;
                consumed=scheduler.Advance(epoch,Quota,Current,workLimit);
            }
            }
            if(consumed>0)ProgressSequence++;
            lock(pumpFrameGate){solveConsumed=consumed;pumpStage=PumpStage.Ready;}
            for(int i=0;i<budget&&scheduler.ReadyCount>0;i++) {
                lock(pumpFrameGate)readyUsed=i;
                if(ReservedRetainedBytes+scheduler.ReadyHeadProjectionReserve>LayeredNavigationScheduler.MaxRetainedBytes)break;
                if(!scheduler.TryTake(_=>true,out var completed))continue;
                ProgressSequence++; // Removing even a stale ready item releases capacity.
                if(!registered.TryGetValue(completed.Identity.StableKey,out var row)){completed.Release();continue;}
                if(!port.ContainsCurrent(row.Request))
                    SetCompletion(completed.Identity.StableKey,row,new NavigationAnswer(row.Request,Array.Empty<NavPoint>(),NavSolveStatus.Stale));
                else if(completed.Status!=NavSolveStatus.Ready)
                    SetCompletion(completed.Identity.StableKey,row,new NavigationAnswer(row.Request,Array.Empty<NavPoint>(),completed.Status));
                else {row.Solved=completed;heldProjectionBytes+=1024L+288L*completed.Route.Legs.Count;
                    projectionQueue.Enqueue(completed.Identity.StableKey);UpdateReservation();}
                if(!ReferenceEquals(row.Solved,completed))completed.Release();
            }
            SetPumpStage(PumpStage.Projection);
            Project(projectionAllowance);
        }

        private void SetCompletion(string key,Registered row,NavigationAnswer answer)
        {long released=row.Solved==null||row.Arrival?0:1024L+288L*row.Solved.Route.Legs.Count;
            long prior=row.Completion==null?0:AnswerBytes(row.Completion);
            if(answer.Corridor!=null&&ReservedRetainedBytes-released-prior+AnswerBytes(answer)>
                LayeredNavigationScheduler.MaxRetainedBytes)
                answer=new NavigationAnswer(row.Request,Array.Empty<NavPoint>(),NavSolveStatus.CapacityExceeded);
            if(row.Completion==null)completionQueue.Enqueue(key);
            else hostAnswerBytes-=prior;
            row.Completion=answer;hostAnswerBytes+=AnswerBytes(answer);
            if(row.Solved!=null&&!row.Arrival)heldProjectionBytes-=1024L+288L*row.Solved.Route.Legs.Count;
            if(row.Scheduled!=null){if(!row.Arrival)ReleaseProvider(row.Request,row.Scheduled.Provider);}
            ClearProjectionOwnership(row,"completion");UpdateReservation();}

        private static long AnswerBytes(NavigationAnswer answer)
            =>checked(1024L+16L*answer.RouteLength+(answer.Corridor?.EstimatedRetainedBytes??0));

        private void Flush(NavigationRoutePort port,int budget)
        {
            for(int i=0;i<budget&&completionQueue.Count>0;i++) {
                var key=completionQueue.Peek();
                if(!registered.TryGetValue(key,out var row)){completionQueue.Dequeue();continue;}
                if(!port.TryComplete(row.Completion))break; // answer mailbox backpressure
                completionQueue.Dequeue();registered.Remove(key);
                hostAnswerBytes-=AnswerBytes(row.Completion);ProgressSequence++;UpdateReservation();
            }
        }

        private static Tuple<NavGeometry,string> ProviderKey(NavigationRequest request)
            =>Tuple.Create(request.Geometry,request.HeldIdentity);
        private void RetainProvider(NavigationRequest request,ILayeredNavigationProvider provider)
        {var entry=providers[ProviderKey(request)];if(!ReferenceEquals(entry.Provider,provider))throw new InvalidOperationException("Provider reference changed at admission.");entry.References++;}
        private void ReleaseProvider(NavigationRequest request,ILayeredNavigationProvider provider)
        {var key=ProviderKey(request);var entry=providers[key];if(!ReferenceEquals(entry.Provider,provider)||entry.References<1)throw new InvalidOperationException("Unbalanced provider ownership.");
            if(--entry.References==0){providers.Remove(key);providerBytes-=entry.Provider.EstimatedRetainedBytes;}}
        private void DropUnusedProvider(NavigationRequest request,ILayeredNavigationProvider provider)
        {var key=ProviderKey(request);if(providers.TryGetValue(key,out var entry)&&entry.References==0&&
            (provider==null||ReferenceEquals(entry.Provider,provider))){providers.Remove(key);providerBytes-=entry.Provider.EstimatedRetainedBytes;}}

        private NavScheduledRequest Prepare(NavigationAdmission admission,NavigationRequest request,out bool oversized)
        {using(Measure(Phase.Preparation))return PrepareCore(admission,request,out oversized);}
        private NavScheduledRequest PrepareCore(NavigationAdmission admission,NavigationRequest request,out bool oversized)
        {
            oversized=false;
            BeforePrepare?.Invoke();
            if(Continue?.Invoke()==false)return null;
            if(!admission.Contains(request))throw new InvalidOperationException("Unpublished route input.");
            ILayeredNavigationProvider provider;
            using(Measure(Phase.Provider))provider=Provider(admission,request,out oversized);
            if(provider==null)return null; // bounded backpressure, original FIFO head stays queued
            var scheduled=BuildScheduled(admission,request,provider);
            // Submit will take over the scheduler's first reference. Exclude the
            // same provider from the external reservation for that atomic handoff.
            long transferring=scheduler.HasProviderReference(provider)?0:provider.EstimatedRetainedBytes;
            scheduler.ReserveExternalBytes(ExternalBytes()+128-transferring);
            long admissionBytes=4096L+(scheduled.Region==null?0:128L*scheduled.Region.Candidates.Count)+
                transferring+(scheduler.PendingCount==0?2048:0);
            if(!scheduler.TryMakeRoomForExternal(admissionBytes,1))return null;
            return scheduled;
        }

        // The physical registration caller prepares the same certified recipe
        // outside transportGate, then publishes the owned scheduler/row/queue
        // and the mailbox transfer at one Session boundary. Pump switches to
        // this caller only when its solve/projection/flush stages use this root.
        // Ordinary registration and solve own a private-fork refusal boundary.
        // Preserve allocator/ledger/scratch identity so the fork can unwind all
        // births, while invariant exceptions still escape to the worker.
        private sealed class OrdinaryPreparationReservation : INavStorageReservation,
            INavStorageScratchAllocator,INavStorageLedgerIdentity,INavStorageLocalBirthAllocator,INavStoragePreparationOwner
        {
            public object PreparationOwner=>scope;
            internal const long Bytes=64;
            internal sealed class Refused : Exception { }
            private readonly NavigationRetentionBudget.Scope scope;
            internal OrdinaryPreparationReservation(NavigationRetentionBudget.Scope scope){this.scope=scope;}
            public bool TryReserve(long bytes)
            {if(!scope.TryReserve(bytes))throw new Refused();return true;}
            public void Register(NavOwnedStorage storage)=>((INavStorageAllocator)scope).Register(storage);
            public bool TryRegisterLocalBirth(NavOwnedStorage storage)=>scope.TryRegisterLocalBirth(storage);
            public object Identity=>((INavStorageLedgerIdentity)scope).Identity;
            public bool Owns(NavOwnedStorage storage)=>((INavStorageLedgerIdentity)scope).Owns(storage);
            public IDisposable ClaimScratch(long bytes)=>((INavStorageScratchAllocator)scope).ClaimScratch(bytes);
        }
        internal bool TryRegisterPhysicalOrdinary(PlayableRouteBinding binding)
        {
            var port=binding.RoutePort;
            if(port==null)throw new ArgumentException("Missing route authority facade.");
            Attach(port);
            if(!port.TryObservePhysicalIngress(out var ingress,out var admissionPin))return false;
            using var observedPin=admissionPin;var observed=observedPin.Value;
            var source=port.PinCommittedHostRoot();
            NavigationRetentionBudget.Scope scope=null;
            NavigationHostRoot candidate=null;
            try{
                if(!port.TryOpenPhysicalHostScope(NavigationPreparationKind.RouteAdmission,epoch,
                    observed.PublicationIdentity,out scope))return false;
                if(!scope.TryReserve(OrdinaryPreparationReservation.Bytes))return false;
                var reservation=new OrdinaryPreparationReservation(scope);
                candidate=source==null?port.CreatePhysicalHostRoot(reservation):
                    source.ForkOwned(reservation);
                if(source==null)port.StageRetainedPhysicalArrivals(candidate,reservation);
                BeforePrepare?.Invoke();
                if(Continue?.Invoke()==false)return false;
                // Account for the temporary provider while its physical copy
                // is admitted. The authored arrangement bound is the same
                // conservative construction reservation used by arrivals.
                long construction=ProspectiveProviderConstruction(observed,ingress.Geometry);
                NavNameKey key;
                if(!NavStorageScratch.TryReserve(reservation,construction,out var constructionLease))return false;
                using(constructionLease){
                    ILayeredNavigationProvider provider=observed.AuthoredTerrain==null?
                        NavigationAdmission.CertifiedFlatProvider(ingress.Geometry):
                        new PartitionNavigationProvider(observed.AuthoredTerrain,
                            ingress.Geometry,ingress.HeldIdentity);
                    if(provider.EstimatedRetainedBytes>construction)
                        throw new InvalidOperationException("Physical provider exceeded construction reserve.");
                    var prepared=BuildScheduled(observed,ingress,provider);
                    key=candidate.RegisterOrdinary(ingress,prepared,observed.SurfaceProviderId,reservation,observed.Geometry);
                }
                var registrationFrame=CapturePumpFrontier();
                candidate.SetFrontier(WithCounters(registrationFrame,transfers:registrationFrame.TransfersUsed+1),epoch,checked(ProgressSequence+1));
                candidate.SetOperation(new NavigationHostRoot.OperationDescriptor(
                    PumpStage.Transfer,transfersUsed,true,false,key.Reference));
                candidate.SealPrepared();
                port.PublishRegisteredPhysicalHost(scope,source,candidate,observed,ingress,key);
                ProgressSequence++;return true;
            }catch(NavigationSession.PhysicalIngressRefused){return false;}
            catch(PartitionNavigationProvider.CopyScratchRefused){return false;}
            catch(OrdinaryPreparationReservation.Refused){return false;}
            finally{
                candidate?.ReleaseStorage();
                scope?.Discard();
                source?.Release();
            }
        }

        internal int AdvancePhysicalOrdinary(PlayableRouteBinding binding,int workLimit)
        {
            using var measured=PerformanceProfile.Measure(ProfileStage.PhysicalSolve);
            var port=binding.RoutePort;
            if(port==null||workLimit<0)throw new ArgumentException("Invalid physical solver allotment.");
            Attach(port);
            var source=port.PinCommittedHostRoot();
            if(source==null)return 0;
            NavigationRetentionBudget.Scope scope=null;
            NavigationHostRoot candidate=null;
            try{
                if(!port.TryOpenPhysicalHostScope(NavigationPreparationKind.Solve,epoch,
                    port.Admission.PublicationIdentity,out scope))return 0;
                if(!scope.TryReserve(OrdinaryPreparationReservation.Bytes))return 0;
                var reservation=new OrdinaryPreparationReservation(scope);
                candidate=source.ForkOwned(reservation);
                bool Current(NavScheduledIdentity id)
                {
                    // The paid host scan owns ordinary admission cancellation.
                    // Cancelling here can drop the scheduler owner between scan
                    // and solve while leaving its live host row behind.
                    return candidate.Rows.TryGetValue(id.StableKey,out var entry)&&
                        entry is NavigationHostRow row&&row.Answer==null&&row.Solved==null;
                }
                long nextEpoch=source.Epoch;
                int consumed;
                using(PerformanceProfile.Measure(ProfileStage.SchedulerAdvance)){
                    consumed=candidate.Scheduler.Advance(nextEpoch,Quota,Current,workLimit);
                    if(consumed==0&&candidate.Scheduler.NeedsNextEpoch){
                        nextEpoch++;
                        consumed=candidate.Scheduler.Advance(nextEpoch,Quota,Current,workLimit);
                    }
                }
                PerformanceProfile.Add(ProfileWork.OrdinaryConsumed,consumed);
                if(consumed==0&&nextEpoch==source.Epoch)return 0;
                var frame=CapturePumpFrontier();
                bool inPump=frame.Stage==PumpStage.Solve&&frame.OriginalBudget>0;
                int budget=inPump?frame.OriginalBudget:
                    Math.Max(1,checked((workLimit+1023)/1024));
                var solveFrame=inPump?WithCounters(frame,PumpStage.Ready,
                    consumed:checked(frame.SolveConsumed+consumed)):
                    new PumpFrontier(PumpStage.Ready,budget,0,0,0,0,checked(budget*16),workLimit,consumed,source.Frontier.AllocationFloor);
                candidate.SetFrontier(solveFrame,nextEpoch,checked(source.Progress+(consumed>0?1:0)));
                candidate.SetOperation(new NavigationHostRoot.OperationDescriptor(
                    PumpStage.Solve,0,true,consumed>0,default));
                candidate.SealPrepared();scope.Attach(candidate);
                port.PublishPhysicalHostRoot(scope,source,candidate);
                epoch=nextEpoch;ProgressSequence=checked(source.Progress+(consumed>0?1:0));return consumed;
            }catch(OrdinaryPreparationReservation.Refused){
                // Discard the entire tentative compiler/scheduler prefix: its
                // cursor and paid quota may already have advanced before a
                // trace scratch reservation refused. Nothing was published.
                return 0;
            }finally{
                candidate?.ReleaseStorage();scope?.Discard();source.Release();
            }
        }

        internal int TransferPhysicalReady(PlayableRouteBinding binding,int limit)
        {
            var port=binding.RoutePort;
            if(port==null||limit<0)throw new ArgumentException("Invalid physical Ready allotment.");
            Attach(port);
            var source=port.PinCommittedHostRoot();
            if(source==null)return 0;
            NavigationRetentionBudget.Scope scope=null;
            NavigationHostRoot candidate=null;
            try{
                if(source.Scheduler.ReadyCount==0||limit==0||
                    !port.TryOpenPhysicalHostScope(NavigationPreparationKind.Projection,
                        source.Epoch,port.Admission.PublicationIdentity,out scope))return 0;
                var reservation=scope.CapacityReservationOwned();
                candidate=source.ForkOwned(reservation);
                int moved=0;
                while(moved<limit&&candidate.Scheduler.ReadyCount>0&&
                    candidate.StageTakeReady(reservation))moved++;
                if(moved==0)return 0;
                var frame=CapturePumpFrontier();
                bool inPump=frame.Stage==PumpStage.Ready&&frame.OriginalBudget>0;
                int budget=inPump?frame.OriginalBudget:Math.Max(1,limit);
                candidate.SetFrontier(inPump?WithCounters(frame,PumpStage.Projection,ready:checked(frame.ReadyUsed+moved)):
                    new PumpFrontier(PumpStage.Projection,budget,0,0,moved,0,checked(budget*16),0,0,source.Frontier.AllocationFloor),
                    source.Epoch,checked(source.Progress+moved));
                candidate.SetOperation(new NavigationHostRoot.OperationDescriptor(
                    PumpStage.Ready,moved,true,true,default));
                candidate.SealPrepared();scope.Attach(candidate);
                port.PublishPhysicalHostRoot(scope,source,candidate);
                ProgressSequence=checked(ProgressSequence+moved);return moved;
            }catch(NavigationRetentionBudget.CapacityRefused){return 0;}finally{
                candidate?.ReleaseStorage();scope?.Discard();source.Release();
            }
        }

        internal int ProjectPhysicalOrdinary(PlayableRouteBinding binding,int allowance)
        {
            using var measured=PerformanceProfile.Measure(ProfileStage.PhysicalProjection);
            var port=binding.RoutePort;
            if(port==null||allowance<0)throw new ArgumentException("Invalid physical projection allotment.");
            Attach(port);
            var source=port.PinCommittedHostRoot();
            if(source==null)return 0;
            NavigationRetentionBudget.Scope scope=null;
            NavigationHostRoot candidate=null;
            try{
                if(source.Projection.Count==0||allowance==0||
                    !port.TryOpenPhysicalHostScope(NavigationPreparationKind.Projection,
                        source.Epoch,port.Admission.PublicationIdentity,out scope))return 0;
                var reservation=scope.CapacityReservationOwned();
                candidate=source.ForkOwned(reservation);
                var key=candidate.Projection.First;
                if(!StageNextPhysicalProjection(candidate,reservation,out var failure))return 0;
                if(failure!=null)LastProjectionFailure=failure;
                var frame=CapturePumpFrontier();
                bool inPump=frame.Stage==PumpStage.Projection&&frame.OriginalBudget>0;
                int budget=inPump?frame.OriginalBudget:
                    Math.Max(1,checked((allowance+15)/16));
                candidate.SetFrontier(new PumpFrontier(PumpStage.Projection,budget,
                    inPump?frame.ScansUsed:0,inPump?frame.TransfersUsed:0,
                    inPump?frame.ReadyUsed:0,inPump?frame.ProjectionUsed+1:1,
                    checked(budget*16),
                    inPump?frame.SolveWorkLimit:0,inPump?frame.SolveConsumed:0,
                    inPump?frame.AllocationFloor:source.Frontier.AllocationFloor,
                    inPump?frame.PhaseUsed:0,inPump?frame.ScanAllowance:0),
                    source.Epoch,checked(source.Progress+1));
                candidate.SetOperation(new NavigationHostRoot.OperationDescriptor(
                    PumpStage.Projection,inPump?frame.ProjectionUsed:0,true,true,key.Reference));
                candidate.SealPrepared();scope.Attach(candidate);
                port.PublishPhysicalHostRoot(scope,source,candidate);
                ProgressSequence++;return 1;
            }catch(NavigationRetentionBudget.CapacityRefused){return 0;}finally{
                candidate?.ReleaseStorage();scope?.Discard();source.Release();
            }
        }

        // The same paid projection action is used by the ordinary host and
        // the source-runner qualification; no synchronous solver substitute.
        internal static bool StageNextPhysicalProjection(NavigationHostRoot candidate,
            INavStorageReservation reservation,out string failure)
        {
            failure=null;
                var key=candidate.Projection.First;
                if(candidate.Rows.TryGetValue(key,out var entry)&&
                    entry is NavigationHostRow row&&row.Answer==null&&row.Solved?.Route!=null){
                    var route=row.Solved.Route;var state=row.ProjectionOwner;
                    if(state==null)throw new InvalidOperationException("Physical Ready row has no projection cursor.");
                    if(state.LegCursor<route.Legs.Count){
                        var leg=route.Legs[state.LegCursor];
                        bool valid=leg.From.Equals(state.Expected);
                        NavNameReference portalName=default;
                        if(valid&&leg.Kind==NavTypedLegKind.Surface)
                            valid=row.Provider is PartitionNavigationProvider?
                                row.Provider.IsValid(leg.From,row.Scheduled.Clearance)&&
                                row.Provider.IsValid(leg.To,row.Scheduled.Clearance)&&
                                row.Request.Geometry.SegmentFree(leg.From.Position,leg.To.Position,
                                    row.Scheduled.Clearance.Radius):
                                row.Provider.CanSweep(leg.From,leg.To,row.Scheduled.Clearance);
                        else if(valid&&leg.Kind==NavTypedLegKind.Portal&&
                            row.Provider is PartitionNavigationProvider partition){
                            var seam=partition.Seams.FirstOrDefault(s=>s.Allowed&&s.Id==leg.PortalId&&
                                ((s.FromSurface==leg.From.SurfaceId&&s.ToSurface==leg.To.SurfaceId)||
                                 (s.ToSurface==leg.From.SurfaceId&&s.FromSurface==leg.To.SurfaceId))&&
                                OnSegment(leg.From.Position,s.From,s.To));
                            valid=leg.From.Position.Equals(leg.To.Position)&&seam!=null;
                            if(valid){leg.RequireCanonicalProviderBinding(row.Provider,reservation);portalName=leg.CanonicalPortalName(row.Provider);}
                        }else valid=false;
                        if(valid){
                            candidate.StageProjectionTransition(key,NavigationHostRow.ProjectionTransition.Leg,
                                portalName,default,null,reservation);
                        }else{
                            failure="physical leg qualification";
                            candidate.StageProjectionFailure(key,reservation);
                        }
                    }else if(row.Provider is PartitionNavigationProvider actual&&
                        state.ReplayCursor<state.PointCount){
                        using(NavStorageScratch.Reserve(reservation,checked(512L+
                            actual.TraceScratchBound+8L*actual.TracePortalBound))){
                            if(actual.TryTrace(state.At,state.Point(state.ReplayCursor),
                                row.Scheduled.Clearance,out var reached,out var trace)){
                                var names=trace.Where(leg=>leg.Kind==NavTypedLegKind.Portal)
                                    .Select(leg=>PhysicalSeamName(actual,leg.PortalId)).ToArray();
                                candidate.StageProjectionTransition(key,NavigationHostRow.ProjectionTransition.Replay,
                                    default,reached,names,reservation);
                            }else{
                                failure="physical XZ replay trace";
                                candidate.StageReplayFailure(key,reservation);
                            }
                        }
                    }else if(state.LegCursor==route.Legs.Count&&state.PointCount>0&&
                        state.Expected.Equals(row.Scheduled.Subscription.AssignedEndpoint)&&
                        state.Point(state.PointCount-1).Equals(state.Expected.Position)&&
                        (!(row.Provider is PartitionNavigationProvider)||
                            state.ReplayCursor==state.PointCount&&state.At.Equals(state.Expected)&&
                            state.PortalsMatch()))candidate.StageReady(key,reservation);
                    else{
                        failure="physical XZ replay sequence/end";
                        candidate.StageReplayFailure(key,reservation);
                    }
                }else if(!candidate.SkipCompletedProjection(reservation))return false;
            return true;
        }

        internal bool FlushPhysicalOrdinary(PlayableRouteBinding binding)
        {
            var port=binding.RoutePort;
            if(port==null)throw new ArgumentException("Missing route authority facade.");
            Attach(port);
            var source=port.PinCommittedHostRoot();
            if(source==null)return false;
            NavigationRetentionBudget.Scope scope=null;
            NavigationHostRoot candidate=null;
            try{
                if(source.Completion.Count==0||
                    !port.TryOpenPhysicalHostScope(NavigationPreparationKind.Projection,
                        source.Epoch,port.Admission.PublicationIdentity,out scope))return false;
                var reservation=scope.CapacityReservationOwned();
                candidate=source.ForkOwned(reservation);
                var key=candidate.Completion.First;
                bool tombstone=!candidate.Rows.ContainsKey(key);
                if(tombstone)candidate.StageDiscardCompletionTombstone(reservation);else candidate.StageFlushCompletion(key,reservation);
                var frame=CapturePumpFrontier();
                bool inPump=frame.Stage==PumpStage.PreFlush||frame.Stage==PumpStage.PostFlush;
                int budget=inPump&&frame.OriginalBudget>0?frame.OriginalBudget:1;
                candidate.SetFrontier(new PumpFrontier(inPump?frame.Stage:PumpStage.Idle,
                    budget,inPump?frame.ScansUsed:0,inPump?frame.TransfersUsed:0,
                    inPump?frame.ReadyUsed:0,inPump?frame.ProjectionUsed:0,
                    checked(budget*16),inPump?frame.SolveWorkLimit:0,
                    inPump?frame.SolveConsumed:0,
                    inPump?frame.AllocationFloor:source.Frontier.AllocationFloor,
                    inPump?frame.PhaseUsed+1:0,inPump?frame.ScanAllowance:0),
                    source.Epoch,checked(source.Progress+(tombstone?0:1)));
                candidate.SetOperation(new NavigationHostRoot.OperationDescriptor(
                    inPump?frame.Stage:PumpStage.PostFlush,0,true,!tombstone,key.Reference));
                candidate.SealPrepared();
                if(tombstone){scope.Attach(candidate);port.PublishPhysicalHostRoot(scope,source,candidate);}else if(!port.PublishCompletedPhysicalHost(scope,source,candidate,key))return false;
                if(!tombstone)ProgressSequence++;return true;
            }catch(NavigationRetentionBudget.CapacityRefused){return false;}finally{
                candidate?.ReleaseStorage();scope?.Discard();source.Release();
            }
        }

        internal bool RetirePhysicalDelivered(PlayableRouteBinding binding)
        {
            var port=binding.RoutePort;
            if(port==null)throw new ArgumentException("Missing route authority facade.");
            Attach(port);
            var source=port.PinCommittedHostRoot();
            if(source==null)return false;
            NavigationRetentionBudget.Scope scope=null;
            NavigationHostRoot candidate=null;
            try{
                NavNameKey key=default;
                bool found=false;
                foreach(var entry in source.Delivered){
                    if(entry.Value is NavigationHostRow row&&
                        port.CanRetireDeliveredPhysicalAnswer(row.Answer)){
                        key=entry.Key;found=true;break;
                    }
                }
                if(!found||!port.TryOpenPhysicalHostScope(NavigationPreparationKind.Projection,
                    source.Epoch,port.Admission.PublicationIdentity,out scope))return false;
                var reservation=scope.CapacityReservationOwned();
                candidate=source.ForkOwned(reservation);
                candidate.StageRetireDelivered(key,reservation);
                candidate.SetFrontier(source.Frontier,source.Epoch,checked(source.Progress+1));
                candidate.SetOperation(new NavigationHostRoot.OperationDescriptor(
                    PumpStage.Retirement,0,true,true,key.Reference));
                candidate.SealPrepared();scope.Attach(candidate);
                port.PublishPhysicalHostRoot(scope,source,candidate);
                ProgressSequence++;return true;
            }catch(NavigationRetentionBudget.CapacityRefused){return false;}finally{
                candidate?.ReleaseStorage();scope?.Discard();source.Release();
            }
        }

        private static NavNameReference PhysicalSeamName(PartitionNavigationProvider provider,
            string portalId)
        {
            var seam=provider.Seams.FirstOrDefault(value=>value.Id==portalId);
            if(seam?.StorageRoot==null||!ReferenceEquals(seam.Id,portalId))
                throw new ArgumentException("Physical replay portal is not the provider seam owner.");
            return new NavNameReference(seam.StorageRoot,NavNameRole.SeamId);
        }

        private static long ProspectiveProviderConstruction(NavigationAdmission admission,NavGeometry geometry)
            =>NavigationProviderConstructionBound.Estimate(admission.AuthoredTerrain,geometry);

        private static NavScheduledRequest BuildScheduled(NavigationAdmission admission,
            NavigationRequest request,ILayeredNavigationProvider provider)
        {
            var identity=request.MemberIdentity;
            NavigationAdmissionGroup group=null;
            if(identity!=null&&identity.GroupId!=0)
                group=admission.Groups.FirstOrDefault(g=>g.GroupId==identity.GroupId);
            NavLocation origin=request.StartLocation??Locate(provider,request.Start,request.Profile.Radius);
            NavLocation endpoint=request.GoalLocation??Locate(provider,request.Goal,request.Profile.Radius);
            var subscription=new NavRouteSubscription(request.Session,identity?.GroupId??0,
                group?.RootOrderRevision??0,group?.CommandSequence??0,group?.IssuedTick??0,
                identity?.OrderRevision??0,request.Entity,identity?.Incarnation??0,
                identity?.OrderRevision??request.Order,identity?.MobilityRevision??0,request.Request,
                origin,endpoint,request.HeldIdentity,
                request.Entity<0?NavRouteProvenance.ProducerProbe:group==null?
                    NavRouteProvenance.LegacySingleton:NavRouteProvenance.CommandMember);
            var clearance=new NavClearanceProfile("ground",1,"ground",(int)(identity?.MobilityRevision??0),request.Profile.Radius);
            var profile=new NavGraphProfile(NavigationAdmission.ExactProfileBinding(request.Profile),"distance",1,request.Profile.GridCell);
            NavScheduledRequest scheduled;
            if(group!=null&&group.Members.Count>1) {
                var terminal=group.OriginalTerminal??Locate(provider,group.OriginalGoal,request.Profile.Radius);
                if(provider.IsValid(terminal,clearance)) {
                    var region=new NavTerminalRegion("accepted-terminal",1,terminal,new[]{terminal});
                    scheduled=new NavScheduledRequest(provider,clearance,profile,Limits,region,subscription,1048576);
                }else scheduled=NavScheduledRequest.ForSingleton(provider,clearance,profile,Limits,subscription,1048576);
            }else scheduled=NavScheduledRequest.ForSingleton(provider,clearance,profile,Limits,subscription,1048576);
            return scheduled;
        }

        private static NavLocation Locate(ILayeredNavigationProvider provider,NavPoint point,double radius)
        {
            var clearance=new NavClearanceProfile("ground",1,"ground",0,radius);
            if(provider is PartitionNavigationProvider partition&&partition.TryLocate(point,clearance,out var found))return found;
            if(provider is LayeredNavigationProvider&&provider.IsValid(new NavLocation(point,NavLocation.FlatSurface),clearance))
                return new NavLocation(point,NavLocation.FlatSurface);
            // Preserve the exact point in a typed invalid-endpoint proposal.
            return new NavLocation(point,NavLocation.FlatSurface);
        }

        private ILayeredNavigationProvider Provider(NavigationAdmission admission,NavigationRequest request,out bool oversized)
        {
            oversized=false;
            var terrain=admission.AuthoredTerrain;
            var key=ProviderKey(request);
            if(providers.TryGetValue(key,out var existing))return existing.Provider;
            ILayeredNavigationProvider provider;
            if(terrain!=null)provider=new PartitionNavigationProvider(terrain,request.Geometry,request.HeldIdentity);
            else if(admission.SurfaceProviderId==NavLocation.FlatSurface)provider=NavigationAdmission.CertifiedFlatProvider(request.Geometry);
            else throw new InvalidOperationException("Unsupported authored route provider.");
            if(provider.EstimatedRetainedBytes>LayeredNavigationScheduler.MaxRetainedBytes-4224){oversized=true;return null;}
            if(!scheduler.TryMakeRoomForExternal(provider.EstimatedRetainedBytes+4224,1))return null;
            providers.Add(key,new ProviderEntry{Provider=provider});providerBytes+=provider.EstimatedRetainedBytes;
            UpdateReservation(128);
            return provider;
        }

        private void Project(int work)
        {using(Measure(Phase.Projection))ProjectCore(work);}
        private void ProjectCore(int work)
        {
            for(int used=0;used<work&&Continue?.Invoke()!=false&&projectionQueue.Count>0;used++) {
                lock(pumpFrameGate)projectionUsed=used;
                ProgressSequence++;
                var key=projectionQueue.Peek();
                if(!registered.TryGetValue(key,out var row)||row.Completion!=null||row.Solved==null){projectionQueue.Dequeue();continue;}
                var request=row.Scheduled;var route=row.Solved.Route;
                if(route==null||route.Subscription.StableKey!=key||route.Legs.Count==0){Fail("route identity/legs");continue;}
                if(row.Points==null){row.Points=new List<NavPoint>();row.TypedPortals=new List<string>();
                    row.ObservedPortals=new List<string>();row.Expected=request.Subscription.Origin;row.At=row.Expected;}
                if(row.Arrival&&(row.LegCursor<route.Legs.Count||row.ReplayCursor<row.Points.Count&&request.Provider is PartitionNavigationProvider)){
                    if(!ProjectArrivalStep(row,out var reason)){if(reason!=null)Fail(reason);else break;}continue;
                }
                if(row.LegCursor<route.Legs.Count){
                    var leg=route.Legs[row.LegCursor];
                    if(row.Arrival){
                        long growth=leg.Kind==NavTypedLegKind.Surface?ListGrowth(row.Points.Count,row.Points.Capacity,1,16):ListGrowth(row.TypedPortals.Count,row.TypedPortals.Capacity,1,8);
                        if(ReservedRetainedBytes+growth>LayeredNavigationScheduler.MaxRetainedBytes)break;
                    }
                    row.LegCursor++;
                    if(!leg.From.Equals(row.Expected)){Fail("leg continuity");continue;}
                    if(leg.Kind==NavTypedLegKind.Surface){
                        bool qualified=request.Provider is PartitionNavigationProvider
                            ? request.Provider.IsValid(leg.From,request.Clearance)&&
                                request.Provider.IsValid(leg.To,request.Clearance)&&
                                row.Request.Geometry.SegmentFree(leg.From.Position,leg.To.Position,request.Clearance.Radius)
                            : request.Provider.CanSweep(leg.From,leg.To,request.Clearance);
                        if(!qualified){Fail("surface qualification");continue;}
                        row.Points.Add(leg.To.Position);
                    }else if(request.Provider is PartitionNavigationProvider partition){
                        if(!leg.From.Position.Equals(leg.To.Position)||
                            !partition.Seams.Any(s=>s.Allowed&&s.Id==leg.PortalId&&
                                ((s.FromSurface==leg.From.SurfaceId&&s.ToSurface==leg.To.SurfaceId)||
                                 (s.ToSurface==leg.From.SurfaceId&&s.FromSurface==leg.To.SurfaceId))&&
                                OnSegment(leg.From.Position,s.From,s.To))){Fail("portal qualification");continue;}
                        row.TypedPortals.Add(leg.PortalId);
                    }else{Fail("overlapping surface");continue;} // overlapping-floor XZ projection is unsupported
                    row.Expected=leg.To;SaveArrivalProgress(row);continue;
                }
                if(row.ReplayCursor<row.Points.Count&&request.Provider is PartitionNavigationProvider actual){
                    if(!actual.TryTrace(row.At,row.Points[row.ReplayCursor++],request.Clearance,out var reached,out var trace)){Fail("XZ replay trace");continue;}
                    var portals=trace.Where(leg=>leg.Kind==NavTypedLegKind.Portal).Select(leg=>leg.PortalId).ToArray();
                    if(row.Arrival&&ReservedRetainedBytes+ListGrowth(row.ObservedPortals.Count,row.ObservedPortals.Capacity,portals.Length,8)>LayeredNavigationScheduler.MaxRetainedBytes){row.ReplayCursor--;break;}
                    row.ObservedPortals.AddRange(portals);
                    row.At=reached;SaveArrivalProgress(row);continue;
                }
                bool valid=row.Expected.Equals(request.Subscription.AssignedEndpoint)&&row.Points.Count>0&&
                    row.Points.Last().Equals(row.Expected.Position)&&
                    (!(request.Provider is PartitionNavigationProvider)||
                        row.At.Equals(row.Expected)&&row.TypedPortals.SequenceEqual(row.ObservedPortals));
                if(!valid){Fail("XZ replay sequence/end");continue;}
                var identity=request.Subscription;
                var corridor=new NavCorridorDescriptor(NavCorridorDescriptor.CurrentVersion,request.GraphKey,
                    request.Provider.Binding,request.Profile.ExactProfileBinding,
                    row.SurfaceProviderId,row.Request.HeldIdentity,row.Request.BaseGeometry.Revision,request.Clearance.Radius,
                    request.Profile.GridCell,identity.Provenance,
                    identity.WorldGeneration,identity.GroupId,identity.RootOrderRevision,
                    identity.CommandSequence,identity.IssuedTick,
                    identity.ActivationRevision,identity.EntityId,identity.Incarnation,
                    identity.MobilityRevision,identity.RequestSequence,row.Request.Order,
                    identity.Origin,identity.AssignedEndpoint,
                    route.Legs.Select(leg=>new NavCorridorLeg(leg.From,leg.To,leg.Kind,leg.PortalId,
                        leg.Cost,NavCorridorSection.IndividualConnector,0,0)));
                SetCompletion(key,row,new NavigationAnswer(row.Request,row.Points.ToArray(),NavSolveStatus.Ready,corridor));
                projectionQueue.Dequeue();

                void Fail(string reason){LastProjectionFailure=reason;SetCompletion(key,row,new NavigationAnswer(row.Request,Array.Empty<NavPoint>(),NavSolveStatus.BlockedConnector));projectionQueue.Dequeue();}
            }
        }
        private bool ProjectArrivalStep(Registered row,out string failure)
        {
            failure=null;var request=row.Scheduled;var route=row.Solved.Route;bool legStep=row.LegCursor<route.Legs.Count;
            var leg=legStep?route.Legs[row.LegCursor]:null;bool point=legStep&&leg.Kind==NavTypedLegKind.Surface,typed=legStep&&!point;
            int observedExtra=legStep?0:((PartitionNavigationProvider)request.Provider).TracePortalBound;
            long traceScratch=legStep?0:((PartitionNavigationProvider)request.Provider).TraceScratchBound;
            var port=row.ArrivalOwnerPort;
            if(!port.TryPrepareArrivalProjectionDelta(row.ArrivalOwner,point,typed,observedExtra,traceScratch,out var delta))return false;
            using(delta){
                var p=delta.Prefix;var next=new ArrivalProjection{Leg=p.Leg,Replay=p.Replay,Expected=p.Expected,At=p.At,Points=p.Points,Typed=p.Typed,Observed=p.Observed};
                if(legStep){
                    if(!leg.From.Equals(p.Expected)){failure="leg continuity";return false;}
                    if(point){
                        bool qualified=request.Provider is PartitionNavigationProvider?request.Provider.IsValid(leg.From,request.Clearance)&&request.Provider.IsValid(leg.To,request.Clearance)&&row.Request.Geometry.SegmentFree(leg.From.Position,leg.To.Position,request.Clearance.Radius):request.Provider.CanSweep(leg.From,leg.To,request.Clearance);
                        if(!qualified){failure="surface qualification";return false;}
                        next.Points=p.Points.Concat(new[]{leg.To.Position}).ToArray();
                    }else if(request.Provider is PartitionNavigationProvider partition){
                        if(!leg.From.Position.Equals(leg.To.Position)||!partition.Seams.Any(s=>s.Allowed&&s.Id==leg.PortalId&&((s.FromSurface==leg.From.SurfaceId&&s.ToSurface==leg.To.SurfaceId)||(s.ToSurface==leg.From.SurfaceId&&s.FromSurface==leg.To.SurfaceId))&&OnSegment(leg.From.Position,s.From,s.To))){failure="portal qualification";return false;}
                        next.Typed=p.Typed.Concat(new[]{leg.PortalId}).ToArray();
                    }else{failure="overlapping surface";return false;}
                    next.Leg++;next.Expected=leg.To;
                }else{
                    var actual=(PartitionNavigationProvider)request.Provider;
                    if(!actual.TryTrace(p.At,p.Points[p.Replay],request.Clearance,out var reached,out var trace)){failure="XZ replay trace";return false;}
                    next.Observed=p.Observed.Concat(trace.Where(l=>l.Kind==NavTypedLegKind.Portal).Select(l=>l.PortalId)).ToArray();next.Replay++;next.At=reached;
                }
                BeforeArrivalProjectionCommit?.Invoke(delta,next);return port.CommitArrivalProjectionDelta(delta,next);
            }
        }
        private void SaveArrivalProgress(Registered row)
        {
            if(!row.Arrival)return;
            currentPort.SaveArrivalProjection(row.Request,new ArrivalProjection{Leg=row.LegCursor,Replay=row.ReplayCursor,
                Expected=row.Expected,At=row.At,Points=row.Points.ToArray(),Typed=row.TypedPortals.ToArray(),Observed=row.ObservedPortals.ToArray()});
        }
        private static long ListGrowth(int count,int capacity,int extra,int bytes)
        {if(count+extra<=capacity)return 0;int next=Math.Max(count+extra,capacity==0?4:capacity*2);return (long)(next-capacity)*bytes;}
        private static bool OnSegment(NavPoint point,NavPoint a,NavPoint b)
        {
            double x=b.X-a.X,z=b.Z-a.Z,length=x*x+z*z;
            double t=length==0?0:Math.Max(0,Math.Min(1,((point.X-a.X)*x+(point.Z-a.Z)*z)/length));
            double dx=point.X-a.X-t*x,dz=point.Z-a.Z-t*z;
            return dx*dx+dz*dz<=1e-14;
        }
    }
}
