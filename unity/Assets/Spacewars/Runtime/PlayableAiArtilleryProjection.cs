using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        // Frozen ai/release.json artillery fields. Projection is generated from the current owner view.
        private PlayableArtillerySupportSnapshot[] ProjectArtillery(PlayableOwner owner,PlayableVision vision,
            IReadOnlyList<PlayableEntitySnapshot> entities,IReadOnlyList<PlayableBuildingSnapshot> projectedBuildings,AiArmyRegistryState armyState=null)
        {
            if(Tick%45!=0)return Array.Empty<PlayableArtillerySupportSnapshot>();
            var guns=entities.Where(e=>e.Owner==owner&&e.Kind==PlayableEntityKind.Shkval&&e.Health>0).OrderBy(e=>e.Id).ToArray();
            if(guns.Length==0)return Array.Empty<PlayableArtillerySupportSnapshot>();
            var allCover=entities.Where(e=>e.Owner==owner&&e.Kind!=PlayableEntityKind.Shkval&&e.Health>0).ToArray();
            var enemies=entities.Where(e=>Hostile(e.Owner,owner)&&e.Health>0).Select(e=>e.Position)
                .Concat(projectedBuildings.Where(b=>Hostile(b.Owner,owner)&&b.Health>0).Select(b=>b.Position)).ToArray();
            var home=projectedBuildings.FirstOrDefault(b=>b.Owner==owner&&b.Kind==PlayableBuildingKind.Headquarters&&b.Health>0);
            var observed=Geometry.Obstacles.Where(o=>vision.IsVisible(new NavPoint(o.MinX,o.MinZ))&&vision.IsVisible(new NavPoint(o.MaxX,o.MinZ))&&
                vision.IsVisible(new NavPoint(o.MinX,o.MaxZ))&&vision.IsVisible(new NavPoint(o.MaxX,o.MaxZ))).ToArray();
            var safeGeometry=new NavGeometry(Geometry.HalfExtent,observed,1);
            var bodies=BallisticBodies(owner);
            var solids=PlayableMap.StaticObstacles(profile);
            double distance(NavPoint a,NavPoint b)=>Distance(a,b);
            double nearest(NavPoint p)=>enemies.Length==0?Double.PositiveInfinity:enemies.Min(e=>distance(p,e));
            PlayableEntitySnapshot[] cover=allCover;
            bool supported(NavPoint p)=>cover.Any(friend=>distance(p,friend.Position)<=12&&
                (enemies.Length==0||nearest(friend.Position)<nearest(p)));
            bool useful(NavPoint p,int gunId)=>PlayableBallistics.BestTarget(p,owner,PlayableUnitRules.Range(profile,PlayableEntityKind.Shkval,units.TryGetValue(gunId,out var actor)&&UnitUpgraded(actor)),
                profile.ShkvalLaunchHeight,profile.ShkvalArcHeight,profile.ShkvalProjectileSpeed,profile.ShkvalProjectileRadius,
                profile.ShkvalBlastRadius,profile.ShkvalDamage,profile.ShkvalFriendlyFirePenalty,solids,profile.BallisticWallHeight,
                bodies.Select(b=>b.Id==gunId?new BallisticBody(b.Id,b.Owner,p,default(NavPoint),b.Radius,b.Height,b.Health,b.Building):b).ToArray(),hostile:Hostile)>0;
            var result=new List<PlayableArtillerySupportSnapshot>();
            foreach(var gun in guns)
            {
                if(armyState!=null)
                {
                    var army=armyState.Armies.FirstOrDefault(a=>a.Major&&a.Phase!=AiArmyPhase.Disbanded&&a.Members.Contains(gun.Id));
                    if(army==null)continue;
                    cover=allCover.Where(u=>army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)).ToArray();
                }
                var currentSupported=supported(gun.Position);var currentUseful=useful(gun.Position,gun.Id);
                var threatened=nearest(gun.Position)<8;
                if(!threatened&&currentSupported&&currentUseful)
                {result.Add(new PlayableArtillerySupportSnapshot(gun.Id,navigation.Generation,Tick,true,false,true,true,null));continue;}
                var candidates=new List<NavPoint>();
                foreach(var friend in cover.OrderBy(f=>distance(gun.Position,f.Position)).ThenBy(f=>f.Id).Take(3))
                {
                    var enemy=enemies.OrderBy(e=>distance(friend.Position,e)).FirstOrDefault();
                    var rear=enemies.Length>0?new NavPoint(friend.Position.X-enemy.X,friend.Position.Z-enemy.Z):
                        home!=null?new NavPoint(home.Position.X-friend.Position.X,home.Position.Z-friend.Position.Z):
                        new NavPoint(gun.Position.X-friend.Position.X,gun.Position.Z-friend.Position.Z);
                    var length=Math.Max(1e-9,Math.Sqrt(rear.X*rear.X+rear.Z*rear.Z));var x=rear.X/length;var z=rear.Z/length;
                    foreach(var lateral in new[]{0d,-3d,3d})candidates.Add(new NavPoint(friend.Position.X+x*5-z*lateral,friend.Position.Z+z*5+x*lateral));
                }
                if(home!=null)candidates.Add(home.Position);
                var radius=PlayableUnitRules.Radius(profile,PlayableEntityKind.Shkval);
                var nav=navigation.Crowd.TryGet(gun.Id,out var n)?n:null;
                NavPoint? chosen=null;bool chosenUseful=false;
                if(nav!=null)
                {
                    var router=new SharedFlowRouter(safeGeometry,profile.Navigation.ForUnit(radius,nav.Speed,nav.TurnSpeed));
                    foreach(var candidate in candidates.Distinct().Where(p=>vision.IsVisible(p)&&safeGeometry.IsFree(p,radius))
                        .Where(p=>!threatened||nearest(p)>nearest(gun.Position))
                        .OrderByDescending(supported).ThenByDescending(p=>useful(p,gun.Id))
                        .ThenBy(p=>distance(gun.Position,p)))
                    {
                        var path=router.FindPath(gun.Position,candidate);if(path.Length==0)continue;
                        var from=gun.Position;bool safe=true;
                        foreach(var point in path)
                        {if(!Geometry.SegmentFree(from,point,radius)||!VisibleSegment(vision,from,point)){safe=false;break;}from=point;}
                        if(!safe||distance(from,candidate)>1.5)continue;
                        chosen=candidate;chosenUseful=useful(candidate,gun.Id);break;
                    }
                }
                result.Add(new PlayableArtillerySupportSnapshot(gun.Id,navigation.Generation,Tick,currentSupported,threatened,currentUseful,chosenUseful,chosen));
            }
            return result.ToArray();
        }
    }
}
