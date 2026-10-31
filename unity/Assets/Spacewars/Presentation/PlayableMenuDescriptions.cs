using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Presentation
{
    // Presentation text reads the active profile and the owner's published research only.
    public static class PlayableMenuDescriptions
    {
        private static string N(double value)=>value.ToString("0.##",CultureInfo.CurrentCulture);
        public static bool RallyProducer(PlayableBuildingKind kind)=>kind==PlayableBuildingKind.Factory||kind==PlayableBuildingKind.Hangar;
        public static string Building(PlayableProfile p,PlayableBuildingKind kind,bool upgraded=false,bool freeEconomy=false,double? maxHealth=null)
        {
            string role=kind switch {
                PlayableBuildingKind.GaussTower=>"Тяжёлая оборона только по земле. Белый луч пробивает цели: после каждой остаётся "+N(p.GaussPierceRetention*100)+"% урона. Выстрел раз в "+N(p.GaussReloadSeconds)+" с; дальность "+N(p.GaussRange)+" м. Для строительства нужен готовый научный центр. Удерживает цель; ручная атака — ПКМ.",
                PlayableBuildingKind.RepairTower=>"Ремонтирует один свой наземный или воздушный юнит: "+N(p.RepairTowerHealthPerSecond)+" HP/с, включая бой. Удерживает цель, затем выбирает наименьший процент здоровья. Несколько вышек не лечат одну цель одновременно. Укреплённый бетон увеличивает здоровье вышки; оружейные улучшения не усиливают лечение.",
                PlayableBuildingKind.SentryTurret=>"Стационарный спаренный пулемёт против земли и воздуха. Удерживает цель; ручная атака — ПКМ. Оружейные системы усиливают урон, железобетон — HP.",
                PlayableBuildingKind.Headquarters=>"Главная база: приносит кредиты и открывает площадки для зданий. Потеря последнего центра означает поражение.",
                PlayableBuildingKind.Outpost=>"Закрепляет территорию, приносит кредиты и открывает площадки для зданий.",
                PlayableBuildingKind.Mine=>"Добывает ресурсы и приносит кредиты после постройки. Для строительства сначала захватите месторождение.",
                PlayableBuildingKind.Factory=>"Производит наземные войска: Исследователей, танки и Шквалы.",
                PlayableBuildingKind.Hangar=>"Производит Ястребов — боевые вертолёты с ракетным вооружением.",
                PlayableBuildingKind.Refinery=>"Приносит дополнительный доход. После постройки научного центра доступно улучшение дохода.",
                _=>"Открывает исследования, которые усиливают войска и здания владельца. Завершённые исследования сохраняются после потери центра."};
            if(freeEconomy&&kind==PlayableBuildingKind.Headquarters)role="Главная база: открывает площадки для зданий. Потеря последнего центра означает поражение.";
            if(freeEconomy&&kind==PlayableBuildingKind.Outpost)role="Закрепляет территорию и открывает площадки для зданий.";
            if(freeEconomy&&(kind==PlayableBuildingKind.Mine||kind==PlayableBuildingKind.Refinery))role="На полигоне действия бесплатны; экономика отключена.";
            double income=kind==PlayableBuildingKind.Refinery&&upgraded?p.RefineryUpgradedIncome:TerritoryRules.Income(p,kind);
            return OfflinePadWorldActions.Name(kind)+"\n"+role+"\n\n"+
                (!freeEconomy&&income>0?"Доход: +"+N(income)+" кредитов каждые "+N(p.IncomePeriodSeconds)+" с\n":"")+
                "Здоровье: "+N(maxHealth??TerritoryRules.Health(p,kind))+"\n"+(freeEconomy?"Бесплатно":"Стоимость: "+N(TerritoryRules.Cost(p,kind))+" кредитов")+"\nПостройка: "+N(freeEconomy?p.ProvingGroundActionSeconds:TerritoryRules.Duration(p,kind))+" с";
        }
        public static string[] DefenseStats(PlayableProfile p,PlayableBuildingKind kind,IEnumerable<PlayableResearchOrderSnapshot> research)
        {
            bool Complete(PlayableResearchKind r)=>research?.Any(o=>o.Complete&&o.Kind==r)==true;
            double multiplier=1+(Complete(PlayableResearchKind.UltimateWeaponSystems)?p.WeaponSystemsLevel3BonusPercent:Complete(PlayableResearchKind.AdvancedWeaponSystems)?p.WeaponSystemsLevel2BonusPercent:Complete(PlayableResearchKind.ImprovedWeaponSystems)?p.WeaponSystemsLevel1BonusPercent:0)/100;
            switch(kind)
            {
                case PlayableBuildingKind.SentryTurret:return new[]{
                    "Урон за попадание: "+N(p.TurretDamage*multiplier),"Очередь: "+N(p.TurretBurstSize)+" выстр.",
                    "Интервал: "+N(p.TurretBurstShotIntervalMs/1000d)+" с","Пауза: "+N(p.TurretBurstPauseMs/1000d)+" с",
                    "Дальность: "+N(p.TurretRange)+" м"};
                case PlayableBuildingKind.GaussTower:return new[]{
                    "Урон за попадание: "+N(p.GaussDamage*multiplier),"Перезарядка: "+N(p.GaussReloadSeconds)+" с",
                    "Дальность: "+N(p.GaussRange)+" м","Остаток урона: "+N(p.GaussPierceRetention*100)+"%"};
                case PlayableBuildingKind.RepairTower:return new[]{"Скорость ремонта: "+N(p.RepairTowerHealthPerSecond)+" HP/с"};
                default:return Array.Empty<string>();
            }
        }
        public static string Unit(PlayableProfile p,PlayableEntityKind kind,IEnumerable<PlayableResearchOrderSnapshot> research=null,PlayableEntitySnapshot entity=null,bool freeEconomy=false)
        {
            bool Complete(PlayableResearchKind r)=>research?.Any(o=>o.Kind==r&&o.Complete)==true;
            double Systems(bool weapon,int level)=>1+(level>=3?PlayableResearchRules.Bonus(p,weapon?PlayableResearchKind.UltimateWeaponSystems:PlayableResearchKind.UltimateDefensiveSystems):level>=2?PlayableResearchRules.Bonus(p,weapon?PlayableResearchKind.AdvancedWeaponSystems:PlayableResearchKind.AdvancedDefensiveSystems):level==1?PlayableResearchRules.Bonus(p,weapon?PlayableResearchKind.ImprovedWeaponSystems:PlayableResearchKind.ImprovedDefensiveSystems):0)/100;
            int Level(bool weapon)=>Complete(weapon?PlayableResearchKind.UltimateWeaponSystems:PlayableResearchKind.UltimateDefensiveSystems)?3:Complete(weapon?PlayableResearchKind.AdvancedWeaponSystems:PlayableResearchKind.AdvancedDefensiveSystems)?2:Complete(weapon?PlayableResearchKind.ImprovedWeaponSystems:PlayableResearchKind.ImprovedDefensiveSystems)?1:0;
            bool upgraded=entity?.Upgraded??Complete(kind==PlayableEntityKind.Tank?PlayableResearchKind.TankChassis:kind==PlayableEntityKind.Explorer?PlayableResearchKind.ExplorerAssaultGuns:PlayableResearchKind.ShkvalGuidance);
            bool salvo=entity?.Upgraded??Complete(PlayableResearchKind.HawkSalvoSystems);
            string role=kind switch {
                PlayableEntityKind.Explorer=>"Быстрый разведчик с автоматическим орудием. Разведывает карту и захватывает территории.",
                PlayableEntityKind.Tank=>"Бронированная боевая машина с пушкой. Ведёт прямой огонь и захватывает территории.",
                PlayableEntityKind.Shkval=>"Ракетная артиллерия для огневой поддержки с большой дистанции. Нуждается в прикрытии.",
                _=>"Боевой вертолёт с ракетным вооружением. Перелетает препятствия и атакует наземные цели; территории не захватывает."};
            double damage=kind==PlayableEntityKind.Tank?p.TankWeaponDamage:kind==PlayableEntityKind.Explorer?p.ExplorerDamage:kind==PlayableEntityKind.Shkval?p.ShkvalDamage:p.HawkDamage;
            damage*=Systems(true,entity?.WeaponSystemsLevel??Level(true));
            double health=entity==null?PlayableUnitRules.Health(p,kind)*Systems(false,Level(false)):PlayableUnitRules.MaximumHealth(p,entity);
            string fire=kind==PlayableEntityKind.Tank?"Перезарядка: "+N(p.TankWeaponReloadSeconds)+" с":kind==PlayableEntityKind.Explorer?
                "Выстрелов в очереди: "+N(upgraded?p.ExplorerAssaultBurstSize:p.ExplorerBurstSize)+"\nИнтервал выстрелов: "+N(p.ExplorerBurstShotIntervalMs/1000d)+" с\nПауза между очередями: "+N(p.ExplorerBurstPauseMs/1000d)+" с":kind==PlayableEntityKind.Shkval?
                "Интервал пусков: "+N(p.ShkvalFireIntervalMs/1000d)+" с":
                "Интервал ракет в залпе: "+N(salvo?p.HawkUpgradedSalvoDelay:p.HawkSalvoDelay)+" с\nПерезарядка: "+N(salvo?p.HawkUpgradedReloadSeconds:p.HawkReloadSeconds)+" с";
            return OfflinePadWorldActions.Name(kind)+"\n"+role+"\n\nЗдоровье: "+(entity==null?N(health):N(entity.ExactHealth??entity.Health)+" / "+N(health))+
                "\nУрон за попадание: "+N(damage)+"\n"+fire+
                "\nДальность огня: "+N(PlayableUnitRules.Range(p,kind,upgraded))+" м\nСкорость: "+N(PlayableUnitRules.Speed(p,kind,upgraded))+" м/с\nОбзор: "+N(PlayableUnitRules.Vision(p,kind))+" м"+
                "\n\n"+(freeEconomy?"Бесплатно":"Стоимость: "+N(PlayableUnitRules.Cost(p,kind))+" кредитов")+"\nПроизводство: "+N(freeEconomy?p.ProvingGroundActionSeconds:PlayableUnitRules.Duration(p,kind))+" с\nМесто в армии: "+N(PlayableUnitRules.Population(p,kind));
        }
    }
}
