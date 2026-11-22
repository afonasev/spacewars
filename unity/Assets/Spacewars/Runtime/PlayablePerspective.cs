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
        internal Action<string,bool> SnapshotCostObserverForTests;
        internal Action BeforeObservedPathForTests;
        internal Action BeforeOwnerReviewForTests;
        internal Action<PlayableAiAction,int> BeforeArmySafetyPathForTests;
        internal readonly int[] SnapshotCostSampleAttemptsForTests=new int[5];
        private T SnapshotSampleValueForTests<T>(int part,string label,Func<T> read)
        {
            if(SnapshotCostObserverForTests==null)return read();
            int attempt=++SnapshotCostSampleAttemptsForTests[part];
            if((attempt&31)!=1)return read();
            using(var measured=new SnapshotCostScopeForTests(SnapshotCostObserverForTests,label))return read();
        }

        private readonly struct SnapshotCostScopeForTests:IDisposable
        {
            private readonly Action<string,bool> observer;private readonly string label;
            internal SnapshotCostScopeForTests(Action<string,bool> observer,string label){this.observer=observer;this.label=label;observer?.Invoke(label,true);}
            public void Dispose()=>observer?.Invoke(label,false);
        }
        private T SnapshotValueForTests<T>(string label,Func<T> read)
        {using(var measured=new SnapshotCostScopeForTests(SnapshotCostObserverForTests,label))return read();}
        internal Action<PlayableRouteProjectionReadback> RouteProjectionReadbackForTests;
        internal Action<string,long,PlayableRouteRequest[]> RouteRequestsReadbackForTests;
        internal string RouteProjectionOwnerFilterForTests;
        internal bool CaptureCurrentExpansionRoutesForTests;

        private static void PublishRouteProjectionReadback(Action<PlayableRouteProjectionReadback> observer,string ownerId,
            PlayableOwner owner,PlayableRouteRequest request,PlayableRouteProjectionReadbackOutcome outcome,string bindingRejection,
            NavPoint? resolvedTarget,bool? originVisible,bool? targetVisible,int geometryRevision,int obstacleCount,int candidateCount,
            List<PlayableRouteProjectionCandidateReadback> candidates)
        {
            observer(new PlayableRouteProjectionReadback(ownerId,owner,request,outcome,bindingRejection,
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
            foreach(var source in lingeringVision)if(source.Team==TeamOf(owner))yield return new VisionSource(source.Point,source.Radius*source.Remaining/source.Duration);
            foreach(var mine in mines)if(!Hostile(mine.Owner,owner))yield return new VisionSource(mine.Point,mine.Vision);
            foreach(var scan in scans)if(!Hostile(scan.Owner,owner))yield return new VisionSource(scan.Point,scan.Radius);
            foreach(var u in units.Values)if(!Hostile(u.Owner,owner)&&u.Health>0&&!Materializing(u)&&TryActor(u.Id,out var n))yield return new VisionSource(n.Position,PlayableUnitRules.Vision(profile,u.Kind));
            foreach(var s in sites.Values)if(s.Site.Kind==PlayableBuildingKind.RelayTower&&RelayOwnerPaint(s) is PlayableOwner relayOwner&&!Hostile(relayOwner,owner))
                yield return new VisionSource(s.Site.Position,profile.RelayVisionRange);
            foreach(var b in buildings.Values)if(!Hostile(b.Owner,owner))
            {
                double radius=PlayableVision.BuildingRadius(profile,b.Kind,b.Phase);
                if(radius>0)yield return new VisionSource(b.Position,radius);
            }
        }
        // Roster membership and teams are immutable for a domain. Allied owners
        // enumerate identical sources in identical order into the same vision.
        // Keep the first owner of each team without persistent derived state.
#if UNITY_INCLUDE_TESTS
        internal bool RepeatAlliedVisionRefreshForTests;
#endif
        private IEnumerable<PlayableOwner> VisionTeamOwners()
        {
#if UNITY_INCLUDE_TESTS
            if(RepeatAlliedVisionRefreshForTests){foreach(var owner in Owners)yield return owner;yield break;}
#endif
            int seen=0;
            foreach(var owner in Owners){int bit=1<<TeamOf(owner);if((seen&bit)!=0)continue;seen|=bit;yield return owner;}
        }
        private void RefreshVision()
        {
            using var measured=PerformanceProfile.Measure(ProfileStage.Vision);
            if(!VisionInputsUnchanged())
            {
                var actual=buildings.Values.Where(b=>b.Phase!=ConstructionPhase.Pending).Select(b=>new KnownBuilding(b.Id,TeamOf(b.Owner),b.Owner,b.Kind,b.Position,b.Heading,b.Upgrade?.Complete==true,CenterUpgraded(b.Id),b.ProducerUpgrade?.Complete==true)).ToArray();
                foreach(PlayableOwner owner in VisionTeamOwners())Vision(owner).Refresh(VisionSources(owner),actual);
                RememberVisionInputs();
            }
            // Ownership observations update even while units stand still during capture.
            foreach(PlayableOwner owner in VisionTeamOwners()){
                var vision=Vision(owner);
                foreach(var relay in sites.Values)
                    if(relay.Site.Kind==PlayableBuildingKind.RelayTower)
                        vision.ObserveRelay(relay.Site.Id,relay.Site.Position,RelayOwnerPaint(relay));
            }
        }
        private bool VisibleTarget(PlayableOwner observer,int id)
        {
            if(VisibleWreck(observer,id))return true;if(!Target(id,out var pos,out _)||wrecks.ContainsKey(id))return false;
            var owner=units.TryGetValue(id,out var unit)?unit.Owner:buildings[id].Owner;
            return Hostile(owner,observer)&&Vision(observer).IsVisible(pos);
        }
        // Only this projection may cross the player presentation boundary. Snapshot() is
        // an internal authority/test view and never published by PlayableRuntime.
        internal PlayableSnapshot PlayerSnapshot(long sequence,RuntimeStatus status,bool paused,PlayableRuntimeMetrics metrics,string failure,int seed,PlayableOwner owner=PlayableOwner.Player)
            =>PlayerSnapshotWithRoutes(sequence,status,paused,metrics,failure,seed,owner,null);
        internal PlayableSnapshot PrimaryFrameSnapshot(long sequence,RuntimeStatus status,bool paused,
            PlayableRuntimeMetrics metrics,string failure,int seed,bool deferRows,ProjectionScope projection=null)
        {
            using (projection?.Enter())
                return ProjectPlayerSnapshot(sequence,status,paused,metrics,failure,seed,PlayableOwner.Player,null,false,deferRows:deferRows);
        }
        internal PlayableSnapshot PlayerSnapshotWithRoutes(long sequence,RuntimeStatus status,bool paused,PlayableRuntimeMetrics metrics,string failure,int seed,PlayableOwner owner,IReadOnlyList<PlayableRouteRequest> routeRequests)
            =>ProjectPlayerSnapshot(sequence,status,paused,metrics,failure,seed,owner,routeRequests,false);
        internal PlayableSnapshot PlayerSnapshotForDelivery(long sequence,RuntimeStatus status,bool paused,PlayableRuntimeMetrics metrics,string failure,int seed,PlayableOwner owner,IReadOnlyList<PlayableRouteRequest> routes)
            =>ProjectPlayerSnapshot(sequence,status,paused,metrics,failure,seed,owner,routes,false,readyRoutesOnly:AsyncObservedPathsEnabled);
        internal PlayableSnapshot PlayerSnapshotForAi(long sequence,RuntimeStatus status,bool paused,PlayableRuntimeMetrics metrics,string failure,int seed,PlayableOwner owner,bool armyRoutes=true)
            =>PlayerSnapshotForArmyAi(sequence,status,paused,metrics,failure,seed,owner,armyRoutes,null);
        internal PlayableSnapshot PlayerSnapshotForArmyAi(long sequence,RuntimeStatus status,bool paused,PlayableRuntimeMetrics metrics,string failure,int seed,PlayableOwner owner,bool armyRoutes,AiArmyRegistryState armyState)
            =>PlayerSnapshotForAiReview(sequence,status,paused,metrics,failure,seed,owner,armyRoutes,armyState,false);
        internal PlayableSnapshot PlayerSnapshotForAiReview(long sequence,RuntimeStatus status,bool paused,PlayableRuntimeMetrics metrics,string failure,int seed,PlayableOwner owner,bool armyRoutes,AiArmyRegistryState armyState,bool territorialRoutes,bool victorySearch=false,Func<int,NavPoint,bool> rejectedRelayApproach=null,IReadOnlyList<NavPoint> knownVictoryOutposts=null)
            =>ProjectPlayerSnapshot(sequence,status,paused,metrics,failure,seed,owner,null,true,armyRoutes,armyState,territorialRoutes:territorialRoutes,victorySearch:victorySearch,
                rejectedRelayApproach:rejectedRelayApproach,knownVictoryOutposts:knownVictoryOutposts);
        private PlayableSnapshot ProjectPlayerSnapshot(long sequence,RuntimeStatus status,bool paused,PlayableRuntimeMetrics metrics,string failure,int seed,PlayableOwner owner,IReadOnlyList<PlayableRouteRequest> routeRequests,bool expansionRoutes,bool armyRoutes=false,AiArmyRegistryState armyState=null,bool fullOverview=false,bool teamSignals=true,bool readyRoutesOnly=false,bool territorialRoutes=false,bool victorySearch=false,Func<int,NavPoint,bool> rejectedRelayApproach=null,IReadOnlyList<NavPoint> knownVictoryOutposts=null,bool deferReviewRoutes=false,bool deferGroundTrails=false,bool deferRows=false)
        {
#if UNITY_INCLUDE_TESTS
            using var costReadback=new SnapshotCostScopeForTests(SnapshotCostObserverForTests,"WholeProjection");
#endif
            using var measured=PerformanceProfile.Measure(ProfileStage.Snapshot);
            // No authority mutation can interleave this synchronous publication.
            // All team vision and relay observations are refreshed together.
            RefreshProjectionVision();
            var v=fullOverview?FullOverviewVision():Vision(owner);
#if UNITY_INCLUDE_TESTS
            var information=SnapshotValueForTests("VisionSnapshot",()=>ProjectionVision(v));
#else
            var information=ProjectionVision(v);
#endif
            // Multiple units of one kind use the same research on this publication.
            var researchStats = new Dictionary<(PlayableOwner,PlayableEntityKind),(double MaxHealth,int Weapon,int Defense)>();
            HashSet<int> endpoints = projectiles.Count == 0 ? null : projectionBatch?.ProjectileEndpointIds ?? new HashSet<int>();
            endpoints?.Clear();
            var damagedCenters = centerDamage.Count == 0 ? null : new HashSet<int>();
            var visibleAttackers = centerDamage.Count == 0 ? null : new HashSet<int>();
            var es=new EntityFrameRows(deferRows);
            using(PerformanceProfile.Measure(ProfileStage.SnapshotUnits)){
            foreach(var u in units.Values)
            {
#if UNITY_INCLUDE_TESTS
                if(!TryActor(u.Id,out var n)||(!fullOverview&&u.Owner!=owner&&!SnapshotSampleValueForTests(0,"UnitVisibilitySample",()=>v.IsVisible(n.Position))))continue;
#else
                if(!TryActor(u.Id,out var n)||(!fullOverview&&u.Owner!=owner&&!v.IsVisible(n.Position)))continue;
#endif
                endpoints?.Add(u.Id);
                if (u.Owner != owner) visibleAttackers?.Add(u.Id);
                // The overview uses the primary player's owner context. Its unrestricted
                // vision can change a repair beam, so beam rows are always captured per view.
                var sharedRows = projectionBatch?.PublicUnits;
                if (sharedRows != null && !Hostile(owner,u.Owner))
                    sharedRows = owner == PlayableOwner.Player ? projectionBatch.PlayerUnits : null;
                if (u.Kind == PlayableEntityKind.Igla || u.Kind == PlayableEntityKind.Skat) sharedRows = null;
                if (sharedRows != null && sharedRows.TryGetValue(u.Id,out var sharedUnit)) {
                    es.AddShared(sharedUnit);
                    continue;
                }
                var key = (u.Owner,u.Kind);
                if (!researchStats.TryGetValue(key, out var stats)) {
#if UNITY_INCLUDE_TESTS
                    stats = (SnapshotSampleValueForTests(2,"UnitHealthResearchSample",()=>UnitMaximumHealth(u.Owner,u.Kind)),
                        SnapshotSampleValueForTests(3,"UnitWeaponResearchSample",()=>ArmySystemsLevel(u.Owner,true)),
                        SnapshotSampleValueForTests(4,"UnitDefenseResearchSample",()=>ArmySystemsLevel(u.Owner,false)));
#else
                    stats = (UnitMaximumHealth(u.Owner,u.Kind),ArmySystemsLevel(u.Owner,true),ArmySystemsLevel(u.Owner,false));
#endif
                    researchStats.Add(key,stats);
                }
                int target = u.Kind == PlayableEntityKind.Skat && !Frozen(u) &&
                    Target(u.DroneRepair.BeamTarget, out var repairPoint, out _) && v.IsVisible(repairPoint)
                    ? u.DroneRepair.BeamTarget : u.Owner==owner&&VisibleTarget(owner,u.Target)?u.Target:0;
                var order=u.Owner==owner?WreckWaitingOrder(u):null;
                if(order!=null&&order.Kind==PlayableTacticalOrderKind.Attack&&!VisibleTarget(owner,order.TargetId))order=null;
#if UNITY_INCLUDE_TESTS
                AddEntity(es,u.Id,u.Owner,u.Kind,n.Position,(int)Math.Ceiling(u.Health),n.Moving&&!Frozen(u),target,n.Heading,u.Turret,order,u.Kind==PlayableEntityKind.Hawk?HawkSalvo(u.Owner):UnitUpgraded(u),u.Owner==owner&&n.Held,u.Owner==owner?(WaitingForWreck(u.Id)?NavigationOutcome.Waiting:n.Outcome):NavigationOutcome.Idle,u.Owner==owner?u.LastOrder:null,SpatialCompletion(u.Id,owner),!Hostile(owner,u.Owner)&&u.Air==null?(NavLocation?)n.Location.Detached():null,SnapshotSampleValueForTests(1,"UnitQueueSample",()=>CaptureUnitQueue(u.Id,owner)),maxHealth:stats.MaxHealth,exactHealth:u.Health,weaponSystemsLevel:stats.Weapon,defensiveSystemsLevel:stats.Defense,altitude:u.Air?.Height??0,pitch:u.Air?.Pitch??0,roll:u.Air?.Roll??0,departing:u.Air?.Departing??false,weaponPitch:u.Kind==PlayableEntityKind.Shkval?u.ShkvalPitch:ExplorerPitch(u),weaponSequence:u.Kind==PlayableEntityKind.Shkval?u.BurstSequence%4:0,weaponReload:u.Kind==PlayableEntityKind.Shkval?u.Reload:0,shkvalCells:u.ShkvalCells==null?null:new ShkvalCellsSnapshot(u.ShkvalCells),materializationProgress:Materializing(u)?1-MaterializationRemaining(u)/u.MaterializationDuration:1,stasisRemaining:StasisRemaining(u),rocketLauncher:ExplorerRocketEquipped(u),iglaBeam:IglaBeam(u,owner),flames:Frozen(u)?null:FlamePose(u));
#else
                AddEntity(es,u.Id,u.Owner,u.Kind,n.Position,(int)Math.Ceiling(u.Health),n.Moving&&!Frozen(u),target,n.Heading,u.Turret,order,u.Kind==PlayableEntityKind.Hawk?HawkSalvo(u.Owner):UnitUpgraded(u),u.Owner==owner&&n.Held,u.Owner==owner?(WaitingForWreck(u.Id)?NavigationOutcome.Waiting:n.Outcome):NavigationOutcome.Idle,u.Owner==owner?u.LastOrder:null,SpatialCompletion(u.Id,owner),!Hostile(owner,u.Owner)&&u.Air==null?(NavLocation?)n.Location.Detached():null,CaptureUnitQueue(u.Id,owner),maxHealth:stats.MaxHealth,exactHealth:u.Health,weaponSystemsLevel:stats.Weapon,defensiveSystemsLevel:stats.Defense,altitude:u.Air?.Height??0,pitch:u.Air?.Pitch??0,roll:u.Air?.Roll??0,departing:u.Air?.Departing??false,weaponPitch:u.Kind==PlayableEntityKind.Shkval?u.ShkvalPitch:ExplorerPitch(u),weaponSequence:u.Kind==PlayableEntityKind.Shkval?u.BurstSequence%4:0,weaponReload:u.Kind==PlayableEntityKind.Shkval?u.Reload:0,shkvalCells:u.ShkvalCells==null?null:new ShkvalCellsSnapshot(u.ShkvalCells),materializationProgress:Materializing(u)?1-MaterializationRemaining(u)/u.MaterializationDuration:1,stasisRemaining:StasisRemaining(u),rocketLauncher:ExplorerRocketEquipped(u),iglaBeam:IglaBeam(u,owner),flames:Frozen(u)?null:FlamePose(u));
#endif
                sharedRows?.Add(u.Id,(es,es.CapturedCount-1));
            }
            }
            PerformanceProfile.Add(ProfileWork.SnapshotUnits,es.CapturedCount);
            var bs=new FrameRows<PlayableBuildingSnapshot>(deferRows);
            using(PerformanceProfile.Measure(ProfileStage.SnapshotBuildings)){
            foreach(var b in buildings.Values)
            {
                if(!fullOverview&&b.Owner!=owner&&(Hostile(b.Owner,owner)&&b.Phase==ConstructionPhase.Pending||!v.IsVisible(b.Position)))continue;
                endpoints?.Add(b.Id);
                if (b.Health > 0) damagedCenters?.Add(b.Id);
                bool own=fullOverview||b.Owner==owner;
                // Armed projections may hide a target for this particular viewer.
                bool allied=!Hostile(b.Owner,owner);
                bool share=projectionBatch!=null&&(own||!allied)&&b.Weapon==null&&!TerritoryRules.ArmedDefense(b.Kind)&&b.Kind!=PlayableBuildingKind.RepairTower;
                var buildingKey=(b.Id,own);
                if(share&&projectionBatch.Buildings.TryGetValue(buildingKey,out var sharedBuilding)){bs.AddShared(sharedBuilding);continue;}
                double duration=TerritoryRules.Duration(Terms(b.TermsRevision),b.Kind);
                AddBuilding(bs,b.Id,b.Owner,b.Kind,b.Position,(int)Math.Ceiling(b.Health),b.Ready?1:b.Build/duration,
                    own?b.Queue:0,own?b.ProductionProgress:0,own?b.Rally:default(NavPoint),b.SiteId,b.SlotId,b.ParentId,b.Phase,own?b.BlockedReason:null,b.Heading,own,own?ProductionSnapshot(b):null,own&&b.RepeatTank,own?LifecycleSnapshot(b):null,own?UpgradeSnapshot(b):null,b.Upgrade?.Complete==true,own?b.RepeatKind:PlayableEntityKind.Tank,b.Kind==PlayableBuildingKind.ScientificCenter&&own?ResearchSnapshot(b.Owner):null,own&&b.HasRally,own?PendingRally(b.Id):null,exactHealth:own?b.Health:(double?)null,launchPhase:b.LaunchPhase,launchTime:b.LaunchTime,launchKind:StagedUnitKind(b),launchUpgraded:b.Kind==PlayableBuildingKind.Hangar?HawkSalvo(b.Owner):FactoryStagedUpgraded(b),maxHealth:BuildingMaximumHealth(b.Owner,b.Kind),turret:TurretSnapshot(b,fullOverview?(PlayableOwner?)null:owner),healing:RepairTowerSnapshot(b,fullOverview?(PlayableOwner?)null:owner),scientificCenterUpgraded:CenterUpgraded(b.Id),producerUpgraded:b.ProducerUpgrade?.Complete==true,systemFailureRemaining:SystemFailureRemaining(b.Id),boostRemaining:BoostRemaining(b.Id),boostRepairing:allied&&b.Repair!=null,boostWorkRemaining:allied?BoostWorkRemaining(b):null,boostWearDamage:own&&boostWear.Contains(b.Id),boostSelling:allied&&b.Sale!=null,concreteLevel:ConcreteLevel(b.Owner),
                    weaponSystemsLevel:TerritoryRules.ArmedDefense(b.Kind)?ArmySystemsLevel(b.Owner,true):0);
                if(share)projectionBatch.Buildings.Add(buildingKey,(bs,bs.CapturedCount-1));
            }
            }
            PerformanceProfile.Add(ProfileWork.SnapshotBuildings,bs.CapturedCount);
            using var envelopeMeasured=PerformanceProfile.Measure(ProfileStage.SnapshotEnvelope);
            var ps=new List<PlayableProjectileSnapshot>();
            // Every projected building is owned, currently visible, or part of
            // the full overview. Remembered buildings live only in the vision.

            foreach(var p in projectiles)
            {
                bool visible=p.Faction==owner||v.IsVisible(p.Position);
                var r=p.Rocket;bool markerVisible=r!=null&&!r.BuildingTarget&&(p.Faction==owner||v.IsVisible(r.Predicted.Point.Ground));
                if(!visible&&!markerVisible)continue;
                int source=visible&&endpoints.Contains(p.Owner)?p.Owner:0;
                int target=visible&&endpoints.Contains(p.Target)?p.Target:0;
                if(r==null){ps.Add(new PlayableProjectileSnapshot(p.Id,source,target,p.Position,p.Kind,Math.Atan2(p.DirectionZ,p.DirectionX),height:p.Height,pitch:p.Spatial?Math.Atan2(p.Hawk?.Direction.Y??p.DirectionY,Math.Sqrt(Math.Pow(p.Hawk?.Direction.X??p.DirectionX,2)+Math.Pow(p.Hawk?.Direction.Z??p.DirectionZ,2))):Math.Atan(p.VerticalSlope),faction:p.Faction,directLaunchTick:source!=0?p.DirectLaunchTick:null,directBarrel:source!=0?p.DirectBarrel:0,rocket:p.Hawk!=null));continue;}
                double u=r.Elapsed/r.Flight.Duration;var point=r.Flight.Point(u);var tangent=r.Flight.Tangent(u);
                double progress=Math.Min(1,u/Math.Max(1e-5,r.Predicted.Progress));
                var marker=markerVisible?new PlayableImpactMarker(r.Predicted.Point.Ground,r.MarkerStartRadius+(r.BlastRadius-r.MarkerStartRadius)*progress,r.MarkerOpacity):null;
                ps.Add(new PlayableProjectileSnapshot(p.Id,source,target,visible?p.Position:default(NavPoint),p.Kind,visible?Math.Atan2(tangent.Z,tangent.X):0,visible?point.Y:0,visible?Math.Atan2(tangent.Y,Math.Sqrt(tangent.X*tangent.X+tangent.Z*tangent.Z)):0,p.Faction,r.Elapsed,visible,marker,visible&&r.Launch!=null&&(p.Faction==owner||source!=0&&v.IsVisible(r.Launch.Ground.Ground))?r.Launch:null));
            }
            var publicMeasured=PerformanceProfile.Measure(ProfileStage.SnapshotPublic);
            var allSites=projectionBatch==null?SiteSnapshots():(projectionBatch.Sites??(projectionBatch.Sites=SiteSnapshots()));
            // Public map markers get owner only. World actions and AI keep the sight-filtered sites.
            var mapRelayControl=projectionBatch?.RelayControl;
            if(mapRelayControl==null){
                mapRelayControl=allSites.Where(s=>s.Site.Kind==PlayableBuildingKind.RelayTower)
                    .Select(s=>new KnownRelay(s.Site.Id,s.Owner)).ToArray();
                if(projectionBatch!=null)projectionBatch.RelayControl=mapRelayControl;
            }
            var publicSites=projectionBatch?.PublicSites;
            if(publicSites==null){
                publicSites=sites.Values.Select(s=>s.Site).ToArray();
                if(projectionBatch!=null)projectionBatch.PublicSites=publicSites;
            }
            var liveSites=allSites.Where(s=>v.IsVisible(s.Site.Position)).Select(s=>
            {
                bool hiddenRequest=s.CenterId!=0&&buildings.TryGetValue(s.CenterId,out var b)&&b.Phase==ConstructionPhase.Pending&&b.Owner!=owner&&!fullOverview;
                if(hiddenRequest)
                    return new TerritorySiteSnapshot(s.Site,s.Claimant,null,s.Progress,s.Contested,0,false,
                        mineReserve:s.MineReserve,mineReserveCapacity:s.MineReserveCapacity);
                if(s.MineReserve.HasValue&&s.CenterId!=0&&s.Owner.HasValue&&Hostile(s.Owner.Value,owner)&&!fullOverview)
                    return new TerritorySiteSnapshot(s.Site,s.Claimant,s.Owner,s.Progress,s.Contested,s.CenterId,s.Ready,
                        s.ControlledSeconds,mineDepleted:s.MineDepleted);
                return s;
            }).ToArray();
            var discovered=sites.Values.Where(s=>v.IsDiscovered(s.Site.Position)).Select(s=>s.Site).ToArray();
            var objectives=PublicObjectives(owner);
            var victoryOutposts=armyRoutes&&(victorySearch||knownVictoryOutposts?.Count>0)
                ?objectives.Where(g=>g.Role==PlayablePublicScoutObjectiveRole.ExpansionOutpost&&
                    (victorySearch||knownVictoryOutposts.Contains(sites[g.SiteId].Site.Position))).Select(g=>g.SiteId).ToHashSet()
                :null;
            var territorialPositions=armyRoutes&&territorialRoutes?PlayableTerritorialPositions.Known(profile,v.IsDiscovered,liveSites,objectives,
                bs.FirstOrDefault(b=>b.Owner==owner&&b.SiteId==HomeSite(owner)&&b.Kind==PlayableBuildingKind.Headquarters)?.Position,
                other=>Hostile(other,owner)).ToArray():Array.Empty<(int Id, NavPoint Point)>();
            publicMeasured.Dispose();
            using(PerformanceProfile.Measure(ProfileStage.SnapshotRequests)){
            if(expansionRoutes&&!deferReviewRoutes)
                routeRequests=ReviewRequests(profile,es,bs,liveSites,objectives,owner,navigation.Generation,Tick,
                    armyRoutes,armyState,territorialPositions,victoryOutposts,other=>Hostile(other,owner),
                    !paused&&status==RuntimeStatus.Running&&Outcome==PlayableMatchOutcome.Playing&&!eliminated.Contains(owner));
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
            int relayRecipients=expansionRoutes||fullOverview?0:RelayRecipients(owner);
            var relayIncome=relayRecipients!=0&&liveSites.Any(s=>s.Site.Kind==PlayableBuildingKind.RelayTower&&
                s.Owner==owner&&s.Progress>=1&&!s.Contested)
                ?Owners.Where(candidate=>(relayRecipients&(1<<(int)candidate))!=0)
                    .ToDictionary(candidate=>candidate,candidate=>Income(candidate)/profile.IncomePeriodSeconds)
                :null;
            using var payloadMeasured=PerformanceProfile.Measure(ProfileStage.SnapshotPayload);
#if UNITY_INCLUDE_TESTS
            var result = new PlayableSnapshot(profile.ProfileId,profile.Revision,navigation.Generation,seed,sequence,Tick,status,paused,Outcome,
                (int)Math.Floor(Balance(owner)),projectileGeometry,deferRows?Array.Empty<PlayableEntitySnapshot>():es.ToArray(),deferRows?Array.Empty<PlayableBuildingSnapshot>():bs.ToArray(),ps.ToArray(),metrics,failure,liveSites,
                Income(owner)/profile.IncomePeriodSeconds,information,discovered,SnapshotValueForTests("Population",()=>ProjectionPopulation(owner)),impacts.Where(p=>(fullOverview||(p.VisibleMask&(1<<(int)owner))!=0)&&v.IsVisible(p.Point.Ground)).Select(p=>new PlayableImpactSnapshot(p.Id,p.Owner,p.Point,p.Radius,p.Tick,fullOverview?255:1<<(int)owner)).ToArray(),researchAvailability:SnapshotValueForTests("ResearchAvailability",()=>ResearchAvailability(owner)),ownerResearch:SnapshotValueForTests("ResearchSnapshot",()=>ResearchSnapshot(owner)),publicScoutObjectives:objectives,
                ownCenterDamage:centerDamage.Values.Where(d=>d.Owner==owner&&damagedCenters.Contains(d.CenterId)&&visibleAttackers.Contains(d.AttackerId)).OrderBy(d=>d.CenterId).ThenBy(d=>d.AttackerId).Select(d=>d.Copy()).ToArray(),
                routeProofs:deferReviewRoutes?null:SnapshotValueForTests("Routes",()=>ProjectRoutesIncludingTerritory(owner,routeRequests,v,es,bs,armyState,expansionRoutes||readyRoutesOnly,readyRoutesOnly,territorialPositions,victoryOutposts,rejectedRelayApproach)),
                artillerySupport:ProjectArtillery(owner,v,es,bs,armyState),owner:owner,exactCredits:Balance(owner),ownerId:offline==null?null:offline.Roster[(int)owner].Id,team:TeamOf(owner),participants:offline?.Roster.ToArray(),activeProfile:profile,publicSites:publicSites,mapRelayControl:mapRelayControl,relayTransferRecipients:relayRecipients,relayRecipientIncome:relayIncome,domination:DominationSnapshot(),homeSiteId:HomeSite(owner),ownerEliminated:eliminated.Contains(owner),settledIncome:SettledIncome(owner),mission:owner==PlayableOwner.Player?CampaignProjection():null,intelEnvelopes:PublicIntelEnvelopes(),groundTrails:expansionRoutes||deferGroundTrails?null:ProjectionGroundTrails(fullOverview?null:information),sounds:expansionRoutes?null:Sounds(fullOverview?(PlayableOwner?)null:owner),incomeEvents:expansionRoutes||fullOverview?null:IncomeEvents(owner),wrecks:WreckSnapshots(fullOverview?(PlayableOwner?)null:owner),buildingDestructions:BuildingDestructions(fullOverview?(PlayableOwner?)null:owner)).WithPresentation(MineSnapshots(fullOverview?null:information),expansionRoutes?Array.Empty<OrbitalMineBlastSnapshot>():MineBlastSnapshots(fullOverview?null:information),OrbitalView(owner),TeamOrbitalScans(owner),expansionRoutes?Array.Empty<OrbitalScanLine>():ScanLines(information),expansionRoutes?null:ScanTriangles(information),teamSignals&&!expansionRoutes&&!fullOverview?ProjectTeamSignals(owner):null,expansionRoutes?null:MatchEvents(fullOverview?(PlayableOwner?)null:owner));
#else
            var result = new PlayableSnapshot(profile.ProfileId,profile.Revision,navigation.Generation,seed,sequence,Tick,status,paused,Outcome,
                (int)Math.Floor(Balance(owner)),projectileGeometry,deferRows?Array.Empty<PlayableEntitySnapshot>():es.ToArray(),deferRows?Array.Empty<PlayableBuildingSnapshot>():bs.ToArray(),ps.ToArray(),metrics,failure,liveSites,
                Income(owner)/profile.IncomePeriodSeconds,information,discovered,ProjectionPopulation(owner),impacts.Where(p=>(fullOverview||(p.VisibleMask&(1<<(int)owner))!=0)&&v.IsVisible(p.Point.Ground)).Select(p=>new PlayableImpactSnapshot(p.Id,p.Owner,p.Point,p.Radius,p.Tick,fullOverview?255:1<<(int)owner)).ToArray(),researchAvailability:ResearchAvailability(owner),ownerResearch:ResearchSnapshot(owner),publicScoutObjectives:objectives,
                ownCenterDamage:centerDamage.Values.Where(d=>d.Owner==owner&&damagedCenters.Contains(d.CenterId)&&visibleAttackers.Contains(d.AttackerId)).OrderBy(d=>d.CenterId).ThenBy(d=>d.AttackerId).Select(d=>d.Copy()).ToArray(),
                routeProofs:deferReviewRoutes?null:ProjectRoutesIncludingTerritory(owner,routeRequests,v,es,bs,armyState,expansionRoutes||readyRoutesOnly,readyRoutesOnly,territorialPositions,victoryOutposts,rejectedRelayApproach),
                artillerySupport:ProjectArtillery(owner,v,es,bs,armyState),owner:owner,exactCredits:Balance(owner),ownerId:offline==null?null:offline.Roster[(int)owner].Id,team:TeamOf(owner),participants:offline?.Roster.ToArray(),activeProfile:profile,publicSites:publicSites,mapRelayControl:mapRelayControl,relayTransferRecipients:relayRecipients,relayRecipientIncome:relayIncome,domination:DominationSnapshot(),homeSiteId:HomeSite(owner),ownerEliminated:eliminated.Contains(owner),settledIncome:SettledIncome(owner),mission:owner==PlayableOwner.Player?CampaignProjection():null,intelEnvelopes:PublicIntelEnvelopes(),groundTrails:expansionRoutes||deferGroundTrails?null:ProjectionGroundTrails(fullOverview?null:information),sounds:expansionRoutes?null:Sounds(fullOverview?(PlayableOwner?)null:owner),incomeEvents:expansionRoutes||fullOverview?null:IncomeEvents(owner),wrecks:WreckSnapshots(fullOverview?(PlayableOwner?)null:owner),buildingDestructions:BuildingDestructions(fullOverview?(PlayableOwner?)null:owner)).WithPresentation(MineSnapshots(fullOverview?null:information),expansionRoutes?Array.Empty<OrbitalMineBlastSnapshot>():MineBlastSnapshots(fullOverview?null:information),OrbitalView(owner),TeamOrbitalScans(owner),expansionRoutes?Array.Empty<OrbitalScanLine>():ScanLines(information),expansionRoutes?null:ScanTriangles(information),teamSignals&&!expansionRoutes&&!fullOverview?ProjectTeamSignals(owner):null,expansionRoutes?null:MatchEvents(fullOverview?(PlayableOwner?)null:owner));
#endif
            if (deferRows) result.SetFrameRows(es.Read,bs.Read);
            return result;
        }
        private static IReadOnlyList<PlayableRouteRequest> ReviewRequests(PlayableProfile profile,
            IReadOnlyList<PlayableEntitySnapshot> es,IReadOnlyList<PlayableBuildingSnapshot> bs,
            IReadOnlyList<TerritorySiteSnapshot> liveSites,IReadOnlyList<PlayablePublicScoutObjective> objectives,
            PlayableOwner owner,long generation,long tick,bool armyRoutes,AiArmyRegistryState armyState,
            IReadOnlyList<(int Id,NavPoint Point)> territorialPositions,ISet<int> victoryOutposts,
            Func<PlayableOwner,bool> hostile,bool playing)
        {
            var assignedScouts=armyRoutes&&armyState!=null?armyState.Armies.Where(a=>a.Role==AiArmyRole.Scout&&
                a.TacticalOwner==AiScoutPlanner.Policy&&a.Phase!=AiArmyPhase.Disbanded).Select(a=>a.LeaderId).ToArray():Array.Empty<int>();
            var scoutEscapes=es.Where(e=>e.Owner==owner&&assignedScouts.Contains(e.Id))
                .SelectMany(e=>bs.Where(b=>AiReinforcementPolicy.Home(owner,b))
                    .Select(b=>new PlayableRouteRequest(e.Id,PlayableRouteTargetKind.FriendlyAnchor,b.Id,generation,tick,
                        e.Position,PlayableUnitRules.Radius(profile,e.Kind),b.Position)));
            {
                var distinct=new HashSet<(int Unit,PlayableRouteTargetKind Kind,int Target,double X,double Z)>();
                var ordered=new List<PlayableRouteRequest>();
                void Append(IEnumerable<PlayableRouteRequest> requests){
                    foreach(var request in requests)
                        if(distinct.Add((request.UnitId,request.Kind,request.TargetId,request.Target.X,request.Target.Z)))
                            ordered.Add(request);
                }
                Append(AiExpansionPlanner.RouteRequests(es,liveSites,objectives,owner,generation,tick,profile));
                if(armyRoutes){
                    Append(AiScoutPlanner.RouteRequests(es,objectives,owner,generation,tick,profile,assignedScouts));
                    Append(scoutEscapes);
                    Append(AiArmyPlanner.RouteRequests(es,bs,objectives,owner,generation,tick,profile,hostile,territorialPositions,victoryOutposts,armyState));
                    Append(AiTacticalExecutor.RouteRequests(es,bs,armyState,owner,generation,tick,profile));
                }else if(playing)
                    Append(AiTacticalExecutor.AssemblyRouteRequests(es,armyState,owner,generation,tick,profile));
                return ordered;
            }
        }

        internal PlayableSnapshot CaptureDecisionView(long sequence,RuntimeStatus status,PlayableRuntimeMetrics metrics,int seed,PlayableOwner owner)
            =>ProjectPlayerSnapshot(sequence,status,false,metrics,null,seed,owner,null,true,deferReviewRoutes:true);

        internal Func<PlayableAiOwnerLoop,PlayableSnapshot> CaptureDecisionRoutes(PlayableSnapshot view)
        {
            var facts=CaptureRouteInputs(view.Owner,Vision(view.Owner),view);
#if UNITY_INCLUDE_TESTS
            var readback=RouteRequestsReadbackForTests;
#endif
            return bot=>{
                facts.PrepareGeometry();
                bool armyRoutes=bot.NeedsArmyRoutes(view.Tick)||bot.NeedsDefenseRoutes(view);
                var registry=bot.ArmyState;
                var known=bot.KnownVictoryOutposts;
                var objectives=view.PublicScoutObjectives;
                var sites=view.Sites;
                var victory=armyRoutes&&(bot.NeedsVictorySearch||known.Length>0)
                    ?objectives.Where(o=>o.Role==PlayablePublicScoutObjectiveRole.ExpansionOutpost &&
                        (bot.NeedsVictorySearch||known.Contains(facts.Site(o.SiteId).Position))).Select(o=>o.SiteId).ToHashSet():null;
                var positions=armyRoutes&&bot.NeedsTerritorialRoutes?PlayableTerritorialPositions.Known(facts.Profile,
                    view.Vision.IsDiscovered,sites,objectives,
                    view.Buildings.FirstOrDefault(b=>b.Owner==view.Owner&&b.SiteId==view.HomeSiteId&&b.Kind==PlayableBuildingKind.Headquarters)?.Position,
                    facts.Hostile).ToArray():Array.Empty<(int Id,NavPoint Point)>();
                var requests=ReviewRequests(facts.Profile,view.Entities,view.Buildings,sites,objectives,view.Owner,view.Generation,
                    view.Tick,armyRoutes,registry,positions,victory,facts.Hostile,view.Outcome==PlayableMatchOutcome.Playing&&!view.OwnerEliminated);
#if UNITY_INCLUDE_TESTS
                if(readback!=null&&(facts.OwnerFilter==null||facts.OwnerFilter==view.OwnerId))
                    readback(view.OwnerId,view.Tick,requests.Where(r=>!facts.CaptureExpansion||r.Kind==PlayableRouteTargetKind.PublicObjective).ToArray());
#endif
                var rejected=bot.RejectedDominationApproaches(view.Tick);
                // A frozen observation can take several waves to settle alternatives.
                // Keep candidate jobs and completed proofs alive across the whole observation.
                var settled = new Dictionary<PlayableRouteRequest, PlayableRouteProof>();
                facts.Lane.BeginProjection(view.Owner);
                try
                {
                    while (true)
                    {
                        try
                        {
                            var proofs = ComputeRoutes(facts, view.Owner, requests, view.Entities, view.Buildings,
                                registry, true, false, positions, victory, rejected, settled);
                            return view.WithDecisionRoutes(proofs);
                        }
                        catch (ObservedPathPending)
                        {
                            // Only the detached review worker waits for route work.
                            facts.Lane.DrainForCapture(null);
                        }
                    }
                }
                finally { facts.Lane.EndProjection(view.Owner); }
            };
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
        internal PlayableGroundTrail[] CapturePresentationTrails()
            =>groundTrails.Visible(Tick,null,profile.GroundTrailLifetimeTicks);

        internal PlayableSnapshot CapturePresentationView(long sequence,RuntimeStatus status,bool paused,
            PlayableRuntimeMetrics metrics,string failure,int seed,PlayableOwner owner,bool overview=false)
            =>ProjectPlayerSnapshot(sequence,status,paused,metrics,failure,seed,owner,null,false,
                fullOverview:overview,deferGroundTrails:true,deferRows:true);

        internal PlayableSpectatorFrame SpectatorSnapshot(long sequence,RuntimeStatus status,bool paused,PlayableRuntimeMetrics metrics,string failure,int seed)
        {
            // One synchronous authority read. No state/callback can run between
            // perspectives; nothing is reused across publications or restores.
            var previous=projectionBatch;
#if UNITY_INCLUDE_TESTS
            if(!DisableProjectionBatchForTests)
#endif
                projectionBatch=new ProjectionBatch(includesOverview:true);
            try{
                var perspectives=Owners.Select(owner=>ProjectPlayerSnapshot(sequence,status,paused,metrics,failure,seed,owner,null,false)).ToArray();
                var overview=ProjectPlayerSnapshot(sequence,status,paused,metrics,failure,seed,PlayableOwner.Player,null,false,fullOverview:true);
                return new PlayableSpectatorFrame(overview,perspectives);
            }finally{projectionBatch=previous;}
        }
        // Synchronous observer memoization is confined to authority. Async lanes own
        // detached solver inputs/results; settled lane answers persist in runtime transport.
        private readonly ObservedPathWorkPool observedPathPool=new ObservedPathWorkPool();
        private ObservedPathLane observedPathLane,armySafetyPathLane;
        internal void EnableAsyncObservedPaths(){if(observedPathLane==null)observedPathLane=new ObservedPathLane();else observedPathLane.Start();armySafetyPathLane??=new ObservedPathLane(false);}
        internal void StopObservedPaths(){observedPathLane?.Stop();armySafetyPathLane?.Stop();}
        internal bool AsyncObservedPathsEnabled=>observedPathLane!=null;
        internal bool ObservedPathsReadyForCapture=>(observedPathLane?.ReadyForCapture??true)&&(armySafetyPathLane?.ReadyForCapture??true);
        internal void DrainObservedPathsForCapture(Func<bool> cancelled){observedPathLane?.DrainForCapture(cancelled);armySafetyPathLane?.DrainForCapture(cancelled);}
        internal void WriteObservedPathState(System.IO.BinaryWriter w){w.Write(observedPathLane!=null);observedPathLane?.WriteState(w);w.Write(armySafetyPathLane!=null);armySafetyPathLane?.WriteState(w);}
        internal void ReadObservedPathState(System.IO.BinaryReader r,bool includesSafety){if(WorldWire.Boolean(r))observedPathLane=ObservedPathLane.ReadState(r);if(includesSafety&&WorldWire.Boolean(r))armySafetyPathLane=ObservedPathLane.ReadState(r);}
        internal bool ObservedPathWorkerStopped=>(observedPathLane==null||observedPathLane.IsStopped)&&(armySafetyPathLane==null||armySafetyPathLane.IsStopped);
        private sealed class ObservedGeometryCache
        {
            internal NavGeometry Authority;
            internal bool[] AuthoredRows;
            internal readonly Dictionary<(double MinX,double MinZ,double MaxX,double MaxZ),List<NavObstacle>> AuthoredByBounds
                =new Dictionary<(double,double,double,double),List<NavObstacle>>();
            internal readonly List<NavObstacle> Scratch=new List<NavObstacle>();
            internal readonly Dictionary<object,NavGeometry> Views=new Dictionary<object,NavGeometry>();

            internal ObservedGeometryCache(IReadOnlyList<NavObstacle> obstacles)
            {
                if(obstacles==null)return;
                foreach(var obstacle in obstacles){
                    var key=Bounds(obstacle);
                    if(!AuthoredByBounds.TryGetValue(key,out var candidates))
                        AuthoredByBounds.Add(key,candidates=new List<NavObstacle>());
                    candidates.Add(obstacle);
                }
            }

            private static (double,double,double,double) Bounds(NavObstacle obstacle)
                =>(obstacle.MinX,obstacle.MinZ,obstacle.MaxX,obstacle.MaxZ);

            internal bool IsAuthored(NavObstacle obstacle)
            {
                if(!AuthoredByBounds.TryGetValue(Bounds(obstacle),out var candidates))return false;
                foreach(var candidate in candidates)
                    if(candidate.GeometryEquals(obstacle))return true;
                return false;
            }
        }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PlayableDomain,ObservedGeometryCache> observedGeometryCaches=new System.Runtime.CompilerServices.ConditionalWeakTable<PlayableDomain,ObservedGeometryCache>();
        private ObservedGeometryCache NewObservedGeometryCache()
            =>new ObservedGeometryCache(offline?.Obstacles??PlayableMap.StaticObstacles(profile));
        private SharedFlowRouter ObservedRouter(PlayableOwner owner,NavGeometry observed,NavigationProfile basis,double radius,double speed,double turn)
            =>observedPathPool.Router(owner,observed,basis,radius,speed,turn);
        private NavGeometry ObservedGeometry(PlayableVision vision)
        {
            // The observer solves on visible solids with the production navigation solver, then
            // checks every segment against authoritative solids. Hidden geometry can only remove proof.
            var cache=observedGeometryCaches.GetValue(this,domain=>domain.NewObservedGeometryCache());
            return BuildObservedGeometry(cache,Geometry,vision,vision.IsVisible);
        }
        private NavGeometry OwnerMovementGeometry(int entity, NavGeometry basis)
        {
            if (!units.TryGetValue(entity, out var unit)) return basis;
            var vision = Vision(unit.Owner);
            var cache = observedGeometryCaches.GetValue(this, domain => domain.NewObservedGeometryCache());
            return BuildObservedGeometry(cache, basis, vision, vision.IsVisible);
        }

        private static NavGeometry BuildObservedGeometry(ObservedGeometryCache cache,NavGeometry geometry,object key,
            Func<NavPoint,bool> visible,bool shareExactView=false)
        {
            // Authoritative geometry and authored map solids are immutable. Resolve the
            // public-map membership once per geometry, not for every obstacle on every tick.
            if(!ReferenceEquals(cache.Authority,geometry)){
                var authoredRows=new bool[geometry.Obstacles.Count];
                for(int i=0;i<authoredRows.Length;i++)authoredRows[i]=cache.IsAuthored(geometry.Obstacles[i]);
                cache.AuthoredRows=authoredRows;cache.Authority=geometry;
            }
            cache.Views.TryGetValue(key,out var previous);
            var observed=cache.Scratch;observed.Clear();
            bool unchanged=previous!=null&&previous.HalfExtent==geometry.HalfExtent;
            for(int i=0;i<geometry.Obstacles.Count;i++){
                var obstacle=geometry.Obstacles[i];
                if(!cache.AuthoredRows[i]&&!ObstacleCornersVisible(obstacle,visible))continue;
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
            NavGeometry result=null;
            if(shareExactView)
            {
                // Other owners can see the same solids, especially on an allied
                // team. Compare the full ordered geometry before sharing a key.
                foreach(var view in cache.Views.Values)
                {
                    if(view.Revision!=observedRevision||view.HalfExtent!=geometry.HalfExtent||
                        view.Obstacles.Count!=observed.Count)continue;
                    int i=0;
                    while(i<observed.Count&&observed[i].GeometryEquals(view.Obstacles[i]))i++;
                    if(i==observed.Count){result=view;break;}
                }
            }
            result??=new NavGeometry(geometry.HalfExtent,observed.ToArray(),observedRevision);
            if(cache.Views.Count>=8&&!cache.Views.ContainsKey(key))cache.Views.Clear();
            cache.Views[key]=result;return result;
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
        private static NavPoint[] RelayApproaches(PlayableProfile profile,TerritorySite relay)
        {
            double radius=Math.Max(0,profile.RelayCaptureRadius-profile.ExplorerCollisionRadius);
            return Enumerable.Range(1,7).Select(i=>new NavPoint(
                relay.Position.X+radius*Math.Cos(i*Math.PI/4),
                relay.Position.Z+radius*Math.Sin(i*Math.PI/4))).ToArray();
        }

        private PlayableRouteProof[] ProjectRoutes(PlayableOwner owner,IReadOnlyList<PlayableRouteRequest> requests,PlayableVision vision,IReadOnlyList<PlayableEntitySnapshot> entities,IReadOnlyList<PlayableBuildingSnapshot> buildings,Spacewars.Runtime.AiArmyRegistryState armyState,bool asynchronousObservation)
            =>ProjectRoutesCore(owner,requests,vision,entities,buildings,armyState,asynchronousObservation,false);
        private PlayableRouteProof[] ProjectRoutesCore(PlayableOwner owner,IReadOnlyList<PlayableRouteRequest> requests,PlayableVision vision,IReadOnlyList<PlayableEntitySnapshot> entities,IReadOnlyList<PlayableBuildingSnapshot> buildings,AiArmyRegistryState armyState,bool asynchronousObservation,bool readyOnly)
            =>ProjectRoutesIncludingTerritory(owner,requests,vision,entities,buildings,armyState,asynchronousObservation,readyOnly,null);
        private readonly struct RouteActor
        {
            internal readonly int Id;
            internal readonly PlayableOwner Owner;
            internal readonly PlayableEntityKind Kind;
            internal readonly double Health,Radius,Speed,TurnSpeed;
            internal readonly NavPoint Position,Goal;
            internal readonly NavigationOutcome Outcome;
            internal readonly long InstalledRevision,InstalledGroup;
            internal readonly PlayableTacticalOrderSnapshot CurrentOrder;
            internal RouteActor(Unit unit,NavUnit nav)
            {
                Id=unit.Id;Owner=unit.Owner;Kind=unit.Kind;Health=unit.Health;CurrentOrder=unit.CurrentOrder;
                Position=nav.Position;Goal=nav.Goal;Outcome=nav.Outcome;InstalledRevision=nav.InstalledRevision;InstalledGroup=nav.InstalledGroupId;Radius=nav.Radius;Speed=nav.Speed;TurnSpeed=nav.TurnSpeed;
            }
        }
        private sealed class RouteInputs
        {
            internal long Generation,Tick;
            internal PlayableProfile Profile;
            internal NavGeometry Geometry,Observed;
            internal Action PrepareGeometry;
            internal ObservedPathLane Lane;
            internal Func<int,RouteActor?> Actor;
            internal Func<NavPoint,bool> Visible;
            internal Func<PlayableOwner,bool> Hostile;
            internal IReadOnlyList<PlayablePublicScoutObjective> Objectives;
            internal Func<int,TerritorySite> Site;
            internal Func<int,NavPoint,double,NavPoint[]> PublicApproaches;
            internal ObservedPathWorkPool Pool;
            internal bool Recast,Domination;
#if UNITY_INCLUDE_TESTS
            internal string OwnerId,OwnerFilter;
            internal bool CaptureExpansion;
            internal Action BeforePath,OnFriendlyCheck;
            internal Action<PlayableRouteProjectionReadback> Readback;
#endif
        }
        private RouteActor? ReadRouteActor(int id)
        {
            if(units.TryGetValue(id,out var unit)&&TryActor(id,out var nav))return new RouteActor(unit,nav);
            return null;
        }

        private ObservedGeometryCache workerGeometryCache;
        private RouteInputs CaptureRouteInputs(PlayableOwner owner,PlayableVision vision,PlayableSnapshot detached=null)
        {
            var observed=detached==null?ObservedGeometry(vision):null;
            var inputs=new RouteInputs{
                Generation=navigation.Generation,Tick=Tick,Profile=profile,Geometry=Geometry,Observed=observed,
                Pool=observedPathPool,Recast=navigation.UsesRecastGroundRoutes,Domination=DominationEnabled,
                Objectives=detached?.PublicScoutObjectives??PublicObjectives(owner),Lane=observedPathLane
            };
#if UNITY_INCLUDE_TESTS
            inputs.BeforePath=BeforeObservedPathForTests;
            inputs.Readback=RouteProjectionReadbackForTests;
            inputs.OwnerId=offline?.Roster[(int)owner].Id;inputs.OwnerFilter=RouteProjectionOwnerFilterForTests;
            inputs.CaptureExpansion=CaptureCurrentExpansionRoutesForTests;
#endif
            if(detached==null){
                inputs.Actor=ReadRouteActor;inputs.Visible=vision.IsVisible;inputs.Hostile=other=>Hostile(other,owner);
                inputs.Site=id=>sites.TryGetValue(id,out var site)?site.Site:null;
                inputs.PublicApproaches=(id,at,r)=>PublicArmyApproaches(owner,id,at,observed,r);
#if UNITY_INCLUDE_TESTS
                inputs.OnFriendlyCheck=()=>FriendlyApproachChecksForTests++;
#endif
                return inputs;
            }
            var geometry=DecisionGeometry();
            inputs.Geometry=null;
            var cache=workerGeometryCache??(workerGeometryCache=NewObservedGeometryCache());
            inputs.PrepareGeometry=()=>{
                inputs.Geometry=geometry.Value;
                inputs.Observed=BuildObservedGeometry(cache,inputs.Geometry,owner,detached.Vision.IsVisible,shareExactView:true);
            };
            inputs.Pool=null;
            var actors=new Dictionary<int,RouteActor>();
            foreach(var entity in detached.Entities){
                if(entity.Owner!=owner)continue;
                var actor=ReadRouteActor(entity.Id);
                if(actor.HasValue)actors.Add(entity.Id,actor.Value);
            }
            var hostiles=Owners.Where(other=>Hostile(other,owner)).ToHashSet();
            var geography=detached.PublicSites.ToDictionary(site=>site.Id);
            var approaches=new Dictionary<(int,NavPoint,double),NavPoint[]>();
            var terms=profile;
            inputs.Actor=id=>actors.TryGetValue(id,out var value)?value:(RouteActor?)null;
            inputs.Visible=detached.Vision.IsVisible;inputs.Hostile=hostiles.Contains;
            inputs.Site=id=>geography.TryGetValue(id,out var site)?site:null;
            inputs.PublicApproaches=(id,at,r)=>{
                var key=(id,at,r);
                if(!approaches.TryGetValue(key,out var points))
                    approaches.Add(key,points=ComputePublicApproaches(terms,inputs.Site(id),at,inputs.Observed,r));
                return points;
            };
            return inputs;
        }
        private PlayableRouteProof[] ProjectRoutesIncludingTerritory(PlayableOwner owner,IReadOnlyList<PlayableRouteRequest> requests,PlayableVision vision,IReadOnlyList<PlayableEntitySnapshot> entities,IReadOnlyList<PlayableBuildingSnapshot> buildings,AiArmyRegistryState armyState,bool asynchronousObservation,bool readyOnly,IReadOnlyList<(int Id, NavPoint Point)> territorialPositions,ISet<int> victoryOutposts=null,Func<int,NavPoint,bool> rejectedRelayApproach=null)
        {
            if(requests==null||requests.Count==0){
                if(asynchronousObservation&&!readyOnly){
                    observedPathLane?.BeginProjection(owner);observedPathLane?.EndProjection(owner);
                }
                return Array.Empty<PlayableRouteProof>();
            }
            var facts=CaptureRouteInputs(owner,vision);
            return ComputeRoutes(facts,owner,requests,entities,buildings,armyState,asynchronousObservation,readyOnly,
                territorialPositions,victoryOutposts,rejectedRelayApproach);
        }
        private static PlayableRouteProof[] ComputeRoutes(RouteInputs facts,PlayableOwner owner,
            IReadOnlyList<PlayableRouteRequest> requests,IReadOnlyList<PlayableEntitySnapshot> entities,
            IReadOnlyList<PlayableBuildingSnapshot> buildings,AiArmyRegistryState armyState,bool asynchronousObservation,bool readyOnly,
            IReadOnlyList<(int Id,NavPoint Point)> territorialPositions,ISet<int> victoryOutposts,Func<int,NavPoint,bool> rejectedRelayApproach,
            Dictionary<PlayableRouteRequest, PlayableRouteProof> settled = null)
        {
            var profile=facts.Profile;
            using var measured=PerformanceProfile.Measure(ProfileStage.Routes);
            var lane=asynchronousObservation?facts.Lane:null;
            if(!readyOnly && settled==null)lane?.BeginProjection(owner);
            try{
            if(requests==null||requests.Count==0)return Array.Empty<PlayableRouteProof>();
            bool pathWorkPending=false;
            long projectionBegin=routePerformanceTrace?System.Diagnostics.Stopwatch.GetTimestamp():0;
            // The observer solves on visible solids with the production navigation solver, then
            // checks every segment against authoritative solids. Hidden geometry can only remove proof.
            var safeGeometry=facts.Observed;
            int observedRevision=safeGeometry.Revision;
            var result=new List<PlayableRouteProof>();
            var bypassFormations=new Dictionary<(long Army,NavPoint Anchor),Dictionary<int,NavPoint>>();
            var bypassAnchors=new Dictionary<int,NavPoint[]>();
            FriendlyApproachOccupancy friendlyOccupancy=null;
            var actorsById=new Dictionary<int,PlayableEntitySnapshot>(entities.Count);
            for(int i=0;i<entities.Count;i++)actorsById.Add(entities[i].Id,entities[i]);
            var buildingsById=new Dictionary<int,PlayableBuildingSnapshot>(buildings.Count);
            for(int i=0;i<buildings.Count;i++)buildingsById.Add(buildings[i].Id,buildings[i]);
            // The public site remains the identity of the order. Choose its physical approach
            // from this owner's visible contacts or the public possible start positions.
            var bearings = new Dictionary<NavPoint, NavPoint?>();
            NavPoint? EnemySide(NavPoint center)
            {
                if (bearings.TryGetValue(center, out var bearing)) return bearing;
                bearing = AiPositionDevelopmentPlanner.ObjectiveThreat(center,entities,buildings,facts.Objectives,facts.Hostile,facts.Visible);
                bearings.Add(center, bearing);
                return bearing;
            }
            foreach(var request in requests)
            {
                if (settled != null && settled.TryGetValue(request, out var proof))
                {
                    if (proof != null) result.Add(proof);
                    continue;
                }
#if UNITY_INCLUDE_TESTS
                var observer=facts.Readback;
                int ownerIndex=(int)owner;
                bool capture=request!=null&&observer!=null&&facts.OwnerId!=null&&
                    (facts.CaptureExpansion&&facts.OwnerId==facts.OwnerFilter&&request.Kind==PlayableRouteTargetKind.PublicObjective||
                    facts.OwnerId=="ai-5"&&request.Tick==8424&&request.Kind==PlayableRouteTargetKind.VisibleEnemy&&request.TargetId==83&&
                    (request.UnitId==38||request.UnitId==52||request.UnitId==60||request.UnitId==89));
                string bindingRejection=null;NavPoint? resolvedTarget=null;bool? originVisible=null,targetVisible=null;
                var candidateReadbacks=capture?new List<PlayableRouteProjectionCandidateReadback>():null;
                PlayableRouteProjectionReadbackOutcome finalOutcome=PlayableRouteProjectionReadbackOutcome.BindingRejected;
#endif
                var actorFacts=request==null?null:facts.Actor(request.UnitId);
                var unit=actorFacts.GetValueOrDefault();
                var nav=unit;
#if UNITY_INCLUDE_TESTS
                if(capture)
                {
                    if(request.UnitId<=0)bindingRejection="unit-id";
                    else if(request.TargetId<=0)bindingRejection="target-id";
                    else if(request.Generation!=facts.Generation)bindingRejection="generation";
                    else if(request.Tick!=facts.Tick)bindingRejection="tick";
                    else if(!actorFacts.HasValue)bindingRejection="unit-missing";
                    else if(unit.Owner!=owner)bindingRejection="unit-owner";
                    else if(unit.Health<=0)bindingRejection="unit-dead";
                    else if(request.Origin.X!=nav.Position.X)bindingRejection="origin-x";
                    else if(request.Origin.Z!=nav.Position.Z)bindingRejection="origin-z";
                    else if(request.Radius!=nav.Radius)bindingRejection="radius";
                    if(bindingRejection!=null){PublishRouteProjectionReadback(observer,facts.OwnerId,owner,request,finalOutcome,bindingRejection,resolvedTarget,originVisible,targetVisible,0,safeGeometry.Obstacles.Count,0,candidateReadbacks);continue;}
                }
                else
#endif
                if(request==null||request.UnitId<=0||request.TargetId<=0||request.Generation!=facts.Generation||request.Tick!=facts.Tick||!actorFacts.HasValue||unit.Owner!=owner||unit.Health<=0||request.Origin.X!=nav.Position.X||request.Origin.Z!=nav.Position.Z||request.Radius!=nav.Radius)continue;
                NavPoint target,goal;bool buildingTarget=false,settledFollow=false,publicStart=false;double friendlyRadius=0;int friendlyVariant=0,approachVariant=0;
                TerritorySite relaySite=null;
                bool mineGuard=false;
                if(request.Kind==PlayableRouteTargetKind.VisibleEnemy||request.Kind==PlayableRouteTargetKind.EnemyApproach)
                {
                    actorsById.TryGetValue(request.TargetId,out var enemy);
                    if(enemy!=null&&(!facts.Hostile(enemy.Owner)||enemy.Health<=0))enemy=null;
                    buildingsById.TryGetValue(request.TargetId,out var enemyBuilding);
                    if(enemyBuilding!=null&&(!facts.Hostile(enemyBuilding.Owner)||enemyBuilding.Health<=0))enemyBuilding=null;
                    if(enemy==null&&enemyBuilding==null)
                    {
#if UNITY_INCLUDE_TESTS
                        if(capture){finalOutcome=PlayableRouteProjectionReadbackOutcome.TargetUnresolved;PublishRouteProjectionReadback(observer,facts.OwnerId,owner,request,finalOutcome,bindingRejection,resolvedTarget,originVisible,targetVisible,observedRevision,safeGeometry.Obstacles.Count,0,candidateReadbacks);}
#endif
                        continue;
                    }
                    if(request.Kind==PlayableRouteTargetKind.EnemyApproach&&enemyBuilding==null)continue;
                    target=goal=enemy!=null?enemy.Position:enemyBuilding.Position;buildingTarget=enemy==null;
                    if(request.Kind==PlayableRouteTargetKind.EnemyApproach)
                    {
                        if(!bypassAnchors.TryGetValue(enemyBuilding.Id,out var anchors))
                        {anchors=AiTurretBypassPolicy.Positions(profile,enemyBuilding,entities.Where(e=>e.Owner==owner));bypassAnchors.Add(enemyBuilding.Id,anchors);}
                        approachVariant=Array.IndexOf(anchors,request.Target);if(approachVariant<0)continue;
                        var army=armyState?.Armies.FirstOrDefault(a=>a.Phase!=AiArmyPhase.Disbanded&&AiArmyPlanner.IsArmyPolicy(a.TacticalOwner)&&a.Members.Contains(unit.Id)&&!a.Reinforcements.Contains(unit.Id));
                        if(army==null)continue;
                        var key=(army.Id,request.Target);
                        if(!bypassFormations.TryGetValue(key,out var formation))
                        {
                            formation=new Dictionary<int,NavPoint>();bypassFormations.Add(key,formation);
                            var members=entities.Where(e=>e.Owner==owner&&e.Health>0&&army.Members.Contains(e.Id)&&!army.Reinforcements.Contains(e.Id)).OrderBy(e=>e.Id).ToArray();
                            var ground=members.Where(e=>!PlayableUnitRules.Aircraft(e.Kind)).ToArray();
                            if(ground.Length>0)
                            {
                                var largest=ground.OrderByDescending(e=>PlayableUnitRules.Radius(profile,e.Kind)).First();
                                var occupied=entities.Where(e=>!PlayableUnitRules.Aircraft(e.Kind)&&!members.Any(m=>m.Id==e.Id)).Select(e=>e.Position).ToArray();
                                var slots=NavArrivalAllocator.Allocate(safeGeometry,profile.Navigation.ForUnit(PlayableUnitRules.Radius(profile,largest.Kind),PlayableUnitRules.Speed(profile,largest.Kind,largest.Upgraded),PlayableUnitRules.Turn(profile,largest.Kind)),request.Target,ground.Length,occupied);
                                if(slots.Length==ground.Length)for(int i=0;i<ground.Length;i++)formation[ground[i].Id]=slots[i];
                            }
                            foreach(var air in members.Where(e=>PlayableUnitRules.Aircraft(e.Kind)))formation[air.Id]=request.Target;
                        }
                        if(army.Objective==((int)PlayableRouteTargetKind.EnemyApproach)+":"+request.TargetId)
                        {
                            var observed=entities.First(e=>e.Id==unit.Id);
                            if(!AiTurretBypassPolicy.RemainingGoal(observed,nav.InstalledGroup,nav.InstalledRevision,nav.Goal,request.Target,out goal))continue;
                        }
                        else if(!formation.TryGetValue(unit.Id,out goal))continue;
                        target=request.Target;buildingTarget=false;
                    }
                }
                else if(request.Kind==PlayableRouteTargetKind.FriendlyAnchor)
                {
                    actorsById.TryGetValue(request.TargetId,out var ally);
                    if(ally!=null&&(ally.Owner!=owner||ally.Health<=0))ally=null;
                    buildingsById.TryGetValue(request.TargetId,out var home);
                    if(home!=null&&(home.Owner!=owner||home.Health<=0||home.Phase!=ConstructionPhase.Ready))home=null;
                    bool currentLeg=ally!=null&&ally.Id==unit.Id&&ally.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&ally.CurrentOrder.Destination.Equals(request.Target);
                    bool followLeg=ally!=null&&ally.Id!=unit.Id&&PlayableUnitRules.FollowGoal(profile,ally.Position,ally.HullHeading).Equals(request.Target);
                    settledFollow=followLeg&&unit.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow&&unit.CurrentOrder.TargetId==ally.Id&&
                        Distance(nav.Position,ally.Position)<=profile.FollowDistance+profile.FollowArrivalTolerance;
                    friendlyVariant=currentLeg?1:followLeg?2:0;
                    if(ally==null&&home==null)continue;target=goal=currentLeg?ally.CurrentOrder.Destination:followLeg?request.Target:ally!=null?ally.Position:home.Position;buildingTarget=!currentLeg&&!followLeg;
                    if(ally!=null&&!currentLeg&&!followLeg)
                        friendlyRadius=Math.Max(profile.FollowDistance+nav.Radius+PlayableUnitRules.Radius(profile,ally.Kind),
                            Math.Max(profile.Navigation.ArrivalSlotSpacing,4*nav.Radius+2*profile.Navigation.ArrivalTolerance+2*profile.Navigation.LocalGridCell));
                    else if(home!=null)
                    {
                        mineGuard=home.Kind==PlayableBuildingKind.Mine;
                        friendlyRadius=TerritoryRules.Radius(profile,home.Kind)+nav.Radius+profile.FollowDistance;
                    }
                }
                else if(request.Kind==PlayableRouteTargetKind.TerritorialPosition)
                {
                    var position=territorialPositions?.FirstOrDefault(t=>t.Id==request.TargetId);
                    if(!position.HasValue||position.Value.Id==0)continue;
                    target=goal=position.Value.Point;
                    if (facts.Objectives.Any(t => t.SiteId == request.TargetId && t.Role == PlayablePublicScoutObjectiveRole.Relay))
                        relaySite = facts.Site(request.TargetId);
                }
                else if(request.Kind==PlayableRouteTargetKind.PublicObjective)
                {
                    // No authority mutation occurs inside this projection. Public geometry is
                    // identical for all requests; do not rebuild its roster/site array per unit.
                    var objective=facts.Objectives.FirstOrDefault(o=>o.SiteId==request.TargetId&&o.Reachable);
                    if(objective==null)continue;target=goal=objective.Approach;
                    publicStart=objective.Role==PlayablePublicScoutObjectiveRole.PossibleEnemyStart||objective.Role==PlayablePublicScoutObjectiveRole.ExpansionOutpost&&victoryOutposts?.Contains(objective.SiteId)==true;
                    if(objective.Role==PlayablePublicScoutObjectiveRole.Relay)relaySite=facts.Site(request.TargetId);
                }
                else continue;
                if(request.Target.X!=target.X||request.Target.Z!=target.Z)
                {
#if UNITY_INCLUDE_TESTS
                    if(capture){finalOutcome=PlayableRouteProjectionReadbackOutcome.TargetMismatch;resolvedTarget=target;PublishRouteProjectionReadback(observer,facts.OwnerId,owner,request,finalOutcome,bindingRejection,resolvedTarget,originVisible,targetVisible,observedRevision,safeGeometry.Obstacles.Count,0,candidateReadbacks);}
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
                    originVisible=facts.Visible(nav.Position);
                    if(originVisible.Value&&(request.Kind==PlayableRouteTargetKind.VisibleEnemy||request.Kind==PlayableRouteTargetKind.EnemyApproach))targetVisible=facts.Visible(request.Kind==PlayableRouteTargetKind.EnemyApproach?buildingsById[request.TargetId].Position:target);
                    if(!originVisible.Value||targetVisible==false)
                    {
                        finalOutcome=PlayableRouteProjectionReadbackOutcome.OriginOrTargetNotVisible;
                        PublishRouteProjectionReadback(observer,facts.OwnerId,owner,request,finalOutcome,bindingRejection,resolvedTarget,originVisible,targetVisible,observedRevision,safeGeometry.Obstacles.Count,0,candidateReadbacks);continue;
                    }
                }
                else
#endif
                if(!facts.Visible(nav.Position)||((request.Kind==PlayableRouteTargetKind.VisibleEnemy||request.Kind==PlayableRouteTargetKind.EnemyApproach)&&!facts.Visible(request.Kind==PlayableRouteTargetKind.EnemyApproach?buildingsById[request.TargetId].Position:target)))continue;
                bool aircraft=PlayableUnitRules.Aircraft(unit.Kind);
                bool ValidRelayPoint(NavPoint point)=>rejectedRelayApproach?.Invoke(request.TargetId,point)!=true&&
                    (aircraft?AirInside(profile,point):safeGeometry.IsFree(point,radius)&&(profile.AuthoredMap==null||profile.AuthoredMap.TryLocate(point,radius,out _)));
                NavPoint[] relayAlternatives=relaySite==null?null:new[]{goal}.Concat(RelayApproaches(profile,relaySite)).Distinct().ToArray();
                NavPoint[] relayGoals=null;
                if (relaySite != null)
                {
                    var bearing = EnemySide(relaySite.Position);
                    relayGoals = relayAlternatives.Where(ValidRelayPoint)
                        .OrderByDescending(point => AiPositionDevelopmentPlanner.FacesThreat(point, relaySite.Position, bearing, radius))
                        .ThenBy(point => Distance(nav.Position,point)).ToArray();
                }
                if(aircraft)
                {
                    var points=relayGoals??new[]{goal};
                    if(points.Length>0&&AirInside(profile,points[0]))result.Add(new PlayableRouteProof(unit.Id,unit.Owner,facts.Generation,facts.Tick,observedRevision,nav.Position,nav.Radius,request.Kind,request.TargetId,target,points[0],new[]{nav.Position,points[0]}));
                    continue;
                }
                var actor=actorsById[unit.Id];
                var publicGoals=publicStart?facts.PublicApproaches(request.TargetId,target,radius):null;
                bool friendly=request.Kind==PlayableRouteTargetKind.FriendlyAnchor;
                double spacing=friendly?Math.Max(profile.Navigation.ArrivalSlotSpacing,4*radius+2*profile.Navigation.ArrivalTolerance+2*profile.Navigation.LocalGridCell):0;
                if(friendly&&buildingTarget)friendlyOccupancy??=new FriendlyApproachOccupancy(entities
#if UNITY_INCLUDE_TESTS
                    ,facts.OnFriendlyCheck
#endif
                );
                var mineBearing=mineGuard?EnemySide(target):null;
                var goals=relayGoals??publicGoals??(buildingTarget?PlayableUnitRules.AttackApproachCandidates(profile,nav.Position,target,friendlyRadius>0?friendlyRadius/profile.AttackApproachRangeRatio:PlayableUnitRules.Range(profile,actor.Kind,actor.Upgraded))
                    .Where(p=>safeGeometry.IsFree(p,radius)&&(!friendly||friendlyOccupancy.IsClear(p,unit.Id,spacing)))
                    .OrderByDescending(p=>mineGuard&&AiPositionDevelopmentPlanner.FacesThreat(p,target,mineBearing,radius))
                    .ThenBy(p=>Distance(nav.Position,p)).ToArray():new[]{goal});
                // Arrival clears the tactical order, but the native crowd retains its
                // accepted goal. Publish that exact goal through the existing home proof.
                bool withdrawalArrival=friendly&&buildingsById.ContainsKey(request.TargetId)&&
                    actor.CurrentOrder==null&&nav.Outcome==NavigationOutcome.Arrived&&actor.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&actor.OrderStamp.Kind==PlayableCommandKind.Move&&
                    actor.OrderStamp.Owner==owner&&actor.OrderStamp.Generation==facts.Generation&&actor.OrderStamp.Source==PlayableAiOpeningComposition.SourceIdentity&&
                    armyState!=null&&armyState.Generation==facts.Generation&&armyState.Armies.Any(a=>(a.Phase==AiArmyPhase.Retreating||a.Phase==AiArmyPhase.Recovering)&&AiArmyPlanner.IsArmyPolicy(a.TacticalOwner)&&a.Members.Contains(actor.Id)&&!a.Reinforcements.Contains(actor.Id));
                // An intermediate escape leg may also have arrived. Retain its
                // exact goal only when it actually reaches this home; otherwise
                // keep the ordinary free home candidates for the next leg.
                bool regroupLeg=friendly&&buildingsById.ContainsKey(request.TargetId)&&
                    actor.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&actor.OrderStamp.Kind==PlayableCommandKind.Move&&
                    actor.OrderStamp.Owner==owner&&actor.OrderStamp.Generation==facts.Generation&&actor.OrderStamp.Source==PlayableAiOpeningComposition.SourceIdentity&&
                    (actor.CurrentOrder==null&&nav.Outcome==NavigationOutcome.Arrived||
                     actor.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&actor.CurrentOrder.CommandSequence==actor.OrderStamp.Sequence&&actor.CurrentOrder.Destination.Equals(nav.Goal))&&
                    armyState!=null&&armyState.Generation==facts.Generation&&armyState.Armies.Any(a=>a.Phase==AiArmyPhase.Regrouping&&AiArmyPlanner.IsArmyPolicy(a.TacticalOwner)&&a.Members.Contains(actor.Id));
                if((withdrawalArrival||regroupLeg)&&Distance(nav.Goal,target)<=friendlyRadius+profile.Navigation.ArrivalTolerance&&safeGeometry.IsFree(nav.Goal,radius))goals=new[]{nav.Goal};
#if UNITY_INCLUDE_TESTS
                int candidateCount=goals.Length;
                bool proofPublished=false;
#endif
                int workCandidate=0;
                bool awaitingCandidate=false;
                PlayableRouteProof resolvedProof=null;
                foreach(var candidate in goals)
                {
#if UNITY_INCLUDE_TESTS
                    bool? candidateVisible=null;int candidateIndex=capture?candidateReadbacks.Count:0;
                    if(capture)
                    {
                        candidateVisible=facts.Visible(candidate);
                    }
#endif
                    var work=new ObservedPathWork(owner,safeGeometry,profile.Navigation,radius,nav.Speed,nav.TurnSpeed,nav.Position,candidate);
                    NavPoint[] path;
                    if(lane!=null){
                        Action before=null;
#if UNITY_INCLUDE_TESTS
                        before=facts.BeforePath;
#endif
                        // An ally's ring approach, current Move and Follow leg may
                        // coexist. Their answers must not replace one another.
                        int cacheCandidate=request.Kind==PlayableRouteTargetKind.EnemyApproach?approachVariant:workCandidate++;
                        if(relayGoals!=null)cacheCandidate=Array.IndexOf(relayAlternatives,candidate)+1;
                        if(request.Kind==PlayableRouteTargetKind.FriendlyAnchor)cacheCandidate=checked(cacheCandidate*3+friendlyVariant);
                        if(!(readyOnly?lane.TryReadReady(work,unit.Id,request.TargetId,request.Kind,cacheCandidate,out path):lane.TryGet(work,unit.Id,request.TargetId,request.Kind,cacheCandidate,out path,before))){awaitingCandidate=true;pathWorkPending=true;break;}
                    }else{
                    var router=facts.Pool.Router(owner,safeGeometry,profile.Navigation,radius,nav.Speed,nav.TurnSpeed);
                    long pathBegin=routePerformanceTrace?System.Diagnostics.Stopwatch.GetTimestamp():0;
                    long expanded=routePerformanceTrace?router.ExpandedCells:0,checks=routePerformanceTrace?router.GridGeometryChecks:0;
                    int rebuilds=routePerformanceTrace?router.RebuildCount:0;
                    using(PerformanceProfile.Measure(ProfileStage.ObservedPath)){
#if UNITY_INCLUDE_TESTS
                        facts.BeforePath?.Invoke();
#endif
                        path=work.Execute(router);
                    }
                    if(routePerformanceTrace){double ms=(System.Diagnostics.Stopwatch.GetTimestamp()-pathBegin)*1000d/System.Diagnostics.Stopwatch.Frequency;
                        if(ms>=5)Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "OBS_PATH tick={0} owner={1} actor={2} kind={3} target={4} radius={5:R} candidates={6} ms={7:F4} expanded={8} checks={9} rebuilds={10} points={11}",
                            facts.Tick,owner,unit.Id,request.Kind,request.TargetId,radius,goals.Length,ms,router.ExpandedCells-expanded,router.GridGeometryChecks-checks,router.RebuildCount-rebuilds,path.Length));}
                    }
                    // A solver's empty route is not arrival evidence. Only the existing
                    // arrival tolerance plus a current free native sweep certifies this
                    // tiny/degenerate friendly leg; unreachable legs still have no proof.
                    if(path.Length==0&&request.Kind==PlayableRouteTargetKind.FriendlyAnchor&&Distance(nav.Position,candidate)<=profile.Navigation.ArrivalTolerance&&
                        facts.Geometry.SegmentFree(nav.Position,candidate,radius)&&safeGeometry.SegmentFree(nav.Position,candidate,radius))path=new[]{candidate};
                    if(path.Length==0)
                    {
#if UNITY_INCLUDE_TESTS
                        if(capture)candidateReadbacks.Add(new PlayableRouteProjectionCandidateReadback(candidateIndex,candidate,candidateVisible,
                            PlayableRouteProjectionCandidateOutcome.RouterReturnedEmpty,path,-1,null,null,null,null));
#endif
                        continue;
                    }
                    // A completed Follow witness may retain an old bend after
                    // both endpoints converge. Inside the native Follow stopping
                    // ring, certify the current tiny leg against both
                    // geometries instead of treating that old detour as distance
                    // still to assemble. Pending/empty work is never bypassed,
                    // and the completed solver answer remains unchanged.
                    if(profile.AuthoredMap==null&&!facts.Recast&&settledFollow&&path.Length>1&&
                        facts.Geometry.SegmentFree(nav.Position,candidate,radius)&&safeGeometry.SegmentFree(nav.Position,candidate,radius))path=new[]{candidate};
#if UNITY_INCLUDE_TESTS
                    if(capture)
                    {
                        var from=nav.Position;bool safe=true;int failedSegment=-1;NavPoint? failedFrom=null,failedTo=null;bool? authoritativeFree=true,visibleSegment=true;
                        for(int pathIndex=0;pathIndex<path.Length;pathIndex++)
                        {
                            var point=path[pathIndex];
                            if(request.Kind!=PlayableRouteTargetKind.PublicObjective&&request.Kind!=PlayableRouteTargetKind.TerritorialPosition)
                            {
                                bool segmentFree=facts.Geometry.SegmentFree(from,point,radius);
                                authoritativeFree=authoritativeFree.Value&&segmentFree;
                                visibleSegment=visibleSegment.Value&&ProjectionSegmentVisible(profile,facts.Visible,from,point);
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
                            if(request.Kind!=PlayableRouteTargetKind.PublicObjective&&request.Kind!=PlayableRouteTargetKind.TerritorialPosition&&!facts.Geometry.SegmentFree(from,point,radius)){safe=false;break;}
                            from=point;
                        }
                        if(!safe)continue;
                    }
                    resolvedProof = new PlayableRouteProof(unit.Id,unit.Owner,facts.Generation,facts.Tick,observedRevision,nav.Position,radius,request.Kind,request.TargetId,target,candidate,path);
                    result.Add(resolvedProof);
#if UNITY_INCLUDE_TESTS
                    if(capture)proofPublished=true;
#endif
                    break;
                }
                if (!awaitingCandidate) settled?.Add(request, resolvedProof);
#if UNITY_INCLUDE_TESTS
                if(capture)
                {
                    finalOutcome=proofPublished?PlayableRouteProjectionReadbackOutcome.ProofPublished:
                        candidateCount==0?PlayableRouteProjectionReadbackOutcome.NoCandidateGenerated:
                        candidateReadbacks.Count>0&&candidateReadbacks.All(x=>x.Outcome==PlayableRouteProjectionCandidateOutcome.Invisible)?PlayableRouteProjectionReadbackOutcome.AllCandidatesInvisible:
                        PlayableRouteProjectionReadbackOutcome.AllCandidatesRejected;
                    PublishRouteProjectionReadback(observer,facts.OwnerId,owner,request,finalOutcome,bindingRejection,resolvedTarget,originVisible,targetVisible,observedRevision,safeGeometry.Obstacles.Count,candidateCount,candidateReadbacks);
                }
#endif
            }
            if(routePerformanceTrace){double ms=(System.Diagnostics.Stopwatch.GetTimestamp()-projectionBegin)*1000d/System.Diagnostics.Stopwatch.Frequency;
                if(ms>=5)Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,"OBS_ROUTES tick={0} owner={1} requests={2} ms={3:F4}",facts.Tick,owner,requests.Count,ms));}
            if(pathWorkPending)throw new ObservedPathPending();
            return result.ToArray();
            }finally{if(!readyOnly && settled==null)lane?.EndProjection(owner);}
        }
#if UNITY_INCLUDE_TESTS
        internal int FriendlyApproachChecksForTests{get;private set;}
#endif
        private sealed class FriendlyApproachOccupancy
        {
            private readonly PlayableTargetSpatialIndex index=new();
            private readonly List<NavPoint> points;
            private readonly List<PlayableTargetSpatialIndex.Entry> nearby=new();

            #if UNITY_INCLUDE_TESTS
            private readonly Action onCheck;
#endif
            internal FriendlyApproachOccupancy(IReadOnlyList<PlayableEntitySnapshot> entities
#if UNITY_INCLUDE_TESTS
                ,Action onCheck
#endif
            )
            {
#if UNITY_INCLUDE_TESTS
                this.onCheck=onCheck;
#endif
                points=new List<NavPoint>(entities.Count);
                // Use the participant projection, including its existing order
                // visibility and pending route viability, for all reservations.
                foreach(var entity in entities){
                    Add(entity.Id,entity.Position);
                    var order=entity.CurrentOrder;
                    if(order?.Kind==PlayableTacticalOrderKind.Move)Add(entity.Id,order.Destination);
                    var queue=entity.Queue;
                    var pending=queue?.Pending;
                    if(queue?.PendingAiRouteViable==true&&pending?.Kind==PlayableCommandKind.Move&&pending.Anchor.HasValue)
                        Add(entity.Id,pending.Anchor.Value.Position);
                }
            }
            private void Add(int id,NavPoint point)
            {
                index.Add(id,point,false);
                points.Add(point);
            }
            internal bool IsClear(NavPoint point,int unitId,double spacing)
            {
                index.Query(point,spacing,nearby);
                foreach(var entry in nearby){
                    if(entry.Id==unitId)continue;
#if UNITY_INCLUDE_TESTS
                    onCheck?.Invoke();
#endif
                    if(!(Distance(point,points[entry.Ordinal])>=spacing))return false;
                }
                return true;
            }
        }
        // Only a due registry-owned tactical Move asks this question. The observer
        // uses current visible facts and the existing router; no per-tick army solve.
        private sealed class PublicApproachView
        {
            internal readonly PlayableProfile Profile;
            internal readonly NavGeometry Geometry;
            internal readonly Dictionary<(int Site,NavPoint Origin,double Radius),NavPoint[]> Approaches=new();

            internal PublicApproachView(PlayableProfile profile,NavGeometry geometry)
            {Profile=profile;Geometry=geometry;}
        }
        private readonly Dictionary<PlayableOwner,PublicApproachView> publicApproachViews=new();
#if UNITY_INCLUDE_TESTS
        internal int PublicApproachBuildsForTests{get;private set;}
#endif
        private NavPoint[] PublicArmyApproaches(PlayableOwner owner,int siteId,NavPoint original,NavGeometry observed,double radius)
        {
            // One bounded current geometry per owner. A former visibility view
            // cannot keep its candidate rings after the owner receives a new one.
            if(!publicApproachViews.TryGetValue(owner,out var view)||!ReferenceEquals(view.Profile,profile)||
                !ReferenceEquals(view.Geometry,observed)){
                view=new PublicApproachView(profile,observed);
                publicApproachViews[owner]=view;
            }
            var key=(siteId,original,radius);
            if(view.Approaches.TryGetValue(key,out var points))return points;
            points=BuildPublicArmyApproaches(siteId,original,observed,radius);
            view.Approaches.Add(key,points);
            return points;
        }
        private NavPoint[] BuildPublicArmyApproaches(int siteId,NavPoint original,NavGeometry observed,double radius)
        {
#if UNITY_INCLUDE_TESTS
            PublicApproachBuildsForTests++;
#endif
            return ComputePublicApproaches(profile,sites.TryGetValue(siteId,out var state)?state.Site:null,original,observed,radius);
        }
        private static NavPoint[] ComputePublicApproaches(PlayableProfile profile,TerritorySite site,NavPoint original,NavGeometry observed,double radius)
        {
            if(site==null)return Array.Empty<NavPoint>();
            // Slot positions are authored public geography. Give a blind order
            // clearance from possible foundations without reading their occupancy.
            double footprint=Math.Max(profile.FactoryFootprintRadius,Math.Max(profile.RefineryFootprintRadius,profile.ScienceFootprintRadius));
            var slots=site.Slots.Select(s=>s.Position).ToArray();
            double groupRadius=Math.Max(radius,Math.Max(profile.TankCollisionRadius,Math.Max(profile.ExplorerCollisionRadius,profile.ShkvalCollisionRadius)));
            double clearance=(footprint+groupRadius)*Math.Sqrt(2);
            bool Clear(NavPoint point)=>observed.IsFree(point,radius)&&(profile.AuthoredMap==null||profile.AuthoredMap.SupportsFootprint(point,radius))&&
                slots.All(s=>Distance(s,point)>clearance);
            if(Clear(original))return new[]{original};

            double ring=Math.Max(Distance(original,site.Position),
                slots.Select(s=>Distance(s,site.Position)+clearance).DefaultIfEmpty(0).Max()+profile.Navigation.ArrivalTolerance);
            return PlayableUnitRules.AttackApproachCandidates(profile,original,site.Position,ring/profile.AttackApproachRangeRatio)
                .Where(Clear).OrderBy(p=>Distance(original,p)).ToArray();
        }

        private bool ObservedGroundDestinationFree(NavGeometry observed,NavPoint point,double radius)=>
            observed.IsFree(point,radius)&&(profile.AuthoredMap==null||profile.AuthoredMap.SupportsFootprint(point,radius));

        internal bool ArmyDestinationObservedFree(PlayableAiAction action)
        {
            var owner=OwnerFor(action.PlayerId);
            var geometry=ObservedGeometry(Vision(owner));
            return action.EntityIds.All(id=>units.TryGetValue(id,out var unit)&&unit.Owner==owner&&unit.Health>0&&
                (PlayableUnitRules.Aircraft(unit.Kind)?AirInside(action.Target):
                    ObservedGroundDestinationFree(geometry,action.Target,PlayableUnitRules.Radius(profile,unit.Kind))));
        }
        internal bool ArmyMoveSafe(PlayableAiAction action)=>ArmyMoveRejection(action,false)==null;
        internal NavLocation? ReinforcementEndpoint(PlayableAiAction action, AiKnowledgeState knowledge)
        {
            if (action.EntityIds.Count != 1 || !units.TryGetValue(action.EntityIds[0], out var unit) ||
                unit.Owner != OwnerFor(action.PlayerId) || unit.Health <= 0) return null;
            var endpoint = navigation.FindNearbyFreeArrival(unit.Id, action.Target,
                profile.FollowDistance + profile.FollowArrivalTolerance, Vision(unit.Owner).IsVisible);
            if (!endpoint.HasValue) return null;
            var paths = new Dictionary<int, NavPoint[]>();
            if (ArmyMoveRejection(action, false, out var view, paths, false, reinforcementGoal: endpoint.Value.Position) != null)
                return null;
            var mover = view.Entities.Single(u => u.Id == unit.Id);
            return AiStaticDefenseSafety.Withdrawal(view, profile, mover, paths[unit.Id], knowledge) ? endpoint : null;
        }
        internal bool ArmyWithdrawalSafe(PlayableAiAction action)=>ArmyMoveRejection(action,true)==null;
        // Authored map blockers are public geography. Dynamic solids remain fog-gated
        // until all four obstacle corners are currently visible.
        private static bool ObstacleCornersVisible(NavObstacle obstacle,Func<NavPoint,bool> visible)
            =>visible(new NavPoint(obstacle.MinX,obstacle.MinZ))&&
                visible(new NavPoint(obstacle.MaxX,obstacle.MinZ))&&
                visible(new NavPoint(obstacle.MinX,obstacle.MaxZ))&&
                visible(new NavPoint(obstacle.MaxX,obstacle.MaxZ));
        internal string ArmyMoveRejection(PlayableAiAction action,bool withdrawal)
            =>ArmyMoveRejection(action,withdrawal,out _,null);
        internal string WithdrawingReinforcementMoveRejection(PlayableAiAction action,AiKnowledgeState knowledge)
            =>ArmyMoveRejection(action,true,out _,null,false,homeRedirect:true,withdrawalKnowledge:knowledge);

        internal string TurretPassageRejection(PlayableAiAction action,AiProfile ai,AiDifficulty difficulty,AiKnowledgeState knowledge,AiTurretBypassPolicy.Passage passage,IReadOnlyDictionary<string,long> readyMemo)
        {
            if(!AiTurretBypassPolicy.Enabled(difficulty)||passage==null||passage.Target!=action.TargetId||!passage.Goal.Equals(action.Target))return "turret passage ownership changed";
            var paths=new Dictionary<int,NavPoint[]>();
            var rejection=ArmyMoveRejection(action,false,out var observed,paths,true);
            if(rejection!=null)return rejection;
            var movers=observed.Entities.Where(u=>action.EntityIds.Contains(u.Id)).ToArray();
            return movers.Length==action.EntityIds.Count&&AiTurretBypassPolicy.Assess(observed,profile,ai,movers,action.TargetId,paths,knowledge,readyMemo,out _,out _,passage)?null:"turret passage no longer fits group health or force budget";
        }
        // Optional call-local witnesses for additional policy checks. Partial paths
        // from a pending/rejected batch must never authorize any recipient.
        private string ArmyMoveRejection(PlayableAiAction action,bool withdrawal,out PlayableAiObservation observed,Dictionary<int,NavPoint[]> validatedPaths)
            =>ArmyMoveRejection(action,withdrawal,out observed,validatedPaths,false);

        private string ArmyMoveRejection(PlayableAiAction action,bool withdrawal,out PlayableAiObservation observed,
            Dictionary<int,NavPoint[]> validatedPaths,bool attackApproach,bool shortWithdrawal=false,AiCombatMicroPolicy micro=null,bool exactReturn=false,AiProfile nativeAiProfile=null,AiDifficulty nativeDifficulty=AiDifficulty.Veteran,bool exactLateral=false,NavPoint? reinforcementGoal=null,
            bool homeRedirect=false,AiKnowledgeState withdrawalKnowledge=null)
        {
            var owner=OwnerFor(action.PlayerId);var lane=armySafetyPathLane;lane?.BeginProjection(owner);
            try{
            var snapshot=PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,action.Seed,owner);
            var o=PlayableAiObservation.From(snapshot);observed=o;var vision=Vision(owner);
            if(homeRedirect&&!o.Buildings.Any(b=>b.Id==action.ParentId&&AiReinforcementPolicy.Home(o,b)))
                return "reinforcement destination is no longer an owned ready building";
            if((action.Kind==PlayableCommandKind.Move||AiArmyPlanner.IsGuardDeployment(action))&&action.SiteId>0&&
                !PlayableTerritorialPositions.Known(profile,vision.IsDiscovered,snapshot.Sites,snapshot.PublicScoutObjectives,
                    snapshot.Buildings.FirstOrDefault(b=>b.Owner==owner&&b.SiteId==snapshot.HomeSiteId&&b.Kind==PlayableBuildingKind.Headquarters)?.Position,
                    snapshot.IsHostile).Any(t=>t.Id==action.SiteId && (t.Point.Equals(action.Target) ||
                        snapshot.PublicSites.Any(relay => relay.Kind==PlayableBuildingKind.RelayTower && relay.Id==t.Id && RelayApproaches(profile,relay).Contains(action.Target)))))
                return "territorial position is no longer known and safe to claim";
            var leader=action.Kind==PlayableCommandKind.Follow?o.Entities.FirstOrDefault(u=>u.Id==action.TargetId&&u.Owner==owner&&u.Health>0):null;
            if(action.Kind==PlayableCommandKind.Follow&&(leader==null||Spacewars.Simulation.Ai.AiRosterCatalog.Initial.For(leader.Kind).lineWeight<=0))return "leader unavailable";
            var target=leader==null?(reinforcementGoal??action.Target):PlayableUnitRules.FollowGoal(profile,leader.Position,leader.HullHeading);
            var movers=action.EntityIds.Select(id=>o.Entities.FirstOrDefault(u=>u.Id==id&&u.Owner==owner&&u.Health>0)).ToArray();
            if(movers.Length==0||movers.Any(u=>u==null))return "mover unavailable";
            var ground=movers.Where(u=>!PlayableUnitRules.Aircraft(u.Kind)).ToArray();
            var air=movers.Where(u=>PlayableUnitRules.Aircraft(u.Kind)).OrderBy(u=>u.Id).ToArray();
            var airIds=air.Select(u=>u.Id).ToArray();
            var airGoals = leader == null ? AirFormationDestinations(target, airIds, ObservedAirArrivalFootprints(o, airIds),
                i => air[i].Position, i => air[i].Kind, ground.Length == 0 ? null : (NavPoint?)new NavPoint(
                    ground.Average(u => u.Position.X), ground.Average(u => u.Position.Z))) : air.Select(_ => target).ToArray();
            if(airGoals==null)return "air formation unavailable";
            var geometry=ObservedGeometry(vision);
            if(leader==null&&ground.Any(u=>!ObservedGroundDestinationFree(geometry,target,PlayableUnitRules.Radius(profile,u.Kind))))
                return AiArmyPlanner.InvalidApproach;
            double largest=0,slotSpeed=profile.Navigation.TankSpeed,slotTurn=profile.Navigation.TankTurnSpeed;
            foreach(var own in ground){if(!TryActor(own.Id,out var mover))return "mover unavailable";double ownRadius=PlayableUnitRules.Radius(profile,own.Kind);if(ownRadius>largest){largest=ownRadius;slotSpeed=mover.Speed;slotTurn=mover.TurnSpeed;}}
            var occupied=o.Entities.Where(u=>!PlayableUnitRules.Aircraft(u.Kind)&&!action.EntityIds.Contains(u.Id)).Select(u=>u.Position).ToArray();
            var slots=ground.Length==0?Array.Empty<NavPoint>():leader==null&&!shortWithdrawal&&!exactReturn&&!exactLateral&&!reinforcementGoal.HasValue?NavArrivalAllocator.Allocate(geometry,profile.Navigation.ForUnit(largest,slotSpeed,slotTurn),target,ground.Length,occupied):ground.Select(_=>target).ToArray();
            if(slots.Length!=ground.Length)return "ground formation unavailable";
            bool pathWorkPending=false;
            foreach(var unit in movers)
            {
                bool airborne=PlayableUnitRules.Aircraft(unit.Kind);
                int index=Array.IndexOf(airborne?air:ground,unit);var goal=(airborne?airGoals:slots)[index];
                if(!TryActor(unit.Id,out var nav))return "mover unavailable";
                var radius=PlayableUnitRules.Radius(profile,unit.Kind);
                if(airborne?!AirInside(goal):!geometry.IsFree(goal,radius))return airborne?"air destination outside map":"ground destination blocked";
                // Public geography and observed solids are the same knowledge boundary
                // as ProjectRoutes. Crossing fog never certifies absence of threats.
                NavPoint[] path;
                if(shortWithdrawal&&!airborne&&!AiCombatMicroPolicy.WithdrawalSpaceFree(o,profile,unit,new[]{goal}))
                    return "short withdrawal lost its free space behind the unit";
                bool directMicro=!airborne&&micro!=null&&
                    (exactLateral||shortWithdrawal||exactReturn&&AiCombatMicroPolicy.IsShortReturn(profile,nativeAiProfile,nativeDifficulty,unit,goal))&&
                    geometry.SegmentFree(unit.Position,goal,radius)&&
                    (profile.AuthoredMap==null||profile.AuthoredMap.TryLocate(goal,radius,out var localGoal)&&
                        localGoal.SurfaceId==nav.Location.SurfaceId&&profile.AuthoredMap.SupportsSweep(unit.Position,goal,radius))&&
                    AiCombatMicroPolicy.WithdrawalSpaceFree(o,profile,unit,new[]{goal});
                // A directly clear leg needs no queued path reconstruction. Its
                // endpoints and full hull sweep are checked against the current world.
                bool directHome=homeRedirect&&!airborne&&geometry.IsFree(unit.Position,radius)&&
                    geometry.SegmentFree(unit.Position,goal,radius)&&
                    (profile.AuthoredMap==null||profile.AuthoredMap.TryLocate(goal,radius,out var homeGoal)&&
                        homeGoal.SurfaceId==nav.Location.SurfaceId&&profile.AuthoredMap.SupportsSweep(unit.Position,goal,radius));
                if(airborne||directMicro||directHome)path=new[]{goal};
                else if(lane!=null){
                    Action before=null;
#if UNITY_INCLUDE_TESTS
                    var safetyHook=BeforeArmySafetyPathForTests;if(safetyHook!=null)before=()=>safetyHook(action,unit.Id);
#endif
                    var work=new ObservedPathWork(owner,geometry,profile.Navigation,radius,nav.Speed,nav.TurnSpeed,unit.Position,goal);
                    if(!lane.TryGet(work,unit.Id,leader?.Id??unit.Id,PlayableRouteTargetKind.FriendlyAnchor,0,out path,before)){pathWorkPending=true;continue;}
                }else{
#if UNITY_INCLUDE_TESTS
                    BeforeArmySafetyPathForTests?.Invoke(action,unit.Id);
#endif
                    path=new SharedFlowRouter(geometry,profile.Navigation.ForUnit(radius,nav.Speed,nav.TurnSpeed)).FindPath(unit.Position,goal);
                }
                if(!airborne&&path.Length==0&&Distance(unit.Position,goal)<=profile.Navigation.ArrivalTolerance&&geometry.SegmentFree(unit.Position,goal,radius))path=new[]{goal};
                if(path.Length==0||!path.Last().Equals(goal))return "ground route unreachable";
                var firing=shortWithdrawal?new HashSet<int>():null;
                if(shortWithdrawal&&!micro.UnderWithdrawalPressure(o,unit,profile)&&!micro.NeedsArtilleryCover(o,profile,nativeAiProfile,nativeDifficulty,unit))return "short withdrawal no longer has observed incoming fire";
                if(shortWithdrawal&&!airborne&&!AiCombatMicroPolicy.WithdrawalSpaceFree(o,profile,unit,path))
                    return "short withdrawal lost its free space";
                bool needsWithdrawal=withdrawal||unit.Kind==PlayableEntityKind.Igla&&action.Kind==PlayableCommandKind.Move&&
                    !AiTacticalExecutor.SafePathForUnit(o,unit.Position,new[]{unit.Position},radius,profile,false);
                if(!attackApproach&&!(needsWithdrawal?AiTacticalExecutor.WithdrawalPathForUnit(o,unit.Position,path,radius,profile,airborne,centerRangeOnly:shortWithdrawal,firingThreats:firing):AiTacticalExecutor.SafePathForUnit(o,unit.Position,path,radius,profile,airborne)))return "observed threat blocks path";
                if(homeRedirect&&!AiStaticDefenseSafety.Withdrawal(o,profile,unit,path,withdrawalKnowledge))
                    return "known defenses block reinforcement withdrawal";
                if(!airborne){var at=unit.Position;foreach(var point in path){if(!geometry.SegmentFree(at,point,radius))return "ground segment blocked";at=point;}}
                validatedPaths?.Add(unit.Id,path);
            }
            if(pathWorkPending)throw new ObservedPathPending();
            return null;
            }finally{lane?.EndProjection(owner);}

        }
        private bool VisibleSegment(PlayableVision vision,NavPoint from,NavPoint to)=>ProjectionSegmentVisible(profile,vision.IsVisible,from,to);
        private static bool ProjectionSegmentVisible(PlayableProfile profile,Func<NavPoint,bool> visible,NavPoint from,NavPoint to)
        {
            double dx=to.X-from.X,dz=to.Z-from.Z,length=Math.Sqrt(dx*dx+dz*dz);
            // VisionCellSize is existing profile metadata; half-cell sampling is a projection invariant.
            int steps=Math.Max(1,(int)Math.Ceiling(length/(profile.VisionCellSize/2)));
            for(int i=0;i<=steps;i++)if(!visible(new NavPoint(from.X+dx*i/steps,from.Z+dz*i/steps)))return false;
            return true;
        }
    }
}
