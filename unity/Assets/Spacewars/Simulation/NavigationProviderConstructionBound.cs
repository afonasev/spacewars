using System;
using System.Linq;

namespace Spacewars.Simulation
{
    // Reserve before provider construction. This does not change retained accounting or solver limits.
    internal static class NavigationProviderConstructionBound
    {
        internal static long Estimate(IPlayableTerrain terrain,NavGeometry movementGeometry)
        {
            if(movementGeometry==null)throw new ArgumentNullException(nameof(movementGeometry));
            if(terrain==null)return checked(4096L+1024+2048+512+
                movementGeometry.Obstacles.Sum(o=>512L+128L*(o.Polygon?.Vertices.Count??4))+4096);
            if(!PartitionNavigationProvider.SupportsTerrain(terrain)||!(terrain is INavArrivalPointTerrain authored))
                throw new ArgumentException("No construction bound for this authored terrain.",nameof(terrain));
            var locations=authored.ArrivalLocations;
            if(locations==null||locations.FootprintCacheRetainedBytes<=0||locations.RegionIndexRetainedBytes<=0||movementGeometry.HalfExtent!=terrain.HalfExtent||locations.Geometry.HalfExtent!=terrain.HalfExtent||
                locations.Supports.Count!=terrain.Supports.Count||locations.Transitions.Count!=terrain.SurfaceTransitions.Count||
                !locations.Supports.Zip(terrain.Supports,(a,b)=>ReferenceEquals(a,b)).All(same=>same)||
                !locations.Transitions.Zip(terrain.SurfaceTransitions,(a,b)=>ReferenceEquals(a,b)).All(same=>same))
                throw new ArgumentException("Authored construction bound requires the same support/transition arrangement and extent.",nameof(terrain));
            long edges=terrain.Supports.Sum(v=>(long)(v.Bounds.Polygon?.Vertices.Count??4));
            // Construction now borrows the already built immutable arrangement.
            // Bound actual seam facades and their copy maps, never hypothetical
            // pairwise edge intersections. Geometry/HOLD caches remain per view.
            return checked(8192L+1024L*terrain.Supports.Count+512L*edges+512L*locations.Seams.Count+256L*(locations.Supports.Count+locations.Transitions.Count+locations.Seams.Count+edges)+
                256L*terrain.SurfaceTransitions.Count+
                movementGeometry.Obstacles.Sum(o=>512L+128L*(o.Polygon?.Vertices.Count??0))+
                movementGeometry.SpatialIndexRetainedBytes+locations.FootprintCacheRetainedBytes+locations.RegionIndexRetainedBytes);
        }
    }
}
