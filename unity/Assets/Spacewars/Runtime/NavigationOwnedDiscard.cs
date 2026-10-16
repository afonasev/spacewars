using System;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        internal int OwnedDiscardPublications{get;private set;}
        internal long LastOwnedDiscardPublicationAllocatedBytes{get;private set;}=-1;
        // Definite invalidity only: a stopped/replaced request is no longer
        // the exact pending owner. No held-binding allocation or qualification
        // of the obsolete route is needed to prove this witness.
        private sealed class PreparedDiscard:NavOwnedStorage,IDisposable
        {
            private static long Layout(int actors)=>checked(NavStorageLayout.ObjectBytesBound(typeof(PreparedDiscard))+96L+((actors*NavStorageLayout.ValueBytes(typeof(ReadyActorFact))+7)/8*8)+8L*actors);
            internal readonly ReadyActorFact[] Actors;internal readonly NavOwnedStorage[] ActorPins,MailboxRetired;
            internal NavigationPreparedAnswerNames Names;internal NavigationHostRoot Host;internal NavigationTransportOwner Transport;internal int TransportAnswers;internal bool TransportExternal;
            internal NavigationCommandSlots<int,NavigationRequest> Pending;
            internal NavigationCommandSlots<int,NavPoint> Goals;
            internal NavigationCommandSlots<int,string> Identities;
            internal NavigationAdmissionStorage Admission;internal NavigationHostRequest RetiredTransport;internal NavOwnedStorage RetiredTracking;
            internal int PendingRevision,GoalRevision,IdentityRevision;
            internal bool Tracked,Published;internal long AnswerBytes,PostAnswerBytes;
            private bool released;
            private PreparedDiscard(int actors):base(Layout(actors)){Actors=new ReadyActorFact[actors];ActorPins=new NavOwnedStorage[actors];MailboxRetired=new NavOwnedStorage[2];Retain();}
            internal static PreparedDiscard Create(int actors,INavStorageReservation r){using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout(actors));return a.Register(new PreparedDiscard(actors));}}
            public void Dispose(){if(released)return;released=true;Release();}
            internal override void ReleaseOwner()=>Dispose();
            protected override void ReleaseEdges(){foreach(var pin in ActorPins)pin?.Release();foreach(var edge in MailboxRetired)edge?.Release();Names?.Release();Host?.Release();Transport?.Release();Pending?.Release();Goals?.Release();Identities?.Release();Admission?.Release();RetiredTransport?.Release();RetiredTracking?.Release();}
        }
        private bool DiscardPrefixIsNoOp()
        {
            // Physical arrival publication was prepared by the caller. The
            // compatibility prefix cannot publish or create arrivals here.
            if(!physicalArrivalEnabled)return false;
            if(retainedGoals.Count==0)return true;
            // Narrow sufficient witness for RefreshHeldMovement being a no-op.
            // More general held bindings remain an explicit compatibility lane.
            for(int i=0;i<Crowd.Units.Count;i++)if(Crowd.Units[i].Held)return false;
            if(!(retainedGoals is NavigationCommandSlots<int,NavPoint> goals)||!(movementIdentities is NavigationCommandSlots<int,string> identities))return false;
            for(int i=0;i<goals.Capacity;i++)if(goals.TryReadSlot(i,out int entity,out _)&&(!Crowd.TryGet(entity,out _)||!identities.TryGetValue(entity,out var identity)||identity!=""))return false;
            return true;
        }
        private bool DiscardSourceCurrent(PreparedDiscard c)
        {
            var answer=c.Names.Answers[0];var request=answer.Request;
            if(!AnswerNamesSourceCurrent(c.Names)||hostAllocationThread!=0||commandGroup!=null||admissionBatchDepth!=0||!ReferenceEquals(committedHostRoot,c.Host)||!ReferenceEquals(pending,c.Pending)||c.Pending.Revision!=c.PendingRevision||c.Pending.TryGetValue(request.Entity,out var expected)&&ReferenceEquals(expected,request)||!ReferenceEquals(retainedGoals,c.Goals)||c.Goals.Revision!=c.GoalRevision||!ReferenceEquals(movementIdentities,c.Identities)||c.Identities.Revision!=c.IdentityRevision||answerCorridorBytes!=c.AnswerBytes||IsCorridorAnswerTracked(answer)!=c.Tracked||!ReferenceEquals(transportOwner,c.Transport)||!c.Transport.CanConsumeCountedPrepared(request,c.TransportAnswers,c.TransportExternal)||!DiscardPrefixIsNoOp())return false;
            if(c.Actors.Length!=0){if(Crowd.Units.Count!=c.Actors.Length)return false;for(int i=0;i<c.Actors.Length;i++)if(!Crowd.TryGet(c.Actors[i].Id,out var actor)||!ReferenceEquals(actor.StorageOwner,c.ActorPins[i])||!c.Actors[i].Matches(actor))return false;}
            return true;
        }
        private bool? TryPublishOwnedDiscard(NavigationPreparedAnswerNames names,double stepDt=0,NavMotionWorkspace motionWorkspace=null)
        {
            if(names==null||names.Answers.Length!=1||names.Answers[0].Request.Entity<0||names.Answers[0].Request.StorageOwner==null)return null;
            var answer=names.Answers[0];var request=answer.Request;int actors;long publication;
            using(EnterTransport()){
                if(!AnswerNamesSourceCurrent(names))return false;
                if(hostAllocationThread!=0||commandGroup!=null||admissionBatchDepth!=0||!(pending is NavigationCommandSlots<int,NavigationRequest>)||!(retainedGoals is NavigationCommandSlots<int,NavPoint>)||!(movementIdentities is NavigationCommandSlots<int,string>)||pending.TryGetValue(request.Entity,out var expected)&&ReferenceEquals(expected,request)||!ReferenceEquals(request.StorageOwner.TransportOwner,transportOwner)||request.StorageOwner.TransportAnswers<1||!DiscardPrefixIsNoOp())return null;
                actors=retainedGoals.Count==0?0:Crowd.Units.Count;publication=admissionPublicationIdentity;
            }
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,publication,out var scope))return false;
            using(scope){PreparedDiscard c=null;try{
                c=PreparedDiscard.Create(actors,scope.OwnedReservation());c.Names=NavPhysicalMacro.Acquire(c,names);
                using(EnterTransport()){
                    if(!AnswerNamesSourceCurrent(names)||actors!=(retainedGoals.Count==0?0:Crowd.Units.Count))return false;
                    c.Transport=NavPhysicalMacro.Acquire(c,transportOwner);c.TransportAnswers=request.StorageOwner.TransportAnswers;c.TransportExternal=request.StorageOwner.TransportExternal;c.Host=NavPhysicalMacro.Acquire(c,committedHostRoot);c.Pending=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,NavigationRequest>)pending);c.PendingRevision=c.Pending.Revision;
                    c.Goals=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,NavPoint>)retainedGoals);c.GoalRevision=c.Goals.Revision;c.Identities=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,string>)movementIdentities);c.IdentityRevision=c.Identities.Revision;
                    c.Tracked=IsCorridorAnswerTracked(answer);c.AnswerBytes=answerCorridorBytes;c.PostAnswerBytes=checked(c.AnswerBytes-(c.Tracked?answer.Corridor.EstimatedRetainedBytes:0));
                    for(int i=0;i<actors;i++){var actor=Crowd.Units[i];c.Actors[i]=new ReadyActorFact(actor);c.ActorPins[i]=NavPhysicalMacro.Acquire(c,actor.StorageOwner);}
                    if(!DiscardSourceCurrent(c))return false;
                }
                c.Admission=PrepareAdmissionProjection(groups,pending,requestSequence,checked(publication+1),names.Operation,scope,names.ObservedAdmission);c.RequireEdge(c.Admission);scope.Attach(c);
                Action<object> publish=_=>{long before=GC.GetAllocatedBytesForCurrentThread();using(var mailbox=Answers.EnterPreparedWrite()){
                    if(!DiscardSourceCurrent(c)||scope.IsCancelled||authorityOperationIdentity==long.MaxValue||RejectedResults==int.MaxValue||OwnedDiscardPublications==int.MaxValue)throw new InvalidOperationException("Prepared discard lost its invalidity witness.");
                    if(names.BarrierCount==0)mailbox.ValidateTakeOne(names.MailboxRevision,names.SourceAnswerCount,c.MailboxRetired);
                    c.RetiredTracking=ExtractAnswerChargeNoThrow(answer);answerCorridorBytes=c.PostAnswerBytes;
                    if(names.BarrierCount==0)mailbox.TakeOneNoThrow(c.MailboxRetired);else c.MailboxRetired[0]=TakeBarrierAnswerNoThrow(names.BarrierIndex);
                    c.RetiredTransport=c.Transport.ConsumeCountedPreparedNoThrow(request);RejectedResults++;if(PublishAdmissionOwnerNoThrow(c.Admission))c.Admission=null;authorityOperationIdentity++;OwnedDiscardPublications++;c.Published=true;
                    LastOwnedDiscardPublicationAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-before;
                }};
                try{using(EnterTransport()){if(stepDt>0)ValidateCompatibilityMotion(motionWorkspace);scope.Publish(c,publish);if(stepDt>0)AdmissionBatch(()=>StepMovementLocked(stepDt,motionWorkspace));}}
                finally{if(c.Published){scope.ResolveCommitted();SignalHost();}}return true;
            }catch(InvalidOperationException){if(c?.Published==true)throw;return false;}finally{c?.Dispose();}}
        }
    }
}
