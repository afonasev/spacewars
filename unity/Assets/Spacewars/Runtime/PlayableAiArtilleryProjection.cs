using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        // Frozen ai/release.json artillery fields. Projection is generated from the current owner view.
        private PlayableArtillerySupportSnapshot[] ProjectArtillery(PlayableOwner owner,PlayableVision vision,
            IReadOnlyList<PlayableEntitySnapshot> entities,IReadOnlyList<PlayableBuildingSnapshot> projectedBuildings,AiArmyRegistryState armyState=null)
        {
            if(BackgroundDecisionProjections)return Array.Empty<PlayableArtillerySupportSnapshot>();
            return CaptureArtillery(owner,vision,entities,projectedBuildings,armyState)?.Invoke()
                ??Array.Empty<PlayableArtillerySupportSnapshot>();
        }

        internal bool BackgroundDecisionProjections { get; set; }
        internal Func<PlayableArtillerySupportSnapshot[]> CaptureReviewArtillery(PlayableSnapshot snapshot,AiArmyRegistryState armyState)
            =>CaptureArtillery(snapshot.Owner,null,snapshot.Entities,snapshot.Buildings,armyState,snapshot.Vision);

        private Func<PlayableArtillerySupportSnapshot[]> CaptureArtillery(PlayableOwner owner,PlayableVision liveVision,
            IReadOnlyList<PlayableEntitySnapshot> entities,IReadOnlyList<PlayableBuildingSnapshot> projectedBuildings,
            AiArmyRegistryState armyState,TeamVisionSnapshot frozenVision=null)
        {
            if(Tick%45!=0)return null;
            var guns=entities.Where(e=>e.Owner==owner&&(e.Kind is PlayableEntityKind.Shkval or PlayableEntityKind.Igla)&&e.Health>0).OrderBy(e=>e.Id).ToArray();
            if(guns.Length==0)return null;
            var motion=new Dictionary<int,(double Speed,double Turn)>();
            foreach(var gun in guns)
                if(TryActor(gun.Id,out var actor))motion.Add(gun.Id,(actor.Speed,actor.TurnSpeed));
            var enemies=Owners.Where(other=>Hostile(other,owner)).ToHashSet();
            var vision=frozenVision??liveVision.Snapshot();
            var terms=profile;
            var tick=Tick;
            var generation=navigation.Generation;
            var geometry=Geometry;
            var detached=BackgroundDecisionProjections?DecisionGeometry():null;
            return ()=>ComputeArtillery(terms,tick,generation,detached?.Value??geometry,owner,vision,guns,entities,projectedBuildings,armyState,motion,enemies);
        }

        // Navigation can retire its retained buffers during a delayed decision.
        // Copy a geometry revision once, while the authority still owns its handles.
        private NavGeometry decisionGeometrySource;
        private Lazy<NavGeometry> decisionGeometry;
        private Lazy<NavGeometry> DecisionGeometry()
        {
            if(ReferenceEquals(Geometry,decisionGeometrySource))return decisionGeometry;
            var obstacles=Geometry.Obstacles.Select(obstacle=>obstacle.Polygon==null?obstacle:
                new NavObstacle(obstacle.Polygon.Detached(),obstacle.Bottom,obstacle.Top)).ToArray();
            double extent=Geometry.HalfExtent;
            int revision=Geometry.Revision;
            decisionGeometry=new Lazy<NavGeometry>(()=>new NavGeometry(extent,obstacles,revision));
            decisionGeometrySource=Geometry;
            return decisionGeometry;
        }

        private static PlayableArtillerySupportSnapshot[] ComputeArtillery(PlayableProfile profile,long tick,long generation,
            NavGeometry geometry,PlayableOwner owner,TeamVisionSnapshot vision,PlayableEntitySnapshot[] guns,
            IReadOnlyList<PlayableEntitySnapshot> entities,IReadOnlyList<PlayableBuildingSnapshot> projectedBuildings,
            AiArmyRegistryState armyState,Dictionary<int,(double Speed,double Turn)> motion,HashSet<PlayableOwner> hostiles)
        {
            using var measured=PerformanceProfile.Measure(ProfileStage.Artillery);
            var allCover=entities.Where(e=>e.Owner==owner&&(e.Kind is not (PlayableEntityKind.Shkval or PlayableEntityKind.Igla))&&e.Health>0).ToArray();
            var enemies=entities.Where(e=>hostiles.Contains(e.Owner)&&e.Health>0).Select(e=>e.Position)
                .Concat(projectedBuildings.Where(b=>hostiles.Contains(b.Owner)&&b.Health>0).Select(b=>b.Position)).ToArray();
            var home=projectedBuildings.FirstOrDefault(b=>b.Owner==owner&&b.Kind==PlayableBuildingKind.Headquarters&&b.Health>0);
            var observed=geometry.Obstacles.Where(o=>vision.IsVisible(new NavPoint(o.MinX,o.MinZ))&&vision.IsVisible(new NavPoint(o.MaxX,o.MinZ))&&
                vision.IsVisible(new NavPoint(o.MinX,o.MaxZ))&&vision.IsVisible(new NavPoint(o.MaxX,o.MaxZ))).ToArray();
            var safeGeometry=new NavGeometry(geometry.HalfExtent,observed,1);
            var groundTargets=entities.Where(e=>hostiles.Contains(e.Owner)&&e.Health>0&&e.Kind!=PlayableEntityKind.Hawk&&vision.IsVisible(e.Position)).Select(e=>e.Position)
                .Concat(projectedBuildings.Where(b=>hostiles.Contains(b.Owner)&&b.Health>0&&b.Phase!=ConstructionPhase.Pending&&vision.IsVisible(b.Position)).Select(b=>b.Position)).ToArray();
            var directThreats=entities.Where(e=>hostiles.Contains(e.Owner)&&e.Health>0&&vision.IsVisible(e.Position)&&
                AiRosterCatalog.Initial.Targets(e.Kind,"ground")).ToArray();
            double distance(NavPoint a,NavPoint b)=>Distance(a,b);
            double nearest(NavPoint p)=>enemies.Length==0?Double.PositiveInfinity:enemies.Min(e=>distance(p,e));
            PlayableEntitySnapshot[] cover=allCover;
            bool supported(NavPoint p)=>cover.Any(friend=>distance(p,friend.Position)<=12&&
                (enemies.Length==0||nearest(friend.Position)<nearest(p)));
            bool useful(NavPoint position,PlayableEntitySnapshot gun)
            {
                double range=PlayableUnitRules.Range(profile,gun.Kind,gun.Upgraded);
                return groundTargets.Any(target=>distance(position,target)<=range&&
                    (gun.Kind!=PlayableEntityKind.Igla||safeGeometry.SegmentFree(position,target,0)));
            }
            var result=new List<PlayableArtillerySupportSnapshot>();
            foreach(var gun in guns)
            {
                bool sniper=gun.Kind==PlayableEntityKind.Igla;
                cover=allCover;
                if(armyState!=null)
                {
                    var army=armyState.Armies.FirstOrDefault(a=>a.Major&&a.Phase!=AiArmyPhase.Disbanded&&a.Members.Contains(gun.Id));
                    if(army==null)continue;
                    if(sniper&&army.Reinforcements.Contains(gun.Id))continue;
                    cover=allCover.Where(u=>army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)).ToArray();
                }
                else if(sniper)continue;
                if(sniper)cover=cover.Where(u=>AiEconomyAdmission.IglaCover(u.Kind)>0&&AiReinforcementPolicy.Available(u)).ToArray();
                var currentSupported=supported(gun.Position);var currentUseful=useful(gun.Position,gun);
                var threatened=sniper?directThreats.Any(enemy=>
                    distance(enemy.Position,gun.Position)<=PlayableUnitRules.Range(profile,enemy.Kind,enemy.Upgraded)+
                        PlayableUnitRules.Radius(profile,gun.Kind)&&
                    !cover.Any(friend=>distance(friend.Position,gun.Position)<=profile.FollowDistance+profile.IglaRange&&
                        distance(friend.Position,enemy.Position)<distance(gun.Position,enemy.Position)&&
                        distance(friend.Position,gun.Position)<distance(gun.Position,enemy.Position))):nearest(gun.Position)<8;
                if(sniper&&!threatened&&cover.Length>0&&gun.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow&&!currentUseful)
                    continue;
                if(!threatened&&currentSupported&&currentUseful)
                {result.Add(new PlayableArtillerySupportSnapshot(gun.Id,generation,tick,true,false,true,true,null));continue;}
                var candidates=new List<NavPoint>();
                foreach(var friend in cover.OrderBy(f=>distance(gun.Position,f.Position)).ThenBy(f=>f.Id).Take(3))
                {
                    var enemy=enemies.OrderBy(e=>distance(friend.Position,e)).FirstOrDefault();
                    var rear=enemies.Length>0?new NavPoint(friend.Position.X-enemy.X,friend.Position.Z-enemy.Z):
                        home!=null?new NavPoint(home.Position.X-friend.Position.X,home.Position.Z-friend.Position.Z):
                        new NavPoint(gun.Position.X-friend.Position.X,gun.Position.Z-friend.Position.Z);
                    var length=Math.Max(1e-9,Math.Sqrt(rear.X*rear.X+rear.Z*rear.Z));var x=rear.X/length;var z=rear.Z/length;
                    double rearSpacing=sniper?profile.FollowDistance:5;
                    foreach(var lateral in new[]{0d,-3d,3d})candidates.Add(new NavPoint(friend.Position.X+x*rearSpacing-z*lateral,friend.Position.Z+z*rearSpacing+x*lateral));
                }
                if(home!=null)candidates.Add(home.Position);
                var radius=PlayableUnitRules.Radius(profile,gun.Kind);
                NavPoint? chosen=null;bool chosenUseful=false;
                if(motion.TryGetValue(gun.Id,out var nav))
                {
                    var router=new SharedFlowRouter(safeGeometry,profile.Navigation.ForUnit(radius,nav.Speed,nav.Turn));
                    foreach(var candidate in candidates.Distinct().Where(p=>safeGeometry.IsFree(p,radius))
                        .Where(p=>!threatened||nearest(p)>nearest(gun.Position))
                        .OrderByDescending(supported).ThenByDescending(p=>useful(p,gun))
                        .ThenBy(p=>distance(gun.Position,p)))
                    {
                        var path=router.FindPath(gun.Position,candidate);if(path.Length==0)continue;
                        var from=gun.Position;bool safe=true;
                        foreach(var point in path)
                        {if(!geometry.SegmentFree(from,point,radius)){safe=false;break;}from=point;}
                        if(!safe||distance(from,candidate)>1.5)continue;
                        chosen=candidate;chosenUseful=useful(candidate,gun);break;
                    }
                }
                result.Add(new PlayableArtillerySupportSnapshot(gun.Id,generation,tick,currentSupported,threatened,currentUseful,chosenUseful,chosen));
            }
            return result.ToArray();
        }
    }
}
