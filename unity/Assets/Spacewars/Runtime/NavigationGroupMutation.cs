using System;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        private void OwnRestoredCommandBacking(NavigationAuthorityOwners source,long publication,NavigationRequest[] restored=null)
        {
            if(source?.HasCatalog!=true){OwnImportedLegacyBacking(restored);return;}
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Decode,Generation,admissionPublicationIdentity,out var scope))throw new InvalidOperationException("Restored live backing admission refused.");
            using(scope){
                NavigationGroupCommand candidate=null;NavigationCommandSlots<long,GroupOrderState> restoredGroups=null;
                NavigationCommandSlots<int,long> members=null,restoredOrders=null;NavigationCommandSlots<int,NavigationRequest> restoredPending=null,restoredAllocating=null;
                NavigationCommandSlots<int,NavPoint> restoredReservations=null,goals=null;NavigationCommandSlots<int,string> identities=null;NavigationCommandSlots<int,NavSolveStatus> failures=null;NavigationCommandSlots<long,NavigationArrivalReservation> ledger=null;
                NavigationRetentionBudget.Scope ledgerScratchScope=null;IDisposable ledgerScratch=null;
                try{
                    if(source.CollectionCatalog.Count!=source.ExpectedCollectionCount)throw new ArgumentException("Missing mandatory authority collection catalogs.");
                    RestoreGroupMap(allocating,((NavRetainedArray<int>)source.CollectionCatalog[9]).ReadOnlyBacking,((NavRetainedArray<int>)source.CollectionCatalog[8]).ReadOnlyBacking);
                    RestoreGroupMap(technicalFailures,((NavRetainedArray<int>)source.CollectionCatalog[11]).ReadOnlyBacking,((NavRetainedArray<int>)source.CollectionCatalog[10]).ReadOnlyBacking);
                    if(source.HasLedgerLayout){
                        source.ValidateLedgerLayout(scope.OwnedReservation());var permutation=source.LedgerRowOrder;
                        if(permutation.Count!=arrivalLedger.Count)throw new ArgumentException("Arrival ledger layout row count mismatch.");
                        // The ordinary detached map retains its sparse backing until
                        // publication replaces it. Publish forbids scratch in its own
                        // scope, so keep this paid temporary lane in a sibling scope.
                        // 2048 covers fixed collection/LINQ staging objects; the
                        // variable allowance includes both live and historical slots.
                        if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Decode,Generation,admissionPublicationIdentity,out ledgerScratchScope))throw new InvalidOperationException("Arrival ledger staging admission refused.");
                        ledgerScratch=NavStorageScratch.Reserve(ledgerScratchScope.OwnedReservation(),checked(2048L+128L*(arrivalLedger.Count+(long)source.LedgerLayout[0])));
                        var sorted=arrivalLedger.Keys.OrderBy(key=>key).ToArray();var keys=new long[permutation.Count];for(int i=0;i<keys.Length;i++)keys[i]=sorted[permutation[i]];
                        RestoreGroupMap(arrivalLedger,keys,source.LedgerLayout.ReadOnlyBacking);
                    }
                    var capacities=(NavRetainedArray<int>)source.CollectionCatalog[12];if(capacities.Count!=(source.HasProbeStorage?12:10))throw new ArgumentException("Missing actual navigation capacities.");
                    long operation=authorityOperationIdentity;
                    restoredGroups=CopyCommandMap(groups,0,scope,operation,null,capacities[0]);members=CopyCommandMap(memberGroups,0,scope,operation,null,capacities[1]);restoredOrders=CopyCommandMap(orders,0,scope,operation,null,capacities[2]);
                    restoredPending=CopyCommandMap(pending,0,scope,operation,r=>r.StorageOwner,capacities[3]);restoredAllocating=CopyCommandMap(allocating,0,scope,operation,r=>r.StorageOwner,capacities[4]);
                    restoredReservations=CopyCommandMap(reservations,0,scope,operation,null,capacities[5]);goals=CopyCommandMap(retainedGoals,0,scope,operation,null,capacities[6]);identities=CopyCommandMap(movementIdentities,0,scope,operation,null,capacities[7]);failures=CopyCommandMap(technicalFailures,0,scope,operation,null,capacities[8]);ledger=CopyCommandMap(arrivalLedger,0,scope,operation,null,capacities[9]);
                    foreach(var row in arrivalLedger){var owner=NavigationLiveArrivalReservation.CopyOwned(row.Value,scope.OwnedReservation());try{ledger.SetPreparing(row.Key,owner.Entry,owner);}finally{owner.ReleaseStorage();}}
                    if(source.IdentityCatalog.Count!=movementIdentities.Count)throw new ArgumentException("Movement identity catalog count mismatch.");
                    int identityIndex=0;foreach(var row in movementIdentities){var owner=source.IdentityCatalog[identityIndex++] as NavOwnedText;if((row.Value!=null)!=(owner!=null)||row.Value!=owner?.Value)throw new ArgumentException("Movement identity BODY index/value mismatch.");identities.SetPreparing(row.Key,owner?.Value,owner);}
                    candidate=NavigationGroupCommand.CreateOwned(Requests.InstanceCapacity,0,scope.OwnedReservation(),source.GroupCatalog.Count);
                    candidate.Groups=restoredGroups;candidate.MemberGroups=members;candidate.Orders=restoredOrders;candidate.Pending=restoredPending;candidate.Allocating=restoredAllocating;candidate.Reservations=restoredReservations;candidate.Goals=goals;candidate.Identities=identities;candidate.Failures=failures;candidate.Ledger=ledger;candidate.OwnMaps();
                    foreach(var entry in source.GroupCatalog){
                        var version=(NavigationAuthorityGroup)entry;
                        var live=NavigationLiveGroup.CreateOwned(version,scope);try{live.SealForInstall(scope);restoredGroups.SetPreparing(version.Data.GroupId,live.PreparedRecord(scope),live);candidate.Births[candidate.BirthCount++]=live;}finally{live.ReleaseStorage();}
                    }
                    candidate.AdmissionOwner=PrepareAdmissionProjection(restoredGroups,restoredPending,requestSequence,publication==0?1:publication,operation,scope,publication>0&&admissionOwner?.Value.PublicationIdentity!=publication?null:admissionOwner);
                    foreach(var queued in Requests.CopyItems())candidate.AppendMailbox(queued);
                    restoredGroups.Seal();candidate.Committed.CrowdNames=CaptureCrowdNameReferences(scope.OwnedReservation());
                    restoredGroups.Seal();members.Seal();restoredOrders.Seal();restoredPending.Seal();restoredAllocating.Seal();restoredReservations.Seal();goals.Seal();identities.Seal();failures.Seal();ledger.Seal();
                    scope.Attach(candidate);scope.Publish(candidate,_=>{using(EnterTransport()){
                        if(operation!=authorityOperationIdentity||groupCommandBacking!=null||scope.IsCancelled)throw new InvalidOperationException("Restored live backing source changed.");
                        for(int i=0;i<candidate.BirthCount;i++)candidate.Births[i].ValidateInstall(scope);
                        for(int i=0;i<candidate.BirthCount;i++)candidate.Births[i].ActivateNoThrow(this);
                        groupCommandBacking=candidate.Committed;if(PublishAdmissionOwnerNoThrow(candidate.AdmissionOwner))candidate.AdmissionOwner=null;groups=restoredGroups;memberGroups=members;orders=restoredOrders;pending=restoredPending;allocating=restoredAllocating;reservations=restoredReservations;retainedGoals=goals;movementIdentities=identities;technicalFailures=failures;arrivalLedger=ledger;using(var mailbox=Requests.EnterPreparedWrite())mailbox.InstallNoThrow(candidate.Mailbox,candidate.MailboxCount,candidate.Committed.MailboxOwners);candidate.Published=true;for(int i=0;i<candidate.BirthCount;i++)candidate.Births[i]=null;candidate.BirthCount=0;
                    }});scope.ResolveCommitted();
                }finally{
                    ledgerScratch?.Dispose();ledgerScratchScope?.Dispose();
                    candidate?.ReleaseStorage();restoredGroups?.ReleaseStorage();members?.ReleaseStorage();restoredOrders?.ReleaseStorage();restoredPending?.ReleaseStorage();restoredAllocating?.ReleaseStorage();restoredReservations?.ReleaseStorage();goals?.ReleaseStorage();identities?.ReleaseStorage();failures?.ReleaseStorage();ledger?.ReleaseStorage();
                }
            }
        }
        // Only the detached legacy candidate enters here. Rebuild ownership from
        // already validated values/aliases; no gameplay step, solver or clock runs.
        private void OwnImportedLegacyBacking(NavigationRequest[] restored)
        {
            if(groupCommandBacking!=null)throw new ArgumentException("Legacy import unexpectedly has physical backing.");
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Decode,Generation,admissionPublicationIdentity,out var scope))throw new InvalidOperationException("Legacy navigation ownership admission refused.");
            int count=checked(pending.Count+allocating.Count+probes.Count+Requests.Count+ProbeAnswerCount+Answers.Count+barrierAnswers.Count+(restored?.Length??0));
            using(scope){NavigationGroupCommand prepared=null;
            using(NavStorageScratch.Reserve(scope.OwnedReservation(),checked(8192L+1024L*count))){
                var r=scope.OwnedReservation();long operation=authorityOperationIdentity;NavigationGroupCommand c=null;
                NavigationCommandSlots<int,NavigationRequest> nextProbes=null;NavigationLiveProbeAnswers queue=null;NavigationCommandSlots<NavigationAnswer,NavigationHostAnswer> tracking=null;bool destinations=false;
                var requests=new System.Collections.Generic.Dictionary<NavigationRequest,NavigationHostRequest>(count,NavigationReferenceComparer<NavigationRequest>.Instance);
                var answers=new System.Collections.Generic.Dictionary<NavigationAnswer,NavigationHostAnswer>(count,NavigationReferenceComparer<NavigationAnswer>.Instance);
                NavLocation? Location(NavLocation? value){
                    if(!value.HasValue||value.Value.NameReference.Owner!=null)return value;
                    var location=value.Value;if(location.SurfaceId==NavLocation.FlatSurface)return new NavLocation(location.Position,NavLocation.FlatSurface);
                    foreach(var name in terrainNameOwners)if(name.Resolve()==location.SurfaceId)return new NavLocation(location.Position,name);
                    throw new ArgumentException("Legacy request refers to an undeclared terrain surface.");
                }
                NavigationRequest Input(NavigationRequest input){if(!requests.TryGetValue(input,out var owner)){
                    owner=NavigationHostRequest.CreateOwned(input.Session,input.Request,input.Entity,input.Order,input.Profile,input.Geometry,input.Start,input.Goal,input.BaseGeometry,input.HeldIdentity,input.MemberIdentity,Location(input.StartLocation),Location(input.GoalLocation),input.CertifiedProviderBinding,r);
                    requests.Add(input,owner);}return owner.Request;}
                NavigationHostAnswer Answer(NavigationAnswer answer){if(!answers.TryGetValue(answer,out var value)){value=ImportLegacyAnswer(Input(answer.Request).StorageOwner,answer,r);answers.Add(answer,value);if(countedCorridorAnswers.Contains(answer))tracking.SetPreparing(value.Answer,value,value);}return value;}
                try{
                    if(restored!=null)foreach(var input in restored)Input(input);
                    c=NavigationGroupCommand.CreateOwned(Requests.InstanceCapacity,0,r,groups.Count);
                    c.Groups=CopyCommandMap(groups,0,scope,operation);c.MemberGroups=CopyCommandMap(memberGroups,0,scope,operation);c.Orders=CopyCommandMap(orders,0,scope,operation);
                    c.Pending=CopyCommandMap(pending,0,scope,operation);c.Allocating=CopyCommandMap(allocating,0,scope,operation);c.Reservations=CopyCommandMap(reservations,0,scope,operation);c.Goals=CopyCommandMap(retainedGoals,0,scope,operation);c.Identities=CopyCommandMap(movementIdentities,0,scope,operation);c.Failures=CopyCommandMap(technicalFailures,0,scope,operation);c.Ledger=CopyCommandMap(arrivalLedger,0,scope,operation);
                    nextProbes=CopyCommandMap(probes,0,scope,operation);queue=NavigationLiveProbeAnswers.CreateOwned(ProbeAnswerCount,r);tracking=NavigationCommandSlots<NavigationAnswer,NavigationHostAnswer>.CreateOwned(Math.Max(4,countedCorridorAnswers.Count),r,true);
                    foreach(var row in groups){var live=CopyCommandGroup(row.Value,scope);try{c.Groups.SetPreparing(row.Key,live.PreparedRecord(scope),live);c.Births[c.BirthCount++]=live;}finally{live.ReleaseStorage();}}
                    foreach(var row in pending){var input=Input(row.Value);c.Pending.SetPreparing(row.Key,input,input.StorageOwner);}
                    foreach(var row in allocating){var input=Input(row.Value);c.Allocating.SetPreparing(row.Key,input,input.StorageOwner);}
                    foreach(var row in probes){var input=Input(row.Value);nextProbes.SetPreparing(row.Key,input,input.StorageOwner);}
                    foreach(var row in movementIdentities){if(row.Value==null){c.Identities.SetPreparing(row.Key,null);continue;}var text=NavOwnedText.Copy(row.Value,r);try{c.Identities.SetPreparing(row.Key,text.Value,text);}finally{text.Release();}}
                    foreach(var row in arrivalLedger){var owner=NavigationLiveArrivalReservation.CopyOwned(row.Value,r);try{c.Ledger.SetPreparing(row.Key,owner.Entry,owner);}finally{owner.ReleaseStorage();}}
                    foreach(var input in Requests.CopyItems())c.AppendMailbox(Input(input));
                    foreach(var answer in ProbeAnswerRows)queue.AppendPreparing(Answer(answer),r);queue.SealForUse(r);
                    var mailbox=Answers.CopyItems();var barrier=barrierAnswers.ToArray();foreach(var answer in mailbox)Answer(answer);foreach(var answer in barrier)Answer(answer);
                    c.Groups.Seal();c.MemberGroups.Seal();c.Orders.Seal();c.Pending.Seal();c.Allocating.Seal();c.Reservations.Seal();c.Goals.Seal();c.Identities.Seal();c.Failures.Seal();c.Ledger.Seal();nextProbes.Seal();tracking.Seal();
                    c.OwnMaps();c.Committed.CrowdNames=CaptureCrowdNameReferences(r);scope.Attach(c);
                    nextProbes.Retain();queue.Retain();tracking.Retain();destinations=true;var oldProbes=ownedProbes;var oldQueue=liveProbeAnswers;var oldTracking=ownedAnswerTracking;
                    // These writes target only the detached restore candidate.
                    // The outer Restore publication remains the sole live boundary.
                    {
                        for(int i=0;i<c.BirthCount;i++)c.Births[i].ValidateInstall(scope);
                        for(int i=0;i<c.BirthCount;i++)c.Births[i].ActivateNoThrow(this);
                        groupCommandBacking=c.Committed;groups=c.Groups;memberGroups=c.MemberGroups;orders=c.Orders;pending=c.Pending;allocating=c.Allocating;reservations=c.Reservations;retainedGoals=c.Goals;movementIdentities=c.Identities;technicalFailures=c.Failures;arrivalLedger=c.Ledger;
                        ownedProbes=nextProbes;probes=nextProbes;liveProbeAnswers=queue;ownedAnswerTracking=tracking;
                        using(var write=Requests.EnterPreparedWrite())write.InstallNoThrow(c.Mailbox,c.MailboxCount,c.Committed.MailboxOwners);c.Published=true;for(int i=0;i<c.BirthCount;i++)c.Births[i]=null;c.BirthCount=0;
                    }
                    oldProbes?.Release();oldQueue?.Release();oldTracking?.Release();probeAnswers.Clear();ClearLegacyProbePayloads();countedCorridorAnswers.Clear();
                    while(Answers.TryDiscard(out _)){}foreach(var answer in mailbox)if(!Answers.TryEnqueue(answers[answer].Answer))throw new ArgumentException("Legacy answer mailbox import exceeded capacity.");
                    foreach(var answer in queue)transportOwner.Answer(answer.Request);
                    ClearBarrierAnswers();foreach(var answer in barrier){var canonical=answers[answer].Answer;RestoreBarrierPayload(canonical);transportOwner.Answer(canonical.Request);}
                    if(restored!=null)for(int i=0;i<restored.Length;i++)restored[i]=requests[restored[i]].Request;
                    foreach(var attempt in arrivalAttempts.Values)for(int i=0;i<attempt.Intents.Length;i++)if(requests.TryGetValue(attempt.Intents[i],out var canonical))attempt.Intents[i]=canonical.Request;
                    prepared=c;
                }finally{
                    foreach(var answer in answers.Values)answer.ReleaseStorage();foreach(var request in requests.Values)request.ReleaseStorage();
                    if(c!=null){c.Groups?.ReleaseStorage();c.MemberGroups?.ReleaseStorage();c.Orders?.ReleaseStorage();c.Pending?.ReleaseStorage();c.Allocating?.ReleaseStorage();c.Reservations?.ReleaseStorage();c.Goals?.ReleaseStorage();c.Identities?.ReleaseStorage();c.Failures?.ReleaseStorage();c.Ledger?.ReleaseStorage();c.ReleaseStorage();}
                    if(destinations&&c?.Published!=true){nextProbes?.Release();queue?.Release();tracking?.Release();}nextProbes?.ReleaseStorage();queue?.ReleaseStorage();tracking?.ReleaseStorage();
                }
            }
            scope.Commit(prepared,_=>{});
            }
        }
        private GroupMemberPhase CurrentMemberPhase(int entity,NavUnit actor,GroupMemberState member)
            =>member.FormationReleased?GroupMemberPhase.Combat:actor.Held?GroupMemberPhase.Held:IsPending(entity)?GroupMemberPhase.PendingRoute:actor.Moving?GroupMemberPhase.March:actor.Outcome==NavigationOutcome.Arrived?GroupMemberPhase.Arrived:actor.Outcome==NavigationOutcome.Unreachable?GroupMemberPhase.Unreachable:actor.Outcome==NavigationOutcome.Blocked||actor.Outcome==NavigationOutcome.Rejected?GroupMemberPhase.Blocked:GroupMemberPhase.Stopped;
        private void RefreshOwnedGroupMembers()
        {
            if(!(memberGroups is NavigationCommandSlots<int,long> slots))return;
            for(int slot=0;slot<slots.Capacity;slot++)if(slots.TryReadSlot(slot,out int entity,out _))TryOwnedMemberState(entity,null,0);
        }
        // Prepared value/path mutation of the actual live group. Publication
        // never assigns an externally mutable member DTO into the owner.
        private bool? TryOwnedFormationContact(int entity,bool visibleContact,long tick)=>TryOwnedMemberState(entity,visibleContact,tick);
        private bool? TryOwnedMemberState(int entity,bool? visibleContact,long tick)
        {
            NavigationLiveGroupObservation observation;NavigationHostRoot host;NavigationAuthorityOwners authority;
            NavigationAdmissionStorage observedAdmission;
            long operation,publication;int index;GroupOrderState metadata;GroupMemberState value;
            using(EnterTransport()){
                if(!memberGroups.TryGetValue(entity,out var groupId))return true;
                if(!(groups is NavigationCommandSlots<long,GroupOrderState> slots)||!(slots.OwnerFor(groupId)is NavigationLiveGroup owner))return null;
                var record=groups[groupId];index=Array.FindIndex(record.Members,m=>m.Entity==entity);if(index<0)throw new InvalidOperationException("Owned group lost member index.");
                observation=owner.ObserveForCopy(ObserveLiveWriteToken());host=committedHostRoot;authority=authorityOwners;host?.Retain();authority?.Retain();operation=authorityOperationIdentity;publication=admissionPublicationIdentity;
                metadata=record;value=record.Members[index];observedAdmission=admissionOwner;observedAdmission?.Retain();
            }
            try{
                if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,publication,out var scope))return false;
                using(scope){
                    NavigationAuthorityMember member=null;NavigationAuthorityGroup version=null;NavigationPreparedGroupWrite write=null;bool published=false,admissionPublished=false;
                    try{
                        using(NavStorageScratch.Reserve(scope.OwnedReservation(),NavStorageLayout.ObjectBytesBound(typeof(GroupOrderState))+NavStorageLayout.ObjectBytesBound(typeof(GroupMemberState)))){
                            using(EnterTransport()){
                                if(operation!=authorityOperationIdentity||publication!=admissionPublicationIdentity||!observation.Owner.MatchesObservation(observation))return false;
                                metadata=metadata.CopyHeader();value=value.Copy();
                            }
                            if(visibleContact.HasValue){
                                if(value.FormationReleased!=visibleContact.Value){metadata.MarchAnchor=0;metadata.MarchInitialized=false;}
                                value.FormationReleased=visibleContact.Value;value.ContactTick=tick;value.ContactPhase="PostMovementVisibility";
                            }
                            using(EnterTransport()){
                                if(operation!=authorityOperationIdentity||!Crowd.TryGet(entity,out var actor)||actor.Incarnation!=value.Incarnation)return false;
                                value.MobilityRevision=actor.MobilityRevision;
                                if(allocating.ContainsKey(entity))value.Phase=GroupMemberPhase.Allocating;
                                else{value.Reservation=reservations.ContainsKey(entity)?entity:0;value.Phase=CurrentMemberPhase(entity,actor,value);}
                            }
                            if(!visibleContact.HasValue&&new NavigationAuthorityMember.Values(value).BitwiseSame(((NavigationAuthorityMember)observation.Version.Members[index]).Data)&&new NavigationAuthorityGroup.Values(metadata).BitwiseSame(observation.Version.Data))return true;
                            if(visibleContact.HasValue)member=NavigationAuthorityMember.CopyOwned(value,scope.OwnedReservation());
                            else member=NavigationAuthorityMember.CopyWithValuesOwned((NavigationAuthorityMember)observation.Version.Members[index],new NavigationAuthorityMember.Values(value),scope.OwnedReservation());
                            version=NavigationAuthorityGroup.CopyWithMemberAndMarchOwned(observation.Version,index,member,new NavigationAuthorityGroup.Values(metadata),scope.OwnedReservation());
                        }
                        write=NavigationPreparedGroupWrite.CreateOwned(observation,version,index,scope);
                        bool reuse;using(EnterTransport())reuse=operation==authorityOperationIdentity&&ReferenceEquals(observedAdmission,admissionOwner)&&admissionBatchDepth==0&&commandGroup==null&&observedAdmission!=null&&observedAdmission.Value.PublishedThroughRequestSequence==requestSequence&&observedAdmission.Groups.Count==groups.Count&&observedAdmission.Requests.Count==pending.Count+probes.Count&&observedAdmission.MatchesGroupReplacement(version);
                        if(reuse){observedAdmission.Retain();write.Admission=observedAdmission;}
                        else write.Admission=PrepareAdmissionProjection(groups,pending,requestSequence,checked(publication+1),operation,scope,observedAdmission,version);
                        write.RequireEdge(write.Admission);scope.Attach(write);
                        scope.Publish(write,_=>{using(EnterTransport()){
                            if(operation!=authorityOperationIdentity||publication!=admissionPublicationIdentity||!ReferenceEquals(observedAdmission,admissionOwner)||!ReferenceEquals(host,committedHostRoot)||!ReferenceEquals(authority,authorityOwners)||scope.IsCancelled)throw new InvalidOperationException("Contact mutation lost observed authority.");
                            write.ValidateInstall(scope,ObserveLiveWriteToken());write.ApplyNoThrow();admissionPublished=PublishAdmissionOwnerNoThrow(write.Admission);if(admissionPublished)write.Admission=null;authorityOperationIdentity=operation+1;published=true;
                        }});
                        scope.ResolveCommitted();write.ReleaseRetired();if(admissionPublished)SignalHost();return true;
                    }catch(InvalidOperationException){if(published)throw;return false;}
                    finally{write?.ReleaseStorage();version?.ReleaseStorage();member?.ReleaseStorage();}
                }
            }finally{observation.ReleasePins();host?.Release();authority?.Release();observedAdmission?.Release();}
        }
    }
}
