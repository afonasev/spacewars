using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        // Mines intentionally have a separate ID space, outside targetable actors.
        private sealed class Mine
        {
            internal int Id,Primary;internal PlayableOwner Owner;internal NavPoint Point;
            internal double Vision,Radius,Direct,Splash,Size,Height,Duration;
            internal bool Pending;
        }
        private readonly List<Mine> mines=new List<Mine>();
        private readonly List<OrbitalMineBlastSnapshot> mineBlasts=new List<OrbitalMineBlastSnapshot>();
        private int nextMine=1;
        private bool ValidMinePlacement(PlayableOwner owner,NavPoint point)
        {
            RefreshVision();double radius=OrbitalData.orbitalMineModelRadius;
            return Vision(owner).IsVisible(point)&&projectileGeometry.IsFree(point,radius)&&
                (profile.AuthoredMap==null||profile.AuthoredMap.SupportsFootprint(point,radius))&&
                !buildings.Values.Any(b=>b.Phase!=ConstructionPhase.Pending&&Distance(b.Position,point)<BuildingRadius(b.Kind)+radius);
        }
        private void PlaceMine(PlayableOwner owner,NavPoint point)
        {
            var data=OrbitalData;
            while(mines.Count(m=>m.Owner==owner)>=(int)data.orbitalMineCapacity)mines.Remove(mines.First(m=>m.Owner==owner));
            mines.Add(new Mine{Id=nextMine++,Owner=owner,Point=point,Vision=data.orbitalMineVision,Radius=data.orbitalMineBlastRadius,Direct=data.orbitalMineDirectDamage,Splash=data.orbitalMineSplashDamage,Size=data.orbitalMineModelRadius,Height=data.orbitalMineModelHeight,Duration=data.orbitalMineEffectSeconds});
            visionInputProfile=null;RefreshVision();
        }
        private OrbitalMineSnapshot[] MineSnapshots(TeamVisionSnapshot vision)=>mines.Where(m=>vision==null||vision.IsVisible(m.Point)).Select(m=>new OrbitalMineSnapshot(m.Id,m.Owner,m.Point,m.Size,m.Height,m.Radius)).ToArray();
        private OrbitalMineBlastSnapshot[] MineBlastSnapshots(TeamVisionSnapshot vision)=>mineBlasts.Where(m=>vision==null||vision.IsVisible(m.Point)).ToArray();
        private BallisticBody MineBody(Mine mine)=>new BallisticBody(-mine.Id,mine.Owner,mine.Point,default(NavPoint),mine.Size,mine.Height,1,false,GroundHeight(mine.Point));
        private void MarkMinesForAreaDamage(BallisticPoint point,double radius)
        {
            var solids=SolidObstacles();var bodies=BallisticBodies();
            foreach(var mine in mines)if(PlayableBallistics.BlastHits(point,radius,MineBody(mine),solids,profile.BallisticWallHeight,bodies,TerrainHeight))mine.Pending=true;
        }
        private void AdvanceMines()
        {
            mineBlasts.RemoveAll(b=>(Tick-b.Tick)/30d>=b.Duration);
            foreach(var mine in mines){
                if(mine.Pending)continue;
                foreach(var unit in units.Values){
                    if(unit.Air!=null||unit.Health<=0||!Hostile(mine.Owner,unit.Owner)||!TryActor(unit.Id,out var actor))continue;
                    if(profile.AuthoredMap!=null&&(!profile.AuthoredMap.TryLocate(mine.Point,0,out var location)||!profile.AuthoredMap.CompatibleCombatSurface(location,actor.Location)))continue;
                    if(SegmentDistance(mine.Point,unit.PreviousPosition,actor.Position)>mine.Size+PlayableUnitRules.Radius(profile,unit.Kind))continue;
                    mine.Pending=true;mine.Primary=unit.Id;break;
                }
            }
            if(!mines.Any(m=>m.Pending))return;
            var solids=SolidObstacles();var bodies=BallisticBodies();
            // Resolve the closure before any mutation, so chains and lethal splash share
            // one geometry publication. A refusal leaves the complete burst retryable.
            var burst=mines.Where(m=>m.Pending).ToList();
            for(int i=0;i<burst.Count;i++){
                var source=burst[i];var at=new BallisticPoint(source.Point.X,GroundHeight(source.Point)+source.Height/2,source.Point.Z);
                foreach(var candidate in mines)if(!burst.Contains(candidate)&&PlayableBallistics.BlastHits(at,source.Radius,MineBody(candidate),solids,profile.BallisticWallHeight,bodies,TerrainHeight))burst.Add(candidate);
            }
            var hits=new List<(Mine mine,int id,double damage)>();var total=new Dictionary<int,double>();
            foreach(var mine in burst){
                var at=new BallisticPoint(mine.Point.X,GroundHeight(mine.Point)+mine.Height/2,mine.Point.Z);
                foreach(var body in bodies){
                    bool primary=mine.Primary==body.Id;
                    if(!primary&&!PlayableBallistics.BlastHits(at,mine.Radius,body,solids,profile.BallisticWallHeight,bodies,TerrainHeight))continue;
                    double damage=primary?mine.Direct:mine.Splash;hits.Add((mine,body.Id,damage));
                    total[body.Id]=(total.TryGetValue(body.Id,out var prior)?prior:0)+damage;
                }
            }
            var prospective=new Dictionary<int,PlayableWreckSnapshot>();var removals=new List<Building>();
            foreach(var pair in total){
                if(units.TryGetValue(pair.Key,out var unit)&&unit.Health<=pair.Value&&TryActor(unit.Id,out var actor))prospective.Add(unit.Id,CreateUnitWreck(unit,actor));
                if(buildings.TryGetValue(pair.Key,out var building)&&building.Health<=pair.Value*(building.Ready?1:profile.ConstructionDamageMultiplier))removals.Add(building);
            }
            var removedActors=prospective.Values.Where(w=>w.Kind!=PlayableEntityKind.Hawk).Select(w=>w.Id).ToArray();
            bool geometryChange=removals.Count>0||removedActors.Length>0||prospective.Values.Any(w=>w.Blocks(Tick));
            if(geometryChange&&!TryRebuildGeometry(removals:removals,additionalWrecks:prospective.Values,removedActors:removedActors.Length==0?null:removedActors))return;
            foreach(var mine in burst){AddLingeringVision(mine.Owner,mine.Point,mine.Vision);mines.Remove(mine);mineBlasts.Add(new OrbitalMineBlastSnapshot(mine.Id,mine.Owner,mine.Point,mine.Radius,Tick,mine.Duration));Sound(PlayableSoundKind.Impact,mine.Point,PlayableEntityKind.Shkval);MatchEvent(MatchEventKind.MineTriggered,mine.Owner,mine.Point,mine.Id);}
            var notified=new HashSet<(int mine,PlayableOwner owner)>();
            foreach(var hit in hits)
            {
                PlayableOwner? damaged=units.TryGetValue(hit.id,out var victim)&&victim.Health>0?victim.Owner:
                    buildings.TryGetValue(hit.id,out var structure)&&structure.Health>0?(PlayableOwner?)structure.Owner:null;
                bool applied=TryDamagePrepared(hit.id,hit.damage,0,hit.mine.Owner,null,default(NavPoint),null,prospective);
                if(applied&&hit.damage>0&&damaged.HasValue&&notified.Add((hit.mine.Id,damaged.Value)))
                    MatchEvent(MatchEventKind.MineHit,damaged.Value,hit.mine.Point,hit.mine.Id);
            }
            visionInputProfile=null;
            if(geometryChange){ReconcileWreckRoutes();RetryWreckWaits();}
        }
        private void WriteMines(BinaryWriter w)
        {
            w.Write(0x4d494e31);w.Write(nextMine);w.Write(mines.Count);
            foreach(var m in mines){w.Write(m.Id);w.Write((int)m.Owner);WorldWire.Number(w,m.Point.X);WorldWire.Number(w,m.Point.Z);foreach(double v in new[]{m.Vision,m.Radius,m.Direct,m.Splash,m.Size,m.Height,m.Duration})WorldWire.Number(w,v);w.Write(m.Pending);w.Write(m.Primary);}
            w.Write(mineBlasts.Count);foreach(var b in mineBlasts){w.Write(b.Id);w.Write((int)b.Owner);WorldWire.Number(w,b.Point.X);WorldWire.Number(w,b.Point.Z);WorldWire.Number(w,b.Radius);w.Write(b.Tick);WorldWire.Number(w,b.Duration);}
        }
        private void ReadMines(BinaryReader r)
        {
            if(r.ReadInt32()!=0x4d494e31)throw new ArgumentException("Missing mine state.");nextMine=r.ReadInt32();int count=r.ReadInt32();
            if(nextMine<1||count<0||count>Owners.Count()*3)throw new ArgumentException("Invalid mine count.");int previous=0;
            for(int i=0;i<count;i++){
                var m=new Mine{Id=r.ReadInt32(),Owner=WorldWire.EnumValue<PlayableOwner>(r),Point=new NavPoint(WorldWire.Number(r),WorldWire.Number(r)),Vision=WorldWire.Number(r),Radius=WorldWire.Number(r),Direct=WorldWire.Number(r),Splash=WorldWire.Number(r),Size=WorldWire.Number(r),Height=WorldWire.Number(r),Duration=WorldWire.Number(r),Pending=r.ReadBoolean(),Primary=r.ReadInt32()};
                if(m.Id<=previous||m.Id>=nextMine||!HasOwner(m.Owner)||m.Vision<=0||m.Radius<=0||m.Direct<=0||m.Splash<=0||m.Size<=0||m.Height<=0||m.Duration<=0||Math.Abs(m.Point.X)>profile.ArenaHalfExtent||Math.Abs(m.Point.Z)>profile.ArenaHalfExtent||m.Primary<0||!m.Pending&&m.Primary!=0||mines.Count(x=>x.Owner==m.Owner)>=3)throw new ArgumentException("Invalid mine.");
                previous=m.Id;mines.Add(m);
            }
            int blasts=r.ReadInt32();if(blasts<0||blasts>10000)throw new ArgumentException("Invalid mine blasts.");var ids=new HashSet<int>();
            for(int i=0;i<blasts;i++){
                var b=new OrbitalMineBlastSnapshot(r.ReadInt32(),WorldWire.EnumValue<PlayableOwner>(r),new NavPoint(WorldWire.Number(r),WorldWire.Number(r)),WorldWire.Number(r),r.ReadInt64(),WorldWire.Number(r));
                if(b.Id<1||b.Id>=nextMine||!ids.Add(b.Id)||mines.Any(m=>m.Id==b.Id)||!HasOwner(b.Owner)||b.Radius<=0||b.Duration<=0||b.Tick>Tick||(Tick-b.Tick)/30d>=b.Duration)throw new ArgumentException("Invalid mine blast.");mineBlasts.Add(b);
            }
            visionInputProfile=null;
        }
    }
}
