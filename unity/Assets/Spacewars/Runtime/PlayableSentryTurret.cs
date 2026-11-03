using System;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private double TurretRange=>profile.TurretRange;
        private double DefenseRange(Building b)=>b.Kind==PlayableBuildingKind.GaussTower?profile.GaussRange:TurretRange;
        private bool TurretLegal(Building b,int id)
            =>(b.Kind!=PlayableBuildingKind.GaussTower||!AirTarget(id))&&id!=b.Id&&!wrecks.ContainsKey(id)&&VisibleTarget(b.Owner,id)&&Target(id,out var point,out var hp)&&hp>0&&Distance(b.Position,point)<=DefenseRange(b);
        private PlayableTurretSnapshot TurretSnapshot(Building b,PlayableOwner? observer=null)
        {
            var s=b.Weapon;
            if(b.Kind==PlayableBuildingKind.GaussTower)return GaussSnapshot(b,observer);
            // Construction and the pre-first-tick ready frame use the same initial pose as collision.
            if(s==null)return TerritoryRules.ArmedDefense(b.Kind)?new PlayableTurretSnapshot(0,Math.PI/2,0,false,0,ArmySystemsLevel(b.Owner,true)):null;
            int target=b.Ready&&b.Sale==null&&TurretLegal(b,s.Target)?s.Target:0;
            if(target!=0&&observer.HasValue&&Target(target,out var p,out _)&&!Vision(observer.Value).IsVisible(p))target=0;
            return new PlayableTurretSnapshot(target,s.Heading,s.Pitch,s.Turning,s.ShotSequence,ArmySystemsLevel(b.Owner,true));
        }
        private PlayableCommandStatus TurretOrder(PlayableCommand command,PlayableOwner owner,out string message)
        {
            message="Выберите собственную готовую турель";
            if(command.EntityIds.Count!=1||!buildings.TryGetValue(command.EntityIds[0],out var b)||!TerritoryRules.ArmedDefense(b.Kind)||b.Owner!=owner||!b.Ready||b.Sale!=null)return PlayableCommandStatus.InvalidEntity;
            if(command.Mode!=PlayableOrderMode.Replace){message="У турели нет очереди перемещения";return PlayableCommandStatus.Rejected;}
            if(command.Kind!=PlayableCommandKind.Attack){message="Турель неподвижна";return PlayableCommandStatus.Rejected;}
            RefreshVision();int target=command.TargetId;
            if(target==0)target=units.Values.Where(u=>TurretLegal(b,u.Id)&&Target(u.Id,out var p,out _)&&Distance(p,command.Target)<=profile.TargetPickRadiusMultiplier*PlayableUnitRules.Radius(profile,u.Kind)).OrderBy(u=>u.Id).Select(u=>u.Id).FirstOrDefault();
            if(!TurretLegal(b,target)){message="Нужна видимая вражеская цель в радиусе стрельбы";return PlayableCommandStatus.InvalidTarget;}
            b.Weapon=b.Weapon??new SentryTurretState();
            if(b.Weapon.Target!=target){InterruptTurretBurst(b.Weapon);b.Weapon.Target=target;}
            message="Цель турели назначена";return PlayableCommandStatus.Applied;
        }
        private void InterruptTurretBurst(SentryTurretState s)
        {
            if(s.Remaining>0)s.Reload=Math.Max(s.Reload,profile.TurretBurstPauseMs/1000d);
            s.Remaining=0;
        }
        private void AdvanceTurrets(double dt)
        {
            foreach(var b in buildings.Values.ToArray())
            {
                if(!buildings.ContainsKey(b.Id)||SystemsDisabled(b.Id)||!TerritoryRules.ArmedDefense(b.Kind)||!b.Ready||b.Health<=0||b.Sale!=null||eliminated.Contains(b.Owner))continue;
                if(b.Kind==PlayableBuildingKind.GaussTower&&!AdvanceGaussPending(b,dt))continue;
                var s=b.Weapon=b.Weapon??new SentryTurretState();s.Reload=Math.Max(0,s.Reload-dt);s.Delay=Math.Max(0,s.Delay-dt);
                if(!TurretLegal(b,s.Target))
                {
                    bool replacing=s.Target!=0;InterruptTurretBurst(s);s.Target=0;
                    int chosen=0;double nearest=double.PositiveInfinity;
                    foreach(var u in units.Values)Consider(u.Id);
                    foreach(var targetBuilding in buildings.Values)if(targetBuilding.Phase!=ConstructionPhase.Pending)Consider(targetBuilding.Id);
                    s.Target=chosen;
                    void Consider(int id)
                    {
                        if(!TurretLegal(b,id)||!Target(id,out var at,out _))return;
                        double d=Distance(b.Position,at);
                        if(chosen==0||replacing&&(d<nearest-1e-9||Math.Abs(d-nearest)<1e-9&&id<chosen)||!replacing&&id<chosen){chosen=id;nearest=d;}
                    }
                }
                if(s.Target==0)
                {
                    double yaw=s.Heading,elevation=s.Pitch;
                    s.Heading=Turn(s.Heading,Math.PI/2,profile.TurretYawSpeed*Math.PI/180*dt);
                    s.Pitch=Turn(s.Pitch,0,profile.TurretPitchSpeed*Math.PI/180*dt);
                    s.Turning=Math.Abs(Angle(s.Heading-yaw))>1e-9||Math.Abs(s.Pitch-elevation)>1e-9;
                    continue;
                }
                Target(s.Target,out var target,out _);double dflat=Distance(b.Position,target);
                double desired=dflat<1e-9?s.Heading:Math.Atan2(target.Z-b.Position.Z,target.X-b.Position.X);
                double pitch=Math.Max(-profile.TurretDepression*Math.PI/180,Math.Min(Math.PI/2,Math.Atan2(TargetHeight(s.Target,target)-GroundHeight(b.Position)-BuildingCombatGeometry.ApronLift-(b.Kind==PlayableBuildingKind.GaussTower?GaussTowerGeometry.PitchHeight:SentryTurretGeometry.PitchHeight)*profile.TurretModelScale,dflat)));
                double oldYaw=s.Heading,oldPitch=s.Pitch;
                s.Heading=Turn(s.Heading,desired,profile.TurretYawSpeed*Math.PI/180*dt);s.Pitch=Turn(s.Pitch,pitch,profile.TurretPitchSpeed*Math.PI/180*dt);
                s.Turning=Math.Abs(Angle(s.Heading-oldYaw))>1e-9||Math.Abs(s.Pitch-oldPitch)>1e-9;
                // Gauss cooldown lands on a discrete tick; discard only floating-point subtraction residue.
                if(s.Reload>(b.Kind==PlayableBuildingKind.GaussTower?1e-9:0)||s.Delay>0)continue;
                if(b.Kind==PlayableBuildingKind.GaussTower){if(Math.Abs(Angle(s.Heading-desired))<=profile.TankAimToleranceRadians&&Math.Abs(s.Pitch-pitch)<=profile.TankAimToleranceRadians)FireGauss(b,s);continue;}
                if(s.Remaining==0){s.BurstSize=s.Remaining=profile.TurretBurstSize;s.BurstSpread=profile.TurretSpreadDeg;s.BurstSequence++;}
                FireTurret(b,s);s.Remaining--;s.Delay=profile.TurretBurstShotIntervalMs/1000d;
                if(s.Remaining==0)s.Reload=profile.TurretBurstPauseMs/1000d;
            }
        }
        private void FireTurret(Building b,SentryTurretState s)
        {
            int side=s.ShotSequence%2;
            var start=SentryTurretGeometry.Muzzle(b.Position,GroundHeight(b.Position)+BuildingCombatGeometry.ApronLift,profile.TurretModelScale,s.Heading,s.Pitch,side);
            double spread=s.BurstSpread*(s.Turning?profile.TurretTurningSpread:1)*Math.PI/180;
            uint hash=unchecked((uint)(b.Id*73856093)^((uint)s.BurstSequence*19349663u)^((uint)s.ShotSequence*83492791u));hash^=hash>>16;hash*=0x7feb352du;hash^=hash>>15;
            double deviation=(hash/(double)uint.MaxValue*2-1)*spread;
            var forward=new BallisticPoint(Math.Cos(s.Heading)*Math.Cos(s.Pitch),Math.Sin(s.Pitch),Math.Sin(s.Heading)*Math.Cos(s.Pitch));
            var lateral=new BallisticPoint(Math.Sin(s.Heading),0,-Math.Cos(s.Heading));
            var direction=HawkRocketFlight.Add(HawkRocketFlight.Scale(forward,Math.Cos(deviation)),HawkRocketFlight.Scale(lateral,Math.Sin(deviation)));
            projectiles.Add(new Projectile{TermsRevision=profile.Revision,Id=nextProjectile++,Owner=b.Id,Target=s.Target,Faction=b.Owner,Kind=PlayableEntityKind.Explorer,
                Damage=WeaponDamage(b.Owner,profile.TurretDamage),Radius=0,Speed=profile.TurretProjectileSpeed,Position=start.Ground,Height=start.Y,Spatial=true,
                DirectionX=direction.X,DirectionY=direction.Y,DirectionZ=direction.Z,Remaining=Math.Max(0,TurretRange*(1+profile.ProjectileExtraRangePercent/100)-Distance(b.Position,start.Ground))});
            s.ShotSequence++;
            Sound(PlayableSoundKind.Shot,b.Position,PlayableEntityKind.Explorer,b.Owner,unitId:b.Id,altitude:start.Y,shotSide:side);
        }
    }
}
