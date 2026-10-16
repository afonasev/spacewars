using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    internal sealed partial class NavPagedList<T> : NavOwnedStorage, IReadOnlyList<T>
    {
        private readonly int pageSize;
        private NavStorageDirectory<T> pages;
        private bool frozen,faulted;
        private Func<long,bool> reserveCopy;
        private static readonly int elementBytes=Math.Max(!typeof(T).IsValueType?8:typeof(T)==typeof(int)?4:typeof(T)==typeof(long)||typeof(T)==typeof(double)?8:typeof(T)==typeof(NavGridCoordinate)?24:typeof(T)==typeof(NavPortalEndpoints)?16:typeof(T)==typeof(NavLocation)||typeof(T)==typeof(NavGraphNode)?64:128,NavStorageLayout.ValueBytes(typeof(T)));
        internal static NavPagedList<T> Create(Func<long,bool> reserve=null,int pageSize=256)
        {using(var allocation=new NavStorageAllocation(reserve)){allocation.Reserve(128);return new NavPagedList<T>(reserve,pageSize,true);}}
        internal NavPagedList(Func<long,bool> reserve=null,int pageSize=256,bool alreadyReserved=false):base(128){if(pageSize<1||pageSize>4096)throw new ArgumentException("Invalid page size.");this.pageSize=pageSize;reserveCopy=reserve;using(var allocation=new NavStorageAllocation(reserve)){if(!alreadyReserved)allocation.Reserve(128);allocation.Register(this);Retain();}try{pages=NavStorageDirectory<T>.Create(reserve);}catch{ReleaseStorage();throw;}}
        internal static NavPagedList<T> CreateOwned(INavStorageReservation reservation,int pageSize=256)
        {if(reservation==null)throw new ArgumentNullException(nameof(reservation));return NavPagedList<T>.Create(reservation.TryReserve,pageSize);}
        internal NavPagedList<T> ForkOwned(INavStorageReservation reservation)
        {if(reservation==null)throw new ArgumentNullException(nameof(reservation));return Fork(reservation.TryReserve);}
        internal bool IsFrozen=>frozen;
        internal int DirectoryCapacity=>pages.Capacity;
        internal void RestoreDirectoryCapacity(int capacity,int maximumItems,int savedItems=0){ValidateDirectoryCapacity(capacity,(maximumItems+pageSize-1)/pageSize,(savedItems+pageSize-1)/pageSize);pages.RestoreCapacity(capacity);}
        internal static void ValidateDirectoryCapacity(int capacity,int maximumPages,int count){int maximum=maximumPages==0?0:4;while(maximum<maximumPages)maximum*=2;if(capacity<count||capacity>maximum||capacity!=0&&(capacity<4||(capacity&(capacity-1))!=0))throw new ArgumentException("Invalid page-directory capacity.");}
        internal long ForkReserveBytes=>3*NavStorageLayout.Charge(128);
        internal long CopyMetadataBytes=>0;
        internal static long InitialStorageBytes=>2*NavStorageLayout.Charge(128);
        internal long DirectoryRetainedBytes=>InitialStorageBytes+pages.DirectoryBytes+NavStorageLayout.Charge(96)*pages.Count;
        internal long CommittedStorageBytes(int width)=>checked(DirectoryRetainedBytes+(long)Math.Max(width,elementBytes)*Capacity);
        internal long AppendGrowthBytes(int width,int additions=1)=>checked((long)additions*pages.WriteGrowthBytes(pageSize,Math.Max(width,elementBytes)));
        internal void Seal(){if(faulted||References<=0)throw new InvalidOperationException("Faulted or released paged storage cannot be sealed.");frozen=true;reserveCopy=null;}
        internal NavPagedList<T> Fork(Func<long,bool> reserve)
        {
            if(!frozen||faulted||References<=0)throw new InvalidOperationException("Only live sealed storage can be forked.");
            if(reserve==null)throw new ArgumentException("Missing paged-list admission.");
            var fork=NavPagedList<T>.Create(reserve,pageSize);fork.Count=Count;try{var initialPages=fork.pages;fork.pages=null;initialPages.Dispose();fork.pages=pages.Fork(reserve);return fork;}catch{fork.ReleaseStorage();throw;}
        }
        private T[] Writable(int page,bool append=false)
        {RequireMutable();return pages.Writable(page,pageSize,elementBytes,reserveCopy,append).Values;}
        internal int Capacity=>checked(pages.Count*pageSize);
        public int Count {get;private set;}
        public T this[int index]
        {get{if(index<0||index>=Count)throw new ArgumentOutOfRangeException(nameof(index));return pages.Get(index/pageSize).Values[index%pageSize];}set{Set(index,value);}}
        internal void Add(T value){RequireMutable();NavStoragePayload<T>.ValidateActive(value,this);var values=Writable(Count/pageSize,Count==Capacity);NavStoragePayload<T>.Retain(value);values[Count%pageSize]=value;Count++;}
        internal void Set(int index,T value){RequireMutable();if(index<0||index>=Count)throw new ArgumentOutOfRangeException(nameof(index));NavStoragePayload<T>.ValidateActive(value,this);var values=Writable(index/pageSize);NavStoragePayload<T>.Retain(value);var old=values[index%pageSize];values[index%pageSize]=value;NavStoragePayload<T>.Release(old);}
        private void RequireMutable(){if(frozen||faulted||References<=0)throw new InvalidOperationException("Paged storage is sealed, faulted or released.");}
        internal void Clear()
        {
            RequireMutable();int count=Count;
            try{
                for(int page=0;page<(int)(((long)count+pageSize-1)/pageSize);page++){
                    var values=Writable(page);int active=(int)Math.Min((long)pageSize,(long)count-(long)page*pageSize);
                    for(int n=0;n<active;n++){var old=values[n];values[n]=default;NavStoragePayload<T>.Release(old);}
                }
                Count=0;
            }catch{faulted=true;reserveCopy=null;throw;}
        }
        internal void AddRange(IEnumerable<T> values){foreach(var value in values)Add(value);}
        internal T[] ToArray(){var result=new T[Count];for(int i=0;i<Count;i++)result[i]=this[i];return result;}
        private bool released;
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){var old=pages;pages=null;Count=0;reserveCopy=null;old?.Release();}
        internal NavOwnedStorage PhysicalRoot=>this;
        public IEnumerator<T> GetEnumerator(){for(int i=0;i<Count;i++)yield return this[i];}
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
    internal sealed partial class NavPackedSlots : NavOwnedStorage
    {
        internal const int PageSize=4096;
        internal NavPackedSlots():this(null){}
        internal static NavPackedSlots Create(Func<long,bool> reserve=null)
        {using(var allocation=new NavStorageAllocation(reserve)){allocation.Reserve(128);return new NavPackedSlots(reserve,true);}}
        internal NavPackedSlots(Func<long,bool> reserve,bool alreadyReserved=false):base(128){reserveCopy=reserve;using(var allocation=new NavStorageAllocation(reserve)){if(!alreadyReserved)allocation.Reserve(128);allocation.Register(this);Retain();}try{pages=NavStorageDirectory<int>.Create(reserve);}catch{ReleaseStorage();throw;}}
        private NavStorageDirectory<int> pages;
        private bool frozen;
        private Func<long,bool> reserveCopy;
        internal long ForkReserveBytes=>3*NavStorageLayout.Charge(128);
        internal long CopyMetadataBytes=>0;
        internal void Seal(){frozen=true;reserveCopy=null;}
        internal int DirectoryCapacity=>pages.Capacity;
        internal NavPackedSlots Fork(Func<long,bool> reserve)
        {if(!frozen)throw new InvalidOperationException("Seal committed slots before preparing a fork.");if(reserve==null)throw new ArgumentException("Missing slot admission.");var fork=NavPackedSlots.Create(reserve);try{var initialPages=fork.pages;fork.pages=null;initialPages.Dispose();fork.pages=pages.Fork(reserve);return fork;}catch{fork.ReleaseStorage();throw;}}
        internal int CapacitySlots=>pages.Count*PageSize;
        internal static long InitialStorageBytes=>2*NavStorageLayout.Charge(128);
        internal long EstimatedRetainedBytes=>InitialStorageBytes+pages.DirectoryBytes+NavStorageLayout.Charge(96)*pages.Count+4L*CapacitySlots;
        internal void Ensure(int slots)
        {if(frozen)throw new InvalidOperationException("Committed packed slots cannot be mutated.");if(slots>CapacitySlots+PageSize)throw new InvalidOperationException("Unbudgeted packed slot expansion.");if(slots>CapacitySlots)pages.Writable(pages.Count,PageSize,4,reserveCopy,true);}
        internal int Get(int slot)=>pages.Get(slot/PageSize).Values[slot%PageSize];
        internal void Set(int slot,int value){if(frozen)throw new InvalidOperationException("Committed packed slots cannot be mutated.");pages.Writable(slot/PageSize,PageSize,4,reserveCopy).Values[slot%PageSize]=value;}
        internal int[][] Save()=>Enumerable.Range(0,pages.Count).Select(n=>(int[])pages.Get(n).Values.Clone()).ToArray();
        private bool released;
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges()=>pages?.Release();
        internal NavOwnedStorage PhysicalRoot=>this;
        internal static NavPackedSlots Restore(int[][] saved,int maximumSlots,int directoryCapacity=-1)=>RestoreAdmitted(saved,maximumSlots,directoryCapacity,null);
        internal static NavPackedSlots RestoreAdmitted(int[][] saved,int maximumSlots,int directoryCapacity,Func<long,bool> reserve)
        {
            if(saved==null||saved.Length*PageSize>maximumSlots+PageSize||saved.Any(p=>p==null||p.Length!=PageSize))throw new ArgumentException("Invalid packed slot pages.");
            var result=NavPackedSlots.Create(reserve);try{
            if(directoryCapacity>=0)NavPagedList<int>.ValidateDirectoryCapacity(directoryCapacity,(maximumSlots+PageSize-1)/PageSize,saved.Length);
            foreach(var page in saved){var copy=result.pages.Writable(result.pages.Count,PageSize,4,reserve,true);Array.Copy(page,copy.Values,PageSize);}
            if(directoryCapacity>=0)result.pages.RestoreCapacity(directoryCapacity);return result;}catch{result.ReleaseStorage();throw;}
        }
    }
    // The authored partition has one canonical location at each .8 m grid
    // sample. Cardinal arcs carry an exact provider trace where they cross
    // seams. No per-support erosion, invented portal or coarser grid is used.
    internal sealed partial class PartitionNavigationCompileWork : NavOwnedStorage
    {
        internal enum Stage { Bounds, Samples, Arcs, Components, Ready }
        private readonly PartitionNavigationProvider provider;
        private NavOwnedStorage ownedProvider,ownedClearance,ownedProfile,ownedLimits;
        private readonly NavClearanceProfile clearance;
        private readonly NavGraphProfile profile;
        private readonly NavGraphCompileLimits limits;
        private NavPagedList<NavLocation> locations;
        private NavPagedList<int> components;
        private NavPagedList<int> arcSlots;
        private NavPagedList<IReadOnlyList<NavTypedLeg>> traces;
        private NavPackedSlots grid,neighbors,incoming;
        private NavPackedSlots traceSlots;
        private long traceBytes;
        private bool frozen,released,borrowedGraph;private Func<long,bool> preparedReservation;
        private int min, max, width, x, z, node, direction, componentCursor;
        private long sampled;
        internal Stage Phase { get; private set; } = Stage.Bounds;
        internal int Consumed { get; private set; }
        internal int SampleCount => locations.Count;
        internal int ArcCount => arcSlots.Count;
        internal LayeredNavigationGraph Graph { get; private set; }
        internal long EstimatedRetainedBytes => 4096L + 32L * locations.Capacity +
            4L * components.Capacity + 4L * arcSlots.Capacity + neighbors.EstimatedRetainedBytes +
            incoming.EstimatedRetainedBytes + traceSlots.EstimatedRetainedBytes +
            8L * traces.Capacity + (grid?.EstimatedRetainedBytes ?? 0) + traceBytes +
            locations.DirectoryRetainedBytes+components.DirectoryRetainedBytes+arcSlots.DirectoryRetainedBytes+traces.DirectoryRetainedBytes;

        internal PartitionNavigationCompileWork(PartitionNavigationProvider provider,NavClearanceProfile clearance,NavGraphProfile profile,NavGraphCompileLimits limits,Func<long,bool> reserve=null,bool initialize=true,bool alreadyReserved=false,bool birthOnly=false):base(1024)
        {
            this.provider=provider;this.clearance=clearance;this.profile=profile;this.limits=limits;preparedReservation=reserve;
            using(var allocation=new NavStorageAllocation(reserve)){if(!alreadyReserved)allocation.Reserve(1024);allocation.Register(this);if(!birthOnly)Retain();}
            if(provider.StorageRoot!=null){ownedProvider=provider.StorageRoot;ownedProvider.Retain();}
            NavMetadataOwnership.RetainDistinct(this,clearance.MetadataOwner,profile.MetadataOwner,limits.MetadataOwner,out ownedClearance,out ownedProfile,out ownedLimits);
            if(!initialize)return;
            try{locations=NavPagedList<NavLocation>.Create(reserve);components=NavPagedList<int>.Create(reserve);arcSlots=NavPagedList<int>.Create(reserve);traces=NavPagedList<IReadOnlyList<NavTypedLeg>>.Create(reserve);grid=NavPackedSlots.Create(reserve);neighbors=NavPackedSlots.Create(reserve);incoming=NavPackedSlots.Create(reserve);traceSlots=NavPackedSlots.Create(reserve);}catch{ReleaseStorage();throw;}
        }
        internal PartitionNavigationCompileWork ForkPrepared(Func<long,bool> reserve)
        {
            if(!frozen)throw new InvalidOperationException("Seal committed compiler before preparing a fork.");if(reserve==null)throw new ArgumentException("Missing partition admission.");
            var fork=new PartitionNavigationCompileWork(provider,clearance,profile,limits,NavStorageLayout.Before(reserve,1024),false,true){traceBytes=traceBytes,min=min,max=max,width=width,x=x,z=z,node=node,direction=direction,componentCursor=componentCursor,sampled=sampled,Phase=Phase,Consumed=Consumed};
            try{fork.locations=locations.Fork(reserve);fork.components=components.Fork(reserve);fork.arcSlots=arcSlots.Fork(reserve);fork.traces=traces.Fork(reserve);fork.grid=grid?.Fork(reserve);fork.neighbors=neighbors.Fork(reserve);fork.incoming=incoming.Fork(reserve);fork.traceSlots=traceSlots.Fork(reserve);if(Graph!=null){Graph.Retain();fork.Graph=Graph;fork.borrowedGraph=true;}return fork;}catch{fork.ReleaseStorage();throw;}
        }
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){ownedClearance?.Release();ownedProfile?.Release();ownedLimits?.Release();ownedProvider?.Release();locations?.Release();components?.Release();arcSlots?.Release();traces?.Release();grid?.Release();neighbors?.Release();incoming?.Release();traceSlots?.Release();Graph?.Release();}
        internal void SealPrepared(){if(frozen)return;locations.Seal();components.Seal();arcSlots.Seal();traces.Seal();grid?.Seal();neighbors.Seal();incoming.Seal();traceSlots.Seal();preparedReservation=null;frozen=true;}
        private int GridIndex(int gx, int gz) => (gx - min) * width + (gz - min);
        private int Root(int n)
        {
            while (components[n] != n) { components.Set(n, components[components[n]]); n = components[n]; }
            return n;
        }
        internal int Advance(int quota)
        {
            if (quota < 0) throw new ArgumentOutOfRangeException(nameof(quota));
            if(frozen&&Phase!=Stage.Ready)throw new InvalidOperationException("Committed compiler cannot be advanced.");
            int used = 0;
            while (used < quota && Phase != Stage.Ready) { Step(); used++; Consumed++; }
            return used;
        }
        private void Step()
        {
            if (Phase == Stage.Bounds) {
                double bound = provider.HalfExtent / profile.GridCell;
                min = (int)Math.Ceiling(-bound); max = (int)Math.Floor(bound);
                sampled = (long)(max - min + 1) * (max - min + 1);
                if (sampled > limits.MaxSamples) throw new ArgumentException("Graph sampling capacity exceeded.");
                width = max - min + 1; x = min; z = min; Phase = Stage.Samples; return;
            }
            if (Phase == Stage.Samples) {
                int gridIndex = GridIndex(x, z);
                grid.Ensure(gridIndex + 1);
                var point = new NavPoint(x * profile.GridCell, z * profile.GridCell);
                if (provider.TryLocate(point, clearance, out var location)) {
                    if (locations.Count >= limits.MaxNodes) throw new ArgumentException("Graph node capacity exceeded.");
                    int id = locations.Count; locations.Add(location);
                    components.Add(id); grid.Set(gridIndex, id + 1);
                    neighbors.Ensure((id + 1) * 4); incoming.Ensure((id + 1) * 4);
                    traceSlots.Ensure((id + 1) * 4);
                }
                if (++z > max) { z = min; x++; }
                if (x > max) Phase = Stage.Arcs;
                return;
            }
            if (Phase == Stage.Arcs) {
                if (node == locations.Count) { Phase = Stage.Components; return; }
                int dir = direction++, from = node;
                if (direction == 4) { direction = 0; node++; }
                int gx = (int)Math.Round(locations[from].Position.X / profile.GridCell);
                int gz = (int)Math.Round(locations[from].Position.Z / profile.GridCell);
                int nx = gx + (dir == 0 ? 1 : dir == 1 ? -1 : 0);
                int nz = gz + (dir == 2 ? 1 : dir == 3 ? -1 : 0);
                if (nx < min || nx > max || nz < min || nz > max) return;
                int to = grid.Get(GridIndex(nx, nz)) - 1;
                if (to < 0) return;
                var start = locations[from]; var end = locations[to];
                NavLocation reached;IReadOnlyList<NavTypedLeg> trace;
                bool found=preparedReservation?.Target is INavStorageReservation allocation
                    ?provider.TryGraphTraceValidatedOwned(start,end.Position,clearance,out reached,out trace,allocation)
                    :provider.TryGraphTraceValidated(start,end.Position,clearance,out reached,out trace);
                try{
                if(!found||!reached.Equals(end))return;
                if (arcSlots.Count >= limits.MaxArcs) throw new ArgumentException("Graph arc capacity exceeded.");
                int slot = from * 4 + dir;
                neighbors.Set(slot, to + 1);
                incoming.Set(to * 4 + (dir ^ 1), slot + 1);
                arcSlots.Add(slot);
                if (trace.Any(l => l.Kind == NavTypedLegKind.Portal)) {
                    traces.Add(trace); traceSlots.Set(slot, traces.Count); traceBytes += 128L + 96L * trace.Count;
                }
                int a = Root(from), b = Root(to);
                if (a != b) components.Set(Math.Max(a, b), Math.Min(a, b));
                return;
                }finally{if(preparedReservation?.Target is INavStorageReservation)(trace as NavOwnedStorage)?.ReleaseOwner();}
            }
            if (Phase == Stage.Components) {
                if (componentCursor < components.Count) { components.Set(componentCursor, Root(componentCursor)); componentCursor++; return; }
                grid?.ReleaseStorage();grid = null;
                locations.Seal();components.Seal();arcSlots.Seal();traces.Seal();neighbors.Seal();incoming.Seal();traceSlots.Seal();
                Graph = LayeredNavigationGraph.FromPartition(provider, NavGraphBinding.CreateAdmitted(provider,clearance,profile,preparedReservation),
                    locations, components, neighbors, incoming, arcSlots, traceSlots, traces, traceBytes,preparedReservation);
                Phase = Stage.Ready;
            }
        }

        internal sealed class Checkpoint
        {
            internal int Version,LocationsDirectoryCapacity,ComponentsDirectoryCapacity,ArcSlotsDirectoryCapacity,TracesDirectoryCapacity,GridDirectoryCapacity,NeighborsDirectoryCapacity,IncomingDirectoryCapacity,TraceSlotsDirectoryCapacity;
            internal string Binding; internal Stage Phase; internal int Consumed, Min, Max, Width, X, Z, Node, Direction, ComponentCursor;
            internal long Sampled; internal NavLocation[] Locations;
            internal int[] Components, ArcSlots;
            internal int[][] Grid, Neighbors, Incoming, TraceSlots;
            internal long TraceBytes;
            internal NavTypedLeg[][] Traces;
        }
        internal Checkpoint Save() => new Checkpoint {
            Version=2,LocationsDirectoryCapacity=locations.DirectoryCapacity,ComponentsDirectoryCapacity=components.DirectoryCapacity,ArcSlotsDirectoryCapacity=arcSlots.DirectoryCapacity,TracesDirectoryCapacity=traces.DirectoryCapacity,
            GridDirectoryCapacity=grid?.DirectoryCapacity??0,NeighborsDirectoryCapacity=neighbors.DirectoryCapacity,IncomingDirectoryCapacity=incoming.DirectoryCapacity,TraceSlotsDirectoryCapacity=traceSlots.DirectoryCapacity,
            Binding = new NavGraphBinding(provider.Binding, clearance, profile).StableKey,
            Phase = Phase, Consumed = Consumed, Min = min, Max = max, Width = width, X = x, Z = z,
            Node = node, Direction = direction, ComponentCursor = componentCursor, Sampled = sampled,
            Locations = locations.Select(location=>location.Detached()).ToArray(), Components = components.ToArray(), Grid = grid?.Save(),
            Neighbors = neighbors.Save(), Incoming = incoming.Save(), ArcSlots = arcSlots.ToArray(),
            TraceSlots = traceSlots.Save(),
            TraceBytes = traceBytes,
            Traces = traces.Select(t => t.Select(leg=>leg.Detached()).ToArray()).ToArray()
        };
        private static string CanonicalSeam(PartitionNavigationProvider provider,string id)
        {if(id==null)return null;foreach(var seam in provider.Seams)if(seam.Id==id)return seam.Id;throw new ArgumentException("Unknown checkpoint seam.");}
        internal static PartitionNavigationCompileWork Restore(PartitionNavigationProvider provider,
            NavClearanceProfile clearance, NavGraphProfile profile, NavGraphCompileLimits limits, Checkpoint saved,Func<long,bool> reserve=null)
        {
            if (saved == null || saved.Version!=2 || saved.Binding != new NavGraphBinding(provider.Binding, clearance, profile).StableKey ||
                saved.Locations.Length > limits.MaxNodes || saved.ArcSlots.Length > limits.MaxArcs ||
                saved.Locations.Length != saved.Components.Length ||
                saved.Neighbors.Length != saved.Incoming.Length ||
                saved.Phase < Stage.Bounds || saved.Phase > Stage.Ready)
                throw new ArgumentException("Invalid partition compile checkpoint.");
            var work = new PartitionNavigationCompileWork(provider,clearance,profile,limits,NavStorageLayout.Before(reserve,1024),true,true) {
                Phase = saved.Phase, Consumed = saved.Consumed, min = saved.Min, max = saved.Max, width = saved.Width,
                x = saved.X, z = saved.Z, node = saved.Node, direction = saved.Direction,
                componentCursor = saved.ComponentCursor, sampled = saved.Sampled, traceBytes = saved.TraceBytes
            };
            try{
            work.locations.RestoreDirectoryCapacity(saved.LocationsDirectoryCapacity,limits.MaxNodes,saved.Locations.Length);work.components.RestoreDirectoryCapacity(saved.ComponentsDirectoryCapacity,limits.MaxNodes,saved.Components.Length);
            work.arcSlots.RestoreDirectoryCapacity(saved.ArcSlotsDirectoryCapacity,limits.MaxArcs,saved.ArcSlots.Length);work.traces.RestoreDirectoryCapacity(saved.TracesDirectoryCapacity,limits.MaxArcs,saved.Traces.Length);
            // Restored locations must regain the exact certificates obtained by sampling.
            foreach(var location in saved.Locations)if(!provider.TryLocate(location.Position,clearance,out var canonical)||!canonical.Equals(location))throw new ArgumentException("Invalid saved sample location.");
            work.locations.AddRange(saved.Locations.Select(location=>reserve==null?location:provider.OwnedCheckpointLocation(location)));
            work.components.AddRange(saved.Components); work.arcSlots.AddRange(saved.ArcSlots);
            var priorNeighbors=work.neighbors;work.neighbors=null;priorNeighbors.Release();work.neighbors=NavPackedSlots.RestoreAdmitted(saved.Neighbors,limits.MaxNodes * 4,saved.NeighborsDirectoryCapacity,reserve);
            var priorIncoming=work.incoming;work.incoming=null;priorIncoming.Release();work.incoming=NavPackedSlots.RestoreAdmitted(saved.Incoming,limits.MaxNodes * 4,saved.IncomingDirectoryCapacity,reserve);
            var priorTraceSlots=work.traceSlots;work.traceSlots=null;priorTraceSlots.Release();work.traceSlots=NavPackedSlots.RestoreAdmitted(saved.TraceSlots,limits.MaxNodes * 4,saved.TraceSlotsDirectoryCapacity,reserve);
            foreach(var t in saved.Traces){
                if(!(reserve?.Target is INavStorageReservation allocation)){work.traces.Add(Array.AsReadOnly((NavTypedLeg[])t.Clone()));continue;}
                var copy=NavPagedList<NavTypedLeg>.Create(reserve);try{
                    foreach(var leg in t){if(leg==null)throw new ArgumentException("Missing partition trace leg.");var owned=NavTypedLeg.CreateOwned(LayeredNavigationCompileWork.CanonicalLocation(provider,leg.From),LayeredNavigationCompileWork.CanonicalLocation(provider,leg.To),leg.Cost,leg.Kind,CanonicalSeam(provider,leg.PortalId),allocation,provider);try{copy.Add(owned);}finally{owned.ReleaseStorage();}}
                    copy.Seal();work.traces.Add(copy);
                }finally{copy.ReleaseStorage();}
            }
            work.grid.ReleaseStorage();work.grid=null;
            if (saved.Phase != Stage.Ready) work.grid = NavPackedSlots.RestoreAdmitted(saved.Grid, limits.MaxSamples,saved.GridDirectoryCapacity,reserve);
            else {
                work.grid = null;
                work.locations.Seal();work.components.Seal();work.arcSlots.Seal();work.traces.Seal();work.neighbors.Seal();work.incoming.Seal();work.traceSlots.Seal();
                work.Graph = LayeredNavigationGraph.FromPartition(provider, NavGraphBinding.CreateAdmitted(provider,clearance,profile,reserve),
                    work.locations, work.components, work.neighbors, work.incoming,
                    work.arcSlots, work.traceSlots, work.traces, work.traceBytes,reserve);
            }
            if(work.locations.DirectoryCapacity!=saved.LocationsDirectoryCapacity||work.components.DirectoryCapacity!=saved.ComponentsDirectoryCapacity||work.arcSlots.DirectoryCapacity!=saved.ArcSlotsDirectoryCapacity||work.traces.DirectoryCapacity!=saved.TracesDirectoryCapacity)
                throw new ArgumentException("Missing captured page-directory capacity.");
            return work;
            }catch{work.ReleaseStorage();throw;}
        }
    }
}
