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
            k==PlayableResearchKind.ExplorerRocketLauncher||k==PlayableResearchKind.TankChassis||k==PlayableResearchKind.ExplorerAssaultGuns||k==PlayableResearchKind.ShkvalGuidance?(PlayableBuildingKind?)PlayableBuildingKind.Factory:null;
        public static string ProducerRequirement(PlayableResearchKind k)=>Producer(k)==PlayableBuildingKind.Hangar?"Нужен готовый собственный ангар":Producer(k)==PlayableBuildingKind.Factory?"Нужна готовая собственная фабрика":null;
        public static bool Global(PlayableResearchKind k)=>Weapon(k)||Defense(k);
        public static bool Weapon(PlayableResearchKind k)=>k==PlayableResearchKind.ImprovedWeaponSystems||k==PlayableResearchKind.AdvancedWeaponSystems||k==PlayableResearchKind.UltimateWeaponSystems;
        public static bool Defense(PlayableResearchKind k)=>k==PlayableResearchKind.ImprovedDefensiveSystems||k==PlayableResearchKind.AdvancedDefensiveSystems||k==PlayableResearchKind.UltimateDefensiveSystems;
        public static bool UpgradedCenterReady(IEnumerable<PlayableBuildingSnapshot> buildings,PlayableOwner owner)=>buildings.Any(b=>b.Owner==owner&&b.Kind==PlayableBuildingKind.ScientificCenter&&b.Phase==ConstructionPhase.Ready&&b.Health>0&&b.PrivateState?.Lifecycle?.Selling!=true&&b.ScientificCenterUpgraded);
        public static bool Local(PlayableResearchKind k)=>k==PlayableResearchKind.ScientificCenterUpgrade;
        public static int Level(PlayableResearchKind k)=>!Global(k)?0:k==PlayableResearchKind.UltimateWeaponSystems||k==PlayableResearchKind.UltimateDefensiveSystems?3:Previous(k).HasValue?2:1;
        public static bool Concrete(PlayableResearchKind k)=>k==PlayableResearchKind.ReinforcedConcrete||k==PlayableResearchKind.AdvancedReinforcedConcrete;
        public static double BuildingBonus(PlayableProfile p,IEnumerable<PlayableResearchOrderSnapshot> orders)=>orders.Any(o=>o.Complete&&o.Kind==PlayableResearchKind.AdvancedReinforcedConcrete)?p.ReinforcedConcreteLevel2BonusPercent:orders.Any(o=>o.Complete&&o.Kind==PlayableResearchKind.ReinforcedConcrete)?p.ReinforcedConcreteBonusPercent:0;
        public static bool NeedsUpgradedCenter(PlayableResearchKind k)=>k==PlayableResearchKind.ExplorerRocketLauncher||Level(k)>=2||k==PlayableResearchKind.AdvancedReinforcedConcrete;
        public static string CenterRequirement(PlayableResearchKind k,bool upgraded)=>NeedsUpgradedCenter(k)&&!upgraded?"Нужен улучшенный научный центр":null;
        public static PlayableResearchKind? Previous(PlayableResearchKind k)=>k==PlayableResearchKind.ExplorerRocketLauncher?PlayableResearchKind.ExplorerAssaultGuns:k==PlayableResearchKind.AdvancedReinforcedConcrete?PlayableResearchKind.ReinforcedConcrete:k==PlayableResearchKind.UltimateWeaponSystems?PlayableResearchKind.AdvancedWeaponSystems:k==PlayableResearchKind.UltimateDefensiveSystems?PlayableResearchKind.AdvancedDefensiveSystems:k==PlayableResearchKind.AdvancedWeaponSystems?PlayableResearchKind.ImprovedWeaponSystems:k==PlayableResearchKind.AdvancedDefensiveSystems?(PlayableResearchKind?)PlayableResearchKind.ImprovedDefensiveSystems:null;
        public static bool Prerequisite(PlayableResearchKind k,IEnumerable<PlayableResearchOrderSnapshot> orders)=>!Previous(k).HasValue||orders.Any(r=>r.Kind==Previous(k).Value&&r.Complete);
        public static double Cost(PlayableProfile p,PlayableResearchKind k)=>k switch {
            PlayableResearchKind.ExplorerRocketLauncher=>p.ExplorerRocketCost,
            PlayableResearchKind.ScientificCenterUpgrade=>p.ScientificCenterUpgradeCost,PlayableResearchKind.UltimateWeaponSystems=>p.WeaponSystemsLevel3Cost,PlayableResearchKind.UltimateDefensiveSystems=>p.DefensiveSystemsLevel3Cost,
            PlayableResearchKind.AdvancedReinforcedConcrete=>p.ReinforcedConcreteLevel2Cost,PlayableResearchKind.ReinforcedConcrete=>p.ReinforcedConcreteCost,PlayableResearchKind.HawkSalvoSystems=>p.HawkSalvoResearchCost,PlayableResearchKind.TankChassis=>p.TankChassisCost,PlayableResearchKind.ExplorerAssaultGuns=>p.ExplorerAssaultCost,PlayableResearchKind.ShkvalGuidance=>p.ShkvalGuidanceCost,
            PlayableResearchKind.ImprovedWeaponSystems=>p.WeaponSystemsLevel1Cost,PlayableResearchKind.AdvancedWeaponSystems=>p.WeaponSystemsLevel2Cost,
            PlayableResearchKind.ImprovedDefensiveSystems=>p.DefensiveSystemsLevel1Cost,PlayableResearchKind.AdvancedDefensiveSystems=>p.DefensiveSystemsLevel2Cost,_=>throw new ArgumentException("Unknown research")};
        public static double Duration(PlayableProfile p,PlayableResearchKind k)=>k switch {
            PlayableResearchKind.ExplorerRocketLauncher=>p.ExplorerRocketSeconds,
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
            PlayableResearchKind.ExplorerRocketLauncher=>"Ракетная установка",
            PlayableResearchKind.ScientificCenterUpgrade=>"Улучшить научный центр",PlayableResearchKind.UltimateWeaponSystems=>"Совершенные оружейные системы",PlayableResearchKind.UltimateDefensiveSystems=>"Совершенные защитные системы",
            PlayableResearchKind.ReinforcedConcrete=>"Усиленный бетон I",PlayableResearchKind.AdvancedReinforcedConcrete=>"Усиленный бетон II",PlayableResearchKind.HawkSalvoSystems=>"Системы залпового огня",PlayableResearchKind.TankChassis=>"Улучшенное шасси",PlayableResearchKind.ExplorerAssaultGuns=>"Штурмовые орудия",PlayableResearchKind.ShkvalGuidance=>"Дальнобойное наведение",
            PlayableResearchKind.ImprovedWeaponSystems=>"Улучшенные оружейные системы",PlayableResearchKind.AdvancedWeaponSystems=>"Передовые оружейные системы",
            PlayableResearchKind.ImprovedDefensiveSystems=>"Улучшенные защитные системы",PlayableResearchKind.AdvancedDefensiveSystems=>"Передовые защитные системы",_=>throw new ArgumentException("Unknown research")};
        public static string RefineryDescription(PlayableProfile p)=>"Доход: "+p.RefineryUpgradedIncome.ToString("0")+" за "+p.IncomePeriodSeconds+" с";
        public static string Description(PlayableProfile p,PlayableResearchKind k)=>k switch {
            PlayableResearchKind.ExplorerRocketLauncher=>"Исследователей вооружают лёгкими ракетами с автонаведением по воздушным целям",
            PlayableResearchKind.TankChassis=>"В затяжных кампаниях Вепрям приходится действовать вдали от ремонтных баз. Усиленное шасси помогает танку держать удар и быстрее менять позицию.",
            PlayableResearchKind.ExplorerAssaultGuns=>"Когда колониальная полиция действует вместе с армией, патрульные машины перевооружают. Исследователь получает штурмовое орудие с более длинной очередью.",
            PlayableResearchKind.ShkvalGuidance=>"Для позиционных боёв «Шквал» получает новый комплекс наведения. Он достаёт более дальние позиции и бьёт по большей площади.",
            PlayableResearchKind.HawkSalvoSystems=>"В воздушном бою считанные секунды отделяют победу от гибели. Новая система помогает Ястребу быстрее открыть огонь после остановки и сократить интервал между ракетами.",
            PlayableResearchKind.ImprovedWeaponSystems=>"Полевые оружейники дорабатывают штатное вооружение по опыту первых боёв. Удары армии становятся сильнее.",
            PlayableResearchKind.AdvancedWeaponSystems=>"Когда спор за месторождение перерастает в затяжную войну, армия переходит на передовое вооружение. Оно наносит больше урона.",
            PlayableResearchKind.UltimateWeaponSystems=>"Во время крупных кампаний корпорации отправляют на фронт разработки из закрытых арсеналов. Они дают армии максимальную прибавку к урону.",
            PlayableResearchKind.ImprovedDefensiveSystems=>"После первых столкновений ремонтные бригады усиливают уязвимые места техники. Машины и авиация выдерживают больше повреждений.",
            PlayableResearchKind.AdvancedDefensiveSystems=>"Для долгих операций вдали от мастерских корпуса и узлы техники защищают по более строгому стандарту. Войска дольше держатся под огнём.",
            PlayableResearchKind.UltimateDefensiveSystems=>"Самую стойкую защиту корпорации разрабатывают для тех, кто первым входит в зону обстрела. После внедрения вся армия выдерживает больше повреждений.",
            PlayableResearchKind.ReinforcedConcrete=>"На спорных территориях колонии строят из усиленного бетона. Стены и опоры всех построек лучше выдерживают обстрел.",
            PlayableResearchKind.AdvancedReinforcedConcrete=>"Бетон для укреплений, рассчитанных на долгую осаду. После внедрения все здания выдерживают более тяжёлый обстрел.",
            PlayableResearchKind.ScientificCenterUpgrade=>"Полевой центр получает стенды для испытаний, которые раньше проводили вдали от фронта.",
            _=>throw new ArgumentException("Unknown research")};
    }
}
