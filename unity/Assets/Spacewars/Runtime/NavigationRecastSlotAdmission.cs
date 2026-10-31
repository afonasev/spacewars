using System;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        private bool LocateCommandGoal(NavPoint point,double radius,GroupOrderState context,out NavLocation location){
            if(terrain is ILayeredPlayableTerrain&&context?.OriginalLocation!=null){
                location=new NavLocation(point,context.OriginalLocation.Value.SurfaceId);
                return Crowd.ValidLocation(location,radius);
            }
            return Crowd.Locate(point,radius,out location);
        }
        private bool LocateRetainedRecastGoal(NavPoint point,double radius,NavigationAuthorityGroup group,int entity,out NavLocation location)
        {
            if(!UsesRecastGroundRoutes)return Crowd.Locate(point,radius,out location);
            location=default;if(group==null)return false;
            foreach(var value in group.Members){var member=(NavigationAuthorityMember)value;
                if(member.Data.Entity!=entity)continue;var goal=member.GoalLocation;
                if(!member.Data.HasGoal||!goal.HasValue||!goal.Value.Position.Equals(point))return false;
                location=goal.Value;return Crowd.ValidLocation(location,radius);
            }
            return false;
        }
        // Atomic command admission replaces the selected actors' old endpoints.
        // Only the SAME actor incarnation owns an old reservation that may be ignored.
        private bool ValidateRecastSlots(int[] ids,NavPoint[] goals)
        {
            if(!UsesRecastGroundRoutes)return true;
            bool Own(int entity,long incarnation)=>Array.IndexOf(ids,entity)>=0&&Crowd.TryGet(entity,out var actor)&&actor.Incarnation==incarnation;
            bool Overlap(NavPoint a,double ar,NavPoint b,double br){double x=a.X-b.X,z=a.Z-b.Z;return x*x+z*z<(ar+br)*(ar+br)-1e-9;}
            for(int i=0;i<ids.Length;i++){
                if(!Crowd.TryGet(ids[i],out var actor)||!geometry.IsFree(goals[i],actor.Radius)||!LocateCommandGoal(goals[i],actor.Radius,commandGroup,out _))return false;
                for(int j=0;j<i;j++){if(!Crowd.TryGet(ids[j],out var peer)||Overlap(goals[i],actor.Radius,goals[j],peer.Radius))return false;}
                foreach(var peer in Crowd.Units)if(!Own(peer.Id,peer.Incarnation)&&Overlap(goals[i],actor.Radius,peer.Position,peer.Radius))return false;
                foreach(var row in arrivalLedger.Values)if(!Own((int)row.Entity,row.Incarnation)&&Overlap(goals[i],actor.Radius,row.Endpoint.Position,row.Radius))return false;
                foreach(var row in reservations){
                    long incarnation=0;double radius=profile.Radius;
                    if(pending.TryGetValue(row.Key,out var pendingRequest)){incarnation=pendingRequest.MemberIdentity?.Incarnation??0;radius=pendingRequest.Profile.Radius;}
                    else if(installedExecutions.TryGetValue(row.Key,out var installed)){incarnation=installed.Corridor.Incarnation;radius=installed.Corridor.Radius;}
                    if(!Own(row.Key,incarnation)&&Overlap(goals[i],actor.Radius,row.Value,radius))return false;
                }
            }
            return true;
        }
    }
}
