using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        private struct HeldRefreshOperation
        {
            internal ResultOperation Replacement;internal NavPoint Goal;internal bool Changed,Eligible;
            internal NavigationPreparedCrowdNameWrite Names;internal NavigationAdmissionStorage Admission;
        }
        private sealed partial class PreparedResultBatch
        {
            internal readonly HeldRefreshOperation[] HeldRows;internal readonly NavOwnedStorage[] HeldRoutes;
            internal bool HeldMode,HasHeldPrefix,ResultOnly;internal int HeldCount;internal PlayableAuthorityTick BarrierAuthority;
            internal NavigationAdmissionStorage HeldAdmission;
            internal void ReleaseHeldEdges(){foreach(var row in HeldRows){row.Replacement.FailureSource.Release();row.Names?.Release();row.Admission?.Release();}foreach(var route in HeldRoutes)route?.Release();HeldAdmission?.Release();}
        }
        private bool HeldSourcesCurrent(PreparedResultBatch c)
        {
            if(!c.HeldMode&&!c.HasHeldPrefix)return true;
            if(c.BarrierAuthority!=null&&!c.BarrierAuthority.MaintenancePendingFor(this))return false;
            foreach(var row in c.HeldRows){var f=row.Replacement.FailureSource;if(f.Actor==null)continue;var a=f.Actor;
                if(!Crowd.TryGet(f.Id,out var actor)||!f.Matches(actor)||!ReferenceEquals(installedExecutions.OwnerFor(f.Id),f.Installed)||f.Installed!=null&&installedExecutions.IsDetached(f.Id)!=f.Detached||f.Group!=null&&!ReferenceEquals(f.Group.ImmutableVersion,f.Version)||a.FailedRequest!=f.SourceFailedRequest||a.FailedOrder!=f.SourceFailedOrder||a.FailedGroupId!=f.SourceFailedGroup||a.FailedRevision!=f.SourceFailedRevision||a.LastFailure!=f.SourceLastFailure||!NavigationAdmissionMemberStorage.SameLocation(a.GoalLocation,f.SourceGoalLocation)||!NavigationAdmissionMemberStorage.SameLocation(a.FailedGoal,f.SourceFailedGoal)||!SameArrivalName(f.SourceLocationName,OwnedOrFlatName(a.Location))||!SameArrivalName(f.SourceGoalName,OwnedOrFlatName(a.GoalLocation))||!SameArrivalName(f.SourceFailureName,OwnedOrFlatName(a.FailedGoal)))return false;
            }return true;
        }
        private NavigationAdmissionStorage PrepareHeldAdmission(PreparedResultBatch c,NavigationAdmissionStorage previous,NavigationRetentionBudget.Scope scope)
        {
            using(NavStorageScratch.Reserve(scope.OwnedReservation(),checked(32L+c.GroupCount*(long)NavStorageLayout.ValueBytes(typeof(RebindGroup))))){
                var overlays=new RebindGroup[c.GroupCount];for(int i=0;i<overlays.Length;i++)overlays[i]=new RebindGroup{Source=c.GroupSources[i],Next=c.Groups[i]};
                return PrepareAdmissionProjection(c.SourceGroups,c.NextPending??c.Pending,c.NextSequence,checked(previous.Value.PublicationIdentity+1),c.Names.Operation,scope,previous,sourceProbes:c.NextProbes??c.Probes,rebindGroups:overlays,rebindGroupCount:overlays.Length);
            }
        }
        private void ObserveResultHeldRows(PreparedResultBatch c)
        {
            int at=0;for(int slot=0;slot<c.Goals.Capacity;slot++)if(c.Goals.TryReadSlot(slot,out int id,out var goal)){
                ref var row=ref c.HeldRows[at++];row.Goal=goal;if(c.Allocating.ContainsKey(id)||!Crowd.TryGet(id,out var actor)||!c.Identities.ContainsKey(id))continue;
                ObserveResultFailureActor(c,actor,ref row.Replacement.FailureSource);
            }
        }
        private bool PrepareResultHeldPrefix(PreparedResultBatch c,NavigationRetentionBudget.Scope scope)
        {
            if(!ObserveResultReissue(c))return false;
            using(NavStorageScratch.Reserve(scope.OwnedReservation(),RebindStagingScratchBound(c.Actors.Length,c.HeldRows.Length,navigationGeometry.Obstacles.Count))){
                for(int i=0;i<c.HeldRows.Length;i++){
                    ref var row=ref c.HeldRows[i];var f=row.Replacement.FailureSource;if(f.Actor==null)continue;var old=c.Identities[f.Id];
                    if(old==ResultHeldIdentity(c,f,false)||old==ResultHeldIdentity(c,f,true))continue;
                    row.Changed=true;c.HeldCount++;c.ExtraOperations=checked(c.ExtraOperations+1);if(!f.Held&&f.Mobility==long.MaxValue)return false;
                    NavLocation location=default;using(EnterTransport()){
                        if(!ResultBatchSourceCurrent(c))return false;
                        row.Eligible=geometry.IsFree(row.Goal,f.Radius)&&Crowd.Locate(row.Goal,f.Radius,out location)&&c.RequestCount+c.ReissueCount+c.RegistrationCount<RouteRequestCapacity;
                    }
                    for(int a=0;a<c.Actors.Length;a++)if(c.Actors[a].Id==f.Id)c.ProjectedDetached[a]=c.Installed[a]!=null;
                    if(!row.Eligible)continue;
                    NavigationLiveGroupObservation observation=default;foreach(var source in c.MarchSources)if(ReferenceEquals(source.Owner,f.Group)){observation=source;break;}if(observation.Owner==null)return false;
                    if(!PrepareResultReplacement(c,ref row.Replacement,f.Id,row.Goal,location,f.Held?f.Mobility:f.Mobility+1,observation,true,scope))return false;
                    int nameIndex=c.Names.Backing.CrowdNames.Find(f.Id);if(nameIndex<0)return false;
                    row.Names=NavigationPreparedCrowdNameWrite.CreateOwned(c.Names.Backing.CrowdNames,nameIndex,row.Replacement.Replacement.Request,scope.OwnedReservation());c.ExtraOperations=checked(c.ExtraOperations+1);
                }
            }return true;
        }
        private bool ResultHeldRequestCurrent(PreparedResultBatch c,NavigationRequest request)
        {
            if(!c.HasHeldPrefix)return true;
            if(request.Session!=Generation||request.ProfileId!=profile.ProfileId||request.ProfileRevision!=profile.Revision||!(ReferenceEquals(request.BaseGeometry,navigationGeometry)||IsCurrentPhysicalAuthorityGeometry(request))||!CurrentMember(request)||!CurrentHeldBinding(request.HeldIdentity,request.Entity))return false;
            foreach(var row in c.HeldRows)if(row.Changed&&row.Replacement.FailureSource.Id==request.Entity&&!row.Replacement.FailureSource.Held&&request.MemberIdentity!=null&&request.MemberIdentity.MobilityRevision!=row.Replacement.FailureSource.Mobility+1)return false;
            return true;
        }
        private void ValidateResultHeldPrefix(PreparedResultBatch c,NavigationLiveWriteToken token)
        {
            if(!c.HasHeldPrefix)return;
            foreach(var row in c.HeldRows)if(row.Eligible){row.Names.Validate(groupCommandBacking.CrowdNames);c.GroupSources[row.Replacement.Group].Owner.ValidateWrite(c.GroupSources[row.Replacement.Group],token,row.Replacement.Member);}
        }
        private void ApplyResultHeldPrefixNoThrow(PreparedResultBatch c,NavMailbox<NavigationRequest>.PreparedWriteGate requests)
        {
            if(!c.HasHeldPrefix)return;
            for(int i=0;i<c.HeldRows.Length;i++){ref var row=ref c.HeldRows[i];if(!row.Changed)continue;var f=row.Replacement.FailureSource;
                installedExecutions.SetDetachedNoThrow(f.Id,true);
                if(!f.Held){f.Actor.ApplyPreparedStopNoThrow(f.Mobility+1,false,out var global,out var local);c.HeldRoutes[2*i]=global;c.HeldRoutes[2*i+1]=local;RestoreHeldFailureNoThrow(f);}
                if(row.Eligible){int g=row.Replacement.Group;if(c.RetiredGroups[g]==null)c.RetiredGroups[g]=c.GroupSources[g].Version;var member=(NavigationAuthorityMember)c.Groups[g].Members[row.Replacement.Member];c.GroupSources[g].Owner.ApplyMemberNoThrow(c.Groups[g],row.Replacement.Member,member.Data,member.ContactPhase,member.GoalLocation);row.Names.ApplyClearFailureNoThrow();ClearReissueFailureNoThrow(f.Actor);requests.AppendOneNoThrow(row.Replacement.Replacement.Request,row.Replacement.Replacement);}
            }
        }
        // Barrier maintenance does not consume answers or advance movement. It
        // shares the result carrier's exact map/arrival/group ownership rules.
        private bool? TryOwnedHeldMaintenance(PlayableAuthorityTick authority)
        {
            int actors,attempts,groupCount,goals,barrier,mailbox;long operation,publication;
            using(EnterTransport()){
                if(!physicalArrivalEnabled||groupCommandBacking==null||hostAllocationThread!=0||admissionBatchDepth!=0||commandGroup!=null||liveArrivalTables==null||arrivalProjectionLeases.Count!=0||(liveArrivalLeaseTable?.Count??0)!=0||!(pending is NavigationCommandSlots<int,NavigationRequest>)||!(allocating is NavigationCommandSlots<int,NavigationRequest>)||!(groups is NavigationCommandSlots<long,GroupOrderState>)||!(orders is NavigationCommandSlots<int,long>)||!(memberGroups is NavigationCommandSlots<int,long>)||!(reservations is NavigationCommandSlots<int,NavPoint>)||!(retainedGoals is NavigationCommandSlots<int,NavPoint>)||!(movementIdentities is NavigationCommandSlots<int,string>)||!(technicalFailures is NavigationCommandSlots<int,NavSolveStatus>)||!(arrivalLedger is NavigationCommandSlots<long,NavigationArrivalReservation>)||probes.Count>0&&ownedProbes==null)return null;
                // The earlier owned arrival publication must have completed. A
                // worker-owned mutable attempt is selected before any writes.
                foreach(var pair in arrivalAttempts){var a=pair.Value;if(a.Working||a.Reconstructing||!a.Retiring&&!a.Published&&a.SnapshotStatus!=NavArrivalStatus.Pending&&a.SnapshotStatus!=NavArrivalStatus.BudgetExhausted)return null;}
                actors=Crowd.Units.Count;attempts=liveArrivalTables.Count;groupCount=groups.Count;goals=retainedGoals.Count;barrier=barrierAnswers.Count;mailbox=Answers.Count;operation=authorityOperationIdentity;publication=admissionPublicationIdentity;
            }
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,publication,out var scope))return false;
            using(scope){PreparedResultBatch c=null;NavigationPreparedAnswerNames names=null;try{
                names=NavigationPreparedAnswerNames.CreateOwned(barrier,barrier,scope.OwnedReservation());
                c=PreparedResultBatch.Create(0,actors,attempts,0,groupCount,scope.OwnedReservation(),goals);c.HeldMode=true;c.BarrierAuthority=authority;c.Names=NavPhysicalMacro.Acquire(c,names);
                using(EnterTransport()){
                    if(operation!=authorityOperationIdentity||publication!=admissionPublicationIdentity||actors!=Crowd.Units.Count||attempts!=liveArrivalTables.Count||groupCount!=groups.Count||goals!=retainedGoals.Count||barrier!=barrierAnswers.Count||mailbox!=Answers.Count)return false;
                    names.BarrierRoot=NavPhysicalMacro.Acquire(names,barrierPayloads);names.BarrierRevision=barrierPayloadRevision;names.Operation=operation;names.MailboxRevision=Answers.Revision;names.BarrierCount=barrier;names.SourceAnswerCount=mailbox;names.Backing=NavPhysicalMacro.Acquire(names,groupCommandBacking);names.ObservedAdmission=NavPhysicalMacro.Acquire(names,admissionOwner);names.Tracking=NavPhysicalMacro.Acquire(names,ownedAnswerTracking);names.TrackingRevision=ownedAnswerTracking?.Revision??0;names.TrackingBytes=answerCorridorBytes;for(int i=0;i<barrier;i++){var answer=barrierAnswers[i];if(answer.StorageOwner==null)return null;names.BarrierSource[i]=names.Answers[i]=answer;names.AnswerOwners[i]=NavPhysicalMacro.Acquire(names,answer.StorageOwner);}
                    c.Host=NavPhysicalMacro.Acquire(c,committedHostRoot);c.Pending=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,NavigationRequest>)pending);c.PendingRevision=c.Pending.Revision;c.Allocating=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,NavigationRequest>)allocating);c.AllocatingRevision=c.Allocating.Revision;
                    c.Probes=NavPhysicalMacro.Acquire(c,ownedProbes);c.ProbeRevision=c.Probes?.Revision??0;c.Ledger=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<long,NavigationArrivalReservation>)arrivalLedger);c.LedgerRevision=c.Ledger.Revision;c.Failures=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,NavSolveStatus>)technicalFailures);c.FailureRevision=c.Failures.Revision;
                    c.Goals=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,NavPoint>)retainedGoals);c.GoalRevision=c.Goals.Revision;c.Identities=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,string>)movementIdentities);c.IdentityRevision=c.Identities.Revision;
                    c.Arrival=NavPhysicalMacro.Acquire(c,liveArrivalTables);c.ArrivalRevision=c.Arrival.Revision;c.RetirementSequence=arrivalRetirementSequence;c.Queue=NavPhysicalMacro.Acquire(c,liveProbeAnswers);c.QueueRevision=c.Queue?.Revision??0;c.InstalledCount=installedExecutions.Count;
                    c.SourceAnswerBytes=c.AnswerBytes=answerCorridorBytes;c.SourceCorridorBytes=c.CorridorBytes=installedCorridorBytes;c.SourceGeometryBytes=c.GeometryBytes=installedGeometryBytes;c.HostBytes=hostRetainedBytes;
                    for(int i=0;i<actors;i++){var actor=Crowd.Units[i];c.Actors[i]=new ReadyActorFact(actor);c.ActorPins[i]=NavPhysicalMacro.Acquire(c,actor.StorageOwner);c.Installed[i]=NavPhysicalMacro.Acquire(c,installedExecutions.OwnerFor(actor.Id));c.ProjectedInstalled[i]=c.Installed[i];c.Detached[i]=c.ProjectedDetached[i]=c.Installed[i]!=null&&installedExecutions.IsDetached(actor.Id);}
                    ObserveResultFailureSources(c);if(!ObserveResultBatchAttempts(c)||!ObserveResultMovementGroups(c))return null;
                    int at=0;for(int slot=0;slot<c.Goals.Capacity;slot++)if(c.Goals.TryReadSlot(slot,out int id,out var goal)){
                        ref var row=ref c.HeldRows[at++];row.Goal=goal;if(c.Allocating.ContainsKey(id)||!Crowd.TryGet(id,out var actor)||!c.Identities.ContainsKey(id))continue;
                        ObserveResultFailureActor(c,actor,ref row.Replacement.FailureSource);
                    }
                }
                if(!ObserveResultReissue(c))return false;
                using(NavStorageScratch.Reserve(scope.OwnedReservation(),RebindStagingScratchBound(actors,goals,navigationGeometry.Obstacles.Count))){
                    for(int i=0;i<c.HeldRows.Length;i++){
                        ref var row=ref c.HeldRows[i];var f=row.Replacement.FailureSource;if(f.Actor==null)continue;var old=c.Identities[f.Id];
                        if(old==ResultHeldIdentity(c,f,false)||old==ResultHeldIdentity(c,f,true))continue;
                        row.Changed=true;c.HeldCount++;if(!f.Held&&f.Mobility==long.MaxValue)return false;
                        NavLocation location=default;using(EnterTransport()){
                            if(!ResultBatchSourceCurrent(c))return false;
                            row.Eligible=geometry.IsFree(row.Goal,f.Radius)&&Crowd.Locate(row.Goal,f.Radius,out location)&&c.RequestCount+c.ReissueCount+c.RegistrationCount<RouteRequestCapacity;
                        }
                        if(!row.Eligible)continue;
                        NavigationLiveGroupObservation observation=default;foreach(var source in c.MarchSources)if(ReferenceEquals(source.Owner,f.Group)){observation=source;break;}if(observation.Owner==null)return null;
                        if(!PrepareResultReplacement(c,ref row.Replacement,f.Id,row.Goal,location,f.Held?f.Mobility:f.Mobility+1,observation,true,scope))return false;
                        int nameIndex=names.Backing.CrowdNames.Find(f.Id);if(nameIndex<0)return false;row.Names=NavigationPreparedCrowdNameWrite.CreateOwned(names.Backing.CrowdNames,nameIndex,row.Replacement.Replacement.Request,scope.OwnedReservation());
                        var previous=names.ObservedAdmission;for(int j=0;j<i;j++)if(c.HeldRows[j].Admission!=null)previous=c.HeldRows[j].Admission;
                        row.Admission=PrepareHeldAdmission(c,previous,scope);
                    }
                }
                // Current owned groups refresh their members only through the
                // concrete writers; the final barrier publication reuses these.
                var last=names.ObservedAdmission;foreach(var row in c.HeldRows)if(row.Admission!=null)last=row.Admission;
                c.HeldAdmission=PrepareHeldAdmission(c,last,scope);
                using(EnterTransport())if(!ResultBatchSourceCurrent(c))return false;
                c.NextPending?.Seal();c.NextLedger?.Seal();c.NextFailures?.Seal();c.NextGoals?.Seal();c.NextReservations?.Seal();c.NextArrival?.SealPrepared();c.SealDestinations();NavUnit.PrepareStopBacking();scope.Attach(c);
                Action<object> publish=_=>{long before=GC.GetAllocatedBytesForCurrentThread();using(var requests=Requests.EnterPreparedWrite()){
                    if(!ResultBatchSourceCurrent(c)||scope.IsCancelled||operation>long.MaxValue-c.ReissueCount-1)throw new InvalidOperationException("Held maintenance lost final source.");
                    if(c.ReissueCount>0)requests.ValidateAppendBatch(c.RequestRevision,c.RequestCount,c.ReissueCount,groupCommandBacking.Mailbox,groupCommandBacking.MailboxOwners,c.RequestValidator,c.RequestTransfer);
                    var token=ObserveLiveWriteToken();for(int g=0;g<c.GroupCount;g++){int writes=0;foreach(var row in c.HeldRows)if(row.Eligible&&row.Replacement.Group==g)writes++;if(c.GroupSources[g].Generation>long.MaxValue-writes)throw new InvalidOperationException("Held group generation exhausted.");}
                    foreach(var row in c.HeldRows)if(row.Eligible){row.Names.Validate(groupCommandBacking.CrowdNames);c.GroupSources[row.Replacement.Group].Owner.ValidateWrite(c.GroupSources[row.Replacement.Group],token,row.Replacement.Member);}
                    ValidateResultFailurePublication(c);
                    if(c.NextPending!=null){c.RetiredPending=groupCommandBacking.Pending;groupCommandBacking.Pending=c.NextPending;pending=c.NextPending;}if(c.NextLedger!=null){c.RetiredLedger=groupCommandBacking.Ledger;groupCommandBacking.Ledger=c.NextLedger;arrivalLedger=c.NextLedger;}if(c.NextFailures!=null){c.RetiredFailures=groupCommandBacking.Failures;groupCommandBacking.Failures=c.NextFailures;technicalFailures=c.NextFailures;}PublishResultFailureMapsNoThrow(c);PublishReissueMapsNoThrow(c);
                    for(int i=0;i<c.HeldRows.Length;i++){ref var row=ref c.HeldRows[i];if(!row.Changed)continue;var f=row.Replacement.FailureSource;
                        installedExecutions.SetDetachedNoThrow(f.Id,true);
                        if(!f.Held){f.Actor.ApplyPreparedStopNoThrow(f.Mobility+1,false,out var global,out var local);c.HeldRoutes[2*i]=global;c.HeldRoutes[2*i+1]=local;RestoreHeldFailureNoThrow(f);}
                        if(row.Eligible){int g=row.Replacement.Group;if(c.RetiredGroups[g]==null)c.RetiredGroups[g]=c.GroupSources[g].Version;var member=(NavigationAuthorityMember)c.Groups[g].Members[row.Replacement.Member];c.GroupSources[g].Owner.ApplyMemberNoThrow(c.Groups[g],row.Replacement.Member,member.Data,member.ContactPhase,member.GoalLocation);row.Names.ApplyClearFailureNoThrow();ClearReissueFailureNoThrow(f.Actor);requests.AppendOneNoThrow(row.Replacement.Replacement.Request,row.Replacement.Replacement);if(PublishAdmissionOwnerNoThrow(row.Admission))row.Admission=null;authorityOperationIdentity++;}
                    }
                    ApplyResultBatchRetirementsNoThrow(c);if(PublishAdmissionOwnerNoThrow(c.HeldAdmission))c.HeldAdmission=null;authorityOperationIdentity=operation+c.ReissueCount+1;c.BarrierAuthority?.CompleteMaintenanceNoThrow();c.TransferNoThrow();LastOwnedHeldMaintenancePublicationAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-before;
                }};
                try{using(EnterTransport())scope.Publish(c,publish);}finally{if(c.Published){scope.ResolveCommitted();try{foreach(var row in c.HeldRows)if(row.Changed){if(row.Eligible)RouteRequested?.Invoke(row.Replacement.Replacement.Request);else MovementCancelled?.Invoke(row.Replacement.FailureSource.Id);}}finally{SignalHost();}}}return true;
            }catch(InvalidOperationException){if(c?.Published==true)throw;return false;}finally{c?.Dispose();names?.Dispose();}}
        }
        internal long LastOwnedHeldMaintenancePublicationAllocatedBytes{get;private set;}=-1;
        private static void RestoreHeldFailureNoThrow(RebindActor f,NavUnit target=null){var a=target??f.Actor;a.FailedRequest=f.SourceFailedRequest;a.FailedOrder=f.SourceFailedOrder;a.FailedGroupId=f.SourceFailedGroup;a.FailedRevision=f.SourceFailedRevision;a.FailedGoal=f.SourceFailedGoal;a.LastFailure=f.SourceLastFailure;}
    }
}
