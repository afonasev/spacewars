using System;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private bool PersonallyVisible(Unit unit,NavUnit self,int target,out NavPoint point)
            =>Target(target,out point,out _)&&DefaultEnemyAlive(target)&&
                Distance(self.Position,point)<=PlayableUnitRules.Vision(profile,unit.Kind)&&VisibleTarget(unit.Owner,target);

        private bool ResponseWeaponCompatible(Unit unit,int target)
            =>!AirTarget(target)||unit.Kind==PlayableEntityKind.Explorer||unit.Kind==PlayableEntityKind.Hawk;

        private void ObserveCombatHit(int victim,PlayableOwner owner,NavPoint position,int attacker,PlayableOwner? source,NavPoint incoming)
        {
            if(!source.HasValue||!Hostile(source.Value,owner)||profile.CombatResponsePursuitRadius<=0)return;
            bool liveAttacker=attacker!=0&&DefaultEnemyAlive(attacker);
            double incomingLength=Distance(default,incoming);
            foreach(var unit in units.Values)
            {
                bool direct=unit.Id==victim;
                if(Frozen(unit)||unit.Health<=0||eliminated.Contains(unit.Owner)||Hostile(unit.Owner,owner)||
                    !TryActor(unit.Id,out var self)||self.Held||HasExplicitCombatWork(unit))continue;
                if(!direct&&(!liveAttacker||profile.CombatResponseHelpRadius<=0||Distance(self.Position,position)>profile.CombatResponseHelpRadius))continue;

                var prior=unit.DefaultCombat;
                if(prior?.Phase==DefaultCombatPhase.Return||prior?.Phase==DefaultCombatPhase.ResponseReturn||
                    !direct&&prior?.Phase==DefaultCombatPhase.Withdrawal)continue;
                if((prior==null||prior.Phase==DefaultCombatPhase.Idle)&&(self.Moving||navigation.IsPending(unit.Id)))continue;

                bool visible=liveAttacker&&PersonallyVisible(unit,self,attacker,out _);
                if(visible&&ResponseWeaponCompatible(unit,attacker))
                {
                    if(prior?.Phase==DefaultCombatPhase.Response)continue;
                    unit.DefaultCombat=new DefaultCombatState
                    {
                        Phase=DefaultCombatPhase.Response,
                        Anchor=prior!=null&&prior.Phase!=DefaultCombatPhase.Idle?prior.Anchor:self.Position,
                        Engaged=attacker
                    };
                    unit.Repath=0;
                }
                else if(direct&&!visible&&incomingLength>0&&profile.CombatResponseWithdrawDistance>0)
                {
                    if(prior?.Phase==DefaultCombatPhase.Withdrawal)
                    {
                        prior.Deadline=elapsed+profile.CombatResponseQuietSeconds;
                        continue;
                    }
                    var anchor=prior!=null&&prior.Phase!=DefaultCombatPhase.Idle?prior.Anchor:self.Position;
                    double distance=Math.Min(profile.CombatResponseWithdrawDistance,profile.CombatResponsePursuitRadius);
                    unit.DefaultCombat=new DefaultCombatState
                    {
                        Phase=DefaultCombatPhase.Withdrawal,
                        Anchor=anchor,
                        Deadline=elapsed+profile.CombatResponseQuietSeconds,
                        WithdrawalGoal=new NavPoint(anchor.X+incoming.X*distance/incomingLength,anchor.Z+incoming.Z*distance/incomingLength)
                    };
                }
            }
        }

        private bool PrepareCombatResponse(Unit unit,NavUnit self,DefaultCombatState state)
        {
            if(state.Phase==DefaultCombatPhase.Response)
            {
                int target=ResponseContact(unit,self,state);
                if(target!=0)
                {
                    SetResponseTarget(unit,target);
                    DefaultStop(unit,self);
                    return true;
                }

                SetResponseTarget(unit,0);
                if(ContinueResponseApproach(unit,self,state))return true;
                BeginResponseReturn(state);
            }

            if(state.Phase==DefaultCombatPhase.Withdrawal)
            {
                if(elapsed<state.Deadline)
                {
                    if(!state.MovementRequested)
                    {
                        DefaultStop(unit,self);
                        state.MovementRequested=true;
                        DefaultMove(unit,self,state.WithdrawalGoal);
                    }
                    SetResponseTarget(unit,ResponseMovingFire(unit,self));
                    return true;
                }
                BeginResponseReturn(state);
            }

            if(!state.MovementRequested)
            {
                DefaultStop(unit,self);
                state.MovementRequested=true;
                if(Distance(self.Position,state.Anchor)<=profile.Navigation.ArrivalTolerance||!DefaultMove(unit,self,state.Anchor))
                    ResetDefaultCombat(unit);
            }
            else if(!self.Moving&&!navigation.IsPending(unit.Id))ResetDefaultCombat(unit);
            SetResponseTarget(unit,ResponseMovingFire(unit,self));
            return true;
        }

        private void SetResponseTarget(Unit unit,int target)
        {
            if(unit.Target==target)return;
            InterruptBurst(unit);
            unit.Target=target;
        }

        private static bool ResponseMovesWithoutStopping(Unit unit)
            =>unit.DefaultCombat?.Phase==DefaultCombatPhase.Withdrawal||unit.DefaultCombat?.Phase==DefaultCombatPhase.ResponseReturn;

        private int ResponseMovingFire(Unit unit,NavUnit self)
        {
            if(unit.Kind!=PlayableEntityKind.Explorer&&!(unit.Kind==PlayableEntityKind.Tank&&profile.TankFiresWhileMoving))return 0;
            int attacker=PreferredAttacker(unit,self);
            return attacker!=0?attacker:DefaultDefenseTarget(unit,self);
        }

        private int ResponseContact(Unit unit,NavUnit self,DefaultCombatState state)
        {
            if(Distance(state.Anchor,self.Position)>profile.CombatResponsePursuitRadius)return 0;
            int attacker=PreferredAttacker(unit,self);
            if(attacker!=0)return attacker;
            double range=DefaultWeaponRange(unit);
            targetSpatialIndex.Query(self.Position,range,targetSpatialIndex.Candidates);
            int best=0;
            bool building=true;
            double bestDistance=double.PositiveInfinity;
            foreach(var candidate in targetSpatialIndex.Candidates)
            {
                int target=candidate.Id;
                if(!PersonallyVisible(unit,self,target,out var point)||!ResponseWeaponCompatible(unit,target))continue;
                double distance=Distance(self.Position,point);
                if(distance>range||!DefaultFireClear(unit,self,target))continue;
                if(target==unit.BurstTarget&&unit.BurstRemaining>0)return target;
                if(best==0||building&&!candidate.Building||building==candidate.Building&&(distance<bestDistance||distance==bestDistance&&target<best))
                {best=target;building=candidate.Building;bestDistance=distance;}
            }
            return best;
        }

        private bool ContinueResponseApproach(Unit unit,NavUnit self,DefaultCombatState state)
        {
            double leash=profile.CombatResponsePursuitRadius;
            double range=DefaultWeaponRange(unit);
            double vision=PlayableUnitRules.Vision(profile,unit.Kind);
            targetSpatialIndex.Query(self.Position,vision,targetSpatialIndex.Candidates);
            bool pending=navigation.IsPending(unit.Id);
            if(state.MovementRequested&&!pending&&!self.Moving&&
                (self.Outcome==NavigationOutcome.Unreachable||self.Outcome==NavigationOutcome.Blocked||self.Outcome==NavigationOutcome.Rejected))return false;
            bool canRequest=!pending&&unit.Repath<=0;
            int best=0;
            double bestDistance=double.PositiveInfinity;
            NavPoint goal=default;
            bool WithinLeash(NavPoint candidate)=>Distance(state.Anchor,candidate)<=leash;
            foreach(var candidate in targetSpatialIndex.Candidates)
            {
                int target=candidate.Id;
                if(!PersonallyVisible(unit,self,target,out var point)||!ResponseWeaponCompatible(unit,target)||Distance(state.Anchor,point)>leash+range)continue;
                if(!canRequest)return true;
                double distance=Distance(self.Position,point);
                if(best!=0&&target!=state.Engaged&&(distance>bestDistance||distance==bestDistance&&target>best))continue;
                bool found=unit.Air==null?TryTargetAttackApproach(unit.Id,self.Position,point,target,out var next,WithinLeash):
                    HawkApproach(unit,target,out next,WithinLeash);
                if(!found||Distance(self.Position,next)<=profile.Navigation.ArrivalTolerance)continue;
                best=target;
                bestDistance=distance;
                goal=next;
                if(target==state.Engaged)break;
            }
            if(best==0)return false;
            state.Engaged=best;
            if(self.Moving&&Distance(self.Goal,goal)<=profile.Navigation.ArrivalTolerance)return true;
            unit.Repath=profile.AttackRepathSeconds;
            state.MovementRequested=DefaultMove(unit,self,goal);
            return state.MovementRequested;
        }

        private static void BeginResponseReturn(DefaultCombatState state)
        {
            state.Phase=DefaultCombatPhase.ResponseReturn;
            state.MovementRequested=false;
        }

        private void EnforceCombatResponseRadius(Unit unit,NavUnit self,double dt)
        {
            var state=unit.DefaultCombat;
            if(state==null||state.Phase!=DefaultCombatPhase.Response&&state.Phase!=DefaultCombatPhase.Withdrawal||
                !self.Moving&&!navigation.IsPending(unit.Id))return;
            if(state.Phase==DefaultCombatPhase.Withdrawal&&!state.MovementRequested)
            {
                DefaultStop(unit,self);
                return;
            }
            double limit=profile.CombatResponsePursuitRadius;
            // Reserve the next movement step for local avoidance near the edge.
            if(Distance(state.Anchor,self.Position)+self.Speed*dt<=limit&&
                (!self.Moving||Distance(state.Anchor,self.Goal)<=limit&&!RouteLeavesResponseRadius(self,state.Anchor,limit)))return;
            DefaultStop(unit,self);
            if(state.Phase==DefaultCombatPhase.Response)BeginResponseReturn(state);
        }

        private static bool RouteLeavesResponseRadius(NavUnit self,NavPoint anchor,double limit)
        {
            foreach(var point in self.Route)
                if(Distance(anchor,point)>limit)return true;
            return false;
        }
    }
}
