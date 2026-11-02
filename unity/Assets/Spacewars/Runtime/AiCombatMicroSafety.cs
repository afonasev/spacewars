using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    internal static class AiCombatMicroSafety
    {
        static readonly object executionAssetGate=new object();
        static string executionAssetIdentity;
        // Real owner construction/restore is a pre-clock gate. This is explicit
        // synchronous startup work, never a safety Advance/transport operation.
        // Model caches depend on authored model identity, not balance/pose values.
        internal static void PrepareExecutionAssets(PlayableProfile profile)
        {
            lock(executionAssetGate){
                string identity=BuildingCombatGeometry.Version;
                if(executionAssetIdentity==identity)return;
                foreach(PlayableBuildingKind kind in Enum.GetValues(typeof(PlayableBuildingKind)))
                    BuildingCombatGeometry.Pose(profile,kind,false,default,0);
                BuildingCombatGeometry.Pose(profile,PlayableBuildingKind.Refinery,true,default,0);
                BuildingCombatGeometry.Pose(profile,PlayableBuildingKind.ScientificCenter,true,default,0);
                executionAssetIdentity=identity;
            }
        }
        internal static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
        static double Height(PlayableProfile p,NavPoint at)=>p.AuthoredMap?.SupportAt(at)?.HeightAt(at)??0;
        internal static IEnumerable<PlayableBuildingSnapshot> Towers(PlayableAiObservation o,AiKnowledgeState knowledge=null)
        {
            if(knowledge!=null&&(knowledge.OwnerId!=o.OwnerId||knowledge.Generation!=o.Generation||knowledge.ObservationTick>o.Tick))throw new ArgumentException("Foreign micro threat knowledge.");
            var known=o.Buildings.Where(b=>o.IsHostile(b.Owner)&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&TerritoryRules.ArmedDefense(b.Kind)).ToList();
            foreach(var c in knowledge?.Contacts??Array.Empty<AiKnownContact>())
                if(c.Building&&o.IsHostile(c.Owner)&&c.HealthAtLastSeen>0&&c.Confidence>0&&Enum.IsDefined(typeof(PlayableBuildingKind),c.Kind)&&TerritoryRules.ArmedDefense((PlayableBuildingKind)c.Kind)&&!known.Any(b=>b.Id==c.Id))
                    known.Add(new PlayableBuildingSnapshot(c.Id,c.Owner,(PlayableBuildingKind)c.Kind,c.Position,c.HealthAtLastSeen,1,0,0,default,includePrivateState:false));
            return known;
        }
        internal static bool Covers(PlayableAiObservation o,PlayableProfile p,PlayableBuildingSnapshot tower,PlayableEntitySnapshot u,NavPoint at)
        {
            bool air=u.Kind==PlayableEntityKind.Hawk;
            double range=tower.Kind==PlayableBuildingKind.GaussTower?p.GaussRange:p.ExplorerRange*p.TurretRangeMultiplier;
            if(air&&tower.Kind==PlayableBuildingKind.GaussTower||Distance(tower.Position,at)>range)return false;
            // The half body height is the existing authored Hawk collision extent
            // used in PlayableHawkCombat, not a new gameplay tuning value.
            double targetHeight=air?u.Altitude+1.8145018368959427*p.HawkModelScale/2:Height(p,at)+(p.AuthoredMap?.DirectFireHeight??1);
            var target=new BallisticPoint(at.X,targetHeight,at.Z);double heading=Math.Atan2(at.Z-tower.Position.Z,at.X-tower.Position.X);
            double pivot=Height(p,tower.Position)+BuildingCombatGeometry.ApronLift+(tower.Kind==PlayableBuildingKind.GaussTower?GaussTowerGeometry.PitchHeight:SentryTurretGeometry.PitchHeight)*p.TurretModelScale;
            double pitch=Math.Atan2(targetHeight-pivot,Distance(tower.Position,at));if(pitch < -p.TurretDepression*Math.PI/180)return false;
            var muzzle=tower.Kind==PlayableBuildingKind.GaussTower?GaussTowerGeometry.Muzzle(tower.Position,Height(p,tower.Position)+BuildingCombatGeometry.ApronLift,p.TurretModelScale,heading,pitch):SentryTurretGeometry.Muzzle(tower.Position,Height(p,tower.Position)+BuildingCombatGeometry.ApronLift,p.TurretModelScale,heading,pitch,0);
            double radius=tower.Kind==PlayableBuildingKind.GaussTower?p.GaussBeamRadius:0;
            return PlayableBallistics.TerrainLineClear(muzzle,target,radius,PlayableMap.SolidObstacles(p),p.BallisticWallHeight,q=>Height(p,q))&&
                !o.Buildings.Where(b=>b.Id!=tower.Id&&b.Id!=u.Id&&b.Phase!=ConstructionPhase.Pending&&b.Health>0).Any(b=>BuildingCombatGeometry.Pose(p,b.Kind,b.ModelUpgraded,b.Position,Height(p,b.Position),b.Heading,constructionProgress:b.Progress).Segment(muzzle,target,radius,out _,out _));
        }
        internal static bool FireClear(PlayableAiObservation o,PlayableProfile p,PlayableEntitySnapshot unit,PlayableEntitySnapshot target)
        {
            double direct=p.AuthoredMap?.DirectFireHeight??1;
            // Same authored air collision extent as ordinary combat. This is a
            // conservative current shot candidate, not a prediction of future fire.
            double FromHeight()=>unit.Kind==PlayableEntityKind.Hawk?unit.Altitude+1.8145018368959427*p.HawkModelScale/2:Height(p,unit.Position)+direct;
            double ToHeight()=>target.Kind==PlayableEntityKind.Hawk?target.Altitude+1.8145018368959427*p.HawkModelScale/2:Height(p,target.Position)+direct;
            var from=new BallisticPoint(unit.Position.X,FromHeight(),unit.Position.Z);var to=new BallisticPoint(target.Position.X,ToHeight(),target.Position.Z);
            double radius=unit.Kind==PlayableEntityKind.Tank?p.TankProjectileCollisionRadius:0;
            return PlayableBallistics.TerrainLineClear(from,to,radius,PlayableMap.SolidObstacles(p),p.BallisticWallHeight,q=>Height(p,q))&&
                !o.Buildings.Where(b=>b.Id!=unit.Id&&b.Id!=target.Id&&b.Health>0&&b.Phase!=ConstructionPhase.Pending).Any(b=>BuildingCombatGeometry.Pose(p,b.Kind,b.ModelUpgraded,b.Position,Height(p,b.Position),b.Heading,constructionProgress:b.Progress).Segment(from,to,radius,out _,out _));
        }
        internal static bool Path(PlayableAiObservation o,PlayableProfile p,PlayableEntitySnapshot u,IEnumerable<NavPoint> path,bool withdrawal,AiKnowledgeState knowledge=null)
        {
            var points=path.ToArray();if(points.Length==0)return false;
            foreach(var tower in Towers(o,knowledge))
            {
                bool escaping=withdrawal&&Covers(o,p,tower,u,u.Position);double previous=Distance(tower.Position,u.Position);var at=u.Position;
                foreach(var end in points)
                {
                    double length=Distance(at,end),dx=end.X-at.X,dz=end.Z-at.Z;
                    double closest=length==0?0:Math.Max(0,Math.Min(1,((tower.Position.X-at.X)*dx+(tower.Position.Z-at.Z)*dz)/(length*length)));
                    // Physical-envelope sampling plus exact closest point protects
                    // tangencies without introducing another gameplay distance.
                    int steps=Math.Max(1,(int)Math.Ceiling(length/PlayableUnitRules.Radius(p,u.Kind)));
                    foreach(var t in Enumerable.Range(1,steps).Select(i=>i/(double)steps).Append(closest).Distinct().OrderBy(t=>t))
                    {
                        var q=new NavPoint(at.X+dx*t,at.Z+dz*t);double distance=Distance(tower.Position,q);
                        if(Covers(o,p,tower,u,q)){if(!escaping||distance<previous)return false;}else escaping=false;
                        previous=distance;
                    }
                    at=end;
                }
                if(Covers(o,p,tower,u,points.Last()))return false;
            }
            return true;
        }
    }
    internal sealed partial class PlayableDomain
    {
        // Consume the same actor/formation path already validated by ArmyMoveRejection.
        // Paths are borrowed for this call only; no new query, solver or saved state.
        string CombatMicroStaticPath(PlayableAiAction action,PlayableAiObservation o,PlayableEntitySnapshot unit,NavPoint[] path,bool withdrawal,AiKnowledgeState knowledge,bool repair,AiProfile ai,AiDifficulty difficulty,AiCombatMicroPolicy micro)
        {
            if(!repair&&!AiCombatMicroSafety.Towers(o,knowledge).Any())return null;
            if(repair)
            {
                var tower=o.Buildings.FirstOrDefault(b=>b.Id==action.ParentId&&b.Owner==o.Owner&&b.Kind==PlayableBuildingKind.RepairTower&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.Lifecycle?.Selling!=true);
                double length=0;var at=unit.Position;foreach(var point in path){length+=Distance(at,point);at=point;}
                if(!micro.RepairEscapeAllowed(o,profile,ai,difficulty,unit,path))return "observed incoming loss makes repair escape futile";
                if(tower==null||path.Length==0||Distance(path.Last(),tower.Position)>profile.TurretRange||!AiCombatMicroPolicy.RepairTimeAllowed(profile,ai,difficulty,unit,length))return "repair route or healing exceeds recovery limit";
            }
            return AiCombatMicroSafety.Path(o,profile,unit,path,withdrawal,knowledge)?null:"known static fire blocks micro path";
        }
    }
}
