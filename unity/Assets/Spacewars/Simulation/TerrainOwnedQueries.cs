using System;
using System.Collections.Generic;
namespace Spacewars.Simulation
{
    internal sealed partial class TerrainLocations
    {
        // Derived immutable acceleration belongs exclusively to this admitted
        // terrain facade. It stores integer selectors, never borrowed geometry.
        // BODY reconstructs it from the validated declarations under the same charge.
        private OwnedQueries ownedQueries;
        internal bool HasOwnedQueries=>ownedQueries!=null;
        internal long OwnedQueryRetainedBytes=>ownedQueries?.Bytes??0;
#if UNITY_INCLUDE_TESTS
        [ThreadStatic] internal static bool DisableOwnedQueriesForTests;
        [ThreadStatic] internal static bool DisableOwnedIncidentForTests;
        [ThreadStatic] internal static long IncidentCandidatesForTests;
#endif
        private bool UseOwnedQueries {
            get {
#if UNITY_INCLUDE_TESTS
                if(DisableOwnedQueriesForTests||DisableUnindexedBoundaryPruningForTests)return false;
#endif
                return ownedQueries!=null&&geometry.HalfExtent*(64*2.2204460492503131e-16)<=1e-9;
            }
        }
        private sealed class OwnedQueries
        {
            internal readonly NavQueryGrid Supports,Edges,Boundaries;
            internal long Bytes=>NavStorageLayout.ObjectBytesBound(typeof(OwnedQueries))+Supports.Bytes+Edges.Bytes+Boundaries.Bytes;
            internal OwnedQueries(TerrainLocations t){Supports=new NavQueryGrid(t.geometry.HalfExtent,t.regions.Length,i=>SupportBox(t.regions[i]));Edges=new NavQueryGrid(t.geometry.HalfExtent,t.edges.Length,i=>new NavQueryBox(t.edges[i].Item1,t.edges[i].Item2));Boundaries=new NavQueryGrid(t.geometry.HalfExtent,t.exterior.Count+t.seams.Count,i=>BoundaryBox(t.exterior,t.seams,i));}
        }
        private static NavQueryBox SupportBox(MapSupport s)=>new NavQueryBox(new NavPoint(s.Bounds.MinX,s.Bounds.MinZ),new NavPoint(s.Bounds.MaxX,s.Bounds.MaxZ));
        private static NavQueryBox BoundaryBox(IReadOnlyList<Seam> exterior,IReadOnlyList<Seam> seams,int i){var e=i<exterior.Count?exterior[i]:seams[i-exterior.Count];return new NavQueryBox(e.From,e.To);}
        private static long MeasureOwnedQueries(double half,IReadOnlyList<MapSupport> supports,Tuple<NavPoint,NavPoint>[] edges,IReadOnlyList<Seam> exterior,IReadOnlyList<Seam> seams)
            =>checked(NavStorageLayout.ObjectBytesBound(typeof(OwnedQueries))+NavQueryGrid.Measure(half,supports.Count,i=>SupportBox(supports[i]))+NavQueryGrid.Measure(half,edges.Length,i=>new NavQueryBox(edges[i].Item1,edges[i].Item2))+NavQueryGrid.Measure(half,exterior.Count+seams.Count,i=>BoundaryBox(exterior,seams,i)));
        private long OwnedQueriesBytes()=>MeasureOwnedQueries(geometry.HalfExtent,regions,edges,exterior,seams);
        private long PhysicalLayoutBytes=>512+(ownedQueries?.Bytes??0);
        private void InitializeOwnedQueries(){ownedQueries=new OwnedQueries(this);if(owned.StorageBytes!=NavStorageLayout.Charge(PhysicalLayoutBytes))throw new ArgumentException("Owned terrain query charge contradiction.");}
        private bool CanUseOwnedIncident {
            get {
#if UNITY_INCLUDE_TESTS
                if(DisableOwnedIncidentForTests)return false;
#endif
                return UseOwnedQueries&&Seams.Count<=4096;
            }
        }
        private bool IncidentMatches(NavPartitionSeam seam,NavPoint point)
        {
#if UNITY_INCLUDE_TESTS
            IncidentCandidatesForTests++;
#endif
            return seam.Allowed&&NearPoint(point,IncidentTolerance,seam.From,seam.To)&&Distance2(point,seam.From,seam.To)<=1e-14;
        }
        private void AppendOwnedIncident(NavPoint point,List<NavPartitionSeam> result)
        {
            // The paid boundary grid contains exterior edges followed by seams.
            // Physical arrangement validation binds each private/public seam at
            // the same index. Deduplicate bucket hits and retain source order.
            // Exact incident predicates below still decide admission.
            Span<ulong> seen=stackalloc ulong[(Seams.Count+63)/64];seen.Clear();
            double reach=IncidentBoxReach;
            foreach(int boundary in ownedQueries.Boundaries.Candidates(point.X-reach,point.Z-reach,point.X+reach,point.Z+reach)){
                int index=boundary-exterior.Count;if(index>=0)seen[index>>6]|=1UL<<(index&63);
            }
            for(int word=0;word<seen.Length;word++){
                ulong pending=seen[word];while(pending!=0){int index=word*64+NavQueryGrid.LowestSetBit(pending);var seam=Seams[index];
                    if(IncidentMatches(seam,point))result.Add(seam);pending&=pending-1;
                }
            }
        }
        private bool OwnedBoundaryBlocked(NavPoint from,NavPoint to,double radius,double r2,bool point)
        {
            double reach=radius+1e-9;
            foreach(int i in ownedQueries.Boundaries.Candidates(Math.Min(from.X,to.X)-reach,Math.Min(from.Z,to.Z)-reach,Math.Max(from.X,to.X)+reach,Math.Max(from.Z,to.Z)+reach)){
                var edge=i<exterior.Count?exterior[i]:seams[i-exterior.Count];if(i>=exterior.Count&&edge.Allowed)continue;
                if(point?UnindexedPointBoundaryHit(from,radius,r2,edge):UnindexedSegmentBoundaryHit(from,to,radius,r2,edge))return true;
            }
            return false;
        }
        private IReadOnlyList<double> OwnedIndexedCuts(NavPoint a,NavPoint b)
        {
            if(edges.Length>4096)return Cuts(a,b,edges);
            double dx=b.X-a.X,dz=b.Z-a.Z,length=dx*dx+dz*dz;if(length==0)return UncutSweep;
            List<double> cuts=null;
            double margin=1e-7+(1e-10+1e-12)/Math.Sqrt(length),reach=margin+1e-9;
            Span<ulong> seen=stackalloc ulong[(edges.Length+63)/64];seen.Clear();
            foreach(int i in ownedQueries.Edges.Candidates(Math.Min(a.X,b.X)-reach,Math.Min(a.Z,b.Z)-reach,Math.Max(a.X,b.X)+reach,Math.Max(a.Z,b.Z)+reach))seen[i>>6]|=1UL<<(i&63);
            // Original source order is required for bit-exact cut/trace results.
            for(int word=0;word<seen.Length;word++){ulong pending=seen[word];while(pending!=0){int i=word*64+LowestSetBit(pending);AddCuts(ref cuts,a,b,dx,dz,length,margin,edges[i]);pending&=pending-1;}}
            return SortUniqueCuts(cuts);
        }
    }
}
