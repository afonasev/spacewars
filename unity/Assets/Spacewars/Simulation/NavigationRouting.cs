using System;
using System.Collections.Generic;

namespace Spacewars.Simulation
{
    public sealed class SharedFlowRouter
    {
#if UNITY_INCLUDE_TESTS
        // Private test-only seams preserve exact FIFO and allocation-failure contracts.
        // These members and call sites are absent from normal Player assemblies.
        private static bool testSparseFields;
        private static Func<int,int[]> testDistanceAllocator;
        private static Action<int,int> testFieldDequeued;
#endif
        private struct Cell : IEquatable<Cell> { public readonly int X, Z; public Cell(int x, int z) { X = x; Z = z; } public bool Equals(Cell other) { return X == other.X && Z == other.Z; } public override bool Equals(object other) { return other is Cell && Equals((Cell)other); } public override int GetHashCode() { return X * 486187739 ^ Z; } }
        private sealed class Distances
        {
            private Dictionary<Cell,int> sparse;private readonly int side;private int[] dense;private int count;private readonly bool generic;
            internal Distances(int boundedSide,IReadOnlyList<Cell> seeds,bool generic=false)
            {
                side=boundedSide;this.generic=generic;
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
            internal int Count=>dense==null?sparse.Count:count;
            internal bool ContainsKey(Cell cell)=>TryGetValue(cell,out _);
            internal bool TryGetValue(Cell cell,out int value)
            {if(dense==null)return sparse.TryGetValue(cell,out value);int index=Index(cell);int stored=index<0?0:dense[index];value=stored==0?0:stored-1;return stored!=0;}
            internal int this[Cell cell]
            {
                get{if(TryGetValue(cell,out var value))return value;throw new KeyNotFoundException();}
                set{int index=Index(cell);if(dense!=null&&index<0){sparse=new Dictionary<Cell,int>();for(int i=0;i<dense.Length;i++)if(dense[i]!=0)sparse.Add(generic?new Cell(i/side,i%side):new Cell(i%side-1,i/side-1),dense[i]-1);dense=null;}
                    if(dense==null){sparse[cell]=value;return;}if(dense[index]==0)count++;dense[index]=checked(value+1);}
            }
        }
        private sealed class Field { public readonly Distances Distance;public readonly Queue<Cell> Frontier=new Queue<Cell>();internal Field(int side,IReadOnlyList<Cell> seeds,bool generic){Distance=new Distances(side,seeds,generic);} }
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
        private readonly NavGeometry geometry; private readonly NavigationProfile profile; private readonly Dictionary<string, Field> fields = new Dictionary<string, Field>();
        private readonly int denseWidth;
        private readonly byte[] denseFree,denseEdges;
        private readonly Queue<string> fieldOrder = new Queue<string>();
        // An exhausted field is a closed set under this exact directed grid graph.
        // Retain at most one such witness even when its distance field is evicted.
        private Distances closedReachability;
        public long ClosedReachabilityHits {get;private set;}
        // Geometry/profile are immutable for this router. Cache only exact grid predicates,
        // retaining direction because numerical boundary predicates need not be symmetric.
        private readonly Dictionary<Cell,bool> freeCells = new Dictionary<Cell,bool>();
        private readonly Dictionary<Cell,byte> edges = new Dictionary<Cell,byte>();
        public long GridGeometryChecks {get;private set;}
        public void PrepareGridPredicates(Func<bool> stopping=null)
        {
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
            if(maximum<0)throw new ArgumentOutOfRangeException(nameof(maximum));
            while(fields.Count>maximum){var key=fieldOrder.Dequeue();CachedCells-=fields[key].Distance.Count;fields.Remove(key);}
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
        {
            if(geometry==null||profile==null)throw new ArgumentNullException();this.geometry=geometry;this.profile=profile;
            // Borrow only pure preparation; never retain the predecessor or its field dictionaries.
            var prior=previousObserverRouter?.preparation;
            preparation=prior!=null&&prior.Matches(geometry,profile)?prior:new GeometryPreparation(geometry,profile);
        }
        public int PreparedGeometryBytes=>preparation?.Bytes??0;
        public int PreparedGeometrySide=>preparation?.AllocatedSide??0;
        public long PreparedCellComputations=>preparation?.CellComputations??0;
        public long PreparedEdgeComputations=>preparation?.EdgeComputations??0;
        private sealed class GeometryPreparation
        {
            private readonly NavGeometry geometry;private readonly double radius,gridCell;private readonly int side;
            private byte[] values;private bool allocationUnavailable;
            internal long CellComputations,EdgeComputations;
            internal GeometryPreparation(NavGeometry geometry,NavigationProfile profile)
            {
                this.geometry=geometry;radius=profile.Radius;gridCell=profile.GridCell;
                double dimension=Math.Ceiling(2d*geometry.HalfExtent/gridCell)+2d;
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
            internal int Bytes=>values==null?0:checked(values.Length);
            internal int AllocatedSide=>values==null?0:side;
            private int Index(Cell cell)=>side==0||cell.X< -1||cell.Z< -1||cell.X>=side-1||cell.Z>=side-1?-1:(cell.Z+1)*side+cell.X+1;
            private bool EnsureValues()
            {
                if(values!=null)return true;if(side==0||allocationUnavailable)return false;
                try{values=new byte[checked(side*side)];}catch(OutOfMemoryException){allocationUnavailable=true;return false;}
                return true;
            }
            // Five independent base-3 digits: unknown=0, blocked=1, free=2. All243 states fit one byte.
            private static int Trit(byte value,int power)=>value/power%3;
            private static byte WithTrit(byte value,int power,int answer)=>checked((byte)(value+(answer-Trit(value,power))*power));
            internal bool CellFree(Cell cell)
            {
                int index=Index(cell);if(index<0||!EnsureValues())return OriginalCellIsFree(cell,geometry,gridCell,radius);
                byte value=values[index];int answer=Trit(value,1);if(answer!=0)return answer==2;
                bool free=OriginalCellIsFree(cell,geometry,gridCell,radius);CellComputations++;
                values[index]=WithTrit(value,1,free?2:1);return free;
            }
            internal bool EdgeFree(Cell from,Cell to)
            {
                int index=Index(from),target=Index(to);long dx=(long)to.X-from.X,dz=(long)to.Z-from.Z;
                int direction=dx==0&&dz==1?0:dx==1&&dz==0?1:dx==0&&dz== -1?2:dx== -1&&dz==0?3:-1;
                if(index<0||target<0||direction<0||!EnsureValues())return geometry.SegmentFree(Point(from,geometry.HalfExtent,gridCell),Point(to,geometry.HalfExtent,gridCell),radius);
                int power=direction==0?3:direction==1?9:direction==2?27:81;byte value=values[index];int answer=Trit(value,power);
                if(answer!=0)return answer==2;
                bool free=geometry.SegmentFree(Point(from,geometry.HalfExtent,gridCell),Point(to,geometry.HalfExtent,gridCell),radius);EdgeComputations++;
                values[index]=WithTrit(value,power,free?2:1);return free;
            }
        }
        public int RebuildCount { get; private set; }
        public int FieldCacheHits {get;private set;}
        public int CachedFieldCount => fields.Count;
        public long ExpandedCells {get;private set;}
        public long CachedCells {get;private set;}
        public NavPoint[] FindPath(NavPoint start,NavPoint goal)
        {
            var key=new PathKey(start,goal);
            if(pathCache.TryGetValue(key,out var cached)){PathCacheHits++;return (NavPoint[])cached.Clone();}
            var result=FindPathCore(start,goal);
            if(result.Length<=8192){
                while(pathCache.Count>0&&(pathCache.Count==64||CachedPathPoints+result.Length>8192)){var old=pathOrder.Dequeue();CachedPathPoints-=pathCache[old].Length;pathCache.Remove(old);}
                pathCache.Add(key,(NavPoint[])result.Clone());CachedPathPoints+=result.Length;pathOrder.Enqueue(key);
            }
            return result;
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
                var directField = GetField(target); ExpandUntil(directField, new[] { from });
                if (directField.Distance.ContainsKey(from)) return Route(start, goal, from, directField);
            }
            var starts = Attachments(start); var goals = Attachments(goal);
            if (starts.Count == 0 || goals.Count == 0) return new NavPoint[0];
            if(ClosedCannotReach(goals,starts)){ClosedReachabilityHits++;return new NavPoint[0];}
            Cell bestStart = default(Cell); Field bestField = null;
            double bestCost = double.PositiveInfinity;
            // A single multi-source field considers every attached goal component. This avoids
            // rebuilding the same arena once per alternative while retaining disconnected roots.
            Field field = GetField(goals); ExpandUntil(field, starts);
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
        private NavPoint[] Route(NavPoint start, NavPoint goal, Cell from, Field field)
        {
            var raw = new List<NavPoint>(); Cell current = from; raw.Add(start); raw.Add(ToPoint(from));
            while (field.Distance[current] != 0) { Cell next = BestNeighbor(field, current); if (next.Equals(current)) return new NavPoint[0]; current = next; raw.Add(ToPoint(current)); }
            raw.Add(goal); var route = Smooth(raw); var previous = start;
            foreach (var point in route)
            {
                if (!geometry.IsFree(point, profile.Radius) || !geometry.SegmentFree(previous, point, profile.Radius)) return new NavPoint[0];
                previous = point;
            }
            return route;
        }
        private Field GetField(Cell target) => GetField(new[] { target });
        private Field GetField(IReadOnlyList<Cell> targets)
        {
            string key = geometry.Revision + ":" + profile.Radius;
            foreach (Cell target in targets) key += ":" + target.X + ":" + target.Z;
            Field field; if (fields.TryGetValue(key, out field)){FieldCacheHits++;return field;}
            int fieldSide=preparation?.AllocatedSide??denseWidth;
#if UNITY_INCLUDE_TESTS
            if(testSparseFields)fieldSide=0;
#endif
            field = new Field(fieldSide,targets,preparation==null);
            foreach (Cell target in targets) { field.Distance[target] = 0; field.Frontier.Enqueue(target); }
            fields[key] = field; fieldOrder.Enqueue(key); CachedCells += field.Distance.Count; RebuildCount++; return field;
        }
        private void ExpandUntil(Field field,IReadOnlyList<Cell> required)
        {
            if(denseWidth>0&&field.Distance.Dense!=null){ExpandDense(field,required);if(field.Frontier.Count==0)closedReachability=field.Distance;return;}
            bool Ready(){for(int i=0;i<required.Count;i++)if(!field.Distance.ContainsKey(required[i]))return false;return true;}
            // BFS discovery fixes shortest distance. When a required cell is discovered,
            // all lower-distance neighbors are already discovered, including exact tie choices.
            // Preserve the queue so another origin can continue this same canonical field.
            while(field.Frontier.Count>0&&!Ready()) {
                Cell cell=field.Frontier.Dequeue();
#if UNITY_INCLUDE_TESTS
                testFieldDequeued?.Invoke(cell.X,cell.Z);
#endif
                ExpandedCells++;int d=field.Distance[cell];
                for(int direction=0;direction<4;direction++) {
                    Cell next=Neighbor(cell,direction);
                    if(field.Distance.ContainsKey(next)||!IsFree(next)||!GridSegmentFreeDirected(cell,next,direction))continue;
                    field.Distance[next]=d+1;CachedCells++;field.Frontier.Enqueue(next);
                }
            }
            if(field.Frontier.Count==0)closedReachability=field.Distance;
        }
        private void ExpandDense(Field field,IReadOnlyList<Cell> required)
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
            if(preparation!=null)return new SharedFlowRouter(next,profile,null);
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
