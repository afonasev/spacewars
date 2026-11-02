using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    internal sealed partial class AiCombatMicroPolicy
    {
        static bool ReachableShot(PlayableAiObservation o,PlayableProfile p,PlayableEntitySnapshot u,PlayableEntitySnapshot enemy)=>
            enemy!=null&&o.IsHostile(enemy.Owner)&&enemy.Health>0&&o.Vision?.IsVisible(enemy.Position)==true&&
            AiRosterCatalog.Initial.Targets(u.Kind,enemy.Kind==PlayableEntityKind.Hawk?"air":"ground")&&Distance(u.Position,enemy.Position)<=PlayableUnitRules.Range(p,u.Kind,u.Upgraded)&&AiCombatMicroSafety.FireClear(o,p,u,enemy);
        static double Pulse(PlayableProfile p,PlayableEntitySnapshot u)=>u.Kind==PlayableEntityKind.Hawk?p.HawkDamage*2:u.Kind==PlayableEntityKind.Tank?p.TankWeaponDamage:u.Kind==PlayableEntityKind.Explorer?p.ExplorerDamage:p.ShkvalDamage;
        bool FocusCertificate(PlayableAiObservation o,PlayableEntitySnapshot u)=>certificates.TryGetValue(u.Id,out var c)&&c.Kind==PlayableCommandKind.Attack&&Matches(c,o,u);
        static PlayableEntitySnapshot[] Assigned(PlayableAiObservation o,PlayableProfile p,AiArmyState army,PlayableEntitySnapshot[] own,PlayableEntitySnapshot enemy)=>own.Where(u=>army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)&&
            (u.TargetId==enemy.Id||u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack&&u.CurrentOrder.TargetId==enemy.Id)&&ReachableShot(o,p,u,enemy)).ToArray();
        static bool SupportedShot(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,AiArmyState army,PlayableEntitySnapshot[] own,PlayableEntitySnapshot u,PlayableEntitySnapshot enemy)=>ReachableShot(o,p,u,enemy)&&
            (u.Kind!=PlayableEntityKind.Hawk||own.Any(a=>a.Id!=u.Id&&army.Members.Contains(a.Id)&&PlayableUnitRules.HealthRatio(p,a)>=V(ai,d,"returnCoreHealth")&&Distance(a.Position,u.Position)<=V(ai,d,"supportRadius"))&&
             !AiCombatMicroSafety.Towers(o).Any(t=>AiCombatMicroSafety.Covers(o,p,t,u,u.Position)));
        bool FocusCurrent(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty difficulty,AiArmyRegistry registry,AiCombatMicroPlan plan,PlayableAiAction action)
        {
            var army=registry.Capture().Armies.FirstOrDefault(a=>a.Id==plan.Army&&a.OwnerId==o.OwnerId&&a.Major&&a.Phase!=AiArmyPhase.Disbanded&&a.Phase!=AiArmyPhase.Retreating&&a.Phase!=AiArmyPhase.Recovering);
            if(army==null)return false;
            var enemy=o.Entities.FirstOrDefault(u=>u.Id==action.TargetId);var own=o.Entities.Where(u=>u.Owner==o.Owner&&Eligible(u)).ToArray();
            var selected=own.Where(u=>action.EntityIds.Contains(u.Id)).OrderBy(u=>u.Id).ToArray();
            if(selected.Length!=action.EntityIds.Count||selected.Any(u=>u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow||u.Queue?.Pending!=null||u.Queue!=null&&u.Queue.Deferred.Count!=0||!SupportedShot(o,p,ai,difficulty,army,own,u,enemy)))return false;
            // Other assignments may have changed during the reaction window.
            // Selected recipients replace their old assignment; count them once.
            var assigned=Assigned(o,p,army,own,enemy).Where(u=>!action.EntityIds.Contains(u.Id)).ToArray();int count=assigned.Length;double damage=assigned.Sum(u=>Pulse(p,u));
            foreach(var unit in selected){if(count>=V(ai,difficulty,"focusGroupSize")||count>0&&damage+Pulse(p,unit)>Health(enemy)*V(ai,difficulty,"overkillRatio"))return false;count++;damage+=Pulse(p,unit);}
            return true;
        }
        AiCombatMicroPlan Chase(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,AiArmyRegistry armies,PlayableEntitySnapshot[] own)
        {
            foreach(var army in armies.Capture().Armies.Where(a=>a.Major&&a.Phase!=AiArmyPhase.Disbanded).OrderBy(a=>a.Id))
            {
                var ordered=own.Where(u=>army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack&&!Reserves(u.Id)).ToArray();
                foreach(var task in ordered.GroupBy(u=>u.CurrentOrder.TargetId).OrderBy(g=>g.Key))
                {
                    var target=o.Entities.FirstOrDefault(e=>e.Id==task.Key&&o.IsHostile(e.Owner)&&e.Health>0);
                    var watching=task.Where(u=>FocusCertificate(o,u)&&!ReachableShot(o,p,u,target)).ToArray();
                    if(watching.Length==0&&V(ai,d,"chaseEnabled")>0&&target?.Moving==true)
                    {
                        // Ordinary strategic AttackMove/Follow and building goals are
                        // untouched. A retreating unit only loses pursuers that have a
                        // real local alternative; nearby friends alone do not justify chase.
                        watching=task.Where(u=>!ReachableShot(o,p,u,target)&&Attention(o,u.Id,ai,d)&&
                            o.Entities.Any(e=>e.Id!=task.Key&&SupportedShot(o,p,ai,d,army,own,u,e))).ToArray();
                    }
                    if(watching.Length==0)continue;
                    foreach(var alternative in o.Entities.Where(e=>e.Id!=task.Key&&watching.All(u=>SupportedShot(o,p,ai,d,army,own,u,e))).OrderBy(e=>Health(e)).ThenBy(e=>e.Id))
                    {
                        var assigned=Assigned(o,p,army,own,alternative);int count=assigned.Length;double damage=assigned.Sum(u=>Pulse(p,u));
                        if(count>=V(ai,d,"focusGroupSize")||damage>=Health(alternative)*V(ai,d,"overkillRatio"))continue;
                        var selected=new List<PlayableEntitySnapshot>();
                        foreach(var u in watching.OrderBy(u=>u.Id))
                        {
                            if(count>=(int)V(ai,d,"focusGroupSize")||count>0&&damage+Pulse(p,u)>Health(alternative)*V(ai,d,"overkillRatio"))break;
                            selected.Add(u);count++;damage+=Pulse(p,u);
                        }
                        if(selected.Count>0)return new AiCombatMicroPlan{Action=Action(o,PlayableCommandKind.Attack,selected.Select(u=>u.Id),alternative.Position,alternative.Id),Phase=AiCombatMicroPhase.Focus,Army=army.Id};
                    }
                    // All members stop the SAME pursuit task. This is one useful
                    // owner action, rather than one Stop for each member/map front.
                    return new AiCombatMicroPlan{Action=Action(o,PlayableCommandKind.Stop,watching.Select(u=>u.Id),watching[0].Position),Phase=AiCombatMicroPhase.Regroup,Army=army.Id};
                }
            }
            return null;
        }
        AiCombatMicroPlan Focus(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,AiArmyRegistry armies,PlayableEntitySnapshot[] own)
        {
            foreach(var army in armies.Capture().Armies.Where(a=>a.Major&&a.Phase!=AiArmyPhase.Disbanded&&a.Phase!=AiArmyPhase.Retreating&&a.Phase!=AiArmyPhase.Recovering).OrderBy(a=>a.Id))
            {
                var shooters=own.Where(u=>army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)&&!clients.ContainsKey(u.Id)&&u.Queue?.Pending==null&&
                    (u.Queue==null||u.Queue.Deferred.Count==0)&&u.CurrentOrder?.Kind!=PlayableTacticalOrderKind.Follow&&Attention(o,u.Id,ai,d)&&
                    (u.Kind==PlayableEntityKind.Hawk?V(ai,d,"airFocusEnabled"):V(ai,d,"focusEnabled"))>0).ToArray();
                var enemies=o.Entities.Where(e=>o.IsHostile(e.Owner)&&e.Health>0&&o.Vision?.IsVisible(e.Position)==true).OrderBy(e=>Health(e)).ThenBy(e=>e.Id).ToArray();
                foreach(var enemy in enemies)
                {
                    // Current fire/explicit assignments, including earlier micro
                    // subsets, consume the same target's overkill/group allowance.
                    var assigned=Assigned(o,p,army,own,enemy);
                    int count=assigned.Length;double damage=assigned.Sum(u=>Pulse(p,u));
                    if(count>=V(ai,d,"focusGroupSize")||damage>=Health(enemy)*V(ai,d,"overkillRatio"))continue;
                    var available=shooters.Where(u=>!assigned.Any(a=>a.Id==u.Id)&&SupportedShot(o,p,ai,d,army,own,u,enemy))
                        .OrderByDescending(u=>u.Kind==PlayableEntityKind.Hawk).ThenBy(u=>Distance(u.Position,enemy.Position)).ThenBy(u=>u.Id).ToArray();
                    var chosen=new List<PlayableEntitySnapshot>();
                    foreach(var u in available)
                    {
                        if(count>=(int)V(ai,d,"focusGroupSize")||count>0&&damage+Pulse(p,u)>Health(enemy)*V(ai,d,"overkillRatio"))break;
                        chosen.Add(u);count++;damage+=Pulse(p,u);
                    }
                    if(chosen.Count>0)return new AiCombatMicroPlan{Action=Action(o,PlayableCommandKind.Attack,chosen.Select(u=>u.Id),enemy.Position,enemy.Id),Phase=AiCombatMicroPhase.Focus,Army=army.Id};
                }
            }
            return null;
        }
    }
}
