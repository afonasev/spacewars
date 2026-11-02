using System;
using System.Linq;

namespace Spacewars.Simulation
{
    public sealed partial class NavIncrementalSingletonRoute
    {
        // An explicit solver mode, never an incomplete LayeredNavigationGraph.
        private readonly NavScheduledRequest flatRecipe;
        private NavPagedList<NavLocation> flatNodes;
        private NavCowDictionary<NavGridCoordinate,int> flatGrid;
        private int flatSample;
        private bool flatPending,flatHasEdge;
        private NavGraphArc flatEdge;
        internal bool IsFlat=>flatRecipe!=null;


        // One label per vertex in each of starts/ends/distance/previous/frontier/
        // chain: six bounded retained collections, not six copies of MaxLabels.
        internal static bool FlatEligible(NavScheduledRequest r)=>FlatBounds(r,out _,out _,out _,out _);
        private static bool FlatBounds(NavScheduledRequest r,out long x,out long z,out int width,out int height)
        {
            x=z=0;width=height=0;
            if(r==null||!r.Singleton||r.Query||!(r.Provider is LayeredNavigationProvider p)||p.Id!="flat"||p.SemanticsVersion!=1||p.Surfaces.Count!=1||p.Surfaces[0].Id!=NavLocation.FlatSurface||p.Portals.Count!=0)return false;
            double g=r.Profile.GridCell;if(double.IsNaN(g)||double.IsInfinity(g)||g<=0)return false;
            // IEEE binary64 multiply/divide each incur at most 2^-53 relative
            // error on normal finite values. At |coordinate| <= 2^40 their
            // composed error is < 2^-11, strictly below Round's half-integer
            // boundary. Exponents [-512,512] keep nonzero products normal and
            // finite. Power-of-two grids retain the original exact 2^53 bound.
            long bits=BitConverter.DoubleToInt64Bits(g);int exponent=(int)((bits>>52)&2047)-1023;
            if(exponent< -512||exponent>512)return false;
            double coordinateBound=(bits&0x000fffffffffffffL)==0?9007199254740991d:1099511627776d;
            var b=p.Surfaces[0].Area.Boundary;
            double a=Math.Ceiling(b.MinX/g),c=Math.Ceiling(b.MinZ/g),lastX=Math.Floor(b.MaxX/g),lastZ=Math.Floor(b.MaxZ/g);
            if(double.IsNaN(a)||double.IsNaN(c)||double.IsNaN(lastX)||double.IsNaN(lastZ)||a< -coordinateBound||c< -coordinateBound||lastX>coordinateBound||lastZ>coordinateBound||lastX<a||lastZ<c)return false;
            double w=lastX-a+1,h=lastZ-c+1;
            if(w>int.MaxValue||h>int.MaxValue)return false;
            try{long count=checked((long)w*(long)h);
                if(count<1||count>int.MaxValue||count>r.Limits.MaxSamples||count>r.Limits.MaxNodes||checked(4*count)>r.Limits.MaxArcs||checked(6*count)>r.MaxLabels)return false;
                x=(long)a;z=(long)c;width=(int)w;height=(int)h;return true;
            }catch(OverflowException){return false;}
        }
        private NavIncrementalSingletonRoute(NavScheduledRequest recipe,Func<long,bool> reserve,bool initialize,bool alreadyReserved=false,bool birthOnly=false):base(2048)
        {
            if(!FlatEligible(recipe))throw new ArgumentException("Uncertified flat N1 recipe.");
            flatRecipe=recipe;subscription=recipe.Subscription;maxStates=recipe.MaxLabels;reservation=reserve;
            if(reserve?.Target is INavStorageReservation owner){NavStorageAdmission.RequireOwned(owner,recipe.StorageRoot);NavMetadataOwnership.Require(subscription,owner);}
            using(var a=new NavStorageAllocation(reserve)){if(!alreadyReserved)a.Reserve(2048);a.Register(this);if(!birthOnly)Retain();}
            if(recipe.StorageRoot!=null){RequireEdge(recipe.StorageRoot);recipe.StorageRoot.Retain();}
            if(subscription.MetadataOwner!=null){RequireEdge(subscription.MetadataOwner);subscription.MetadataOwner.Retain();ownedSubscription=subscription.MetadataOwner;}
            if(!initialize)return;
            try{
                flatNodes=NavPagedList<NavLocation>.Create(reserve);flatGrid=NavCowDictionary<NavGridCoordinate,int>.Create(NavPhysicalMapBinding<NavGridCoordinate,int>.Comparer,insertionOrder:true,reserve:reserve);
                starts=NavCowDictionary<int,double>.Create(reserve:reserve);ends=NavCowDictionary<int,double>.Create(reserve:reserve);distance=NavCowDictionary<int,double>.Create(reserve:reserve);previous=NavCowDictionary<int,NavGraphArc>.Create(reserve:reserve);frontier=NavCowOrderedSet<NavQueueEntry>.Create(reserve:reserve);legs=NavPagedList<NavTypedLeg>.Create(reserve);chain=NavPagedList<NavGraphArc>.Create(reserve);
                Status=NavSolveStatus.Pending;phase=Phase.FlatCatalog;
            }catch{ReleaseStorage();throw;}
        }
        internal static NavIncrementalSingletonRoute CreateFlat(NavScheduledRequest r)=>new NavIncrementalSingletonRoute(r,null,true);
        internal static NavIncrementalSingletonRoute CreateOwnedFlat(NavScheduledRequest r,INavStorageReservation owner)
            =>new NavIncrementalSingletonRoute(r,NavStorageLayout.Before(owner.TryReserve,2048),true,true);
        internal static long FlatInitialStorageBytes=>InitialStorageBytes+8L*NavStorageLayout.Charge(128)+NavPagedList<NavLocation>.InitialStorageBytes;
        private long FlatBytes=>flatNodes.CommittedStorageBytes(64)+flatGrid.CommittedStorageBytes(24);
        internal bool FlatNeedsGraph=>IsFlat&&Status==NavSolveStatus.Pending&&(phase==Phase.FlatCatalog||phase==Phase.Search&&startSeeded&&current>=0&&!flatPending);
        private long FlatGrowth=>phase==Phase.FlatCatalog?flatNodes.AppendGrowthBytes(64)+flatGrid.MutationGrowthBytes(24):
            phase==Phase.Search&&flatPending?distance.MutationGrowthBytes(4)+previous.MutationGrowthBytes(4)+2L*frontier.MutationGrowthBytes:
            phase==Phase.Search?2L*frontier.MutationGrowthBytes+distance.MutationGrowthBytes(4):
            phase==Phase.Origins?starts.MutationGrowthBytes(4):phase==Phase.Endpoints?ends.MutationGrowthBytes(4):
            phase==Phase.Trace?chain.AppendGrowthBytes(128):phase==Phase.Reconstruct?legs.AppendGrowthBytes(8)+640L:0;
        private bool FlatSweep(NavLocation a,NavLocation b)=>flatRecipe.Provider.CanSweep(a,b,flatRecipe.Clearance);
        private double FlatCost(NavLocation a,NavLocation b)=>LayeredNavigationProvider.Distance(a.Position,b.Position)*flatRecipe.Provider.SurfaceCost(a.SurfaceId);
        private NavGridCoordinate FlatCoordinate(NavLocation at)=>new NavGridCoordinate(at,(long)Math.Round(at.Position.X/flatRecipe.Profile.GridCell),(long)Math.Round(at.Position.Z/flatRecipe.Profile.GridCell));

