using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private readonly Dictionary<int,PlayableWreckSnapshot> wrecks=new Dictionary<int,PlayableWreckSnapshot>();
        private static long WreckTicks(double seconds)=>(long)Math.Ceiling(Math.Round(seconds,6)*30d);
        private static double WreckChoice(int id,long tick,int salt)
        {unchecked{uint x=(uint)id*747796405u+(uint)tick*2891336453u+(uint)salt;x=((x>>((int)(x>>28)+4))^x)*277803737u;x=(x>>22)^x;return x/4294967296d;}}
        private bool WreckLandingFree(NavPoint p,double radius,int ignore)
        {
            if(!Geometry.IsFree(p,radius))return false;
            if(profile.AuthoredMap!=null&&!navigation.Crowd.Locate(p,radius,out _))return false;
            foreach(var u in units.Values)if(u.Id!=ignore&&u.Air==null&&TryActor(u.Id,out var actor)&&Distance(p,actor.Position)<=radius+actor.Radius)return false;
            return !wrecks.Values.Any(w=>w.Id!=ignore&&w.HasGround&&w.DestroyedTick<0&&Tick<w.ExpireTick&&Distance(p,w.Position)<=radius+w.Radius);
        }
        private PlayableWreckSnapshot CreateUnitWreck(Unit u,NavUnit actor)
        {
            bool air=u.Kind==PlayableEntityKind.Hawk;double radius=PlayableUnitRules.Radius(profile,u.Kind);
            long land=air?Tick+WreckTicks(profile.HawkWreckFallSeconds):Tick;
            if(air&&(!WreckLandingFree(actor.Position,radius,u.Id)||WreckChoice(u.Id,Tick,71^matchSeed)*100>=profile.HawkWreckChancePercent))land=-1;
            long reveal=air?Math.Max(Tick,land):Tick+WreckTicks(profile.ImpactEffectSec);
            long sink=reveal+WreckTicks(profile.UnitWreckLifetimeSeconds),expire=sink+WreckTicks(profile.WreckSinkSeconds);
            if(land<0)expire=Tick+Math.Max(WreckTicks(profile.UnitDeathSmokeSeconds),WreckTicks(profile.HawkDebrisSeconds));
            double burial=profile.WreckBurialMin+(profile.WreckBurialMax-profile.WreckBurialMin)*WreckChoice(u.Id,Tick,149^matchSeed);
            int variant=1+(int)(WreckChoice(u.Id,Tick,283^matchSeed)*(air?4:3));
            return new PlayableWreckSnapshot(u.Id,u.Owner,u.Kind,actor.Position,actor.Heading,radius,u.Air?.Height??GroundHeight(actor.Position),burial,variant,Tick,land,reveal,sink,expire,upgraded:air?HawkSalvo(u.Owner):UnitUpgraded(u));
        }
        private bool VisibleWreck(PlayableOwner owner,int id)=>wrecks.TryGetValue(id,out var w)&&w.Blocks(Tick)&&Vision(owner).IsVisible(w.Position);
        private bool WreckObstaclePublished(PlayableWreckSnapshot w)=>Geometry.Obstacles.Any(o=>o.GeometryEquals(new NavObstacle(w.Position,w.Radius)));
        private IEnumerable<NavObstacle> WreckObstacles(int[] removed=null)=>wrecks.Values.Where(w=>w.Blocks(Tick)&&(w.Kind!=PlayableEntityKind.Hawk||WreckObstaclePublished(w))&&(removed==null||!removed.Contains(w.Id))).OrderBy(w=>w.Id).Select(w=>new NavObstacle(w.Position,w.Radius));
        private PlayableWreckSnapshot[] WreckSnapshots(PlayableOwner? owner=null)=>wrecks.Values.Where(w=>!owner.HasValue||Vision(owner.Value).IsVisible(w.Position)).OrderBy(w=>w.Id).ToArray();
        private bool DestroyWreck(int id,PlayableEntityKind weapon)
        {
            if(!wrecks.TryGetValue(id,out var w)||!w.Blocks(Tick))return false;
            return TryDestroyWrecks(new[]{id},weapon);
        }
        private bool TryDestroyWrecks(int[] ids,PlayableEntityKind weapon)
        {
            if(ids.Length==0)return true;
            // Prepare the prospective geometry before changing any hit/lifecycle facts.
            // A refused direct hit or blast remains available for the projectile to retry.
            if(!TryRebuildGeometry(removedWrecks:ids))return false;
            foreach(int id in ids)wrecks[id]=wrecks[id].Destroy(Tick,weapon);
            ReconcileWreckRoutes();RetryWreckWaits();return true;
        }
        private void AdvanceWrecks()
        {
            bool changed=false;Dictionary<int,PlayableWreckSnapshot> next=null;
            foreach(var w in wrecks.Values){
                long end=w.DestroyedTick>=0?w.DestroyedTick+Math.Max(WreckTicks(profile.WreckHitSmokeSeconds),WreckTicks(profile.DirectImpactSeconds)):w.ExpireTick;
                if(Tick>=end){
                    changed|=w.HasGround&&w.DestroyedTick<0;
                    if(next==null)next=new Dictionary<int,PlayableWreckSnapshot>(wrecks);next.Remove(w.Id);
                }
                else if(w.HasGround&&w.DestroyedTick<0&&w.Kind==PlayableEntityKind.Hawk&&Tick>=w.LandTick&&!WreckObstaclePublished(w)){
                    // A refused landing must retry after its scheduled tick, and must
                    // recheck occupancy before publishing its first ground obstacle.
                    if(next==null)next=new Dictionary<int,PlayableWreckSnapshot>(wrecks);
                    if(!WreckLandingFree(w.Position,w.Radius,w.Id))next[w.Id]=w.WithoutLanding(Tick+Math.Max(WreckTicks(profile.UnitDeathSmokeSeconds),WreckTicks(profile.HawkDebrisSeconds)),GroundHeight(w.Position)+w.Radius);
                    else changed=true;
                }
            }
            if(next==null)return;
            // Keep lifecycle entries until the prospective obstacle set commits.
            // Busy host boundaries and budget refusals are retried on a later tick.
            if(changed&&!TryRebuildGeometry(wreckState:next.Values))return;
            wrecks.Clear();foreach(var row in next)wrecks.Add(row.Key,row.Value);
            if(changed){ReconcileWreckRoutes();RetryWreckWaits();}
        }
        private void WriteWrecks(BinaryWriter writer)
        {
            writer.Write(0x57524B32);WorldWire.Array(writer,wrecks.Values.OrderBy(w=>w.Id).ToArray(),w=>{
                writer.Write(w.Id);writer.Write((int)w.Owner);writer.Write((int)w.Kind);WorldWire.Write(writer,w.Position);
                WorldWire.Number(writer,w.Heading);WorldWire.Number(writer,w.Radius);WorldWire.Number(writer,w.DeathHeight);WorldWire.Number(writer,w.Burial);writer.Write(w.Variant);
                writer.Write(w.DeathTick);writer.Write(w.LandTick);writer.Write(w.RevealTick);writer.Write(w.SinkTick);writer.Write(w.ExpireTick);writer.Write(w.DestroyedTick);writer.Write((int)w.ImpactKind);writer.Write(w.Upgraded);
            });
        }
        private void ReadWrecks(BinaryReader r)
        {
            int wreckVersion=r.ReadInt32();if(wreckVersion!=0x57524B31&&wreckVersion!=0x57524B32)throw new ArgumentException("Invalid wreck section.");
            foreach(var w in WorldWire.Array(r,()=>{
                int id=r.ReadInt32();var owner=WorldWire.EnumValue<PlayableOwner>(r);var kind=WorldWire.EnumValue<PlayableEntityKind>(r);var point=WorldWire.ReadPoint(r);
                double heading=WorldWire.Number(r),radius=WorldWire.Number(r),height=WorldWire.Number(r),burial=WorldWire.Number(r);int variant=r.ReadInt32();
                long death=r.ReadInt64(),land=r.ReadInt64(),reveal=r.ReadInt64(),sink=r.ReadInt64(),expire=r.ReadInt64(),destroyed=r.ReadInt64();var impact=WorldWire.EnumValue<PlayableEntityKind>(r);bool upgraded=wreckVersion==0x57524B32&&r.ReadBoolean();
                if(id<1||id>=nextId||units.ContainsKey(id)||!HasOwner(owner)||death<0||death>Tick||land< -1||land>=0&&land<death||reveal<death||sink<reveal||expire<=death||radius<=0||burial<0||variant<1||variant>(kind==PlayableEntityKind.Hawk?4:3)||destroyed< -1||destroyed>Tick)throw new ArgumentException("Invalid wreck state.");
                return WorldWire.Record(r,()=>new PlayableWreckSnapshot(id,owner,kind,point,heading,radius,height,burial,variant,death,land,reveal,sink,expire,destroyed,impact,upgraded));
            }))if(wrecks.ContainsKey(w.Id))throw new ArgumentException("Duplicate wreck.");else wrecks.Add(w.Id,w);
        }
    }
}
