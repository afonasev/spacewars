using System;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        private sealed partial class PreparedRebind
        {
            internal NavigationGroupCommand MixedArrival;internal NavigationCommandAuthority RetiredCommand;
            internal NavigationCommandSlots<long,GroupOrderState> MixedSourceGroups,MixedGroups;
            internal NavigationCommandSlots<int,long> MixedSourceMembers,MixedMembers;
            internal int MixedGroupRevision,MixedMemberRevision,MixedRequests,ArrivalAssignments,ArrivalDispositionCount,ArrivalTerminalCount,SourceUnreachable,NextUnreachable;
            internal long MixedCommit,NextCommit;internal bool MixedObserved;
            internal void ReleaseMixedArrivalEdges(){MixedArrival?.ReleaseStorage();RetiredCommand?.ReleaseStorage();MixedSourceGroups?.Release();MixedSourceMembers?.Release();MixedGroups?.ReleaseStorage();MixedMembers?.ReleaseStorage();}
        }
        private static int ArrivalDispositionAt(PreparedRebind c,int ordinal){for(int n=0;n<c.Retries.Length;n++)if(c.Retries[n].Selected&&c.Retries[n].ArrivalOrder==ordinal)return n;throw new InvalidOperationException("Missing arrival disposition.");}
        private void ObserveMixedArrivalSource(PreparedRebind c)
        {
            c.MixedSourceGroups=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<long,GroupOrderState>)groups);c.MixedGroupRevision=c.MixedSourceGroups.Revision;c.MixedSourceMembers=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,long>)memberGroups);c.MixedMemberRevision=c.MixedSourceMembers.Revision;
            c.SourceOrders=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,long>)orders);c.OrderRevision=c.SourceOrders.Revision;c.SourceReservations=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,NavPoint>)reservations);c.ReservationRevision=c.SourceReservations.Revision;c.SourceGoals=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,NavPoint>)retainedGoals);c.GoalRevision=c.SourceGoals.Revision;c.SourceFailures=NavPhysicalMacro.Acquire(c,(NavigationCommandSlots<int,NavSolveStatus>)technicalFailures);c.FailureRevision=c.SourceFailures.Revision;
            c.RequestsRevision=Requests.Revision;c.MixedRequests=Requests.Count;c.MixedCommit=c.NextCommit=arrivalCommitSequence;c.SourceUnreachable=c.NextUnreachable=UnreachableResults;
            for(int i=0;i<c.Actors.Length;i++){ref var fact=ref c.Actors[i];
                fact.SourceLocationName=OwnedOrFlatName(fact.Actor.Location);fact.SourceGoalName=OwnedOrFlatName(fact.Actor.GoalLocation);fact.SourceFailureName=OwnedOrFlatName(fact.Actor.FailedGoal);fact.SourceGoalLocation=fact.Actor.GoalLocation;fact.SourceFailedGoal=fact.Actor.FailedGoal;fact.SourceFailedRequest=fact.Actor.FailedRequest;fact.SourceFailedOrder=fact.Actor.FailedOrder;fact.SourceFailedGroup=fact.Actor.FailedGroupId;fact.SourceFailedRevision=fact.Actor.FailedRevision;fact.SourceLastFailure=fact.Actor.LastFailure;
            }
            c.MixedObserved=true;
        }
        private bool MixedArrivalSourceCurrent(PreparedRebind c)
        {
            if(!c.MixedObserved)return true;
            if(!ReferenceEquals(groups,c.MixedSourceGroups)||c.MixedSourceGroups.Revision!=c.MixedGroupRevision||!ReferenceEquals(memberGroups,c.MixedSourceMembers)||c.MixedSourceMembers.Revision!=c.MixedMemberRevision||!ReferenceEquals(orders,c.SourceOrders)||c.SourceOrders.Revision!=c.OrderRevision||!ReferenceEquals(reservations,c.SourceReservations)||c.SourceReservations.Revision!=c.ReservationRevision||!ReferenceEquals(retainedGoals,c.SourceGoals)||c.SourceGoals.Revision!=c.GoalRevision||!ReferenceEquals(technicalFailures,c.SourceFailures)||c.SourceFailures.Revision!=c.FailureRevision||Requests.Revision!=c.RequestsRevision||Requests.Count!=c.MixedRequests||arrivalCommitSequence!=c.MixedCommit||UnreachableResults!=c.SourceUnreachable)return false;
            foreach(var fact in c.Actors)if(fact.Actor.FailedRequest!=fact.SourceFailedRequest||fact.Actor.FailedOrder!=fact.SourceFailedOrder||fact.Actor.FailedGroupId!=fact.SourceFailedGroup||fact.Actor.FailedRevision!=fact.SourceFailedRevision||fact.Actor.LastFailure!=fact.SourceLastFailure||!NavigationAdmissionMemberStorage.SameLocation(fact.SourceGoalLocation,fact.Actor.GoalLocation)||!NavigationAdmissionMemberStorage.SameLocation(fact.SourceFailedGoal,fact.Actor.FailedGoal)||!SameArrivalName(fact.SourceLocationName,OwnedOrFlatName(fact.Actor.Location))||!SameArrivalName(fact.SourceGoalName,OwnedOrFlatName(fact.Actor.GoalLocation))||!SameArrivalName(fact.SourceFailureName,OwnedOrFlatName(fact.Actor.FailedGoal)))return false;
            return true;
        }
        private static bool SameArrivalName(NavNameReference a,NavNameReference b)=>ReferenceEquals(a.Owner,b.Owner)&&a.Role==b.Role&&a.Index==b.Index;
        private static void ApplyArrivalRetryPreparing(PreparedRebind c,ref RebindRetry row)
        {
            if(!row.Valid){c.Allocating.Remove(row.Entity);return;}
            c.Allocating.SetPreparing(row.Entity,row.Next.Request,row.Next);c.Identities.SetPreparing(row.Entity,row.Next.Request.HeldIdentity,row.Next.HeldIdentityOwner);
        }
        private bool PrepareMixedArrival(PreparedRebind c,NavigationRetentionBudget.Scope scope)
        {
            var r=scope.OwnedReservation();c.MixedArrival=NavigationGroupCommand.CreateOwned(Requests.InstanceCapacity,c.ArrivalAssignments,r);c.RequireEdge(c.MixedArrival);var command=c.MixedArrival;
            c.MixedGroups=CopyOrdinaryRebindMap(c,c.MixedSourceGroups,r);c.MixedMembers=CopyOrdinaryRebindMap(c,c.MixedSourceMembers,r);c.NextOrders=CopyOrdinaryRebindMap(c,c.SourceOrders,r);c.NextPending=CopyOrdinaryRebindMap(c,c.SourcePending,r,checked(c.SourcePending.Count+c.ArrivalAssignments));c.NextReservations=CopyOrdinaryRebindMap(c,c.SourceReservations,r,checked(c.SourceReservations.Count+c.ArrivalAssignments));c.NextGoals=CopyOrdinaryRebindMap(c,c.SourceGoals,r);c.NextFailures=CopyOrdinaryRebindMap(c,c.SourceFailures,r);c.NextLedger=CopyOrdinaryRebindMap(c,c.SourceLedger,r,checked(c.SourceLedger.Count+c.ArrivalAssignments));
            command.Groups=c.MixedGroups;command.MemberGroups=c.MixedMembers;command.Orders=c.NextOrders;command.Pending=c.NextPending;command.Allocating=c.Allocating;command.Reservations=c.NextReservations;command.Goals=c.NextGoals;command.Identities=c.Identities;command.Failures=c.NextFailures;command.Ledger=c.NextLedger;command.OwnMaps();
            PrepareCommandMailbox(command,c.RequestsRevision,c.MixedRequests,scope,CommandMailboxKind.MixedArrival);
            if(command.MailboxCount+c.ArrivalAssignments>Requests.InstanceCapacity)return false;
            int issued=0;
            for(int ordinal=0;ordinal<c.ArrivalDispositionCount;ordinal++){
                ref var row=ref c.Retries[ArrivalDispositionAt(c,ordinal)];if(row.ArrivalKind==2){ApplyArrivalRetryPreparing(c,ref row);continue;}if(row.ArrivalKind==3){c.Allocating.Remove(row.Entity);c.NextFailures.Remove(row.Entity);c.NextGoals.Remove(row.Entity);if(row.ArrivalTerminalStatus==NavArrivalStatus.UnreachableInGraph){if(c.NextUnreachable==int.MaxValue)return false;c.NextUnreachable++;}continue;}if(row.ArrivalKind!=1)continue;var intent=row.Source.Request;var proposal=row.ArrivalProposal;
                row.Next=NavigationHostRequest.CreateOwned(intent.Session,intent.Request,intent.Entity,intent.Order,intent.Profile,intent.Geometry,intent.Start,proposal.Endpoint.Position,intent.BaseGeometry,intent.HeldIdentity,intent.MemberIdentity,intent.StartLocation,proposal.Endpoint,null,r);
                var request=row.Next.Request;command.OwnIssued(issued,row.Next);command.Issued[issued++]=request;command.AppendMailbox(request);c.Allocating.Remove(request.Entity);c.NextPending.SetPreparing(request.Entity,request,row.Next);c.NextGoals.SetPreparing(request.Entity,request.Goal);c.NextReservations.SetPreparing(request.Entity,request.Goal);
                var value=new NavigationArrivalReservation{CommitOrder=checked(++c.NextCommit),Id=request.Request,World=Generation,Group=proposal.Member.GroupId,Root=proposal.Member.RootOrderRevision,Command=proposal.Member.CommandSequence,IssuedTick=proposal.Member.IssuedTick,Activation=proposal.Member.ActivationRevision,Entity=request.Entity,Incarnation=proposal.Member.Incarnation,Mobility=proposal.Member.MobilityRevision,Request=request.Request,Order=request.Order,Endpoint=proposal.Endpoint,Radius=proposal.Member.Radius,Proof=proposal.Proof.StableKey,Provider=proposal.Proof.ProviderBinding,Profile=NavigationAdmission.ExactProfileBinding(request.Profile),Hold=request.HeldIdentity,State=NavArrivalReservationKind.PendingRequest};
                var owner=NavigationLiveArrivalReservation.CopyOwned(value,r);try{c.NextLedger.SetPreparing(request.Request,owner.Entry,owner);}finally{owner.ReleaseStorage();}
            }
            foreach(var groupId in command.Issued.Select(request=>c.MixedMembers[request.Entity]).Distinct()){
                var live=(NavigationLiveGroup)c.MixedGroups.OwnerFor(groupId);NavigationAuthorityGroup version;
                using(EnterTransport()){if(!ArrivalRetrySourceCurrent(c))return false;version=live.ImmutableVersion;version.Retain();}
                try{foreach(var request in command.Issued.Where(request=>c.MixedMembers[request.Entity]==groupId)){
                    int index=Enumerable.Range(0,version.Members.Count).Single(i=>((NavigationAuthorityMember)version.Members[i]).Data.Entity==request.Entity);var original=(NavigationAuthorityMember)version.Members[index];var data=original.Data;var actor=c.Actors.First(fact=>fact.Id==request.Entity);
                    var values=new NavigationAuthorityMember.Values(data.Entity,data.Slot,data.Incarnation,data.OrderRevision,data.MobilityRevision,actor.Held?GroupMemberPhase.Held:data.FormationReleased?GroupMemberPhase.Combat:GroupMemberPhase.PendingRoute,request.Goal,request.GoalLocation.Value.Position,true,true,data.FormationReleased,data.Branch,request.Entity,data.ContactTick,data.ProgressRequest,data.Progress,data.ProgressVersion,data.ProgressOffset);
                    var member=NavigationAuthorityMember.CreateOwned(values,original.ContactOwner,request.GoalLocation.Value.NameReference,r);try{var next=NavigationAuthorityGroup.CopyWithMemberOwned(version,index,member,r);version.Release();version=next;}finally{member.ReleaseStorage();}
                }
                var birth=NavigationLiveGroup.CreateOwned(version,scope);try{birth.SealForInstall(scope);c.MixedGroups.SetPreparing(groupId,birth.PreparedRecord(scope),birth);command.Births[command.BirthCount++]=birth;}finally{birth.ReleaseStorage();}
                }finally{version.Release();}
            }
            command.Committed.CrowdNames=CaptureCrowdNameReferences(r);
            for(int ordinal=0;ordinal<c.ArrivalDispositionCount;ordinal++){ref var row=ref c.Retries[ArrivalDispositionAt(c,ordinal)];if(row.ArrivalKind!=3)continue;int at=command.Committed.CrowdNames.Find(row.Entity);if(at<0)return false;row.ArrivalFailureNames=NavigationPreparedCrowdNameWrite.CreateOwned(command.Committed.CrowdNames,at,row.Source.Request,r);c.RequireEdge(row.ArrivalFailureNames);row.ArrivalFailureNames.ApplyNoThrow(false);}
            c.MixedGroups.Seal();c.MixedMembers.Seal();c.NextOrders.Seal();c.NextPending.Seal();c.NextReservations.Seal();c.NextGoals.Seal();c.NextFailures.Seal();c.NextLedger.Seal();return true;
        }
        private void ApplyTerminalArrivalScalarsNoThrow(PreparedRebind c)
        {
            if(c.ArrivalTerminalCount==0)return;
            foreach(var row in c.Retries)if(row.ArrivalKind==3){var request=row.Source.Request;var actor=c.Actors[row.ActorIndex].Actor;actor.FailedRequest=request.Request;actor.FailedOrder=request.Order;actor.FailedGroupId=request.MemberIdentity.GroupId;actor.FailedRevision=request.MemberIdentity.OrderRevision;actor.FailedGoal=request.GoalLocation;actor.LastFailure=row.ArrivalTerminalStatus==NavArrivalStatus.UnreachableInGraph?NavigationOutcome.Unreachable:NavigationOutcome.Rejected;}
            UnreachableResults=c.NextUnreachable;
        }
        private void InstallMixedArrivalNoThrow(PreparedRebind c,NavigationRetentionBudget.Scope scope)
        {
            var command=c.MixedArrival;for(int i=0;i<command.BirthCount;i++)command.Births[i].ActivateNoThrow(this);
            c.RetiredCommand=groupCommandBacking;groupCommandBacking=command.Committed;groups=c.MixedGroups;memberGroups=c.MixedMembers;orders=c.NextOrders;pending=c.NextPending;allocating=c.Allocating;reservations=c.NextReservations;retainedGoals=c.NextGoals;movementIdentities=c.Identities;technicalFailures=c.NextFailures;arrivalLedger=c.NextLedger;arrivalCommitSequence=c.NextCommit;command.Published=true;for(int i=0;i<command.BirthCount;i++)command.Births[i]=null;command.BirthCount=0;
        }
    }
}
