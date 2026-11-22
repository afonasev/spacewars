using System;
using System.Threading;

namespace Spacewars.Simulation
{
    // Borrow roots pinned by the private, closed frame until Dispatch joins.
    // Workers see copied coordinates and immutable terrain only. Refcounts, the
    // host reservation, HOLD checks and actual trace publication stay on owner.
    internal sealed class NavTranscriptTraceBatch : INavMotionWork, IDisposable
    {
        private struct Query
        {
            internal NavLocation From, SegmentFrom, Reached;
            internal NavPoint Target;
            internal double Cost;
            internal bool Ready;

            internal bool UseSection(NavTypedRoute route,PartitionNavigationProvider provider)
            {
                if(!route.TrySection(provider,From,Target,out Reached))return false;
                SegmentFrom=new NavLocation(From.Position,From.SurfaceId);
                Cost=LayeredNavigationProvider.Distance(From.Position,Target);
                return Ready=true;
            }
            internal void Compute(PartitionNavigationProvider provider,double radius)
            {
                if(Ready)return;
                int count=0;
                NavLocation segmentFrom=default,end=default;
                double cost=0;
                bool valid=provider.PrepareSurfaceTrace(From,Target,radius,(a,b,length,kind,portal)=>{
                    count++;
                    if(kind==NavTypedLegKind.Surface && portal==null){segmentFrom=a;end=b;cost=length;}
                });
                if(!valid || count!=1 || end.SurfaceId==null || !end.Position.Equals(Target))return;
                SegmentFrom=segmentFrom;Reached=end;Cost=cost;Ready=true;
            }
        }

        private struct Row
        {
            internal NavTypedRoute Route;
            internal PartitionNavigationProvider Provider;
            internal Query Chord, Next;
            internal bool HasNext;
            internal double Radius;
        }
        private readonly Row[] rows;
        private readonly IDisposable storage;
        private int cursor=-1;
#if UNITY_INCLUDE_TESTS
        [ThreadStatic] internal static long PublishedLegsForTests, HelperRowsForTests;
        private int helperRows;
#endif
        private NavTranscriptTraceBatch(int count,IDisposable storage)
        {rows=new Row[count];this.storage=storage;}

        internal static NavTranscriptTraceBatch Prepare(NavPreparedCrowdStep frame,INavStorageReservation reservation)
        {
            var executor=NavMotionExecutor.Current;
            if(executor==null || frame.After.Length<2 || !frame.Working.StableSelectClearance)return null;
            int eligible=0;
            for(int i=0;i<frame.After.Length && eligible<2;i++)
                if(frame.RecastRoutes[i]!=null && frame.RecastReceipts[i].Verified && !frame.RecastReceipts[i].ProductionUnmapped)eligible++;
            if(eligible<2)return null;
            long bytes=checked(NavStorageLayout.ObjectBytesBound(typeof(NavTranscriptTraceBatch))+32L+
                (long)frame.After.Length*NavStorageLayout.ValueBytes(typeof(Row)));
            if(!NavStorageScratch.TryReserve(reservation,bytes,out var storage))return null;
            NavTranscriptTraceBatch batch=null;
            try {
                batch=new NavTranscriptTraceBatch(frame.After.Length,storage);
                long scratch=0;int active=0, certified=0;
                for(int i=0;i<batch.rows.Length;i++){
                    var route=frame.RecastRoutes[i];var receipt=frame.RecastReceipts[i];
                    if(route==null || !receipt.Verified || receipt.ProductionUnmapped ||
                        !NavRecastIntervals.CanTraceRemaining(route,reservation,frame.Working))continue;
                    ref var row=ref batch.rows[i];
                    row.Route=route;row.Provider=(PartitionNavigationProvider)route.Legs[0].RecastAuthorityProvider;
                    row.Radius=route.RecastPlan.Spine.Radius;
                    row.Chord.From=frame.Before[i].Location;row.Chord.Target=frame.After[i].Position;
                    var actor=frame.After[i];
                    if(actor.localIndex==actor.localRoute.Length && actor.routeIndex>=0 && actor.routeIndex<actor.route.Length){
                        row.HasNext=true;row.Next.From=actor.Location;row.Next.Target=actor.route[actor.routeIndex];
                    }
                    bool chordReady=row.Chord.UseSection(route,row.Provider);
                    bool nextReady=!row.HasNext || row.Next.UseSection(route,row.Provider);
                    if(chordReady && nextReady){certified++;continue;}
                    scratch=Math.Max(scratch,row.Provider.TraceScratchBound);active++;
                }
                if(active==0 && certified!=0)return batch;
                if(active<2 && certified==0 || !NavStorageScratch.TryReserve(reservation,checked((long)executor.HelperCount*(scratch+512)),out var lanes)){
                    batch.Dispose();return null;
                }
                using(lanes)executor.DispatchWorkers(batch);
#if UNITY_INCLUDE_TESTS
                HelperRowsForTests+=batch.helperRows;
#endif
                return batch;
            } catch {storage.Dispose();throw;}
        }

