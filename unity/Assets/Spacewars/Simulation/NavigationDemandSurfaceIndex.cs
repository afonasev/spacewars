using System;
namespace Spacewars.Simulation
{
    public sealed partial class PartitionNavigationProvider
    {
        internal int DemandSurfaceCount=>locations.SurfaceIndex.Count;
        internal int DemandSurfaceOrdinal(string id)=>locations.SurfaceIndex.TryGetValue(new NavNameKey(id),out int n)?n:throw new ArgumentException("Unknown demand surface.");
        internal NavObstacle DemandSurfaceBounds(string id)
        {var b=bySurface[id].Area.Boundary;return new NavObstacle(b.MinX,b.MinZ,b.MaxX,b.MaxZ);}
    }
    internal sealed partial class NavDemandQueryWork
    {
        private NavPackedSlots surfaceHeads,surfaceTails,surfaceNext;
        private int indexedCount;
        internal int IndexedCount=>indexedCount;
        internal bool CanBookkeeping=>request.LazyPartition?LazyBookkeeping:Status==NavSolveStatus.Pending&&(HeuristicCanBookkeeping||BoundBusy||request.DemandQueryV3&&phase==Phase.Catalog&&indexedCount<locations.Count);
        private int IndexedSlot(NavPackedSlots slots,int n)=>n<slots.CapacitySlots?slots.Get(n):0;
        private int NextOriginNode
        {
            get{int s=Provider.DemandSurfaceOrdinal(request.Subscription.Origin.SurfaceId);return origin==0?IndexedSlot(surfaceHeads,s)-1:IndexedSlot(surfaceNext,origin-1)-1;}
        }
        internal void StepBookkeeping()
        {
            if(frozen||!CanBookkeeping)throw new InvalidOperationException("Unpaid demand index step.");
            if(request.LazyPartition){StepLazyBookkeeping();return;}
            if(HeuristicCanBookkeeping){StepHeuristicPreparation();return;}
            if(BoundBusy){StepSeamBound();return;}
            if(surfaceHeads.CapacitySlots<Provider.DemandSurfaceCount){surfaceHeads.Ensure(surfaceHeads.CapacitySlots+1);surfaceTails.Ensure(surfaceTails.CapacitySlots+1);return;}
            int id=indexedCount,s=Provider.DemandSurfaceOrdinal(locations[id].SurfaceId),tail=surfaceTails.Get(s)-1;surfaceNext.Ensure(id+1);
            if(tail<0)surfaceHeads.Set(s,id+1);else surfaceNext.Set(tail,id+1);surfaceTails.Set(s,id+1);if(request.DemandQueryV6)FoldHeuristicNode(id);indexedCount++;
        }
        private void InitializeSurfaceIndex(NavDemandQueryCatalog shared)
        {
            if(!request.DemandQueryV3)return;
            if(shared!=null){surfaceHeads=shared.Heads;surfaceTails=shared.Tails;surfaceNext=shared.Next;surfaceHeads.Retain();surfaceTails.Retain();surfaceNext.Retain();indexedCount=locations.Count;}
            else{surfaceHeads=NavPackedSlots.Create(reserve);surfaceTails=NavPackedSlots.Create(reserve);surfaceNext=NavPackedSlots.Create(reserve);}
        }
        private void ForkSurfaceIndex(NavDemandQueryWork f,Func<long,bool> admission)
        {
            if(!request.DemandQueryV3)return;f.indexedCount=indexedCount;
            if(sample==width*width&&indexedCount==locations.Count){f.surfaceHeads=surfaceHeads;f.surfaceTails=surfaceTails;f.surfaceNext=surfaceNext;surfaceHeads.Retain();surfaceTails.Retain();surfaceNext.Retain();}
            else{f.surfaceHeads=surfaceHeads.Fork(admission);f.surfaceTails=surfaceTails.Fork(admission);f.surfaceNext=surfaceNext.Fork(admission);}
        }
        internal static void ValidateSurfaceIndex(PartitionNavigationProvider provider,NavPagedList<NavLocation> nodes,int indexed,NavPackedSlots heads,NavPackedSlots tails,NavPackedSlots next)
        {
            if(heads==null||tails==null||next==null||indexed<0||indexed>nodes.Count||nodes.Count-indexed>1||heads.CapacitySlots!=tails.CapacitySlots||heads.CapacitySlots>provider.DemandSurfaceCount+NavPackedSlots.PageSize||indexed>0&&heads.CapacitySlots<provider.DemandSurfaceCount||next.CapacitySlots<indexed||next.CapacitySlots>indexed+NavPackedSlots.PageSize)throw new ArgumentException("Demand index dimensions mismatch.");
            var seen=new bool[indexed];int total=0;
            for(int s=0;s<heads.CapacitySlots;s++){
                int head=heads.Get(s)-1,tail=tails.Get(s)-1;if(s>=provider.DemandSurfaceCount&&(head!=-1||tail!=-1)||(head<0)!=(tail<0))throw new ArgumentException("Demand index surface header mismatch.");
                int last=-1;for(int n=head;n>=0;){if(n<=last||n>=indexed||seen[n]||provider.DemandSurfaceOrdinal(nodes[n].SurfaceId)!=s)throw new ArgumentException("Demand index coverage/order/surface mismatch.");seen[n]=true;total++;last=n;n=next.Get(n)-1;}if(last!=tail)throw new ArgumentException("Demand index tail mismatch.");
            }
            if(total!=indexed)throw new ArgumentException("Demand index missing global IDs.");for(int n=indexed;n<next.CapacitySlots;n++)if(next.Get(n)!=0)throw new ArgumentException("Demand future index payload.");
        }
        internal static double TypedLowerBound(NavPoint node,NavPoint endpoint,NavObstacle b)
        {
            var p=new NavPoint(Math.Max(b.MinX,Math.Min(b.MaxX,node.X)),Math.Max(b.MinZ,Math.Min(b.MaxZ,node.Z)));
            return Math.Abs(node.X-p.X)+Math.Abs(node.Z-p.Z)+LayeredNavigationProvider.Distance(p,endpoint);
        }
        private long SurfaceIndexBytes=>(surfaceHeads?.EstimatedRetainedBytes??0)+(surfaceTails?.EstimatedRetainedBytes??0)+(surfaceNext?.EstimatedRetainedBytes??0);
    }
}
