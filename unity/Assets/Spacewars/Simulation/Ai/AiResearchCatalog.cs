using System;
using System.Collections.Generic;
using System.Linq;
namespace Spacewars.Simulation.Ai
{
    public enum AiResearchEffect { Mobility, BurstDamage, AttackRange, GlobalDamage, GlobalHealth }
    public sealed class AiResearchDescriptor
    {
        internal AiResearchDescriptor(PlayableResearchKind kind,PlayableEntityKind unit,AiResearchEffect effect){Kind=kind;Unit=unit;Effect=effect;}
        public PlayableResearchKind Kind{get;} public PlayableEntityKind Unit{get;} public AiResearchEffect Effect{get;}
        public PlayableBuildingKind Prerequisite=>PlayableBuildingKind.ScientificCenter;
        public bool Global=>PlayableResearchRules.Global(Kind);
        public bool Affects(PlayableEntityKind kind)=>Global||Unit==kind;
        public double Cost(PlayableProfile p)=>PlayableResearchRules.Cost(p,Kind);
        public double Duration(PlayableProfile p)=>PlayableResearchRules.Duration(p,Kind);
        public double Before(PlayableProfile p)=>Global?1+(PlayableResearchRules.Previous(Kind).HasValue?PlayableResearchRules.Bonus(p,PlayableResearchRules.Previous(Kind).Value)/100:0):Effect==AiResearchEffect.Mobility?PlayableUnitRules.Speed(p,Unit):Effect==AiResearchEffect.BurstDamage?PlayableUnitRules.DamagePerSecond(p,Unit):PlayableUnitRules.Range(p,Unit);
        public double After(PlayableProfile p)=>Global?1+PlayableResearchRules.Bonus(p,Kind)/100:Effect==AiResearchEffect.Mobility?PlayableUnitRules.Speed(p,Unit,true):Effect==AiResearchEffect.BurstDamage?PlayableUnitRules.DamagePerSecond(p,Unit,true):PlayableUnitRules.Range(p,Unit,true);
        public double RelativeGain(PlayableProfile p)=>Math.Max(0,After(p)/Before(p)-1);
    }
    // All ordinary QueueResearch branches, with gameplay effects and terms. No AI balance constants.
    public static class AiResearchCatalog
    {
        public static IReadOnlyList<AiResearchDescriptor> All{get;}=Array.AsReadOnly(new[]{
            new AiResearchDescriptor(PlayableResearchKind.HawkSalvoSystems,PlayableEntityKind.Hawk,AiResearchEffect.BurstDamage),
            new AiResearchDescriptor(PlayableResearchKind.TankChassis,PlayableEntityKind.Tank,AiResearchEffect.Mobility),
            new AiResearchDescriptor(PlayableResearchKind.ExplorerAssaultGuns,PlayableEntityKind.Explorer,AiResearchEffect.BurstDamage),
            new AiResearchDescriptor(PlayableResearchKind.ShkvalGuidance,PlayableEntityKind.Shkval,AiResearchEffect.AttackRange),
            new AiResearchDescriptor(PlayableResearchKind.ImprovedWeaponSystems,PlayableEntityKind.Tank,AiResearchEffect.GlobalDamage),
            new AiResearchDescriptor(PlayableResearchKind.AdvancedWeaponSystems,PlayableEntityKind.Tank,AiResearchEffect.GlobalDamage),
            new AiResearchDescriptor(PlayableResearchKind.ImprovedDefensiveSystems,PlayableEntityKind.Tank,AiResearchEffect.GlobalHealth),
            new AiResearchDescriptor(PlayableResearchKind.AdvancedDefensiveSystems,PlayableEntityKind.Tank,AiResearchEffect.GlobalHealth)});
        public static AiResearchDescriptor For(PlayableResearchKind kind)=>All.Single(d=>d.Kind==kind);
    }
}