        private bool FlatNeighbor(int node,int direction,out int to)
        {
            var key=FlatCoordinate(flatNodes[node]);
            // Old sorted arcs enumerate To IDs ascending: -X,-Z,+Z,+X.
            long x=key.Item2+(direction==0?-1:direction==3?1:0),z=key.Item3+(direction==1?-1:direction==2?1:0);
            return flatGrid.TryGetValue(new NavGridCoordinate(key.Item1,x,z),out to);
        }
        // The scheduler pays exactly one Graph or Connector primitive BEFORE this
        // call. A geometry probe and its relaxation are distinct saved boundaries.
        internal void StepFlat(bool graphLane)
        {
            if(!IsFlat||frozen||Status!=NavSolveStatus.Pending||graphLane!=FlatNeedsGraph)throw new InvalidOperationException("Unpaid flat N1 primitive.");
            if(phase==Phase.FlatCatalog){
                FlatBounds(flatRecipe,out long x,out long z,out int width,out int height);
                if(flatSample==width*height){phase=Phase.Origins;cursor=0;return;}
                var at=flatRecipe.Provider.Surfaces[0].OwnedLocation(new NavPoint((x+flatSample/height)*flatRecipe.Profile.GridCell,(z+flatSample%height)*flatRecipe.Profile.GridCell));
                if(flatRecipe.Provider.IsValid(at,flatRecipe.Clearance)){int id=flatNodes.Count;flatNodes.Add(at);flatGrid.Add(FlatCoordinate(at),id);}flatSample++;return;
            }
            if(phase==Phase.Origins||phase==Phase.Endpoints){
                if(cursor==flatNodes.Count){
                    if((phase==Phase.Origins?starts:ends).Count==0){Finish(NavSolveStatus.BlockedConnector);return;}
                    phase=phase==Phase.Origins?Phase.Endpoints:Phase.Search;cursor=phase==Phase.Search?-1:0;return;
                }
                var exact=phase==Phase.Origins?subscription.Origin:subscription.AssignedEndpoint;var at=flatNodes[cursor];
                if(FlatSweep(exact,at))(phase==Phase.Origins?starts:ends)[cursor]=FlatCost(exact,at);cursor++;return;
            }
            if(phase==Phase.Search){
                if(!startSeeded){if(++cursor<flatNodes.Count){if(starts.TryGetValue(cursor,out double value)){distance[cursor]=value;frontier.Add(new NavQueueEntry{Cost=value,Node=cursor});}return;}startSeeded=true;cursor=0;return;}
                if(current<0){
                    if(frontier.Count==0||frontier.Min.Cost>best){if(chosen<0)Finish(NavSolveStatus.UnreachableInGraph);else{traceNode=chosen;phase=Phase.Trace;}return;}
                    var entry=frontier.Min;frontier.Remove(entry);if(!distance.TryGetValue(entry.Node,out double known)||known!=entry.Cost)return;
                    current=entry.Node;arcCursor=0;
                    if(ends.TryGetValue(current,out double egress)){double value=known+egress;if(value<best||value==best&&current<chosen){best=value;chosen=current;}}return;
                }
                if(!flatPending){
                    flatHasEdge=false;flatEdge=default;
                    if(FlatNeighbor(current,arcCursor,out int to)){
                        int low=Math.Min(current,to),high=Math.Max(current,to);var from=flatNodes[low];var target=flatNodes[high];
                        if(FlatSweep(from,target)){flatHasEdge=true;flatEdge=new NavGraphArc(current,to,FlatCost(from,target),NavGraphArcKind.Surface,null);}
                    }
                    flatPending=true;return;
                }
                if(flatHasEdge){double value=distance[current]+flatEdge.Cost;
                    if(!distance.TryGetValue(flatEdge.To,out double old)||value<old){if(!distance.ContainsKey(flatEdge.To)&&distance.Count==maxStates){Finish(NavSolveStatus.CapacityExceeded);return;}if(distance.ContainsKey(flatEdge.To))frontier.Remove(new NavQueueEntry{Cost=old,Node=flatEdge.To});distance[flatEdge.To]=value;previous[flatEdge.To]=flatEdge;frontier.Add(new NavQueueEntry{Cost=value,Node=flatEdge.To});}}
                flatPending=flatHasEdge=false;flatEdge=default;if(++arcCursor==4){current=-1;arcCursor=0;}return;
            }
            if(phase==Phase.Trace){if(previous.TryGetValue(traceNode,out var arc)){chain.Add(arc);traceNode=arc.From;}else phase=Phase.Reconstruct;return;}
            if(phase==Phase.Reconstruct){
                if(reconstructCursor==0){int first=chain.Count==0?chosen:chain[chain.Count-1].From;AddFlatSurface(subscription.Origin,flatNodes[first]);}
                else if(reconstructCursor<=chain.Count){var arc=chain[chain.Count-reconstructCursor];AddFlatSurface(flatNodes[arc.From],flatNodes[arc.To],arc.Cost);}
                else{AddFlatSurface(flatNodes[chosen],subscription.AssignedEndpoint);Route=new NavTypedRoute(subscription,legs,routeCost,NavStorageLayout.Before(reservation,128),true);Finish(NavSolveStatus.Ready);}
                reconstructCursor++;return;
            }
            throw new InvalidOperationException("Invalid flat N1 phase.");
        }
        private void AddFlatSurface(NavLocation from,NavLocation to,double? cost=null)
        {
            double value=cost??FlatCost(from,to);var owner=reservation?.Target as INavStorageReservation;
            var leg=owner==null?new NavTypedLeg(from,to,value,NavTypedLegKind.Surface,null):NavTypedLeg.CreateOwned(from,to,value,NavTypedLegKind.Surface,null,owner,flatRecipe.Provider);
            try{Add(leg);}finally{leg.ReleaseStorage();}
        }
        private NavIncrementalSingletonRoute ForkFlat(Func<long,bool> reserve)
        {
            var f=new NavIncrementalSingletonRoute(flatRecipe,NavStorageLayout.Before(reserve,2048),false,true){phase=phase,cursor=cursor,arcCursor=arcCursor,current=current,chosen=chosen,reconstructCursor=reconstructCursor,reconstructLeg=reconstructLeg,traceNode=traceNode,best=best,routeCost=routeCost,startSeeded=startSeeded,Status=Status,flatSample=flatSample,flatPending=flatPending,flatHasEdge=flatHasEdge,flatEdge=flatEdge};
            try{f.flatNodes=flatNodes.Fork(reserve);f.flatGrid=flatGrid.ForkPrepared(reserve);f.starts=starts.ForkPrepared(reserve);f.ends=ends.ForkPrepared(reserve);f.distance=distance.ForkPrepared(reserve);f.previous=previous.ForkPrepared(reserve);f.frontier=frontier.ForkPrepared(reserve);f.legs=legs.Fork(reserve);f.chain=chain.Fork(reserve);if(Route!=null){Route.Retain();f.Route=Route;f.borrowedRoute=true;}return f;}catch{f.ReleaseOwner();throw;}
        }
    }
}
