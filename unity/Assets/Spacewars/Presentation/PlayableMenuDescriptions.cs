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
        internal const string RelayDescription="Через такие ретрансляторы дальние колонии держат связь. Доход от узла получает тот, кто его контролирует. Вышку можно передать союзнику.";
        private static string N(double value)=>value.ToString("0.##",CultureInfo.CurrentCulture);
        public static bool RallyProducer(PlayableBuildingKind kind)=>kind==PlayableBuildingKind.Factory||kind==PlayableBuildingKind.Hangar;
        public static string Building(PlayableProfile p,PlayableBuildingKind kind,bool upgraded=false,bool freeEconomy=false,double? maxHealth=null)
        {
            string role=kind switch {
                PlayableBuildingKind.GaussTower=>"Тяжёлая башня для обороны узких проходов. Её луч пробивает несколько наземных целей подряд, теряя мощность с каждым попаданием.",
                PlayableBuildingKind.RepairTower=>"Стационарный ремонтный пост для баз под обстрелом. Чинит машины, авиацию и здания по одной цели за раз.",
                PlayableBuildingKind.SentryTurret=>"Стандартная охрана колониальных объектов. Спаренный пулемёт прикрывает периметр от наземных рейдов и налётов с воздуха.",
                PlayableBuildingKind.Headquarters=>"Отсюда корпорация управляет операцией и разворачивает базу. Штаб приносит доход. С потерей последнего центра кампания заканчивается.",
                PlayableBuildingKind.Outpost=>"Такие посты ставят на спорной территории вдали от штаба. Форпост приносит доход и даёт место под строительство новой базы.",
                PlayableBuildingKind.Mine=>"За право поставить такую шахту корпорации ведут войны. На захваченном месторождении она добывает сырьё и приносит доход. Запас постепенно истощается, и после его исчерпания доход падает.",
                PlayableBuildingKind.Factory=>"Сборочные цеха для колоний, которым далеко до верфей снабжения. Выпускают патрульных Исследователей, танки «Вепрь» и ракетные установки «Шквал».",
                PlayableBuildingKind.Hangar=>"На дальних рубежах ангар заменяет аэродром. Здесь собирают ударные вертолёты «Ястреб» и ремонтные дроны «Скат».",
                PlayableBuildingKind.Refinery=>"Готовит сырьё к отправке на экспорт. После постройки научного центра завод можно модернизировать, чтобы увеличить доход.",
                PlayableBuildingKind.RelayTower=>RelayDescription,
                _=>"Полевые лаборатории дорабатывают оружие и броню под текущие реалии на поле боя и тактическую обстановку. Войска продолжают применять готовые разработки даже после потери центра."};
            if(freeEconomy&&kind==PlayableBuildingKind.Headquarters)role="Главная база: открывает площадки для зданий. Потеря последнего центра означает поражение.";
            if(freeEconomy&&kind==PlayableBuildingKind.Outpost)role="Закрепляет территорию и открывает площадки для зданий.";
            if(freeEconomy&&(kind==PlayableBuildingKind.Mine||kind==PlayableBuildingKind.Refinery))role="На полигоне действия бесплатны; экономика отключена.";
            double income=kind==PlayableBuildingKind.Refinery&&upgraded?p.RefineryUpgradedIncome:TerritoryRules.Income(p,kind);
            return OfflinePadWorldActions.Name(kind)+"\n"+role+"\n\n"+
                (!freeEconomy&&income>0?"Доход: +"+N(income)+" кредитов каждые "+N(p.IncomePeriodSeconds)+" с\n":"")+
                "Здоровье: "+N(maxHealth??PlayableStatRounding.MaximumHealth(p,TerritoryRules.Health(p,kind)))+"\n"+(freeEconomy?"Бесплатно":"Стоимость: "+N(TerritoryRules.Cost(p,kind))+" кредитов")+"\nПостройка: "+N(freeEconomy?p.ProvingGroundActionSeconds:TerritoryRules.Duration(p,kind))+" с";
        }
        public static string[] DefenseStats(PlayableProfile p,PlayableBuildingKind kind,IEnumerable<PlayableResearchOrderSnapshot> research)
        {
            bool Complete(PlayableResearchKind r)=>research?.Any(o=>o.Complete&&o.Kind==r)==true;
            double multiplier=1+(Complete(PlayableResearchKind.UltimateWeaponSystems)?p.WeaponSystemsLevel3BonusPercent:Complete(PlayableResearchKind.AdvancedWeaponSystems)?p.WeaponSystemsLevel2BonusPercent:Complete(PlayableResearchKind.ImprovedWeaponSystems)?p.WeaponSystemsLevel1BonusPercent:0)/100;
            switch(kind)
            {
                case PlayableBuildingKind.SentryTurret:return new[]{
                    "Урон за попадание: "+N(PlayableStatRounding.Damage(p,p.TurretDamage*multiplier)),"Очередь: "+N(p.TurretBurstSize)+" выстр.",
                    "Интервал: "+N(p.TurretBurstShotIntervalMs/1000d)+" с","Пауза: "+N(p.TurretBurstPauseMs/1000d)+" с",
                    "Дальность: "+N(p.TurretRange)+" м"};
                case PlayableBuildingKind.GaussTower:return new[]{
                    "Урон за попадание: "+N(PlayableStatRounding.Damage(p,p.GaussDamage*multiplier)),"Перезарядка: "+N(p.GaussReloadSeconds)+" с",
                    "Дальность: "+N(p.GaussRange)+" м","Остаток урона: "+N(p.GaussPierceRetention*100)+"%"};
                case PlayableBuildingKind.RepairTower:return new[]{"Скорость ремонта: "+N(p.RepairTowerHealthPerSecond)+" HP/с","Дальность ремонта: "+N(p.RepairTowerRange)+" м"};
                default:return Array.Empty<string>();
            }
        }
        internal static (string Name,string Description,string[] Stats) LocalBuildingUpgrade(
            PlayableProfile p,PlayableBuildingKind kind)
            =>kind==PlayableBuildingKind.Refinery
                ?("Улучшение завода","Увеличивает доход завода переработки.",
                    new[]{"Доход: "+N(p.RefineryIncomePerPeriod)+" → "+N(p.RefineryUpgradedIncome),
                        "Период: "+N(p.IncomePeriodSeconds)+" с"})
                :("Улучшение: "+OfflinePadWorldActions.Name(kind),"Улучшение в общей очереди производства.",
                    new[]{"Скорость: ×"+N(p.UpgradedProducerSpeedMultiplier)});

        internal static string[] ResearchCardStats(PlayableProfile p,PlayableResearchKind kind,
            IEnumerable<PlayableResearchOrderSnapshot> research,bool freeEconomy=false)
            =>ResearchStats(p,kind,research).Append("Исследование: "+
                N(freeEconomy?p.ProvingGroundActionSeconds:PlayableResearchRules.Duration(p,kind))+" с").ToArray();

        internal static string ResearchDetail(PlayableProfile p,PlayableResearchKind kind,IEnumerable<PlayableResearchOrderSnapshot> research)
            =>PlayableResearchRules.Description(p,kind)+"\n"+string.Join("\n",ResearchStats(p,kind,research));

        internal static string[] ResearchStats(PlayableProfile p,PlayableResearchKind kind,IEnumerable<PlayableResearchOrderSnapshot> research)
        {
            string Change(string label,double before,double after,string unit="")
                =>label+": "+N(before)+" → "+N(after)+unit;
            if(PlayableResearchRules.Global(kind)||PlayableResearchRules.Concrete(kind))
            {
                string label=PlayableResearchRules.Weapon(kind)?"Урон за попадание":
                    PlayableResearchRules.Defense(kind)?"Макс. здоровье":"Здоровье зданий";
                return new[]{label+": +"+N(PlayableResearchRules.Bonus(p,kind))+"%"};
            }

            switch(kind)
            {
                case PlayableResearchKind.TankChassis:
                    var defense=research?.Where(r=>r.Complete&&PlayableResearchRules.Defense(r.Kind))
                        .OrderByDescending(r=>PlayableResearchRules.Level(r.Kind)).FirstOrDefault();
                    double health=PlayableUnitRules.Health(p,PlayableEntityKind.Tank)*
                        (1+(defense==null?0:PlayableResearchRules.Bonus(p,defense.Kind))/100);
                    return new[]{
                        Change("Скорость",PlayableUnitRules.Speed(p,PlayableEntityKind.Tank),PlayableUnitRules.Speed(p,PlayableEntityKind.Tank,true)," м/с"),
                        Change("Макс. здоровье",PlayableStatRounding.MaximumHealth(p,health),
                            PlayableStatRounding.MaximumHealth(p,health*(1+p.TankChassisHealthBonusPercent/100))," HP")};
                case PlayableResearchKind.ExplorerRocketLauncher:
                    double assaultDps=PlayableUnitRules.DamagePerSecond(p,PlayableEntityKind.Explorer,true);
                    return new[]{
                        Change("Дальность по земле",p.ExplorerRange,p.ExplorerRocketGroundRange," м"),
                        Change("Дальность по воздуху",p.ExplorerRange,p.ExplorerRocketAirRange," м"),
                        Change("Урон в секунду по земле",assaultDps,PlayableUnitRules.ExplorerRocketDamagePerSecond(p,false)),
                        Change("Урон в секунду по воздуху",assaultDps,PlayableUnitRules.ExplorerRocketDamagePerSecond(p,true))};
                case PlayableResearchKind.ExplorerAssaultGuns:
                    return new[]{Change("Выстрелов в очереди",p.ExplorerBurstSize,p.ExplorerAssaultBurstSize)};
                case PlayableResearchKind.ShkvalGuidance:
                    return new[]{
                        Change("Дальность",PlayableUnitRules.Range(p,PlayableEntityKind.Shkval),PlayableUnitRules.Range(p,PlayableEntityKind.Shkval,true)," м"),
                        Change("Радиус взрыва",PlayableUnitRules.ShkvalBlastRadius(p,false),PlayableUnitRules.ShkvalBlastRadius(p,true)," м"),
                        Change("Интервал пусков",PlayableUnitRules.ShkvalFireInterval(p,false),PlayableUnitRules.ShkvalFireInterval(p,true)," с")};
                case PlayableResearchKind.HawkSalvoSystems:
                    return new[]{
                        Change("Подготовка",p.HawkPreparationSeconds,p.HawkUpgradedPreparationSeconds," с"),
                        Change("Интервал ракет",p.HawkSalvoDelay,p.HawkUpgradedSalvoDelay," с")};
                case PlayableResearchKind.ScientificCenterUpgrade:
                    return new[]{"Оружие: II–III","Защита: II–III","Усиленный бетон: II","Ракетная установка"};
                default:
                    throw new ArgumentException("Unknown research",nameof(kind));
            }
        }

        public static string Unit(PlayableProfile p,PlayableEntityKind kind,IEnumerable<PlayableResearchOrderSnapshot> research=null,PlayableEntitySnapshot entity=null,bool freeEconomy=false)
        {
            bool Complete(PlayableResearchKind r)=>research?.Any(o=>o.Kind==r&&o.Complete)==true;
            double Systems(bool weapon,int level)=>1+(level>=3?PlayableResearchRules.Bonus(p,weapon?PlayableResearchKind.UltimateWeaponSystems:PlayableResearchKind.UltimateDefensiveSystems):level>=2?PlayableResearchRules.Bonus(p,weapon?PlayableResearchKind.AdvancedWeaponSystems:PlayableResearchKind.AdvancedDefensiveSystems):level==1?PlayableResearchRules.Bonus(p,weapon?PlayableResearchKind.ImprovedWeaponSystems:PlayableResearchKind.ImprovedDefensiveSystems):0)/100;
            int Level(bool weapon)=>Complete(weapon?PlayableResearchKind.UltimateWeaponSystems:PlayableResearchKind.UltimateDefensiveSystems)?3:Complete(weapon?PlayableResearchKind.AdvancedWeaponSystems:PlayableResearchKind.AdvancedDefensiveSystems)?2:Complete(weapon?PlayableResearchKind.ImprovedWeaponSystems:PlayableResearchKind.ImprovedDefensiveSystems)?1:0;
            if(kind==PlayableEntityKind.Cerberus)return "Цербер\nПередовая военная разработка и редкий гость в мирах фронтира. Цербер предназначен для прорыва плотных скоплений противника, несёт два плазменных огнемёта, которые не оставляют шанса лёгкой и средней технике. Может повредить союзную технику, которая будет недостаточно осторожна при маневрировании рядом с ведущим огонь Цербером."+
                $"\nПрочность {N(p.CerberusHealth*Systems(false,entity?.DefensiveSystemsLevel??Level(false)))} HP · Начало огня {N(p.CerberusEngagementRange)} м · Длина струи {N(p.CerberusRange)} м\nКаждая струя {N(p.CerberusDamage*Systems(true,entity?.WeaponSystemsLevel??Level(true)))} HP/с · Огонь {N(p.CerberusFireTime)} с · Охлаждение {N(p.CerberusCooldown)} с"+
                $"\nСкорость: {N(p.CerberusSpeed)} м/с\nРазворот: {N(p.CerberusTurn)} °/с\nУгол струи: {N(p.CerberusAngle)}°"+
                "\n"+(freeEconomy?"Бесплатно":"Стоимость: "+N(p.CerberusCreditCost)+" кредитов")+
                "\nПроизводство: "+N(freeEconomy?p.ProvingGroundActionSeconds:p.CerberusDuration)+" с\nМесто в армии: "+N(p.CerberusPopulation);
            bool upgraded=entity?.Upgraded??Complete(kind==PlayableEntityKind.Tank?PlayableResearchKind.TankChassis:kind==PlayableEntityKind.Explorer?PlayableResearchKind.ExplorerAssaultGuns:PlayableResearchKind.ShkvalGuidance);
            bool salvo=entity?.Upgraded??Complete(PlayableResearchKind.HawkSalvoSystems);
            bool rocket=kind==PlayableEntityKind.Explorer&&(entity?.RocketLauncher??Complete(PlayableResearchKind.ExplorerRocketLauncher));
            string role=kind switch {
                PlayableEntityKind.Explorer when rocket=>PlayableResearchRules.Description(p,PlayableResearchKind.ExplorerRocketLauncher),
                PlayableEntityKind.Explorer=>"Лёгкий полицейский транспорт дальних колоний. На нём патрулируют поселения и разведывают маршруты за их пределами. При налёте его автоматическое орудие служит лёгким ПВО.",
                PlayableEntityKind.Tank=>"Основной танк корпоративных армий в войнах за ресурсы. Прост в сборке и неприхотлив в полевых условиях. Держит удар на передовой и прикрывает менее защищённую технику.",
                PlayableEntityKind.Igla=>"Дальнобойная самоходная установка, вооруженная смертоносным гаус лучом. Нет брони, которую Игла не могла бы пробить. В бою требуется обязательное прикрытие.",
                PlayableEntityKind.Shkval=>"Мобильная ракетная артиллерия для позиционных боёв. Обстреливает укрепления с дальней дистанции. Особенно опасен, когда противник стягивает технику на тесный участок фронта.",
                _=>"Ударный вертолёт для внезапных атак по наземным и воздушным целям. В мирное время на нём доставляют срочные грузы в поселения, до которых трудно добраться по земле."};
            double damage=kind==PlayableEntityKind.Igla?p.IglaDamage:kind==PlayableEntityKind.Tank?p.TankWeaponDamage:kind==PlayableEntityKind.Explorer?(rocket?p.ExplorerRocketDamage:p.ExplorerDamage):kind==PlayableEntityKind.Shkval?p.ShkvalDamage:p.HawkDamage;
            damage=PlayableStatRounding.Damage(p,damage*Systems(true,entity?.WeaponSystemsLevel??Level(true)));
            double health=entity==null?PlayableStatRounding.MaximumHealth(p,PlayableUnitRules.Health(p,kind)*Systems(false,Level(false))*(kind==PlayableEntityKind.Tank&&upgraded?1+p.TankChassisHealthBonusPercent/100:1)):PlayableUnitRules.MaximumHealth(p,entity);
            if(kind==PlayableEntityKind.Skat)
                return "Скат\nЛетающий ремонтный дрон, который берут в рейсы вдали от мастерских. В бою он идёт следом за колонной, чинит подбитые машины и помогает восстанавливать постройки под обстрелом.\n\nЗдоровье: "+N(health)+
                    "\nСкорость: "+N(p.SkatSpeed)+" м/с\nСкорость ремонта: "+N(p.SkatRepairRate)+" HP/с\nДальность ремонта: "+N(p.SkatRepairRange)+" м"+
                    "\n"+(freeEconomy?"Бесплатно":"Стоимость: "+N(p.SkatCreditCost)+" кредитов")+
                    "\nПроизводство: "+N(freeEconomy?p.ProvingGroundActionSeconds:p.SkatProductionSeconds)+" с\nМесто в армии: "+N(p.SkatPopulationCost);
            string fire=rocket?"Ракет в залпе: 2\nИнтервал пусков по земле: "+N(p.ExplorerRocketGroundInterval)+" с\nИнтервал пусков по воздуху: "+N(p.ExplorerRocketAirInterval)+" с":
                kind==PlayableEntityKind.Igla?$"Перезарядка: {N(p.IglaReload)} с\nПодготовка к выстрелу: {N(p.IglaPreparation)} с\nСектор наведения: ±{N(p.IglaHalfAngle)}°":
                kind==PlayableEntityKind.Tank?"Перезарядка: "+N(p.TankWeaponReloadSeconds)+" с":kind==PlayableEntityKind.Explorer?
                "Выстрелов в очереди: "+N(upgraded?p.ExplorerAssaultBurstSize:p.ExplorerBurstSize)+"\nИнтервал выстрелов: "+N(p.ExplorerBurstShotIntervalMs/1000d)+" с\nПауза между очередями: "+N(p.ExplorerBurstPauseMs/1000d)+" с":kind==PlayableEntityKind.Shkval?
                "Интервал пусков: "+N(PlayableUnitRules.ShkvalFireInterval(p,upgraded))+" с\nРадиус взрыва: "+N(PlayableUnitRules.ShkvalBlastRadius(p,upgraded))+" м":
                "Интервал ракет в залпе: "+N(salvo?p.HawkUpgradedSalvoDelay:p.HawkSalvoDelay)+" с\nПерезарядка: "+N(PlayableUnitRules.HawkSalvoPause(p))+" с";
            string range=rocket?"Дальность по земле: "+N(p.ExplorerRocketGroundRange)+" м\nДальность по воздуху: "+N(p.ExplorerRocketAirRange)+" м":
                "Дальность огня: "+N(PlayableUnitRules.Range(p,kind,upgraded))+" м";
            return OfflinePadWorldActions.Name(kind)+"\n"+role+"\n\nЗдоровье: "+(entity==null?N(health):N(PlayableStatRounding.DisplayHealth(entity.ExactHealth??entity.Health))+" / "+N(health))+
                (kind==PlayableEntityKind.Shkval?"\nПрямое попадание: "+N(PlayableStatRounding.Damage(p,p.ShkvalDirectHitDamage*Systems(true,entity?.WeaponSystemsLevel??Level(true))))+"\nУрон по площади: "+N(damage):"\nУрон за попадание: "+N(damage))+"\n"+fire+
                "\n"+range+"\nСкорость: "+N(PlayableUnitRules.Speed(p,kind,upgraded))+" м/с\nОбзор: "+N(PlayableUnitRules.Vision(p,kind))+" м"+
                "\n\n"+(freeEconomy?"Бесплатно":"Стоимость: "+N(PlayableUnitRules.Cost(p,kind))+" кредитов")+"\nПроизводство: "+N(freeEconomy?p.ProvingGroundActionSeconds:PlayableUnitRules.Duration(p,kind))+" с\nМесто в армии: "+N(PlayableUnitRules.Population(p,kind));
        }
    }
}
