using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    // Human input projection only: the ordinary host still admits and executes
    // the command. Search precision comes from the existing navigation profile.
    public static class HumanMoveDestination
    {
        public sealed class Group
        {
            public readonly int[] Entities;public readonly NavPoint Target;
            public Group(IEnumerable<int> ids,NavPoint target){Entities=ids.ToArray();Target=target;}
        }
        private sealed class Region
        {
            internal readonly double Radius;internal readonly NavPoint Target;
            private readonly NavGeometry geometry,destinationGeometry;private readonly IPlayableTerrain terrain;
            private readonly double cell;private readonly Dictionary<NavLocation,NavLocation> reached=new Dictionary<NavLocation,NavLocation>();
            private readonly Queue<NavLocation> pending=new Queue<NavLocation>();
            private readonly NavLocation source;
            internal Region(PlayableProfile profile,NavGeometry geometry,NavGeometry destinationGeometry,NavLocation source,NavPoint requested,double radius)
            {
                Radius=radius;this.geometry=geometry;this.destinationGeometry=destinationGeometry;terrain=profile.AuthoredMap;cell=profile.Navigation.GridCell;this.source=source;
                if(destinationGeometry.IsFree(requested,Radius)&&Step(source,requested,out _)){Target=requested;return;}
                if(NearestDirectExit(profile,requested,out var direct)){Target=direct;return;}
                NavLocation best=source;double bestDistance=Distance(source.Position,requested);
                var first=Neighbors(source).ToArray();foreach(var next in first)Add(next);
                while(pending.Count>0){
                    var current=pending.Dequeue();double distance=Distance(current.Position,requested);
                    if(distance<bestDistance&&destinationGeometry.IsFree(current.Position,Radius)){best=current;bestDistance=distance;}
                    if(destinationGeometry.IsFree(requested,Radius)&&Step(current,requested,out _)){Target=requested;return;}
                    foreach(var next in Neighbors(current))Add(next);
                }
                // Refine the nearest reachable lattice position toward the requested
                // boundary, retaining exact footprint and sweep checks at every step.
                var safe=best;var outside=requested;double tolerance=Math.Min(profile.Navigation.LocalGridCell,profile.Navigation.ArrivalTolerance);
                while(Distance(safe.Position,outside)>tolerance*tolerance){
                    var middle=new NavPoint((safe.Position.X+outside.X)/2,(safe.Position.Z+outside.Z)/2);
                    if(destinationGeometry.IsFree(middle,Radius)&&Step(safe,middle,out var refined))safe=refined;else outside=middle;
                }
                Target=safe.Position;
            }
            private bool NearestDirectExit(PlayableProfile profile,NavPoint requested,out NavPoint result)
            {
                // If a reachable boundary meets a geometric lower bound, a full
                // component flood cannot find a closer result at our precision.
                // This keeps the common water/footprint click off the slow path.
                result=default;double tolerance=Math.Min(profile.Navigation.LocalGridCell,profile.Navigation.ArrivalTolerance),lowerBound=0;
                var candidates=new List<NavPoint>();
                void Circle(NavPoint center,double radius){
                    double x=requested.X-center.X,z=requested.Z-center.Z,distance=Math.Sqrt(x*x+z*z);
                    if(distance>radius)return;lowerBound=Math.Max(lowerBound,radius-distance);
                    if(distance>0)candidates.Add(new NavPoint(center.X+x/distance*(radius+tolerance),center.Z+z/distance*(radius+tolerance)));
                    else{candidates.Add(new NavPoint(center.X-radius-tolerance,center.Z));candidates.Add(new NavPoint(center.X+radius+tolerance,center.Z));candidates.Add(new NavPoint(center.X,center.Z-radius-tolerance));candidates.Add(new NavPoint(center.X,center.Z+radius+tolerance));}
                }
                double edge=destinationGeometry.HalfExtent-Radius;
                var bounded=new NavPoint(Math.Clamp(requested.X,-edge,edge),Math.Clamp(requested.Z,-edge,edge));
                if(!bounded.Equals(requested)){lowerBound=Math.Sqrt(Distance(bounded,requested));candidates.Add(bounded);}
                foreach(var obstacle in destinationGeometry.Obstacles){
                    if(obstacle.CircleRadius>0){Circle(new NavPoint((obstacle.MinX+obstacle.MaxX)/2,(obstacle.MinZ+obstacle.MaxZ)/2),obstacle.CircleRadius+Radius);continue;}
                    if(obstacle.Polygon!=null)continue;
                    var closest=new NavPoint(Math.Clamp(requested.X,obstacle.MinX,obstacle.MaxX),Math.Clamp(requested.Z,obstacle.MinZ,obstacle.MaxZ));
                    if(!closest.Equals(requested)){Circle(closest,Radius);continue;}
                    double exit=Math.Min(Math.Min(requested.X-obstacle.MinX,obstacle.MaxX-requested.X),Math.Min(requested.Z-obstacle.MinZ,obstacle.MaxZ-requested.Z))+Radius;
                    lowerBound=Math.Max(lowerBound,exit);
                    candidates.Add(new NavPoint(obstacle.MinX-Radius-tolerance,requested.Z));candidates.Add(new NavPoint(obstacle.MaxX+Radius+tolerance,requested.Z));
                    candidates.Add(new NavPoint(requested.X,obstacle.MinZ-Radius-tolerance));candidates.Add(new NavPoint(requested.X,obstacle.MaxZ+Radius+tolerance));
                }
                foreach(var candidate in candidates.OrderBy(p=>Distance(p,requested)).ThenBy(p=>Distance(p,source.Position)).ThenBy(p=>p.X).ThenBy(p=>p.Z)){
                    if(Distance(candidate,requested)>(lowerBound+tolerance)*(lowerBound+tolerance)||!destinationGeometry.IsFree(candidate,Radius)||!Step(source,candidate,out _))continue;
                    result=candidate;return true;
                }
                return false;
            }
            private static double Distance(NavPoint a,NavPoint b)=>(a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z);
            private bool Step(NavLocation from,NavPoint point,out NavLocation next)
            {
                next=default;
                if(!geometry.SegmentFree(from.Position,point,Radius))return false;
                if(terrain!=null)return terrain.TryTraverse(from,point,Radius,out next);
                next=new NavLocation(point,NavLocation.FlatSurface);return true;
            }
            private IEnumerable<NavLocation> Neighbors(NavLocation from)
            {
                int x=(int)Math.Round(from.Position.X/cell),z=(int)Math.Round(from.Position.Z/cell);
                // Eight adjacent lattice directions are the search topology, not a
                // tunable movement distance. Every edge needs a physical sweep.
                for(int dx=-1;dx<=1;dx++)for(int dz=-1;dz<=1;dz++){
                    var point=new NavPoint((x+dx)*cell,(z+dz)*cell);
                    if(!point.Equals(from.Position)&&Step(from,point,out var next))yield return next;
                }
            }
            private void Add(NavLocation location){if(reached.ContainsKey(location))return;reached.Add(location,location);pending.Enqueue(location);}
            internal bool Contains(NavLocation origin)
            {
                if(origin.Equals(source)||Step(origin,Target,out _))return true;
                foreach(var adjacent in Neighbors(origin))if(reached.ContainsKey(adjacent))return true;
                return false;
            }
        }
        public static Group[] Resolve(PlayableProfile profile,PlayableSnapshot view,IEnumerable<int> recipients,NavPoint requested,NavGeometry knownMovementGeometry=null)
        {
            var ids=recipients.ToArray();
            if(ids.Length==0||double.IsNaN(requested.X)||double.IsNaN(requested.Z)||double.IsInfinity(requested.X)||double.IsInfinity(requested.Z)||view.Geometry==null)return new[]{new Group(ids,requested)};
            var selected=new HashSet<int>(ids);
            var units=view.Entities.Where(e=>selected.Contains(e.Id)&&e.Owner==view.Owner&&e.Health>0).ToArray();
            // Preserve validation of foreign/missing IDs in the authority, rather
            // than silently converting a rejected command into a smaller one.
            if(units.Length!=ids.Length)return new[]{new Group(ids,requested)};
            var temporary=view.Wrecks.Where(w=>w.Blocks(view.Tick)).Select(w=>new NavObstacle(w.Position,w.Radius)).ToArray();
            // Participant Geometry is projectile geometry, not a movement query.
            // Reconstruct only public terrain and visible/own physical footprints.
            if(knownMovementGeometry==null){
                var obstacles=new List<NavObstacle>(PlayableMap.StaticObstacles(profile));
                foreach(var b in view.Buildings.Where(b=>b.Health>0&&b.Phase!=ConstructionPhase.Pending)){
                    double r=TerritoryRules.Radius(profile,b.Kind);obstacles.Add(new NavObstacle(b.Position.X-r,b.Position.Z-r,b.Position.X+r,b.Position.Z+r));
                }
                obstacles.AddRange(temporary);knownMovementGeometry=new NavGeometry(profile.ArenaHalfExtent,obstacles.ToArray(),view.Geometry.Revision);
            }
            var geometry=temporary.Length==0?knownMovementGeometry:new NavGeometry(knownMovementGeometry.HalfExtent,knownMovementGeometry.Obstacles.Where(o=>!temporary.Any(w=>w.GeometryEquals(o))).ToArray(),knownMovementGeometry.Revision);
            var regions=new List<Region>();var targets=new Dictionary<NavPoint,List<int>>();
            double groupRadius=units.Where(e=>e.Kind!=PlayableEntityKind.Hawk).Select(e=>PlayableUnitRules.Radius(profile,e.Kind)).DefaultIfEmpty(0).Max();
            foreach(var unit in units.OrderByDescending(e=>PlayableUnitRules.Radius(profile,e.Kind)).ThenBy(e=>e.Id)){
                NavPoint target=requested;
                if(unit.Kind!=PlayableEntityKind.Hawk){
                    double radius=PlayableUnitRules.Radius(profile,unit.Kind);NavLocation origin;
                    bool located=profile.AuthoredMap==null;
                    if(located)origin=new NavLocation(unit.Position,NavLocation.FlatSurface);
                    else if(unit.Location.HasValue&&profile.AuthoredMap.IsValidLocation(unit.Location.Value,radius)){origin=unit.Location.Value;located=true;}
                    else located=profile.AuthoredMap.TryLocate(unit.Position,radius,out origin);
                    // Production may legitimately start inside its departure footprint;
                    // the ordinary host owns that escape and must retain the raw goal.
                    if(located&&geometry.IsFree(origin.Position,radius)){
                        // A connected selection keeps one destination with clearance
                        // for its largest ground member, instead of splitting a
                        // formation merely because its unit kinds have different radii.
                        if(geometry.IsFree(origin.Position,groupRadius)&&(profile.AuthoredMap==null||profile.AuthoredMap.IsValidLocation(origin,groupRadius)))radius=groupRadius;
                        var region=regions.FirstOrDefault(r=>r.Radius==radius&&r.Contains(origin));
                        if(region==null){region=new Region(profile,geometry,knownMovementGeometry,origin,requested,radius);regions.Add(region);}
                        target=region.Target;
                    }
                }
                if(!targets.TryGetValue(target,out var group)){group=new List<int>();targets.Add(target,group);}group.Add(unit.Id);
            }
            return targets.Select(p=>new Group(p.Value.OrderBy(id=>id),p.Key)).ToArray();
        }
    }
}
