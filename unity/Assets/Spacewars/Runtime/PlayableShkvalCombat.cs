using System;
using System.Linq;
using System.Collections.Generic;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private sealed class Rocket
        {
            public BallisticFlight Flight;public BallisticContact Predicted;
            public double Elapsed,Radius,BlastRadius,MarkerStartRadius,MarkerOpacity,BuildingHeight;
        }
        private readonly List<PlayableImpactSnapshot> impacts=new List<PlayableImpactSnapshot>();
        private BallisticBody[] BallisticBodies(PlayableOwner? observer=null,double buildingHeight=0)
        {
            var result=new List<BallisticBody>();
            foreach(var u in units.Values)if(navigation.Crowd.TryGet(u.Id,out var n)&&(observer==null||u.Owner==observer||Vision(observer.Value).IsVisible(n.Position)))
                result.Add(new BallisticBody(u.Id,u.Owner,n.Position,u.Velocity,PlayableUnitRules.Radius(profile,u.Kind),2*PlayableUnitRules.Radius(profile,u.Kind),u.Health,false,GroundHeight(n.Position)));
            foreach(var b in buildings.Values)if(b.Phase!=ConstructionPhase.Pending&&(observer==null||b.Owner==observer||Vision(observer.Value).IsVisible(b.Position)))
                result.Add(new BallisticBody(b.Id,b.Owner,b.Position,default(NavPoint),BuildingRadius(b.Kind),buildingHeight>0?buildingHeight:profile.ShkvalBuildingCollisionHeight,b.Health,true,GroundHeight(b.Position)));
            return result.ToArray();
        }
        private BallisticFlight BaselineFlight(NavPoint from,NavPoint target)=>new BallisticFlight(new BallisticPoint(from.X,GroundHeight(from)+profile.ShkvalLaunchHeight,from.Z),new BallisticPoint(target.X,GroundHeight(target),target.Z),profile.ShkvalArcHeight,profile.ShkvalProjectileSpeed);
        private BallisticFlight GuidedFlight(Unit u,NavPoint from,NavPoint target)
        {
            var velocity=units.TryGetValue(u.Target,out var victim)?victim.Velocity:default(NavPoint);
            return PlayableBallistics.Aim(from,target,velocity,profile.ShkvalLaunchHeight,profile.ShkvalArcHeight,profile.ShkvalProjectileSpeed,true,profile.ShkvalLeadSpeedThreshold,profile.ShkvalLeadFalloff,profile.ShkvalMaxLeadTimeSec,TerrainHeight);
        }
        private int ArtilleryTarget(Unit u,NavPoint from)=>PlayableBallistics.BestTarget(from,u.Owner,PlayableUnitRules.Range(profile,u.Kind,UnitUpgraded(u)),profile.ShkvalLaunchHeight,profile.ShkvalArcHeight,profile.ShkvalProjectileSpeed,profile.ShkvalProjectileRadius,profile.ShkvalBlastRadius,profile.ShkvalDamage,profile.ShkvalFriendlyFirePenalty,SolidObstacles(),profile.BallisticWallHeight,BallisticBodies(u.Owner),TerrainHeight,UnitUpgraded(u),profile.ShkvalLeadSpeedThreshold,profile.ShkvalLeadFalloff,profile.ShkvalMaxLeadTimeSec,Hostile);
        private void AdvanceShkval(Unit u,NavUnit self,double dt)
        {
            u.Reload=Math.Max(0,u.Reload-dt);u.Repath=Math.Max(0,u.Repath-dt);
            bool moved=Math.Abs(u.Velocity.X)>1e-5||Math.Abs(u.Velocity.Z)>1e-5;
            u.StoppedSeconds=moved?0:u.StoppedSeconds+dt;
            if(u.Target!=0&&!VisibleTarget(u.Owner,u.Target)){if(u.ExplicitTarget&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack)u.CurrentOrder=null;u.Target=0;u.ExplicitTarget=false;if(u.HasAttackMove&&!self.Moving&&!navigation.IsPending(u.Id))navigation.Move(u.Id,u.AttackMove);}
            if(IsFollowing(u)&&!FollowCanFire(u)){ClearFollowBurst(u);u.Target=0;return;}
            // Ordinary movement keeps its spatial order. Attack-move stops for a useful visible shot.
            if(!u.ExplicitTarget)u.Target=(!self.Moving&&!navigation.IsPending(u.Id))||u.HasAttackMove?ArtilleryTarget(u,self.Position):0;
            if(!Target(u.Target,out var target,out _)){if(u.HasAttackMove&&!self.Moving&&!navigation.IsPending(u.Id)&&Distance(self.Position,u.AttackMove)>profile.Navigation.ArrivalTolerance)navigation.Move(u.Id,u.AttackMove);return;}
            double distance=Distance(self.Position,target);
            if(distance>PlayableUnitRules.Range(profile,u.Kind,UnitUpgraded(u)))
            {
                if(u.ExplicitTarget&&u.Repath<=0&&!self.Moving&&!navigation.IsPending(u.Id))
                {if(TryAttackApproach(u.Id,self.Position,target,out var approach))navigation.Move(u.Id,approach);u.Repath=profile.AttackRepathSeconds;}
                return;
            }
            if(!self.Held&&(self.Moving||navigation.IsPending(u.Id)))navigation.Stop(u.Id,false);
            double desired=Math.Atan2(target.Z-self.Position.Z,target.X-self.Position.X);
            u.Turret=Turn(u.Turret,desired,profile.ShkvalTurretTurnSpeed*dt);
            if(moved||u.StoppedSeconds<profile.ShkvalStopForMs/1000||u.Reload>0||Math.Abs(Angle(desired-u.Turret))>profile.ShkvalAimToleranceRad)return;
            var flight=UnitUpgraded(u)?GuidedFlight(u,self.Position,target):BaselineFlight(self.Position,target);
            // Launch prediction is observation-bound. Physical contacts below use full current state.
            var prediction=PlayableBallistics.FirstContact(flight,profile.ShkvalProjectileRadius,SolidObstacles(),profile.BallisticWallHeight,BallisticBodies(u.Owner),u.Id,terrain:TerrainHeight).Value;
            projectiles.Add(new Projectile{TermsRevision=profile.Revision,Id=nextProjectile++,Owner=u.Id,Target=u.Target,Faction=u.Owner,Kind=PlayableEntityKind.Shkval,Position=self.Position,Damage=profile.ShkvalDamage,
                Rocket=new Rocket{Flight=flight,Predicted=prediction,Radius=profile.ShkvalProjectileRadius,BlastRadius=profile.ShkvalBlastRadius,MarkerStartRadius=profile.ShkvalMarkerStartRadius,MarkerOpacity=profile.ShkvalMarkerOpacity,BuildingHeight=profile.ShkvalBuildingCollisionHeight}});
            u.Reload=profile.ShkvalFireIntervalMs/1000;
        }
        private bool AdvanceRocket(Projectile p,double dt)
        {
            var r=p.Rocket;double from=r.Elapsed/r.Flight.Duration;r.Elapsed=Math.Min(r.Flight.Duration,r.Elapsed+dt);double to=r.Elapsed/r.Flight.Duration;
            var bodies=BallisticBodies(null,r.BuildingHeight);var solids=SolidObstacles();
            var hit=PlayableBallistics.FirstContact(r.Flight,r.Radius,solids,profile.BallisticWallHeight,bodies,p.Owner,from,to,TerrainHeight);
            p.Position=r.Flight.Point(to).Ground;
            if(hit==null)return false;
            // Snapshot every hit before mutations so a destroyed cover still shields this explosion.
            var victims=bodies.Where(b=>PlayableBallistics.BlastHits(hit.Value.Point,r.BlastRadius,b,solids,profile.BallisticWallHeight,bodies,TerrainHeight)).Select(b=>b.Id).ToArray();
            int visibleMask=0;foreach(PlayableOwner owner in Owners)if(Vision(owner).IsVisible(hit.Value.Point.Ground))visibleMask|=1<<(int)owner;
            impacts.Add(new PlayableImpactSnapshot(p.Id,p.Faction,hit.Value.Point,r.BlastRadius,Tick,visibleMask));
            foreach(int id in victims)DamageFromProjectile(id,p.Damage,p.Owner);
            return true;
        }
    }
}
