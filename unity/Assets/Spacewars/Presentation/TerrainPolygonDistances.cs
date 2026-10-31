using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Presentation
{
    // A preparation-local query over immutable presentation geometry. In particular,
    // NavObstacle.Footprint constructs a polygon for a rectangle: never do that for
    // every pixel. This does not replace any navigation/collision query.
    public sealed class TerrainPolygonDistances
    {
        readonly NavPolygon[] polygons;
        public TerrainPolygonDistances(IEnumerable<NavObstacle> shapes)=>polygons=shapes.Select(s=>s.Footprint).ToArray();
        public double Minimum(NavPoint point)
        {
            double best=double.MaxValue;
            foreach(var polygon in polygons){
                double x=Math.Max(polygon.MinX-point.X,Math.Max(0,point.X-polygon.MaxX));
                double z=Math.Max(polygon.MinZ-point.Z,Math.Max(0,point.Z-polygon.MaxZ));
                double lower=x*x+z*z;
                // Conservative roundoff slack affects pruning only; accepted queries
                // still use the original polygon calculation and exact resolution.
                if(lower>best+Math.Max(1e-12,best*1e-12))continue;
                best=Math.Min(best,polygon.DistanceSquared(point));
                if(best==0)return 0;
            }
            return best==double.MaxValue?double.MaxValue:Math.Sqrt(best);
        }
    }
}
