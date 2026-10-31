using System;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        private void ValidatePhysicalRestoredRoutes(){
            if(!(terrain is IRecastPhysicalTerrain physical))return;
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Decode,Generation,0,out var scope))throw new InvalidOperationException("Physical route restore qualification refused.");
            using(scope){var r=scope.OwnedReservation();foreach(var actor in Crowd.Units){
                if(!actor.Moving)continue;
                if(!installedExecutions.TryGetValue(actor.Id,out var installed)||installed.StorageOwner?.Corridor?.TypedRoute==null||!actor.GoalLocation.HasValue)throw new ArgumentException("Moving physical actor lost its accepted typed route.");
                var route=installed.StorageOwner.Corridor.TypedRoute;int cursor=0;
                if(memberGroups.TryGetValue(actor.Id,out var groupId)){var member=groups[groupId].Members.Single(m=>m.Entity==actor.Id);if(member.Recast.Request>0)cursor=member.Recast.Cursor;}
                if(cursor<0||cursor>=route.Legs.Count||route.Legs[cursor].From.SurfaceId!=actor.Location.SurfaceId)throw new ArgumentException("Physical actor/cursor surface mismatch.");
                var at=actor.Location;var provider=route.Legs[0].RecastAuthorityProvider;
                using(var scratch=NavStorageScratch.Reserve(r,8192))foreach(var point in actor.localRoute.Skip(actor.localIndex).Concat(actor.route.Skip(actor.routeIndex))){
                    if(!geometry.SegmentFree(at.Position,point,actor.Radius)||!physical.TracePhysical(at,point,actor.Radius,provider,route,cursor,r,out var end,out var trace))throw new ArgumentException($"Invalid current physical route continuation actor={actor.Id} cursor={cursor} from={at.SurfaceId}:{at.Position.X},{at.Position.Z} to={point.X},{point.Z}.");
                    try{foreach(var leg in trace)if(leg.Kind==NavTypedLegKind.Portal&&!RecastLayerTrace.MatchPortal(route,ref cursor,leg.PortalId,leg.From.SurfaceId,leg.To.SurfaceId))throw new ArgumentException("Physical route portal continuation changed.");}finally{(trace as NavOwnedStorage)?.ReleaseOwner();}at=end;
                }
                // A target at a zero-XZ portal can leave only its source-side
                // XZ waypoint in the remaining motor suffix. Qualify the same
                // accepted semantic step the motor will take on its next frame,
                // without changing any loaded actor or saved cursor.
                if(!at.Equals(actor.GoalLocation.Value)&&at.Position.Equals(actor.GoalLocation.Value.Position)){
                    using(var scratch=NavStorageScratch.Reserve(r,8192)){
                        if(!geometry.SegmentFree(at.Position,actor.GoalLocation.Value.Position,actor.Radius)||!physical.TracePhysical(at,actor.GoalLocation.Value.Position,actor.Radius,provider,route,cursor,r,out var end,out var trace))throw new ArgumentException("Invalid terminal physical route continuation.");
                        try{int crossed=0;foreach(var leg in trace)if(leg.Kind==NavTypedLegKind.Portal){if(!RecastLayerTrace.MatchPortal(route,ref cursor,leg.PortalId,leg.From.SurfaceId,leg.To.SurfaceId))throw new ArgumentException("Physical route terminal portal changed.");crossed++;}if(crossed>0)at=end;}finally{(trace as NavOwnedStorage)?.ReleaseOwner();}
                    }
                }
                if(!at.Equals(actor.GoalLocation.Value))throw new ArgumentException("Current physical route endpoint changed.");
            }}
        }
        private void ValidateSurfaceState(NavigationSessionState state)
        {
            if(state.Crowd?.Units==null||state.Crowd.Units.Any(u=>u==null)||state.Requests.Any(r=>r==null)||state.Groups!=null&&state.Groups.Any(g=>g==null||g.Members==null||g.Members.Any(m=>m==null)))throw new ArgumentException("Invalid surface checkpoint collections.");
            var actorById=state.Crowd.Units.ToDictionary(u=>u.Id);
            var pendingByEntity=new System.Collections.Generic.Dictionary<int,NavigationRequestState>();
            foreach(int index in state.PendingRequestIndices.Concat(state.AllocationRequestIndices??Array.Empty<int>())){if(index<0||index>=state.Requests.Length||!pendingByEntity.TryAdd(state.Requests[index].Entity,state.Requests[index]))throw new ArgumentException("Invalid pending surface request indices.");}
            bool legacy=state.SurfaceSemanticsVersion==0;
            if(!legacy&&(state.SurfaceSemanticsVersion!=(terrain?.SurfaceSemanticsVersion??1)||state.SurfaceProviderId!=(terrain?.Id??NavLocation.FlatSurface)))throw new ArgumentException("Terrain surface semantics mismatch.");
            // Explicit legacy codec: the two currently supported authored providers
            // declare single-valued canonical partitions. No generic first-overlap
            // decoder is offered for future layered providers.
            if(legacy&&terrain!=null&&!(terrain is ThreeCrossingsMap)&&!(terrain is FoundryMap))throw new ArgumentException("No legacy location codec for this terrain.");
            NavLocation Bind(NavPoint point,double radius){if(!Crowd.Locate(point,radius,out var bound))throw new ArgumentException("Invalid legacy terrain location.");return bound;}
            void Check(NavLocation? location,NavPoint point,double radius,bool required){
                if(!location.HasValue){if(required)throw new ArgumentException("Missing surface reference.");return;}
                if(!location.Value.Position.Equals(point)||!Crowd.ValidLocation(location.Value,radius))throw new ArgumentException("Invalid surface/XZ reference.");
            }
            foreach(var unit in state.Crowd.Units){
                if(legacy){unit.Location=Bind(unit.Position,unit.Radius);if(unit.Moving)unit.GoalLocation=Bind(unit.Goal,0);}
                Check(unit.Location,unit.Position,unit.Radius,true);Check(unit.GoalLocation,unit.Goal,0,unit.Moving);
                // Physical layered routes are checked after their paid typed BODY
                // and current member cursor have been restored, below.
                if(terrain!=null&&!(terrain is IRecastPhysicalTerrain)&&unit.Moving){var location=unit.Location.Value;
                    foreach(var point in unit.LocalRoute.Skip(unit.LocalIndex).Concat(unit.Route.Skip(unit.RouteIndex)))if(!Crowd.Traverse(location,point,unit.Radius,out location))throw new ArgumentException("Invalid saved surface route.");
                    if(!unit.GoalLocation.HasValue||!Crowd.CompatibleArrival(location,unit.GoalLocation.Value,unit.Radius))throw new ArgumentException("Saved surface route endpoint mismatch.");
                }
            }
            foreach(var request in state.Requests){
                if(request.Entity<0){if(request.StartLocation.HasValue||request.GoalLocation.HasValue)throw new ArgumentException("Unexpected producer surface binding.");continue;}
                if(legacy){request.StartLocation=Bind(request.Start,0);request.GoalLocation=Bind(request.Goal,0);}
                Check(request.StartLocation,request.Start,0,true);Check(request.GoalLocation,request.Goal,0,true);
            }
            foreach(var group in state.Groups??Array.Empty<GroupOrderState>()){
                bool spatial=group.Kind==PlayableCommandKind.Move||group.Kind==PlayableCommandKind.AttackMove;
                if(legacy&&spatial)group.OriginalLocation=Bind(group.OriginalGoal,0);
                Check(group.OriginalLocation,group.OriginalGoal,0,spatial&&group.OwnerId!=null);
                foreach(var member in group.Members){if(legacy&&member.HasGoal)member.GoalLocation=Bind(member.Goal,0);Check(member.GoalLocation,member.Goal,0,member.HasGoal);}
            }
            if(!legacy)foreach(var group in state.Groups??Array.Empty<GroupOrderState>())foreach(var member in group.Members){
                if(pendingByEntity.TryGetValue(member.Entity,out var request)){
                    if(!member.HasGoal||!member.Goal.Equals(request.Goal)||!member.GoalLocation.Equals(request.GoalLocation))throw new ArgumentException("Pending group surface assignment mismatch.");
                }else{if(!actorById.TryGetValue(member.Entity,out var actor))throw new ArgumentException("Missing surface group actor.");if(actor.Moving&&(!member.HasGoal||!member.Goal.Equals(actor.Goal)||!member.GoalLocation.Equals(actor.GoalLocation))&&!(actor.InstalledRequest>0&&actor.FailedRequest>actor.InstalledRequest&&actor.FailedGroupId==group.GroupId&&actor.FailedRevision==member.OrderRevision&&actor.FailedGoal.Equals(member.GoalLocation)&&state.Orders.Any(o=>o.Entity==actor.Id&&o.Order==actor.FailedOrder)))throw new ArgumentException("Installed group surface assignment mismatch.");}
            }
            foreach(var row in state.Reservations.Concat(state.RetainedGoals)){
                var member=state.Groups?.SelectMany(g=>g.Members).FirstOrDefault(m=>m.Entity==row.Entity);
                if(!legacy){
                    if(member==null||!member.HasGoal||!member.Goal.Equals(row.Point)||!member.GoalLocation.HasValue)throw new ArgumentException("Unbound reservation/retained goal.");
                    if(pendingByEntity.TryGetValue(row.Entity,out var request)&&(!request.Goal.Equals(row.Point)||!request.GoalLocation.Equals(member.GoalLocation)))throw new ArgumentException("Reservation/pending surface mismatch.");
                }
            }
        }
    }
}
