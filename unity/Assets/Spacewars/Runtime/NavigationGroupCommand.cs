using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal readonly struct NavigationCommandSourcePins : IDisposable
    {
        private readonly NavOwnedStorage host,authority,backing;
        internal NavigationCommandSourcePins(NavOwnedStorage host,NavOwnedStorage authority,NavOwnedStorage backing){this.host=host;this.authority=authority;this.backing=backing;host?.Retain();authority?.Retain();backing?.Retain();}
        public void Dispose(){host?.Release();authority?.Release();backing?.Release();}
    }
    // Session lifetime backing. Prepared actor/birth/issued arrays stay in the
    // command carrier and retire after publication, never after a later order.
    internal sealed class NavigationCommandAuthority : NavOwnedStorage
    {
        internal NavigationCommandSlots<long,GroupOrderState> Groups;
        internal NavigationCommandSlots<int,long> MemberGroups,Orders;
        internal NavigationCommandSlots<int,NavigationRequest> Pending,Allocating;
        internal NavigationCommandSlots<int,NavPoint> Reservations,Goals;
        internal NavigationCommandSlots<int,string> Identities;internal NavigationCommandSlots<int,NavSolveStatus> Failures;
        internal NavigationCommandSlots<long,NavigationArrivalReservation> Ledger;
        internal NavigationRequest[] Mailbox;internal NavOwnedStorage[] MailboxOwners;
        internal NavigationCrowdNameBacking CrowdNames;
        private bool released;
        internal override bool IsImmutableCaptureRoot=>false;
        private static long Layout(int capacity)=>checked(NavStorageLayout.ObjectBytesBound(typeof(NavigationCommandAuthority))+2*32L+16L*capacity);
        private NavigationCommandAuthority(int capacity):base(Layout(capacity)){Mailbox=new NavigationRequest[capacity];MailboxOwners=new NavOwnedStorage[capacity];Retain();}
        internal static NavigationCommandAuthority CreateOwned(int capacity,INavStorageReservation reservation){using(var allocation=new NavStorageAllocation(reservation.TryReserve)){allocation.Reserve(Layout(capacity));return allocation.Register(new NavigationCommandAuthority(capacity));}}
        internal void OwnMaps(){Groups=NavPhysicalMacro.Acquire(this,Groups);MemberGroups=NavPhysicalMacro.Acquire(this,MemberGroups);Orders=NavPhysicalMacro.Acquire(this,Orders);Pending=NavPhysicalMacro.Acquire(this,Pending);Allocating=NavPhysicalMacro.Acquire(this,Allocating);Reservations=NavPhysicalMacro.Acquire(this,Reservations);Goals=NavPhysicalMacro.Acquire(this,Goals);Identities=NavPhysicalMacro.Acquire(this,Identities);Failures=NavPhysicalMacro.Acquire(this,Failures);Ledger=NavPhysicalMacro.Acquire(this,Ledger);}
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){Groups?.Release();MemberGroups?.Release();Orders?.Release();Pending?.Release();Allocating?.Release();Reservations?.Release();Goals?.Release();Identities?.Release();Failures?.Release();Ledger?.Release();CrowdNames?.Release();for(int i=0;i<MailboxOwners.Length;i++){MailboxOwners[i]?.Release();MailboxOwners[i]=null;Mailbox[i]=null;}}
    }
    // One ordinary group-command candidate, including its mailbox backing.
    // No live authority map is used as a preparation target.
    internal sealed class NavigationGroupCommand : NavOwnedStorage
    {
        internal NavigationCommandSlots<long,GroupOrderState> Groups;
        internal NavigationCommandSlots<int,long> MemberGroups,Orders;
        internal NavigationCommandSlots<int,NavigationRequest> Pending,Allocating;
        internal NavigationCommandSlots<int,NavPoint> Reservations,Goals;
        internal NavigationCommandSlots<long,NavigationArrivalReservation> Ledger;
        internal NavigationCommandSlots<int,string> Identities;internal NavigationCommandSlots<int,NavSolveStatus> Failures;
        internal NavigationCommandAuthority Committed;
        internal readonly NavOwnedStorage[] RetiredActorRoutes;
        internal NavigationRequest[] Mailbox=>Committed.Mailbox;internal NavigationRequest[] Issued;internal NavUnit[] Actors;internal long[] ActorMobility;internal NavigationInstalledExecutionState[] Installed;
        internal readonly bool[] InstalledDetached;
        internal NavigationAdmissionStorage AdmissionOwner;
        internal NavigationAdmission Admission=>AdmissionOwner.Value;
        private NavOwnedStorage[] mailboxOwners=>Committed.MailboxOwners;private NavOwnedStorage[] issuedOwners;
        internal NavigationLiveGroup[] Births;internal int BirthCount;
        internal NavigationHostRoot ExpectedHost;internal NavigationAuthorityOwners ExpectedAuthority;
        internal NavigationRetentionBudget Budget;internal long Operation,ExpectedPublication,ExpectedRequestSequence,ExpectedGroupSequence,MailboxRevision;
        internal GroupOrderState CommandContext;internal bool Arrival,IsStop,StopHold,IsRemoval;internal int RemovalSourceCount;internal NavCrowdSpatialStorage RemovalSpatial;internal long StopCorridorBytes,StopGeometryBytes;internal int AllocationThread;internal CertifiedRouteKernel Worker;
        internal IDictionary<long,GroupOrderState> OldGroups;internal IDictionary<int,long> OldMembers,OldOrders;
        internal IDictionary<int,NavigationRequest> OldPending,OldAllocating;internal IDictionary<int,NavSolveStatus> OldFailures;internal IDictionary<int,NavPoint> OldReservations,OldGoals;internal IDictionary<int,string> OldIdentities;
        internal void PinObservation(){ExpectedHost=NavPhysicalMacro.Acquire(this,ExpectedHost);ExpectedAuthority=NavPhysicalMacro.Acquire(this,ExpectedAuthority);}
        internal void ReleaseObservationPins(){ExpectedHost?.Release();ExpectedAuthority?.Release();ExpectedHost=null;ExpectedAuthority=null;OldGroups=null;OldMembers=OldOrders=null;OldPending=OldAllocating=null;OldFailures=null;OldReservations=OldGoals=null;OldIdentities=null;CommandContext=null;Budget=null;}
        internal int MailboxCount;internal long RequestSequence,GroupSequence,Publication;
        private bool released;internal bool Published;
        internal override bool IsImmutableCaptureRoot=>false;
        private static long Layout(int issuedCount,int birthCapacity)=>checked(NavStorageLayout.ObjectBytesBound(typeof(NavigationGroupCommand))+8*32L+64L*issuedCount+8L*((issuedCount+7)/8)+8L*birthCapacity);
        private NavigationGroupCommand(int issuedCount,int birthCapacity):base(Layout(issuedCount,birthCapacity))
        {RetiredActorRoutes=new NavOwnedStorage[issuedCount*3];Issued=new NavigationRequest[issuedCount];issuedOwners=new NavOwnedStorage[issuedCount];Births=new NavigationLiveGroup[birthCapacity];Actors=new NavUnit[issuedCount];ActorMobility=new long[issuedCount];Installed=new NavigationInstalledExecutionState[issuedCount];InstalledDetached=new bool[issuedCount];Retain();}
        internal void ObserveInstalled(int index,NavigationInstalledExecutionState state,bool detached)
        {if(state?.StorageOwner!=null){RequireEdge(state.StorageOwner);state.StorageOwner.Retain();}Installed[index]=state;InstalledDetached[index]=detached;}
        internal static NavigationGroupCommand CreateOwned(int capacity,int issuedCount,INavStorageReservation reservation,int birthCapacity=-1)
        {
            if(birthCapacity<0)birthCapacity=issuedCount+1;
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){allocation.Reserve(Layout(issuedCount,birthCapacity));var candidate=allocation.Register(new NavigationGroupCommand(issuedCount,birthCapacity));
                try{candidate.Committed=NavigationCommandAuthority.CreateOwned(capacity,reservation);candidate.RequireEdge(candidate.Committed);candidate.Committed.Retain();return candidate;}catch{candidate.ReleaseStorage();throw;}}
        }
        internal void OwnMaps(){Committed.Groups=Groups;Committed.MemberGroups=MemberGroups;Committed.Orders=Orders;Committed.Pending=Pending;Committed.Allocating=Allocating;Committed.Reservations=Reservations;Committed.Goals=Goals;Committed.Identities=Identities;Committed.Failures=Failures;Committed.Ledger=Ledger;Committed.OwnMaps();}
        internal void OwnIssued(int index,NavigationHostRequest request){RequireEdge(request);request.Retain();issuedOwners[index]=request;}
        internal void AppendMailbox(NavigationRequest request){var owner=request.StorageOwner;if(owner!=null){RequireEdge(owner);owner.Retain();}Mailbox[MailboxCount]=request;mailboxOwners[MailboxCount++]=owner;}
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){RemovalSpatial?.Release();RemovalSpatial=null;AdmissionOwner?.Release();AdmissionOwner=null;foreach(var route in RetiredActorRoutes)route?.Release();for(int i=0;i<issuedOwners.Length;i++){issuedOwners[i]?.Release();issuedOwners[i]=null;Issued[i]=null;Actors[i]=null;Installed[i]?.StorageOwner?.Release();Installed[i]=null;}ReleaseObservationPins();if(Committed!=null){if(!Published)Committed.ReleaseStorage();Committed.Release();Committed=null;}Groups=null;MemberGroups=Orders=null;Pending=Allocating=null;Reservations=Goals=null;Identities=null;Failures=null;Ledger=null;}

    }
    public sealed partial class NavigationSession
    {
        private NavigationCommandAuthority groupCommandBacking;private NavigationGroupCommand preparedCommandGroup;
        private NavigationRetentionBudget.Scope preparedCommandScope;
        private bool commandMoveRefused;
        private NavigationCommandSlots<TKey,TValue> CopyCommandMap<TKey,TValue>(IDictionary<TKey,TValue> map,int added,NavigationRetentionBudget.Scope scope,long operation,Func<TValue,NavOwnedStorage> owner=null,int exactCapacity=-1)
        {
            int layoutLength;using(EnterTransport()){if(operation!=authorityOperationIdentity)throw new InvalidOperationException("Map source operation changed.");layoutLength=NavigationCommandLayout.Length(map);}
            using(NavStorageScratch.Reserve(scope.OwnedReservation(),checked(128L+2*(32L+4L*layoutLength)))){
                using(EnterTransport()){if(operation!=authorityOperationIdentity)throw new InvalidOperationException("Map source operation changed.");}
                int capacity=exactCapacity<0?Math.Max(4,checked(NavigationCommandLayout.Capture(map)[0]+added)):exactCapacity;
                var result=NavigationCommandSlots<TKey,TValue>.CreateOwned(capacity,scope.OwnedReservation());
                try{using(EnterTransport()){if(operation!=authorityOperationIdentity)throw new InvalidOperationException("Map source operation changed.");}result.CopyLayoutFrom(map,owner);return result;}catch{result.ReleaseStorage();throw;}
            }
        }
        private static NavigationLiveGroup CopyCommandGroup(GroupOrderState record,NavigationRetentionBudget.Scope scope)
        {
            var reservation=scope.OwnedReservation();NavOwnedStorage[] members=null;NavRetainedArray<NavOwnedStorage> memberArray=null;NavigationAuthorityGroup version=null;
            using(NavStorageScratch.Reserve(reservation,32L+8L*record.Members.Length)){
                members=new NavOwnedStorage[record.Members.Length];try{
                    for(int i=0;i<members.Length;i++)members[i]=NavigationAuthorityMember.CopyOwned(record.Members[i],reservation);
                    memberArray=NavRetainedArray<NavOwnedStorage>.Copy(members,reservation.TryReserve);
                    version=NavigationAuthorityGroup.CopyNamesOwned(record,memberArray,reservation);
                    var live=NavigationLiveGroup.CreateOwned(version,scope);live.SealForInstall(scope);return live;
                }finally{version?.ReleaseStorage();memberArray?.Release();foreach(var member in members)member?.ReleaseOwner();}
            }
        }
        // Returns null while command activation/arrival cancellation is still on
        // the older path. Those seams remain part of the open A5-a work.
        private bool? TryPreparedOrdinaryMoveGroup(int[] ids,NavPoint[] goals,bool legacySingle=false)
        {
            NavigationRetentionBudget budget;NavigationHostRoot observedHost;NavigationAuthorityOwners observedAuthority;
            IDictionary<long,GroupOrderState> oldGroups;IDictionary<int,long> oldMembers,oldOrders;
            IDictionary<int,NavigationRequest> oldPending,oldAllocating;IDictionary<int,NavPoint> oldReservations,oldGoals;IDictionary<int,string> oldIdentities;IDictionary<int,NavSolveStatus> oldFailures;
            long operation,publication,oldRequestSequence,oldGroupSequence,mailboxRevision;int mailboxCount;GroupOrderState context;NavigationCommandSourcePins sourcePins;int allocationThread;CertifiedRouteKernel worker;
            using(EnterTransport()){
                RejectTransferCallbackReentry();
                if(preparedCommandGroup!=null)return null;
                context=commandGroup;
                if(ids==null||goals==null||ids.Length==0||ids.Length!=goals.Length||Requests.Count+RegisteredRouteCount+ids.Length>RouteRequestCapacity)return false;
                allocationThread=hostAllocationThread;worker=certifiedHost;budget=physicalHostBudget;observedHost=committedHostRoot;observedAuthority=authorityOwners;operation=authorityOperationIdentity;publication=admissionPublicationIdentity;oldRequestSequence=requestSequence;oldGroupSequence=groupSequence;Requests.ObserveState(out mailboxRevision,out mailboxCount);
                oldGroups=groups;oldMembers=memberGroups;oldOrders=orders;oldPending=pending;oldAllocating=allocating;oldReservations=reservations;oldGoals=retainedGoals;oldIdentities=movementIdentities;oldFailures=technicalFailures;if(observedAuthority==null||!observedAuthority.BelongsTo(budget)||observedHost!=null&&!observedHost.BelongsTo(budget)||groupCommandBacking!=null&&!groupCommandBacking.BelongsTo(budget))throw new ArgumentException("Group source belongs to another ledger.");sourcePins=new NavigationCommandSourcePins(observedHost,observedAuthority,groupCommandBacking);
            }
            using(sourcePins){
            if(!budget.TryOpenScope(NavigationPreparationKind.Admission,Generation,publication,out var scope))return false;
            bool staged=false;try{
                NavigationGroupCommand candidate=null;
                NavigationCommandSlots<long,GroupOrderState> nextGroups=null;NavigationCommandSlots<int,long> nextMembers=null,nextOrders=null;
                NavigationCommandSlots<int,NavigationRequest> nextPending=null,nextAllocating=null;NavigationCommandSlots<int,NavPoint> nextReservations=null,nextGoals=null;NavigationCommandSlots<int,string> nextIdentities=null;NavigationCommandSlots<int,NavSolveStatus> nextFailures=null;NavigationCommandSlots<long,NavigationArrivalReservation> nextLedger=null;
                List<NavigationLiveGroup> liveBirths=null;NavOwnedStorage[] sourceGroupPins=null;IDisposable sourceScratch=null;
                try{
                    int groupCount;using(EnterTransport()){if(operation!=authorityOperationIdentity||!ReferenceEquals(groups,oldGroups))return false;groupCount=oldGroups.Count;}
                    sourceScratch=NavStorageScratch.Reserve(scope.OwnedReservation(),checked(128L+32L+8L*groupCount+32L+8L*(ids.Length+1)+32L+4L*ids.Length));
                    sourceGroupPins=new NavOwnedStorage[groupCount];var validationIds=(int[])ids.Clone();Array.Sort(validationIds);for(int i=1;i<validationIds.Length;i++)if(validationIds[i]==validationIds[i-1])return false;validationIds=null;liveBirths=new List<NavigationLiveGroup>(ids.Length+1);
                    using(EnterTransport()){
                        if(operation!=authorityOperationIdentity||!ReferenceEquals(groups,oldGroups)||oldGroups.Count!=groupCount)return false;
                        for(int i=0;i<ids.Length;i++)if(!Crowd.TryGet(ids[i],out var actor)||!geometry.IsFree(goals[i],actor.Radius)||!Crowd.Locate(goals[i],actor.Radius,out _))return false;
                        if(oldGroups is NavigationCommandSlots<long,GroupOrderState> ownedGroups){int at=0;foreach(var row in oldGroups){var owner=ownedGroups.OwnerFor(row.Key);owner?.Retain();sourceGroupPins[at++]=owner;}}
                    }
                    // All factories and callbacks precede the authoritative gate.
                    nextGroups=CopyCommandMap(oldGroups,1,scope,operation);nextMembers=CopyCommandMap(oldMembers,ids.Length,scope,operation);nextOrders=CopyCommandMap(oldOrders,ids.Length,scope,operation);
                    nextPending=CopyCommandMap(oldPending,ids.Length,scope,operation,r=>r.StorageOwner);nextAllocating=CopyCommandMap(oldAllocating,ids.Length,scope,operation,r=>r.StorageOwner);nextReservations=CopyCommandMap(oldReservations,ids.Length,scope,operation);nextGoals=CopyCommandMap(oldGoals,ids.Length,scope,operation);nextIdentities=CopyCommandMap(oldIdentities,ids.Length,scope,operation);nextFailures=CopyCommandMap(oldFailures,ids.Length,scope,operation);nextLedger=CopyCommandMap(arrivalLedger,0,scope,operation);
                    using(NavStorageScratch.Reserve(scope.OwnedReservation(),checked(32L+8L*nextLedger.Count))){
                        var retiredRows=nextLedger.Where(row=>row.Value.State==NavArrivalReservationKind.PendingRequest&&Array.IndexOf(ids,(int)row.Value.Entity)>=0).Select(row=>row.Key).ToArray();
                        foreach(long key in retiredRows)nextLedger.Remove(key);
                    }
                    long groupId=legacySingle&&oldMembers.TryGetValue(ids[0],out var existingGroup)?existingGroup:context?.GroupId??checked(oldGroupSequence+1);bool arrival=!legacySingle&&context!=null&&(context.Kind==PlayableCommandKind.Move||context.Kind==PlayableCommandKind.AttackMove);
                    long oldMemberCount=0;foreach(var row in oldGroups)oldMemberCount=checked(oldMemberCount+row.Value.Members.Length);
                    long projectionScratch=checked(1024L+NavStorageLayout.ObjectBytesBound(typeof(GroupOrderState))+32L+ids.Length*(NavStorageLayout.ObjectBytesBound(typeof(GroupMemberState))+128L)+(oldMemberCount+ids.Length)*64L+(oldGroups.Count+1)*256L+(oldPending.Count+probes.Count+ids.Length)*64L);
                    using(NavStorageScratch.Reserve(scope.OwnedReservation(),projectionScratch)){
                        var next=legacySingle&&oldGroups.TryGetValue(groupId,out var existingRecord)?existingRecord.CopyHeader():context==null?new GroupOrderState{WorldGeneration=Generation,GroupId=groupId,OrderRevision=groupId,MembershipRevision=1,Kind=PlayableCommandKind.Move,Origin=PlayableOrderOrigin.Unknown,OriginalGoal=goals[0]}:context.CopyHeader();next.Members=new GroupMemberState[ids.Length];
                        if(marchConfigured){next.MarchVersion=1;next.MarchProfileRevision=marchProfileRevision;next.MarchStretch=marchStretch;}
                        // Preparation scratch holds DTOs only until concrete owners
                        // have copied them; it is never a live authority owner.
                        var indices=Enumerable.Range(0,ids.Length).OrderBy(i=>ids[i]).ToArray();
                        for(int j=0;j<indices.Length;j++){
                            int i=indices[j],id=ids[i];Crowd.TryGet(id,out var actor);Crowd.Locate(goals[i],actor.Radius,out var location);oldOrders.TryGetValue(id,out long previousOrder);
                            next.Members[j]=new GroupMemberState{Entity=id,Incarnation=actor.Incarnation,OrderRevision=context==null?groupId:commandActivationRevision,MobilityRevision=actor.MobilityRevision,Goal=goals[i],GoalLocation=location,HasGoal=true,Reservation=arrival?0:id,Phase=arrival?GroupMemberPhase.Allocating:actor.Held?GroupMemberPhase.Held:GroupMemberPhase.PendingRoute};
                            if(legacySingle&&oldGroups.TryGetValue(groupId,out var previousRecord)){
                                var original=previousRecord.Members.Single(m=>m.Entity==id);var replacement=original.Copy();replacement.Goal=goals[i];replacement.GoalLocation=location;replacement.HasGoal=true;replacement.Reservation=id;replacement.Phase=actor.Held?GroupMemberPhase.Held:replacement.FormationReleased?GroupMemberPhase.Combat:GroupMemberPhase.PendingRoute;replacement.MobilityRevision=actor.MobilityRevision;next.Members[j]=replacement;
                            }
                            if(!legacySingle&&nextMembers.TryGetValue(id,out long previousGroup)){
                                var old=nextGroups[previousGroup];
                                using(NavStorageScratch.Reserve(scope.OwnedReservation(),checked(128L+NavStorageLayout.ObjectBytesBound(typeof(GroupOrderState))+2*32L+old.Members.Length*(16L+NavStorageLayout.ObjectBytesBound(typeof(GroupMemberState)))))){
                                    var replacement=old.Copy();replacement.Members=replacement.Members.Where(m=>m.Entity!=id).ToArray();replacement.MembershipRevision++;replacement.MarchAnchor=0;replacement.MarchInitialized=false;
                                    nextMembers.Remove(id);if(replacement.Members.Length==0)nextGroups.Remove(previousGroup);else {var birth=CopyCommandGroup(replacement,scope);liveBirths.Add(birth);nextGroups.SetPreparing(previousGroup,birth.PreparedRecord(scope),birth);}
                                }
                            }
                            nextOrders.SetPreparing(id,checked(previousOrder+1));nextFailures.Remove(id);nextAllocating.Remove(id);if(arrival)nextReservations.Remove(id);else nextReservations.SetPreparing(id,goals[i]);nextGoals.SetPreparing(id,goals[i]);
                        }
                        if(legacySingle&&oldGroups.TryGetValue(groupId,out var originalGroup)){
                            var changed=next.Members[0];next.Members=originalGroup.Members.Select(m=>m.Entity==changed.Entity?changed:m.Copy()).ToArray();
                        }else if(nextGroups.TryGetValue(groupId,out var remaining)){
                            if(remaining.CommandSequence!=next.CommandSequence||remaining.OrderRevision!=next.OrderRevision)throw new InvalidOperationException("Issuance collision.");
                            next.MarchVersion=remaining.MarchVersion;next.MarchProfileRevision=remaining.MarchProfileRevision;next.MarchStretch=remaining.MarchStretch;next.MarchAnchor=0;next.MarchInitialized=false;
                            int slot=remaining.Members.Max(m=>m.Slot)+1;foreach(var member in SpatialSlotOrder(next.Members,next.OriginalGoal))member.Slot=slot++;
                            next.Members=remaining.Members.Concat(next.Members).OrderBy(m=>m.Entity).ToArray();next.MembershipRevision=remaining.MembershipRevision+1;
                        }else {int slot=0;foreach(var member in SpatialSlotOrder(next.Members,next.OriginalGoal))member.Slot=slot++;}
                        var live=CopyCommandGroup(next,scope);liveBirths.Add(live);next=live.PreparedRecord(scope);nextGroups.SetPreparing(groupId,next,live);
                        foreach(int id in ids)nextMembers.SetPreparing(id,groupId);
                        candidate=NavigationGroupCommand.CreateOwned(Requests.InstanceCapacity,ids.Length,scope.OwnedReservation());
                        candidate.Groups=nextGroups;candidate.MemberGroups=nextMembers;candidate.Orders=nextOrders;candidate.Pending=nextPending;candidate.Allocating=nextAllocating;candidate.Reservations=nextReservations;candidate.Goals=nextGoals;candidate.Identities=nextIdentities;candidate.Failures=nextFailures;candidate.Ledger=nextLedger;candidate.OwnMaps();
                        using(NavStorageScratch.Reserve(scope.OwnedReservation(),32L+8L*mailboxCount)){foreach(var queued in Requests.CopyItemsAtRevision(mailboxRevision))candidate.AppendMailbox(queued);}
                        long sequence=oldRequestSequence;int issued=0;
                        foreach(int i in indices){int id=ids[i];Crowd.TryGet(id,out var actor);Crowd.Locate(goals[i],actor.Radius,out var location);
                            NavigationHostRequest requestOwner=null;var unitProfile=NavigationProfile.CreateForUnitOwned(profile,actor.Radius,actor.Speed,actor.TurnSpeed,scope.OwnedReservation());
                            try{requestOwner=NavigationHostRequest.CreateOwned(Generation,checked(++sequence),id,nextOrders[id],unitProfile,MovementGeometry(id),actor.Position,goals[i],navigationGeometry,HeldIdentity(id),new NavigationMemberIdentity(actor.Incarnation,actor.MobilityRevision,groupId,legacySingle?next.Members.Single(m=>m.Entity==id).OrderRevision:context==null?groupId:commandActivationRevision,next.MembershipRevision),actor.Location,location,null,scope.OwnedReservation());
                            try{var request=requestOwner.Request;if(arrival){nextPending.Remove(id);nextAllocating.SetPreparing(id,request,requestOwner);}else nextPending.SetPreparing(id,request,requestOwner);nextIdentities.SetPreparing(id,request.HeldIdentity,requestOwner.HeldOwner);if(!arrival)candidate.AppendMailbox(request);candidate.Actors[issued]=actor;using(EnterTransport()){if(operation!=authorityOperationIdentity)throw new InvalidOperationException("Installed observation source changed.");installedExecutions.TryGetValue(id,out var observedInstalled);candidate.ObserveInstalled(issued,observedInstalled,installedExecutions.IsDetached(id));}candidate.OwnIssued(issued,requestOwner);candidate.Issued[issued++]=request;}finally{requestOwner.ReleaseStorage();}
                            }finally{unitProfile.StorageRoot.Release();}
                        }
                        candidate.RequestSequence=sequence;candidate.GroupSequence=Math.Max(oldGroupSequence,groupId);candidate.Publication=checked(publication+1);
                        candidate.AdmissionOwner=PrepareAdmissionProjection(nextGroups,nextPending,sequence,candidate.Publication,operation,scope,admissionOwner);
                    }
                    nextGroups.Seal();nextMembers.Seal();nextOrders.Seal();nextPending.Seal();nextAllocating.Seal();nextReservations.Seal();nextGoals.Seal();nextIdentities.Seal();nextFailures.Seal();nextLedger.Seal();
                    candidate.Committed.CrowdNames=CaptureCrowdNameReferences(scope.OwnedReservation());foreach(var actor in candidate.Actors)candidate.Committed.CrowdNames.ClearFailedPreparing(actor.Id);
                    candidate.Budget=budget;candidate.ExpectedHost=observedHost;candidate.ExpectedAuthority=observedAuthority;candidate.PinObservation();
                    candidate.Operation=operation;candidate.ExpectedPublication=publication;candidate.ExpectedRequestSequence=oldRequestSequence;candidate.ExpectedGroupSequence=oldGroupSequence;candidate.MailboxRevision=mailboxRevision;candidate.CommandContext=context;candidate.Arrival=arrival;candidate.AllocationThread=allocationThread;candidate.Worker=worker;
                    candidate.OldGroups=oldGroups;candidate.OldMembers=oldMembers;candidate.OldOrders=oldOrders;candidate.OldPending=oldPending;candidate.OldAllocating=oldAllocating;candidate.OldReservations=oldReservations;candidate.OldGoals=oldGoals;candidate.OldIdentities=oldIdentities;candidate.OldFailures=oldFailures;
                    foreach(var birth in liveBirths){if(ReferenceEquals(nextGroups.OwnerFor(birth.ImmutableVersion.Data.GroupId),birth))candidate.Births[candidate.BirthCount++]=birth;else birth.AbortPrivate();birth.ReleaseStorage();}liveBirths=null;
                    foreach(var pin in sourceGroupPins)pin?.Release();sourceGroupPins=null;sourceScratch.Dispose();sourceScratch=null;scope.Attach(candidate);
                    if(context!=null){using(EnterTransport()){ValidatePreparedGroup(candidate,scope);preparedCommandGroup=candidate;preparedCommandScope=scope;staged=true;}return true;}
                    PublishPreparedGroup(candidate,scope);return true;
                }catch(InvalidOperationException){if(candidate?.Published==true)throw;return false;}
                catch(Exception exception)when(exception is ArgumentException||exception is NullReferenceException||exception is IndexOutOfRangeException){using(EnterTransport()){if(candidate?.Published!=true&&(operation!=authorityOperationIdentity||!ReferenceEquals(observedAuthority,authorityOwners)||!ReferenceEquals(observedHost,committedHostRoot)))return false;}throw;}
                finally{if(!staged)candidate?.ReleaseStorage();nextGroups?.ReleaseStorage();nextMembers?.ReleaseStorage();nextOrders?.ReleaseStorage();nextPending?.ReleaseStorage();nextAllocating?.ReleaseStorage();nextReservations?.ReleaseStorage();nextGoals?.ReleaseStorage();nextIdentities?.ReleaseStorage();nextFailures?.ReleaseStorage();nextLedger?.ReleaseStorage();if(liveBirths!=null)foreach(var birth in liveBirths)birth.ReleaseStorage();if(sourceGroupPins!=null)foreach(var pin in sourceGroupPins)pin?.Release();sourceScratch?.Dispose();}
            }finally{if(!staged)scope.Dispose();}
            }
        }
        // Rebinding a tactical issuance changes group metadata only. Prepare
        // owned records/maps before publication, just as for a movement command;
        // never place a detached DTO into an already owned group map.
        private void FinishOwnedMetadataGroup(int[] ids)
        {
            NavigationGroupCommand c=null;NavigationCommandSourcePins pins;var births=new List<NavigationLiveGroup>();
            long operation,publication;GroupOrderState context;
            NavigationHostRoot host;NavigationAuthorityOwners authority;NavigationRetentionBudget budget;int allocationThread;CertifiedRouteKernel worker;
            IDictionary<long,GroupOrderState> oldGroups;IDictionary<int,long> oldMembers,oldOrders;
            IDictionary<int,NavigationRequest> oldPending,oldAllocating;IDictionary<int,NavPoint> oldReservations,oldGoals;IDictionary<int,string> oldIdentities;IDictionary<int,NavSolveStatus> oldFailures;
            long oldRequestSequence,oldGroupSequence;IDictionary<long,NavigationArrivalReservation> oldLedger;
            using(EnterTransport()){
                operation=authorityOperationIdentity;publication=admissionPublicationIdentity;context=commandGroup;
                host=committedHostRoot;authority=authorityOwners;budget=physicalHostBudget;allocationThread=hostAllocationThread;worker=certifiedHost;
                oldGroups=groups;oldMembers=memberGroups;oldOrders=orders;oldPending=pending;oldAllocating=allocating;oldReservations=reservations;oldGoals=retainedGoals;oldIdentities=movementIdentities;oldFailures=technicalFailures;oldLedger=arrivalLedger;
                oldRequestSequence=requestSequence;oldGroupSequence=groupSequence;
                pins=new NavigationCommandSourcePins(host,authority,groupCommandBacking);
            }
            using(pins){
                AfterMetadataObservationForQa?.Invoke();
                if(!budget.TryOpenScope(NavigationPreparationKind.Admission,Generation,publication,out var scope))throw new InvalidOperationException("Metadata group admission refused.");
                using(scope)try{
                    c=NavigationGroupCommand.CreateOwned(Requests.InstanceCapacity,0,scope.OwnedReservation(),ids.Length+1);
                    c.Budget=budget;c.ExpectedHost=host;c.ExpectedAuthority=authority;c.PinObservation();
                    c.Operation=operation;c.ExpectedPublication=publication;c.ExpectedRequestSequence=oldRequestSequence;c.ExpectedGroupSequence=oldGroupSequence;c.CommandContext=context;c.AllocationThread=allocationThread;c.Worker=worker;
                    c.OldGroups=oldGroups;c.OldMembers=oldMembers;c.OldOrders=oldOrders;c.OldPending=oldPending;c.OldAllocating=oldAllocating;c.OldReservations=oldReservations;c.OldGoals=oldGoals;c.OldIdentities=oldIdentities;c.OldFailures=oldFailures;
                    c.Groups=CopyCommandMap(oldGroups,1,scope,operation);c.MemberGroups=CopyCommandMap(oldMembers,ids.Length,scope,operation);
                    c.Orders=CopyCommandMap(oldOrders,0,scope,operation);c.Pending=CopyCommandMap(oldPending,0,scope,operation,r=>r.StorageOwner);c.Allocating=CopyCommandMap(oldAllocating,0,scope,operation,r=>r.StorageOwner);
                    c.Reservations=CopyCommandMap(oldReservations,0,scope,operation);c.Goals=CopyCommandMap(oldGoals,0,scope,operation);c.Identities=CopyCommandMap(oldIdentities,0,scope,operation);c.Failures=CopyCommandMap(oldFailures,0,scope,operation);c.Ledger=CopyCommandMap(oldLedger,0,scope,operation);c.OwnMaps();
                    void Install(GroupOrderState record){
                        var live=CopyCommandGroup(record,scope);
                        try{c.Groups.SetPreparing(record.GroupId,live.PreparedRecord(scope),live);births.Add(live);}catch{live.ReleaseStorage();throw;}
                    }
                    using(NavStorageScratch.Reserve(scope.OwnedReservation(),checked(8192L+ids.Length*1024L+groups.Values.Sum(g=>256L+256L*g.Members.Length)))){
                        var next=context.CopyHeader();var members=new List<GroupMemberState>();
                        foreach(int id in ids){
                            if(!Crowd.TryGet(id,out var actor))throw new InvalidOperationException("Accepted group member is absent.");
                            if(c.MemberGroups.TryGetValue(id,out var previous)){
                                var old=c.Groups[previous].Copy();old.Members=old.Members.Where(m=>m.Entity!=id).ToArray();old.MembershipRevision++;old.MarchAnchor=0;old.MarchInitialized=false;
                                c.MemberGroups.Remove(id);if(old.Members.Length==0)c.Groups.Remove(previous);else Install(old);
                            }
                            retainedGoals.TryGetValue(id,out var goal);
                            members.Add(new GroupMemberState{Entity=id,Incarnation=actor.Incarnation,OrderRevision=commandActivationRevision==0?next.OrderRevision:commandActivationRevision,MobilityRevision=actor.MobilityRevision,Slot=id,Goal=goal,
                                GoalLocation=retainedGoals.ContainsKey(id)&&Crowd.Locate(goal,actor.Radius,out var location)?(NavLocation?)location:null,HasGoal=retainedGoals.ContainsKey(id),Reservation=reservations.ContainsKey(id)?id:0,
                                Phase=allocating.ContainsKey(id)?GroupMemberPhase.Allocating:actor.Held?GroupMemberPhase.Held:IsPending(id)?GroupMemberPhase.PendingRoute:actor.Moving?GroupMemberPhase.March:GroupMemberPhase.Stopped});
                        }
                        if(c.Groups.TryGetValue(next.GroupId,out var existing)){
                            if(existing.CommandSequence!=next.CommandSequence||existing.OrderRevision!=next.OrderRevision)throw new InvalidOperationException("Issuance collision.");
                            next.MarchVersion=existing.MarchVersion;next.MarchProfileRevision=existing.MarchProfileRevision;next.MarchStretch=existing.MarchStretch;next.MarchAnchor=0;next.MarchInitialized=false;
                            int slot=existing.Members.Max(m=>m.Slot)+1;foreach(var member in SpatialSlotOrder(members,next.OriginalGoal))member.Slot=slot++;
                            next.Members=existing.Members.Concat(members).OrderBy(m=>m.Entity).ToArray();next.MembershipRevision=existing.MembershipRevision+1;
                        }else{
                            if(marchConfigured){next.MarchVersion=1;next.MarchProfileRevision=marchProfileRevision;next.MarchStretch=marchStretch;}
                            int slot=0;foreach(var member in SpatialSlotOrder(members,next.OriginalGoal))member.Slot=slot++;next.Members=members.ToArray();
                        }
                        Install(next);foreach(int id in ids)c.MemberGroups.SetPreparing(id,next.GroupId);
                        // Intermediate detached-subset births need not be activated.
                        foreach(var birth in births)if(ReferenceEquals(c.Groups.OwnerFor(birth.ImmutableVersion.Data.GroupId),birth))c.Births[c.BirthCount++]=birth;else birth.AbortPrivate();
                        c.RequestSequence=oldRequestSequence;c.GroupSequence=Math.Max(oldGroupSequence,next.GroupId);c.Publication=checked(publication+1);
                        c.AdmissionOwner=PrepareAdmissionProjection(c.Groups,c.Pending,c.RequestSequence,c.Publication,operation,scope,admissionOwner);
                    }
                    Requests.ObserveState(out c.MailboxRevision,out var count);using(NavStorageScratch.Reserve(scope.OwnedReservation(),32L+8L*count))foreach(var queued in Requests.CopyItemsAtRevision(c.MailboxRevision))c.AppendMailbox(queued);
                    c.Groups.Seal();c.MemberGroups.Seal();c.Orders.Seal();c.Pending.Seal();c.Allocating.Seal();c.Reservations.Seal();c.Goals.Seal();c.Identities.Seal();c.Failures.Seal();c.Ledger.Seal();
                    c.Committed.CrowdNames=CaptureCrowdNameReferences(scope.OwnedReservation());scope.Attach(c);PublishPreparedGroup(c,scope,ids);
                }finally{
                    c?.Groups?.ReleaseStorage();c?.MemberGroups?.ReleaseStorage();c?.Orders?.ReleaseStorage();c?.Pending?.ReleaseStorage();c?.Allocating?.ReleaseStorage();c?.Reservations?.ReleaseStorage();c?.Goals?.ReleaseStorage();c?.Identities?.ReleaseStorage();c?.Failures?.ReleaseStorage();c?.Ledger?.ReleaseStorage();c?.ReleaseStorage();foreach(var birth in births)birth.ReleaseStorage();
                }
            }
        }
        // QA-only rendezvous runs outside the transport gate, after observation pins exist.
        internal Action AfterMetadataObservationForQa;
        internal NavigationGroupCommand LastGroupGuardCandidateForQa;
        [Flags] internal enum GroupGuardMismatch : ulong
        {
            Budget=1UL<<0,
            Host=1UL<<1,
            Authority=1UL<<2,
            Operation=1UL<<3,
            Publication=1UL<<4,
            RequestSequence=1UL<<5,
            GroupSequence=1UL<<6,
            CommandContext=1UL<<7,
            AdmissionBatch=1UL<<8,
            AllocationThread=1UL<<9,
            Worker=1UL<<10,
            Groups=1UL<<11,
            Members=1UL<<12,
            Pending=1UL<<13,
            Allocating=1UL<<14,
            Orders=1UL<<15,
            Reservations=1UL<<16,
            Goals=1UL<<17,
            Identities=1UL<<18,
            Failures=1UL<<19,
            OperationExhausted=1UL<<20,
            Cancelled=1UL<<21
        }
        internal struct GroupGuardObservation
        {
            public GroupGuardMismatch Mismatch;
            public bool BudgetMatches;
            public bool HostMatches;
            public bool AuthorityMatches;
            public bool OperationMatches;
            public bool PublicationMatches;
            public bool RequestSequenceMatches;
            public bool GroupSequenceMatches;
            public bool CommandContextMatches;
            public bool AdmissionBatchMatches;
            public bool AllocationThreadMatches;
            public bool WorkerMatches;
            public bool GroupsMatches;
            public bool MembersMatches;
            public bool PendingMatches;
            public bool AllocatingMatches;
            public bool OrdersMatches;
            public bool ReservationsMatches;
            public bool GoalsMatches;
            public bool IdentitiesMatches;
            public bool FailuresMatches;
            public bool OperationExhaustedMatches;
            public bool CancelledMatches;
            public long ExpectedOperation,ActualOperation,ExpectedPublication,ActualPublication,ExpectedRequest,ActualRequest,ExpectedGroup,ActualGroup;
            public int ExpectedAllocationThread,ActualAllocationThread,AdmissionBatch,ExpectedHostStage,ActualHostStage;
            public PlayableCommandKind CommandKind;public long CommandSequence,CommandGroupId;
        }
        internal GroupGuardObservation LastGroupGuardObservationForQa;
        private GroupGuardObservation ObserveGroupGuard(NavigationGroupCommand candidate,NavigationRetentionBudget.Scope scope)
        {
            var value=new GroupGuardObservation{
                BudgetMatches=!(!ReferenceEquals(candidate.Budget,physicalHostBudget)),
                HostMatches=!(!ReferenceEquals(candidate.ExpectedHost,committedHostRoot)),
                AuthorityMatches=!(!ReferenceEquals(candidate.ExpectedAuthority,authorityOwners)),
                OperationMatches=!(candidate.Operation!=authorityOperationIdentity),
                PublicationMatches=!(candidate.ExpectedPublication!=admissionPublicationIdentity),
                RequestSequenceMatches=!(requestSequence!=candidate.ExpectedRequestSequence),
                GroupSequenceMatches=!(groupSequence!=candidate.ExpectedGroupSequence),
                CommandContextMatches=!(!ReferenceEquals(commandGroup,candidate.CommandContext)),
                AdmissionBatchMatches=!(admissionBatchDepth!=0),
                AllocationThreadMatches=!(hostAllocationThread!=candidate.AllocationThread),
                WorkerMatches=!(!ReferenceEquals(certifiedHost,candidate.Worker)),
                GroupsMatches=!(!ReferenceEquals(groups,candidate.OldGroups)),
                MembersMatches=!(!ReferenceEquals(memberGroups,candidate.OldMembers)),
                PendingMatches=!(!ReferenceEquals(pending,candidate.OldPending)),
                AllocatingMatches=!(!ReferenceEquals(allocating,candidate.OldAllocating)),
                OrdersMatches=!(!ReferenceEquals(orders,candidate.OldOrders)),
                ReservationsMatches=!(!ReferenceEquals(reservations,candidate.OldReservations)),
                GoalsMatches=!(!ReferenceEquals(retainedGoals,candidate.OldGoals)),
                IdentitiesMatches=!(!ReferenceEquals(movementIdentities,candidate.OldIdentities)),
                FailuresMatches=!(!ReferenceEquals(technicalFailures,candidate.OldFailures)),
                OperationExhaustedMatches=!(authorityOperationIdentity==long.MaxValue),
                CancelledMatches=!(scope.IsCancelled),
                ExpectedOperation=candidate.Operation,ActualOperation=authorityOperationIdentity,ExpectedPublication=candidate.ExpectedPublication,ActualPublication=admissionPublicationIdentity,
                ExpectedRequest=candidate.ExpectedRequestSequence,ActualRequest=requestSequence,ExpectedGroup=candidate.ExpectedGroupSequence,ActualGroup=groupSequence,
                ExpectedAllocationThread=candidate.AllocationThread,ActualAllocationThread=hostAllocationThread,AdmissionBatch=admissionBatchDepth,
                ExpectedHostStage=candidate.ExpectedHost==null?-1:(int)candidate.ExpectedHost.Frontier.Stage,ActualHostStage=committedHostRoot==null?-1:(int)committedHostRoot.Frontier.Stage,
                CommandKind=candidate.CommandContext?.Kind??default,CommandSequence=candidate.CommandContext?.CommandSequence??0,CommandGroupId=candidate.CommandContext?.GroupId??0
            };
            if(!value.BudgetMatches)value.Mismatch|=GroupGuardMismatch.Budget;
            if(!value.HostMatches)value.Mismatch|=GroupGuardMismatch.Host;
            if(!value.AuthorityMatches)value.Mismatch|=GroupGuardMismatch.Authority;
            if(!value.OperationMatches)value.Mismatch|=GroupGuardMismatch.Operation;
            if(!value.PublicationMatches)value.Mismatch|=GroupGuardMismatch.Publication;
            if(!value.RequestSequenceMatches)value.Mismatch|=GroupGuardMismatch.RequestSequence;
            if(!value.GroupSequenceMatches)value.Mismatch|=GroupGuardMismatch.GroupSequence;
            if(!value.CommandContextMatches)value.Mismatch|=GroupGuardMismatch.CommandContext;
            if(!value.AdmissionBatchMatches)value.Mismatch|=GroupGuardMismatch.AdmissionBatch;
            if(!value.AllocationThreadMatches)value.Mismatch|=GroupGuardMismatch.AllocationThread;
            if(!value.WorkerMatches)value.Mismatch|=GroupGuardMismatch.Worker;
            if(!value.GroupsMatches)value.Mismatch|=GroupGuardMismatch.Groups;
            if(!value.MembersMatches)value.Mismatch|=GroupGuardMismatch.Members;
            if(!value.PendingMatches)value.Mismatch|=GroupGuardMismatch.Pending;
            if(!value.AllocatingMatches)value.Mismatch|=GroupGuardMismatch.Allocating;
            if(!value.OrdersMatches)value.Mismatch|=GroupGuardMismatch.Orders;
            if(!value.ReservationsMatches)value.Mismatch|=GroupGuardMismatch.Reservations;
            if(!value.GoalsMatches)value.Mismatch|=GroupGuardMismatch.Goals;
            if(!value.IdentitiesMatches)value.Mismatch|=GroupGuardMismatch.Identities;
            if(!value.FailuresMatches)value.Mismatch|=GroupGuardMismatch.Failures;
            if(!value.OperationExhaustedMatches)value.Mismatch|=GroupGuardMismatch.OperationExhausted;
            if(!value.CancelledMatches)value.Mismatch|=GroupGuardMismatch.Cancelled;
            return value;
        }
        private void ValidatePreparedGroup(NavigationGroupCommand candidate,NavigationRetentionBudget.Scope scope)
        {
            scope.AssertOwnerLive();
            if(!ReferenceEquals(candidate.Budget,physicalHostBudget)||!ReferenceEquals(candidate.ExpectedHost,committedHostRoot)||!ReferenceEquals(candidate.ExpectedAuthority,authorityOwners)||candidate.Operation!=authorityOperationIdentity||candidate.ExpectedPublication!=admissionPublicationIdentity||requestSequence!=candidate.ExpectedRequestSequence||groupSequence!=candidate.ExpectedGroupSequence||!ReferenceEquals(commandGroup,candidate.CommandContext)||admissionBatchDepth!=0||hostAllocationThread!=candidate.AllocationThread||!ReferenceEquals(certifiedHost,candidate.Worker)||!ReferenceEquals(groups,candidate.OldGroups)||!ReferenceEquals(memberGroups,candidate.OldMembers)||!ReferenceEquals(pending,candidate.OldPending)||!ReferenceEquals(allocating,candidate.OldAllocating)||!ReferenceEquals(orders,candidate.OldOrders)||!ReferenceEquals(reservations,candidate.OldReservations)||!ReferenceEquals(retainedGoals,candidate.OldGoals)||!ReferenceEquals(movementIdentities,candidate.OldIdentities)||!ReferenceEquals(technicalFailures,candidate.OldFailures)||authorityOperationIdentity==long.MaxValue||scope.IsCancelled){LastGroupGuardObservationForQa=ObserveGroupGuard(candidate,scope);LastGroupGuardCandidateForQa=candidate;throw new InvalidOperationException("Group command lost its observed authority.");}
            for(int i=0;i<candidate.BirthCount;i++)candidate.Births[i].ValidateInstall(scope);
            for(int i=0;i<candidate.Issued.Length;i++){
                var request=candidate.Issued[i];var expected=candidate.Actors[i];
                if(!Crowd.TryGet(request.Entity,out var current)||!ReferenceEquals(expected,current)||current.Incarnation!=request.MemberIdentity.Incarnation||current.MobilityRevision!=request.MemberIdentity.MobilityRevision||!current.Position.Equals(request.Start)||!current.Location.Equals(request.StartLocation.Value)||current.Radius!=request.Profile.Radius||current.Speed!=request.Profile.TankSpeed||current.TurnSpeed!=request.Profile.TankTurnSpeed)throw new InvalidOperationException("Prepared actor changed during group preparation.");
                installedExecutions.TryGetValue(request.Entity,out var installed);if(!ReferenceEquals(installed,candidate.Installed[i])||installedExecutions.IsDetached(request.Entity)!=candidate.InstalledDetached[i])throw new InvalidOperationException("Prepared installed execution changed.");
            }
        }
        private void ValidateMetadataGroupSource(NavigationGroupCommand candidate,NavigationRetentionBudget.Scope scope)
        {
            scope.AssertOwnerLive();
            if(candidate.Issued.Length!=0||candidate.Actors.Length!=0||candidate.IsStop||candidate.IsRemoval||candidate.Arrival)
                throw new InvalidOperationException("Metadata publication cannot carry movement effects.");
            if(!ReferenceEquals(candidate.Budget,physicalHostBudget)||!ReferenceEquals(candidate.ExpectedAuthority,authorityOwners)||candidate.Operation!=authorityOperationIdentity||candidate.ExpectedPublication!=admissionPublicationIdentity||requestSequence!=candidate.ExpectedRequestSequence||groupSequence!=candidate.ExpectedGroupSequence||!ReferenceEquals(commandGroup,candidate.CommandContext)||admissionBatchDepth!=0||!ReferenceEquals(certifiedHost,candidate.Worker)||!ReferenceEquals(groups,candidate.OldGroups)||!ReferenceEquals(memberGroups,candidate.OldMembers)||!ReferenceEquals(pending,candidate.OldPending)||!ReferenceEquals(allocating,candidate.OldAllocating)||!ReferenceEquals(orders,candidate.OldOrders)||!ReferenceEquals(reservations,candidate.OldReservations)||!ReferenceEquals(retainedGoals,candidate.OldGoals)||!ReferenceEquals(movementIdentities,candidate.OldIdentities)||!ReferenceEquals(technicalFailures,candidate.OldFailures)||authorityOperationIdentity==long.MaxValue||scope.IsCancelled){LastGroupGuardObservationForQa=ObserveGroupGuard(candidate,scope);LastGroupGuardCandidateForQa=candidate;throw new InvalidOperationException("Group command lost its observed authority.");}
            for(int i=0;i<candidate.BirthCount;i++)candidate.Births[i].ValidateInstall(scope);
        }
        private void PublishPreparedGroup(NavigationGroupCommand candidate,NavigationRetentionBudget.Scope scope,int[] metadataRecipients=null)
        {
            NavigationCommandAuthority retired=null;NavigationHostRoot retiredObservationHost=null;
            try{scope.Publish(candidate,_=>{
                using(EnterTransport())using(var mailboxGate=Requests.EnterPreparedWrite()){
                    RejectTransferCallbackReentry();
                    if(metadataRecipients!=null){
                        // Metadata consumes the command readset, not the host frontier.
                        // Validate that readset and mailbox before refreshing only its
                        // host pin/token, then run the unchanged complete guard below.
                        ValidateMetadataGroupSource(candidate,scope);mailboxGate.Validate(candidate.MailboxRevision,candidate.Mailbox,candidate.MailboxCount);
                        if(AfterMetadataObservationForQa!=null)LastGroupGuardObservationForQa=ObserveGroupGuard(candidate,scope);
                        var current=committedHostRoot;
                        if(current!=null&&(!current.IsImmutableCaptureRoot||current.References<=0||!current.BelongsTo(physicalHostBudget)))throw new InvalidOperationException("Metadata host is not a live immutable owner of this ledger.");
                        current?.Retain();retiredObservationHost=candidate.ExpectedHost;candidate.ExpectedHost=current;candidate.AllocationThread=hostAllocationThread;
                    }
                    ValidatePreparedGroup(candidate,scope);mailboxGate.Validate(candidate.MailboxRevision,candidate.Mailbox,candidate.MailboxCount);
                    for(int i=0;i<candidate.BirthCount;i++)candidate.Births[i].ActivateNoThrow(this);
                    if(metadataRecipients!=null)foreach(int id in metadataRecipients)if(memberGroups.ContainsKey(id))DetachInstalledExecution(id);
                    retired=groupCommandBacking;groupCommandBacking=candidate.Committed;
                    groups=candidate.Groups;memberGroups=candidate.MemberGroups;orders=candidate.Orders;pending=candidate.Pending;allocating=candidate.Allocating;reservations=candidate.Reservations;retainedGoals=candidate.Goals;movementIdentities=candidate.Identities;technicalFailures=candidate.Failures;arrivalLedger=candidate.Ledger;
                    requestSequence=candidate.RequestSequence;groupSequence=candidate.GroupSequence;if(PublishAdmissionOwnerNoThrow(candidate.AdmissionOwner))candidate.AdmissionOwner=null;authorityOperationIdentity=candidate.Operation+1;
                    for(int i=0;i<candidate.Actors.Length;i++){var actor=candidate.Actors[i];actor.FailedRequest=actor.FailedOrder=actor.FailedGroupId=actor.FailedRevision=0;actor.FailedGoal=null;actor.LastFailure=NavigationOutcome.Idle;if(candidate.Installed[i]!=null)installedExecutions.SetDetachedNoThrow(actor.Id,true);}
                    mailboxGate.InstallNoThrow(candidate.Mailbox,candidate.MailboxCount,candidate.Committed.MailboxOwners);commandGroup=null;commandActivationRevision=0;preparedCommandGroup=null;preparedCommandScope=null;candidate.Published=true;for(int i=0;i<candidate.BirthCount;i++)candidate.Births[i]=null;candidate.BirthCount=0;
                }
            });}finally{retiredObservationHost?.Release();}
            scope.ResolveCommitted();candidate.ReleaseObservationPins();retired?.ReleaseStorage();PruneArrivalAttempts();
            SignalHost();if(!candidate.Arrival)foreach(var request in candidate.Issued)RouteRequested?.Invoke(request);
        }
        private NavigationRetentionBudget.Scope DetachPreparedCommandNoThrow(){var scope=preparedCommandScope;preparedCommandScope=null;preparedCommandGroup=null;commandMoveRefused=false;return scope;}
        private void FinishPreparedCommandGroup(bool applied,IEnumerable<int> ids)
        {
            var candidate=preparedCommandGroup;var scope=preparedCommandScope;
            try{
                if(applied){var accepted=ids.Distinct().OrderBy(id=>id).ToArray();if(!accepted.SequenceEqual(candidate.IsStop?candidate.Actors.Select(a=>a.Id):candidate.Issued.Select(r=>r.Entity)))throw new InvalidOperationException("Prepared group differs from accepted recipients.");if(candidate.IsStop)PublishPreparedStop(candidate,scope);else PublishPreparedGroup(candidate,scope);}
            }finally{
                using(EnterTransport()){if(ReferenceEquals(preparedCommandGroup,candidate)){preparedCommandGroup=null;preparedCommandScope=null;commandGroup=null;commandActivationRevision=0;}}
                scope.Dispose();candidate.ReleaseStorage();
            }
        }
    }
}
