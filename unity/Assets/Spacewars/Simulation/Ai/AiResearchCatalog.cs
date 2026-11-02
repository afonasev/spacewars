using System;
using System.Collections.Generic;
using System.Linq;
namespace Spacewars.Simulation.Ai
{
    public enum AiResearchEffect { Mobility, BurstDamage, AttackRange, GlobalDamage, GlobalHealth, BuildingDurability }
    public sealed class AiResearchDescriptor
    {
        internal AiResearchDescriptor(PlayableResearchKind kind,PlayableEntityKind unit,AiResearchEffect effect){Kind=kind;Unit=unit;Effect=effect;}
        public PlayableResearchKind Kind{get;} public PlayableEntityKind Unit{get;} public AiResearchEffect Effect{get;}
        public PlayableBuildingKind Prerequisite=>PlayableBuildingKind.ScientificCenter;
        public bool Global=>PlayableResearchRules.Global(Kind);
        public bool Buildings=>Effect==AiResearchEffect.BuildingDurability;
        public bool Affects(PlayableEntityKind kind)=>!Buildings&&(Global||Unit==kind);
        public double Cost(PlayableProfile p)=>PlayableResearchRules.Cost(p,Kind);
        public double Duration(PlayableProfile p)=>PlayableResearchRules.Duration(p,Kind);
        public double Before(PlayableProfile p)=>Buildings?1:Global?1+(PlayableResearchRules.Previous(Kind).HasValue?PlayableResearchRules.Bonus(p,PlayableResearchRules.Previous(Kind).Value)/100:0):Effect==AiResearchEffect.Mobility?PlayableUnitRules.Speed(p,Unit):Effect==AiResearchEffect.BurstDamage?PlayableUnitRules.DamagePerSecond(p,Unit):PlayableUnitRules.Range(p,Unit);
        public double After(PlayableProfile p)=>Buildings?1+p.ReinforcedConcreteBonusPercent/100:Global?1+PlayableResearchRules.Bonus(p,Kind)/100:Effect==AiResearchEffect.Mobility?PlayableUnitRules.Speed(p,Unit,true):Effect==AiResearchEffect.BurstDamage?PlayableUnitRules.DamagePerSecond(p,Unit,true):PlayableUnitRules.Range(p,Unit,true);
        public double RelativeGain(PlayableProfile p)
        {
            if(Buildings)return p.ReinforcedConcreteBonusPercent/100;
            double ratio=After(p)/Before(p);
            if(Kind==PlayableResearchKind.TankChassis)ratio*=1+p.TankChassisHealthBonusPercent/100;
            if(Kind==PlayableResearchKind.ShkvalGuidance)
            {
                // Geometric area is a payoff proxy for the existing splash effect,
                // not extra direct-hit damage. Pi cancels in the same-profile ratio.
                double radius=PlayableUnitRules.ShkvalBlastRadius(p,true)/PlayableUnitRules.ShkvalBlastRadius(p,false);
                ratio*=radius*radius;
            }
            return Math.Max(0,ratio-1);
        }
    }
    // Military research choices retain their unit eligibility. Building durability
    // is a separate infrastructure investment with its own beneficiary calculation.
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
        // Building durability has no unit beneficiary or military size gate. Keep it
        // outside All so military roster/eligibility consumers cannot classify it as Tank.
        public static AiResearchDescriptor BuildingDurability{get;}=new AiResearchDescriptor(PlayableResearchKind.ReinforcedConcrete,default,AiResearchEffect.BuildingDurability);
        public static AiResearchDescriptor For(PlayableResearchKind kind)=>kind==PlayableResearchKind.ReinforcedConcrete?BuildingDurability:All.Single(d=>d.Kind==kind);
    }
}
