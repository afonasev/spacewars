using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    // Shared gameplay/UI dispatch. All tuning remains in the named Balance Lab profile.
    public static class PlayableResearchRules
    {
        public const string ArmySystemsProfileId="army-systems-research-v1";
        public const string HawkSalvoProfileId="hawk-salvo-research-v1";
        public static PlayableBuildingKind? Producer(PlayableResearchKind k)=>k==PlayableResearchKind.HawkSalvoSystems?PlayableBuildingKind.Hangar:
            k==PlayableResearchKind.TankChassis||k==PlayableResearchKind.ExplorerAssaultGuns||k==PlayableResearchKind.ShkvalGuidance?(PlayableBuildingKind?)PlayableBuildingKind.Factory:null;
        public static string ProducerRequirement(PlayableResearchKind k)=>Producer(k)==PlayableBuildingKind.Hangar?"Нужен готовый собственный ангар":Producer(k)==PlayableBuildingKind.Factory?"Нужна готовая собственная фабрика":null;
        public static bool Global(PlayableResearchKind k)=>Weapon(k)||Defense(k);
        public static bool Weapon(PlayableResearchKind k)=>k==PlayableResearchKind.ImprovedWeaponSystems||k==PlayableResearchKind.AdvancedWeaponSystems;
        public static bool Defense(PlayableResearchKind k)=>k==PlayableResearchKind.ImprovedDefensiveSystems||k==PlayableResearchKind.AdvancedDefensiveSystems;
        public static PlayableResearchKind? Previous(PlayableResearchKind k)=>k==PlayableResearchKind.AdvancedWeaponSystems?PlayableResearchKind.ImprovedWeaponSystems:k==PlayableResearchKind.AdvancedDefensiveSystems?(PlayableResearchKind?)PlayableResearchKind.ImprovedDefensiveSystems:null;
        public static bool Prerequisite(PlayableResearchKind k,IEnumerable<PlayableResearchOrderSnapshot> orders)=>!Previous(k).HasValue||orders.Any(r=>r.Kind==Previous(k).Value&&r.Complete);
        public static double Cost(PlayableProfile p,PlayableResearchKind k)=>k switch {
            PlayableResearchKind.HawkSalvoSystems=>p.HawkSalvoResearchCost,PlayableResearchKind.TankChassis=>p.TankChassisCost,PlayableResearchKind.ExplorerAssaultGuns=>p.ExplorerAssaultCost,PlayableResearchKind.ShkvalGuidance=>p.ShkvalGuidanceCost,
            PlayableResearchKind.ImprovedWeaponSystems=>p.WeaponSystemsLevel1Cost,PlayableResearchKind.AdvancedWeaponSystems=>p.WeaponSystemsLevel2Cost,
            PlayableResearchKind.ImprovedDefensiveSystems=>p.DefensiveSystemsLevel1Cost,PlayableResearchKind.AdvancedDefensiveSystems=>p.DefensiveSystemsLevel2Cost,_=>throw new ArgumentException("Unknown research")};
        public static double Duration(PlayableProfile p,PlayableResearchKind k)=>k switch {
            PlayableResearchKind.HawkSalvoSystems=>p.HawkSalvoResearchSeconds,PlayableResearchKind.TankChassis=>p.TankChassisSeconds,PlayableResearchKind.ExplorerAssaultGuns=>p.ExplorerAssaultSeconds,PlayableResearchKind.ShkvalGuidance=>p.ShkvalGuidanceSeconds,
            PlayableResearchKind.ImprovedWeaponSystems=>p.WeaponSystemsLevel1Seconds,PlayableResearchKind.AdvancedWeaponSystems=>p.WeaponSystemsLevel2Seconds,
            PlayableResearchKind.ImprovedDefensiveSystems=>p.DefensiveSystemsLevel1Seconds,PlayableResearchKind.AdvancedDefensiveSystems=>p.DefensiveSystemsLevel2Seconds,_=>throw new ArgumentException("Unknown research")};
        public static double Bonus(PlayableProfile p,PlayableResearchKind k)=>k switch {
            PlayableResearchKind.ImprovedWeaponSystems=>p.WeaponSystemsLevel1BonusPercent,PlayableResearchKind.AdvancedWeaponSystems=>p.WeaponSystemsLevel2BonusPercent,
            PlayableResearchKind.ImprovedDefensiveSystems=>p.DefensiveSystemsLevel1BonusPercent,PlayableResearchKind.AdvancedDefensiveSystems=>p.DefensiveSystemsLevel2BonusPercent,_=>0};
        public static string Name(PlayableResearchKind k)=>k switch {
            PlayableResearchKind.HawkSalvoSystems=>"Системы залпового огня",PlayableResearchKind.TankChassis=>"Шасси",PlayableResearchKind.ExplorerAssaultGuns=>"Штурмовые орудия",PlayableResearchKind.ShkvalGuidance=>"Наведение",
            PlayableResearchKind.ImprovedWeaponSystems=>"Улучшенные оружейные системы",PlayableResearchKind.AdvancedWeaponSystems=>"Передовые оружейные системы",
            PlayableResearchKind.ImprovedDefensiveSystems=>"Улучшенные защитные системы",PlayableResearchKind.AdvancedDefensiveSystems=>"Передовые защитные системы",_=>throw new ArgumentException("Unknown research")};
        public static string Description(PlayableProfile p,PlayableResearchKind k)=>k==PlayableResearchKind.HawkSalvoSystems?"Ускоряет подготовку, пуск второй ракеты и перезарядку всех Ястребов. Количество ракет не меняется.":Global(k)?(Weapon(k)?"Урон всех юнитов":"Максимальное здоровье всех юнитов")+" +"+Bonus(p,k).ToString("0.#")+"% суммарно.":k==PlayableResearchKind.TankChassis?"Улучшает ходовые характеристики танка.":k==PlayableResearchKind.ExplorerAssaultGuns?"Усиливает вооружение Исследователя.":"Улучшает наведение ракет Шквала.";
    }
}
