using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        // Aircraft never enter the ground crowd or its route/footprint authority.
        private sealed class AirState
        {
            public NavUnit Actor;
            public double Height, PreviousHeight, Pitch, Roll, YawVelocity, ShortRemaining;
            public bool Short, Escape, Departing;
            public int FacingTarget;
            public NavPoint Velocity,YieldGoal; public bool Yielding;
            public readonly Queue<PlayableCommand> Deferred=new Queue<PlayableCommand>();
        }
        private bool TryActor(int id,out NavUnit actor)
        {
            if(units.TryGetValue(id,out var u)&&u.Air!=null){actor=u.Air.Actor;return true;}
            return navigation.Crowd.TryGet(id,out actor);
        }
        private void InitializeHawk(Unit unit,NavPoint p)
        {
            unit.Air=new AirState{Height=ClearHeight(p),PreviousHeight=ClearHeight(p),Actor=new NavUnit(unit.Id,p,profile.HawkRadius,profile.HawkSpeed,profile.HawkYawSpeed){Team=TeamOf(unit.Owner)}};
        }
        private static double ApproachAir(double value,double target,double delta)=>value+Math.Max(-delta,Math.Min(delta,target-value));
        private bool AirInside(NavPoint p)=>!double.IsNaN(p.X)&&!double.IsNaN(p.Z)&&!double.IsInfinity(p.X)&&!double.IsInfinity(p.Z)&&Math.Abs(p.X)<=profile.ArenaHalfExtent-profile.HawkRadius&&Math.Abs(p.Z)<=profile.ArenaHalfExtent-profile.HawkRadius;
        private double AirSurface(NavPoint p)=>profile.AuthoredMap?.SupportAt(p)?.HeightAt(p)??0;
        private double ClearHeight(NavPoint p)
        {
            double h=AirSurface(p);
            foreach(var solid in SolidObstacles())if(solid.DistanceSquared(p)<=profile.HawkRadius*profile.HawkRadius)h=Math.Max(h,double.IsNaN(solid.Top)?AirSurface(p)+profile.BallisticWallHeight:solid.Top);
            foreach(var b in buildings.Values)if(b.Phase!=ConstructionPhase.Pending&&Distance(b.Position,p)<BuildingRadius(b.Kind)+profile.HawkRadius)
                h=Math.Max(h,AirSurface(b.Position)+BuildingHeightForAir(b.Kind));
            return h+profile.HawkClearance;
        }
        private double BuildingHeightForAir(PlayableBuildingKind k)
        {
            if(k==PlayableBuildingKind.Hangar)return profile.HangarModelHeightMeters*profile.HangarModelScale;
            double visual=k==PlayableBuildingKind.Headquarters?profile.HeadquartersModelHeightMeters*profile.HeadquartersModelScale:k==PlayableBuildingKind.Factory?profile.FactoryModelHeightMeters*profile.FactoryModelScale:k==PlayableBuildingKind.Refinery?profile.RefineryModelHeightMeters*profile.RefineryModelScale:k==PlayableBuildingKind.Outpost?profile.OutpostModelHeightMeters*profile.OutpostModelScale:k==PlayableBuildingKind.Mine?profile.MineModelHeightMeters*profile.MineModelScale:profile.ScienceModelHeightMeters*profile.ScienceModelScale;
            return Math.Max(profile.ShkvalBuildingCollisionHeight,visual);
        }
        private PlayableCommandStatus ApplyAirCommand(PlayableCommand command,out string message,bool recordHuman)
        {
            message="Invalid aircraft order.";var ids=command.CopyEntityIds();var owner=OwnerFor(command.PlayerId);
            var invalid=MoveRecipientFailure(ids,owner);if(invalid.HasValue)return invalid.Value;
            bool spatial=command.Kind==PlayableCommandKind.Move||command.Kind==PlayableCommandKind.AttackMove;
            if(spatial&&!AirInside(command.Target))return PlayableCommandStatus.InvalidTarget;
            if(command.Kind==PlayableCommandKind.Attack&&!VisibleTarget(owner,command.TargetId))return PlayableCommandStatus.InvalidTarget;
            if(command.Kind==PlayableCommandKind.Follow&&(!units.TryGetValue(command.TargetId,out var leader)||Hostile(leader.Owner,owner)||ids.Contains(command.TargetId)))return PlayableCommandStatus.InvalidTarget;
            int[] ground=ids.Where(id=>units[id].Air==null).ToArray();
            var aircraft=ids.Where(id=>units[id].Air!=null).OrderBy(id=>id).ToArray();
            var destinations=new List<NavPoint>();
            if(spatial)
            {
                foreach(int id in aircraft)
                {
                    bool found=false;
                    // Search deterministic rings for separate group endpoints. Bound follows map diameter.
                    double spacing=2*profile.HawkRadius+profile.HawkSeparation;
                    for(int ring=0;ring<=Math.Ceiling(2*profile.ArenaHalfExtent/spacing)&&!found;ring++)
                    for(int slot=0;slot<(ring==0?1:8*ring);slot++)
                    {
                        double angle=slot*Math.PI/(4*Math.Max(1,ring));
                        var p=new NavPoint(command.Target.X+ring*spacing*Math.Cos(angle),command.Target.Z+ring*spacing*Math.Sin(angle));
                        if(!AirInside(p)||destinations.Any(x=>Distance(x,p)<spacing)||units.Values.Any(other=>other.Air!=null&&!aircraft.Contains(other.Id)&&Distance(other.Air.Actor.Position,p)<spacing))continue;
                        destinations.Add(p);found=true;break;
                    }
                    if(!found)return PlayableCommandStatus.InvalidTarget;
                }
            }
            if(command.Mode==PlayableOrderMode.Append&&aircraft.Any(id=>(units[id].Air.Actor.Moving||units[id].CurrentOrder!=null)&&units[id].Air.Deferred.Count>=profile.UnitOrderQueueLimit))return PlayableCommandStatus.Overflow;
            if(ground.Length>0)
            {
                // Validate/admit all ground effects first; no air mutation occurs on a refused ground transaction.
                var groundCommand=RecipientCommand(command,ground);
                if(spatial){if(!navigation.Crowd.Locate(command.Target,0,out var location)||command.TargetLocation.HasValue&&!command.TargetLocation.Value.Equals(location))return PlayableCommandStatus.InvalidTarget;groundCommand=groundCommand.WithResolvedLocation(location);}
                PlayableCommandStatus status;
                if(command.Mode==PlayableOrderMode.Append)status=AppendTactical(groundCommand,out message);
                else status=ApplyTacticalPrepared(groundCommand,out message,out _);
                if(status!=PlayableCommandStatus.Applied&&status!=PlayableCommandStatus.Accepted)return status;
            }
            for(int i=0;i<aircraft.Length;i++)
            {
                var u=units[aircraft[i]];var air=u.Air;
                if(command.Mode==PlayableOrderMode.Append&&(air.Actor.Moving||u.CurrentOrder!=null)){air.Deferred.Enqueue(new PlayableCommand(command.Generation,command.Sequence,command.PlayerId,command.Kind,new[]{u.Id},spatial?destinations[i]:command.Target,targetId:command.TargetId,origin:command.Origin,source:command.Source,jobId:command.JobId,actionId:command.ActionId));continue;}
                air.Deferred.Clear();SetAirOrder(u,command,spatial?destinations[i]:command.Target);
            }
            if(recordHuman&&command.Origin==PlayableOrderOrigin.Human)RecordHumanAction(command.PlayerId);
            message="Aircraft order accepted.";return PlayableCommandStatus.Applied;
        }
        private void SetAirOrder(Unit u,PlayableCommand command,NavPoint goal)
        {
            var a=u.Air;var n=a.Actor;bool previousShort=a.Short&&n.Moving;bool stationary=!n.Moving&&Distance(default,a.Velocity)<1e-5;
            if(u.BurstRemaining>0)u.Reload=profile.HawkReloadSeconds;u.BurstRemaining=0;u.BurstTarget=0;u.StoppedSeconds=0;
            a.Short=false;a.Escape=false;a.FacingTarget=0;n.Held=false;
            u.LastOrder=new PlayableOrderStamp(u.Id,u.Owner,navigation.Generation,nextOrderRevision++,command.Sequence,Tick,command.Origin,command.Kind,command.Source,command.JobId,command.ActionId);
            if(command.Kind==PlayableCommandKind.Stop||command.Kind==PlayableCommandKind.Hold)
            {n.Goal=n.Position;n.Moving=false;n.Held=command.Kind==PlayableCommandKind.Hold;n.Outcome=n.Held?NavigationOutcome.Held:NavigationOutcome.Stopped;u.CurrentOrder=null;u.Target=0;u.ExplicitTarget=false;return;}
            if(command.Kind==PlayableCommandKind.Attack||command.Kind==PlayableCommandKind.Follow)
            {u.Target=command.Kind==PlayableCommandKind.Attack?command.TargetId:0;u.ExplicitTarget=u.Target!=0;u.CurrentOrder=new PlayableTacticalOrderSnapshot(u.Id,u.Owner,navigation.Generation,command.Sequence,Tick,command.Kind==PlayableCommandKind.Attack?PlayableTacticalOrderKind.Attack:PlayableTacticalOrderKind.Follow,goal,command.TargetId);n.Moving=false;return;}
            double distance=Distance(n.Position,goal);double angle=Math.Atan2(goal.Z-n.Position.Z,goal.X-n.Position.X);bool sideways=Math.Abs(Angle(angle-n.Heading))>Math.PI/4;
            if(stationary&&!previousShort&&sideways)
            {
                a.Short=distance<=profile.HawkShortDistance;
                if(!a.Short&&u.Target!=0&&VisibleTarget(u.Owner,u.Target)){a.Short=true;a.Escape=true;a.FacingTarget=u.Target;}
            }
            a.ShortRemaining=profile.HawkShortSeconds;n.Goal=goal;n.Moving=true;n.Outcome=NavigationOutcome.Moving;
            u.HasAttackMove=command.Kind==PlayableCommandKind.AttackMove;u.AttackMove=goal;u.ExplicitTarget=false;
            u.CurrentOrder=new PlayableTacticalOrderSnapshot(u.Id,u.Owner,navigation.Generation,command.Sequence,Tick,u.HasAttackMove?PlayableTacticalOrderKind.AttackMove:PlayableTacticalOrderKind.Move,goal,0);
        }
        private void AdvanceHawks(double dt)
        {
            var all=units.Values.Where(u=>u.Air!=null).OrderBy(u=>u.Id).ToArray();
            foreach(var u in all)
            {
                var a=u.Air;var n=a.Actor;a.PreviousHeight=a.Height;
                if(a.Departing)
                {
                    double safe=ClearHeight(n.Position);a.Height=ApproachAir(a.Height,safe,profile.HawkClimbSpeed*dt);
                    if(a.Height>=safe-1e-7)a.Departing=false;
                    else{double aim=n.Heading;if(Target(u.Target,out var liftTarget,out _)&&VisibleTarget(u.Owner,u.Target)&&Distance(n.Position,liftTarget)>1e-9)aim=Math.Atan2(liftTarget.Z-n.Position.Z,liftTarget.X-n.Position.X);AdvanceAirYaw(a,aim,dt);continue;}
                }
                if(u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow&&TryActor(u.CurrentOrder.TargetId,out var leader))
                {n.Goal=PlayableUnitRules.FollowGoal(profile,leader.Position,leader.Heading);n.Moving=Distance(n.Position,n.Goal)>profile.FollowArrivalTolerance;}
                bool yielding=a.Yielding;var savedGoal=n.Goal;bool savedMoving=n.Moving;
                if(yielding){n.Goal=a.YieldGoal;n.Moving=true;}
                double distance=Distance(n.Position,n.Goal);double desired=Math.Atan2(n.Goal.Z-n.Position.Z,n.Goal.X-n.Position.X);
                if(a.Short&&a.Escape&&(a.ShortRemaining<=0||!VisibleTarget(u.Owner,a.FacingTarget))){a.Short=false;a.Escape=false;}
                double facing=desired;if(a.Short&&a.Escape&&Target(a.FacingTarget,out var enemy,out _))facing=Math.Atan2(enemy.Z-n.Position.Z,enemy.X-n.Position.X);
                bool aimExists=Target(u.Target,out var aimPoint,out _);bool idleAim=!n.Moving&&aimExists&&VisibleTarget(u.Owner,u.Target);if(idleAim&&Distance(n.Position,aimPoint)>1e-9)facing=Math.Atan2(aimPoint.Z-n.Position.Z,aimPoint.X-n.Position.X);else if(idleAim)facing=n.Heading;
                double yawTarget=(n.Moving&&(!a.Short||a.Escape)||idleAim)?Math.Max(-profile.HawkYawSpeed,Math.Min(profile.HawkYawSpeed,Angle(facing-n.Heading)/dt)):0;
                a.YawVelocity=ApproachAir(a.YawVelocity,yawTarget,profile.HawkYawAcceleration*dt);n.Heading=Angle(n.Heading+a.YawVelocity*dt);
                double forward=0,right=0;
                if(n.Moving&&distance>profile.Navigation.ArrivalTolerance)
                {
                    double relative=Angle(desired-n.Heading);
                    if(a.Short)
                    {
                        a.ShortRemaining=Math.Max(0,a.ShortRemaining-dt);
                        forward=Math.Cos(relative);right=Math.Sin(relative);
                        // Finite short orders are bounded by HawkShortDistance and finish at their goal.
                        // Only a long target-facing escape uses the timed turn budget.
                    }
                    else forward=Math.Max(0,Math.Cos(relative));
                }
                else n.Moving=false;
                double desiredSpeed=Math.Min(profile.HawkSpeed,Math.Sqrt(2*profile.HawkBraking*Math.Max(0,distance-profile.Navigation.ArrivalTolerance)));
                double vf=forward*(forward<0?profile.HawkReverseSpeed:desiredSpeed),vr=right*Math.Min(profile.HawkStrafeSpeed,desiredSpeed);
                var wanted=new NavPoint(vf*Math.Cos(n.Heading)-vr*Math.Sin(n.Heading),vf*Math.Sin(n.Heading)+vr*Math.Cos(n.Heading));
                double speed=Distance(default,a.Velocity);double acceleration=Distance(default,wanted)<speed?profile.HawkBraking:profile.HawkAcceleration;
                var delta=new NavPoint(wanted.X-a.Velocity.X,wanted.Z-a.Velocity.Z);double change=Distance(default,delta);double fraction=change>acceleration*dt?acceleration*dt/change:1;
                var velocity=new NavPoint(a.Velocity.X+delta.X*fraction,a.Velocity.Z+delta.Z*fraction);
                var next=new NavPoint(n.Position.X+velocity.X*dt,n.Position.Z+velocity.Z*dt);
                double required=ClearHeight(n.Position),nextClear=ClearHeight(next);
                if(n.Moving&&distance>1e-9)
                {
                    double look=Math.Min(distance,profile.HawkLookahead);
                    // Sampling at at most a footprint radius plus exact solid sweep below prevents thin-wall misses.
                    int steps=Math.Max(1,(int)Math.Ceiling(look/profile.HawkRadius));
                    for(int k=1;k<=steps;k++){double t=look*k/steps/distance;required=Math.Max(required,ClearHeight(new NavPoint(n.Position.X+(n.Goal.X-n.Position.X)*t,n.Position.Z+(n.Goal.Z-n.Position.Z)*t)));}
                }
                a.Height=ApproachAir(a.Height,Math.Max(required,nextClear),dt*(a.Height<Math.Max(required,nextClear)?profile.HawkClimbSpeed:profile.HawkDescentSpeed));
                bool geometryClear=AirInside(next)&&a.Height+1e-7>=nextClear&&AirSweepClear(n.Position,next,a.Height);
                if(!geometryClear||!AirSeparation(u,next,all))
                {
                    // A deterministic lateral alternative; never climb through another aircraft.
                    double sign=(u.Id&1)==0?1:-1;
                    var side=new NavPoint(n.Position.X-sign*Math.Sin(n.Heading)*profile.HawkStrafeSpeed*dt,n.Position.Z+sign*Math.Cos(n.Heading)*profile.HawkStrafeSpeed*dt);
                    if(geometryClear&&n.Moving&&AirInside(side)&&a.Height>=ClearHeight(side)&&AirSweepClear(n.Position,side,a.Height)&&AirSeparation(u,side,all))next=side;
                    else next=n.Position;
                    velocity=new NavPoint((next.X-n.Position.X)/dt,(next.Z-n.Position.Z)/dt);
                }
                double ax=(velocity.X-a.Velocity.X)/dt,az=(velocity.Z-a.Velocity.Z)/dt;
                a.Pitch=ApproachAir(a.Pitch,(velocity.X*Math.Cos(n.Heading)+velocity.Z*Math.Sin(n.Heading))/profile.HawkSpeed*profile.HawkCruisePitch+(ax*Math.Cos(n.Heading)+az*Math.Sin(n.Heading))/profile.HawkAcceleration*profile.HawkAccelerationPitch,profile.HawkAttitudeSpeed*dt);
                a.Roll=ApproachAir(a.Roll,Math.Max(-profile.HawkBank,Math.Min(profile.HawkBank,(-ax*Math.Sin(n.Heading)+az*Math.Cos(n.Heading))/profile.HawkAcceleration*profile.HawkBank+a.YawVelocity/profile.HawkYawSpeed*profile.HawkBank)),profile.HawkAttitudeSpeed*dt);
                n.Position=next;a.Velocity=velocity;
                if(yielding){if(Distance(n.Position,a.YieldGoal)<=profile.Navigation.ArrivalTolerance&&Distance(default,velocity)<.01){a.Yielding=false;a.Velocity=default;}n.Goal=savedGoal;n.Moving=savedMoving;if(!a.Yielding&&n.Held)n.Goal=n.Position;continue;}
                if(!n.Moving&&Distance(default,velocity)<1e-5){a.Short=false;n.Outcome=n.Held?NavigationOutcome.Held:NavigationOutcome.Arrived;if(u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move||u.CurrentOrder?.Kind==PlayableTacticalOrderKind.AttackMove)u.CurrentOrder=null;if(a.Deferred.Count>0){var command=a.Deferred.Dequeue();SetAirOrder(u,command,command.Target);}}
            }
        }
        private void AdvanceAirYaw(AirState a,double desired,double dt)
        {
            double rate=Math.Max(-profile.HawkYawSpeed,Math.Min(profile.HawkYawSpeed,Angle(desired-a.Actor.Heading)/dt));
            a.YawVelocity=ApproachAir(a.YawVelocity,rate,profile.HawkYawAcceleration*dt);a.Actor.Heading=Angle(a.Actor.Heading+a.YawVelocity*dt);
        }
        private bool AirSeparation(Unit self,NavPoint next,Unit[] all)
        {
            foreach(var other in all)if(other.Id!=self.Id)
            {
                double spacing=2*profile.HawkRadius+profile.HawkSeparation;
                if(SegmentDistance(other.Air.Actor.Position,self.Air.Actor.Position,next)<spacing)return false;
            }
            return true;
        }
        private static double SegmentDistance(NavPoint p,NavPoint a,NavPoint b)
        {double dx=b.X-a.X,dz=b.Z-a.Z,len=dx*dx+dz*dz,t=len==0?0:Math.Max(0,Math.Min(1,((p.X-a.X)*dx+(p.Z-a.Z)*dz)/len));return Distance(p,new NavPoint(a.X+t*dx,a.Z+t*dz));}
        private bool AirSweepClear(NavPoint from,NavPoint to,double height)
        {
            foreach(var s in SolidObstacles())
            {
                double top=double.IsNaN(s.Top)?Math.Max(AirSurface(from),AirSurface(to))+profile.BallisticWallHeight:s.Top;
                if(height-profile.HawkRadius>top)continue;
                // Exact planar segment predicate includes radius against thin authored solids.
                var geometry=new NavGeometry(profile.ArenaHalfExtent,new[]{s},1);
                if(!geometry.SegmentFree(from,to,profile.HawkRadius))return false;
            }
            return true;
        }
    }
}
