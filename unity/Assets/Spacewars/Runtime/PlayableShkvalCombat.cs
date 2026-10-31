using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private sealed class Rocket
        {
            public BallisticFlight Flight;public BallisticContact Predicted;
            public ShkvalLaunchSnapshot Launch;
            public bool BuildingTarget; // Observation established at launch; retained through target loss/destruction.
            public double Elapsed,Radius,BlastRadius,MarkerStartRadius,MarkerOpacity,BuildingHeight;
        }
        private void WriteShkvalLaunches(BinaryWriter w)
        {
            var rows=projectiles.Where(p=>p.Rocket?.Launch!=null).ToArray();
            var magazines=units.Values.Where(u=>u.ShkvalCells!=null).OrderBy(u=>u.Id).ToArray();
            var launchers=units.Values.Where(u=>u.Kind==PlayableEntityKind.Shkval).OrderBy(u=>u.Id).ToArray();
            // Always emit this extension before the default-combat extension, including matches without artillery.
            w.Write(0x53484B34);w.Write(rows.Length);
            foreach(var p in rows){var l=p.Rocket.Launch;w.Write(p.Id);w.Write(l.Barrel);WorldWire.Write(w,l.Ground);WorldWire.Number(w,l.Hull);WorldWire.Number(w,l.Turret);w.Write(l.Upgraded);WorldWire.Number(w,l.Pitch);}
            w.Write(0x53484B32);w.Write(magazines.Length);
            foreach(var u in magazines){w.Write(u.Id);foreach(double cell in u.ShkvalCells)WorldWire.Number(w,cell);}
            w.Write(0x53484B33);w.Write(launchers.Length);foreach(var u in launchers){w.Write(u.Id);WorldWire.Number(w,u.ShkvalPitch);}
        }
        private void ReadShkvalLaunches(BinaryReader r)
        {
            if(r.BaseStream.Position==r.BaseStream.Length)return;
            if(r.ReadInt32()!=0x53484B34)throw new ArgumentException("Invalid Shkval launch extension.");
            int count=r.ReadInt32();if(count<0||count>projectiles.Count)throw new ArgumentException("Invalid Shkval launch count.");
            for(int i=0;i<count;i++){
                int id=r.ReadInt32(),barrel=r.ReadInt32();var ground=WorldWire.ReadBallisticPoint(r);double hull=WorldWire.Number(r),turret=WorldWire.Number(r);bool upgraded=WorldWire.Boolean(r);double pitch=WorldWire.Number(r);
                var p=projectiles.SingleOrDefault(x=>x.Id==id);
                if(p?.Rocket==null||p.Rocket.Launch!=null||barrel<0||barrel>=4||pitch<=-Math.PI/2||pitch>=Math.PI/2)throw new ArgumentException("Invalid Shkval launch binding.");
                var terms=Terms(p.TermsRevision);
                var frame=ShkvalLaunchGeometry.At(ground,hull,turret,pitch,upgraded,barrel,terms.ShkvalModelRadius*terms.ShkvalModelScale/2.05,terms.ShkvalProjectileLength,profile.AuthoredMap?.SurfaceGradient(ground.Ground)??default(NavPoint));
                if(!frame.Center.Equals(p.Rocket.Flight.Start))throw new ArgumentException("Invalid physical Shkval launch origin.");
                p.Rocket.Launch=new ShkvalLaunchSnapshot(barrel,ground,hull,turret,upgraded,p.Rocket.Flight.Tangent(0),p.Rocket.Flight.Duration,p.Rocket.Flight.Start.Y,pitch,p.Rocket.Flight.Start);
            }
            if(r.ReadInt32()!=0x53484B32)throw new ArgumentException("Invalid Shkval magazine extension.");
            count=r.ReadInt32();if(count<0||count>units.Count)throw new ArgumentException("Invalid Shkval magazine count.");
            for(int i=0;i<count;i++){
                int id=r.ReadInt32();if(!units.TryGetValue(id,out var u)||u.Kind!=PlayableEntityKind.Shkval||u.ShkvalCells!=null)throw new ArgumentException("Invalid Shkval magazine binding.");
                u.ShkvalCells=new double[4];
                for(int cell=0;cell<4;cell++){double value=WorldWire.Number(r);if(value<0)throw new ArgumentException("Invalid Shkval cell timer.");u.ShkvalCells[cell]=value;}
            }
            if(r.ReadInt32()!=0x53484B33)throw new ArgumentException("Invalid Shkval elevation extension.");
            count=r.ReadInt32();if(count!=units.Values.Count(u=>u.Kind==PlayableEntityKind.Shkval))throw new ArgumentException("Invalid Shkval elevation count.");
            var seen=new HashSet<int>();for(int i=0;i<count;i++){int id=r.ReadInt32();double pitch=WorldWire.Number(r);if(!seen.Add(id)||!units.TryGetValue(id,out var u)||u.Kind!=PlayableEntityKind.Shkval||pitch<=-Math.PI/2||pitch>=Math.PI/2)throw new ArgumentException("Invalid Shkval elevation binding.");u.ShkvalPitch=pitch;}
        }
        private readonly List<PlayableImpactSnapshot> impacts=new List<PlayableImpactSnapshot>();
        private BallisticBody[] BallisticBodies(PlayableOwner? observer=null,double buildingHeight=0)
        {
            var result=new List<BallisticBody>();
            foreach(var u in units.Values)if(TryActor(u.Id,out var n)&&(observer==null||u.Owner==observer||Vision(observer.Value).IsVisible(n.Position)))
                result.Add(new BallisticBody(u.Id,u.Owner,n.Position,u.Velocity,PlayableUnitRules.Radius(profile,u.Kind),u.Air!=null?HawkBodyHeight:2*PlayableUnitRules.Radius(profile,u.Kind),u.Health,false,u.Air?.Height??GroundHeight(n.Position)));
            foreach(var b in buildings.Values)if(b.Phase!=ConstructionPhase.Pending&&(observer==null||b.Owner==observer||Vision(observer.Value).IsVisible(b.Position)))
                result.Add(new BallisticBody(b.Id,b.Owner,b.Position,default(NavPoint),BuildingRadius(b.Kind),buildingHeight>0?buildingHeight:profile.ShkvalBuildingCollisionHeight,b.Health,true,GroundHeight(b.Position),BuildingCombat(b)));
            return result.ToArray();
        }
        private BallisticFlight BaselineFlight(NavPoint from,NavPoint target)=>new BallisticFlight(new BallisticPoint(from.X,GroundHeight(from)+profile.ShkvalLaunchHeight,from.Z),new BallisticPoint(target.X,GroundHeight(target),target.Z),profile.ShkvalArcHeight,profile.ShkvalProjectileSpeed);
        private BallisticFlight GuidedFlight(Unit u,NavPoint from,NavPoint target)
        {
            var velocity=units.TryGetValue(u.Target,out var victim)?victim.Velocity:default(NavPoint);
            return PlayableBallistics.Aim(from,target,velocity,profile.ShkvalLaunchHeight,profile.ShkvalArcHeight,profile.ShkvalProjectileSpeed,true,profile.ShkvalLeadSpeedThreshold,profile.ShkvalLeadFalloff,profile.ShkvalMaxLeadTimeSec,TerrainHeight);
        }
        private int ArtilleryTarget(Unit u,NavPoint from)=>PlayableBallistics.BestTarget(from,u.Owner,PlayableUnitRules.Range(profile,u.Kind,UnitUpgraded(u)),profile.ShkvalLaunchHeight,profile.ShkvalArcHeight,profile.ShkvalProjectileSpeed,profile.ShkvalProjectileRadius,profile.ShkvalBlastRadius,profile.ShkvalDamage*WeaponMultiplier(u.Owner),profile.ShkvalFriendlyFirePenalty,SolidObstacles(),profile.BallisticWallHeight,BallisticBodies(u.Owner).Where(b=>!AirTarget(b.Id)).ToArray(),TerrainHeight,UnitUpgraded(u),profile.ShkvalLeadSpeedThreshold,profile.ShkvalLeadFalloff,profile.ShkvalMaxLeadTimeSec,Hostile);
        private void AdvanceShkval(Unit u,NavUnit self,double dt)
        {
            bool defaultCombat=u.DefaultCombat!=null;
            if(u.ShkvalCells!=null)for(int cell=0;cell<4;cell++)u.ShkvalCells[cell]=Math.Max(0,u.ShkvalCells[cell]-dt);
            u.Reload=Math.Max(0,u.Reload-dt);u.Repath=Math.Max(0,u.Repath-dt);
            bool moved=Math.Abs(u.Velocity.X)>1e-5||Math.Abs(u.Velocity.Z)>1e-5;
            u.StoppedSeconds=moved?0:u.StoppedSeconds+dt;
            if(u.ExplicitTarget&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack&&!VisibleTarget(u.Owner,u.Target)&&TargetAlive(u.Target)){CombatStop(u.Id);ReturnWeaponToRest(u,self,dt);return;}
            if(u.Target!=0&&!VisibleTarget(u.Owner,u.Target)){if(u.ExplicitTarget&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack)u.CurrentOrder=null;u.Target=0;u.ExplicitTarget=false;if(u.HasAttackMove&&!self.Moving&&CanResumeCombat(u.Id))CombatMove(u.Id,u.AttackMove);}
            if(IsFollowing(u)&&!FollowCanFire(u)){ClearFollowBurst(u);u.Target=0;ReturnWeaponToRest(u,self,dt);return;}
            // Ordinary movement keeps its spatial order. Attack-move stops for a useful visible shot.
            if(!defaultCombat&&!u.ExplicitTarget)u.Target=(!self.Moving&&!navigation.IsPending(u.Id))||u.HasAttackMove?ArtilleryTarget(u,self.Position):0;
            if(!Target(u.Target,out var target,out _)){if(u.HasAttackMove&&!self.Moving&&CanResumeCombat(u.Id)&&Distance(self.Position,u.AttackMove)>profile.Navigation.ArrivalTolerance)CombatMove(u.Id,u.AttackMove);ReturnWeaponToRest(u,self,dt);return;}
            double distance=Distance(self.Position,target);
            if(distance>PlayableUnitRules.Range(profile,u.Kind,UnitUpgraded(u)))
            {
                if(u.ExplicitTarget&&u.Repath<=0&&!self.Moving&&!navigation.IsPending(u.Id))
                {if(TryAttackApproach(u.Id,self.Position,target,out var approach))CombatMove(u.Id,approach);u.Repath=profile.AttackRepathSeconds;}
                ReturnWeaponToRest(u,self,dt);return;
            }
            if(!self.Held&&(self.Moving||navigation.IsPending(u.Id)))CombatStop(u.Id);
            var ground=new BallisticPoint(self.Position.X,GroundHeight(self.Position),self.Position.Z);
            var slope=profile.AuthoredMap?.SurfaceGradient(self.Position)??default(NavPoint);
            double scale=profile.ShkvalModelRadius*profile.ShkvalModelScale/2.05;
            int barrel=u.BurstSequence%4;
            var aimed=UnitUpgraded(u)?GuidedFlight(u,self.Position,target):BaselineFlight(self.Position,target);
            ShkvalLaunchGeometry.Aim(ground,self.Heading,UnitUpgraded(u),barrel,scale,profile.ShkvalProjectileLength,slope,aimed.End,profile.ShkvalArcHeight,out double desired,out double pitch);
            u.Turret=Turn(u.Turret,desired,profile.ShkvalTurretTurnSpeed*dt);
            u.ShkvalPitch=Turn(u.ShkvalPitch,pitch,profile.ShkvalTurretTurnSpeed*dt);
            // Numerical pose equality prevents even a small release snap; turn speed remains profiled.
            if(moved||u.StoppedSeconds<profile.ShkvalStopForMs/1000||u.Reload>0||Math.Abs(Angle(desired-u.Turret))>1e-8||Math.Abs(pitch-u.ShkvalPitch)>1e-8)return;
            var start=ShkvalLaunchGeometry.At(ground,self.Heading,u.Turret,u.ShkvalPitch,UnitUpgraded(u),barrel,scale,profile.ShkvalProjectileLength,slope).Center;
            var flight=new BallisticFlight(start,aimed.End,profile.ShkvalArcHeight,profile.ShkvalProjectileSpeed);
            // Launch prediction is observation-bound. Physical contacts below use full current state.
            var prediction=PlayableBallistics.FirstContact(flight,profile.ShkvalProjectileRadius,SolidObstacles(),profile.BallisticWallHeight,BallisticBodies(u.Owner),u.Id,terrain:TerrainHeight).Value;
            // Four bores and two reserve rockets are approved visual invariants, never ammo gates.
            // Feed cadence follows the existing firing interval: one empty interval, one feed interval.
            double interval=profile.ShkvalFireIntervalMs/1000;
            if(u.ShkvalCells==null)u.ShkvalCells=new double[4];
            int fired=u.BurstSequence%4;u.ShkvalCells[fired]=2*interval;
            // Also handles explicit rapid-fire fixtures: begin the oldest empty cell immediately.
            for(int offset=1;offset<4;offset++){int cell=(fired+offset)%4;if(u.ShkvalCells[cell]>0){u.ShkvalCells[cell]=Math.Min(u.ShkvalCells[cell],interval);break;}}
            projectiles.Add(new Projectile{TermsRevision=profile.Revision,Id=nextProjectile++,Owner=u.Id,Target=u.Target,Faction=u.Owner,Kind=PlayableEntityKind.Shkval,Position=start.Ground,Damage=profile.ShkvalDamage*WeaponMultiplier(u.Owner),
                Rocket=new Rocket{Launch=new ShkvalLaunchSnapshot(u.BurstSequence++%4,new BallisticPoint(self.Position.X,GroundHeight(self.Position),self.Position.Z),self.Heading,u.Turret,UnitUpgraded(u),flight.Tangent(0),flight.Duration,flight.Start.Y,u.ShkvalPitch,flight.Start),Flight=flight,Predicted=prediction,BuildingTarget=buildings.ContainsKey(u.Target),Radius=profile.ShkvalProjectileRadius,BlastRadius=profile.ShkvalBlastRadius,MarkerStartRadius=profile.ShkvalMarkerStartRadius,MarkerOpacity=profile.ShkvalMarkerOpacity,BuildingHeight=profile.ShkvalBuildingCollisionHeight}});
            RememberDefaultShot(u);Sound(PlayableSoundKind.Shot,self.Position,u.Kind,u.Owner);
            u.Reload=profile.ShkvalFireIntervalMs/1000;
        }
        private bool AdvanceRocket(Projectile p,double dt)
        {
            var r=p.Rocket;double previousElapsed=r.Elapsed;var previousPosition=p.Position;double from=r.Elapsed/r.Flight.Duration;r.Elapsed=Math.Min(r.Flight.Duration,r.Elapsed+dt);double to=r.Elapsed/r.Flight.Duration;
            var bodies=BallisticBodies(null,r.BuildingHeight);var solids=SolidObstacles();
            var hit=PlayableBallistics.FirstContact(r.Flight,r.Radius,solids,profile.BallisticWallHeight,bodies,p.Owner,from,to,TerrainHeight);
            p.Position=r.Flight.Point(to).Ground;
            if(hit==null)return false;
            // Snapshot every hit before mutations so a destroyed cover still shields this explosion.
            var victims=bodies.Where(b=>PlayableBallistics.BlastHits(hit.Value.Point,r.BlastRadius,b,solids,profile.BallisticWallHeight,bodies,TerrainHeight)).Select(b=>b.Id).ToArray();
            int visibleMask=0;foreach(PlayableOwner owner in Owners)if(Vision(owner).IsVisible(hit.Value.Point.Ground))visibleMask|=1<<(int)owner;
            var wreckVictims=wrecks.Values.Where(w=>w.Blocks(Tick)&&PlayableBallistics.BlastHits(hit.Value.Point,r.BlastRadius,new BallisticBody(w.Id,w.Owner,w.Position,default(NavPoint),w.Radius,2*w.Radius,1,false,GroundHeight(w.Position)),solids,profile.BallisticWallHeight,bodies,TerrainHeight)).Select(w=>w.Id).ToArray();
            // One prospective publication covers every lethal hit and removed
            // wreck. Refusal precedes all impact facts, so the entire rocket can retry.
            var prospective=new System.Collections.Generic.Dictionary<int,PlayableWreckSnapshot>();
            var removals=new System.Collections.Generic.List<Building>();
            foreach(int id in victims){
                if(units.TryGetValue(id,out var unit)&&unit.Health-p.Damage<=0&&TryActor(id,out var actor))prospective.Add(id,CreateUnitWreck(unit,actor));
                if(buildings.TryGetValue(id,out var building)&&building.Phase!=ConstructionPhase.Pending&&
                    building.Health-p.Damage*(building.Ready?1:profile.ConstructionDamageMultiplier)<=0)removals.Add(building);
            }
            bool geometryChange=wreckVictims.Length>0||removals.Count>0||prospective.Values.Any(w=>w.Blocks(Tick));
            if(geometryChange&&!TryRebuildGeometry(removedWrecks:wreckVictims,removals:removals,additionalWrecks:prospective.Values)){
                r.Elapsed=previousElapsed;p.Position=previousPosition;return false;
            }
            foreach(int id in wreckVictims)wrecks[id]=wrecks[id].Destroy(Tick,PlayableEntityKind.Shkval);
            Sound(PlayableSoundKind.Impact,hit.Value.Point.Ground,p.Kind);
            impacts.Add(new PlayableImpactSnapshot(p.Id,p.Faction,hit.Value.Point,r.BlastRadius,Tick,visibleMask));
            foreach(int id in victims)TryDamagePrepared(id,p.Damage,p.Owner,p.Faction,null,default(NavPoint),null,prospective);
            if(geometryChange){ReconcileWreckRoutes();RetryWreckWaits();}
            return true;
        }
    }
}
