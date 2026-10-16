using System;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        internal int OwnedMovementPublications{get;private set;}
        internal long LastOwnedMovementPublicationAllocatedBytes{get;private set;}=-1;
        private sealed class PreparedMovement:NavOwnedStorage,IDisposable
        {
            private static readonly long HeaderBytes=NavStorageLayout.ObjectBytesBound(typeof(PreparedMovement)),ObservationBytes=NavStorageLayout.ValueBytes(typeof(NavigationLiveGroupObservation)),GroupBytes=NavStorageLayout.ValueBytes(typeof(RebindGroup)),AttemptBytes=NavStorageLayout.ValueBytes(typeof(RebindAttempt)),LeaseBytes=NavStorageLayout.ValueBytes(typeof(RebindLease));
            private static long Align(long n)=>checked((n+7)/8*8);
            private static long Layout(int actors,int groups,int attempts,int leases)=>checked(HeaderBytes+7*32L+8L*actors+Align(actors)+Align(groups*ObservationBytes)+Align(groups*GroupBytes)+Align(attempts*AttemptBytes)+Align(leases*LeaseBytes)+Align(9*4L));
            internal readonly NavigationInstalledExecutionOwner[] Installed;internal readonly bool[] Detached;
            internal readonly NavigationLiveGroupObservation[] GroupSources;internal readonly RebindGroup[] Changes;internal readonly RebindAttempt[] Attempts;internal readonly int[] Revisions;
            internal NavPreparedCrowdStep Frame;internal NavigationCommandAuthority Backing;internal NavigationHostRoot Host;internal NavigationAdmissionStorage SourceAdmission,Admission;
            internal NavigationCommandSlots<long,GroupOrderState> Groups;internal NavigationCommandSlots<int,long> Members;
            internal NavigationCommandSlots<int,NavigationRequest> Pending,Allocating,Probes;
            internal NavigationCommandSlots<long,NavigationArrivalReservation> Ledger;
            internal NavigationCommandSlots<int,NavPoint> Goals,Reservations;internal NavigationCommandSlots<int,string> Identities;
            internal NavigationLiveArrivalTables Arrival;internal NavigationLiveArrivalLeaseTable LeaseTable;internal readonly RebindLease[] Leases;internal int LeaseRevision,LeaseTokenRevision;
            internal CertifiedRouteKernel Worker;internal long Operation,Publication,Sequence,Attachment,RequestsRevision,AnswersRevision,Retirement;
            internal int RequestsCount,ProbeCount,ChangeCount,InstalledCount;internal bool SuspendPending,MarchConfigured,Published,Destinations,AdmissionChanged;internal double MarchStretch;internal int MarchRevision;
            private bool disposed;
            private PreparedMovement(int actors,int groups,int attempts,int leases):base(Layout(actors,groups,attempts,leases))
            {Leases=new RebindLease[leases];Installed=new NavigationInstalledExecutionOwner[actors];Detached=new bool[actors];GroupSources=new NavigationLiveGroupObservation[groups];Changes=new RebindGroup[groups];Attempts=new RebindAttempt[attempts];Revisions=new int[9];Retain();}
            internal static PreparedMovement Create(int actors,int groups,int attempts,int leases,INavStorageReservation r){using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout(actors,groups,attempts,leases));return a.Register(new PreparedMovement(actors,groups,attempts,leases));}}
            public void Dispose(){if(disposed)return;disposed=true;Release();}
            internal override void ReleaseOwner()=>Dispose();
            protected override void ReleaseEdges()
            {
                Frame?.Release();LeaseTable?.Release();foreach(var row in Leases)row.Owner?.Release();foreach(var v in Installed)v?.Release();foreach(var v in GroupSources)v.ReleasePins();foreach(var v in Attempts)v.Owner?.Release();
                for(int i=0;i<ChangeCount;i++){if(Destinations)Changes[i].Next?.Release();Changes[i].Next?.ReleaseStorage();Changes[i].Retired?.Release();}
                Backing?.Release();Host?.Release();SourceAdmission?.Release();Admission?.Release();Groups?.Release();Members?.Release();Pending?.Release();Allocating?.Release();Probes?.Release();Ledger?.Release();Goals?.Release();Reservations?.Release();Identities?.Release();Arrival?.Release();
            }
        }
        private bool MovementHostCompatible(PreparedMovement c)
        {
            // A solver-only publication can replace the pinned frontier while this
            // carrier computes. It cannot change authority geometry, attachment or
            // the issued quantum; all authority maps/actor headers remain guarded.
            var now=committedHostRoot;if(ReferenceEquals(now,c.Host))return true;
            return now!=null&&c.Host!=null&&ReferenceEquals(now.AuthorityGeometry,c.Host.AuthorityGeometry)&&now.TickQuantum==c.Host.TickQuantum&&now.QuantumGeneration==c.Host.QuantumGeneration&&now.QuantumSerial==c.Host.QuantumSerial&&now.QuantumTick==c.Host.QuantumTick&&now.QuantumBudget==c.Host.QuantumBudget;
        }
        private bool MovementSourceCurrent(PreparedMovement c)
        {
            if(c.SuspendPending!=SuspendPendingMovement||!physicalArrivalEnabled||admissionBatchDepth!=0||commandGroup!=null||!MovementHostCompatible(c)||!ReferenceEquals(groupCommandBacking,c.Backing)||!ReferenceEquals(admissionOwner,c.SourceAdmission)||authorityOperationIdentity!=c.Operation||admissionPublicationIdentity!=c.Publication||requestSequence!=c.Sequence||!ReferenceEquals(certifiedHost,c.Worker)||certifiedHostAttachmentIdentity!=c.Attachment||!ReferenceEquals(Crowd,c.Frame.SourceCrowd)||!c.Frame.SourceCurrent()||marchConfigured!=c.MarchConfigured||marchStretch!=c.MarchStretch||marchProfileRevision!=c.MarchRevision||barrierAnswers.Count!=0||Answers.Count!=0||Answers.Revision!=c.AnswersRevision||Requests.Count!=c.RequestsCount||Requests.Revision!=c.RequestsRevision||!ReferenceEquals(groups,c.Groups)||c.Groups.Revision!=c.Revisions[0]||!ReferenceEquals(memberGroups,c.Members)||c.Members.Revision!=c.Revisions[1]||!ReferenceEquals(pending,c.Pending)||c.Pending.Revision!=c.Revisions[2]||!ReferenceEquals(allocating,c.Allocating)||c.Allocating.Revision!=c.Revisions[3]||!ReferenceEquals(arrivalLedger,c.Ledger)||c.Ledger.Revision!=c.Revisions[4]||!ReferenceEquals(retainedGoals,c.Goals)||c.Goals.Revision!=c.Revisions[5]||!ReferenceEquals(reservations,c.Reservations)||c.Reservations.Revision!=c.Revisions[6]||!ReferenceEquals(movementIdentities,c.Identities)||c.Identities.Revision!=c.Revisions[7]||(c.Probes==null?probes.Count!=0:!ReferenceEquals(probes,c.Probes)||c.Probes.Revision!=c.Revisions[8])||probes.Count!=c.ProbeCount||!ReferenceEquals(liveArrivalTables,c.Arrival)||c.Arrival.Count!=c.Attempts.Length||arrivalRetirementSequence!=c.Retirement||!ReferenceEquals(liveArrivalLeaseTable,c.LeaseTable)||(c.LeaseTable?.Revision??0)!=c.LeaseRevision||(c.LeaseTable?.Tokens.Revision??0)!=c.LeaseTokenRevision||arrivalProjectionLeases.Count!=c.Leases.Length||(liveArrivalLeaseTable?.Count??0)!=c.Leases.Length||installedExecutions.Count!=c.InstalledCount)return false;
            for(int i=0;i<c.GroupSources.Length;i++)if(!c.GroupSources[i].Owner.MatchesObservation(c.GroupSources[i]))return false;
            for(int i=0;i<c.Attempts.Length;i++){var f=c.Attempts[i];var a=f.Owner.Record;if(!ReferenceEquals(c.Arrival.Attempts.OwnerFor(c.Arrival.SortedId(i)),f.Owner)||f.Owner.Revision!=f.Revision||a.Working!=f.Working||a.Reconstructing!=f.Reconstructing||a.Retiring!=f.Retiring||a.RestoredRetiringCompute!=f.Restored||a.Retirement!=f.Retirement)return false;}
            for(int i=0;i<c.Leases.Length;i++){var row=c.Leases[i];var lease=row.Owner.Record;if(!ReferenceEquals(c.LeaseTable.Tokens.OwnerFor(c.LeaseTable.AcquisitionToken(i)),row.Owner)||row.Owner.Revision!=row.Revision||!row.Values.Same(NavigationArrivalLeaseValues.Read(lease))||lease.Attached!=row.Attached||lease.StagingBytes!=row.Staging||!ReferenceEquals(lease.Provider,row.Provider)||!ReferenceEquals(lease.Projection,row.Projection)||!ReferenceEquals(row.Owner.RetirementPrevious,row.Previous)||!ReferenceEquals(row.Owner.RetirementNext,row.Next))return false;}
            for(int i=0;i<c.Installed.Length;i++)if(!ReferenceEquals(installedExecutions.OwnerFor(c.Frame.Source[i].Id),c.Installed[i])||c.Installed[i]!=null&&installedExecutions.IsDetached(c.Frame.Source[i].Id)!=c.Detached[i])return false;
            return true;
        }
        private bool MovementPrefixNoOp(PreparedMovement c)
        {
            for(int i=0;i<c.Attempts.Length;i++){
                var a=c.Attempts[i].Owner.Record;
                if(!a.Retiring&&!a.Working&&!a.Reconstructing&&!a.Published&&a.SnapshotStatus!=NavArrivalStatus.Pending&&a.SnapshotStatus!=NavArrivalStatus.BudgetExhausted)return false;
                bool present=false;foreach(var intent in a.Intents)if(ReadyContainsRequest(c.Ledger,c.Allocating,intent.Request)){present=true;break;}if(!present)return false;
            }
            for(int i=0;i<c.Goals.Capacity;i++)if(c.Goals.TryReadSlot(i,out int id,out _)&&!c.Allocating.ContainsKey(id)&&Crowd.TryGet(id,out _)&&c.Identities.TryGetValue(id,out var held)&&!CurrentHeldBinding(held,id))return false;
            return true;
        }
        private bool MovementCleanupNoOp(PreparedMovement c)
        {
            foreach(var actor in c.Frame.After)if(!actor.Moving&&!c.Pending.ContainsKey(actor.Id)&&!c.Allocating.ContainsKey(actor.Id)){
                if(c.Goals.ContainsKey(actor.Id)||c.Reservations.ContainsKey(actor.Id))return false;
                for(int i=0;i<c.Ledger.Capacity;i++)if(c.Ledger.TryReadSlot(i,out _,out var row)&&row.Entity==actor.Id)return false;
            }
            foreach(var group in c.GroupSources)for(int i=0;i<group.Version.Members.Count;i++){
                var member=((NavigationAuthorityMember)group.Version.Members[i]).Data;int at=c.Frame.Find(member.Entity);if(at<0)return false;var actor=c.Frame.After[at];
                if(actor.Incarnation!=member.Incarnation||actor.MobilityRevision!=member.MobilityRevision||member.Reservation!=(c.Reservations.ContainsKey(actor.Id)?actor.Id:0))return false;
                var phase=member.FormationReleased?GroupMemberPhase.Combat:actor.Held?GroupMemberPhase.Held:c.Pending.ContainsKey(actor.Id)||c.Allocating.ContainsKey(actor.Id)?GroupMemberPhase.PendingRoute:actor.Moving?GroupMemberPhase.March:actor.Outcome==NavigationOutcome.Arrived?GroupMemberPhase.Arrived:actor.Outcome==NavigationOutcome.Unreachable?GroupMemberPhase.Unreachable:actor.Outcome==NavigationOutcome.Blocked||actor.Outcome==NavigationOutcome.Rejected?GroupMemberPhase.Blocked:GroupMemberPhase.Stopped;
                if(member.Phase!=phase)return false;
            }
            return true;
        }
        private bool PrepareMovementMarch(PreparedMovement c,double dt,NavigationRetentionBudget.Scope scope)
        {
            if(!c.MarchConfigured)return true;
            foreach(var observation in c.GroupSources){
                using(EnterTransport()){if(!MovementSourceCurrent(c)||!MarchRecordMatches(c.Groups[observation.Version.Data.GroupId],observation.Version))return false;}
                var next=GroupMarch.PrepareOwned(c.Frame,observation.Version,c.Installed,c.Detached,c.Members,c.MarchStretch,c.MarchRevision,dt,scope.OwnedReservation());
                if(next!=null)c.Changes[c.ChangeCount++]=new RebindGroup{Source=observation,Next=next};
            }return true;
        }
        // Host computation reads pinned immutable inputs. Pure Host frontier changes
        // commute with this paid movement frame; authority maps, actor headers,
        // attachment and arrival/order observations must still match exactly.
        private bool? TryPublishOwnedMovement(double dt)
        {
            int actors,groupCount,attemptCount,leaseCount;long publication,operation;NavCrowd sourceCrowd;
            using(EnterTransport()){
                if(!physicalArrivalEnabled||admissionBatchDepth!=0||commandGroup!=null||Crowd.HasPreferredMotion||barrierAnswers.Count!=0||Answers.Count!=0||arrivalProjectionLeases.Count>0&&liveArrivalLeaseTable==null||probes.Count>0&&!(probes is NavigationCommandSlots<int,NavigationRequest>)||groupCommandBacking==null||!(groups is NavigationCommandSlots<long,GroupOrderState>)||!(memberGroups is NavigationCommandSlots<int,long>)||!(pending is NavigationCommandSlots<int,NavigationRequest>)||!(allocating is NavigationCommandSlots<int,NavigationRequest>)||!(arrivalLedger is NavigationCommandSlots<long,NavigationArrivalReservation>)||!(retainedGoals is NavigationCommandSlots<int,NavPoint>)||!(reservations is NavigationCommandSlots<int,NavPoint>)||!(movementIdentities is NavigationCommandSlots<int,string>)||liveArrivalTables==null)return null;
                actors=Crowd.Units.Count;groupCount=groups.Count;attemptCount=liveArrivalTables.Count;leaseCount=liveArrivalLeaseTable?.Count??0;publication=admissionPublicationIdentity;operation=authorityOperationIdentity;sourceCrowd=Crowd;
            }
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,publication,out var scope))return false;
            using(scope){PreparedMovement c=null;try{
                c=PreparedMovement.Create(actors,groupCount,attemptCount,leaseCount,scope.OwnedReservation());c.Frame=NavPreparedCrowdStep.Create(sourceCrowd,actors,scope.OwnedReservation());
                using(EnterTransport()){
                    if(operation!=authorityOperationIdentity||!ReferenceEquals(sourceCrowd,Crowd)||publication!=admissionPublicationIdentity||actors!=Crowd.Units.Count||groupCount!=groups.Count||attemptCount!=liveArrivalTables.Count||leaseCount!=(liveArrivalLeaseTable?.Count??0)||barrierAnswers.Count!=0||Answers.Count!=0)return false;
                    c.SuspendPending=SuspendPendingMovement;c.Operation=authorityOperationIdentity;c.Publication=publication;c.Sequence=requestSequence;c.Attachment=certifiedHostAttachmentIdentity;c.Worker=certifiedHost;c.RequestsRevision=Requests.Revision;c.RequestsCount=Requests.Count;c.AnswersRevision=Answers.Revision;c.InstalledCount=installedExecutions.Count;c.ProbeCount=probes.Count;c.MarchConfigured=marchConfigured;c.MarchStretch=marchStretch;c.MarchRevision=marchProfileRevision;c.Retirement=arrivalRetirementSequence;
                    c.Backing=NavPhysicalMacro.Acquire(c,groupCommandBacking);c.Host=NavPhysicalMacro.Acquire(c,committedHostRoot);c.SourceAdmission=NavPhysicalMacro.Acquire(c,admissionOwner);c.Arrival=NavPhysicalMacro.Acquire(c,liveArrivalTables);c.LeaseTable=NavPhysicalMacro.Acquire(c,liveArrivalLeaseTable);c.LeaseRevision=c.LeaseTable?.Revision??0;c.LeaseTokenRevision=c.LeaseTable?.Tokens.Revision??0;
                    c.Groups=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<long,GroupOrderState>)groups);c.Revisions[0]=c.Groups.Revision;c.Members=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,long>)memberGroups);c.Revisions[1]=c.Members.Revision;
                    c.Pending=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,NavigationRequest>)pending);c.Revisions[2]=c.Pending.Revision;c.Allocating=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,NavigationRequest>)allocating);c.Revisions[3]=c.Allocating.Revision;
                    c.Ledger=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<long,NavigationArrivalReservation>)arrivalLedger);c.Revisions[4]=c.Ledger.Revision;c.Goals=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,NavPoint>)retainedGoals);c.Revisions[5]=c.Goals.Revision;c.Reservations=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,NavPoint>)reservations);c.Revisions[6]=c.Reservations.Revision;c.Identities=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,string>)movementIdentities);c.Revisions[7]=c.Identities.Revision;
                    c.Probes=NavPhysicalMacro.Acquire(c,probes as NavigationCommandSlots<int,NavigationRequest>);c.Revisions[8]=c.Probes?.Revision??0;c.Frame.Observe();
                    for(int i=0;i<actors;i++){c.Installed[i]=NavPhysicalMacro.Acquire(c,installedExecutions.OwnerFor(c.Frame.Source[i].Id));c.Detached[i]=c.Installed[i]!=null&&installedExecutions.IsDetached(c.Frame.Source[i].Id);}
                    int at=0;for(int i=0;i<c.Groups.Capacity;i++)if(c.Groups.TryReadSlot(i,out long id,out _)){if(!(c.Groups.OwnerFor(id)is NavigationLiveGroup owner))return null;c.GroupSources[at++]=owner.ObserveForCopy(ObserveLiveWriteToken());}
                    for(int i=1;i<c.GroupSources.Length;i++){var value=c.GroupSources[i];int j=i;while(j>0&&c.GroupSources[j-1].Version.Data.GroupId>value.Version.Data.GroupId){c.GroupSources[j]=c.GroupSources[j-1];j--;}c.GroupSources[j]=value;}
                    for(int i=0;i<attemptCount;i++){var a=c.Arrival.Attempts[c.Arrival.SortedId(i)];if(a.StorageOwner==null)return null;c.Attempts[i]=new RebindAttempt{Owner=NavPhysicalMacro.Acquire(c,a.StorageOwner),Revision=a.StorageOwner.Revision,Working=a.Working,Reconstructing=a.Reconstructing,Retiring=a.Retiring,Restored=a.RestoredRetiringCompute,Retirement=a.Retirement};}
                    for(int i=0;i<leaseCount;i++){var lease=c.LeaseTable.Tokens[c.LeaseTable.AcquisitionToken(i)];if(lease.StorageOwner==null)return null;ref var row=ref c.Leases[i];row.Owner=NavPhysicalMacro.Acquire(c,lease.StorageOwner);row.Values=NavigationArrivalLeaseValues.Read(lease);row.Revision=row.Owner.Revision;row.Attached=lease.Attached;row.Staging=lease.StagingBytes;row.Provider=lease.Provider;row.Projection=lease.Projection;row.Previous=row.Owner.RetirementPrevious;row.Next=row.Owner.RetirementNext;}
                }
                using(NavStorageScratch.Reserve(scope.OwnedReservation(),RebindStagingScratchBound(actors,0,navigationGeometry.Obstacles.Count)))using(EnterTransport()){if(!MovementSourceCurrent(c))return false;if(!MovementPrefixNoOp(c))return null;}
                if(c.SuspendPending)foreach(var actor in c.Frame.After)if(c.Pending.ContainsKey(actor.Id)||c.Allocating.ContainsKey(actor.Id))actor.Moving=false;
                if(!PrepareMovementMarch(c,dt,scope))return false;using(NavStorageScratch.Reserve(scope.OwnedReservation(),RebindStagingScratchBound(actors,0,navigationGeometry.Obstacles.Count)))c.Frame.Compute(dt);
                using(EnterTransport()){if(!MovementSourceCurrent(c))return false;if(!MovementCleanupNoOp(c))return null;}
                c.Admission=PrepareAdmissionProjection(c.Groups,c.Pending,c.Sequence,checked(publication+1),c.Operation,scope,c.SourceAdmission,rebindGroups:c.Changes,rebindGroupCount:c.ChangeCount);
                for(int i=0;i<c.ChangeCount;i++)c.Changes[i].Next.Retain();c.Destinations=true;scope.Attach(c);
                Action<object> publish=_=>{
                    if(!MovementSourceCurrent(c)||scope.IsCancelled||c.Operation>long.MaxValue-3||OwnedMovementPublications==int.MaxValue)throw new InvalidOperationException("Prepared movement lost final source.");
                    var token=ObserveLiveWriteToken();for(int i=0;i<c.ChangeCount;i++)c.Changes[i].Source.Owner.ValidateMarchWrite(c.Changes[i].Source,token);
                    long before=GC.GetAllocatedBytesForCurrentThread();
                    for(int i=0;i<c.ChangeCount;i++){ref var change=ref c.Changes[i];change.Retired=change.Source.Version;change.Source.Owner.ApplyMarchNoThrow(change.Next);}
                    c.Frame.ApplyNoThrow();if(PublishAdmissionOwnerNoThrow(c.Admission)){c.Admission=null;c.AdmissionChanged=true;}authorityOperationIdentity=c.Operation+3;OwnedMovementPublications++;c.Destinations=false;c.Published=true;LastOwnedMovementPublicationAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-before;
                };
                using(EnterTransport())scope.Publish(c,publish);scope.ResolveCommitted();if(c.AdmissionChanged)SignalHost();return true;
            }catch(InvalidOperationException){if(c?.Published==true)throw;return false;}finally{c?.Dispose();}}
        }
    }
}
