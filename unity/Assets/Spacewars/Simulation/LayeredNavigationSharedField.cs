using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    internal struct NavPackedFieldCell {internal double Distance;internal NavGraphArc Next;internal byte Flags;}
    internal sealed partial class NavPackedFieldStorage : NavOwnedStorage
    {
        private const int LabelsPerPage=64;
        internal static readonly int CellBytes=Math.Max(64,NavStorageLayout.ValueBytes(typeof(NavPackedFieldCell)));
        internal sealed class SavedPage {internal int Index;internal double[] Distance;internal NavGraphArc[] Next;internal byte[] Flags;}
        private NavStorageDirectory<NavPackedFieldCell> pages;
        private bool frozen,released;
        private Func<long,bool> reserveCopy;
        internal static NavPackedFieldStorage Create(int capacity,Func<long,bool> reserve=null)
        {using(var allocation=new NavStorageAllocation(reserve)){allocation.Reserve(256);return new NavPackedFieldStorage(capacity,reserve,true);}}
        internal NavPackedFieldStorage(int capacity,Func<long,bool> reserve=null,bool alreadyReserved=false):base(256)
        {
            if(capacity<0)throw new ArgumentException("Invalid packed field capacity.");Capacity=capacity;reserveCopy=reserve;
            using(var allocation=new NavStorageAllocation(reserve)){if(!alreadyReserved)allocation.Reserve(256);allocation.Register(this);pages=NavStorageDirectory<NavPackedFieldCell>.Create(reserve);pages.InitializeSlots((int)(((long)capacity+LabelsPerPage-1)/LabelsPerPage));Retain();}
        }
        internal int Capacity {get;}
        internal int Count {get;private set;}
        internal static long RootStorageBytes(long capacity)=>NavStorageLayout.Charge(256)+NavStorageLayout.Charge(128);
        internal long EstimatedRetainedBytes=>RootStorageBytes(Capacity)+pages.DirectoryBytes+NavStorageLayout.Charge(96L+(long)CellBytes*LabelsPerPage)*pages.AllocatedPages;
        internal NavPackedFieldStorage ForkPrepared(Func<long,bool> reserve)
        {if(!frozen)throw new InvalidOperationException("Seal committed packed field before preparing a fork.");if(reserve==null)throw new ArgumentException("Missing field admission.");var fork=NavPackedFieldStorage.Create(Capacity,reserve);fork.Count=Count;try{var initialPages=fork.pages;fork.pages=null;initialPages.Dispose();fork.pages=pages.Fork(reserve);return fork;}catch{fork.ReleaseStorage();throw;}}
        internal void SealPrepared(){frozen=true;reserveCopy=null;}
        private NavPackedFieldCell[] Get(int index,bool create)
        {
            if(index<0||index>=Capacity)throw new ArgumentOutOfRangeException(nameof(index));
            if(create&&frozen)throw new InvalidOperationException("Committed packed field cannot be mutated.");
            return create?pages.Writable(index/LabelsPerPage,LabelsPerPage,CellBytes,reserveCopy).Values:pages.Get(index/LabelsPerPage)?.Values;
        }
        internal long GrowthForIndex(int index)=>pages.WriteGrowthBytes(LabelsPerPage,CellBytes);
        internal bool TryGet(int index,out double cost){var page=Get(index,false);int offset=index%LabelsPerPage;cost=page==null?0:page[offset].Distance;return page!=null&&(page[offset].Flags&1)!=0;}
        internal bool TryNext(int index,out NavGraphArc arc){var page=Get(index,false);int offset=index%LabelsPerPage;arc=page==null?default:page[offset].Next;return page!=null&&(page[offset].Flags&2)!=0;}
        internal void Set(int index,double cost,NavGraphArc? next)
        {if(next.HasValue)NavStoragePayload<NavGraphArc>.Validate(next.Value,this);var page=Get(index,true);int offset=index%LabelsPerPage;if((page[offset].Flags&1)==0)Count++;page[offset].Flags|=1;page[offset].Distance=cost;if(next.HasValue){var value=next.Value;NavStoragePayload<NavGraphArc>.Retain(value);var old=page[offset].Next;bool owned=(page[offset].Flags&2)!=0;page[offset].Flags|=2;page[offset].Next=value;if(owned)NavStoragePayload<NavGraphArc>.Release(old);}}
        internal SavedPage[] Save()
        {
            var rows=new List<SavedPage>();
            for(int index=0;index<pages.Count;index++){
                var page=pages.Get(index);if(page==null)continue;
                var row=new SavedPage{Index=index,Distance=new double[LabelsPerPage],Next=new NavGraphArc[LabelsPerPage],Flags=new byte[LabelsPerPage]};
                for(int i=0;i<LabelsPerPage;i++){row.Distance[i]=page.Values[i].Distance;row.Next[i]=page.Values[i].Next.Detached();row.Flags[i]=page.Values[i].Flags;}rows.Add(row);
            }
            return rows.ToArray();
        }
        internal void Restore(SavedPage[] saved,int expectedCount)=>RestoreMapped(saved,expectedCount,null);
        internal void RestoreMapped(SavedPage[] saved,int expectedCount,Func<NavGraphArc,NavGraphArc> canonical)
        {
            if(saved==null)throw new ArgumentException("Missing packed field pages.");var seen=new HashSet<int>();
            foreach(var row in saved){
                if(row==null||!seen.Add(row.Index)||row.Distance==null||row.Next==null||row.Flags==null||row.Index<0||(long)row.Index*LabelsPerPage>=Capacity||row.Distance.Length!=LabelsPerPage||row.Next.Length!=LabelsPerPage||row.Flags.Length!=LabelsPerPage)throw new ArgumentException("Invalid packed field page.");
                if(!row.Flags.Any(flag=>(flag&1)!=0))throw new ArgumentException("Empty allocated packed label page.");
                int first=row.Index*LabelsPerPage;
                for(int n=0;n<LabelsPerPage;n++)if(row.Flags[n]>3||row.Flags[n]==2||canonical!=null&&(row.Flags[n]&2)==0&&!row.Next[n].Equals(default(NavGraphArc))||first+n>=Capacity&&row.Flags[n]!=0||(row.Flags[n]&1)!=0&&(double.IsNaN(row.Distance[n])||double.IsInfinity(row.Distance[n])||row.Distance[n]<0))throw new ArgumentException("Invalid packed field label.");
                var page=Get(first,true);for(int n=0;n<LabelsPerPage;n++){var value=new NavPackedFieldCell{Distance=row.Distance[n],Next=(row.Flags[n]&2)!=0&&canonical!=null?canonical(row.Next[n]):row.Next[n],Flags=row.Flags[n]};NavStoragePayload<NavPackedFieldCell>.Validate(value,this);NavStoragePayload<NavPackedFieldCell>.Retain(value);var old=page[n];page[n]=value;NavStoragePayload<NavPackedFieldCell>.Release(old);}Count+=row.Flags.Count(flag=>(flag&1)!=0);
            }
            if(Count!=expectedCount)throw new ArgumentException("Packed field count mismatch.");
        }
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges()=>pages?.Release();
        internal NavOwnedStorage PhysicalRoot=>this;
    }
    // Entries are 16 bytes; positions are 4 bytes in independent lazy 256-slot
    // pages. Estimates include rounded capacities, 32-byte array headers and
    // both reference directories. No dictionary or duplicate heap nodes.
    internal sealed partial class NavFieldFrontier : NavOwnedStorage, System.Collections.Generic.IEnumerable<NavQueueEntry>
    {
        private const int PageSize=256;
        internal long LegacyMutationGrowthBytes=>legacy?.MutationGrowthBytes??0;
        private NavCowOrderedSet<NavQueueEntry> legacy;
        private NavStorageDirectory<NavQueueEntry> heap;
        private NavStorageDirectory<int> positions;
        private bool frozen;
        private Func<long,bool> reserveCopy;

        internal NavFieldFrontier ForkPrepared(Func<long,bool> reserve)
        {
            if(!frozen)throw new InvalidOperationException("Seal committed frontier before preparing a fork.");
            if(legacy==null)return ForkPackedPrepared(reserve);
            if(reserve==null)throw new ArgumentException("Missing frontier admission.");
            var fork=NavFieldFrontier.Create(nodes,candidates,maxLabels,false,reserve);try{var initialLegacy=fork.legacy;fork.legacy=null;initialLegacy.ReleaseStorage();fork.legacy=legacy.ForkPrepared(reserve);return fork;}catch{fork.ReleaseStorage();throw;}
        }
        internal NavFieldFrontier ForkPackedPrepared(Func<long,bool> reserve)
        {
            if(heap==null)throw new InvalidOperationException("Packed prepared frontier required.");
            if(!frozen)throw new InvalidOperationException("Seal committed frontier before preparing a fork.");
            if(reserve==null)throw new ArgumentException("Missing frontier admission.");
            var fork=NavFieldFrontier.Create(nodes,candidates,maxLabels,true,reserve);fork.count=count;fork.heapPages=heapPages;fork.positionPages=positionPages;try{var initialHeap=fork.heap;var initialPositions=fork.positions;fork.heap=null;fork.positions=null;initialHeap.Dispose();initialPositions.Dispose();fork.heap=heap.Fork(reserve);fork.positions=positions.Fork(reserve);return fork;}catch{fork.ReleaseStorage();throw;}
        }
        internal void SealPrepared(){frozen=true;reserveCopy=null;legacy?.Seal();}
        private void Reserve(long bytes){if(reserveCopy!=null&&!reserveCopy(bytes))throw new InvalidOperationException("Frontier copy/growth exceeds prepared reservation.");}
        private void WritablePosition(int page)
        {if(frozen)throw new InvalidOperationException("Committed frontier cannot be mutated.");bool added=positions.Get(page)==null;positions.Writable(page,PageSize,4,reserveCopy);if(added)positionPages++;}
        private void WritableHeap(int page)
        {if(frozen)throw new InvalidOperationException("Committed frontier cannot be mutated.");bool added=heap.Get(page)==null;heap.Writable(page,PageSize,16,reserveCopy);if(added)heapPages++;}
        private readonly int nodes,candidates,maxLabels;
        private int count,heapPages,positionPages;
        internal static NavFieldFrontier Create(int nodes,int candidates,int maxLabels,bool compact,Func<long,bool> reserve=null)
        {using(var a=new NavStorageAllocation(reserve)){a.Reserve(256);return new NavFieldFrontier(nodes,candidates,maxLabels,compact,reserve,true);}}
        internal NavFieldFrontier(int nodes,int candidates,int maxLabels,bool compact,Func<long,bool> reserve=null,bool alreadyReserved=false):base(256)
        {
            this.nodes=nodes;this.candidates=candidates;this.maxLabels=maxLabels;reserveCopy=reserve;
            using(var allocation=new NavStorageAllocation(reserve)){if(!alreadyReserved)allocation.Reserve(256);allocation.Register(this);
                if(!compact){legacy=NavCowOrderedSet<NavQueueEntry>.Create(reserve:reserve);Retain();return;}
                long capacity=(long)nodes*candidates;if(capacity>int.MaxValue)throw new ArgumentException("Frontier capacity overflow.");
                heap=NavStorageDirectory<NavQueueEntry>.Create(reserve);heap.InitializeSlots((maxLabels+PageSize-1)/PageSize);
                positions=NavStorageDirectory<int>.Create(reserve);positions.InitializeSlots((int)((capacity+PageSize-1)/PageSize));Retain();
            }
        }
        internal int Count=>legacy?.Count??count;
        internal long EstimatedRetainedBytes=>legacy!=null?StorageBytes+legacy.CommittedStorageBytes:
            RootStorageBytes((long)nodes*candidates,maxLabels)+heap.DirectoryBytes+positions.DirectoryBytes+NavStorageLayout.Charge(4096L+96)*heapPages+NavStorageLayout.Charge(1024L+96)*positionPages;
        private int Key(NavQueueEntry e){if(e.Node<0||e.Node>=nodes||e.Candidate<0||e.Candidate>=candidates||double.IsNaN(e.Cost)||double.IsInfinity(e.Cost)||e.Cost<0)throw new ArgumentException("Invalid field frontier entry.");return checked(e.Candidate*nodes+e.Node);}
        private int Position(int key)=>positions.Get(key/PageSize)?.Values[key%PageSize]??0;
        private void Position(int key,int value){int page=key/PageSize;WritablePosition(page);positions.Get(page).Values[key%PageSize]=value;}
        private NavQueueEntry Get(int index)=>heap.Get(index/PageSize).Values[index%PageSize];
        private void Set(int index,NavQueueEntry value){int page=index/PageSize;WritableHeap(page);heap.Get(page).Values[index%PageSize]=value;Position(Key(value),index+1);}
        private void Swap(int a,int b){var x=Get(a);Set(a,Get(b));Set(b,x);}
        private void Up(int index){while(index>0){int p=(index-1)/2;if(Get(p).CompareTo(Get(index))<=0)break;Swap(p,index);index=p;}}
        private void Down(int index){while(index*2+1<count){int c=index*2+1;if(c+1<count&&Get(c+1).CompareTo(Get(c))<0)c++;if(Get(index).CompareTo(Get(c))<=0)break;Swap(index,c);index=c;}}
        internal NavTreeShape<NavQueueEntry> SaveTree()=>legacy?.SaveShape();
        internal void RestoreTree(NavTreeShape<NavQueueEntry> shape){if(legacy==null)throw new ArgumentException("Unexpected tree in packed frontier.");legacy.RestoreShape(shape,maxLabels);}
        internal NavQueueEntry[] SaveHeap()=>heap==null?null:HeapEntries().ToArray();
        internal void RestoreHeap(NavQueueEntry[] saved)
        {
            if(heap==null||saved==null||saved.Length>AllocatedHeapCapacity)throw new ArgumentException("Invalid raw frontier heap capacity.");
            for(int n=0;n<saved.Length;n++){
                int key=Key(saved[n]);if(Position(key)!=0||n>0&&Get((n-1)/2).CompareTo(saved[n])>0)throw new ArgumentException("Invalid raw heap invariant/duplicate label.");
                Set(n,saved[n]);
            }
            count=saved.Length;
        }
        internal static long RootStorageBytes(long capacity,int maxLabels)=>NavStorageLayout.Charge(256)+2*NavStorageLayout.Charge(128);
        internal int AllocatedHeapCapacity=>heapPages*PageSize;
        internal int[] AllocatedPositionPages=>positions==null?null:Enumerable.Range(0,positions.Count).Where(n=>positions.Get(n)!=null).ToArray();
        internal void RestoreAllocated(int capacity,int[] pages)
        {
            if(heap==null){if(capacity!=0||pages!=null)throw new ArgumentException("Unexpected legacy frontier capacities.");return;}
            if(capacity<0||capacity%PageSize!=0||capacity/PageSize>heap.Count||pages==null||!pages.SequenceEqual(pages.Distinct().OrderBy(n=>n))||pages.Any(n=>n<0||n>=positions.Count))throw new ArgumentException("Invalid frontier allocation checkpoint.");
            for(int n=0;n<capacity/PageSize;n++)WritableHeap(n);
            foreach(int n in pages)WritablePosition(n);
        }
        private bool released;
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){heap?.Release();positions?.Release();legacy?.Release();}
        internal NavQueueEntry Min=>legacy!=null?legacy.Min:count==0?default:Get(0);
        internal bool Add(NavQueueEntry value)
        {
            int key=Key(value);if(legacy!=null)return legacy.Add(value);
            int existing=Position(key);if(existing!=0){if(Get(existing-1).CompareTo(value)==0)return false;throw new ArgumentException("Duplicate frontier label with inconsistent cost.");}
            if(count==maxLabels)throw new ArgumentException("Field frontier label cap.");int at=count++;Set(at,value);Up(at);return true;
        }
        internal bool Remove(NavQueueEntry value)
        {
            int key=Key(value);if(legacy!=null)return legacy.Remove(value);
            int index=Position(key)-1;if(index<0||Get(index).CompareTo(value)!=0)return false;
            Position(key,0);int last=--count;
            if(index!=last){Set(index,Get(last));if(index>0&&Get(index).CompareTo(Get((index-1)/2))<0)Up(index);else Down(index);}
            WritableHeap(last/PageSize);heap.Get(last/PageSize).Values[last%PageSize]=default;return true;
        }
        internal long GrowthForAdd(int node,int candidate)
        {
            if(legacy!=null)return 256;
            int key=Key(new NavQueueEntry{Node=node,Candidate=candidate});int resulting=count-(Position(key)>0?1:0);
            return heap.WriteGrowthBytes(PageSize,16)+positions.WriteGrowthBytes(PageSize,4);
        }
        public System.Collections.Generic.IEnumerator<NavQueueEntry> GetEnumerator()
        {if(legacy!=null)return legacy.GetEnumerator();return HeapEntries().GetEnumerator();}
        private IEnumerable<NavQueueEntry> HeapEntries(){for(int n=0;n<count;n++)yield return Get(n);}
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
    // One immutable terminal-region artifact; labels are finite region candidates,
    // never assigned member slots. Independent subscriptions can reuse every label.
    public sealed partial class NavSharedTerminalField : NavOwnedStorage
    {
        private enum Phase { Seed, Search, Done, ValidateSeeds, SeedByNode }
        private readonly LayeredNavigationGraph graph; private readonly NavTerminalRegion region;private NavOwnedStorage ownedRegion;
        private NavOwnedText ownedKey;
        private NavCowDictionary<long,double> distance;
        private NavCowDictionary<long,NavGraphArc> next;
        private NavPackedFieldStorage packed;
        private NavFieldFrontier frontier;
        private bool frozen,released,graphOwned;
        private readonly int maxLabels; private Phase phase; private int candidate, node, current = -1, arcCursor, currentCandidate;
        public NavSharedTerminalField(LayeredNavigationGraph graph, NavTerminalRegion region, int maxLabels) : this(graph,region,maxLabels,false) {}
        internal NavSharedTerminalField(LayeredNavigationGraph graph, NavTerminalRegion region, int maxLabels,bool legacyStorage,Func<long,bool> reserve=null,bool alreadyReserved=false):base(512)
        {
            this.graph = graph ?? throw new ArgumentNullException(nameof(graph)); this.region = region ?? throw new ArgumentNullException(nameof(region));
            if (maxLabels < 1 || maxLabels > 1048576) throw new ArgumentOutOfRangeException(nameof(maxLabels)); this.maxLabels = maxLabels;
            Status = graph.IsValid(region.Requested) ? NavSolveStatus.Pending : NavSolveStatus.InvalidEndpoint;
            phase=Status==NavSolveStatus.Pending?(NodeSeedEligible?Phase.ValidateSeeds:Phase.Seed):Phase.Done;
            bool compact=graph.SupportsPackedField&&!legacyStorage;
            long capacity=(long)graph.Nodes.Count*region.Candidates.Count;
            if(compact&&capacity>int.MaxValue){Status=NavSolveStatus.CapacityExceeded;phase=Phase.Done;compact=false;}
            using(var allocation=new NavStorageAllocation(reserve)){if(!alreadyReserved)allocation.Reserve(512);allocation.Register(this);graph.Retain();graphOwned=true;if(region.StorageRoot!=null){if(IsRegistered)RequireEdge(region.StorageRoot);ownedRegion=region.StorageRoot;ownedRegion.Retain();}
                if(!compact){distance=NavCowDictionary<long,double>.Create(reserve:reserve);next=NavCowDictionary<long,NavGraphArc>.Create(reserve:reserve);}
                frontier=NavFieldFrontier.Create(graph.Nodes.Count,region.Candidates.Count,maxLabels,compact,reserve);
                if(compact)packed=NavPackedFieldStorage.Create((int)capacity,reserve);
                if(reserve?.Target is INavStorageReservation ownedReservation){ownedKey=NavOwnedText.Digest(w=>{w.Write(graph.Binding.StableKey);w.Write(region.StableKey);},NavContract.StringWireBytes(graph.Binding.StableKey)+NavContract.StringWireBytes(region.StableKey),ownedReservation);StableKey=ownedKey.Value;}
                else StableKey=NavContract.DigestAdmitted(w=>{w.Write(graph.Binding.StableKey);w.Write(region.StableKey);},NavContract.StringWireBytes(graph.Binding.StableKey)+NavContract.StringWireBytes(region.StableKey),reserve);Retain();
            }
        }
        private NavSharedTerminalField(NavSharedTerminalField source,Func<long,bool> reserve,bool alreadyReserved=false):base(512)
        {
            graph=source.graph;region=source.region;maxLabels=source.maxLabels;StableKey=source.StableKey;Status=source.Status;
            phase=source.phase;candidate=source.candidate;node=source.node;current=source.current;arcCursor=source.arcCursor;currentCandidate=source.currentCandidate;
            using(var allocation=new NavStorageAllocation(reserve)){if(!alreadyReserved)allocation.Reserve(512);allocation.Register(this);graph.Retain();graphOwned=true;if(region.StorageRoot!=null){if(IsRegistered)RequireEdge(region.StorageRoot);ownedRegion=region.StorageRoot;ownedRegion.Retain();}if(source.ownedKey!=null){RequireEdge(source.ownedKey);source.ownedKey.Retain();ownedKey=source.ownedKey;}Retain();}
        }
        internal static NavSharedTerminalField CreateOwned(LayeredNavigationGraph graph,NavTerminalRegion region,int maxLabels,INavStorageReservation reservation,bool legacyStorage=false)
        {NavStorageAdmission.RequireOwned(reservation,graph);NavStorageAdmission.RequireOwned(reservation,region?.StorageRoot);return new NavSharedTerminalField(graph,region,maxLabels,legacyStorage,NavStorageLayout.Before(reservation.TryReserve,512),true);}
        internal static NavSharedTerminalField CopyRegionAndCreateOwned(LayeredNavigationGraph graph,NavTerminalRegion region,int maxLabels,INavStorageReservation reservation,bool legacyStorage=false)
        {
            NavStorageAdmission.RequireOwned(reservation,graph);if(region==null)throw new ArgumentNullException(nameof(region));
            if(region.StorageRoot!=null){NavStorageAdmission.RequireOwned(reservation,region.StorageRoot);return CreateOwned(graph,region,maxLabels,reservation,legacyStorage);}
            var copy=NavTerminalRegion.CopyOwned(region,reservation);try{return CreateOwned(graph,copy,maxLabels,reservation,legacyStorage);}finally{copy.StorageRoot.ReleaseOwner();}
        }
        internal NavSharedTerminalField ForkOwned(INavStorageReservation reservation)
        {NavStorageAdmission.RequireOwned(reservation,this);NavStorageAdmission.RequireOwned(reservation,graph);return ForkPrepared(reservation.TryReserve);}
        public string StableKey { get; } public NavSolveStatus Status { get; private set; }
        public LayeredNavigationGraph Graph => graph; public NavTerminalRegion Region => region;
        public int LabelCount => packed != null ? packed.Count : distance.Count;
        public int FrontierCount => frontier.Count;
        public long EstimatedRetainedBytes => packed == null ?
            StorageBytes + (ownedKey?.StorageBytes??0) + distance.CommittedStorageBytes(8) + next.CommittedStorageBytes(8) + frontier.EstimatedRetainedBytes :
            StorageBytes+(ownedKey?.StorageBytes??0)+packed.EstimatedRetainedBytes + frontier.EstimatedRetainedBytes;
        internal NavSharedTerminalField ForkPackedPrepared(Func<long,bool> reserve)
        {if(packed==null)throw new InvalidOperationException("Packed prepared field required.");return ForkPrepared(reserve);}
        internal NavSharedTerminalField ForkPrepared(Func<long,bool> reserve)
        {
            if(!frozen)throw new InvalidOperationException("Seal committed field before preparing a fork.");
            if(reserve==null)throw new ArgumentException("Missing field admission.");
            var fork=new NavSharedTerminalField(this,NavStorageLayout.Before(reserve,512),true);
            try{
                if(packed!=null)fork.packed=packed.ForkPrepared(reserve);
                else{fork.distance=distance.ForkPrepared(reserve);fork.next=next.ForkPrepared(reserve);}
                fork.frontier=frontier.ForkPrepared(reserve);return fork;
            }catch{fork.ReleaseStorage();throw;}
        }
        internal bool IsSealed=>frozen;
        internal void SealPackedPrepared()=>SealPrepared();
        internal void SealPrepared(){frozen=true;packed?.SealPrepared();frontier.SealPrepared();distance?.Seal();next?.Seal();}
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){packed?.Release();frontier?.Release();distance?.Release();next?.Release();ownedRegion?.Release();ownedKey?.Release();if(graphOwned){graphOwned=false;graph.Release();}}
        internal NavOwnedStorage PhysicalRoot=>this;
        public sealed class Checkpoint
        {
            public int Version { get; internal set; }
            public string StableKey { get; internal set; }
            public NavSolveStatus Status { get; internal set; }
            internal int Phase, Candidate, Node, Current, ArcCursor, CurrentCandidate;
            internal KeyValuePair<long, double>[] Distance;
            internal KeyValuePair<long, NavGraphArc>[] Next;
            internal NavQueueEntry[] Frontier,FrontierHeap;internal NavTreeShape<NavQueueEntry> FrontierShape;
            internal NavPackedFieldStorage.SavedPage[] PackedPages;
            internal int PackedLabels;internal bool PackedStorage;internal int FrontierHeapCapacity;internal int[] FrontierPositionPages;
        }
        public Checkpoint Save() => new Checkpoint {
            Version = 3, StableKey = StableKey, Status = Status, Phase = (int)phase,
            Candidate = candidate, Node = node, Current = current, ArcCursor = arcCursor,
            CurrentCandidate = currentCandidate, Distance = distance?.ToArray()??Array.Empty<KeyValuePair<long,double>>(), Next = next?.Select(pair=>new KeyValuePair<long,NavGraphArc>(pair.Key,pair.Value.Detached())).ToArray()??Array.Empty<KeyValuePair<long,NavGraphArc>>(),
            Frontier = frontier.OrderBy(e=>e).ToArray(),FrontierHeap=frontier.SaveHeap(),FrontierShape=frontier.SaveTree(), PackedPages = packed?.Save(), PackedLabels = packed?.Count ?? 0,PackedStorage=packed!=null,
            FrontierHeapCapacity=frontier.AllocatedHeapCapacity,FrontierPositionPages=frontier.AllocatedPositionPages
        };
        public static NavSharedTerminalField Restore(LayeredNavigationGraph graph,NavTerminalRegion region,int maxLabels,Checkpoint state)=>RestoreCore(graph,region,maxLabels,state,null);
        internal static NavSharedTerminalField RestoreOwned(LayeredNavigationGraph graph,NavTerminalRegion region,int maxLabels,Checkpoint state,INavStorageReservation reservation)
        {
            if(reservation==null)throw new ArgumentNullException(nameof(reservation));
            NavStorageAdmission.RequireOwned(reservation,graph);NavStorageAdmission.RequireOwned(reservation,region?.StorageRoot);
            if(state==null)throw new ArgumentException("Missing field checkpoint.");
            long scratch=checked(2048L+128L*(state.PackedPages?.Length??0)+128L*(state.FrontierPositionPages?.Length??0)+256L*(state.Frontier?.Length??0));
            using(NavStorageScratch.Reserve(reservation,scratch))return RestoreCore(graph,region,maxLabels,state,reservation.TryReserve);
        }
        internal static NavSharedTerminalField CopyRegionAndRestoreOwned(LayeredNavigationGraph graph,NavTerminalRegion region,int maxLabels,Checkpoint state,INavStorageReservation reservation)
        {
            NavStorageAdmission.RequireOwned(reservation,graph);if(region==null)throw new ArgumentNullException(nameof(region));
            if(region.StorageRoot!=null){NavStorageAdmission.RequireOwned(reservation,region.StorageRoot);return RestoreOwned(graph,region,maxLabels,state,reservation);}
            var copy=NavTerminalRegion.CopyOwned(region,reservation);try{return RestoreOwned(graph,copy,maxLabels,state,reservation);}finally{copy.StorageRoot.ReleaseOwner();}
        }
        private static NavSharedTerminalField RestoreCore(LayeredNavigationGraph graph,NavTerminalRegion region,int maxLabels,Checkpoint state,Func<long,bool> reserve)
        {
            if(state==null||state.PackedLabels<0||state.PackedLabels>maxLabels)throw new ArgumentException("Invalid packed label count before allocation.");
            var field = new NavSharedTerminalField(graph,region,maxLabels,state.PackedPages==null,NavStorageLayout.Before(reserve,512),true);
            try{
            if (state == null || (state.Version != 1&&state.Version!=2&&state.Version!=3) || state.StableKey != field.StableKey ||
                state.Version>=2&&state.PackedStorage!=(state.PackedPages!=null)||
                state.Distance==null||state.Next==null||state.Frontier==null||state.PackedPages==null&&state.PackedLabels!=0||state.PackedPages!=null&&(state.Distance.Length!=0||state.Next.Length!=0)||
                state.Distance.Length > maxLabels || state.Frontier.Length > maxLabels ||
                (state.PackedPages == null) != (field.packed == null) ||
                state.Phase < 0 || state.Phase > (int)Phase.SeedByNode)
                throw new ArgumentException("Invalid or stale shared field checkpoint.");
            field.phase = (Phase)state.Phase; field.Status = state.Status;
            field.candidate = state.Candidate; field.node = state.Node; field.current = state.Current;
            field.arcCursor = state.ArcCursor; field.currentCandidate = state.CurrentCandidate;
            foreach (var pair in state.Distance){
                int n=(int)pair.Key,c=(int)(pair.Key>>32);if(n<0||n>=graph.Nodes.Count||c<0||c>=region.Candidates.Count||double.IsNaN(pair.Value)||double.IsInfinity(pair.Value)||pair.Value<0)throw new ArgumentException("Invalid legacy field label.");
                field.distance.Add(pair.Key,pair.Value);
            }
            foreach (var pair in state.Next){int n=(int)pair.Key;if(!field.distance.ContainsKey(pair.Key)||pair.Value.From!=n||pair.Value.To<0||pair.Value.To>=graph.Nodes.Count||pair.Value.Cost<0)throw new ArgumentException("Invalid legacy successor.");field.next.Add(pair.Key,graph.ResolveCheckpointArc(pair.Value));}
            if (field.packed != null) field.packed.RestoreMapped(state.PackedPages,state.PackedLabels,graph.ResolveCheckpointArc);
            if(state.Version==1&&field.packed!=null)field.frontier.RestoreAllocated(((state.Frontier.Length+255)/256)*256,state.PackedPages.Select(p=>p.Index/4).Distinct().OrderBy(n=>n).ToArray());
            if(state.Version>=2){
                if(field.packed!=null&&(state.FrontierPositionPages==null||!state.FrontierPositionPages.SequenceEqual(state.PackedPages.Select(p=>p.Index/4).Distinct().OrderBy(n=>n))))throw new ArgumentException("Position pages must match all discovered label pages.");
                field.frontier.RestoreAllocated(state.FrontierHeapCapacity,state.FrontierPositionPages);
                if(field.packed!=null&&state.FrontierHeapCapacity<((state.Frontier.Length+255)/256)*256)throw new ArgumentException("Missing allocated heap page.");
            }
            if(state.Version==3&&field.packed!=null){
                if(state.FrontierShape!=null||state.FrontierHeap==null||state.FrontierHeap.Length!=state.Frontier.Length)throw new ArgumentException("Missing exact raw heap permutation.");
                field.frontier.RestoreHeap(state.FrontierHeap);
                if(!field.frontier.OrderBy(e=>e).SequenceEqual(state.Frontier))throw new ArgumentException("Raw heap and canonical frontier disagree.");
                foreach(var item in field.frontier)if(!field.TryGet(item.Node,item.Candidate,out var cost)||cost!=item.Cost)throw new ArgumentException("Invalid raw heap distance.");
            }else if(state.Version==3){
                if(state.FrontierHeap!=null)throw new ArgumentException("Unexpected heap in legacy frontier.");
                field.frontier.RestoreTree(state.FrontierShape);
                if(!field.frontier.SequenceEqual(state.Frontier))throw new ArgumentException("Legacy tree/frontier mismatch.");
                foreach(var item in field.frontier)if(!field.TryGet(item.Node,item.Candidate,out var cost)||cost!=item.Cost)throw new ArgumentException("Invalid legacy tree distance.");
            }else{
                if(state.Version==3&&state.FrontierHeap!=null)throw new ArgumentException("Unexpected raw heap in legacy storage.");
                foreach (var item in state.Frontier){
                    if(!field.TryGet(item.Node,item.Candidate,out var cost)||cost!=item.Cost||!field.frontier.Add(item))throw new ArgumentException("Invalid or duplicate field frontier checkpoint.");
                }
            }
            if(state.Version>=2&&(field.frontier.AllocatedHeapCapacity!=state.FrontierHeapCapacity||
                !((field.frontier.AllocatedPositionPages??Array.Empty<int>()).SequenceEqual(state.FrontierPositionPages??Array.Empty<int>()))))throw new ArgumentException("Incomplete frontier allocation checkpoint.");
            field.ValidateNodeSeedState(null);return field;
            }catch{field.ReleaseStorage();throw;}
        }
        private static long Key(int n, int c) => ((long)c << 32) | (uint)n;
        private int Index(int n, int c) => c * graph.Nodes.Count + n;
        public bool TryGet(int n, int c, out double cost)
        { if (packed == null) return distance.TryGetValue(Key(n, c), out cost);
            return packed.TryGet(Index(n, c), out cost); }
        public bool TryNext(int n, int c, out NavGraphArc arc)
        { if (packed == null) return next.TryGetValue(Key(n, c), out arc);
            return packed.TryNext(Index(n, c), out arc); }
        private void Set(int n, int c, double cost, NavGraphArc? successor = null)
        {
            if (packed == null) {
                long key = Key(n, c); distance[key] = cost; if (successor.HasValue) next[key] = successor.Value;
            } else packed.Set(Index(n, c), cost, successor);
        }
        internal static long InitialStorageBytes(LayeredNavigationGraph graph,NavTerminalRegion region,int maxLabels,bool ownedStorage=false)
        {
            long key=ownedStorage?NavStorageLayout.Charge(280):0;
            if(!graph.SupportsPackedField)return 512L+2L*(128L+256L)+(256L+64L+128L)+key;
            long capacity=(long)graph.Nodes.Count*region.Candidates.Count;if(capacity>int.MaxValue)return LayeredNavigationScheduler.MaxRetainedBytes;
            return NavStorageLayout.Charge(512)+key+NavPackedFieldStorage.RootStorageBytes(capacity)+NavFieldFrontier.RootStorageBytes(capacity,maxLabels);
        }
        // First packed seed may allocate one label directory/page, heap page and
        // position page together. Prediction never evaluates attachment geometry.
        internal static long FirstStepGrowthBytes(LayeredNavigationGraph graph,NavTerminalRegion region)
            =>graph.SupportsPackedField&&graph.Nodes.Count>0&&region.Candidates.Count>0?11392L:4096L;
        internal long NextGrowthBytes {
            get {
                if(phase==Phase.ValidateSeeds)return 0;
                if(phase==Phase.SeedByNode&&node<graph.Nodes.Count&&candidate==0&&graph.Nodes[node].Location.SurfaceId!=region.Requested.SurfaceId)return 0;
                if(packed==null)return distance.MutationGrowthBytes(8)+next.MutationGrowthBytes(8)+2L*frontier.LegacyMutationGrowthBytes;
                int n,c;bool adds=false;
                if(phase==Phase.Seed||phase==Phase.SeedByNode){if(candidate>=region.Candidates.Count||node>=graph.Nodes.Count)return 0;
                    n=node;c=candidate;adds=!TryGet(n,c,out _);
                }else if(phase==Phase.Search&&current>=0&&arcCursor<graph.Incoming(current).Count){
                    var arc=graph.Incoming(current)[arcCursor];n=arc.From;c=currentCandidate;TryGet(current,c,out var value);
                    adds=!TryGet(n,c,out var old)||value+arc.Cost<old;
                }else return 0;
                if(adds&&!TryGet(n,c,out _)&&LabelCount==maxLabels)return 0;
                return adds?packed.GrowthForIndex(Index(n,c))+frontier.GrowthForAdd(n,c):0;
            }
        }
        public NavWorkResult Advance(int quota)
        {
            if (quota < 0) throw new ArgumentOutOfRangeException(nameof(quota));
            if(frozen&&Status==NavSolveStatus.Pending)throw new InvalidOperationException("Committed field cannot be advanced."); int used = 0;
            while (used < quota && Status == NavSolveStatus.Pending) { Step(); used++; }
            return new NavWorkResult(Status, used);
        }
        private void Step(NavIncrementalSharedRoute observer=null)
        {
            if(phase==Phase.ValidateSeeds){
                if(!graph.IsValid(region.Candidates[candidate])){Finish(NavSolveStatus.InvalidEndpoint);return;}
                if(++candidate==region.Candidates.Count){candidate=0;node=0;phase=Phase.SeedByNode;}return;
            }
            if(phase==Phase.SeedByNode){
                if(candidate==0&&graph.Nodes[node].Location.SurfaceId!=region.Requested.SurfaceId){AdvanceSeedNode();return;}
                int n=node,c=candidate;
                if(graph.TryAttach(region.Candidates[c],n,out var attachment)){
                    Set(n,c,attachment.Cost);frontier.Add(new NavQueueEntry{Cost=attachment.Cost,Node=n,Candidate=c});
                }
                if(++candidate==region.Candidates.Count){candidate=0;AdvanceSeedNode();}return;
            }
            if (phase == Phase.Seed) {
                if (candidate == region.Candidates.Count) { phase = Phase.Search; return; }
                if (node == graph.Nodes.Count) { candidate++; node = 0; return; }
                if (node == 0 && !graph.IsValid(region.Candidates[candidate])) { Finish(NavSolveStatus.InvalidEndpoint); return; }
                int n = node++; NavGraphAttachment attachment;
                if (graph.TryAttach(region.Candidates[candidate], n, out attachment)) {
                    if (!TryGet(n, candidate, out _)) {
                        if (LabelCount == maxLabels) { Finish(NavSolveStatus.CapacityExceeded); return; }
                        Set(n, candidate, attachment.Cost);observer?.ObserveFieldLabel(n,candidate,attachment.Cost);
                        frontier.Add(new NavQueueEntry { Cost = attachment.Cost, Node = n, Candidate = candidate });
                    }
                }
                return;
            }
            if (current < 0) {
                if (frontier.Count == 0) { Finish(LabelCount == 0 ? NavSolveStatus.BlockedConnector : NavSolveStatus.Ready); return; }
                var entry = frontier.Min; frontier.Remove(entry);
                if (!TryGet(entry.Node, entry.Candidate, out double known) || known != entry.Cost) return;
                current = entry.Node; currentCandidate = entry.Candidate; arcCursor = 0; return;
            }
            var incoming = graph.Incoming(current);
            if (arcCursor == incoming.Count) { current = -1; return; }
            var arc = incoming[arcCursor++];
            TryGet(current, currentCandidate, out double currentCost);
            double cost = currentCost + arc.Cost;
            if (!TryGet(arc.From, currentCandidate, out double old) || cost < old) {
                if (!TryGet(arc.From, currentCandidate, out _) && LabelCount == maxLabels) { Finish(NavSolveStatus.CapacityExceeded); return; }
                if (TryGet(arc.From, currentCandidate, out _)) frontier.Remove(new NavQueueEntry { Cost = old, Node = arc.From, Candidate = currentCandidate });
                Set(arc.From, currentCandidate, cost, arc);observer?.ObserveFieldLabel(arc.From,currentCandidate,cost);
                frontier.Add(new NavQueueEntry { Cost = cost, Node = arc.From, Candidate = currentCandidate });
            }
        }
        private bool NodeSeedEligible=>graph.PhysicalProvider is PartitionNavigationProvider&&graph.Nodes.Count>0&&PartialCapacitySafe;
        private void AdvanceSeedNode()
        {if(++node==graph.Nodes.Count){node=0;candidate=region.Candidates.Count;phase=Phase.Search;}}
        // Only a current request can consume this certificate. Cache still requires Ready.
        internal bool PartialCapacitySafe=>checked((long)graph.Nodes.Count*region.Candidates.Count)<=maxLabels;
        internal bool SeedComplete=>phase==Phase.Search;
        internal bool Certifies(double best)=>Status==NavSolveStatus.Pending&&PartialCapacitySafe&&phase==Phase.Search&&current<0&&frontier.Count>0&&!double.IsInfinity(best)&&frontier.Min.Cost>best;
        internal NavWorkResult AdvanceOne(NavIncrementalSharedRoute observer)
        {
            if(observer==null||!ReferenceEquals(observer.PreparedField,this)||!PartialCapacitySafe)throw new ArgumentException("Partial field observer identity/capacity changed.");
            if(frozen||Status!=NavSolveStatus.Pending)throw new InvalidOperationException("Partial field step needs a private pending field.");
            Step(observer);return new NavWorkResult(Status,1);
        }
        private void Finish(NavSolveStatus status) { Status = status; phase = Phase.Done; }
    }

    // Exact ingress and assigned-slot egress are subscriber work, not cache keys.
    public sealed partial class NavIncrementalSharedRoute : NavOwnedStorage
    {
        private enum Phase { Egress, Ingress, Select, Origin, Arcs, Terminal, Endpoint, Done }
        private NavSharedTerminalField field; private readonly NavRouteSubscription subscription;
        private NavOwnedStorage ownedSubscription;
        private NavCowDictionary<int,double> egress,ingress;
        private NavPagedList<NavTypedLeg> legs;
        private bool frozen,released,borrowedRoute;private Func<long,bool> reservation;
        private Phase phase; private int cursor, candidate, chosenNode = -1, chosenCandidate = -1, walkNode, traversed, arcLeg;
        private double best = double.PositiveInfinity, routeCost;
        public NavIncrementalSharedRoute(NavSharedTerminalField field,NavRouteSubscription subscription):this(field,subscription,null,true){}
        private NavIncrementalSharedRoute(NavSharedTerminalField field,NavRouteSubscription subscription,Func<long,bool> reserve,bool initialize,bool alreadyReserved=false,bool birthOnly=false):base(2048)
        {
            this.field=field??throw new ArgumentNullException(nameof(field));this.subscription=subscription??throw new ArgumentNullException(nameof(subscription));reservation=reserve;
            if(reserve?.Target is INavStorageReservation owner){NavStorageAdmission.RequireOwned(owner,field);NavMetadataOwnership.Require(subscription,owner);}
            using(var allocation=new NavStorageAllocation(reserve)){if(!alreadyReserved)allocation.Reserve(2048);allocation.Register(this);if(!birthOnly)Retain();}field.Retain();if(subscription.MetadataOwner!=null){RequireEdge(subscription.MetadataOwner);subscription.MetadataOwner.Retain();ownedSubscription=subscription.MetadataOwner;}if(!initialize)return;
            try{egress=NavCowDictionary<int,double>.Create(reserve:reserve);ingress=NavCowDictionary<int,double>.Create(reserve:reserve);legs=NavPagedList<NavTypedLeg>.Create(reserve);
                Status=field.Graph.IsValid(subscription.Origin)&&field.Graph.IsValid(subscription.AssignedEndpoint)?NavSolveStatus.Pending:NavSolveStatus.InvalidEndpoint;phase=Status==NavSolveStatus.Pending?Phase.Egress:Phase.Done;
            }catch{ReleaseStorage();throw;}
        }
        internal static NavIncrementalSharedRoute CreateOwned(NavSharedTerminalField field,NavRouteSubscription subscription,INavStorageReservation reservation)
        {NavStorageAdmission.RequireOwned(reservation,field);NavStorageAdmission.RequireOwned(reservation,field.Graph);NavMetadataOwnership.Require(subscription,reservation);return new NavIncrementalSharedRoute(field,subscription,NavStorageLayout.Before(reservation.TryReserve,2048),true,true);}
        internal static NavIncrementalSharedRoute CopySubscriptionAndCreateOwned(NavSharedTerminalField field,NavRouteSubscription subscription,INavStorageReservation reservation)
        {NavStorageAdmission.RequireOwned(reservation,field);NavStorageAdmission.RequireOwned(reservation,field.Graph);if(subscription==null)throw new ArgumentNullException(nameof(subscription));if(subscription.MetadataOwner!=null)return CreateOwned(field,subscription,reservation);var copy=NavRouteSubscription.CopyOwned(subscription,reservation);try{return CreateOwned(field,copy,reservation);}finally{copy.MetadataOwner.Release();}}
        internal NavIncrementalSharedRoute ForkOwned(NavSharedTerminalField field,INavStorageReservation reservation)
        {
            NavStorageAdmission.RequireOwned(reservation,this);NavStorageAdmission.RequireOwned(reservation,this.field);
            NavStorageAdmission.RequireOwned(reservation,field);NavStorageAdmission.RequireOwned(reservation,field.Graph);
            if(!ReferenceEquals(field.Graph,this.field.Graph))throw new ArgumentException("Prepared connector field must preserve physical graph identity.");
            return ForkPrepared(field,reservation.TryReserve);
        }
        public NavSolveStatus Status {get;private set;}public NavTypedRoute Route {get;private set;}
        internal NavIncrementalSharedRoute ForkPrepared(NavSharedTerminalField target,Func<long,bool> reserve)
        {
            if(target==null||target.StableKey!=field.StableKey)throw new ArgumentException("Stale prepared connector field.");if(!frozen)throw new InvalidOperationException("Seal committed connector before preparing a fork.");if(reserve==null)throw new ArgumentException("Missing connector admission.");
            var fork=new NavIncrementalSharedRoute(target,subscription,NavStorageLayout.Before(reserve,2048),false,true){phase=phase,cursor=cursor,candidate=candidate,chosenNode=chosenNode,chosenCandidate=chosenCandidate,walkNode=walkNode,traversed=traversed,arcLeg=arcLeg,best=best,routeCost=routeCost,Status=Status};
            try{fork.egress=egress.ForkPrepared(reserve);fork.ingress=ingress.ForkPrepared(reserve);fork.legs=legs.Fork(reserve);if(Route!=null){Route.Retain();fork.Route=Route;fork.borrowedRoute=true;}return fork;}catch{fork.ReleaseStorage();throw;}
        }
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){egress?.Release();ingress?.Release();legs?.Release();Route?.Release();ownedSubscription?.Release();field.Release();}
        internal void SealPrepared(){frozen=true;egress.Seal();ingress.Seal();legs.Seal();reservation=null;}
        public int LegCount => legs.Count;
        public int StateCount => ingress.Count + egress.Count + legs.Count;
        internal long EstimatedRetainedBytes=>StorageBytes+egress.CommittedStorageBytes(4)+ingress.CommittedStorageBytes(4)+legs.CommittedStorageBytes(8)+NavStorageLayout.Charge(128)*legs.Count+(Route==null?0:NavStorageLayout.Charge(128));
        internal static long InitialStorageBytes=>NavStorageLayout.Charge(2048)+6*NavStorageLayout.Charge(128)+NavPagedList<NavTypedLeg>.InitialStorageBytes;
        internal long NextGrowthBytes=>Status!=NavSolveStatus.Pending?0:phase==Phase.Egress?egress.MutationGrowthBytes(4):phase==Phase.Ingress?ingress.MutationGrowthBytes(4):
            phase==Phase.Origin||phase==Phase.Arcs||phase==Phase.Terminal||phase==Phase.Endpoint?legs.AppendGrowthBytes(8)+256L:0;

        public sealed class Checkpoint
        {
            public int Version { get; internal set; }
            public string FieldKey { get; internal set; }
            public string SubscriptionKey { get; internal set; }
            public NavSolveStatus Status { get; internal set; }
            internal int Phase, Cursor, Candidate, ChosenNode, ChosenCandidate, WalkNode, Traversed, ArcLeg;
            internal double Best, RouteCost;
            internal KeyValuePair<int, double>[] Egress, Ingress;
            internal NavTypedLeg[] Legs;
        }
        public Checkpoint Save() => new Checkpoint {
            Version = 1, FieldKey = field.StableKey, SubscriptionKey = subscription.StableKey,
            Status = Status, Phase = (int)phase, Cursor = cursor, Candidate = candidate,
            ChosenNode = chosenNode, ChosenCandidate = chosenCandidate, WalkNode = walkNode,
            Traversed = traversed, ArcLeg = arcLeg, Best = best, RouteCost = routeCost,
            Egress = egress.ToArray(), Ingress = ingress.ToArray(), Legs = legs.Select(leg=>leg.Detached()).ToArray()
        };
        public static NavIncrementalSharedRoute Restore(NavSharedTerminalField field, NavRouteSubscription subscription, Checkpoint state)
        {
            var work = new NavIncrementalSharedRoute(field, subscription);
            if (state == null || state.Version != 1 || state.FieldKey != field.StableKey ||
                state.SubscriptionKey != subscription.StableKey || state.Phase < 0 || state.Phase > (int)Phase.Done)
                throw new ArgumentException("Invalid or stale shared connector checkpoint.");
            work.Status = state.Status; work.phase = (Phase)state.Phase; work.cursor = state.Cursor;
            work.candidate = state.Candidate; work.chosenNode = state.ChosenNode;
            work.chosenCandidate = state.ChosenCandidate; work.walkNode = state.WalkNode;
            work.traversed = state.Traversed; work.arcLeg = state.ArcLeg; work.best = state.Best; work.routeCost = state.RouteCost;
            foreach (var pair in state.Egress) work.egress.Add(pair.Key, pair.Value);
            foreach (var pair in state.Ingress) work.ingress.Add(pair.Key, pair.Value);
            work.legs.AddRange(state.Legs);
            if(work.Status==NavSolveStatus.Ready)work.Route=new NavTypedRoute(subscription,work.legs,work.routeCost);
            try{work.ValidatePartialShared(null);return work;}catch{work.ReleaseStorage();throw;}
        }
        internal NavSharedTerminalField PreparedField=>field;
        internal bool MatchesPreparedJob(NavSharedTerminalField expected,NavRouteSubscription value)=>ReferenceEquals(field,expected)&&ReferenceEquals(subscription,value);
        internal bool NeedsField=>field.Status==NavSolveStatus.Pending&&
            (phase==Phase.Select&&cursor==field.Graph.Nodes.Count&&!field.Certifies(best)||
             phase==Phase.Egress&&cursor==field.Region.Candidates.Count&&egress.Count==0||
             phase==Phase.Ingress&&cursor==field.Graph.Nodes.Count&&ingress.Count==0);
        internal void RebindPreparedField(NavSharedTerminalField target)
        {
            if(frozen||target==null||target.StableKey!=field.StableKey||!ReferenceEquals(target.Graph,field.Graph))throw new ArgumentException("Mutable connector field rebind changed graph/recipe.");
            if(IsRegistered)RequireEdge(target);target.Retain();var old=field;field=target;old.Release();
        }
        internal void ObserveFieldLabel(int n,int c,double tail)
        {
            if(frozen)throw new InvalidOperationException("Committed connector cannot observe field mutation.");
            if(phase==Phase.Select&&(n<cursor||n==cursor&&c<candidate))Consider(n,c,tail);
        }
        private void Consider(int n,int c,double tail)
        {
            if(ingress.TryGetValue(n,out double start)&&egress.TryGetValue(c,out double end)){
                double total=start+tail+end;
                if(total<best||total==best&&(n<chosenNode||n==chosenNode&&c<chosenCandidate)){best=total;chosenNode=n;chosenCandidate=c;}
            }
        }
        internal NavWorkResult AdvancePartial(int quota)=>AdvanceCore(quota,true);
        public NavWorkResult Advance(int quota)=>AdvanceCore(quota,false);
        private NavWorkResult AdvanceCore(int quota,bool partial)
        {
            if (quota < 0) throw new ArgumentOutOfRangeException(nameof(quota));if(frozen&&Status==NavSolveStatus.Pending)throw new InvalidOperationException("Committed connector cannot be advanced."); int used = 0;
            while (used < quota && Status == NavSolveStatus.Pending) {
                if(field.Status==NavSolveStatus.Pending){if(!partial||!field.PartialCapacitySafe||!field.SeedComplete||NeedsField)break;}
                else if(field.Status!=NavSolveStatus.Ready){Finish(field.Status);break;}
                Step(); used++;
            }
            return new NavWorkResult(Status, used);
        }
        private void Step()
        {
            var graph = field.Graph;
            if (phase == Phase.Egress) {
                if (cursor == field.Region.Candidates.Count) {
                    if (egress.Count == 0) { Finish(NavSolveStatus.BlockedConnector); return; }
                    phase = Phase.Ingress; cursor = 0; return;
                }
                var end = field.Region.Candidates[cursor];
                if (graph.CanSweep(end, subscription.AssignedEndpoint))
                    egress[cursor] = LayeredNavigationProvider.Distance(end.Position, subscription.AssignedEndpoint.Position) * graph.SurfaceCost(end.SurfaceId);
                cursor++; return;
            }
            if (phase == Phase.Ingress) {
                if (cursor == graph.Nodes.Count) {
                    if (ingress.Count == 0) { Finish(NavSolveStatus.BlockedConnector); return; }
                    phase = Phase.Select; cursor = 0; candidate = 0; return;
                }
                NavGraphAttachment a;
                if (graph.TryAttach(subscription.Origin, cursor, out a)) ingress[cursor] = a.Cost;
                cursor++; return;
            }
            if (phase == Phase.Select) {
                if (cursor == graph.Nodes.Count) {
                    if (chosenNode < 0) { Finish(NavSolveStatus.UnreachableInGraph); return; }
                    walkNode = chosenNode; phase = Phase.Origin; return;
                }
                if (candidate == field.Region.Candidates.Count) { cursor++; candidate = 0; return; }
                // One paid row lookup removes only pairs with no possible ingress.
                // Advancing the prefix over that row cannot change its exact minimum.
                if(candidate==0&&!ingress.ContainsKey(cursor)){cursor++;return;}
                int c = candidate++;
                if(field.TryGet(cursor,c,out double tail))Consider(cursor,c,tail);
                return;
            }
            if (phase == Phase.Origin) { AddSurface(subscription.Origin,graph.Nodes[walkNode].Location); phase = Phase.Arcs; return; }
            if (phase == Phase.Arcs) {
                if (field.TryNext(walkNode, chosenCandidate, out var arc)) {
                    if (arcLeg == 0 && traversed++ >= (long)graph.Nodes.Count * field.Region.Candidates.Count) throw new InvalidOperationException("Shared field cycle.");
                    var trace=NavSolverLegs.ExpandedArc(graph,arc,reservation?.Target as INavStorageReservation);var leg=trace[arcLeg++];
                    try{Add(leg);}finally{if(arc.Trace==null)leg.ReleaseStorage();}
                    if (arcLeg == trace.Count) { arcLeg = 0; walkNode = arc.To; }
                } else phase = Phase.Terminal;
                return;
            }
            if (phase == Phase.Terminal) { AddSurface(graph.Nodes[walkNode].Location,field.Region.Candidates[chosenCandidate]); phase = Phase.Endpoint; return; }
            if (phase == Phase.Endpoint) {
                AddSurface(field.Region.Candidates[chosenCandidate],subscription.AssignedEndpoint);
                Route = new NavTypedRoute(subscription,legs,routeCost,NavStorageLayout.Before(reservation,128),true); Finish(NavSolveStatus.Ready);
            }
        }
        private void AddSurface(NavLocation from,NavLocation to){var leg=NavSolverLegs.Surface(field.Graph,from,to,reservation?.Target as INavStorageReservation);try{Add(leg);}finally{leg.ReleaseStorage();}}
        private void Add(NavTypedLeg leg) { legs.Add(leg); routeCost += leg.Cost; }
        private void Finish(NavSolveStatus status) { Status = status; phase = Phase.Done; }
    }
}
