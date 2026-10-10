using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private static bool IsFollowing(Unit u)=>u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow;
        private Unit FollowLeader(Unit follower,int target)
        {
            if(!units.TryGetValue(target,out var leader)||leader.Id==follower.Id||leader.Health<=0||Hostile(follower.Owner,leader.Owner)||!navigation.Crowd.TryGet(target,out _))return null;
            var seen=new HashSet<int>{follower.Id};var current=leader;
            while(current!=null){if(!seen.Add(current.Id))return null;current=IsFollowing(current)&&units.TryGetValue(current.CurrentOrder.TargetId,out var next)?next:null;}
            return leader;
        }
        private PlayableCommandStatus Follow(int[] ids,int target,long sequence,PlayableOwner issuer,out string message,out HashSet<int> appliedIds)
        {
            appliedIds=new HashSet<int>();int applied=0;
            // SOURCE visits authority units, preserving rejected/self/cyclic actors.
            var selection=new HashSet<int>(ids);
            foreach(var u in units.Values.Where(u=>selection.Contains(u.Id)&&u.Owner==issuer)){
                if(FollowLeader(u,target)==null)continue;
                navigation.Stop(u.Id,false);ClearFollowBurst(u);u.Target=0;u.ExplicitTarget=false;u.HasAttackMove=false;u.Repath=0;
                u.CurrentOrder=new PlayableTacticalOrderSnapshot(u.Id,u.Owner,navigation.Generation,sequence,Tick,PlayableTacticalOrderKind.Follow,default(NavPoint),target);appliedIds.Add(u.Id);applied++;
            }
            message=applied>0?"Following allied leader.":"No valid allied leader.";
            return applied>0?PlayableCommandStatus.Applied:PlayableCommandStatus.InvalidTarget;
        }
        private void AdvanceFollow(bool request=true)
        {
            foreach(var u in units.Values){if(!IsFollowing(u)||!navigation.Crowd.TryGet(u.Id,out var self))continue;
                var leader=FollowLeader(u,u.CurrentOrder.TargetId);
                if(leader==null){CombatStop(u.Id);ClearFollowBurst(u);u.Target=0;u.ExplicitTarget=false;u.CurrentOrder=null;continue;}
                navigation.Crowd.TryGet(leader.Id,out var anchor);
                if(Distance(self.Position,anchor.Position)<=profile.FollowDistance+profile.FollowArrivalTolerance){if(self.Moving||self.Held||navigation.IsPending(u.Id))CombatStop(u.Id);continue;}
                // Retain each accepted path until it finishes. Requesting every tick
                // would invalidate late answers and starve the asynchronous solver.
                var goal=new NavPoint(anchor.Position.X-Math.Cos(anchor.Heading)*profile.FollowDistance,anchor.Position.Z-Math.Sin(anchor.Heading)*profile.FollowDistance);
                bool settledSameGoal=self.Outcome==NavigationOutcome.Arrived&&Distance(goal,self.Goal)<=1e-9&&Distance(self.Position,goal)<=profile.Navigation.ArrivalTolerance;
                if(request&&!self.Moving&&CanResumeCombat(u.Id)&&!settledSameGoal)CombatMove(u.Id,goal);
            }
        }
        private bool FollowCanFire(Unit u)
        {
            if(!IsFollowing(u))return true;var leader=FollowLeader(u,u.CurrentOrder.TargetId);
            return leader!=null&&navigation.Crowd.TryGet(u.Id,out var self)&&navigation.Crowd.TryGet(leader.Id,out var anchor)&&
                Math.Sqrt(u.Velocity.X*u.Velocity.X+u.Velocity.Z*u.Velocity.Z)<=.0001&&Math.Sqrt(leader.Velocity.X*leader.Velocity.X+leader.Velocity.Z*leader.Velocity.Z)<=.0001&&Distance(self.Position,anchor.Position)<=profile.FollowDistance+profile.FollowArrivalTolerance;
        }
        private static void ClearFollowBurst(Unit u){u.BurstRemaining=0;u.BurstTarget=0;u.BurstDelay=0;}
        private void InterruptBurstIfFollowingCannotFire(Unit u){if(!FollowCanFire(u))ClearFollowBurst(u);}
        private int FollowDefenseTarget(Unit u,NavPoint position,double range)
        {
            if(u.BurstRemaining>0){
                if(VisibleTarget(u.Owner,u.BurstTarget)&&Target(u.BurstTarget,out var burstPoint,out _)&&Distance(position,burstPoint)<=range)return u.BurstTarget;
                ClearFollowBurst(u);return 0;
            }
            return units.Keys.Concat(buildings.Keys).Where(id=>VisibleTarget(u.Owner,id)&&Target(id,out var p,out _)&&Distance(position,p)<=range)
                .OrderBy(id=>{Target(id,out var p,out _);return Distance(position,p);}).ThenBy(id=>id).FirstOrDefault();
        }
    }
}
