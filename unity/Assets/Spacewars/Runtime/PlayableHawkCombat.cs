using System;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        // Authored model extent from design/hawk/model-manifest.json; not a balance knob.
        private double HawkBodyHeight=>1.8145018368959427*profile.HawkModelScale;
        private bool AirTarget(int id)=>units.TryGetValue(id,out var u)&&u.Air!=null;
        private double TargetHeight(int id,NavPoint point)=>units.TryGetValue(id,out var u)&&u.Air!=null?u.Air.Height+HawkBodyHeight/2:GroundHeight(point)+DirectFireHeight;
        private double ExplorerPitch(Unit u)
        {
            if(u.Kind!=PlayableEntityKind.Explorer||!TryActor(u.Id,out var self)||!Target(u.Target,out var target,out _))return 0;
            return Math.Atan2(TargetHeight(u.Target,target)-GroundHeight(self.Position)-DirectFireHeight,Distance(self.Position,target));
        }
        private int HawkTarget(Unit u)
        {
            int best=0;double distance=profile.HawkRange;
            foreach(var other in units.Values)
            {
                if(!Hostile(u.Owner,other.Owner)||!VisibleTarget(u.Owner,other.Id)||!TryActor(other.Id,out var n))continue;
                double d=Distance(u.Air.Actor.Position,n.Position);if(d<=distance&&(best==0||d<distance||other.Id<best)){best=other.Id;distance=d;}
            }
            foreach(var other in buildings.Values)
            {
                if(other.Phase==ConstructionPhase.Pending||!Hostile(u.Owner,other.Owner)||!VisibleTarget(u.Owner,other.Id))continue;
                double d=Distance(u.Air.Actor.Position,other.Position);if(d<distance||(best==0&&d<=distance)){best=other.Id;distance=d;}
            }
            return best;
        }
        private bool HawkLineClear(Unit u,NavPoint from,double height,int target)
        {
            return SpatialFireClear(u,from,height,target,profile.HawkMissileRadius);
        }
        private bool SpatialFireClear(Unit u,NavPoint from,double height,int target,double radius)
        {
            if(!Target(target,out var point,out _))return false;
            var a=new BallisticPoint(from.X,height,from.Z);var b=new BallisticPoint(point.X,TargetHeight(target,point),point.Z);
            return !AirContact(a,b,radius,u.Id,u.Owner,false,out int hit,out _,out _)||hit==target;
        }
        private bool HawkApproach(Unit u,int target,out NavPoint goal)
        {
            goal=default;if(!Target(target,out var point,out _))return false;double best=double.PositiveInfinity;
            foreach(var candidate in PlayableUnitRules.AttackApproachCandidates(profile,u.Air.Actor.Position,point,profile.HawkRange))
            {
                if(!AirInside(candidate)||!HawkLineClear(u,candidate,ClearHeight(candidate)+HawkBodyHeight/2,target))continue;
                double cost=Distance(u.Air.Actor.Position,candidate);if(cost>=best)continue;best=cost;goal=candidate;
            }
            return !double.IsPositiveInfinity(best);
        }
        private void AdvanceHawkCombat(Unit u,double dt)
        {
            var air=u.Air;var self=air.Actor;
            u.Reload=Math.Max(0,u.Reload-dt);u.BurstDelay=Math.Max(0,u.BurstDelay-dt);u.Repath=Math.Max(0,u.Repath-dt);
            bool horizontal=Math.Abs(u.Velocity.X)>1e-5||Math.Abs(u.Velocity.Z)>1e-5||self.Moving&&!air.Departing||air.Yielding;
            bool turning=Math.Abs(air.YawVelocity)>profile.HawkPreparationYawSpeed;
            u.StoppedSeconds=horizontal||turning?0:u.StoppedSeconds+dt;
            bool Valid(int id)=>VisibleTarget(u.Owner,id)&&Target(id,out var p,out _)&&Distance(self.Position,p)<=profile.HawkRange&&HawkLineClear(u,self.Position,air.Height+HawkBodyHeight/2,id);
            if(u.BurstRemaining>0&&(horizontal||turning||!Valid(u.BurstTarget)))InterruptBurst(u);
            if(u.Target!=0&&!VisibleTarget(u.Owner,u.Target))
            {if(u.BurstRemaining>0)InterruptBurst(u);u.Target=0;u.ExplicitTarget=false;if(u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack)u.CurrentOrder=null;}
            if(!u.ExplicitTarget&&(!self.Moving||air.Departing||u.HasAttackMove)&&u.BurstRemaining==0)u.Target=HawkTarget(u);
            if(!Target(u.Target,out var target,out _))
            {
                if(u.HasAttackMove&&!self.Held&&!self.Moving&&Distance(self.Position,u.AttackMove)>profile.Navigation.ArrivalTolerance){self.Goal=u.AttackMove;self.Moving=true;self.Outcome=NavigationOutcome.Moving;}
                return;
            }
            double distance=Distance(self.Position,target);bool clear=distance<=profile.HawkRange&&HawkLineClear(u,self.Position,air.Height+HawkBodyHeight/2,u.Target);
            if(!clear)
            {
                if(u.ExplicitTarget&&!self.Held&&u.Repath<=0&&!air.Departing&&HawkApproach(u,u.Target,out var approach))
                {self.Goal=approach;self.Moving=true;self.Outcome=NavigationOutcome.Moving;air.Short=false;u.Repath=profile.AttackRepathSeconds;}
                return;
            }
            if(!self.Held&&!air.Departing&&(u.ExplicitTarget||u.HasAttackMove)&&self.Moving)
            {self.Moving=false;self.Goal=self.Position;air.Short=false;return;}
            if(horizontal||turning||u.StoppedSeconds<HawkPreparation(u)||u.Reload>0)return;
            double desired=distance<1e-9?self.Heading:Math.Atan2(target.Z-self.Position.Z,target.X-self.Position.X);
            u.Turret=self.Heading;
            if(Math.Abs(Angle(desired-self.Heading))>profile.HawkAimTolerance)return;
            if(u.BurstRemaining>0)
            {
                if(u.BurstDelay>0)return;
                if(!Valid(u.BurstTarget)){InterruptBurst(u);return;}
                LaunchHawk(u,u.BurstTarget,1);u.BurstRemaining=0;u.BurstTarget=0;u.Reload=u.HawkBurstReload;
            }
            else
            {
                LaunchHawk(u,u.Target,0);u.BurstRemaining=1;u.BurstSize=2;u.BurstTarget=u.Target;u.BurstDelay=HawkSalvoDelay(u);u.HawkBurstReload=HawkReload(u);u.BurstSequence++;
            }
        }
        private void LaunchHawk(Unit u,int target,int side)
        {
            if(!Target(target,out var goal,out _))return;
            var n=u.Air.Actor;
            // Exact authored muzzleLeft/muzzleRight coordinates in the committed Unity prefab.
            double lateral=(side==0?.99:-.99)*profile.HawkModelScale,forward=.74*profile.HawkModelScale;
            var start=new BallisticPoint(n.Position.X+Math.Cos(n.Heading)*forward+Math.Sin(n.Heading)*lateral,u.Air.Height+.56*profile.HawkModelScale,n.Position.Z+Math.Sin(n.Heading)*forward-Math.Cos(n.Heading)*lateral);
            var end=new BallisticPoint(goal.X,TargetHeight(target,goal),goal.Z);var direction=HawkRocketFlight.Sub(end,start);var normalized=HawkRocketFlight.Normal(direction);
            double lifetime=(HawkRocketFlight.Length(direction)+profile.HawkRange*profile.ProjectileExtraRangePercent/100)/profile.HawkMissileSpeed;
            projectiles.Add(new Projectile{TermsRevision=profile.Revision,Id=nextProjectile++,Owner=u.Id,Target=target,Faction=u.Owner,Kind=PlayableEntityKind.Hawk,Damage=profile.HawkDamage*WeaponMultiplier(u.Owner),Radius=profile.HawkMissileRadius,Speed=profile.HawkMissileSpeed,Position=start.Ground,Height=start.Y,Spatial=true,DirectionX=normalized.X,DirectionY=normalized.Y,DirectionZ=normalized.Z,
                Hawk=new HawkRocketFlight(target,start,direction,profile.HawkMissileSpeed,profile.HawkMissileTurn,lifetime)});
            Sound(PlayableSoundKind.Shot,n.Position,u.Kind,u.Owner);
        }
        private void FireAirTracer(Unit u,NavPoint from,NavPoint target)
        {
            double height=GroundHeight(from)+DirectFireHeight;var start=new BallisticPoint(from.X,height,from.Z);var end=new BallisticPoint(target.X,TargetHeight(u.Target,target),target.Z);
            var velocity=units.TryGetValue(u.Target,out var victim)?victim.Velocity:default;double flight=HawkRocketFlight.Length(HawkRocketFlight.Sub(end,start))/profile.ExplorerProjectileSpeed;
            end=new BallisticPoint(end.X+velocity.X*flight,end.Y,end.Z+velocity.Z*flight);
            var direction=HawkRocketFlight.Normal(HawkRocketFlight.Sub(end,start));
            uint hash=unchecked((uint)(u.Id*73856093)^((uint)u.BurstSequence*19349663u)^((uint)(u.BurstSize-u.BurstRemaining)*83492791u));hash^=hash>>16;hash*=0x7feb352du;hash^=hash>>15;
            double spread=(hash/(double)uint.MaxValue*2-1)*u.BurstSpread*Math.PI/180;
            // Rotate spread in a stable perpendicular plane, including exactly vertical fire.
            var axis=Math.Abs(direction.Y)<.9?new BallisticPoint(0,1,0):new BallisticPoint(1,0,0);
            var tangent=HawkRocketFlight.Normal(HawkRocketFlight.Sub(axis,HawkRocketFlight.Scale(direction,HawkRocketFlight.Dot(axis,direction))));
            direction=HawkRocketFlight.Add(HawkRocketFlight.Scale(direction,Math.Cos(spread)),HawkRocketFlight.Scale(tangent,Math.Sin(spread)));
            double offset=profile.ExplorerMuzzleOffset;start=HawkRocketFlight.Add(start,HawkRocketFlight.Scale(direction,offset));
            projectiles.Add(new Projectile{TermsRevision=profile.Revision,Id=nextProjectile++,Owner=u.Id,Target=u.Target,Faction=u.Owner,Kind=u.Kind,Damage=profile.ExplorerDamage*WeaponMultiplier(u.Owner),Radius=0,Speed=profile.ExplorerProjectileSpeed,Position=start.Ground,Height=start.Y,Spatial=true,DirectionX=direction.X,DirectionY=direction.Y,DirectionZ=direction.Z,
                Remaining=HawkRocketFlight.Length(HawkRocketFlight.Sub(end,start))+profile.ExplorerRange*profile.ProjectileExtraRangePercent/100});
            Sound(PlayableSoundKind.Shot,from,u.Kind,u.Owner);
        }
        private bool AdvanceAirProjectile(Projectile p,double dt)
        {
            var from=new BallisticPoint(p.Position.X,p.Height,p.Position.Z);var before=p.Hawk;BallisticPoint to;double move=0;
            BallisticPoint oldDirection=default;double oldLife=0;
            if(before!=null)
            {
                oldDirection=before.Direction;oldLife=before.RemainingLife;
                BallisticPoint? target=Target(p.Target,out var point,out _)?new BallisticPoint(point.X,TargetHeight(p.Target,point),point.Z):(BallisticPoint?)null;
                before.Advance(dt,target);to=before.Position;
            }
            else{move=Math.Min(p.Speed*dt,p.Remaining);to=new BallisticPoint(from.X+p.DirectionX*move,from.Y+p.DirectionY*move,from.Z+p.DirectionZ*move);}
            if(AirContact(from,to,p.Radius,p.Owner,null,true,out int hit,out var contact,out _))
            {
                bool hostile=hit!=0&&(units.TryGetValue(hit,out var unit)?Hostile(unit.Owner,p.Faction):buildings.TryGetValue(hit,out var building)&&Hostile(building.Owner,p.Faction));
                if(hostile&&!TryDamageWithOwner(hit,p.Damage,p.Owner,p.Faction))
                {if(before!=null)p.Hawk=HawkRocketFlight.Restore(p.Target,from,oldDirection,before.Speed,before.TurnSpeed,oldLife);return false;}
                var fact=new PlayableDirectContact(p.Id,hit,from,contact);
                PublishSound(new PlayableSoundEvent(++nextSoundEvent,Tick,PlayableSoundKind.Impact,contact.Ground,p.Kind,buildings.ContainsKey(hit),contact:fact),null);return true;
            }
            p.Position=to.Ground;p.Height=to.Y;
            if(before!=null){p.DirectionX=before.Direction.X;p.DirectionY=before.Direction.Y;p.DirectionZ=before.Direction.Z;return before.RemainingLife<=0;}
            p.Remaining-=move;return p.Remaining<=0;
        }
        // Earliest contact wins: a wall behind a unit cannot consume its hit. Motion is
        // relative to the same authority step, never a renderer collider or target teleport.
        private bool AirContact(BallisticPoint from,BallisticPoint to,double radius,int source,PlayableOwner? observer,bool moving,out int hit,out BallisticPoint point,out double fraction)
        {
            hit=0;fraction=double.PositiveInfinity;point=to;
            bool TerrainClear(BallisticPoint end)=>PlayableBallistics.TerrainLineClear(from,end,radius,SolidObstacles(),profile.BallisticWallHeight,AirSurface);
            if(!TerrainClear(to))
            {
                double low=0,high=1;
                // Fixed bisection iterations are a numerical bound, not designer tuning.
                for(int i=0;i<40;i++){double mid=(low+high)/2;var at=LerpAir(from,to,mid);if(TerrainClear(at))low=mid;else high=mid;}
                fraction=high;
            }
            foreach(var u in units.Values)
            {
                if(u.Id==source||!TryActor(u.Id,out var n)||observer.HasValue&&u.Owner!=observer&&!Vision(observer.Value).IsVisible(n.Position))continue;
                double bodyHeight=u.Air!=null?HawkBodyHeight:2*PlayableUnitRules.Radius(profile,u.Kind);
                double y=u.Air?.Height??GroundHeight(n.Position),oldY=moving?(u.Air?.PreviousHeight??GroundHeight(u.PreviousPosition)):y;
                var previous=moving?u.PreviousPosition:n.Position;double r=PlayableUnitRules.Radius(profile,u.Kind)+radius,vertical=bodyHeight/2+radius,scale=r/vertical;
                var a=new BallisticPoint(from.X,from.Y*scale,from.Z);var b=new BallisticPoint(to.X,to.Y*scale,to.Z);
                var c=new BallisticPoint(previous.X,(oldY+bodyHeight/2)*scale,previous.Z);var d=new BallisticPoint(n.Position.X,(y+bodyHeight/2)*scale,n.Position.Z);
                if(HawkRocketFlight.Contact(a,b,c,d,r,out double t)&&t<fraction){fraction=t;hit=u.Id;}
            }
            foreach(var b in buildings.Values)
            {
                if(b.Id==source||b.Phase==ConstructionPhase.Pending||observer.HasValue&&b.Owner!=observer&&!Vision(observer.Value).IsVisible(b.Position))continue;
                double height=BuildingHeightForAir(b.Kind);
                double t=AirCylinderContact(from,to,b.Position,BuildingRadius(b.Kind)+radius,GroundHeight(b.Position)-radius,GroundHeight(b.Position)+height+radius);
                if(t<fraction){fraction=t;hit=b.Id;}
            }
            if(double.IsPositiveInfinity(fraction))return false;point=LerpAir(from,to,fraction);return true;
        }
        private static BallisticPoint LerpAir(BallisticPoint a,BallisticPoint b,double t)=>new BallisticPoint(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t,a.Z+(b.Z-a.Z)*t);
        private static double AirCylinderContact(BallisticPoint a,BallisticPoint b,NavPoint center,double radius,double bottom,double top)
        {
            double dx=b.X-a.X,dz=b.Z-a.Z,x=a.X-center.X,z=a.Z-center.Z,aa=dx*dx+dz*dz,bb=2*(x*dx+z*dz),cc=x*x+z*z-radius*radius,enter=0,exit=1;
            if(aa<1e-18){if(cc>0)return double.PositiveInfinity;}
            else{double discriminant=bb*bb-4*aa*cc;if(discriminant<0)return double.PositiveInfinity;enter=Math.Max(0,(-bb-Math.Sqrt(discriminant))/(2*aa));exit=Math.Min(1,(-bb+Math.Sqrt(discriminant))/(2*aa));}
            double dy=b.Y-a.Y;if(Math.Abs(dy)<1e-18){if(a.Y<bottom||a.Y>top)return double.PositiveInfinity;}
            else{double t1=(bottom-a.Y)/dy,t2=(top-a.Y)/dy;enter=Math.Max(enter,Math.Min(t1,t2));exit=Math.Min(exit,Math.Max(t1,t2));}
            return enter<=exit?enter:double.PositiveInfinity;
        }
    }
}
