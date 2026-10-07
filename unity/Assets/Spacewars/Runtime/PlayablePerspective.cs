using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

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
        {
            RefreshVision();var v=Vision(owner);var information=v.Snapshot();
            var es=new List<PlayableEntitySnapshot>();
            foreach(var u in units.Values)
            {
                if(!navigation.Crowd.TryGet(u.Id,out var n)||(u.Owner!=owner&&!v.IsVisible(n.Position)))continue;
                int target=u.Owner==owner&&VisibleTarget(owner,u.Target)?u.Target:0;
                var order=u.Owner==owner?u.CurrentOrder:null;
                if(order!=null&&order.Kind==PlayableTacticalOrderKind.Attack&&!VisibleTarget(owner,order.TargetId))order=null;
                es.Add(new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,n.Position,u.Health,n.Moving,target,n.Heading,u.Turret,order,UnitUpgraded(u),u.Owner==owner&&n.Held,u.Owner==owner?n.Outcome:NavigationOutcome.Idle,u.Owner==owner?u.LastOrder:null));
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
            return new PlayableSnapshot(profile.ProfileId,profile.Revision,navigation.Generation,seed,sequence,Tick,status,paused,Outcome,
                (int)Math.Floor(Balance(owner)),projectileGeometry,es.ToArray(),bs.ToArray(),ps.ToArray(),metrics,failure,liveSites,
                Income(owner)/profile.IncomePeriodSeconds,information,discovered,Population(owner),impacts.Where(p=>(p.VisibleMask&(1<<(int)owner))!=0&&v.IsVisible(p.Point.Ground)).Select(p=>new PlayableImpactSnapshot(p.Id,p.Owner,p.Point,p.Radius,p.Tick,1<<(int)owner)).ToArray(),researchAvailability:ResearchAvailability(owner),ownerResearch:ResearchSnapshot(owner),publicScoutObjectives:PublicObjectives(owner),
                ownCenterDamage:centerDamage.Values.Where(d=>d.Owner==owner&&bs.Any(b=>b.Id==d.CenterId&&b.Health>0)&&es.Any(e=>e.Id==d.AttackerId&&e.Owner!=owner)).OrderBy(d=>d.CenterId).ThenBy(d=>d.AttackerId).Select(d=>d.Copy()).ToArray(),
                routeProofs:ProjectRoutes(owner,routeRequests,v,es),
                artillerySupport:ProjectArtillery(owner,v,es,bs),owner:owner,exactCredits:Balance(owner),ownerId:offline==null?null:offline.Roster[(int)owner].Id,team:TeamOf(owner),participants:offline?.Roster.ToArray(),activeProfile:profile);
        }
        private PlayableRouteProof[] ProjectRoutes(PlayableOwner owner,IReadOnlyList<PlayableRouteRequest> requests,PlayableVision vision,IReadOnlyList<PlayableEntitySnapshot> entities)
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
                NavPoint target,goal;
                if(request.Kind==PlayableRouteTargetKind.VisibleEnemy)
                {
                    var enemy=entities.FirstOrDefault(e=>e.Id==request.TargetId&&Hostile(e.Owner,owner)&&e.Health>0);
                    if(enemy==null)continue;target=goal=enemy.Position;
                }
                else if(request.Kind==PlayableRouteTargetKind.FriendlyAnchor)
                {
                    var ally=entities.FirstOrDefault(e=>e.Id==request.TargetId&&e.Owner==owner&&e.Health>0);
                    if(ally==null)continue;target=goal=ally.Position;
                }
                else if(request.Kind==PlayableRouteTargetKind.PublicObjective)
                {
                    var objective=TerritoryRules.PublicScoutObjectives(profile,owner).FirstOrDefault(o=>o.SiteId==request.TargetId&&o.Reachable);
                    if(objective==null)continue;target=goal=objective.Approach;
                }
                else continue;
                if(request.Target.X!=target.X||request.Target.Z!=target.Z)continue;
                var radius=nav.Radius;
                if(!vision.IsVisible(nav.Position)||!vision.IsVisible(target)||!vision.IsVisible(goal))continue;
                var router=new SharedFlowRouter(safeGeometry,profile.Navigation.ForUnit(radius,nav.Speed,nav.TurnSpeed));
                var path=router.FindPath(nav.Position,goal);
                if(path.Length==0)continue;
                var from=nav.Position;bool safe=true;
                foreach(var point in path)
                {
                    if(!Geometry.SegmentFree(from,point,radius)||!VisibleSegment(vision,from,point)){safe=false;break;}
                    from=point;
                }
                if(safe)result.Add(new PlayableRouteProof(unit.Id,unit.Owner,navigation.Generation,Tick,observedRevision,nav.Position,radius,request.Kind,request.TargetId,target,goal,path));
            }
            return result.ToArray();
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
