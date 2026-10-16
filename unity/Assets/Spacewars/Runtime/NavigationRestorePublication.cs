using System;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // One cold candidate, never used by a worker before guarded installation.
    internal sealed class NavigationRestoreCandidate : NavOwnedStorage,IDisposable
    {
        private static long Layout(int groups,int leases)=>checked(NavStorageLayout.ObjectBytesBound(typeof(NavigationRestoreCandidate))+NavStorageLayout.ObjectBytesBound(typeof(NavigationSession))+96L+8L*groups+16L*leases);
        internal NavigationSession Session;internal NavigationRequest[] Requests;
        internal readonly NavigationLiveGroup[] Groups;internal readonly ArrivalProviderLease[] Leases;
        internal bool Transferred;internal int LeaseCount;
        internal override bool IsImmutableCaptureRoot=>false;
        private NavigationRestoreCandidate(int groups,int leases):base(Layout(groups,leases)){Groups=new NavigationLiveGroup[groups];Leases=new ArrivalProviderLease[leases];Retain();}
        internal static NavigationRestoreCandidate CreateOwned(int groups,int leases,INavStorageReservation reservation){using(var allocation=new NavStorageAllocation(reservation.TryReserve)){allocation.Reserve(Layout(groups,leases));return allocation.Register(new NavigationRestoreCandidate(groups,leases));}}
        public void Dispose()=>Release();
        protected override void ReleaseEdges(){if(!Transferred&&Session!=null){Session.ReleaseCommittedHostRoot();Session.DestroyNavigationAuthority();}Session=null;Requests=null;}
    }
    public sealed partial class NavigationSession
    {
        internal NavigationRequest[] RestoreWorldState(NavigationSessionState state)
        {
            if(state==null||state.Generation!=Generation)throw new ArgumentException("Navigation restore generation mismatch.");
            NavigationAuthorityOwners sourceAuthority;NavigationHostRoot sourceHost;NavigationCommandAuthority sourceBacking;NavigationAdmission sourceAdmission;NavigationInstalledExecutions sourceInstalled;
            NavigationCommandSlots<NavigationAnswer,NavigationHostAnswer> sourceTracking;int trackingRevision;NavCrowd sourceCrowd;long operation,requestsRevision,answersRevision;NavigationRetentionBudget budget;
            using(EnterTransport()){
                if(certifiedHost!=null||hostAllocationThread!=0||admissionBatchDepth!=0||commandGroup!=null||Crowd.Units.Count!=0||pending.Count!=0||orders.Count!=0||Requests.Count!=0||Answers.Count!=0||(ownedAnswerTracking?.Count??0)!=0||countedCorridorAnswers.Count!=0)throw new ArgumentException("Navigation restore destination is not detached and empty.");
                sourceAuthority=authorityOwners;sourceHost=committedHostRoot;sourceBacking=groupCommandBacking;sourceAdmission=admission;sourceCrowd=Crowd;sourceInstalled=installedExecutions;sourceTracking=ownedAnswerTracking;trackingRevision=sourceTracking?.Revision??0;sourceTracking?.Retain();
                operation=authorityOperationIdentity;requestsRevision=Requests.Revision;answersRevision=Answers.Revision;budget=physicalHostBudget;
                sourceAuthority?.Retain();sourceHost?.Retain();sourceBacking?.Retain();sourceInstalled?.Retain();
            }
            try{
                if(state.RestoreBudget!=null&&!ReferenceEquals(state.RestoreBudget,budget))throw new ArgumentException("Restore belongs to another outer ledger.");
                if(!budget.TryOpenScope(NavigationPreparationKind.Decode,Generation,sourceAdmission.PublicationIdentity,out var scope))throw new InvalidOperationException("Cold candidate admission refused.");
                using(scope){NavigationRestoreCandidate candidate=null;
                    try{
                        int groupCount=state.Groups?.Length??0;
                        // One lease per captured request is a checked upper bound;
                        // spare slots and the restored request array are paid up front.
                        candidate=NavigationRestoreCandidate.CreateOwned(groupCount,state.Requests?.Length??0,scope.OwnedReservation());
                        candidate.Session=new NavigationSession(Generation,geometry,profile,navigationGeometry,terrain,budget,null,transportGate,terrainNameOwners);
                        candidate.Session.marchConfigured=marchConfigured;candidate.Session.marchStretch=marchStretch;candidate.Session.marchProfileRevision=marchProfileRevision;
                        candidate.Requests=candidate.Session.RestoreWorldStateCore(state);
                        int at=0;if(candidate.Session.groups is NavigationCommandSlots<long,GroupOrderState> slots)for(int i=0;i<slots.Capacity;i++)if(slots.TryReadSlot(i,out long key,out _)){
                            var group=slots.OwnerFor(key) as NavigationLiveGroup;if(group==null||!group.BelongsToSession(candidate.Session)||at==candidate.Groups.Length)throw new ArgumentException("Cold live-group installation owner mismatch.");candidate.Groups[at++]=group;
                        }
                        if(at!=candidate.Groups.Length)throw new ArgumentException("Cold group catalog count mismatch.");
                        foreach(var lease in candidate.Session.arrivalProjectionLeases.Values)if(!ReferenceEquals(lease.Session,candidate.Session))throw new ArgumentException("Cold lease Session mismatch.");
                        candidate.LeaseCount=candidate.Session.arrivalProjectionLeases.Count;
                        if(candidate.LeaseCount>candidate.Leases.Length)throw new ArgumentException("Cold lease count exceeds request catalog.");
                        candidate.Session.arrivalProjectionLeases.Values.CopyTo(candidate.Leases,0);
                            scope.Attach(candidate);NavigationAuthorityOwners retiredAuthority=null;NavigationHostRoot retiredHost=null;NavigationCommandAuthority retiredBacking=null;NavigationTransportOwner retiredTransport=null;NavMailbox<NavigationAnswer> retiredAnswers=null;NavRetainedArray<NavNameReference> retiredTerrain=null;NavigationInstalledExecutions retiredInstalled=null;NavigationLiveArrivalTables retiredArrival=null;NavigationLiveArrivalLeaseTable retiredLeases=null;NavigationLiveRegistrations retiredRegistered=null;NavigationCommandSlots<int,NavigationRequest> retiredProbes=null;NavigationLiveProbeAnswers retiredProbeAnswers=null;NavigationCommandSlots<NavigationAnswer,NavigationHostAnswer> retiredTracking=null;NavigationBarrierReceipt retiredBarrier=null,retiredLegacyProbe=null;
                            scope.Publish(candidate,_=>{using(EnterTransport()){
                                if(!ReferenceEquals(sourceTracking,ownedAnswerTracking)||(ownedAnswerTracking?.Revision??0)!=trackingRevision||operation!=authorityOperationIdentity||!ReferenceEquals(sourceAuthority,authorityOwners)||!ReferenceEquals(sourceHost,committedHostRoot)||!ReferenceEquals(sourceBacking,groupCommandBacking)||!ReferenceEquals(sourceAdmission,admission)||!ReferenceEquals(sourceInstalled,installedExecutions)||!ReferenceEquals(sourceCrowd,Crowd)||Requests.Revision!=requestsRevision||Answers.Revision!=answersRevision||Requests.Count!=0||Answers.Count!=0||Crowd.Units.Count!=0||certifiedHost!=null||hostAllocationThread!=0||commandGroup!=null||admissionBatchDepth!=0||scope.IsCancelled)throw new InvalidOperationException("Cold restore lost destination source.");
                                foreach(var group in candidate.Groups)if(!group.BelongsToSession(candidate.Session))throw new InvalidOperationException("Cold group changed before installation.");
                                retiredAuthority=authorityOwners;retiredHost=committedHostRoot;retiredBacking=groupCommandBacking;retiredTransport=transportOwner;retiredAnswers=Answers;retiredTerrain=terrainNameOwners;retiredInstalled=installedExecutions;retiredArrival=liveArrivalTables;retiredLeases=liveArrivalLeaseTable;retiredRegistered=liveRegistrations;retiredProbes=ownedProbes;retiredProbeAnswers=liveProbeAnswers;retiredTracking=ownedAnswerTracking;retiredBarrier=barrierPayloads;retiredLegacyProbe=legacyProbePayloads;
                                Requests.CloseNoThrow();Answers.CloseNoThrow();
                                foreach(var group in candidate.Groups)group.ActivateNoThrow(this);for(int i=0;i<candidate.LeaseCount;i++)candidate.Leases[i].Session=this;
                                AdoptColdStateNoThrow(candidate.Session);authorityOperationIdentity=operation+1;candidate.Transferred=true;
                            }});
                            scope.ResolveCommitted();retiredBarrier?.Release();retiredLegacyProbe?.Release();sourceCrowd.ReleaseCrowdStorage();retiredTracking?.Release();retiredProbes?.Release();retiredProbeAnswers?.Release();retiredRegistered?.Release();retiredLeases?.Release();retiredArrival?.Release();retiredInstalled?.Release();retiredTerrain?.Release();retiredAnswers.ReleaseRetention();retiredTransport?.Release();retiredHost?.Release();retiredBacking?.ReleaseStorage();retiredAuthority?.Release();
                        return candidate.Requests;
                    }finally{candidate?.Dispose();}
                }
            }finally{sourceTracking?.Release();sourceInstalled?.Release();sourceBacking?.Release();sourceHost?.Release();sourceAuthority?.Release();}
        }
        private void AdoptColdStateNoThrow(NavigationSession candidate)
        {
            authorityOwners=candidate.authorityOwners;terrainNameOwners=candidate.terrainNameOwners;
            geometry=candidate.geometry;
            navigationGeometry=candidate.navigationGeometry;
            profile=candidate.profile;
            Crowd=candidate.Crowd;Crowd.StorageSource=this;
            committedHostRoot=candidate.committedHostRoot;
            groupCommandBacking=candidate.groupCommandBacking;
            transportOwner=candidate.transportOwner;
            Requests=candidate.Requests;
            Answers=candidate.Answers;
            liveRegistrations=candidate.liveRegistrations;requestTransferHandler=candidate.requestTransferHandler;requestEnqueueValidator=candidate.requestEnqueueValidator;
            registeredRoutes=candidate.registeredRoutes;
            registeredGraphKeys=candidate.registeredGraphKeys;
            ownedProbes=candidate.ownedProbes;liveProbeAnswers=candidate.liveProbeAnswers;
            probes=candidate.probes;
            probeAnswers=candidate.probeAnswers;
            legacyProbePayloads=candidate.legacyProbePayloads;barrierPayloads=candidate.barrierPayloads;barrierPayloadRevision=candidate.barrierPayloadRevision;
            pending=candidate.pending;
            orders=candidate.orders;
            reservations=candidate.reservations;
            technicalFailures=candidate.technicalFailures;
            retainedGoals=candidate.retainedGoals;
            movementIdentities=candidate.movementIdentities;
            requestSequence=candidate.requestSequence;
            RejectedResults=candidate.RejectedResults;
            AppliedResults=candidate.AppliedResults;
            UnreachableResults=candidate.UnreachableResults;
            LastCorridorFailure=candidate.LastCorridorFailure;
            installedExecutions=candidate.installedExecutions;
            countedCorridorAnswers=candidate.countedCorridorAnswers;ownedAnswerTracking=candidate.ownedAnswerTracking;
            installedCorridorBytes=candidate.installedCorridorBytes;
            installedGeometryBytes=candidate.installedGeometryBytes;
            answerCorridorBytes=candidate.answerCorridorBytes;
            hostRetainedBytes=candidate.hostRetainedBytes;
            groups=candidate.groups;
            memberGroups=candidate.memberGroups;
            groupSequence=candidate.groupSequence;
            commandActivationRevision=candidate.commandActivationRevision;
            RetireAdmissionNoThrow(admissionOwner);admissionOwner=candidate.admissionOwner;admission=candidate.admission;
            admissionPublicationIdentity=candidate.admissionPublicationIdentity;
            allocating=candidate.allocating;
            liveArrivalLeaseTable=candidate.liveArrivalLeaseTable;liveArrivalTables=candidate.liveArrivalTables;arrivalAttempts=candidate.arrivalAttempts;
            arrivalLedger=candidate.arrivalLedger;
            arrivalProviders=candidate.arrivalProviders;
            arrivalProviderIds=candidate.arrivalProviderIds;
            arrivalProjectionLeases=candidate.arrivalProjectionLeases;
            arrivalRetirements=candidate.arrivalRetirements;
            arrivalSequence=candidate.arrivalSequence;
            arrivalCommitSequence=candidate.arrivalCommitSequence;
            arrivalProviderSequence=candidate.arrivalProviderSequence;
            arrivalLeaseSequence=candidate.arrivalLeaseSequence;
            arrivalAcquisitionSequence=candidate.arrivalAcquisitionSequence;
            arrivalRetirementSequence=candidate.arrivalRetirementSequence;
            arrivalReleaseSequence=candidate.arrivalReleaseSequence;
            physicalArrivalEnabled=candidate.physicalArrivalEnabled;
        }
    }
}
