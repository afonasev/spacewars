using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        internal long LastOwnedPhysicalCompletionAllocatedBytes{get;private set;}=-1;
        private sealed class PreparedPhysicalCompletion:NavOwnedStorage,IDisposable
        {
            private static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(PreparedPhysicalCompletion));
            internal NavigationHostRoot Source,Next,RetiredHost;
            internal NavigationHostAnswer Answer;
            internal NavigationLiveRegistrations Registrations;
            internal NavigationLiveArrivalTables Arrival,NextArrival,RetiredArrival;internal NavigationLiveArrivalAttempt PrunedAttempt;internal int AttemptRevision;internal NavigationLiveArrivalLeaseTable LeaseTable;
            internal NavigationLiveArrivalLease Lease,RetiredLease;internal ArrivalProviderRecord Provider;
            internal int ArrivalRevision,LeaseTableRevision,LeaseRevision,LeaseSlot,Acquisition,ProviderReferences;
            internal long ReleaseSequence;
            internal NavigationHostRequest RetiredRequest;
            internal NavDeclarationStorage RetiredRecipe;
            internal NavigationCommandSlots<NavigationAnswer,NavigationHostAnswer> Tracking,NextTracking,RetiredTracking;
            internal NavMailboxRetention Retention;internal NavigationAnswer[] Items;
            internal Action<NavigationAnswer> Validate,Accept;
            internal CertifiedRouteKernel Worker;internal long Attachment,Operation,AnswerRevision,SourceBytes,PostBytes;
            internal int TrackingRevision,RegistrationRevision,RegistrationIndex,AnswerCount,AllocationThread;
            internal bool NewTransport,Published;private bool released,hostDestination,answerDestination,trackingDestination,transportDestination,arrivalDestination;
            private PreparedPhysicalCompletion():base(Layout){Retain();}
            internal static PreparedPhysicalCompletion Create(INavStorageReservation r){using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout);return a.Register(new PreparedPhysicalCompletion());}}
            internal void Seal(){if(NextArrival!=null){NextArrival.Retain();arrivalDestination=true;}Next.Retain();hostDestination=true;Answer.Retain();answerDestination=true;if(NextTracking!=null){NextTracking.Retain();trackingDestination=true;}if(NewTransport){Answer.RequestOwner.Retain();transportDestination=true;}}
            internal void TransferNoThrow(){hostDestination=answerDestination=trackingDestination=transportDestination=arrivalDestination=false;Published=true;}
            public void Dispose(){if(released)return;released=true;Release();}
            internal override void ReleaseOwner()=>Dispose();
            protected override void ReleaseEdges(){if(transportDestination)Answer?.RequestOwner.Release();Source?.Release();Next?.Release();RetiredHost?.Release();Answer?.Release();Registrations?.Release();PrunedAttempt?.Release();RetiredArrival?.Release();if(arrivalDestination)NextArrival?.Release();NextArrival?.ReleaseStorage();Arrival?.Release();LeaseTable?.Release();Lease?.Release();RetiredLease?.Release();RetiredRequest?.Release();RetiredRecipe?.Release();Tracking?.Release();NextTracking?.ReleaseStorage();RetiredTracking?.Release();Retention?.Release();if(hostDestination)Next?.Release();if(answerDestination)Answer?.Release();if(trackingDestination)NextTracking?.Release();}
        }
        private bool PhysicalCompletionSourceCurrent(PreparedPhysicalCompletion c)
            =>ReferenceEquals(committedHostRoot,c.Source)&&ReferenceEquals(ownedAnswerTracking,c.Tracking)&&(ownedAnswerTracking?.Revision??0)==c.TrackingRevision&&answerCorridorBytes==c.SourceBytes&&countedCorridorAnswers.Count==0&&ReferenceEquals(liveRegistrations,c.Registrations)&&c.Registrations.CanExtract(c.RegistrationIndex,c.Answer.Answer.Request,c.RegistrationRevision)&&c.RegistrationRevision<int.MaxValue&&authorityOperationIdentity==c.Operation&&commandGroup==null&&admissionBatchDepth==0&&ReferenceEquals(certifiedHost,c.Worker)&&certifiedHostAttachmentIdentity==c.Attachment&&hostAllocationThread==c.AllocationThread&&c.AllocationThread==0&&transportOwner.CanAnswerPrepared(c.Answer.RequestOwner,c.NewTransport)&&PhysicalCompletionLeaseCurrent(c);
        private bool PhysicalCompletionPruneIsNoOp(ArrivalProviderLease completed,ArrivalAttempt remove=null)
        {
            if(!(allocating is NavigationCommandSlots<int,NavigationRequest> ownedAllocating)||liveArrivalTables==null||liveArrivalLeaseTable==null)return false;
            for(int i=0;i<liveArrivalTables.Count;i++){var attempt=liveArrivalTables.Attempts[liveArrivalTables.SortedId(i)];bool present=false;for(int n=0;n<attempt.Intents.Length;n++)if(ReadyContainsRequest(arrivalLedger,ownedAllocating,attempt.Intents[n].Request)){present=true;break;}if(!present&&!ReferenceEquals(attempt,remove)){
                    bool retained=attempt.Working||attempt.RestoredRetiringCompute;
                    for(int n=0;n<liveArrivalLeaseTable.Count&&!retained;n++){var lease=liveArrivalLeaseTable.Tokens[liveArrivalLeaseTable.AcquisitionToken(n)];if(!ReferenceEquals(lease,completed)&&lease.Attempt==attempt.Id)retained=true;}
                    if(!attempt.Retiring||!retained)return false;
                }}
            for(int i=0;i<liveArrivalLeaseTable.Count;i++){var lease=liveArrivalLeaseTable.Tokens[liveArrivalLeaseTable.AcquisitionToken(i)];if(!ReferenceEquals(lease,completed)&&!lease.Retiring&&!ReadyContainsRequest(arrivalLedger,ownedAllocating,lease.Request))return false;}
            return true;
        }
        private bool PhysicalCompletionPrunedAttemptCurrent(ArrivalProviderLease lease,NavigationLiveArrivalAttempt owner)
        {
            if(owner==null)return PhysicalCompletionPruneIsNoOp(lease);var attempt=owner.Record;
            if(!attempt.Retiring||attempt.Working||attempt.Reconstructing||attempt.RestoredRetiringCompute||!attempt.Work.HasPhysicalLifetime||attempt.OwnedProviders==null||attempt.Id!=lease.Attempt||!liveArrivalTables.Attempts.TryGetValue(attempt.Id,out var current)||!ReferenceEquals(current,attempt)||!(allocating is NavigationCommandSlots<int,NavigationRequest> ownedAllocating))return false;
            for(int i=0;i<attempt.Intents.Length;i++)if(ReadyContainsRequest(arrivalLedger,ownedAllocating,attempt.Intents[i].Request))return false;
            for(int i=0;i<liveArrivalLeaseTable.Count;i++){var other=liveArrivalLeaseTable.Tokens[liveArrivalLeaseTable.AcquisitionToken(i)];if(!ReferenceEquals(other,lease)&&other.Attempt==attempt.Id)return false;}
            return PhysicalCompletionPruneIsNoOp(lease,attempt);
        }
        private bool PhysicalCompletionLeaseCurrent(PreparedPhysicalCompletion c)
        {
            if(c.Lease==null)return true;var lease=c.Lease.Record;
            return ReferenceEquals(c.Arrival,liveArrivalTables)&&c.Arrival.Revision==c.ArrivalRevision&&c.ArrivalRevision<int.MaxValue&&ReferenceEquals(c.LeaseTable,liveArrivalLeaseTable)&&c.LeaseTable.Revision==c.LeaseTableRevision&&c.LeaseTableRevision<int.MaxValue&&c.LeaseTable.Tokens.Revision<int.MaxValue&&c.Lease.Revision==c.LeaseRevision&&c.LeaseRevision<int.MaxValue&&lease.Attached&&lease.StagingBytes==0&&ReferenceEquals(lease.Session,this)&&lease.World==Generation&&arrivalReleaseSequence==c.ReleaseSequence&&c.ReleaseSequence<long.MaxValue&&c.LeaseTable.CanExtract(lease,out int slot,out int acquisition)&&slot==c.LeaseSlot&&acquisition==c.Acquisition&&c.Provider.References==c.ProviderReferences&&c.ProviderReferences>1&&c.Provider.StorageOwner.Revision<int.MaxValue&&c.Arrival.TryProviderSlots(c.Provider,out _,out _)&&ReferenceEquals(c.Provider.Provider,lease.Provider)&&(c.PrunedAttempt==null||c.PrunedAttempt.Revision==c.AttemptRevision)&&PhysicalCompletionPrunedAttemptCurrent(lease,c.PrunedAttempt);
        }
        // All destinations and the optional live lease extraction are prepared
        // before the sole Attach. A retired last-lease attempt is removed by COW.
        private bool? PublishOwnedPhysicalCompletion(NavigationRetentionBudget.Scope scope,NavigationHostRoot source,NavigationHostRoot next,NavigationHostRow row)
        {
            using(EnterTransport())if(!physicalArrivalEnabled||row.AnswerOwner==null||liveRegistrations==null||countedCorridorAnswers.Count!=0)return null;
            using(EnterTransport())if(row.ArrivalToken!=0){
                if(!arrivalProjectionLeases.TryGetValue(row.ArrivalToken,out var lease)||lease.StorageOwner==null||liveArrivalLeaseTable==null)return null;
                if(!PhysicalCompletionPruneIsNoOp(lease)&&(!arrivalAttempts.TryGetValue(lease.Attempt,out var attempt)||!PhysicalCompletionPrunedAttemptCurrent(lease,attempt.StorageOwner)))return null;
            }
            PreparedPhysicalCompletion c=null;try{
                var r=scope.OwnedReservation();c=PreparedPhysicalCompletion.Create(r);c.Source=NavPhysicalMacro.Acquire(c,source);c.Next=NavPhysicalMacro.Acquire(c,next);c.Answer=NavPhysicalMacro.Acquire(c,row.AnswerOwner);
                int capacity;bool add;
                using(EnterTransport()){
                    c.Registrations=NavPhysicalMacro.Acquire(c,liveRegistrations);c.RegistrationRevision=c.Registrations.Revision;c.RegistrationIndex=c.Registrations.IndexOf(row.Request);
                    c.Tracking=NavPhysicalMacro.Acquire(c,ownedAnswerTracking);c.TrackingRevision=c.Tracking?.Revision??0;c.SourceBytes=answerCorridorBytes;c.PostBytes=c.SourceBytes;
                    c.Worker=certifiedHost;c.Attachment=certifiedHostAttachmentIdentity;c.Operation=authorityOperationIdentity;c.AllocationThread=hostAllocationThread;c.NewTransport=c.Answer.RequestOwner.TransportOwner==null;
                    if(row.ArrivalToken!=0){
                        if(!arrivalProjectionLeases.TryGetValue(row.ArrivalToken,out var lease))return false;
                        c.Arrival=NavPhysicalMacro.Acquire(c,liveArrivalTables);c.ArrivalRevision=c.Arrival.Revision;c.LeaseTable=NavPhysicalMacro.Acquire(c,liveArrivalLeaseTable);c.LeaseTableRevision=c.LeaseTable.Revision;c.Lease=NavPhysicalMacro.Acquire(c,lease.StorageOwner);c.LeaseRevision=c.Lease.Revision;c.ReleaseSequence=arrivalReleaseSequence;
                        if(!c.LeaseTable.CanExtract(lease,out c.LeaseSlot,out c.Acquisition)||!arrivalProviders.TryGetValue(lease.Provider,out c.Provider))return false;c.ProviderReferences=c.Provider.References;
                        if(!PhysicalCompletionPruneIsNoOp(lease)){
                            if(!arrivalAttempts.TryGetValue(lease.Attempt,out var attempt)||!PhysicalCompletionPrunedAttemptCurrent(lease,attempt.StorageOwner))return false;
                            c.PrunedAttempt=NavPhysicalMacro.Acquire(c,attempt.StorageOwner);c.AttemptRevision=c.PrunedAttempt.Revision;
                        }
                    }
                    Answers.ObserveState(out c.AnswerRevision,out c.AnswerCount);Answers.ObserveRetainedBacking(out c.Items,out var retention,out c.Validate,out c.Accept);c.Retention=NavPhysicalMacro.Acquire(c,retention);
                    if(!PhysicalCompletionSourceCurrent(c)||c.AnswerCount==Answers.InstanceCapacity)return false;
                    if(row.Answer.Status==NavSolveStatus.Ready&&row.Answer.Corridor==null||row.Answer.Corridor!=null&&(!c.Registrations.TryGraphKey(row.Request,out var key)||row.Answer.Corridor.GraphKey!=key||row.Answer.Corridor.ProviderBinding!=row.Request.CertifiedProviderBinding))throw new InvalidOperationException("Prepared completion corridor lost its registration.");
                    add=row.Answer.Corridor!=null&&!IsCorridorAnswerTracked(row.Answer);capacity=c.Tracking?.Capacity??4;if(add&&c.Tracking?.Count==capacity){if(capacity>AuthoritySlotMap<NavigationAnswer,NavigationHostAnswer>.MaximumSlots/2)return false;capacity=checked(capacity*2);}
                    if(add)c.PostBytes=checked(c.SourceBytes+row.Answer.Corridor.EstimatedRetainedBytes);
                }
                if(add){c.NextTracking=NavigationCommandSlots<NavigationAnswer,NavigationHostAnswer>.CreateOwned(capacity,r,true);using(EnterTransport()){if(!PhysicalCompletionSourceCurrent(c))return false;if(c.Tracking!=null)c.NextTracking.CopyPreparedFrom(c.Tracking);}c.NextTracking.AddPreparing(row.Answer,c.Answer,c.Answer);c.NextTracking.Seal();}
                if(c.PrunedAttempt!=null){
                    var a=c.Arrival;c.NextArrival=NavigationLiveArrivalTables.AllocatePrepared(a.Attempts.Capacity,a.References.Capacity,r,a,a.Ids.Capacity);
                    using(EnterTransport()){if(!PhysicalCompletionSourceCurrent(c))return false;c.NextArrival.CopySourceObserved(a);}
                    c.NextArrival.DecrementProviderPreparing(c.Lease.Record.Provider,r);
                    foreach(var provider in c.PrunedAttempt.Record.OwnedProviders)c.NextArrival.DecrementProviderPreparing(provider,r);
                    c.NextArrival.RemoveAttemptPreparing(c.PrunedAttempt.Record);if(c.NextArrival.Ids.Count==0)c.NextArrival.ResetEmptyProvidersPreparing();c.NextArrival.SealPrepared();
                }
                c.Seal();scope.Attach(c);
                Action<object> publish=_=>{long before=GC.GetAllocatedBytesForCurrentThread();using(var mailbox=Answers.EnterPreparedWrite()){
                    if(!PhysicalCompletionSourceCurrent(c)||scope.IsCancelled)throw new InvalidOperationException("Prepared completion lost its source.");
                    mailbox.ValidateRetainedAppend(c.AnswerRevision,c.AnswerCount,c.Items,c.Retention,c.Validate,c.Accept);
                    c.RetiredHost=committedHostRoot;committedHostRoot=c.Next;if(c.NextTracking!=null){c.RetiredTracking=ownedAnswerTracking;ownedAnswerTracking=c.NextTracking;}answerCorridorBytes=c.PostBytes;
                    transportOwner.AnswerPreparedNoThrow(c.Answer.RequestOwner,c.NewTransport);mailbox.AppendRetainedNoThrow(row.Answer,c.Answer);c.Registrations.ExtractNoThrow(c.RegistrationIndex,out c.RetiredRequest,out c.RetiredRecipe);
                    if(c.Lease!=null){c.RetiredLease=c.LeaseTable.ExtractNoThrow(c.LeaseSlot,c.Acquisition);if(c.LeaseTable.Count==0){c.RetiredLease.RetiredLeaseTable=c.LeaseTable;liveArrivalLeaseTable=c.LeaseTable.TakeEmptyResetNoThrow();arrivalProjectionLeases=liveArrivalLeaseTable.Ordered;}arrivalReleaseSequence++;
                        if(c.NextArrival==null)c.Arrival.ChangeProviderReferencesNoThrow(c.Provider,c.ProviderReferences-1);
                        else{c.RetiredArrival=liveArrivalTables;liveArrivalTables=c.NextArrival;arrivalAttempts=c.NextArrival.Ordered;arrivalProviders=c.NextArrival.References;arrivalProviderIds=c.NextArrival.Ids;}LastOwnedLeaseReleaseAllocatedBytes=0;LastOwnedArrivalPruneAllocatedBytes=0;}
                    c.TransferNoThrow();LastOwnedPhysicalCompletionAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-before;
                }};
                using(EnterTransport())scope.Publish(c,publish);scope.ResolveCommitted();return true;
            }catch(InvalidOperationException){if(c?.Published==true)throw;return false;}finally{c?.Dispose();}
        }
    }
}
