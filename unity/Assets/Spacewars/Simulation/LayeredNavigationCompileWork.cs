using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Spacewars.Simulation
{
    internal readonly struct NavGridCoordinate : IEquatable<NavGridCoordinate>,IComparable<NavGridCoordinate>
    {
        internal readonly string Item1;internal readonly long Item2,Item3;internal readonly NavNameReference NameReference;
        internal NavGridCoordinate(string surface,long x,long z){Item1=surface;Item2=x;Item3=z;NameReference=default;}
        internal NavGridCoordinate(NavLocation location,long x,long z){Item1=location.SurfaceId;Item2=x;Item3=z;NameReference=location.NameReference;}
        public int CompareTo(NavGridCoordinate other){int c=StringComparer.Ordinal.Compare(Item1,other.Item1);if(c!=0)return c;c=Item2.CompareTo(other.Item2);return c!=0?c:Item3.CompareTo(other.Item3);}
        public bool Equals(NavGridCoordinate other)=>Item1==other.Item1&&Item2==other.Item2&&Item3==other.Item3;
        public override bool Equals(object other)=>other is NavGridCoordinate value&&Equals(value);
        public override int GetHashCode()=>(Item1?.GetHashCode()??0)^Item2.GetHashCode()^Item3.GetHashCode();
    }
    internal readonly struct NavPortalEndpoints
    {
        internal readonly int Item1,Item2;internal readonly bool Ready;
        internal NavPortalEndpoints(int entry,int exit){Item1=entry;Item2=exit;Ready=true;}
        internal Tuple<int,int> Legacy()=>Ready?Tuple.Create(Item1,Item2):null;
        internal static NavPortalEndpoints FromLegacy(Tuple<int,int> pair)=>pair==null?default:new NavPortalEndpoints(pair.Item1,pair.Item2);
    }
    internal sealed class NavTreeShape<T>
    {internal T[] Keys;internal int[] Left,Right;internal int Root;}
    // Persistent ordered nodes make a prepared update copy only its search
    // path. Key order is preserved separately for dictionary checkpoint bytes.
    internal sealed partial class NavCowDictionary<TKey,TValue> : NavOwnedStorage, IReadOnlyDictionary<TKey,TValue>
    {
        internal sealed partial class Node : NavOwnedStorage
        {
            internal readonly TKey Key;internal readonly TValue Value;internal readonly Node Left,Right;
            internal readonly int Height,Count;
            internal Node(TKey key,TValue value,Node left,Node right)
            :base(NodeBytes)
            {Key=key;Value=value;Left=left;Right=right;Height=1+Math.Max(H(left),H(right));Count=1+C(left)+C(right);}
            private int ownedEdges;
            // Value fallbacks have no active-name or ownership work. Preserve every
            // typed/reference hook and the exact edge flags/allocation sequence.
            internal void AcquireEdges(){if(IsRegistered){if(Left!=null)RequireEdge(Left);if(Right!=null)RequireEdge(Right);}if(NavStoragePayload<TKey>.HasOwnership)NavStoragePayload<TKey>.ValidateActive(Key,this);if(NavStoragePayload<TValue>.HasOwnership)NavStoragePayload<TValue>.ValidateActive(Value,this);Left?.Retain();ownedEdges|=1;Right?.Retain();ownedEdges|=2;if(NavStoragePayload<TKey>.HasOwnership)NavStoragePayload<TKey>.Retain(Key);ownedEdges|=4;if(NavStoragePayload<TValue>.HasOwnership)NavStoragePayload<TValue>.Retain(Value);ownedEdges|=8;}
            protected override void ReleaseEdges(){if((ownedEdges&1)!=0)Left?.Release();if((ownedEdges&2)!=0)Right?.Release();if((ownedEdges&4)!=0&&NavStoragePayload<TKey>.HasOwnership)NavStoragePayload<TKey>.Release(Key);if((ownedEdges&8)!=0&&NavStoragePayload<TValue>.HasOwnership)NavStoragePayload<TValue>.Release(Value);}
        }
        internal static readonly long NodeBytes=NavStorageLayout.NodeBytes<TKey,TValue>();
        private NavStorageAllocation allocation;
        private Node root;
        private readonly IComparer<TKey> comparer;
        internal IComparer<TKey> KeyComparer=>comparer;
        private NavPagedList<TKey> order;
        private bool frozen;
        private Func<long,bool> reserve;
        internal static NavCowDictionary<TKey,TValue> Create(IComparer<TKey> comparer=null,bool insertionOrder=true,Func<long,bool> reserve=null)
        {using(var allocation=new NavStorageAllocation(reserve)){allocation.Reserve(128);return new NavCowDictionary<TKey,TValue>(comparer,insertionOrder,reserve,true);}}
        internal NavCowDictionary(IComparer<TKey> comparer=null,bool insertionOrder=true,Func<long,bool> reserve=null,bool alreadyReserved=false):base(128)
        {this.comparer=comparer??Comparer<TKey>.Default;this.reserve=reserve;using(var allocation=new NavStorageAllocation(reserve)){if(!alreadyReserved)allocation.Reserve(128);allocation.Register(this);Retain();}try{order=insertionOrder?NavPagedList<TKey>.Create(reserve):null;}catch{Release();throw;}}
        internal static NavCowDictionary<TKey,TValue> CreateOwned(INavStorageReservation reservation,IComparer<TKey> comparer=null,bool insertionOrder=true)
        {if(reservation==null)throw new ArgumentNullException(nameof(reservation));return NavCowDictionary<TKey,TValue>.Create(comparer,insertionOrder,reservation.TryReserve);}
        internal NavCowDictionary<TKey,TValue> ForkOwned(INavStorageReservation reservation)
        {NavStorageAdmission.RequireOwned(reservation,this);return ForkPrepared(reservation.TryReserve);}
        private static int H(Node n)=>n?.Height??0;
        private static int C(Node n)=>n?.Count??0;
        private Node Make(TKey key,TValue value,Node left,Node right)
        {allocation.Reserve(NodeBytes);var node=allocation.Register(new Node(key,value,left,right));node.AcquireEdges();return node;}
        private Node Balance(Node n)
        {
            if(H(n.Left)-H(n.Right)>1){
                var left=n.Left;
                if(H(left.Left)<H(left.Right)){var middle=left.Right;left=Make(middle.Key,middle.Value,Make(left.Key,left.Value,left.Left,middle.Left),middle.Right);}
                return Make(left.Key,left.Value,left.Left,Make(n.Key,n.Value,left.Right,n.Right));
            }
            if(H(n.Right)-H(n.Left)>1){
                var right=n.Right;
                if(H(right.Right)<H(right.Left)){var middle=right.Left;right=Make(middle.Key,middle.Value,middle.Left,Make(right.Key,right.Value,middle.Right,right.Right));}
                return Make(right.Key,right.Value,Make(n.Key,n.Value,n.Left,right.Left),right.Right);
            }
            return n;
        }
        private Node Put(Node n,TKey key,TValue value)
        {
            if(n==null)return Make(key,value,null,null);int cmp=comparer.Compare(key,n.Key);
            if(cmp==0)return Make(key,value,n.Left,n.Right);
            return Balance(cmp<0?Make(n.Key,n.Value,Put(n.Left,key,value),n.Right):Make(n.Key,n.Value,n.Left,Put(n.Right,key,value)));
        }
        private Node Delete(Node n,TKey key)
        {
            int cmp=comparer.Compare(key,n.Key);
            if(cmp<0)return Balance(Make(n.Key,n.Value,Delete(n.Left,key),n.Right));
            if(cmp>0)return Balance(Make(n.Key,n.Value,n.Left,Delete(n.Right,key)));
            if(n.Left==null)return n.Right;if(n.Right==null)return n.Left;
            var successor=n.Right;while(successor.Left!=null)successor=successor.Left;
            return Balance(Make(successor.Key,successor.Value,n.Left,Delete(n.Right,successor.Key)));
        }
        private Node DeleteMin(Node n,out TKey minimum)
        {
            if(n.Left==null){minimum=n.Key;return n.Right;}
            return Balance(Make(n.Key,n.Value,DeleteMin(n.Left,out minimum),n.Right));
        }
        internal NavCowDictionary<TKey,TValue> ForkPrepared(Func<long,bool> reserve)
        {
            if(!frozen)throw new InvalidOperationException("Seal committed map before preparing a fork.");
            if(reserve==null)throw new ArgumentException("Missing persistent map admission.");
            var fork=NavCowDictionary<TKey,TValue>.Create(comparer,false,reserve);root?.Retain();fork.root=root;
            try{if(order!=null)fork.order=order.Fork(reserve);return fork;}catch{fork.ReleaseStorage();throw;}
        }
        internal void Seal(){frozen=true;reserve=null;order?.Seal();}
        // Capture reads one immutable entry without allocating an iterator.
        // Preserve the same insertion/sorted order as GetEnumerator.
        internal KeyValuePair<TKey,TValue> EntryAt(int index)
        {
            if((uint)index>=(uint)Count)throw new ArgumentOutOfRangeException(nameof(index));
            if(order!=null){for(int i=0;i<order.Count;i++){var key=order[i];if(TryGetValue(key,out var value)&&index--==0)return new KeyValuePair<TKey,TValue>(key,value);}throw new InvalidOperationException("Dictionary insertion order differs from its count.");}
            var node=root;while(node!=null){int left=C(node.Left);if(index==left)return new KeyValuePair<TKey,TValue>(node.Key,node.Value);if(index<left)node=node.Left;else{index-=left+1;node=node.Right;}}
            throw new InvalidOperationException("Dictionary rank differs from its immutable count.");
        }
        public int Count=>C(root);
        internal long CommittedStorageBytes(int keyBytes)=>checked(StorageBytes+NavStorageLayout.Charge(NodeBytes)*Count+(order?.CommittedStorageBytes(keyBytes)??0));
        // A mutation retains its previous root during preparation. Reserve every
        // path/rotation node separately in the owner; this is a normal-step bound.
        internal long MutationGrowthBytes(int keyBytes,bool insertion=true)=>checked(NavStorageLayout.Charge(NodeBytes)*(9L*H(root)+8L)+(insertion?(order?.AppendGrowthBytes(keyBytes)??0):0));
        public bool TryGetValue(TKey key,out TValue value)
        {
            for(var n=root;n!=null;){int cmp=comparer.Compare(key,n.Key);if(cmp==0){value=n.Value;return true;}n=cmp<0?n.Left:n.Right;}
            value=default;return false;
        }
        // Strict AVL successor: no insertion-order scan and no temporary iterator.
        internal bool TrySuccessor(TKey key,out KeyValuePair<TKey,TValue> result)
        {
            Node found=null;
            for(var n=root;n!=null;)if(comparer.Compare(n.Key,key)>0){found=n;n=n.Left;}else n=n.Right;
            result=found==null?default:new KeyValuePair<TKey,TValue>(found.Key,found.Value);return found!=null;
        }
        public bool ContainsKey(TKey key)=>TryGetValue(key,out _);
        public TValue this[TKey key]
        {
            get{if(!TryGetValue(key,out var value))throw new KeyNotFoundException();return value;}
            set{using var measured=PerformanceProfile.Sample(ProfileStage.TreeMutation);if(frozen)throw new InvalidOperationException("Committed map cannot be mutated.");NavStoragePayload<TKey>.ValidateActive(key,this);NavStoragePayload<TValue>.ValidateActive(value,this);bool added=!ContainsKey(key);try{using(allocation=new NavStorageAllocation(reserve,true)){var next=Put(root,key,value);next?.Retain();var old=root;root=next;old?.Release();}}finally{allocation=null;}if(added)order?.Add(key);}
        }
        public void Add(TKey key,TValue value){if(ContainsKey(key))throw new ArgumentException("Duplicate persistent key.");this[key]=value;}
        internal bool Remove(TKey key){using var measured=PerformanceProfile.Sample(ProfileStage.TreeMutation);if(frozen)throw new InvalidOperationException("Committed map cannot be mutated.");if(!ContainsKey(key))return false;try{using(allocation=new NavStorageAllocation(reserve,true)){var next=Delete(root,key);next?.Retain();var old=root;root=next;old?.Release();}}finally{allocation=null;}return true;}
        // The extracted key is borrowed; the exact frontier caller uses a pure
        // numeric value. Publish it only after the allocation transaction ends.
        internal bool TryTakeMin(out TKey minimum)
        {
            using var measured=PerformanceProfile.Sample(ProfileStage.TreeMutation);
            minimum=default;
            if(frozen)throw new InvalidOperationException("Committed map cannot be mutated.");
            if(root==null)return false;
            TKey extracted;
            try{using(allocation=new NavStorageAllocation(reserve,true)){
                var next=DeleteMin(root,out extracted);next?.Retain();var old=root;root=next;old?.Release();
            }}finally{allocation=null;}
            minimum=extracted;return true;
        }
        internal NavTreeShape<TKey> SaveShape()
        {
            var keys=new List<TKey>();var left=new List<int>();var right=new List<int>();
            int Walk(Node n){if(n==null)return -1;int at=keys.Count;keys.Add(n.Key);left.Add(-1);right.Add(-1);left[at]=Walk(n.Left);right[at]=Walk(n.Right);return at;}
            int index=Walk(root);return new NavTreeShape<TKey>{Keys=keys.ToArray(),Left=left.ToArray(),Right=right.ToArray(),Root=index};
        }
        internal void RestoreShape(NavTreeShape<TKey> shape,TValue value,int maximum)=>RestoreShapeMapped(shape,value,maximum,null);
        internal void RestoreShapeMapped(NavTreeShape<TKey> shape,TValue value,int maximum,Func<TKey,TKey> canonical)
        {
            if(frozen||order!=null||shape==null||shape.Keys==null||shape.Left==null||shape.Right==null||shape.Keys.Length>maximum||shape.Left.Length!=shape.Keys.Length||shape.Right.Length!=shape.Keys.Length||
                (shape.Root==-1)!=(shape.Keys.Length==0)||shape.Root<-1||shape.Root>=shape.Keys.Length)throw new ArgumentException("Invalid persistent tree shape capacity.");
            var seen=new bool[shape.Keys.Length];int visited=0;
            Node Build(int at,int depth,bool hasLower,TKey lower,bool hasUpper,TKey upper){
                if(at==-1)return null;
                if(at<0||at>=seen.Length||seen[at]||depth>64||hasLower&&comparer.Compare(shape.Keys[at],lower)<=0||hasUpper&&comparer.Compare(shape.Keys[at],upper)>=0)throw new ArgumentException("Invalid persistent tree ordering/cycle.");
                seen[at]=true;visited++;var l=Build(shape.Left[at],depth+1,hasLower,lower,true,shape.Keys[at]);var r=Build(shape.Right[at],depth+1,true,shape.Keys[at],hasUpper,upper);
                if(Math.Abs(H(l)-H(r))>1)throw new ArgumentException("Invalid persistent tree balance.");return Make(canonical==null?shape.Keys[at]:canonical(shape.Keys[at]),value,l,r);
            }
            using(allocation=new NavStorageAllocation(reserve)){var restored=Build(shape.Root,0,false,default,false,default);if(visited!=seen.Length)throw new ArgumentException("Orphan persistent tree node.");restored?.Retain();var old=root;root=restored;old?.Release();}allocation=null;
        }
        private bool released;
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){var old=root;root=null;old?.Release();order?.Release();}
        internal NavOwnedStorage PhysicalRoot=>this;
        internal TKey Min{get{var n=root;if(n==null)return default;while(n.Left!=null)n=n.Left;return n.Key;}}
        public IEnumerable<TKey> Keys=>this.Select(p=>p.Key);
        public IEnumerable<TValue> Values=>this.Select(p=>p.Value);
        private IEnumerable<KeyValuePair<TKey,TValue>> Sorted(Node n)
        {if(n==null)yield break;foreach(var p in Sorted(n.Left))yield return p;yield return new KeyValuePair<TKey,TValue>(n.Key,n.Value);foreach(var p in Sorted(n.Right))yield return p;}
        public IEnumerator<KeyValuePair<TKey,TValue>> GetEnumerator()
        {return (order==null?Sorted(root):Insertion()).GetEnumerator();}
        private IEnumerable<KeyValuePair<TKey,TValue>> Insertion(){foreach(var key in order)if(TryGetValue(key,out var value))yield return new KeyValuePair<TKey,TValue>(key,value);}
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
    internal sealed partial class NavCowOrderedSet<T> : NavOwnedStorage, IEnumerable<T>
    {
        private NavCowDictionary<T,bool> values;
        internal static NavCowOrderedSet<T> Create(IComparer<T> comparer=null,Func<long,bool> reserve=null)
        {using(var a=new NavStorageAllocation(reserve)){a.Reserve(64);return new NavCowOrderedSet<T>(comparer,reserve,true);}}
        private static NavCowOrderedSet<T> Create(Func<long,bool> reserve)
        {using(var a=new NavStorageAllocation(reserve)){a.Reserve(64);return new NavCowOrderedSet<T>(reserve,true);}}
        internal NavCowOrderedSet(IComparer<T> comparer=null,Func<long,bool> reserve=null,bool alreadyReserved=false):base(64){using(var allocation=new NavStorageAllocation(reserve)){if(!alreadyReserved)allocation.Reserve(64);allocation.Register(this);values=NavCowDictionary<T,bool>.Create(comparer,false,reserve);Retain();}}
        internal NavCowOrderedSet<T> ForkPrepared(Func<long,bool> reserve)
        {if(reserve==null)throw new ArgumentException("Missing set admission.");var fork=NavCowOrderedSet<T>.Create(reserve);try{fork.values=values.ForkPrepared(reserve);return fork;}catch{fork.ReleaseStorage();throw;}}
        private NavCowOrderedSet(Func<long,bool> reserve,bool alreadyReserved=false):base(64){using(var allocation=new NavStorageAllocation(reserve)){if(!alreadyReserved)allocation.Reserve(64);allocation.Register(this);Retain();}}
        internal NavTreeShape<T> SaveShape()=>values.SaveShape();
        internal void RestoreShape(NavTreeShape<T> shape,int maximum)=>values.RestoreShape(shape,true,maximum);
        internal void RestoreShapeMapped(NavTreeShape<T> shape,int maximum,Func<T,T> canonical)=>values.RestoreShapeMapped(shape,true,maximum,canonical);
        internal void Seal()=>values.Seal();
        private bool released;
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges()=>values?.Release();
        internal int Count=>values.Count;internal T Min=>values.Min;
        internal long CommittedStorageBytes=>StorageBytes+values.CommittedStorageBytes(0);
        internal long MutationGrowthBytes=>values.MutationGrowthBytes(0,false);
        internal bool Contains(T value)=>values.ContainsKey(value);
        internal bool Add(T value){if(Contains(value))return false;values.Add(value,true);return true;}
        internal bool Remove(T value)=>values.Remove(value);
        internal bool TryTakeMin(out T minimum)=>values.TryTakeMin(out minimum);
        internal void UnionWith(IEnumerable<T> items){foreach(var value in items)Add(value);}
        public IEnumerator<T> GetEnumerator()=>values.Keys.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }

    // Each call to Step performs at most one sample/predicate, portal qualification,
    // edge qualification, component lookup, or output materialization. Authoring
    // validation and provider digesting precede this derived graph work.
    public sealed partial class LayeredNavigationCompileWork : NavOwnedStorage
    {
        public enum Stage { SurfaceBounds, Samples, Portals, GridEdges, PortalEdges, PortalArcs, Nodes, SortedArcs, Adjacency, WrapAdjacency, Ready }
        private readonly ILayeredNavigationProvider provider;
        private PartitionNavigationCompileWork partition;
        private readonly NavClearanceProfile clearance;
        private readonly NavGraphProfile profile;
        private readonly NavGraphCompileLimits limits;
        private NavPagedList<NavLocation> samples;
        private NavCowDictionary<NavGridCoordinate,int> grid;
        private NavPagedList<NavPortalEndpoints> portalEnds;
        private NavPagedList<NavConnectorResult> qualifications;
        private NavCowOrderedSet<NavGraphArc> sortedArcs;
        private NavPagedList<NavGraphArc> arcs;
        private NavPagedList<NavGraphNode> nodes;
        private NavCowDictionary<int,NavPagedList<NavGraphArc>> outLists;
        private NavCowDictionary<int,NavPagedList<NavGraphArc>> inLists;
        private NavCowDictionary<int,IReadOnlyList<NavGraphArc>> outgoing;
        private NavCowDictionary<int,IReadOnlyList<NavGraphArc>> incoming;
        private NavCowDictionary<NavNameKey,double> surfaceCosts;
        private NavPagedList<int> parent;
        private int surface, portal, gridCursor, neighbor, end, portalGridCursor, portalArcCursor, nodeCursor, arcCursor, wrapCursor;
        private int gridCount;
        private bool frozen;private Func<long,bool> preparedReservation;private bool released,borrowedGraph;
        private long x, z, minX, maxX, minZ, maxZ, sampled;
        public Stage Phase { get; private set; } = Stage.SurfaceBounds;
        public LayeredNavigationGraph Graph { get; private set; }
        public int Consumed { get; private set; }
        public int SampleCount => partition != null ? partition.SampleCount : samples.Count;
        public int ArcCount => partition != null ? partition.ArcCount : sortedArcs.Count + arcs.Count;
        internal long ProviderRetainedBytes=>provider.EstimatedRetainedBytes;
        internal static long FlatInitialStorageBytes=>6080L+33*NavOwnedStorage.CaptureHeaderBytes;
        private long adjacencyStorageBytes;
        public long EstimatedRetainedBytes => partition != null ? partition.EstimatedRetainedBytes :
            checked(StorageBytes+samples.CommittedStorageBytes(64)+parent.CommittedStorageBytes(4)+
                portalEnds.CommittedStorageBytes(16)+qualifications.CommittedStorageBytes(8)+NavConnectorResult.StorageCharge*qualifications.Count+
                arcs.CommittedStorageBytes(128)+nodes.CommittedStorageBytes(64)+
                grid.CommittedStorageBytes(24)+sortedArcs.CommittedStorageBytes+
                outLists.CommittedStorageBytes(4)+inLists.CommittedStorageBytes(4)+outgoing.CommittedStorageBytes(4)+incoming.CommittedStorageBytes(4)+
                adjacencyStorageBytes+(pendingOutgoing?.CommittedStorageBytes(128)??0)+surfaceCosts.CommittedStorageBytes(8));
        internal long NextGrowthBytes {
            get {
                if(partition!=null)return 4352L;
                switch(Phase){
                    case Stage.SurfaceBounds:return 128L+128L*(surfaceCosts.Count+1);
                    case Stage.Samples:return samples.AppendGrowthBytes(64)+parent.AppendGrowthBytes(4)+grid.MutationGrowthBytes(24);
                    case Stage.Portals:return samples.AppendGrowthBytes(64,2)+parent.AppendGrowthBytes(4,2)+portalEnds.AppendGrowthBytes(16)+qualifications.AppendGrowthBytes(8)+NavConnectorResult.StorageCharge;
                    case Stage.GridEdges:case Stage.PortalEdges:return 2L*sortedArcs.MutationGrowthBytes;
                    case Stage.PortalArcs:return sortedArcs.MutationGrowthBytes;
                    case Stage.Nodes:return nodes.AppendGrowthBytes(64);
                    case Stage.SortedArcs:return sortedArcs.MutationGrowthBytes+arcs.AppendGrowthBytes(128);
                    case Stage.Adjacency:
                        if(arcCursor>=arcs.Count)return 0;
                        var arc=arcs[arcCursor];
                        return AdjacencyGrowth(outLists,arc.From)+AdjacencyGrowth(inLists,arc.To);
                    case Stage.WrapAdjacency:return outgoing.MutationGrowthBytes(4)+incoming.MutationGrowthBytes(4)+128L;
                    default:return 0;
                }
            }
        }
        private static long AdjacencyGrowth(NavCowDictionary<int,NavPagedList<NavGraphArc>> index,int node)
            =>index.MutationGrowthBytes(4)+(index.TryGetValue(node,out var list)?list.AppendGrowthBytes(128):192L+32L+512L+64L);

        private NavOwnedStorage ownedProvider,ownedClearance,ownedProfile,ownedLimits;
        internal ILayeredNavigationProvider PhysicalProvider=>provider;
        public LayeredNavigationCompileWork(ILayeredNavigationProvider provider,NavClearanceProfile clearance,NavGraphProfile profile,NavGraphCompileLimits limits):this(provider,clearance,profile,limits,null,true){}
        private LayeredNavigationCompileWork(ILayeredNavigationProvider provider,NavClearanceProfile clearance,NavGraphProfile profile,NavGraphCompileLimits limits,Func<long,bool> reserve,bool initialize,bool alreadyReserved=false,bool birthOnly=false):base(2048)
        {
            this.provider=provider??throw new ArgumentNullException(nameof(provider));this.clearance=clearance??throw new ArgumentNullException(nameof(clearance));this.profile=profile??throw new ArgumentNullException(nameof(profile));this.limits=limits??throw new ArgumentNullException(nameof(limits));preparedReservation=reserve;
            using(var allocation=new NavStorageAllocation(reserve)){if(!alreadyReserved)allocation.Reserve(2048);allocation.Register(this);if(!birthOnly)Retain();}
            if(provider is INavStorageFacade ownedFacade&&ownedFacade.StorageRoot!=null){ownedProvider=ownedFacade.StorageRoot;ownedProvider.Retain();}
            NavMetadataOwnership.RetainDistinct(this,clearance.MetadataOwner,profile.MetadataOwner,limits.MetadataOwner,out ownedClearance,out ownedProfile,out ownedLimits);
            if(!initialize)return;
            try{
                if(provider is PartitionNavigationProvider actual){partition=new PartitionNavigationCompileWork(actual,clearance,profile,limits,NavStorageLayout.Before(reserve,1024),true,true);return;}
                samples=NavPagedList<NavLocation>.Create(reserve);parent=NavPagedList<int>.Create(reserve);
                portalEnds=NavPagedList<NavPortalEndpoints>.Create(reserve);qualifications=NavPagedList<NavConnectorResult>.Create(reserve);
                arcs=NavPagedList<NavGraphArc>.Create(reserve);nodes=NavPagedList<NavGraphNode>.Create(reserve);
                grid=NavCowDictionary<NavGridCoordinate,int>.Create(insertionOrder:true,reserve:reserve);sortedArcs=NavCowOrderedSet<NavGraphArc>.Create(NavGraphArcComparer.Instance,reserve);
                outLists=NavCowDictionary<int,NavPagedList<NavGraphArc>>.Create(reserve:reserve);inLists=NavCowDictionary<int,NavPagedList<NavGraphArc>>.Create(reserve:reserve);
                outgoing=NavCowDictionary<int,IReadOnlyList<NavGraphArc>>.Create(reserve:reserve);incoming=NavCowDictionary<int,IReadOnlyList<NavGraphArc>>.Create(reserve:reserve);
                surfaceCosts=NavCowDictionary<NavNameKey,double>.Create(insertionOrder:true,reserve:reserve);
            }catch{ReleaseStorage();throw;}
        }
        internal static LayeredNavigationCompileWork CreateOwned(ILayeredNavigationProvider provider,NavClearanceProfile clearance,NavGraphProfile profile,NavGraphCompileLimits limits,INavStorageReservation reservation)
        {NavStorageAdmission.RequireOwned(reservation,(provider as INavStorageFacade)?.StorageRoot);NavMetadataOwnership.RequireGraph(clearance,profile,limits,reservation);return new LayeredNavigationCompileWork(provider,clearance,profile,limits,NavStorageLayout.Before(reservation.TryReserve,2048),true,true);}
        // Explicit boundary for immutable public inputs. Production owners keep
        // the admitted provider in their request/cache root and call CreateOwned.
        private static ILayeredNavigationProvider CopyProviderOwned(ILayeredNavigationProvider source,INavStorageReservation reservation)
        {
            if(source is LayeredNavigationProvider layered)return LayeredNavigationProvider.CopyOwned(layered,reservation);
            if(source is PartitionNavigationProvider partition)return PartitionNavigationProvider.CopyOwned(partition,reservation);
            throw new ArgumentException("Unsupported owned provider recipe.");
        }
        private static bool SameProviderLedger(ILayeredNavigationProvider provider,INavStorageReservation reservation)
        {var root=(provider as INavStorageFacade)?.StorageRoot;return root!=null&&reservation is INavStorageLedgerIdentity identity&&identity.Owns(root);}
        internal static LayeredNavigationCompileWork CopyProviderAndCreateOwned(ILayeredNavigationProvider provider,NavClearanceProfile clearance,NavGraphProfile profile,NavGraphCompileLimits limits,INavStorageReservation reservation)
            =>CopyBoundary(provider,clearance,profile,limits,null,false,reservation);
        internal static LayeredNavigationCompileWork CopyProviderAndRestoreOwned(ILayeredNavigationProvider provider,NavClearanceProfile clearance,NavGraphProfile profile,NavGraphCompileLimits limits,Checkpoint state,INavStorageReservation reservation)
            =>CopyBoundary(provider,clearance,profile,limits,state,true,reservation);
        private static LayeredNavigationCompileWork CopyBoundary(ILayeredNavigationProvider provider,NavClearanceProfile clearance,NavGraphProfile profile,NavGraphCompileLimits limits,Checkpoint state,bool restore,INavStorageReservation reservation)
        {
            if(reservation==null)throw new ArgumentNullException(nameof(reservation));
            if(provider==null)throw new ArgumentNullException(nameof(provider));
            var suppliedProviderRoot=(provider as INavStorageFacade)?.StorageRoot;
            if(suppliedProviderRoot!=null)NavStorageAdmission.RequireOwned(reservation,suppliedProviderRoot);
            NavMetadataOwnership.CheckCopyInputs(clearance,profile,limits,reservation);if(restore)RequireCheckpointNames(state,reservation);
            NavGraphMetadata metadata=null;ILayeredNavigationProvider copy=null;
            try{
                metadata=NavGraphMetadata.CopyUnowned(clearance,profile,limits,reservation);
                if(clearance.MetadataOwner==null)clearance=metadata.Clearance;if(profile.MetadataOwner==null)profile=metadata.Profile;if(limits.MetadataOwner==null)limits=metadata.Limits;
                if(!SameProviderLedger(provider,reservation))copy=CopyProviderOwned(provider,reservation);
                var owned=copy??provider;
                return restore?RestoreOwned(owned,clearance,profile,limits,state,reservation):CreateOwned(owned,clearance,profile,limits,reservation);
            }finally{metadata?.Release();if(copy!=null)((INavStorageFacade)copy).StorageRoot.Release();}
        }
        internal LayeredNavigationCompileWork ForkOwned(INavStorageReservation reservation)
        {NavStorageAdmission.RequireOwned(reservation,this);return ForkPrepared(reservation.TryReserve);}
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){pendingOutgoing?.ReleaseStorage();ownedClearance?.Release();ownedProfile?.Release();ownedLimits?.Release();ownedProvider?.Release();partition?.Release();samples?.Release();parent?.Release();portalEnds?.Release();qualifications?.Release();arcs?.Release();nodes?.Release();grid?.Release();sortedArcs?.Release();outLists?.Release();inLists?.Release();outgoing?.Release();incoming?.Release();surfaceCosts?.Release();if(partition==null)Graph?.Release();}
        internal NavOwnedStorage PhysicalRoot=>this;

        public int Advance(int quota)
        {
            if (quota < 0) throw new ArgumentOutOfRangeException(nameof(quota));
            if(frozen&&Phase!=Stage.Ready)throw new InvalidOperationException("Committed compiler cannot be advanced.");
            if (partition != null) {
                int done = partition.Advance(quota);
                Consumed = partition.Consumed; Graph = partition.Graph;
                Phase = partition.Phase == PartitionNavigationCompileWork.Stage.Ready ? Stage.Ready :
                    partition.Phase == PartitionNavigationCompileWork.Stage.Arcs ? Stage.GridEdges :
                    partition.Phase == PartitionNavigationCompileWork.Stage.Components ? Stage.Nodes : Stage.Samples;
                if(Phase==Stage.Ready)preparedReservation=null;return done;
            }
            int used = 0;
            while (used < quota && Phase != Stage.Ready) { Step(); used++; Consumed++; }
            return used;
        }
        internal LayeredNavigationCompileWork ForkPartitionPrepared(Func<long,bool> reserve)
        {if(partition==null)throw new InvalidOperationException("Partition prepared compiler required.");return ForkPrepared(reserve);}
        internal LayeredNavigationCompileWork ForkPrepared(Func<long,bool> reserve)
        {
            if(!frozen)throw new InvalidOperationException("Seal committed compiler before preparing a fork.");if(reserve==null)throw new ArgumentException("Missing compiler admission.");
            var fork=new LayeredNavigationCompileWork(provider,clearance,profile,limits,NavStorageLayout.Before(reserve,2048),false,true){surface=surface,portal=portal,gridCursor=gridCursor,neighbor=neighbor,end=end,portalGridCursor=portalGridCursor,portalArcCursor=portalArcCursor,nodeCursor=nodeCursor,arcCursor=arcCursor,wrapCursor=wrapCursor,gridCount=gridCount,x=x,z=z,minX=minX,maxX=maxX,minZ=minZ,maxZ=maxZ,sampled=sampled,Phase=Phase,Consumed=Consumed,adjacencyStorageBytes=adjacencyStorageBytes};
            try{
                if(partition!=null){fork.partition=partition.ForkPrepared(reserve);fork.Graph=fork.partition.Graph;return fork;}
                fork.samples=samples.Fork(reserve);fork.portalEnds=portalEnds.Fork(reserve);fork.qualifications=qualifications.Fork(reserve);fork.arcs=arcs.Fork(reserve);fork.nodes=nodes.Fork(reserve);fork.parent=parent.Fork(reserve);
                fork.grid=grid.ForkPrepared(reserve);fork.sortedArcs=sortedArcs.ForkPrepared(reserve);fork.outLists=outLists.ForkPrepared(reserve);fork.inLists=inLists.ForkPrepared(reserve);fork.outgoing=outgoing.ForkPrepared(reserve);fork.incoming=incoming.ForkPrepared(reserve);fork.surfaceCosts=surfaceCosts.ForkPrepared(reserve);if(Graph!=null){Graph.Retain();fork.Graph=Graph;fork.borrowedGraph=true;}return fork;
            }catch{fork.ReleaseStorage();throw;}
        }
        private static readonly Func<long,bool> Unbudgeted=n=>true;
        // Only the prepared compiler owns this mutable list. It is never
        // inserted into a published map until sealed. Retaining it across
        // Advance(1) calls avoids a new list/page/tree path for every edge.
        private NavPagedList<NavGraphArc> pendingOutgoing;
        private int pendingOutgoingNode;
        private void AppendOutgoing(NavGraphArc arc)
        {
            if (pendingOutgoing != null && pendingOutgoingNode != arc.From) FlushOutgoing();
            if (pendingOutgoing == null) {
                pendingOutgoing = outLists.TryGetValue(arc.From, out var prior)
                    ? prior.Fork(preparedReservation ?? Unbudgeted)
                    : NavPagedList<NavGraphArc>.Create(preparedReservation, 4);
                pendingOutgoingNode = arc.From;
            }
            pendingOutgoing.Add(arc);
        }
        private void FlushOutgoing()
        {
            if (pendingOutgoing == null) return;
            long prior = outLists.TryGetValue(pendingOutgoingNode, out var old) ? old.CommittedStorageBytes(128) : 0;
            pendingOutgoing.Seal();
            outLists[pendingOutgoingNode] = pendingOutgoing;
            adjacencyStorageBytes += pendingOutgoing.CommittedStorageBytes(128) - prior;
            pendingOutgoing.ReleaseStorage(); pendingOutgoing = null; pendingOutgoingNode = 0;
        }
        // A region exists only for one adjacency-list mutation. Immutable map
        // nodes keep their original allocator and their existing local journal.
        private sealed class AdjacencyBirthRegion : INavStorageReservation, INavStorageLedgerIdentity, IDisposable
        {
            // x64: four references + bool + 16-byte object header <=64.
            // Unity Mono Delegate has twelve pointer/reference fields + bool;
            // MulticastDelegate adds one reference: aligned header+fields =128.
            // Direct method-group construction has no DelegateData/invocation array.
            internal const long RegionBytes=64, DelegateBytes=128;
            private Func<long,bool> parent;
            private IDisposable scratch;
            private NavOwnedStorage births;
            internal readonly Func<long,bool> Reserve;
            private bool disposed;
            private AdjacencyBirthRegion(Func<long,bool> parent,IDisposable scratch)
            {this.parent=parent;this.scratch=scratch;Reserve=TryReserve;}
            internal static AdjacencyBirthRegion Open(Func<long,bool> parent,NavOwnedStorage index)
            {
                var target=parent?.Target;
                // This opaque adapter advertises optional capabilities even when
                // its wrapped allocator lacks them. Preserve its legacy path.
                if(target is NavStorageReservation||!(target is INavStorageLocalBirthAllocator)||
                    !(target is INavStorageScratchAllocator)||!(target is INavStorageReservation owner)||
                    !(target is INavStorageLedgerIdentity))return null;
                NavStorageAdmission.RequireOwned(owner,index);
                var scratch=NavStorageScratch.Reserve(parent,RegionBytes+DelegateBytes);
                try{return new AdjacencyBirthRegion(parent,scratch);}catch{scratch.Dispose();throw;}
            }
            public bool TryReserve(long bytes)
            {if(disposed)throw new InvalidOperationException("Adjacency birth region is closed.");return parent(bytes);}
            public void Register(NavOwnedStorage storage)
            {
                if(disposed)throw new InvalidOperationException("Adjacency birth region is closed.");
                var target=parent.Target;
                if(((INavStorageLocalBirthAllocator)target).TryRegisterLocalBirth(storage)){
                    storage.BirthNext=births;births=storage;
                }else ((INavStorageAllocator)target).Register(storage);
            }
            public object Identity=>((INavStorageLedgerIdentity)parent.Target).Identity;
            public bool Owns(NavOwnedStorage storage)=>((INavStorageLedgerIdentity)parent.Target).Owns(storage);
            public void Dispose()
            {
                if(disposed)return;disposed=true;
                try{while(births!=null){var next=births.BirthNext;births.BirthNext=null;births.Release();births=next;}}
                finally{parent=null;var value=scratch;scratch=null;value.Dispose();}
            }
        }
        private void AppendAdjacency(NavCowDictionary<int,NavPagedList<NavGraphArc>> index,int node,NavGraphArc arc)
        {
            bool existed=index.TryGetValue(node,out var priorList);long prior=existed?priorList.CommittedStorageBytes(128):0;
            using(var region=AdjacencyBirthRegion.Open(preparedReservation,index)){
                var reserve=region?.Reserve??preparedReservation;
                NavPagedList<NavGraphArc> list=null;
                try{
                    list=existed?priorList.Fork(reserve??Unbudgeted):NavPagedList<NavGraphArc>.Create(reserve,4);
                    list.Add(arc);list.Seal();index[node]=list;adjacencyStorageBytes+=list.CommittedStorageBytes(128)-prior;
                }finally{list?.ReleaseStorage();}
            }
        }
        internal void SealPrepared()
        {
            if(frozen)return;
            FlushOutgoing();
            if(partition!=null)partition.SealPrepared();
            else{samples.Seal();portalEnds.Seal();qualifications.Seal();arcs.Seal();nodes.Seal();parent.Seal();grid.Seal();sortedArcs.Seal();outLists.Seal();inLists.Seal();outgoing.Seal();incoming.Seal();surfaceCosts.Seal();}
            preparedReservation=null;frozen=true;
        }
        private sealed class GridComparer : IComparer<NavGridCoordinate>
        {public int Compare(NavGridCoordinate a,NavGridCoordinate b){int c=StringComparer.Ordinal.Compare(a.Item1,b.Item1);if(c!=0)return c;c=a.Item2.CompareTo(b.Item2);return c!=0?c:a.Item3.CompareTo(b.Item3);}}
        // Versioned, authority-neutral continuation. The provider/profile are
        // external immutable inputs and must match the exact captured binding.
        public sealed class Checkpoint
        {
            public int Version { get; internal set; }
            public string GraphBinding { get; internal set; }
            public Stage Phase { get; internal set; }
            internal NavLocation[] Samples;
            internal int[] Parent;
            internal Tuple<int, int>[] PortalEnds;
            internal NavConnectorResult[] Qualifications;
            internal NavGraphArc[] PendingArcs, MaterializedArcs;
            internal NavTreeShape<NavGraphArc> PendingArcShape;internal KeyValuePair<Tuple<string,long,long>,int>[] GridEntries;
            internal int Surface, Portal, GridCursor, Neighbor, End, PortalGridCursor, PortalArcCursor;
            internal int NodeCursor, ArcCursor, WrapCursor, GridCount, Consumed;
            internal long X, Z, MinX, MaxX, MinZ, MaxZ, Sampled;
            internal PartitionNavigationCompileWork.Checkpoint Partition;
        }
        public Checkpoint Save()
        {
            if (partition != null) return new Checkpoint {
                Version = 2, GraphBinding = new NavGraphBinding(provider.Binding, clearance, profile).StableKey,
                Phase = Phase, Consumed = Consumed, Partition = partition.Save()
            };
            var detachedShape=sortedArcs.SaveShape();detachedShape.Keys=detachedShape.Keys.Select(arc=>arc.Detached()).ToArray();
            return new Checkpoint {
                Version = 2, GraphBinding = new NavGraphBinding(provider.Binding, clearance, profile).StableKey,
                Phase = Phase, Samples = samples.Select(location=>location.Detached()).ToArray(), Parent = parent.ToArray(),
                PortalEnds = portalEnds.Select(pair=>pair.Legacy()).ToArray(), Qualifications = qualifications.Select(q=>q.Detached()).ToArray(),
                PendingArcs = sortedArcs.Select(arc=>arc.Detached()).ToArray(), MaterializedArcs = arcs.Select(arc=>arc.Detached()).ToArray(),PendingArcShape=detachedShape,GridEntries=grid.Select(pair=>new KeyValuePair<Tuple<string,long,long>,int>(Tuple.Create(pair.Key.Item1,pair.Key.Item2,pair.Key.Item3),pair.Value)).ToArray(),
                Surface = surface, Portal = portal, GridCursor = gridCursor, Neighbor = neighbor,
                End = end, PortalGridCursor = portalGridCursor, PortalArcCursor = portalArcCursor,
                NodeCursor = nodeCursor, ArcCursor = arcCursor, WrapCursor = wrapCursor,
                GridCount = gridCount, Consumed = Consumed, X = x, Z = z, MinX = minX,
                MaxX = maxX, MinZ = minZ, MaxZ = maxZ, Sampled = sampled
            };
        }
        public static LayeredNavigationCompileWork Restore(ILayeredNavigationProvider provider, NavClearanceProfile clearance,
            NavGraphProfile profile, NavGraphCompileLimits limits, Checkpoint state)=>RestoreCore(provider,clearance,profile,limits,state,null);
        internal static LayeredNavigationCompileWork RestoreOwned(ILayeredNavigationProvider provider,NavClearanceProfile clearance,NavGraphProfile profile,NavGraphCompileLimits limits,Checkpoint state,INavStorageReservation reservation)
        {NavStorageAdmission.RequireOwned(reservation,(provider as INavStorageFacade)?.StorageRoot);NavMetadataOwnership.RequireGraph(clearance,profile,limits,reservation);RequireCheckpointNames(state,reservation);return RestoreCore(provider,clearance,profile,limits,state,reservation.TryReserve);}
        private static void RequireCheckpointNames(Checkpoint state,INavStorageReservation reservation)
        {
            if(state==null)return;
            void Arc(NavGraphArc arc){if(arc.NameReference.Owner!=null)arc.NameReference.Require(reservation,arc.PortalId);if(arc.Trace is NavOwnedStorage owner)NavStorageAdmission.RequireOwned(reservation,owner);if(arc.Trace!=null)foreach(var leg in arc.Trace){if(leg==null)throw new ArgumentException("Null checkpoint leg.");if(leg.IsRegistered)NavStorageAdmission.RequireOwned(reservation,leg);leg.From.RequireName(reservation);leg.To.RequireName(reservation);}}
            if(state.Samples!=null)foreach(var location in state.Samples)location.RequireName(reservation);
            if(state.PendingArcs!=null)foreach(var arc in state.PendingArcs)Arc(arc);
            if(state.MaterializedArcs!=null)foreach(var arc in state.MaterializedArcs)Arc(arc);
            if(state.PendingArcShape?.Keys!=null)foreach(var arc in state.PendingArcShape.Keys)Arc(arc);
            if(state.Qualifications!=null)foreach(var q in state.Qualifications){if(q==null)continue;if(q.IsRegistered)NavStorageAdmission.RequireOwned(reservation,q);q.Origin.RequireName(reservation);q.Entry.RequireName(reservation);q.Exit.RequireName(reservation);q.Endpoint.RequireName(reservation);}
            if(state.Partition?.Locations!=null)foreach(var location in state.Partition.Locations)location.RequireName(reservation);
            if(state.Partition?.Traces!=null)foreach(var trace in state.Partition.Traces)if(trace!=null)foreach(var leg in trace){if(leg==null)throw new ArgumentException("Null partition checkpoint leg.");if(leg.IsRegistered)NavStorageAdmission.RequireOwned(reservation,leg);leg.From.RequireName(reservation);leg.To.RequireName(reservation);}
        }
        private static LayeredNavigationCompileWork RestoreCore(ILayeredNavigationProvider provider,NavClearanceProfile clearance,NavGraphProfile profile,NavGraphCompileLimits limits,Checkpoint state,Func<long,bool> reserve)
        {
            if (state == null || (state.Version != 1&&state.Version!=2) || state.GraphBinding != new NavGraphBinding(provider.Binding, clearance, profile).StableKey)
                throw new ArgumentException("Stale graph compilation checkpoint.");
            var work = new LayeredNavigationCompileWork(provider,clearance,profile,limits,NavStorageLayout.Before(reserve,2048),state.Partition==null,true);
            try{
            if (state.Partition != null) {
                if (!(provider is PartitionNavigationProvider actual)) throw new ArgumentException("Partition provider required.");
                work.partition = PartitionNavigationCompileWork.Restore(actual, clearance, profile, limits, state.Partition,reserve);
                work.Phase = state.Phase; work.Consumed = state.Consumed; work.Graph = work.partition.Graph;
                return work;
            }
            if (provider is PartitionNavigationProvider) throw new ArgumentException("Partition checkpoint required.");
            if (state.Samples.Length > limits.MaxNodes || state.MaterializedArcs.Length + state.PendingArcs.Length > limits.MaxArcs ||
                state.Parent.Length != state.Samples.Length || state.GridCount > state.Samples.Length || state.PortalEnds.Length > provider.Portals.Count)
                throw new ArgumentException("Invalid graph compilation checkpoint capacity.");
            work.Phase = state.Phase;work.samples.AddRange(state.Samples.Select(location=>reserve==null?location:CanonicalLocation(provider,location)));work.parent.AddRange(state.Parent);
            work.portalEnds.AddRange(state.PortalEnds.Select(NavPortalEndpoints.FromLegacy));
            foreach(var q in state.Qualifications){
                if(reserve==null){work.qualifications.Add(q);continue;}
                if(q==null||q.ProviderBinding!=provider.Binding)throw new ArgumentException("Invalid qualification provider binding.");
                string portal=CanonicalPortal(provider,q.PortalId);
                var copy=NavConnectorResult.CreateOwned(q.Status,provider.Binding,portal,CanonicalLocation(provider,q.Origin),CanonicalLocation(provider,q.Entry),CanonicalLocation(provider,q.Exit),CanonicalLocation(provider,q.Endpoint),q.Cost,reserve,provider);
                try{work.qualifications.Add(copy);}finally{copy.ReleaseOwner();}
            }
            if(reserve!=null&&(state.PendingArcs.Any(arc=>arc.Trace!=null)||state.MaterializedArcs.Any(arc=>arc.Trace!=null)))throw new ArgumentException("Unexpected partition trace in flat checkpoint.");
            if(state.Version==2){work.sortedArcs.RestoreShapeMapped(state.PendingArcShape,limits.MaxArcs,reserve==null?(Func<NavGraphArc,NavGraphArc>)null:arc=>CanonicalArc(provider,arc));if(!work.sortedArcs.SequenceEqual(state.PendingArcs))throw new ArgumentException("Pending arc tree/frontier mismatch.");}
            else work.sortedArcs.UnionWith(state.PendingArcs.Select(arc=>reserve==null?arc:CanonicalArc(provider,arc)));work.arcs.AddRange(state.MaterializedArcs.Select(arc=>reserve==null?arc:CanonicalArc(provider,arc)));
            work.surface = state.Surface; work.portal = state.Portal; work.gridCursor = state.GridCursor;
            work.neighbor = state.Neighbor; work.end = state.End; work.portalGridCursor = state.PortalGridCursor;
            work.portalArcCursor = state.PortalArcCursor; work.nodeCursor = state.NodeCursor;
            work.arcCursor = state.ArcCursor; work.wrapCursor = state.WrapCursor;
            work.gridCount = state.GridCount; work.Consumed = state.Consumed;
            work.x = state.X; work.z = state.Z; work.minX = state.MinX; work.maxX = state.MaxX;
            work.minZ = state.MinZ; work.maxZ = state.MaxZ; work.sampled = state.Sampled;
            // SurfaceBounds has not entered the next surface yet; Samples has.
            // Restoring future costs changes the next prepared-root reservation.
            if (!Enum.IsDefined(typeof(Stage), state.Phase) || state.Surface < 0 ||
                state.Surface > provider.Surfaces.Count || state.Phase == Stage.Samples && state.Surface == provider.Surfaces.Count ||
                state.Phase > Stage.Samples && state.Surface != provider.Surfaces.Count)
                throw new ArgumentException("Invalid surface continuation cursor.");
            int costCount = state.Phase == Stage.SurfaceBounds ? state.Surface :
                state.Phase == Stage.Samples ? state.Surface + 1 : provider.Surfaces.Count;
            for (int i = 0; i < costCount; i++) {
                var s = provider.Surfaces[i]; work.surfaceCosts.Add(NavNameKey.Surface(s), s.DistanceCost);
            }
            if(state.Version==2){
                if(state.GridEntries==null||state.GridEntries.Length>state.Samples.Length||state.Phase>Stage.Portals&&state.GridEntries.Length!=state.GridCount)throw new ArgumentException("Invalid exact grid checkpoint.");
                foreach(var pair in state.GridEntries){
                    if(pair.Key==null||pair.Value<0||pair.Value>=state.Samples.Length||state.Samples[pair.Value].SurfaceId!=pair.Key.Item1||
                        !state.Samples[pair.Value].Position.Equals(new NavPoint(pair.Key.Item2*profile.GridCell,pair.Key.Item3*profile.GridCell)))throw new ArgumentException("Invalid exact grid entry.");
                    work.grid.Add(new NavGridCoordinate(work.samples[pair.Value],pair.Key.Item2,pair.Key.Item3),pair.Value);
                }
            }else{
                int restoredGridCount = state.Phase <= Stage.Portals ? state.Samples.Length : state.GridCount;
                for (int i = 0; i < restoredGridCount; i++) {
                    var location = state.Samples[i];long gx=(long)Math.Round(location.Position.X/profile.GridCell),gz=(long)Math.Round(location.Position.Z/profile.GridCell);
                    work.grid.Add(new NavGridCoordinate(work.samples[i],gx,gz),i);
                }
            }
            for (int i = 0; i < state.NodeCursor; i++) work.nodes.Add(new NavGraphNode(i, work.samples[i], work.Root(i)));
            for (int i = 0; i < state.ArcCursor; i++) {
                var a = work.arcs[i];
                work.AppendAdjacency(work.outLists,a.From,a);work.AppendAdjacency(work.inLists,a.To,a);
            }
            for (int i = 0; i < state.WrapCursor; i++) {
                if (work.outLists.TryGetValue(i, out var outs)) work.outgoing[i] = outs;
                if (work.inLists.TryGetValue(i, out var ins)) work.incoming[i] = ins;
            }
            if(state.Phase==Stage.Ready){work.SealPrepared();work.Graph=LayeredNavigationGraph.FromPrepared(provider,
                NavGraphBinding.CreateAdmitted(provider,clearance,profile,reserve),work.nodes,work.arcs,work.qualifications,work.outgoing,work.incoming,work.surfaceCosts,reserve);}
            return work;
            }catch{work.ReleaseStorage();throw;}
        }
        internal static NavLocation CanonicalLocation(ILayeredNavigationProvider provider,NavLocation location)
        {if(location.SurfaceId==null)return location;foreach(var surface in provider.Surfaces)if(surface.Id==location.SurfaceId)return surface.OwnedLocation(location.Position);throw new ArgumentException("Unknown checkpoint surface.");}
        private static NavGraphArc CanonicalArc(ILayeredNavigationProvider provider,NavGraphArc arc)
        {
            if(arc.NameReference.Owner!=null&&!ReferenceEquals(arc.PortalId,arc.NameReference.Resolve()))throw new ArgumentException("Contradictory checkpoint arc name.");
            string id=CanonicalPortal(provider,arc.PortalId);NavNameReference name=default;
            if(id!=null)foreach(var portal in provider.Portals)if(ReferenceEquals(portal.Id,id)&&portal.StorageRoot!=null){name=new NavNameReference(portal.StorageRoot,NavNameRole.PortalId);break;}
            return new NavGraphArc(arc.From,arc.To,arc.Cost,arc.Kind,id,null,name);
        }
        private static string CanonicalPortal(ILayeredNavigationProvider provider,string id)
        {if(id==null)return null;foreach(var portal in provider.Portals)if(portal.Id==id)return portal.Id;throw new ArgumentException("Unknown checkpoint portal.");}
        private void AddArc(int from, int to, double cost, NavGraphArcKind kind, string portalId = null)
        {
            var arc = CanonicalArc(provider,new NavGraphArc(from, to, cost, kind, portalId));
            if (sortedArcs.Contains(arc)) return;
            if (sortedArcs.Count >= limits.MaxArcs) throw new ArgumentException("Graph arc capacity exceeded.");
            sortedArcs.Add(arc);
            int a = Root(from), b = Root(to);
            if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
        }
        private int Root(int n)
        {
            while (parent[n] != n) { parent[n] = parent[parent[n]]; n = parent[n]; }
            return n;
        }
        private int AddSample(NavLocation location)
        {
            if (samples.Count >= limits.MaxNodes) throw new ArgumentException("Graph node capacity exceeded.");
            int id = samples.Count; samples.Add(location); parent.Add(id); return id;
        }
        private void SurfaceArc(int a, int b)
        {
            var from = samples[a]; var to = samples[b];
            if (!provider.CanSweep(from, to, clearance)) return;
            double cost = LayeredNavigationProvider.Distance(from.Position, to.Position) * surfaceCosts[new NavNameKey(from.SurfaceId)];
            AddArc(a, b, cost, NavGraphArcKind.Surface);
            AddArc(b, a, cost, NavGraphArcKind.Surface);
        }
        private void Step()
        {
            if (Phase == Stage.SurfaceBounds) {
                if (surface == provider.Surfaces.Count) { Phase = Stage.Portals; return; }
                var s = provider.Surfaces[surface]; surfaceCosts[NavNameKey.Surface(s)] = s.DistanceCost;
                double a = Math.Ceiling(s.Area.Boundary.MinX / profile.GridCell), b = Math.Floor(s.Area.Boundary.MaxX / profile.GridCell);
                double c = Math.Ceiling(s.Area.Boundary.MinZ / profile.GridCell), d = Math.Floor(s.Area.Boundary.MaxZ / profile.GridCell);
                double count = (b - a + 1) * (d - c + 1);
                if (a < -9007199254740991d || b > 9007199254740991d || c < -9007199254740991d || d > 9007199254740991d ||
                    double.IsNaN(count) || double.IsInfinity(count) || count > limits.MaxSamples - sampled)
                    throw new ArgumentException("Graph sampling capacity exceeded.");
                if (b < a || d < c) { surface++; return; }
                minX = x = (long)a; maxX = (long)b; minZ = z = (long)c; maxZ = (long)d;
                sampled += (long)count; Phase = Stage.Samples; return;
            }
            if (Phase == Stage.Samples) {
                var s = provider.Surfaces[surface];
                var location = s.OwnedLocation(new NavPoint(x * profile.GridCell, z * profile.GridCell));
                if (provider.IsValid(location, clearance)) grid.Add(new NavGridCoordinate(location,x,z),AddSample(location));
                if (++z > maxZ) { z = minZ; x++; }
                if (x > maxX) { surface++; Phase = Stage.SurfaceBounds; }
                return;
            }
            if (Phase == Stage.Portals) {
                if (portal == provider.Portals.Count) { gridCount = grid.Count; Phase = Stage.GridEdges; return; }
                var p = provider.Portals[portal++]; NavConnectorResult q;
                if(provider is LayeredNavigationProvider actual)q=actual.QualifyPortalOwned(p.Id,clearance,preparedReservation);
                else{
                    // Third-party qualification is scratch, admitted before calling
                    // its public factory; retained storage is always a private copy.
                    if(preparedReservation!=null&&!preparedReservation(NavConnectorResult.StorageCharge))throw new InvalidOperationException("Qualification scratch exceeds prepared reservation.");
                    q=NavConnectorResult.CopyOwned(provider.QualifyPortal(p.Id,clearance),preparedReservation,provider);
                }
                try{
                    qualifications.Add(q);
                    portalEnds.Add(q.Status==NavConnectorStatus.Ready?new NavPortalEndpoints(AddSample(p.Entry),AddSample(p.Exit)):default);
                }finally{q.ReleaseOwner();}
                return;
            }
            if (Phase == Stage.GridEdges) {
                if (gridCursor == gridCount) { Phase = Stage.PortalEdges; portal = 0; return; }
                var location = samples[gridCursor];
                long gx = (long)Math.Round(location.Position.X / profile.GridCell), gz = (long)Math.Round(location.Position.Z / profile.GridCell);
                var key = neighbor==0?new NavGridCoordinate(location,gx+1,gz):new NavGridCoordinate(location,gx,gz+1);
                if (grid.TryGetValue(key, out int other)) SurfaceArc(gridCursor, other);
                if (++neighbor == 2) { neighbor = 0; gridCursor++; }
                return;
            }
            if (Phase == Stage.PortalEdges) {
                if (portal == provider.Portals.Count) { Phase = Stage.PortalArcs; portal = 0; return; }
                var pair = portalEnds[portal];
                if (!pair.Ready) { portal++; return; }
                int endpoint = end == 0 ? pair.Item1 : pair.Item2;
                var location = samples[endpoint];
                if (portalGridCursor == gridCount) {
                    portalGridCursor = 0;
                    if (++end == 2) { end = 0; portal++; }
                    return;
                }
                var sample = samples[portalGridCursor++];
                if (sample.SurfaceId == location.SurfaceId &&
                    LayeredNavigationProvider.Distance(sample.Position, location.Position) <= Math.Sqrt(2) * profile.GridCell)
                    SurfaceArc(endpoint, portalGridCursor - 1);
                return;
            }
            if (Phase == Stage.PortalArcs) {
                if (portal == provider.Portals.Count) { Phase = Stage.Nodes; return; }
                var pair = portalEnds[portal]; var p = provider.Portals[portal];
                if (pair.Ready) {
                    if (portalArcCursor == 0) AddArc(pair.Item1, pair.Item2, p.TraversalCost, NavGraphArcKind.Portal, p.Id);
                    else if (p.Direction == NavPortalDirection.Both) AddArc(pair.Item2, pair.Item1, p.TraversalCost, NavGraphArcKind.Portal, p.Id);
                }
                if (++portalArcCursor == 2) { portalArcCursor = 0; portal++; }
                return;
            }
            if (Phase == Stage.Nodes) {
                if (nodeCursor == samples.Count) { Phase = Stage.SortedArcs; return; }
                nodes.Add(new NavGraphNode(nodeCursor, samples[nodeCursor], Root(nodeCursor))); nodeCursor++;
                return;
            }
            if (Phase == Stage.SortedArcs) {
                if (sortedArcs.Count == 0) { Phase = Stage.Adjacency; return; }
                var a = sortedArcs.Min; sortedArcs.Remove(a); arcs.Add(a); return;
            }
            if (Phase == Stage.Adjacency) {
                if (arcCursor == arcs.Count) { FlushOutgoing(); Phase = Stage.WrapAdjacency; return; }
                var a = arcs[arcCursor++];
                AppendOutgoing(a);AppendAdjacency(inLists,a.To,a); return;
            }
            if (Phase == Stage.WrapAdjacency) {
                if (wrapCursor == nodes.Count) {
                    samples.Seal();portalEnds.Seal();qualifications.Seal();arcs.Seal();nodes.Seal();parent.Seal();grid.Seal();sortedArcs.Seal();
                    outLists.Seal();inLists.Seal();outgoing.Seal();incoming.Seal();surfaceCosts.Seal();
                    Graph = LayeredNavigationGraph.FromPrepared(provider, NavGraphBinding.CreateAdmitted(provider,clearance,profile,preparedReservation),
                        nodes, arcs,
                        qualifications, outgoing, incoming, surfaceCosts,preparedReservation);
                    Phase = Stage.Ready;preparedReservation=null; return;
                }
                if (outLists.TryGetValue(wrapCursor, out var outs)) outgoing[wrapCursor] = outs;
                if (inLists.TryGetValue(wrapCursor, out var ins)) incoming[wrapCursor] = ins;
                wrapCursor++; return;
            }
        }
    }
}
