using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        private static bool GeometryActorsCurrent(PreparedRebind c)
        {
            foreach(var fact in c.Actors){var actor=fact.Actor;if(actor.Outcome!=fact.SourceOutcome||!actor.Goal.Equals(fact.SourceGoal)||!NavigationAdmissionMemberStorage.SameLocation(actor.GoalLocation,fact.SourceGoalLocation)||!NavigationAdmissionMemberStorage.SameLocation(actor.FailedGoal,fact.SourceFailedGoal)||actor.FailedRequest!=fact.SourceFailedRequest||actor.FailedOrder!=fact.SourceFailedOrder||actor.FailedGroupId!=fact.SourceFailedGroup||actor.FailedRevision!=fact.SourceFailedRevision||actor.LastFailure!=fact.SourceLastFailure)return false;}return true;
        }
        private void PrepareGeometryGroupResets(PreparedRebind c,INavStorageReservation r)
        {
            for(int a=0;a<c.Actors.Length;a++){
                var fact=c.Actors[a];if(fact.Preserve||fact.Group==null||fact.Version.Data.MarchVersion==0)continue;
                int index=-1;for(int g=0;g<c.GroupCount;g++)if(ReferenceEquals(c.RebindGroups[g].Source.Owner,fact.Group))index=g;
                if(index<0){using(EnterTransport()){if(!OrdinaryRebindSourceCurrent(c))throw new InvalidOperationException("Geometry group source changed.");var observation=fact.Group.ObserveForCopy(ObserveLiveWriteToken());index=c.GroupCount;c.RebindGroups[index].Source=observation;c.GroupCount++;}}
                ref var group=ref c.RebindGroups[index];var prior=group.Next??group.Source.Version;int member=-1;for(int m=0;m<prior.Members.Count;m++)if(((NavigationAuthorityMember)prior.Members[m]).Data.Entity==fact.Id)member=m;
                if(member<0)throw new InvalidOperationException("Geometry actor lost its group member.");
                GroupOrderState header;using(EnterTransport()){if(!OrdinaryRebindSourceCurrent(c))throw new InvalidOperationException("Geometry march source changed.");header=groups[prior.Data.GroupId].CopyHeader();}header.MarchAnchor=0;header.MarchInitialized=false;
                var source=(NavigationAuthorityMember)prior.Members[member];var v=source.Data;
                var values=new NavigationAuthorityMember.Values(v.Entity,v.Slot,v.Incarnation,v.OrderRevision,v.MobilityRevision,v.Phase,v.Goal,v.Location,v.HasLocation,v.HasGoal,v.FormationReleased,v.Branch,v.Reservation,v.ContactTick,0,v.ProgressVersion==2?v.Progress:0,v.ProgressVersion,v.ProgressVersion==2?v.Progress:0);
                var changed=NavigationAuthorityMember.CopyWithValuesOwned(source,values,r);
                try{var next=NavigationAuthorityGroup.CopyWithMemberAndMarchOwned(prior,member,changed,new NavigationAuthorityGroup.Values(header),r);group.Next?.ReleaseStorage();group.Next=next;}finally{changed.ReleaseStorage();}
            }
            for(int g=0;g<c.GroupCount;g++){ref var group=ref c.RebindGroups[g];if(group.Next==null)group.Next=NavigationAuthorityGroup.CopyWithMemberOwned(group.Source.Version,0,(NavigationAuthorityMember)group.Source.Version.Members[0],r);group.Writes=group.Next.Members.Count;}
        }
        private static void ApplyGeometryRejectedNoThrow(PreparedRebind c)
        {
            foreach(var row in c.Retries)if(row.GeometryRejected&&row.Source!=null){var request=row.Source.Request;var actor=c.Actors[row.ActorIndex].Actor;row.ArrivalFailureNames.ApplyNoThrow(false);actor.FailedRequest=request.Request;actor.FailedOrder=request.Order;actor.FailedGroupId=request.MemberIdentity.GroupId;actor.FailedRevision=request.MemberIdentity.OrderRevision;actor.FailedGoal=request.GoalLocation;actor.LastFailure=NavigationOutcome.Rejected;}
        }
        private static void ApplyGeometryGroupsNoThrow(PreparedRebind c)
        {
            for(int g=0;g<c.GroupCount;g++){ref var group=ref c.RebindGroups[g];group.Retired=group.Source.Version;for(int m=0;m<group.Next.Members.Count;m++){var member=(NavigationAuthorityMember)group.Next.Members[m];group.Source.Owner.ApplyMemberNoThrow(group.Next,m,member.Data,member.ContactPhase,member.GoalLocation);}}
        }
    }
}
