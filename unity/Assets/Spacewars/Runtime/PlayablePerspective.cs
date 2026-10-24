using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
#if UNITY_INCLUDE_TESTS
    internal enum PlayableRouteProjectionReadbackOutcome
    {
        BindingRejected, TargetUnresolved, TargetMismatch, OriginOrTargetNotVisible,
        NoCandidateGenerated, AllCandidatesInvisible, AllCandidatesRejected, ProofPublished
    }
    internal enum PlayableRouteProjectionCandidateOutcome { Invisible, RouterReturnedEmpty, UnsafeSegment, ProofPublished }
    internal sealed class PlayableRouteProjectionCandidateReadback
    {
        internal PlayableRouteProjectionCandidateReadback(int index,NavPoint candidate,bool? visible,
            PlayableRouteProjectionCandidateOutcome outcome,NavPoint[] path,int failedSegmentIndex,
            NavPoint? failedFrom,NavPoint? failedTo,bool? authoritativeSegmentFree,bool? visibleSegment)
        {
            Index=index;Candidate=candidate;Visible=visible;Outcome=outcome;
            Path=Array.AsReadOnly((NavPoint[])(path??Array.Empty<NavPoint>()).Clone());
            FailedSegmentIndex=failedSegmentIndex;FailedFrom=failedFrom;FailedTo=failedTo;
            AuthoritativeSegmentFree=authoritativeSegmentFree;VisibleSegment=visibleSegment;
        }
        internal int Index{get;} internal NavPoint Candidate{get;} internal bool? Visible{get;}
        internal PlayableRouteProjectionCandidateOutcome Outcome{get;} internal IReadOnlyList<NavPoint> Path{get;}
        internal int FailedSegmentIndex{get;} internal NavPoint? FailedFrom{get;} internal NavPoint? FailedTo{get;}
        internal bool? AuthoritativeSegmentFree{get;} internal bool? VisibleSegment{get;}
    }
    internal sealed class PlayableRouteProjectionReadback
    {
        internal PlayableRouteProjectionReadback(string ownerId,PlayableOwner owner,PlayableRouteRequest request,
            PlayableRouteProjectionReadbackOutcome outcome,string bindingRejection,NavPoint? resolvedTarget,
            bool? originVisible,bool? targetVisible,int observedGeometryRevision,int observedObstacleCount,
            int candidateCount,PlayableRouteProjectionCandidateReadback[] candidates)
        {
            OwnerId=ownerId;Owner=owner;UnitId=request.UnitId;Kind=request.Kind;TargetId=request.TargetId;
            Generation=request.Generation;Tick=request.Tick;Origin=request.Origin;Radius=request.Radius;RequestedTarget=request.Target;
            Outcome=outcome;BindingRejection=bindingRejection;ResolvedTarget=resolvedTarget;
            OriginVisible=originVisible;TargetVisible=targetVisible;ObservedGeometryRevision=observedGeometryRevision;
            ObservedObstacleCount=observedObstacleCount;CandidateCount=candidateCount;
            Candidates=Array.AsReadOnly((PlayableRouteProjectionCandidateReadback[])(candidates??Array.Empty<PlayableRouteProjectionCandidateReadback>()).Clone());
        }
        internal string OwnerId{get;} internal PlayableOwner Owner{get;} internal int UnitId{get;}
        internal PlayableRouteTargetKind Kind{get;} internal int TargetId{get;} internal long Generation{get;} internal long Tick{get;}
        internal NavPoint Origin{get;} internal double Radius{get;} internal NavPoint RequestedTarget{get;}
        internal PlayableRouteProjectionReadbackOutcome Outcome{get;} internal string BindingRejection{get;}
        internal NavPoint? ResolvedTarget{get;} internal bool? OriginVisible{get;} internal bool? TargetVisible{get;}
        internal int ObservedGeometryRevision{get;} internal int ObservedObstacleCount{get;} internal int CandidateCount{get;}
        internal IReadOnlyList<PlayableRouteProjectionCandidateReadback> Candidates{get;}
    }
