using System;
using System.Collections.Generic;
using System.Globalization;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        internal int OwnedRebindPublications{get;private set;}
        internal long LastOwnedRebindPublicationAllocatedBytes{get;private set;}=-1;
        private struct RebindActor
        {
            internal NavUnit Actor;
            internal NavOwnedStorage Owner,Global,Local,LocationOwner;
            internal NavigationInstalledExecutionOwner Installed;
            internal NavigationLiveGroup Group;
            internal NavigationAuthorityGroup Version;
            internal NavPoint Position,SourceGoal;internal NavLocation Location;internal NavigationOutcome SourceOutcome,GeometryOutcome;internal NavRetainedArray<NavPoint> RetiredGlobal,RetiredLocal;internal NavOwnedStorage RetiredInstalled;
            internal long SourceFailedRequest,SourceFailedOrder,SourceFailedGroup,SourceFailedRevision;internal NavigationOutcome SourceLastFailure;
            internal NavNameReference SourceLocationName,SourceGoalName,SourceFailureName;internal NavLocation? SourceGoalLocation,SourceFailedGoal;
            internal long Incarnation,Mobility,InstalledGroup,InstalledRevision,InstalledRequest,InstalledOrder;
            internal int Id,Team;internal double Radius,Speed,TurnSpeed,TargetSpeed,TargetTurn;internal PlayableDomain.BalanceMobilitySource BalanceMobility;
            internal bool Held,Moving,Detached,Active,Preserve;
            internal static bool Bits(double a,double b)=>BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b);
            internal bool Matches(NavUnit unit)
                =>ReferenceEquals(unit,Actor)&&ReferenceEquals(unit.StorageOwner,Owner)&&ReferenceEquals(unit.StorageOwner.Global,Global)&&ReferenceEquals(unit.StorageOwner.Local,Local)&&unit.Id==Id&&unit.Team==Team&&unit.Incarnation==Incarnation&&unit.MobilityRevision==Mobility&&unit.Held==Held&&unit.Moving==Moving&&Bits(unit.Position.X,Position.X)&&Bits(unit.Position.Z,Position.Z)&&Bits(unit.Radius,Radius)&&Bits(unit.Speed,Speed)&&Bits(unit.TurnSpeed,TurnSpeed)&&unit.InstalledGroupId==InstalledGroup&&unit.InstalledRevision==InstalledRevision&&unit.InstalledRequest==InstalledRequest&&unit.InstalledOrder==InstalledOrder&&NavigationAdmissionMemberStorage.SameLocation(unit.Location,Location);
            internal void Release(){RetiredGlobal?.Release();RetiredLocal?.Release();RetiredInstalled?.Release();Owner?.Release();Global?.Release();Local?.Release();LocationOwner?.Release();Installed?.Release();Group?.Release();Version?.Release();}
        }
        private struct RebindRetry
        {
            internal NavigationHostRequest Source,Next;
            internal NavigationPreparedCrowdNameWrite ArrivalFailureNames;internal NavArrivalStatus ArrivalTerminalStatus;
            internal NavArrivalProposal ArrivalProposal;internal int ArrivalOrder;internal byte ArrivalKind;
            internal NavPoint Goal;internal int ActorIndex,Entity,GroupIndex,MemberIndex;internal bool Valid,Assigned,WasAllocating,Selected,GeometryRejected;
            internal long Group,Order,Membership;
        }
        private sealed partial class PreparedRebind : NavOwnedStorage,IDisposable
        {
            private static long Aligned(long n)=>checked((n+7)/8*8);
            private static long Layout(int actors,int retries,int fifo,int barrier,int requests,int answers,int rallies,int attempts,int leases,int groupCapacity,int foundations)=>checked(NavStorageLayout.ObjectBytesBound(typeof(PreparedRebind))+13*32L+Aligned(foundations*NavStorageLayout.ValueBytes(typeof(PlayableDomain.GeometryFoundationSource)))+Aligned(attempts*NavStorageLayout.ValueBytes(typeof(RebindAttempt)))+Aligned(leases*NavStorageLayout.ValueBytes(typeof(RebindLease)))+Aligned((attempts+leases)*NavStorageLayout.ValueBytes(typeof(RebindRetirement)))+Aligned(rallies*NavStorageLayout.ValueBytes(typeof(PlayableDomain.BalanceRallySource)))+Aligned(actors*NavStorageLayout.ValueBytes(typeof(RebindActor)))+Aligned(retries*NavStorageLayout.ValueBytes(typeof(RebindRetry)))+Aligned(groupCapacity*NavStorageLayout.ValueBytes(typeof(RebindGroup)))+Aligned((fifo+barrier+answers)*NavStorageLayout.ValueBytes(typeof(RebindDrain)))+24L*requests+24L*answers);
            internal readonly RebindActor[] Actors;internal readonly RebindRetry[] Retries;
            internal NavigationHostRoot Host;
            internal NavigationAuthorityOwners SourceCore,Core,RetiredCore;
            internal NavigationCommandAuthority Backing;
            internal NavigationAdmissionStorage SourceAdmission,Admission;
            internal NavigationCommandSlots<int,NavigationRequest> SourceAllocating,Allocating,SourcePending;
            internal NavigationCommandSlots<int,string> SourceIdentities,Identities;
            internal NavOwnedStorage RetiredAllocating,RetiredIdentities;
            internal CertifiedRouteKernel Worker;internal long Attachment,Operation,Sequence,NextSequence,Publication,RequestsRevision,AnswersRevision;
            internal int AllocatingRevision,IdentityRevision,PendingRevision,AllocationThread,InstalledCount;
            internal NavigationGroupCommand Bootstrap;
            internal readonly PlayableDomain.GeometryFoundationSource[] Foundations;internal PlayableDomain GeometryDomain;internal NavGeometry GeometryDomainSource;internal bool GeometryChange;internal bool Published;private bool released,coreDestination,allocatingDestination,identityDestination;
            private PreparedRebind(int actors,int retries,int fifo,int barrier,int requests,int answers,int rallies,int attempts,int leases,int groupCapacity,int foundations):base(Layout(actors,retries,fifo,barrier,requests,answers,rallies,attempts,leases,groupCapacity,foundations)){Foundations=new PlayableDomain.GeometryFoundationSource[foundations];ArrivalAttempts=new RebindAttempt[attempts];ArrivalLeases=new RebindLease[leases];ArrivalRetirements=new RebindRetirement[attempts+leases];BalanceRallies=new PlayableDomain.BalanceRallySource[rallies];Actors=new RebindActor[actors];Retries=new RebindRetry[retries];RebindGroups=new RebindGroup[groupCapacity];Drains=new RebindDrain[fifo+barrier+answers];QueuedRequests=new NavigationRequest[requests];AnswerRows=new NavigationAnswer[answers];RetiredRequests=new NavOwnedStorage[2*requests];RetiredAnswers=new NavOwnedStorage[2*answers];FifoCount=fifo;BarrierCount=barrier;Retain();}
            internal static PreparedRebind Create(int actors,int retries,INavStorageReservation r,int fifo=0,int barrier=0,int requests=0,int answers=0,int rallies=0,int attempts=0,int leases=0,int groupCapacity=-1,int foundations=0){if(groupCapacity<0)groupCapacity=retries;using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout(actors,retries,fifo,barrier,requests,answers,rallies,attempts,leases,groupCapacity,foundations));return a.Register(new PreparedRebind(actors,retries,fifo,barrier,requests,answers,rallies,attempts,leases,groupCapacity,foundations));}}
            internal void SealDestinations(){if(OrdinaryFailure){SealOrdinaryFailureDestinations();return;}if(ArrivalRetry){SealArrivalRetryDestinations();return;}if(Ordinary){SealOrdinaryDestinations();return;}Core.Retain();coreDestination=true;Allocating.Retain();allocatingDestination=true;Identities.Retain();identityDestination=true;}
            internal void TransferNoThrow(){TransferOrdinaryNoThrow();TransferArrivalRetryNoThrow();TransferOrdinaryFailureNoThrow();coreDestination=allocatingDestination=identityDestination=false;Published=true;}
            public void Dispose(){if(released)return;released=true;Release();}
            internal override void ReleaseOwner()=>Dispose();
            protected override void ReleaseEdges()
            {
                ReleaseOrdinaryFailureEdges();ReleaseMixedArrivalEdges();ReleaseArrivalRetryEdges();ReleaseRebindArrivalEdges();ReleaseOrdinaryEdges();foreach(var actor in Actors)actor.Release();foreach(var row in Retries){row.Source?.Release();row.Next?.ReleaseStorage();row.ArrivalFailureNames?.Release();}
                Bootstrap?.ReleaseStorage();Host?.Release();SourceCore?.Release();Backing?.Release();SourceAdmission?.Release();SourceAllocating?.Release();SourcePending?.Release();SourceIdentities?.Release();
                if(coreDestination)Core?.Release();if(allocatingDestination)Allocating?.Release();if(identityDestination)Identities?.Release();Core?.ReleaseStorage();Allocating?.ReleaseStorage();Identities?.ReleaseStorage();Admission?.Release();RetiredCore?.Release();RetiredAllocating?.Release();RetiredIdentities?.Release();
            }
        }
        private void ObserveRebindActors(PreparedRebind c)
        {
                        for(int i=0;i<c.Actors.Length;i++){
                            var actor=Crowd.Units[i];ref var fact=ref c.Actors[i];fact.Actor=actor;fact.Owner=NavPhysicalMacro.Acquire(c,actor.StorageOwner);fact.Global=NavPhysicalMacro.Acquire(c,actor.StorageOwner.Global);fact.Local=NavPhysicalMacro.Acquire(c,actor.StorageOwner.Local);fact.Position=actor.Position;fact.Location=actor.Location;fact.LocationOwner=NavPhysicalMacro.Acquire(c,actor.Location.NameReference.Owner);fact.Id=actor.Id;fact.Team=actor.Team;fact.Incarnation=actor.Incarnation;fact.Mobility=actor.MobilityRevision;fact.Radius=actor.Radius;fact.Speed=actor.Speed;fact.TurnSpeed=actor.TurnSpeed;fact.TargetSpeed=fact.Speed;fact.TargetTurn=fact.TurnSpeed;if(c.BalanceDomain!=null&&!c.BalanceDomain.TryBalanceMobility(actor.Id,c.BalanceProfile,out fact.BalanceMobility,out fact.TargetSpeed,out fact.TargetTurn))throw new InvalidOperationException("Balance actor source changed.");fact.Held=actor.Held;fact.Moving=actor.Moving;fact.SourceGoal=actor.Goal;fact.SourceOutcome=actor.Outcome;fact.SourceGoalLocation=actor.GoalLocation;fact.SourceFailedGoal=actor.FailedGoal;fact.SourceFailedRequest=actor.FailedRequest;fact.SourceFailedOrder=actor.FailedOrder;fact.SourceFailedGroup=actor.FailedGroupId;fact.SourceFailedRevision=actor.FailedRevision;fact.SourceLastFailure=actor.LastFailure;fact.InstalledGroup=actor.InstalledGroupId;fact.InstalledRevision=actor.InstalledRevision;fact.InstalledRequest=actor.InstalledRequest;fact.InstalledOrder=actor.InstalledOrder;
                            fact.Installed=NavPhysicalMacro.Acquire(c,installedExecutions.OwnerFor(actor.Id));fact.Detached=fact.Installed!=null&&installedExecutions.IsDetached(actor.Id);fact.Active=fact.Installed!=null&&TryActiveInstalledExecution(actor.Id,out _);
                            if(memberGroups.TryGetValue(actor.Id,out var gid)&&groups is NavigationCommandSlots<long,GroupOrderState> ownedGroups&&ownedGroups.OwnerFor(gid) is NavigationLiveGroup group){fact.Group=NavPhysicalMacro.Acquire(c,group);fact.Version=NavPhysicalMacro.Acquire(c,group.ImmutableVersion);}
                        }
        }
        private bool RebindSourceCurrent(PreparedRebind c)
        {
            if(!RebindBalanceSourceCurrent(c))return false;
            if(c.Ordinary)return OrdinaryRebindSourceCurrent(c);
            if(c.Backing==null)return EmptyRebindSourceCurrent(c);
            if(!ReferenceEquals(committedHostRoot,c.Host)||!ReferenceEquals(authorityOwners,c.SourceCore)||!ReferenceEquals(groupCommandBacking,c.Backing)||!ReferenceEquals(admissionOwner,c.SourceAdmission)||authorityOperationIdentity!=c.Operation||requestSequence!=c.Sequence||admissionPublicationIdentity!=c.Publication||commandGroup!=null||admissionBatchDepth!=0||hostAllocationThread!=c.AllocationThread||certifiedHostAttachmentIdentity!=c.Attachment||!ReferenceEquals(certifiedHost,c.Worker)||!ReferenceEquals(allocating,c.SourceAllocating)||c.SourceAllocating.Revision!=c.AllocatingRevision||!ReferenceEquals(movementIdentities,c.SourceIdentities)||c.SourceIdentities.Revision!=c.IdentityRevision||!ReferenceEquals(pending,c.SourcePending)||c.SourcePending.Revision!=c.PendingRevision||pending.Count!=0||probes.Count!=0||ProbeAnswerCount!=0||barrierAnswers.Count!=0||Requests.Count!=0||Answers.Count!=0||Requests.Revision!=c.RequestsRevision||Answers.Revision!=c.AnswersRevision||Crowd.Units.Count!=c.Actors.Length||installedExecutions.Count!=c.InstalledCount)return false;
            for(int i=0;i<c.Actors.Length;i++){
                var row=c.Actors[i];if(!Crowd.TryGet(row.Id,out var actor)||!row.Matches(actor)||!ReferenceEquals(installedExecutions.OwnerFor(row.Id),row.Installed)||row.Installed!=null&&installedExecutions.IsDetached(row.Id)!=row.Detached||row.Group!=null&&!ReferenceEquals(row.Group.ImmutableVersion,row.Version))return false;
            }
            return true;
        }
        private bool EmptyRebindSourceCurrent(PreparedRebind c)
        {
            if(!RebindBalanceSourceCurrent(c)||!ReferenceEquals(committedHostRoot,c.Host)||!ReferenceEquals(authorityOwners,c.SourceCore)||groupCommandBacking!=null||!ReferenceEquals(admissionOwner,c.SourceAdmission)||authorityOperationIdentity!=c.Operation||requestSequence!=c.Sequence||admissionPublicationIdentity!=c.Publication||commandGroup!=null||admissionBatchDepth!=0||hostAllocationThread!=c.AllocationThread||certifiedHostAttachmentIdentity!=c.Attachment||!ReferenceEquals(certifiedHost,c.Worker)||!EmptyCommandStateForProbe()||probes.Count!=0||ProbeAnswerCount!=0||barrierAnswers.Count!=0||Requests.Count!=0||Answers.Count!=0||Requests.Revision!=c.RequestsRevision||Answers.Revision!=c.AnswersRevision||!Requests.HasInitialPreparedBacking||Crowd.Units.Count!=c.Actors.Length||installedExecutions.Count!=0)return false;
            if(c.Bootstrap!=null&&!ProbeBootstrapNamesCurrent(c.Bootstrap.Committed.CrowdNames))return false;
            for(int i=0;i<c.Actors.Length;i++)if(!Crowd.TryGet(c.Actors[i].Id,out var actor)||!c.Actors[i].Matches(actor))return false;
            return true;
        }
        // Numeric held rows have bounded formatting; account for the actual
        // culture sign string as well as geometry-sized temporary arrays.
        internal static long RebindStagingScratchBound(int actors,int retries,int obstacles)
        {
            if(actors<0||retries<0||obstacles<0)throw new ArgumentOutOfRangeException();
            long rowChars=checked(256L+4L*CultureInfo.CurrentCulture.NumberFormat.NegativeSign.Length);
            long joinedChars=checked((rowChars+1)*actors);
            long obstacleBytes=checked((obstacles+(long)actors)*NavStorageLayout.ValueBytes(typeof(NavObstacle)));
            return checked(8192L+4096L*actors+4096L*retries+64L+2L*(32L+(obstacleBytes+7)/8*8)+
                32L*rowChars*actors+NavContract.DigestScratchBytes(128L+3L*joinedChars));
        }
        private static NavGeometry RebindMovementGeometry(PreparedRebind c,RebindActor mover,NavGeometry basis)
        {
            int held=0;foreach(var other in c.Actors)if(other.Id!=mover.Id&&other.Held&&other.Team==mover.Team)held++;
            if(held==0)return basis;
            var solids=new NavObstacle[checked(basis.Obstacles.Count+held)];for(int i=0;i<basis.Obstacles.Count;i++)solids[i]=basis.Obstacles[i];
            int at=basis.Obstacles.Count;foreach(var other in c.Actors)if(other.Id!=mover.Id&&other.Held&&other.Team==mover.Team)solids[at++]=new NavObstacle(other.Position,other.Radius);
            return new NavGeometry(basis.HalfExtent,solids,basis.Revision);
        }
        private static string RebindHeldIdentity(PreparedRebind c,RebindActor mover)
        {
            int held=0;foreach(var other in c.Actors)if(other.Id!=mover.Id&&other.Held&&other.Team==mover.Team)held++;
            var rows=new string[held];int at=0;var culture=CultureInfo.InvariantCulture;
            foreach(var other in c.Actors)if(other.Id!=mover.Id&&other.Held&&other.Team==mover.Team)rows[at++]=other.Id+":"+other.Incarnation+":"+other.Mobility+":"+other.Team+":"+other.Position.X.ToString("R",culture)+":"+other.Position.Z.ToString("R",culture)+":"+other.Radius.ToString("R",culture);
            string raw=string.Join("|",rows);return raw.Length<=256?raw:NavContract.Digest(w=>{w.Write("held-binding-v1");w.Write(raw);});
        }
        // Allocating arrivals or idle movement, with no queued/pending work
        // to drain. Unsupported lanes are selected before preparation.
        private bool? TryOwnedRebind(NavigationProfile target,PlayableDomain balanceDomain=null,PlayableProfile balanceProfile=null)
        {
            if(System.Threading.Monitor.IsEntered(transportGate))return hostAllocationThread!=0?(bool?)false:null;
            using(EnterTransport())if(!RebindHostBoundaryQuiescent())return false;
            var ordinary=TryOwnedOrdinaryRebind(target,balanceDomain,balanceProfile);if(ordinary.HasValue)return ordinary;
            int actors,retries,obstacles;long publication;
            using(EnterTransport()){
                if((groupCommandBacking==null?(!EmptyCommandStateForProbe()||!Requests.HasInitialPreparedBacking):(!(allocating is NavigationCommandSlots<int,NavigationRequest>)||!(movementIdentities is NavigationCommandSlots<int,string>)||!(pending is NavigationCommandSlots<int,NavigationRequest>)))||pending.Count!=0||probes.Count!=0||ProbeAnswerCount!=0||barrierAnswers.Count!=0||Requests.Count!=0||Answers.Count!=0||commandGroup!=null||admissionBatchDepth!=0||hostAllocationThread!=0)return null;
                var ownedAllocating=allocating as NavigationCommandSlots<int,NavigationRequest>;
                for(int slot=0;ownedAllocating!=null&&slot<ownedAllocating.Capacity;slot++)if(ownedAllocating.TryReadSlot(slot,out _,out var request)&&request.StorageOwner==null)return null;
                for(int i=0;i<Crowd.Units.Count;i++){var actor=Crowd.Units[i];if(actor.StorageOwner==null||installedExecutions.ContainsKey(actor.Id)&&installedExecutions.OwnerFor(actor.Id)==null)return null;}
                if(!CanRebind)throw new InvalidOperationException("Navigation still has too many in-flight routes to rebind atomically.");actors=Crowd.Units.Count;retries=allocating.Count;obstacles=navigationGeometry.Obstacles.Count;publication=admissionPublicationIdentity;
            }
            if(target==null)throw new ArgumentNullException(nameof(target));
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,publication,out var scope))return false;
            using(scope){PreparedRebind c=null;try{
                var r=scope.OwnedReservation();c=PreparedRebind.Create(actors,retries,r,rallies:balanceDomain?.PreparedBalanceRallyCount??0);BindPreparedBalance(c,balanceDomain,balanceProfile);
                using(NavStorageScratch.Reserve(r,RebindStagingScratchBound(actors,retries,obstacles))){
                    using(EnterTransport()){
                        if(Crowd.Units.Count!=actors||allocating.Count!=retries||navigationGeometry.Obstacles.Count!=obstacles||admissionPublicationIdentity!=publication)throw new InvalidOperationException("Rebind observation shape changed.");
                        c.Host=NavPhysicalMacro.Acquire(c,committedHostRoot);c.SourceCore=NavPhysicalMacro.Acquire(c,authorityOwners);c.Backing=NavPhysicalMacro.Acquire(c,groupCommandBacking);c.SourceAdmission=NavPhysicalMacro.Acquire(c,admissionOwner);c.SourceAllocating=NavPhysicalMacro.Acquire(c,allocating as NavigationCommandSlots<int,NavigationRequest>);c.AllocatingRevision=c.SourceAllocating?.Revision??0;c.SourceIdentities=NavPhysicalMacro.Acquire(c,movementIdentities as NavigationCommandSlots<int,string>);c.IdentityRevision=c.SourceIdentities?.Revision??0;c.SourcePending=NavPhysicalMacro.Acquire(c,pending as NavigationCommandSlots<int,NavigationRequest>);c.PendingRevision=c.SourcePending?.Revision??0;
                        c.Operation=authorityOperationIdentity;c.Sequence=c.NextSequence=requestSequence;c.Publication=publication;c.Worker=certifiedHost;c.Attachment=certifiedHostAttachmentIdentity;c.AllocationThread=hostAllocationThread;c.RequestsRevision=Requests.Revision;c.AnswersRevision=Answers.Revision;c.InstalledCount=installedExecutions.Count;
                        ObserveRebindActors(c);
                        int n=0;for(int slot=0;c.SourceAllocating!=null&&slot<c.SourceAllocating.Capacity;slot++)if(c.SourceAllocating.TryReadSlot(slot,out int entity,out var old)){
                            ref var row=ref c.Retries[n++];row.Source=NavPhysicalMacro.Acquire(c,old.StorageOwner);row.Entity=entity;row.ActorIndex=-1;for(int i=0;i<actors;i++)if(c.Actors[i].Id==entity){row.ActorIndex=i;break;}
                            var member=Member(entity);row.Valid=member!=null&&old.MemberIdentity!=null&&memberGroups.TryGetValue(entity,out var gid)&&gid==old.MemberIdentity.GroupId&&member.OrderRevision==old.MemberIdentity.OrderRevision&&row.ActorIndex>=0&&c.Actors[row.ActorIndex].Incarnation==old.MemberIdentity.Incarnation;
                            if(row.Valid){row.Group=memberGroups[entity];row.Order=member.OrderRevision;row.Membership=groups[row.Group].MembershipRevision;}
                        }
                        if(!RebindSourceCurrent(c))throw new InvalidOperationException("Rebind observation lost its source.");
                    }
                    if(c.Backing==null)c.Bootstrap=PrepareEmptyProbeCommandBacking(scope);
                    c.Core=NavigationAuthorityOwners.CreateOwned((NavGeometry)c.SourceCore.Geometry.Facade,(NavGeometry)c.SourceCore.NavigationGeometry.Facade,target,r);
                    var nextProfile=(NavigationProfile)c.Core.Profile.Facade;var basis=(NavGeometry)c.Core.NavigationGeometry.Facade;bool compatible=SameNavigationBinding((NavigationProfile)c.SourceCore.Profile.Facade,nextProfile);
                    for(int i=0;i<actors;i++){ref var fact=ref c.Actors[i];fact.Preserve=fact.Installed!=null&&compatible&&!fact.Detached&&fact.Active&&ReferenceEquals(fact.Installed.State.BaseGeometry,basis)&&fact.Installed.Corridor.Corridor.SurfaceProviderId==(terrain?.Id??NavLocation.FlatSurface)&&SameNavigationBinding(fact.Installed.State.Profile,nextProfile.ForUnit(fact.Radius,fact.TargetSpeed,fact.TargetTurn));}
                    c.Allocating=NavigationCommandSlots<int,NavigationRequest>.CreateOwned((c.SourceAllocating??c.Bootstrap.Allocating).Capacity,r);c.Identities=NavigationCommandSlots<int,string>.CreateOwned((c.SourceIdentities??c.Bootstrap.Identities).Capacity,r);
                    using(EnterTransport()){if(!RebindSourceCurrent(c))throw new InvalidOperationException("Rebind maps changed.");c.Allocating.CopyPreparedFrom(c.SourceAllocating??c.Bootstrap.Allocating);c.Identities.CopyPreparedFrom(c.SourceIdentities??c.Bootstrap.Identities);}
                    for(int i=0;i<retries;i++){
                        ref var row=ref c.Retries[i];if(!row.Valid){c.Allocating.Remove(row.Entity);continue;}var fact=c.Actors[row.ActorIndex];var old=row.Source.Request;
                        row.Next=NavigationHostRequest.CreateOwned(Generation,checked(++c.NextSequence),old.Entity,old.Order,nextProfile.ForUnit(fact.Radius,fact.TargetSpeed,fact.TargetTurn),RebindMovementGeometry(c,fact,basis),fact.Position,old.Goal,basis,RebindHeldIdentity(c,fact),new NavigationMemberIdentity(fact.Incarnation,fact.Mobility,row.Group,row.Order,row.Membership),fact.Location,old.GoalLocation,null,r);
                        c.Allocating.SetPreparing(row.Entity,row.Next.Request,row.Next);c.Identities.SetPreparing(row.Entity,row.Next.Request.HeldIdentity,row.Next.HeldIdentityOwner);
                    }
                    c.Allocating.Seal();c.Identities.Seal();c.Admission=PrepareAdmissionProjection(c.Bootstrap?.Groups??groups,c.Bootstrap?.Pending??pending,c.NextSequence,checked(publication+1),c.Operation,scope,c.SourceAdmission,null,c.Core);
                }
                c.SealDestinations();scope.Attach(c);
                Action<object> publish=_=>{long before=GC.GetAllocatedBytesForCurrentThread();if(!RebindSourceCurrent(c)||scope.IsCancelled||c.Operation==long.MaxValue||OwnedRebindPublications==int.MaxValue)throw new InvalidOperationException("Prepared Rebind lost its final source.");
                    if(c.Bootstrap!=null){using(var mailbox=Requests.EnterPreparedWrite()){mailbox.ValidateInitialAppend(c.RequestsRevision,requestEnqueueValidator,requestTransferHandler);InstallProbeCommandBackingNoThrow(c.Bootstrap);mailbox.InstallNoThrow(c.Bootstrap.Mailbox,0,c.Bootstrap.Committed.MailboxOwners);}}
                    c.RetiredCore=authorityOwners;authorityOwners=c.Core;geometry=(NavGeometry)c.Core.Geometry.Facade;navigationGeometry=(NavGeometry)c.Core.NavigationGeometry.Facade;profile=(NavigationProfile)c.Core.Profile.Facade;ApplyRebindMobilityNoThrow(c);Crowd.Rebind(profile);
                    c.RetiredAllocating=groupCommandBacking.Allocating;groupCommandBacking.Allocating=c.Allocating;allocating=c.Allocating;c.RetiredIdentities=groupCommandBacking.Identities;groupCommandBacking.Identities=c.Identities;movementIdentities=c.Identities;
                    for(int i=0;i<c.Actors.Length;i++)if(c.Actors[i].Installed!=null)installedExecutions.SetDetachedNoThrow(c.Actors[i].Id,!c.Actors[i].Preserve);
                    requestSequence=c.NextSequence;if(PublishAdmissionOwnerNoThrow(c.Admission))c.Admission=null;authorityOperationIdentity=c.Operation+(c.BalanceDomain==null?1:2);OwnedRebindPublications++;c.TransferNoThrow();LastOwnedRebindPublicationAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-before;
                };
                using(EnterTransport())scope.Publish(c,publish);scope.ResolveCommitted();FinishPreparedBalance(c);foreach(var row in c.Retries)if(row.Next!=null)EndpointAssigned?.Invoke(row.Next.Request);SignalHost();return true;
            }catch(InvalidOperationException){if(c?.Published==true)throw;return false;}finally{c?.Dispose();}}
        }
    }
}
