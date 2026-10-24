using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    // An explicitly versioned query catalog, never an incomplete ordinary graph.
    // Samples and origin probes pipeline under independent Graph/Connector quotas.
    internal sealed partial class NavDemandQueryWork : NavOwnedStorage, INavPhysicalBody
    {
        private enum Phase { Catalog, Seed, Pop, Candidates, Edges, Trace, Reconstruct, Done, BoundedReconstruct }
        private NavScheduledRequest request;
        private NavDemandQueryCatalog catalog;
        private PartitionNavigationProvider Provider=>catalog?.Provider??(PartitionNavigationProvider)(lazyStore?.Recipe.Provider??request.Provider);
        private NavPagedList<NavLocation> locations;
        private NavPackedSlots grid;
        private NavCowDictionary<int,double> starts,ends,distance;
        private NavCowDictionary<int,NavGraphArc> previous;
        private NavCowOrderedSet<NavQueueEntry> frontier;
        private NavPagedList<NavGraphArc> chain;
        private NavPagedList<NavTypedLeg> legs;
        private Func<long,bool> reserve;
        private bool frozen,released,pending;
        private Phase phase;
        private int min,width,sample,origin,terminal,seed=-1,current=-1,direction,candidate,chosen=-1,chosenCandidate=-1,traceNode,reconstruct,reconstructLeg;
        private NavGraphArc pendingArc;
        private double best=double.PositiveInfinity,routeCost;
        internal NavSolveStatus Status {get;private set;}=NavSolveStatus.Pending;
        internal NavTypedRoute Route {get;private set;}
        internal int SampleCount=>request.LazyPartition?lazyStore.Points.Count:locations.Count;
        internal int FrontierCount=>request.DemandQueryV5?exactFrontier.Count:frontier.Count;
        internal bool CanGraph=>request.LazyPartition?LazyGraph:!BoundBusy&&Status==NavSolveStatus.Pending&&(phase==Phase.Catalog&&sample<width*width&&(!request.DemandQueryV3||indexedCount==locations.Count)||(phase==Phase.Edges||request.DemandQueryV2&&phase==Phase.Candidates&&candidate==request.Region.Candidates.Count)&&!pending&&direction<4);
        internal bool CanConnector=>request.LazyPartition?LazyConnector:!BoundBusy&&Status==NavSolveStatus.Pending&&!(request.DemandQueryV2&&phase==Phase.Candidates&&candidate==request.Region.Candidates.Count)&&(phase!=Phase.Catalog?phase!=Phase.Edges||pending||direction==4:terminal<2*request.Region.Candidates.Count||(request.DemandQueryV3?NextOriginNode>=0:origin<locations.Count)||sample==width*width&&(!request.DemandQueryV3||indexedCount==locations.Count)&&HeuristicReady);
        internal static bool Eligible(NavScheduledRequest r)
        {
            if(!r.Query||!(r.Provider is PartitionNavigationProvider p))return false;
            double bound=p.HalfExtent/r.Profile.GridCell;
            if(double.IsNaN(bound)||double.IsInfinity(bound)||bound>int.MaxValue/4)return false;
            long w=(long)Math.Floor(bound)-(long)Math.Ceiling(-bound)+1,s=w*w;
            return w>0&&s<=r.Limits.MaxSamples&&s<=r.Limits.MaxNodes&&4*s<=r.Limits.MaxArcs&&s*r.Region.Candidates.Count<=r.MaxLabels;
        }
        internal NavDemandQueryWork(NavScheduledRequest request,Func<long,bool> reserve=null,bool initialize=true,bool birthOnly=false,NavDemandQueryCatalog shared=null,NavLazyPartitionStore lazyShared=null):base(2048)
        {
            if(!request.DemandQuery||!(request.CertifiedLazyPartition?CertifiedEligible(request):request.LazyPartition?LazyEligible(request):Eligible(request)))throw new ArgumentException("Demand query requires conservative full-catalog capacity.");
            if(shared!=null&&!shared.Compatible(request))throw new ArgumentException("Incompatible query catalog.");
            if(reserve?.Target is INavStorageReservation owner){NavStorageAdmission.RequireOwned(owner,request.StorageRoot);if(shared!=null)NavStorageAdmission.RequireOwned(owner,shared);}
            this.request=request;this.reserve=reserve;
            using(var a=new NavStorageAllocation(reserve)){a.Reserve(2048);a.Register(this);if(!birthOnly)Retain();}
            if(IsRegistered)RequireEdge(request.StorageRoot);request.StorageRoot?.Retain();
            double b=Provider.HalfExtent/request.Profile.GridCell;min=(int)Math.Ceiling(-b);width=(int)Math.Floor(b)-min+1;
            if(shared!=null){if(IsRegistered)RequireEdge(shared);shared.Retain();catalog=shared;sample=width*width;}
            if(!initialize)return;
            try{if(catalog!=null){locations=catalog.Locations;locations.Retain();grid=catalog.Grid;grid.Retain();}else{locations=NavPagedList<NavLocation>.Create(reserve);grid=NavPackedSlots.Create(reserve);}starts=NavCowDictionary<int,double>.Create(reserve:reserve);ends=NavCowDictionary<int,double>.Create(reserve:reserve);distance=NavCowDictionary<int,double>.Create(reserve:reserve);previous=NavCowDictionary<int,NavGraphArc>.Create(reserve:reserve);frontier=NavCowOrderedSet<NavQueueEntry>.Create(reserve:reserve);chain=NavPagedList<NavGraphArc>.Create(reserve);legs=NavPagedList<NavTypedLeg>.Create(reserve);InitializeSurfaceIndex(shared);InitializeSeamBound();InitializeExact();InitializeHeuristic(shared);InitializeLazy(lazyShared);}catch{ReleaseOwner();throw;}
        }
        internal override void ReleaseOwner(){if(released)return;released=true;Release();}
        protected override void ReleaseEdges(){corridorSource?.Release();originRectangles?.Release();lazyStore?.Release();exactDistance?.Release();exactFrontier?.Release();seamBoxes?.Release();nodeBounds?.Release();surfaceHeads?.Release();surfaceTails?.Release();surfaceNext?.Release();catalog?.Release();request.StorageRoot?.Release();locations?.Release();grid?.Release();starts?.Release();ends?.Release();distance?.Release();previous?.Release();frontier?.Release();chain?.Release();legs?.Release();Route?.Release();if(pending)NavStoragePayload<NavGraphArc>.Release(pendingArc);}
        internal void Seal(){if(frozen)return;originRectangles?.Seal();lazyStore?.Seal();exactDistance?.Seal();exactFrontier?.Seal();seamBoxes?.Seal();nodeBounds?.Seal();surfaceHeads?.Seal();surfaceTails?.Seal();surfaceNext?.Seal();locations.Seal();grid.Seal();starts.Seal();ends.Seal();distance.Seal();previous.Seal();frontier.Seal();chain.Seal();legs.Seal();reserve=null;frozen=true;}
        internal NavDemandQueryWork Fork(Func<long,bool> admission)
        {
            if(!frozen||admission==null)throw new InvalidOperationException("Demand query fork needs sealed source/admission.");
            if(IsRegistered){if(!(admission.Target is INavStorageReservation owner))throw new ArgumentException("Missing demand query ledger.");NavStorageAdmission.RequireOwned(owner,this);}
            var f=new NavDemandQueryWork(request,admission,false){phase=phase,sample=sample,origin=origin,terminal=terminal,seed=seed,current=current,direction=direction,candidate=candidate,chosen=chosen,chosenCandidate=chosenCandidate,traceNode=traceNode,reconstruct=reconstruct,reconstructLeg=reconstructLeg,best=best,routeCost=routeCost,Status=Status};
            try{ForkBounded(f);ForkLazy(f,admission);ForkExact(f,admission);ForkSeamBound(f,admission);ForkSurfaceIndex(f,admission);f.catalog=catalog;catalog?.Retain();if(request.DemandQueryV2&&sample==width*width){f.locations=locations;locations.Retain();f.grid=grid;grid.Retain();}else{f.locations=locations.Fork(admission);f.grid=grid.Fork(admission);}f.starts=starts.ForkPrepared(admission);f.ends=ends.ForkPrepared(admission);f.distance=distance.ForkPrepared(admission);f.previous=previous.ForkPrepared(admission);f.frontier=frontier.ForkPrepared(admission);f.chain=chain.Fork(admission);f.legs=legs.Fork(admission);f.pending=pending;f.pendingArc=pendingArc;if(pending)NavStoragePayload<NavGraphArc>.Retain(pendingArc);f.Route=Route;Route?.Retain();return f;}catch{f.ReleaseOwner();throw;}
        }
        internal NavDemandQueryCatalog ExportCatalog(Func<long,bool> admission)
        {if(request.LazyPartition||sample!=width*width||request.DemandQueryV3&&indexedCount!=locations.Count)return null;if(catalog!=null){catalog.Retain();return catalog;}locations.Seal();grid.Seal();surfaceHeads?.Seal();surfaceTails?.Seal();surfaceNext?.Seal();return new NavDemandQueryCatalog(request,locations,grid,min,width,admission,heads:surfaceHeads,tails:surfaceTails,next:surfaceNext);}
        internal long EstimatedRetainedBytes=>(originRectangles?.EstimatedRetainedBytes??0)+(lazyStore?.EstimatedRetainedBytes??0)+ExactBytes+SeamBoundBytes+SurfaceIndexBytes+StorageBytes+locations.CommittedStorageBytes(64)+grid.EstimatedRetainedBytes+starts.CommittedStorageBytes(4)+ends.CommittedStorageBytes(4)+distance.CommittedStorageBytes(4)+previous.CommittedStorageBytes(4)+frontier.CommittedStorageBytes+chain.CommittedStorageBytes(128)+legs.CommittedStorageBytes(8)+128L*legs.Count;
        internal long NextGrowthBytes=>(originRectangles?.NextGrowthBytes??0)+(lazyStore?.NextGrowthBytes??0)+ExactGrowth+SeamBoundGrowth+locations.AppendGrowthBytes(64)+(request.DemandQueryV3?98304L:32768L)+starts.MutationGrowthBytes(4)+ends.MutationGrowthBytes(4)+distance.MutationGrowthBytes(4)+previous.MutationGrowthBytes(4)+2*frontier.MutationGrowthBytes+chain.AppendGrowthBytes(128)+legs.AppendGrowthBytes(8);
        private bool Sweep(NavLocation from,NavLocation to)=>Provider.CanSweep(from,to,request.Clearance);
        private double Cost(NavLocation from,NavLocation to)=>LayeredNavigationProvider.Distance(from.Position,to.Position)*Provider.SurfaceCost(from.SurfaceId);
        private NavQueueEntry Entry(int node,double cost)
        {
            double h=request.DemandQueryV4?nodeBounds[node]:request.DemandQueryV3?TypedLowerBound(locations[node].Position,request.Subscription.AssignedEndpoint.Position,Provider.DemandSurfaceBounds(request.Subscription.AssignedEndpoint.SurfaceId)):Cost(locations[node],request.Subscription.AssignedEndpoint);
            double margin=8.0*(locations.Count+4)*(Provider.TracePortalBound+4.0)*2.2204460492503131e-16;
            return new NavQueueEntry{Node=node,Cost=request.DemandQueryV4?ConservativeSeamPriority(cost,h,margin):Math.Max(0,(cost+h)*(1-margin))};
        }
        internal void StepGraph()
        {
            using(var scratch=request.DemandQueryV5&&reserve?.Target is INavStorageScratchAllocator?NavStorageScratch.Reserve(reserve,ExactActionScratch):null)StepGraphCore();
        }
        private void StepGraphCore()
        {
            if(frozen||!CanGraph)throw new InvalidOperationException("Unpaid demand Graph step.");
            if(request.LazyPartition){StepLazyGraph();return;}
            if(phase==Phase.Catalog){
                int gx=min+sample/width,gz=min+sample%width;grid.Ensure(sample+1);
                if(Provider.TryLocate(new NavPoint(gx*request.Profile.GridCell,gz*request.Profile.GridCell),request.Clearance,out var at)){locations.Add(at);grid.Set(sample,locations.Count);}sample++;return;
            }
            if(request.DemandQueryV2&&phase==Phase.Candidates)phase=Phase.Edges;
            int dir=direction++;var from=locations[current];int x=(int)Math.Round(from.Position.X/request.Profile.GridCell)+ (dir==0?1:dir==1?-1:0)-min;
            int z=(int)Math.Round(from.Position.Z/request.Profile.GridCell)+(dir==2?1:dir==3?-1:0)-min;
            if(x<0||z<0||x>=width||z>=width)return;int to=grid.Get(x*width+z)-1;if(to<0)return;if(request.DemandQueryV5?exactDistance.TryGetValue(to,out var settledExact)&&settledExact<=exactDistance[current]:request.DemandQueryV3&&distance.TryGetValue(to,out double settled)&&settled<=distance[current])return;
            TraceDemandArc(to);
        }
        private void TraceDemandArc(int to)
        {
            NavLocation reached;IReadOnlyList<NavTypedLeg> trace;var from=NodeLocation(current);var owner=reserve?.Target as INavStorageReservation;
            bool found=owner==null?Provider.TryGraphTraceValidated(from,NodeLocation(to).Position,request.Clearance,out reached,out trace):Provider.TryGraphTraceValidatedOwned(from,NodeLocation(to).Position,request.Clearance,out reached,out trace,owner);
            try{
                if(!found||!reached.Equals(NodeLocation(to)))return;
                bool portal=false;double sum=0;string name=null;foreach(var leg in trace){sum+=leg.Cost;if(leg.Kind==NavTypedLegKind.Portal){portal=true;if(name==null)name=leg.PortalId;}}
                double arcCost=portal?sum:Cost(from,NodeLocation(to));if(request.DemandQueryV5){var next=exactDistance[current].Add(arcCost);if(exactDistance.TryGetValue(to,out var knownExact)&&next>=knownExact)return;}else if(request.DemandQueryV3&&distance.TryGetValue(to,out double known)&&distance[current]+arcCost>=known)return;
                pendingArc=new NavGraphArc(current,to,arcCost,portal?NavGraphArcKind.Portal:NavGraphArcKind.Surface,name,portal?trace:null);
                NavStoragePayload<NavGraphArc>.ValidateActive(pendingArc,this);NavStoragePayload<NavGraphArc>.Retain(pendingArc);pending=true;
            }finally{if(owner!=null)(trace as NavOwnedStorage)?.ReleaseOwner();}

        }
        internal void StepConnector()
        {
            using(var scratch=request.DemandQueryV5&&reserve?.Target is INavStorageScratchAllocator?NavStorageScratch.Reserve(reserve,ExactActionScratch):null)StepConnectorCore();
        }
        private void StepConnectorCore()
        {
            if(frozen||!CanConnector)throw new InvalidOperationException("Unpaid demand Connector step.");
            if(request.LazyPartition&&phase==Phase.Catalog){StepLazyOrigins();return;}
            if(request.DemandQueryV5&&phase!=Phase.Catalog){if(!StepBounded())StepExactConnector();return;}
            if(phase==Phase.Catalog){
                int c=request.Region.Candidates.Count;
                if(terminal<c){if(!Provider.IsValid(request.Region.Candidates[terminal++],request.Clearance))Finish(NavSolveStatus.InvalidEndpoint);return;}
                if(terminal<2*c){int i=terminal++-c;var at=request.Region.Candidates[i];if(Sweep(at,request.Subscription.AssignedEndpoint))ends[i]=Cost(at,request.Subscription.AssignedEndpoint);return;}
                if(request.DemandQueryV3?NextOriginNode>=0:origin<locations.Count){int i=request.DemandQueryV3?NextOriginNode:origin;if(request.DemandQueryV3)origin=i+1;else origin++;if(Sweep(request.Subscription.Origin,locations[i]))starts[i]=Cost(request.Subscription.Origin,locations[i]);return;}
                if(request.DemandQueryV3)origin=locations.Count;
                if(!Provider.IsValid(request.Region.Requested,request.Clearance)){Finish(NavSolveStatus.InvalidEndpoint);return;}
                if(starts.Count==0||ends.Count==0){Finish(NavSolveStatus.BlockedConnector);return;}phase=Phase.Seed;return;
            }
            if(phase==Phase.Seed){
                if(starts.TrySuccessor(seed,out var next)){if(!EnsureBound(next.Key))return;seed=next.Key;distance[seed]=next.Value;frontier.Add(Entry(seed,next.Value));}else phase=Phase.Pop;return;
            }
            if(phase==Phase.Pop){
                if(frontier.Count==0||frontier.Min.Cost>best){if(chosen<0)Finish(NavSolveStatus.UnreachableInGraph);else{traceNode=chosen;phase=Phase.Trace;}return;}
                var item=frontier.Min;frontier.Remove(item);if(!distance.TryGetValue(item.Node,out double known)||item.Cost!=Entry(item.Node,known).Cost)return;
                current=item.Node;candidate=request.DemandQueryV3&&locations[current].SurfaceId!=request.Subscription.AssignedEndpoint.SurfaceId?request.Region.Candidates.Count:0;direction=0;phase=request.DemandQueryV3&&candidate==request.Region.Candidates.Count?Phase.Edges:Phase.Candidates;return;
            }
            if(request.DemandQueryV2&&phase==Phase.Candidates&&candidate==request.Region.Candidates.Count)phase=Phase.Edges;
            if(request.DemandQueryV2&&phase==Phase.Edges&&!pending&&direction==4){phase=Phase.Pop;current=-1;StepConnector();return;}
            if(phase==Phase.Candidates){
                if(candidate==request.Region.Candidates.Count){phase=Phase.Edges;return;}
                int c=candidate++;if(!ends.TryGetValue(c,out double egress))return;var at=request.Region.Candidates[c];var node=locations[current];if(at.SurfaceId!=node.SurfaceId)return;
                double total=(distance[current]+Cost(node,at))+egress;if(total>best||!Sweep(node,at))return;
                if(total<best||total==best&&(current<chosen||current==chosen&&c<chosenCandidate)){best=total;chosen=current;chosenCandidate=c;}return;
            }
            if(phase==Phase.Edges){
                if(!pending){phase=Phase.Pop;current=-1;return;}
                var arc=pendingArc;double value=distance[current]+arc.Cost;
                if(!distance.TryGetValue(arc.To,out double old)||value<old){
                    if(!distance.ContainsKey(arc.To)&&distance.Count==request.MaxLabels){Finish(NavSolveStatus.CapacityExceeded);return;}
                    if(!EnsureBound(arc.To))return;
                    if(distance.ContainsKey(arc.To))frontier.Remove(Entry(arc.To,old));distance[arc.To]=value;previous[arc.To]=arc;frontier.Add(Entry(arc.To,value));
                }
                NavStoragePayload<NavGraphArc>.Release(pendingArc);pendingArc=default;pending=false;return;
            }
            if(phase==Phase.Trace){if(previous.TryGetValue(traceNode,out var arc)){chain.Add(arc);traceNode=arc.From;}else phase=Phase.Reconstruct;return;}
            if(phase==Phase.Reconstruct){
                if(reconstruct==0){int first=chain.Count==0?chosen:chain[chain.Count-1].From;AddSurface(request.Subscription.Origin,locations[first]);}
                else if(reconstruct<=chain.Count){var arc=chain[chain.Count-reconstruct];if(arc.Trace!=null){Add(arc.Trace[reconstructLeg++]);if(reconstructLeg<arc.Trace.Count)return;reconstructLeg=0;}else AddSurface(locations[arc.From],locations[arc.To]);}
                else if(reconstruct==chain.Count+1)AddSurface(locations[chosen],request.Region.Candidates[chosenCandidate]);
                else{AddSurface(request.Region.Candidates[chosenCandidate],request.Subscription.AssignedEndpoint);Route=new NavTypedRoute(request.Subscription,legs,routeCost,NavStorageLayout.Before(reserve,128),true);Finish(NavSolveStatus.Ready);}reconstruct++;
            }
        }
        private void AddSurface(NavLocation from,NavLocation to)
        {
            if(!Sweep(from,to))throw new InvalidOperationException("Unqualified demand surface leg.");
            var owner=reserve?.Target as INavStorageReservation;var leg=owner==null?new NavTypedLeg(from,to,Cost(from,to),NavTypedLegKind.Surface,null):NavTypedLeg.CreateOwned(from,to,Cost(from,to),NavTypedLegKind.Surface,null,owner,Provider);
            try{Add(leg);}finally{leg.ReleaseStorage();}
        }
        private void Add(NavTypedLeg leg){double next=routeCost+leg.Cost;if(request.DemandQueryV5&&!NavPhysicalSolveValidation.Cost(next))throw new ArgumentException("Exact public leg fold is not finite.");legs.Add(leg);routeCost=next;}
        private void Finish(NavSolveStatus status){Status=status;phase=Phase.Done;}
    }
}

