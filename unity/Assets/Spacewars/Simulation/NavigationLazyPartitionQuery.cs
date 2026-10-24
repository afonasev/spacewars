using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
namespace Spacewars.Simulation
{
    // Paid classifications only. Missing ID means Unknown, -1 means Absent,
    // positive value selects the canonical location at value-1. Never a full catalog.
    internal sealed class NavLazyPartitionStore : NavOwnedStorage,INavPhysicalBody
    {
        internal readonly NavScheduledRequest Recipe;
        internal NavCowDictionary<int,int> Cells;
        internal NavPagedList<NavLocation> Points;
        internal NavOwnedExclusionState Exclusion;
        private bool frozen;
        internal NavLazyPartitionStore(NavScheduledRequest recipe,Func<long,bool> reserve=null,bool initialize=true,bool birthOnly=false):base(256)
        {
            if(!recipe.LazyPartition)throw new ArgumentException("Partial store needs explicit lazy policy.");
            Recipe=recipe;using(var a=new NavStorageAllocation(reserve)){a.Reserve(256);a.Register(this);if(!birthOnly)Retain();}
            if(IsRegistered)RequireEdge(recipe.StorageRoot);recipe.StorageRoot?.Retain();
            if(initialize){Cells=NavCowDictionary<int,int>.Create(insertionOrder:false,reserve:reserve);Points=NavPagedList<NavLocation>.Create(reserve);if(recipe.CertifiedLazyPartition)Exclusion=new NavOwnedExclusionState(recipe,reserve);}
        }
        internal int State(int id)=>Exclusion?.Excludes(id)==true?-1:Cells.TryGetValue(id,out int value)?value:0;
        internal NavLocation Location(int id){int n=State(id);if(n<=0)throw new ArgumentException("Unknown/Absent is not a node.");return Points[n-1];}
        internal bool Compatible(NavScheduledRequest r)=>r.LazyPartition&&Recipe.LazyPartition&&r.CertifiedLazyPartition==Recipe.CertifiedLazyPartition&&r.RectangularOrigins==Recipe.RectangularOrigins&&r.TerminalRectangleProof==Recipe.TerminalRectangleProof&&r.TerminalCornerProof==Recipe.TerminalCornerProof&&r.OriginShadowProof==Recipe.OriginShadowProof&&r.TerminalCostProof==Recipe.TerminalCostProof&&r.MetricContract==Recipe.MetricContract&&r.Provider.Binding==Recipe.Provider.Binding&&r.Profile.ExactProfileBinding==Recipe.Profile.ExactProfileBinding&&r.Profile.GridCell==Recipe.Profile.GridCell&&r.Profile.CostId==Recipe.Profile.CostId&&r.Profile.CostRevision==Recipe.Profile.CostRevision&&r.Clearance.Id==Recipe.Clearance.Id&&r.Clearance.Revision==Recipe.Clearance.Revision&&r.Clearance.MobilityClass==Recipe.Clearance.MobilityClass&&r.Clearance.Radius==Recipe.Clearance.Radius&&r.Limits.MaxSamples==Recipe.Limits.MaxSamples&&r.Limits.MaxNodes==Recipe.Limits.MaxNodes&&r.Limits.MaxArcs==Recipe.Limits.MaxArcs&&r.MaxLabels==Recipe.MaxLabels;
        internal void Classify(int id,int min,int width)
        {
            if(frozen||id<0||id>=checked(width*width)||State(id)!=0)throw new InvalidOperationException("Invalid paid classification.");
            double g=Recipe.Profile.GridCell;var p=new NavPoint((min+id/width)*g,(min+id%width)*g);
            if(((PartitionNavigationProvider)Recipe.Provider).TryLocate(p,Recipe.Clearance,out var at)){if(Points.Count>=Recipe.Limits.MaxNodes||Exclusion!=null&&Points.Count>=Exclusion.Cursor.TerminalCertificate.RemainingCount)throw new ArgumentException("Positive capacity exceeded.");Points.Add(at);Cells.Add(id,Points.Count);}else Cells.Add(id,-1);
        }
        internal void Seal(){if(frozen)return;Cells.Seal();Points.Seal();Exclusion?.Seal();frozen=true;}
        internal NavLazyPartitionStore Fork(Func<long,bool> reserve)
        {
            if(!frozen)throw new InvalidOperationException("Seal partial store before fork.");
            if(reserve==null){if(IsRegistered)throw new ArgumentException("Owned partial store needs its ledger.");return Restore(Recipe,Save(Recipe));}
            var n=new NavLazyPartitionStore(Recipe,reserve,false);
            try{n.Cells=Cells.ForkPrepared(reserve);n.Points=Points.Fork(reserve);if(Exclusion!=null)n.Exclusion=Exclusion.Fork(reserve);return n;}catch{n.ReleaseOwner();throw;}
        }
        internal long EstimatedRetainedBytes=>StorageBytes+(Exclusion?.StorageBytes??0)+Cells.CommittedStorageBytes(4)+Points.CommittedStorageBytes(64);
        internal long NextGrowthBytes=>Cells.MutationGrowthBytes(4)+Points.AppendGrowthBytes(64);
        protected override void ReleaseEdges(){Cells?.Release();Points?.Release();Exclusion?.Release();Recipe.StorageRoot?.Release();}
        internal override bool IsImmutableCaptureRoot=>IsRegistered&&frozen;
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(Recipe.CertifiedLazyPartition?NavPhysicalStorageKind.LazyPartitionStoreV2:NavPhysicalStorageKind.LazyPartitionStoreV1);
        public void WritePhysicalBody(NavPhysicalBodyWriter w)
        {if(!IsImmutableCaptureRoot||NavStorageLayout.ObjectBytesBound(GetType())>256)throw new ArgumentException("Partial store ownership/layout.");NavPhysicalMacro.WriteEdge(w,this,Recipe.StorageRoot);NavPhysicalMacro.WriteEdge(w,this,Cells);NavPhysicalMacro.WriteEdge(w,this,Points);if(Recipe.CertifiedLazyPartition)NavPhysicalMacro.WriteEdge(w,this,Exclusion);}
        internal static NavLazyPartitionStore ReadPhysicalBody(NavPhysicalBodyReader r,bool certified=false)
        {
            var recipe=NavPhysicalMacro.ReadEdge<NavDeclarationStorage>(r);if(!(recipe.Facade is NavScheduledRequest q)||!q.LazyPartition||q.CertifiedLazyPartition!=certified)throw new ArgumentException("Partial store recipe.");
            var n=new NavLazyPartitionStore(q,r.Reservation.TryReserve,false,true);try{n.Cells=NavPhysicalMacro.Acquire(n,NavPhysicalMacro.ReadEdge<NavCowDictionary<int,int>>(r));n.Points=NavPhysicalMacro.Acquire(n,NavPhysicalMacro.ReadEdge<NavPagedList<NavLocation>>(r));if(certified)n.Exclusion=NavPhysicalMacro.Acquire(n,NavPhysicalMacro.ReadEdge<NavOwnedExclusionState>(r));n.frozen=true;using(NavStorageScratch.Reserve(r.Reservation,4096L+4L*n.Points.Count))n.Validate();return n;}catch{n.ReleaseOwner();throw;}
        }
        internal void Validate()
        {
            double b=((PartitionNavigationProvider)Recipe.Provider).HalfExtent/Recipe.Profile.GridCell;int min=(int)Math.Ceiling(-b),width=(int)Math.Floor(b)-min+1;var seen=new bool[Points.Count];int count=0;
            if(Recipe.CertifiedLazyPartition!=(Exclusion!=null)||Exclusion!=null&&(!ReferenceEquals(Exclusion.Recipe,Recipe)||Exclusion.Progress!=NavExclusionProgress.Certified&&(Cells.Count!=0||Points.Count!=0)))throw new ArgumentException("Partial proof association.");Exclusion?.Validate();
            if(Points.Count>Recipe.Limits.MaxNodes||Exclusion?.Progress==NavExclusionProgress.Certified&&Points.Count>Exclusion.Cursor.TerminalCertificate.RemainingCount)throw new ArgumentException("Partial positive capacity.");
            if(Cells.Count>checked(width*width)||Points.Count>Cells.Count)throw new ArgumentException("Partial store capacity.");
            foreach(var p in Cells){if(p.Key<0||p.Key>=width*width||p.Value==0||p.Value< -1||p.Value>Points.Count)throw new ArgumentException("Partial classification dimensions.");if(Exclusion?.Excludes(p.Key)==true)throw new ArgumentException("Materialized excluded cell.");if(p.Value<0)continue;int i=p.Value-1;if(seen[i])throw new ArgumentException("Partial location alias index.");seen[i]=true;count++;var at=Points[i];if(at.Position.X!=(min+p.Key/width)*Recipe.Profile.GridCell||at.Position.Z!=(min+p.Key%width)*Recipe.Profile.GridCell||!NavTypedLeg.SamePhysicalLocation(at,((PartitionNavigationProvider)Recipe.Provider).OwnedCheckpointLocation(at)))throw new ArgumentException("Partial lattice/provider binding.");}
            if(count!=Points.Count)throw new ArgumentException("Unreferenced partial location.");
        }
        internal sealed class Checkpoint {internal Checkpoint CopyLogical()=>(Checkpoint)MemberwiseClone();internal NavScheduledRequest Recipe;internal string ProviderBinding;internal KeyValuePair<int,int>[] Cells;internal NavTreeShape<int> Shape;internal NavLocation[] Points;internal int Directory;internal NavExclusionCursor? Exclusion;}
        internal Checkpoint Save(NavScheduledRequest detached)=>new Checkpoint{Recipe=detached,ProviderBinding=Recipe.Provider.Binding,Cells=Cells.ToArray(),Shape=Cells.SaveShape(),Points=Points.Select(p=>p.Detached()).ToArray(),Directory=Points.DirectoryCapacity,Exclusion=Exclusion?.Cursor};
        internal static NavLazyPartitionStore Restore(NavScheduledRequest recipe,Checkpoint s)
        {
            if(s.ProviderBinding!=recipe.Provider.Binding)throw new ArgumentException("Partial logical provider binding changed.");
            var n=new NavLazyPartitionStore(recipe);try{if(recipe.CertifiedLazyPartition!=(s.Exclusion.HasValue))throw new ArgumentException("Logical proof missing.");if(s.Exclusion.HasValue)n.Exclusion.RestoreCursor(s.Exclusion.Value);n.Cells.RestoreShape(s.Shape,0,recipe.Limits.MaxSamples);foreach(var p in s.Cells){if(!n.Cells.ContainsKey(p.Key))throw new ArgumentException("Partial shape/key mismatch.");n.Cells[p.Key]=p.Value;}if(n.Cells.Count!=s.Cells.Length)throw new ArgumentException("Partial shape missing values.");n.Points.AddRange(s.Points.Select(((PartitionNavigationProvider)recipe.Provider).OwnedCheckpointLocation));n.Points.RestoreDirectoryCapacity(s.Directory,recipe.Limits.MaxSamples,n.Points.Count);n.Validate();return n;}catch{n.ReleaseOwner();throw;}
        }
    }
    internal sealed partial class NavDemandQueryWork
    {
        private NavLazyPartitionStore lazyStore;
        private NavOriginRectangleWork originRectangles;
        private bool OriginPreparationReady=>originRectangles==null||originRectangles.Prepared;
        private bool OriginRowReady=>originRectangles==null||lazyStage!=4||!originRectangles.NeedsRowStep(lazyX,lazyZ);
        private int lazyStage,lazyLow,lazyHigh,lazyMinX,lazyMaxX,lazyMinZ,lazyMaxZ,lazyX,lazyZ,lazyMask,lazyTo=-1,lazyTrace;
        private const int LazyScalarBytes=48;
        private NavLocation NodeLocation(int id)=>request.LazyPartition?lazyStore.Location(id):locations[id];
        private int NodeCapacity=>request.LazyPartition?checked(width*width):locations.Count;
        private bool PresentNode(int id)=>id>=0&&id<NodeCapacity&&(!request.LazyPartition||lazyStore.State(id)>0);
        internal static bool CertifiedEligible(NavScheduledRequest r)
        {if(!r.Query||!(r.Provider is PartitionNavigationProvider p)||r.Clearance.Radius<0)return false;double g=r.Profile.GridCell,b=p.HalfExtent/g;if(double.IsNaN(b)||double.IsInfinity(b)||double.IsNaN(g)||double.IsInfinity(g)||g<=0||b<0||b>int.MaxValue/4)return false;long w=(long)Math.Floor(b)-(long)Math.Ceiling(-b)+1;return w>0&&w<=46340&&w*w<=r.Limits.MaxSamples;}
        internal bool CertificateInsufficient=>request.CertifiedLazyPartition&&lazyStore.Exclusion.Progress==NavExclusionProgress.Insufficient;
        private bool CapacityReady=>!request.CertifiedLazyPartition||lazyStore.Exclusion.Progress==NavExclusionProgress.Certified;
        internal static bool LazyEligible(NavScheduledRequest r)
        {if(!r.Query||!(r.Provider is PartitionNavigationProvider p))return false;double b=p.HalfExtent/r.Profile.GridCell;if(double.IsNaN(b)||double.IsInfinity(b)||b>int.MaxValue/4)return false;long w=(long)Math.Floor(b)-(long)Math.Ceiling(-b)+1,s=w*w;return w>0&&s<=r.Limits.MaxSamples&&s<=r.Limits.MaxNodes&&4*s<=r.Limits.MaxArcs&&s<=r.MaxLabels;}
        private int LazyOriginId=>lazyStage==4?lazyX*width+lazyZ:-1;
        private bool LazyBookkeeping=>shadowBlocker<0&&Status==NavSolveStatus.Pending&&phase==Phase.Catalog&&(!CapacityReady&&!CertificateInsufficient||CapacityReady&&(!OriginPreparationReady||!HeuristicReady||lazyStage<4||lazyStage==4&&(!OriginRowReady||lazyStore.State(LazyOriginId)<0)));
        private bool LazyEdgePhase=>phase==Phase.Edges||phase==Phase.Candidates&&candidate==request.Region.Candidates.Count;
        private bool LazyGraph=>shadowBlocker<0&&CapacityReady&&OriginPreparationReady&&OriginRowReady&&Status==NavSolveStatus.Pending&&(phase==Phase.Catalog&&lazyStage==4&&lazyStore.State(LazyOriginId)==0||LazyEdgePhase&&!pending&&lazyMask!=0);
        private bool LazyConnector=>CapacityReady&&OriginPreparationReady&&(OriginRowReady||shadowBlocker>=0)&&Status==NavSolveStatus.Pending&&(shadowBlocker>=0||(phase!=Phase.Catalog?!LazyEdgePhase||pending||lazyMask==0:terminal<2*request.Region.Candidates.Count||lazyStage==4&&lazyStore.State(LazyOriginId)>0||lazyStage==5&&HeuristicReady));
        private void InitializeLazy(NavLazyPartitionStore shared)
        {if(!request.LazyPartition)return;if(shared!=null&&!shared.Compatible(request))throw new ArgumentException("Incompatible partial store.");lazyStore=shared==null?new NavLazyPartitionStore(request,reserve):shared.Fork(reserve);lazyHigh=width;if(request.RectangularOrigins)originRectangles=new NavOriginRectangleWork(request,reserve);}
        internal NavLazyPartitionStore ExportLazyStore(){if(!request.LazyPartition||!CapacityReady)return null;lazyStore.Seal();lazyStore.Retain();return lazyStore;}
        private void NextLazyOrigin(){if(request.RectangularOrigins){lazyZ++;return;}if(++lazyZ>lazyMaxZ){lazyZ=lazyMinZ;lazyX++;}if(lazyX>lazyMaxX)lazyStage=5;}
        private void StepLazyBookkeeping()
        {
            if(!CapacityReady){lazyStore.Exclusion.Step();return;}
            if(!OriginPreparationReady){originRectangles.StepPreparation();return;}
            if(!HeuristicReady){
                if(hStage==0){hBox=new NavHeuristicBox(Provider.DemandSurfaceBounds(request.Subscription.AssignedEndpoint.SurfaceId));hEligible=HeuristicDomain(hBox);hBox.Include(request.Subscription.AssignedEndpoint.Position);hContract=request.MetricContract;hStage=1;return;}
                if(hCandidateCursor<request.Region.Candidates.Count){var p=request.Region.Candidates[hCandidateCursor++].Position;hEligible&=NavExactHeuristic.Coordinate(p);hBox.Include(p);FoldTerminalCandidate(p);return;}hStage=3;return;
            }
            if(lazyStage<4){var box=Provider.DemandSurfaceBounds(request.Subscription.Origin.SurfaceId);double bound=lazyStage==0?box.MinX:lazyStage==1?box.MaxX:lazyStage==2?box.MinZ:box.MaxZ;
                if(lazyLow<lazyHigh){int mid=lazyLow+(lazyHigh-lazyLow)/2;double v=(min+mid)*request.Profile.GridCell;bool before=(lazyStage&1)==0?v<bound:v<=bound;if(before)lazyLow=mid+1;else lazyHigh=mid;return;}
                if(lazyStage==0)lazyMinX=lazyLow;else if(lazyStage==1)lazyMaxX=lazyLow-1;else if(lazyStage==2)lazyMinZ=lazyLow;else lazyMaxZ=lazyLow-1;
                lazyStage++;lazyLow=0;lazyHigh=width;if(lazyStage==4){lazyX=lazyMinX;lazyZ=lazyMinZ;if(lazyMinX>lazyMaxX||lazyMinZ>lazyMaxZ)lazyStage=5;}return;
            }
            if(lazyStage==4&&!OriginRowReady){originRectangles.StepRow(lazyX,lazyZ,lazyMinZ,lazyMaxZ,out lazyZ,out bool done);if(done){lazyX++;lazyZ=lazyMinZ;if(lazyX>lazyMaxX)lazyStage=5;}return;}
            if(lazyStage==4&&lazyStore.State(LazyOriginId)<0){int end=request.RectangularOrigins?lazyZ:lazyStore.Exclusion?.SkipRow(lazyX,lazyZ,lazyMaxZ)??lazyZ;if(end>lazyZ){lazyZ=end;if(lazyZ>lazyMaxZ){lazyZ=lazyMinZ;lazyX++;}if(lazyX>lazyMaxX)lazyStage=5;}else NextLazyOrigin();return;}throw new InvalidOperationException("Unpaid lazy preparation transition.");
        }
        private void StepLazyOrigins()
        {
            if(shadowBlocker>=0){StepOriginShadow();return;}
            int c=request.Region.Candidates.Count;
            if(terminal<c){if(!Provider.IsValid(request.Region.Candidates[terminal++],request.Clearance))Finish(NavSolveStatus.InvalidEndpoint);return;}
            if(terminal<2*c){int i=terminal++-c;var at=request.Region.Candidates[i];if(Sweep(at,request.Subscription.AssignedEndpoint))ends[i]=Cost(at,request.Subscription.AssignedEndpoint);return;}
            if(lazyStage==4){int id=LazyOriginId;var at=NodeLocation(id);int blocker=-1;bool visible=request.OriginShadowProof?Provider.CanSweep(request.Subscription.Origin,at,request.Clearance,out blocker):Sweep(request.Subscription.Origin,at);if(visible)starts[id]=Cost(request.Subscription.Origin,at);NextLazyOrigin();if(!visible&&blocker>=0&&lazyStage==4&&lazyZ<=lazyMaxZ)BeginOriginShadow(blocker);return;}
            if(lazyStage!=5||!HeuristicReady)throw new InvalidOperationException("Incomplete exhaustive Origins.");
            if(!Provider.IsValid(request.Region.Requested,request.Clearance)){Finish(NavSolveStatus.InvalidEndpoint);return;}if(starts.Count==0||ends.Count==0){Finish(NavSolveStatus.BlockedConnector);return;}phase=Phase.Seed;
        }
        private int LazyNeighbor(int dir){int x=current/width+(dir==0?1:dir==1?-1:0),z=current%width+(dir==2?1:dir==3?-1:0);return x<0||z<0||x>=width||z>=width?-1:x*width+z;}
        private void PrepareLazyMask()
        {
            lazyMask=0;lazyTo=-1;lazyTrace=0;
            for(int d=0;d<4;d++){int to=LazyNeighbor(d);if(to<0||lazyStore.State(to)<0||exactDistance.TryGetValue(to,out var g)&&g<=exactDistance[current])continue;lazyMask|=1<<d;}
        }
        private void StepLazyGraph()
        {
            if(phase==Phase.Catalog){int id=LazyOriginId;lazyStore.Classify(id,min,width);if(lazyStore.State(id)<0)NextLazyOrigin();return;}
            if(phase==Phase.Candidates)phase=Phase.Edges;
            int dir=0;while((lazyMask&(1<<dir))==0)dir++;direction=dir+1;int to=LazyNeighbor(dir);
            if(lazyStore.State(to)==0){lazyStore.Classify(to,min,width);if(lazyStore.State(to)<0){lazyMask&=~(1<<dir);lazyTo=-1;lazyTrace=0;}else{lazyTo=to;lazyTrace=1;}return;}
            if(lazyTrace!=0&&lazyTo!=to)throw new InvalidOperationException("Lost pending classify/trace continuation.");
            TraceDemandArc(to);lazyMask&=~(1<<dir);lazyTo=-1;lazyTrace=0;
        }
        private void WriteLazy(BinaryWriter w){foreach(int n in new[]{lazyStage,lazyLow,lazyHigh,lazyMinX,lazyMaxX,lazyMinZ,lazyMaxZ,lazyX,lazyZ,lazyMask,lazyTo,lazyTrace})w.Write(n);}
        private void ReadLazy(BinaryReader r){lazyStage=r.ReadInt32();lazyLow=r.ReadInt32();lazyHigh=r.ReadInt32();lazyMinX=r.ReadInt32();lazyMaxX=r.ReadInt32();lazyMinZ=r.ReadInt32();lazyMaxZ=r.ReadInt32();lazyX=r.ReadInt32();lazyZ=r.ReadInt32();lazyMask=r.ReadInt32();lazyTo=r.ReadInt32();lazyTrace=r.ReadInt32();}
        private void ForkLazy(NavDemandQueryWork n,Func<long,bool> admission)
        {if(!request.LazyPartition)return;n.lazyStore=lazyStore.Fork(admission);if(originRectangles!=null)n.originRectangles=originRectangles.Fork(admission);n.lazyStage=lazyStage;n.lazyLow=lazyLow;n.lazyHigh=lazyHigh;n.lazyMinX=lazyMinX;n.lazyMaxX=lazyMaxX;n.lazyMinZ=lazyMinZ;n.lazyMaxZ=lazyMaxZ;n.lazyX=lazyX;n.lazyZ=lazyZ;n.lazyMask=lazyMask;n.lazyTo=lazyTo;n.lazyTrace=lazyTrace;}
        private void ValidateLazy(INavStorageReservation allocation)
        {
            if((Route!=null)!=(Status==NavSolveStatus.Ready)||Status==NavSolveStatus.Ready&&chosen<0&&!boundedSelected||seed< -1||seed>=NodeCapacity||current< -1||current>=NodeCapacity||direction<0||direction>4||chosen< -1||chosen>=NodeCapacity||chosenCandidate< -1||chosenCandidate>=request.Region.Candidates.Count||(chosen<0)!=(chosenCandidate<0)||traceNode<0||traceNode>=NodeCapacity||reconstruct<0||reconstruct>chain.Count+3||reconstructLeg<0||pending&&phase!=Phase.Edges||phase==Phase.Catalog&&(seed!=-1||current!=-1||exactDistance.Count!=0||exactFrontier.Count!=0||previous.Count!=0)||
                lazyStore==null||((phase==Phase.Candidates||phase==Phase.Edges)&&(!PresentNode(current)||!exactDistance.ContainsKey(current)))||chain.Count>exactDistance.Count||!lazyStore.Compatible(request)||catalog!=null||locations.Count!=0||grid.CapacitySlots!=0||indexedCount!=0||sample!=0||distance.Count!=0||frontier.Count!=0||lazyStage<0||lazyStage>5||lazyLow<0||lazyHigh<lazyLow||lazyHigh>width||lazyMask<0||lazyMask>15||lazyTrace<0||lazyTrace>1||lazyTrace==1&&(!PresentNode(lazyTo)||phase!=Phase.Edges)||!Enum.IsDefined(typeof(Phase),phase)||!Enum.IsDefined(typeof(NavSolveStatus),Status)||(Status==NavSolveStatus.Pending)==(phase==Phase.Done)||phase!=Phase.Catalog&&Status==NavSolveStatus.Pending&&(lazyStage!=5||!HeuristicReady)||exactDistance.Count>request.MaxLabels||FrontierCount>exactDistance.Count||candidate<0||candidate>request.Region.Candidates.Count||terminal<0||terminal>2*request.Region.Candidates.Count)throw new ArgumentException("Lazy query phase/owner dimensions.");
            if(surfaceHeads.CapacitySlots!=0||surfaceTails.CapacitySlots!=0||surfaceNext.CapacitySlots!=0||hNext!=-1||hLast!=-1)throw new ArgumentException("Lazy query contains complete catalog state.");
            var originBox=Provider.DemandSurfaceBounds(request.Subscription.Origin.SurfaceId);
            bool Before(int stage,int k){double v=(min+k)*request.Profile.GridCell;double b=stage==0?originBox.MinX:stage==1?originBox.MaxX:stage==2?originBox.MinZ:originBox.MaxZ;return (stage&1)==0?v<b:v<=b;}
            for(int stage=0;stage<Math.Min(lazyStage,4);stage++){int found=stage==0?lazyMinX:stage==1?lazyMaxX+1:stage==2?lazyMinZ:lazyMaxZ+1;if(found<0||found>width||found>0&&!Before(stage,found-1)||found<width&&Before(stage,found))throw new ArgumentException("Lazy origin bound witness.");}
            if(lazyStage<4&&(lazyLow>0&&!Before(lazyStage,lazyLow-1)||lazyHigh<width&&Before(lazyStage,lazyHigh)))throw new ArgumentException("Lazy binary search bracket witness.");
            if(lazyStage==4&&(lazyX<lazyMinX||lazyX>lazyMaxX||lazyZ<lazyMinZ||lazyZ>lazyMaxZ+(request.RectangularOrigins?1:0))||lazyStage==5&&lazyMinX<=lazyMaxX&&lazyMinZ<=lazyMaxZ&&(lazyX!=lazyMaxX+1||lazyZ!=lazyMinZ))throw new ArgumentException("Lazy Origins prefix cursor.");
            if(lazyTrace==1&&(direction<1||direction>4||lazyTo!=LazyNeighbor(direction-1)||(lazyMask&(1<<(direction-1)))==0))throw new ArgumentException("Lazy classify/trace direction.");
            if(request.RectangularOrigins!=(originRectangles!=null)||originRectangles!=null&&!ReferenceEquals(originRectangles.Recipe,request))throw new ArgumentException("Origins rectangle query owner/policy.");originRectangles?.ValidateQueryCursor(lazyStage,lazyX,lazyZ,lazyMinZ,lazyMaxZ);
            lazyStore.Validate();
            foreach(var p in starts)if(!PresentNode(p.Key)||NodeLocation(p.Key).SurfaceId!=request.Subscription.Origin.SurfaceId||p.Value!=Cost(request.Subscription.Origin,NodeLocation(p.Key)))throw new ArgumentException("Lazy ingress binding/cost.");
            foreach(var p in ends)if(p.Key<0||p.Key>=request.Region.Candidates.Count||p.Value!=Cost(request.Region.Candidates[p.Key],request.Subscription.AssignedEndpoint))throw new ArgumentException("Lazy egress binding/cost.");
            foreach(var p in previous){ValidateArc(p.Value,allocation);if(p.Key!=p.Value.To||!exactDistance.TryGetValue(p.Key,out var value)||!exactDistance.TryGetValue(p.Value.From,out var parent)||value!=parent.Add(p.Value.Cost))throw new ArgumentException("Lazy predecessor objective.");}
            if(pending){ValidateArc(pendingArc,allocation);if(pendingArc.From!=current)throw new ArgumentException("Lazy pending arc source.");}
            var box=new NavHeuristicBox(Provider.DemandSurfaceBounds(request.Subscription.AssignedEndpoint.SurfaceId));bool eligible=HeuristicDomain(box);box.Include(request.Subscription.AssignedEndpoint.Position);for(int i=0;i<hCandidateCursor;i++){var p=request.Region.Candidates[i].Position;eligible&=NavExactHeuristic.Coordinate(p);box.Include(p);}if(hStage<0||hStage>3||hStage==2||hCandidateCursor<0||hCandidateCursor>request.Region.Candidates.Count||hShared||hStage>0&&(!box.Equals(hBox)||hEligible!=eligible||hContract!=request.MetricContract)||hStage==3&&hCandidateCursor!=request.Region.Candidates.Count)throw new ArgumentException("Lazy exact heuristic prefix.");
            var seen=new int[NodeCapacity];int epoch=0;
            foreach(var pair in previous){int walk=pair.Key;epoch++;while(previous.TryGetValue(walk,out var a)&&seen[walk]!=-1){if(seen[walk]==epoch)throw new ArgumentException("Lazy predecessor cycle.");seen[walk]=epoch;walk=a.From;}walk=pair.Key;while(previous.TryGetValue(walk,out var a)&&seen[walk]==epoch){seen[walk]=-1;walk=a.From;}}
            int chainNode=chosen;foreach(var arc in chain){if(arc.To!=chainNode||!previous.TryGetValue(chainNode,out var prior)||!NavPhysicalMacro.SameArc(prior,arc))throw new ArgumentException("Lazy trace prefix.");chainNode=arc.From;}if((chain.Count>0||phase==Phase.Trace)&&chainNode!=traceNode)throw new ArgumentException("Lazy trace cursor.");
            ValidateExact();NavPhysicalSolveValidation.Legs(legs,out double sum);if(sum!=routeCost||Route!=null&&(Route.Cost!=routeCost||Route.Legs.Count!=legs.Count||!ReferenceEquals(Route.Subscription,request.Subscription)))throw new ArgumentException("Lazy result fold/binding.");
        }
    }
}
