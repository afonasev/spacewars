using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    // Source preparation only. The solver still certifies each actual member /
    // endpoint pair, including fallback candidates when preferred slots are blocked.
    internal static class NavigationArrivalSlotOrdering
    {
        internal static IReadOnlyList<NavLocation> Align(IReadOnlyList<NavLocation> candidates,
            IEnumerable<NavArrivalMember> group)
        {
            var members=group.OrderBy(m=>m.Order).ThenBy(m=>m.EntityId).ThenBy(m=>m.Incarnation).ToArray();
            int n=members.Length;
            if(n<2||n>candidates.Count||n>LayeredNavigationScheduler.MaxQueue)return candidates;
            // Euclidean assignment is a planar preference, never a substitute for
            // portal reachability. Leave mixed-surface cohorts to the existing solver.
            string surface=candidates[0].SurfaceId;
            if(members.Any(m=>m.Origin.SurfaceId!=surface)||candidates.Take(n).Any(c=>c.SurfaceId!=surface))return candidates;

            // Shortest augmenting path assignment. Arrays are invocation-local,
            // linear in group size; the enclosing source construction admits scratch.
            // Canonical row/column traversal resolves equal costs deterministically.
            var u=new double[n+1];var v=new double[n+1];var owner=new int[n+1];
            var previous=new int[n+1];var minimum=new double[n+1];var used=new bool[n+1];
            for(int row=1;row<=n;row++){
                owner[0]=row;int column=0;
                Array.Clear(used,0,used.Length);
                for(int k=1;k<=n;k++)minimum[k]=double.PositiveInfinity;
                do{
                    used[column]=true;int current=owner[column],next=0;double delta=double.PositiveInfinity;
                    var origin=members[current-1].Origin.Position;
                    for(int k=1;k<=n;k++)if(!used[k]){
                        var point=candidates[k-1].Position;double dx=origin.X-point.X,dz=origin.Z-point.Z;
                        double cost=Math.Sqrt(dx*dx+dz*dz)-u[current]-v[k];
                        if(cost<minimum[k]){minimum[k]=cost;previous[k]=column;}
                        if(minimum[k]<delta){delta=minimum[k];next=k;}
                    }
                    for(int k=0;k<=n;k++)if(used[k]){u[owner[k]]+=delta;v[k]-=delta;}else if(k>0)minimum[k]-=delta;
                    column=next;
                }while(owner[column]!=0);
                do{int parent=previous[column];owner[column]=owner[parent];column=parent;}while(column!=0);
            }
            var ordered=candidates.ToArray();
            for(int k=1;k<=n;k++)ordered[owner[k]-1]=candidates[k-1];
            return ordered;
        }
    }
}
