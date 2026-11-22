using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableAiOwnerLoop
    {
        internal bool IsAppliedCombatFocus(PlayableOrderStamp stamp,int target)=>combatMicro.IsAppliedFocus(stamp,target);
        internal bool IsAppliedCombatWithdrawal(PlayableOrderStamp stamp)=>combatMicro.IsAppliedWithdrawal(stamp);
        internal bool IsAppliedCerberusManeuver(PlayableOrderStamp stamp) =>
            combatMicro.IsAppliedCerberusManeuver(stamp);
        internal void CombatReturnShot(PlayableOrderStamp stamp,long tick,bool completedWithdrawal=false)=>combatMicro.ReturnShot(stamp,OwnerId,tick,nativeProfile,Difficulty,completedWithdrawal);
        internal void RetireUnavailableGroundRepairs(PlayableDomain domain)
            =>combatMicro.RetireUnavailableGroundRepairs(domain,OwnerId,nativeProfile,Difficulty);
        internal void RetireDeniedGroundRepair(PlayableDomain domain,MotionExecutionBinding binding,string reason)
            =>combatMicro.RetireDeniedGroundRepair(domain,OwnerId,binding,reason,nativeProfile,Difficulty);
        internal bool MissedCombatRelease(PlayableAiOwnerLoop current,long observedTick)=>combatMicro.MissedRelease(current.combatMicro,observedTick,nativeProfile,Difficulty);
        private AiCombatMicroAttention combatAttention=new AiCombatMicroAttention();
        private AiCombatMicroPolicy combatMicro=new AiCombatMicroPolicy();
        private PlayableAiObservation microAdmission;
        internal AiCombatMicroAttention CombatAttention=>combatAttention;
        internal void InheritCombatAttention(PlayableAiOwnerLoop authoritative)
        {combatAttention=authoritative.combatAttention.Copy();}
        void BindCombatMicro(PlayableAiObservation view)
        {
            microAdmission=view;
            combatMicro.GroupPassage=id=>(armyPlanner.Passing&&armies.ArmyFor(id)==armyPlanner.Capture().ArmyId)||(flankPlanner.Passing&&armies.ArmyFor(id)==flankPlanner.Capture().ArmyId);
            // Rebind from this owner on every review/delivery; clones and restored
            // owners never inherit a closure into a former owner or unsaved truth.
            armyPlanner.CombatMicroOrder=flankPlanner.CombatMicroOrder=(o,a,u)=>combatMicro.CurrentOverride(o,a,u);
            armies.CombatMicroAuthorization=intent=>combatMicro.Authorizes(intent,microAdmission,profile,nativeProfile,nativeConfig.Difficulty,armies);
        }
        bool MicroReserved(PlayableAiAction a)=>Tactical(a)&&a.EntityIds.Any(combatMicro.Reserves);
        bool ProtectsCombatReturn(PlayableAiAction action,Func<int,PlayableEntitySnapshot> actor,long tick)=>
            Tactical(action)&&action.EntityIds.Any(id=>combatMicro.HasAppliedReturn(id)&&
                combatMicro.ProtectsReturn(actor(id),OwnerId,Generation,tick,nativeProfile,Difficulty,armies));
        Exception PreemptCombatMicro(PlayableAiAction a)
        {
            Exception failure=null;
            void Attempt(Action operation){try{operation();}catch(Exception error){if(failure==null)failure=error;else PlayableDomain.PreserveTacticalSecondary(failure,error);}}
            foreach(var item in pendingItems.Where(x=>x.PolicyName==AiCombatMicroPolicy.Policy&&x.Action.EntityIds.Intersect(a.EntityIds).Any()).ToArray())
                Attempt(()=>CancelItem(item,PlayableAiDeliveryStatus.Cancelled,"Urgent defense preempts local micro."));
            Attempt(()=>combatMicro.Preempt(a.EntityIds));
            return failure;
        }
        long CombatReaction(PlayableAiAction action,long tick,long expires)
        {
            long maximum=AiProfile.SecondsToTicks(nativeProfile.DifficultyValue(nativeConfig.Difficulty,"control.reactionMaxSeconds"),30);
            // Original intent lifetime remains authoritative for every policy.
            maximum=Math.Min(maximum,Math.Max(0,expires-tick));
            if(maximum<ObservationDelayTicks)throw new InvalidOperationException("New intent lacks its minimum reaction window.");
            long spread=maximum-ObservationDelayTicks;
            ulong key=unchecked((ulong)(tick*397+action.ActionId*7919+opening.MatchSeed));
            return ObservationDelayTicks+(long)(key%(ulong)(spread+1));
        }
        internal bool EconomyContinuity=>nativeProfile.Value("economy.continuityEnabled")>0;
        static bool EssentialEconomy(PlayableAiAction action)=>AiRosterCatalog.IsProduction(action.Kind)||
            action.Kind is PlayableCommandKind.BuildFactory or PlayableCommandKind.BuildRefinery||
            action.Kind==PlayableCommandKind.BuildAt&&action.BuildingKind is PlayableBuildingKind.Factory or
                PlayableBuildingKind.Hangar or PlayableBuildingKind.Refinery or PlayableBuildingKind.Mine or
                PlayableBuildingKind.Outpost or PlayableBuildingKind.Headquarters;
        Pending[] DeliveryOrder(long tick)
        {
            if(!EconomyContinuity||pendingItems.Count<2)return pendingItems.ToArray();
            long refill=AiCombatMicroAttention.RefillTicks(nativeProfile,Difficulty);
            int Rank(Pending item)
            {
                if(item.DueTick>tick||item.Intent.ExpiresTick<tick)return 0;
                if(EssentialEconomy(item.Action))return tick-item.DueTick>=refill?3:1;
                return item.Intent.Priority>=2?2:0;
            }
            return pendingItems.OrderByDescending(Rank).ToArray();
        }
        private PlayableEntityKind? repeatDecision;
        private bool replaceScout;
        internal void PrepareProductionRepeat(PlayableSnapshot snapshot)
        {
            var observation=PlayableAiObservation.From(snapshot);
            repeatDecision=RepeatKind(observation);
            replaceScout=ScoutReplacementNeeded(observation);
        }
        private PlayableEntityKind? RepeatKind(PlayableAiObservation observation)
        {
            if(!openingExecutor.Capture().InitialForceFulfilled||!doctrine.Ready)return null;
            var targets=doctrine.ProductionTargets(observation,nativeProfile);
            if(targets.Count(value=>value>0)!=1)return null;
            if(targets[0]>0)return PlayableEntityKind.Tank;
            if(targets[1]>0)return PlayableEntityKind.Explorer;
            if(targets[2]>0)return PlayableEntityKind.Shkval;
            return targets[3]>0?PlayableEntityKind.Hawk:(PlayableEntityKind?)null;
        }
        bool RepeatMatchesDoctrine(PlayableAiObservation observation,PlayableEntityKind kind)
            =>RepeatKind(observation)==kind;
        bool ScoutReplacementNeeded(PlayableAiObservation observation)=>ScoutBudget>0&&
            !observation.Entities.Any(u=>u.Owner==observation.Owner&&u.Kind==PlayableEntityKind.Explorer&&u.Health>0)&&
            !observation.Buildings.Any(b=>b.Owner==observation.Owner&&b.PrivateState?.Orders.Any(o=>o.Kind==PlayableEntityKind.Explorer)==true);
        void EnableRepeatProduction(PlayableDomain domain,Pending item)
        {
            if(!openingExecutor.Capture().InitialForceFulfilled||!doctrine.Ready||launches.ReservationFor(item.Action)!=null)return;
            var kind=roster.Production(item.Action.Kind).kind;
            if(domain.BackgroundDecisionProjections?repeatDecision==kind:RepeatMatchesDoctrine(RepeatObservation(domain),kind))
                domain.SetAiProductionRepeat(item.Action.EntityIds.Single(),OwnerId,kind);
        }
        PlayableAiObservation RepeatObservation(PlayableDomain domain)=>PlayableAiObservation.From(
            domain.PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,opening.MatchSeed,domain.OwnerFor(OwnerId)));
        internal bool? RepeatProductionAdmission(PlayableDomain domain,int producerId,PlayableEntityKind kind)
        {
            if(!EconomyContinuity)return null;
            if(pendingItems.Any(p=>(AiRosterCatalog.IsProduction(p.Action.Kind)||p.Action.Kind==PlayableCommandKind.UpgradeProducer)&&p.Action.EntityIds.Contains(producerId)))return false;
            long free=(long)domain.AiLiquidCredits(OwnerId)-budget.SafetyReserve-budget.Reserved-budget.Unpaid;
            // Preserve the price of another factory for later expansion or investment.
            if(free<profile.FactoryCreditCost+PlayableUnitRules.Cost(profile,kind))return false;
            bool ground=kind!=PlayableEntityKind.Hawk&&kind!=PlayableEntityKind.Explorer;
            if(domain.BackgroundDecisionProjections)return repeatDecision==kind&&!(ground&&replaceScout)?true:(bool?)null;
            var observation=RepeatObservation(domain);
            return !(ground&&ScoutReplacementNeeded(observation))&&RepeatMatchesDoctrine(observation,kind)?true:(bool?)null;
        }
        long IntentLifetime(PlayableAiAction action)
        {
            long ordinary=DecisionIntervalTicks+ObservationDelayTicks;
            if(!EconomyContinuity||!EssentialEconomy(action))return ordinary;
            long maxReaction=AiProfile.SecondsToTicks(nativeProfile.DifficultyValue(Difficulty,"control.reactionMaxSeconds"),30);
            long service=maxReaction+2*AiCombatMicroAttention.RefillTicks(nativeProfile,Difficulty);
            return Math.Max(ordinary,Math.Min(service,budget.ReservationLifetimeTicks));
        }
        bool CombatAttentionAllows(PlayableDomain domain,Pending item,int index)
        {
            bool micro=item.PolicyName==AiCombatMicroPolicy.Policy;
            bool other=pendingItems.Any(p=>(p.PolicyName==AiCombatMicroPolicy.Policy)!=micro&&p.DueTick<=domain.Tick&&p.Intent.ExpiresTick>=domain.Tick);
            if(combatAttention.Allows(domain.Tick,nativeProfile,nativeConfig.Difficulty,micro,other))return true;
            pendingItems.Insert(Math.Min(index,pendingItems.Count),item);return false;
        }
    }
    internal sealed partial class PlayableDomain
    {
        internal bool RetireGroundRepairMove(string ownerId,int entity,int tower,long sequence,long action,long issued,
            long remainingTicks,AiProfile ai,AiDifficulty difficulty,out PlayableOrderStamp stamp,out NavLocation location,out string reason)
        {
            stamp=null;location=default;reason=null;
            if(!TryReadGroundRepairMove(ownerId,entity,sequence,action,issued,out var unit,out var actor,out var execution))return false;
            if(!CombatExecutionRepairReady(tower,unit.Owner,entity))reason="tower-unavailable";
            else if(remainingTicks<0)reason="repair-phase-expired";
            else if(!WaitingForWreck(entity)&&execution.TerminalFailure is TerminalRouteFailure failure&&TerminalProofMatches(execution,actor))
                reason="route-"+failure.Outcome.ToString().ToLowerInvariant();
            else if(execution.Assigned.HasValue&&!AtAssignedTacticalEndpoint(execution,actor)&&
                !AiCombatMicroPolicy.RepairFitsPhase(profile,ai,difficulty,unit.Kind,unit.Health,
                    UnitMaximumHealth(unit.Owner,unit.Kind),Distance(actor.Position,execution.Assigned.Value.Position),remainingTicks))
                reason="travel-and-healing-exceed-phase";
            if(reason==null)return false;
            stamp=execution.Stamp;location=actor.Location.Detached();
            return CommitInternalTactical(execution.Issuance,new[]{entity},InternalTacticalKind.CancelRepair,activationRevision:stamp.Revision);
        }

        internal bool CancelDeniedGroundRepairMove(MotionExecutionBinding binding,long action,long issued,out NavLocation location)
        {
            location=default;
            int entity=(int)binding.Entity;
            if(!TryReadGroundRepairMove(binding.Owner,entity,binding.Command,action,issued,out _,out var actor,out var execution)||
                execution.Stamp.Revision!=binding.Activation||!CombatExecutionGroundBindingCurrent(binding,actor))return false;
            location=actor.Location.Detached();
            return CommitInternalTactical(execution.Issuance,new[]{entity},InternalTacticalKind.CancelRepair,activationRevision:binding.Activation);
        }
        private bool TryReadGroundRepairMove(string ownerId,int entity,long sequence,long action,long issued,
            out Unit unit,out NavUnit actor,out QueueExecution execution)
        {
            actor=null;
            execution=null;
            if(!units.TryGetValue(entity,out unit)||unit.Air!=null||unit.Health<=0||unit.Owner!=OwnerFor(ownerId)||Frozen(unit)||
                !TryActor(entity,out actor)||actor.Held||unit.LastOrder is not PlayableOrderStamp stamp||
                stamp.UnitId!=entity||stamp.Owner!=unit.Owner||stamp.Generation!=navigation.Generation||stamp.Origin!=PlayableOrderOrigin.Ai||
                stamp.Source!=PlayableAiOpeningComposition.SourceIdentity||stamp.Kind!=PlayableCommandKind.Move||
                stamp.Sequence!=sequence||stamp.ActionId!=action||stamp.JobId!=action||stamp.Tick!=issued||
                !tacticalQueues.TryGetValue(entity,out var queue))return false;

            // If a repair replaces a fighting order, the old active execution
            // can coexist with the pending repair until its route is installed.
            execution=queue.Pending??queue.Active;
            var queuedStamp=execution?.Stamp;
            return execution!=null&&!execution.Interrupted&&execution.Incarnation==actor.Incarnation&&queuedStamp!=null&&
                queuedStamp.Revision==stamp.Revision&&queuedStamp.Sequence==stamp.Sequence&&queuedStamp.ActionId==stamp.ActionId;
        }
        private void ReleaseOutOfRangeMicroFocus(Unit unit,NavUnit self)
        {
            if(!unit.ExplicitTarget||unit.CurrentOrder?.Kind!=PlayableTacticalOrderKind.Attack||
                unit.LastOrder?.Origin!=PlayableOrderOrigin.Ai||combatExecutionOwners==null||
                !combatExecutionOwners().Any(owner=>owner.IsAppliedCombatFocus(unit.LastOrder,unit.Target)))return;
            if(Target(unit.Target,out var point,out _)&&Distance(self.Position,point)<=DefaultWeaponRange(unit)&&
                DefaultFireClear(unit,self,unit.Target))return;

            if(unit.Air==null)
            {
                if(self.Moving||navigation.IsPending(unit.Id))CombatStop(unit.Id);
                if(tacticalQueues.TryGetValue(unit.Id,out var queue)&&queue.Active?.Stamp.Sequence==unit.LastOrder.Sequence)
                    queue.Active=null;
            }
            else if(self.Moving)
            {
                self.Goal=self.Position;
                self.Moving=false;
                unit.Air.Short=false;
            }
            InterruptBurst(unit);
            unit.CurrentOrder=null;
            unit.Target=0;
            unit.ExplicitTarget=false;
            ResetDefaultCombat(unit);
        }
        internal string CombatMicroRejection(PlayableAiAction action,AiCombatMicroPhase phase,AiKnowledgeState knowledge,AiProfile ai,AiDifficulty difficulty,AiCombatMicroPolicy micro)
        {
            var view=PlayableAiObservation.From(PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,action.Seed,OwnerFor(action.PlayerId)));
            var own=view.Entities.Where(u=>action.EntityIds.Contains(u.Id)&&u.Owner==view.Owner&&u.Health>0&&!u.Held&&!u.Departing&&u.OrderStamp?.Origin!=PlayableOrderOrigin.Human).ToArray();
            if(own.Length!=action.EntityIds.Count)return "micro actor changed";
            bool resume=phase==AiCombatMicroPhase.Resume&&action.Kind==PlayableCommandKind.AttackMove;
            bool lateral = AiCombatMicroPolicy.CerberusManeuver(phase);
            if(action.Kind==PlayableCommandKind.Move||action.Kind==PlayableCommandKind.Follow||resume)
            {
                bool withdrawal=phase==AiCombatMicroPhase.Withdraw||phase==AiCombatMicroPhase.Repair;
                bool shortWithdrawal=phase==AiCombatMicroPhase.Withdraw;
                bool returning=phase==AiCombatMicroPhase.Return&&action.Kind==PlayableCommandKind.Follow;
                var paths=new Dictionary<int,NavPoint[]>();
                // The live core can still be in contact after the enemy switches
                // targets. Follow is local recovery into that verified core.
                string rejection=ArmyMoveRejection(action,withdrawal,out var routeView,paths,resume||returning||lateral,shortWithdrawal,micro,exactReturn:resume,nativeAiProfile:ai,nativeDifficulty:difficulty,exactLateral:lateral);if(rejection!=null)return rejection;
                foreach(var unit in own)
                {
                    var current=routeView.Entities.FirstOrDefault(u=>u.Id==unit.Id&&u.Owner==routeView.Owner&&u.Health>0);
                    if(current==null||!paths.TryGetValue(unit.Id,out var path))return "micro validated path unavailable";
                    if (lateral && !AiCombatMicroPolicy.CerberusMoveStillHelpful(routeView, profile, difficulty,
                        current, action.TargetId, path.Last(), phase))
                        return "Cerberus maneuver no longer clears the danger";
                    if(shortWithdrawal)
                    {
                        if(!micro.RotationWithdrawalSafe(routeView,profile,ai,difficulty,current,path.Last()))return "withdrawal lost compatible cover or enters splash";
                        double length=0;
                        var at=current.Position;
                        foreach(var point in path){length+=Distance(at,point);at=point;}
                        if(length>AiCombatMicroPolicy.RotationLimit(profile,ai,difficulty,current)+profile.Navigation.ArrivalTolerance)
                            return "short withdrawal route exceeds its distance budget";
                    }
                    if(resume||returning)
                    {
                        if(!micro.RotationReturnSafe(routeView,profile,ai,difficulty,current,path.Last()))return "combat return enters artillery splash or loses cover";
                        var anchor=routeView.Entities.FirstOrDefault(e=>e.Id==action.TargetId&&e.Owner==routeView.Owner&&e.Health>0);
                        if(anchor==null||Distance(path.Last(),anchor.Position)>ai.DifficultyValue(difficulty,"micro.supportRadius"))
                            return "combat return endpoint lost its supporting army";
                        var at=current.Position;
                        foreach(var point in path)
                        {
                            if(!AiCombatMicroPolicy.SplashPathSafe(routeView,profile,current,at,point))return "combat return path enters artillery splash";
                            if(!VisibleSegment(Vision(view.Owner),at,point))return "combat return route left current vision";
                            at=point;
                        }
                    }
                    rejection=CombatMicroStaticPath(action,routeView,current,path,withdrawal,knowledge,phase==AiCombatMicroPhase.Repair,ai,difficulty,micro);if(rejection!=null)return rejection;
                }
                return null;
            }
            if(action.Kind==PlayableCommandKind.Stop)return null;
            var enemy=view.Entities.FirstOrDefault(e=>e.Id==action.TargetId&&view.IsHostile(e.Owner)&&e.Health>0&&view.Vision?.IsVisible(e.Position)==true);
            if(enemy==null)return "focus target unavailable";
            foreach(var unit in own)
            {
                if(!AiRosterCatalog.Initial.Targets(unit.Kind,enemy.Kind==PlayableEntityKind.Hawk?"air":"ground")||
                    Math.Sqrt(Math.Pow(unit.Position.X-enemy.Position.X,2)+Math.Pow(unit.Position.Z-enemy.Position.Z,2))>PlayableUnitRules.Range(profile,unit.Kind,unit.Upgraded))return "focus would require pursuit";
                if(unit.Kind==PlayableEntityKind.Hawk&&AiCombatMicroSafety.Towers(view,knowledge).Any(t=>AiCombatMicroSafety.Covers(view,profile,t,unit,unit.Position)))return "known antiair fire blocks air focus";
                if(!units.TryGetValue(unit.Id,out var actor)||!TryActor(unit.Id,out var nav)||!DefaultFireClear(actor,nav,enemy.Id))return "focus has no current firing line";
            }
            return null;
        }
    }
}
