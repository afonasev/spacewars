using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableAiOwnerLoop
    {
        private AiCombatMicroAttention combatAttention=new AiCombatMicroAttention();
        private AiCombatMicroPolicy combatMicro=new AiCombatMicroPolicy();
        private PlayableAiObservation microAdmission;
        internal AiCombatMicroAttention CombatAttention=>combatAttention;
        internal void InheritCombatAttention(PlayableAiOwnerLoop authoritative)
        {combatAttention=authoritative.combatAttention.Copy();}
        void BindCombatMicro(PlayableAiObservation view)
        {
            microAdmission=view;
            // Rebind from this owner on every review/delivery; clones and restored
            // owners never inherit a closure into a former owner or unsaved truth.
            armyPlanner.CombatMicroOrder=flankPlanner.CombatMicroOrder=(o,a,u)=>combatMicro.CurrentOverride(o,a,u);
            armies.CombatMicroAuthorization=intent=>combatMicro.Authorizes(intent,microAdmission,profile,nativeProfile,nativeConfig.Difficulty,armies);
        }
        bool MicroReserved(PlayableAiAction a)=>Tactical(a)&&a.EntityIds.Any(combatMicro.Reserves);
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
        internal string CombatMicroRejection(PlayableAiAction action,AiCombatMicroPhase phase,AiKnowledgeState knowledge,AiProfile ai,AiDifficulty difficulty,AiCombatMicroPolicy micro)
        {
            var view=PlayableAiObservation.From(PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,action.Seed,OwnerFor(action.PlayerId)));
            var own=view.Entities.Where(u=>action.EntityIds.Contains(u.Id)&&u.Owner==view.Owner&&u.Health>0&&!u.Held&&!u.Departing&&u.OrderStamp?.Origin!=PlayableOrderOrigin.Human).ToArray();
            if(own.Length!=action.EntityIds.Count)return "micro actor changed";
            if(action.Kind==PlayableCommandKind.Move||action.Kind==PlayableCommandKind.Follow)
            {
                bool withdrawal=phase==AiCombatMicroPhase.Withdraw||phase==AiCombatMicroPhase.Repair;
                var paths=new Dictionary<int,NavPoint[]>();
                string rejection=ArmyMoveRejection(action,withdrawal,out var routeView,paths);if(rejection!=null)return rejection;
                foreach(var unit in own)
                {
                    var current=routeView.Entities.FirstOrDefault(u=>u.Id==unit.Id&&u.Owner==routeView.Owner&&u.Health>0);
                    if(current==null||!paths.TryGetValue(unit.Id,out var path))return "micro validated path unavailable";
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
