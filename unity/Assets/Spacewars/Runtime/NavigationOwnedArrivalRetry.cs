using System;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        internal int OwnedArrivalRetryPublications{get;private set;}
        internal long LastOwnedArrivalRetryPublicationAllocatedBytes{get;private set;}=-1;
        private sealed partial class PreparedRebind
        {
            internal bool ArrivalRetry;private bool arrivalRetryDestinations;
            internal ArrivalAttempt RetryAttempt;internal NavigationArrivalWork RetryWork;
            internal NavigationHostArrivalHistory RetryHistory;internal NavigationHostArrivalIntents RetryIntents;
            internal NavArrivalStatus RetryStatus;internal long RetryEpoch;
            internal void SealArrivalRetryDestinations(){if(MixedArrival==null){Allocating.Retain();allocatingDestination=true;Identities.Retain();identityDestination=true;}NextArrival?.Retain();arrivalRetryDestinations=true;}
            internal void TransferArrivalRetryNoThrow(){arrivalRetryDestinations=false;}
            internal void ReleaseArrivalRetryEdges(){if(arrivalRetryDestinations)NextArrival?.Release();}
        }
        private bool ArrivalRetrySourceCurrent(PreparedRebind c)
        {
            if(!RebindHostBoundaryQuiescent()||!ReferenceEquals(authorityOwners,c.SourceCore)||!ReferenceEquals(groupCommandBacking,c.Backing)||!ReferenceEquals(admissionOwner,c.SourceAdmission)||authorityOperationIdentity!=c.Operation||requestSequence!=c.Sequence||admissionPublicationIdentity!=c.Publication||commandGroup!=null||admissionBatchDepth!=0||hostAllocationThread!=c.AllocationThread||certifiedHostAttachmentIdentity!=c.Attachment||!ReferenceEquals(certifiedHost,c.Worker)||!ReferenceEquals(allocating,c.SourceAllocating)||c.SourceAllocating.Revision!=c.AllocatingRevision||!ReferenceEquals(pending,c.SourcePending)||c.SourcePending.Revision!=c.PendingRevision||!ReferenceEquals(movementIdentities,c.SourceIdentities)||c.SourceIdentities.Revision!=c.IdentityRevision||Crowd.Units.Count!=c.Actors.Length||installedExecutions.Count!=c.InstalledCount||!RebindArrivalsCurrent(c)||!MixedArrivalSourceCurrent(c))return false;
            var a=c.RetryAttempt;
            if(a.Published||a.Retiring||a.Working||a.Reconstructing||a.Epoch!=c.RetryEpoch||a.SnapshotStatus!=c.RetryStatus||!ReferenceEquals(a.OwnedWork,c.RetryWork)||!ReferenceEquals(a.OwnedHistory,c.RetryHistory)||!ReferenceEquals(a.OwnedIntents,c.RetryIntents)||committedHostRoot==null||!committedHostRoot.ArrivalWork.TryGetValue(a.Id,out var work)||!ReferenceEquals(work,c.RetryWork)||!committedHostRoot.ArrivalHistory.TryGetValue(a.Id,out var history)||!ReferenceEquals(history,c.RetryHistory)||!committedHostRoot.ArrivalIntentSets.TryGetValue(a.Id,out var intents)||!ReferenceEquals(intents,c.RetryIntents))return false;
            foreach(var fact in c.Actors)if(!Crowd.TryGet(fact.Id,out var actor)||!fact.Matches(actor)||!ReferenceEquals(installedExecutions.OwnerFor(fact.Id),fact.Installed)||fact.Installed!=null&&installedExecutions.IsDetached(fact.Id)!=fact.Detached||fact.Group!=null&&!ReferenceEquals(fact.Group.ImmutableVersion,fact.Version))return false;
            foreach(var row in c.Retries)if(row.Selected&&(!allocating.TryGetValue(row.Entity,out var request)||!ReferenceEquals(request,row.Source.Request)))return false;
            return true;
        }
        // null selects the ordinary Ready proposal path. false is a refused
        // preparation; no part of a retry may escape into a second transaction.
        private bool? TryRetryOwnedArrival(ArrivalAttempt attempt)
        {
            int actors,attempts,leases,obstacles;long publication;
            using(EnterTransport()){
                if(groupCommandBacking==null||commandGroup!=null||admissionBatchDepth!=0||!RebindHostBoundaryQuiescent()||attempt.Published||attempt.Retiring||attempt.Working||attempt.Reconstructing||attempt.StorageOwner==null||attempt.SnapshotStatus==NavArrivalStatus.Pending||attempt.SnapshotStatus==NavArrivalStatus.BudgetExhausted||!(allocating is NavigationCommandSlots<int,NavigationRequest>)||!(pending is NavigationCommandSlots<int,NavigationRequest>)||!(movementIdentities is NavigationCommandSlots<int,string>))return false;
                actors=Crowd.Units.Count;attempts=liveArrivalTables?.Count??0;leases=liveArrivalLeaseTable?.Count??0;obstacles=navigationGeometry.Obstacles.Count;publication=admissionPublicationIdentity;
                foreach(var actor in Crowd.Units)if(actor.StorageOwner==null)return false;
            }
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Arrival,Generation,publication,out var scope))return false;
            using(scope){PreparedRebind c=null;try{
                var r=scope.OwnedReservation();c=PreparedRebind.Create(actors,attempt.Intents.Length,r,attempts:attempts,leases:leases);c.ArrivalRetry=true;
                using(NavStorageScratch.Reserve(r,RebindStagingScratchBound(actors,c.Retries.Length,obstacles))){
                    using(EnterTransport()){
                        if(Crowd.Units.Count!=actors||admissionPublicationIdentity!=publication||attempt.Published||attempt.Retiring||attempt.Working||attempt.Reconstructing)return false;
                        c.Host=NavPhysicalMacro.Acquire(c,committedHostRoot);c.SourceCore=NavPhysicalMacro.Acquire(c,authorityOwners);c.Backing=NavPhysicalMacro.Acquire(c,groupCommandBacking);c.SourceAdmission=NavPhysicalMacro.Acquire(c,admissionOwner);c.SourceAllocating=NavPhysicalMacro.Acquire(c,allocating as NavigationCommandSlots<int,NavigationRequest>);c.AllocatingRevision=c.SourceAllocating.Revision;c.SourcePending=NavPhysicalMacro.Acquire(c,pending as NavigationCommandSlots<int,NavigationRequest>);c.PendingRevision=c.SourcePending.Revision;c.SourceIdentities=NavPhysicalMacro.Acquire(c,movementIdentities as NavigationCommandSlots<int,string>);c.IdentityRevision=c.SourceIdentities.Revision;
                        c.Operation=authorityOperationIdentity;c.Sequence=c.NextSequence=requestSequence;c.Publication=publication;c.Worker=certifiedHost;c.Attachment=certifiedHostAttachmentIdentity;c.AllocationThread=hostAllocationThread;c.InstalledCount=installedExecutions.Count;
                        c.RetryAttempt=attempt;c.RetryEpoch=attempt.Epoch;c.RetryStatus=attempt.SnapshotStatus;c.RetryWork=attempt.OwnedWork;c.RetryHistory=attempt.OwnedHistory;c.RetryIntents=attempt.OwnedIntents;
                        ObserveRebindActors(c);if(!ObserveRebindArrivals(c))return false;
                        int selected=0;
                        for(int n=0;n<c.Retries.Length;n++){
                            var old=attempt.Intents[n];if(!allocating.TryGetValue(old.Entity,out var active)||!ReferenceEquals(active,old))continue;

                            if(old.StorageOwner==null||old.MemberIdentity==null)return false;
                            ref var row=ref c.Retries[n];row.Selected=true;selected++;row.Source=NavPhysicalMacro.Acquire(c,old.StorageOwner);row.Entity=old.Entity;row.ActorIndex=-1;
                            for(int i=0;i<actors;i++)if(c.Actors[i].Id==old.Entity){row.ActorIndex=i;break;}
                            var member=Member(old.Entity);row.Valid=member!=null&&memberGroups.TryGetValue(old.Entity,out var gid)&&gid==old.MemberIdentity.GroupId&&member.OrderRevision==old.MemberIdentity.OrderRevision&&row.ActorIndex>=0&&c.Actors[row.ActorIndex].Incarnation==old.MemberIdentity.Incarnation;
                            if(row.Valid){row.Group=memberGroups[old.Entity];row.Order=member.OrderRevision;row.Membership=groups[row.Group].MembershipRevision;}
                        }
                        if(selected==0)return null;
                        var occupancy=CaptureArrivalOccupancySnapshot(attempt.Input.Members);int ordinal=0;
                        foreach(var proposal in attempt.Work.Proposals.OrderBy(value=>value.Member.RequestSequence)){
                            for(int n=0;n<c.Retries.Length;n++){ref var row=ref c.Retries[n];if(!row.Selected||row.Source.Request.Request!=proposal.Member.RequestSequence||!CurrentArrivalIntent(row.Source.Request))continue;
                                row.ArrivalProposal=proposal;row.ArrivalOrder=ordinal++;row.ArrivalKind=occupancy.Occupants.Any(o=>o.BlocksPlacement&&ArrivalOverlap(proposal.Endpoint,proposal.Member.Radius,o.Location,o.Radius))?(byte)2:(byte)1;if(row.ArrivalKind==1)c.ArrivalAssignments++;break;
                            }
                        }
                        for(int n=0;n<c.Retries.Length;n++){ref var row=ref c.Retries[n];if(!row.Selected||row.ArrivalKind!=0)continue;if(CurrentArrivalIntent(row.Source.Request)&&attempt.SnapshotStatus!=NavArrivalStatus.Stale){row.ArrivalKind=3;row.ArrivalTerminalStatus=attempt.Work.MemberOutcomes.TryGetValue(row.Entity,out var outcome)?outcome:attempt.SnapshotStatus;c.ArrivalTerminalCount++;}else row.ArrivalKind=2;row.ArrivalOrder=ordinal++;}
                        c.ArrivalDispositionCount=ordinal;if(c.ArrivalAssignments==selected)return null;
                        if(c.ArrivalAssignments>0||c.ArrivalTerminalCount>0)ObserveMixedArrivalSource(c);
                        if(!ArrivalRetrySourceCurrent(c))return false;
                    }
                    c.Allocating=CopyOrdinaryRebindMap(c,c.SourceAllocating,r);c.Identities=CopyOrdinaryRebindMap(c,c.SourceIdentities,r);
                    var basis=(NavGeometry)c.SourceCore.NavigationGeometry.Facade;var currentProfile=(NavigationProfile)c.SourceCore.Profile.Facade;
                    for(int i=0;i<c.ArrivalDispositionCount;i++){
                        int at=ArrivalDispositionAt(c,i);ref var row=ref c.Retries[at];if(row.ArrivalKind!=2)continue;if(!row.Valid)continue;var old=row.Source.Request;var fact=c.Actors[row.ActorIndex];
                        row.Next=NavigationHostRequest.CreateOwned(Generation,checked(++c.NextSequence),row.Entity,old.Order,currentProfile.ForUnit(fact.Radius,fact.Speed,fact.TurnSpeed),RebindMovementGeometry(c,fact,basis),fact.Position,old.Goal,basis,RebindHeldIdentity(c,fact),new NavigationMemberIdentity(fact.Incarnation,fact.Mobility,row.Group,row.Order,row.Membership),fact.Location,old.GoalLocation,null,r);

                    }
                    if(c.ArrivalAssignments>0||c.ArrivalTerminalCount>0){if(!PrepareMixedArrival(c,scope))return false;}else for(int ordinal=0;ordinal<c.ArrivalDispositionCount;ordinal++)ApplyArrivalRetryPreparing(c,ref c.Retries[ArrivalDispositionAt(c,ordinal)]);
                    if(!PrepareRebindRetainedPrune(c,scope))return false;
                    if(attempt.StorageOwner.Revision>int.MaxValue-2)return false;
                    c.Allocating.Seal();c.Identities.Seal();c.NextArrival?.SealPrepared();c.Admission=PrepareAdmissionProjection(c.MixedGroups??groups,c.NextPending??pending,c.NextSequence,checked(publication+1),c.Operation,scope,c.SourceAdmission);
                }
                c.SealDestinations();scope.Attach(c);Action<object> publish=_=>{
                    long before=GC.GetAllocatedBytesForCurrentThread();if(!ArrivalRetrySourceCurrent(c)||scope.IsCancelled||c.Operation==long.MaxValue||OwnedArrivalRetryPublications==int.MaxValue||attempt.StorageOwner.Revision>int.MaxValue-2)throw new NavigationCommandRefused("Prepared arrival retry lost source.");ValidateRebindRetirements(c);if(c.ArrivalTerminalCount>0&&c.NextUnreachable<0)throw new InvalidOperationException("Arrival unreachable counter exhausted.");
                    if(c.MixedArrival!=null){using(var mailbox=Requests.EnterPreparedWrite()){mailbox.Validate(c.RequestsRevision,c.MixedArrival.Mailbox,c.MixedArrival.MailboxCount);for(int i=0;i<c.MixedArrival.BirthCount;i++)c.MixedArrival.Births[i].ValidateInstall(scope);InstallMixedArrivalNoThrow(c,scope);mailbox.InstallNoThrow(c.MixedArrival.Mailbox,c.MixedArrival.MailboxCount,c.MixedArrival.Committed.MailboxOwners);}}
                    else{c.RetiredAllocating=c.Backing.Allocating;c.Backing.Allocating=c.Allocating;allocating=c.Allocating;c.RetiredIdentities=c.Backing.Identities;c.Backing.Identities=c.Identities;movementIdentities=c.Identities;}
                    if(c.NextArrival!=null){c.RetiredArrival=liveArrivalTables;liveArrivalTables=c.NextArrival;arrivalAttempts=c.NextArrival.Ordered;arrivalProviders=c.NextArrival.References;arrivalProviderIds=c.NextArrival.Ids;}
                    ApplyTerminalArrivalScalarsNoThrow(c);attempt.Published=true;attempt.StorageOwner.ChangedNoThrow();ApplyRebindRetirementsNoThrow(c);requestSequence=c.NextSequence;if(PublishAdmissionOwnerNoThrow(c.Admission))c.Admission=null;authorityOperationIdentity=c.Operation+1;OwnedArrivalRetryPublications++;c.TransferNoThrow();LastOwnedArrivalRetryPublicationAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-before;
                };
                using(EnterTransport())scope.Publish(c,publish);scope.ResolveCommitted();for(int i=0;i<c.ArrivalDispositionCount;i++){var row=c.Retries[ArrivalDispositionAt(c,i)];if(row.ArrivalKind==3){var outcome=row.ArrivalTerminalStatus==NavArrivalStatus.UnreachableInGraph?NavigationOutcome.Unreachable:NavigationOutcome.Rejected;RouteFailed?.Invoke(row.Source.Request,outcome);MovementCancelled?.Invoke(row.Entity);}else if(row.Next!=null){EndpointAssigned?.Invoke(row.Next.Request);if(row.ArrivalKind==1)RouteRequested?.Invoke(row.Next.Request);}}SignalHost();return true;
            }catch(Exception error)when(NavigationCommandRefused.IsExpected(error,scope)){if(c?.Published==true)throw;return false;}finally{c?.Dispose();}}
        }
    }
}
