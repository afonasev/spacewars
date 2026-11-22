using System;
using System.Collections.Generic;
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
        internal bool CanAdmitFollowWaypoint(int id,NavPoint waypoint)
        {
            using(EnterTransport())return ValidateRecastSlots(new[]{id},new[]{waypoint},false);
        }

        internal bool CanOccupyCombatStance(int id, NavPoint goal)
        {
            using (EnterTransport()) return ValidateMovementSlots(new[] { id }, new[] { goal }, false);
        }

        internal bool TryFindBlockedFormationSlot(NavUnit actor, NavLocation assigned, out NavLocation? replacement,
            (NavPoint Center, double Radius)? objective = null)
        {
            replacement = null;
            using (EnterTransport())
            {
                if (!UsesRecastGroundRoutes || !Crowd.NearArrivalSlot(actor, assigned) ||
                    !memberGroups.TryGetValue(actor.Id, out long groupId) || !groups.TryGetValue(groupId, out var group))
                    return false;
                double pitch = Crowd.ArrivalSlotRecoveryPitch(actor);
                var ids = new[] { actor.Id };
                bool CanAssign(NavPoint point)
                {
                    var location = new NavLocation(point, assigned.SurfaceId);
                    return ValidateMovementSlots(ids, new[] { point }, false, location) &&
                        Crowd.Traverse(actor.Location, point, actor.Radius, out var reached, actor.Id) &&
                        reached.Equals(location);
                }
                if (CanAssign(assigned.Position))
                {
                    replacement = assigned;
                    return true;
                }
                if (!Crowd.Units.Any(peer => peer.Id != actor.Id && peer.Location.SurfaceId == assigned.SurfaceId &&
                    !NavFormationLayout.Separated(peer.Position, assigned.Position, actor.Radius + peer.Radius)))
                    return false;

                var direction = RetainedFormationDirection(group) ??
                    new NavPoint(Math.Cos(actor.Heading), Math.Sin(actor.Heading));
                var side = NavFormationLayout.Side(direction);
                double towardsActor = NavFormationLayout.Dot(new NavPoint(actor.Position.X - assigned.Position.X,
                    actor.Position.Z - assigned.Position.Z), side);
                double sideStep = towardsActor < 0 ? -pitch : pitch;
                bool Inside(NavPoint point) => objective is { } zone &&
                    NavFormationLayout.SquaredDistance(point, zone.Center) <= zone.Radius * zone.Radius;
                bool preserveObjective = Inside(assigned.Position);
                foreach (double offset in new[] { sideStep, -sideStep, 2 * sideStep, -2 * sideStep })
                {
                    var point = NavFormationLayout.At(assigned.Position, direction, 0, offset);
                    if (preserveObjective && !Inside(point) || !CanAssign(point)) continue;
                    replacement = new NavLocation(point, assigned.SurfaceId);
                    break;
                }
                return true;
            }
        }
        internal NavLocation ResolveSingleMoveEndpoint(PlayableProfile activeProfile,int id,NavPoint requested,NavLocation? explicitLocation)
        {
            if(!Crowd.TryGet(id,out var actor))throw new ArgumentException("Movement recipient is missing.",nameof(id));
            var origin=actor.Location;
            if(double.IsNaN(requested.X)||double.IsInfinity(requested.X)||double.IsNaN(requested.Z)||double.IsInfinity(requested.Z))
                return origin;

            NavLocation goal;
            bool located;
            if(explicitLocation.HasValue){
                goal=explicitLocation.Value;
                located=Crowd.ValidLocation(goal,actor.Radius);
            }else located=Crowd.Locate(requested,actor.Radius,out goal);

            NavGeometry destinationGeometry;
            using(EnterTransport()){
                if(located&&ValidateMovementSlots(new[]{id},new[]{requested},false,goal))return goal;
                var surface=located?goal.SurfaceId:origin.SurfaceId;
                var occupied=new List<NavObstacle>();
                void Add(NavLocation location,double radius)
                {
                    if(location.SurfaceId==surface)occupied.Add(new NavObstacle(location.Position,radius));
                }
                foreach(var peer in Crowd.Units)
                    if(peer.Id!=id)Add(peer.Location,peer.Radius);
                foreach(var row in arrivalLedger.Values)
                    if(row.Entity!=id)Add(row.Endpoint,row.Radius);
                foreach(var row in reservations){
                    if(row.Key==id)continue;
                    if(pending.TryGetValue(row.Key,out var request)&&request.GoalLocation.HasValue)
                        Add(request.GoalLocation.Value,request.Profile.Radius);
                    else if(installedExecutions.TryGetValue(row.Key,out var installed))
                        Add(installed.Corridor.Endpoint,installed.Corridor.Radius);
                    else if(Crowd.Locate(row.Value,0,out var reserved))
                        Add(reserved,profile.Radius);
                }
                destinationGeometry=occupied.Count==0?geometry:
                    new NavGeometry(geometry.HalfExtent,geometry.Obstacles.Concat(occupied).ToArray(),geometry.Revision);
            }
            var target=HumanMoveDestination.NearestReachable(activeProfile,geometry,destinationGeometry,origin,requested,actor.Radius);
            if(target.Equals(origin.Position))return origin;
            if(located&&Crowd.ValidLocation(new NavLocation(target,goal.SurfaceId),actor.Radius))
                return new NavLocation(target,goal.SurfaceId);
            return Crowd.Locate(target,actor.Radius,out var fallback)?fallback:origin;
        }

        internal NavLocation? FindNearbyFreeArrival(int id, NavPoint requested, double radius,
            Func<NavPoint, bool> visible)
        {
            using (EnterTransport())
            {
                if (!Crowd.TryGet(id, out var actor)) return null;
                var ids = new[] { id };
                var goals = new[] { requested };
                bool located = Crowd.Locate(requested, actor.Radius, out var preferred);
                if (located && visible(requested) && ValidateMovementSlots(ids, goals, false, preferred))
                    return preferred;

                // Only this small arrival zone is usable. Route reachability is
                // checked by the caller with the ordinary observed path planner.
                double cell = Math.Max(profile.GridCell, radius / 8);
                double limit = radius * radius;
                int minX = (int)Math.Ceiling((requested.X - radius) / cell);
                int maxX = (int)Math.Floor((requested.X + radius) / cell);
                int minZ = (int)Math.Ceiling((requested.Z - radius) / cell);
                int maxZ = (int)Math.Floor((requested.Z + radius) / cell);
                NavLocation? best = null;
                double bestDistance = double.PositiveInfinity;
                for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++)
                {
                    var point = new NavPoint(x * cell, z * cell);
                    double distance = NavFormationLayout.SquaredDistance(point, requested);
                    if (distance > limit || distance >= bestDistance || !visible(point))
                        continue;
                    NavLocation candidate;
                    if (located) candidate = new NavLocation(point, preferred.SurfaceId);
                    else if (!Crowd.Locate(point, actor.Radius, out candidate)) continue;
                    goals[0] = point;
                    if (!ValidateMovementSlots(ids, goals, false, candidate))
                        continue;
                    best = candidate;
                    bestDistance = distance;
                }
                return best;
            }
        }

        private bool ValidateRecastSlots(int[] ids,NavPoint[] goals,bool recordRefusal=true)
            =>!UsesRecastGroundRoutes||ValidateMovementSlots(ids,goals,recordRefusal);

        private bool ValidateMovementSlots(int[] ids,NavPoint[] goals,bool recordRefusal,NavLocation? explicitLocation=null)
        {
            bool Own(int entity,long incarnation)=>Array.IndexOf(ids,entity)>=0&&Crowd.TryGet(entity,out var actor)&&actor.Incarnation==incarnation;
            bool Overlap(NavPoint a,double ar,NavPoint b,double br){double x=a.X-b.X,z=a.Z-b.Z;return x*x+z*z<(ar+br)*(ar+br)-1e-9;}
            bool Refused(string reason){if(recordRefusal)RefuseCommandAdmissionForQa(reason);return false;}
            for(int i=0;i<ids.Length;i++){
                if(!Crowd.TryGet(ids[i],out var actor))return Refused("movement-recipient-missing");
                NavLocation location;
                bool located;
                if(explicitLocation.HasValue){location=explicitLocation.Value;located=Crowd.ValidLocation(location,actor.Radius);}
                else located=LocateCommandGoal(goals[i],actor.Radius,commandGroup,out location);
                if(!located||!geometry.IsFree(goals[i],actor.Radius))return Refused("movement-arrival-location");
                for(int j=0;j<i;j++){if(!Crowd.TryGet(ids[j],out var peer)||Overlap(goals[i],actor.Radius,goals[j],peer.Radius))return Refused("movement-arrival-slots-overlap");}
                foreach(var peer in Crowd.Units)if(peer.Location.SurfaceId==location.SurfaceId&&!Own(peer.Id,peer.Incarnation)&&Overlap(goals[i],actor.Radius,peer.Position,peer.Radius))return Refused("movement-arrival-occupied");
                foreach(var row in arrivalLedger.Values)if(row.Endpoint.SurfaceId==location.SurfaceId&&!Own((int)row.Entity,row.Incarnation)&&Overlap(goals[i],actor.Radius,row.Endpoint.Position,row.Radius))return Refused("movement-arrival-reserved");
                foreach(var row in reservations){
                    long incarnation=0;double radius=profile.Radius;string surface=null;
                    if(pending.TryGetValue(row.Key,out var pendingRequest)){incarnation=pendingRequest.MemberIdentity?.Incarnation??0;radius=pendingRequest.Profile.Radius;surface=pendingRequest.GoalLocation?.SurfaceId;}
                    else if(installedExecutions.TryGetValue(row.Key,out var installed)){incarnation=installed.Corridor.Incarnation;radius=installed.Corridor.Radius;surface=installed.Corridor.Endpoint.SurfaceId;}
                    if((surface==null||surface==location.SurfaceId)&&!Own(row.Key,incarnation)&&Overlap(goals[i],actor.Radius,row.Value,radius))return Refused("movement-arrival-reserved");
                }
            }
            return true;
        }
    }
}
