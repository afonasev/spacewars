using System;
using System.Collections.Generic;
namespace Spacewars.Simulation
{
    // Shared by route providers and live gameplay. Clearance includes arrival error
    // and the local lattice, so parked outer slots leave traversable inner aisles.
    public static class NavArrivalAllocator
    {
        public static NavPoint[] Allocate(NavGeometry geometry,NavigationProfile profile,NavPoint center,int count,IReadOnlyList<NavPoint> occupied=null)
        {
            if(count<0)throw new ArgumentOutOfRangeException("count");
            var result=new List<NavPoint>();
            double spacing=Math.Max(profile.ArrivalSlotSpacing,4*profile.Radius+2*profile.ArrivalTolerance+2*profile.LocalGridCell);
            int limit=(int)Math.Ceiling(geometry.HalfExtent*2/spacing);
            // Spiral shells keep small groups close to the requested center and make
            // the result stable when reinforcement count grows. Geometry determines a
            // finite work bound; route providers still validate actual connectivity.
            for(int ring=0;ring<=limit&&result.Count<count;ring++)
                for(int z=-ring;z<=ring&&result.Count<count;z++)for(int x=-ring;x<=ring&&result.Count<count;x++){
                    if(ring>0&&Math.Abs(x)!=ring&&Math.Abs(z)!=ring)continue;
                    var point=new NavPoint(center.X+x*spacing,center.Z+z*spacing);
                    if(!geometry.IsFree(point,profile.Radius)||!Separated(point,result,spacing)||!Separated(point,occupied,spacing))continue;
                    result.Add(point);
                }
            return result.ToArray();
        }
        private static bool Separated(NavPoint point,IReadOnlyList<NavPoint> others,double spacing)
        {
            if(others==null)return true;
            for(int i=0;i<others.Count;i++){double x=point.X-others[i].X,z=point.Z-others[i].Z;if(x*x+z*z<spacing*spacing-1e-9)return false;}
            return true;
        }
    }
}
