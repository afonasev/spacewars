using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Spacewars.Simulation
{
    public enum NavSolveStatus { Pending, Ready, InvalidEndpoint, BlockedConnector, UnreachableInGraph, CapacityExceeded, Stale }
    public enum NavTypedLegKind { Surface, Portal }
    public sealed partial class NavTypedLeg : NavOwnedStorage
    {
        // Includes two 40-byte locations with non-owning name selectors plus
        // scalar fields, owning edges and intrusive fields; capture adds 16.
        internal const long StorageCharge=208;
        internal NavTypedLeg Detached()=>new NavTypedLeg(From.Detached(),To.Detached(),Cost,Kind,PortalId);
        private NavTypedLeg(NavLocation from,NavLocation to,double cost,NavTypedLegKind kind,string portalId,bool raw):base(StorageCharge)
        {From=from;To=to;Cost=cost;Kind=kind;PortalId=portalId;}
        internal NavTypedLeg(NavLocation from,NavLocation to,double cost,NavTypedLegKind kind,string portalId):this(from,to,cost,kind,portalId,true)
        {using(var allocation=new NavStorageAllocation(null)){allocation.Register(this);Retain();}}
        internal static NavTypedLeg CreateOwned(NavLocation from,NavLocation to,double cost,NavTypedLegKind kind,string portalId,INavStorageReservation reservation,ILayeredNavigationProvider provider=null)
        {
            if(reservation==null)throw new ArgumentNullException(nameof(reservation));
            from.RequireName(reservation);to.RequireName(reservation);
            var owner=(provider as INavStorageFacade)?.StorageRoot;
            if(owner!=null){
                NavStorageAdmission.RequireOwned(reservation,owner);
                from=LayeredNavigationCompileWork.CanonicalLocation(provider,from);to=LayeredNavigationCompileWork.CanonicalLocation(provider,to);
                if(portalId!=null){
                    string canonical=null;
                    if(provider is PartitionNavigationProvider partition){foreach(var seam in partition.Seams)if(seam.Id==portalId){canonical=seam.Id;break;}}
                    else foreach(var portal in provider.Portals)if(portal.Id==portalId){canonical=portal.Id;break;}
                    if(canonical==null)throw new ArgumentException("Unknown owned leg portal binding.");portalId=canonical;
                }
                using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                    allocation.Reserve(StorageCharge);var result=allocation.Register(new NavTypedLeg(from,to,cost,kind,portalId,true));result.RequireEdge(owner);owner.Retain();result.ownedProvider=owner;result.Retain();return result;
                }
            }
            NavOwnedText fromText=null,toText=null,portalText=null;
            try{
                using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                    allocation.Reserve(StorageCharge);fromText=NavOwnedText.Copy(from.SurfaceId,reservation);toText=NavOwnedText.Copy(to.SurfaceId,reservation);
                    if(portalId!=null)portalText=NavOwnedText.Copy(portalId,reservation);
                    var leg=allocation.Register(new NavTypedLeg(new NavLocation(from.Position,new NavNameReference(fromText,NavNameRole.Text)),new NavLocation(to.Position,new NavNameReference(toText,NavNameRole.Text)),cost,kind,portalText?.Value,true));
                    leg.OwnMetadata(fromText,toText,portalText);leg.Retain();return leg;
                }
            }finally{fromText?.Release();toText?.Release();portalText?.Release();}
        }
        private bool released;
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){ownedFrom?.Release();ownedTo?.Release();ownedPortal?.Release();ownedProvider?.Release();}
        public NavLocation From { get; } public NavLocation To { get; } public double Cost { get; }
        public NavTypedLegKind Kind { get; } public string PortalId { get; }
    }
    public sealed partial class NavTypedRoute : NavOwnedStorage
    {
        private NavOwnedStorage ownedSubscription;
        internal NavTypedRoute(NavRouteSubscription subscription,IReadOnlyList<NavTypedLeg> legs,double cost,Func<long,bool> reserve=null,bool alreadyReserved=false):base(128)
        {
            if(subscription==null||legs==null)throw new ArgumentNullException();
            if(reserve?.Target is INavStorageReservation owner){NavMetadataOwnership.Require(subscription,owner);if(legs is NavOwnedStorage ownedLegs)NavStorageAdmission.RequireOwned(owner,ownedLegs);else foreach(var leg in legs)NavStorageAdmission.RequireOwned(owner,leg);}
            Subscription=subscription;Cost=NavContract.Number(cost);using(var allocation=new NavStorageAllocation(reserve)){if(!alreadyReserved)allocation.Reserve(128);allocation.Register(this);Retain();
                try{if(subscription.MetadataOwner!=null){RequireEdge(subscription.MetadataOwner);subscription.MetadataOwner.Retain();ownedSubscription=subscription.MetadataOwner;}
                    if(legs is NavOwnedStorage storage){if(legs is NavPagedList<NavTypedLeg> paged)paged.Seal();RequireEdge(storage);storage.Retain();Legs=legs;}
                    else Legs=NavRetainedArray<NavTypedLeg>.Copy(legs,reserve);
                }catch{ReleaseStorage();throw;}
            }
        }
        internal static NavTypedRoute CopySubscriptionAndCreateOwned(NavRouteSubscription subscription,IReadOnlyList<NavTypedLeg> legs,double cost,INavStorageReservation reservation)
        {
            if(subscription==null||legs==null)throw new ArgumentNullException();if(subscription.MetadataOwner!=null)NavMetadataOwnership.Require(subscription,reservation);
            if(legs is NavOwnedStorage root)NavStorageAdmission.RequireOwned(reservation,root);else foreach(var leg in legs)NavStorageAdmission.RequireOwned(reservation,leg);
            var copied=subscription.MetadataOwner==null?NavRouteSubscription.CopyOwned(subscription,reservation):null;
            try{return new NavTypedRoute(copied??subscription,legs,cost,NavStorageLayout.Before(reservation.TryReserve,128),true);}finally{copied?.MetadataOwner.Release();}
        }
        private bool released;
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){(Legs as NavOwnedStorage)?.Release();ownedSubscription?.Release();}
        public NavRouteSubscription Subscription { get; } public IReadOnlyList<NavTypedLeg> Legs { get; } public double Cost { get; }
    }
    public sealed class NavWorkResult
    {
        internal NavWorkResult(NavSolveStatus status, int consumed) { Status = status; Consumed = consumed; }
        public NavSolveStatus Status { get; } public int Consumed { get; }
    }
    internal struct NavQueueEntry : IComparable<NavQueueEntry>
    {
        internal double Cost; internal int Node, Candidate;
        public int CompareTo(NavQueueEntry other)
        {
            int c = Cost.CompareTo(other.Cost); if (c != 0) return c;
            c = Node.CompareTo(other.Node); return c != 0 ? c : Candidate.CompareTo(other.Candidate);
        }
    }
    internal static class NavSolverLegs
    {
        internal static NavTypedLeg Surface(LayeredNavigationGraph graph,NavLocation from,NavLocation to,INavStorageReservation reservation=null)
        {
            if (!graph.CanSweep(from, to)) throw new InvalidOperationException("Unqualified surface leg.");
            double cost=LayeredNavigationProvider.Distance(from.Position,to.Position)*graph.SurfaceCost(from.SurfaceId);return reservation==null?new NavTypedLeg(from,to,cost,NavTypedLegKind.Surface,null):NavTypedLeg.CreateOwned(from,to,cost,NavTypedLegKind.Surface,null,reservation,graph.PhysicalProvider);
        }
        internal static NavTypedLeg Arc(LayeredNavigationGraph graph,NavGraphArc arc,INavStorageReservation reservation=null)
        {
            if (arc.Trace != null) throw new InvalidOperationException("Partition arc requires its complete typed trace.");
            var from = graph.Nodes[arc.From].Location; var to = graph.Nodes[arc.To].Location;
            if (arc.Kind == NavGraphArcKind.Surface && !graph.CanSweep(from, to)) throw new InvalidOperationException("Unqualified graph leg.");
            // Portal arcs are emitted only after qualification by the immutable graph compiler.
            var kind=arc.Kind==NavGraphArcKind.Portal?NavTypedLegKind.Portal:NavTypedLegKind.Surface;return reservation==null?new NavTypedLeg(from,to,arc.Cost,kind,arc.PortalId):NavTypedLeg.CreateOwned(from,to,arc.Cost,kind,arc.PortalId,reservation,graph.PhysicalProvider);
        }
        internal static IReadOnlyList<NavTypedLeg> ExpandedArc(LayeredNavigationGraph graph,NavGraphArc arc,INavStorageReservation reservation=null)
        {if(arc.Trace!=null)return arc.Trace;if(reservation!=null&&!reservation.TryReserve(48))throw new InvalidOperationException("Expanded arc scratch exceeds scope.");return new[]{Arc(graph,arc,reservation)};}
    }
    // Pure singleton search. Advance charges each sweep, node attachment, frontier
    // pop, arc relaxation and reconstructed leg. Zero heuristic is admissible A*.
    public sealed partial class NavIncrementalSingletonRoute : NavOwnedStorage
    {
        private enum Phase { Direct, Origins, Endpoints, Search, Trace, Reconstruct, Done }
        private readonly LayeredNavigationGraph graph; private readonly NavRouteSubscription subscription;
        private NavOwnedStorage ownedSubscription;
        private NavCowDictionary<int,double> starts,ends,distance;
        private NavCowDictionary<int,NavGraphArc> previous;
        private NavCowOrderedSet<NavQueueEntry> frontier;
        private NavPagedList<NavTypedLeg> legs;private NavPagedList<NavGraphArc> chain;
        private bool frozen,released,borrowedRoute;private Func<long,bool> reservation;
        private Phase phase; private int cursor, arcCursor, current = -1, chosen = -1, reconstructCursor, reconstructLeg, traceNode;
        private double best = double.PositiveInfinity, routeCost; private bool startSeeded;
        private readonly int maxStates;
        public NavIncrementalSingletonRoute(LayeredNavigationGraph graph,NavRouteSubscription subscription,int maxStates=int.MaxValue):this(graph,subscription,maxStates,null,true){}
        private NavIncrementalSingletonRoute(LayeredNavigationGraph graph,NavRouteSubscription subscription,int maxStates,Func<long,bool> reserve,bool initialize,bool alreadyReserved=false,bool birthOnly=false):base(2048)
        {
            if(maxStates<1)throw new ArgumentOutOfRangeException(nameof(maxStates));this.maxStates=maxStates;this.graph=graph??throw new ArgumentNullException(nameof(graph));this.subscription=subscription??throw new ArgumentNullException(nameof(subscription));reservation=reserve;
            if(reserve?.Target is INavStorageReservation owner){NavStorageAdmission.RequireOwned(owner,graph);NavMetadataOwnership.Require(subscription,owner);}
            using(var allocation=new NavStorageAllocation(reserve)){if(!alreadyReserved)allocation.Reserve(2048);allocation.Register(this);if(!birthOnly)Retain();}graph.Retain();if(subscription.MetadataOwner!=null){RequireEdge(subscription.MetadataOwner);subscription.MetadataOwner.Retain();ownedSubscription=subscription.MetadataOwner;}
            if(!initialize)return;
            try{starts=NavCowDictionary<int,double>.Create(reserve:reserve);ends=NavCowDictionary<int,double>.Create(reserve:reserve);distance=NavCowDictionary<int,double>.Create(reserve:reserve);previous=NavCowDictionary<int,NavGraphArc>.Create(reserve:reserve);frontier=NavCowOrderedSet<NavQueueEntry>.Create(reserve:reserve);legs=NavPagedList<NavTypedLeg>.Create(reserve);chain=NavPagedList<NavGraphArc>.Create(reserve);
                Status=graph.IsValid(subscription.Origin)&&graph.IsValid(subscription.AssignedEndpoint)?NavSolveStatus.Pending:NavSolveStatus.InvalidEndpoint;phase=Status==NavSolveStatus.Pending?Phase.Direct:Phase.Done;
            }catch{ReleaseStorage();throw;}
        }
        internal static NavIncrementalSingletonRoute CreateOwned(LayeredNavigationGraph graph,NavRouteSubscription subscription,int maxStates,INavStorageReservation reservation)
        {NavStorageAdmission.RequireOwned(reservation,graph);NavMetadataOwnership.Require(subscription,reservation);return new NavIncrementalSingletonRoute(graph,subscription,maxStates,NavStorageLayout.Before(reservation.TryReserve,2048),true,true);}
        internal static NavIncrementalSingletonRoute CopySubscriptionAndCreateOwned(LayeredNavigationGraph graph,NavRouteSubscription subscription,int maxStates,INavStorageReservation reservation)
        {NavStorageAdmission.RequireOwned(reservation,graph);if(subscription==null)throw new ArgumentNullException(nameof(subscription));if(subscription.MetadataOwner!=null)return CreateOwned(graph,subscription,maxStates,reservation);var copy=NavRouteSubscription.CopyOwned(subscription,reservation);try{return CreateOwned(graph,copy,maxStates,reservation);}finally{copy.MetadataOwner.Release();}}
        internal NavIncrementalSingletonRoute ForkOwned(INavStorageReservation reservation)
        {NavStorageAdmission.RequireOwned(reservation,this);NavStorageAdmission.RequireOwned(reservation,graph);return ForkPrepared(reservation.TryReserve);}
        public NavSolveStatus Status {get;private set;}public NavTypedRoute Route {get;private set;}
        internal NavIncrementalSingletonRoute ForkPrepared(Func<long,bool> reserve)
        {
            if(!frozen)throw new InvalidOperationException("Seal committed singleton before preparing a fork.");if(reserve==null)throw new ArgumentException("Missing singleton admission.");
            var fork=new NavIncrementalSingletonRoute(graph,subscription,maxStates,NavStorageLayout.Before(reserve,2048),false,true){phase=phase,cursor=cursor,arcCursor=arcCursor,current=current,chosen=chosen,reconstructCursor=reconstructCursor,reconstructLeg=reconstructLeg,traceNode=traceNode,best=best,routeCost=routeCost,startSeeded=startSeeded,Status=Status};
            try{fork.starts=starts.ForkPrepared(reserve);fork.ends=ends.ForkPrepared(reserve);fork.distance=distance.ForkPrepared(reserve);fork.previous=previous.ForkPrepared(reserve);fork.frontier=frontier.ForkPrepared(reserve);fork.legs=legs.Fork(reserve);fork.chain=chain.Fork(reserve);if(Route!=null){Route.Retain();fork.Route=Route;fork.borrowedRoute=true;}return fork;}catch{fork.ReleaseStorage();throw;}
        }
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){starts?.Release();ends?.Release();distance?.Release();previous?.Release();frontier?.Release();legs?.Release();chain?.Release();Route?.Release();ownedSubscription?.Release();graph.Release();}
        internal void SealPrepared(){frozen=true;starts.Seal();ends.Seal();distance.Seal();previous.Seal();frontier.Seal();legs.Seal();chain.Seal();reservation=null;}
        public int FrontierCount => frontier.Count;
        public int LegCount => legs.Count;
        public int StateCount => starts.Count + ends.Count + distance.Count + previous.Count + chain.Count;
        internal long EstimatedRetainedBytes=>StorageBytes+starts.CommittedStorageBytes(4)+ends.CommittedStorageBytes(4)+distance.CommittedStorageBytes(4)+previous.CommittedStorageBytes(4)+frontier.CommittedStorageBytes+legs.CommittedStorageBytes(8)+NavStorageLayout.Charge(128)*legs.Count+chain.CommittedStorageBytes(128)+(Route==null?0:512L);
        internal static long InitialStorageBytes=>NavStorageLayout.Charge(2048)+12*NavStorageLayout.Charge(128)+NavStorageLayout.Charge(64)+NavStorageLayout.Charge(128)+2*NavPagedList<NavTypedLeg>.InitialStorageBytes;
        internal long NextGrowthBytes {
            get {
                if(Status!=NavSolveStatus.Pending)return 0;
                switch(phase){
                    case Phase.Direct:return 512;
                    case Phase.Origins:return starts.MutationGrowthBytes(4);
                    case Phase.Endpoints:return ends.MutationGrowthBytes(4);
                    case Phase.Search:return distance.MutationGrowthBytes(4)+previous.MutationGrowthBytes(4)+2L*frontier.MutationGrowthBytes;
                    case Phase.Trace:return chain.AppendGrowthBytes(128);
                    case Phase.Reconstruct:return legs.AppendGrowthBytes(8)+640L;
                    default:return 0;
                }
            }
        }

        public sealed class Checkpoint
        {
            public int Version { get; internal set; }
            public string GraphKey { get; internal set; }
            public string SubscriptionKey { get; internal set; }
            public NavSolveStatus Status { get; internal set; }
            internal int Phase, Cursor, ArcCursor, Current, Chosen, ReconstructCursor, ReconstructLeg, TraceNode;
            internal double Best, RouteCost;
            internal bool StartSeeded;
            internal KeyValuePair<int, double>[] Starts, Ends, Distance;
            internal KeyValuePair<int, NavGraphArc>[] Previous;
            internal NavQueueEntry[] Frontier;internal NavTreeShape<NavQueueEntry> FrontierShape;
            internal NavTypedLeg[] Legs;
            internal NavGraphArc[] Chain;
        }
        public Checkpoint Save() => new Checkpoint {
            Version = 2, GraphKey = graph.Binding.StableKey, SubscriptionKey = subscription.StableKey,
            Status = Status, Phase = (int)phase, Cursor = cursor, ArcCursor = arcCursor,
            Current = current, Chosen = chosen, ReconstructCursor = reconstructCursor, ReconstructLeg = reconstructLeg, TraceNode = traceNode,
            Best = best, RouteCost = routeCost, StartSeeded = startSeeded,
            Starts = starts.ToArray(), Ends = ends.ToArray(), Distance = distance.ToArray(),
            Previous = previous.Select(pair=>new KeyValuePair<int,NavGraphArc>(pair.Key,pair.Value.Detached())).ToArray(), Frontier = frontier.ToArray(),FrontierShape=frontier.SaveShape(), Legs = legs.Select(leg=>leg.Detached()).ToArray(), Chain = chain.Select(arc=>arc.Detached()).ToArray()
        };
        public static NavIncrementalSingletonRoute Restore(LayeredNavigationGraph graph, NavRouteSubscription subscription,
            Checkpoint state, int maxStates = int.MaxValue)
        {
            var work = new NavIncrementalSingletonRoute(graph, subscription, maxStates);
            if (state == null || (state.Version != 1&&state.Version!=2) || state.GraphKey != graph.Binding.StableKey ||
                state.SubscriptionKey != subscription.StableKey || state.Distance.Length > maxStates ||
                state.Frontier.Length > maxStates || state.Phase < 0 || state.Phase > (int)Phase.Done)
                throw new ArgumentException("Invalid or stale singleton checkpoint.");
            work.Status = state.Status; work.phase = (Phase)state.Phase; work.cursor = state.Cursor;
            work.arcCursor = state.ArcCursor; work.current = state.Current; work.chosen = state.Chosen;
            work.reconstructCursor = state.ReconstructCursor; work.reconstructLeg = state.ReconstructLeg; work.traceNode = state.TraceNode;
            work.best = state.Best; work.routeCost = state.RouteCost; work.startSeeded = state.StartSeeded;
            foreach (var pair in state.Starts) work.starts.Add(pair.Key, pair.Value);
            foreach (var pair in state.Ends) work.ends.Add(pair.Key, pair.Value);
            foreach (var pair in state.Distance) work.distance.Add(pair.Key, pair.Value);
            foreach (var pair in state.Previous) work.previous.Add(pair.Key, pair.Value);
            if(state.Version==2){work.frontier.RestoreShape(state.FrontierShape,maxStates);if(!work.frontier.SequenceEqual(state.Frontier))throw new ArgumentException("Singleton tree/frontier mismatch.");}else foreach (var entry in state.Frontier) work.frontier.Add(entry);
            foreach(var entry in work.frontier)if(entry.Node<0||entry.Node>=graph.Nodes.Count||entry.Candidate!=0||double.IsNaN(entry.Cost)||double.IsInfinity(entry.Cost)||entry.Cost<0||!work.distance.TryGetValue(entry.Node,out var cost)||cost!=entry.Cost)
                throw new ArgumentException("Invalid singleton frontier label/cost.");
            work.legs.AddRange(state.Legs); work.chain.AddRange(state.Chain);
            if (work.Status == NavSolveStatus.Ready) work.Route = new NavTypedRoute(subscription, work.legs, work.routeCost);
            return work;
        }
        public NavWorkResult Advance(int quota)
        {
            if (quota < 0) throw new ArgumentOutOfRangeException(nameof(quota));if(frozen&&Status==NavSolveStatus.Pending)throw new InvalidOperationException("Committed singleton cannot be advanced."); int used = 0;
            while (used < quota && Status == NavSolveStatus.Pending) { Step(); used++; }
            return new NavWorkResult(Status, used);
        }
        private void Step()
        {
            if (phase == Phase.Direct) {
                if (graph.CanSweep(subscription.Origin, subscription.AssignedEndpoint)) {
                    var direct=NavSolverLegs.Surface(graph,subscription.Origin,subscription.AssignedEndpoint,reservation?.Target as INavStorageReservation);
                    try{if(reservation!=null&&!reservation(48))throw new InvalidOperationException("Direct route scratch exceeds scope.");Route=new NavTypedRoute(subscription,new[]{direct},direct.Cost,NavStorageLayout.Before(reservation,128),true);}finally{direct.ReleaseStorage();}Finish(NavSolveStatus.Ready); return;
                }
                phase = Phase.Origins; return;
            }
            if (phase == Phase.Origins || phase == Phase.Endpoints) {
                if (cursor == graph.Nodes.Count) {
                    if (phase == Phase.Origins) { if (starts.Count == 0) { Finish(NavSolveStatus.BlockedConnector); return; } phase = Phase.Endpoints; cursor = 0; }
                    else { if (ends.Count == 0) { Finish(NavSolveStatus.BlockedConnector); return; } phase = Phase.Search; cursor = -1; }
                    return;
                }
                NavGraphAttachment a;
                var exact = phase == Phase.Origins ? subscription.Origin : subscription.AssignedEndpoint;
                if (graph.TryAttach(exact, cursor, out a)) (phase == Phase.Origins ? starts : ends)[cursor] = a.Cost;
                cursor++; return;
            }
            if (phase == Phase.Search) {
                if (!startSeeded) {
                    // Seed one origin per work unit, in stable node order.
                    cursor++;
                    if (cursor < graph.Nodes.Count) {
                        if (starts.TryGetValue(cursor, out double sourceCost)) {
                            if (distance.Count == maxStates) { Finish(NavSolveStatus.CapacityExceeded); return; }
                            distance[cursor] = sourceCost; frontier.Add(new NavQueueEntry { Cost = sourceCost, Node = cursor });
                        }
                        return;
                    }
                    startSeeded = true; cursor = 0; return;
                }
                if (current < 0) {
                    if (frontier.Count == 0 || frontier.Min.Cost > best) { if (chosen < 0) Finish(NavSolveStatus.UnreachableInGraph); else { traceNode = chosen; phase = Phase.Trace; } return; }
                    var entry = frontier.Min; frontier.Remove(entry);
                    if (!distance.TryGetValue(entry.Node, out double known) || known != entry.Cost) return;
                    current = entry.Node; arcCursor = 0;
                    if (ends.TryGetValue(current, out double egress)) {
                        double candidate = known + egress;
                        if (candidate < best || candidate == best && current < chosen) { best = candidate; chosen = current; }
                    }
                    return;
                }
                var arcs = graph.Outgoing(current);
                if (arcCursor == arcs.Count) { current = -1; return; }
                var arc = arcs[arcCursor++]; double nextCost = distance[current] + arc.Cost;
                if (!distance.TryGetValue(arc.To, out double old) || nextCost < old) {
                    if (!distance.ContainsKey(arc.To) && distance.Count == maxStates) { Finish(NavSolveStatus.CapacityExceeded); return; }
                    if (distance.ContainsKey(arc.To)) frontier.Remove(new NavQueueEntry { Cost = old, Node = arc.To });
                    distance[arc.To] = nextCost; previous[arc.To] = arc; frontier.Add(new NavQueueEntry { Cost = nextCost, Node = arc.To });
                }
                return;
            }
            if (phase == Phase.Trace) {
                if (previous.TryGetValue(traceNode, out var predecessor)) { chain.Add(predecessor); traceNode = predecessor.From; }
                else phase = Phase.Reconstruct;
                return;
            }
            if (phase == Phase.Reconstruct) {
                if (reconstructCursor == 0) {
                    int first = chain.Count == 0 ? chosen : chain[chain.Count - 1].From;
                    AddSurface(subscription.Origin,graph.Nodes[first].Location);
                } else if (reconstructCursor <= chain.Count) {
                    var arc=chain[chain.Count-reconstructCursor];var trace=NavSolverLegs.ExpandedArc(graph,arc,reservation?.Target as INavStorageReservation);
                    var leg=trace[reconstructLeg++];try{Add(leg);}finally{if(arc.Trace==null)leg.ReleaseStorage();}
                    if (reconstructLeg < trace.Count) return;
                    reconstructLeg = 0;
                }
                else { AddSurface(graph.Nodes[chosen].Location,subscription.AssignedEndpoint); Route = new NavTypedRoute(subscription,legs,routeCost,NavStorageLayout.Before(reservation,128),true); Finish(NavSolveStatus.Ready); }
                reconstructCursor++;
            }
        }
        private void AddSurface(NavLocation from,NavLocation to){var leg=NavSolverLegs.Surface(graph,from,to,reservation?.Target as INavStorageReservation);try{Add(leg);}finally{leg.ReleaseStorage();}}
        private void Add(NavTypedLeg leg) { legs.Add(leg); routeCost += leg.Cost; }
        private void Finish(NavSolveStatus status) { Status = status; phase = Phase.Done; }
    }
}
