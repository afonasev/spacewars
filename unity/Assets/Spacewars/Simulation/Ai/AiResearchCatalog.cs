using System;
using System.Collections.Generic;
using System.Linq;
namespace Spacewars.Simulation.Ai
{
    public enum AiResearchEffect { Mobility, BurstDamage, AttackRange }
    public sealed class AiResearchDescriptor
    {
        internal AiResearchDescriptor(PlayableResearchKind kind,PlayableEntityKind unit,AiResearchEffect effect){Kind=kind;Unit=unit;Effect=effect;}
        public PlayableResearchKind Kind{get;} public PlayableEntityKind Unit{get;} public AiResearchEffect Effect{get;}
        public PlayableBuildingKind Prerequisite=>PlayableBuildingKind.ScientificCenter;
        public double Cost(PlayableProfile p)=>Kind==PlayableResearchKind.TankChassis?p.TankChassisCost:Kind==PlayableResearchKind.ExplorerAssaultGuns?p.ExplorerAssaultCost:p.ShkvalGuidanceCost;
        public double Duration(PlayableProfile p)=>Kind==PlayableResearchKind.TankChassis?p.TankChassisSeconds:Kind==PlayableResearchKind.ExplorerAssaultGuns?p.ExplorerAssaultSeconds:p.ShkvalGuidanceSeconds;
        public double Before(PlayableProfile p)=>Effect==AiResearchEffect.Mobility?PlayableUnitRules.Speed(p,Unit):Effect==AiResearchEffect.BurstDamage?PlayableUnitRules.DamagePerSecond(p,Unit):PlayableUnitRules.Range(p,Unit);
        public double After(PlayableProfile p)=>Effect==AiResearchEffect.Mobility?PlayableUnitRules.Speed(p,Unit,true):Effect==AiResearchEffect.BurstDamage?PlayableUnitRules.DamagePerSecond(p,Unit,true):PlayableUnitRules.Range(p,Unit,true);
        public double RelativeGain(PlayableProfile p)=>Math.Max(0,After(p)/Before(p)-1);
    }
    // All ordinary QueueResearch branches, with gameplay effects and terms. No AI balance constants.
    public static class AiResearchCatalog
    {
        public static IReadOnlyList<AiResearchDescriptor> All{get;}=Array.AsReadOnly(new[]{
            new AiResearchDescriptor(PlayableResearchKind.TankChassis,PlayableEntityKind.Tank,AiResearchEffect.Mobility),
            new AiResearchDescriptor(PlayableResearchKind.ExplorerAssaultGuns,PlayableEntityKind.Explorer,AiResearchEffect.BurstDamage),
            new AiResearchDescriptor(PlayableResearchKind.ShkvalGuidance,PlayableEntityKind.Shkval,AiResearchEffect.AttackRange)});
        public static AiResearchDescriptor For(PlayableResearchKind kind)=>All.Single(d=>d.Kind==kind);
    }
}
