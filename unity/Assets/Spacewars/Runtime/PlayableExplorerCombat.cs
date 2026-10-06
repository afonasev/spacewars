using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private void FireTracer(Unit u,NavPoint from,NavPoint target)
        {
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
            // Web burst projectiles are physical points (radius 0), not tracer-thickness collision capsules.
            projectiles.Add(new Projectile{Id=nextProjectile++,Owner=u.Id,Faction=u.Owner,Target=u.Target,Kind=u.Kind,
                Position=new NavPoint(from.X+x*offset,from.Z+z*offset),Height=GroundHeight(from)+DirectFireHeight+slope*offset,VerticalSlope=slope,DirectionX=x,DirectionZ=z,Radius=0,Speed=speed,Damage=profile.ExplorerDamage,
                Remaining=Math.Max(0,profile.ExplorerRange*(1+profile.ProjectileExtraRangePercent/100)-offset)});
        }
    }
}
