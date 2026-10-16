using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Spacewars.Simulation
{
    public enum NavigationOutcome { Idle, Moving, Arrived, Stopped, Held, Unreachable, Blocked, Rejected }
    public sealed class NavUnit : INavStorageFacade
    {
        internal NavLiveUnitStorage StorageOwner{get;private set;}
        NavOwnedStorage INavStorageFacade.StorageRoot=>StorageOwner;
        internal void BindStorage(NavLiveUnitStorage owner){if(owner==null||StorageOwner!=null)throw new ArgumentException("Actor already owned.");StorageOwner=owner;}
        internal NavUnit(int id, NavPoint position,double radius,double speed,double turn) { Id = id; Position = position;Radius=radius;Speed=speed;TurnSpeed=turn; }
        public NavLocation Location{get;internal set;} public NavLocation? GoalLocation{get;internal set;}
        public double Radius{get;} public double Speed{get; internal set;} public double TurnSpeed{get; internal set;}
        public NavigationOutcome Outcome {get;internal set;}
        public long FailedGroupId{get;internal set;} public long FailedRevision{get;internal set;} public long FailedRequest{get;internal set;} public long FailedOrder{get;internal set;} public NavLocation? FailedGoal{get;internal set;} public NavigationOutcome LastFailure{get;internal set;}
        public long InstalledGroupId{get;internal set;} public long InstalledRevision{get;internal set;} public long InstalledRequest{get;internal set;} public long InstalledOrder{get;internal set;}
        public long Incarnation {get;internal set;} public long MobilityRevision {get;internal set;} public int Team {get;set;} public int Id { get; private set; } public NavPoint Position { get; internal set; } public double Heading { get; internal set; } public bool Moving { get; internal set; } public bool Held { get; internal set; } public NavPoint Goal { get; internal set; } public IReadOnlyList<NavPoint> Route { get { return routeView; } }
        internal NavPoint[] route = Array.Empty<NavPoint>(); internal IReadOnlyList<NavPoint> routeView = EmptyRouteView; internal int routeIndex, noProgressTicks; internal NavPoint[] localRoute=Array.Empty<NavPoint>(); internal int localIndex; internal double blockedSeconds;
        private static readonly System.Collections.ObjectModel.ReadOnlyCollection<NavPoint> EmptyRouteView=Array.AsReadOnly(Array.Empty<NavPoint>());
        internal void BindRouteBackingNoThrow(NavRetainedArray<NavPoint> global,NavRetainedArray<NavPoint> local)
        {route=global?.ReadOnlyBacking??Array.Empty<NavPoint>();routeView=global==null?(IReadOnlyList<NavPoint>)EmptyRouteView:global;localRoute=local?.ReadOnlyBacking??Array.Empty<NavPoint>();routeIndex=localIndex=noProgressTicks=0;blockedSeconds=0;}
        internal void ClearOwnedRoutes(){if(StorageOwner==null)return;StorageOwner.ClearNoThrow(out var global,out var local);global?.Release();local?.Release();}
        internal static void PrepareStopBacking(){_=EmptyRouteView;}
        internal void ApplyPreparedStopNoThrow(long mobility,bool hold,out NavRetainedArray<NavPoint> global,out NavRetainedArray<NavPoint> local){global=local=null;if(StorageOwner!=null)StorageOwner.ClearNoThrow(out global,out local);MobilityRevision=mobility;route=Array.Empty<NavPoint>();routeView=EmptyRouteView;routeIndex=0;localRoute=Array.Empty<NavPoint>();localIndex=0;blockedSeconds=0;noProgressTicks=0;Moving=false;Held=hold;Outcome=hold?NavigationOutcome.Held:NavigationOutcome.Stopped;FailedRequest=FailedOrder=FailedGroupId=FailedRevision=0;FailedGoal=null;LastFailure=NavigationOutcome.Idle;}
        internal void ApplyPreparedGeometryNoThrow(NavigationOutcome outcome,out NavRetainedArray<NavPoint> global,out NavRetainedArray<NavPoint> local,out NavOwnedStorage installed)
        {StorageOwner.ClearNoThrow(out global,out local);StorageOwner.SetInstalledNoThrow(null,false,null,out installed);BindRouteBackingNoThrow(null,null);Moving=false;Outcome=outcome;}
        internal void CopyCaptureHeaderTo(NavUnitSaveState target)
        {
            target.FailedGroupId=FailedGroupId;target.FailedRevision=FailedRevision;target.FailedRequest=FailedRequest;target.FailedOrder=FailedOrder;target.FailedGoal=FailedGoal;target.LastFailure=LastFailure;target.InstalledGroupId=InstalledGroupId;target.InstalledRevision=InstalledRevision;target.InstalledRequest=InstalledRequest;target.InstalledOrder=InstalledOrder;target.Incarnation=Incarnation;target.MobilityRevision=MobilityRevision;target.Team=Team;target.Id=Id;target.Position=Position;target.Goal=Goal;target.Location=Location;target.GoalLocation=GoalLocation;target.Radius=Radius;target.Speed=Speed;target.TurnSpeed=TurnSpeed;target.Heading=Heading;target.Moving=Moving;target.Held=Held;target.Outcome=Outcome;target.RouteIndex=routeIndex;target.LocalIndex=localIndex;target.NoProgressTicks=noProgressTicks;target.BlockedSeconds=blockedSeconds;
        }
        internal bool SameCaptureHeader(NavUnitSaveState source)
            =>source!=null&&source.Id==Id&&SameBits(source.Radius,Radius)&&SameLocation(source.Location,Location)&&SameLocation(source.GoalLocation,GoalLocation)&&SameBits(source.Speed,Speed)&&SameBits(source.TurnSpeed,TurnSpeed)&&source.Outcome==Outcome&&source.FailedGroupId==FailedGroupId&&source.FailedRevision==FailedRevision&&source.FailedRequest==FailedRequest&&source.FailedOrder==FailedOrder&&SameLocation(source.FailedGoal,FailedGoal)&&source.LastFailure==LastFailure&&source.InstalledGroupId==InstalledGroupId&&source.InstalledRevision==InstalledRevision&&source.InstalledRequest==InstalledRequest&&source.InstalledOrder==InstalledOrder&&source.Incarnation==Incarnation&&source.MobilityRevision==MobilityRevision&&source.Team==Team&&SameBits(source.Position.X,Position.X)&&SameBits(source.Position.Z,Position.Z)&&SameBits(source.Goal.X,Goal.X)&&SameBits(source.Goal.Z,Goal.Z)&&SameBits(source.Heading,Heading)&&source.Moving==Moving&&source.Held==Held&&source.RouteIndex==routeIndex&&source.LocalIndex==localIndex&&source.NoProgressTicks==noProgressTicks&&SameBits(source.BlockedSeconds,blockedSeconds);
        internal NavUnit CopyMovementHeader(){var copy=new NavUnit(Id,Position,Radius,Speed,TurnSpeed);copy.CopyMovementValuesFrom(this);return copy;}
        internal void CopyMovementValuesFrom(NavUnit source){Location=source.Location;GoalLocation=source.GoalLocation;Speed=source.Speed;TurnSpeed=source.TurnSpeed;Outcome=source.Outcome;FailedGroupId=source.FailedGroupId;FailedRevision=source.FailedRevision;FailedRequest=source.FailedRequest;FailedOrder=source.FailedOrder;FailedGoal=source.FailedGoal;LastFailure=source.LastFailure;InstalledGroupId=source.InstalledGroupId;InstalledRevision=source.InstalledRevision;InstalledRequest=source.InstalledRequest;InstalledOrder=source.InstalledOrder;Incarnation=source.Incarnation;MobilityRevision=source.MobilityRevision;Team=source.Team;Position=source.Position;Heading=source.Heading;Moving=source.Moving;Held=source.Held;Goal=source.Goal;route=source.route;routeView=source.routeView;routeIndex=source.routeIndex;noProgressTicks=source.noProgressTicks;localRoute=source.localRoute;localIndex=source.localIndex;blockedSeconds=source.blockedSeconds;}
        internal bool SameMovementValues(NavUnit source)=>Id==source.Id&&SameBits(Radius,source.Radius)&&SameLocation(Location,source.Location)&&SameLocation(GoalLocation,source.GoalLocation)&&SameBits(Speed,source.Speed)&&SameBits(TurnSpeed,source.TurnSpeed)&&Outcome==source.Outcome&&FailedGroupId==source.FailedGroupId&&FailedRevision==source.FailedRevision&&FailedRequest==source.FailedRequest&&FailedOrder==source.FailedOrder&&SameLocation(FailedGoal,source.FailedGoal)&&LastFailure==source.LastFailure&&InstalledGroupId==source.InstalledGroupId&&InstalledRevision==source.InstalledRevision&&InstalledRequest==source.InstalledRequest&&InstalledOrder==source.InstalledOrder&&Incarnation==source.Incarnation&&MobilityRevision==source.MobilityRevision&&Team==source.Team&&SameBits(Position.X,source.Position.X)&&SameBits(Position.Z,source.Position.Z)&&SameBits(Heading,source.Heading)&&Moving==source.Moving&&Held==source.Held&&SameBits(Goal.X,source.Goal.X)&&SameBits(Goal.Z,source.Goal.Z)&&ReferenceEquals(route,source.route)&&ReferenceEquals(routeView,source.routeView)&&routeIndex==source.routeIndex&&noProgressTicks==source.noProgressTicks&&ReferenceEquals(localRoute,source.localRoute)&&localIndex==source.localIndex&&SameBits(blockedSeconds,source.blockedSeconds);
        private static bool SameBits(double a,double b)=>BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b);
        private static bool SameLocation(NavLocation? a,NavLocation? b)=>a.HasValue==b.HasValue&&(!a.HasValue||SameLocation(a.Value,b.Value));
        private static bool SameLocation(NavLocation a,NavLocation b)=>SameBits(a.Position.X,b.Position.X)&&SameBits(a.Position.Z,b.Position.Z)&&a.SurfaceId==b.SurfaceId&&ReferenceEquals(a.NameReference.Owner,b.NameReference.Owner)&&a.NameReference.Role==b.NameReference.Role&&a.NameReference.Index==b.NameReference.Index;
        internal void ReplaceRoute(NavPoint[] value) { if(StorageOwner!=null){if(value.Length!=0)throw new InvalidOperationException("Owned actor requires a prepared route.");ClearOwnedRoutes();return;}route = value; routeView = Array.AsReadOnly(route); routeIndex = 0; localRoute=Array.Empty<NavPoint>();localIndex=0;blockedSeconds=0;noProgressTicks=0; }
    }
    // Authority state only. The spatial index is derived from these ordered units on restore.
    [Serializable]
    public sealed class NavUnitSaveState
    {
        public long FailedGroupId,FailedRevision,FailedRequest,FailedOrder;public NavLocation? FailedGoal;public NavigationOutcome LastFailure;
        public long InstalledGroupId,InstalledRevision,InstalledRequest,InstalledOrder;
        public long Incarnation,MobilityRevision; public int Team, Id, RouteIndex, LocalIndex, NoProgressTicks;
        public NavPoint Position, Goal; public NavLocation? Location,GoalLocation;
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
    public sealed partial class NavCrowd : INavMotionClearance
    {
        internal INavCrowdStorageSource StorageSource;
        internal NavCrowdSpatialStorage SpatialStorage=>spatial;
        internal void ReleaseCrowdStorage(){foreach(var unit in orderedUnits)unit.StorageOwner?.Release();spatial?.Release();spatial=null;}
        private NavUnit CreateUnit(int id,NavPoint point,double radius,double speed,double turn)=>StorageSource==null?new NavUnit(id,point,radius,speed,turn):StorageSource.CreateUnit(id,point,radius,speed,turn);
        private readonly IPlayableTerrain terrain;
        private IReadOnlyList<NavNameReference> ownedSurfaceNames;
        internal void BindSurfaceNamesNoThrow(IReadOnlyList<NavNameReference> names){ownedSurfaceNames=names;}
        private NavLocation LegacyRestoredLocation(NavLocation value)
        {
            // The legacy string has no owned identity. Match BODY hydration's
            // implicit flat selector so subsequent requests share the same names.
            if(value.NameReference.Owner==null&&value.SurfaceId==NavLocation.FlatSurface)return new NavLocation(value.Position,NavLocation.FlatSurface);
            return OwnedLocation(value);
        }
        internal void BindLegacyRestoredSurfaceNames()
        {
            foreach(var actor in units.Values){actor.Location=LegacyRestoredLocation(actor.Location);if(actor.GoalLocation.HasValue)actor.GoalLocation=LegacyRestoredLocation(actor.GoalLocation.Value);if(actor.FailedGoal.HasValue)actor.FailedGoal=LegacyRestoredLocation(actor.FailedGoal.Value);}
        }
        private NavLocation OwnedLocation(NavLocation value)
        {
            if(ownedSurfaceNames==null)return value;
            if(value.NameReference.Owner!=null){((NavOwnedStorage)ownedSurfaceNames).RequireEdge(value.NameReference.Owner);return value;}
            if(value.SurfaceId==NavLocation.FlatSurface)return value;
            for(int i=0;i<ownedSurfaceNames.Count;i++)if(ownedSurfaceNames[i].Resolve()==value.SurfaceId)return new NavLocation(value.Position,ownedSurfaceNames[i]);
            throw new InvalidOperationException("Terrain returned an undeclared actor surface.");
        }
        private long nextIncarnation; private double maximumRadius;
        private NavGeometry geometry; private NavigationProfile profile; private readonly Dictionary<int, NavUnit> units; private readonly List<NavUnit> orderedUnits; private readonly IReadOnlyList<NavUnit> unitView; private NavCrowdSpatialStorage spatial;
        // Transient authority preference. It is rebuilt before every step and is
        // deliberately outside the saved physical actor state.
        private readonly Dictionary<int,double> preferredMaxSpeed;
        private NavPreparedCrowdStep preparedStep;
        public NavCrowd(NavGeometry geometry, NavigationProfile profile,IPlayableTerrain terrain=null) { if (geometry == null || profile == null) throw new ArgumentNullException(); this.terrain=terrain;this.geometry = geometry; this.profile = profile;units=new Dictionary<int,NavUnit>();orderedUnits=new List<NavUnit>();preferredMaxSpeed=new Dictionary<int,double>();unitView=orderedUnits.AsReadOnly(); }
        internal NavGeometry Geometry=>geometry;
        internal long MotionMembership=>nextIncarnation;
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
                ProfileValues=new double[profile.WireValueCount],RepairCount=RepairCount,RepairFailed=RepairFailed,
                MaxNoProgressTicks=MaxNoProgressTicks,Units=new NavUnitSaveState[orderedUnits.Count]};
            for(int i=0;i<orderedUnits.Count;i++){
                var u=orderedUnits[i];state.Units[i]=new NavUnitSaveState{Route=new NavPoint[u.route.Length],LocalRoute=new NavPoint[u.localRoute.Length]};
            }
            CopyCaptureStateTo(state);
            return state;
        }
        internal NavigationProfile CaptureProfile=>profile;
        internal void CopyCaptureSummaryTo(NavCrowdSaveState state)
        {state.NextIncarnation=nextIncarnation;state.HalfExtent=geometry.HalfExtent;state.MaximumRadius=maximumRadius;state.GeometryRevision=geometry.Revision;state.ProfileId=profile.ProfileId;state.ProfileRevision=profile.Revision;state.RepairCount=RepairCount;state.RepairFailed=RepairFailed;state.MaxNoProgressTicks=MaxNoProgressTicks;}
        internal bool SameCaptureSummary(NavCrowdSaveState state)
            =>state.NextIncarnation==nextIncarnation&&BitConverter.DoubleToInt64Bits(state.HalfExtent)==BitConverter.DoubleToInt64Bits(geometry.HalfExtent)&&BitConverter.DoubleToInt64Bits(state.MaximumRadius)==BitConverter.DoubleToInt64Bits(maximumRadius)&&state.GeometryRevision==geometry.Revision&&state.ProfileId==profile.ProfileId&&state.ProfileRevision==profile.Revision&&state.RepairCount==RepairCount&&state.RepairFailed==RepairFailed&&state.MaxNoProgressTicks==MaxNoProgressTicks;
        // The caller prepares the exact DTO records/arrays before the read
        // gate. Observation only copies scalar values and existing route cells.
        internal void CopyCaptureStateTo(NavCrowdSaveState state)
        {
            if(state==null||state.Units==null||state.Units.Length!=orderedUnits.Count||state.Obstacles==null||state.Obstacles.Length!=geometry.Obstacles.Count||state.ProfileValues==null||state.ProfileValues.Length!=profile.WireValueCount)throw new InvalidOperationException("Crowd capture graph shape changed.");
            CopyCaptureSummaryTo(state);
            for(int i=0;i<state.Obstacles.Length;i++)state.Obstacles[i]=geometry.Obstacles[i];
            for(int i=0;i<state.ProfileValues.Length;i++)state.ProfileValues[i]=profile.WireValueAt(i);
            for(int i=0;i<orderedUnits.Count;i++){
                var u=orderedUnits[i];var target=state.Units[i];if(target==null||target.Route==null||target.Route.Length!=u.route.Length||target.LocalRoute==null||target.LocalRoute.Length!=u.localRoute.Length)throw new InvalidOperationException("Crowd capture route shape changed.");
                u.CopyCaptureHeaderTo(target);
                System.Array.Copy(u.route,target.Route,u.route.Length);System.Array.Copy(u.localRoute,target.LocalRoute,u.localRoute.Length);
            }
        }
        public void RestoreState(NavCrowdSaveState state)=>RestoreStateCore(state,null,null);
        internal void RestoreStateCore(NavCrowdSaveState state,IReadOnlyList<NavOwnedStorage> globalRoles,IReadOnlyList<NavOwnedStorage> localRoles)
        {
            if(state==null||units.Count!=0||state.Obstacles==null||state.ProfileValues==null||state.Units==null||
                state.HalfExtent!=geometry.HalfExtent||state.GeometryRevision!=geometry.Revision||
                state.ProfileId!=profile.ProfileId||state.ProfileRevision!=profile.Revision||
                state.Obstacles.Length!=geometry.Obstacles.Count||state.ProfileValues.Length!=profile.WireValueCount)
                throw new ArgumentException("Crowd save provenance or destination mismatch.",nameof(state));
            for(int i=0;i<state.Obstacles.Length;i++)if(!state.Obstacles[i].GeometryEquals(geometry.Obstacles[i]))throw new ArgumentException("Geometry mismatch.",nameof(state));
            if(!profile.MatchesWireValues(state.ProfileValues))throw new ArgumentException("Profile mismatch.",nameof(state));
            var seen=new HashSet<int>();
            foreach(var u in state.Units)if(u==null||!seen.Add(u.Id)||u.Route==null||u.LocalRoute==null||
                u.RouteIndex<0||u.RouteIndex>u.Route.Length||u.LocalIndex<0||u.LocalIndex>u.LocalRoute.Length||
                u.NoProgressTicks<0||u.BlockedSeconds<0||!geometry.IsFree(u.Position,u.Radius)||
                u.Radius<=0||u.Speed<=0||u.TurnSpeed<=0||double.IsNaN(u.Heading+u.Speed+u.TurnSpeed+u.BlockedSeconds))
                throw new ArgumentException("Invalid crowd unit save.",nameof(state));
            if(state.MaximumRadius<0||double.IsNaN(state.MaximumRadius)||double.IsInfinity(state.MaximumRadius)||System.Linq.Enumerable.Any(state.Units,u=>u.Radius>state.MaximumRadius)||state.NextIncarnation<0||state.RepairCount<0||state.RepairFailed<0||state.MaxNoProgressTicks<0||System.Linq.Enumerable.Any(state.Units,u=>u.Incarnation<1||u.Incarnation>state.NextIncarnation||u.MobilityRevision<0||!Enum.IsDefined(typeof(NavigationOutcome),u.Outcome)||u.Held&&u.Moving))throw new ArgumentException("Invalid crowd allocator/counters.");
            int actorIndex=0;foreach(var u in state.Units){
                var unit=CreateUnit(u.Id,u.Position,u.Radius,u.Speed,u.TurnSpeed);units.Add(u.Id,unit);orderedUnits.Add(unit);maximumRadius=Math.Max(maximumRadius,u.Radius);if(u.Location.HasValue){if(!ValidLocation(u.Location.Value,u.Radius)||!u.Location.Value.Position.Equals(u.Position))throw new ArgumentException("Invalid saved actor surface.");unit.Location=u.Location.Value;}
                else if(!Locate(u.Position,u.Radius,out var legacy))throw new ArgumentException("Unsupported legacy actor surface.");else unit.Location=legacy;
                if(u.GoalLocation.HasValue&&(!ValidLocation(u.GoalLocation.Value,u.Radius)||!u.GoalLocation.Value.Position.Equals(u.Goal)))throw new ArgumentException("Invalid saved goal surface.");
                unit.FailedGroupId=u.FailedGroupId;unit.FailedRevision=u.FailedRevision;unit.FailedRequest=u.FailedRequest;unit.FailedOrder=u.FailedOrder;unit.FailedGoal=u.FailedGoal;unit.LastFailure=u.LastFailure;unit.InstalledGroupId=u.InstalledGroupId;unit.InstalledRevision=u.InstalledRevision;unit.InstalledRequest=u.InstalledRequest;unit.InstalledOrder=u.InstalledOrder;unit.GoalLocation=u.GoalLocation;unit.Incarnation=u.Incarnation;unit.MobilityRevision=u.MobilityRevision;unit.Team=u.Team;unit.Goal=u.Goal;unit.Heading=u.Heading;
                unit.Moving=u.Moving;unit.Held=u.Held;unit.Outcome=u.Outcome;
                if(unit.StorageOwner!=null){
                    if(globalRoles!=null){var global=globalRoles[actorIndex] as NavRetainedArray<NavPoint>;var local=localRoles[actorIndex] as NavRetainedArray<NavPoint>;if(!SameRoute(global,u.Route)||!SameRoute(local,u.LocalRoute))throw new ArgumentException("Actor BODY route differs from DTO.");unit.StorageOwner.Hydrate(global,local);}
                    else{NavRetainedArray<NavPoint> global=null,local=null;try{if(u.Route.Length!=0)global=StorageSource.CopyRoute(u.Route,null);if(u.LocalRoute.Length!=0)local=StorageSource.CopyRoute(u.LocalRoute,null);unit.StorageOwner.Hydrate(global,local);}finally{global?.Release();local?.Release();}}
                }
                else{unit.route=(NavPoint[])u.Route.Clone();unit.routeView=Array.AsReadOnly(unit.route);unit.localRoute=(NavPoint[])u.LocalRoute.Clone();}
                unit.routeIndex=u.RouteIndex;
                unit.localIndex=u.LocalIndex;unit.noProgressTicks=u.NoProgressTicks;unit.blockedSeconds=u.BlockedSeconds;actorIndex++;
            }
            maximumRadius=state.MaximumRadius;nextIncarnation=state.NextIncarnation;RepairCount=state.RepairCount;RepairFailed=state.RepairFailed;MaxNoProgressTicks=state.MaxNoProgressTicks;
            BuildIndex();
        }
        private static bool SameRoute(NavRetainedArray<NavPoint> owner,NavPoint[] points){if((owner?.Count??0)!=points.Length)return false;for(int i=0;i<points.Length;i++)if(!owner[i].Equals(points[i]))return false;return true;}
        public NavUnit Add(int id,NavPoint point)=>Add(id,point,profile.Radius,profile.TankSpeed,profile.TankTurnSpeed);
        public NavUnit Add(int id,NavPoint point,double radius,double speed,double turn,NavLocation? assignedLocation=null)
        {
            if(units.ContainsKey(id)||radius<=0||speed<=0||turn<=0||double.IsNaN(radius+speed+turn)||double.IsInfinity(radius+speed+turn)||!geometry.IsFree(point,radius)||!UnitFree(null,point,point,radius))throw new ArgumentException("Invalid unit placement.");
            NavLocation location;if(assignedLocation.HasValue){location=assignedLocation.Value;if(!location.Position.Equals(point)||!ValidLocation(location,radius))throw new ArgumentException("Invalid assigned unit surface.");}else if(!Locate(point,radius,out location))throw new ArgumentException("Unsupported unit surface.");
            location=OwnedLocation(location);int expected=units.Count+1;long incarnation=nextIncarnation;var sourceGeometry=geometry;var sourceProfile=profile;var next=CreateSpatial(expected);NavUnit unit=null;bool inserted=false;
            try{
                if(units.Count+1!=expected||units.ContainsKey(id))throw new InvalidOperationException("Actor placement lost source.");
                unit=CreateUnit(id,point,radius,speed,turn);
                if(units.Count+1!=expected||units.ContainsKey(id)||nextIncarnation!=incarnation||!ReferenceEquals(geometry,sourceGeometry)||!ReferenceEquals(profile,sourceProfile))throw new InvalidOperationException("Actor birth lost source.");
                // Admission callbacks can move neighbors without changing count.
                // Rebuild from current actors and repeat all placement predicates.
                BuildIndex();if(!geometry.IsFree(point,radius)||!UnitFree(null,point,point,radius)||!ValidLocation(location,radius))throw new InvalidOperationException("Actor placement changed during admission.");
                unit.Incarnation=++nextIncarnation;unit.Location=location;units.Add(id,unit);inserted=true;maximumRadius=Math.Max(maximumRadius,radius);orderedUnits.Add(unit);orderedUnits.Sort((a,b)=>a.Id.CompareTo(b.Id));
                var old=spatial;spatial=next;next=null;BuildIndex();old?.Release();return unit;
            }finally{next?.Release();if(!inserted)unit?.StorageOwner?.Release();}
        }
        public bool TryGet(int id,out NavUnit unit)=>units.TryGetValue(id,out unit);
        public void Rebind(NavigationProfile next){profile=next;}
        public void SetMobility(int id,double speed,double turn){if(units.TryGetValue(id,out var unit)){unit.Speed=speed;unit.TurnSpeed=turn;}}
        public void SetSpeed(int id,double speed){if(units.TryGetValue(id,out var unit)&&speed>0)unit.Speed=speed;}
        internal bool HasPreferredMotion=>preferredMaxSpeed.Count!=0;
        internal void ClearPreferredMotion()=>preferredMaxSpeed.Clear();
        internal void PreferMaxSpeed(int id,double maxSpeed)
        {
            if(!units.ContainsKey(id)||double.IsNaN(maxSpeed)||double.IsInfinity(maxSpeed)||maxSpeed<0)
                throw new ArgumentException("Invalid preferred motion speed.");
            preferredMaxSpeed[id]=maxSpeed;
        }
        public bool CanPlace(NavPoint point)=>CanPlace(point,profile.Radius);
        public bool CanPlace(NavPoint point,double radius)=>geometry.IsFree(point,radius)&&Locate(point,radius,out _)&&UnitFree(null,point,point,radius);
        internal NavCrowdSpatialStorage PrepareRemovalSpatial(int id)=>units.ContainsKey(id)?CreateSpatial(units.Count-1):null;
        public bool Remove(int id)
        {
            var next=PrepareRemovalSpatial(id);try{var removed=RemovePrepared(id,next);if(removed)next=null;return removed;}finally{next?.Release();}
        }
        internal bool RemovePrepared(int id,NavCrowdSpatialStorage next)
        {
            if(!units.TryGetValue(id,out var unit))return false;
            if(units.Count==1?next!=null:next==null||next.ActorCount!=units.Count-1)throw new InvalidOperationException("Actor removal lost prepared index.");
            units.Remove(id);orderedUnits.Remove(unit);var old=spatial;spatial=next;BuildIndex();old?.Release();unit.StorageOwner?.Release();return true;
        }
        internal bool ValidateRoute(NavUnit unit,NavPoint position,NavLocation start,NavPoint goal,IReadOnlyList<NavPoint> route,NavLocation? assigned,out NavLocation terminal)
        {
            terminal=start;if(route==null||route.Count==0||!geometry.IsFree(goal,unit.Radius)||Distance(route[route.Count-1],goal)>1e-4)return false;
            var previous=position;for(int i=0;i<=route.Count;i++){var point=i==route.Count?goal:route[i];if(!geometry.IsFree(point,unit.Radius)||!geometry.SegmentFree(previous,point,unit.Radius)||!Traverse(terminal,point,unit.Radius,out terminal))return false;previous=point;}
            return !assigned.HasValue||assigned.Value.Position.Equals(goal)&&CompatibleArrival(terminal,assigned.Value,unit.Radius);
        }
        public bool SetRoute(int id,NavPoint goal,NavPoint[] route,NavLocation? assigned=null)
        {
            if(!units.TryGetValue(id,out var unit)||!ValidateRoute(unit,unit.Position,unit.Location,goal,route,assigned,out var terminal))return false;
            if(unit.StorageOwner!=null){var next=StorageSource.CopyRoute(route,goal);unit.StorageOwner.SetGlobalNoThrow(next,out var old,out var local);old?.Release();local?.Release();}
            else{var exact=new NavPoint[route.Length+1];Array.Copy(route,exact,route.Length);exact[route.Length]=goal;unit.ReplaceRoute(exact);}
            unit.GoalLocation=assigned??terminal;unit.Goal=goal;unit.Held=false;unit.Moving=true;unit.Outcome=NavigationOutcome.Moving;return true;
        }
        // Tactical combat suspension keeps cursor and mobility identity so pending replacement remains valid.
        internal void Suspend(int id){if(units.TryGetValue(id,out var u))u.Moving=false;}
        internal bool Resume(int id){if(!units.TryGetValue(id,out var u)||u.Held||u.routeIndex>=u.route.Length)return false;u.Moving=true;return true;}
        public void Stop(int id, bool hold) { NavUnit unit; if (!units.TryGetValue(id, out unit)) return; unit.MobilityRevision++;unit.ReplaceRoute(new NavPoint[0]); unit.Moving = false; unit.Held = hold;unit.Outcome=hold?NavigationOutcome.Held:NavigationOutcome.Stopped; }
        internal void BindGeometryNoThrow(NavGeometry value){geometry=value;}
        internal NavigationOutcome PrepareGeometryOutcome(NavUnit unit,NavGeometry value)=>unit.Held?NavigationOutcome.Held:unit.Outcome==NavigationOutcome.Arrived&&unit.GoalLocation.HasValue&&value.IsFree(unit.Position,unit.Radius)&&value.IsFree(unit.Goal,unit.Radius)&&Distance(unit.Position,unit.Goal)<=profile.ArrivalTolerance&&CompatibleArrival(unit.Location,unit.GoalLocation.Value,unit.Radius)?NavigationOutcome.Arrived:NavigationOutcome.Stopped;
        public void SetGeometry(NavGeometry geometry) { if (geometry == null) throw new ArgumentNullException("geometry"); this.geometry = geometry; foreach (NavUnit unit in units.Values) { unit.ReplaceRoute(new NavPoint[0]); unit.Moving = false;unit.Outcome=unit.Held?NavigationOutcome.Held:unit.Outcome==NavigationOutcome.Arrived&&unit.GoalLocation.HasValue&&geometry.IsFree(unit.Position,unit.Radius)&&geometry.IsFree(unit.Goal,unit.Radius)&&Distance(unit.Position,unit.Goal)<=profile.ArrivalTolerance&&CompatibleArrival(unit.Location,unit.GoalLocation.Value,unit.Radius)?NavigationOutcome.Arrived:NavigationOutcome.Stopped; } }
        public void Step(double dt)
        {
            ValidateStepDuration(dt);
            if(preparedStep!=null){StepCore(dt,null);return;}
            using(var workspace=StorageSource!=null?StorageSource.CreateMotionWorkspace(this):new NavMotionWorkspace(this,unitView.Count,MotionMembership))Step(dt,workspace);
        }
        private static void ValidateStepDuration(double dt)
        {if(dt<=0d||Double.IsNaN(dt)||Double.IsInfinity(dt))throw new ArgumentException("Invalid step duration.");}
        internal void Step(double dt,NavMotionWorkspace workspace)
        {
            ValidateStepDuration(dt);if(workspace==null||!workspace.SourceCurrent(this))throw new InvalidOperationException("Motion workspace lost its source.");StepCore(dt,workspace);
        }
        private void StepCore(double dt,NavMotionWorkspace workspace)
        {
            NeighborCandidates=0;BuildIndex();NavLocalRepairWorkspace repair=null;
            try{StepJoint(dt,ref repair,workspace);}finally{repair?.Release();}
        }
        public void Reject(int id,NavigationOutcome outcome){ NavUnit unit; if(!units.TryGetValue(id,out unit))return; Stop(id,false);unit.Outcome=outcome;}
        bool INavMotionClearance.ClearMotion(int actor,NavPoint end)
        {var unit=unitView[actor];return geometry.SegmentFree(unit.Position,end,unit.Radius)&&Traverse(unit.Location,end,unit.Radius,out _);}
        private void StepJoint(double dt,ref NavLocalRepairWorkspace repair,NavMotionWorkspace workspace)
        {
            int count=unitView.Count;
            var motion=preparedStep?.Motion??workspace.Motion;var proposals=preparedStep?.Proposals??workspace.Proposals;
            var neighbors=preparedStep?.Neighbors??workspace.Neighbors;var neighborCounts=preparedStep?.NeighborCounts??workspace.NeighborCounts;
            // Freeze the projected After state, including ready/failure/held and
            // march preferences. Before remains the publication read set.
            for(int i=0;i<count;i++){
                var unit=unitView[i];bool active=unit.Moving&&!unit.Held&&unit.routeIndex<unit.route.Length;var target=unit.Position;double speed=unit.Speed;
                if(active){
                    if(unit.localIndex>=unit.localRoute.Length)while(unit.routeIndex+1<unit.route.Length&&geometry.SegmentFree(unit.Position,unit.route[unit.routeIndex+1],unit.Radius)&&TerrainClear(unit,unit.Position,unit.route[unit.routeIndex+1])&&HeldSegmentFree(unit,unit.Position,unit.route[unit.routeIndex+1]))unit.routeIndex++;
                    bool local=unit.localIndex<unit.localRoute.Length;target=local?unit.localRoute[unit.localIndex]:unit.route[unit.routeIndex];
                    if(Distance(target,unit.Position)<=profile.ArrivalTolerance){unit.blockedSeconds=0;unit.noProgressTicks=0;if(local)unit.localIndex++;else if(++unit.routeIndex>=unit.route.Length){unit.Moving=false;unit.Outcome=NavigationOutcome.Arrived;}active=false;}
                    if(TryPreferredSpeed(unit.Id,out var preferred)){speed=Math.Min(speed,preferred);if(speed<=0)active=false;}
                }else if(unit.routeIndex>=unit.route.Length)unit.Moving=false;
                motion[i]=new NavMotionBody{Id=unit.Id,Position=unit.Position,Target=target,Heading=unit.Heading,Radius=unit.Radius,Speed=speed,TurnSpeed=unit.TurnSpeed,Active=active};
            }
            NavCrowdMotion.Neighbors(motion,neighbors,neighborCounts,profile,dt);
            for(int i=0;i<count;i++){NeighborCandidates+=neighborCounts[i]<0?profile.CrowdNeighborCap+1:neighborCounts[i];proposals[i]=NavCrowdMotion.Select(motion,i,neighbors,neighborCounts[i],profile,dt,this);}
            NavCrowdMotion.ResolveJoint(motion,proposals,dt);
            for(int i=0;i<count;i++){
                var unit=unitView[i];var next=proposals[i];if(!motion[i].Active)continue;unit.Heading=next.Heading;
                if(!next.Position.Equals(unit.Position)){
                    if(!Traverse(unit.Location,next.Position,unit.Radius,out var moved))throw new InvalidOperationException("Prepared crowd terrain changed during motion.");
                    unit.Location=moved;unit.Position=next.Position;unit.blockedSeconds=0;unit.noProgressTicks=0;
                }
            }
            BuildIndex();
            for(int i=0;i<count;i++){
                var unit=unitView[i];var next=proposals[i];
                if(motion[i].Active&&next.Position.Equals(motion[i].Position)&&next.Blocked&&!next.Overflow){
                    MarkNoProgress(unit);unit.blockedSeconds+=dt;
                    if(unit.noProgressTicks*dt>=profile.BlockedTimeoutSeconds){RejectMovingUnit(unit,NavigationOutcome.Blocked);continue;}
                    if(unit.blockedSeconds>=profile.RepairRetrySeconds){unit.blockedSeconds=0;CommitDetour(unit,unit.route[unit.routeIndex],ref repair);}
                }
            }
        }
        private bool TryPreferredSpeed(int id,out double speed)
        {if(preparedStep==null)return preferredMaxSpeed.TryGetValue(id,out speed);speed=preparedStep.Preferred[ActorIndex(id)];return !double.IsNaN(speed);}
        private void RejectMovingUnit(NavUnit unit,NavigationOutcome outcome)
        {if(preparedStep==null){Reject(unit.Id,outcome);return;}unit.MobilityRevision++;unit.BindRouteBackingNoThrow(null,null);unit.Moving=unit.Held=false;unit.Outcome=outcome;preparedStep.ClearRoutes(unit);}
        private bool FreeMove(NavUnit unit,NavPoint candidate){return Clear(unit,unit.Position,candidate);}
        private bool Clear(NavUnit unit,NavPoint from,NavPoint to){return geometry.SegmentFree(from,to,unit.Radius)&&TerrainClear(unit,from,to)&&UnitFree(unit,from,to);}
        internal bool Locate(NavPoint point,double radius,out NavLocation location)
        {location=default;if(double.IsNaN(point.X)||double.IsInfinity(point.X)||double.IsNaN(point.Z)||double.IsInfinity(point.Z))return false;if(terrain!=null){if(!terrain.TryLocate(point,radius,out location))return false;location=OwnedLocation(location);return true;}location=new NavLocation(point,NavLocation.FlatSurface);return geometry.IsFree(point,radius);}
        internal bool ValidLocation(NavLocation location,double radius)=>terrain==null?location.SurfaceId==NavLocation.FlatSurface&&Math.Abs(location.Position.X)+radius<=geometry.HalfExtent&&Math.Abs(location.Position.Z)+radius<=geometry.HalfExtent:terrain.IsValidLocation(location,radius);
        internal bool Traverse(NavLocation from,NavPoint to,double radius,out NavLocation location)
        {if(terrain!=null){if(!terrain.TryTraverse(from,to,radius,out location))return false;location=OwnedLocation(location);return true;}location=new NavLocation(to,NavLocation.FlatSurface);return from.SurfaceId==NavLocation.FlatSurface&&geometry.SegmentFree(from.Position,to,radius);}
        private bool ClearFrom(NavUnit unit,NavLocation from,NavPoint to,out NavLocation location)
        {location=default;return geometry.SegmentFree(from.Position,to,unit.Radius)&&UnitFree(unit,from.Position,to)&&Traverse(from,to,unit.Radius,out location);}
        private bool TerrainClear(NavUnit unit,NavPoint from,NavPoint to)
        {return Traverse(unit.Location,from,unit.Radius,out var start)&&Traverse(start,to,unit.Radius,out _);}
        internal bool CompatibleArrival(NavLocation actual,NavLocation assigned,double radius)
        {return ValidLocation(actual,radius)&&ValidLocation(assigned,radius)&&(actual.SurfaceId==assigned.SurfaceId||Traverse(actual,assigned.Position,radius,out var end)&&end.Equals(assigned));}
        private bool CommitDetour(NavUnit unit,NavPoint target,ref NavLocalRepairWorkspace workspace)
        {
            long clock=Stopwatch.GetTimestamp();double cell=profile.LocalGridCell;
            int radius=(int)Math.Ceiling(profile.LocalDetourDistance/cell);
            if(workspace==null)workspace=preparedStep!=null?NavLocalRepairWorkspace.Create(radius,preparedStep.Reservation.TryReserve,ownedSurfaceNames as NavOwnedStorage):StorageSource==null?NavLocalRepairWorkspace.Create(radius,null,ownedSurfaceNames as NavOwnedStorage):StorageSource.CreateRepairWorkspace(radius,ownedSurfaceNames as NavOwnedStorage);
            workspace.Reset();var nodes=workspace.Nodes;var open=workspace.Open;
            int start=workspace.Index(0,0);nodes[start]=new NavLocalRepairWorkspace.Node{Seen=true,Location=unit.Location,Parent=-1};open[workspace.OpenCount++]=start;
            int best=-1;double bestDistance=Distance(unit.Position,target);
            while(workspace.OpenCount>0){
                int selected=0;for(int i=1;i<workspace.OpenCount;i++)if(nodes[open[i]].Score<nodes[open[selected]].Score-1e-9||(Math.Abs(nodes[open[i]].Score-nodes[open[selected]].Score)<=1e-9&&nodes[open[i]].Side<nodes[open[selected]].Side))selected=i;
                int currentIndex=open[selected];Array.Copy(open,selected+1,open,selected,workspace.OpenCount-selected-1);workspace.OpenCount--;
                if(nodes[currentIndex].Closed)continue;nodes[currentIndex].Closed=true;var current=nodes[currentIndex];
                var point=new NavPoint(unit.Position.X+current.X*cell,unit.Position.Z+current.Z*cell);
                double remaining=Distance(point,target);
                if(currentIndex!=start&&remaining<bestDistance){best=currentIndex;bestDistance=remaining;}
                if(currentIndex!=start&&ClearFrom(unit,current.Location,target,out _)){best=currentIndex;break;}
                for(int ox=-1;ox<=1;ox++)for(int oz=-1;oz<=1;oz++){
                    if(ox==0&&oz==0)continue;int x=current.X+ox,z=current.Z+oz;
                    if(x*x+z*z>radius*radius)continue;
                    var nextPoint=new NavPoint(unit.Position.X+x*cell,unit.Position.Z+z*cell);
                    if(!ClearFrom(unit,current.Location,nextPoint,out var nextLocation))continue;
                    int key=workspace.Index(x,z);double cost=current.Cost+Distance(point,nextPoint);
                    if(nodes[key].Seen){if(nodes[key].Closed||cost>=nodes[key].Cost)continue;}
                    else{nodes[key].Seen=true;nodes[key].X=x;nodes[key].Z=z;open[workspace.OpenCount++]=key;}
                    nodes[key].Location=nextLocation;nodes[key].Cost=cost;nodes[key].Score=cost+Distance(nextPoint,target);nodes[key].Side=(target.X-unit.Position.X)*z-(target.Z-unit.Position.Z)*x;nodes[key].Parent=currentIndex;
                }
            }
            if(best<0){RepairFailed++;RepairCpuMs+=(Stopwatch.GetTimestamp()-clock)*1000d/Stopwatch.Frequency;return false;}
            int count=0;for(int n=best;n!=start;n=nodes[n].Parent)workspace.Reversed[count++]=new NavPoint(unit.Position.X+nodes[n].X*cell,unit.Position.Z+nodes[n].Z*cell);
            Array.Reverse(workspace.Reversed,0,count);
            // Smooth only verified local edges. The original global route stays intact.
            var anchorLocation=unit.Location;int index=0;
            while(index<count){int far=index;for(int j=index+1;j<count;j++)if(ClearFrom(unit,anchorLocation,workspace.Reversed[j],out _))far=j;if(!ClearFrom(unit,anchorLocation,workspace.Reversed[far],out anchorLocation))return false;workspace.Smoothed[workspace.RouteCount++]=workspace.Reversed[far];index=far+1;}
            if(preparedStep!=null)preparedStep.ReplaceLocal(unit,workspace);
            else if(unit.StorageOwner!=null){var next=StorageSource.CopyRoute(workspace,null);var retired=unit.StorageOwner.Local;unit.StorageOwner.Local=next;unit.localRoute=next.ReadOnlyBacking;retired?.Release();}
            else{unit.localRoute=new NavPoint[workspace.RouteCount];Array.Copy(workspace.Smoothed,unit.localRoute,workspace.RouteCount);}
            unit.localIndex=0;RepairCount++;RepairCpuMs+=(Stopwatch.GetTimestamp()-clock)*1000d/Stopwatch.Frequency;return true;
        }
        private static double Distance(NavPoint a,NavPoint b){double x=a.X-b.X,z=a.Z-b.Z;return Math.Sqrt(x*x+z*z);}
        private void MarkNoProgress(NavUnit unit) { unit.noProgressTicks++; if (unit.noProgressTicks > MaxNoProgressTicks) MaxNoProgressTicks = unit.noProgressTicks; }
        private bool HeldSegmentFree(NavUnit self,NavPoint from,NavPoint to)
        {
            for(int i=0;i<unitView.Count;i++){var other=unitView[i];if(other!=self&&other.Held&&other.Team==self.Team&&SegmentPointDistanceSquared(other.Position,from,to)<(self.Radius+other.Radius)*(self.Radius+other.Radius))return false;}
            return true;
        }
        private bool UnitFree(NavUnit self, NavPoint from, NavPoint to,double radius=0)
        {
            radius=self?.Radius??radius;double maxRadius=maximumRadius;
            int reach = (int)Math.Ceiling((radius+maxRadius + Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Z - from.Z) * (to.Z - from.Z)) + profile.GridCell) / profile.GridCell); int cx = Cell(to.X), cz = Cell(to.Z);
            for (int x=cx-reach;x<=cx+reach;x++)for(int z=cz-reach;z<=cz+reach;z++){
                if(spatial==null)continue;
                for(int i=spatial.First(Key(x,z));i>=0;i=spatial.Next(i)){
                    var other=unitView[i];if(other==self)continue;NeighborCandidates++;
                    double distance=SegmentPointDistanceSquared(other.Position,from,to);if(distance<(radius+other.Radius)*(radius+other.Radius)-.000000001d)return false;
                }
            }
            return true;
        }
        private NavCrowdSpatialStorage CreateSpatial(int count)=>count==0?null:StorageSource==null?NavCrowdSpatialStorage.Create(count,null):StorageSource.CreateSpatialStorage(count);
        private void BuildIndex()
        {
            if(unitView.Count==0){var old=spatial;spatial=null;old?.Release();return;}
            if(spatial==null||spatial.ActorCount!=unitView.Count){
                var next=CreateSpatial(unitView.Count);
                var old=spatial;spatial=next;old?.Release();
            }
            spatial.Reset();if(preparedStep==null){foreach(var unit in units.Values)AddToIndex(unit);}
            else foreach(int row in preparedStep.InitialRows)AddToIndex(unitView[row]);
        }
        private int ActorIndex(int id){int lo=0,hi=unitView.Count-1;while(lo<=hi){int mid=(lo+hi)/2,at=unitView[mid].Id;if(at==id)return mid;if(at<id)lo=mid+1;else hi=mid-1;}for(int i=0;i<unitView.Count;i++)if(unitView[i].Id==id)return i;throw new InvalidOperationException("Spatial actor is absent.");}
        private void AddToIndex(NavUnit unit)=>spatial.Add(ActorIndex(unit.Id),Key(Cell(unit.Position.X),Cell(unit.Position.Z)));
        private void Remove(NavUnit unit)=>spatial.Remove(ActorIndex(unit.Id));
        private int Cell(double value) { return (int)Math.Floor((value + geometry.HalfExtent) / profile.GridCell); } private static long Key(int x, int z) { return ((long)x << 32) ^ (uint)z; }
        private static NavPoint Forward(NavPoint point, double heading, double distance) { return new NavPoint(point.X + Math.Cos(heading) * distance, point.Z + Math.Sin(heading) * distance); }
        private static double Turn(double current, double desired, double maximum) { double difference = Normalize(desired - current); return current + Math.Max(-maximum, Math.Min(maximum, difference)); }
        private static double Normalize(double value) { while (value > Math.PI) value -= Math.PI * 2d; while (value < -Math.PI) value += Math.PI * 2d; return value; }
        private static double SegmentPointDistanceSquared(NavPoint point, NavPoint a, NavPoint b) { double dx = b.X - a.X, dz = b.Z - a.Z, length = dx * dx + dz * dz; if (length == 0d) { dx = point.X - a.X; dz = point.Z - a.Z; return dx * dx + dz * dz; } double t = Math.Max(0d, Math.Min(1d, ((point.X - a.X) * dx + (point.Z - a.Z) * dz) / length)); dx = point.X - (a.X + dx * t); dz = point.Z - (a.Z + dz * t); return dx * dx + dz * dz; }
    }
}
