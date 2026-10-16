using System;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        internal int OwnedProbeAnswerPublications{get;private set;}
        internal long LastOwnedProbeAnswerPublicationAllocatedBytes{get;private set;}=-1;
        private sealed class PreparedProbeAnswer:NavOwnedStorage,IDisposable
        {
            private static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(PreparedProbeAnswer))+48;
            internal readonly NavOwnedStorage[] MailboxRetired;
            internal NavigationPreparedAnswerNames Names;
            internal NavigationHostRoot Host;internal NavigationTransportOwner Transport;internal int TransportAnswers;internal bool TransportExternal;
            internal NavigationCommandSlots<int,NavigationRequest> SourceMap,Map,RetiredMap;
            internal NavigationLiveProbeAnswers SourceQueue,Queue,RetiredQueue;
            internal NavigationHostAnswer Payload;
            internal NavigationHostRequest RetiredTransport;internal NavOwnedStorage RetiredTracking;
            internal NavigationAdmissionStorage Admission;
            internal int MapRevision,QueueRevision;
            internal bool Accepted,Published;private bool released,mapDestination,queueDestination;
            private PreparedProbeAnswer():base(Layout){MailboxRetired=new NavOwnedStorage[2];Retain();}
            internal static PreparedProbeAnswer Create(INavStorageReservation r){using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout);return a.Register(new PreparedProbeAnswer());}}
            internal void SealDestinations(){if(Accepted){Map.Retain();mapDestination=true;Queue.Retain();queueDestination=true;}}
            internal void TransferNoThrow(){mapDestination=queueDestination=false;Published=true;}
            public void Dispose(){if(released)return;released=true;Release();}
            internal override void ReleaseOwner()=>Dispose();
            protected override void ReleaseEdges(){foreach(var value in MailboxRetired)value?.Release();Names?.Release();Host?.Release();Transport?.Release();SourceMap?.Release();SourceQueue?.Release();if(mapDestination)Map?.Release();if(queueDestination)Queue?.Release();Map?.ReleaseStorage();Queue?.ReleaseStorage();Payload?.Release();Admission?.Release();RetiredMap?.Release();RetiredQueue?.Release();RetiredTransport?.Release();RetiredTracking?.Release();}
        }
        private bool ProbeAnswerSourceCurrent(PreparedProbeAnswer c)
            =>AnswerNamesSourceCurrent(c.Names)&&hostAllocationThread==0&&commandGroup==null&&admissionBatchDepth==0&&ReferenceEquals(committedHostRoot,c.Host)&&ReferenceEquals(ownedProbes,c.SourceMap)&&ReferenceEquals(probes,c.SourceMap)&&c.SourceMap.Revision==c.MapRevision&&ReferenceEquals(liveProbeAnswers,c.SourceQueue)&&c.SourceQueue.Revision==c.QueueRevision&&ReferenceEquals(transportOwner,c.Transport)&&c.Transport.CanConsumeCountedPrepared(c.Names.Answers[0].Request,c.TransportAnswers,c.TransportExternal);
        private bool? TryPublishOwnedProbeAnswer(NavigationPreparedAnswerNames names,double stepDt=0,NavMotionWorkspace motionWorkspace=null)
        {
            if(names==null||names.Answers.Length!=1||names.Answers[0].Request.Entity>=0)return null;
            var answer=names.Answers[0];var request=answer.Request;long publication;
            using(EnterTransport()){
                if(ownedProbes==null||liveProbeAnswers==null||request.StorageOwner==null||hostAllocationThread!=0||commandGroup!=null||admissionBatchDepth!=0)return null;
                if(!AnswerNamesSourceCurrent(names))return false;publication=admissionPublicationIdentity;
            }
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,publication,out var scope))return false;
            using(scope){PreparedProbeAnswer c=null;try{
                var r=scope.OwnedReservation();c=PreparedProbeAnswer.Create(r);c.Names=NavPhysicalMacro.Acquire(c,names);
                using(EnterTransport()){
                    c.Transport=NavPhysicalMacro.Acquire(c,transportOwner);c.TransportAnswers=request.StorageOwner.TransportAnswers;c.TransportExternal=request.StorageOwner.TransportExternal;c.Host=NavPhysicalMacro.Acquire(c,committedHostRoot);c.SourceMap=NavPhysicalMacro.Acquire(c,ownedProbes);c.MapRevision=c.SourceMap.Revision;c.SourceQueue=NavPhysicalMacro.Acquire(c,liveProbeAnswers);c.QueueRevision=c.SourceQueue.Revision;
                    if(!ProbeAnswerSourceCurrent(c))return false;
                    c.Accepted=request.Session==Generation&&(ReferenceEquals(request.Geometry,navigationGeometry)||IsCurrentPhysicalAuthorityGeometry(request))&&c.SourceMap.TryGetValue(request.Entity,out var expected)&&ReferenceEquals(expected,request)&&
                        (answer.Status!=NavSolveStatus.Ready||(names.HasQualification[0]?names.Qualified[0]:answer.Corridor==null&&request.CertifiedProviderBinding==null));
                }
                if(c.Accepted){
                    c.Payload=answer.StorageOwner==null?(answer.Status==NavSolveStatus.Ready?NavigationHostAnswer.CreateLegacyProbeReady(request.StorageOwner,answer.ReadOnlyRouteBacking,r):NavigationHostAnswer.CreateTerminal(request.StorageOwner,answer.Status,r)):NavPhysicalMacro.Acquire(c,answer.StorageOwner);
                    c.Map=NavigationCommandSlots<int,NavigationRequest>.CreateOwned(c.SourceMap.Capacity,r);
                    c.Queue=NavigationLiveProbeAnswers.CreateOwned(NavigationLiveProbeAnswers.GrowthCapacity(c.SourceQueue.Capacity,c.SourceQueue.Count),r);
                    using(EnterTransport()){if(!ProbeAnswerSourceCurrent(c))return false;c.Map.CopyPreparedFrom(c.SourceMap);c.Queue.CopyObserved(c.SourceQueue);}
                    c.Map.Remove(request.Entity);c.Map.Seal();c.Queue.AppendPreparing(c.Payload,r);c.Queue.SealForUse(r);
                    c.Admission=PrepareAdmissionProjection(groups,pending,requestSequence,checked(publication+1),names.Operation,scope,names.ObservedAdmission,sourceProbes:c.Map);
                }
                c.SealDestinations();scope.Attach(c);
                Action<object> publish=_=>{long before=GC.GetAllocatedBytesForCurrentThread();using(var mailbox=Answers.EnterPreparedWrite()){
                    if(!ProbeAnswerSourceCurrent(c)||scope.IsCancelled||authorityOperationIdentity==long.MaxValue||OwnedProbeAnswerPublications==int.MaxValue||!c.Accepted&&RejectedResults==int.MaxValue)throw new InvalidOperationException("Prepared producer receipt lost its source.");
                    if(names.BarrierCount==0)mailbox.ValidateTakeOne(names.MailboxRevision,names.SourceAnswerCount,c.MailboxRetired);
                    if(c.Accepted){c.RetiredMap=ownedProbes;ownedProbes=c.Map;probes=c.Map;c.RetiredQueue=liveProbeAnswers;liveProbeAnswers=c.Queue;if(PublishAdmissionOwnerNoThrow(c.Admission))c.Admission=null;}
                    else{c.RetiredTracking=ExtractAnswerChargeNoThrow(answer);c.RetiredTransport=c.Transport.ConsumeCountedPreparedNoThrow(request);RejectedResults++;}
                    if(names.BarrierCount==0)mailbox.TakeOneNoThrow(c.MailboxRetired);else c.MailboxRetired[0]=TakeBarrierAnswerNoThrow(names.BarrierIndex);
                    authorityOperationIdentity++;OwnedProbeAnswerPublications++;c.TransferNoThrow();LastOwnedProbeAnswerPublicationAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-before;
                }};
                try{using(EnterTransport()){if(stepDt>0)ValidateCompatibilityMotion(motionWorkspace);scope.Publish(c,publish);if(stepDt>0)AdmissionBatch(()=>StepMovementLocked(stepDt,motionWorkspace));}}
                finally{if(c.Published){scope.ResolveCommitted();SignalHost();}}return true;
            }catch(InvalidOperationException){if(c?.Published==true)throw;return false;}finally{c?.Dispose();}}
        }
    }
}
