using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Concrete live reservation storage. Its strings and location selector are
    // owned; immutable BODY reservation versions are a separate capture seam.
    internal sealed class NavigationLiveArrivalReservation : NavOwnedStorage
    {
        private static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(NavigationLiveArrivalReservation))+NavStorageLayout.ObjectBytesBound(typeof(NavigationArrivalReservation));
        internal NavigationArrivalReservation Entry {get;private set;}
        private NavOwnedText proof,provider,profile,hold;private NavNameReference endpoint;private bool released;
        internal override bool IsImmutableCaptureRoot=>false;
        private NavigationLiveArrivalReservation():base(Layout){Retain();}
        internal static NavigationLiveArrivalReservation CopyOwned(NavigationArrivalReservation value,INavStorageReservation reservation)
        {
            if(value==null)throw new ArgumentNullException(nameof(value));
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(Layout);var owner=allocation.Register(new NavigationLiveArrivalReservation());
                try{
                    owner.proof=NavOwnedText.Copy(value.Proof,reservation);owner.provider=NavOwnedText.Copy(value.Provider,reservation);owner.profile=NavOwnedText.Copy(value.Profile,reservation);if(value.Hold!=null)owner.hold=NavOwnedText.Copy(value.Hold,reservation);
                    NavOwnedText privateName=null;try{owner.endpoint=NavigationHostRequest.OwnLocationName(value.Endpoint,reservation,out privateName);owner.endpoint.Owner.Retain();}finally{privateName?.Release();}
                    owner.Entry=value.Copy();owner.Entry.Proof=owner.proof.Value;owner.Entry.Provider=owner.provider.Value;owner.Entry.Profile=owner.profile.Value;owner.Entry.Hold=owner.hold?.Value;owner.Entry.Endpoint=new NavLocation(value.Endpoint.Position,owner.endpoint);return owner;
                }catch{owner.ReleaseStorage();throw;}
            }
        }
        internal static NavigationLiveArrivalReservation CopyWithStateOwned(NavigationLiveArrivalReservation source,NavArrivalReservationKind state,INavStorageReservation reservation)
        {
            NavStorageAdmission.RequireOwned(reservation,source);
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(Layout);var owner=allocation.Register(new NavigationLiveArrivalReservation());
                try{owner.proof=NavPhysicalMacro.Acquire(owner,source.proof);owner.provider=NavPhysicalMacro.Acquire(owner,source.provider);owner.profile=NavPhysicalMacro.Acquire(owner,source.profile);owner.hold=NavPhysicalMacro.Acquire(owner,source.hold);owner.endpoint=source.endpoint;owner.RequireEdge(owner.endpoint.Owner);owner.endpoint.Owner.Retain();owner.Entry=source.Entry.Copy();owner.Entry.State=state;return owner;}
                catch{owner.ReleaseStorage();throw;}
            }
        }
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){proof?.Release();provider?.Release();profile?.Release();hold?.Release();endpoint.Owner?.Release();Entry=null;}
    }
    public sealed partial class NavigationSession
    {
        // Called before entering the gameplay barrier's transport transaction.
        // A refused preparation leaves the ready attempt available for retry.
        private bool PrepareOwnedReadyArrivals()
        {
            if(!physicalArrivalEnabled)return false;
            ArrivalAttempt ready=null;
            using(EnterTransport())foreach(var pair in arrivalAttempts){var a=pair.Value;if(!a.Retiring&&!a.Working&&!a.Reconstructing&&!a.Published&&(a.SnapshotStatus!=NavArrivalStatus.Pending&&a.SnapshotStatus!=NavArrivalStatus.BudgetExhausted)){ready=a;break;}}
            if(ready!=null){var retry=TryRetryOwnedArrival(ready);if(!retry.HasValue)TryPublishOwnedArrival(ready);return true;}return false;
        }
        private bool TryPublishOwnedArrival(ArrivalAttempt attempt)
        {
            NavigationCommandSourcePins pins;NavigationHostRoot host;NavigationAuthorityOwners authority;long operation,publication,mailboxRevision;int mailboxCount;
            NavigationArrivalWork work;NavigationHostArrivalHistory history;NavigationHostArrivalIntents intents;CertifiedRouteKernel worker;long workerGeneration,attemptEpoch;
            using(EnterTransport()){
                if(commandGroup!=null||admissionBatchDepth!=0||hostAllocationThread!=0||attempt.Retiring||attempt.Working||attempt.Reconstructing||attempt.Published||attempt.SnapshotStatus!=NavArrivalStatus.Ready)return false;
                host=committedHostRoot;authority=authorityOwners;operation=authorityOperationIdentity;publication=admissionPublicationIdentity;Requests.ObserveState(out mailboxRevision,out mailboxCount);pins=new NavigationCommandSourcePins(host,authority,groupCommandBacking);
                work=attempt.OwnedWork;history=attempt.OwnedHistory;intents=attempt.OwnedIntents;
                worker=certifiedHost;workerGeneration=certifiedHostAttachmentIdentity;attemptEpoch=attempt.Epoch;
            }
            using(pins){
                if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Arrival,Generation,publication,out var scope))return false;
                using(scope){
                    NavigationGroupCommand candidate=null;NavigationCommandAuthority retired=null;
                    NavigationCommandSlots<long,GroupOrderState> nextGroups=null;NavigationCommandSlots<int,long> nextMembers=null,nextOrders=null;
                    NavigationCommandSlots<int,NavigationRequest> nextPending=null,nextAllocating=null;NavigationCommandSlots<int,NavPoint> nextReservations=null,nextGoals=null;NavigationCommandSlots<int,string> nextIdentities=null;NavigationCommandSlots<int,NavSolveStatus> nextFailures=null;NavigationCommandSlots<long,NavigationArrivalReservation> nextLedger=null;
                    long commit=0;
                    try{
                        using(NavStorageScratch.Reserve(scope.OwnedReservation(),checked(8192L+4096L*attempt.Intents.Length+512L*groups.Values.Sum(g=>g.Members.Length)))){
                            NavArrivalProposal[] proposals;
                            using(EnterTransport()){
                                if(operation!=authorityOperationIdentity||publication!=admissionPublicationIdentity)return false;
                                proposals=attempt.Work.Proposals.Where(p=>CurrentArrivalIntent(attempt.Intents.Single(r=>r.Request==p.Member.RequestSequence))).OrderBy(p=>p.Member.RequestSequence).ToArray();
                                // A mixed outcome must remain retryable until every exact active intent
                                // has a disposition in the same publication.
                                foreach(var intent in attempt.Intents)if(allocating.TryGetValue(intent.Entity,out var active)&&ReferenceEquals(active,intent)&&!proposals.Any(p=>p.Member.RequestSequence==intent.Request))return false;
                                if(Requests.Count+proposals.Length>Requests.InstanceCapacity)return false;
                            }
                            nextGroups=CopyCommandMap(groups,0,scope,operation);nextMembers=CopyCommandMap(memberGroups,0,scope,operation);nextOrders=CopyCommandMap(orders,0,scope,operation);
                            nextPending=CopyCommandMap(pending,proposals.Length,scope,operation,r=>r.StorageOwner);nextAllocating=CopyCommandMap(allocating,0,scope,operation,r=>r.StorageOwner);nextReservations=CopyCommandMap(reservations,proposals.Length,scope,operation);nextGoals=CopyCommandMap(retainedGoals,0,scope,operation);nextIdentities=CopyCommandMap(movementIdentities,0,scope,operation);nextFailures=CopyCommandMap(technicalFailures,0,scope,operation);nextLedger=CopyCommandMap(arrivalLedger,proposals.Length,scope,operation);
                            candidate=NavigationGroupCommand.CreateOwned(Requests.InstanceCapacity,proposals.Length,scope.OwnedReservation());
                            candidate.Groups=nextGroups;candidate.MemberGroups=nextMembers;candidate.Orders=nextOrders;candidate.Pending=nextPending;candidate.Allocating=nextAllocating;candidate.Reservations=nextReservations;candidate.Goals=nextGoals;candidate.Identities=nextIdentities;candidate.Failures=nextFailures;candidate.Ledger=nextLedger;candidate.OwnMaps();
                            using(NavStorageScratch.Reserve(scope.OwnedReservation(),32L+8L*mailboxCount))foreach(var queued in Requests.CopyItemsAtRevision(mailboxRevision))candidate.AppendMailbox(queued);
                            commit=arrivalCommitSequence;int issued=0;
                            foreach(var proposal in proposals){
                                var intent=attempt.Intents.Single(r=>r.Request==proposal.Member.RequestSequence);
                                using(EnterTransport()){if(operation!=authorityOperationIdentity)return false;var occupancy=CaptureArrivalOccupancySnapshot(attempt.Input.Members);if(occupancy.Occupants.Any(o=>o.BlocksPlacement&&ArrivalOverlap(proposal.Endpoint,proposal.Member.Radius,o.Location,o.Radius)))return false;}
                                var owner=NavigationHostRequest.CreateOwned(intent.Session,intent.Request,intent.Entity,intent.Order,intent.Profile,intent.Geometry,intent.Start,proposal.Endpoint.Position,intent.BaseGeometry,intent.HeldIdentity,intent.MemberIdentity,intent.StartLocation,proposal.Endpoint,null,scope.OwnedReservation());
                                try{
                                    var request=owner.Request;candidate.OwnIssued(issued,owner);candidate.Issued[issued++]=request;candidate.AppendMailbox(request);
                                    nextAllocating.Remove(request.Entity);nextPending.SetPreparing(request.Entity,request,owner);nextGoals.SetPreparing(request.Entity,request.Goal);nextReservations.SetPreparing(request.Entity,request.Goal);
                                    var row=new NavigationArrivalReservation{CommitOrder=checked(++commit),Id=request.Request,World=Generation,Group=proposal.Member.GroupId,Root=proposal.Member.RootOrderRevision,Command=proposal.Member.CommandSequence,IssuedTick=proposal.Member.IssuedTick,Activation=proposal.Member.ActivationRevision,Entity=request.Entity,Incarnation=proposal.Member.Incarnation,Mobility=proposal.Member.MobilityRevision,Request=request.Request,Order=request.Order,Endpoint=proposal.Endpoint,Radius=proposal.Member.Radius,Proof=proposal.Proof.StableKey,Provider=proposal.Proof.ProviderBinding,Profile=NavigationAdmission.ExactProfileBinding(request.Profile),Hold=request.HeldIdentity,State=NavArrivalReservationKind.PendingRequest};
                                    var ledgerOwner=NavigationLiveArrivalReservation.CopyOwned(row,scope.OwnedReservation());try{nextLedger.SetPreparing(request.Request,ledgerOwner.Entry,ledgerOwner);}finally{ledgerOwner.ReleaseStorage();}
                                }finally{owner.ReleaseStorage();}
                            }
                            foreach(var groupId in candidate.Issued.Select(r=>nextMembers[r.Entity]).Distinct()){
                                if(!(nextGroups.OwnerFor(groupId)is NavigationLiveGroup live))throw new InvalidOperationException("Arrival group has no live owner.");
                                NavigationAuthorityGroup version;using(EnterTransport()){if(operation!=authorityOperationIdentity)return false;version=live.ImmutableVersion;version.Retain();}
                                try{
                                    foreach(var request in candidate.Issued.Where(r=>nextMembers[r.Entity]==groupId)){
                                        int index=Enumerable.Range(0,version.Members.Count).Single(i=>((NavigationAuthorityMember)version.Members[i]).Data.Entity==request.Entity);
                                        var original=(NavigationAuthorityMember)version.Members[index];var data=original.Data;
                                        Crowd.TryGet(request.Entity,out var actor);
                                        var value=new NavigationAuthorityMember.Values(data.Entity,data.Slot,data.Incarnation,data.OrderRevision,data.MobilityRevision,actor.Held?GroupMemberPhase.Held:data.FormationReleased?GroupMemberPhase.Combat:GroupMemberPhase.PendingRoute,request.Goal,request.GoalLocation.Value.Position,true,true,data.FormationReleased,data.Branch,request.Entity,data.ContactTick,data.ProgressRequest,data.Progress);
                                        var member=NavigationAuthorityMember.CreateOwned(value,original.ContactOwner,request.GoalLocation.Value.NameReference,scope.OwnedReservation());
                                        try{var next=NavigationAuthorityGroup.CopyWithMemberOwned(version,index,member,scope.OwnedReservation());version.Release();version=next;}finally{member.ReleaseStorage();}
                                    }
                                    var birth=NavigationLiveGroup.CreateOwned(version,scope);try{birth.SealForInstall(scope);nextGroups.SetPreparing(groupId,birth.PreparedRecord(scope),birth);candidate.Births[candidate.BirthCount++]=birth;}finally{birth.ReleaseStorage();}
                                }finally{version.Release();}
                            }
                            candidate.AdmissionOwner=PrepareAdmissionProjection(nextGroups,nextPending,requestSequence,checked(publication+1),operation,scope,admissionOwner);
                            candidate.Committed.CrowdNames=CaptureCrowdNameReferences(scope.OwnedReservation());
                            candidate.ExpectedHost=host;candidate.ExpectedAuthority=authority;candidate.PinObservation();
                            nextGroups.Seal();nextMembers.Seal();nextOrders.Seal();nextPending.Seal();nextAllocating.Seal();nextReservations.Seal();nextGoals.Seal();nextIdentities.Seal();nextFailures.Seal();nextLedger.Seal();
                        }
                        scope.Attach(candidate);scope.Publish(candidate,_=>{using(EnterTransport())using(var mailbox=Requests.EnterPreparedWrite()){
                                // Pure worker frontier frames do not change
                                // these Ready inputs and must not starve the
                                // authority's request publication. Validate
                                // the exact retained attempt roles instead.
                                if(operation!=authorityOperationIdentity||publication!=admissionPublicationIdentity||!ReferenceEquals(authority,authorityOwners)||!ReferenceEquals(physicalHostBudget,scope.Identity)||
                                    commandGroup!=null||admissionBatchDepth!=0||hostAllocationThread!=0||!ReferenceEquals(worker,certifiedHost)||workerGeneration!=certifiedHostAttachmentIdentity||
                                    !arrivalAttempts.TryGetValue(attempt.Id,out var current)||!ReferenceEquals(current,attempt)||attempt.Epoch!=attemptEpoch||attempt.Published||attempt.Retiring||attempt.Working||attempt.Reconstructing||attempt.SnapshotStatus!=NavArrivalStatus.Ready||
                                    work==null||history==null||intents==null||!ReferenceEquals(attempt.Work,work)||!ReferenceEquals(attempt.History,history)||!ReferenceEquals(attempt.OwnedIntents,intents)||committedHostRoot==null||
                                    !committedHostRoot.ArrivalWork.TryGetValue(attempt.Id,out var currentWork)||!ReferenceEquals(currentWork,work)||
                                    !committedHostRoot.ArrivalHistory.TryGetValue(attempt.Id,out var currentHistory)||!ReferenceEquals(currentHistory,history)||
                                    !committedHostRoot.ArrivalIntentSets.TryGetValue(attempt.Id,out var currentIntents)||!ReferenceEquals(currentIntents,intents)||scope.IsCancelled)throw new InvalidOperationException("Arrival publication lost observed authority.");
                                mailbox.Validate(mailboxRevision,candidate.Mailbox,candidate.MailboxCount);for(int i=0;i<candidate.BirthCount;i++)candidate.Births[i].ValidateInstall(scope);
                                for(int i=0;i<candidate.BirthCount;i++)candidate.Births[i].ActivateNoThrow(this);
                                retired=groupCommandBacking;groupCommandBacking=candidate.Committed;groups=nextGroups;memberGroups=nextMembers;orders=nextOrders;pending=nextPending;allocating=nextAllocating;reservations=nextReservations;retainedGoals=nextGoals;movementIdentities=nextIdentities;technicalFailures=nextFailures;arrivalLedger=nextLedger;
                                arrivalCommitSequence=commit;attempt.Published=true;attempt.StorageOwner?.ChangedNoThrow();if(PublishAdmissionOwnerNoThrow(candidate.AdmissionOwner))candidate.AdmissionOwner=null;authorityOperationIdentity=operation+1;mailbox.InstallNoThrow(candidate.Mailbox,candidate.MailboxCount,candidate.Committed.MailboxOwners);candidate.Published=true;for(int i=0;i<candidate.BirthCount;i++)candidate.Births[i]=null;candidate.BirthCount=0;
                            }});
                        scope.ResolveCommitted();candidate.ReleaseObservationPins();retired?.ReleaseStorage();ReleaseArrivalDerivatives(attempt);PruneArrivalAttempts();SignalHost();foreach(var request in candidate.Issued){EndpointAssigned?.Invoke(request);RouteRequested?.Invoke(request);}return true;
                    }catch(InvalidOperationException){if(candidate?.Published==true)throw;return false;}
                    finally{candidate?.ReleaseStorage();nextGroups?.ReleaseStorage();nextMembers?.ReleaseStorage();nextOrders?.ReleaseStorage();nextPending?.ReleaseStorage();nextAllocating?.ReleaseStorage();nextReservations?.ReleaseStorage();nextGoals?.ReleaseStorage();nextIdentities?.ReleaseStorage();nextFailures?.ReleaseStorage();nextLedger?.ReleaseStorage();}
                }
            }
        }
    }
}
