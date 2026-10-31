using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    // Shared gameplay/UI dispatch. All tuning remains in the named Balance Lab profile.
    public static class PlayableResearchRules
    {
        public const string ArmySystemsProfileId="army-systems-research-v3";
        public const string ReinforcedConcreteProfileId="reinforced-concrete-v2";
        public const string HawkSalvoProfileId="hawk-salvo-research-v1";
        public static PlayableBuildingKind? Producer(PlayableResearchKind k)=>k==PlayableResearchKind.HawkSalvoSystems?PlayableBuildingKind.Hangar:
            k==PlayableResearchKind.TankChassis||k==PlayableResearchKind.ExplorerAssaultGuns||k==PlayableResearchKind.ShkvalGuidance?(PlayableBuildingKind?)PlayableBuildingKind.Factory:null;
        public static string ProducerRequirement(PlayableResearchKind k)=>Producer(k)==PlayableBuildingKind.Hangar?"Нужен готовый собственный ангар":Producer(k)==PlayableBuildingKind.Factory?"Нужна готовая собственная фабрика":null;
        public static bool Global(PlayableResearchKind k)=>Weapon(k)||Defense(k);
        public static bool Weapon(PlayableResearchKind k)=>k==PlayableResearchKind.ImprovedWeaponSystems||k==PlayableResearchKind.AdvancedWeaponSystems||k==PlayableResearchKind.UltimateWeaponSystems;
        public static bool Defense(PlayableResearchKind k)=>k==PlayableResearchKind.ImprovedDefensiveSystems||k==PlayableResearchKind.AdvancedDefensiveSystems||k==PlayableResearchKind.UltimateDefensiveSystems;
        public static bool UpgradedCenterReady(IEnumerable<PlayableBuildingSnapshot> buildings,PlayableOwner owner)=>buildings.Any(b=>b.Owner==owner&&b.Kind==PlayableBuildingKind.ScientificCenter&&b.Phase==ConstructionPhase.Ready&&b.Health>0&&b.PrivateState?.Lifecycle?.Selling!=true&&b.ScientificCenterUpgraded);
        public static bool Local(PlayableResearchKind k)=>k==PlayableResearchKind.ScientificCenterUpgrade;
        public static int Level(PlayableResearchKind k)=>!Global(k)?0:k==PlayableResearchKind.UltimateWeaponSystems||k==PlayableResearchKind.UltimateDefensiveSystems?3:Previous(k).HasValue?2:1;
        public static bool Concrete(PlayableResearchKind k)=>k==PlayableResearchKind.ReinforcedConcrete||k==PlayableResearchKind.AdvancedReinforcedConcrete;
        public static double BuildingBonus(PlayableProfile p,IEnumerable<PlayableResearchOrderSnapshot> orders)=>orders.Any(o=>o.Complete&&o.Kind==PlayableResearchKind.AdvancedReinforcedConcrete)?p.ReinforcedConcreteLevel2BonusPercent:orders.Any(o=>o.Complete&&o.Kind==PlayableResearchKind.ReinforcedConcrete)?p.ReinforcedConcreteBonusPercent:0;
        public static bool NeedsUpgradedCenter(PlayableResearchKind k)=>Level(k)>=2||k==PlayableResearchKind.AdvancedReinforcedConcrete;
        public static string CenterRequirement(PlayableResearchKind k,bool upgraded)=>NeedsUpgradedCenter(k)&&!upgraded?"Нужен улучшенный научный центр":null;
        public static PlayableResearchKind? Previous(PlayableResearchKind k)=>k==PlayableResearchKind.AdvancedReinforcedConcrete?PlayableResearchKind.ReinforcedConcrete:k==PlayableResearchKind.UltimateWeaponSystems?PlayableResearchKind.AdvancedWeaponSystems:k==PlayableResearchKind.UltimateDefensiveSystems?PlayableResearchKind.AdvancedDefensiveSystems:k==PlayableResearchKind.AdvancedWeaponSystems?PlayableResearchKind.ImprovedWeaponSystems:k==PlayableResearchKind.AdvancedDefensiveSystems?(PlayableResearchKind?)PlayableResearchKind.ImprovedDefensiveSystems:null;
        public static bool Prerequisite(PlayableResearchKind k,IEnumerable<PlayableResearchOrderSnapshot> orders)=>!Previous(k).HasValue||orders.Any(r=>r.Kind==Previous(k).Value&&r.Complete);
        public static double Cost(PlayableProfile p,PlayableResearchKind k)=>k switch {
            PlayableResearchKind.ScientificCenterUpgrade=>p.ScientificCenterUpgradeCost,PlayableResearchKind.UltimateWeaponSystems=>p.WeaponSystemsLevel3Cost,PlayableResearchKind.UltimateDefensiveSystems=>p.DefensiveSystemsLevel3Cost,
            PlayableResearchKind.AdvancedReinforcedConcrete=>p.ReinforcedConcreteLevel2Cost,PlayableResearchKind.ReinforcedConcrete=>p.ReinforcedConcreteCost,PlayableResearchKind.HawkSalvoSystems=>p.HawkSalvoResearchCost,PlayableResearchKind.TankChassis=>p.TankChassisCost,PlayableResearchKind.ExplorerAssaultGuns=>p.ExplorerAssaultCost,PlayableResearchKind.ShkvalGuidance=>p.ShkvalGuidanceCost,
            PlayableResearchKind.ImprovedWeaponSystems=>p.WeaponSystemsLevel1Cost,PlayableResearchKind.AdvancedWeaponSystems=>p.WeaponSystemsLevel2Cost,
            PlayableResearchKind.ImprovedDefensiveSystems=>p.DefensiveSystemsLevel1Cost,PlayableResearchKind.AdvancedDefensiveSystems=>p.DefensiveSystemsLevel2Cost,_=>throw new ArgumentException("Unknown research")};
        public static double Duration(PlayableProfile p,PlayableResearchKind k)=>k switch {
            PlayableResearchKind.ScientificCenterUpgrade=>p.ScientificCenterUpgradeSeconds,PlayableResearchKind.UltimateWeaponSystems=>p.WeaponSystemsLevel3Seconds,PlayableResearchKind.UltimateDefensiveSystems=>p.DefensiveSystemsLevel3Seconds,
            PlayableResearchKind.AdvancedReinforcedConcrete=>p.ReinforcedConcreteLevel2Seconds,PlayableResearchKind.ReinforcedConcrete=>p.ReinforcedConcreteSeconds,PlayableResearchKind.HawkSalvoSystems=>p.HawkSalvoResearchSeconds,PlayableResearchKind.TankChassis=>p.TankChassisSeconds,PlayableResearchKind.ExplorerAssaultGuns=>p.ExplorerAssaultSeconds,PlayableResearchKind.ShkvalGuidance=>p.ShkvalGuidanceSeconds,
            PlayableResearchKind.ImprovedWeaponSystems=>p.WeaponSystemsLevel1Seconds,PlayableResearchKind.AdvancedWeaponSystems=>p.WeaponSystemsLevel2Seconds,
            PlayableResearchKind.ImprovedDefensiveSystems=>p.DefensiveSystemsLevel1Seconds,PlayableResearchKind.AdvancedDefensiveSystems=>p.DefensiveSystemsLevel2Seconds,_=>throw new ArgumentException("Unknown research")};
        public static double Bonus(PlayableProfile p,PlayableResearchKind k)=>k switch {
            PlayableResearchKind.ReinforcedConcrete=>p.ReinforcedConcreteBonusPercent,PlayableResearchKind.AdvancedReinforcedConcrete=>p.ReinforcedConcreteLevel2BonusPercent,
            PlayableResearchKind.UltimateWeaponSystems=>p.WeaponSystemsLevel3BonusPercent,PlayableResearchKind.UltimateDefensiveSystems=>p.DefensiveSystemsLevel3BonusPercent,
            PlayableResearchKind.ImprovedWeaponSystems=>p.WeaponSystemsLevel1BonusPercent,PlayableResearchKind.AdvancedWeaponSystems=>p.WeaponSystemsLevel2BonusPercent,
            PlayableResearchKind.ImprovedDefensiveSystems=>p.DefensiveSystemsLevel1BonusPercent,PlayableResearchKind.AdvancedDefensiveSystems=>p.DefensiveSystemsLevel2BonusPercent,_=>0};
        public static string Name(PlayableResearchKind k)=>k switch {
            PlayableResearchKind.ScientificCenterUpgrade=>"Улучшить научный центр",PlayableResearchKind.UltimateWeaponSystems=>"Совершенные оружейные системы",PlayableResearchKind.UltimateDefensiveSystems=>"Совершенные защитные системы",
            PlayableResearchKind.ReinforcedConcrete=>"Усиленный бетон I",PlayableResearchKind.AdvancedReinforcedConcrete=>"Усиленный бетон II",PlayableResearchKind.HawkSalvoSystems=>"Системы залпового огня",PlayableResearchKind.TankChassis=>"Шасси",PlayableResearchKind.ExplorerAssaultGuns=>"Штурмовые орудия",PlayableResearchKind.ShkvalGuidance=>"Наведение",
            PlayableResearchKind.ImprovedWeaponSystems=>"Улучшенные оружейные системы",PlayableResearchKind.AdvancedWeaponSystems=>"Передовые оружейные системы",
            PlayableResearchKind.ImprovedDefensiveSystems=>"Улучшенные защитные системы",PlayableResearchKind.AdvancedDefensiveSystems=>"Передовые защитные системы",_=>throw new ArgumentException("Unknown research")};
        public static string RefineryDescription(PlayableProfile p)=>"Доход: "+p.RefineryUpgradedIncome.ToString("0")+" за "+p.IncomePeriodSeconds+" с";
        public static string Description(PlayableProfile p,PlayableResearchKind k)=>Local(k)?"Открывает уровни II и III оружейных и защитных систем и бетон II в этом центре.":Concrete(k)?"Максимальное здоровье всех зданий +"+Bonus(p,k).ToString("0.#")+"% суммарно. Сохраняет процент текущего здоровья. Действует на существующие и будущие здания.":k==PlayableResearchKind.HawkSalvoSystems?"Ускоряет подготовку, пуск второй ракеты и перезарядку всех Ястребов. Добавляет доводку ракет по удаляющейся цели. Количество ракет не меняется.":Global(k)?(Weapon(k)?"Урон всех юнитов":"Максимальное здоровье всех юнитов")+" +"+Bonus(p,k).ToString("0.#")+"% суммарно.":k==PlayableResearchKind.TankChassis?"Улучшает ходовые характеристики танка.":k==PlayableResearchKind.ExplorerAssaultGuns?"Усиливает вооружение Исследователя.":"Улучшает наведение ракет Шквала.";
    }
}
