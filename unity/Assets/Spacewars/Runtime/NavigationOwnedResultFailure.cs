using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        private sealed partial class PreparedResultBatch
        {
            internal NavigationCommandSlots<int,NavPoint> Reservations,NextReservations,RetiredReservations,NextGoals,RetiredGoals;
            internal NavigationCommandSlots<long,GroupOrderState> SourceGroups;
            internal NavigationCommandSlots<int,long> SourceMembers,SourceOrders;
            internal NavigationLiveArrivalTables NextArrival,RetiredArrival;
            internal int ReservationRevision,GroupsRevision,MembersRevision,OrdersRevision,RetirementCount,FailureCount,UnreachableCount,SourceUnreachable;
            internal long NextRetirement;internal bool PrefixNoOp;private bool failureDestinations;
            internal Func<int,bool> RetentionCallback;
            internal void SealResultFailureDestinations(){NextReservations?.Retain();NextGoals?.Retain();NextArrival?.Retain();failureDestinations=true;}
            internal void TransferResultFailureNoThrow(){failureDestinations=false;}
            internal void ReleaseResultFailureEdges(){Reservations?.Release();SourceGroups?.Release();SourceMembers?.Release();SourceOrders?.Release();if(failureDestinations){NextReservations?.Release();NextGoals?.Release();NextArrival?.Release();}NextReservations?.ReleaseStorage();NextGoals?.ReleaseStorage();NextArrival?.ReleaseStorage();RetiredReservations?.Release();RetiredGoals?.Release();RetiredArrival?.Release();}
        }
        private void ObserveResultFailureSources(PreparedResultBatch c)
        {
            c.Reservations=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,NavPoint>)reservations);c.ReservationRevision=c.Reservations.Revision;
            c.SourceGroups=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<long,GroupOrderState>)groups);c.GroupsRevision=c.SourceGroups.Revision;c.SourceMembers=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,long>)memberGroups);c.MembersRevision=c.SourceMembers.Revision;c.SourceOrders=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,long>)orders);c.OrdersRevision=c.SourceOrders.Revision;
            c.RetentionCallback=PreserveInstalledExecution;c.SourceUnreachable=UnreachableResults;c.NextRetirement=c.RetirementSequence;
        }
        private bool ResultFailureSourcesCurrent(PreparedResultBatch c)
        {
            if(!ReferenceEquals(reservations,c.Reservations)||c.Reservations.Revision!=c.ReservationRevision||!ReferenceEquals(groups,c.SourceGroups)||c.SourceGroups.Revision!=c.GroupsRevision||!ReferenceEquals(memberGroups,c.SourceMembers)||c.SourceMembers.Revision!=c.MembersRevision||!ReferenceEquals(orders,c.SourceOrders)||c.SourceOrders.Revision!=c.OrdersRevision||!ReferenceEquals(PreserveInstalledExecution,c.RetentionCallback)||UnreachableResults!=c.SourceUnreachable)return false;
            foreach(var op in c.Operations){var f=op.FailureSource;if(f.Actor==null)continue;var a=f.Actor;
                if(!Crowd.TryGet(f.Id,out var actor)||!f.Matches(actor)||!ReferenceEquals(installedExecutions.OwnerFor(f.Id),f.Installed)||f.Installed!=null&&installedExecutions.IsDetached(f.Id)!=f.Detached||f.Group!=null&&!ReferenceEquals(f.Group.ImmutableVersion,f.Version)||a.FailedRequest!=f.SourceFailedRequest||a.FailedOrder!=f.SourceFailedOrder||a.FailedGroupId!=f.SourceFailedGroup||a.FailedRevision!=f.SourceFailedRevision||a.LastFailure!=f.SourceLastFailure||!NavigationAdmissionMemberStorage.SameLocation(a.GoalLocation,f.SourceGoalLocation)||!NavigationAdmissionMemberStorage.SameLocation(a.FailedGoal,f.SourceFailedGoal)||!SameArrivalName(f.SourceLocationName,OwnedOrFlatName(a.Location))||!SameArrivalName(f.SourceGoalName,OwnedOrFlatName(a.GoalLocation))||!SameArrivalName(f.SourceFailureName,OwnedOrFlatName(a.FailedGoal))||op.RetentionDomain!=null&&!op.RetentionDomain.NavigationRetentionCurrent(f.Id,op.Retention))return false;
            }return true;
        }
        private bool ResultBatchPrefixNoOp(PreparedResultBatch c)
        {
            if(!physicalArrivalEnabled)return false;
            for(int slot=0;slot<c.Goals.Capacity;slot++)if(c.Goals.TryReadSlot(slot,out int id,out _)&&!c.Allocating.ContainsKey(id)&&Crowd.TryGet(id,out _)&&c.Identities.TryGetValue(id,out var held)&&!CurrentHeldBinding(held,id))return false;
            return true;
        }
        private bool ObserveResultBatchAttempts(PreparedResultBatch c)
        {
            for(int i=0;i<c.Attempts.Length;i++){var a=c.Arrival.Attempts[c.Arrival.SortedId(i)];if(a.StorageOwner==null||!a.Work.HasPhysicalLifetime||a.OwnedProviders==null)return false;ref var f=ref c.Attempts[i];f.Owner=NavPhysicalMacro.Acquire(c,a.StorageOwner);f.Revision=f.Owner.Revision;f.Working=a.Working;f.Reconstructing=a.Reconstructing;f.Retiring=a.Retiring;f.Restored=a.RestoredRetiringCompute;f.Retirement=a.Retirement;}return true;
        }
        private bool ResultBatchAttemptsCurrent(PreparedResultBatch c)
        {
            if(c.Arrival.Count!=c.Attempts.Length||arrivalProjectionLeases.Count!=0||(liveArrivalLeaseTable?.Count??0)!=0)return false;
            for(int i=0;i<c.Attempts.Length;i++){var f=c.Attempts[i];var a=f.Owner.Record;if(!ReferenceEquals(c.Arrival.Attempts.OwnerFor(c.Arrival.SortedId(i)),f.Owner)||f.Owner.Revision!=f.Revision||a.Working!=f.Working||a.Reconstructing!=f.Reconstructing||a.Retiring!=f.Retiring||a.RestoredRetiringCompute!=f.Restored||a.Retirement!=f.Retirement)return false;}return true;
        }
        private bool PrepareResultBatchPrune(PreparedResultBatch c,NavigationRetentionBudget.Scope scope)
        {
            for(int i=0;i<c.Attempts.Length;i++){ref var f=ref c.Attempts[i];if(f.Removed)continue;var a=f.Owner.Record;bool present=false;foreach(var intent in a.Intents)if(ReadyContainsRequest(c.NextLedger??c.Ledger,c.Allocating,intent.Request)){present=true;break;}if(present)continue;
                if(!f.Retiring&&!f.PlannedRetiring){if(f.Revision==int.MaxValue)return false;f.PlannedRetiring=true;c.Retirements[c.RetirementCount++]=new RebindRetirement{Index=i,Sequence=checked(++c.NextRetirement)};}
                if(!f.Working&&!f.Restored){var r=scope.OwnedReservation();if(c.NextArrival==null){c.NextArrival=NavigationLiveArrivalTables.AllocatePrepared(c.Arrival.Attempts.Capacity,c.Arrival.References.Capacity,r,c.Arrival,c.Arrival.Ids.Capacity);using(EnterTransport()){if(!ResultBatchSourceCurrent(c))return false;c.NextArrival.CopySourceObserved(c.Arrival);}}
                    foreach(var provider in a.OwnedProviders)c.NextArrival.DecrementProviderPreparing(provider,r);c.NextArrival.RemoveAttemptPreparing(a);if(c.NextArrival.Ids.Count==0)c.NextArrival.ResetEmptyProvidersPreparing();f.Removed=true;
                }
            }return true;
        }
        private void ObserveResultFailureActor(PreparedResultBatch c,NavUnit actor,ref RebindActor f)
        {
            f.Actor=actor;f.Owner=NavPhysicalMacro.Acquire(c,actor.StorageOwner);f.Global=NavPhysicalMacro.Acquire(c,actor.StorageOwner.Global);f.Local=NavPhysicalMacro.Acquire(c,actor.StorageOwner.Local);f.LocationOwner=NavPhysicalMacro.Acquire(c,actor.Location.NameReference.Owner);f.Position=actor.Position;f.Location=actor.Location;f.Id=actor.Id;f.Team=actor.Team;f.Incarnation=actor.Incarnation;f.Mobility=actor.MobilityRevision;f.Radius=actor.Radius;f.Speed=actor.Speed;f.TurnSpeed=actor.TurnSpeed;f.Held=actor.Held;f.Moving=actor.Moving;f.InstalledGroup=actor.InstalledGroupId;f.InstalledRevision=actor.InstalledRevision;f.InstalledRequest=actor.InstalledRequest;f.InstalledOrder=actor.InstalledOrder;f.Installed=NavPhysicalMacro.Acquire(c,installedExecutions.OwnerFor(actor.Id));f.Detached=f.Installed!=null&&installedExecutions.IsDetached(actor.Id);
            if(c.SourceMembers.TryGetValue(actor.Id,out var group)&&c.SourceGroups.OwnerFor(group) is NavigationLiveGroup live){f.Group=NavPhysicalMacro.Acquire(c,live);f.Version=NavPhysicalMacro.Acquire(c,live.ImmutableVersion);}
            f.SourceLocationName=OwnedOrFlatName(actor.Location);f.SourceGoalName=OwnedOrFlatName(actor.GoalLocation);f.SourceFailureName=OwnedOrFlatName(actor.FailedGoal);f.SourceGoalLocation=actor.GoalLocation;f.SourceFailedGoal=actor.FailedGoal;f.SourceFailedRequest=actor.FailedRequest;f.SourceFailedOrder=actor.FailedOrder;f.SourceFailedGroup=actor.FailedGroupId;f.SourceFailedRevision=actor.FailedRevision;f.SourceLastFailure=actor.LastFailure;
        }
        private static bool ResultBatchCapacityExceeded(PreparedResultBatch c,int at)
        {
            var answer=c.Names.Answers[at];if(answer.Corridor==null)return false;var request=answer.Request;int actor=-1;for(int i=0;i<c.Actors.Length;i++)if(c.Actors[i].Id==request.Entity){actor=i;break;}if(actor<0)return false;
            var prior=c.ProjectedInstalled[actor];long old=prior==null?0:prior.Corridor.Corridor.EstimatedRetainedBytes+16L*prior.Route.Count;long consumed=c.Operations[at].Tracked?answer.Corridor.EstimatedRetainedBytes:0;
            return c.HostBytes+c.AnswerBytes-consumed+c.CorridorBytes-old+ResultBatchGeometryBytes(c,actor,request)+answer.Corridor.EstimatedRetainedBytes+16L*(answer.RouteLength+1)>LayeredNavigationScheduler.MaxRetainedBytes;
        }
        private bool PrepareFailureIntoResultBatch(PreparedResultBatch c,int at,NavigationRetentionBudget.Scope scope,NavSolveStatus effectiveStatus,bool invalidReady=false)
        {
            var answer=c.Names.Answers[at];var request=answer.Request;ref var op=ref c.Operations[at];
            if(answer.StorageOwner==null||answer.Corridor!=null&&!invalidReady&&!(answer.Status==NavSolveStatus.Ready&&effectiveStatus==NavSolveStatus.CapacityExceeded)||request.MemberIdentity==null||c.Names.Writes[at]==null)return false;
            using(NavStorageScratch.Reserve(scope.OwnedReservation(),RebindStagingScratchBound(c.Actors.Length,1,navigationGeometry.Obstacles.Count))){using(EnterTransport()){
                if(!ResultBatchSourceCurrent(c)||request.Session!=Generation||request.ProfileId!=profile.ProfileId||request.ProfileRevision!=profile.Revision||!(ReferenceEquals(request.BaseGeometry,navigationGeometry)||IsCurrentPhysicalAuthorityGeometry(request))||!CurrentMember(request)||!CurrentHeldBinding(request.HeldIdentity,request.Entity)||c.Allocating.ContainsKey(request.Entity)||!Crowd.TryGet(request.Entity,out var actor))return false;
                if(!invalidReady)foreach(var attempt in c.Attempts)foreach(var intent in attempt.Owner.Record.Intents)if(intent.Request==request.Request)return false;
                ObserveResultFailureActor(c,actor,ref op.FailureSource);op.Actor=-1;for(int i=0;i<c.Actors.Length;i++)if(c.Actors[i].Id==request.Entity){op.Actor=i;break;}if(op.Actor<0)return false;
            }}
            bool keep=op.FailureSource.Held||op.FailureSource.Moving;if(!keep&&c.RetentionCallback!=null){var domain=c.RetentionCallback.Target as PlayableDomain;if(domain==null||!domain.IsNavigationRetentionCallback(c.RetentionCallback))return false;op.RetentionDomain=domain;op.Retention=domain.ObserveNavigationRetention(request.Entity);keep=op.Retention.Keep;}
            op.FailureReject=!keep;if(op.FailureReject&&op.FailureSource.Mobility==long.MaxValue)return false;
            if(op.Tracked)c.AnswerBytes-=answer.Corridor.EstimatedRetainedBytes;op.Kind=ResultOperationKind.Failure;op.FailureOutcome=effectiveStatus==NavSolveStatus.UnreachableInGraph?NavigationOutcome.Unreachable:NavigationOutcome.Rejected;if(op.FailureOutcome==NavigationOutcome.Unreachable)c.UnreachableCount++;c.FailureCount++;if(invalidReady){c.InvalidReadyCount++;c.RejectedCount++;}NavUnit.PrepareStopBacking();
            if(c.NextPending==null)c.NextPending=ResultBatchCopy(c,c.Pending,scope);c.NextPending.Remove(request.Entity);
            bool technical=effectiveStatus==NavSolveStatus.InvalidEndpoint||effectiveStatus==NavSolveStatus.BlockedConnector||effectiveStatus==NavSolveStatus.CapacityExceeded;var failures=c.NextFailures??c.Failures;
            if(failures.ContainsKey(request.Entity)||technical){int capacity=technical&&!failures.ContainsKey(request.Entity)?checked(failures.Count+1):0;if(c.NextFailures==null)c.NextFailures=ResultBatchCopy(c,c.Failures,scope,capacity);else if(capacity>c.NextFailures.Capacity){var grown=NavigationCommandSlots<int,NavSolveStatus>.CreateOwned(capacity,scope.OwnedReservation());try{grown.CopyPreparedFrom(c.NextFailures);}catch{grown.ReleaseStorage();throw;}c.NextFailures.ReleaseStorage();c.NextFailures=grown;}c.NextFailures.Remove(request.Entity);if(technical)c.NextFailures.SetPreparing(request.Entity,effectiveStatus);}
            var ledger=c.NextLedger??c.Ledger;for(int slot=0;slot<ledger.Capacity;slot++)if(ledger.TryReadSlot(slot,out long key,out var row)&&row.Entity==request.Entity&&row.State==NavArrivalReservationKind.PendingRequest){if(c.NextLedger==null)c.NextLedger=ResultBatchCopy(c,c.Ledger,scope);c.NextLedger.Remove(key);}
            if(!PrepareResultBatchPrune(c,scope))return false;
            if((c.NextReservations??c.Reservations).ContainsKey(request.Entity)){if(c.NextReservations==null)c.NextReservations=ResultBatchCopy(c,c.Reservations,scope);c.NextReservations.Remove(request.Entity);}
            if((c.NextGoals??c.Goals).ContainsKey(request.Entity)){if(c.NextGoals==null)c.NextGoals=ResultBatchCopy(c,c.Goals,scope);c.NextGoals.Remove(request.Entity);}return true;
        }
        private void ValidateResultFailurePublication(PreparedResultBatch c)
        {
            if(UnreachableResults>int.MaxValue-c.UnreachableCount||OwnedOrdinaryFailurePublications>int.MaxValue-(c.FailureCount-c.InvalidReadyCount))throw new InvalidOperationException("Failure result counters exhausted.");
            for(int i=0;i<c.Operations.Length;i++)if(c.Operations[i].Kind==ResultOperationKind.Failure)c.Names.Writes[i].Validate(groupCommandBacking.CrowdNames);
            for(int i=0;i<c.RetirementCount;i++)if(c.Attempts[c.Retirements[i].Index].Revision==int.MaxValue)throw new InvalidOperationException("Result attempt revision exhausted.");
        }
        private void PublishResultFailureMapsNoThrow(PreparedResultBatch c)
        {
            if(c.NextReservations!=null){c.RetiredReservations=groupCommandBacking.Reservations;groupCommandBacking.Reservations=c.NextReservations;reservations=c.NextReservations;}if(c.NextGoals!=null){c.RetiredGoals=groupCommandBacking.Goals;groupCommandBacking.Goals=c.NextGoals;retainedGoals=c.NextGoals;}
            if(c.NextArrival!=null){c.RetiredArrival=liveArrivalTables;liveArrivalTables=c.NextArrival;arrivalAttempts=c.NextArrival.Ordered;arrivalProviders=c.NextArrival.References;arrivalProviderIds=c.NextArrival.Ids;}
        }
        private static void ApplyResultFailureNoThrow(PreparedResultBatch c,int at)
        {
            var op=c.Operations[at];var actor=op.FailureSource.Actor;var request=c.Names.Answers[at].Request;if(op.FailureReject){actor.ApplyPreparedStopNoThrow(op.FailureSource.Mobility+1,false,out var global,out var local);c.DisplacedRoutes[at*2]=global;c.DisplacedRoutes[at*2+1]=local;actor.Outcome=op.FailureOutcome;}
            c.Names.Writes[at].ApplyNoThrow(false);actor.FailedRequest=request.Request;actor.FailedOrder=request.Order;actor.FailedGroupId=request.MemberIdentity.GroupId;actor.FailedRevision=request.MemberIdentity.OrderRevision;actor.FailedGoal=request.GoalLocation;actor.LastFailure=op.FailureOutcome;
        }
        private void ApplyResultBatchRetirementsNoThrow(PreparedResultBatch c)
        {
            for(int i=0;i<c.RetirementCount;i++){var row=c.Retirements[i];var owner=c.Attempts[row.Index].Owner;owner.Record.Retiring=true;owner.Record.Retirement=row.Sequence;owner.Record.RetirementReason=owner.RemovedReason.Value;owner.ChangedNoThrow();}arrivalRetirementSequence=c.NextRetirement;
        }
    }
}
