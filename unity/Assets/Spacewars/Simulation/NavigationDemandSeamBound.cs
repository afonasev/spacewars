using System;
using System.Linq;
namespace Spacewars.Simulation
{
    internal sealed partial class NavDemandQueryWork
    {
        // Endpoint-specific, owned query state. Never shared in the catalog.
        // Two corners per admitted seam, in the provider's canonical full order.
        private NavPagedList<NavPoint> seamBoxes;
        private NavCowDictionary<int,double> nodeBounds;
        private int seamCursor,boundNode=-1,boundCursor;
        private double boundMinimum=double.PositiveInfinity;
        private bool BoundBusy=>request.DemandQueryV4&&(seamCursor<Provider.Seams.Count||boundNode>=0);
        private long SeamBoundBytes=>(seamBoxes?.CommittedStorageBytes(16)??0)+(nodeBounds?.CommittedStorageBytes(4)??0);
        private long SeamBoundGrowth=>request.DemandQueryV4?2*seamBoxes.AppendGrowthBytes(16)+nodeBounds.MutationGrowthBytes(4):0;
        private void InitializeSeamBound()
        {if(request.DemandQueryV4){seamBoxes=NavPagedList<NavPoint>.Create(reserve);nodeBounds=NavCowDictionary<int,double>.Create(reserve:reserve);}}
        private void ForkSeamBound(NavDemandQueryWork f,Func<long,bool> admission)
        {
            if(!request.DemandQueryV4)return;
            f.seamCursor=seamCursor;f.boundNode=boundNode;f.boundCursor=boundCursor;f.boundMinimum=boundMinimum;
            f.seamBoxes=seamBoxes.Fork(admission);f.nodeBounds=nodeBounds.ForkPrepared(admission);
        }
        private bool TargetSeam(NavPartitionSeam seam)=>seam.Allowed&&(seam.FromSurface==request.Subscription.AssignedEndpoint.SurfaceId||seam.ToSurface==request.Subscription.AssignedEndpoint.SurfaceId);
        internal static void SeamBox(NavPartitionSeam seam,out NavPoint lo,out NavPoint hi)
        {
            // Incident.NearPoint admits only this expanded axis-aligned box.
            // Extra rounding expansion covers the addition/subtraction in both
            // NearPoint and this construction, including large coordinates.
            double scale=Math.Max(1,Math.Max(Math.Max(Math.Abs(seam.From.X),Math.Abs(seam.From.Z)),Math.Max(Math.Abs(seam.To.X),Math.Abs(seam.To.Z))));
            double reach=TerrainLocations.IncidentBoxReach+8*scale*2.2204460492503131e-16;
            lo=new NavPoint(Math.Min(seam.From.X,seam.To.X)-reach,Math.Min(seam.From.Z,seam.To.Z)-reach);
            hi=new NavPoint(Math.Max(seam.From.X,seam.To.X)+reach,Math.Max(seam.From.Z,seam.To.Z)+reach);
        }
        internal static double SeamLowerBound(NavPoint node,NavPoint endpoint,NavPoint lo,NavPoint hi)
        {
            var p=new NavPoint(Math.Max(lo.X,Math.Min(hi.X,node.X)),Math.Max(lo.Z,Math.Min(hi.Z,node.Z)));
            return Math.Abs(node.X-p.X)+Math.Abs(node.Z-p.Z)+LayeredNavigationProvider.Distance(p,endpoint);
        }
        private double BaseBound(int node)=>TypedLowerBound(locations[node].Position,request.Subscription.AssignedEndpoint.Position,Provider.DemandSurfaceBounds(request.Subscription.AssignedEndpoint.SurfaceId));
        private bool EnsureBound(int node)
        {
            if(!request.DemandQueryV4||nodeBounds.ContainsKey(node))return true;
            if(BoundBusy)throw new InvalidOperationException("Bound lane must finish before connector resumes.");
            boundNode=node;boundCursor=0;boundMinimum=double.PositiveInfinity;return false;
        }
        private void StepSeamBound()
        {
            if(seamCursor<Provider.Seams.Count){
                var seam=Provider.Seams[seamCursor];
                if(TargetSeam(seam)){SeamBox(seam,out var lo,out var hi);seamBoxes.Add(lo);seamBoxes.Add(hi);}
                seamCursor++;return;
            }
            // Exactly one box per Bookkeeping operation. Target-surface and
            // missing-index fallbacks each take one constant-cost operation.
            var at=locations[boundNode];bool target=at.SurfaceId==request.Subscription.AssignedEndpoint.SurfaceId;
            if(!target&&boundCursor<seamBoxes.Count){boundMinimum=Math.Min(boundMinimum,SeamLowerBound(at.Position,request.Subscription.AssignedEndpoint.Position,seamBoxes[boundCursor],seamBoxes[boundCursor+1]));boundCursor+=2;}
            if(!target&&boundCursor<seamBoxes.Count)return;
            double value=target?LayeredNavigationProvider.Distance(at.Position,request.Subscription.AssignedEndpoint.Position):double.IsPositiveInfinity(boundMinimum)?BaseBound(boundNode):Math.Max(BaseBound(boundNode),boundMinimum);
            nodeBounds[boundNode]=value;boundNode=-1;boundCursor=0;boundMinimum=double.PositiveInfinity;
        }
        private double ConservativeSeamPriority(double cost,double h,double legacyMargin)
        {
            // Existing relative budget remains intact. Absolute subtraction
            // handles cancellation for close points at large coordinates.
            // A seam evaluation uses <= 16 arithmetic/sqrt operations. A path
            // has <= N cardinal arcs with <= 2P+1 trace legs each. 64 eps per
            // leg at coordinate scale bounds their absolute error plus bound,
            // box and g+h arithmetic; min/max do not accumulate roundoff.
            // Restore uses the same expression, without search/re-preparation.
            double scale=Math.Max(1,Math.Max(Provider.HalfExtent,Math.Max(Math.Abs(request.Subscription.AssignedEndpoint.Position.X),Math.Abs(request.Subscription.AssignedEndpoint.Position.Z))));
            double absolute=64*(locations.Count+4.0)*(2*Provider.TracePortalBound+4.0)*scale*2.2204460492503131e-16;
            return Math.Max(0,(cost+h)*(1-legacyMargin)-absolute);
        }
        internal long SeamContinuationWireBytes=>(request.CertifiedGroupCorridor?BoundedScalarBytes+128L+(corridorSource?.SeamContinuationWireBytes??0):0)+(request.DemandQueryV5?checked((lazyStore==null?0:8L*lazyStore.Cells.Count)+1024L+276L*exactDistance.Count+276L*exactFrontier.Count+24L*previous.Count):request.DemandQueryV4?64+16L*seamBoxes.Count+12L*nodeBounds.Count:0);
        internal void WriteSeamContinuation(System.IO.BinaryWriter w)
        {
            if(request.CertifiedGroupCorridor){WriteBounded(w);w.Write(corridorSource!=null);if(corridorSource!=null){w.Write(corridorSource.request.ArtifactKey);corridorSource.WriteSeamContinuation(w);}}
            if(request.DemandQueryV5){w.Write(request.TerminalCostProof?"terminal-cost-v13":request.OriginShadowProof?"origin-shadow-v12":request.TerminalCornerProof?"terminal-corner-v11":request.TerminalRectangleProof?"terminal-rectangle-v10":request.RectangularOrigins?"rectangle-origins-v9":request.CertifiedLazyPartition?"certified-lazy-partition-v8":request.LazyPartition?"lazy-partition-v7":request.DemandQueryV6?"exact-astar-v6":"exact-cost-v5");WriteScalars(w);if(request.LazyPartition){if(request.CertifiedLazyPartition)lazyStore.Exclusion.Write(w);if(request.RectangularOrigins)originRectangles.WriteTranscript(w);w.Write(lazyStore.Cells.Count);foreach(var cell in lazyStore.Cells){w.Write(cell.Key);w.Write(cell.Value);}}w.Write(exactDistance.Count);foreach(var pair in exactDistance){w.Write(pair.Key);pair.Value.Write(w);}w.Write(exactFrontier.Count);foreach(var item in exactFrontier)item.Write(w);w.Write(previous.Count);foreach(var pair in previous){w.Write(pair.Key);w.Write(pair.Value.From);w.Write(pair.Value.To);w.Write(pair.Value.Cost);}return;}
            if(!request.DemandQueryV4)return;
            w.Write("target-seam-bound-v4");w.Write(seamCursor);w.Write(boundNode);w.Write(boundCursor);w.Write(boundMinimum);
            w.Write(seamBoxes.Count);foreach(var point in seamBoxes){w.Write(point.X);w.Write(point.Z);}
            w.Write(nodeBounds.Count);foreach(var pair in nodeBounds){w.Write(pair.Key);w.Write(pair.Value);}
        }
        private void WriteSeamBody(NavPhysicalBodyWriter w)
        {
            w.Output.Write(seamCursor);w.Output.Write(boundNode);w.Output.Write(boundCursor);w.Output.Write(boundMinimum);
            NavPhysicalMacro.WriteEdge(w,this,seamBoxes);NavPhysicalMacro.WriteEdge(w,this,nodeBounds);
        }
        private void ReadSeamBody(NavPhysicalBodyReader r)
        {
            seamCursor=r.Input.ReadInt32();boundNode=r.Input.ReadInt32();boundCursor=r.Input.ReadInt32();boundMinimum=r.Input.ReadDouble();
            seamBoxes=NavPhysicalMacro.Acquire(this,NavPhysicalMacro.ReadEdge<NavPagedList<NavPoint>>(r));nodeBounds=NavPhysicalMacro.Acquire(this,NavPhysicalMacro.ReadEdge<NavCowDictionary<int,double>>(r));
        }
        private void RestoreSeamBound(Checkpoint state)
        {
            if(!request.DemandQueryV4)return;
            if(state.SeamBoxes==null||state.NodeBounds==null)throw new ArgumentException("Missing logical seam lane.");
            seamCursor=state.SeamCursor;boundNode=state.BoundNode;boundCursor=state.BoundCursor;boundMinimum=state.BoundMinimum;
            seamBoxes.AddRange(state.SeamBoxes);seamBoxes.RestoreDirectoryCapacity(state.SeamDirectory,checked(2*Provider.Seams.Count),seamBoxes.Count);
            foreach(var pair in state.NodeBounds)nodeBounds.Add(pair.Key,pair.Value);
        }
        private double ValidateNodeBound(int node,int limit,out double minimum)
        {
            minimum=double.PositiveInfinity;var at=locations[node];
            for(int i=0;i<limit;i+=2)minimum=Math.Min(minimum,SeamLowerBound(at.Position,request.Subscription.AssignedEndpoint.Position,seamBoxes[i],seamBoxes[i+1]));
            return at.SurfaceId==request.Subscription.AssignedEndpoint.SurfaceId?LayeredNavigationProvider.Distance(at.Position,request.Subscription.AssignedEndpoint.Position):double.IsPositiveInfinity(minimum)?BaseBound(node):Math.Max(BaseBound(node),minimum);
        }
        private void ValidateSeamBound()
        {
            if(!request.DemandQueryV4)return;
            if(seamBoxes==null||nodeBounds==null||seamCursor<0||seamCursor>Provider.Seams.Count||seamBoxes.Count%2!=0||boundNode< -1||boundNode>=locations.Count||boundCursor<0||boundCursor>seamBoxes.Count||boundCursor%2!=0||nodeBounds.Count>locations.Count)throw new ArgumentException("Seam lane dimensions mismatch.");
            int cursor=0;for(int i=0;i<seamCursor;i++)if(TargetSeam(Provider.Seams[i])){SeamBox(Provider.Seams[i],out var lo,out var hi);if(cursor+2>seamBoxes.Count||!lo.Equals(seamBoxes[cursor++])||!hi.Equals(seamBoxes[cursor++]))throw new ArgumentException("Seam prefix box mismatch.");}
            if(cursor!=seamBoxes.Count||seamCursor<Provider.Seams.Count&&(sample!=0&&(sample!=width*width||indexedCount!=locations.Count)||distance.Count!=0||boundNode>=0||nodeBounds.Count!=0)||boundNode<0&&(boundCursor!=0||!double.IsPositiveInfinity(boundMinimum))||boundNode>=0&&(nodeBounds.ContainsKey(boundNode)||seamCursor!=Provider.Seams.Count||phase!=Phase.Seed&&!(phase==Phase.Edges&&pending&&pendingArc.To==boundNode)))throw new ArgumentException("Seam lane phase/prefix mismatch.");
            if(boundNode>=0){ValidateNodeBound(boundNode,boundCursor,out double min);if(min!=boundMinimum)throw new ArgumentException("Seam reduction prefix mismatch.");}
            foreach(var pair in nodeBounds)if(pair.Key<0||pair.Key>=locations.Count||pair.Value!=ValidateNodeBound(pair.Key,seamBoxes.Count,out _))throw new ArgumentException("Seam cached bound mismatch.");
            foreach(var pair in distance)if(!nodeBounds.ContainsKey(pair.Key))throw new ArgumentException("Missing paid node bound.");
        }
    }
}
