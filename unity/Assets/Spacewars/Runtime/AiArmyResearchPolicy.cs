using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Runtime
{
    public sealed class AiArmyResearchPolicy
    {
        private readonly HashSet<PlayableResearchKind> autonomous=new HashSet<PlayableResearchKind>();
        private readonly HashSet<PlayableResearchKind> priority=new HashSet<PlayableResearchKind>();
        public bool Autonomous {get;private set;}
        public double UtilityScale {get;private set;}
        public bool Prioritizes(PlayableResearchKind kind)=>priority.Contains(kind);
        public bool Allows(PlayableResearchKind kind)=>!PlayableResearchRules.Global(kind)||autonomous.Contains(kind)||Prioritizes(kind);
        public static AiArmyResearchPolicy Observe(PlayableAiObservation o,PlayableProfile p,AiDifficulty difficulty,AiGamePhase phase,AiDecisionArbiter memory)
        {
            var result=new AiArmyResearchPolicy {UtilityScale=p.ArmySystemsAiUtilityScale(difficulty),Autonomous=o.Tick>=AiProfile.SecondsToTicks(p.ArmySystemsAiDelaySeconds(difficulty),30)};
            foreach(bool weapon in new[]{true,false})
            {
                int visible=o.Entities.Where(e=>e.Health>0&&o.IsHostile(e.Owner)&&(o.Vision==null||o.Vision.IsVisible(e.Position))).Select(e=>weapon?e.WeaponSystemsLevel:e.DefensiveSystemsLevel).DefaultIfEmpty(0).Max();
                var kinds=weapon?new[]{PlayableResearchKind.ImprovedWeaponSystems,PlayableResearchKind.AdvancedWeaponSystems}:new[]{PlayableResearchKind.ImprovedDefensiveSystems,PlayableResearchKind.AdvancedDefensiveSystems};
                for(int level=1;level<=2;level++)
                {
                    var kind=kinds[level-1];
                    bool timeReady=o.Tick>=AiProfile.SecondsToTicks(p.ArmySystemsAiDelaySeconds(difficulty,level),30);
                    if(timeReady)result.autonomous.Add(kind);
                    // Recruit does not collect opponent upgrade intelligence.
                    if(difficulty!=AiDifficulty.Recruit&&visible>=level)memory.RememberArmyResearch(kind,o.Tick);
                    bool behind=difficulty!=AiDifficulty.Recruit&&memory.ArmyResearchSeen(kind,out var first)&&o.Tick-first>=AiProfile.SecondsToTicks(p.ArmySystemsAiCatchupSeconds(difficulty),30)&&!o.OwnerResearch.Any(x=>x.Kind==kind&&x.Complete);
                    if(behind||timeReady&&difficulty==AiDifficulty.Veteran&&phase!=AiGamePhase.Early)result.priority.Add(kind);
                }
            }
            return result;
        }
    }
    public sealed partial class AiDecisionArbiter
    {
        // Fixed four gameplay facts; no arbitrary intent key or hidden opponent state.
        internal const string ArmyResearchMemoPrefix="observed-army-research:";
        internal void RememberArmyResearch(PlayableResearchKind kind,long tick)
        {string key=ArmyResearchMemoPrefix+kind;if(!history.ContainsKey(key))history[key]=new History{FirstTick=tick,Conditions=kind.ToString()};}
        internal bool ArmyResearchSeen(PlayableResearchKind kind,out long tick)
        {if(history.TryGetValue(ArmyResearchMemoPrefix+kind,out var h)){tick=h.FirstTick;return true;}tick=0;return false;}
    }
}
