using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        internal long LastOwnedStaleIngressAllocatedBytes{get;private set;}=-1;
        private sealed class PreparedStaleIngress:NavOwnedStorage,IDisposable
        {
            private static readonly long Layout=NavStorageLayout.ObjectBytesBound(typeof(PreparedStaleIngress))+48;
            internal readonly NavOwnedStorage[] Retired;
            internal NavigationHostRoot Source,Next,RetiredHost;
            internal NavigationAdmissionStorage Admission;
            internal NavigationHostRequest Request;internal NavigationHostAnswer Answer;internal NavigationTransportOwner Transport;internal int SourceTransportAnswers;internal bool SourceTransportExternal;
            internal NavMailboxRetention Retention;internal NavigationAnswer[] AnswerItems;
            internal Action<NavigationAnswer> ValidateAnswer,AcceptAnswer;internal CertifiedRouteKernel Worker;internal long Attachment;internal int AllocationThread;
            internal long Operation,RequestRevision,AnswerRevision;internal int RequestCount,AnswerCount;
            internal bool NewTransport,Published;private bool released,hostDestination,answerDestination,transportDestination;
            private PreparedStaleIngress():base(Layout){Retired=new NavOwnedStorage[2];Retain();}
            internal static PreparedStaleIngress Create(INavStorageReservation r){using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout);return a.Register(new PreparedStaleIngress());}}
            internal void Seal(){Next.Retain();hostDestination=true;Answer.Retain();answerDestination=true;if(NewTransport){Request.Retain();transportDestination=true;}}
            internal void Transfer(){hostDestination=answerDestination=transportDestination=false;Published=true;}
            public void Dispose(){if(released)return;released=true;Release();}
            internal override void ReleaseOwner()=>Dispose();
            protected override void ReleaseEdges(){foreach(var edge in Retired)edge?.Release();Source?.Release();Next?.Release();RetiredHost?.Release();Admission?.Release();Transport?.Release();Request?.Release();Answer?.ReleaseStorage();Retention?.Release();if(hostDestination)Next?.Release();if(answerDestination)Answer?.Release();if(transportDestination)Request?.Release();}
        }
        internal bool TryObservePhysicalStale(out NavigationRequest request)
        {request=null;using(EnterTransport()){return commandGroup==null&&admissionBatchDepth==0&&Requests.TryPeek(out request)&&request.StorageOwner!=null&&request.Request<=Admission.PublishedThroughRequestSequence&&!Admission.Contains(request);}}
        private bool StaleIngressCurrent(PreparedStaleIngress c)
            =>ReferenceEquals(committedHostRoot,c.Source)&&ReferenceEquals(admissionOwner,c.Admission)&&authorityOperationIdentity==c.Operation&&commandGroup==null&&admissionBatchDepth==0&&c.Request.Request.Request<=Admission.PublishedThroughRequestSequence&&!Admission.Contains(c.Request.Request)&&ReferenceEquals(certifiedHost,c.Worker)&&certifiedHostAttachmentIdentity==c.Attachment&&hostAllocationThread==c.AllocationThread&&ReferenceEquals(transportOwner,c.Transport)&&c.Request.TransportAnswers==c.SourceTransportAnswers&&c.Request.TransportExternal==c.SourceTransportExternal&&c.Transport.CanAppendAnswerPrepared(c.Request,c.NewTransport);
        internal bool PublishOwnedStaleIngress(NavigationRetentionBudget.Scope scope,NavigationHostRoot source,NavigationHostRoot next,NavigationRequest request)
        {
            PreparedStaleIngress c=null;try{
                var r=scope.OwnedReservation();c=PreparedStaleIngress.Create(r);c.Source=NavPhysicalMacro.Acquire(c,source);c.Next=NavPhysicalMacro.Acquire(c,next);c.Request=NavPhysicalMacro.Acquire(c,request.StorageOwner);
                using(EnterTransport()){
                    c.Admission=NavPhysicalMacro.Acquire(c,admissionOwner);c.Operation=authorityOperationIdentity;c.Worker=certifiedHost;c.Attachment=certifiedHostAttachmentIdentity;c.AllocationThread=hostAllocationThread;c.Transport=NavPhysicalMacro.Acquire(c,transportOwner);c.NewTransport=c.Request.TransportOwner==null;c.SourceTransportAnswers=c.Request.TransportAnswers;c.SourceTransportExternal=c.Request.TransportExternal;Requests.ObserveState(out c.RequestRevision,out c.RequestCount);Answers.ObserveState(out c.AnswerRevision,out c.AnswerCount);Answers.ObserveRetainedBacking(out c.AnswerItems,out var retention,out c.ValidateAnswer,out c.AcceptAnswer);c.Retention=NavPhysicalMacro.Acquire(c,retention);if(!StaleIngressCurrent(c)||c.AnswerCount==Answers.InstanceCapacity)return false;
                }
                if(c.SourceTransportAnswers==int.MaxValue)return false;
                c.Answer=NavigationHostAnswer.CreateTerminal(c.Request,NavSolveStatus.Stale,r);c.Seal();scope.Attach(c);
                Action<object> publish=_=>{long before=GC.GetAllocatedBytesForCurrentThread();using(var requests=Requests.EnterPreparedWrite())using(var answers=Answers.EnterPreparedWrite()){
                    if(!StaleIngressCurrent(c)||scope.IsCancelled||c.Operation==long.MaxValue)throw new InvalidOperationException("Prepared stale ingress changed.");
                    requests.ValidateTransferOne(c.RequestRevision,c.RequestCount,request,requestTransferHandler,c.Retired);answers.ValidateRetainedAppend(c.AnswerRevision,c.AnswerCount,c.AnswerItems,c.Retention,c.ValidateAnswer,c.AcceptAnswer);
                    c.RetiredHost=committedHostRoot;committedHostRoot=c.Next;c.Transport.AppendAnswerPreparedNoThrow(c.Request,c.NewTransport);answers.AppendRetainedNoThrow(c.Answer.Answer,c.Answer);requests.TakeOneNoThrow(c.Retired);authorityOperationIdentity++;c.Transfer();LastOwnedStaleIngressAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-before;
                }};
                using(EnterTransport())scope.Publish(c,publish);scope.ResolveCommitted();return true;
            }catch(InvalidOperationException){if(c?.Published==true)throw;return false;}finally{c?.Dispose();}
        }
    }
}