#endif
    internal sealed partial class PlayableDomain
    {
#if UNITY_INCLUDE_TESTS
        // A test-only, per-domain observer. It is absent from non-test runtime builds and is
        // never written to authority state or serialized checkpoints.
        internal Action<PlayableRouteProjectionReadback> RouteProjectionReadbackForTests;
        internal Action<string,long,PlayableRouteRequest[]> RouteRequestsReadbackForTests;
        internal string RouteProjectionOwnerFilterForTests;
        internal bool CaptureCurrentExpansionRoutesForTests;

        private void PublishRouteProjectionReadback(Action<PlayableRouteProjectionReadback> observer,int ownerIndex,
            PlayableOwner owner,PlayableRouteRequest request,PlayableRouteProjectionReadbackOutcome outcome,string bindingRejection,
            NavPoint? resolvedTarget,bool? originVisible,bool? targetVisible,int geometryRevision,int obstacleCount,int candidateCount,
            List<PlayableRouteProjectionCandidateReadback> candidates)
        {
            observer(new PlayableRouteProjectionReadback(offline.Roster[ownerIndex].Id,owner,request,outcome,bindingRejection,
                resolvedTarget,originVisible,targetVisible,geometryRevision,obstacleCount,candidateCount,candidates.ToArray()));
        }
#endif
        private static readonly bool routePerformanceTrace=Array.IndexOf(Environment.GetCommandLineArgs(),"-routePerformanceEvidence")>=0;
        private readonly Dictionary<int,PlayableVision> visions=new Dictionary<int,PlayableVision>();
        private PlayableVision Vision(PlayableOwner owner)
        {
            if(!visions.TryGetValue(TeamOf(owner),out var v))visions.Add(TeamOf(owner),v=new PlayableVision(TeamOf(owner),profile.ArenaHalfExtent,profile.ArenaHalfExtent,profile.VisionCellSize,profile.FogEdgeFeather));
            return v;
        }
        private IEnumerable<VisionSource> VisionSources(PlayableOwner owner)
        {
            if(offline?.ProvingGround!=null){yield return new VisionSource(default(NavPoint),profile.ArenaHalfExtent*2);yield break;}
            foreach(var u in units.Values)if(!Hostile(u.Owner,owner)&&u.Health>0&&TryActor(u.Id,out var n))yield return new VisionSource(n.Position,PlayableUnitRules.Vision(profile,u.Kind));
            foreach(var b in buildings.Values)if(!Hostile(b.Owner,owner))
            {
                double radius=PlayableVision.BuildingRadius(profile,b.Kind,b.Phase);
                if(radius>0)yield return new VisionSource(b.Position,radius);
            }
        }
        private void RefreshVision()
        {
            using var measured=PerformanceProfile.Measure(ProfileStage.Vision);
            var actual=buildings.Values.Where(b=>b.Phase!=ConstructionPhase.Pending).Select(b=>new KnownBuilding(b.Id,TeamOf(b.Owner),b.Owner,b.Kind,b.Position,b.Heading,b.Upgrade?.Complete==true)).ToArray();
            foreach(PlayableOwner owner in Owners)Vision(owner).Refresh(VisionSources(owner),actual);
        }
        private bool VisibleTarget(PlayableOwner observer,int id)
        {
            if(!Target(id,out var pos,out _))return false;
            var owner=units.TryGetValue(id,out var unit)?unit.Owner:buildings[id].Owner;
            return Hostile(owner,observer)&&Vision(observer).IsVisible(pos);
        }
        // Only this projection may cross the player presentation boundary. Snapshot() is
        // an internal authority/test view and never published by PlayableRuntime.
        internal PlayableSnapshot PlayerSnapshot(long sequence,RuntimeStatus status,bool paused,PlayableRuntimeMetrics metrics,string failure,int seed,PlayableOwner owner=PlayableOwner.Player)
            =>PlayerSnapshotWithRoutes(sequence,status,paused,metrics,failure,seed,owner,null);
        internal PlayableSnapshot PlayerSnapshotWithRoutes(long sequence,RuntimeStatus status,bool paused,PlayableRuntimeMetrics metrics,string failure,int seed,PlayableOwner owner,IReadOnlyList<PlayableRouteRequest> routeRequests)
            =>ProjectPlayerSnapshot(sequence,status,paused,metrics,failure,seed,owner,routeRequests,false);
        internal PlayableSnapshot PlayerSnapshotForAi(long sequence,RuntimeStatus status,bool paused,PlayableRuntimeMetrics metrics,string failure,int seed,PlayableOwner owner,bool armyRoutes=true)
            =>PlayerSnapshotForArmyAi(sequence,status,paused,metrics,failure,seed,owner,armyRoutes,null);
        internal PlayableSnapshot PlayerSnapshotForArmyAi(long sequence,RuntimeStatus status,bool paused,PlayableRuntimeMetrics metrics,string failure,int seed,PlayableOwner owner,bool armyRoutes,AiArmyRegistryState armyState)
            =>ProjectPlayerSnapshot(sequence,status,paused,metrics,failure,seed,owner,null,true,armyRoutes,armyState);
        private PlayableSnapshot ProjectPlayerSnapshot(long sequence,RuntimeStatus status,bool paused,PlayableRuntimeMetrics metrics,string failure,int seed,PlayableOwner owner,IReadOnlyList<PlayableRouteRequest> routeRequests,bool expansionRoutes,bool armyRoutes=false,AiArmyRegistryState armyState=null,bool fullOverview=false)
        {
            using var measured=PerformanceProfile.Measure(ProfileStage.Snapshot);
            RefreshVision();var v=fullOverview?FullOverviewVision():Vision(owner);var information=v.Snapshot();
            var es=new List<PlayableEntitySnapshot>();
            using(PerformanceProfile.Measure(ProfileStage.SnapshotUnits)){
            foreach(var u in units.Values)
            {
                if(!TryActor(u.Id,out var n)||(!fullOverview&&u.Owner!=owner&&!v.IsVisible(n.Position)))continue;
                int target=u.Owner==owner&&VisibleTarget(owner,u.Target)?u.Target:0;
                var order=u.Owner==owner?u.CurrentOrder:null;
                if(order!=null&&order.Kind==PlayableTacticalOrderKind.Attack&&!VisibleTarget(owner,order.TargetId))order=null;
                es.Add(new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,n.Position,(int)Math.Ceiling(u.Health),n.Moving,target,n.Heading,u.Turret,order,UnitUpgraded(u),u.Owner==owner&&n.Held,u.Owner==owner?n.Outcome:NavigationOutcome.Idle,u.Owner==owner?u.LastOrder:null,SpatialCompletion(u.Id,owner),u.Owner==owner&&u.Air==null?(NavLocation?)n.Location:null,TacticalQueueProjection(u.Id,owner),maxHealth:UnitMaximumHealth(u.Owner,u.Kind),exactHealth:u.Health,weaponSystemsLevel:ArmySystemsLevel(u.Owner,true),defensiveSystemsLevel:ArmySystemsLevel(u.Owner,false),altitude:u.Air?.Height??0,pitch:u.Air?.Pitch??0,roll:u.Air?.Roll??0,departing:u.Air?.Departing??false,weaponPitch:ExplorerPitch(u)));
            }
            }
            PerformanceProfile.Add(ProfileWork.SnapshotUnits,es.Count);
            var bs=new List<PlayableBuildingSnapshot>();
            using(PerformanceProfile.Measure(ProfileStage.SnapshotBuildings)){
            foreach(var b in buildings.Values)
            {
                if(!fullOverview&&b.Owner!=owner&&(b.Phase==ConstructionPhase.Pending||!v.IsVisible(b.Position)))continue;
                bool own=fullOverview||b.Owner==owner;double duration=TerritoryRules.Duration(Terms(b.TermsRevision),b.Kind);
                bs.Add(new PlayableBuildingSnapshot(b.Id,b.Owner,b.Kind,b.Position,(int)Math.Ceiling(b.Health),b.Ready?1:b.Build/duration,
                    own?b.Queue:0,own?b.ProductionProgress:0,own?b.Rally:default(NavPoint),b.SiteId,b.SlotId,b.ParentId,b.Phase,own?b.BlockedReason:null,b.Heading,own,own?ProductionSnapshot(b):null,own&&b.RepeatTank,own?LifecycleSnapshot(b):null,own?UpgradeSnapshot(b):null,b.Upgrade?.Complete==true,own?b.RepeatKind:PlayableEntityKind.Tank,b.Kind==PlayableBuildingKind.ScientificCenter&&own?ResearchSnapshot(b.Owner):null,own&&b.HasRally,own?PendingRally(b.Id):null,exactHealth:own?b.Health:(double?)null,launchPhase:b.LaunchPhase,launchTime:b.LaunchTime,launchKind:FactoryStagedKind(b),launchUpgraded:FactoryStagedUpgraded(b)));
            }
            }
            PerformanceProfile.Add(ProfileWork.SnapshotBuildings,bs.Count);
            using var envelopeMeasured=PerformanceProfile.Measure(ProfileStage.SnapshotEnvelope);
            var ps=new List<PlayableProjectileSnapshot>();
            foreach(var p in projectiles)
            {
                bool visible=p.Faction==owner||v.IsVisible(p.Position);
                var r=p.Rocket;bool markerVisible=r!=null&&(p.Faction==owner||v.IsVisible(r.Predicted.Point.Ground));
                if(!visible&&!markerVisible)continue;
                int source=visible&&es.Any(e=>e.Id==p.Owner)?p.Owner:0;
                int target=visible&&(es.Any(e=>e.Id==p.Target)||bs.Any(b=>b.Id==p.Target))?p.Target:0;
                if(r==null){ps.Add(new PlayableProjectileSnapshot(p.Id,source,target,p.Position,p.Kind,Math.Atan2(p.DirectionZ,p.DirectionX),height:p.Height,pitch:p.Spatial?Math.Atan2(p.Hawk?.Direction.Y??p.DirectionY,Math.Sqrt(Math.Pow(p.Hawk?.Direction.X??p.DirectionX,2)+Math.Pow(p.Hawk?.Direction.Z??p.DirectionZ,2))):Math.Atan(p.VerticalSlope),faction:p.Faction));continue;}
                double u=r.Elapsed/r.Flight.Duration;var point=r.Flight.Point(u);var tangent=r.Flight.Tangent(u);
                double progress=Math.Min(1,u/Math.Max(1e-5,r.Predicted.Progress));
                var marker=markerVisible?new PlayableImpactMarker(r.Predicted.Point.Ground,r.MarkerStartRadius+(r.BlastRadius-r.MarkerStartRadius)*progress,r.MarkerOpacity):null;
                ps.Add(new PlayableProjectileSnapshot(p.Id,source,target,visible?p.Position:default(NavPoint),p.Kind,visible?Math.Atan2(tangent.Z,tangent.X):0,visible?point.Y:0,visible?Math.Atan2(tangent.Y,Math.Sqrt(tangent.X*tangent.X+tangent.Z*tangent.Z)):0,p.Faction,r.Elapsed,visible,marker));
            }
            var publicMeasured=PerformanceProfile.Measure(ProfileStage.SnapshotPublic);
            var liveSites=SiteSnapshots().Where(s=>v.IsVisible(s.Site.Position)).Select(s=>
            {
                bool hiddenRequest=s.CenterId!=0&&buildings.TryGetValue(s.CenterId,out var b)&&b.Phase==ConstructionPhase.Pending&&b.Owner!=owner&&!fullOverview;
                return hiddenRequest?new TerritorySiteSnapshot(s.Site,s.Claimant,null,s.Progress,s.Contested,0,false):s;
            }).ToArray();
            var discovered=sites.Values.Where(s=>v.IsDiscovered(s.Site.Position)).Select(s=>s.Site).ToArray();
            var objectives=PublicObjectives(owner);
            publicMeasured.Dispose();
            using(PerformanceProfile.Measure(ProfileStage.SnapshotRequests)){
            if(expansionRoutes)routeRequests=AiExpansionPlanner.RouteRequests(es,liveSites,objectives,owner,navigation.Generation,Tick,profile)
                .Concat(armyRoutes?AiScoutPlanner.RouteRequests(es,objectives,owner,navigation.Generation,Tick,profile):Array.Empty<PlayableRouteRequest>())
                .Concat(armyRoutes?AiArmyPlanner.RouteRequests(es,bs,objectives,owner,navigation.Generation,Tick,profile,other=>Hostile(other,owner)).Concat(AiTacticalExecutor.RouteRequests(es,bs,armyState,owner,navigation.Generation,Tick,profile)):
                    !paused&&status==RuntimeStatus.Running&&Outcome==PlayableMatchOutcome.Playing&&!eliminated.Contains(owner)?AiTacticalExecutor.AssemblyRouteRequests(es,armyState,owner,navigation.Generation,Tick,profile):Array.Empty<PlayableRouteRequest>()).GroupBy(r=>new {r.UnitId,r.Kind,r.TargetId,r.Target}).Select(g=>g.First()).ToArray();
            }
#if UNITY_INCLUDE_TESTS
            // Ordinary participant-view queries do not generate route requests. Observe
            // only the real request-generation path, including an empty generated set.
            if(RouteRequestsReadbackForTests!=null&&expansionRoutes&&routeRequests!=null)
            {
                int requestOwnerIndex=(int)owner;
                string requestOwnerId=offline!=null&&requestOwnerIndex>=0&&requestOwnerIndex<offline.Roster.Count?
                    offline.Roster[requestOwnerIndex].Id:owner.ToString();
                if(RouteProjectionOwnerFilterForTests==null||requestOwnerId==RouteProjectionOwnerFilterForTests)
                    RouteRequestsReadbackForTests(requestOwnerId,Tick,routeRequests.Where(r=>!CaptureCurrentExpansionRoutesForTests||r.Kind==PlayableRouteTargetKind.PublicObjective).ToArray());
            }
#endif
            using var payloadMeasured=PerformanceProfile.Measure(ProfileStage.SnapshotPayload);
            return new PlayableSnapshot(profile.ProfileId,profile.Revision,navigation.Generation,seed,sequence,Tick,status,paused,Outcome,
                (int)Math.Floor(Balance(owner)),projectileGeometry,es.ToArray(),bs.ToArray(),ps.ToArray(),metrics,failure,liveSites,
                Income(owner)/profile.IncomePeriodSeconds,information,discovered,Population(owner),impacts.Where(p=>(fullOverview||(p.VisibleMask&(1<<(int)owner))!=0)&&v.IsVisible(p.Point.Ground)).Select(p=>new PlayableImpactSnapshot(p.Id,p.Owner,p.Point,p.Radius,p.Tick,fullOverview?255:1<<(int)owner)).ToArray(),researchAvailability:ResearchAvailability(owner),ownerResearch:ResearchSnapshot(owner),publicScoutObjectives:objectives,
                ownCenterDamage:centerDamage.Values.Where(d=>d.Owner==owner&&bs.Any(b=>b.Id==d.CenterId&&b.Health>0)&&es.Any(e=>e.Id==d.AttackerId&&e.Owner!=owner)).OrderBy(d=>d.CenterId).ThenBy(d=>d.AttackerId).Select(d=>d.Copy()).ToArray(),
                routeProofs:ProjectRoutes(owner,routeRequests,v,es,bs,armyState),
                artillerySupport:ProjectArtillery(owner,v,es,bs,armyState),owner:owner,exactCredits:Balance(owner),ownerId:offline==null?null:offline.Roster[(int)owner].Id,team:TeamOf(owner),participants:offline?.Roster.ToArray(),activeProfile:profile,homeSiteId:HomeSite(owner),ownerEliminated:eliminated.Contains(owner),settledIncome:SettledIncome(owner),mission:owner==PlayableOwner.Player?CampaignProjection():null,intelEnvelopes:PublicIntelEnvelopes(),groundTrails:expansionRoutes?null:groundTrails.Visible(Tick,fullOverview?null:information,profile.GroundTrailLifetimeTicks),sounds:expansionRoutes?null:Sounds(fullOverview?(PlayableOwner?)null:owner),incomeEvents:expansionRoutes||fullOverview?null:IncomeEvents(owner));
        }
        private AiIntelEnvelope[] publicIntelEnvelopes;
        private AiIntelEnvelope[] PublicIntelEnvelopes()
        {
            RefreshPublicGeographyProfile();
            return publicIntelEnvelopes??(publicIntelEnvelopes=sites.Values.OrderBy(s=>s.Site.Id).Select(s=>new AiIntelEnvelope(s.Site.Id,new[]{new VisionSource(s.Site.Position,TerritoryRules.Radius(profile,s.Site.Kind)*Math.Sqrt(2))}.Concat(s.Site.Slots.Select(slot=>new VisionSource(slot.Position,Math.Max(profile.ScienceFootprintRadius,Math.Max(profile.FactoryFootprintRadius,profile.RefineryFootprintRadius))*Math.Sqrt(2)))))).ToArray());
        }
        private PlayableVision overviewVision;
        private PlayableVision FullOverviewVision()
        {
            if(overviewVision==null){overviewVision=new PlayableVision(-1,profile.ArenaHalfExtent,profile.ArenaHalfExtent,profile.VisionCellSize,profile.FogEdgeFeather);overviewVision.Refresh(new[]{new VisionSource(default(NavPoint),profile.ArenaHalfExtent*3)},Array.Empty<KnownBuilding>());}
            return overviewVision;
        }
        internal PlayableSpectatorFrame SpectatorSnapshot(long sequence,RuntimeStatus status,bool paused,PlayableRuntimeMetrics metrics,string failure,int seed)
        {
            var perspectives=Owners.Select(owner=>PlayerSnapshot(sequence,status,paused,metrics,failure,seed,owner)).ToArray();
            var overview=ProjectPlayerSnapshot(sequence,status,paused,metrics,failure,seed,PlayableOwner.Player,null,false,fullOverview:true);
            return new PlayableSpectatorFrame(overview,perspectives);
        }
        // Reconstructible observer-only memoization. It carries no authority/proof state and
        // a restored domain starts cold. Each value is confined to its authority thread.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PlayableDomain,Dictionary<PlayableOwner,List<ObservedRouteCache>>> observedRouteCaches=new System.Runtime.CompilerServices.ConditionalWeakTable<PlayableDomain,Dictionary<PlayableOwner,List<ObservedRouteCache>>>();
        private sealed class ObservedRouteCache
        {
            internal NavGeometry Geometry;internal NavigationProfile Profile;internal double Radius,Speed,Turn;internal SharedFlowRouter Router;
        }
        private const int ObservedRouteFieldLimit=64;
        private const int ObservedRouteProfileLimit=8;
        private sealed class ObservedGeometryCache
        {
            internal NavGeometry Authority;
            internal bool[] Authored;
            internal readonly List<NavObstacle> Scratch=new List<NavObstacle>();
            internal readonly Dictionary<PlayableVision,NavGeometry> Views=new Dictionary<PlayableVision,NavGeometry>();
        }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PlayableDomain,ObservedGeometryCache> observedGeometryCaches=new System.Runtime.CompilerServices.ConditionalWeakTable<PlayableDomain,ObservedGeometryCache>();
        private SharedFlowRouter ObservedRouter(PlayableOwner owner,NavGeometry observed,NavigationProfile basis,double radius,double speed,double turn)
        {
            var caches=observedRouteCaches.GetValue(this,_=>new Dictionary<PlayableOwner,List<ObservedRouteCache>>());
            if(!caches.TryGetValue(owner,out var pool))caches.Add(owner,pool=new List<ObservedRouteCache>());
            bool SameGeometry(NavGeometry a,NavGeometry b)=>ReferenceEquals(a,b)||a.HalfExtent==b.HalfExtent&&a.Obstacles.Count==b.Obstacles.Count&&a.Obstacles.Select((o,i)=>o.GeometryEquals(b.Obstacles[i])).All(x=>x);
            // Mixed armies alternate footprints on every review. Retain independent exact
            // predicates/fields instead of discarding the previous unit's whole router.
            var cache=pool.Find(c=>ReferenceEquals(c.Profile,basis)&&c.Radius==radius&&c.Speed==speed&&c.Turn==turn);
            if(cache==null){
                if(pool.Count==ObservedRouteProfileLimit)pool.RemoveAt(0);
                cache=new ObservedRouteCache{Geometry=observed,Profile=basis,Radius=radius,Speed=speed,Turn=turn};
            }else{
                pool.Remove(cache);
                if(!SameGeometry(cache.Geometry,observed)){
                    cache.Router=cache.Router?.ForGeometry(observed);cache.Geometry=observed;
                }
            }
            pool.Add(cache); // Bounded least-recently-used profile ownership, never persisted.
            // Four-player reviews include more than eight recurring objectives/candidates.
            // Keep a bounded working set (64 fields per typed observer router), not
            // a seven-entry cache that cyclically evicts every public objective.
            // A request can add both floor and attachment fields. Evict old fields while
            // retaining exact grid predicates for this unchanged observer geometry/profile.
            if(cache.Router==null)cache.Router=new SharedFlowRouter(cache.Geometry,basis.ForUnit(radius,speed,turn),null);
            else if(cache.Router.CachedFieldCount>=ObservedRouteFieldLimit-1)cache.Router.TrimFields(ObservedRouteFieldLimit-2);
            return cache.Router;
        }
        private NavGeometry ObservedGeometry(PlayableVision vision)
        {
            // The observer solves on visible solids with the production navigation solver, then
            // checks every segment against authoritative solids. Hidden geometry can only remove proof.
            var cache=observedGeometryCaches.GetValue(this,_=>new ObservedGeometryCache());
            // Authoritative geometry and authored map solids are immutable. Resolve the
            // public-map membership once per geometry, not for every obstacle on every tick.
            if(!ReferenceEquals(cache.Authority,Geometry)){
                cache.Authority=Geometry;cache.Authored=new bool[Geometry.Obstacles.Count];
                for(int i=0;i<cache.Authored.Length;i++)cache.Authored[i]=offline?.Obstacles.Any(a=>a.GeometryEquals(Geometry.Obstacles[i]))==true;
            }
            cache.Views.TryGetValue(vision,out var previous);
            var observed=cache.Scratch;observed.Clear();
            bool unchanged=previous!=null&&previous.HalfExtent==Geometry.HalfExtent;
            for(int i=0;i<Geometry.Obstacles.Count;i++){
                var obstacle=Geometry.Obstacles[i];
                if(!cache.Authored[i]&&!ObstacleCornersVisible(obstacle,vision))continue;
                int index=observed.Count;observed.Add(obstacle);
                if(unchanged&&(index>=previous.Obstacles.Count||!obstacle.GeometryEquals(previous.Obstacles[index])))unchanged=false;
            }
            // Exact ordered solids determine reuse, never a hash or a visibility revision.
            // A hidden authority change must not invalidate an unchanged observer view.
            if(unchanged&&observed.Count==previous.Obstacles.Count)return previous;
            // This identity depends only on observed solids, never on hidden authority revisions.
            int observedRevision=1;
            unchecked{foreach(var obstacle in observed)observedRevision=observedRevision*31+obstacle.MinX.GetHashCode()+obstacle.MinZ.GetHashCode()+obstacle.MaxX.GetHashCode()+obstacle.MaxZ.GetHashCode();}
            observedRevision=observedRevision==int.MinValue?int.MaxValue:Math.Max(1,Math.Abs(observedRevision));
            var result=new NavGeometry(Geometry.HalfExtent,observed.ToArray(),observedRevision);
            if(cache.Views.Count>=8&&!cache.Views.ContainsKey(vision))cache.Views.Clear();
            cache.Views[vision]=result;return result;
        }
        internal void PreparePublicNavigation(PlayableOwner owner,Func<bool> stopping=null)
        {
            // No private observation projection: that lazily creates research ledgers.
            // Warm only pure public route inputs and derived observer caches.
            var vision=Vision(owner);var observed=ObservedGeometry(vision);var objectives=PublicObjectives(owner);
            // Future produced units must not rasterize a new footprint in a live AI
            // decision. Only immutable observer geometry/grid predicates are warmed.
            foreach(PlayableEntityKind kind in Enum.GetValues(typeof(PlayableEntityKind))){
                if(stopping?.Invoke()==true)return;
                double radius=PlayableUnitRules.Radius(profile,kind),turn=PlayableUnitRules.Turn(profile,kind);
                ObservedRouter(owner,observed,profile.Navigation,radius,PlayableUnitRules.Speed(profile,kind),turn).PrepareGridPredicates(stopping);
                if(kind==PlayableEntityKind.Tank)
                    ObservedRouter(owner,observed,profile.Navigation,radius,PlayableUnitRules.Speed(profile,kind,true),turn).PrepareGridPredicates(stopping);
            }
            foreach(var unit in units.Values.Where(u=>u.Owner==owner&&u.Kind==PlayableEntityKind.Explorer&&u.Health>0)){
                if(stopping?.Invoke()==true)return;
                if(!TryActor(unit.Id,out var nav))continue;
                foreach(var objective in objectives){
                    if(stopping?.Invoke()==true)return;
                    if(objective.Reachable)ObservedRouter(owner,observed,profile.Navigation,nav.Radius,nav.Speed,nav.TurnSpeed).FindPath(nav.Position,objective.Approach);
                }
            }
        }
        private PlayableRouteProof[] ProjectRoutes(PlayableOwner owner,IReadOnlyList<PlayableRouteRequest> requests,PlayableVision vision,IReadOnlyList<PlayableEntitySnapshot> entities,IReadOnlyList<PlayableBuildingSnapshot> buildings,Spacewars.Runtime.AiArmyRegistryState armyState)
        {
            using var measured=PerformanceProfile.Measure(ProfileStage.Routes);
            if(requests==null||requests.Count==0)return Array.Empty<PlayableRouteProof>();
            long projectionBegin=routePerformanceTrace?System.Diagnostics.Stopwatch.GetTimestamp():0;
            // The observer solves on visible solids with the production navigation solver, then
            // checks every segment against authoritative solids. Hidden geometry can only remove proof.
            var safeGeometry=ObservedGeometry(vision);
            int observedRevision=safeGeometry.Revision;
            var result=new List<PlayableRouteProof>();
            PlayablePublicScoutObjective[] publicObjectives=null;
            foreach(var request in requests)
            {
#if UNITY_INCLUDE_TESTS
                var observer=RouteProjectionReadbackForTests;
                int ownerIndex=(int)owner;
                bool capture=request!=null&&observer!=null&&offline!=null&&ownerIndex>=0&&ownerIndex<offline.Roster.Count&&
                    (CaptureCurrentExpansionRoutesForTests&&offline.Roster[ownerIndex].Id==RouteProjectionOwnerFilterForTests&&request.Kind==PlayableRouteTargetKind.PublicObjective||
                    offline.Roster[ownerIndex].Id=="ai-5"&&request.Tick==8424&&request.Kind==PlayableRouteTargetKind.VisibleEnemy&&request.TargetId==83&&
                    (request.UnitId==38||request.UnitId==52||request.UnitId==60||request.UnitId==89));
                string bindingRejection=null;NavPoint? resolvedTarget=null;bool? originVisible=null,targetVisible=null;
                var candidateReadbacks=capture?new List<PlayableRouteProjectionCandidateReadback>():null;
                PlayableRouteProjectionReadbackOutcome finalOutcome=PlayableRouteProjectionReadbackOutcome.BindingRejected;
#endif
                Unit unit=null;NavUnit nav=null;
#if UNITY_INCLUDE_TESTS
                if(capture)
                {
                    if(request.UnitId<=0)bindingRejection="unit-id";
                    else if(request.TargetId<=0)bindingRejection="target-id";
                    else if(request.Generation!=navigation.Generation)bindingRejection="generation";
                    else if(request.Tick!=Tick)bindingRejection="tick";
                    else if(!units.TryGetValue(request.UnitId,out unit))bindingRejection="unit-missing";
                    else if(unit.Owner!=owner)bindingRejection="unit-owner";
                    else if(unit.Health<=0)bindingRejection="unit-dead";
                    else if(!TryActor(unit.Id,out nav))bindingRejection="nav-unit-missing";
                    else if(request.Origin.X!=nav.Position.X)bindingRejection="origin-x";
                    else if(request.Origin.Z!=nav.Position.Z)bindingRejection="origin-z";
                    else if(request.Radius!=nav.Radius)bindingRejection="radius";
                    if(bindingRejection!=null){PublishRouteProjectionReadback(observer,ownerIndex,owner,request,finalOutcome,bindingRejection,resolvedTarget,originVisible,targetVisible,0,safeGeometry.Obstacles.Count,0,candidateReadbacks);continue;}
                }
                else
#endif
                if(request==null||request.UnitId<=0||request.TargetId<=0||request.Generation!=navigation.Generation||request.Tick!=Tick||!units.TryGetValue(request.UnitId,out unit)||unit.Owner!=owner||unit.Health<=0||!TryActor(unit.Id,out nav)||request.Origin.X!=nav.Position.X||request.Origin.Z!=nav.Position.Z||request.Radius!=nav.Radius)continue;
                NavPoint target,goal;bool buildingTarget=false;double friendlyRadius=0;
                if(request.Kind==PlayableRouteTargetKind.VisibleEnemy)
                {
                    var enemy=entities.FirstOrDefault(e=>e.Id==request.TargetId&&Hostile(e.Owner,owner)&&e.Health>0);
                    var enemyBuilding=buildings.FirstOrDefault(b=>b.Id==request.TargetId&&Hostile(b.Owner,owner)&&b.Health>0);
                    if(enemy==null&&enemyBuilding==null)
                    {
#if UNITY_INCLUDE_TESTS
                        if(capture){finalOutcome=PlayableRouteProjectionReadbackOutcome.TargetUnresolved;PublishRouteProjectionReadback(observer,ownerIndex,owner,request,finalOutcome,bindingRejection,resolvedTarget,originVisible,targetVisible,observedRevision,safeGeometry.Obstacles.Count,0,candidateReadbacks);}
#endif
                        continue;
                    }
                    target=goal=enemy!=null?enemy.Position:enemyBuilding.Position;buildingTarget=enemy==null;
                }
                else if(request.Kind==PlayableRouteTargetKind.FriendlyAnchor)
                {
                    var ally=entities.FirstOrDefault(e=>e.Id==request.TargetId&&e.Owner==owner&&e.Health>0);
                    var home=buildings.FirstOrDefault(b=>b.Id==request.TargetId&&b.Owner==owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready);
                    bool currentLeg=ally!=null&&ally.Id==unit.Id&&ally.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&ally.CurrentOrder.Destination.Equals(request.Target);
                    bool followLeg=ally!=null&&ally.Id!=unit.Id&&PlayableUnitRules.FollowGoal(profile,ally.Position,ally.HullHeading).Equals(request.Target);
                    if(ally==null&&home==null)continue;target=goal=currentLeg?ally.CurrentOrder.Destination:followLeg?request.Target:ally!=null?ally.Position:home.Position;buildingTarget=!currentLeg&&!followLeg;
                    if(ally!=null&&!currentLeg&&!followLeg)
                        friendlyRadius=Math.Max(profile.FollowDistance+nav.Radius+PlayableUnitRules.Radius(profile,ally.Kind),
                            Math.Max(profile.Navigation.ArrivalSlotSpacing,4*nav.Radius+2*profile.Navigation.ArrivalTolerance+2*profile.Navigation.LocalGridCell));
                    else if(home!=null)friendlyRadius=TerritoryRules.Radius(profile,home.Kind)+nav.Radius+profile.FollowDistance;
                }
                else if(request.Kind==PlayableRouteTargetKind.PublicObjective)
                {
                    // No authority mutation occurs inside this projection. Public geometry is
                    // identical for all requests; do not rebuild its roster/site array per unit.
                    var objective=(publicObjectives??(publicObjectives=PublicObjectives(owner))).FirstOrDefault(o=>o.SiteId==request.TargetId&&o.Reachable);
                    if(objective==null)continue;target=goal=objective.Approach;
                }
                else continue;
                if(request.Target.X!=target.X||request.Target.Z!=target.Z)
                {
#if UNITY_INCLUDE_TESTS
                    if(capture){finalOutcome=PlayableRouteProjectionReadbackOutcome.TargetMismatch;resolvedTarget=target;PublishRouteProjectionReadback(observer,ownerIndex,owner,request,finalOutcome,bindingRejection,resolvedTarget,originVisible,targetVisible,observedRevision,safeGeometry.Obstacles.Count,0,candidateReadbacks);}
#endif
                    continue;
                }
#if UNITY_INCLUDE_TESTS
                if(capture)resolvedTarget=target;
#endif
                var radius=nav.Radius;
                // Routes may cross fog using known destinations and observed solids.
                // Only a hostile semantic target still requires current visibility.
#if UNITY_INCLUDE_TESTS
                if(capture)
                {
                    originVisible=vision.IsVisible(nav.Position);
                    if(originVisible.Value&&request.Kind==PlayableRouteTargetKind.VisibleEnemy)targetVisible=vision.IsVisible(target);
                    if(!originVisible.Value||targetVisible==false)
                    {
                        finalOutcome=PlayableRouteProjectionReadbackOutcome.OriginOrTargetNotVisible;
                        PublishRouteProjectionReadback(observer,ownerIndex,owner,request,finalOutcome,bindingRejection,resolvedTarget,originVisible,targetVisible,observedRevision,safeGeometry.Obstacles.Count,0,candidateReadbacks);continue;
                    }
                }
                else
#endif
                if(!vision.IsVisible(nav.Position)||(request.Kind==PlayableRouteTargetKind.VisibleEnemy&&!vision.IsVisible(target)))continue;
                var actor=entities.First(e=>e.Id==unit.Id);
                var goals=buildingTarget?PlayableUnitRules.AttackApproachCandidates(profile,nav.Position,target,friendlyRadius>0?friendlyRadius/profile.AttackApproachRangeRatio:PlayableUnitRules.Range(profile,actor.Kind,actor.Upgraded))
                    .Where(p=>safeGeometry.IsFree(p,radius))
                    .Where(p=>request.Kind!=PlayableRouteTargetKind.FriendlyAnchor||entities.Where(e=>e.Id!=unit.Id).All(e=>
                        Distance(p,e.Position)>=Math.Max(profile.Navigation.ArrivalSlotSpacing,4*radius+2*profile.Navigation.ArrivalTolerance+2*profile.Navigation.LocalGridCell)&&
                        (e.CurrentOrder?.Kind!=PlayableTacticalOrderKind.Move||Distance(p,e.CurrentOrder.Destination)>=Math.Max(profile.Navigation.ArrivalSlotSpacing,4*radius+2*profile.Navigation.ArrivalTolerance+2*profile.Navigation.LocalGridCell))))
                    .OrderBy(p=>Distance(nav.Position,p)).ToArray():new[]{goal};
                // Arrival clears the tactical order, but the native crowd retains its
                // accepted goal. Publish that exact goal through the existing home proof.
                bool withdrawalArrival=request.Kind==PlayableRouteTargetKind.FriendlyAnchor&&buildings.Any(b=>b.Id==request.TargetId)&&
                    actor.CurrentOrder==null&&nav.Outcome==NavigationOutcome.Arrived&&actor.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&actor.OrderStamp.Kind==PlayableCommandKind.Move&&
                    actor.OrderStamp.Owner==owner&&actor.OrderStamp.Generation==navigation.Generation&&actor.OrderStamp.Source==PlayableAiOpeningComposition.SourceIdentity&&
                    armyState!=null&&armyState.Generation==navigation.Generation&&armyState.Armies.Any(a=>(a.Phase==AiArmyPhase.Retreating||a.Phase==AiArmyPhase.Recovering)&&a.TacticalOwner==AiArmyPlanner.Policy&&a.Members.Contains(actor.Id)&&!a.Reinforcements.Contains(actor.Id));
                if(withdrawalArrival)goals=Distance(nav.Goal,target)<=friendlyRadius+profile.Navigation.ArrivalTolerance&&safeGeometry.IsFree(nav.Goal,radius)?new[]{nav.Goal}:Array.Empty<NavPoint>();
#if UNITY_INCLUDE_TESTS
                int candidateCount=goals.Length;
                bool proofPublished=false;
#endif
                foreach(var candidate in goals)
                {
#if UNITY_INCLUDE_TESTS
                    bool? candidateVisible=null;int candidateIndex=capture?candidateReadbacks.Count:0;
                    if(capture)
                    {
                        candidateVisible=vision.IsVisible(candidate);
                    }
#endif
                    var router=ObservedRouter(owner,safeGeometry,profile.Navigation,radius,nav.Speed,nav.TurnSpeed);
                    long pathBegin=routePerformanceTrace?System.Diagnostics.Stopwatch.GetTimestamp():0;
                    long expanded=routePerformanceTrace?router.ExpandedCells:0,checks=routePerformanceTrace?router.GridGeometryChecks:0;
                    int rebuilds=routePerformanceTrace?router.RebuildCount:0;
                    NavPoint[] path;using(PerformanceProfile.Measure(ProfileStage.ObservedPath))path=router.FindPath(nav.Position,candidate);
                    if(routePerformanceTrace){double ms=(System.Diagnostics.Stopwatch.GetTimestamp()-pathBegin)*1000d/System.Diagnostics.Stopwatch.Frequency;
                        if(ms>=5)Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "OBS_PATH tick={0} owner={1} actor={2} kind={3} target={4} radius={5:R} candidates={6} ms={7:F4} expanded={8} checks={9} rebuilds={10} points={11}",
                            Tick,owner,unit.Id,request.Kind,request.TargetId,radius,goals.Length,ms,router.ExpandedCells-expanded,router.GridGeometryChecks-checks,router.RebuildCount-rebuilds,path.Length));}
                    // A solver's empty route is not arrival evidence. Only the existing
                    // arrival tolerance plus a current free native sweep certifies this
                    // tiny/degenerate friendly leg; unreachable legs still have no proof.
                    if(path.Length==0&&request.Kind==PlayableRouteTargetKind.FriendlyAnchor&&Distance(nav.Position,candidate)<=profile.Navigation.ArrivalTolerance&&
                        Geometry.SegmentFree(nav.Position,candidate,radius)&&safeGeometry.SegmentFree(nav.Position,candidate,radius))path=new[]{candidate};
                    if(path.Length==0)
                    {
#if UNITY_INCLUDE_TESTS
                        if(capture)candidateReadbacks.Add(new PlayableRouteProjectionCandidateReadback(candidateIndex,candidate,candidateVisible,
                            PlayableRouteProjectionCandidateOutcome.RouterReturnedEmpty,path,-1,null,null,null,null));
#endif
                        continue;
                    }
#if UNITY_INCLUDE_TESTS
                    if(capture)
                    {
                        var from=nav.Position;bool safe=true;int failedSegment=-1;NavPoint? failedFrom=null,failedTo=null;bool? authoritativeFree=true,visibleSegment=true;
                        for(int pathIndex=0;pathIndex<path.Length;pathIndex++)
                        {
                            var point=path[pathIndex];
                            if(request.Kind!=PlayableRouteTargetKind.PublicObjective)
                            {
                                bool segmentFree=Geometry.SegmentFree(from,point,radius);
                                authoritativeFree=authoritativeFree.Value&&segmentFree;
                                visibleSegment=visibleSegment.Value&&VisibleSegment(vision,from,point);
                                if(!segmentFree){safe=false;failedSegment=pathIndex;failedFrom=from;failedTo=point;break;}
                            }
                            from=point;
                        }
                        if(!safe)
                        {
                            candidateReadbacks.Add(new PlayableRouteProjectionCandidateReadback(candidateIndex,candidate,candidateVisible,
                                PlayableRouteProjectionCandidateOutcome.UnsafeSegment,path,failedSegment,failedFrom,failedTo,authoritativeFree,visibleSegment));
                            continue;
                        }
                        candidateReadbacks.Add(new PlayableRouteProjectionCandidateReadback(candidateIndex,candidate,candidateVisible,
                            PlayableRouteProjectionCandidateOutcome.ProofPublished,path,-1,null,null,authoritativeFree,visibleSegment));
                    }
                    else
#endif
                    {
                        var from=nav.Position;bool safe=true;
                        foreach(var point in path)
                        {
                            // Routes may cross fog using observed geometry; authoritative geometry and the
                            // ordinary native route barrier remain responsible for hidden solids.
                            if(request.Kind!=PlayableRouteTargetKind.PublicObjective&&!Geometry.SegmentFree(from,point,radius)){safe=false;break;}
                            from=point;
                        }
                        if(!safe)continue;
                    }
                    result.Add(new PlayableRouteProof(unit.Id,unit.Owner,navigation.Generation,Tick,observedRevision,nav.Position,radius,request.Kind,request.TargetId,target,candidate,path));
#if UNITY_INCLUDE_TESTS
                    if(capture)proofPublished=true;
#endif
                    break;
                }
