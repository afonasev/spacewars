using System;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Authored presentation accents only. These lines never feed navigation or ownership.
    public static class FoundryRouteFinish
    {
        public static NavPoint[][] Connections(FoundryMap map)
        {
            // West pocket opens toward the center. East pocket's front wall requires
            // the existing outer approach; never paint a shortcut through that wall.
            var paths=new NavPoint[4][];int i=0;
            foreach(int end in new[]{-1,1})
            {
                paths[i++]=new[]{map.Point(0,end*35),map.Point(-44,end*35),map.Point(-44,end*70)};
                paths[i++]=new[]{map.Point(0,end*35),map.Point(58,end*35),map.Point(58,end*70),map.Point(38,end*70)};
            }
            return paths;
        }
        public static Vector2 Coverage(FoundryMap map,NavPoint point,FoundrySurfaceProfile settings,NavPoint[][] paths)
        {
            if(!map.SupportsFootprint(point,0))return Vector2.zero;
            var support=map.SupportAt(point);float climb=0;
            if(support.Id=="flank-ramp"||support.Id=="rear-flank-ramp")
                climb=Mathf.SmoothStep(0,1,(float)Math.Sqrt(support.Bounds.BoundaryDistanceSquared(point))/settings.routeEdgeFade);
            double distance=double.MaxValue;
            foreach(var path in paths)for(int i=1;i<path.Length;i++)
            {
                var a=path[i-1];var b=path[i];double dx=b.X-a.X,dz=b.Z-a.Z;
                double t=Math.Max(0,Math.Min(1,((point.X-a.X)*dx+(point.Z-a.Z)*dz)/(dx*dx+dz*dz)));
                double x=point.X-a.X-t*dx,z=point.Z-a.Z-t*dz;distance=Math.Min(distance,Math.Sqrt(x*x+z*z));
            }
            float connection=1-Mathf.SmoothStep(0,1,((float)distance-settings.connectionWidth*.5f+settings.routeEdgeFade)/settings.routeEdgeFade);
            return new Vector2(climb,connection);
        }
    }
}
