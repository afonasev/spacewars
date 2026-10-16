using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        internal int OwnedOrdinaryFailurePublications{get;private set;}
        internal long LastOwnedOrdinaryFailurePublicationAllocatedBytes{get;private set;}=-1;
        private sealed partial class PreparedRebind
        {
            internal bool OrdinaryFailure,FailureReject,FailureTracked,FailureRetentionReady;private bool failureDestinations;
            internal int FailureActor;internal NavigationOutcome FailureOutcome;
            internal NavigationPreparedAnswerNames FailureNames;
            internal Func<int,bool> FailureRetentionCallback;internal PlayableDomain FailureRetentionDomain;
            internal PlayableDomain.NavigationRetentionSource FailureRetention;
            internal void SealOrdinaryFailureDestinations(){NextPending.Retain();NextReservations?.Retain();NextGoals?.Retain();NextFailures?.Retain();NextLedger?.Retain();NextArrival?.Retain();failureDestinations=true;}
            internal void TransferOrdinaryFailureNoThrow(){failureDestinations=false;}
            internal void ReleaseOrdinaryFailureEdges(){FailureNames?.Release();if(failureDestinations){NextPending?.Release();NextReservations?.Release();NextGoals?.Release();NextFailures?.Release();NextLedger?.Release();NextArrival?.Release();}}
        }
        private bool OrdinaryFailurePrefixNoOp(PreparedRebind c)
        {
            if(!physicalArrivalEnabled)return false;
            for(int slot=0;slot<c.SourceGoals.Capacity;slot++)if(c.SourceGoals.TryReadSlot(slot,out int entity,out _)&&!c.SourceAllocating.ContainsKey(entity)&&Crowd.TryGet(entity,out _)&&c.SourceIdentities.TryGetValue(entity,out var identity)&&!CurrentHeldBinding(identity,entity))return false;
            return true;
        }
        private bool OrdinaryFailureSourceCurrent(PreparedRebind c)
        {
            var names=c.FailureNames;var request=names.Answers[0].Request;
            if(!c.FailureRetentionReady||!AnswerNamesSourceCurrent(names)||hostAllocationThread!=0||commandGroup!=null||admissionBatchDepth!=0||!ReferenceEquals(committedHostRoot,c.Host)||!ReferenceEquals(authorityOwners,c.SourceCore)||!ReferenceEquals(groupCommandBacking,c.Backing)||!ReferenceEquals(admissionOwner,c.SourceAdmission)||authorityOperationIdentity!=c.Operation||requestSequence!=c.Sequence||admissionPublicationIdentity!=c.Publication||certifiedHostAttachmentIdentity!=c.Attachment||!ReferenceEquals(certifiedHost,c.Worker)||!ReferenceEquals(pending,c.SourcePending)||c.SourcePending.Revision!=c.PendingRevision||!c.SourcePending.TryGetValue(request.Entity,out var current)||!ReferenceEquals(current,request)||!ReferenceEquals(allocating,c.SourceAllocating)||c.SourceAllocating.Revision!=c.AllocatingRevision||!ReferenceEquals(movementIdentities,c.SourceIdentities)||c.SourceIdentities.Revision!=c.IdentityRevision||!ReferenceEquals(PreserveInstalledExecution,c.FailureRetentionCallback)||c.FailureRetentionDomain!=null&&!c.FailureRetentionDomain.NavigationRetentionCurrent(request.Entity,c.FailureRetention)||!ReferenceEquals(ownedAnswerTracking,c.Tracking)||(c.Tracking?.Revision??0)!=c.TrackingRevision||IsCorridorAnswerTracked(names.Answers[0])!=c.FailureTracked||answerCorridorBytes!=c.SourceAnswerBytes||!transportOwner.CanConsumePrepared(request)||!RebindArrivalsCurrent(c)||!MixedArrivalSourceCurrent(c)||Crowd.Units.Count!=c.Actors.Length||installedExecutions.Count!=c.InstalledCount)return false;
            foreach(var fact in c.Actors)if(!Crowd.TryGet(fact.Id,out var actor)||!fact.Matches(actor)||!ReferenceEquals(installedExecutions.OwnerFor(fact.Id),fact.Installed)||fact.Installed!=null&&installedExecutions.IsDetached(fact.Id)!=fact.Detached||fact.Group!=null&&!ReferenceEquals(fact.Group.ImmutableVersion,fact.Version))return false;
            return true;
        }
        private bool? TryPublishOwnedOrdinaryFailure(NavigationPreparedAnswerNames names,double stepDt=0,NavMotionWorkspace motionWorkspace=null)
        {
            if(names==null||names.Answers.Length!=1||names.Writes[0]==null)return null;var answer=names.Answers[0];var request=answer.Request;
            var effectiveStatus=names.SingleCapacityExceeded?NavSolveStatus.CapacityExceeded:answer.Status;
            if(request.Entity<0||request.StorageOwner==null||request.MemberIdentity==null||answer.StorageOwner==null||effectiveStatus==NavSolveStatus.Ready||answer.Corridor!=null&&!names.SingleCapacityExceeded)return null;
            int actors,attempts,leases,obstacles;long publication;
            using(EnterTransport()){
                if(!AnswerNamesSourceCurrent(names))return false;
                if(hostAllocationThread!=0||commandGroup!=null||admissionBatchDepth!=0||groupCommandBacking==null||!(pending is NavigationCommandSlots<int,NavigationRequest>)||!(allocating is NavigationCommandSlots<int,NavigationRequest>)||!(movementIdentities is NavigationCommandSlots<int,string>)||!(retainedGoals is NavigationCommandSlots<int,NavPoint>)||!(reservations is NavigationCommandSlots<int,NavPoint>)||!(technicalFailures is NavigationCommandSlots<int,NavSolveStatus>)||!pending.TryGetValue(request.Entity,out var current)||!ReferenceEquals(current,request)||allocating.ContainsKey(request.Entity)||!transportOwner.CanConsumePrepared(request))return null;
                actors=Crowd.Units.Count;attempts=liveArrivalTables?.Count??0;leases=liveArrivalLeaseTable?.Count??0;obstacles=navigationGeometry.Obstacles.Count;publication=admissionPublicationIdentity;
                foreach(var actor in Crowd.Units)if(actor.StorageOwner==null)return null;
            }
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,publication,out var scope))return false;
            using(scope){PreparedRebind c=null;try{
                var r=scope.OwnedReservation();c=PreparedRebind.Create(actors,0,r,requests:1,answers:1,attempts:attempts,leases:leases);c.OrdinaryFailure=true;c.FailureNames=NavPhysicalMacro.Acquire(c,names);
                using(NavStorageScratch.Reserve(r,RebindStagingScratchBound(actors,1,obstacles))){
                    using(EnterTransport()){
                        if(!AnswerNamesSourceCurrent(names)||Crowd.Units.Count!=actors||admissionPublicationIdentity!=publication)return false;
                        c.Host=NavPhysicalMacro.Acquire(c,committedHostRoot);c.SourceCore=NavPhysicalMacro.Acquire(c,authorityOwners);c.Backing=NavPhysicalMacro.Acquire(c,groupCommandBacking);c.SourceAdmission=NavPhysicalMacro.Acquire(c,admissionOwner);c.SourcePending=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,NavigationRequest>)pending);c.PendingRevision=c.SourcePending.Revision;c.SourceAllocating=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,NavigationRequest>)allocating);c.AllocatingRevision=c.SourceAllocating.Revision;c.SourceIdentities=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,string>)movementIdentities);c.IdentityRevision=c.SourceIdentities.Revision;
                        c.Operation=authorityOperationIdentity;c.Sequence=c.NextSequence=requestSequence;c.Publication=publication;c.Worker=certifiedHost;c.Attachment=certifiedHostAttachmentIdentity;c.InstalledCount=installedExecutions.Count;c.Tracking=NavPhysicalMacro.Acquire(c,ownedAnswerTracking);c.TrackingRevision=c.Tracking?.Revision??0;c.SourceAnswerBytes=c.PostAnswerBytes=answerCorridorBytes;c.FailureTracked=IsCorridorAnswerTracked(answer);if(c.FailureTracked)c.PostAnswerBytes-=answer.Corridor.EstimatedRetainedBytes;c.FailureRetentionCallback=PreserveInstalledExecution;
                        ObserveRebindActors(c);if(!ObserveRebindArrivals(c))return null;ObserveMixedArrivalSource(c);c.FailureActor=-1;for(int i=0;i<c.Actors.Length;i++)if(c.Actors[i].Id==request.Entity){c.FailureActor=i;break;}
                        if(c.FailureActor<0||RebindAssigned(c,request)||request.Session!=Generation||request.ProfileId!=profile.ProfileId||request.ProfileRevision!=profile.Revision||!(ReferenceEquals(request.BaseGeometry,navigationGeometry)||IsCurrentPhysicalAuthorityGeometry(request))||!CurrentMember(request)||!CurrentHeldBinding(request.HeldIdentity,request.Entity)||!OrdinaryFailurePrefixNoOp(c))return null;
                    }
                    var fact=c.Actors[c.FailureActor];bool keep=fact.Held||fact.Moving;
                    if(!keep&&c.FailureRetentionCallback!=null){var domain=c.FailureRetentionCallback.Target as PlayableDomain;if(domain==null||!domain.IsNavigationRetentionCallback(c.FailureRetentionCallback))return null;c.FailureRetentionDomain=domain;c.FailureRetention=domain.ObserveNavigationRetention(request.Entity);keep=c.FailureRetention.Keep;}
                    c.FailureReject=!keep;c.FailureRetentionReady=true;if(c.FailureReject&&fact.Mobility==long.MaxValue)return false;
                    c.FailureOutcome=effectiveStatus==NavSolveStatus.UnreachableInGraph?NavigationOutcome.Unreachable:NavigationOutcome.Rejected;if(c.FailureOutcome==NavigationOutcome.Unreachable){if(c.NextUnreachable==int.MaxValue)return false;c.NextUnreachable++;}
                    NavUnit.PrepareStopBacking();c.NextPending=CopyOrdinaryRebindMap(c,c.SourcePending,r);c.NextPending.Remove(request.Entity);
                    bool technical=effectiveStatus==NavSolveStatus.InvalidEndpoint||effectiveStatus==NavSolveStatus.BlockedConnector||effectiveStatus==NavSolveStatus.CapacityExceeded;
                    if(c.SourceFailures.ContainsKey(request.Entity)||technical){c.NextFailures=CopyOrdinaryRebindMap(c,c.SourceFailures,r,technical&&!c.SourceFailures.ContainsKey(request.Entity)?checked(c.SourceFailures.Count+1):0);c.NextFailures.Remove(request.Entity);if(technical)c.NextFailures.SetPreparing(request.Entity,effectiveStatus);c.NextFailures.Seal();}
                    for(int slot=0;slot<c.SourceLedger.Capacity;slot++)if(c.SourceLedger.TryReadSlot(slot,out long key,out var row)&&row.Entity==request.Entity&&row.State==NavArrivalReservationKind.PendingRequest){if(c.NextLedger==null)c.NextLedger=CopyOrdinaryRebindMap(c,c.SourceLedger,r);c.NextLedger.Remove(key);}
                    if(!PrepareRebindRetainedPrune(c,scope))return false;
                    if(c.SourceReservations.ContainsKey(request.Entity)){c.NextReservations=CopyOrdinaryRebindMap(c,c.SourceReservations,r);c.NextReservations.Remove(request.Entity);c.NextReservations.Seal();}
                    if(c.SourceGoals.ContainsKey(request.Entity)){c.NextGoals=CopyOrdinaryRebindMap(c,c.SourceGoals,r);c.NextGoals.Remove(request.Entity);c.NextGoals.Seal();}
                    c.NextPending.Seal();c.NextLedger?.Seal();c.NextArrival?.SealPrepared();if(stepDt==0)c.Admission=PrepareAdmissionProjection(groups,c.NextPending,c.Sequence,checked(publication+1),c.Operation,scope,c.SourceAdmission);
                }
                c.SealDestinations();scope.Attach(c);Action<object> publish=_=>{
                    long before=GC.GetAllocatedBytesForCurrentThread();using(var mailbox=Answers.EnterPreparedWrite()){
                        if(!OrdinaryFailureSourceCurrent(c)||scope.IsCancelled||c.Operation>long.MaxValue-(stepDt>0?4:1)||OwnedOrdinaryFailurePublications==int.MaxValue)throw new InvalidOperationException("Prepared ordinary failure lost source.");ValidateRebindRetirements(c);names.Writes[0].Validate(c.Backing.CrowdNames);if(names.BarrierCount==0)mailbox.ValidateTakeOne(names.MailboxRevision,names.SourceAnswerCount,c.RetiredAnswers);
                        if(stepDt>0){admissionBatchDepth=1;authorityOperationIdentity=c.Operation+2;}
                        c.RetiredPending=c.Backing.Pending;c.Backing.Pending=c.NextPending;pending=c.NextPending;
                        if(c.NextFailures!=null){c.RetiredFailures=c.Backing.Failures;c.Backing.Failures=c.NextFailures;technicalFailures=c.NextFailures;}if(c.NextLedger!=null){c.RetiredLedger=c.Backing.Ledger;c.Backing.Ledger=c.NextLedger;arrivalLedger=c.NextLedger;}
                        if(c.NextArrival!=null){c.RetiredArrival=liveArrivalTables;liveArrivalTables=c.NextArrival;arrivalAttempts=c.NextArrival.Ordered;arrivalProviders=c.NextArrival.References;arrivalProviderIds=c.NextArrival.Ids;}ApplyRebindRetirementsNoThrow(c);
                        if(c.NextReservations!=null){c.RetiredReservations=c.Backing.Reservations;c.Backing.Reservations=c.NextReservations;reservations=c.NextReservations;}if(c.NextGoals!=null){c.RetiredGoals=c.Backing.Goals;c.Backing.Goals=c.NextGoals;retainedGoals=c.NextGoals;}
                        var fact=c.Actors[c.FailureActor];var actor=fact.Actor;if(c.FailureReject){actor.ApplyPreparedStopNoThrow(fact.Mobility+1,false,out var global,out var local);c.RetiredRequests[0]=global;c.RetiredRequests[1]=local;actor.Outcome=c.FailureOutcome;}
                        names.Writes[0].ApplyNoThrow(false);actor.FailedRequest=request.Request;actor.FailedOrder=request.Order;actor.FailedGroupId=request.MemberIdentity.GroupId;actor.FailedRevision=request.MemberIdentity.OrderRevision;actor.FailedGoal=request.GoalLocation;actor.LastFailure=c.FailureOutcome;UnreachableResults=c.NextUnreachable;
                        c.Drains[0].RetiredTracking=ExtractAnswerChargeNoThrow(answer);answerCorridorBytes=c.PostAnswerBytes;if(names.BarrierCount==0)mailbox.TakeOneNoThrow(c.RetiredAnswers);else c.RetiredAnswers[0]=TakeBarrierAnswerNoThrow(names.BarrierIndex);c.Drains[0].RetiredTransport=transportOwner.ConsumePreparedNoThrow(request);
                        if(stepDt==0){if(PublishAdmissionOwnerNoThrow(c.Admission))c.Admission=null;authorityOperationIdentity=c.Operation+1;}OwnedOrdinaryFailurePublications++;c.TransferNoThrow();LastOwnedOrdinaryFailurePublicationAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-before;
                    }
                };
                try{using(EnterTransport()){bool moved=false;try{if(stepDt>0)ValidateCompatibilityMotion(motionWorkspace);scope.Publish(c,publish);if(stepDt>0){StepMovementLocked(stepDt,motionWorkspace);moved=true;}}finally{if(c.Published&&stepDt>0){admissionBatchDepth--;if(moved)PublishAdmission();}}}}
                finally{if(c.Published){scope.ResolveCommitted();try{RouteFailed?.Invoke(request,c.FailureOutcome);MovementCancelled?.Invoke(request.Entity);}finally{SignalHost();}}}return true;
            }catch(InvalidOperationException){if(c?.Published==true)throw;return false;}finally{c?.Dispose();}}
        }
    }
}
