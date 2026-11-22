using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private void FireTracer(Unit u,NavPoint from,NavPoint target)
        {
            if(AirTarget(u.Target)){FireAirTracer(u,from,target);return;}
            var velocity=units.TryGetValue(u.Target,out var victim)?victim.Velocity:default(NavPoint);
            double dx=target.X-from.X,dz=target.Z-from.Z,speed=profile.ExplorerProjectileSpeed;
            double a=velocity.X*velocity.X+velocity.Z*velocity.Z-speed*speed,b=2*(dx*velocity.X+dz*velocity.Z),c=dx*dx+dz*dz,t=0;
            if(Math.Abs(a)<1e-9){if(Math.Abs(b)>1e-9)t=-c/b;}
            else {double disc=b*b-4*a*c;if(disc>=0){double one=(-b-Math.Sqrt(disc))/(2*a),two=(-b+Math.Sqrt(disc))/(2*a);t=one>0?one:two;if(two>0&&two<t)t=two;}}
            if(t<0||double.IsNaN(t)||double.IsInfinity(t))t=0;
            // Addressed integer hash: deterministic across runs, independent of rendering or collection iteration.
            int burst=u.BurstSize;double spreadDegrees=u.BurstSpread;
            uint hash=unchecked((uint)(u.Id*73856093)^((uint)u.BurstSequence*19349663u)^((uint)(burst-u.BurstRemaining)*83492791u));
            hash^=hash>>16;hash*=0x7feb352du;hash^=hash>>15;
            double spread=(hash/(double)uint.MaxValue*2-1)*spreadDegrees*Math.PI/180;
            double angle=Math.Atan2(dz+velocity.Z*t,dx+velocity.X*t)+spread;
            double x=Math.Cos(angle),z=Math.Sin(angle),offset=profile.ExplorerMuzzleOffset;
            var predicted=new NavPoint(target.X+velocity.X*t,target.Z+velocity.Z*t);double slope=(GroundHeight(predicted)-GroundHeight(from))/Math.Max(1e-9,Distance(from,predicted));
            double length=Distance(from,predicted);
            double startHeight=GroundHeight(from)+DirectFireHeight;
            double travel=profile.ExplorerRange*(1+profile.ProjectileExtraRangePercent/100);
            double end=Math.Min(length,travel);
            var muzzle=new NavPoint(from.X+x*offset,from.Z+z*offset);
            var aimEnd=new NavPoint(from.X+x*end,from.Z+z*end);
            double muzzleHeight=startHeight+slope*offset,aimHeight=startHeight+slope*end;
            if(end>offset&&(!ProjectileClear(muzzle,aimEnd,muzzleHeight,aimHeight,0)||
                !GroundShotBodyClear(u,muzzle,aimEnd,muzzleHeight,aimHeight,0,u.Target)))return;

            RememberDefaultShot(u);Sound(PlayableSoundKind.Shot,from,u.Kind,u.Owner,unitId:u.Id);
            // Web burst projectiles are physical points (radius 0), not tracer-thickness collision capsules.
            projectiles.Add(new Projectile{TermsRevision=profile.Revision,Id=nextProjectile++,Owner=u.Id,Faction=u.Owner,Target=u.Target,Kind=u.Kind,
                DirectLaunchTick=Tick,DirectBarrel=(burst-u.BurstRemaining)%2,
                Position=muzzle,Height=muzzleHeight,VerticalSlope=slope,DirectionX=x,DirectionZ=z,Radius=0,Speed=speed,Damage=WeaponDamage(u.Owner,profile.ExplorerDamage),
                Remaining=Math.Max(0,travel-offset)});
        }
    }
}
