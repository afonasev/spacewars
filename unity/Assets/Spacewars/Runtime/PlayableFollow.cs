using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private static bool IsFollowing(Unit u)=>u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow;
#if UNITY_INCLUDE_TESTS
        internal Action<long, int, int, NavPoint, NavPoint, NavPoint, long, string> FollowReadbackForTests;
#endif
        private Unit FollowLeader(Unit follower,int target)
        {
            if(!units.TryGetValue(target,out var leader)||leader.Id==follower.Id||leader.Health<=0||Hostile(follower.Owner,leader.Owner)||!TryActor(target,out _))return null;
            var seen=new HashSet<int>{follower.Id};var current=leader;
            while(current!=null){if(!seen.Add(current.Id))return null;current=IsFollowing(current)&&units.TryGetValue(current.CurrentOrder.TargetId,out var next)?next:null;}
            return leader;
        }
        private PlayableCommandStatus Follow(int[] ids,int target,long sequence,PlayableOwner issuer,out string message,out HashSet<int> appliedIds,PreparedTacticalCommit preparation=null)
        {
            appliedIds=new HashSet<int>();int applied=0;
            // Validate in authority order before admitting the complete group stop.
            var selection=new HashSet<int>(ids);
            var followers=units.Values.Where(u=>selection.Contains(u.Id)&&u.Owner==issuer&&FollowLeader(u,target)!=null).ToArray();
            if(followers.Length>0&&!(preparation==null?navigation.TryStopGroup(followers.Select(u=>u.Id).ToArray(),false):navigation.StageTacticalStop(followers.Select(u=>u.Id).ToArray(),false,preparation.NavigationInputs))){message="Follow order could not be prepared.";return PlayableCommandStatus.Overflow;}
            if(preparation!=null&&followers.Length>0&&!preparation.BeginProposal()){message="Follow domain source changed.";return PlayableCommandStatus.Rejected;}
            foreach(var u in followers){
                ClearFollowBurst(u);u.Target=0;u.ExplicitTarget=false;u.HasAttackMove=false;u.Repath=0;
                u.CurrentOrder=new PlayableTacticalOrderSnapshot(u.Id,u.Owner,navigation.Generation,sequence,Tick,PlayableTacticalOrderKind.Follow,default(NavPoint),target);appliedIds.Add(u.Id);applied++;
            }
            message=applied>0?"Following allied leader.":"No valid allied leader.";
            return applied>0?PlayableCommandStatus.Applied:PlayableCommandStatus.InvalidTarget;
        }
        private void AdvanceFollow(bool request=true)
        {
            foreach(var u in units.Values){if(u.Air!=null||Frozen(u)||!IsFollowing(u)||!TryActor(u.Id,out var self))continue;
                var leader=FollowLeader(u,u.CurrentOrder.TargetId);
                if(leader==null){CombatStop(u.Id);ClearFollowBurst(u);u.Target=0;u.ExplicitTarget=false;u.CurrentOrder=null;continue;}
                TryActor(leader.Id,out var anchor);
                if(Distance(self.Position,anchor.Position)<=profile.FollowDistance+profile.FollowArrivalTolerance){if(self.Moving||self.Held||navigation.IsPending(u.Id))CombatStop(u.Id);continue;}
                // Retain each accepted path until it finishes. Requesting every tick
                // would invalidate late answers and starve the asynchronous solver.
                if(!request||self.Moving)continue;
                if(!CanResumeCombat(u.Id)||u.Repath>0)
                {
#if UNITY_INCLUDE_TESTS
                    if(FollowReadbackForTests!=null)
                        FollowReadbackForTests(Tick,u.Id,leader.Id,self.Position,anchor.Position,self.Goal,
                            navigation.PendingRequestFor(u.Id)?.Request??0,u.Repath>0?"cooldown":WaitingForWreck(u.Id)?"wreck":"pending");
#endif
                    continue;
                }
                var goal=PlayableUnitRules.FollowGoal(profile,anchor.Position,anchor.Heading);
                if(navigation.UsesRecastGroundRoutes&&
                    !TryGroundFollowWaypoint(self,anchor,leader.Air!=null,goal,out goal))
                {
#if UNITY_INCLUDE_TESTS
                    FollowReadbackForTests?.Invoke(Tick,u.Id,leader.Id,self.Position,anchor.Position,goal,0,"no-waypoint");
#endif
                    u.Repath=profile.Navigation.RepairRetrySeconds;
                    continue;
                }
                bool settledSameGoal=self.Outcome==NavigationOutcome.Arrived&&Distance(goal,self.Goal)<=1e-9&&Distance(self.Position,goal)<=profile.Navigation.ArrivalTolerance;
                if(settledSameGoal)
                {
#if UNITY_INCLUDE_TESTS
                    FollowReadbackForTests?.Invoke(Tick,u.Id,leader.Id,self.Position,anchor.Position,goal,0,"settled");
#endif
                    continue;
                }
#if UNITY_INCLUDE_TESTS
                if(FollowReadbackForTests!=null)navigation.BeginCommandAdmissionForQa(u.CurrentOrder.CommandSequence);
#endif
                bool accepted=CombatMove(u.Id,goal);
#if UNITY_INCLUDE_TESTS
                if(FollowReadbackForTests!=null)
                    FollowReadbackForTests(Tick,u.Id,leader.Id,self.Position,anchor.Position,goal,
                        navigation.PendingRequestFor(u.Id)?.Request??0,accepted?"submitted":navigation.LastCommandAdmissionRefusal??"refused");
#endif
                if(!accepted)u.Repath=profile.Navigation.RepairRetrySeconds;
            }
        }
        private bool TryGroundFollowWaypoint(NavUnit follower,NavUnit leader,bool airLeader,NavPoint rear,out NavPoint waypoint)
        {
            bool Available(NavPoint point)=>CampaignMoveAllowed(follower.Id,point)&&navigation.CanAdmitFollowWaypoint(follower.Id,point);
            waypoint=rear;
            if(Available(rear))return true;
            // Prefer the original spacing before sampling the existing arrival band.
            // Inside the tolerance, a free waypoint still completes the same Follow.
            int count=profile.Navigation.CrowdHeadingIntervals;
            // Only refine after both ordinary samples fail. Probe the spaces
            // between their headings before retrying the same unavailable points.
            for(int pass=0;pass<2;pass++)
                for(int band=0;band<2;band++)
                {
                    double distance=profile.FollowDistance+band*profile.FollowArrivalTolerance*.5;
                    int first=pass==0&&band==0?1:0;
                    for(int sample=first;sample<count;sample++)
                    {
                        double step=pass==0?(sample+1)/2:sample/2+.5;
                        double angle=leader.Heading+(sample%2==0?-step:step)*2*Math.PI/count;
                        waypoint=new NavPoint(leader.Position.X-Math.Cos(angle)*distance,leader.Position.Z-Math.Sin(angle)*distance);
                        if(!navigation.Crowd.Locate(waypoint,follower.Radius,out var location)||
                            !airLeader&&location.SurfaceId!=leader.Location.SurfaceId)continue;
                        if(Available(waypoint))return true;
                    }
                    if(profile.FollowArrivalTolerance==0)break;
                }
            return false;
        }
        private bool FollowCanFire(Unit u)
        {
            if(!IsFollowing(u))return true;var leader=FollowLeader(u,u.CurrentOrder.TargetId);
            return leader!=null&&TryActor(u.Id,out var self)&&TryActor(leader.Id,out var anchor)&&
                Math.Sqrt(u.Velocity.X*u.Velocity.X+u.Velocity.Z*u.Velocity.Z)<=.0001&&Math.Sqrt(leader.Velocity.X*leader.Velocity.X+leader.Velocity.Z*leader.Velocity.Z)<=.0001&&Distance(self.Position,anchor.Position)<=profile.FollowDistance+profile.FollowArrivalTolerance;
        }
        private static void ClearFollowBurst(Unit u){u.BurstRemaining=0;u.BurstTarget=0;u.BurstDelay=0;if(u.Kind==PlayableEntityKind.Explorer)u.HawkBurstReload=0;}
        private void InterruptBurstIfFollowingCannotFire(Unit u){if(!FollowCanFire(u))ClearFollowBurst(u);}
        private int FollowDefenseTarget(Unit u,NavPoint position,double range)
        {
            if(u.Kind==PlayableEntityKind.Igla&&TryActor(u.Id,out var sniper))
                return KeepAutomaticTarget(u,sniper)?u.Target:DefaultDefenseTarget(u,sniper);
            if(ExplorerRocketEquipped(u)&&TryActor(u.Id,out var actor))return RocketAutomaticTarget(u,actor);
            if(u.BurstRemaining>0){
                if(DefaultEnemyAlive(u.BurstTarget)&&VisibleTarget(u.Owner,u.BurstTarget)&&Target(u.BurstTarget,out var burstPoint,out _)&&Distance(position,burstPoint)<=range)return u.BurstTarget;
                ClearFollowBurst(u);return 0;
            }
            return NearestSpatialTarget(position,u.Owner,range,false,u.Kind==PlayableEntityKind.Explorer);
        }
    }
}
