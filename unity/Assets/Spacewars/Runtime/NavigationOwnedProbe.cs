using System;
using System.Collections.Generic;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        private NavigationCommandSlots<int,NavigationRequest> ownedProbes;
        private NavigationLiveProbeAnswers liveProbeAnswers;
        private int ProbeAnswerCount=>liveProbeAnswers?.Count??probeAnswers.Count;
        private IEnumerable<NavigationAnswer> ProbeAnswerRows=>liveProbeAnswers!=null?(IEnumerable<NavigationAnswer>)liveProbeAnswers:probeAnswers;
        private NavigationLiveProbeAnswers retiredProbeQueues;
        private NavigationProbeReceipt retiredProbeReceipts;
        private void ClearProbeAnswersNoThrow()
        {
            if(liveProbeAnswers==null){probeAnswers.Clear();ClearLegacyProbePayloads();return;}if(liveProbeAnswers.Count==0)return;
            var old=liveProbeAnswers;liveProbeAnswers=old.TakeResetNoThrow();old.RetiredNext=retiredProbeQueues;retiredProbeQueues=old;
        }
        private static void ReleaseRetiredProbeQueues(NavigationLiveProbeAnswers root)
        {while(root!=null){var next=root.RetiredNext;root.RetiredNext=null;root.Release();root=next;}}
        private static void ReleaseRetiredProbeReceipts(NavigationProbeReceipt receipt)
        {while(receipt!=null){var next=receipt.RetiredNext;receipt.RetiredNext=null;var transport=receipt.RetiredTransport;receipt.RetiredTransport=null;var tracking=receipt.RetiredTracking;receipt.RetiredTracking=null;var reset=receipt.RetiredReset;receipt.RetiredReset=null;transport?.Release();tracking?.Release();reset?.Release();receipt.Release();receipt=next;}}
        internal long LastOwnedProbeTakeAllocatedBytes{get;private set;}=-1;
        private bool TryTakeOwnedProbeAnswer(out NavigationAnswer answer)
        {
            using(EnterTransport()){
                long before=GC.GetAllocatedBytesForCurrentThread();answer=null;if(liveProbeAnswers.Count==0)return false;var receipt=liveProbeAnswers.ReceiptAt(0);var payload=receipt.Answer;
                if(ownedAnswerTracking?.Revision==int.MaxValue||authorityOperationIdentity==long.MaxValue||!liveProbeAnswers.CanTakeFirst(receipt,liveProbeAnswers.Revision)||!transportOwner.CanConsumePrepared(payload.Answer.Request))throw new InvalidOperationException("Producer receipt lost its consumer owner.");
                receipt=liveProbeAnswers.TakeFirstNoThrow();answer=payload.Answer;receipt.RetiredTracking=ExtractAnswerChargeNoThrow(answer);receipt.RetiredTransport=transportOwner.ConsumePreparedNoThrow(answer.Request);receipt.RetiredNext=retiredProbeReceipts;retiredProbeReceipts=receipt;authorityOperationIdentity++;LastOwnedProbeTakeAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-before;return true;
            }
        }
        private int[] CaptureCommandCapacities()
        {
            var caps=new int[ownedProbes!=null&&liveProbeAnswers!=null?12:10];caps[0]=NavigationCommandLayout.Capacity(groups);caps[1]=NavigationCommandLayout.Capacity(memberGroups);caps[2]=NavigationCommandLayout.Capacity(orders);caps[3]=NavigationCommandLayout.Capacity(pending);caps[4]=NavigationCommandLayout.Capacity(allocating);caps[5]=NavigationCommandLayout.Capacity(reservations);caps[6]=NavigationCommandLayout.Capacity(retainedGoals);caps[7]=NavigationCommandLayout.Capacity(movementIdentities);caps[8]=NavigationCommandLayout.Capacity(technicalFailures);caps[9]=NavigationCommandLayout.Capacity(arrivalLedger);if(caps.Length==12){caps[10]=ownedProbes.Capacity;caps[11]=liveProbeAnswers.Capacity;}return caps;
        }
        internal int OwnedProbePublications{get;private set;}
        internal long LastOwnedProbePublicationAllocatedBytes{get;private set;}=-1;
        private sealed class PreparedProbe : NavOwnedStorage,IDisposable
        {
            private static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(PreparedProbe));
            internal NavigationHostRoot Host;internal NavigationAuthorityOwners Core;
            internal NavigationCommandAuthority Backing;
            internal NavigationGroupCommand Bootstrap;
            internal NavigationAdmissionStorage SourceAdmission,Admission;
            internal IDictionary<int,NavigationRequest> SourceProbes;
            internal NavigationCommandSlots<int,NavigationRequest> SourceOwned,Probes,RetiredProbes;
            internal NavigationLiveProbeAnswers SourceAnswers,NewAnswers;
            internal NavigationHostRequest Request;
            internal long Operation,Sequence,Publication,MailboxRevision,Attachment;
            internal int MapRevision,MailboxCount,AnswerRevision,AllocationThread;
            internal CertifiedRouteKernel Worker;
            internal bool Cancel,Published;private bool released,mapDestination,answerDestination,mailboxDestination;
            private PreparedProbe():base(Layout){Retain();}
            internal static PreparedProbe Create(INavStorageReservation r){using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout);return a.Register(new PreparedProbe());}}
            internal void SealDestinations(){Probes.Retain();mapDestination=true;if(NewAnswers!=null){NewAnswers.Retain();answerDestination=true;}if(Request!=null){Request.Retain();mailboxDestination=true;}}
            internal void TransferNoThrow(){mapDestination=answerDestination=mailboxDestination=false;Published=true;}
            public void Dispose(){if(released)return;released=true;Release();}
            internal override void ReleaseOwner()=>Dispose();
            protected override void ReleaseEdges(){Host?.Release();Core?.Release();Backing?.Release();SourceAdmission?.Release();SourceOwned?.Release();SourceAnswers?.Release();if(mapDestination)Probes?.Release();if(answerDestination)NewAnswers?.Release();if(mailboxDestination)Request?.Release();Probes?.ReleaseStorage();NewAnswers?.ReleaseStorage();Request?.ReleaseStorage();Admission?.Release();RetiredProbes?.Release();Bootstrap?.ReleaseStorage();}
        }
        private bool EmptyCommandStateForProbe()
            =>groups.Count==0&&memberGroups.Count==0&&orders.Count==0&&pending.Count==0&&allocating.Count==0&&reservations.Count==0&&retainedGoals.Count==0&&movementIdentities.Count==0&&technicalFailures.Count==0&&arrivalLedger.Count==0;
        private bool ProbeSourceCurrent(PreparedProbe c)
        {
            if(!ReferenceEquals(committedHostRoot,c.Host)||!ReferenceEquals(authorityOwners,c.Core)||!ReferenceEquals(groupCommandBacking,c.Backing)||!ReferenceEquals(admissionOwner,c.SourceAdmission)||authorityOperationIdentity!=c.Operation||requestSequence!=c.Sequence||admissionPublicationIdentity!=c.Publication||commandGroup!=null||admissionBatchDepth!=0||hostAllocationThread!=c.AllocationThread||!ReferenceEquals(certifiedHost,c.Worker)||certifiedHostAttachmentIdentity!=c.Attachment||!ReferenceEquals(probes,c.SourceProbes)||!ReferenceEquals(ownedProbes,c.SourceOwned)||c.SourceOwned!=null&&c.SourceOwned.Revision!=c.MapRevision||c.SourceOwned==null&&probes.Count!=0||!ReferenceEquals(liveProbeAnswers,c.SourceAnswers)||c.SourceAnswers!=null&&c.SourceAnswers.Revision!=c.AnswerRevision||c.SourceAnswers==null&&probeAnswers.Count!=0||Requests.Revision!=c.MailboxRevision||Requests.Count!=c.MailboxCount)return false;
            if(c.Backing==null){if(!EmptyCommandStateForProbe()||c.MailboxCount!=0)return false;if(c.Bootstrap!=null&&!ProbeBootstrapNamesCurrent(c.Bootstrap.Committed.CrowdNames))return false;}
            return true;
        }
        private bool ProbeBootstrapNamesCurrent(NavigationCrowdNameBacking names)
        {
            if(names==null||names.Keys.Length!=Crowd.Units.Count)return false;
            for(int i=0;i<Crowd.Units.Count;i++){var actor=Crowd.Units[i];int index=names.Find(actor.Id);if(index<0||!NavigationAdmissionMemberStorage.SameName(names.Names[index*3],OwnedOrFlatName(actor.Location))||!NavigationAdmissionMemberStorage.SameName(names.Names[index*3+1],OwnedOrFlatName(actor.GoalLocation))||!NavigationAdmissionMemberStorage.SameName(names.Names[index*3+2],OwnedOrFlatName(actor.FailedGoal)))return false;}
            return true;
        }
        private NavigationGroupCommand PrepareEmptyProbeCommandBacking(NavigationRetentionBudget.Scope scope)
        {
            var r=scope.OwnedReservation();var c=NavigationGroupCommand.CreateOwned(Requests.InstanceCapacity,0,r,0);
            try{
                c.Groups=NavigationCommandSlots<long,GroupOrderState>.CreateOwned(4,r);c.MemberGroups=NavigationCommandSlots<int,long>.CreateOwned(4,r);c.Orders=NavigationCommandSlots<int,long>.CreateOwned(4,r);
                c.Pending=NavigationCommandSlots<int,NavigationRequest>.CreateOwned(4,r);c.Allocating=NavigationCommandSlots<int,NavigationRequest>.CreateOwned(4,r);c.Reservations=NavigationCommandSlots<int,NavPoint>.CreateOwned(4,r);c.Goals=NavigationCommandSlots<int,NavPoint>.CreateOwned(4,r);c.Identities=NavigationCommandSlots<int,string>.CreateOwned(4,r);c.Failures=NavigationCommandSlots<int,NavSolveStatus>.CreateOwned(4,r);c.Ledger=NavigationCommandSlots<long,NavigationArrivalReservation>.CreateOwned(4,r);
                c.OwnMaps();c.Groups.Seal();c.MemberGroups.Seal();c.Orders.Seal();c.Pending.Seal();c.Allocating.Seal();c.Reservations.Seal();c.Goals.Seal();c.Identities.Seal();c.Failures.Seal();c.Ledger.Seal();c.Committed.CrowdNames=CaptureCrowdNameReferences(r);return c;
            }catch{c.Groups?.ReleaseStorage();c.MemberGroups?.ReleaseStorage();c.Orders?.ReleaseStorage();c.Pending?.ReleaseStorage();c.Allocating?.ReleaseStorage();c.Reservations?.ReleaseStorage();c.Goals?.ReleaseStorage();c.Identities?.ReleaseStorage();c.Failures?.ReleaseStorage();c.Ledger?.ReleaseStorage();c.ReleaseStorage();throw;}
            finally{c.Groups?.ReleaseStorage();c.MemberGroups?.ReleaseStorage();c.Orders?.ReleaseStorage();c.Pending?.ReleaseStorage();c.Allocating?.ReleaseStorage();c.Reservations?.ReleaseStorage();c.Goals?.ReleaseStorage();c.Identities?.ReleaseStorage();c.Failures?.ReleaseStorage();c.Ledger?.ReleaseStorage();}
        }
        private void InstallProbeCommandBackingNoThrow(NavigationGroupCommand c)
        {
            groupCommandBacking=c.Committed;groups=c.Groups;memberGroups=c.MemberGroups;orders=c.Orders;pending=c.Pending;allocating=c.Allocating;reservations=c.Reservations;retainedGoals=c.Goals;movementIdentities=c.Identities;technicalFailures=c.Failures;arrivalLedger=c.Ledger;c.Published=true;
        }
        private bool? TryOwnedProbe(int producer,long order,NavigationProfile typed,NavPoint start,NavPoint goal,bool cancel,out NavigationRequest result)
        {
            result=null;if(producer<=0)return null;if(System.Threading.Monitor.IsEntered(transportGate))return ownedProbes!=null?(bool?)false:null;
            long publication;using(EnterTransport()){
                if(commandGroup!=null||admissionBatchDepth!=0)return ownedProbes!=null?(bool?)false:null;
                if(ownedProbes==null&&(probes.Count!=0||NavigationCommandLayout.Length(probes)!=1||probeAnswers.Count!=0))return null;
                if(groupCommandBacking==null&&(!EmptyCommandStateForProbe()||Requests.Count!=0||!Requests.HasInitialPreparedBacking))return null;
                if(cancel&&ownedProbes!=null&&!probes.ContainsKey(-producer)){authorityOperationIdentity=checked(authorityOperationIdentity+1);return true;}
                if(!cancel&&Requests.Count+RegisteredRouteCount>=RouteRequestCapacity)return false;
                publication=admissionPublicationIdentity;
            }
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,publication,out var scope))return false;
            using(scope){PreparedProbe c=null;try{
                var r=scope.OwnedReservation();c=PreparedProbe.Create(r);c.Cancel=cancel;
                using(EnterTransport()){
                    c.Host=NavPhysicalMacro.Acquire(c,committedHostRoot);c.Core=NavPhysicalMacro.Acquire(c,authorityOwners);c.Backing=NavPhysicalMacro.Acquire(c,groupCommandBacking);c.SourceAdmission=NavPhysicalMacro.Acquire(c,admissionOwner);c.SourceProbes=probes;c.SourceOwned=NavPhysicalMacro.Acquire(c,ownedProbes);c.MapRevision=c.SourceOwned?.Revision??0;c.SourceAnswers=NavPhysicalMacro.Acquire(c,liveProbeAnswers);c.AnswerRevision=c.SourceAnswers?.Revision??0;c.Operation=authorityOperationIdentity;c.Sequence=requestSequence;c.Publication=admissionPublicationIdentity;c.Worker=certifiedHost;c.Attachment=certifiedHostAttachmentIdentity;c.AllocationThread=hostAllocationThread;Requests.ObserveState(out c.MailboxRevision,out c.MailboxCount);
                    if(!ProbeSourceCurrent(c))throw new InvalidOperationException("Producer probe lost observation.");
                }
                if(c.Backing==null)c.Bootstrap=PrepareEmptyProbeCommandBacking(scope);
                if(c.SourceAnswers==null)c.NewAnswers=NavigationLiveProbeAnswers.CreateOwned(0,r);
                int capacity=c.SourceOwned?.Capacity??0;
                if(!cancel&&!c.SourceProbes.ContainsKey(-producer)&&c.SourceProbes.Count==capacity)capacity=capacity==0?4:checked(capacity*2);
                c.Probes=capacity==0?NavigationCommandSlots<int,NavigationRequest>.CreateEmptyOwned(r):NavigationCommandSlots<int,NavigationRequest>.CreateOwned(capacity,r);
                using(EnterTransport()){if(!ProbeSourceCurrent(c))throw new InvalidOperationException("Producer probe map changed.");if(c.SourceOwned!=null)c.Probes.CopyPreparedFrom(c.SourceOwned);}
                long sequence=c.Sequence;
                if(cancel)c.Probes.Remove(-producer);
                else{c.Request=NavigationHostRequest.CreateOwned(Generation,checked(++sequence),-producer,order,typed,(NavGeometry)c.Core.NavigationGeometry.Facade,start,goal,(NavGeometry)c.Core.NavigationGeometry.Facade,null,null,null,null,null,r);c.Probes.SetPreparing(-producer,c.Request.Request,c.Request);}
                c.Probes.Seal();var sourceGroups=c.Bootstrap==null?groups:c.Bootstrap.Groups;var sourcePending=c.Bootstrap==null?pending:c.Bootstrap.Pending;
                c.Admission=PrepareAdmissionProjection(sourceGroups,sourcePending,sequence,checked(c.Publication+1),c.Operation,scope,c.SourceAdmission,sourceProbes:c.Probes);
                c.SealDestinations();scope.Attach(c);
                Action<object> publish=_=>{long before=GC.GetAllocatedBytesForCurrentThread();using(var mailbox=Requests.EnterPreparedWrite()){
                    if(!ProbeSourceCurrent(c)||scope.IsCancelled||c.Operation==long.MaxValue||OwnedProbePublications==int.MaxValue)throw new InvalidOperationException("Prepared producer probe lost final source.");
                    if(!cancel){transportOwner.Validate(c.Request.Request);if(c.Bootstrap!=null)mailbox.ValidateInitialAppend(c.MailboxRevision,requestEnqueueValidator,requestTransferHandler);else mailbox.ValidateAppendOne(c.MailboxRevision,c.MailboxCount,c.Backing.Mailbox,c.Backing.MailboxOwners,requestEnqueueValidator,requestTransferHandler);}
                    if(c.Bootstrap!=null){InstallProbeCommandBackingNoThrow(c.Bootstrap);mailbox.InstallNoThrow(c.Bootstrap.Mailbox,0,c.Bootstrap.Committed.MailboxOwners);}
                    c.RetiredProbes=ownedProbes;ownedProbes=c.Probes;probes=c.Probes;if(c.NewAnswers!=null)liveProbeAnswers=c.NewAnswers;
                    if(!cancel){mailbox.AppendOneNoThrow(c.Request.Request,c.Request);requestSequence=sequence;}
                    if(PublishAdmissionOwnerNoThrow(c.Admission))c.Admission=null;authorityOperationIdentity=c.Operation+1;OwnedProbePublications++;c.TransferNoThrow();LastOwnedProbePublicationAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-before;
                }};
                using(EnterTransport())scope.Publish(c,publish);scope.ResolveCommitted();result=c.Request?.Request;SignalHost();return true;
            }catch(InvalidOperationException){if(c?.Published==true)throw;return false;}finally{c?.Dispose();}}
        }
    }
}
