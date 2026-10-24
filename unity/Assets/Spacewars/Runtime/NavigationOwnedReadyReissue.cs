using System;
using System.Globalization;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        private sealed partial class PreparedResultBatch
        {
            internal NavigationCommandSlots<int,long> NextOrders,RetiredOrders;
            internal NavigationCommandSlots<int,string> NextIdentities,RetiredIdentities;
            internal NavigationLiveRegistrations ReissueRegistrations;
            internal long SourceSequence,NextSequence,RequestRevision;internal int RequestCount,RegistrationCount,RegistrationRevision,ReissueCount,ExtraOperations;
            internal Action<NavigationRequest> RequestValidator,RequestTransfer;
            internal bool ReissueObserved,ReissueDestinations;
            internal void SealReissueDestinations(){NextOrders?.Seal();NextIdentities?.Seal();NextOrders?.Retain();NextIdentities?.Retain();foreach(var op in Operations)op.Replacement?.Retain();foreach(var row in HeldRows)row.Replacement.Replacement?.Retain();ReissueDestinations=true;}
            internal void TransferReissueNoThrow(){ReissueDestinations=false;}
            internal void ReleaseReissueEdges(){ReissueRegistrations?.Release();if(ReissueDestinations){NextOrders?.Release();NextIdentities?.Release();foreach(var op in Operations)op.Replacement?.Release();foreach(var row in HeldRows)row.Replacement.Replacement?.Release();}NextOrders?.ReleaseStorage();NextIdentities?.ReleaseStorage();RetiredOrders?.Release();RetiredIdentities?.Release();foreach(var op in Operations)op.Replacement?.ReleaseStorage();foreach(var row in HeldRows)row.Replacement.Replacement?.ReleaseStorage();}
            internal long ProjectedSequence(long original)=>ReissueObserved?NextSequence:original;
        }
        private bool ObserveResultReissue(PreparedResultBatch c)
        {
            if(c.ReissueObserved)return true;
            using(EnterTransport()){
                if(!ResultBatchSourceCurrent(c))return false;if(liveRegistrations==null&&RegisteredRequests.Count!=0)return false;
                c.SourceSequence=c.NextSequence=requestSequence;c.RequestRevision=Requests.Revision;c.RequestCount=Requests.Count;c.RegistrationCount=RegisteredRequests.Count;c.RegistrationRevision=liveRegistrations?.Revision??0;c.ReissueRegistrations=NavPhysicalMacro.Acquire(c,liveRegistrations);c.RequestValidator=requestEnqueueValidator;c.RequestTransfer=requestTransferHandler;c.ReissueObserved=true;return true;
            }
        }
        private bool ResultReissueSourcesCurrent(PreparedResultBatch c)
            =>!c.ReissueObserved||requestSequence==c.SourceSequence&&Requests.Revision==c.RequestRevision&&Requests.Count==c.RequestCount&&ReferenceEquals(liveRegistrations,c.ReissueRegistrations)&&(liveRegistrations?.Revision??0)==c.RegistrationRevision&&RegisteredRequests.Count==c.RegistrationCount&&ReferenceEquals(requestEnqueueValidator,c.RequestValidator)&&ReferenceEquals(requestTransferHandler,c.RequestTransfer);
        private bool PrepareMovedRejectedReady(PreparedResultBatch c,int at,NavigationRetentionBudget.Scope scope)
        {
            var identity=c.Names.Answers[at].Request.MemberIdentity;if(identity==null||identity.GroupId==0||!c.SourceGroups.ContainsKey(identity.GroupId))return false;
            if(!ObserveResultReissue(c))return false;
            ref var op=ref c.Operations[at];var request=c.Names.Answers[at].Request;NavLocation location=default;bool eligible;
            using(EnterTransport()){
                if(!ResultBatchSourceCurrent(c)||!Crowd.TryGet(request.Entity,out var actor))return false;
                c.ExtraOperations=checked(c.ExtraOperations+1);
                eligible=geometry.IsFree(request.Goal,actor.Radius)&&Crowd.Locate(request.Goal,actor.Radius,out location)&&c.RequestCount+c.ReissueCount+c.RegistrationCount<RouteRequestCapacity;
                if(eligible)ObserveResultFailureActor(c,actor,ref op.FailureSource);
            }
            if(!eligible)return PrepareFailureIntoResultBatch(c,at,scope,NavSolveStatus.Ready,true);
            if(!PrepareResultReplacement(c,ref op,request.Entity,request.Goal,location,op.FailureSource.Mobility,c.Names.GroupObservations[at],false,scope))return false;
            c.ExtraOperations++;c.RejectedCount++;if(op.Tracked)c.AnswerBytes-=c.Names.Answers[at].Corridor.EstimatedRetainedBytes;return true;
        }
        private bool PrepareResultReplacement(PreparedResultBatch c,ref ResultOperation op,int entity,NavPoint goal,NavLocation location,long mobility,NavigationLiveGroupObservation observation,bool refreshPhase,NavigationRetentionBudget.Scope scope)
        {
            var fact=op.FailureSource;var unitProfile=NavigationProfile.CreateForUnitOwned(profile,fact.Radius,fact.Speed,fact.TurnSpeed,scope.OwnedReservation());
            try{
                NavGeometry basis=null;long order,groupId,memberOrder,membership;
                using(EnterTransport()){
                    if(!ResultBatchSourceCurrent(c))return false;
                    if(!c.SourceGroups.TryGetValue(fact.Version.Data.GroupId,out var group))return false;
                    int index=-1;for(int i=0;i<group.Members.Length;i++)if(group.Members[i].Entity==entity){index=i;break;}if(index<0)return false;var member=group.Members[index];
                    (c.NextOrders??c.SourceOrders).TryGetValue(entity,out order);
                    groupId=group.GroupId;memberOrder=member.OrderRevision;membership=group.MembershipRevision;basis=navigationGeometry;basis.StorageRoot.Retain();
                }
                try{
                    var memberIdentity=new NavigationMemberIdentity(fact.Incarnation,mobility,groupId,memberOrder,membership);
                    var movement=ResultMovementGeometry(c,fact,basis);var held=ResultHeldIdentity(c,fact,false);
                    op.Replacement=NavigationHostRequest.CreateOwned(Generation,checked(c.NextSequence+1),entity,checked(order+1),unitProfile,movement,fact.Position,goal,basis,held,memberIdentity,fact.Location,location,null,scope.OwnedReservation());
                }finally{basis.StorageRoot.Release();}
            }finally{unitProfile.StorageRoot.Release();}
            var pending=c.NextPending??c.Pending;int minimum=pending.ContainsKey(entity)?0:checked(pending.Count+1);
            if(c.NextPending==null)c.NextPending=ResultBatchCopy(c,c.Pending,scope,minimum);else if(minimum>c.NextPending.Capacity){var grown=NavigationCommandSlots<int,NavigationRequest>.CreateOwned(minimum,scope.OwnedReservation());try{grown.CopyPreparedFrom(c.NextPending);}catch{grown.ReleaseStorage();throw;}c.NextPending.ReleaseStorage();c.NextPending=grown;}c.NextPending.Remove(entity);
            var ledger=c.NextLedger??c.Ledger;
            for(int i=0;i<ledger.Capacity;i++)if(ledger.TryReadSlot(i,out long key,out var row)&&row.Entity==entity&&row.State==NavArrivalReservationKind.PendingRequest){if(c.NextLedger==null)c.NextLedger=ResultBatchCopy(c,c.Ledger,scope);c.NextLedger.Remove(key);}
            if(!PrepareResultBatchPrune(c,scope))return false;
            if(c.NextOrders==null)c.NextOrders=ResultBatchCopy(c,c.SourceOrders,scope);c.NextOrders.SetPreparing(entity,op.Replacement.Request.Order);
            if(c.NextGoals==null)c.NextGoals=ResultBatchCopy(c,c.Goals,scope,c.Goals.ContainsKey(entity)?0:checked(c.Goals.Count+1));c.NextGoals.SetPreparing(entity,goal);
            if(c.NextReservations==null)c.NextReservations=ResultBatchCopy(c,c.Reservations,scope,c.Reservations.ContainsKey(entity)?0:checked(c.Reservations.Count+1));c.NextReservations.SetPreparing(entity,goal);
            if((c.NextFailures??c.Failures).ContainsKey(entity)){if(c.NextFailures==null)c.NextFailures=ResultBatchCopy(c,c.Failures,scope);c.NextFailures.Remove(entity);}
            c.NextPending.SetPreparing(entity,op.Replacement.Request,op.Replacement);
            if(c.NextIdentities==null)c.NextIdentities=ResultBatchCopy(c,c.Identities,scope,c.Identities.ContainsKey(entity)?0:checked(c.Identities.Count+1));c.NextIdentities.SetPreparing(entity,op.Replacement.Request.HeldIdentity,op.Replacement.HeldOwner);
            if(observation.Owner==null)return false;
            int g=-1;for(int i=0;i<c.GroupCount;i++)if(ReferenceEquals(c.GroupSources[i].Owner,observation.Owner)){g=i;break;}if(g<0){g=c.GroupCount++;c.GroupSources[g]=observation;}
            var previous=c.Groups[g]??observation.Version;int memberIndex=-1;for(int i=0;i<previous.Members.Count;i++)if(((NavigationAuthorityMember)previous.Members[i]).Data.Entity==entity){memberIndex=i;break;}if(memberIndex<0)return false;
            var original=(NavigationAuthorityMember)previous.Members[memberIndex];var d=original.Data;
            var values=new NavigationAuthorityMember.Values(d.Entity,d.Slot,d.Incarnation,d.OrderRevision,refreshPhase?mobility:d.MobilityRevision,refreshPhase?(fact.Held?GroupMemberPhase.Held:d.FormationReleased?GroupMemberPhase.Combat:GroupMemberPhase.PendingRoute):d.Phase,goal,location.Position,true,true,d.FormationReleased,d.Branch,refreshPhase?entity:d.Reservation,d.ContactTick,d.ProgressRequest,d.Progress,d.ProgressVersion,d.ProgressOffset);
            var changed=NavigationAuthorityMember.CreateOwned(values,original.ContactOwner,op.Replacement.Request.GoalLocation.Value.NameReference,scope.OwnedReservation());NavigationAuthorityGroup next=null;
            try{next=NavigationAuthorityGroup.CopyWithMemberAndMarchOwned(previous,memberIndex,changed,previous.Data,scope.OwnedReservation());}finally{changed.ReleaseStorage();}c.Groups[g]?.ReleaseStorage();c.Groups[g]=next;
            op.Kind=ResultOperationKind.Reissue;op.Group=g;op.Member=memberIndex;c.NextSequence=op.Replacement.Request.Request;c.ReissueCount++;return true;
        }
        private static NavGeometry ResultMovementGeometry(PreparedResultBatch c,RebindActor mover,NavGeometry basis)
        {
            int held=0;foreach(var other in c.Actors)if(other.Id!=mover.Id&&other.Held&&other.Team==mover.Team)held++;
            if(held==0)return basis;var solids=new NavObstacle[checked(basis.Obstacles.Count+held)];for(int i=0;i<basis.Obstacles.Count;i++)solids[i]=basis.Obstacles[i];
            int at=basis.Obstacles.Count;foreach(var other in c.Actors)if(other.Id!=mover.Id&&other.Held&&other.Team==mover.Team)solids[at++]=new NavObstacle(other.Position,other.Radius);
            return new NavGeometry(basis.HalfExtent,solids,basis.Revision);
        }
        private static string ResultHeldIdentity(PreparedResultBatch c,RebindActor mover,bool legacy)
        {
            int held=0;foreach(var other in c.Actors)if(other.Id!=mover.Id&&other.Held&&other.Team==mover.Team)held++;
            var rows=new string[held];int at=0;var culture=CultureInfo.InvariantCulture;
            foreach(var other in c.Actors)if(other.Id!=mover.Id&&other.Held&&other.Team==mover.Team)rows[at++]=other.Id+":"+other.Incarnation+":"+other.Mobility+":"+other.Team+":"+other.Position.X.ToString("R",culture)+":"+other.Position.Z.ToString("R",culture)+":"+other.Radius.ToString("R",culture);
            string raw=string.Join("|",rows);return legacy||raw.Length<=256?raw:NavContract.Digest(w=>{w.Write("held-binding-v1");w.Write(raw);});
        }
        private void PublishReissueMapsNoThrow(PreparedResultBatch c)
        {
            if(c.NextOrders!=null){c.RetiredOrders=groupCommandBacking.Orders;groupCommandBacking.Orders=c.NextOrders;orders=c.NextOrders;}
            if(c.NextIdentities!=null){c.RetiredIdentities=groupCommandBacking.Identities;groupCommandBacking.Identities=c.NextIdentities;movementIdentities=c.NextIdentities;}
            if(c.ReissueObserved)requestSequence=c.NextSequence;
        }
        private static void ClearReissueFailureNoThrow(NavUnit actor){actor.FailedRequest=actor.FailedOrder=actor.FailedGroupId=actor.FailedRevision=0;actor.FailedGoal=null;actor.LastFailure=NavigationOutcome.Idle;}
    }
}
