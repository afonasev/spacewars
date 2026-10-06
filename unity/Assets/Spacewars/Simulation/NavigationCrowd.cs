using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Spacewars.Simulation
{
    public enum NavigationOutcome { Idle, Moving, Arrived, Stopped, Held, Unreachable, Blocked, Rejected }
    public sealed class NavUnit
    {
        internal NavUnit(int id, NavPoint position,double radius,double speed,double turn) { Id = id; Position = position;Radius=radius;Speed=speed;TurnSpeed=turn; }
        public double Radius{get;} public double Speed{get; internal set;} public double TurnSpeed{get;}
        public NavigationOutcome Outcome {get;internal set;}
        public long Incarnation {get;internal set;} public long MobilityRevision {get;internal set;} public int Team {get;set;} public int Id { get; private set; } public NavPoint Position { get; internal set; } public double Heading { get; internal set; } public bool Moving { get; internal set; } public bool Held { get; internal set; } public NavPoint Goal { get; internal set; } public IReadOnlyList<NavPoint> Route { get { return routeView; } }
        internal NavPoint[] route = new NavPoint[0]; internal IReadOnlyList<NavPoint> routeView = Array.AsReadOnly(new NavPoint[0]); internal int routeIndex, noProgressTicks; internal NavPoint[] localRoute=Array.Empty<NavPoint>(); internal int localIndex; internal double blockedSeconds;
        internal void ReplaceRoute(NavPoint[] value) { route = value; routeView = Array.AsReadOnly(route); routeIndex = 0; localRoute=Array.Empty<NavPoint>();localIndex=0;blockedSeconds=0;noProgressTicks=0; }
    }
    // Authority state only. The spatial index is derived from these ordered units on restore.
    [Serializable]
    public sealed class NavUnitSaveState
    {
        public long Incarnation,MobilityRevision; public int Team, Id, RouteIndex, LocalIndex, NoProgressTicks;
        public NavPoint Position, Goal;
        public double Radius, Speed, TurnSpeed, Heading, BlockedSeconds;
        public bool Moving, Held;
        public NavigationOutcome Outcome;
        public NavPoint[] Route, LocalRoute;
    }
    [Serializable]
    public sealed class NavCrowdSaveState
    {
        public long NextIncarnation; public double HalfExtent,MaximumRadius;
        public int GeometryRevision, ProfileRevision, RepairCount, RepairFailed, MaxNoProgressTicks;
        public string ProfileId;
        public NavObstacle[] Obstacles;
        public double[] ProfileValues;
        public NavUnitSaveState[] Units;
    }
    public sealed class NavCrowd
    {
        private long nextIncarnation; private double maximumRadius;
        private NavGeometry geometry; private readonly NavigationProfile profile; private readonly Dictionary<int, NavUnit> units = new Dictionary<int, NavUnit>(); private readonly List<NavUnit> orderedUnits = new List<NavUnit>(); private readonly IReadOnlyList<NavUnit> unitView; private readonly Dictionary<long, List<NavUnit>> spatial = new Dictionary<long, List<NavUnit>>();
        public NavCrowd(NavGeometry geometry, NavigationProfile profile) { if (geometry == null || profile == null) throw new ArgumentNullException(); this.geometry = geometry; this.profile = profile; unitView = orderedUnits.AsReadOnly(); }
        public IReadOnlyList<NavUnit> Units { get { return unitView; } }
        public int NeighborCandidates { get; private set; }
        public int RepairCount { get; private set; }
        public int RepairFailed { get; private set; }
        public double RepairCpuMs { get; private set; }
        public int MaxNoProgressTicks { get; private set; }
        public NavCrowdSaveState CaptureState()
        {
            var state=new NavCrowdSaveState{NextIncarnation=nextIncarnation,HalfExtent=geometry.HalfExtent,MaximumRadius=maximumRadius,GeometryRevision=geometry.Revision,
                Obstacles=new NavObstacle[geometry.Obstacles.Count],ProfileId=profile.ProfileId,ProfileRevision=profile.Revision,
                ProfileValues=new double[NavigationProfile.Metadata.Count],RepairCount=RepairCount,RepairFailed=RepairFailed,
                MaxNoProgressTicks=MaxNoProgressTicks,Units=new NavUnitSaveState[orderedUnits.Count]};
            for(int i=0;i<state.Obstacles.Length;i++)state.Obstacles[i]=geometry.Obstacles[i];
            for(int i=0;i<state.ProfileValues.Length;i++)state.ProfileValues[i]=NavigationProfile.Metadata[i].Read(profile);
            for(int i=0;i<orderedUnits.Count;i++){
                var u=orderedUnits[i];state.Units[i]=new NavUnitSaveState{Incarnation=u.Incarnation,MobilityRevision=u.MobilityRevision,Team=u.Team,Id=u.Id,Position=u.Position,Goal=u.Goal,Radius=u.Radius,Speed=u.Speed,
                    TurnSpeed=u.TurnSpeed,Heading=u.Heading,Moving=u.Moving,Held=u.Held,Outcome=u.Outcome,
                    Route=(NavPoint[])u.route.Clone(),RouteIndex=u.routeIndex,LocalRoute=(NavPoint[])u.localRoute.Clone(),
                    LocalIndex=u.localIndex,NoProgressTicks=u.noProgressTicks,BlockedSeconds=u.blockedSeconds};
            }
            return state;
        }
        public void RestoreState(NavCrowdSaveState state)
        {
            if(state==null||units.Count!=0||state.Obstacles==null||state.ProfileValues==null||state.Units==null||
                state.HalfExtent!=geometry.HalfExtent||state.GeometryRevision!=geometry.Revision||
                state.ProfileId!=profile.ProfileId||state.ProfileRevision!=profile.Revision||
                state.Obstacles.Length!=geometry.Obstacles.Count||state.ProfileValues.Length!=NavigationProfile.Metadata.Count)
                throw new ArgumentException("Crowd save provenance or destination mismatch.",nameof(state));
            for(int i=0;i<state.Obstacles.Length;i++)if(!state.Obstacles[i].GeometryEquals(geometry.Obstacles[i]))throw new ArgumentException("Geometry mismatch.",nameof(state));
            for(int i=0;i<state.ProfileValues.Length;i++)if(state.ProfileValues[i]!=NavigationProfile.Metadata[i].Read(profile))throw new ArgumentException("Profile mismatch.",nameof(state));
            var seen=new HashSet<int>();
            foreach(var u in state.Units)if(u==null||!seen.Add(u.Id)||u.Route==null||u.LocalRoute==null||
                u.RouteIndex<0||u.RouteIndex>u.Route.Length||u.LocalIndex<0||u.LocalIndex>u.LocalRoute.Length||
                u.NoProgressTicks<0||u.BlockedSeconds<0||!geometry.IsFree(u.Position,u.Radius)||
                u.Radius<=0||u.Speed<=0||u.TurnSpeed<=0||double.IsNaN(u.Heading+u.Speed+u.TurnSpeed+u.BlockedSeconds))
                throw new ArgumentException("Invalid crowd unit save.",nameof(state));
            if(state.MaximumRadius<0||double.IsNaN(state.MaximumRadius)||double.IsInfinity(state.MaximumRadius)||System.Linq.Enumerable.Any(state.Units,u=>u.Radius>state.MaximumRadius)||state.NextIncarnation<0||state.RepairCount<0||state.RepairFailed<0||state.MaxNoProgressTicks<0||System.Linq.Enumerable.Any(state.Units,u=>u.Incarnation<1||u.Incarnation>state.NextIncarnation||u.MobilityRevision<0||!Enum.IsDefined(typeof(NavigationOutcome),u.Outcome)||u.Held&&u.Moving))throw new ArgumentException("Invalid crowd allocator/counters.");
            foreach(var u in state.Units){
                var unit=new NavUnit(u.Id,u.Position,u.Radius,u.Speed,u.TurnSpeed);units.Add(u.Id,unit);orderedUnits.Add(unit);maximumRadius=Math.Max(maximumRadius,u.Radius);unit.Incarnation=u.Incarnation;unit.MobilityRevision=u.MobilityRevision;unit.Team=u.Team;unit.Goal=u.Goal;unit.Heading=u.Heading;
                unit.Moving=u.Moving;unit.Held=u.Held;unit.Outcome=u.Outcome;unit.route=(NavPoint[])u.Route.Clone();
                unit.routeView=Array.AsReadOnly(unit.route);unit.routeIndex=u.RouteIndex;unit.localRoute=(NavPoint[])u.LocalRoute.Clone();
                unit.localIndex=u.LocalIndex;unit.noProgressTicks=u.NoProgressTicks;unit.blockedSeconds=u.BlockedSeconds;
            }
            maximumRadius=state.MaximumRadius;nextIncarnation=state.NextIncarnation;RepairCount=state.RepairCount;RepairFailed=state.RepairFailed;MaxNoProgressTicks=state.MaxNoProgressTicks;
            BuildIndex();
        }
        public NavUnit Add(int id,NavPoint point)=>Add(id,point,profile.Radius,profile.TankSpeed,profile.TankTurnSpeed);
        public NavUnit Add(int id,NavPoint point,double radius,double speed,double turn)
        {
            if(units.ContainsKey(id)||radius<=0||speed<=0||turn<=0||double.IsNaN(radius+speed+turn)||double.IsInfinity(radius+speed+turn)||!CanPlace(point,radius))throw new ArgumentException("Invalid unit placement.");
            var unit=new NavUnit(id,point,radius,speed,turn){Incarnation=++nextIncarnation};units.Add(id,unit);maximumRadius=Math.Max(maximumRadius,radius);orderedUnits.Add(unit);orderedUnits.Sort((a,b)=>a.Id.CompareTo(b.Id));AddToIndex(unit);return unit;
        }
        public bool TryGet(int id,out NavUnit unit)=>units.TryGetValue(id,out unit);
        public void SetSpeed(int id,double speed){if(units.TryGetValue(id,out var unit)&&speed>0)unit.Speed=speed;}
        public bool CanPlace(NavPoint point)=>CanPlace(point,profile.Radius);
        public bool CanPlace(NavPoint point,double radius)=>geometry.IsFree(point,radius)&&UnitFree(null,point,point,radius);
        public bool Remove(int id)
        {
            NavUnit unit;
            if (!units.TryGetValue(id, out unit)) return false;
            Remove(unit); units.Remove(id); orderedUnits.Remove(unit); return true;
        }
        public bool SetRoute(int id, NavPoint goal, NavPoint[] route)
        { NavUnit unit; if (!units.TryGetValue(id, out unit) || route == null || route.Length == 0 || !geometry.IsFree(goal, unit.Radius)) return false;
            // Numerical tolerance only for float NavMesh corner conversion, never arrival range.
            if(Distance(route[route.Length-1],goal)>1e-4)return false;
            var exact=new NavPoint[route.Length+1];Array.Copy(route,exact,route.Length);exact[route.Length]=goal;route=exact;
            NavPoint previous = unit.Position; for (int i = 0; i < route.Length; i++) { if (!geometry.IsFree(route[i], unit.Radius) || !geometry.SegmentFree(previous, route[i], unit.Radius)) return false; previous = route[i]; } unit.ReplaceRoute((NavPoint[])route.Clone()); unit.Goal = goal; unit.Held = false; unit.Moving = true; unit.Outcome=NavigationOutcome.Moving; return true; }
        public void Stop(int id, bool hold) { NavUnit unit; if (!units.TryGetValue(id, out unit)) return; unit.MobilityRevision++;unit.ReplaceRoute(new NavPoint[0]); unit.Moving = false; unit.Held = hold;unit.Outcome=hold?NavigationOutcome.Held:NavigationOutcome.Stopped; }
        public void SetGeometry(NavGeometry geometry) { if (geometry == null) throw new ArgumentNullException("geometry"); this.geometry = geometry; foreach (NavUnit unit in units.Values) { unit.ReplaceRoute(new NavPoint[0]); unit.Moving = false;unit.Outcome=unit.Held?NavigationOutcome.Held:NavigationOutcome.Stopped; } }
        public void Step(double dt)
        {
            if (dt <= 0d || Double.IsNaN(dt) || Double.IsInfinity(dt)) throw new ArgumentException("Invalid step duration."); NeighborCandidates = 0; BuildIndex();
            foreach (NavUnit unit in orderedUnits) StepUnit(unit, dt);
        }
        public void Reject(int id,NavigationOutcome outcome){ NavUnit unit; if(!units.TryGetValue(id,out unit))return; Stop(id,false);unit.Outcome=outcome;}
        private void StepUnit(NavUnit unit, double dt)
        {
            if (!unit.Moving || unit.Held || unit.routeIndex >= unit.route.Length) { if(unit.routeIndex>=unit.route.Length)unit.Moving=false; return; }
            if(unit.localIndex>=unit.localRoute.Length)
                while(unit.routeIndex+1<unit.route.Length&&geometry.SegmentFree(unit.Position,unit.route[unit.routeIndex+1],unit.Radius)&&HeldSegmentFree(unit,unit.Position,unit.route[unit.routeIndex+1]))unit.routeIndex++;
            bool local=unit.localIndex<unit.localRoute.Length;
            NavPoint target=local?unit.localRoute[unit.localIndex]:unit.route[unit.routeIndex];
            double dx=target.X-unit.Position.X,dz=target.Z-unit.Position.Z,distance=Math.Sqrt(dx*dx+dz*dz);
            if(distance<=profile.ArrivalTolerance){
                unit.blockedSeconds=0;unit.noProgressTicks=0;
                if(local)unit.localIndex++;else if(++unit.routeIndex>=unit.route.Length){unit.Moving=false;unit.Outcome=NavigationOutcome.Arrived;}
                return;
            }
            double desired=Math.Atan2(dz,dx);
            unit.Heading=Turn(unit.Heading,desired,unit.TurnSpeed*dt);
            if(Math.Abs(Normalize(desired-unit.Heading))>profile.ForwardAlignmentAngle)return;
            var candidate=Forward(unit.Position,unit.Heading,Math.Min(unit.Speed*dt,distance));
            if(!FreeMove(unit,candidate)){
                MarkNoProgress(unit);unit.blockedSeconds+=dt;
                if(unit.noProgressTicks*dt>=profile.BlockedTimeoutSeconds){Reject(unit.Id,NavigationOutcome.Blocked);return;}
                if(unit.blockedSeconds>=profile.RepairRetrySeconds){unit.blockedSeconds=0;CommitDetour(unit,unit.route[unit.routeIndex]);}
                return;
            }
            Remove(unit);unit.Position=candidate;AddToIndex(unit);unit.blockedSeconds=0;unit.noProgressTicks=0;
        }
        private bool FreeMove(NavUnit unit,NavPoint candidate){return Clear(unit,unit.Position,candidate);}
        private bool Clear(NavUnit unit,NavPoint from,NavPoint to){return geometry.SegmentFree(from,to,unit.Radius)&&UnitFree(unit,from,to);}
        private sealed class LocalNode
        {
            public int X,Z;public double Cost,Score,Side;public LocalNode Parent;public bool Closed;
        }
        private bool CommitDetour(NavUnit unit,NavPoint target)
        {
            var clock=Stopwatch.StartNew();
            // Finite local lattice derived from the named grid resolution/range. Exact
            // predicates, rather than inflated neighbor boxes, govern every edge.
            double cell=profile.LocalGridCell;
            int radius=(int)Math.Ceiling(profile.LocalDetourDistance/cell);
            var nodes=new Dictionary<long,LocalNode>();var open=new List<LocalNode>();
            var start=new LocalNode{X=0,Z=0};nodes[Key(0,0)]=start;open.Add(start);
            LocalNode best=null;double bestDistance=Distance(unit.Position,target);
            while(open.Count>0){
                int selected=0;for(int i=1;i<open.Count;i++)if(open[i].Score<open[selected].Score-1e-9||(Math.Abs(open[i].Score-open[selected].Score)<=1e-9&&open[i].Side<open[selected].Side))selected=i;
                var current=open[selected];open.RemoveAt(selected);if(current.Closed)continue;current.Closed=true;
                var point=new NavPoint(unit.Position.X+current.X*cell,unit.Position.Z+current.Z*cell);
                double remaining=Distance(point,target);
                if(current!=start&&remaining<bestDistance){best=current;bestDistance=remaining;}
                if(current!=start&&Clear(unit,point,target)){best=current;break;}
                for(int ox=-1;ox<=1;ox++)for(int oz=-1;oz<=1;oz++){
                    if(ox==0&&oz==0)continue;int x=current.X+ox,z=current.Z+oz;
                    if(x*x+z*z>radius*radius)continue;
                    var nextPoint=new NavPoint(unit.Position.X+x*cell,unit.Position.Z+z*cell);
                    if(!Clear(unit,point,nextPoint))continue;
                    long key=Key(x,z);LocalNode next;double cost=current.Cost+Distance(point,nextPoint);
                    if(nodes.TryGetValue(key,out next)){if(next.Closed||cost>=next.Cost)continue;}
                    else{next=new LocalNode{X=x,Z=z};nodes[key]=next;open.Add(next);}
                    next.Cost=cost;next.Score=cost+Distance(nextPoint,target);next.Side=(target.X-unit.Position.X)*z-(target.Z-unit.Position.Z)*x;next.Parent=current;
                }
            }
            if(best==null){RepairFailed++;RepairCpuMs+=clock.Elapsed.TotalMilliseconds;return false;}
            var reversed=new List<NavPoint>();for(var n=best;n!=start;n=n.Parent)reversed.Add(new NavPoint(unit.Position.X+n.X*cell,unit.Position.Z+n.Z*cell));reversed.Reverse();
            // Smooth only verified local edges. The original global route stays intact.
            var route=new List<NavPoint>();var anchor=unit.Position;int index=0;
            while(index<reversed.Count){int far=index;for(int j=index+1;j<reversed.Count;j++)if(Clear(unit,anchor,reversed[j]))far=j;route.Add(reversed[far]);anchor=reversed[far];index=far+1;}
            unit.localRoute=route.ToArray();unit.localIndex=0;RepairCount++;RepairCpuMs+=clock.Elapsed.TotalMilliseconds;return true;
        }
        private static double Distance(NavPoint a,NavPoint b){double x=a.X-b.X,z=a.Z-b.Z;return Math.Sqrt(x*x+z*z);}
        private void MarkNoProgress(NavUnit unit) { unit.noProgressTicks++; if (unit.noProgressTicks > MaxNoProgressTicks) MaxNoProgressTicks = unit.noProgressTicks; }
        private bool HeldSegmentFree(NavUnit self,NavPoint from,NavPoint to)
        {
            foreach(var other in orderedUnits)if(other!=self&&other.Held&&other.Team==self.Team&&SegmentPointDistanceSquared(other.Position,from,to)<(self.Radius+other.Radius)*(self.Radius+other.Radius))return false;
            return true;
        }
        private bool UnitFree(NavUnit self, NavPoint from, NavPoint to,double radius=0)
        {
            radius=self?.Radius??radius;double maxRadius=maximumRadius;
            int reach = (int)Math.Ceiling((radius+maxRadius + Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Z - from.Z) * (to.Z - from.Z)) + profile.GridCell) / profile.GridCell); int cx = Cell(to.X), cz = Cell(to.Z);
            for (int x = cx - reach; x <= cx + reach; x++) for (int z = cz - reach; z <= cz + reach; z++) { List<NavUnit> candidates; if (!spatial.TryGetValue(Key(x, z), out candidates)) continue; for (int i = 0; i < candidates.Count; i++) { NavUnit other = candidates[i]; if (other == self) continue; NeighborCandidates++; double distance = SegmentPointDistanceSquared(other.Position, from, to); if (distance < (radius+other.Radius) * (radius+other.Radius) - .000000001d) return false; } }
            return true;
        }
        private void BuildIndex() { spatial.Clear(); foreach (NavUnit unit in units.Values) AddToIndex(unit); }
        private void AddToIndex(NavUnit unit) { long key = Key(Cell(unit.Position.X), Cell(unit.Position.Z)); List<NavUnit> list; if (!spatial.TryGetValue(key, out list)) { list = new List<NavUnit>(); spatial[key] = list; } list.Add(unit); }
        private void Remove(NavUnit unit) { List<NavUnit> list; if (spatial.TryGetValue(Key(Cell(unit.Position.X), Cell(unit.Position.Z)), out list)) list.Remove(unit); }
        private int Cell(double value) { return (int)Math.Floor((value + geometry.HalfExtent) / profile.GridCell); } private static long Key(int x, int z) { return ((long)x << 32) ^ (uint)z; }
        private static NavPoint Forward(NavPoint point, double heading, double distance) { return new NavPoint(point.X + Math.Cos(heading) * distance, point.Z + Math.Sin(heading) * distance); }
        private static double Turn(double current, double desired, double maximum) { double difference = Normalize(desired - current); return current + Math.Max(-maximum, Math.Min(maximum, difference)); }
        private static double Normalize(double value) { while (value > Math.PI) value -= Math.PI * 2d; while (value < -Math.PI) value += Math.PI * 2d; return value; }
        private static double SegmentPointDistanceSquared(NavPoint point, NavPoint a, NavPoint b) { double dx = b.X - a.X, dz = b.Z - a.Z, length = dx * dx + dz * dz; if (length == 0d) { dx = point.X - a.X; dz = point.Z - a.Z; return dx * dx + dz * dz; } double t = Math.Max(0d, Math.Min(1d, ((point.X - a.X) * dx + (point.Z - a.Z) * dz) / length)); dx = point.X - (a.X + dx * t); dz = point.Z - (a.Z + dz * t); return dx * dx + dz * dz; }
    }
}
