using System;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        internal int OwnedResultBatchPublications{get;private set;}
        internal long LastOwnedResultBatchPublicationAllocatedBytes{get;private set;}=-1;
        internal int LastResultBatchStage{get;private set;}
        internal int LastResultBatchSourceReason{get;private set;}
        internal string LastResultBatchDetail{get;private set;}
        private enum ResultOperationKind:byte{Discard,Probe,Ready,Failure,Reissue}
        private struct ResultOperation
        {
            internal ResultOperationKind Kind;internal bool Tracked,Detached;
            internal int Actor,Group,Member;internal NavigationHostAnswer Payload;internal NavigationHostRequest Replacement;
            internal RebindActor FailureSource;internal bool FailureReject;internal NavigationOutcome FailureOutcome;
            internal PlayableDomain RetentionDomain;internal PlayableDomain.NavigationRetentionSource Retention;
        }
        // One initial ordered answer set, one shared projection of each touched
        // backing. Private preparation never swaps live Session state.
        private sealed partial class PreparedResultBatch:NavOwnedStorage,IDisposable
        {
            private static long Align(long n)=>checked((n+7)/8*8);
            internal static long Layout(int count,int actors,int attempts,int mailbox,int groups,int held=0)=>checked((held==0?0:64L+Align(held*NavStorageLayout.ValueBytes(typeof(HeldRefreshOperation)))+16L*held)+NavStorageLayout.ObjectBytesBound(typeof(PreparedResultBatch))+19*32L+Align(count*NavStorageLayout.ValueBytes(typeof(ResultOperation)))+Align(actors*NavStorageLayout.ValueBytes(typeof(ReadyActorFact)))+2*Align(groups*NavStorageLayout.ValueBytes(typeof(NavigationLiveGroupObservation)))+24L*groups+32L*count+24L*actors+2*Align(actors)+Align(groups)+16L*mailbox+Align(attempts*NavStorageLayout.ValueBytes(typeof(RebindAttempt)))+Align(attempts*NavStorageLayout.ValueBytes(typeof(RebindRetirement))));
            internal NavigationBarrierReceipt RetiredBarrier;
            internal readonly ResultOperation[] Operations;
            internal readonly ReadyActorFact[] Actors;internal readonly NavOwnedStorage[] ActorPins;
            internal readonly NavigationInstalledExecutionOwner[] Installed,ProjectedInstalled;
            internal readonly bool[] Detached,ProjectedDetached;
            internal readonly NavigationLiveGroupObservation[] GroupSources;
            internal readonly NavigationAuthorityGroup[] Groups,RetiredGroups;
            internal readonly NavOwnedStorage[] MailboxRetired,RetiredTracking;
            internal readonly NavigationHostRequest[] RetiredTransport;
            internal readonly RebindAttempt[] Attempts;internal readonly RebindRetirement[] Retirements;
            internal readonly NavOwnedStorage[] DisplacedRoutes;
            internal NavigationPreparedAnswerNames Names;internal NavigationHostRoot Host;
            internal NavigationCommandSlots<int,NavigationRequest> Pending,Allocating,Probes,NextPending,NextProbes,RetiredPending,RetiredProbes;
            internal NavigationCommandSlots<long,NavigationArrivalReservation> Ledger,NextLedger,RetiredLedger;
            internal NavigationCommandSlots<int,NavSolveStatus> Failures,NextFailures,RetiredFailures;
            internal NavigationCommandSlots<int,NavPoint> Goals;internal NavigationCommandSlots<int,string> Identities;
            internal NavigationLiveArrivalTables Arrival;internal NavigationLiveProbeAnswers Queue,NextQueue,RetiredQueue;
            internal int PendingRevision,AllocatingRevision,ProbeRevision,LedgerRevision,FailureRevision,GoalRevision,IdentityRevision,ArrivalRevision,QueueRevision,GroupCount,ReadyCount,ProbeCount,RejectedCount,DiscardCount,InstalledCount;
            internal bool HasCorridorDiagnostic;internal string CorridorDiagnostic;internal int InvalidReadyCount;
            internal long SourceAnswerBytes,AnswerBytes,SourceCorridorBytes,CorridorBytes,SourceGeometryBytes,GeometryBytes,HostBytes,SourceHostBytes,RetirementSequence;
            internal bool Published,PhysicalCommutation;private bool released,destinations;
            private PreparedResultBatch(int count,int actors,int attempts,int mailbox,int groups,int held):base(Layout(count,actors,attempts,mailbox,groups,held))
            {HeldRows=held==0?Array.Empty<HeldRefreshOperation>():new HeldRefreshOperation[held];HeldRoutes=held==0?Array.Empty<NavOwnedStorage>():new NavOwnedStorage[2*held];Operations=new ResultOperation[count];Actors=new ReadyActorFact[actors];ActorPins=new NavOwnedStorage[actors];Installed=new NavigationInstalledExecutionOwner[actors];ProjectedInstalled=new NavigationInstalledExecutionOwner[actors];Detached=new bool[actors];ProjectedDetached=new bool[actors];GroupSources=new NavigationLiveGroupObservation[groups];Groups=new NavigationAuthorityGroup[groups];RetiredGroups=new NavigationAuthorityGroup[groups];MarchSources=new NavigationLiveGroupObservation[groups];MarchWrites=new bool[groups];PreMarchGroups=new NavigationAuthorityGroup[groups];MailboxRetired=new NavOwnedStorage[2*mailbox];RetiredTransport=new NavigationHostRequest[count];RetiredTracking=new NavOwnedStorage[count];Attempts=new RebindAttempt[attempts];Retirements=new RebindRetirement[attempts];DisplacedRoutes=new NavOwnedStorage[2*count];Retain();}
            internal static PreparedResultBatch Create(int count,int actors,int attempts,int mailbox,int groups,INavStorageReservation r,int held=0){using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout(count,actors,attempts,mailbox,groups,held));return a.Register(new PreparedResultBatch(count,actors,attempts,mailbox,groups,held));}}
            internal void SealDestinations(){SealReissueDestinations();SealResultFailureDestinations();NextPending?.Retain();NextProbes?.Retain();NextLedger?.Retain();NextFailures?.Retain();NextQueue?.Retain();for(int i=0;i<GroupCount;i++)Groups[i].Retain();destinations=true;}
            internal void TransferNoThrow(){TransferReissueNoThrow();TransferResultFailureNoThrow();destinations=false;Published=true;}
            public void Dispose(){if(released)return;released=true;Release();}
            internal override void ReleaseOwner()=>Dispose();
            protected override void ReleaseEdges(){
                RetiredBarrier?.Release();foreach(var op in Operations)op.Payload?.Release();foreach(var pin in ActorPins)pin?.Release();foreach(var value in Installed)value?.Release();foreach(var fact in Attempts)fact.Owner?.Release();foreach(var op in Operations)op.FailureSource.Release();foreach(var edge in DisplacedRoutes)edge?.Release();ReleaseHeldEdges();ReleaseReissueEdges();ReleaseResultFailureEdges();ReleaseResultMovementEdges();foreach(var edge in MailboxRetired)edge?.Release();foreach(var edge in RetiredTransport)edge?.Release();foreach(var edge in RetiredTracking)edge?.Release();
                for(int i=0;i<GroupCount;i++){if(destinations)Groups[i]?.Release();Groups[i]?.ReleaseStorage();RetiredGroups[i]?.Release();}
                Names?.Release();Host?.Release();Pending?.Release();Allocating?.Release();Probes?.Release();Ledger?.Release();Failures?.Release();Goals?.Release();Identities?.Release();Arrival?.Release();Queue?.Release();
                if(destinations){NextPending?.Release();NextProbes?.Release();NextLedger?.Release();NextFailures?.Release();NextQueue?.Release();}
                NextPending?.ReleaseStorage();NextProbes?.ReleaseStorage();NextLedger?.ReleaseStorage();NextFailures?.ReleaseStorage();NextQueue?.ReleaseStorage();RetiredPending?.Release();RetiredProbes?.Release();RetiredLedger?.Release();RetiredFailures?.Release();RetiredQueue?.Release();
            }
        }
        private bool ResultBatchSourceCurrent(PreparedResultBatch c)
        {
            LastResultBatchSourceReason=0;
            if(!HeldSourcesCurrent(c))return ResultBatchSourceFailure(1,"held");if(!ResultReissueSourcesCurrent(c))return ResultBatchSourceFailure(2,"reissue");if(!ResultMovementSourcesCurrent(c))return ResultBatchSourceFailure(3,"movement");
            if(!physicalArrivalEnabled)return ResultBatchSourceFailure(4,"physical-arrival");if(!AnswerNamesSourceCurrent(c.Names,c.HeldMode?0:-1))return ResultBatchSourceFailure(5,"answer-names");if(!c.PhysicalCommutation&&hostAllocationThread!=0)return ResultBatchSourceFailure(6,"host-allocation");if(admissionBatchDepth!=0)return ResultBatchSourceFailure(7,"admission-batch");if(commandGroup!=null)return ResultBatchSourceFailure(8,"command-group");if(c.PhysicalCommutation?!ResultBatchPhysicalHostCurrent(c):!ReferenceEquals(committedHostRoot,c.Host))return ResultBatchSourceFailure(9,"host-root");
            if(!ReferenceEquals(pending,c.Pending)||c.Pending.Revision!=c.PendingRevision)return ResultBatchSourceFailure(10,"pending");if(!ReferenceEquals(allocating,c.Allocating)||c.Allocating.Revision!=c.AllocatingRevision)return ResultBatchSourceFailure(11,"allocating");if(c.Probes==null?probes.Count!=0:!ReferenceEquals(probes,c.Probes)||c.Probes.Revision!=c.ProbeRevision)return ResultBatchSourceFailure(12,"probes");if(!ReferenceEquals(arrivalLedger,c.Ledger)||c.Ledger.Revision!=c.LedgerRevision)return ResultBatchSourceFailure(13,"arrival-ledger");if(!ReferenceEquals(technicalFailures,c.Failures)||c.Failures.Revision!=c.FailureRevision)return ResultBatchSourceFailure(14,"failures");if(!ReferenceEquals(retainedGoals,c.Goals)||c.Goals.Revision!=c.GoalRevision)return ResultBatchSourceFailure(15,"goals");if(!ReferenceEquals(movementIdentities,c.Identities)||c.Identities.Revision!=c.IdentityRevision)return ResultBatchSourceFailure(16,"identities");if(!ReferenceEquals(liveArrivalTables,c.Arrival)||c.Arrival.Revision!=c.ArrivalRevision)return ResultBatchSourceFailure(17,"arrival-tables");if(arrivalRetirementSequence!=c.RetirementSequence)return ResultBatchSourceFailure(18,"retirement-sequence");if(arrivalProjectionLeases.Count!=0)return ResultBatchSourceFailure(19,"arrival-projection-leases");if(!ReferenceEquals(liveProbeAnswers,c.Queue)||c.Queue!=null&&c.Queue.Revision!=c.QueueRevision)return ResultBatchSourceFailure(20,"probe-queue");
            if(answerCorridorBytes!=c.SourceAnswerBytes)return ResultBatchSourceFailure(21,"answer-bytes");if(installedCorridorBytes!=c.SourceCorridorBytes)return ResultBatchSourceFailure(22,"corridor-bytes");if(installedGeometryBytes!=c.SourceGeometryBytes)return ResultBatchSourceFailure(23,"geometry-bytes");if(hostRetainedBytes!=(c.PhysicalCommutation?c.SourceHostBytes:c.HostBytes))return ResultBatchSourceFailure(24,"host-bytes");if(Crowd.Units.Count!=c.Actors.Length)return ResultBatchSourceFailure(25,"actor-count");if(installedExecutions.Count!=c.InstalledCount)return ResultBatchSourceFailure(26,"installed-count");if(!c.PrefixNoOp&&!c.HeldMode&&!c.HasHeldPrefix)return ResultBatchSourceFailure(27,"prefix");if(!ResultFailureSourcesCurrent(c))return ResultBatchSourceFailure(28,"failure-sources");
            for(int i=0;i<c.Actors.Length;i++){var fact=c.Actors[i];if(!Crowd.TryGet(fact.Id,out var actor)||!fact.Matches(actor)||!ReferenceEquals(actor.StorageOwner,c.ActorPins[i])||!ReferenceEquals(installedExecutions.OwnerFor(fact.Id),c.Installed[i])||c.Installed[i]!=null&&installedExecutions.IsDetached(fact.Id)!=c.Detached[i])return ResultBatchSourceFailure(40,"actor-fact");}
            if(!ResultBatchAttemptsCurrent(c))return ResultBatchSourceFailure(41,"attempts");
            for(int i=0;i<c.Operations.Length;i++){var answer=c.Names.Answers[i];if(!transportOwner.CanConsumePrepared(answer.Request)||IsCorridorAnswerTracked(answer)!=c.Operations[i].Tracked)return ResultBatchSourceFailure(42,"operation-ownership");}
            return true;
        }
        // A paid result+motion carrier writes no derived solver root. Immutable
        // frontier/grant changes commute within the same geometry and execution mode,
        // and while the actual scheduler capacity input remains unchanged.
        private bool ResultBatchPhysicalHostCurrent(PreparedResultBatch c)
        {
            var now=committedHostRoot;var source=c.Host;
            return now!=null&&source!=null&&ReferenceEquals(now.AuthorityGeometry,source.AuthorityGeometry)&&now.TickQuantum==source.TickQuantum&&now.QuantumGeneration==source.QuantumGeneration&&now.Scheduler.RetainedBytes==c.HostBytes;
        }
        private bool ResultBatchSourceFailure(int reason,string detail){LastResultBatchSourceReason=reason;if(MovementDiagnosticObserver!=null)LastResultBatchDetail=string.Concat("source|",detail);return false;}
        private bool ResultBatchFalse(int stage,string detail){LastResultBatchStage=stage;if(MovementDiagnosticObserver!=null)LastResultBatchDetail=LastResultBatchSourceReason==0?detail:string.Concat(detail,"|sourceReason=",LastResultBatchSourceReason);return false;}
        private NavigationCommandSlots<K,V> ResultBatchCopy<K,V>(PreparedResultBatch c,NavigationCommandSlots<K,V> source,NavigationRetentionBudget.Scope scope,int capacity=0)
        {
            capacity=Math.Max(source.Capacity,capacity);var copy=capacity==0?NavigationCommandSlots<K,V>.CreateEmptyOwned(scope.OwnedReservation(),source.UsesReferenceKeys):NavigationCommandSlots<K,V>.CreateOwned(capacity,scope.OwnedReservation(),source.UsesReferenceKeys);
            try{using(EnterTransport()){if(!ResultBatchSourceCurrent(c))throw new InvalidOperationException("Result batch copy lost source.");copy.CopyPreparedFrom(source);}return copy;}catch{copy.ReleaseStorage();throw;}
        }
        private static long ResultBatchGeometryBytes(PreparedResultBatch c,int replacing=-1,NavigationRequest replacement=null)
        {
            NavGeometry GeometryAt(int at){if(at/2==replacing)return at%2==0?replacement.Geometry:replacement.BaseGeometry;var installed=c.ProjectedInstalled[at/2];return installed==null?null:(at%2==0?installed.Request.Request.Geometry:installed.Request.Request.BaseGeometry);}
            long bytes=0;
            for(int i=0;i<c.ProjectedInstalled.Length*2;i++){
                var geometry=GeometryAt(i);if(geometry==null)continue;bool seen=false;for(int j=0;j<i;j++)if(ReferenceEquals(GeometryAt(j),geometry)){seen=true;break;}if(seen)continue;
                long vertices=0;for(int j=0;j<geometry.Obstacles.Count;j++)vertices+=geometry.Obstacles[j].Polygon?.Vertices.Count??0;
                bytes=checked(bytes+256L+512L*geometry.Obstacles.Count+128L*vertices);
            }return bytes;
        }
        private bool PrepareProbeIntoResultBatch(PreparedResultBatch c,int at,NavigationRetentionBudget.Scope scope)
        {
            var answer=c.Names.Answers[at];var request=answer.Request;ref var op=ref c.Operations[at];
            if(c.Probes==null||c.Queue==null)return false;
            var active=c.NextProbes??c.Probes;
            bool current=request.Session==Generation&&(ReferenceEquals(request.Geometry,navigationGeometry)||IsCurrentPhysicalAuthorityGeometry(request))&&active.TryGetValue(request.Entity,out var expected)&&ReferenceEquals(expected,request);
            if(current&&answer.Status==NavSolveStatus.Ready&&c.Names.HasQualification[at]){c.HasCorridorDiagnostic=true;c.CorridorDiagnostic=c.Names.QualificationFailures[at];}
            bool accepted=current&&(answer.Status!=NavSolveStatus.Ready||(c.Names.HasQualification[at]?c.Names.Qualified[at]:answer.Corridor==null&&request.CertifiedProviderBinding==null));
            c.ProbeCount++;
            if(!accepted){op.Kind=ResultOperationKind.Discard;c.RejectedCount++;if(op.Tracked)c.AnswerBytes-=answer.Corridor.EstimatedRetainedBytes;return true;}
            op.Kind=ResultOperationKind.Probe;op.Payload=answer.StorageOwner==null?(answer.Status==NavSolveStatus.Ready?NavigationHostAnswer.CreateLegacyProbeReady(request.StorageOwner,answer.ReadOnlyRouteBacking,scope.OwnedReservation()):NavigationHostAnswer.CreateTerminal(request.StorageOwner,answer.Status,scope.OwnedReservation())):NavPhysicalMacro.Acquire(c,answer.StorageOwner);
            if(c.NextProbes==null)c.NextProbes=ResultBatchCopy(c,c.Probes,scope);c.NextProbes.Remove(request.Entity);
            if(c.NextQueue==null){c.NextQueue=NavigationLiveProbeAnswers.CreateOwned(NavigationLiveProbeAnswers.GrowthCapacity(c.Queue.Capacity,c.Queue.Count),scope.OwnedReservation());using(EnterTransport()){if(!ResultBatchSourceCurrent(c))throw new InvalidOperationException("Result batch FIFO lost source.");c.NextQueue.CopyObserved(c.Queue);}}
            else if(c.NextQueue.Count==c.NextQueue.Capacity){var grown=NavigationLiveProbeAnswers.CreateOwned(NavigationLiveProbeAnswers.GrowthCapacity(c.NextQueue.Capacity,c.NextQueue.Count),scope.OwnedReservation());try{grown.CopyObserved(c.NextQueue);}catch{grown.ReleaseStorage();throw;}c.NextQueue.ReleaseStorage();c.NextQueue=grown;}
            c.NextQueue.AppendPreparing(op.Payload,scope.OwnedReservation());return true;
        }
        private bool? TryPublishOwnedResultBatch(NavigationPreparedAnswerNames names,double dt,bool allowComposedMovement=true,bool resultOnly=false,NavMotionWorkspace motionWorkspace=null,bool requireTrafficFrame=false)
        {
            LastResultBatchFailure=null;LastResultBatchStage=0;LastResultBatchSourceReason=0;LastResultBatchDetail=null;
            if(names==null||names.Single&&!resultOnly)return null;
            int actors,attempts,groupCount,heldGoals=0;bool heldPrefix=false;long publication;
            using(EnterTransport()){
                if(!AnswerNamesSourceCurrent(names))return ResultBatchFalse(101,"initial-answer-names");
                if(hostAllocationThread!=0&&(committedHostRoot==null||UseLegacyHost||resultOnly)||admissionBatchDepth!=0||commandGroup!=null||groupCommandBacking==null||!(reservations is NavigationCommandSlots<int,NavPoint>)||!(groups is NavigationCommandSlots<long,GroupOrderState>)||!(memberGroups is NavigationCommandSlots<int,long>)||!(orders is NavigationCommandSlots<int,long>)||probes.Count>0&&ownedProbes==null||ProbeAnswerCount>0&&liveProbeAnswers==null||liveArrivalTables==null||arrivalProjectionLeases.Count!=0||!(pending is NavigationCommandSlots<int,NavigationRequest>)||!(allocating is NavigationCommandSlots<int,NavigationRequest>)||!(arrivalLedger is NavigationCommandSlots<long,NavigationArrivalReservation>)||!(technicalFailures is NavigationCommandSlots<int,NavSolveStatus>)||!(retainedGoals is NavigationCommandSlots<int,NavPoint>)||!(movementIdentities is NavigationCommandSlots<int,string>))return null;
                for(int i=0;i<names.Answers.Length;i++){
                    if(names.Answers[i].Request.StorageOwner==null||!transportOwner.CanConsumePrepared(names.Answers[i].Request))return null;
                    for(int j=0;j<i;j++)if(ReferenceEquals(names.Answers[i].Request,names.Answers[j].Request))return null;
                }
                actors=Crowd.Units.Count;attempts=liveArrivalTables.Count;groupCount=groups.Count;publication=admissionPublicationIdentity;
            }
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,publication,out var scope))return ResultBatchFalse(100,"admission-scope-refused");
            using(scope){PreparedResultBatch c=null;try{
                if(resultOnly||names.Answers.Length>0)using(NavStorageScratch.Reserve(scope.OwnedReservation(),RebindStagingScratchBound(actors,retainedGoals.Count,navigationGeometry.Obstacles.Count)))using(EnterTransport()){
                    if(!AnswerNamesSourceCurrent(names))return ResultBatchFalse(102,"held-scan-answer-names");
                    foreach(var pair in retainedGoals)if(!allocating.ContainsKey(pair.Key)&&Crowd.TryGet(pair.Key,out _)&&movementIdentities.TryGetValue(pair.Key,out var identity)&&!CurrentHeldBinding(identity,pair.Key)){heldPrefix=true;break;}
                    if(heldPrefix)heldGoals=retainedGoals.Count;
                }
                if(resultOnly&&!heldPrefix&&!HasTraffic)return null;
                if(!heldPrefix&&names.Answers.Length==1&&(names.Answers[0].Status!=NavSolveStatus.Ready||names.Answers[0].Request.Entity<0||names.SingleCapacityExceeded))return null;
                c=PreparedResultBatch.Create(names.Answers.Length,actors,attempts,names.Single?(names.BarrierCount>0?0:1):names.SourceAnswerCount,groupCount,scope.OwnedReservation(),heldGoals);c.ResultOnly=resultOnly;c.HasHeldPrefix=heldPrefix;c.Names=NavPhysicalMacro.Acquire(c,names);
                using(EnterTransport()){
                    if(!AnswerNamesSourceCurrent(names)||actors!=Crowd.Units.Count||attempts!=liveArrivalTables.Count||groupCount!=groups.Count)return ResultBatchFalse(103,"capture-counts-or-answer-names");
                    c.Host=NavPhysicalMacro.Acquire(c,committedHostRoot);c.PhysicalCommutation=c.Host!=null&&!UseLegacyHost&&!resultOnly&&!heldPrefix;c.Pending=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,NavigationRequest>)pending);c.PendingRevision=c.Pending.Revision;c.Allocating=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,NavigationRequest>)allocating);c.AllocatingRevision=c.Allocating.Revision;
                    c.Probes=NavPhysicalMacro.Acquire(c,ownedProbes);c.ProbeRevision=c.Probes?.Revision??0;c.Ledger=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<long,NavigationArrivalReservation>)arrivalLedger);c.LedgerRevision=c.Ledger.Revision;c.Failures=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,NavSolveStatus>)technicalFailures);c.FailureRevision=c.Failures.Revision;
                    c.Goals=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,NavPoint>)retainedGoals);c.GoalRevision=c.Goals.Revision;c.Identities=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,string>)movementIdentities);c.IdentityRevision=c.Identities.Revision;
                    c.Arrival=NavPhysicalMacro.Acquire(c,liveArrivalTables);c.ArrivalRevision=c.Arrival.Revision;c.RetirementSequence=arrivalRetirementSequence;c.Queue=NavPhysicalMacro.Acquire(c,liveProbeAnswers);c.QueueRevision=c.Queue?.Revision??0;c.InstalledCount=installedExecutions.Count;
                    c.SourceAnswerBytes=c.AnswerBytes=answerCorridorBytes;c.SourceCorridorBytes=c.CorridorBytes=installedCorridorBytes;c.SourceGeometryBytes=c.GeometryBytes=installedGeometryBytes;c.SourceHostBytes=hostRetainedBytes;c.HostBytes=c.PhysicalCommutation?c.Host.Scheduler.RetainedBytes:hostRetainedBytes;
                    for(int i=0;i<actors;i++){var actor=Crowd.Units[i];c.Actors[i]=new ReadyActorFact(actor);c.ActorPins[i]=NavPhysicalMacro.Acquire(c,actor.StorageOwner);c.Installed[i]=NavPhysicalMacro.Acquire(c,installedExecutions.OwnerFor(actor.Id));c.ProjectedInstalled[i]=c.Installed[i];c.Detached[i]=c.ProjectedDetached[i]=c.Installed[i]!=null&&installedExecutions.IsDetached(actor.Id);}
                    ObserveResultFailureSources(c);if(!ObserveResultBatchAttempts(c)||!ObserveResultMovementGroups(c))return null;
                    for(int i=0;i<names.Answers.Length;i++)c.Operations[i].Tracked=IsCorridorAnswerTracked(names.Answers[i]);
                    if(c.HasHeldPrefix){if(c.Goals.Count!=heldGoals)return ResultBatchFalse(104,"held-goal-count");ObserveResultHeldRows(c);}
                }
                using(NavStorageScratch.Reserve(scope.OwnedReservation(),RebindStagingScratchBound(actors,names.Answers.Length,navigationGeometry.Obstacles.Count))){using(EnterTransport()){c.PrefixNoOp=ResultBatchPrefixNoOp(c);if(!c.PrefixNoOp&&!c.HasHeldPrefix)return null;if(!ResultBatchSourceCurrent(c))return ResultBatchFalse(105,"prefix-source");}}
                if(c.HasHeldPrefix&&!PrepareResultHeldPrefix(c,scope))return ResultBatchFalse(106,"held-prefix-prepare");
                using(NavStorageScratch.Reserve(scope.OwnedReservation(),RebindStagingScratchBound(actors,names.Answers.Length,navigationGeometry.Obstacles.Count)))for(int i=0;i<names.Answers.Length;i++){
                    var request=names.Answers[i].Request;
                    if(request.Entity<0){if(!PrepareProbeIntoResultBatch(c,i,scope))return null;continue;}
                    var pending=c.NextPending??c.Pending;
                    if(!pending.TryGetValue(request.Entity,out var expected)||!ReferenceEquals(expected,request)||!ResultHeldRequestCurrent(c,request)){
                        c.Operations[i].Kind=ResultOperationKind.Discard;c.RejectedCount++;c.DiscardCount++;if(c.Operations[i].Tracked)c.AnswerBytes-=names.Answers[i].Corridor.EstimatedRetainedBytes;continue;
                    }
                    var effective=names.Answers[i].Status;if(effective==NavSolveStatus.Ready&&ResultBatchCapacityExceeded(c,i))effective=NavSolveStatus.CapacityExceeded;
                    bool prepared=effective!=NavSolveStatus.Ready?PrepareFailureIntoResultBatch(c,i,scope,effective):PrepareReadyIntoResultBatch(c,i,scope);
                    if(!prepared){using(EnterTransport())if(!ResultBatchSourceCurrent(c))return ResultBatchFalse(107,"operation-source-after-prepare");return null;}
                }
                if((allowComposedMovement||HasTraffic)&&!PrepareResultMovement(c,dt,scope))return ResultBatchFalse(108,"prepare-result-movement");
                if((HasTraffic||requireTrafficFrame)&&!c.ComposedMovement)return ResultBatchFalse(109,"required-composed-movement-absent");
                if(c.PhysicalCommutation&&!c.ComposedMovement)using(EnterTransport()){
                    // Compatibility motion retains its original strict boundary.
                    // Only the fully paid composed carrier may commute with Host.
                    if(hostAllocationThread!=0||!ReferenceEquals(committedHostRoot,c.Host)||hostRetainedBytes!=c.HostBytes)return null;
                    c.PhysicalCommutation=false;
                }
                if(!resultOnly&&(names.Answers.Length<=1||c.HasHeldPrefix)&&!c.ComposedMovement)return null;
                if(resultOnly)c.HeldAdmission=PrepareHeldAdmission(c,names.ObservedAdmission,scope);
                using(EnterTransport()){if(!ResultBatchSourceCurrent(c))return ResultBatchFalse(110,"pre-seal-source");}
                c.NextPending?.Seal();c.NextProbes?.Seal();c.NextLedger?.Seal();c.NextFailures?.Seal();c.NextGoals?.Seal();c.NextReservations?.Seal();c.NextArrival?.SealPrepared();c.NextQueue?.SealForUse(scope.OwnedReservation());c.SealDestinations();scope.Attach(c);
                Action<object> publish=_=>{long before=GC.GetAllocatedBytesForCurrentThread();using(var requests=Requests.EnterPreparedWrite())using(var mailbox=Answers.EnterPreparedWrite()){
                    if(c.PhysicalCommutation&&!c.ComposedMovement||!ResultBatchSourceCurrent(c)||scope.IsCancelled||authorityOperationIdentity>long.MaxValue-(names.Answers.Length==0?3:4)-c.ExtraOperations||AppliedResults>int.MaxValue-c.ReadyCount||RejectedResults>int.MaxValue-c.RejectedCount||OwnedReadyPublications>int.MaxValue-c.ReadyCount||OwnedProbeAnswerPublications>int.MaxValue-c.ProbeCount||OwnedDiscardPublications>int.MaxValue-c.DiscardCount||names.Answers.Length>0&&OwnedResultBatchPublications==int.MaxValue)throw new InvalidOperationException("Prepared result batch lost its final source.");
                    if(names.Single){if(names.BarrierCount==0)mailbox.ValidateTakeOne(names.MailboxRevision,names.SourceAnswerCount,c.MailboxRetired);}else mailbox.ValidateTakeBatch(names.MailboxRevision,names.SourceAnswerCount,names.Answers,names.BarrierCount,c.MailboxRetired);ValidateResultFailurePublication(c);
                    if(c.ReissueCount>0)requests.ValidateAppendBatch(c.RequestRevision,c.RequestCount,c.ReissueCount,groupCommandBacking.Mailbox,groupCommandBacking.MailboxOwners,c.RequestValidator,c.RequestTransfer);
                    var token=ObserveLiveWriteToken();
                    for(int g=0;g<c.GroupCount;g++){
                        int writes=0;for(int i=0;i<c.Operations.Length;i++)if((c.Operations[i].Kind==ResultOperationKind.Ready||c.Operations[i].Kind==ResultOperationKind.Reissue)&&c.Operations[i].Group==g)writes++;
                        foreach(var row in c.HeldRows)if(row.Eligible&&row.Replacement.Group==g)writes++;
                        if(c.MarchWrites[g])writes++;if(c.GroupSources[g].Generation>long.MaxValue-writes)throw new InvalidOperationException("Result batch group generation exhausted.");
                    }
                    for(int i=0;i<c.Operations.Length;i++)if(c.Operations[i].Kind==ResultOperationKind.Ready){var op=c.Operations[i];names.Writes[i].Validate(groupCommandBacking.CrowdNames);names.Routes[i].Validate(Crowd);if(op.Group>=0)c.GroupSources[op.Group].Owner.ValidateWrite(c.GroupSources[op.Group],token,op.Member);}
                    for(int i=0;i<c.Operations.Length;i++)if(c.Operations[i].Kind==ResultOperationKind.Reissue){var op=c.Operations[i];names.Writes[i].Validate(groupCommandBacking.CrowdNames);c.GroupSources[op.Group].Owner.ValidateWrite(c.GroupSources[op.Group],token,op.Member);}
                    ValidateResultHeldPrefix(c,token);ValidateResultMovementInstall(c,scope,token);
                    // Original Step defers admission through both results and
                    // movement. These are its entry/result operation increments;
                    // no result-only admission is installed before movement.
                    if(!c.ComposedMovement&&!c.ResultOnly){admissionBatchDepth=1;authorityOperationIdentity+=2+c.ExtraOperations;}
                    if(c.NextPending!=null){c.RetiredPending=groupCommandBacking.Pending;groupCommandBacking.Pending=c.NextPending;pending=c.NextPending;}
                    if(c.NextProbes!=null){c.RetiredProbes=ownedProbes;ownedProbes=c.NextProbes;probes=c.NextProbes;}
                    if(c.NextLedger!=null){c.RetiredLedger=groupCommandBacking.Ledger;groupCommandBacking.Ledger=c.NextLedger;arrivalLedger=c.NextLedger;}
                    if(c.NextFailures!=null){c.RetiredFailures=groupCommandBacking.Failures;groupCommandBacking.Failures=c.NextFailures;technicalFailures=c.NextFailures;}
                    if(c.NextQueue!=null){c.RetiredQueue=liveProbeAnswers;liveProbeAnswers=c.NextQueue;}PublishResultFailureMapsNoThrow(c);PublishReissueMapsNoThrow(c);
                    ApplyResultHeldPrefixNoThrow(c,requests);
                    for(int i=0;i<c.Operations.Length;i++){
                        var op=c.Operations[i];var answer=names.Answers[i];var request=answer.Request;
                        if(op.Kind==ResultOperationKind.Ready){
                            if(op.Group>=0){int g=op.Group;if(c.RetiredGroups[g]==null)c.RetiredGroups[g]=c.GroupSources[g].Version;var member=(NavigationAuthorityMember)c.Groups[g].Members[op.Member];c.GroupSources[g].Owner.ApplyMemberNoThrow(c.Groups[g],op.Member,member.Data,member.ContactPhase,member.GoalLocation);}
                            names.Routes[i].ApplyNoThrow();names.Writes[i].ApplyNoThrow(true);var actor=names.Routes[i].Actor.Unit;actor.StorageOwner.SetInstalledNoThrow(names.Installed[i],op.Detached,null,out names.RetiredInstalled[i]);names.Installed[i]=null;
                            actor.FailedRequest=actor.FailedOrder=actor.FailedGroupId=actor.FailedRevision=0;actor.FailedGoal=null;actor.LastFailure=NavigationOutcome.Idle;actor.InstalledGroupId=request.MemberIdentity?.GroupId??0;actor.InstalledRevision=request.MemberIdentity?.OrderRevision??0;actor.InstalledRequest=request.Request;actor.InstalledOrder=request.Order;
                        }
                        if(op.Kind==ResultOperationKind.Reissue){int g=op.Group;if(c.RetiredGroups[g]==null)c.RetiredGroups[g]=c.GroupSources[g].Version;var member=(NavigationAuthorityMember)c.Groups[g].Members[op.Member];c.GroupSources[g].Owner.ApplyMemberNoThrow(c.Groups[g],op.Member,member.Data,member.ContactPhase,member.GoalLocation);names.Writes[i].ApplyClearFailureNoThrow();ClearReissueFailureNoThrow(op.FailureSource.Actor);requests.AppendOneNoThrow(op.Replacement.Request,op.Replacement);}
                        if(op.Kind==ResultOperationKind.Failure)ApplyResultFailureNoThrow(c,i);
                        if(op.Kind!=ResultOperationKind.Probe){c.RetiredTracking[i]=ExtractAnswerChargeNoThrow(answer);c.RetiredTransport[i]=transportOwner.ConsumePreparedNoThrow(request);}
                    }
                    if(names.Single){if(names.BarrierCount>0)c.RetiredBarrier=TakeBarrierAnswerNoThrow(names.BarrierIndex);else mailbox.TakeOneNoThrow(c.MailboxRetired);}else{c.RetiredBarrier=TakeAllBarrierAnswersNoThrow();mailbox.TakeBatchNoThrow(c.MailboxRetired);}answerCorridorBytes=c.AnswerBytes;installedCorridorBytes=c.CorridorBytes;installedGeometryBytes=c.GeometryBytes;
                    ApplyResultBatchRetirementsNoThrow(c);UnreachableResults+=c.UnreachableCount;OwnedOrdinaryFailurePublications+=c.FailureCount-c.InvalidReadyCount;
                    if(c.ResultOnly){if(PublishAdmissionOwnerNoThrow(c.HeldAdmission))c.HeldAdmission=null;authorityOperationIdentity=names.Operation+3+c.ExtraOperations;}ApplyResultMovementNoThrow(c);AppliedResults+=c.ReadyCount;RejectedResults+=c.RejectedCount;OwnedReadyPublications+=c.ReadyCount;OwnedProbeAnswerPublications+=c.ProbeCount;OwnedDiscardPublications+=c.DiscardCount;if(c.HasCorridorDiagnostic)LastCorridorFailure=c.CorridorDiagnostic;if(names.Answers.Length>0)OwnedResultBatchPublications++;c.TransferNoThrow();
                    long allocated=GC.GetAllocatedBytesForCurrentThread()-before;if(names.Answers.Length>0)LastOwnedResultBatchPublicationAllocatedBytes=allocated;if(c.ReadyCount>0)LastOwnedReadyPublicationAllocatedBytes=allocated;if(c.ProbeCount>0)LastOwnedProbeAnswerPublicationAllocatedBytes=allocated;if(c.DiscardCount>0)LastOwnedDiscardPublicationAllocatedBytes=allocated;if(c.FailureCount>0)LastOwnedOrdinaryFailurePublicationAllocatedBytes=allocated;if(c.ComposedMovement)LastOwnedMovementPublicationAllocatedBytes=allocated;
                }};
                try{using(EnterTransport()){
                    bool moved=false;try{if(!c.ComposedMovement&&!c.ResultOnly)ValidateCompatibilityMotion(motionWorkspace);scope.Publish(c,publish);if(!c.ComposedMovement&&!c.ResultOnly){StepMovementLocked(dt,motionWorkspace);moved=true;}}
                    finally{if(c.Published&&!c.ComposedMovement&&!c.ResultOnly){admissionBatchDepth--;if(moved)PublishAdmission();}}
                }}finally{if(c.Published){scope.ResolveCommitted();if(c.ComposedMovement)motionWorkspace?.Dispose();try{foreach(var row in c.HeldRows)if(row.Changed){if(row.Eligible)RouteRequested?.Invoke(row.Replacement.Replacement.Request);else MovementCancelled?.Invoke(row.Replacement.FailureSource.Id);}for(int i=0;i<c.Operations.Length;i++){var op=c.Operations[i];if(op.Kind==ResultOperationKind.Ready)RouteActivated?.Invoke(names.Answers[i].Request);else if(op.Kind==ResultOperationKind.Reissue)RouteRequested?.Invoke(op.Replacement.Request);else if(op.Kind==ResultOperationKind.Failure){RouteFailed?.Invoke(names.Answers[i].Request,op.FailureOutcome);MovementCancelled?.Invoke(names.Answers[i].Request.Entity);}}}finally{if(names.Answers.Length>0||!c.ComposedMovement||c.MovementAdmissionChanged)SignalHost();}}}return true;
            }catch(InvalidOperationException error){LastResultBatchFailure=error.Message;if(c?.Published==true)throw;return ResultBatchFalse(111,"invalid-operation");}finally{c?.Dispose();}}
        }
        private bool PrepareReadyIntoResultBatch(PreparedResultBatch c,int at,NavigationRetentionBudget.Scope scope)
        {
            var answer=c.Names.Answers[at];var request=answer.Request;ref var op=ref c.Operations[at];
            if(!c.Names.HasQualification[at])PrepareOwnedCorridorQualification(c.Names,at,scope);
            if(!c.Names.HasQualification[at]||c.Names.Writes[at]==null)return false;
            c.HasCorridorDiagnostic=true;c.CorridorDiagnostic=c.Names.QualificationFailures[at];
            using(EnterTransport())if(!ResultBatchSourceCurrent(c)||request.Session!=Generation||request.ProfileId!=profile.ProfileId||request.ProfileRevision!=profile.Revision||!(ReferenceEquals(request.BaseGeometry,navigationGeometry)||IsCurrentPhysicalAuthorityGeometry(request))||!CurrentMember(request)||!CurrentHeldBinding(request.HeldIdentity,request.Entity))return false;
            int actor=-1;for(int i=0;i<c.Actors.Length;i++)if(c.Actors[i].Id==request.Entity){actor=i;break;}if(actor<0||c.Actors[actor].Held)return false;
            for(int i=0;i<at;i++)if(c.Operations[i].Kind==ResultOperationKind.Ready&&c.Operations[i].Actor==actor)return false;
            bool valid=c.Names.Qualified[at];var actual=c.ActorPins[actor] as NavLiveUnitStorage;var unit=actual.Unit;
            if(valid){var previous=c.Actors[actor].Position;for(int i=0;i<answer.RouteLength;i++){var point=answer.ReadOnlyRouteBacking[i];if(!request.Geometry.SegmentFree(previous,point,request.Profile.Radius)){valid=false;break;}previous=point;}}
            if(valid)valid=Crowd.ValidateRoute(unit,c.Actors[actor].Position,unit.Location,request.Goal,answer.ReadOnlyRouteBacking,request.GoalLocation,out _);
            if(!valid){
                if(Math.Abs(c.Actors[actor].Position.X-request.Start.X)>1e-4||Math.Abs(c.Actors[actor].Position.Z-request.Start.Z)>1e-4)return PrepareMovedRejectedReady(c,at,scope);
                return PrepareFailureIntoResultBatch(c,at,scope,NavSolveStatus.Ready,true);
            }
            if(!PrepareAnswerInstallDestination(c.Names,at,scope))return false;
            op.Kind=ResultOperationKind.Ready;op.Actor=actor;op.Group=-1;op.Member=-1;op.Detached=answer.Corridor.Group==0||!memberGroups.TryGetValue(request.Entity,out var currentGroup)||currentGroup!=answer.Corridor.Group;
            if(op.Tracked)c.AnswerBytes-=answer.Corridor.EstimatedRetainedBytes;
            var old=c.ProjectedInstalled[actor];long oldBytes=old==null?0:old.Corridor.Corridor.EstimatedRetainedBytes+16L*old.Route.Count;
            c.CorridorBytes=checked(c.CorridorBytes-oldBytes+answer.Corridor.EstimatedRetainedBytes+16L*c.Names.Installed[at].Route.Count);c.ProjectedInstalled[actor]=c.Names.Installed[at];c.ProjectedDetached[actor]=op.Detached;c.GeometryBytes=ResultBatchGeometryBytes(c);
            if(c.HostBytes+c.AnswerBytes+c.CorridorBytes+c.GeometryBytes>LayeredNavigationScheduler.MaxRetainedBytes)return false;
            if(c.NextPending==null)c.NextPending=ResultBatchCopy(c,c.Pending,scope);c.NextPending.Remove(request.Entity);
            var ledger=c.NextLedger??c.Ledger;bool hasReservation=ledger.TryGetValue(request.Request,out _),historical=false;
            for(int i=0;i<ledger.Capacity;i++)if(ledger.TryReadSlot(i,out _,out var row)&&row.Entity==request.Entity&&row.State==NavArrivalReservationKind.InstalledExecution){historical=true;break;}
            if(hasReservation||historical){
                if(c.NextLedger==null)c.NextLedger=ResultBatchCopy(c,c.Ledger,scope);ledger=c.NextLedger;
                for(int i=0;i<ledger.Capacity;i++)if(ledger.TryReadSlot(i,out long key,out var row)&&row.Entity==request.Entity&&row.State==NavArrivalReservationKind.InstalledExecution)ledger.Remove(key);
                if(hasReservation){if(!(ledger.OwnerFor(request.Request)is NavigationLiveArrivalReservation source))return false;var installed=NavigationLiveArrivalReservation.CopyWithStateOwned(source,NavArrivalReservationKind.InstalledExecution,scope.OwnedReservation());try{ledger.SetPreparing(request.Request,installed.Entry,installed);}finally{installed.ReleaseStorage();}}
            }
            if(!PrepareResultBatchPrune(c,scope))return false;
            if((c.NextFailures??c.Failures).ContainsKey(request.Entity)){if(c.NextFailures==null)c.NextFailures=ResultBatchCopy(c,c.Failures,scope);c.NextFailures.Remove(request.Entity);}
            var observation=c.Names.GroupObservations[at];if(observation.Owner!=null){
                int group=-1;for(int i=0;i<c.GroupCount;i++)if(ReferenceEquals(c.GroupSources[i].Owner,observation.Owner)){group=i;break;}
                if(group<0){group=c.GroupCount++;c.GroupSources[group]=observation;}
                var previous=c.Groups[group]??observation.Version;int member=-1;for(int i=0;i<previous.Members.Count;i++)if(((NavigationAuthorityMember)previous.Members[i]).Data.Entity==request.Entity){member=i;break;}if(member<0)return false;
                var original=(NavigationAuthorityMember)previous.Members[member];var d=original.Data;var g=previous.Data;
                var values=new NavigationAuthorityMember.Values(d.Entity,d.Slot,d.Incarnation,d.OrderRevision,d.MobilityRevision,d.FormationReleased?GroupMemberPhase.Combat:GroupMemberPhase.March,d.Goal,d.Location,d.HasLocation,d.HasGoal,d.FormationReleased,d.Branch,d.Reservation,d.ContactTick,g.MarchVersion==1?0:d.ProgressRequest,g.MarchVersion==1?0:d.Progress,d.ProgressVersion,d.ProgressOffset);
                var groupValues=new NavigationAuthorityGroup.Values(g.WorldGeneration,g.GroupId,g.OrderRevision,g.CommandSequence,g.IssuedTick,g.MembershipRevision,g.JobId,g.ActionId,g.MarchVersion,g.MarchProfileRevision,g.TeamId,g.TargetId,g.MarchStretch,g.MarchVersion==1?0:g.MarchAnchor,g.MarchVersion==1?false:g.MarchInitialized,g.Origin,g.Kind,g.OriginalGoal,g.HasLocation,g.Location);
                var changed=NavigationAuthorityMember.CopyWithValuesOwned(original,values,scope.OwnedReservation());NavigationAuthorityGroup next=null;try{next=NavigationAuthorityGroup.CopyWithMemberAndMarchOwned(previous,member,changed,groupValues,scope.OwnedReservation());}finally{changed.ReleaseStorage();}c.Groups[group]?.ReleaseStorage();c.Groups[group]=next;op.Group=group;op.Member=member;
            }
            c.ReadyCount++;return true;
        }
    }
}
