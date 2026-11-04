using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Spacewars.Simulation
{
    public enum NavigationOutcome { Idle, Moving, Arrived, Stopped, Held, Unreachable, Blocked, Rejected, Waiting }
    public sealed class NavUnit : INavStorageFacade
    {
        internal NavLiveUnitStorage StorageOwner{get;private set;}
        NavOwnedStorage INavStorageFacade.StorageRoot=>StorageOwner;
        internal void BindStorage(NavLiveUnitStorage owner){if(owner==null||StorageOwner!=null)throw new ArgumentException("Actor already owned.");StorageOwner=owner;}
        internal NavUnit(int id, NavPoint position,double radius,double speed,double turn,bool canYield=false) { Id = id; Position = position;Radius=radius;Speed=speed;TurnSpeed=turn;CanYield=canYield; }
        public NavLocation Location{get;internal set;} public NavLocation? GoalLocation{get;internal set;}
        public double Radius{get;} public double Speed{get; internal set;} public double TurnSpeed{get; internal set;}
        public NavigationOutcome Outcome {get;internal set;}
        public long FailedGroupId{get;internal set;} public long FailedRevision{get;internal set;} public long FailedRequest{get;internal set;} public long FailedOrder{get;internal set;} public NavLocation? FailedGoal{get;internal set;} public NavigationOutcome LastFailure{get;internal set;}
        public long InstalledGroupId{get;internal set;} public long InstalledRevision{get;internal set;} public long InstalledRequest{get;internal set;} public long InstalledOrder{get;internal set;}
        public long Incarnation {get;internal set;} public long MobilityRevision {get;internal set;} public int Team {get;set;} public int Id { get; private set; } public NavPoint Position { get; internal set; } public double Heading { get; internal set; } public bool Moving { get; internal set; } public bool Held { get; internal set; } public NavPoint Goal { get; internal set; } public IReadOnlyList<NavPoint> Route { get { return routeView; } } public bool CanYield {get;}
        public double ReverseDistance{get;internal set;} public double ReverseSpeedRatio{get;internal set;} public double ReverseConeRadians{get;internal set;}
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
            target.FailedGroupId=FailedGroupId;target.FailedRevision=FailedRevision;target.FailedRequest=FailedRequest;target.FailedOrder=FailedOrder;target.FailedGoal=FailedGoal;target.LastFailure=LastFailure;target.InstalledGroupId=InstalledGroupId;target.InstalledRevision=InstalledRevision;target.InstalledRequest=InstalledRequest;target.InstalledOrder=InstalledOrder;target.Incarnation=Incarnation;target.MobilityRevision=MobilityRevision;target.Team=Team;target.CanYield=CanYield;target.ReverseDistance=ReverseDistance;target.ReverseSpeedRatio=ReverseSpeedRatio;target.ReverseConeRadians=ReverseConeRadians;target.Id=Id;target.Position=Position;target.Goal=Goal;target.Location=Location;target.GoalLocation=GoalLocation;target.Radius=Radius;target.Speed=Speed;target.TurnSpeed=TurnSpeed;target.Heading=Heading;target.Moving=Moving;target.Held=Held;target.Outcome=Outcome;target.RouteIndex=routeIndex;target.LocalIndex=localIndex;target.NoProgressTicks=noProgressTicks;target.BlockedSeconds=blockedSeconds;
        }
        internal bool SameCaptureHeader(NavUnitSaveState source)
            =>source!=null&&source.Id==Id&&source.CanYield==CanYield&&SameReverse(source.ReverseDistance,source.ReverseSpeedRatio,source.ReverseConeRadians)&&SameBits(source.Radius,Radius)&&SameLocation(source.Location,Location)&&SameLocation(source.GoalLocation,GoalLocation)&&SameBits(source.Speed,Speed)&&SameBits(source.TurnSpeed,TurnSpeed)&&source.Outcome==Outcome&&source.FailedGroupId==FailedGroupId&&source.FailedRevision==FailedRevision&&source.FailedRequest==FailedRequest&&source.FailedOrder==FailedOrder&&SameLocation(source.FailedGoal,FailedGoal)&&source.LastFailure==LastFailure&&source.InstalledGroupId==InstalledGroupId&&source.InstalledRevision==InstalledRevision&&source.InstalledRequest==InstalledRequest&&source.InstalledOrder==InstalledOrder&&source.Incarnation==Incarnation&&source.MobilityRevision==MobilityRevision&&source.Team==Team&&SameBits(source.Position.X,Position.X)&&SameBits(source.Position.Z,Position.Z)&&SameBits(source.Goal.X,Goal.X)&&SameBits(source.Goal.Z,Goal.Z)&&SameBits(source.Heading,Heading)&&source.Moving==Moving&&source.Held==Held&&source.RouteIndex==routeIndex&&source.LocalIndex==localIndex&&source.NoProgressTicks==noProgressTicks&&SameBits(source.BlockedSeconds,blockedSeconds);
        internal NavUnit CopyMovementHeader(){var copy=new NavUnit(Id,Position,Radius,Speed,TurnSpeed,CanYield);copy.CopyMovementValuesFrom(this);return copy;}
        internal bool SameReverse(double distance,double ratio,double cone)=>SameBits(ReverseDistance,distance)&&SameBits(ReverseSpeedRatio,ratio)&&SameBits(ReverseConeRadians,cone);
        internal void CopyMovementValuesFrom(NavUnit source){ReverseDistance=source.ReverseDistance;ReverseSpeedRatio=source.ReverseSpeedRatio;ReverseConeRadians=source.ReverseConeRadians;Location=source.Location;GoalLocation=source.GoalLocation;Speed=source.Speed;TurnSpeed=source.TurnSpeed;Outcome=source.Outcome;FailedGroupId=source.FailedGroupId;FailedRevision=source.FailedRevision;FailedRequest=source.FailedRequest;FailedOrder=source.FailedOrder;FailedGoal=source.FailedGoal;LastFailure=source.LastFailure;InstalledGroupId=source.InstalledGroupId;InstalledRevision=source.InstalledRevision;InstalledRequest=source.InstalledRequest;InstalledOrder=source.InstalledOrder;Incarnation=source.Incarnation;MobilityRevision=source.MobilityRevision;Team=source.Team;Position=source.Position;Heading=source.Heading;Moving=source.Moving;Held=source.Held;Goal=source.Goal;route=source.route;routeView=source.routeView;routeIndex=source.routeIndex;noProgressTicks=source.noProgressTicks;localRoute=source.localRoute;localIndex=source.localIndex;blockedSeconds=source.blockedSeconds;}
        internal bool SameMovementValues(NavUnit source)=>Id==source.Id&&CanYield==source.CanYield&&SameReverse(source.ReverseDistance,source.ReverseSpeedRatio,source.ReverseConeRadians)&&SameBits(Radius,source.Radius)&&SameLocation(Location,source.Location)&&SameLocation(GoalLocation,source.GoalLocation)&&SameBits(Speed,source.Speed)&&SameBits(TurnSpeed,source.TurnSpeed)&&Outcome==source.Outcome&&FailedGroupId==source.FailedGroupId&&FailedRevision==source.FailedRevision&&FailedRequest==source.FailedRequest&&FailedOrder==source.FailedOrder&&SameLocation(FailedGoal,source.FailedGoal)&&LastFailure==source.LastFailure&&InstalledGroupId==source.InstalledGroupId&&InstalledRevision==source.InstalledRevision&&InstalledRequest==source.InstalledRequest&&InstalledOrder==source.InstalledOrder&&Incarnation==source.Incarnation&&MobilityRevision==source.MobilityRevision&&Team==source.Team&&SameBits(Position.X,source.Position.X)&&SameBits(Position.Z,source.Position.Z)&&SameBits(Heading,source.Heading)&&Moving==source.Moving&&Held==source.Held&&SameBits(Goal.X,source.Goal.X)&&SameBits(Goal.Z,source.Goal.Z)&&ReferenceEquals(route,source.route)&&ReferenceEquals(routeView,source.routeView)&&routeIndex==source.routeIndex&&noProgressTicks==source.noProgressTicks&&ReferenceEquals(localRoute,source.localRoute)&&localIndex==source.localIndex&&SameBits(blockedSeconds,source.blockedSeconds);
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
        public long Incarnation,MobilityRevision; public int Team, Id, RouteIndex, LocalIndex, NoProgressTicks; public bool CanYield; public double ReverseDistance,ReverseSpeedRatio,ReverseConeRadians;
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
    public sealed partial class NavCrowd : INavMotionClearance,INavRecastSweepAuthority,INavLayerSweepAuthority
    {
        internal INavCrowdStorageSource StorageSource;
        internal NavCrowdSpatialStorage SpatialStorage=>spatial;
        internal void ReleaseCrowdStorage(){foreach(var unit in orderedUnits)unit.StorageOwner?.Release();spatial?.Release();spatial=null;}
        private NavUnit CreateUnit(int id,NavPoint point,double radius,double speed,double turn,bool canYield=false)=>StorageSource==null?new NavUnit(id,point,radius,speed,turn,canYield):StorageSource.CreateUnit(id,point,radius,speed,turn,canYield);
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
        private NavMotionBody[] sampledMotion;
        private long nextIncarnation; private double maximumRadius;
        private NavGeometry geometry; private NavigationProfile profile; private readonly Dictionary<int, NavUnit> units; private readonly List<NavUnit> orderedUnits; private readonly IReadOnlyList<NavUnit> unitView; private NavCrowdSpatialStorage spatial;
        // Transient authority preference. It is rebuilt before every step and is
        // deliberately outside the saved physical actor state.
        private readonly Dictionary<int,double> preferredMaxSpeed;
        private NavPreparedCrowdStep preparedStep;
        public NavCrowd(NavGeometry geometry, NavigationProfile profile,IPlayableTerrain terrain=null) { if (geometry == null || profile == null) throw new ArgumentNullException(); this.terrain=terrain;this.geometry = geometry; this.profile = profile;units=new Dictionary<int,NavUnit>();orderedUnits=new List<NavUnit>();preferredMaxSpeed=new Dictionary<int,double>();unitView=orderedUnits.AsReadOnly(); }
        internal int PhysicalLayer(NavLocation location)
        {
            if(!(terrain is ILayeredPlayableTerrain layered))return -1;
            for(int i=0;i<layered.LayeredNavigation.Surfaces.Count;i++)if(layered.LayeredNavigation.Surfaces[i].Id==location.SurfaceId)return i;
            return -1;
        }
        private bool PhysicalSweep(NavLocation from,NavPoint to,double radius)
        {
            int layer=PhysicalLayer(from);
            // A sweep crossing a portal uses the conservative all-layer check.
            // Single-layer certificates never authorize skipping a destination blocker.
            if(layer>=0&&(!terrain.TryTraverse(from,to,radius,out var reached)||reached.SurfaceId!=from.SurfaceId))layer=-1;
            return geometry.SegmentFree(from.Position,to,radius,layer);
        }
        internal NavGeometry Geometry=>geometry;
        internal NavigationProfile Profile=>profile;
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
                !ValidReverse(u.ReverseDistance,u.ReverseSpeedRatio,u.ReverseConeRadians)||u.NoProgressTicks<0||u.BlockedSeconds<0||!geometry.IsFree(u.Position,u.Radius,PhysicalLayer(u.Location??new NavLocation(u.Position,NavLocation.FlatSurface)))||
                u.Radius<=0||u.Speed<=0||u.TurnSpeed<=0||double.IsNaN(u.Heading+u.Speed+u.TurnSpeed+u.BlockedSeconds))
                throw new ArgumentException("Invalid crowd unit save.",nameof(state));
            if(state.MaximumRadius<0||double.IsNaN(state.MaximumRadius)||double.IsInfinity(state.MaximumRadius)||System.Linq.Enumerable.Any(state.Units,u=>u.Radius>state.MaximumRadius)||state.NextIncarnation<0||state.RepairCount<0||state.RepairFailed<0||state.MaxNoProgressTicks<0||System.Linq.Enumerable.Any(state.Units,u=>u.Incarnation<1||u.Incarnation>state.NextIncarnation||u.MobilityRevision<0||!Enum.IsDefined(typeof(NavigationOutcome),u.Outcome)||u.Held&&u.Moving))throw new ArgumentException("Invalid crowd allocator/counters.");
            int actorIndex=0;foreach(var u in state.Units){
                var unit=CreateUnit(u.Id,u.Position,u.Radius,u.Speed,u.TurnSpeed,u.CanYield);unit.ReverseDistance=u.ReverseDistance;unit.ReverseSpeedRatio=u.ReverseSpeedRatio;unit.ReverseConeRadians=u.ReverseConeRadians;units.Add(u.Id,unit);orderedUnits.Add(unit);maximumRadius=Math.Max(maximumRadius,u.Radius);if(u.Location.HasValue){if(!ValidLocation(u.Location.Value,u.Radius)||!u.Location.Value.Position.Equals(u.Position))throw new ArgumentException("Invalid saved actor surface.");unit.Location=u.Location.Value;}
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
        private static bool ValidReverse(double distance,double ratio,double cone)=>double.IsFinite(distance)&&double.IsFinite(ratio)&&double.IsFinite(cone)&&distance>=0&&distance<=4&&(distance==0||ratio>=.1&&ratio<=1&&cone>=.05&&cone<=1);
        public NavUnit Add(int id,NavPoint point)=>Add(id,point,profile.Radius,profile.TankSpeed,profile.TankTurnSpeed);
        public NavUnit Add(int id,NavPoint point,double radius,double speed,double turn,NavLocation? assignedLocation=null,bool canYield=false)
        {
            if(StorageSource!=null)return StorageSource.Add(this,id,point,radius,speed,turn,assignedLocation,canYield);
            if(units.ContainsKey(id)||radius<=0||speed<=0||turn<=0||double.IsNaN(radius+speed+turn)||double.IsInfinity(radius+speed+turn)||!geometry.IsFree(point,radius)||!UnitFree(null,point,point,radius))throw new ArgumentException("Invalid unit placement.");
            NavLocation location;if(assignedLocation.HasValue){location=assignedLocation.Value;if(!location.Position.Equals(point)||!ValidLocation(location,radius))throw new ArgumentException("Invalid assigned unit surface.");}else if(!Locate(point,radius,out location))throw new ArgumentException("Unsupported unit surface.");
            location=OwnedLocation(location);int expected=units.Count+1;long incarnation=nextIncarnation;var sourceGeometry=geometry;var sourceProfile=profile;var next=CreateSpatial(expected);NavUnit unit=null;bool inserted=false;
            try{
                if(units.Count+1!=expected||units.ContainsKey(id))throw new InvalidOperationException("Actor placement lost source.");
                unit=CreateUnit(id,point,radius,speed,turn,canYield);
                if(units.Count+1!=expected||units.ContainsKey(id)||nextIncarnation!=incarnation||!ReferenceEquals(geometry,sourceGeometry)||!ReferenceEquals(profile,sourceProfile))throw new InvalidOperationException("Actor birth lost source.");
                // Admission callbacks can move neighbors without changing count.
                // Rebuild from current actors and repeat all placement predicates.
                BuildIndex();if(!geometry.IsFree(point,radius)||!UnitFree(null,point,point,radius)||!ValidLocation(location,radius))throw new InvalidOperationException("Actor placement changed during admission.");
                unit.Incarnation=++nextIncarnation;unit.Location=location;units.Add(id,unit);inserted=true;maximumRadius=Math.Max(maximumRadius,radius);orderedUnits.Add(unit);orderedUnits.Sort((a,b)=>a.Id.CompareTo(b.Id));
                var old=spatial;spatial=next;next=null;BuildIndex();old?.Release();return unit;
            }finally{next?.Release();if(!inserted)unit?.StorageOwner?.Release();}
        }
        // Birth preflight deliberately does not touch the live spatial index or
        // NeighborCandidates. It uses the same swept overlap predicate as UnitFree.
        internal NavLocation PrepareBirthLocation(int id,NavPoint point,double radius,double speed,double turn,NavLocation? assigned)
        {
            if(units.ContainsKey(id)||radius<=0||speed<=0||turn<=0||double.IsNaN(radius+speed+turn)||double.IsInfinity(radius+speed+turn)||!geometry.IsFree(point,radius))throw new ArgumentException("Invalid unit placement.");
            foreach(var other in orderedUnits)if(SegmentPointDistanceSquared(other.Position,point,point)<(radius+other.Radius)*(radius+other.Radius)-1e-9)throw new ArgumentException("Invalid unit placement.");
            NavLocation location;if(assigned.HasValue){location=assigned.Value;if(!location.Position.Equals(point)||!ValidLocation(location,radius))throw new ArgumentException("Invalid assigned unit surface.");}else if(!Locate(point,radius,out location))throw new ArgumentException("Unsupported unit surface.");
            return OwnedLocation(location);
        }
        internal void PrepareBirthIndex(NavUnit actor,NavCrowdSpatialStorage next)
        {
            if(next==null||next.ActorCount!=orderedUnits.Count+1||units.ContainsKey(actor.Id)||nextIncarnation==long.MaxValue)throw new InvalidOperationException("Actor birth lost prepared index.");
            next.Reset();_=units.Values;
            // All collection growth is completed before the first actor mutation.
            units.EnsureCapacity(checked(units.Count+1));if(orderedUnits.Capacity<orderedUnits.Count+1)orderedUnits.Capacity=Math.Max(checked(orderedUnits.Count+1),orderedUnits.Capacity==0?4:checked(orderedUnits.Capacity*2));
        }
        internal NavCrowdSpatialStorage AddPreparedNoThrow(NavUnit actor,NavCrowdSpatialStorage next)
        {
            actor.Incarnation=++nextIncarnation;units.Add(actor.Id,actor);orderedUnits.Add(actor);
            // Add historically sorts even a hydrated, non-sorted actor list.
            for(int i=1;i<orderedUnits.Count;i++){var value=orderedUnits[i];int j=i;while(j>0&&orderedUnits[j-1].Id>value.Id){orderedUnits[j]=orderedUnits[j-1];j--;}orderedUnits[j]=value;}
            maximumRadius=Math.Max(maximumRadius,actor.Radius);
            // Mechanical fill follows the exact accepted Dictionary slot order,
            // including reused removal slots. All rows/capacity were proven above.
            foreach(var unit in units.Values)next.Add(ActorIndex(unit.Id),Key(Cell(unit.Position.X),Cell(unit.Position.Z)));
            var old=spatial;spatial=next;return old;
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
            if(StorageSource!=null)return StorageSource.Remove(id);
            var next=PrepareRemovalSpatial(id);try{var removed=RemovePrepared(id,next);if(removed)next=null;return removed;}finally{next?.Release();}
        }
        internal bool RemovePrepared(int id,NavCrowdSpatialStorage next)
        {
            if(!units.TryGetValue(id,out var unit))return false;
            if(units.Count==1?next!=null:next==null||next.ActorCount!=units.Count-1)throw new InvalidOperationException("Actor removal lost prepared index.");
            units.Remove(id);orderedUnits.Remove(unit);var old=spatial;spatial=next;BuildIndex();old?.Release();unit.StorageOwner?.Release();return true;
        }
        internal bool ValidatePhysicalTypedRoute(NavUnit unit,NavLocation start,NavPoint goal,NavTypedRoute route,NavLocation? assigned){
            if(!(terrain is StackedRampQualificationMap physical)||route==null||!route.Subscription.Origin.Equals(start)||!unit.Position.Equals(start.Position)||!assigned.HasValue||!route.Subscription.AssignedEndpoint.Equals(assigned.Value)||!assigned.Value.Position.Equals(goal))return false;
            var at=start;foreach(var leg in route.Legs){
                if(!leg.From.Equals(at)||!physical.IsValidLocation(leg.From,unit.Radius)||!physical.IsValidLocation(leg.To,unit.Radius)||!PhysicalSweep(leg.From,leg.To.Position,unit.Radius))return false;
                if(leg.Kind==NavTypedLegKind.Surface){if(!physical.TryTraverse(leg.From,leg.To,unit.Radius,out var end)||!end.Equals(leg.To))return false;}
                else if(!leg.From.Position.Equals(leg.To.Position)||Math.Abs(physical.Height(leg.From)-physical.Height(leg.To))>1e-9)return false;
                at=leg.To;
            }
            return at.Equals(assigned.Value);
        }
        internal bool ValidateRoute(NavUnit unit,NavPoint position,NavLocation start,NavPoint goal,IReadOnlyList<NavPoint> route,NavLocation? assigned,out NavLocation terminal)
        {
            terminal=start;if(route==null||route.Count==0||!geometry.IsFree(goal,unit.Radius,PhysicalLayer(unit.GoalLocation??unit.Location))||Distance(route[route.Count-1],goal)>1e-4)return false;
            var previous=position;for(int i=0;i<=route.Count;i++){var point=i==route.Count?goal:route[i];if(!geometry.IsFree(point,unit.Radius,PhysicalLayer(terminal))||!geometry.SegmentFree(previous,point,unit.Radius,PhysicalLayer(terminal))||!Traverse(terminal,point,unit.Radius,out terminal,unit.Id))return false;previous=point;}
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
            using var profileExecution=PerformanceProfile.Measure(ProfileStage.CrowdExecution);
            if(NavCrowdMotion.CausalActor!=0)NavCrowdMotion.Causal=new NavCrowdMotion.CausalFrame{ComputeSerial=++NavCrowdMotion.CausalSerial};
            NeighborCandidates=0;BuildIndex();NavLocalRepairWorkspace repair=null;
            try{sampledMotion=preparedStep?.Motion??workspace.Motion;StepJoint(dt,ref repair,workspace);}finally{sampledMotion=null;repair?.Release();}
        }
        public void Reject(int id,NavigationOutcome outcome){ NavUnit unit; if(!units.TryGetValue(id,out unit))return; Stop(id,false);unit.Outcome=outcome;}
        // Closed production types only: these providers have immutable query
        // inputs and no paid/persisted effects. Do not infer purity from the
        // IPlayableTerrain interface, binding text, or a generic callback.
        internal bool StableSelectClearance {
            get {
#if UNITY_INCLUDE_TESTS
                if(TerrainLocations.IndexedCutCandidateForTests!=null)return false;
#endif
                // ThreeCrossingsMap is sealed; its traversal reads immutable geometry,
                // supports and seams. Its bounded footprint memo is derived only.
                return terrain==null||terrain is AiTestMap||terrain is ThreeCrossingsMap||terrain is ProvingGroundMap;
            }
        }
        internal int SelectClearanceMemoCapacity=>terrain is ThreeCrossingsMap?128:16;
        // Concurrent traversals have independent cut/incident scratch. Reuse the
        // conservative full trace bound instead of assuming serial peak reuse.
        internal long ParallelSelectScratchBound=>checked(4096+(terrain is INavArrivalPointTerrain provider?provider.ArrivalLocations.TraceScratchBound:0));
        internal bool AllowsCurrentMotion(int actor,NavPoint end)
        {
            if(preparedStep==null)return true;
            bool receiver=sampledMotion!=null&&sampledMotion[actor].Yielding&&sampledMotion[actor].YieldLabel>0;
            return sampledMotion!=null&&sampledMotion[actor].YieldLabel<0||RecastFormationMotion.Allows(preparedStep.Formation[actor],preparedStep.RecastRoutes[actor],unitView[actor].Position,end,receiver);
        }
        internal bool AllowsCurrentMotionWithoutRowCap(int actor,NavPoint end,NavPoint future)
        {
            if(preparedStep==null)return false;
            var hint=preparedStep.Formation[actor];if((hint.Flags&NavFormationFrameHint.Bound)==0)return false;
            hint.Flags&=unchecked((byte)~NavFormationFrameHint.Bound);var route=preparedStep.RecastRoutes[actor];var from=unitView[actor].Position;
            return RecastFormationMotion.Allows(hint,route,from,end)&&RecastFormationMotion.Allows(hint,route,from,future);
        }
        bool INavMotionClearance.ClearMotion(int actor,NavPoint end)
        {
            var unit=unitView[actor];
            bool yielding=sampledMotion!=null&&sampledMotion[actor].Yielding;
            // Reject the pure geometric domain before paying for terrain traces.
            if(preparedStep!=null&&(sampledMotion==null||sampledMotion[actor].YieldLabel>=0)&&!(yielding&&sampledMotion[actor].YieldLabel>0)&&!RecastFormationMotion.DirectionalAllows(preparedStep.Formation[actor],preparedStep.RecastRoutes[actor],unit.Position,end))return false;
            if(!PhysicalSweep(unit.Location,end,unit.Radius)||!Traverse(unit.Location,end,unit.Radius,out var moved,unit.Id))return false;
            // Flat Native rows also publish typed physical routes. Their next
            // corner must remain reachable after avoidance, just like terrain rows.
            if(terrain==null&&preparedStep?.RecastRoutes[actor]==null||!(yielding?sampledMotion[actor].OwnMoving:unit.Moving)||unit.Held)return true;
            // The published cursor must remain reachable after an aligned turn or avoidance step.
            // Yielding restores its own cursor rather than the shortcut selected for this frame.
            int local=yielding?sampledMotion[actor].OwnLocalIndex:unit.localIndex;
            int global=yielding?sampledMotion[actor].OwnRouteIndex:unit.routeIndex;
            if(local<unit.localRoute.Length)return Traverse(moved,unit.localRoute[local],unit.Radius,out _,unit.Id);
            if(!yielding&&preparedStep!=null&&(preparedStep.Formation[actor].Flags&NavFormationFrameHint.Guide)!=0){
                // Select may execute on a helper. Its immutable clearance query
                // cannot consume the authority thread's preparation scope.
                // The owner verifies the selected displacement and typed receipt
                // in FilterRecastSweeps after all helper rows have joined.
                var target=preparedStep.Formation[actor].Target;
                // A temporary lane target cannot discard the accepted route link.
                // This query remains pure on parallel Select helpers.
                if(!geometry.SegmentFree(end,target,unit.Radius,PhysicalLayer(moved))||!HeldSegmentFree(unit,end,target)||!Traverse(moved,target,unit.Radius,out _,unit.Id))return false;
                var hint=preparedStep.Formation[actor];
                // A recovering bank connector can precede the first currently
                // reachable motor target. The owner certifies its complete
                // remaining typed/HOLD prefix+suffix after the helper sweep.
                if((hint.Flags&(NavFormationFrameHint.Directional|NavFormationFrameHint.Bound))==NavFormationFrameHint.Directional)return true;
                return global<unit.route.Length&&geometry.SegmentFree(end,unit.route[global],unit.Radius,PhysicalLayer(moved))&&HeldSegmentFree(unit,end,unit.route[global])&&Traverse(moved,unit.route[global],unit.Radius,out _,unit.Id);
            }
            return global>=unit.route.Length||Traverse(moved,unit.route[global],unit.Radius,out _,unit.Id);
        }
        internal bool TryRetireInvalidRecastLocal(int row)
        {
            if(preparedStep==null||row<0||row>=unitView.Count)return false;
            var unit=unitView[row];var route=preparedStep.RecastRoutes[row];var hint=preparedStep.Formation[row];
            if(!unit.Moving||unit.Held||route==null||unit.routeIndex>=unit.route.Length||unit.localIndex>=unit.localRoute.Length||!unit.GoalLocation.HasValue||
                (hint.Flags&(NavFormationFrameHint.Guide|NavFormationFrameHint.Directional))!=(NavFormationFrameHint.Guide|NavFormationFrameHint.Directional)||(hint.Flags&(NavFormationFrameHint.Wait|NavFormationFrameHint.PolicyWait))!=0||
                RecastFormationMotion.DirectionalAllows(hint,route,unit.Position,unit.localRoute[unit.localIndex])||!RecastFormationMotion.DirectionalAllows(hint,route,unit.Position,hint.Target))return false;
            var binding=route.Subscription;
            if(binding.Provenance!=NavRouteProvenance.CommandMember||binding.EntityId!=unit.Id||binding.Incarnation!=unit.Incarnation||binding.MobilityRevision!=unit.MobilityRevision||binding.RequestSequence!=unit.InstalledRequest||binding.GroupId!=unit.InstalledGroupId||binding.ActivationRevision!=unit.InstalledRevision||binding.MemberOrderRevision!=unit.InstalledRevision||!binding.AssignedEndpoint.Equals(unit.GoalLocation.Value))return false;
            var state=preparedStep.RecastStates[row];var r=preparedStep.Reservation;
            // Actual avoidance can pass the lateral coordinate of an unfinished
            // corner in a monotonic bank recovery. That old straight edge is no
            // longer executable; a role cap or temporary peer wait is different.
            if(!NavRecastIntervals.Advance(route,state,unit.Location,hint.Target,r,out var receipt,this,unit.Id)||
                !ContinuationTraverse(route,state,unit.Location,hint.Target,unit.Radius,r,out var reached)||!reached.Position.Equals(hint.Target)||reached.SurfaceId!=receipt.ToSurface||
                !NavRecastIntervals.TryContinuationRejoin(route,receipt.After,reached,Array.Empty<NavPoint>(),0,unit.route,unit.routeIndex,unit.GoalLocation.Value,r,out int same,this,unit.Id,true)||same!=unit.routeIndex)return false;
            preparedStep.RetireLocal(unit);return true;
        }
        private bool CanRetireRecastLocal(int row,int nextLocal)
        {
            var route=preparedStep?.RecastRoutes[row];if(route==null)return true;
            var unit=unitView[row];
            // The repair was certified from its exact corner. Arrival tolerance
            // alone cannot transfer that certificate to an earlier physical
            // point: it may still be on the wrong side of a held circle/seam.
            // Retire only if the remaining prefix and unchanged full endpoint
            // suffix qualify from this ACTUAL pose, otherwise finish the corner.
            return unit.GoalLocation.HasValue&&NavRecastIntervals.TryContinuationRejoin(route,preparedStep.RecastStates[row],unit.Location,unit.localRoute,nextLocal,unit.route,unit.routeIndex,unit.GoalLocation.Value,preparedStep.Reservation,out int same,this,unit.Id,true)&&same==unit.routeIndex;
        }
        private void StepJoint(double dt,ref NavLocalRepairWorkspace repair,NavMotionWorkspace workspace)
        {
            int count=unitView.Count;
            var motion=preparedStep?.Motion??workspace.Motion;var proposals=preparedStep?.Proposals??workspace.Proposals;
            var neighbors=preparedStep?.Neighbors??workspace.Neighbors;var neighborCounts=preparedStep?.NeighborCounts??workspace.NeighborCounts;
            var componentLabels=preparedStep?.ComponentLabels??workspace.ComponentLabels;var originalSpeeds=preparedStep?.OriginalSpeeds??workspace.OriginalSpeeds;
            var yieldLabels=preparedStep?.YieldLabels??workspace.YieldLabels;var yieldQueue=preparedStep?.YieldQueue??workspace.YieldQueue;
            // Freeze the projected After state, including ready/failure/held and
            // march preferences. Before remains the publication read set.
            using(PerformanceProfile.Measure(ProfileStage.CrowdPreparation))
            for(int i=0;i<count;i++){
                var unit=unitView[i];int ownRouteIndex=unit.routeIndex,ownLocalIndex=unit.localIndex;bool ownMoving=unit.Moving;var ownOutcome=unit.Outcome;bool active=unit.Moving&&!unit.Held&&unit.routeIndex<unit.route.Length;var target=unit.Position;double speed=unit.Speed;
                if(active){
                    if(TryRetireInvalidRecastLocal(i))ownLocalIndex=unit.localIndex;
                    if(unit.localIndex>=unit.localRoute.Length){
                        if(preparedStep?.RecastRoutes[i]!=null){
                            int selected=-1;
                            // Bounded candidates from the accepted ready route only.
                            // Full typed trace, rather than visiting every point,
                            // permits a forward rejoin. No new global path search.
                            int first=NavRecastIntervals.FirstTargetSurface(preparedStep.RecastRoutes[i],preparedStep.RecastStates[i]);
                            // The former ascending scan retained the greatest valid index.
                            // Test that same candidate first; every accepted sweep guard remains.
                            var hint=preparedStep.Formation[i];bool recovering=(hint.Flags&(NavFormationFrameHint.Directional|NavFormationFrameHint.Bound))==NavFormationFrameHint.Directional;
                            for(int candidate=unit.route.Length-1;candidate>=first;candidate--){
                                if(recovering&&!RecastFormationMotion.DirectionalAllows(hint,preparedStep.RecastRoutes[i],unit.Position,unit.route[candidate]))continue;
                                if(!NavRecastIntervals.CanTarget(preparedStep.RecastRoutes[i],preparedStep.RecastStates[i],unit.Location,candidate,preparedStep.Reservation,this,unit.Id)||!PhysicalSweep(unit.Location,unit.route[candidate],unit.Radius)||!TerrainClear(unit,unit.Position,unit.route[candidate])||!HeldSegmentFree(unit,unit.Position,unit.route[candidate]))continue;
                                // Publish a motor handoff from this actual physical
                                // state, never from the bank guide's forecast receipt.
                                if(recovering&&(!unit.GoalLocation.HasValue||!NavRecastIntervals.TryContinuationRejoin(preparedStep.RecastRoutes[i],preparedStep.RecastStates[i],unit.Location,Array.Empty<NavPoint>(),0,unit.route,candidate,unit.GoalLocation.Value,preparedStep.Reservation,out int same,this,unit.Id,true)||same!=candidate))continue;
                                selected=candidate;break;
                            }
                            if(selected>=0)unit.routeIndex=selected;else if(!recovering)active=false;
                            // A rejected straight bank connector is not a HOLD
                            // certificate or an unreachable result. Keep ordinary
                            // Select/refusal/local repair alive for an around path;
                            // every actual step still needs the owner domain/trace.
                        }else while(unit.routeIndex+1<unit.route.Length&&PhysicalSweep(unit.Location,unit.route[unit.routeIndex+1],unit.Radius)&&TerrainClear(unit,unit.Position,unit.route[unit.routeIndex+1])&&HeldSegmentFree(unit,unit.Position,unit.route[unit.routeIndex+1]))unit.routeIndex++;
                    }
                    bool local=unit.localIndex<unit.localRoute.Length;target=!active?unit.Position:local?unit.localRoute[unit.localIndex]:unit.route[unit.routeIndex];
                    // Intermediate global corners advance only through the full-sweep
                    // guard above. Terminal tolerance cannot skip an obstructed turn.
                    if(active&&Distance(target,unit.Position)<=profile.ArrivalTolerance&&(local||unit.routeIndex+1==unit.route.Length)&&(!local||CanRetireRecastLocal(i,unit.localIndex+1))){unit.blockedSeconds=0;unit.noProgressTicks=0;if(local)unit.localIndex++;else if(++unit.routeIndex>=unit.route.Length){unit.Moving=false;unit.Outcome=NavigationOutcome.Arrived;}active=false;}
                    if(active&&preparedStep!=null&&preparedStep.RecastRoutes[i]!=null)target=CurrentMotionTarget(unit,preparedStep.Formation[i]);
                    if(TryPreferredSpeed(unit.Id,out var preferred)){speed=Math.Min(speed,preferred);if(speed<=0)active=false;}
                }else if(unit.routeIndex>=unit.route.Length)unit.Moving=false;
                if(!active&&unit.CanYield&&!unit.Held&&TryPreferredSpeed(unit.Id,out var receiverCap))speed=Math.Min(speed,receiverCap);
                double ownPaceCap=0;
                if(preparedStep!=null&&!double.IsNaN(preparedStep.RecastReceiver[i])&&!unit.Held){ownPaceCap=speed;speed=Math.Min(unit.Speed,preparedStep.RecastReceiver[i]);}
                motion[i]=new NavMotionBody{LocalLookaheadSeconds=active&&preparedStep?.RecastRoutes[i]!=null&&unit.localIndex<unit.localRoute.Length&&speed>0?Math.Max(dt,Distance(unit.Position,target)/speed):0,OwnPaceCap=ownPaceCap,Id=unit.Id,Position=unit.Position,Target=target,Heading=unit.Heading,Radius=unit.Radius,Speed=speed,TurnSpeed=unit.TurnSpeed,ReverseDistance=unit.ReverseDistance,ReverseSpeedRatio=unit.ReverseSpeedRatio,ReverseConeRadians=unit.ReverseConeRadians,Active=active,OwnRouteIndex=ownRouteIndex,OwnLocalIndex=ownLocalIndex,OwnMoving=ownMoving,OwnOutcome=ownOutcome,TerminalSeed=preparedStep!=null&&preparedStep.TerminalSeeds[i]};
            }
            using(PerformanceProfile.Measure(ProfileStage.CrowdYield))PrepareProductionClearance(dt,motion,yieldQueue,yieldLabels);
            if(preparedStep!=null)RedirectOccupiedExitGuides(motion);
            // Candidate allies contribute their bounded reach speed before q9
            // truncation. Their preferred velocity remains zero until a chain
            // proposal explicitly activates one for a frame.
            // Include every non-held, positively capped Tank receiver before
            // q9. This bounded overcoverage also includes transitive chain rows.
            for(int i=0;i<count;i++)if(!motion[i].Active&&!unitView[i].Held&&unitView[i].CanYield&&motion[i].Speed>0)motion[i].Potential=true;
            using(PerformanceProfile.Measure(ProfileStage.CrowdNeighbors))NavCrowdMotion.Neighbors(motion,neighbors,neighborCounts,profile,dt,componentLabels);
            using(PerformanceProfile.Measure(ProfileStage.CrowdScale))NavCrowdMotion.ConstrainOverflowComponents(motion,neighbors,neighborCounts,componentLabels,originalSpeeds,profile,dt);
            using(PerformanceProfile.Measure(ProfileStage.CrowdSelect)){
                if(preparedStep!=null&&preparedStep.TrySelectParallel(dt)){
                    for(int i=0;i<count;i++)NeighborCandidates+=neighborCounts[i]<0?profile.CrowdNeighborCap+1:neighborCounts[i];
                }else for(int i=0;i<count;i++){NeighborCandidates+=neighborCounts[i]<0?profile.CrowdNeighborCap+1:neighborCounts[i];proposals[i]=NavCrowdMotion.Select(motion,i,neighbors,neighborCounts[i],profile,dt,this,unitView[i].Goal,false,preparedStep?.RecastRoutes[i]!=null?(NavPoint?)motion[i].Target:null);}
            }
            if(preparedStep!=null)RecordInitialRecastPolicyWait(motion,proposals);
            if(NavCrowdMotion.CausalActor!=0)for(int i=0;i<count;i++)if(motion[i].Id==NavCrowdMotion.CausalActor){NavCrowdMotion.Causal.Body=motion[i];NavCrowdMotion.Causal.Selected=proposals[i];NavCrowdMotion.Causal.SelectedSeen=true;if(preparedStep!=null){NavCrowdMotion.Causal.HintFlags=preparedStep.Formation[i].Flags;NavCrowdMotion.Causal.GuideTarget=preparedStep.Formation[i].Target;NavCrowdMotion.Causal.ForwardLimit=preparedStep.Formation[i].ForwardLimit;}}
            if(NavCrowdMotion.CausalActor!=0&&NavCrowdMotion.CausalPeerActor!=0)for(int i=0;i<count;i++)if(motion[i].Id==NavCrowdMotion.CausalActor||motion[i].Id==NavCrowdMotion.CausalPeerActor){
                bool focus=motion[i].Id==NavCrowdMotion.CausalActor;var value=new NavCrowdMotion.CausalPeer{SelectedSeen=true,Body=motion[i],Selected=proposals[i],Held=unitView[i].Held,LocalLength=unitView[i].localRoute.Length,GlobalLength=unitView[i].route.Length,NeighborCount=neighborCounts[i],NeighborMembershipKnown=neighborCounts[i]>=0};
                int other=focus?NavCrowdMotion.CausalPeerActor:NavCrowdMotion.CausalActor;
                for(int k=0;k<neighborCounts[i];k++)if(motion[neighbors[i*NavCrowdMotion.NeighborCapacity+k]].Id==other)value.HasOtherNeighbor=true;
                if(preparedStep!=null){var hint=preparedStep.Formation[i];value.PreparedSeen=true;value.HintFlags=hint.Flags;value.GuideTarget=hint.Target;value.ForwardLimit=hint.ForwardLimit;value.TargetLeg=hint.TargetLeg;
                    var route=preparedStep.RecastRoutes[i];if(route!=null){var id=route.Subscription;value.TypedSeen=true;value.TypedState=preparedStep.RecastStates[i];value.World=id.WorldGeneration;value.Request=id.RequestSequence;value.Group=id.GroupId;value.Root=id.RootOrderRevision;value.Activation=id.ActivationRevision;value.Entity=id.EntityId;value.Incarnation=id.Incarnation;value.MemberOrder=id.MemberOrderRevision;value.Mobility=id.MobilityRevision;}}
                if(focus)NavCrowdMotion.Causal.FocusContext=value;else NavCrowdMotion.Causal.Peer=value;
            }
            using(PerformanceProfile.Measure(ProfileStage.CrowdYield))ProposeProductionClearance(dt,motion,proposals,neighborCounts);
            using(PerformanceProfile.Measure(ProfileStage.CrowdJoint))NavCrowdMotion.ResolveJoint(motion,proposals,dt);
            using(PerformanceProfile.Measure(ProfileStage.CrowdYield))ActivateYieldChains(dt,motion,proposals,yieldLabels,yieldQueue,neighborCounts);
            for(int i=0;i<count;i++)proposals[i].IndependentTurn=false; // temporary first-winner provenance is consumed
            using(PerformanceProfile.Measure(ProfileStage.CrowdJoint))NavCrowdMotion.ResolveJoint(motion,proposals,dt);
            // A stopped receiver retracts its dependent chain translation. Joint
            // remains the final all-body safety check after that retraction.
            for(int i=0;i<count;i++)if(motion[i].Yielding&&proposals[i].Position.Equals(motion[i].Position)){
                int label=motion[i].YieldLabel;for(int j=0;j<count;j++)if(motion[j].YieldLabel==label){proposals[j].Position=motion[j].Position;proposals[j].Velocity=default;}
            }
            using(PerformanceProfile.Measure(ProfileStage.CrowdJoint))NavCrowdMotion.ResolveJoint(motion,proposals,dt);
            if(NavCrowdMotion.CausalActor!=0)for(int i=0;i<count;i++)if(motion[i].Id==NavCrowdMotion.CausalActor)NavCrowdMotion.Causal.PreFilter=proposals[i];
            if(NavCrowdMotion.CausalActor!=0&&NavCrowdMotion.CausalPeerActor!=0)for(int i=0;i<count;i++){
                if(motion[i].Id==NavCrowdMotion.CausalActor){NavCrowdMotion.Causal.FocusContext.PreFilter=proposals[i];NavCrowdMotion.Causal.FocusContext.PreFilterBody=motion[i];NavCrowdMotion.Causal.FocusContext.PreFilterSeen=true;}
                if(motion[i].Id==NavCrowdMotion.CausalPeerActor){NavCrowdMotion.Causal.Peer.PreFilter=proposals[i];NavCrowdMotion.Causal.Peer.PreFilterBody=motion[i];NavCrowdMotion.Causal.Peer.PreFilterSeen=true;}
            }
            if(preparedStep!=null)using(PerformanceProfile.Measure(ProfileStage.CrowdCertificates)){FilterRecastSweeps(dt,motion,proposals);RetryPolicyStoppedYield(dt,motion,proposals,yieldLabels,yieldQueue,neighborCounts);RetryPolicyStoppedPeers(dt,motion,proposals,neighbors,neighborCounts,yieldQueue);}
            if(NavCrowdMotion.CausalActor!=0)for(int i=0;i<count;i++)if(motion[i].Id==NavCrowdMotion.CausalActor){NavCrowdMotion.Causal.Final=proposals[i];NavCrowdMotion.Causal.FinalSeen=true;}
            if(NavCrowdMotion.CausalActor!=0&&NavCrowdMotion.CausalPeerActor!=0)for(int i=0;i<count;i++){
                if(motion[i].Id==NavCrowdMotion.CausalActor){NavCrowdMotion.Causal.FocusContext.PolicyRefused=NavCrowdMotion.Causal.PolicyRefused;NavCrowdMotion.Causal.FocusContext.TypedRefused=NavCrowdMotion.Causal.TypedRefused;NavCrowdMotion.Causal.FocusContext.Final=proposals[i];NavCrowdMotion.Causal.FocusContext.FinalBody=motion[i];NavCrowdMotion.Causal.FocusContext.FinalSeen=true;if(preparedStep!=null)NavCrowdMotion.Causal.FocusContext.FinalHintFlags=preparedStep.Formation[i].Flags;}
                if(motion[i].Id==NavCrowdMotion.CausalPeerActor){NavCrowdMotion.Causal.Peer.Final=proposals[i];NavCrowdMotion.Causal.Peer.FinalBody=motion[i];NavCrowdMotion.Causal.Peer.FinalSeen=true;if(preparedStep!=null)NavCrowdMotion.Causal.Peer.FinalHintFlags=preparedStep.Formation[i].Flags;}
            }
            for(int i=0;i<count;i++){
                var unit=unitView[i];var next=proposals[i];var body=motion[i];
                // Final stationarity and the accepted committed cohort authorize
                // one turn toward its temporary target. Joint may have refused
                // translation earlier; its cleared Refuse flag is not a certificate.
                if(body.Yielding&&body.YieldLabel>0&&body.Speed>0&&next.Position.Equals(body.Position)&&next.Velocity.Equals(default(NavPoint))&&!next.Blocked&&!next.StaticNoWinner&&!next.Overflow&&!next.Refuse&&!body.Target.Equals(body.Position)){
                    next.Heading=NavCrowdMotion.Turn(body.Heading,Math.Atan2(body.Target.Z-body.Position.Z,body.Target.X-body.Position.X),body.TurnSpeed*dt);proposals[i].Heading=next.Heading;
                }
                if(preparedStep!=null&&!body.Yielding&&preparedStep.RecastRoutes[i]!=null&&preparedStep.ComponentLabels[i]>=0&&preparedStep.RecastReceipts[i].Verified&&preparedStep.RecastReceipts[i].To.Equals(next.Position))unit.routeIndex=preparedStep.ComponentLabels[i];
                if(!motion[i].Active)continue;if(motion[i].Yielding){unit.routeIndex=motion[i].OwnRouteIndex;unit.localIndex=motion[i].OwnLocalIndex;unit.Moving=motion[i].OwnMoving;unit.Outcome=motion[i].OwnOutcome;}unit.Heading=next.Heading;
                if(!next.Position.Equals(unit.Position)){
                    if(!Traverse(unit.Location,next.Position,unit.Radius,out var moved,unit.Id))throw new InvalidOperationException("Prepared crowd terrain changed during motion.");
                    if(preparedStep?.RecastRoutes[i]!=null&&preparedStep.RecastReceipts[i].Verified&&preparedStep.RecastReceipts[i].ToSurface!=moved.SurfaceId)throw new InvalidOperationException("Recast receipt differs from actual terrain sweep.");
                    if(preparedStep!=null&&preparedStep.RecastReceipts[i].ProductionUnmapped){var receipt=preparedStep.RecastReceipts[i];receipt.ToSurface=moved.SurfaceId;preparedStep.RecastReceipts[i]=receipt;}
                    unit.Location=moved;unit.Position=next.Position;unit.blockedSeconds=0;unit.noProgressTicks=0;
                }
            }
            BuildIndex();
            for(int i=0;i<count;i++){
                var unit=unitView[i];var next=proposals[i];
                // Actual peer-refused own translation must reach the existing
                // bounded recovery path too. A later accepted translation above
                // resets progress; cooperative/producer turns and overflow wait.
                if(preparedStep!=null&&(preparedStep.Formation[i].Flags&(NavFormationFrameHint.Wait|NavFormationFrameHint.PolicyWait))!=0)continue;
                if(motion[i].Active&&!motion[i].Yielding&&next.Position.Equals(motion[i].Position)&&(next.Blocked||next.StaticNoWinner||next.JointRefused||next.CurrentProgressExhausted)&&!next.Overflow){
                    // A fresh exhausted turn with a real static obstruction
                    // needs a path decision, not more heuristic rotations. Use
                    // the existing bounded repair once on this failure edge;
                    // failed/continuing stalls retain the original retry timer.
                    bool freshStatic=next.CurrentProgressExhausted&&unit.noProgressTicks==0&&unit.localIndex>=unit.localRoute.Length&&CurrentMotorObstruction(i,true);
                    MarkNoProgress(unit);unit.blockedSeconds+=dt;
                    if(unit.noProgressTicks*dt>=profile.BlockedTimeoutSeconds){RejectMovingUnit(unit,NavigationOutcome.Blocked);continue;}
                    // A formation guide is temporary and may face an occupied lane.
                    // Repair toward accepted route intent;
                    // the same formation bound still filters every detour step.
                    if(freshStatic||unit.blockedSeconds>=profile.RepairRetrySeconds){unit.blockedSeconds=0;CommitDetour(unit,unit.route[unit.routeIndex],ref repair);}
                }else if(motion[i].Active&&!motion[i].Yielding&&next.Position.Equals(motion[i].Position)&&!next.Overflow&&!next.Refuse&&unit.localIndex>=unit.localRoute.Length&&CurrentMotorObstruction(i,false)){
                    // A useful turn is not no-progress failure evidence. But a
                    // real body obstructing the current motor still
                    // needs a bounded bypass: waiting for Select to exhaust its
                    // turns can otherwise prevent repair forever. Keep the same
                    // repair timer, paid search, endpoint suffix and all sweeps;
                    // no failure/progress credit is manufactured by this probe.
                    unit.blockedSeconds+=dt;
                    // A shortest in-place alignment may take up to pi/turnSpeed.
                    // Let that finite turn finish before replacing its motor;
                    // otherwise retry can alternate two valid bypasses forever.
                    double turnWindow=unit.TurnSpeed>0?Math.PI/unit.TurnSpeed+dt:0;
                    if(unit.blockedSeconds>=Math.Max(profile.RepairRetrySeconds,turnWindow)){unit.blockedSeconds=0;CommitDetour(unit,unit.route[unit.routeIndex],ref repair);}
                }
            }
        }
        private bool CurrentMotorObstruction(int row,bool stationaryOnly)
        {
            if(preparedStep==null||preparedStep.RecastRoutes[row]==null)return false;
            var unit=unitView[row];var target=CurrentMotionTarget(unit,preparedStep.Formation[row]);
            for(int i=0;i<unitView.Count;i++){
                var peer=unitView[i];if(i==row||stationaryOnly&&peer.Moving&&!peer.Held||peer.Location.SurfaceId!=unit.Location.SurfaceId)continue;
                double radius=unit.Radius+peer.Radius;
                if(SegmentPointDistanceSquared(peer.Position,unit.Position,target)<radius*radius-1e-9)return true;
            }
            return false;
        }
        private bool TryPreferredSpeed(int id,out double speed)
        {if(preparedStep==null)return preferredMaxSpeed.TryGetValue(id,out speed);speed=preparedStep.Preferred[ActorIndex(id)];return !double.IsNaN(speed);}
        private void RejectMovingUnit(NavUnit unit,NavigationOutcome outcome)
        {if(preparedStep==null){Reject(unit.Id,outcome);return;}unit.MobilityRevision++;unit.BindRouteBackingNoThrow(null,null);unit.Moving=unit.Held=false;unit.Outcome=outcome;preparedStep.ClearRoutes(unit);}
        private bool FreeMove(NavUnit unit,NavPoint candidate){return Clear(unit,unit.Position,candidate);}
        internal bool PhysicalSupportContact(NavUnit a,NavUnit b)
        {
            double dx=a.Position.X-b.Position.X,dz=a.Position.Z-b.Position.Z,gap=a.Radius+b.Radius+profile.ArrivalTolerance;if(dx*dx+dz*dz>gap*gap||dx*dx+dz*dz<(a.Radius+b.Radius)*(a.Radius+b.Radius)-1e-9)return false;
            var midpoint=new NavPoint((a.Position.X+b.Position.X)*.5,(a.Position.Z+b.Position.Z)*.5);
            if(terrain==null)return a.Location.SurfaceId==NavLocation.FlatSurface&&b.Location.SurfaceId==NavLocation.FlatSurface&&geometry.IsFree(a.Position,a.Radius)&&geometry.IsFree(b.Position,b.Radius)&&geometry.SegmentFree(a.Position,b.Position,Math.Min(a.Radius,b.Radius));
            if(terrain is IRecastPhysicalTerrain physical){
                if(a.Location.SurfaceId!=b.Location.SurfaceId)return false;
                double radius=Math.Min(a.Radius,b.Radius);var middle=new NavLocation(midpoint,a.Location.SurfaceId);var support=physical.PhysicalSupportAt(a.Location);
                return support!=null&&ReferenceEquals(support,physical.PhysicalSupportAt(b.Location))&&ReferenceEquals(support,physical.PhysicalSupportAt(middle))&&
                    geometry.SegmentFree(a.Position,b.Position,radius)&&terrain.IsValidLocation(a.Location,a.Radius)&&terrain.IsValidLocation(b.Location,b.Radius)&&terrain.IsValidLocation(middle,radius)&&
                    physical.TryTraverse(a.Location,b.Position,radius,out var end)&&end.Equals(b.Location);
            }
            var first=terrain.SupportAt(a.Position);if(first==null||!ReferenceEquals(first,terrain.SupportAt(b.Position))||!ReferenceEquals(first,terrain.SupportAt(midpoint)))return false;
            if(!geometry.SegmentFree(a.Position,b.Position,Math.Min(a.Radius,b.Radius))||!terrain.SupportsFootprint(a.Position,a.Radius)||!terrain.SupportsFootprint(b.Position,b.Radius)||!terrain.SupportsFootprint(midpoint,Math.Min(a.Radius,b.Radius))||!terrain.SupportsSweep(a.Position,b.Position,Math.Min(a.Radius,b.Radius)))return false;
            return terrain.TryLocate(a.Position,a.Radius,out var la)&&terrain.TryLocate(b.Position,b.Radius,out var lb)&&la.Equals(a.Location)&&lb.Equals(b.Location);
        }
        private void ActivateYieldChains(double dt,NavMotionBody[] bodies,NavMotionProposal[] proposals,int[] yieldLabels,int[] queue,int[] neighborCounts)
            =>ActivateYieldChainsCore(dt,bodies,proposals,yieldLabels,queue,neighborCounts,false);
        private void RecordInitialRecastPolicyWait(NavMotionBody[] bodies,NavMotionProposal[] proposals)
        {
            // Joint may retract a policy-forbidden own proposal first. Preserve
            // that voluntary wait before its stationary result loses the reason;
            // it must not accrue a physical-block timeout while ahead of its row.
            // This only tags derived policy. Final owner sweeps, cohort handling
            // and all-body Joint still decide every actual displacement.
            for(int i=0;i<bodies.Length;i++){
                var body=bodies[i];var next=proposals[i];var route=preparedStep.RecastRoutes[i];
                if(route==null||!body.Active||!body.OwnMoving||body.Yielding||body.YieldLabel!=0||next.Overflow||next.Refuse)continue;
                if(next.RoleCapWait||!next.Position.Equals(body.Position)&&!RecastFormationMotion.Allows(preparedStep.Formation[i],route,body.Position,next.Position,false))preparedStep.Formation[i].Flags|=NavFormationFrameHint.PolicyWait;
            }
        }
        private bool StoppedYieldReceiver(NavMotionBody[] bodies,NavMotionProposal[] proposals,int row)
        {
            var body=bodies[row];var next=proposals[row];
            if(preparedStep==null||preparedStep.RecastRoutes[row]==null||!body.Active||!body.OwnMoving||body.Yielding||body.YieldLabel!=0||body.TerminalSeed||unitView[row].Held||!unitView[row].CanYield||!(body.Speed>0)||
                !next.Position.Equals(body.Position)||!next.Velocity.Equals(default(NavPoint))||next.Overflow)return false;
            var flags=preparedStep.Formation[row].Flags;
            if((flags&NavFormationFrameHint.PolicyWait)!=0)return true;
            // An earlier independent translation can also be invalidated by
            // actual peer safety. A previously stalled, now physically refused
            // ally is available to the same certified cohort as a policy stop.
            // Fresh actors and useful independent turns keep their first choice.
            return unitView[row].noProgressTicks>0&&(flags&NavFormationFrameHint.Wait)==0&&(next.JointRefused||next.StaticNoWinner||next.Blocked);
        }
        private void RetryPolicyStoppedYield(double dt,NavMotionBody[] bodies,NavMotionProposal[] proposals,int[] yieldLabels,int[] queue,int[] neighborCounts)
        {
            bool stopped=false;for(int i=0;i<bodies.Length;i++)if(StoppedYieldReceiver(bodies,proposals,i)){stopped=true;break;}if(!stopped)return;
            if(!ActivateYieldChainsCore(dt,bodies,proposals,yieldLabels,queue,neighborCounts,true))return;
            NavCrowdMotion.ResolveJoint(bodies,proposals,dt);
            // The same all-or-nothing cohort retraction and owner certificates
            // apply when a final policy or peer refusal stopped an ally.
            for(int i=0;i<bodies.Length;i++)if(bodies[i].Yielding&&proposals[i].Position.Equals(bodies[i].Position)){
                int label=bodies[i].YieldLabel;for(int j=0;j<bodies.Length;j++)if(bodies[j].YieldLabel==label){proposals[j].Position=bodies[j].Position;proposals[j].Velocity=default;}
            }
            NavCrowdMotion.ResolveJoint(bodies,proposals,dt);FilterRecastSweeps(dt,bodies,proposals);
        }
        private bool ActivateYieldChainsCore(double dt,NavMotionBody[] bodies,NavMotionProposal[] proposals,int[] yieldLabels,int[] queue,int[] neighborCounts,bool policyStoppedOnly)
        {
            int count=bodies.Length;bool activated=false;
            // Freeze independent choices before any cooperative target substitution.
            // Zero reserves a certified own choice; positive labels belong to queues.
            // A later root must not enlist that actor and reverse its accepted turn.
            for(int i=0;i<count;i++){
                // Retain the first pass's independent choice unless its actual
                // stationary refusal qualified this previously stalled receiver.
                bool retained=policyStoppedOnly&&yieldLabels[i]==0&&!StoppedYieldReceiver(bodies,proposals,i);
                yieldLabels[i]=retained||PreserveIndependentAvoidance(dt,bodies,proposals,i)?0:-1;
            }
            for(int root=0;root<count;root++){
                if(yieldLabels[root]==0||!bodies[root].Active||!unitView[root].CanYield||unitView[root].Held||bodies[root].TerminalSeed||bodies[root].YieldLabel!=0||proposals[root].Overflow||bodies[root].Speed<=0)continue;
                if(policyStoppedOnly){
                    if(preparedStep.RecastRoutes[root]==null||neighborCounts[root]<0||unitView[root].noProgressTicks==0||(preparedStep.Formation[root].Flags&(NavFormationFrameHint.Wait|NavFormationFrameHint.PolicyWait))!=0||!proposals[root].Position.Equals(bodies[root].Position)||!proposals[root].Velocity.Equals(default(NavPoint))||!(proposals[root].JointRefused||proposals[root].StaticNoWinner||proposals[root].Blocked))continue;
                    bool contact=false;for(int peer=0;peer<count;peer++)if(peer!=root&&StoppedYieldReceiver(bodies,proposals,peer)&&unitView[peer].Team==unitView[root].Team&&PhysicalSupportContact(unitView[root],unitView[peer])){contact=true;break;}if(!contact)continue;
                }
                // Only an own-intent forward sweep obstructed by a certified
                // free ally can seed displacement. Sampling/refusal flags alone
                // do not prove that a receiver is in the initiator's way.
                var own=bodies[root];own.Speed=own.OwnSpeed;double aim=Math.Atan2(own.Target.Z-own.Position.Z,own.Target.X-own.Position.X);
                double heading=NavCrowdMotion.Turn(own.Heading,aim,own.TurnSpeed*dt);
                // Preserve the actual independent choice before either the
                // own-intent fallback or any cooperative target substitution.
                if(Math.Abs(NavCrowdMotion.Angle(aim-heading))>profile.ForwardAlignmentAngle){
                    // Joint can refuse a selected avoidance translation while
                    // preserving its heading. Only certified free-ally contact
                    // permits an own-intent turn here; alignment still gates all
                    // chain translation and receiver displacement below.
                    if(proposals[root].Refuse||!proposals[root].Position.Equals(own.Position)||!proposals[root].Velocity.Equals(default(NavPoint)))continue;
                    double turnStep=Math.Min(own.Speed*dt,Distance(own.Target,own.Position));var turnEnd=new NavPoint(own.Position.X+Math.Cos(heading)*turnStep,own.Position.Z+Math.Sin(heading)*turnStep);
                    if(!geometry.SegmentFree(own.Position,turnEnd,own.Radius,PhysicalLayer(unitView[root].Location))||!Traverse(unitView[root].Location,turnEnd,own.Radius,out _,unitView[root].Id))continue;
                    bool allyObstruction=false,hardObstruction=false;
                    for(int peer=0;peer<count;peer++){
                        if(peer==root||SegmentPointDistanceSquared(bodies[peer].Position,own.Position,turnEnd)>=(own.Radius+bodies[peer].Radius)*(own.Radius+bodies[peer].Radius)-1e-9)continue;
                        if(unitView[peer].Held||!unitView[peer].CanYield||unitView[peer].Team!=unitView[root].Team||bodies[peer].TerminalSeed||!(bodies[peer].Speed>0)||!PhysicalSupportContact(unitView[root],unitView[peer]))hardObstruction=true;
                        else allyObstruction=true;
                    }
                    if(allyObstruction&&!hardObstruction)proposals[root].Heading=heading;
                    continue;
                }
                double ownStep=Math.Min(own.Speed*dt,Distance(own.Target,own.Position));var ownEnd=new NavPoint(own.Position.X+Math.Cos(heading)*ownStep,own.Position.Z+Math.Sin(heading)*ownStep);bool obstructed=false;
                for(int peer=0;peer<count;peer++)if(peer!=root&&!unitView[peer].Held&&unitView[peer].CanYield&&unitView[peer].Team==unitView[root].Team&&PhysicalSupportContact(unitView[root],unitView[peer])&&SegmentPointDistanceSquared(bodies[peer].Position,own.Position,ownEnd)<(own.Radius+bodies[peer].Radius)*(own.Radius+bodies[peer].Radius)-1e-9){obstructed=true;break;}
                if(!obstructed)continue;
                int qh=0,qt=0,group=root+1;yieldLabels[root]=group;queue[qt++]=root;
                while(qh<qt){int at=queue[qh++];for(int peer=0;peer<count;peer++){
                    if(yieldLabels[peer]>=0||bodies[peer].YieldLabel!=0||unitView[peer].Held||!unitView[peer].CanYield||bodies[peer].TerminalSeed||unitView[peer].Team!=unitView[root].Team||!PhysicalSupportContact(unitView[at],unitView[peer]))continue;
                    yieldLabels[peer]=group;queue[qt++]=peer;
                }}
                if(qt<2)continue;
                var delta=new NavPoint(bodies[root].Target.X-bodies[root].Position.X,bodies[root].Target.Z-bodies[root].Position.Z);double length=Math.Sqrt(delta.X*delta.X+delta.Z*delta.Z);if(length<=1e-9)continue;
                var direction=new NavPoint(delta.X/length,delta.Z/length);double step=ownStep;
                for(int n=0;n<qt;n++)step=Math.Min(step,bodies[queue[n]].Speed*dt);
                if(step<=0||double.IsNaN(step)||double.IsInfinity(step))continue;
                bool clear=true;
                for(int n=0;n<qt;n++)bodies[queue[n]].YieldLabel=group;
                for(int n=0;n<qt;n++){
                    int row=queue[n];var actor=unitView[row];var end=new NavPoint(bodies[row].Position.X+direction.X*step,bodies[row].Position.Z+direction.Z*step);
                    if(!geometry.SegmentFree(bodies[row].Position,end,bodies[row].Radius,PhysicalLayer(actor.Location))||!Traverse(actor.Location,end,bodies[row].Radius,out _,actor.Id)||!UnitFreeYield(actor,bodies,bodies[row].Position,end)){clear=false;break;}
                    // Test the ACTUAL cohort displacement before consuming an
                    // independent stationary turn. Its own forward prediction
                    // can be legal while this root's different direction crosses
                    // an unaccepted seam. Later refusal would erase the turn.
                    var selected=proposals[row];
                    if(bodies[row].OwnMoving&&selected.IndependentTurn&&selected.Position.Equals(bodies[row].Position)&&selected.Velocity.Equals(default(NavPoint))&&!selected.Heading.Equals(bodies[row].Heading)&&preparedStep?.RecastRoutes[row]!=null&&
                        !NavRecastIntervals.Advance(preparedStep.RecastRoutes[row],preparedStep.RecastStates[row],actor.Location,end,preparedStep.Reservation,out _,this,actor.Id)){clear=false;break;}
                }
                if(!clear){for(int n=0;n<qt;n++)bodies[queue[n]].YieldLabel=0;continue;}
                for(int n=0;n<qt;n++){int row=queue[n];bodies[row].Target=new NavPoint(bodies[row].Position.X+direction.X*step,bodies[row].Position.Z+direction.Z*step);bodies[row].Active=true;bodies[row].Potential=false;bodies[row].Yielding=true;}
                ProposeAcceptedYieldChain(dt,bodies,proposals,neighborCounts,queue,qt,step);activated=true;
            }
            return activated;
        }
        bool INavLayerSweepAuthority.TraceLayerSweep(ILayeredNavigationProvider provider,NavLocation from,NavPoint to,double radius,NavTypedRoute route,int cursor,INavStorageReservation r,out NavLocation reached,out IReadOnlyList<NavTypedLeg> trace){
            reached=default;trace=null;
            using(var scratch=NavStorageScratch.Reserve(r,4096)){
                if(terrain is IRecastPhysicalTerrain physical)return physical.TracePhysical(from,to,radius,provider,route,cursor,r,out reached,out trace);
                if(terrain!=null||!(provider is LayeredNavigationProvider flat)||flat.Id!="flat"||flat.SemanticsVersion!=1||flat.Surfaces.Count!=1||flat.Surfaces[0].Id!=NavLocation.FlatSurface||flat.Portals.Count!=0||from.SurfaceId!=NavLocation.FlatSurface)return false;
                var end=new NavLocation(to,NavLocation.FlatSurface);
                if(!PhysicalSweep(from,to,radius)||!RecastLayerTrace.Try(provider,from,end,new NavClearanceProfile("ground",1,"ground",0,radius),out trace,r))return false;
                reached=end;return true;
            }
        }
        bool INavRecastSweepAuthority.AllowsRecastSweep(int actor,NavLocation from,NavPoint to,double radius)
        {int row=preparedStep==null?ActorIndex(actor):preparedStep.Find(actor);return row>=0&&PhysicalSweep(from,to,radius)&&HeldSegmentFree(unitView[row],from.Position,to);}
        private static bool SharedExitHandoffSource(NavUnit actor,NavTypedRoute route,NavRecastStepState state,NavFormationFrameHint hint,string exit)
        {
            if(state.Phase!=1||actor.localRoute.Length==0||actor.localIndex<actor.localRoute.Length||hint.TargetLeg<0||hint.TargetLeg>=route.Legs.Count||route.Legs[hint.TargetLeg].Kind!=NavTypedLegKind.Portal)return false;
            int next=hint.TargetLeg+1;while(next<route.Legs.Count&&route.Legs[next].Kind!=NavTypedLegKind.Portal)next++;
            if(next>=route.Legs.Count||state.Cursor!=next-1||state.Cursor<=hint.TargetLeg)return false;
            var leg=route.Legs[state.Cursor];var portal=route.Legs[next];string support=actor.Location.SurfaceId;
            return leg.Kind==NavTypedLegKind.Surface&&leg.From.SurfaceId==support&&leg.To.SurfaceId==support&&route.Legs[hint.TargetLeg].To.SurfaceId==support&&portal.From.SurfaceId==support&&portal.To.SurfaceId==exit;
        }
        private void RedirectOccupiedExitGuides(NavMotionBody[] bodies)
        {
            for(int i=0;i<bodies.Length;i++){
                var body=bodies[i];var actor=unitView[i];var hint=preparedStep.Formation[i];var route=preparedStep.RecastRoutes[i];
                bool reachedRecovery=(hint.Flags&(NavFormationFrameHint.Guide|NavFormationFrameHint.Directional|NavFormationFrameHint.Bound))==(NavFormationFrameHint.Guide|NavFormationFrameHint.Directional)&&Distance(actor.Position,hint.Target)<=profile.ArrivalTolerance;
                if(!body.Active||!body.OwnMoving||body.Yielding||body.YieldLabel!=0||actor.Held||route?.RecastPlan==null||actor.localIndex<actor.localRoute.Length||actor.routeIndex>=actor.route.Length||!actor.GoalLocation.HasValue||
                    !reachedRecovery&&(hint.Flags&(NavFormationFrameHint.Guide|NavFormationFrameHint.Bound))!=(NavFormationFrameHint.Guide|NavFormationFrameHint.Bound)||(hint.Flags&(NavFormationFrameHint.Wait|NavFormationFrameHint.PolicyWait|NavFormationFrameHint.PreEntry))!=0)continue;
                var binding=route.Subscription;
                if(binding.Provenance!=NavRouteProvenance.CommandMember||binding.EntityId!=actor.Id||binding.Incarnation!=actor.Incarnation||binding.MobilityRevision!=actor.MobilityRevision||binding.RequestSequence!=actor.InstalledRequest||binding.GroupId!=actor.InstalledGroupId||binding.ActivationRevision!=actor.InstalledRevision||binding.MemberOrderRevision!=actor.InstalledRevision||!binding.AssignedEndpoint.Equals(actor.GoalLocation.Value))continue;
                string exit=route.RecastPlan.Spine.Node(route.RecastPlan.Exit).SurfaceId;var state=preparedStep.RecastStates[i];
                bool terminal=state.Phase==2,shared=SharedExitHandoffSource(actor,route,state,hint,exit);
                if(reachedRecovery?(state.Phase!=1&&state.Phase!=2)||actor.Location.SurfaceId!=exit:!terminal&&!shared||terminal&&actor.Location.SurfaceId!=exit)continue;
                if(!reachedRecovery&&(!RecastFormationMotion.Axis(route,hint.TargetLeg,out var start,out _,out var direction,out _)||hint.ForwardLimit-RecastFormationMotion.Station(actor.Position,start,direction)<=profile.ArrivalTolerance))continue;
                if(terminal&&!reachedRecovery){
                    if(!NavRecastIntervals.Advance(route,preparedStep.RecastStates[i],actor.Location,hint.Target,preparedStep.Reservation,out var guide,this,actor.Id)||guide.ToSurface!=exit)continue;
                    var location=new NavLocation(guide.To,guide.ToSurface);bool occupied=false;
                    for(int j=0;j<unitView.Count;j++){
                        if(j==i)continue;var peer=unitView[j];double dx=peer.Position.X-location.Position.X,dz=peer.Position.Z-location.Position.Z,radius=actor.Radius+peer.Radius;
                        if(dx*dx+dz*dz>=radius*radius||peer.Location.SurfaceId!=location.SurfaceId)continue;
                        // Resolve actual support, not just overlapping XZ footprints.
                        if(terrain is IRecastPhysicalTerrain physical){var support=physical.PhysicalSupportAt(location);occupied=support!=null&&ReferenceEquals(support,physical.PhysicalSupportAt(peer.Location))&&terrain.IsValidLocation(location,actor.Radius)&&terrain.IsValidLocation(peer.Location,peer.Radius);}
                        else if(terrain!=null){var support=terrain.SupportAt(location.Position);occupied=support!=null&&ReferenceEquals(support,terrain.SupportAt(peer.Position))&&terrain.TryLocate(location.Position,actor.Radius,out var targetLocation)&&targetLocation.Equals(location)&&terrain.TryLocate(peer.Position,peer.Radius,out var peerLocation)&&peerLocation.Equals(peer.Location);}
                        if(occupied)break;
                    }
                    if(!occupied)continue;
                }
                // A completed last-Shared local detour may return to its fully
                // accepted suffix even when the temporary lane endpoint is free.
                var target=actor.route[actor.routeIndex];
                if(!RecastFormationMotion.DirectionalAllows(hint,route,actor.Position,target))continue;
                // Keep the original Guide/directional domain, even a few ulps
                // before its endpoint. Only a complete current typed/HOLD motor
                // suffix may replace the reached temporary aim.
                if(reachedRecovery&&(!RecastFormationMotion.DirectionalTailAllows(hint,route,actor.Position,actor.route,actor.routeIndex,actor.Goal)||!NavRecastIntervals.CanTarget(route,state,actor.Location,actor.routeIndex,preparedStep.Reservation,this,actor.Id)))continue;
                // A completed local repair may rejoin the accepted exit while
                // its body is still on the final shared surface. The full
                // continuation and the existing Guide+Bound filters still own it.
                if(shared&&!NavRecastIntervals.CanTarget(route,state,actor.Location,actor.routeIndex,preparedStep.Reservation,this,actor.Id))continue;
                if(!NavRecastIntervals.Advance(route,preparedStep.RecastStates[i],actor.Location,target,preparedStep.Reservation,out _,this,actor.Id)||!NavRecastIntervals.TryContinuationRejoin(route,preparedStep.RecastStates[i],actor.Location,Array.Empty<NavPoint>(),0,actor.route,actor.routeIndex,actor.GoalLocation.Value,preparedStep.Reservation,out int same,this,actor.Id)||same!=actor.routeIndex)continue;
                // Keep Guide+Bound and their owner/helper guards. Only this
                // derived aim changes; the accepted motor cursor is untouched.
                hint.Target=target;preparedStep.Formation[i]=hint;bodies[i].Target=target;
            }
        }
        private void RetryPolicyStoppedPeers(double dt,NavMotionBody[] bodies,NavMotionProposal[] proposals,int[] neighbors,int[] counts,int[] stopped)
        {
            int stoppedCount=0;bool retried=false;
            // Yield-chain construction has finished; borrow its paid queue only
            // for the immutable set of actually stationary normal peers. Keep
            // the original policy set, including any non-own-moving receiver.
            for(int i=0;i<bodies.Length;i++)if(bodies[i].Active&&!bodies[i].Yielding&&bodies[i].YieldLabel==0&&!unitView[i].Held&&(bodies[i].OwnMoving||(preparedStep.Formation[i].Flags&NavFormationFrameHint.PolicyWait)!=0)&&proposals[i].Position.Equals(bodies[i].Position)&&proposals[i].Velocity.Equals(default(NavPoint)))stopped[stoppedCount++]=i;
            if(stoppedCount==0)return;
            try{
                for(int n=0;n<stoppedCount;n++)bodies[stopped[n]].Active=false;
                for(int i=0;i<bodies.Length;i++){
                    var old=proposals[i];var body=bodies[i];
                    bool ownStopped=!body.Active&&Array.IndexOf(stopped,i,0,stoppedCount)>=0;
                    if(ownStopped)body.Active=true; // original value, for eligibility and this own Select only
                    // Normal forecasting can undo the previous retry's avoidance
                    // turn while the same neighbors are still actually stopped.
                    // Requalify that turn against this frame's stopped set too;
                    // no new heading memory, progress credit or motion waiver.
                    // An installed local repair also records an actual earlier
                    // stall. Its first translation can clear noProgress before
                    // a guide resumes and starts the same stationary turn cycle.
                    // Reuse that owned route evidence without a new counter or
                    // changing the fresh-turn exclusion.
                    bool priorStall=unitView[i].noProgressTicks>0||unitView[i].localRoute.Length>0;
                    bool normalTurn=!old.JointRefused&&!old.Blocked&&!old.StaticNoWinner&&!old.Heading.Equals(body.Heading);
                    // A fresh own turn can already belong to an observed hold
                    // cycle: its stationary footprint is an ACTUAL joint refuser.
                    // Requalify that turn against actual stopped trajectories,
                    // rather than forecasting that the refused peer will leave.
                    // Contact with a peer refused by another actor supplies no provenance.
                    bool refusedContact=false;
                    if(normalTurn&&!priorStall&&body.Active&&body.OwnMoving&&!body.Yielding&&body.YieldLabel==0&&!unitView[i].Held&&counts[i]>=0){
                        for(int k=0;k<counts[i];k++){
                            int peer=neighbors[i*NavCrowdMotion.NeighborCapacity+k];var other=bodies[peer];var blocked=proposals[peer];
                            if(peer!=i&&other.OwnMoving&&!other.Yielding&&other.YieldLabel==0&&!unitView[peer].Held&&blocked.JointRefused&&blocked.HasJointRefuser&&blocked.JointFirstRefuser==body.Id&&blocked.Position.Equals(other.Position)&&blocked.Velocity.Equals(default(NavPoint))&&PhysicalSupportContact(unitView[i],unitView[peer])){refusedContact=true;break;}
                        }
                    }
                    bool stalledTurn=normalTurn&&(priorStall||refusedContact);
                    if(!body.Active||!body.OwnMoving||body.Yielding||body.YieldLabel!=0||unitView[i].Held||counts[i]<0||old.Overflow||!(old.JointRefused||(old.StaticNoWinner&&old.Heading==body.Heading)||stalledTurn)||!old.Position.Equals(body.Position)||!old.Velocity.Equals(default(NavPoint))||(preparedStep.Formation[i].Flags&(NavFormationFrameHint.Wait|NavFormationFrameHint.PolicyWait))!=0)continue;
                    var route=preparedStep.RecastRoutes[i];if(route==null)continue;
                    int witness=-1;for(int k=0;k<counts[i];k++){int peer=neighbors[i*NavCrowdMotion.NeighborCapacity+k];if(peer!=i&&(Array.IndexOf(stopped,peer,0,stoppedCount)>=0||(old.StaticNoWinner&&!bodies[peer].Active&&!unitView[peer].Held&&!bodies[peer].Yielding&&bodies[peer].YieldLabel==0&&proposals[peer].Position.Equals(bodies[peer].Position)&&proposals[peer].Velocity.Equals(default(NavPoint))))){witness=peer;break;}}
                    if(witness<0)continue;
                    var hint=preparedStep.Formation[i];
                    if(NavCrowdMotion.CausalActor!=0&&body.Id==NavCrowdMotion.CausalActor){var peer=bodies[witness];if(Array.IndexOf(stopped,witness,0,stoppedCount)>=0)peer.Active=true;NavCrowdMotion.Causal.RetryAttempted=true;NavCrowdMotion.Causal.RetryPeerId=peer.Id;NavCrowdMotion.Causal.RetryPeerBody=peer;NavCrowdMotion.Causal.RetryPeerHintFlags=preparedStep.Formation[witness].Flags;NavCrowdMotion.Causal.RetryPeerInitiallyZero=true;NavCrowdMotion.Causal.RetryPeerMatchedRefuser=NavCrowdMotion.Causal.Last.Actor==body.Id&&NavCrowdMotion.Causal.Last.Peer==peer.Id;}
                    NavMotionProposal next;
                    if(ownStopped)bodies[i].Active=true;
                    try{next=NavCrowdMotion.Select(bodies,i,neighbors,counts[i],profile,dt,this,unitView[i].Goal,true,bodies[i].Target);}
                    finally{if(ownStopped)bodies[i].Active=false;}
                    if(NavCrowdMotion.CausalActor!=0&&body.Id==NavCrowdMotion.CausalActor)NavCrowdMotion.Causal.RetrySelected=next;
                    // A failed retry must not erase an existing normal turn.
                    // Only a selected useful avoidance turn or translation can
                    // replace it; original Joint/static-no-winner paths remain.
                    if(stalledTurn&&next.Position.Equals(body.Position)&&!next.IndependentTurn)continue;
                    if(!RecastFormationMotion.Allows(hint,route,body.Position,next.Position,false))continue;
                    if(!NavRecastIntervals.Advance(route,preparedStep.RecastStates[i],unitView[i].Location,next.Position,preparedStep.Reservation,out var receipt,this,unitView[i].Id,(hint.Flags&NavFormationFrameHint.PreEntry)!=0?hint.TargetLeg:-1)||!GuidedContinuation(i,receipt))continue;
                    // A stationary turn retains the original failure provenance so the
                    // unchanged apply path still advances genuine no-progress counters.
                    next.JointRefused=old.JointRefused;next.HasJointRefuser=old.HasJointRefuser;next.JointFirstRefuser=old.JointFirstRefuser;
                    if(old.StaticNoWinner&&next.Position.Equals(body.Position))next.StaticNoWinner=true;
                    proposals[i]=next;retried=true;
                    if(NavCrowdMotion.CausalActor!=0&&body.Id==NavCrowdMotion.CausalActor)NavCrowdMotion.Causal.RetryAccepted=true;
                }
            }finally{for(int n=0;n<stoppedCount;n++)bodies[stopped[n]].Active=true;}
            if(!retried)return;
            NavCrowdMotion.ResolveJoint(bodies,proposals,dt);
            for(int i=0;i<bodies.Length;i++)proposals[i].IndependentTurn=false;
            FilterRecastSweeps(dt,bodies,proposals);
        }
        private bool GuidedContinuation(int row,NavRecastSweepReceipt receipt)
        {
            var unit=unitView[row];preparedStep.ComponentLabels[row]=unit.routeIndex;
            if((preparedStep.Formation[row].Flags&NavFormationFrameHint.Guide)==0||unit.localIndex<unit.localRoute.Length||unit.routeIndex>=unit.route.Length)return true;
            // The stored motor is a PHYSICAL continuation preference. The
            // actual displacement and future bank handoff separately retain
            // the temporary directional domain; it must not forbid saving a
            // physically reachable motor before that handoff is executed.
            var moved=new NavLocation(receipt.To,receipt.ToSurface);var route=preparedStep.RecastRoutes[row];var hint=preparedStep.Formation[row];
            if(PhysicalSweep(moved,unit.route[unit.routeIndex],unit.Radius)&&HeldSegmentFree(unit,moved.Position,unit.route[unit.routeIndex])&&NavRecastIntervals.CanTarget(route,receipt.After,moved,unit.routeIndex,preparedStep.Reservation,this,unit.Id)&&ContinuationTraverse(route,receipt.After,moved,unit.route[unit.routeIndex],unit.Radius,preparedStep.Reservation,out _))return true;
            if(!unit.GoalLocation.HasValue)return false;
            for(int candidate=unit.route.Length-1;candidate>=NavRecastIntervals.FirstTargetSurface(route,receipt.After);candidate--){
                if(!NavRecastIntervals.TryContinuationRejoin(route,receipt.After,moved,Array.Empty<NavPoint>(),0,unit.route,candidate,unit.GoalLocation.Value,preparedStep.Reservation,out int same,this,unit.Id,true)||same!=candidate)continue;
                // Component discovery is finished and helper jobs are joined.
                // Reuse that paid scratch for a proposed ACTUAL-state motor
                // index; only the final matching receipt may publish it below.
                preparedStep.ComponentLabels[row]=candidate;return true;
            }
            return false;
        }
        private void FilterRecastSweeps(double dt,NavMotionBody[] bodies,NavMotionProposal[] proposals)
        {
            int PrefixLimit(int row)=>((preparedStep.Formation[row].Flags&NavFormationFrameHint.PreEntry)!=0&&!(bodies[row].Yielding&&bodies[row].YieldLabel>0))?preparedStep.Formation[row].TargetLeg:-1;
            bool refused=false;
            for(int i=0;i<bodies.Length;i++){
                var route=preparedStep.RecastRoutes[i];preparedStep.ComponentLabels[i]=-1;
                if(bodies[i].YieldLabel>=0&&!RecastFormationMotion.Allows(preparedStep.Formation[i],route,bodies[i].Position,proposals[i].Position,bodies[i].Yielding&&bodies[i].YieldLabel>0)){if(bodies[i].Id==NavCrowdMotion.CausalActor)NavCrowdMotion.Causal.PolicyRefused=true;if(NavCrowdMotion.CausalPeerActor!=0&&bodies[i].Id==NavCrowdMotion.CausalPeerActor)NavCrowdMotion.Causal.Peer.PolicyRefused=true;proposals[i].Position=bodies[i].Position;proposals[i].Velocity=default;preparedStep.Formation[i].Flags|=NavFormationFrameHint.PolicyWait;refused=true;}
                if(route==null)continue;
                if(bodies[i].YieldLabel<0){
                    // Producer clearance remains unconditional here. Reconcile a
                    // compatible actual seam sweep without HOLD projection gates;
                    // an incompatible displacement is explicitly unmapped, never
                    // a fabricated cursor/return intent or next-frame exception.
                    preparedStep.RecastReceipts[i]=RecastProductionReceipt(i,proposals[i].Position);continue;
                }
                if(!NavRecastIntervals.Advance(route,preparedStep.RecastStates[i],unitView[i].Location,proposals[i].Position,preparedStep.Reservation,out var receipt,this,unitView[i].Id,PrefixLimit(i))){
                    if(bodies[i].Id==NavCrowdMotion.CausalActor)NavCrowdMotion.Causal.TypedRefused=true;
                    if(NavCrowdMotion.CausalPeerActor!=0&&bodies[i].Id==NavCrowdMotion.CausalPeerActor)NavCrowdMotion.Causal.Peer.TypedRefused=true;
                    proposals[i].Position=bodies[i].Position;proposals[i].Velocity=default;refused=true;
                    if(!NavRecastIntervals.Advance(route,preparedStep.RecastStates[i],unitView[i].Location,bodies[i].Position,preparedStep.Reservation,out receipt,this,unitView[i].Id,PrefixLimit(i)))throw new InvalidOperationException("Recast physical interval binding was lost.");
                }
                if(!bodies[i].Yielding&&!GuidedContinuation(i,receipt)){
                    if(bodies[i].Id==NavCrowdMotion.CausalActor)NavCrowdMotion.Causal.TypedRefused=true;
                    if(NavCrowdMotion.CausalPeerActor!=0&&bodies[i].Id==NavCrowdMotion.CausalPeerActor)NavCrowdMotion.Causal.Peer.TypedRefused=true;
                    var body=bodies[i];var unit=unitView[i];var hint=preparedStep.Formation[i];
                    // The rejected translation supplies no usable heading. Turn
                    // only toward a fully certified current motor target, keeping
                    // the stationary refusal and its original continuation.
                    if(body.Active&&body.OwnMoving&&body.YieldLabel==0&&!unit.Held&&(hint.Flags&NavFormationFrameHint.Guide)!=0&&(hint.Flags&(NavFormationFrameHint.Wait|NavFormationFrameHint.PolicyWait))==0&&unit.localIndex>=unit.localRoute.Length&&unit.routeIndex<unit.route.Length&&unit.GoalLocation.HasValue&&!proposals[i].Position.Equals(body.Position)&&!proposals[i].Velocity.Equals(default(NavPoint))&&
                        NavRecastIntervals.Advance(route,preparedStep.RecastStates[i],unit.Location,body.Target,preparedStep.Reservation,out var targetReceipt,this,unit.Id,PrefixLimit(i))&&
                        NavRecastIntervals.TryContinuationRejoin(route,targetReceipt.After,new NavLocation(targetReceipt.To,targetReceipt.ToSurface),Array.Empty<NavPoint>(),0,unit.route,unit.routeIndex,unit.GoalLocation.Value,preparedStep.Reservation,out int targetIndex,this,unit.Id)&&targetIndex==unit.routeIndex)
                        proposals[i].Heading=NavCrowdMotion.Turn(body.Heading,Math.Atan2(body.Target.Z-body.Position.Z,body.Target.X-body.Position.X),body.TurnSpeed*dt);
                    proposals[i].Position=bodies[i].Position;proposals[i].Velocity=default;refused=true;
                    if(!NavRecastIntervals.Advance(route,preparedStep.RecastStates[i],unitView[i].Location,bodies[i].Position,preparedStep.Reservation,out receipt,this,unitView[i].Id,PrefixLimit(i)))throw new InvalidOperationException("Recast guided continuation refusal invalidated its prior interval.");
                }
                preparedStep.RecastReceipts[i]=receipt;
            }
            // Refusal can create a stationary peer; preserve the existing joint
            // safety fixed point before any physical location is applied.
            if(refused)NavCrowdMotion.ResolveJoint(bodies,proposals,dt);
            for(int i=0;i<bodies.Length;i++)if(preparedStep.RecastRoutes[i]!=null&&bodies[i].YieldLabel<0&&!preparedStep.RecastReceipts[i].To.Equals(proposals[i].Position))preparedStep.RecastReceipts[i]=RecastProductionReceipt(i,proposals[i].Position);
            for(int i=0;i<bodies.Length;i++)if(preparedStep.RecastRoutes[i]!=null&&bodies[i].YieldLabel>=0&&preparedStep.RecastReceipts[i].Verified&&!preparedStep.RecastReceipts[i].To.Equals(proposals[i].Position)){
                if(!NavRecastIntervals.Advance(preparedStep.RecastRoutes[i],preparedStep.RecastStates[i],unitView[i].Location,proposals[i].Position,preparedStep.Reservation,out var receipt,this,unitView[i].Id,PrefixLimit(i)))throw new InvalidOperationException("Joint refusal invalidated Recast interval.");preparedStep.RecastReceipts[i]=receipt;preparedStep.ComponentLabels[i]=-1;
            }
        }
        private NavRecastSweepReceipt RecastProductionReceipt(int row,NavPoint end)
        {
            if(NavRecastIntervals.Advance(preparedStep.RecastRoutes[row],preparedStep.RecastStates[row],unitView[row].Location,end,preparedStep.Reservation,out var receipt))return receipt;
            var prior=preparedStep.RecastStates[row];return new NavRecastSweepReceipt{ProductionUnmapped=true,Before=prior,After=new NavRecastStepState(3,prior.Cursor,prior.Station,prior.HighWater),From=unitView[row].Position,To=end,FromSurface=unitView[row].Location.SurfaceId,ToSurface=end.Equals(unitView[row].Position)?unitView[row].Location.SurfaceId:null,FirstPortalLeg=-1,LastPortalLeg=-1};
        }
        private bool PreserveIndependentAvoidance(double dt,NavMotionBody[] bodies,NavMotionProposal[] proposals,int root)
        {
            var own=bodies[root];own.Speed=own.OwnSpeed;var selected=proposals[root];var actor=unitView[root];
            if(!own.Active||!own.OwnMoving||own.Yielding||own.YieldLabel>0||actor.Held||!actor.CanYield||own.TerminalSeed||!(own.Speed>0)||selected.Overflow||selected.Refuse)return false;
            bool pureTurn=selected.IndependentTurn&&selected.Position.Equals(own.Position)&&selected.Velocity.Equals(default(NavPoint))&&!selected.Heading.Equals(own.Heading);
            bool translated=!selected.Position.Equals(own.Position)&&NavCrowdMotion.Length(selected.Velocity)>0&&NavCrowdMotion.ImprovesGoal(own.Position,selected.Position,actor.Goal);
            if(!pureTurn&&!translated)return false;
            // At an actor's own final endpoint, the selected independent future
            // sweep already certifies useful progress. An unrelated own-forward
            // contact test must not let a queue replace that selected direction.
            if(own.OwnRouteIndex+1==actor.route.Length&&own.Target.Equals(actor.Goal))return true;
            double dx=own.Target.X-own.Position.X,dz=own.Target.Z-own.Position.Z,length=Math.Sqrt(dx*dx+dz*dz);
            static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
            if(!(length>0)||!Finite(length)||!Finite(own.Speed)||!Finite(dt)||!(dt>0))return false;
            // D is the cooperative direction from the CURRENT own waypoint.
            // It is intentionally independent of the bounded heading/sweep.
            double directionX=dx/length,directionZ=dz/length;
            double aim=Math.Atan2(dz,dx),heading=NavCrowdMotion.Turn(own.Heading,aim,own.TurnSpeed*dt),step=Math.Min(own.Speed*dt,length);
            var end=new NavPoint(own.Position.X+Math.Cos(heading)*step,own.Position.Z+Math.Sin(heading)*step);
            // A useful stationary turn must not become a cooperative translation
            // across a seam absent from this actor's accepted corridor. The full
            // sweep filter would refuse that translation and lose the turn on
            // every following tick. Reserve the existing independent choice;
            // all later physical and peer sweep guards still apply to movement.
            if(pureTurn&&preparedStep?.RecastRoutes[root]!=null&&actor.routeIndex<actor.route.Length){
                var route=preparedStep.RecastRoutes[root];var state=preparedStep.RecastStates[root];var r=preparedStep.Reservation;
                // The current motor can be a certified local bypass prefix too.
                // Its turn must survive the same refused cooperative seam step.
                bool targetAllowed=actor.localIndex<actor.localRoute.Length?
                    RecastFormationMotion.DirectionalAllows(preparedStep.Formation[root],route,actor.Position,own.Target)&&NavRecastIntervals.Advance(route,state,actor.Location,own.Target,r,out _,this,actor.Id):
                    NavRecastIntervals.CanTarget(route,state,actor.Location,actor.routeIndex,r,this,actor.Id);
                if(targetAllowed&&
                    !NavRecastIntervals.Advance(route,state,actor.Location,end,r,out _,this,actor.Id))return true;
            }
            if(!geometry.SegmentFree(own.Position,end,own.Radius,PhysicalLayer(actor.Location))||!Traverse(actor.Location,end,own.Radius,out _,actor.Id))return false;
            for(int peer=0;peer<bodies.Length;peer++){
                var receiver=bodies[peer];var other=unitView[peer];
                if(peer==root||!receiver.Active||!receiver.OwnMoving||receiver.Yielding||other.Held||!other.CanYield||other.Team!=actor.Team||receiver.TerminalSeed||!(receiver.Speed>0)||!Finite(receiver.Speed)||!PhysicalSupportContact(actor,other))continue;
                if(receiver.OwnRouteIndex!=other.routeIndex||receiver.OwnLocalIndex!=other.localIndex)continue;
                double radius=own.Radius+receiver.Radius;
                if(SegmentPointDistanceSquared(receiver.Position,own.Position,end)>=radius*radius-1e-9)continue;
                double gx=other.Goal.X-receiver.Position.X,gz=other.Goal.Z-receiver.Position.Z,dot=directionX*gx+directionZ*gz;
                if(Finite(gx)&&Finite(gz)&&Finite(dot)&&dot<0)return true;
            }
            return false;
        }
        // Called only after this queue has passed the existing full certificate.
        // Keep its one agreed speed; independent steering would break the step.
        private void ProposeAcceptedYieldChain(double dt,NavMotionBody[] bodies,NavMotionProposal[] proposals,int[] neighborCounts,int[] queue,int count,double step)
        {
            double speed=step/dt;
            for(int n=0;n<count;n++){
                var body=bodies[queue[n]];double distance=Distance(body.Target,body.Position),bound=Math.Min(step,Math.Min(body.Speed*dt,distance));
                // One full representable coordinate gap on each axis bounds the
                // endpoint-addition roundoff throughout this bounded sweep.
                double margin=YieldCoordinateRoundoff(body.Position.X,bound)+YieldCoordinateRoundoff(body.Position.Z,bound);
                double headroom=bound>margin?YieldSpeedDown(bound-margin):0;
                speed=Math.Min(speed,Math.Min(body.Speed,Math.Min(distance/dt,YieldSpeedDown(headroom/dt))));
            }
            // Fixed passes only: generate once, verify real binary64 bounds, and
            // fail the whole queue closed on an unproved numerical result.
            bool valid=true;
            for(int n=0;n<count;n++){
                int row=queue[n];var body=bodies[row];var next=new NavMotionProposal{Position=body.Position,Heading=body.Heading};
                if(neighborCounts[row]<0){next.Overflow=true;proposals[row]=next;continue;}
                double distance=Distance(body.Target,body.Position),aim=Math.Atan2(body.Target.Z-body.Position.Z,body.Target.X-body.Position.X);
                if(distance>0)next.Heading=NavCrowdMotion.Turn(body.Heading,aim,body.TurnSpeed*dt);
                if(speed>0&&distance>0&&Math.Abs(NavCrowdMotion.Angle(aim-next.Heading))<=profile.ForwardAlignmentAngle){
                    next.Velocity=new NavPoint(Math.Cos(next.Heading)*speed,Math.Sin(next.Heading)*speed);
                    next.Position=new NavPoint(body.Position.X+next.Velocity.X*dt,body.Position.Z+next.Velocity.Z*dt);
                    double actualSpeed=NavCrowdMotion.Length(next.Velocity),actualStep=Distance(next.Position,body.Position),bound=Math.Min(step,Math.Min(body.Speed*dt,distance));
                    valid&=actualSpeed<=body.Speed&&actualSpeed*dt<=step&&actualSpeed*dt<=distance&&actualStep<=bound;
                }
                proposals[row]=next;
            }
            if(!valid)for(int n=0;n<count;n++){int row=queue[n];proposals[row].Position=bodies[row].Position;proposals[row].Velocity=default;}
            for(int n=0;n<count;n++){
                int row=queue[n];var body=bodies[row];var next=proposals[row];
                if(!next.Position.Equals(body.Target)&&!next.Position.Equals(body.Position)&&(!geometry.SegmentFree(body.Position,next.Position,body.Radius,PhysicalLayer(unitView[row].Location))||!Traverse(unitView[row].Location,next.Position,body.Radius,out _,unitView[row].Id)||!UnitFreeYield(unitView[row],bodies,body.Position,next.Position))){next.Position=body.Position;next.Velocity=default;next.Blocked=true;proposals[row]=next;}
            }
        }
        private static double YieldSpeedDown(double value)=>value>0?BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(value)-1):0;
        private static double YieldCoordinateRoundoff(double position,double reach)
        {double magnitude=Math.Abs(position)+reach;return double.IsInfinity(magnitude)?double.PositiveInfinity:BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(magnitude)+1)-magnitude;}
        private bool UnitFreeYield(NavUnit self,NavMotionBody[] bodies,NavPoint from,NavPoint to)
        {
            double radius=self.Radius,maxRadius=maximumRadius;int selfRow=-1;for(int i=0;i<bodies.Length;i++)if(bodies[i].Id==self.Id){selfRow=i;break;}
            int label=selfRow>=0?bodies[selfRow].YieldLabel:0;int reach=(int)Math.Ceiling((radius+maxRadius+Math.Sqrt((to.X-from.X)*(to.X-from.X)+(to.Z-from.Z)*(to.Z-from.Z))+profile.GridCell)/profile.GridCell),cx=Cell(to.X),cz=Cell(to.Z);
            for(int x=cx-reach;x<=cx+reach;x++)for(int z=cz-reach;z<=cz+reach;z++)if(spatial!=null)for(int i=spatial.First(Key(x,z));i>=0;i=spatial.Next(i)){
                var other=unitView[i];if(other==self)continue;int otherRow=-1;for(int j=0;j<bodies.Length;j++)if(bodies[j].Id==other.Id){otherRow=j;break;}if(label>0&&otherRow>=0&&bodies[otherRow].YieldLabel==label)continue;
                double distance=SegmentPointDistanceSquared(other.Position,from,to);if(distance<(radius+other.Radius)*(radius+other.Radius)-1e-9)return false;
            }return true;
        }
        private bool Clear(NavUnit unit,NavPoint from,NavPoint to){return geometry.SegmentFree(from,to,unit.Radius,PhysicalLayer(unit.Location))&&TerrainClear(unit,from,to)&&UnitFree(unit,from,to);}
        internal bool Locate(NavPoint point,double radius,out NavLocation location)
        {location=default;if(double.IsNaN(point.X)||double.IsInfinity(point.X)||double.IsNaN(point.Z)||double.IsInfinity(point.Z))return false;if(terrain!=null){if(!terrain.TryLocate(point,radius,out location))return false;location=OwnedLocation(location);return true;}location=new NavLocation(point,NavLocation.FlatSurface);return geometry.IsFree(point,radius);}
        internal bool ValidLocation(NavLocation location,double radius)=>terrain==null?location.SurfaceId==NavLocation.FlatSurface&&Math.Abs(location.Position.X)+radius<=geometry.HalfExtent&&Math.Abs(location.Position.Z)+radius<=geometry.HalfExtent:terrain.IsValidLocation(location,radius);
        internal bool Traverse(NavLocation from,NavPoint to,double radius,out NavLocation location,int actor=0)
        {
            if(actor!=0&&terrain is IRecastPhysicalTerrain physical&&preparedStep!=null){int row=preparedStep.Find(actor);if(row>=0&&preparedStep.RecastRoutes[row]!=null){if(!physical.TraverseAccepted(from,to,radius,preparedStep.RecastRoutes[row],preparedStep.RecastStates[row].Cursor,out location))return false;location=OwnedLocation(location);return true;}}
            if(terrain!=null){if(!terrain.TryTraverse(from,to,radius,out location))return false;location=OwnedLocation(location);return true;}location=new NavLocation(to,NavLocation.FlatSurface);return from.SurfaceId==NavLocation.FlatSurface&&PhysicalSweep(from,to,radius);}
        internal bool ContinuationTraverse(NavTypedRoute route,NavRecastStepState state,NavLocation from,NavPoint to,double radius,INavStorageReservation r,out NavLocation location)
        {
            if(!(terrain is IRecastPhysicalTerrain))return Traverse(from,to,radius,out location);
            if(!((INavLayerSweepAuthority)this).TraceLayerSweep(route.Legs[0].RecastAuthorityProvider,from,to,radius,route,state.Cursor,r,out location,out var trace))return false;
            try{int cursor=state.Cursor;foreach(var leg in trace)if(leg.Kind==NavTypedLegKind.Portal&&!RecastLayerTrace.MatchPortal(route,ref cursor,leg.PortalId,leg.From.SurfaceId,leg.To.SurfaceId))return false;return true;}
            finally{(trace as NavOwnedStorage)?.ReleaseOwner();}
        }
        // A cached immutable typed suffix never caches live HOLD/geometry denial.
        internal bool RecheckContinuationSuffix(int actor,NavPoint from,IReadOnlyList<NavPoint> global,int first,NavPoint goal,double radius)
        {
            int row=preparedStep==null?ActorIndex(actor):preparedStep.Find(actor);if(row<0)return false;var unit=unitView[row];
            for(int i=first;i<global.Count;i++){
                var to=global[i];if(!geometry.SegmentFree(from,to,radius)||!HeldSegmentFree(unit,from,to))return false;from=to;
            }
            return geometry.SegmentFree(from,goal,radius)&&HeldSegmentFree(unit,from,goal);
        }
        private bool ClearFrom(NavUnit unit,NavLocation from,NavPoint to,out NavLocation location)
        {location=default;return PhysicalSweep(from,to,unit.Radius)&&UnitFree(unit,from.Position,to)&&Traverse(from,to,unit.Radius,out location,unit.Id);}
        private bool TerrainClear(NavUnit unit,NavPoint from,NavPoint to)
        {return Traverse(unit.Location,from,unit.Radius,out var start,unit.Id)&&Traverse(start,to,unit.Radius,out _,unit.Id);}
        internal bool CompatibleArrival(NavLocation actual,NavLocation assigned,double radius)
        {return ValidLocation(actual,radius)&&ValidLocation(assigned,radius)&&(actual.SurfaceId==assigned.SurfaceId||Traverse(actual,assigned.Position,radius,out var end)&&end.Equals(assigned));}
        private static NavPoint CurrentMotionTarget(NavUnit unit,NavFormationFrameHint hint)
        {
            if(unit.localIndex<unit.localRoute.Length)return unit.localRoute[unit.localIndex];
            return (hint.Flags&NavFormationFrameHint.Guide)!=0?hint.Target:unit.route[unit.routeIndex];
        }
        internal bool CanReuseRepairCertificate(int row,NavUnit unit,NavTypedRoute typed)
        {
            // Working headers deliberately have no StorageOwner. Their immutable
            // global backing is retained by the prepared frame's explicit pin.
            if(preparedStep==null||row<0||!ReferenceEquals(unitView[row],unit)||!preparedStep.AllowRepairCertificateReuse||!StableSelectClearance||typed==null||
                !ReferenceEquals(preparedStep.RecastRoutes[row],typed)||!typed.IsImmutableCaptureRoot)return false;
            var global=preparedStep.Globals[row];
            return global!=null&&global.IsImmutableCaptureRoot&&ReferenceEquals(global.ReadOnlyBacking,unit.route)&&
                typed.Legs[0].RecastAuthorityProvider is PartitionNavigationProvider provider&&provider.StorageRoot!=null&&provider.StorageRoot.IsImmutableCaptureRoot&&
                preparedStep.Reservation is INavStorageLedgerIdentity ledger&&ledger.Owns(typed)&&ledger.Owns(global)&&ledger.Owns(provider.StorageRoot);
        }
        private bool CommitDetour(NavUnit unit,NavPoint target,ref NavLocalRepairWorkspace workspace)
        {
            long clock=Stopwatch.GetTimestamp();double cell=profile.LocalGridCell;
            int row=preparedStep?.Find(unit.Id)??-1;var typed=row>=0?preparedStep.RecastRoutes[row]:null;
            var hint=row>=0?preparedStep.Formation[row]:default;int connector=unit.routeIndex;bool recovering=typed!=null&&(hint.Flags&(NavFormationFrameHint.Directional|NavFormationFrameHint.Bound))==NavFormationFrameHint.Directional;
            if(typed!=null){
                var binding=typed.Subscription;
                if(unit.Held||!unit.GoalLocation.HasValue||binding.Provenance!=NavRouteProvenance.CommandMember||binding.EntityId!=unit.Id||binding.Incarnation!=unit.Incarnation||binding.MobilityRevision!=unit.MobilityRevision||binding.RequestSequence!=unit.InstalledRequest||binding.GroupId!=unit.InstalledGroupId||binding.ActivationRevision!=unit.InstalledRevision||binding.MemberOrderRevision!=unit.InstalledRevision||!binding.AssignedEndpoint.Equals(unit.GoalLocation.Value))return false;
            }
            int obstructedCursor=-1;
            var prior=row>=0&&preparedStep.RecastReceipts[row].Verified?preparedStep.RecastReceipts[row].After:row>=0?preparedStep.RecastStates[row]:default;
            // The same selected local/guide/motor target owns both steering and
            // recovery. The final endpoint remains the full typed/HOLD suffix;
            // it is not a competing short-step progress objective.
            if(typed!=null){
                target=CurrentMotionTarget(unit,hint);
                // A live body can occupy a previously certified local corner.
                // Propose a replacement toward the still-owned guide, retaining
                // the old prefix unless a complete typed/HOLD repair certifies.
                bool occupied=false;
                if(unit.localIndex<unit.localRoute.Length&&(hint.Flags&NavFormationFrameHint.Guide)!=0&&
                    (hint.Flags&(NavFormationFrameHint.Wait|NavFormationFrameHint.PolicyWait))==0&&
                    Traverse(unit.Location,target,unit.Radius,out var localAt,unit.Id)){
                    for(int n=0;n<unitView.Count;n++){
                        var peer=unitView[n];if(peer.Id==unit.Id||peer.Location.SurfaceId!=localAt.SurfaceId)continue;
                        double x=peer.Position.X-target.X,z=peer.Position.Z-target.Z,occupancyRadius=peer.Radius+unit.Radius;
                        if(x*x+z*z<occupancyRadius*occupancyRadius-1e-9){occupied=true;break;}
                    }
                }
                if(occupied&&RecastFormationMotion.DirectionalAllows(hint,typed,unit.Position,hint.Target)&&
                    NavRecastIntervals.Advance(typed,prior,unit.Location,hint.Target,preparedStep.Reservation,out _,this,unit.Id))target=hint.Target;
                // An occupied intermediate motor is not a compulsory physical
                // rendezvous. Rejoining before that point merely repeats the
                // same obstruction. The bounded prefix may rejoin beyond the
                // occupied motors only after the unchanged full typed/HOLD
                // endpoint suffix qualifies; installed cursors stay untouched
                // until that complete certificate exists. STOP remains yieldable.
                if(unit.localIndex>=unit.localRoute.Length&&
                    (hint.Flags&(NavFormationFrameHint.Guide|NavFormationFrameHint.Wait|NavFormationFrameHint.PolicyWait))==0&&target.Equals(unit.route[unit.routeIndex])){
                    for(int cursor=unit.routeIndex;cursor+1<unit.route.Length;cursor++){
                        var motor=unit.route[cursor];
                        if(!Traverse(unit.Location,motor,unit.Radius,out var motorAt,unit.Id))break;
                        bool motorOccupied=false;
                        for(int n=0;n<unitView.Count;n++){
                            var peer=unitView[n];if(peer.Id==unit.Id||peer.Moving&&!peer.Held||peer.Location.SurfaceId!=motorAt.SurfaceId)continue;
                            double x=peer.Position.X-motor.X,z=peer.Position.Z-motor.Z,blockedRadius=peer.Radius+unit.Radius;
                            if(x*x+z*z<blockedRadius*blockedRadius-1e-9){motorOccupied=true;break;}
                        }
                        if(!motorOccupied)break;
                        obstructedCursor=cursor;target=unit.route[cursor+1];
                    }
                }
            }
            bool currentObjective=typed!=null;
            bool directionalContinuation=recovering||currentObjective&&(hint.Flags&NavFormationFrameHint.Directional)!=0;
            bool pure=StableSelectClearance;
            var domain=pure?new RecastFormationMotion.PreparedDomain(hint,typed):default;
            var suffixMemo=default(NavRecastIntervals.RepairSuffixMemo);
            if(CanReuseRepairCertificate(row,unit,typed))
                suffixMemo=new NavRecastIntervals.RepairSuffixMemo(typed,unit.route,unit.GoalLocation.Value,preparedStep.Reservation,this,unit.Id);

            bool Connector(NavLocation from,int candidate){
                // Cheap interval/fan screening before any full-prefix allocation.
                // Physical clearance alone cannot admit a terminal waypoint early.
                if(candidate<=obstructedCursor||candidate<NavRecastIntervals.FirstTargetSurface(typed,prior))return false;
                int leg=-1,seen=0;for(int n=0;n<typed.Legs.Count;n++)if(typed.Legs[n].Kind==NavTypedLegKind.Surface&&seen++==candidate){leg=n;break;}
                if(leg<0){if(candidate!=seen)return false;leg=typed.Legs.Count-1;}
                if(leg>=typed.RecastPlan.TerminalLeg&&prior.Phase!=2&&!NavRecastIntervals.InTerminalFan(typed,from))return false;
                return (!directionalContinuation||domain.DirectionalAllows(hint,typed,from.Position,unit.route[candidate]))&&ClearFrom(unit,from,unit.route[candidate],out _);
            }
            // A role cap is frame-local: it guards the actual dt sweep below,
            // not every future edge of a certified avoidance route. Planning
            // keeps directional/Wait, physical peers, HOLD and the full typed
            // suffix. Installation grants no motion past the live role cap.
            var repairHint=hint;repairHint.Flags&=unchecked((byte)~NavFormationFrameHint.Bound);
            bool Edge(NavLocation from,NavPoint to,out NavLocation reached){reached=default;return (typed==null||domain.Allows(repairHint,typed,from.Position,to))&&ClearFrom(unit,from,to,out reached);}

            int radius=(int)Math.Ceiling(profile.LocalDetourDistance/cell);
            if(workspace==null)workspace=preparedStep!=null?NavLocalRepairWorkspace.Create(radius,preparedStep.Reservation.TryReserve,ownedSurfaceNames as NavOwnedStorage):StorageSource==null?NavLocalRepairWorkspace.Create(radius,null,ownedSurfaceNames as NavOwnedStorage):StorageSource.CreateRepairWorkspace(radius,ownedSurfaceNames as NavOwnedStorage);
            workspace.Reset();var nodes=workspace.Nodes;var open=workspace.Open;
            int start=workspace.Index(0,0);nodes[start]=new NavLocalRepairWorkspace.Node{Seen=true,Location=unit.Location,Parent=-1};open[workspace.OpenCount++]=start;
            int best=-1,partial=-1,rejoin=unit.routeIndex,suffixFirst=unit.routeIndex;double bestDistance=Distance(unit.Position,target),goalDistance=Distance(unit.Position,unit.Goal);
            // A held footprint invalidates every suffix containing that edge.
            // Moving peers still constrain only the local prefix and connector.
            if(typed!=null)suffixFirst=NavRecastIntervals.FirstTargetSurface(typed,prior);
            if(typed!=null)for(int n=unit.route.Length-2;n>=suffixFirst;n--)if(!HeldSegmentFree(unit,unit.route[n],unit.route[n+1])){suffixFirst=n+1;break;}
            var candidateScratch=workspace;
            int prefixNode=-1,prefixCount=0;bool prefixValid=false;NavLocation prefixAnchor=default;

            bool BuildPrefix(int node,out NavLocation anchor){
                if(pure&&prefixNode==node){candidateScratch.RouteCount=prefixCount;anchor=prefixAnchor;return prefixValid;}
                prefixNode=node;prefixValid=false;prefixCount=0;prefixAnchor=default;
                anchor=unit.Location;
                candidateScratch.RouteCount=0;
                int count=0;for(int n=node;n!=start;n=nodes[n].Parent)candidateScratch.Reversed[count++]=new NavPoint(unit.Position.X+nodes[n].X*cell,unit.Position.Z+nodes[n].Z*cell);
                Array.Reverse(candidateScratch.Reversed,0,count);
                int index=0;
                while(index<count){int far=index;for(int j=index+1;j<count;j++)if(Edge(anchor,candidateScratch.Reversed[j],out _))far=j;if(!Edge(anchor,candidateScratch.Reversed[far],out anchor)){candidateScratch.RouteCount=0;return false;}candidateScratch.Smoothed[candidateScratch.RouteCount++]=candidateScratch.Reversed[far];index=far+1;}
                if(candidateScratch.RouteCount==0||Distance(unit.Position,candidateScratch.Smoothed[0])<=profile.ArrivalTolerance){candidateScratch.RouteCount=0;return false;}
                prefixValid=true;prefixCount=candidateScratch.RouteCount;prefixAnchor=anchor;return true;
            }
            bool completeDomainProof=pure&&directionalContinuation&&domain.IsPrepared&&CanReuseRepairCertificate(row,unit,typed);
            int firstDirectionalSuffix=unit.route.Length;
            if(completeDomainProof&&unit.route.Length>0&&domain.DirectionalAllows(hint,typed,unit.route[unit.route.Length-1],unit.Goal)){
                firstDirectionalSuffix=0;
                for(int index=unit.route.Length-2;index>=0;index--)if(!domain.DirectionalAllows(hint,typed,unit.route[index],unit.route[index+1])){firstDirectionalSuffix=index+1;break;}
            }
            int checkedDomainNode=-1;bool noDirectionalRejoin=false;
            bool CompleteCandidate(int node,int candidate,ref NavRecastIntervals.RepairSuffixMemo memo){
                if(!BuildPrefix(node,out var anchor))return false;
                if(completeDomainProof){
                    // A successful legacy completion necessarily has ONE such
                    // endpoint. Include every index, even post-prefix/fan
                    // fallbacks. The last forbidden fixed suffix edge rules out
                    // exactly its preceding indices; later tails are all valid.
                    // If any connector is possible, keep the old search and
                    // exact selected cursor unchanged; only an empty set prunes.
                    if(checkedDomainNode!=node){checkedDomainNode=node;noDirectionalRejoin=true;for(int index=firstDirectionalSuffix;index<unit.route.Length;index++)if(domain.DirectionalAllows(hint,typed,anchor.Position,unit.route[index])){noDirectionalRejoin=false;break;}}
                    if(noDirectionalRejoin){candidateScratch.RouteCount=0;return false;}
                }
                // A complete repair must be executable under the same passage
                // direction after its prefix. A merely typed motor suffix can
                // return through counterflow and pull against the guide again.
                // If none qualifies, retain the existing strict guide-progress
                // partial search and its full typed/HOLD endpoint certificate.
                if(directionalContinuation&&!domain.DirectionalAllows(hint,typed,anchor.Position,unit.route[candidate])||!ClearFrom(unit,anchor,unit.route[candidate],out _)||
                    !NavRecastIntervals.TryContinuationRejoin(typed,prior,unit.Location,candidateScratch,0,unit.route,candidate,unit.GoalLocation.Value,preparedStep.Reservation,out int selected,this,ref memo,unit.Id)||selected<=obstructedCursor||selected>=unit.route.Length||directionalContinuation&&!domain.TailAllows(hint,typed,anchor.Position,unit.route,selected,unit.Goal)||!ClearFrom(unit,anchor,unit.route[selected],out _)){candidateScratch.RouteCount=0;return false;}
                rejoin=selected;return true;
            }
            while(workspace.OpenCount>0){
                int selected=0;for(int i=1;i<workspace.OpenCount;i++)if(nodes[open[i]].Score<nodes[open[selected]].Score-1e-9||(Math.Abs(nodes[open[i]].Score-nodes[open[selected]].Score)<=1e-9&&nodes[open[i]].Side<nodes[open[selected]].Side))selected=i;
                int currentIndex=open[selected];Array.Copy(open,selected+1,open,selected,workspace.OpenCount-selected-1);workspace.OpenCount--;
                if(nodes[currentIndex].Closed)continue;nodes[currentIndex].Closed=true;var current=nodes[currentIndex];
                var point=new NavPoint(unit.Position.X+current.X*cell,unit.Position.Z+current.Z*cell);
                double remaining=Distance(point,target);
                if(typed==null){
                    if(currentIndex!=start&&remaining<bestDistance){best=currentIndex;bestDistance=remaining;}
                    if(currentIndex!=start&&ClearFrom(unit,current.Location,target,out _)){best=currentIndex;break;}
                }else if(currentIndex!=start){
                    // Preserve safe net progress even when moving traffic has no
                    // currently clear complete connector. Directional bank alignment
                    // may temporarily move away from the assigned final endpoint;
                    // its certified guide is the progress objective only here.
                    // The entire unchanged endpoint suffix is qualified once,
                    // only after the complete-goal search is exhausted.
                    if(remaining<bestDistance&&(currentObjective||Distance(point,unit.Goal)<goalDistance)&&Distance(unit.Position,point)>profile.ArrivalTolerance){partial=currentIndex;bestDistance=remaining;}
                    // A closer point is not a completed repair. Connect to a
                    // live-peer-clear accepted suffix before certifying the prefix.
                    connector=-1;
                    if(unit.routeIndex>=suffixFirst&&unit.routeIndex<unit.route.Length&&Connector(current.Location,unit.routeIndex)&&CompleteCandidate(currentIndex,unit.routeIndex,ref suffixMemo))connector=unit.routeIndex;
                    if(connector<0&&!(checkedDomainNode==currentIndex&&noDirectionalRejoin))for(int candidate=unit.route.Length-1;candidate>=suffixFirst&&!(checkedDomainNode==currentIndex&&noDirectionalRejoin);candidate--)if(candidate!=unit.routeIndex&&Connector(current.Location,candidate)&&CompleteCandidate(currentIndex,candidate,ref suffixMemo)){connector=candidate;break;}
                    // Only a certified complete prefix/suffix terminates search.
                    // Ordinary rejection leaves the paid grid/parents intact.
                    if(connector>=0){best=currentIndex;break;}
                }
                for(int ox=-1;ox<=1;ox++)for(int oz=-1;oz<=1;oz++){
                    if(ox==0&&oz==0)continue;int x=current.X+ox,z=current.Z+oz;
                    if(x*x+z*z>radius*radius)continue;
                    var nextPoint=new NavPoint(unit.Position.X+x*cell,unit.Position.Z+z*cell);
                    if(!Edge(current.Location,nextPoint,out var nextLocation))continue;
                    int key=workspace.Index(x,z);double cost=current.Cost+Distance(point,nextPoint);
                    if(nodes[key].Seen){if(nodes[key].Closed||cost>=nodes[key].Cost)continue;}
                    else{nodes[key].Seen=true;nodes[key].X=x;nodes[key].Z=z;open[workspace.OpenCount++]=key;}
                    nodes[key].Location=nextLocation;nodes[key].Cost=cost;nodes[key].Score=cost+Distance(nextPoint,target);nodes[key].Side=(target.X-unit.Position.X)*z-(target.Z-unit.Position.Z)*x;nodes[key].Parent=currentIndex;
                }
                // A near-contact start can have a narrow clear escape between
                // all eight lattice directions. Connect that physical start to
                // visible EXISTING cells, only if its ordinary expansion failed.
                // Same paid circle/nodes and full edge guards; no larger motion
                // step or counter credit is granted by a planning connector.
                if(typed!=null&&currentIndex==start&&workspace.OpenCount==0)for(int x=-radius;x<=radius;x++)for(int z=-radius;z<=radius;z++){
                    if(x*x+z*z>radius*radius||Math.Abs(x)<=1&&Math.Abs(z)<=1)continue;
                    var nextPoint=new NavPoint(unit.Position.X+x*cell,unit.Position.Z+z*cell);
                    if(!Edge(unit.Location,nextPoint,out var nextLocation))continue;
                    int key=workspace.Index(x,z);double cost=Distance(unit.Position,nextPoint);
                    nodes[key]=new NavLocalRepairWorkspace.Node{Seen=true,Location=nextLocation,X=x,Z=z,Cost=cost,Score=cost+Distance(nextPoint,target),Side=(target.X-unit.Position.X)*z-(target.Z-unit.Position.Z)*x,Parent=start};open[workspace.OpenCount++]=key;
                }
            }
            if(best<0&&typed!=null&&partial>=0&&BuildPrefix(partial,out _)&&
                NavRecastIntervals.TryContinuationRejoin(typed,prior,unit.Location,candidateScratch,0,unit.route,Math.Max(unit.routeIndex,obstructedCursor+1),unit.GoalLocation.Value,preparedStep.Reservation,out int progressRejoin,this,ref suffixMemo,unit.Id)&&progressRejoin>obstructedCursor){
                // This certifies the whole typed/HOLD suffix, not present moving
                // peer clearance beyond the safe prefix. Arrival stays physical.
                best=partial;rejoin=progressRejoin;
            }
            if(best<0){RepairFailed++;RepairCpuMs+=(Stopwatch.GetTimestamp()-clock)*1000d/Stopwatch.Frequency;return false;}
            if(typed==null){
                int count=0;for(int n=best;n!=start;n=nodes[n].Parent)workspace.Reversed[count++]=new NavPoint(unit.Position.X+nodes[n].X*cell,unit.Position.Z+nodes[n].Z*cell);
                Array.Reverse(workspace.Reversed,0,count);
                // Preserve the original untyped smoothing/fallback semantics.
                var anchorLocation=unit.Location;int index=0;
                while(index<count){int far=index;for(int j=index+1;j<count;j++)if(ClearFrom(unit,anchorLocation,workspace.Reversed[j],out _))far=j;if(!ClearFrom(unit,anchorLocation,workspace.Reversed[far],out anchorLocation))return false;workspace.Smoothed[workspace.RouteCount++]=workspace.Reversed[far];index=far+1;}
            }
            if(typed!=null&&unit.localIndex<unit.localRoute.Length&&unit.localRoute.Length-unit.localIndex==workspace.RouteCount){
                bool same=true;for(int n=0;n<workspace.RouteCount;n++)if(!unit.localRoute[unit.localIndex+n].Equals(workspace.Smoothed[n])){same=false;break;}
                if(same){
                    RepairCpuMs+=(Stopwatch.GetTimestamp()-clock)*1000d/Stopwatch.Frequency;
                    if(rejoin==unit.routeIndex)return false;
                    // Requalifying a different accepted suffix retains the useful
                    // prefix and its index; it grants no install/progress credit.
                    unit.routeIndex=rejoin;return true;
                }
            }
            if(preparedStep!=null)preparedStep.ReplaceLocal(unit,workspace);
            else if(unit.StorageOwner!=null){var next=StorageSource.CopyRoute(workspace,null);var retired=unit.StorageOwner.Local;unit.StorageOwner.Local=next;unit.localRoute=next.ReadOnlyBacking;retired?.Release();}
            else{unit.localRoute=new NavPoint[workspace.RouteCount];Array.Copy(workspace.Smoothed,unit.localRoute,workspace.RouteCount);}
            unit.localIndex=0;unit.routeIndex=rejoin;RepairCount++;RepairCpuMs+=(Stopwatch.GetTimestamp()-clock)*1000d/Stopwatch.Frequency;return true;
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
            if(spatial==null)return true;
            radius=self?.Radius??radius;
            double pad=radius+maximumRadius+profile.GridCell;
            int left,right,bottom,top;
            if(self==null){
                // Keep placement queries on their original index neighbourhood.
                int reach=(int)Math.Ceiling(pad/profile.GridCell);
                int x=Cell(to.X),z=Cell(to.Z);
                left=x-reach;right=x+reach;bottom=z-reach;top=z+reach;
            }else{
                // A peer can touch the swept disc only inside the padded segment bounds.
                left=Cell(Math.Min(from.X,to.X)-pad);right=Cell(Math.Max(from.X,to.X)+pad);
                bottom=Cell(Math.Min(from.Z,to.Z)-pad);top=Cell(Math.Max(from.Z,to.Z)+pad);
                if(((double)right-left+1)*((double)top-bottom+1)>unitView.Count){
                    for(int i=0;i<unitView.Count;i++)if(Intersects(unitView[i]))return false;
                    return true;
                }
            }
            for(int x=left;x<=right;x++)for(int z=bottom;z<=top;z++)
                for(int i=spatial.First(Key(x,z));i>=0;i=spatial.Next(i))
                    if(Intersects(unitView[i]))return false;

            bool Intersects(NavUnit other)
            {
                if(other==self)return false;
                NeighborCandidates++;
                double limit=radius+other.Radius;
                return SegmentPointDistanceSquared(other.Position,from,to)<limit*limit-.000000001d;
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