namespace Spacewars.Simulation
{
    internal sealed partial class NavDemandQueryWork
    {
        internal bool Matches(NavScheduledRequest recipe)=>ReferenceEquals(request,recipe);
        internal override bool IsImmutableCaptureRoot=>frozen&&IsRegistered&&reserve==null;
        NavPhysicalTag INavPhysicalBody.PhysicalTag=>new NavPhysicalTag(request.DemandQueryV3?NavPhysicalStorageKind.DemandQueryWorkV3:request.DemandQueryV2?NavPhysicalStorageKind.DemandQueryWorkV2:NavPhysicalStorageKind.DemandQueryWorkV1,value:request.CertifiedGroupCorridor?NavPhysicalValueKind.CertifiedGroupCorridor115V1:request.TerminalCostProof?NavPhysicalValueKind.TerminalCostPolicyV13:request.OriginShadowProof?NavPhysicalValueKind.OriginShadowPolicyV12:request.TerminalCornerProof?NavPhysicalValueKind.TerminalCornerPolicyV11:request.TerminalRectangleProof?NavPhysicalValueKind.TerminalRectanglePolicyV10:request.RectangularOrigins?NavPhysicalValueKind.RectangleOriginsPolicyV9:request.CertifiedLazyPartition?NavPhysicalValueKind.CertifiedLazyPartitionPolicyV8:request.LazyPartition?NavPhysicalValueKind.LazyPartitionPolicyV7:request.DemandQueryV6?NavPhysicalValueKind.ExactAstarPolicyV6:request.DemandQueryV5?NavPhysicalValueKind.ExactCostPolicyV5:request.DemandQueryV4?NavPhysicalValueKind.SeamBoundPolicyV4:NavPhysicalValueKind.None);
        private void WriteScalars(System.IO.BinaryWriter w)
        {
            w.Write((int)phase);w.Write((int)Status);foreach(int i in new[]{sample,origin,terminal,seed,current,direction,candidate,chosen,chosenCandidate,traceNode,reconstruct,reconstructLeg})w.Write(i);
            w.Write(best);w.Write(routeCost);w.Write(pending);if(request.DemandQueryV5){exactBest.Write(w);exactRoutePrefix.Write(w);}if(request.DemandQueryV6)WriteHeuristic(w);if(request.LazyPartition)WriteLazy(w);if(request.TerminalRectangleProof)WriteTerminalRectangle(w);if(request.OriginShadowProof)WriteOriginShadow(w);if(request.CertifiedGroupCorridor)WriteBounded(w);
        }
        private void ReadScalars(System.IO.BinaryReader r)
        {
            phase=(Phase)r.ReadInt32();Status=(NavSolveStatus)r.ReadInt32();sample=r.ReadInt32();origin=r.ReadInt32();terminal=r.ReadInt32();seed=r.ReadInt32();current=r.ReadInt32();direction=r.ReadInt32();candidate=r.ReadInt32();chosen=r.ReadInt32();chosenCandidate=r.ReadInt32();traceNode=r.ReadInt32();reconstruct=r.ReadInt32();reconstructLeg=r.ReadInt32();best=r.ReadDouble();routeCost=r.ReadDouble();byte b=r.ReadByte();if(b>1)throw new ArgumentException("Noncanonical pending arc flag.");pending=b==1;if(request.DemandQueryV5){exactBest=NavExactCost.Read(r);exactRoutePrefix=NavExactCost.Read(r);}if(request.DemandQueryV6)ReadHeuristic(r);if(request.LazyPartition)ReadLazy(r);if(request.TerminalRectangleProof)ReadTerminalRectangle(r);if(request.OriginShadowProof)ReadOriginShadow(r);if(request.CertifiedGroupCorridor)ReadBounded(r);
        }
        void INavPhysicalBody.WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!IsImmutableCaptureRoot||StorageBytes!=NavStorageLayout.Charge(2048)||NavStorageLayout.ObjectBytesBound(GetType())>2048)throw new ArgumentException("Demand BODY layout/ownership contradiction.");
            WriteScalars(writer.Output);NavPhysicalMacro.WriteEdge(writer,this,request.StorageRoot);
            NavPhysicalMacro.WriteEdge(writer,this,locations);NavPhysicalMacro.WriteEdge(writer,this,grid);NavPhysicalMacro.WriteEdge(writer,this,starts);NavPhysicalMacro.WriteEdge(writer,this,ends);NavPhysicalMacro.WriteEdge(writer,this,distance);NavPhysicalMacro.WriteEdge(writer,this,previous);NavPhysicalMacro.WriteEdge(writer,this,frontier);NavPhysicalMacro.WriteEdge(writer,this,chain);NavPhysicalMacro.WriteEdge(writer,this,legs);NavPhysicalMacro.WriteEdge(writer,this,Route,true);
            if(pending)NavPhysicalGraphValue.WriteArc(writer,pendingArc);if(request.DemandQueryV2)NavPhysicalMacro.WriteEdge(writer,this,catalog,true);if(request.DemandQueryV3){writer.Output.Write(indexedCount);NavPhysicalMacro.WriteEdge(writer,this,surfaceHeads);NavPhysicalMacro.WriteEdge(writer,this,surfaceTails);NavPhysicalMacro.WriteEdge(writer,this,surfaceNext);}
            if(request.DemandQueryV4)WriteSeamBody(writer);if(request.DemandQueryV5){NavPhysicalMacro.WriteEdge(writer,this,exactDistance);NavPhysicalMacro.WriteEdge(writer,this,exactFrontier);}if(request.LazyPartition)NavPhysicalMacro.WriteEdge(writer,this,lazyStore);if(request.RectangularOrigins)NavPhysicalMacro.WriteEdge(writer,this,originRectangles);if(request.CertifiedGroupCorridor)NavPhysicalMacro.WriteEdge(writer,this,corridorSource,true);
        }
        internal static NavDemandQueryWork ReadPhysicalBody(NavPhysicalBodyReader reader,bool v2=false,bool v3=false,bool v4=false,bool v5=false,bool v6=false,bool lazy=false,bool certified=false,bool rectangular=false,bool terminalRectangle=false,bool terminalCorner=false,bool originShadow=false,bool terminalCost=false,bool bounded=false)
        {
            using(NavStorageScratch.Reserve(reader.Reservation,4096)){
            // Scalars precede recipe; retain only a fixed-size scratch value until
            // metadata is read. No compiler/search replay or provider rebuild.
            int scalarBytes=(lazy?671+LazyScalarBytes:v6?671:v5?617:73)+(terminalRectangle?TerminalRectangleScalarBytes:0)+(originShadow?OriginShadowScalarBytes:0)+(bounded?BoundedScalarBytes:0);byte[] scalar=reader.Input.ReadBytes(scalarBytes);if(scalar.Length!=scalarBytes)throw new ArgumentException("Truncated demand BODY.");
            var recipeRoot=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(reader);if(!(recipeRoot.Facade is NavScheduledRequest recipe)||!recipe.DemandQuery||recipe.DemandQueryV2!=v2||recipe.DemandQueryV3!=v3||recipe.DemandQueryV4!=v4||recipe.DemandQueryV5!=v5||recipe.DemandQueryV6!=v6||recipe.LazyPartition!=lazy||recipe.CertifiedLazyPartition!=certified||recipe.RectangularOrigins!=rectangular||recipe.TerminalRectangleProof!=terminalRectangle||recipe.TerminalCornerProof!=terminalCorner||recipe.OriginShadowProof!=originShadow||recipe.TerminalCostProof!=terminalCost||recipe.CertifiedGroupCorridor!=bounded)throw new ArgumentException("Demand BODY lacks new-policy recipe.");
            var work=new NavDemandQueryWork(recipe,reader.Reservation.TryReserve,false,true);
            using(var stream=new System.IO.MemoryStream(scalar))using(var input=new System.IO.BinaryReader(stream))work.ReadScalars(input);
            work.locations=NavPhysicalMacro.Acquire(work,NavPhysicalMacro.ReadEdge<NavPagedList<NavLocation>>(reader));work.grid=NavPhysicalMacro.Acquire(work,NavPhysicalMacro.ReadEdge<NavPackedSlots>(reader));
            work.starts=NavPhysicalMacro.Acquire(work,NavPhysicalMacro.ReadEdge<NavCowDictionary<int,double>>(reader));work.ends=NavPhysicalMacro.Acquire(work,NavPhysicalMacro.ReadEdge<NavCowDictionary<int,double>>(reader));work.distance=NavPhysicalMacro.Acquire(work,NavPhysicalMacro.ReadEdge<NavCowDictionary<int,double>>(reader));work.previous=NavPhysicalMacro.Acquire(work,NavPhysicalMacro.ReadEdge<NavCowDictionary<int,NavGraphArc>>(reader));work.frontier=NavPhysicalMacro.Acquire(work,NavPhysicalMacro.ReadEdge<NavCowOrderedSet<NavQueueEntry>>(reader));work.chain=NavPhysicalMacro.Acquire(work,NavPhysicalMacro.ReadEdge<NavPagedList<NavGraphArc>>(reader));work.legs=NavPhysicalMacro.Acquire(work,NavPhysicalMacro.ReadEdge<NavPagedList<NavTypedLeg>>(reader));work.Route=NavPhysicalMacro.Acquire(work,NavPhysicalMacro.ReadEdge<NavTypedRoute>(reader,true));
            if(work.pending){work.pendingArc=NavPhysicalGraphValue.ReadArc(reader);NavStoragePayload<NavGraphArc>.ValidateActive(work.pendingArc,work);NavStoragePayload<NavGraphArc>.Retain(work.pendingArc);}
            if(v2)work.catalog=NavPhysicalMacro.Acquire(work,NavPhysicalMacro.ReadEdge<NavDemandQueryCatalog>(reader,true));
            if(v3){work.indexedCount=reader.Count(recipe.Limits.MaxNodes);work.surfaceHeads=NavPhysicalMacro.Acquire(work,NavPhysicalMacro.ReadEdge<NavPackedSlots>(reader));work.surfaceTails=NavPhysicalMacro.Acquire(work,NavPhysicalMacro.ReadEdge<NavPackedSlots>(reader));work.surfaceNext=NavPhysicalMacro.Acquire(work,NavPhysicalMacro.ReadEdge<NavPackedSlots>(reader));}
            if(v4)work.ReadSeamBody(reader);if(v5){work.exactDistance=NavPhysicalMacro.Acquire(work,NavPhysicalMacro.ReadEdge<NavCowDictionary<int,NavExactCost>>(reader));work.exactFrontier=NavPhysicalMacro.Acquire(work,NavPhysicalMacro.ReadEdge<NavCowOrderedSet<NavExactQueueEntry>>(reader));}
            if(lazy)work.lazyStore=NavPhysicalMacro.Acquire(work,NavPhysicalMacro.ReadEdge<NavLazyPartitionStore>(reader));if(rectangular)work.originRectangles=NavPhysicalMacro.Acquire(work,NavPhysicalMacro.ReadEdge<NavOriginRectangleWork>(reader));
            if(bounded)work.corridorSource=NavPhysicalMacro.Acquire(work,NavPhysicalMacro.ReadEdge<NavDemandQueryWork>(reader,true));
            if(work.catalog!=null&&(!work.catalog.Compatible(recipe)||!ReferenceEquals(work.locations,work.catalog.Locations)||!ReferenceEquals(work.grid,work.catalog.Grid)||work.sample!=work.width*work.width||v3&&(!ReferenceEquals(work.surfaceHeads,work.catalog.Heads)||!ReferenceEquals(work.surfaceTails,work.catalog.Tails)||!ReferenceEquals(work.surfaceNext,work.catalog.Next)||work.indexedCount!=work.locations.Count)))throw new ArgumentException("Query shared catalog association mismatch.");
            work.frozen=true;work.reserve=null;
            using(NavStorageScratch.Reserve(reader.Reservation,16384L+4L*work.locations.Count+(lazy?8L*work.width*work.width:0)+(v6?ExactActionScratch:0)))work.Validate(reader.Reservation);return work;}
        }
        private void Validate(INavStorageReservation allocation=null)
        {
            ValidateTerminalRectangle();ValidateOriginShadow();ValidateBounded();
            if(request.LazyPartition){ValidateLazy(allocation);return;}
            int n=locations.Count,c=request.Region.Candidates.Count,dc=request.DemandQueryV5?exactDistance.Count:distance.Count,fc=FrontierCount;
            ValidateSeamBound();
            if(request.DemandQueryV3){ValidateSurfaceIndex(Provider,locations,indexedCount,surfaceHeads,surfaceTails,surfaceNext);if(phase!=Phase.Catalog&&Status==NavSolveStatus.Pending&&indexedCount!=n||phase==Phase.Catalog&&origin>0&&(origin>indexedCount||locations[origin-1].SurfaceId!=request.Subscription.Origin.SurfaceId))throw new ArgumentException("Demand index/origin prefix mismatch.");}
            if(!Enum.IsDefined(typeof(Phase),phase)||!Enum.IsDefined(typeof(NavSolveStatus),Status)||sample<0||sample>width*width||n>sample||origin<0||origin>n||terminal<0||terminal>2*c||seed< -1||seed>=Math.Max(1,n)||current< -1||current>=n||direction<0||direction>4||candidate<0||candidate>c||chosen< -1||chosen>=n||chosenCandidate< -1||chosenCandidate>=c||traceNode<0||traceNode>=Math.Max(1,n)||reconstruct<0||reconstruct>chain.Count+3||reconstructLeg<0||previous.Count>dc||dc>request.MaxLabels||fc>dc||chain.Count>n||(Status==NavSolveStatus.Pending)==(phase==Phase.Done)||(Route!=null)!=(Status==NavSolveStatus.Ready)||pending&&(phase!=Phase.Edges||current<0||direction==0)||(chosen<0)!=(chosenCandidate<0)||(!request.DemandQueryV5&&(chosen<0)!=double.IsPositiveInfinity(best))||!NavPhysicalSolveValidation.Cost(routeCost))throw new ArgumentException("Demand dimensions/phase contradiction.");
            if(phase!=Phase.Catalog&&Status==NavSolveStatus.Pending&&(sample!=width*width||origin!=n||terminal!=2*c)||phase==Phase.Catalog&&(seed!=-1||current!=-1||dc!=0||fc!=0)||phase==Phase.Seed&&(current!=-1||previous.Count!=0))throw new ArgumentException("Demand catalog/search boundary contradiction.");
            // Validate the stored prefix and exact provider name selectors, without
            // constructing a graph, searching, or synthesizing components.
            int expected=0;
            for(int i=0;i<sample;i++){
                int id=grid.Get(i)-1;
                if(id<0)continue;
                if(id!=expected++||id>=n)throw new ArgumentException("Demand packed grid node order contradiction.");
                var at=locations[id];var canonical=Provider.OwnedCheckpointLocation(at);
                if(!NavTypedLeg.SamePhysicalLocation(at,canonical)||at.Position.X!=(min+i/width)*request.Profile.GridCell||at.Position.Z!=(min+i%width)*request.Profile.GridCell)throw new ArgumentException("Demand catalog position/owner contradiction.");
            }
            if(expected!=n||grid.CapacitySlots<(sample==0?0:sample)||grid.CapacitySlots>sample+NavPackedSlots.PageSize)throw new ArgumentException("Demand catalog prefix/capacity contradiction.");
            for(int i=sample;i<grid.CapacitySlots;i++)if(grid.Get(i)!=0)throw new ArgumentException("Demand future grid payload.");
            foreach(var pair in starts)if(pair.Key<0||pair.Key>=origin||request.DemandQueryV3&&locations[pair.Key].SurfaceId!=request.Subscription.Origin.SurfaceId||!NavPhysicalSolveValidation.Cost(pair.Value)||pair.Value!=Cost(request.Subscription.Origin,locations[pair.Key]))throw new ArgumentException("Demand origin prefix contradiction.");
            foreach(var pair in ends)if(pair.Key<0||pair.Key>=Math.Max(0,terminal-c)||pair.Value!=Cost(request.Region.Candidates[pair.Key],request.Subscription.AssignedEndpoint))throw new ArgumentException("Demand terminal prefix contradiction.");
            foreach(var pair in distance)if(pair.Key<0||pair.Key>=n||!NavPhysicalSolveValidation.Cost(pair.Value)||!previous.ContainsKey(pair.Key)&&(!starts.TryGetValue(pair.Key,out double value)||value!=pair.Value)||phase==Phase.Seed&&pair.Key>seed)throw new ArgumentException("Demand distance/seed contradiction.");
            foreach(var pair in previous){ValidateArc(pair.Value,allocation);if(request.DemandQueryV5?pair.Key!=pair.Value.To||!exactDistance.TryGetValue(pair.Key,out var exactValue)||!exactDistance.TryGetValue(pair.Value.From,out var exactParent)||exactValue!=exactParent.Add(pair.Value.Cost):pair.Key!=pair.Value.To||!distance.TryGetValue(pair.Key,out double value)||!distance.TryGetValue(pair.Value.From,out double parent)||value<parent+pair.Value.Cost)throw new ArgumentException("Demand predecessor contradiction.");}
            foreach(var item in frontier)if(item.Node<0||item.Node>=n||item.Candidate!=0||!distance.TryGetValue(item.Node,out double value)||item.Cost!=Entry(item.Node,value).Cost)throw new ArgumentException("Demand frontier contradiction.");
            if(pending){ValidateArc(pendingArc,allocation);if(pendingArc.From!=current)throw new ArgumentException("Demand pending arc source contradiction.");}
            var seen=new int[n];int epoch=0;foreach(var pair in previous){int walk=pair.Key;epoch++;while(previous.TryGetValue(walk,out var a)&&seen[walk]!=-1){if(seen[walk]==epoch)throw new ArgumentException("Demand predecessor cycle.");seen[walk]=epoch;walk=a.From;}walk=pair.Key;while(previous.TryGetValue(walk,out var a)&&seen[walk]==epoch){seen[walk]=-1;walk=a.From;}}
            int node=chosen;foreach(var arc in chain){if(arc.To!=node||!previous.TryGetValue(node,out var prior)||!NavPhysicalMacro.SameArc(prior,arc))throw new ArgumentException("Demand reverse-chain alias contradiction.");node=arc.From;}
            if((chain.Count>0||phase==Phase.Trace)&&node!=traceNode)throw new ArgumentException("Demand trace cursor contradiction.");
            ValidateHeuristic();if(request.DemandQueryV5)ValidateExact();
            NavPhysicalSolveValidation.Legs(legs,out double sum);if(sum!=routeCost)throw new ArgumentException("Demand route sum contradiction.");
            if(Route!=null){if(!ReferenceEquals(Route.Subscription,request.Subscription)||Route.Cost!=routeCost||Route.Legs.Count!=legs.Count)throw new ArgumentException("Demand result binding contradiction.");for(int i=0;i<legs.Count;i++)if(!ReferenceEquals(Route.Legs[i],legs[i]))throw new ArgumentException("Demand result leg alias contradiction.");}
        }
        private void ValidateArc(NavGraphArc arc,INavStorageReservation allocation)
        {
            if(arc.From<0||!PresentNode(arc.From)||arc.To<0||!PresentNode(arc.To)||!NavPhysicalSolveValidation.Cost(arc.Cost))throw new ArgumentException("Demand arc dimensions contradiction.");
            var from=NodeLocation(arc.From);var to=NodeLocation(arc.To);double dx=Math.Abs(from.Position.X-to.Position.X),dz=Math.Abs(from.Position.Z-to.Position.Z);
            if(!((dx==request.Profile.GridCell&&dz==0)||(dz==request.Profile.GridCell&&dx==0))){
                int fx=(int)Math.Round(from.Position.X/request.Profile.GridCell),fz=(int)Math.Round(from.Position.Z/request.Profile.GridCell),tx=(int)Math.Round(to.Position.X/request.Profile.GridCell),tz=(int)Math.Round(to.Position.Z/request.Profile.GridCell);if(Math.Abs(fx-tx)+Math.Abs(fz-tz)!=1)throw new ArgumentException("Demand noncardinal arc.");
            }
            if(arc.Trace==null){if(arc.Kind!=NavGraphArcKind.Surface||arc.PortalId!=null||arc.Cost!=Cost(from,to))throw new ArgumentException("Demand surface arc weight contradiction.");}
            else{NavPhysicalSolveValidation.Legs(arc.Trace,out double cost);if(cost!=arc.Cost||arc.Kind!=NavGraphArcKind.Portal||!arc.Trace[0].From.Equals(from)||!arc.Trace[arc.Trace.Count-1].To.Equals(to))throw new ArgumentException("Demand portal trace contradiction.");}
            if(allocation!=null)Provider.VerifyPhysicalTrace(Provider.OwnedCheckpointLocation(from),Provider.OwnedCheckpointLocation(to),request.Clearance,arc.Trace,allocation);
        }
        internal sealed class Checkpoint
        {
            internal Checkpoint CopyLogical()=>(Checkpoint)MemberwiseClone();
            internal NavPoint[] SeamBoxes;internal int SeamDirectory,SeamCursor,BoundNode,BoundCursor;internal double BoundMinimum;internal KeyValuePair<int,double>[] NodeBounds;
            internal NavOriginRectangleWork.Checkpoint OriginRectangles;internal NavScheduledRequest HeuristicCatalogRecipe;internal NavLazyPartitionStore.Checkpoint LazyStore;
            internal KeyValuePair<int,NavExactCost>[] ExactDistance;internal NavExactQueueEntry[] ExactFrontier;internal NavTreeShape<NavExactQueueEntry> ExactShape;
            internal Checkpoint Corridor;internal NavScheduledRequest CorridorRecipe;
            internal string Recipe;internal int Indexed,HeadDirectory,TailDirectory,NextDirectory;internal int[][] Heads,Tails,Next;internal byte[] Scalars;internal NavLocation[] Locations;internal int[][] Grid;internal int GridDirectory,LocationDirectory,ChainDirectory,LegDirectory;
            internal KeyValuePair<int,double>[] Starts,Ends,Distance;internal KeyValuePair<int,NavGraphArc>[] Previous;internal NavQueueEntry[] Frontier;internal NavTreeShape<NavQueueEntry> FrontierShape;
            internal NavGraphArc[] Chain;internal NavTypedLeg[] Legs;internal NavGraphArc Pending;
        }
        internal Checkpoint Save()=>SaveWithAliases(null);
        internal Checkpoint SaveWithAliases(Dictionary<NavDemandQueryWork,Checkpoint> aliases)
        {
            if(aliases!=null&&aliases.TryGetValue(this,out var known))return known;
            byte[] scalar;using(var s=new System.IO.MemoryStream()){using(var w=new System.IO.BinaryWriter(s,System.Text.Encoding.UTF8,true))WriteScalars(w);scalar=s.ToArray();}
            var copies=new Dictionary<NavTypedLeg,NavTypedLeg>();var donor=SaveBounded(copies,aliases);
            NavTypedLeg Leg(NavTypedLeg leg){if(!copies.TryGetValue(leg,out var copy)){copy=leg.Detached();copies.Add(leg,copy);}return copy;}
            var traces=new Dictionary<IReadOnlyList<NavTypedLeg>,IReadOnlyList<NavTypedLeg>>();
            NavGraphArc Arc(NavGraphArc arc){IReadOnlyList<NavTypedLeg> trace=null;if(arc.Trace!=null&&!traces.TryGetValue(arc.Trace,out trace)){trace=Array.AsReadOnly(arc.Trace.Select(Leg).ToArray());traces.Add(arc.Trace,trace);}return new NavGraphArc(arc.From,arc.To,arc.Cost,arc.Kind,arc.PortalId,trace);}
            var saved=new Checkpoint{Corridor=donor,CorridorRecipe=corridorSource==null?null:DetachSharedRecipe(corridorSource.request,request.Provider),OriginRectangles=originRectangles?.Save(),LazyStore=lazyStore?.Save(DetachSharedRecipe(lazyStore.Recipe,lazyStore.Recipe.Provider)),HeuristicCatalogRecipe=request.DemandQueryV6&&hShared?DetachSharedRecipe(catalog.Recipe,catalog.Recipe.Provider):null,ExactDistance=exactDistance?.ToArray(),ExactFrontier=exactFrontier?.ToArray(),ExactShape=exactFrontier?.SaveShape(),SeamBoxes=seamBoxes?.ToArray(),SeamDirectory=seamBoxes?.DirectoryCapacity??0,SeamCursor=seamCursor,BoundNode=boundNode,BoundCursor=boundCursor,BoundMinimum=boundMinimum,NodeBounds=nodeBounds?.ToArray(),Indexed=indexedCount,Heads=surfaceHeads?.Save(),Tails=surfaceTails?.Save(),Next=surfaceNext?.Save(),HeadDirectory=surfaceHeads?.DirectoryCapacity??0,TailDirectory=surfaceTails?.DirectoryCapacity??0,NextDirectory=surfaceNext?.DirectoryCapacity??0,Recipe=request.ArtifactKey,Scalars=scalar,Locations=locations.Select(at=>new NavLocation(at.Position,at.SurfaceId)).ToArray(),Grid=grid.Save(),GridDirectory=grid.DirectoryCapacity,LocationDirectory=locations.DirectoryCapacity,ChainDirectory=chain.DirectoryCapacity,LegDirectory=legs.DirectoryCapacity,Starts=starts.ToArray(),Ends=ends.ToArray(),Distance=distance.ToArray(),Previous=previous.Select(p=>new KeyValuePair<int,NavGraphArc>(p.Key,Arc(p.Value))).ToArray(),Frontier=frontier.ToArray(),FrontierShape=frontier.SaveShape(),Chain=chain.Select(Arc).ToArray(),Legs=legs.Select(Leg).ToArray(),Pending=pending?Arc(pendingArc):default};if(aliases!=null)aliases.Add(this,saved);return saved;
        }
        internal static NavDemandQueryWork Restore(NavScheduledRequest recipe,Checkpoint state)=>RestoreWithAliases(recipe,state,null);
        internal static NavDemandQueryWork RestoreWithAliases(NavScheduledRequest recipe,Checkpoint state,Dictionary<Checkpoint,NavDemandQueryWork> aliases)
        {
            if(aliases!=null&&state!=null&&aliases.TryGetValue(state,out var known)){if(!known.Matches(recipe))throw new ArgumentException("Logical donor recipe alias changed.");known.Retain();return known;}
            if(state==null||state.Recipe!=recipe.ArtifactKey||state.Scalars==null||state.Scalars.Length!=((recipe.LazyPartition?671+LazyScalarBytes:recipe.DemandQueryV6?671:recipe.DemandQueryV5?617:73)+(recipe.TerminalRectangleProof?TerminalRectangleScalarBytes:0)+(recipe.OriginShadowProof?OriginShadowScalarBytes:0)+(recipe.CertifiedGroupCorridor?BoundedScalarBytes:0)))throw new ArgumentException("Stale demand checkpoint.");
            var w=new NavDemandQueryWork(recipe);try{
                using(var s=new System.IO.MemoryStream(state.Scalars))using(var r=new System.IO.BinaryReader(s))w.ReadScalars(r);
                var copies=new Dictionary<NavTypedLeg,NavTypedLeg>();w.RestoreBounded(state,copies,aliases);
                NavTypedLeg Leg(NavTypedLeg leg){if(!copies.TryGetValue(leg,out var copy)){copy=new NavTypedLeg(w.Provider.OwnedCheckpointLocation(leg.From),w.Provider.OwnedCheckpointLocation(leg.To),leg.Cost,leg.Kind,leg.PortalId);copies.Add(leg,copy);}return copy;}
                var traces=new Dictionary<IReadOnlyList<NavTypedLeg>,IReadOnlyList<NavTypedLeg>>();
                NavGraphArc Arc(NavGraphArc arc){IReadOnlyList<NavTypedLeg> trace=null;if(arc.Trace!=null&&!traces.TryGetValue(arc.Trace,out trace)){trace=Array.AsReadOnly(arc.Trace.Select(Leg).ToArray());traces.Add(arc.Trace,trace);}return new NavGraphArc(arc.From,arc.To,arc.Cost,arc.Kind,arc.PortalId,trace);}
                w.locations.AddRange(state.Locations.Select(w.Provider.OwnedCheckpointLocation));w.locations.RestoreDirectoryCapacity(state.LocationDirectory,recipe.Limits.MaxNodes,w.locations.Count);w.grid.ReleaseOwner();w.grid=NavPackedSlots.Restore(state.Grid,w.width*w.width,state.GridDirectory);
                if(recipe.DemandQueryV3){w.surfaceHeads.ReleaseOwner();w.surfaceTails.ReleaseOwner();w.surfaceNext.ReleaseOwner();w.surfaceHeads=NavPackedSlots.Restore(state.Heads,w.Provider.DemandSurfaceCount,state.HeadDirectory);w.surfaceTails=NavPackedSlots.Restore(state.Tails,w.Provider.DemandSurfaceCount,state.TailDirectory);w.surfaceNext=NavPackedSlots.Restore(state.Next,w.locations.Count,state.NextDirectory);w.indexedCount=state.Indexed;}
                if(recipe.LazyPartition){if(state.LazyStore==null)throw new ArgumentException("Missing lazy checkpoint.");var oldPartial=w.lazyStore;w.lazyStore=null;oldPartial.Release();w.lazyStore=NavLazyPartitionStore.Restore(DetachSharedRecipe(state.LazyStore.Recipe,recipe.Provider),state.LazyStore);}else if(state.LazyStore!=null)throw new ArgumentException("Unexpected lazy checkpoint.");
                if(recipe.RectangularOrigins!=(state.OriginRectangles!=null))throw new ArgumentException("Origins rectangle checkpoint policy.");if(recipe.RectangularOrigins){w.originRectangles.ReleaseOwner();w.originRectangles=NavOriginRectangleWork.Restore(recipe,state.OriginRectangles);}
                w.RestoreSeamBound(state);if(recipe.DemandQueryV5){if(state.ExactDistance==null||state.ExactFrontier==null||state.ExactShape==null)throw new ArgumentException("Missing exact query checkpoint.");foreach(var pair in state.ExactDistance)w.exactDistance.Add(pair.Key,pair.Value);w.exactFrontier.RestoreShape(state.ExactShape,recipe.MaxLabels);if(!w.exactFrontier.SequenceEqual(state.ExactFrontier))throw new ArgumentException("Exact checkpoint frontier mismatch.");}else if(state.ExactDistance!=null||state.ExactFrontier!=null||state.ExactShape!=null)throw new ArgumentException("Unexpected exact checkpoint.");
                foreach(var p in state.Starts)w.starts.Add(p.Key,p.Value);foreach(var p in state.Ends)w.ends.Add(p.Key,p.Value);foreach(var p in state.Distance)w.distance.Add(p.Key,p.Value);foreach(var p in state.Previous)w.previous.Add(p.Key,Arc(p.Value));
                w.frontier.RestoreShape(state.FrontierShape,recipe.MaxLabels);if(!w.frontier.SequenceEqual(state.Frontier))throw new ArgumentException("Demand checkpoint frontier mismatch.");
                w.chain.AddRange(state.Chain.Select(Arc));w.chain.RestoreDirectoryCapacity(state.ChainDirectory,recipe.Limits.MaxNodes,w.chain.Count);w.legs.AddRange(state.Legs.Select(Leg));w.legs.RestoreDirectoryCapacity(state.LegDirectory,(int)Math.Min(int.MaxValue-4096L,3L+recipe.Limits.MaxNodes*(2L*w.Provider.TracePortalBound+1)),w.legs.Count);
                if(recipe.DemandQueryV6&&w.hShared){if(state.HeuristicCatalogRecipe==null)throw new ArgumentException("Missing logical heuristic shared recipe.");w.locations.Seal();w.grid.Seal();w.surfaceHeads.Seal();w.surfaceTails.Seal();w.surfaceNext.Seal();w.catalog=new NavDemandQueryCatalog(DetachSharedRecipe(state.HeuristicCatalogRecipe,recipe.Provider),w.locations,w.grid,w.min,w.width,heads:w.surfaceHeads,tails:w.surfaceTails,next:w.surfaceNext);if(!w.catalog.Compatible(recipe))throw new ArgumentException("Logical heuristic catalog incompatible.");}else if(state.HeuristicCatalogRecipe!=null)throw new ArgumentException("Unexpected logical heuristic shared recipe.");
                if(w.pending){w.pendingArc=Arc(state.Pending);NavStoragePayload<NavGraphArc>.Retain(w.pendingArc);}if(w.Status==NavSolveStatus.Ready)w.Route=new NavTypedRoute(recipe.Subscription,w.legs,w.routeCost);w.Validate();if(aliases!=null)aliases.Add(state,w);return w;
            }catch{w.ReleaseOwner();throw;}
        }
    }
}
