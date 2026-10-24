using System;
using System.Threading;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal enum NavigationPreparationKind { Decode, Reconstruction, Admission, Arrival, Solve, Projection, Capture, RouteAdmission }

    // One budget spans committed owners and all simultaneously staged allocations.
    // The host supplies a deduplicated committed charge; growth forecasts never
    // replace the successful allocation credits held by a prepared operation.
    internal sealed class NavigationRetentionBudget : INavStorageLedger, INavStorageCaptureLane
    {
        internal static readonly long RootBytes=Math.Max(128+NavOwnedStorage.CaptureHeaderBytes,NavStorageLayout.ObjectBytesBound(typeof(NavigationRetentionBudget))+NavStorageLayout.ObjectBytesBound(typeof(object)));
        internal const long ScopeBytes=256;
        private readonly object gate=new object();
        private long committed,held,physical,sequence,peak,liveHeadroom;
        private Scope activeHead;
        private long ProtectedBytes(){long value=liveHeadroom;for(var scope=activeHead;scope!=null;scope=scope.ActiveNext)value=Math.Max(value,scope.ProtectedLiveBytes);return value;}
        internal long AuthorityPublicationBound {get;private set;}
        internal void ObservePublicationBound(long bytes){lock(gate)AuthorityPublicationBound=bytes;}
        internal int LiveActorBound {get;private set;}
        internal int LiveBarrierBound {get;private set;}=NavMailbox<NavigationAnswer>.Capacity;
        internal void ObserveLiveShape(int actors,int barriers){lock(gate){LiveActorBound=actors;LiveBarrierBound=barriers;}}
        internal sealed class CapacityRefused : InvalidOperationException {internal CapacityRefused():base("Live navigation capacity refused.") {}}
        private static bool Background(NavigationPreparationKind kind)=>kind==NavigationPreparationKind.RouteAdmission||kind==NavigationPreparationKind.Solve||kind==NavigationPreparationKind.Projection||kind==NavigationPreparationKind.Arrival;
        internal long LiveHeadroom {get{lock(gate)return ProtectedBytes();}}
        internal bool TryProtectLive(long bytes,int actors=0)
        {
            if(bytes<0||actors<0)throw new ArgumentOutOfRangeException();
            lock(gate){long previous=ProtectedBytes();if(bytes>previous&&bytes>LayeredNavigationScheduler.MaxRetainedBytes-committed-held-physical)return false;liveHeadroom=bytes;return true;}
        }
        // A scratch credit proves that the additional floor fits before it is
        // installed. Returning the credit exposes exactly that protected space.
        internal void ProtectInstallation(long bytes,INavStorageReservation reservation)
        {
            if(!(reservation is INavStoragePreparationOwner context)||!(context.PreparationOwner is Scope owner)||!ReferenceEquals(owner.Identity,this))throw new InvalidOperationException("Installation protection needs its actual preparation scope.");
            long delta;lock(gate){owner.AssertOwnerLive();delta=Math.Max(0,bytes-ProtectedBytes());}
            if(delta==0){lock(gate)owner.ProtectedLiveBytes=Math.Max(owner.ProtectedLiveBytes,bytes);return;}
            if(!NavStorageScratch.TryReserve(reservation,delta,out var lease))throw new CapacityRefused();
            using(lease){lock(gate)owner.ProtectedLiveBytes=Math.Max(owner.ProtectedLiveBytes,bytes);}
        }
        private sealed class CapacityReservation : INavStorageReservation,INavStorageScratchAllocator,INavStorageLedgerIdentity,INavStorageLocalBirthAllocator,INavStoragePreparationOwner
        {
            private readonly Scope scope;internal CapacityReservation(Scope scope){this.scope=scope;}
            public bool TryReserve(long bytes){if(!scope.TryReserve(bytes))throw new CapacityRefused();return true;}
            public void Register(NavOwnedStorage storage)=>((INavStorageAllocator)scope).Register(storage);
            public bool TryRegisterLocalBirth(NavOwnedStorage storage)=>scope.TryRegisterLocalBirth(storage);
            public object PreparationOwner=>scope;
            public object Identity=>scope.Identity;public bool Owns(NavOwnedStorage storage)=>scope.Owns(storage);
            public IDisposable ClaimScratch(long bytes)=>scope.ClaimScratch(bytes);
        }
        private long captureEpoch;private bool captureActive;
        bool INavStorageCaptureLane.TryEnterCapture(out long epoch)
        {lock(gate){epoch=0;if(captureActive||captureEpoch==long.MaxValue)return false;captureActive=true;epoch=++captureEpoch;return true;}}
        void INavStorageCaptureLane.ExitCapture(long epoch)
        {lock(gate){if(!captureActive||epoch!=captureEpoch)throw new InvalidOperationException("Invalid physical capture lane release.");captureActive=false;}}
        internal bool CaptureActive {get{lock(gate)return captureActive;}}
        private int active,resolvingScopes;
        private Action pendingResolutionWake;
        internal Func<long,bool> AllocationAdmission;
        internal NavigationRetentionBudget(long committedBytes=0,long sequence=0)
        {
            if(committedBytes<0||committedBytes>LayeredNavigationScheduler.MaxRetainedBytes-RootBytes||sequence<0)
                throw new ArgumentException("Invalid committed navigation retention.");
            committed=committedBytes+RootBytes;this.sequence=sequence;peak=committed;
        }
        internal long RetainedBytes {get{lock(gate)return committed+held+physical;}}
        internal long PhysicalBytes {get{lock(gate)return physical;}}
        void INavStorageLedger.Released(long bytes){using var measured=PerformanceProfile.Sample(ProfileStage.RetentionRelease);lock(gate){if(bytes<0||bytes>physical)throw new InvalidOperationException("Invalid physical release.");physical-=bytes;}}
        internal long CommittedBytes {get{lock(gate)return committed-RootBytes;}}
        internal long HeldBytes {get{lock(gate)return held;}}
        internal long PeakRetainedBytes {get{lock(gate)return peak;}}
        internal long Sequence {get{lock(gate)return sequence;}}
        internal int ActiveScopes {get{lock(gate)return active;}}
        private long resolvedScopes;
        internal long ResolvedScopes {get{lock(gate)return resolvedScopes;}}
        internal bool TryOpenScope(NavigationPreparationKind kind,long owner,long publication,out Scope scope,Action wakeAfterResolution=null)
            =>TryOpenScope(kind,owner,publication,out scope,wakeAfterResolution,out _);
        internal bool TryOpenScope(NavigationPreparationKind kind,long owner,long publication,out Scope scope,
            Action wakeAfterResolution,out bool resolvingRefusal)
        {
            resolvingRefusal=false;
            if(!Enum.IsDefined(typeof(NavigationPreparationKind),kind)||owner<0||publication<0)
                throw new ArgumentException("Invalid navigation preparation identity.");
            lock(gate){
                scope=null;
                if(AllocationAdmission?.Invoke(ScopeBytes)==false)return false;
                if(resolvingScopes!=0){
                    // Coalesce the already allocated host event snapshot. Only
                    // this known transient refusal registers an availability wake.
                    resolvingRefusal=true;
                    if(wakeAfterResolution!=null)pendingResolutionWake=wakeAfterResolution;
                    return false;
                }
                if(ScopeBytes>LayeredNavigationScheduler.MaxRetainedBytes-committed-held-physical-(Background(kind)?ProtectedBytes():0)||sequence==long.MaxValue)return false;
                // Charge the scope before its allocation; it owns its metadata.
                held+=ScopeBytes;active++;sequence++;peak=Math.Max(peak,committed+held+physical);
                scope=new Scope(this,kind,owner,publication,sequence){ActiveNext=activeHead};activeHead=scope;return true;
            }
        }
        internal sealed class Scope : IDisposable, INavStorageReservation, INavStorageScratchAllocator, INavStorageLedgerIdentity, INavStorageLocalBirthAllocator, INavStoragePreparationOwner
        {
            public object PreparationOwner=>this;
            internal Scope ActiveNext;internal long ProtectedLiveBytes;
            private readonly NavigationRetentionBudget budget;
            private long bytes=ScopeBytes,registered,scratchHeld;
            private object prepared;private NavOwnedStorage preparedStorage;
            private NavOwnedStorage births;
            private int ownerThread=Thread.CurrentThread.ManagedThreadId;
            private bool resolved,cancelled,resolving,publishing,published;
            internal Scope(NavigationRetentionBudget budget,NavigationPreparationKind kind,long owner,long publication,long token)
            {this.budget=budget;Kind=kind;Owner=owner;Publication=publication;Token=token;}
            internal NavigationPreparationKind Kind {get;}
            internal long Owner {get;} internal long Publication {get;} internal long Token {get;}
            // Scope accounting includes cumulative converted credits, even when
            // mutation-local births have died. Budget.PhysicalBytes is live retention.
            internal long HeldBytes {get{lock(budget.gate)return bytes+registered;}}
            internal bool IsResolving {get{lock(budget.gate)return resolving;}}
            internal bool IsResolved {get{lock(budget.gate)return resolved;}}
            internal bool IsCancelled {get{lock(budget.gate)return cancelled;}}
            internal object Prepared {get{lock(budget.gate)return prepared;}}
            private void CheckOwner(){if(ownerThread!=Thread.CurrentThread.ManagedThreadId)throw new InvalidOperationException("Navigation preparation belongs to another owner thread.");}
            private void CheckLive(){if(resolved)throw new InvalidOperationException("Navigation preparation is already resolved.");}
            internal void AssertOwnerLive(){lock(budget.gate){CheckOwner();CheckLive();}}
            bool INavStorageReservation.TryReserve(long bytes)=>TryReserve(bytes);
            internal INavStorageReservation OwnedReservation(Func<long,bool> decorated=null)
            {if(decorated==null)return this;if(!TryReserve(64))throw new InvalidOperationException("Reservation adapter exceeds scope.");return new NavStorageReservation(decorated,this);}
            internal INavStorageReservation CapacityReservationOwned()
            {if(!TryReserve(64))throw new CapacityRefused();return new CapacityReservation(this);}
            internal bool TryReserve(long allocationBytes)
            {
                using var measured=PerformanceProfile.Sample(ProfileStage.RetentionReserve);
                if(allocationBytes<0)throw new ArgumentException("Negative prepared allocation.");
                lock(budget.gate){CheckOwner();CheckLive();
                    if(budget.AllocationAdmission?.Invoke(allocationBytes)==false||publishing||published||resolving||cancelled||allocationBytes>LayeredNavigationScheduler.MaxRetainedBytes-budget.committed-budget.held-budget.physical-(Background(Kind)?budget.ProtectedBytes():0))return false;
                    bytes+=allocationBytes;budget.held+=allocationBytes;budget.peak=Math.Max(budget.peak,budget.committed+budget.held+budget.physical);return true;
                }
            }
            void INavStorageAllocator.Register(NavOwnedStorage storage)=>RegisterStorage(storage,true);
            public bool TryRegisterLocalBirth(NavOwnedStorage storage){RegisterStorage(storage,false);return true;}
            private void RegisterStorage(NavOwnedStorage storage,bool scopeBirth)
            {
                using var measured=PerformanceProfile.Sample(ProfileStage.RetentionRegister);
                if(storage==null)throw new ArgumentNullException(nameof(storage));
                lock(budget.gate){CheckOwner();CheckLive();
                    if(publishing||published||resolving||storage.StorageBytes>bytes-ScopeBytes-scratchHeld)throw new InvalidOperationException("Allocation has no successful credit.");
                    storage.BindLedger(budget);if(scopeBirth){storage.BirthNext=births;births=storage;}
                    bytes-=storage.StorageBytes;budget.held-=storage.StorageBytes;budget.physical+=storage.StorageBytes;registered+=storage.StorageBytes;
                }
            }
            private sealed class ScratchLease : IDisposable
            {
                private Scope owner;private readonly long bytes;
                internal ScratchLease(Scope owner,long bytes){this.owner=owner;this.bytes=bytes;}
                public void Dispose(){var value=owner;if(value==null)return;value.ReturnScratch(bytes);owner=null;}
            }
            public object Identity=>budget;
            public bool Owns(NavOwnedStorage storage){lock(budget.gate){CheckOwner();CheckLive();return storage!=null&&storage.BelongsTo(budget);}}
            public IDisposable ClaimScratch(long admittedBytes)
            {
                lock(budget.gate){CheckOwner();CheckLive();
                    if(publishing||published||resolving||admittedBytes<NavStorageScratch.LeaseBytes||admittedBytes>bytes-ScopeBytes-scratchHeld)throw new InvalidOperationException("Scratch has no successful credit.");
                    scratchHeld+=admittedBytes;return new ScratchLease(this,admittedBytes);
                }
            }
            private void ReturnScratch(long scratchBytes)
            {
                lock(budget.gate){CheckOwner();CheckLive();if(scratchBytes>scratchHeld)throw new InvalidOperationException("Duplicate scratch release.");scratchHeld-=scratchBytes;bytes-=scratchBytes;budget.held-=scratchBytes;}
            }
            internal void Publish(object sealedCandidate,Action<object> publish)
            {
                if(publish==null||sealedCandidate==null)throw new ArgumentException("Invalid navigation publication.");
                lock(budget.gate){CheckOwner();CheckLive();if(cancelled)throw new OperationCanceledException("Navigation preparation was cancelled.");
                    if(publishing||published||resolving||scratchHeld!=0||!ReferenceEquals(prepared,sealedCandidate))throw new InvalidOperationException("Invalid prepared publication phase/root.");publishing=true;
                }
                // Never acquire a transport gate while holding the budget gate.
                // The caller atomically validates and transfers state/token/quota
                // in its short transport publication callback.
                try{publish(sealedCandidate);}catch{lock(budget.gate)publishing=false;throw;}
                lock(budget.gate){publishing=false;published=true;budget.liveHeadroom=Math.Max(budget.liveHeadroom,ProtectedLiveBytes);}
            }
            internal void ResolveCommitted()
            {lock(budget.gate){CheckOwner();CheckLive();if(!published)throw new InvalidOperationException("No committed publication to resolve.");}ResolvePhysical(false);}
            // Standalone convenience only. Actual host publication uses Publish,
            // leaves the transport gate, then calls ResolveCommitted.
            internal void Commit(object sealedCandidate,Action<object> publish)
            {Publish(sealedCandidate,publish);ResolveCommitted();}
            internal void Attach(object candidate)
            {
                if(candidate==null)throw new ArgumentNullException(nameof(candidate));
                var storage=candidate as NavOwnedStorage??(candidate as INavStorageFacade)?.StorageRoot;
                lock(budget.gate){CheckOwner();CheckLive();if(prepared!=null)throw new InvalidOperationException("Prepared root already attached.");if(storage!=null&&storage.IsRegistered&&!storage.BelongsTo(budget))throw new ArgumentException("Prepared storage belongs to another ledger.");prepared=candidate;preparedStorage=storage;}
            }
            // Cancellation marks the stable operation, never frees an in-use fork
            // from a foreign thread. Its owner terminally discards it afterwards.
            internal void RequestCancel(){lock(budget.gate){if(!resolved)cancelled=true;}}
            internal void RelinquishOwner(){lock(budget.gate){CheckOwner();CheckLive();if(scratchHeld!=0)throw new InvalidOperationException("Cannot hand off live scratch.");ownerThread=0;}}
            internal void AcquireOwner(){lock(budget.gate){CheckLive();if(ownerThread!=0)throw new InvalidOperationException("Preparation already acquired.");ownerThread=Thread.CurrentThread.ManagedThreadId;}}
            internal void Commit(object sealedCandidate,long committedBytes,Action<object> publish)
            {
                if(publish==null||sealedCandidate==null||committedBytes<0)throw new ArgumentException("Invalid navigation publication.");
                lock(budget.gate){CheckOwner();CheckLive();
                    if(cancelled)throw new OperationCanceledException("Navigation preparation was cancelled.");
                    if(!ReferenceEquals(prepared,sealedCandidate))throw new InvalidOperationException("Publication is not the attached prepared root.");
                    if(births!=null||scratchHeld!=0)throw new InvalidOperationException("Physical ownership or live scratch cannot be replaced by a caller estimate.");
                    long next=checked(committedBytes+RootBytes);
                    if(next-budget.committed>bytes||next>LayeredNavigationScheduler.MaxRetainedBytes-(budget.held-bytes)-budget.physical)
                        throw new ArgumentException("Unreserved navigation publication.");
                    // Caller holds the authority gate and publishes one sealed root.
                    // A failed publication leaves this scope unresolved for discard.
                    publish(sealedCandidate);budget.committed=next;Resolve();
                }
            }
            private void Resolve()
            {
                // Drop the owner's references before releasing its allocation
                // credits, so an original-session stop ACK cannot retain a fork.
                prepared=null;preparedStorage=null;
                if(ReferenceEquals(budget.activeHead,this))budget.activeHead=ActiveNext;
                else{var prior=budget.activeHead;while(prior!=null&&!ReferenceEquals(prior.ActiveNext,this))prior=prior.ActiveNext;if(prior==null)throw new InvalidOperationException("Preparation missing from ledger.");prior.ActiveNext=ActiveNext;}
                ActiveNext=null;ProtectedLiveBytes=0;budget.held-=bytes;bytes=registered=0;budget.active--;resolved=true;budget.resolvedScopes++;
            }
            private bool ResolvePhysical(bool discard)
            {
                NavOwnedStorage head,owned;
                lock(budget.gate){if(resolved)return false;CheckOwner();if(resolving||publishing||scratchHeld!=0)throw new InvalidOperationException("Preparation is resolving/publishing or has live scratch.");
                    resolving=true;budget.resolvingScopes++;head=births;births=null;owned=discard&&!published?preparedStorage:null;prepared=null;preparedStorage=null;
                }
                bool released=false;
                try{
                    owned?.ReleaseOwner();
                    while(head!=null){var next=head.BirthNext;head.BirthNext=null;head.Release();head=next;}
                    released=true;
                }finally{
                    Action resume=null;
                    // Availability is published only after every owning edge and
                    // birth has gone. A teardown fault remains an error; it must
                    // neither retain nor invoke a stale availability callback.
                    lock(budget.gate){
                        if(!released)budget.pendingResolutionWake=null;
                        else{
                            try{Resolve();}
                            catch{budget.pendingResolutionWake=null;throw;}
                            resolving=false;budget.resolvingScopes--;
                            if(budget.resolvingScopes==0){resume=budget.pendingResolutionWake;budget.pendingResolutionWake=null;}
                        }
                    }
                    resume?.Invoke();
                }
                return true;
            }
            internal bool Discard()=>ResolvePhysical(true);
            public void Dispose(){Discard();}
        }
    }
}