#if UNITY_INCLUDE_TESTS
                if(capture)
                {
                    finalOutcome=proofPublished?PlayableRouteProjectionReadbackOutcome.ProofPublished:
                        candidateCount==0?PlayableRouteProjectionReadbackOutcome.NoCandidateGenerated:
                        candidateReadbacks.Count>0&&candidateReadbacks.All(x=>x.Outcome==PlayableRouteProjectionCandidateOutcome.Invisible)?PlayableRouteProjectionReadbackOutcome.AllCandidatesInvisible:
                        PlayableRouteProjectionReadbackOutcome.AllCandidatesRejected;
                    PublishRouteProjectionReadback(observer,ownerIndex,owner,request,finalOutcome,bindingRejection,resolvedTarget,originVisible,targetVisible,observedRevision,safeGeometry.Obstacles.Count,candidateCount,candidateReadbacks);
                }
#endif
            }
            if(routePerformanceTrace){double ms=(System.Diagnostics.Stopwatch.GetTimestamp()-projectionBegin)*1000d/System.Diagnostics.Stopwatch.Frequency;
                if(ms>=5)Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,"OBS_ROUTES tick={0} owner={1} requests={2} ms={3:F4}",Tick,owner,requests.Count,ms));}
            return result.ToArray();
        }
        // Only a due registry-owned tactical Move asks this question. The observer
        // uses current visible facts and the existing router; no per-tick army solve.
        internal bool ArmyMoveSafe(PlayableAiAction action)=>CheckArmyMove(action,false);
        internal bool ArmyWithdrawalSafe(PlayableAiAction action)=>CheckArmyMove(action,true);
        // Authored map blockers are public geography. Dynamic solids remain fog-gated
        // until all four obstacle corners are currently visible.
        private static bool ObstacleCornersVisible(NavObstacle obstacle,PlayableVision vision)
            =>vision.IsVisible(new NavPoint(obstacle.MinX,obstacle.MinZ))&&
                vision.IsVisible(new NavPoint(obstacle.MaxX,obstacle.MinZ))&&
                vision.IsVisible(new NavPoint(obstacle.MinX,obstacle.MaxZ))&&
                vision.IsVisible(new NavPoint(obstacle.MaxX,obstacle.MaxZ));
        private bool CheckArmyMove(PlayableAiAction action,bool withdrawal)
        {
            var owner=OwnerFor(action.PlayerId);var snapshot=PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,action.Seed,owner);
            var o=PlayableAiObservation.From(snapshot);var vision=Vision(owner);
            var leader=action.Kind==PlayableCommandKind.Follow?o.Entities.FirstOrDefault(u=>u.Id==action.TargetId&&u.Owner==owner&&u.Health>0):null;
            if(action.Kind==PlayableCommandKind.Follow&&(leader==null||Spacewars.Simulation.Ai.AiRosterCatalog.Initial.For(leader.Kind).lineWeight<=0))return false;
            var target=leader==null?action.Target:PlayableUnitRules.FollowGoal(profile,leader.Position,leader.HullHeading);
            // Move coordinates and own-entity Follow goals remain actionable through fog.
            var geometry=ObservedGeometry(vision);
            double largest=0,slotSpeed=profile.Navigation.TankSpeed,slotTurn=profile.Navigation.TankTurnSpeed;
            foreach(int id in action.EntityIds){var own=o.Entities.FirstOrDefault(u=>u.Id==id&&u.Owner==owner&&u.Health>0);if(own==null||!TryActor(id,out var mover))return false;double ownRadius=PlayableUnitRules.Radius(profile,own.Kind);if(ownRadius>largest){largest=ownRadius;slotSpeed=mover.Speed;slotTurn=mover.TurnSpeed;}}
            var slots=leader==null?NavArrivalAllocator.Allocate(geometry,profile.Navigation.ForUnit(largest,slotSpeed,slotTurn),target,action.EntityIds.Count):action.EntityIds.Select(_=>target).ToArray();
            if(slots.Length!=action.EntityIds.Count||leader==null&&slots.Length==1&&!slots[0].Equals(target))return false;
            for(int index=0;index<action.EntityIds.Count;index++)
            {
                int id=action.EntityIds[index];var goal=slots[index];
                var unit=o.Entities.FirstOrDefault(u=>u.Id==id&&u.Owner==owner&&u.Health>0);if(unit==null||!TryActor(id,out var nav))return false;
                var radius=PlayableUnitRules.Radius(profile,unit.Kind);if(!geometry.IsFree(goal,radius))return false;
                var path=new SharedFlowRouter(geometry,profile.Navigation.ForUnit(radius,nav.Speed,nav.TurnSpeed)).FindPath(unit.Position,goal);
                if(path.Length==0&&Distance(unit.Position,goal)<=profile.Navigation.ArrivalTolerance&&geometry.SegmentFree(unit.Position,goal,radius)&&VisibleSegment(vision,unit.Position,goal))path=new[]{goal};
                if(path.Length==0||!path.Last().Equals(goal)||!(withdrawal?AiTacticalExecutor.WithdrawalPath(o,unit.Position,path,radius,profile):AiTacticalExecutor.SafePath(o,unit.Position,path,radius,profile)))return false;
                var at=unit.Position;foreach(var point in path){if(!geometry.SegmentFree(at,point,radius)||!VisibleSegment(vision,at,point))return false;at=point;}
            }
            return true;
        }
        private bool VisibleSegment(PlayableVision vision,NavPoint from,NavPoint to)
        {
            double dx=to.X-from.X,dz=to.Z-from.Z,length=Math.Sqrt(dx*dx+dz*dz);
            // VisionCellSize is existing profile metadata; half-cell sampling is a projection invariant.
            int steps=Math.Max(1,(int)Math.Ceiling(length/(profile.VisionCellSize/2)));
            for(int i=0;i<=steps;i++)if(!vision.IsVisible(new NavPoint(from.X+dx*i/steps,from.Z+dz*i/steps)))return false;
            return true;
        }
    }
}
