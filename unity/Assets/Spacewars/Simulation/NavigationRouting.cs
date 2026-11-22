using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Spacewars.Simulation
{
    public sealed class SharedFlowRouter : IDisposable
    {
#if UNITY_INCLUDE_TESTS
        // Private test-only seams preserve exact FIFO and allocation-failure contracts.
        // These members and call sites are absent from normal Player assemblies.
        private static bool testSparseFields;
        private static Func<int,int[]> testDistanceAllocator;
        private static Action<int,int> testFieldDequeued;
        private static Func<int,bool> testNativePageAdmission;
        private static Action<object,int> testFieldPublication;
#endif
        private struct Cell : IEquatable<Cell> { public readonly int X, Z; public Cell(int x, int z) { X = x; Z = z; } public bool Equals(Cell other) { return X == other.X && Z == other.Z; } public override bool Equals(object other) { return other is Cell && Equals((Cell)other); } public override int GetHashCode() { return X * 486187739 ^ Z; } }
        private sealed class Distances : IDisposable
        {
            private Dictionary<Cell,int> sparse;private readonly int side;private int[] dense;private int count;private readonly bool generic;
            // Large observer fields allocate only visited pages, instead of repeatedly
            // growing/copying a dictionary entry for every discovered grid cell. Pages
            // belong to this field (no eviction/reuse across fields or geometries).
            private const int PageShift=5,PageSide=1<<PageShift,PageCells=PageSide*PageSide;
            // Two unsigned 16-bit entries per word. Zero is undiscovered; 65535
            // redirects to the exact sparse int value, so long detours never saturate.
            private const int PageWords=PageCells/2,OverflowEntry=ushort.MaxValue;
            private readonly int pagedSide;private Dictionary<Cell,Page> pages;
            private Cell lastPageKey;private Page lastPage;private bool pageAllocationUnavailable;
            private readonly bool native;private NativePages nativePages;private bool disposed;
            internal bool Retired;
            internal long NativeBytes=>nativePages?.Bytes??0;
            private readonly struct Page
            {
                internal readonly int[] Managed;internal readonly IntPtr Native;
                internal Page(int[] managed){Managed=managed;Native=IntPtr.Zero;}
                internal Page(IntPtr native){Managed=null;Native=native;}
                internal bool Present=>Managed!=null||Native!=IntPtr.Zero;
            }
            // One SafeHandle per field, not per page. The intrusive list owns every
            // actual allocation, even if a subsequent dictionary insertion refuses.
            // Normal lifetime is deterministic; SafeHandle only covers abandoned callers.
            private sealed unsafe class NativePages : SafeHandle
            {
                internal long Bytes {get;private set;}
                internal NativePages():base(IntPtr.Zero,true){}
                public override bool IsInvalid=>handle==IntPtr.Zero;
                internal IntPtr Allocate()
                {
                    if(IsClosed)throw new ObjectDisposedException(nameof(NativePages));
                    int bytes=checked(IntPtr.Size+PageWords*sizeof(int));
#if UNITY_INCLUDE_TESTS
                    if(testNativePageAdmission!=null&&!testNativePageAdmission(bytes))throw new OutOfMemoryException("Native distance page refused");
#endif
                    var block=Marshal.AllocHGlobal(bytes);
                    if(block==IntPtr.Zero)throw new OutOfMemoryException();
                    *(IntPtr*)block=handle;var payload=(int*)((byte*)block+IntPtr.Size);
                    for(int i=0;i<PageWords;i++)payload[i]=0;
                    SetHandle(block);Bytes+=bytes;return (IntPtr)payload;
                }
                protected override bool ReleaseHandle()
                {
                    var block=handle;
                    while(block!=IntPtr.Zero){var next=*(IntPtr*)block;Marshal.FreeHGlobal(block);block=next;}
                    handle=IntPtr.Zero;Bytes=0;return true;
                }
            }
            public void Dispose(){if(disposed)return;disposed=true;nativePages?.Dispose();lastPage=default;pages?.Clear();}

            internal Distances(int boundedSide,IReadOnlyList<Cell> seeds,bool generic=false,int pagedSide=0,bool native=false)
            {
                side=boundedSide;this.generic=generic;this.pagedSide=pagedSide;this.native=native;
                if(side>0&&seeds!=null){bool valid=true;foreach(var seed in seeds)if(Index(seed)<0){valid=false;break;}
                    if(valid)try{int length=checked(side*side);
#if UNITY_INCLUDE_TESTS
                        dense=testDistanceAllocator==null?new int[length]:testDistanceAllocator(length);
#else
                        dense=new int[length];
#endif
                        if(dense!=null&&dense.Length!=length)dense=null;}catch(OutOfMemoryException){dense=null;}
                }
                if(dense==null)sparse=new Dictionary<Cell,int>();
            }
            private int Index(Cell cell)=>generic?(side==0||cell.X<0||cell.Z<0||cell.X>=side||cell.Z>=side?-1:cell.X*side+cell.Z):(side==0||cell.X< -1||cell.Z< -1||cell.X>=side-1||cell.Z>=side-1?-1:(cell.Z+1)*side+cell.X+1);
            internal int[] Dense=>dense;
            internal void AddDense(int index,int distance){dense[index]=distance+1;count++;}
            internal int Count=>dense==null?count+sparse.Count:count;
            private bool TryPage(Cell cell,bool create,out Page page,out int index)
            {
                if(disposed)throw new ObjectDisposedException(nameof(Distances));
                page=default;index=0;long x=(long)cell.X+1,z=(long)cell.Z+1;
                if(pagedSide==0||x<0||z<0||x>=pagedSide||z>=pagedSide)return false;
                var key=new Cell((int)(x>>PageShift),(int)(z>>PageShift));
                index=((int)z&(PageSide-1))*PageSide+((int)x&(PageSide-1));
                if(lastPage.Present&&key.Equals(lastPageKey)){page=lastPage;return true;}
                if(pages!=null&&pages.TryGetValue(key,out page)){lastPageKey=key;lastPage=page;return true;}
                if(!create||pageAllocationUnavailable)return false;
                try{
                    if(native){if(nativePages==null)nativePages=new NativePages();page=new Page(nativePages.Allocate());}
                    else{
#if UNITY_INCLUDE_TESTS
                        var array=testDistanceAllocator==null?new int[PageWords]:testDistanceAllocator(PageWords);
#else
                        var array=new int[PageWords];
#endif
                        if(array==null||array.Length!=PageWords){pageAllocationUnavailable=true;return false;}
                        page=new Page(array);
                    }
                    if(pages==null)pages=new Dictionary<Cell,Page>();
                    pages.Add(key,page);lastPageKey=key;lastPage=page;return true;
                }catch(OutOfMemoryException){pageAllocationUnavailable=true;page=default;return false;}
            }
            private unsafe int PageEntry(Page page,int offset)
            {
                int word=page.Managed!=null?page.Managed[offset>>1]:((int*)page.Native)[offset>>1];
                GC.KeepAlive(nativePages);return (int)((uint)word>>((offset&1)*16))&ushort.MaxValue;
            }
            private unsafe void SetPageEntry(Page page,int offset,int value)
            {
                int index=offset>>1,shift=(offset&1)*16;
                int word=page.Managed!=null?page.Managed[index]:((int*)page.Native)[index];
                int next=unchecked((int)(((uint)word&~(0xffffu<<shift))|((uint)value<<shift)));
                if(page.Managed!=null)page.Managed[index]=next;else ((int*)page.Native)[index]=next;
                GC.KeepAlive(nativePages);
            }
            internal bool ContainsKey(Cell cell)=>TryGetValue(cell,out _);
            internal bool TryGetValue(Cell cell,out int value)
            {if(dense==null){if(TryPage(cell,false,out var page,out int offset)){int entry=PageEntry(page,offset);if(entry==OverflowEntry)return sparse.TryGetValue(cell,out value);value=entry==0?0:entry-1;return entry!=0;}return sparse.TryGetValue(cell,out value);}int index=Index(cell);int stored=index<0?0:dense[index];value=stored==0?0:stored-1;return stored!=0;}
            internal int this[Cell cell]
            {
                get{if(TryGetValue(cell,out var value))return value;throw new KeyNotFoundException();}
                set{int index=Index(cell);if(dense!=null&&index<0){sparse=new Dictionary<Cell,int>();for(int i=0;i<dense.Length;i++)if(dense[i]!=0)sparse.Add(generic?new Cell(i/side,i%side):new Cell(i%side-1,i/side-1),dense[i]-1);dense=null;count=0;}
                    if(dense==null){if(TryPage(cell,true,out var page,out int offset)){
                        int stored=checked(value+1),previous=PageEntry(page,offset);
                        if(stored>=OverflowEntry){sparse[cell]=value;if(previous!=0&&previous!=OverflowEntry)count--;SetPageEntry(page,offset,OverflowEntry);}
                        else{if(previous==OverflowEntry)sparse.Remove(cell);if(previous==0||previous==OverflowEntry)count++;SetPageEntry(page,offset,stored);}
                    }else sparse[cell]=value;return;}if(dense[index]==0)count++;dense[index]=checked(value+1);}
            }
        }
        private sealed class Field { public bool FreeRoots;public readonly Distances Distance;public readonly Queue<Cell> Frontier=new Queue<Cell>();internal Field(int side,IReadOnlyList<Cell> seeds,bool generic,int pagedSide,bool native){Distance=new Distances(side,seeds,generic,pagedSide,native);} }
        // Each router owns one immutable geometry/profile. Keys retain only ordered roots.
        // Lookup views may borrow a caller list; dictionary entries snapshot multi-root lists.
        private readonly struct FieldKey : IEquatable<FieldKey>
        {
            private readonly Cell single;private readonly IReadOnlyList<Cell> roots;private readonly int hash;
            internal int Count {get;}
            internal Cell At(int index)=>Count==1?single:roots[index];
            internal FieldKey(Cell root){single=root;roots=null;Count=1;hash=unchecked(397^root.GetHashCode());}
            internal FieldKey(IReadOnlyList<Cell> values){Count=values.Count;single=Count==1?values[0]:default;roots=Count>1?values:null;int h=Count;for(int i=0;i<Count;i++)h=unchecked(h*397^values[i].GetHashCode());hash=h;}
            internal FieldKey Snapshot(){if(Count<=1)return this;var copy=new Cell[Count];for(int i=0;i<Count;i++)copy[i]=roots[i];return new FieldKey(copy);}
            public bool Equals(FieldKey other){if(Count!=other.Count)return false;for(int i=0;i<Count;i++)if(!At(i).Equals(other.At(i)))return false;return true;}
            public override bool Equals(object other)=>other is FieldKey key&&Equals(key);
            public override int GetHashCode()=>hash;
        }
        // Constrained generic calls keep this view on the stack; no shared mutable scratch.
        private readonly struct SingleCellList : IReadOnlyList<Cell>
        {
            private readonly Cell value;internal SingleCellList(Cell cell){value=cell;}
            public int Count=>1;public Cell this[int index]=>index==0?value:throw new IndexOutOfRangeException();
            public IEnumerator<Cell> GetEnumerator(){yield return value;}
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
        }
        private readonly struct PathKey : IEquatable<PathKey> {
            private readonly long ax,az,bx,bz;
            internal PathKey(NavPoint a,NavPoint b){ax=BitConverter.DoubleToInt64Bits(a.X);az=BitConverter.DoubleToInt64Bits(a.Z);bx=BitConverter.DoubleToInt64Bits(b.X);bz=BitConverter.DoubleToInt64Bits(b.Z);}
            public bool Equals(PathKey o)=>ax==o.ax&&az==o.az&&bx==o.bx&&bz==o.bz;
            public override bool Equals(object o)=>o is PathKey k&&Equals(k);
            public override int GetHashCode()=>unchecked(((ax.GetHashCode()*397^az.GetHashCode())*397^bx.GetHashCode())*397^bz.GetHashCode());
        }
        private readonly Dictionary<PathKey,NavPoint[]> pathCache=new Dictionary<PathKey,NavPoint[]>();
        private readonly Queue<PathKey> pathOrder=new Queue<PathKey>();
        public int CachedPathCount=>pathCache.Count;
        public int CachedPathPoints{get;private set;}
        public long PathCacheHits{get;private set;}
        private readonly NavGeometry geometry; private readonly NavigationProfile profile; private readonly Dictionary<FieldKey, Field> fields = new Dictionary<FieldKey, Field>();
        private readonly int denseWidth;
        private readonly byte[] denseFree,denseEdges;
        private readonly Queue<FieldKey> fieldOrder = new Queue<FieldKey>();
        // An exhausted field is a closed set under this exact directed grid graph.
        // Retain at most one such witness even when its distance field is evicted.
        private Distances closedReachability;
        private readonly bool nativeDistancePages;
        private bool disposed;
        public long NativeDistanceBytes {
            get{long bytes=closedReachability?.Retired==true?closedReachability.NativeBytes:0;foreach(var field in fields.Values)bytes+=field.Distance.NativeBytes;return bytes;}
        }
        private void SetClosedReachability(Distances next)
        {
            var previous=closedReachability;closedReachability=next;
            if(previous!=next&&previous?.Retired==true)previous.Dispose();
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            foreach(var field in fields.Values)field.Distance.Dispose();
            closedReachability?.Dispose();closedReachability=null;fields.Clear();fieldOrder.Clear();CachedCells=0;
            pathCache.Clear();pathOrder.Clear();CachedPathPoints=0;
        }
        private void ThrowIfDisposed(){if(disposed)throw new ObjectDisposedException(nameof(SharedFlowRouter));}

        public long ClosedReachabilityHits {get;private set;}
        // Geometry/profile are immutable for this router. Cache only exact grid predicates,
        // retaining direction because numerical boundary predicates need not be symmetric.
        private readonly Dictionary<Cell,bool> freeCells = new Dictionary<Cell,bool>();
        private readonly Dictionary<Cell,byte> edges = new Dictionary<Cell,byte>();
        public long GridGeometryChecks {get;private set;}
        public void PrepareGridPredicates(Func<bool> stopping=null)
        {
            ThrowIfDisposed();
            if(preparation!=null){preparation.Prepare(stopping);return;}
            if(denseWidth==0)return; // Never preallocate an unbounded sparse arena.
            for(int x=0;x<denseWidth;x++){
                if(stopping?.Invoke()==true)return;
                for(int z=0;z<denseWidth;z++){
                    var cell=new Cell(x,z);if(!IsFree(cell))continue;
                    for(int direction=0;direction<4;direction++){
                        var next=Neighbor(cell,direction);
                        if(next.X<0||next.Z<0||next.X>=denseWidth||next.Z>=denseWidth||!IsFree(next))continue;
                        GridSegmentFreeDirected(cell,next,direction);
                    }
                }
            }
        }
        public void TrimFields(int maximum) {
            ThrowIfDisposed();if(maximum<0)throw new ArgumentOutOfRangeException(nameof(maximum));
            while(fields.Count>maximum){var key=fieldOrder.Dequeue();var distance=fields[key].Distance;CachedCells-=distance.Count;fields.Remove(key);distance.Retired=true;if(distance!=closedReachability)distance.Dispose();}
        }
        public SharedFlowRouter(NavGeometry geometry, NavigationProfile profile) { if (geometry == null || profile == null) throw new ArgumentNullException(); this.geometry = geometry; this.profile = profile;
            // Bounded dense addressing removes hot dictionary lookups on ordinary maps.
            // Larger geometries retain sparse storage instead of allocating their whole arena.
            double width=Math.Ceiling(2*geometry.HalfExtent/profile.GridCell);
            if(width>0&&width<=1024){denseWidth=(int)width;denseFree=new byte[denseWidth*denseWidth];denseEdges=new byte[denseFree.Length];}
        }
        // Observer-only reconstructible preparation. Generic two-argument callers stay unprepared.
        public const int ObserverPreparationPayloadLimit=128*1024;
        private readonly GeometryPreparation preparation;
        public SharedFlowRouter(NavGeometry geometry,NavigationProfile profile,SharedFlowRouter previousObserverRouter)
            :this(geometry,profile,previousObserverRouter,false){}
        // Large observed maps memoize the same exact predicates in bounded tiles.
        // Existing callers retain their original dense eligibility/fallback contract.
        public SharedFlowRouter(NavGeometry geometry,NavigationProfile profile,SharedFlowRouter previousObserverRouter,bool memoizeLargeGeometry)
            :this(geometry,profile,previousObserverRouter,memoizeLargeGeometry,false){}
        public SharedFlowRouter(NavGeometry geometry,NavigationProfile profile,SharedFlowRouter previousObserverRouter,bool memoizeLargeGeometry,bool nativeDistancePages)
        {
            this.nativeDistancePages=nativeDistancePages;
            if(geometry==null||profile==null)throw new ArgumentNullException();this.geometry=geometry;this.profile=profile;
            // Borrow only pure preparation; never retain the predecessor or its field dictionaries.
            var prior=previousObserverRouter?.preparation;
            preparation=prior!=null&&prior.MemoizeLargeGeometry==memoizeLargeGeometry&&prior.Matches(geometry,profile)?prior:new GeometryPreparation(geometry,profile,memoizeLargeGeometry);
        }
        public int PreparedGeometryBytes=>preparation?.Bytes??0;
        public int PreparedGeometrySide=>preparation?.AllocatedSide??0;
        public long PreparedCellComputations=>preparation?.CellComputations??0;
        public long PreparedEdgeComputations=>preparation?.EdgeComputations??0;
        private sealed class GeometryPreparation
        {
            private readonly NavGeometry geometry;private readonly double radius,gridCell;private readonly int side;
            private readonly int tiledSide;
            internal readonly bool MemoizeLargeGeometry;
            private const int TileSide=32,TileCells=TileSide*TileSide,TileLimit=ObserverPreparationPayloadLimit/TileCells;
            private Dictionary<Cell,byte[]> tiles;private Queue<Cell> tileOrder;
            // Reuse only the immediately previous lookup; this does not promote FIFO entries.
            private Cell lastTileKey;private byte[] lastTile;
            private byte[] values;private bool allocationUnavailable;
            internal long CellComputations,EdgeComputations;
            internal GeometryPreparation(NavGeometry geometry,NavigationProfile profile,bool memoizeLargeGeometry)
            {
                this.geometry=geometry;radius=profile.Radius;gridCell=profile.GridCell;MemoizeLargeGeometry=memoizeLargeGeometry;
                double dimension=Math.Ceiling(2d*geometry.HalfExtent/gridCell)+2d;
                if(memoizeLargeGeometry&&!double.IsNaN(dimension)&&!double.IsInfinity(dimension)&&dimension>=2d&&dimension<=int.MaxValue)tiledSide=(int)dimension;
                // One-cell halo is the existing attachment ring, not a larger search footprint.
                if(double.IsNaN(dimension)||double.IsInfinity(dimension)||dimension<2d||dimension>Math.Sqrt(ObserverPreparationPayloadLimit))return;
                int candidate=checked((int)dimension);long payload=checked((long)candidate*candidate);
                if(payload<=ObserverPreparationPayloadLimit)side=candidate;
            }
            internal void Prepare(Func<bool> stopping)
            {
                for(int x=-1;x<side-1;x++){
                    if(stopping?.Invoke()==true)return;
                    for(int z=-1;z<side-1;z++){
                        var cell=new Cell(x,z);if(!CellFree(cell))continue;
                        for(int direction=0;direction<4;direction++){
                            var next=Neighbor(cell,direction);if(CellFree(next))EdgeFree(cell,next);
                        }
                    }
                }
            }
            internal bool Matches(NavGeometry next,NavigationProfile nextProfile)=>ReferenceEquals(geometry,next)&&radius==nextProfile.Radius&&gridCell==nextProfile.GridCell;
            internal int Bytes=>values==null?checked((tiles?.Count??0)*TileCells):checked(values.Length);
            internal int AllocatedSide=>values==null?0:side;
            internal int PagedDistanceSide=>side==0?tiledSide:0;
            private int Index(Cell cell)=>side==0||cell.X< -1||cell.Z< -1||cell.X>=side-1||cell.Z>=side-1?-1:(cell.Z+1)*side+cell.X+1;
            private bool EnsureValues()
            {
                if(values!=null)return true;if(side==0||allocationUnavailable)return false;
                try{values=new byte[checked(side*side)];}catch(OutOfMemoryException){allocationUnavailable=true;return false;}
                return true;
            }
            private bool TryTile(Cell cell,out byte[] tile,out int index)
            {
                tile=null;index=0;
                // Preserve nonrepresentable/out-of-halo and dense-allocation-failure fallbacks.
                long x=(long)cell.X+1,z=(long)cell.Z+1;
                if(side!=0||tiledSide==0||x<0||z<0||x>=tiledSide||z>=tiledSide)return false;
                var key=new Cell((int)(x/TileSide),(int)(z/TileSide));index=(int)(z%TileSide)*TileSide+(int)(x%TileSide);
                if(lastTile!=null&&key.Equals(lastTileKey)){tile=lastTile;return true;}
                if(tiles!=null&&tiles.TryGetValue(key,out tile)){lastTileKey=key;lastTile=tile;return true;}
                if(allocationUnavailable)return false;
                // Clear before recycling, including failure after an old array is cleared.
                lastTile=null;
                try{
                    if(tiles==null){tiles=new Dictionary<Cell,byte[]>(TileLimit);tileOrder=new Queue<Cell>(TileLimit);}
                    if(tiles.Count==TileLimit){var old=tileOrder.Dequeue();tile=tiles[old];tiles.Remove(old);Array.Clear(tile,0,tile.Length);}
                    else tile=new byte[TileCells];
                    tiles.Add(key,tile);tileOrder.Enqueue(key);lastTileKey=key;lastTile=tile;return true;
                }catch(OutOfMemoryException){allocationUnavailable=true;tile=null;return false;}
            }
            // Five independent base-3 digits: unknown=0, blocked=1, free=2. All243 states fit one byte.
            private static int Trit(byte value,int power)=>value/power%3;
            private static byte WithTrit(byte value,int power,int answer)=>checked((byte)(value+(answer-Trit(value,power))*power));
            internal bool CellFree(Cell cell)
            {
                int index=Index(cell);byte[] storage;
                if(index>=0&&EnsureValues())storage=values;
                else if(!TryTile(cell,out storage,out index))return OriginalCellIsFree(cell,geometry,gridCell,radius);
                byte value=storage[index];int answer=Trit(value,1);if(answer!=0)return answer==2;
                bool free=OriginalCellIsFree(cell,geometry,gridCell,radius);CellComputations++;
                storage[index]=WithTrit(value,1,free?2:1);return free;
            }
            internal bool EdgeFree(Cell from,Cell to)=>EdgeFreeCore(from,to,false);
            internal bool EdgeFreeCertified(Cell from,Cell to)=>EdgeFreeCore(from,to,true);
            private bool EdgeFreeCore(Cell from,Cell to,bool freeEndpoints)
            {
                int index=Index(from),target=Index(to);long dx=(long)to.X-from.X,dz=(long)to.Z-from.Z;
                int direction=dx==0&&dz==1?0:dx==1&&dz==0?1:dx==0&&dz== -1?2:dx== -1&&dz==0?3:-1;
                byte[] storage;
                if(direction<0)return geometry.SegmentFree(Point(from,geometry.HalfExtent,gridCell),Point(to,geometry.HalfExtent,gridCell),radius);
                if(index>=0&&target>=0&&EnsureValues())storage=values;
                else if((long)to.X+1<0||(long)to.Z+1<0||(long)to.X+1>=tiledSide||(long)to.Z+1>=tiledSide||!TryTile(from,out storage,out index))return geometry.SegmentFree(Point(from,geometry.HalfExtent,gridCell),Point(to,geometry.HalfExtent,gridCell),radius);
                int power=direction==0?3:direction==1?9:direction==2?27:81;byte value=storage[index];int answer=Trit(value,power);
                if(answer!=0)return answer==2;
                bool free=freeEndpoints?geometry.SegmentFreeBetweenFreeEndpoints(Point(from,geometry.HalfExtent,gridCell),Point(to,geometry.HalfExtent,gridCell),radius):geometry.SegmentFree(Point(from,geometry.HalfExtent,gridCell),Point(to,geometry.HalfExtent,gridCell),radius);EdgeComputations++;
                storage[index]=WithTrit(value,power,free?2:1);return free;
            }
            // Dense expansion already proves these array indices and direction.
            // Unknown predicates still use the original exact computation/cache.
            internal bool DenseCellFree(Cell cell,int index)
            {int answer=values[index]%3;return answer!=0?answer==2:CellFree(cell);}
            internal bool DenseEdgeFree(Cell from,Cell to,int index,int direction)
            {int power=direction==0?3:direction==1?9:direction==2?27:81;int answer=values[index]/power%3;return answer!=0?answer==2:EdgeFree(from,to);}
        }
        public int RebuildCount { get; private set; }
        public int FieldCacheHits {get;private set;}
        public int CachedFieldCount => fields.Count;
        public long ExpandedCells {get;private set;}
        public long CachedCells {get;private set;}
        public NavPoint[] FindPath(NavPoint start,NavPoint goal)
        {
            ThrowIfDisposed();
            var key=new PathKey(start,goal);
            if(pathCache.TryGetValue(key,out var cached)){PathCacheHits++;return (NavPoint[])cached.Clone();}
            var result=FindPathCore(start,goal);
            if(result.Length<=8192){
                while(pathCache.Count>0&&(pathCache.Count==64||CachedPathPoints+result.Length>8192)){var old=pathOrder.Dequeue();CachedPathPoints-=pathCache[old].Length;pathCache.Remove(old);}
                pathCache.Add(key,(NavPoint[])result.Clone());CachedPathPoints+=result.Length;pathOrder.Enqueue(key);
            }
            return result;
        }
        // Wreck waiting asks for reachability, never for waypoints. A clear row or
        // column certifies all the original four-neighbour grid edges along that leg.
        // An inconclusive certificate always falls back to the ordinary router.
        internal bool HasPath(NavPoint start,NavPoint goal)
        {
            ThrowIfDisposed();
            if(!geometry.IsFree(start,profile.Radius)||!geometry.IsFree(goal,profile.Radius))return false;
            if(geometry.SegmentFreeBetweenFreeEndpoints(start,goal,profile.Radius))return true;
            var from=ToCell(start);var to=ToCell(goal);
            if(Attached(start,from)&&Attached(goal,to)&&geometry.SegmentFree(ToPoint(to),goal,profile.Radius))
            {
                var a=ToPoint(from);var b=ToPoint(to);
                var corner=new NavPoint(a.X,b.Z);
                if(ClearGridLeg(a,corner)&&ClearGridLeg(corner,b))return true;
                corner=new NavPoint(b.X,a.Z);
                if(ClearGridLeg(a,corner)&&ClearGridLeg(corner,b))return true;
                // A wreck can cut both L-shaped walks while leaving an open middle lane.
                var middle=ToPoint(new Cell((int)(((long)from.X+to.X)/2),(int)(((long)from.Z+to.Z)/2)));
                var c=new NavPoint(a.X,middle.Z);var d=new NavPoint(b.X,middle.Z);
                if(ClearGridLeg(a,c)&&ClearGridLeg(c,d)&&ClearGridLeg(d,b))return true;
                c=new NavPoint(middle.X,a.Z);d=new NavPoint(middle.X,b.Z);
                if(ClearGridLeg(a,c)&&ClearGridLeg(c,d)&&ClearGridLeg(d,b))return true;
            }
            return FindPath(start,goal).Length!=0;
        }

        // Tri-state bounded reachability: false means unfinished, never unreachable.
        // Each credit expands at most four directed grid edges; attachments have a
        // fixed 3x3 stencil. Repeated calls resume the exact immutable field frontier.
        internal bool TryHasPath(NavPoint start,NavPoint goal,ref int credits,out bool reachable)
        {
            ThrowIfDisposed();
            reachable=false;if(credits<=0)return false;
            if(!geometry.IsFree(start,profile.Radius)||!geometry.IsFree(goal,profile.Radius))return true;
            if(geometry.SegmentFreeBetweenFreeEndpoints(start,goal,profile.Radius)){reachable=true;return true;}
            var starts=Attachments(start);var goals=Attachments(goal);
            if(starts.Count==0||goals.Count==0)return true;
            if(ClosedCannotReach(goals,starts))return true;
            var field=GetCertifiedField(goals);
            bool Found(){foreach(var origin in starts)if(field.Distance.ContainsKey(origin))return true;return false;}
            while(credits>0&&field.Frontier.Count>0&&!Found()){
                credits--;var cell=field.Frontier.Dequeue();ExpandedCells++;int distance=field.Distance[cell];
                for(int direction=0;direction<4;direction++){
                    var next=Neighbor(cell,direction);
                    if(field.Distance.ContainsKey(next)||!IsFree(next)||!GridSegmentFreeDirected(cell,next,direction))continue;
                    field.Distance[next]=distance+1;CachedCells++;field.Frontier.Enqueue(next);
                }
            }
            reachable=Found();if(field.Frontier.Count==0)SetClosedReachability(field.Distance);
            return reachable||field.Frontier.Count==0;
        }

        private bool ClearGridLeg(NavPoint a,NavPoint b)
        {
            // Reject even an overlapping obstacle bounding box. All grid centres and
            // their swept discs then fit inside this empty strip, in either direction.
            double pad=profile.Radius+(1+geometry.HalfExtent)*1e-12;
            double left=Math.Min(a.X,b.X)-pad,right=Math.Max(a.X,b.X)+pad;
            double bottom=Math.Min(a.Z,b.Z)-pad,top=Math.Max(a.Z,b.Z)+pad;
            if(left< -geometry.HalfExtent||right>geometry.HalfExtent||bottom< -geometry.HalfExtent||top>geometry.HalfExtent)return false;
            var obstacles=geometry.Obstacles;
            for(int i=0;i<obstacles.Count;i++){
                var obstacle=obstacles[i];
                if(left<=obstacle.MaxX&&right>=obstacle.MinX&&bottom<=obstacle.MaxZ&&top>=obstacle.MinZ)return false;
            }
            return true;
        }

        private NavPoint[] FindPathCore(NavPoint start, NavPoint goal)
        {
            if (!geometry.IsFree(start, profile.Radius) || !geometry.IsFree(goal, profile.Radius)) return new NavPoint[0];
            if (geometry.SegmentFree(start, goal, profile.Radius)) return new[] { goal };
            Cell from = ToCell(start), target = ToCell(goal);
            // Retain the checked floor fast path and its shared field/cache behavior.
            if (Attached(start, from) && Attached(goal, target)&&
                !(closedReachability!=null&&closedReachability.ContainsKey(target)&&!closedReachability.ContainsKey(from)))
            {
                if(nativeDistancePages&&preparation?.AllocatedSide==0&&TryShortestGridRoute(start,goal,from,target,out var shortest))
                    return shortest;
                var directField = GetCertifiedSingleField(target); ExpandSingle(directField, from);
                if (directField.Distance.ContainsKey(from)) return Route(start, goal, from, directField);
            }
            var starts = Attachments(start); var goals = Attachments(goal);
            if (starts.Count == 0 || goals.Count == 0) return new NavPoint[0];
            if(ClosedCannotReach(goals,starts)){ClosedReachabilityHits++;return new NavPoint[0];}
            Cell bestStart = default(Cell); Field bestField = null;
            double bestCost = double.PositiveInfinity;
            // A single multi-source field considers every attached goal component. This avoids
            // rebuilding the same arena once per alternative while retaining disconnected roots.
            Field field = GetCertifiedField(goals); ExpandUntil(field, starts);
            foreach (Cell begin in starts)
            {
                int distance;
                if (!field.Distance.TryGetValue(begin, out distance)) continue;
                double cost = distance * profile.GridCell + Length(start, ToPoint(begin));
                if (cost >= bestCost) continue; // Stable stencil order breaks exact ties.
                bestCost = cost; bestStart = begin; bestField = field;
            }
            return bestField == null ? new NavPoint[0] : Route(start, goal, bestStart, bestField);
        }
        private bool Attached(NavPoint endpoint, Cell cell) => IsFree(cell) && geometry.SegmentFree(endpoint, ToPoint(cell), profile.Radius);
        private bool ClosedCannotReach(IReadOnlyList<Cell> roots,IReadOnlyList<Cell> origins)
        {
            if(closedReachability==null)return false;
            foreach(var root in roots)if(!closedReachability.ContainsKey(root))return false;
            foreach(var origin in origins)if(closedReachability.ContainsKey(origin))return false;
            // Every root is inside the exhausted outgoing closure; no attachment of
            // the origin is. No symmetry assumption or approximate geometry is used.
            return true;
        }
        private List<Cell> Attachments(NavPoint endpoint)
        {
            Cell floor = ToCell(endpoint); var result = new List<Cell>();
            if (Attached(endpoint, floor)) result.Add(floor);
            // One adjacent-cell ring (3x3) is a technical grid attachment invariant, not a
            // gameplay search radius. Never project endpoints or reduce the typed footprint.
            for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++)
            {
                if (x == 0 && z == 0) continue;
                var cell = new Cell(floor.X + x, floor.Z + z);
                if (Attached(endpoint, cell)) result.Add(cell);
            }
            return result;
        }
        private static double Length(NavPoint a, NavPoint b) => Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
        // Find the first Manhattan-shortest walk in the distance field's direction
        // order. If the grid needs a detour or a directed-edge decision, keep the
        // canonical field solver. Bound speculative work on every query.
        private bool TryShortestGridRoute(NavPoint start,NavPoint goal,Cell from,Cell target,out NavPoint[] route)
        {
            long span = Math.Abs((long)from.X - target.X) + Math.Abs((long)from.Z - target.Z);
            long stepsLeft = 8 * span + 256;
            var walk = new List<(Cell Cell, int NextDirection)> { (from, 0) };
            HashSet<Cell> deadEnds = null;
            while (walk.Count > 0 && stepsLeft-- > 0)
            {
                int tail = walk.Count - 1;
                var (cell, firstDirection) = walk[tail];
                if (cell.Equals(target))
                {
                    var raw = new List<NavPoint>(walk.Count + 2) { start };
                    foreach (var step in walk) raw.Add(ToPoint(step.Cell));
                    raw.Add(goal);
                    route = ValidateSmoothedRoute(start, raw);
                    return true;
                }

                bool advanced = false;
                for (int direction = firstDirection; direction < 4; direction++)
                {
                    bool toward = direction switch
                    {
                        0 => cell.Z < target.Z,
                        1 => cell.X < target.X,
                        2 => cell.Z > target.Z,
                        _ => cell.X > target.X
                    };
                    if (!toward) continue;
                    var next = Neighbor(cell, direction);
                    if (deadEnds?.Contains(next) == true || !IsFree(next)) continue;

                    bool forward = GridSegmentFreeDirected(cell, next, direction);
                    bool reverse = GridSegmentFreeDirected(next, cell, (direction + 2) % 4);
                    if (forward != reverse)
                    {
                        route = null;
                        return false;
                    }
                    if (!forward) continue;

                    walk[tail] = (cell, direction + 1);
                    walk.Add((next, 0));
                    advanced = true;
                    break;
                }
                if (advanced) continue;

                // Every step gets closer to the target, so this suffix cannot
                // reach the goal on any other Manhattan-shortest walk either.
                (deadEnds ??= new HashSet<Cell>()).Add(cell);
                walk.RemoveAt(tail);
            }

            route = null;
            return false;
        }

        private NavPoint[] Route(NavPoint start, NavPoint goal, Cell from, Field field)
        {
            var raw = new List<NavPoint>(); Cell current = from; raw.Add(start); raw.Add(ToPoint(from));
            while (field.Distance[current] != 0) { Cell next = BestNeighbor(field, current); if (next.Equals(current)) return new NavPoint[0]; current = next; raw.Add(ToPoint(current)); }
            raw.Add(goal);return ValidateSmoothedRoute(start,raw);
        }

        private NavPoint[] ValidateSmoothedRoute(NavPoint start,List<NavPoint> raw)
        {
            var route=Smooth(raw);
            var previous=start;
            foreach (var point in route)
            {
                if (!geometry.IsFree(point, profile.Radius) || !geometry.SegmentFree(previous, point, profile.Radius)) return new NavPoint[0];
                previous = point;
            }
            return route;
        }
        // Only callers that have attached every root to this immutable geometry/radius
        // may certify the field. Discovered cells already passed IsFree before enqueue.
        // Keep GetField untrusted for synthetic/private callers and fallback behavior.
        private Field GetCertifiedField(IReadOnlyList<Cell> targets)
        {var field=GetField(targets);field.FreeRoots=true;return field;}
        private Field GetCertifiedSingleField(Cell target)
        {var field=GetField(target);field.FreeRoots=true;return field;}
        private Field GetField(Cell target)=>GetFieldCore(new FieldKey(target),null);
        private Field GetField(IReadOnlyList<Cell> targets)=>GetFieldCore(new FieldKey(targets),targets);
        private Field GetFieldCore(FieldKey key,IReadOnlyList<Cell> targets)
        {
            Field field; if (fields.TryGetValue(key, out field)){FieldCacheHits++;return field;}
            // A single-root seed array is needed only when constructing a new field.
            if(targets==null)targets=new[]{key.At(0)};
            var storedKey=key.Snapshot();
            int fieldSide=preparation?.AllocatedSide??denseWidth;
            int pagedSide=preparation?.PagedDistanceSide??0;
#if UNITY_INCLUDE_TESTS
            if(testSparseFields){fieldSide=0;pagedSide=0;}
#endif
            field = new Field(fieldSide,targets,preparation==null,pagedSide,nativeDistancePages);
            try{
                foreach (Cell target in targets) { field.Distance[target] = 0; field.Frontier.Enqueue(target); }
#if UNITY_INCLUDE_TESTS
                testFieldPublication?.Invoke(field,0);
#endif
                fields[storedKey] = field;
#if UNITY_INCLUDE_TESTS
                testFieldPublication?.Invoke(field,1);
#endif
                fieldOrder.Enqueue(storedKey); CachedCells += field.Distance.Count; RebuildCount++; return field;
            }catch{
                // The dictionary may have accepted the field before FIFO growth
                // refused. No published counter/order entry owns its native blocks yet.
                fields.Remove(storedKey);field.Distance.Dispose();throw;
            }
        }
        private void ExpandSingle(Field field,Cell required)=>ExpandRequired(field,new SingleCellList(required));
        private void ExpandUntil(Field field,IReadOnlyList<Cell> required)=>ExpandRequired(field,required);
        private void ExpandRequired<TRequired>(Field field,TRequired required) where TRequired:IReadOnlyList<Cell>
        {
            if(preparation!=null&&field.Distance.Dense!=null){ExpandPreparedDense(field,required);if(field.Frontier.Count==0)SetClosedReachability(field.Distance);return;}
            if(denseWidth>0&&field.Distance.Dense!=null){ExpandDense(field,required);if(field.Frontier.Count==0)SetClosedReachability(field.Distance);return;}
            // Distances only grow during this invocation. Count each missing request entry
            // once (including duplicates), then retire it when that exact cell is discovered.
            int remaining=0;
            for(int i=0;i<required.Count;i++)if(!field.Distance.ContainsKey(required[i]))remaining++;
            // BFS discovery fixes shortest distance. When a required cell is discovered,
            // all lower-distance neighbors are already discovered, including exact tie choices.
            // Preserve the queue so another origin can continue this same canonical field.
            while(field.Frontier.Count>0&&remaining>0) {
                Cell cell=field.Frontier.Dequeue();
#if UNITY_INCLUDE_TESTS
                testFieldDequeued?.Invoke(cell.X,cell.Z);
#endif
                ExpandedCells++;int d=field.Distance[cell];
                for(int direction=0;direction<4;direction++) {
                    Cell next=Neighbor(cell,direction);
                    if(field.Distance.ContainsKey(next)||!IsFree(next))continue;
                    // No cache access may intervene while EdgeFreeCore holds a tile array:
                    // resolving endpoints there could recycle that array at FIFO eviction.
                    if(!(field.FreeRoots&&preparation!=null?preparation.EdgeFreeCertified(cell,next):GridSegmentFreeDirected(cell,next,direction)))continue;
                    field.Distance[next]=d+1;CachedCells++;field.Frontier.Enqueue(next);
                    for(int i=0;i<required.Count;i++)if(next.Equals(required[i]))remaining--;
                }
            }
            if(field.Frontier.Count==0)SetClosedReachability(field.Distance);
        }
        private void ExpandPreparedDense<TRequired>(Field field,TRequired required) where TRequired:IReadOnlyList<Cell>
        {
            int side=preparation.AllocatedSide,remaining=0;var distances=field.Distance.Dense;var wanted=new int[required.Count];
            for(int i=0;i<required.Count;i++){
                var cell=required[i];int index=cell.X< -1||cell.Z< -1||cell.X>=side-1||cell.Z>=side-1?-1:(cell.Z+1)*side+cell.X+1;
                // An outside required cell keeps the original exhaustion behavior.
                wanted[i]=index;if(index<0||distances[index]==0)remaining++;
            }
            while(field.Frontier.Count>0&&remaining>0){
                var cell=field.Frontier.Dequeue();
#if UNITY_INCLUDE_TESTS
                testFieldDequeued?.Invoke(cell.X,cell.Z);
#endif
                ExpandedCells++;int index=(cell.Z+1)*side+cell.X+1,d=distances[index]-1;
                for(int direction=0;direction<4;direction++){
                    var next=Neighbor(cell,direction);
                    if(next.X< -1||next.Z< -1||next.X>=side-1||next.Z>=side-1)continue;
                    int ni=(next.Z+1)*side+next.X+1;
                    if(distances[ni]!=0||!preparation.DenseCellFree(next,ni)||!preparation.DenseEdgeFree(cell,next,index,direction))continue;
                    field.Distance.AddDense(ni,d+1);CachedCells++;field.Frontier.Enqueue(next);
                    for(int i=0;i<wanted.Length;i++)if(ni==wanted[i])remaining--;
                }
            }
        }
        private void ExpandDense<TRequired>(Field field,TRequired required) where TRequired:IReadOnlyList<Cell>
        {
            var distances=field.Distance.Dense;int remaining=0;var wanted=new int[required.Count];
            for(int i=0;i<required.Count;i++){if(!DenseIndex(required[i],out int index))throw new InvalidOperationException("Free attachment outside dense grid.");wanted[i]=index;if(distances[index]==0)remaining++;}
            while(field.Frontier.Count>0&&remaining>0){
                var cell=field.Frontier.Dequeue();
#if UNITY_INCLUDE_TESTS
                testFieldDequeued?.Invoke(cell.X,cell.Z);
#endif
                ExpandedCells++;int index=cell.X*denseWidth+cell.Z,d=distances[index]-1;
                for(int direction=0;direction<4;direction++){
                    var next=Neighbor(cell,direction);
                    if(next.X<0||next.Z<0||next.X>=denseWidth||next.Z>=denseWidth)continue;
                    int ni=next.X*denseWidth+next.Z;if(distances[ni]!=0)continue;
                    byte free=denseFree[ni];if(free==1||free==0&&!IsFree(next))continue;
                    byte state=denseEdges[index];int tested=1<<direction,allowed=1<<(direction+4);
                    if((state&tested)!=0){if((state&allowed)==0)continue;}else if(!GridSegmentFreeDirected(cell,next,direction))continue;
                    field.Distance.AddDense(ni,d+1);CachedCells++;field.Frontier.Enqueue(next);
                    for(int i=0;i<wanted.Length;i++)if(ni==wanted[i])remaining--;
                }
            }
        }
        // Changing observed solids invalidates all distance fields, but grid predicates
        // outside the exact changed bounds still describe the same geometry.
        public SharedFlowRouter ForGeometry(NavGeometry next)
        {
            ThrowIfDisposed();
            if(preparation!=null)return new SharedFlowRouter(next,profile,null,preparation.MemoizeLargeGeometry,nativeDistancePages);
            var result=new SharedFlowRouter(next,profile);
            if(denseWidth==0||denseWidth!=result.denseWidth||geometry.HalfExtent!=next.HalfExtent)return result;
            Array.Copy(denseFree,result.denseFree,denseFree.Length);Array.Copy(denseEdges,result.denseEdges,denseEdges.Length);
            var changed=new List<NavObstacle>();
            foreach(var o in geometry.Obstacles){bool same=false;foreach(var n in next.Obstacles)if(o.GeometryEquals(n)){same=true;break;}if(!same)changed.Add(o);}
            foreach(var n in next.Obstacles){bool same=false;foreach(var o in geometry.Obstacles)if(n.GeometryEquals(o)){same=true;break;}if(!same)changed.Add(n);}
            foreach(var o in changed){
                double reach=profile.Radius+profile.GridCell;
                int Bound(double coordinate,int margin)=>(int)Math.Max(0,Math.Min(denseWidth-1,Math.Floor((coordinate+geometry.HalfExtent)/profile.GridCell)+margin));
                int left=Bound(o.MinX-reach,-1),right=Bound(o.MaxX+reach,1),bottom=Bound(o.MinZ-reach,-1),top=Bound(o.MaxZ+reach,1);
                for(int x=left;x<=right;x++)for(int z=bottom;z<=top;z++){
                    int index=x*denseWidth+z;result.denseFree[index]=0;result.denseEdges[index]=0;
                }
            }
            return result;
        }
        private static Cell Neighbor(Cell cell,int direction)=>direction==0?new Cell(cell.X,cell.Z+1):direction==1?new Cell(cell.X+1,cell.Z):direction==2?new Cell(cell.X,cell.Z-1):new Cell(cell.X-1,cell.Z);
        private bool GridSegmentFree(Cell cell,Cell next)
        {long dx=(long)next.X-cell.X,dz=(long)next.Z-cell.Z;int direction=dx==0&&dz==1?0:dx==1&&dz==0?1:dx==0&&dz== -1?2:dx== -1&&dz==0?3:-1;return direction<0?geometry.SegmentFree(ToPoint(cell),ToPoint(next),profile.Radius):GridSegmentFreeDirected(cell,next,direction);}
        private bool GridSegmentFreeDirected(Cell cell,Cell next,int direction)
        {
            if(preparation!=null)return preparation.EdgeFree(cell,next);
            bool dense=DenseIndex(cell,out int index);byte state;
            if(dense)state=denseEdges[index];else edges.TryGetValue(cell,out state);
            int tested=1<<direction,allowed=1<<(direction+4);
            if((state&tested)==0){GridGeometryChecks++;bool free=geometry.SegmentFree(ToPoint(cell),ToPoint(next),profile.Radius);state=(byte)(state|tested|(free?allowed:0));if(dense)denseEdges[index]=state;else edges[cell]=state;}
            return (state&allowed)!=0;
        }
        private Cell BestNeighbor(Field field,Cell current) {
            if(!field.Distance.TryGetValue(current,out int best))return current;Cell result=current;
            for(int direction=0;direction<4;direction++){Cell next=Neighbor(current,direction);if(field.Distance.TryGetValue(next,out int d)&&d<best&&GridSegmentFreeDirected(current,next,direction)){best=d;result=next;}}
            return result;
        }
        private IEnumerable<Cell> Neighbors(Cell cell) { yield return new Cell(cell.X, cell.Z + 1); yield return new Cell(cell.X + 1, cell.Z); yield return new Cell(cell.X, cell.Z - 1); yield return new Cell(cell.X - 1, cell.Z); }
        private Cell ToCell(NavPoint point) { return new Cell((int)Math.Floor((point.X + geometry.HalfExtent) / profile.GridCell), (int)Math.Floor((point.Z + geometry.HalfExtent) / profile.GridCell)); }
        private static NavPoint Point(Cell cell,double halfExtent,double gridCell)=>new NavPoint(-halfExtent+(cell.X+.5d)*gridCell,-halfExtent+(cell.Z+.5d)*gridCell);
        private static bool OriginalCellIsFree(Cell cell,NavGeometry geometry,double gridCell,double radius)
        {NavPoint point=Point(cell,geometry.HalfExtent,gridCell);return point.X>= -geometry.HalfExtent&&point.X<=geometry.HalfExtent&&point.Z>= -geometry.HalfExtent&&geometry.IsFree(point,radius);}
        private NavPoint ToPoint(Cell cell) { return new NavPoint(-geometry.HalfExtent + (cell.X + .5d) * profile.GridCell, -geometry.HalfExtent + (cell.Z + .5d) * profile.GridCell); }
        private bool IsFree(Cell cell) {
            if(preparation!=null)return preparation.CellFree(cell);
            bool dense=DenseIndex(cell,out int index),free;
            if(dense&&denseFree[index]!=0)return denseFree[index]==2;
            if(!dense&&freeCells.TryGetValue(cell,out free))return free;
            NavPoint point=ToPoint(cell);GridGeometryChecks++;
            free=point.X>=-geometry.HalfExtent&&point.X<=geometry.HalfExtent&&point.Z>=-geometry.HalfExtent&&geometry.IsFree(point,profile.Radius);
            if(dense)denseFree[index]=(byte)(free?2:1);else freeCells[cell]=free;return free;
        }
        private bool DenseIndex(Cell cell,out int index){index=0;if(denseWidth==0||cell.X<0||cell.Z<0||cell.X>=denseWidth||cell.Z>=denseWidth)return false;index=cell.X*denseWidth+cell.Z;return true;}
        private NavPoint[] Smooth(List<NavPoint> raw) { var result = new List<NavPoint>(); int anchor = 0; while (anchor < raw.Count - 1) { int chosen = anchor + 1; for (int next = chosen + 1; next < raw.Count; next++) if (geometry.SegmentFree(raw[anchor], raw[next], profile.Radius)) chosen = next; else break; result.Add(raw[chosen]); anchor = chosen; } return result.ToArray(); }
    }
}
