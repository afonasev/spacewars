using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private readonly Dictionary<int,PlayableVision> visions=new Dictionary<int,PlayableVision>();
        private PlayableVision Vision(PlayableOwner owner)
        {
            if(!visions.TryGetValue(TeamOf(owner),out var v))visions.Add(TeamOf(owner),v=new PlayableVision(TeamOf(owner),profile.ArenaHalfExtent,profile.ArenaHalfExtent,profile.VisionCellSize,profile.FogEdgeFeather));
            return v;
        }
        private IEnumerable<VisionSource> VisionSources(PlayableOwner owner)
        {
            foreach(var u in units.Values)if(!Hostile(u.Owner,owner)&&u.Health>0&&navigation.Crowd.TryGet(u.Id,out var n))yield return new VisionSource(n.Position,PlayableUnitRules.Vision(profile,u.Kind));
            foreach(var b in buildings.Values)if(!Hostile(b.Owner,owner))
            {
                double radius=PlayableVision.BuildingRadius(profile,b.Kind,b.Phase);
                if(radius>0)yield return new VisionSource(b.Position,radius);
            }
        }
        private void RefreshVision()
        {
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
        private PlayableSnapshot ProjectPlayerSnapshot(long sequence,RuntimeStatus status,bool paused,PlayableRuntimeMetrics metrics,string failure,int seed,PlayableOwner owner,IReadOnlyList<PlayableRouteRequest> routeRequests,bool expansionRoutes,bool armyRoutes=false,AiArmyRegistryState armyState=null)
        {
            RefreshVision();var v=Vision(owner);var information=v.Snapshot();
            var es=new List<PlayableEntitySnapshot>();
            foreach(var u in units.Values)
            {
                if(!navigation.Crowd.TryGet(u.Id,out var n)||(u.Owner!=owner&&!v.IsVisible(n.Position)))continue;
                int target=u.Owner==owner&&VisibleTarget(owner,u.Target)?u.Target:0;
                var order=u.Owner==owner?u.CurrentOrder:null;
                if(order!=null&&order.Kind==PlayableTacticalOrderKind.Attack&&!VisibleTarget(owner,order.TargetId))order=null;
                es.Add(new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,n.Position,u.Health,n.Moving,target,n.Heading,u.Turret,order,UnitUpgraded(u),u.Owner==owner&&n.Held,u.Owner==owner?n.Outcome:NavigationOutcome.Idle,u.Owner==owner?u.LastOrder:null,SpatialCompletion(u.Id,owner),u.Owner==owner?(NavLocation?)n.Location:null,TacticalQueueProjection(u.Id,owner)));
            }
            var bs=new List<PlayableBuildingSnapshot>();
            foreach(var b in buildings.Values)
            {
                if(b.Owner!=owner&&(b.Phase==ConstructionPhase.Pending||!v.IsVisible(b.Position)))continue;
                bool own=b.Owner==owner;double duration=TerritoryRules.Duration(Terms(b.TermsRevision),b.Kind);
                bs.Add(new PlayableBuildingSnapshot(b.Id,b.Owner,b.Kind,b.Position,(int)Math.Ceiling(b.Health),b.Ready?1:b.Build/duration,
                    own?b.Queue:0,own?b.ProductionProgress:0,own?b.Rally:default(NavPoint),b.SiteId,b.SlotId,b.ParentId,b.Phase,own?b.BlockedReason:null,b.Heading,own,own?ProductionSnapshot(b):null,own&&b.RepeatTank,own?LifecycleSnapshot(b):null,own?UpgradeSnapshot(b):null,b.Upgrade?.Complete==true,own?b.RepeatKind:PlayableEntityKind.Tank,b.Kind==PlayableBuildingKind.ScientificCenter&&own?ResearchSnapshot(owner):null,own&&b.HasRally,own?PendingRally(b.Id):null,exactHealth:own?b.Health:(double?)null));
            }
            var ps=new List<PlayableProjectileSnapshot>();
            foreach(var p in projectiles)
            {
                bool visible=p.Faction==owner||v.IsVisible(p.Position);
                var r=p.Rocket;bool markerVisible=r!=null&&(p.Faction==owner||v.IsVisible(r.Predicted.Point.Ground));
                if(!visible&&!markerVisible)continue;
                int source=visible&&es.Any(e=>e.Id==p.Owner)?p.Owner:0;
                int target=visible&&(es.Any(e=>e.Id==p.Target)||bs.Any(b=>b.Id==p.Target))?p.Target:0;
                if(r==null){ps.Add(new PlayableProjectileSnapshot(p.Id,source,target,p.Position,p.Kind,Math.Atan2(p.DirectionZ,p.DirectionX),height:p.Height,pitch:Math.Atan(p.VerticalSlope),faction:p.Faction));continue;}
                double u=r.Elapsed/r.Flight.Duration;var point=r.Flight.Point(u);var tangent=r.Flight.Tangent(u);
                double progress=Math.Min(1,u/Math.Max(1e-5,r.Predicted.Progress));
                var marker=markerVisible?new PlayableImpactMarker(r.Predicted.Point.Ground,r.MarkerStartRadius+(r.BlastRadius-r.MarkerStartRadius)*progress,r.MarkerOpacity):null;
                ps.Add(new PlayableProjectileSnapshot(p.Id,source,target,visible?p.Position:default(NavPoint),p.Kind,visible?Math.Atan2(tangent.Z,tangent.X):0,visible?point.Y:0,visible?Math.Atan2(tangent.Y,Math.Sqrt(tangent.X*tangent.X+tangent.Z*tangent.Z)):0,p.Faction,r.Elapsed,visible,marker));
            }
            var liveSites=SiteSnapshots().Where(s=>v.IsVisible(s.Site.Position)).Select(s=>
            {
                bool hiddenRequest=s.CenterId!=0&&buildings.TryGetValue(s.CenterId,out var b)&&b.Phase==ConstructionPhase.Pending&&b.Owner!=owner;
                return hiddenRequest?new TerritorySiteSnapshot(s.Site,s.Claimant,null,s.Progress,s.Contested,0,false):s;
            }).ToArray();
            var discovered=sites.Values.Where(s=>v.IsDiscovered(s.Site.Position)).Select(s=>s.Site).ToArray();
            var objectives=PublicObjectives(owner);
            if(expansionRoutes)routeRequests=AiExpansionPlanner.RouteRequests(es,liveSites,objectives,owner,navigation.Generation,Tick,profile)
                .Concat(armyRoutes?AiArmyPlanner.RouteRequests(es,bs,objectives,owner,navigation.Generation,Tick,profile,other=>Hostile(other,owner)).Concat(AiTacticalExecutor.RouteRequests(es,bs,armyState,owner,navigation.Generation,Tick,profile)):
                    !paused&&status==RuntimeStatus.Running&&Outcome==PlayableMatchOutcome.Playing&&!eliminated.Contains(owner)?AiTacticalExecutor.AssemblyRouteRequests(es,armyState,owner,navigation.Generation,Tick,profile):Array.Empty<PlayableRouteRequest>()).ToArray();
            return new PlayableSnapshot(profile.ProfileId,profile.Revision,navigation.Generation,seed,sequence,Tick,status,paused,Outcome,
                (int)Math.Floor(Balance(owner)),projectileGeometry,es.ToArray(),bs.ToArray(),ps.ToArray(),metrics,failure,liveSites,
                Income(owner)/profile.IncomePeriodSeconds,information,discovered,Population(owner),impacts.Where(p=>(p.VisibleMask&(1<<(int)owner))!=0&&v.IsVisible(p.Point.Ground)).Select(p=>new PlayableImpactSnapshot(p.Id,p.Owner,p.Point,p.Radius,p.Tick,1<<(int)owner)).ToArray(),researchAvailability:ResearchAvailability(owner),ownerResearch:ResearchSnapshot(owner),publicScoutObjectives:objectives,
                ownCenterDamage:centerDamage.Values.Where(d=>d.Owner==owner&&bs.Any(b=>b.Id==d.CenterId&&b.Health>0)&&es.Any(e=>e.Id==d.AttackerId&&e.Owner!=owner)).OrderBy(d=>d.CenterId).ThenBy(d=>d.AttackerId).Select(d=>d.Copy()).ToArray(),
                routeProofs:ProjectRoutes(owner,routeRequests,v,es,bs,armyState),
                artillerySupport:ProjectArtillery(owner,v,es,bs,armyState),owner:owner,exactCredits:Balance(owner),ownerId:offline==null?null:offline.Roster[(int)owner].Id,team:TeamOf(owner),participants:offline?.Roster.ToArray(),activeProfile:profile,homeSiteId:HomeSite(owner),ownerEliminated:eliminated.Contains(owner),settledIncome:SettledIncome(owner),intelEnvelopes:sites.Values.OrderBy(s=>s.Site.Id).Select(s=>new AiIntelEnvelope(s.Site.Id,new[]{new VisionSource(s.Site.Position,TerritoryRules.Radius(profile,s.Site.Kind)*Math.Sqrt(2))}.Concat(s.Site.Slots.Select(slot=>new VisionSource(slot.Position,Math.Max(profile.ScienceFootprintRadius,Math.Max(profile.FactoryFootprintRadius,profile.RefineryFootprintRadius))*Math.Sqrt(2)))))).ToArray(),sounds:expansionRoutes?null:Sounds(owner));
        }
        // Reconstructible observer-only memoization. It carries no authority/proof state and
        // a restored domain starts cold. Each value is confined to its authority thread.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PlayableDomain,Dictionary<PlayableOwner,ObservedRouteCache>> observedRouteCaches=new System.Runtime.CompilerServices.ConditionalWeakTable<PlayableDomain,Dictionary<PlayableOwner,ObservedRouteCache>>();
        private sealed class ObservedRouteCache
        {
            internal NavGeometry Geometry;internal NavigationProfile Profile;internal double Radius,Speed,Turn;internal SharedFlowRouter Router;
        }
        private const int ObservedRouteFieldLimit=8;
        private SharedFlowRouter ObservedRouter(PlayableOwner owner,NavGeometry observed,NavigationProfile basis,double radius,double speed,double turn)
        {
            var caches=observedRouteCaches.GetValue(this,_=>new Dictionary<PlayableOwner,ObservedRouteCache>());
            bool SameGeometry(NavGeometry a,NavGeometry b)=>a.HalfExtent==b.HalfExtent&&a.Obstacles.Count==b.Obstacles.Count&&a.Obstacles.Select((o,i)=>o.GeometryEquals(b.Obstacles[i])).All(x=>x);
            if(!caches.TryGetValue(owner,out var cache)||!ReferenceEquals(cache.Profile,basis)||cache.Radius!=radius||cache.Speed!=speed||cache.Turn!=turn||!SameGeometry(cache.Geometry,observed)){
                cache=new ObservedRouteCache{Geometry=observed,Profile=basis,Radius=radius,Speed=speed,Turn=turn};caches[owner]=cache;
            }
            // FindPath can add a floor field and an attachment field. Reset between
            // requests so each owner's single typed router retains at most eight fields.
            if(cache.Router==null||cache.Router.CachedFieldCount>=ObservedRouteFieldLimit-1)cache.Router=new SharedFlowRouter(cache.Geometry,basis.ForUnit(radius,speed,turn));
            return cache.Router;
        }
        private PlayableRouteProof[] ProjectRoutes(PlayableOwner owner,IReadOnlyList<PlayableRouteRequest> requests,PlayableVision vision,IReadOnlyList<PlayableEntitySnapshot> entities,IReadOnlyList<PlayableBuildingSnapshot> buildings,Spacewars.Runtime.AiArmyRegistryState armyState)
        {
            if(requests==null||requests.Count==0)return Array.Empty<PlayableRouteProof>();
            // The observer solves on visible solids with the production navigation solver, then
            // checks every segment against authoritative solids. Hidden geometry can only remove proof.
            bool visibleObstacle(NavObstacle o)=>vision.IsVisible(new NavPoint(o.MinX,o.MinZ))&&vision.IsVisible(new NavPoint(o.MaxX,o.MinZ))&&vision.IsVisible(new NavPoint(o.MinX,o.MaxZ))&&vision.IsVisible(new NavPoint(o.MaxX,o.MaxZ));
            var observed=Geometry.Obstacles.Where(visibleObstacle).ToArray();
            // This identity depends only on observed solids, never on hidden authority revisions.
            int observedRevision=1;
            unchecked{foreach(var obstacle in observed)observedRevision=observedRevision*31+obstacle.MinX.GetHashCode()+obstacle.MinZ.GetHashCode()+obstacle.MaxX.GetHashCode()+obstacle.MaxZ.GetHashCode();}
            observedRevision=observedRevision==int.MinValue?int.MaxValue:Math.Max(1,Math.Abs(observedRevision));
            var safeGeometry=new NavGeometry(Geometry.HalfExtent,observed,observedRevision);
            var result=new List<PlayableRouteProof>();
            foreach(var request in requests)
            {
                if(request==null||request.UnitId<=0||request.TargetId<=0||request.Generation!=navigation.Generation||request.Tick!=Tick||!units.TryGetValue(request.UnitId,out var unit)||unit.Owner!=owner||unit.Health<=0||!navigation.Crowd.TryGet(unit.Id,out var nav)||request.Origin.X!=nav.Position.X||request.Origin.Z!=nav.Position.Z||request.Radius!=nav.Radius)continue;
                NavPoint target,goal;bool buildingTarget=false;double friendlyRadius=0;
                if(request.Kind==PlayableRouteTargetKind.VisibleEnemy)
                {
                    var enemy=entities.FirstOrDefault(e=>e.Id==request.TargetId&&Hostile(e.Owner,owner)&&e.Health>0);
                    var enemyBuilding=buildings.FirstOrDefault(b=>b.Id==request.TargetId&&Hostile(b.Owner,owner)&&b.Health>0);
                    if(enemy==null&&enemyBuilding==null)continue;target=goal=enemy!=null?enemy.Position:enemyBuilding.Position;buildingTarget=enemy==null;
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
                    var objective=PublicObjectives(owner).FirstOrDefault(o=>o.SiteId==request.TargetId&&o.Reachable);
                    if(objective==null)continue;target=goal=objective.Approach;
                }
                else continue;
                if(request.Target.X!=target.X||request.Target.Z!=target.Z)continue;
                var radius=nav.Radius;
                // A public objective is a known map destination, rather than a current
                // enemy observation.  Its route may deliberately leave the present
                // vision envelope; the observer still solves it using observed solids
                // only, so that doing so cannot reveal a hidden obstacle or owner.
                if(!vision.IsVisible(nav.Position)||(request.Kind!=PlayableRouteTargetKind.PublicObjective&&!vision.IsVisible(target)))continue;
                var actor=entities.First(e=>e.Id==unit.Id);
                var goals=buildingTarget?PlayableUnitRules.AttackApproachCandidates(profile,nav.Position,target,friendlyRadius>0?friendlyRadius/profile.AttackApproachRangeRatio:PlayableUnitRules.Range(profile,actor.Kind,actor.Upgraded))
                    .Where(p=>vision.IsVisible(p)&&safeGeometry.IsFree(p,radius))
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
                if(withdrawalArrival)goals=Distance(nav.Goal,target)<=friendlyRadius+profile.Navigation.ArrivalTolerance&&vision.IsVisible(nav.Goal)&&safeGeometry.IsFree(nav.Goal,radius)?new[]{nav.Goal}:Array.Empty<NavPoint>();
                foreach(var candidate in goals)
                {
                    if(!vision.IsVisible(candidate))continue;
                    var router=ObservedRouter(owner,safeGeometry,profile.Navigation,radius,nav.Speed,nav.TurnSpeed);
                    var path=router.FindPath(nav.Position,candidate);
                    // A solver's empty route is not arrival evidence. Only the existing
                    // arrival tolerance plus a current free, visible native sweep certifies
                    // this tiny/degenerate friendly leg; unreachable legs still have no proof.
                    if(path.Length==0&&request.Kind==PlayableRouteTargetKind.FriendlyAnchor&&Distance(nav.Position,candidate)<=profile.Navigation.ArrivalTolerance&&
                        Geometry.SegmentFree(nav.Position,candidate,radius)&&safeGeometry.SegmentFree(nav.Position,candidate,radius)&&VisibleSegment(vision,nav.Position,candidate))path=new[]{candidate};
                    if(path.Length==0)continue;
                    var from=nav.Position;bool safe=true;
                    foreach(var point in path)
                    {
                        // Expansion Explorer routes depend only on observed geometry. Authority execution
                        // still handles hidden obstacles through the ordinary route barrier.
                        if((request.Kind!=PlayableRouteTargetKind.PublicObjective&&(!Geometry.SegmentFree(from,point,radius)||!VisibleSegment(vision,from,point)))){safe=false;break;}
                        from=point;
                    }
                    if(!safe)continue;
                    result.Add(new PlayableRouteProof(unit.Id,unit.Owner,navigation.Generation,Tick,observedRevision,nav.Position,radius,request.Kind,request.TargetId,target,candidate,path));
                    break;
                }
            }
            return result.ToArray();
        }
        // Only a due registry-owned tactical Move asks this question. The observer
        // uses current visible facts and the existing router; no per-tick army solve.
        internal bool ArmyMoveSafe(PlayableAiAction action)=>CheckArmyMove(action,false);
        internal bool ArmyWithdrawalSafe(PlayableAiAction action)=>CheckArmyMove(action,true);
        private bool CheckArmyMove(PlayableAiAction action,bool withdrawal)
        {
            var owner=OwnerFor(action.PlayerId);var snapshot=PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,action.Seed,owner);
            var o=PlayableAiObservation.From(snapshot);var vision=Vision(owner);
            var leader=action.Kind==PlayableCommandKind.Follow?o.Entities.FirstOrDefault(u=>u.Id==action.TargetId&&u.Owner==owner&&u.Health>0):null;
            if(action.Kind==PlayableCommandKind.Follow&&(leader==null||Spacewars.Simulation.Ai.AiRosterCatalog.Initial.For(leader.Kind).lineWeight<=0))return false;
            var target=leader==null?action.Target:PlayableUnitRules.FollowGoal(profile,leader.Position,leader.HullHeading);
            if(!vision.IsVisible(target))return false;
            var observed=Geometry.Obstacles.Where(x=>vision.IsVisible(new NavPoint(x.MinX,x.MinZ))&&vision.IsVisible(new NavPoint(x.MaxX,x.MinZ))&&vision.IsVisible(new NavPoint(x.MinX,x.MaxZ))&&vision.IsVisible(new NavPoint(x.MaxX,x.MaxZ))).ToArray();
            var geometry=new NavGeometry(Geometry.HalfExtent,observed,1);
            var slots=leader==null?navigation.AllocateArrivalSlots(target,action.EntityIds):action.EntityIds.Select(_=>target).ToArray();
            if(slots.Length!=action.EntityIds.Count||leader==null&&slots.Length==1&&!slots[0].Equals(target))return false;
            for(int index=0;index<action.EntityIds.Count;index++)
            {
                int id=action.EntityIds[index];var goal=slots[index];
                var unit=o.Entities.FirstOrDefault(u=>u.Id==id&&u.Owner==owner&&u.Health>0);if(unit==null||!navigation.Crowd.TryGet(id,out var nav))return false;
                var radius=PlayableUnitRules.Radius(profile,unit.Kind);if(!geometry.IsFree(goal,radius)||!vision.IsVisible(goal))return false;
                var path=new SharedFlowRouter(geometry,profile.Navigation.ForUnit(radius,nav.Speed,nav.TurnSpeed)).FindPath(unit.Position,goal);
                if(path.Length==0&&Distance(unit.Position,goal)<=profile.Navigation.ArrivalTolerance&&Geometry.SegmentFree(unit.Position,goal,radius)&&VisibleSegment(vision,unit.Position,goal))path=new[]{goal};
                if(path.Length==0||!path.Last().Equals(goal)||!(withdrawal?AiTacticalExecutor.WithdrawalPath(o,unit.Position,path,radius,profile):AiTacticalExecutor.SafePath(o,unit.Position,path,radius,profile)))return false;
                var at=unit.Position;foreach(var point in path){if(!Geometry.SegmentFree(at,point,radius)||!VisibleSegment(vision,at,point))return false;at=point;}
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