        void INavMotionWork.RunRows()
        {
#if UNITY_INCLUDE_TESTS
            int executed=0;
#endif
            int i;
            while((i=Interlocked.Increment(ref cursor))<rows.Length){
                ref var row=ref rows[i];
                if(row.Provider==null || row.Chord.Ready && (!row.HasNext || row.Next.Ready))continue;
#if UNITY_INCLUDE_TESTS
                executed++;
#endif
                row.Chord.Compute(row.Provider,row.Radius);
                if(row.HasNext)row.Next.Compute(row.Provider,row.Radius);
            }
#if UNITY_INCLUDE_TESTS
            if(NavMotionExecutor.Current==null)Interlocked.Add(ref helperRows,executed);
#endif
        }

        internal bool TryAppend(int index,bool next,NavTypedRoute route,int cursor,NavLocation from,NavPoint target,
            INavRecastSweepAuthority authority,int entity,INavStorageReservation reservation,NavPagedList<NavTypedLeg> destination,
            out NavLocation reached, NavLocation? expected=null)
        {
            ref var row=ref rows[index];
            ref var query=ref (next?ref row.Next:ref row.Chord);
            reached=default;
            if(!query.Ready || !ReferenceEquals(row.Route,route) || cursor<0 || cursor>=route.Legs.Count ||
                route.Legs[cursor].From.SurfaceId!=from.SurfaceId || !NavTypedLeg.SamePhysicalLocation(query.From,from) ||
                !SamePoint(query.From.Position,from.Position) || !SamePoint(query.Target,target) ||
                (expected.HasValue && !query.Reached.Equals(expected.Value)) ||
                !authority.AllowsRecastSweep(entity,from,target,row.Radius))return false;
            // No portal occurred anywhere along this qualified traversal. The
            // ordered portal cursor therefore remains at its input position.
            using var births=reservation is INavStorageLocalBirthAllocator?new NavStorageLocalBirths(reservation):null;
            var leg=NavTypedLeg.CreateOwned(query.SegmentFrom,query.Reached,query.Cost,NavTypedLegKind.Surface,null,
                births??reservation,row.Provider);
            try{destination.Add(leg);}finally{leg.ReleaseStorage();}
#if UNITY_INCLUDE_TESTS
            PublishedLegsForTests++;
#endif
            reached=query.Reached;return true;
        }
        private static bool SamePoint(NavPoint a,NavPoint b)=>
            BitConverter.DoubleToInt64Bits(a.X)==BitConverter.DoubleToInt64Bits(b.X) &&
            BitConverter.DoubleToInt64Bits(a.Z)==BitConverter.DoubleToInt64Bits(b.Z);
        public void Dispose()=>storage.Dispose();
    }
}
