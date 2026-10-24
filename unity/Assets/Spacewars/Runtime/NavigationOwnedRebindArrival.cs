using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        private struct RebindAttempt
        {
            internal NavigationLiveArrivalAttempt Owner;internal int Revision;
            internal bool Working,Reconstructing,Retiring,Restored,PlannedRetiring,Removed;
            internal long Retirement;
        }
        private struct RebindLease
        {
            internal NavigationLiveArrivalLease Owner,Previous,Next;internal NavigationArrivalLeaseValues Values;
            internal int Revision;internal bool Attached,PlannedRetiring;internal long Staging;
            internal ILayeredNavigationProvider Provider;internal CertifiedRouteKernel.Registered Projection;
        }
        private struct RebindRetirement{internal int Index;internal bool Lease;internal long Sequence;}
        private sealed partial class PreparedRebind
        {
            internal readonly RebindAttempt[] ArrivalAttempts;internal readonly RebindLease[] ArrivalLeases;internal readonly RebindRetirement[] ArrivalRetirements;
            internal NavigationLiveArrivalTables SourceArrival,NextArrival,RetiredArrival;internal NavigationLiveArrivalLeaseTable SourceLeases;
            internal NavigationLiveArrivalLease RetirementHead,RetirementTail;
            internal NavigationCommandSlots<long,NavigationArrivalReservation> SourceLedger,NextLedger,RetiredLedger;
            internal int ArrivalRevision,LeaseRevision,LeaseTokenRevision,LedgerRevision,RetirementCount,PendingRows,AllocatingRows,AssignedCount,OrdinaryCount;
            internal long SourceRetirement,NextRetirement,SourceRelease;
            internal void ReleaseRebindArrivalEdges(){SourceArrival?.Release();NextArrival?.ReleaseStorage();RetiredArrival?.Release();SourceLeases?.Release();SourceLedger?.Release();NextLedger?.ReleaseStorage();RetiredLedger?.Release();foreach(var row in ArrivalAttempts)row.Owner?.Release();foreach(var row in ArrivalLeases)row.Owner?.Release();}
        }
        private bool RebindHostBoundaryQuiescent()=>hostAllocationThread==0||hostAllocationThread==System.Threading.Thread.CurrentThread.ManagedThreadId&&certifiedHost!=null&&!certifiedHost.PumpExecuting;
        private bool ObserveRebindArrivals(PreparedRebind c)
        {
            if((liveArrivalTables?.Count??0)!=c.ArrivalAttempts.Length||(liveArrivalLeaseTable?.Count??0)!=c.ArrivalLeases.Length||arrivalProjectionLeases.Count!=c.ArrivalLeases.Length)return false;
            c.SourceArrival=NavPhysicalMacro.Acquire(c,liveArrivalTables);c.ArrivalRevision=c.SourceArrival?.Revision??0;c.SourceLeases=NavPhysicalMacro.Acquire(c,liveArrivalLeaseTable);c.LeaseRevision=c.SourceLeases?.Revision??0;c.LeaseTokenRevision=c.SourceLeases?.Tokens.Revision??0;c.RetirementHead=c.SourceLeases?.RetirementHead;c.RetirementTail=c.SourceLeases?.RetirementTail;c.SourceRetirement=c.NextRetirement=arrivalRetirementSequence;c.SourceRelease=arrivalReleaseSequence;
            c.SourceLedger=NavPhysicalMacro.Acquire(c,arrivalLedger as NavigationCommandSlots<long,NavigationArrivalReservation>);if(c.SourceLedger==null)return false;c.LedgerRevision=c.SourceLedger.Revision;
            for(int i=0;i<c.ArrivalAttempts.Length;i++){var attempt=c.SourceArrival.Attempts[c.SourceArrival.SortedId(i)];if(attempt.StorageOwner==null||!attempt.Work.HasPhysicalLifetime||attempt.OwnedProviders==null)return false;ref var row=ref c.ArrivalAttempts[i];row.Owner=NavPhysicalMacro.Acquire(c,attempt.StorageOwner);row.Revision=row.Owner.Revision;row.Working=attempt.Working;row.Reconstructing=attempt.Reconstructing;row.Retiring=attempt.Retiring;row.Restored=attempt.RestoredRetiringCompute;row.Retirement=attempt.Retirement;}
            for(int i=0;i<c.ArrivalLeases.Length;i++){var lease=c.SourceLeases.Tokens[c.SourceLeases.AcquisitionToken(i)];if(lease.StorageOwner==null||!ReferenceEquals(lease.Session,this)||lease.World!=Generation)return false;ref var row=ref c.ArrivalLeases[i];row.Owner=NavPhysicalMacro.Acquire(c,lease.StorageOwner);row.Values=NavigationArrivalLeaseValues.Read(lease);row.Revision=row.Owner.Revision;row.Attached=lease.Attached;row.Staging=lease.StagingBytes;row.Provider=lease.Provider;row.Projection=lease.Projection;row.Previous=row.Owner.RetirementPrevious;row.Next=row.Owner.RetirementNext;}
            return true;
        }
        private bool RebindArrivalsCurrent(PreparedRebind c)
        {
            if(!ReferenceEquals(liveArrivalTables,c.SourceArrival)||(c.SourceArrival?.Revision??0)!=c.ArrivalRevision||!ReferenceEquals(liveArrivalLeaseTable,c.SourceLeases)||(c.SourceLeases?.Revision??0)!=c.LeaseRevision||(c.SourceLeases?.Tokens.Revision??0)!=c.LeaseTokenRevision||!ReferenceEquals(c.SourceLeases?.RetirementHead,c.RetirementHead)||!ReferenceEquals(c.SourceLeases?.RetirementTail,c.RetirementTail)||(c.SourceArrival?.Count??0)!=c.ArrivalAttempts.Length||(c.SourceLeases?.Count??0)!=c.ArrivalLeases.Length||arrivalProjectionLeases.Count!=c.ArrivalLeases.Length||!ReferenceEquals(arrivalLedger,c.SourceLedger)||c.SourceLedger.Revision!=c.LedgerRevision||arrivalRetirementSequence!=c.SourceRetirement||arrivalReleaseSequence!=c.SourceRelease)return false;
            for(int i=0;i<c.ArrivalAttempts.Length;i++){var r=c.ArrivalAttempts[i];var a=r.Owner.Record;if(!ReferenceEquals(c.SourceArrival.Attempts.OwnerFor(c.SourceArrival.SortedId(i)),r.Owner)||r.Owner.Revision!=r.Revision||a.Working!=r.Working||a.Reconstructing!=r.Reconstructing||a.Retiring!=r.Retiring||a.RestoredRetiringCompute!=r.Restored||a.Retirement!=r.Retirement)return false;}
            for(int i=0;i<c.ArrivalLeases.Length;i++){var r=c.ArrivalLeases[i];var lease=r.Owner.Record;if(!ReferenceEquals(c.SourceLeases.Tokens.OwnerFor(c.SourceLeases.AcquisitionToken(i)),r.Owner)||r.Owner.Revision!=r.Revision||!r.Values.Same(NavigationArrivalLeaseValues.Read(lease))||lease.Attached!=r.Attached||lease.StagingBytes!=r.Staging||!ReferenceEquals(lease.Provider,r.Provider)||!ReferenceEquals(lease.Projection,r.Projection)||!ReferenceEquals(r.Owner.RetirementPrevious,r.Previous)||!ReferenceEquals(r.Owner.RetirementNext,r.Next))return false;}
            return true;
        }
        private static bool RebindAssigned(PreparedRebind c,NavigationRequest request)
        {foreach(var row in c.ArrivalAttempts)foreach(var intent in row.Owner.Record.Intents)if(intent.Request==request.Request)return true;return false;}
        // Simulate each legacy CancelArrival prune before inserting that row's
        // replacement. Retained work only needs scalar retirements; unretained
        // tables/provider edges are projected by the existing COW factories.
        private bool PrepareRebindCancellation(PreparedRebind c,int entity,NavigationRetentionBudget.Scope scope)
        {
            c.Allocating.Remove(entity);
            for(int slot=0;slot<c.NextLedger.Capacity;slot++)if(c.NextLedger.TryReadSlot(slot,out long key,out var entry)&&entry.Entity==entity&&entry.State==NavArrivalReservationKind.PendingRequest)c.NextLedger.Remove(key);
            return PrepareRebindRetainedPrune(c,scope);
        }
        private bool PrepareRebindRetainedPrune(PreparedRebind c,NavigationRetentionBudget.Scope scope)
        {
            for(int a=0;a<c.ArrivalAttempts.Length;a++){
                ref var attempt=ref c.ArrivalAttempts[a];if(attempt.Removed)continue;var record=attempt.Owner.Record;bool hasLease=false;
                for(int n=0;n<c.ArrivalLeases.Length;n++){ref var lease=ref c.ArrivalLeases[n];if(lease.Values.Attempt!=record.Id)continue;hasLease=true;
                    if(!lease.Values.Retiring&&!lease.PlannedRetiring&&!ReadyContainsRequest(c.NextLedger??c.SourceLedger,c.Allocating??c.SourceAllocating,lease.Values.Request)){
                        if(!c.SourceLeases.CanMarkRetiring(lease.Owner.Record)||lease.Revision==int.MaxValue)return false;lease.PlannedRetiring=true;c.ArrivalRetirements[c.RetirementCount++]=new RebindRetirement{Index=n,Lease=true,Sequence=checked(++c.NextRetirement)};
                    }
                }
                bool present=false;foreach(var request in record.Intents)if(ReadyContainsRequest(c.NextLedger??c.SourceLedger,c.Allocating??c.SourceAllocating,request.Request)){present=true;break;}if(present)continue;
                if(!attempt.Retiring&&!attempt.PlannedRetiring){if(attempt.Revision==int.MaxValue)return false;attempt.PlannedRetiring=true;c.ArrivalRetirements[c.RetirementCount++]=new RebindRetirement{Index=a,Sequence=checked(++c.NextRetirement)};}
                if(!hasLease&&!attempt.Working&&!attempt.Restored){
                    var r=scope.OwnedReservation();if(c.NextArrival==null){var source=c.SourceArrival;c.NextArrival=NavigationLiveArrivalTables.AllocatePrepared(source.Attempts.Capacity,source.References.Capacity,r,source,source.Ids.Capacity);using(EnterTransport()){if(!(c.OrdinaryFailure?OrdinaryFailureSourceCurrent(c):c.ArrivalRetry?ArrivalRetrySourceCurrent(c):OrdinaryRebindSourceCurrent(c)))throw new NavigationCommandRefused("Rebind arrival copy lost source.");c.NextArrival.CopySourceObserved(source);}}
                    foreach(var provider in record.OwnedProviders)c.NextArrival.DecrementProviderPreparing(provider,r);
                    c.NextArrival.RemoveAttemptPreparing(record);if(c.NextArrival.Ids.Count==0)c.NextArrival.ResetEmptyProvidersPreparing();attempt.Removed=true;
                }
            }
            int leases=0;for(int i=0;i<c.RetirementCount;i++)if(c.ArrivalRetirements[i].Lease)leases++;return c.LeaseRevision<=int.MaxValue-leases;
        }
        private void ValidateRebindRetirements(PreparedRebind c)
        {
            if(!RebindArrivalsCurrent(c))throw new NavigationCommandRefused("Rebind arrival source changed.");int leases=0;
            for(int i=0;i<c.RetirementCount;i++){var row=c.ArrivalRetirements[i];if(row.Lease){leases++;var lease=c.ArrivalLeases[row.Index];if(lease.Owner.Revision==int.MaxValue||!c.SourceLeases.CanMarkRetiring(lease.Owner.Record))throw new NavigationCommandRefused("Rebind lease retirement changed.");}else if(c.ArrivalAttempts[row.Index].Owner.Revision==int.MaxValue)throw new NavigationCommandRefused("Rebind attempt revision exhausted.");}
            if(c.LeaseRevision>int.MaxValue-leases)throw new NavigationCommandRefused("Rebind retirement table exhausted.");
        }
        private void ApplyRebindRetirementsNoThrow(PreparedRebind c)
        {
            for(int i=0;i<c.RetirementCount;i++){var row=c.ArrivalRetirements[i];if(row.Lease)c.SourceLeases.MarkRetiringNoThrow(c.ArrivalLeases[row.Index].Owner.Record,row.Sequence);else{var owner=c.ArrivalAttempts[row.Index].Owner;owner.Record.Retiring=true;owner.Record.Retirement=row.Sequence;owner.Record.RetirementReason=owner.RemovedReason.Value;owner.ChangedNoThrow();}}
            arrivalRetirementSequence=c.NextRetirement;
        }
    }
}
