using System;
using System.IO;
namespace Spacewars.Simulation
{
    internal struct NavHeuristicBox : IEquatable<NavHeuristicBox>
    {
        internal double MinX,MinZ,MaxX,MaxZ;
        internal NavHeuristicBox(NavObstacle bounds){MinX=bounds.MinX;MinZ=bounds.MinZ;MaxX=bounds.MaxX;MaxZ=bounds.MaxZ;}
        internal void Include(NavPoint p){MinX=Math.Min(MinX,p.X);MinZ=Math.Min(MinZ,p.Z);MaxX=Math.Max(MaxX,p.X);MaxZ=Math.Max(MaxZ,p.Z);}
        internal bool Contains(NavPoint p)=>p.X>=MinX&&p.X<=MaxX&&p.Z>=MinZ&&p.Z<=MaxZ;
        public bool Equals(NavHeuristicBox b)=>MinX==b.MinX&&MinZ==b.MinZ&&MaxX==b.MaxX&&MaxZ==b.MaxZ;
        internal void Write(BinaryWriter w){w.Write(MinX);w.Write(MinZ);w.Write(MaxX);w.Write(MaxZ);}
        internal static NavHeuristicBox Read(BinaryReader r)=>new NavHeuristicBox{MinX=r.ReadDouble(),MinZ=r.ReadDouble(),MaxX=r.ReadDouble(),MaxZ=r.ReadDouble()};
    }
    internal static class NavExactHeuristic
    {
        internal static double Power(int e)=>BitConverter.Int64BitsToDouble((long)(e+1023)<<52);
        internal static bool Coordinate(double v)=>!double.IsNaN(v)&&!double.IsInfinity(v)&&Math.Abs(v)<=Power(200);
        internal static bool Coordinate(NavPoint p)=>Coordinate(p.X)&&Coordinate(p.Z);
        internal static bool Domain(double grid,double extent,int min,int width,int portals,NavPoint origin,NavPoint endpoint,NavHeuristicBox box,NavDistanceMetricContract contract)
        {
            // All surfaces cost1 by sealed Partition constructor/owned decoder.
            // Grid samples are monotone k*G; checking both extremes covers all
            // catalog nodes, including borrowed catalogs without a free scan.
            return contract==NavDistanceMetricContract.ManagedBinary64SqrtV1&&grid>=Power(-200)&&grid<=Power(199)&&extent>0&&extent<=Power(199)&&portals>=0&&portals<=1048576&&width>0&&Math.Abs((long)min)<(1L<<29)&&Math.Abs((long)min+width-1)<(1L<<29)&&Coordinate(min*grid)&&Coordinate(((long)min+width-1)*grid)&&Coordinate(origin)&&Coordinate(endpoint)&&Coordinate(box.MinX)&&Coordinate(box.MinZ)&&Coordinate(box.MaxX)&&Coordinate(box.MaxZ)&&box.MinX<=box.MaxX&&box.MinZ<=box.MaxZ;
        }
        internal static NavExactCost Geometry(NavPoint x,NavPoint endpoint,NavHeuristicBox box)
        {
            double px=Math.Max(box.MinX,Math.Min(box.MaxX,x.X)),pz=Math.Max(box.MinZ,Math.Min(box.MaxZ,x.Z));
            var dx=NavExactCost.Difference(x.X,px);var dz=NavExactCost.Difference(x.Z,pz);
            var ex=NavExactCost.Difference(px,endpoint.X);var ez=NavExactCost.Difference(pz,endpoint.Z);
            return dx.Add(dz).Add(ex>=ez?ex:ez);
        }
        internal static bool TryTerminalBound(NavPoint x,NavPoint endpoint,bool eligible,NavDistanceMetricContract metric,out NavExactCost lower)
        {
            lower=default;if(!eligible||metric!=NavDistanceMetricContract.ManagedBinary64SqrtV1||!Coordinate(x)||!Coordinate(endpoint))return false;
            lower=NavExactCost.ConservativeHeuristicScale(NavExactCost.FromDouble(LayeredNavigationProvider.Distance(x,endpoint)));return true;
        }
        internal static NavExactCost Value(NavPoint x,NavPoint endpoint,NavHeuristicBox box)=>NavExactCost.ConservativeHeuristicScale(Geometry(x,endpoint,box));
    }
    internal sealed partial class NavDemandQueryWork
    {
        // V6 state only, 54 wire bytes. No endpoint-specific shared catalog data.
        private NavHeuristicBox hBox;
        private int hStage,hCandidateCursor,hNext=-1,hLast=-1;
        private bool hShared,hEligible;
        private NavDistanceMetricContract hContract;
        internal bool HeuristicActive=>request.DemandQueryV6&&hStage==3&&hEligible;
        internal NavDistanceMetricContract EffectiveMetricContract=>hContract;
        private bool HeuristicReady=>!request.DemandQueryV6||hStage==3;
        private bool HeuristicCanBookkeeping=>request.DemandQueryV6&&hStage<3&&(hStage<2||hShared||sample==width*width&&indexedCount==locations.Count);
        private void InitializeHeuristic(NavDemandQueryCatalog shared){if(request.DemandQueryV6)hShared=shared!=null;}
        private void ForkHeuristic(NavDemandQueryWork f){if(!request.DemandQueryV6)return;f.hBox=hBox;f.hStage=hStage;f.hCandidateCursor=hCandidateCursor;f.hNext=hNext;f.hLast=hLast;f.hShared=hShared;f.hEligible=hEligible;f.hContract=hContract;ForkTerminalRectangle(f);ForkOriginShadow(f);}
        private bool HeuristicDomain(NavHeuristicBox raw)=>NavExactHeuristic.Domain(request.Profile.GridCell,Provider.HalfExtent,min,width,Provider.TracePortalBound,request.Subscription.Origin.Position,request.Subscription.AssignedEndpoint.Position,raw,request.MetricContract);
        private void FoldHeuristicNode(int id)
        {var at=locations[id];hEligible&=NavExactHeuristic.Coordinate(at.Position);if(at.SurfaceId==request.Subscription.AssignedEndpoint.SurfaceId)hBox.Include(at.Position);}
        private void StepHeuristicPreparation()
        {
            if(!request.DemandQueryV6||hStage>=3)throw new InvalidOperationException("No paid heuristic preparation.");
            if(hStage==0){hBox=new NavHeuristicBox(Provider.DemandSurfaceBounds(request.Subscription.AssignedEndpoint.SurfaceId));hEligible=HeuristicDomain(hBox);hBox.Include(request.Subscription.AssignedEndpoint.Position);hContract=request.MetricContract;hNext=hShared?IndexedSlot(surfaceHeads,Provider.DemandSurfaceOrdinal(request.Subscription.AssignedEndpoint.SurfaceId))-1:-1;hStage=1;return;}
            if(hStage==1){if(hCandidateCursor<request.Region.Candidates.Count){var p=request.Region.Candidates[hCandidateCursor++].Position;hEligible&=NavExactHeuristic.Coordinate(p);hBox.Include(p);FoldTerminalCandidate(p);}if(hCandidateCursor==request.Region.Candidates.Count)hStage=2;return;}
            if(hShared&&hNext>=0){int id=hNext;FoldHeuristicNode(id);hLast=id;hNext=IndexedSlot(surfaceNext,id)-1;return;}
            if(sample!=width*width||indexedCount!=locations.Count)throw new InvalidOperationException("Heuristic frozen before full catalog.");
            hStage=3;
        }
        private NavExactCost NodeHeuristic(int node)
        {if(!request.DemandQueryV6)return default;if(!HeuristicReady)throw new InvalidOperationException("Heuristic priority before paid freeze.");return hEligible?NavExactHeuristic.Value(NodeLocation(node).Position,request.Subscription.AssignedEndpoint.Position,hBox):default;}
        private void WriteHeuristic(BinaryWriter w)
        {w.Write(hStage);w.Write(hCandidateCursor);w.Write(hNext);w.Write(hLast);w.Write(hShared);w.Write(hEligible);w.Write((int)hContract);hBox.Write(w);}
        private void ReadHeuristic(BinaryReader r)
        {hStage=r.ReadInt32();hCandidateCursor=r.ReadInt32();hNext=r.ReadInt32();hLast=r.ReadInt32();byte shared=r.ReadByte(),eligible=r.ReadByte();if(shared>1||eligible>1)throw new ArgumentException("Noncanonical heuristic flag.");hShared=shared==1;hEligible=eligible==1;hContract=(NavDistanceMetricContract)r.ReadInt32();hBox=NavHeuristicBox.Read(r);}
        // Logical DTO metadata is detached while its source is live, then
        // rebound to the caller's canonical provider. It must not retain a
        // released owned recipe/provider merely to restore a shared prefix.
        internal static NavScheduledRequest DetachSharedRecipe(NavScheduledRequest r,ILayeredNavigationProvider provider)
        {
            var c=r.Clearance;var p=r.Profile;var q=r.Subscription;
            var clearance=new NavClearanceProfile(c.Id,c.Revision,c.MobilityClass,c.MobilityRevision,c.Radius);var profile=new NavGraphProfile(p.ExactProfileBinding,p.CostId,p.CostRevision,p.GridCell);var limits=new NavGraphCompileLimits(r.Limits.MaxSamples,r.Limits.MaxNodes,r.Limits.MaxArcs);
            var region=new NavTerminalRegion(r.Region.Id,r.Region.Revision,r.Region.Requested.Detached(),System.Linq.Enumerable.Select(r.Region.Candidates,at=>at.Detached()));
            var sub=new NavRouteSubscription(q.WorldGeneration,q.GroupId,q.RootOrderRevision,q.CommandSequence,q.IssuedTick,q.ActivationRevision,q.EntityId,q.Incarnation,q.MemberOrderRevision,q.MobilityRevision,q.RequestSequence,q.Origin.Detached(),q.AssignedEndpoint.Detached(),q.HoldBinding,q.Provenance);
            return new NavScheduledRequest(provider,clearance,profile,limits,region,sub,r.MaxLabels,r.Singleton,r.Query,r.GroupQuery,r.DemandQuery,r.DemandQueryV2,r.DemandQueryV3,r.DemandQueryV4,r.DemandQueryV5,r.DemandQueryV6,r.LazyPartition,r.CertifiedLazyPartition,r.RectangularOrigins,r.TerminalRectangleProof,r.TerminalCornerProof,r.OriginShadowProof,r.TerminalCostProof,r.CertifiedGroupCorridor);
        }
        private void ValidateHeuristic()
        {
            if(!request.DemandQueryV6)return;
            if(hStage<0||hStage>3||hCandidateCursor<0||hCandidateCursor>request.Region.Candidates.Count||hShared!=(catalog!=null)||hLast< -1||hLast>=locations.Count||hNext< -1||hNext>=locations.Count||hStage<2&&hLast!=-1||hStage>=2&&hCandidateCursor!=request.Region.Candidates.Count||phase!=Phase.Catalog&&Status==NavSolveStatus.Pending&&hStage!=3)throw new ArgumentException("Heuristic phase/domain dimensions mismatch.");
            if(hStage==0){if(hCandidateCursor!=0||hNext!=-1||hLast!=-1||hEligible||hContract!=NavDistanceMetricContract.Unknown||!hBox.Equals(default)||indexedCount!=0&&!hShared)throw new ArgumentException("Heuristic pre-initialization payload.");return;}
            var box=new NavHeuristicBox(Provider.DemandSurfaceBounds(request.Subscription.AssignedEndpoint.SurfaceId));bool eligible=HeuristicDomain(box);box.Include(request.Subscription.AssignedEndpoint.Position);
            for(int n=0;n<hCandidateCursor;n++){var p=request.Region.Candidates[n].Position;eligible&=NavExactHeuristic.Coordinate(p);box.Include(p);}
            void Node(int n){var at=locations[n];eligible&=NavExactHeuristic.Coordinate(at.Position);if(at.SurfaceId==request.Subscription.AssignedEndpoint.SurfaceId)box.Include(at.Position);}
            if(hShared){int next=IndexedSlot(surfaceHeads,Provider.DemandSurfaceOrdinal(request.Subscription.AssignedEndpoint.SurfaceId))-1,last=-1;
                while(next>=0&&next<=hLast){Node(next);last=next;next=IndexedSlot(surfaceNext,next)-1;}
                if(last!=hLast||next!=hNext||hStage==3&&next!=-1)throw new ArgumentException("Heuristic shared paid prefix mismatch.");
            }else{if(hLast!=-1||hNext!=-1)throw new ArgumentException("Unexpected shared heuristic cursor.");for(int n=0;n<indexedCount;n++)Node(n);}
            if(!box.Equals(hBox)||eligible!=hEligible||hContract!=request.MetricContract||hStage==3&&(sample!=width*width||indexedCount!=locations.Count))throw new ArgumentException("Heuristic hull/eligibility/freeze mismatch.");
        }
    }
}
