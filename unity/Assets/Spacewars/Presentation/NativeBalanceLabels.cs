using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Presentation
{
    internal static class NativeBalanceLabels
    {
        private static readonly Dictionary<string,string> Labels=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
            {"Maximum stretch","Максимальное растяжение группы"},{"Deferred unit orders","Лимит отложенных приказов"},{"Ring padding","Отступ круга выделения"},{"Ring width","Толщина круга выделения"},{"Surface lift","Высота круга над землёй"},
            {"Science health","Научный центр · здоровье"},{"Science construction time","Научный центр · время строительства"},{"Science vision","Научный центр · обзор"},{"Science price","Научный центр · цена"},
            {"Upgrade price","Улучшение переработчика · цена"},{"Upgrade duration","Улучшение переработчика · время"},{"Upgraded income","Улучшенный переработчик · доход"},{"Turbine rotation","Скорость вращения турбины"},
            {"Chassis health bonus","Шасси · бонус здоровья"},{"Chassis price","Шасси · цена"},{"Chassis duration","Шасси · время исследования"},{"Chassis Vepr speed","Шасси · скорость Вепря"},
            {"Assault gun price","Штурмовая пушка · цена"},{"Assault gun duration","Штурмовая пушка · время исследования"},{"Assault burst size","Штурмовая пушка · длина очереди"},{"Assault spread","Штурмовая пушка · разброс"},
            {"Guidance price","Наведение · цена"},{"Guidance duration","Наведение · время исследования"},{"Guidance range","Наведение · дальность"},
            {"HeadquartersVisionRange","Штаб · обзор"},{"OutpostVisionRange","Форпост · обзор"},{"FactoryVisionRange","Фабрика · обзор"},{"RefineryVisionRange","Переработчик · обзор"},{"MineVisionRange","Шахта · обзор"},{"ConstructionVisionMultiplier","Обзор во время строительства"},
            {"Outpost health","Форпост · здоровье"},{"Outpost build seconds","Форпост · время строительства"},{"Outpost credit cost","Форпост · цена"},{"Outpost income per period","Форпост · доход"},
            {"Mine health","Шахта · здоровье"},{"Mine build seconds","Шахта · время строительства"},{"Mine credit cost","Шахта · цена"},{"Mine income per period","Шахта · доход"},
            {"Headquarters capture radius","Штаб · радиус захвата"},{"Headquarters capture seconds","Штаб · время захвата"},{"Outpost capture radius","Форпост · радиус захвата"},{"Outpost capture seconds","Форпост · время захвата"},{"Mine capture radius","Шахта · радиус захвата"},{"Mine capture seconds","Шахта · время захвата"},
            {"Construction damage multiplier","Урон по строящемуся зданию"},{"Evacuation retry seconds","Интервал повторной эвакуации"},
            {"speed","Скорость"},{"turnSpeed","Скорость поворота корпуса"},{"turretTurnSpeed","Скорость башни"},{"aimToleranceRad","Допуск наведения"},{"range","Дальность огня"},{"vision","Обзор"},{"health","Здоровье"},{"stopForMs","Подготовка к выстрелу"},{"populationCost","Место в армии"},{"creditCost","Цена производства"},{"productionMenuOrder","Порядок в меню производства"},{"productionDurationSec","Время производства"},{"damage","Урон"},{"burstSize","Снарядов в очереди"},{"burstShotIntervalMs","Интервал между снарядами"},{"burstPauseMs","Пауза между очередями"},{"projectileSpeed","Скорость снаряда"},{"spreadDeg","Разброс"},{"tracerLength","Длина трассера"},{"tracerThickness","Толщина трассера"},{"Muzzle distance","Смещение дульного среза"},
            {"Hull turn","Скорость поворота корпуса"},{"Turret turn","Скорость башни"},{"Aim tolerance","Допуск наведения"},{"Attack range","Дальность огня"},{"Preparation","Подготовка к выстрелу"},{"Population cost","Место в армии"},{"Credit cost","Цена производства"},{"Production order","Порядок в меню производства"},{"Production time","Время производства"},{"Shell damage","Урон снаряда"},{"Reload","Перезарядка"},{"Shell speed","Скорость снаряда"},{"Projectile overtravel","Запас дальности снаряда"},
            {"Income period","Период дохода"},{"Building refund","Возврат за отмену здания"},{"Unit refund","Возврат за отмену юнита"},{"HQ health","Штаб · здоровье"},{"HQ build time","Штаб · время строительства"},{"HQ cost","Штаб · цена"},{"HQ income","Штаб · доход"},{"Factory health","Фабрика · здоровье"},{"Factory build time","Фабрика · время строительства"},{"Factory cost","Фабрика · цена"},{"Refinery health","Переработчик · здоровье"},{"Refinery build time","Переработчик · время строительства"},{"Refinery cost","Переработчик · цена"},{"Refinery income","Переработчик · доход"},
            {"Heading intervals","Направлений движения"},{"Speed bands","Вариантов скорости"},{"Lookahead","Горизонт прогноза"},{"Local neighbor capacity","Лимит соседних объектов"},
            {"Gate motion","Время открытия ворот"},{"Exit speed","Скорость выезда"},{"Group route excess","Допуск длины группового маршрута"},
            {"Army capacity","Вместимость армии"},{"Following distance","Дистанция следования"},{"Following arrival tolerance","Допуск прибытия при следовании"},{"Idle auto-defense range","Дальность автообороны"},{"Enemy advance delay","Задержка наступления противника"},{"Attack approach","Дистанция сближения для атаки"},{"Attack repath","Интервал обновления пути атаки"}
        };
        internal const string Escalation="Эскалация";
        internal const string Research="Исследования", Mechanics="Общие механики";
        private static string ResearchEntity(PlayableProfileField f)
        {
            if(f.Path.StartsWith("research.reinforcedConcrete."))return "Усиленный бетон";
            if(f.Path.StartsWith("research.centerUpgrade."))return "Улучшение научного центра";
            if(f.Path.StartsWith("research.weaponSystems."))return "Оружейные системы";
            if(f.Path.StartsWith("research.defensiveSystems."))return "Защитные системы";
            if(f.Path.StartsWith("research.tankChassis"))return "Улучшенное шасси";
            if(f.Path.StartsWith("research.explorerRocket."))return "Ракетная установка";
            if(f.Path.StartsWith("research.explorerAssault"))return "Штурмовые орудия";
            if(f.Path.StartsWith("research.shkvalGuidance")||f.Path=="artillery.blastRadius")return "Улучшенное наведение";
            if(f.Group=="Ястреб · hawk-salvo-research-v1")return "Залповый огонь";
            return null;
        }
        private static string ObjectEntity(PlayableProfileField f)
        {
            var n=(f.FieldName??"").ToLowerInvariant();
            if(n=="hawkroofseconds"||n=="hawkplatformseconds"||n=="hawkspinseconds")return "Ангар";
            if(n.StartsWith("gauss"))return "Гаусс-башня";
            if(n.StartsWith("repairtower"))return "Ремонтная вышка";
            if(n.StartsWith("turret"))return "Пулемётная турель";
            if(n.StartsWith("igla"))return "Игла";
            if(n.StartsWith("cerberus"))return "Цербер";
            if(n.StartsWith("skat"))return "Скат";
            if(n.StartsWith("hawk"))return "Ястреб";
            if(n.Contains("tank"))return OfflinePadWorldActions.VeprName;
            if(n.Contains("explorer"))return "Исследователь";
            if(n.Contains("shkval"))return "Шквал";
            if(n.Contains("headquarters"))return "Штаб";
            if(n.Contains("outpost"))return "Форпост";
            if(n.StartsWith("mine"))return "Шахта";
            if(n.StartsWith("hangar"))return "Ангар";
            if(n.Contains("factory"))return "Фабрика";
            if(n.Contains("refinery"))return "Переработчик";
            if(n.StartsWith("science")||n.StartsWith("scientific"))return "Научный центр";
            if(f.Group=="Relay towers")return "Ретранслятор";
            return null;
        }
        private static string SharedEntity(PlayableProfileField f)
        {
            if(f.Path.StartsWith("wrecks.")||f.Path=="presentation.unitWreck.lifetimeSeconds")return "Остовы";
            if(f.Group=="Capture")return "Захват";
            if(f.Path.StartsWith("domination.",StringComparison.Ordinal))return "Доминирование";
            if(f.Path.StartsWith("crowdMotion.")||f.Path.StartsWith("navigation."))return "Приказы и движение";
            if(f.FieldName=="unitOrderQueueLimit")return "Приказы и движение";
            if(f.FieldName=="idleAutoDefenseMultiplier"||f.Path.StartsWith("combat.response.",StringComparison.Ordinal))return "Бой";
            if(f.FieldName=="constructionVisionMultiplier")return "Строительство, ремонт и продажа";
            if(f.Group=="Construction"||f.Group.StartsWith("Здания · Жизненный"))return "Строительство, ремонт и продажа";
            if(f.Path.StartsWith("production."))return "Производство";
            if(f.Group=="Army")return "Армия";
            if(f.Group=="Follow"||f.Group.StartsWith("Group "))return "Приказы и движение";
            if(f.Group=="Combat"||f.Group=="Behavior"||f.Path.StartsWith("combatRounding.")||f.Group.StartsWith("Базовый бой"))return "Бой";
            if(f.Group.StartsWith("Полигон"))return "Полигон";
            return "Общие правила";
        }
        internal static string Section(PlayableProfileField f)
        {
            if(f.Path.StartsWith("orbital.",StringComparison.Ordinal))return Escalation;
            if(f.Path.StartsWith("ai.")||(f.FieldName??"")=="enemyAdvanceDelaySeconds")return "ИИ";
            if(f.Path.StartsWith("wrecks.")||f.Path=="presentation.unitWreck.lifetimeSeconds")return Mechanics;
            if(ResearchEntity(f)!=null||f.Path.StartsWith("research."))return Research;
            if(f.Path.StartsWith("production.")||f.Group=="Capture"||f.Group=="Construction"||f.Group.StartsWith("Здания · Жизненный"))return Mechanics;
            var obj=ObjectEntity(f);
            if(obj!=null)return obj==OfflinePadWorldActions.VeprName||obj=="Исследователь"||obj=="Шквал"||obj=="Ястреб"||obj=="Скат"||obj=="Цербер"||obj=="Игла"?"Юниты":"Здания";
            if(f.Group=="Economy")return "Экономика";
            return Mechanics;
        }
        internal static string Entity(PlayableProfileField f)
        {
            string section=Section(f);
            if(section==Escalation){string name=f.FieldName??"";return name.StartsWith("orbitalNuclear",StringComparison.Ordinal)?"Ядерный удар":name.StartsWith("orbitalBoost",StringComparison.Ordinal)?"Ускоренная выработка":name.StartsWith("orbitalNanobots",StringComparison.Ordinal)?"Ремонтные нанороботы":name.StartsWith("orbitalStasis",StringComparison.Ordinal)?"Стазис":name.StartsWith("orbitalReinforcement",StringComparison.Ordinal)?"Призыв подкрепления":name.StartsWith("orbitalSystemFailure",StringComparison.Ordinal)?"Сбой систем":name.StartsWith("orbitalMine",StringComparison.Ordinal)?"Мина-ловушка":name.StartsWith("orbitalHack",StringComparison.Ordinal)?"Взлом управления":name.StartsWith("orbitalScan",StringComparison.Ordinal)?"Сканирование":(name.StartsWith("orbitalThreshold",StringComparison.Ordinal))?"Пороги уровней":name.StartsWith("orbitalUi",StringComparison.Ordinal)||name.StartsWith("orbitalCard",StringComparison.Ordinal)||name=="orbitalPanelWidth"||name=="orbitalIconSize"||name=="orbitalLevelFontSize"?"Панель":"Перезарядка";}
            if(section==Research)return ResearchEntity(f)??"Прочие исследования";
            if(section=="ИИ")return f.Path.StartsWith("ai.research.")?"Исследования":"Боевое поведение";
            if(section==Mechanics)return SharedEntity(f);
            if(section=="Экономика")return "Доход и возвраты";
            return ObjectEntity(f)??"Общие";
        }
        internal static string AiEntity(Spacewars.Simulation.Ai.AiProfileField f)=>f.Group.StartsWith("ИИ / ")?f.Group.Substring(5):f.Group;
        internal static string[] RelatedResearch(string obj)=>obj switch {
            OfflinePadWorldActions.VeprName=>new[]{"Улучшенное шасси","Оружейные системы","Защитные системы"},
            "Исследователь"=>new[]{"Штурмовые орудия","Ракетная установка","Оружейные системы","Защитные системы"},
            "Игла" or "Цербер"=>new[]{"Оружейные системы","Защитные системы"},
            "Шквал"=>new[]{"Улучшенное наведение","Оружейные системы","Защитные системы"},
            "Ястреб"=>new[]{"Залповый огонь","Оружейные системы","Защитные системы"},
            "Научный центр"=>new[]{"Улучшение научного центра","Усиленный бетон","Оружейные системы","Защитные системы"},
            _=>Array.Empty<string>()};
        internal static string Glyph(string name)=>name switch {
            "Игла"=>"igla","Цербер"=>"cerberus","Скат"=>"skat",
            "Ракетная установка"=>ModelPortrait.RocketLauncherArtwork,
            "Ретранслятор"=>"relay-tower","Пулемётная турель"=>"sentry-turret","Гаусс-башня"=>"gauss-tower","Ремонтная вышка"=>"repair-tower",
            "Усиленный бетон"=>"reinforced-concrete","Ястреб"=>"hawk",OfflinePadWorldActions.VeprName=>"tank","Исследователь"=>"explorer","Шквал"=>"shkval","Штаб"=>"headquarters","Форпост"=>"outpost","Шахта"=>"mine","Фабрика"=>"factory","Ангар"=>"hangar","Переработчик"=>"refinery","Научный центр"=>"science","Улучшение научного центра"=>"science","Оружейные системы"=>"weapon-systems-1","Защитные системы"=>"defensive-systems-1",
            "Улучшенное шасси"=>"tankHybridUpgraded","Штурмовые орудия"=>"explorerUpgraded","Улучшенное наведение"=>"shkval-guidance-3","Залповый огонь"=>"hawkUpgraded",_=>null};
        private static (int order, string label, string help) CommonStat(PlayableProfileField field)
        {
            var section=Section(field);
            if(section!="Юниты"&&section!="Здания")return default;
            string name=field.FieldName??"";
            bool unit=section=="Юниты";
            if(name.EndsWith("Health"))
                return (1,"Здоровье","Максимальный запас здоровья базового объекта.");
            if(name.EndsWith("Vision")||name.EndsWith("VisionRange"))
                return (2,"Радиус обзора","Радиус, в котором объект открывает местность и противников для своей команды.");
            if(name.EndsWith("CreditCost"))
                return (3,unit?"Цена производства":"Цена строительства","Кредиты, списываемые при заказе.");
            if(unit&&(name.EndsWith("ProductionSeconds")||name.EndsWith("ProductionDurationSec")||name is "iglaDuration" or "cerberusDuration"))
                return (4,"Время производства","Время активного производства до выпуска. Ожидание в очереди считается отдельно.");
            if(!unit&&name.EndsWith("BuildSeconds"))
                return (4,"Время строительства","Время от начала строительства до готовности здания.");
            if(unit&&(name.EndsWith("PopulationCost")||name is "iglaPopulation" or "cerberusPopulation"))
                return (5,"Место в армии","Часть лимита армии, которую занимает один юнит.");
            return name switch
            {
                "tankSpeed" or "explorerSpeed" or "shkvalSpeed" or "hawkSpeed" or "iglaSpeed" or "cerberusSpeed" or "skatSpeed" => (6,"Максимальная скорость","Предельная скорость движения вперёд."),
                "tankTurnSpeed" or "explorerTurnSpeed" or "shkvalTurnSpeed" or "hawkYawSpeed" or "iglaHullTurn" or "cerberusTurn" or "skatYawSpeed" => (7,"Скорость поворота корпуса","Предельная угловая скорость поворота корпуса."),
                "tankTurretTurnSpeed" or "explorerTurretTurnSpeed" or "shkvalTurretTurnSpeed" or "turretYawSpeed" => (8,"Скорость поворота башни","Предельная скорость горизонтального поворота башни к цели."),
                "tankRange" or "explorerRange" or "shkvalBaseRange" or "turretRange" or "gaussRange" or "iglaRange" => (9,"Дальность огня",null),
                "hawkRange" => (9,"Дальность огня","Предельная дальность стрельбы по горизонтальной проекции."),
                "cerberusEngagementRange" => (9,"Дистанция начала огня",null),
                "cerberusRange" => (10,"Длина струи",null),
                "cerberusAngle" => (11,"Угол струи",null),
                "cerberusDamage" => (12,"Урон одной струи",null),
                "cerberusFireTime" => (13,"Время до перегрева",null),
                "cerberusCooldown" => (14,"Время полного охлаждения",null),
                "skatRepairRange" => (9,"Дальность ремонта",null),
                "skatRepairRate" => (10,"Скорость ремонта",null),
                "tankWeaponDamage" or "explorerDamage" or "turretDamage" or "hawkDamage" or "iglaDamage" => (10,"Урон за попадание","Базовый урон одного попадания до применения боевых улучшений."),
                "shkvalDamage" => (10,"Урон по площади",null),
                "shkvalDirectHitDamage" => (11,"Урон при прямом попадании",null),
                "tankStopForMilliseconds" or "explorerStopForMs" or "shkvalStopForMs" => (12,"Подготовка к выстрелу","Время остановки перед выстрелом, когда стрельба на ходу запрещена."),
                "hawkPreparationSeconds" or "iglaPreparation" => (12,"Подготовка к выстрелу",null),
                "iglaHalfAngle" => (13,"Сектор наведения в каждую сторону","Предельный угол отклонения орудия вправо или влево от продольной оси корпуса."),
                "tankAimToleranceRadians" or "explorerAimToleranceRad" or "shkvalAimToleranceRad" or "hawkAimTolerance" => (13,"Допуск наведения",null),
                "tankWeaponReloadMilliseconds" or "shkvalFireIntervalMs" or "gaussReloadSeconds" or "hawkReloadSeconds" or "iglaReload" => (14,"Перезарядка",null),
                "tankProjectileSpeed" or "explorerProjectileSpeed" or "shkvalProjectileSpeed" or "turretProjectileSpeed" or "hawkMissileSpeed" => (15,"Скорость снаряда","Скорость полёта снаряда."),
                "explorerBurstSize" or "turretBurstSize" => (16,"Выстрелов в очереди",null),
                "explorerBurstShotIntervalMs" or "turretBurstShotIntervalMs" => (17,"Интервал между выстрелами в очереди",null),
                "explorerBurstPauseMs" or "turretBurstPauseMs" => (18,"Пауза между очередями",null),
                "explorerSpreadDeg" or "turretSpreadDeg" => (19,"Разброс",null),
                _ => default
            };
        }
        internal static int Order(PlayableProfileField field)
        {
            int order=CommonStat(field).order;
            if(order!=0)return order;
            return Bucket(field) switch
            {
                "Уровень I"=>31,"Уровень II"=>32,"Уровень III"=>33,
                "Модернизация переработчика" or "Модернизация производства"=>40,
                _=>30
            };
        }
        internal static string Bucket(PlayableProfileField f)
        {
            if(Section(f)==Research)
            {
                if(f.Path.Contains(".level3."))return "Уровень III";
                if(f.Path.Contains(".level2."))return "Уровень II";
                if(f.Path.Contains(".level1.")||f.Path.StartsWith("research.reinforcedConcrete."))return "Уровень I";
                return "Цена, время и эффект";
            }
            if((f.FieldName??"").StartsWith("refineryUpgrade"))return "Модернизация переработчика";
            if(f.Group.StartsWith("Здания · Фабрика · Улучшение")||f.Group.StartsWith("Здания · Ангар · Улучшение"))return "Модернизация производства";
            var section=Section(f);
            if(section=="Юниты"||section=="Здания")
            {
                if(f.FieldName is "skatRepairRange" or "skatRepairRate")return "Ремонт";
                int order=CommonStat(f).order;
                if(order>0&&order<=7)return "Основные характеристики";
                if(order>=8)return "Оружие";
                return "Особенности";
            }
            return "Основные параметры";
        }
        internal static string Help(PlayableProfileField f)
        {
            if(CommonStat(f).help is string help)return help;
            if(Tips.TryGetValue(f.FieldName??"",out var explained))return explained;
            if(f.Description.Any(c=>c>='А'&&c<='я'))return f.Description;
            var n=(f.FieldName??"").ToLowerInvariant();
            if(n.Contains("captureradius"))return "Расстояние от точки захвата, в пределах которого наземный юнит может захватывать объект.";
            if(n.Contains("captureseconds"))return "Время полного захвата или снятия прогресса. Количество юнитов не ускоряет захват.";
            if(n.Contains("vision"))return "Радиус обзора: в этих пределах объект открывает местность и врагов для своей команды.";
            if(n.Contains("income"))return "Количество кредитов за период дохода. Длительность периода задаётся в разделе «Экономика».";
            if(n.Contains("health"))return "Максимальный запас прочности. При нулевом здоровье объект уничтожается.";
            if(n.Contains("creditcost")||n.EndsWith("cost"))return f.Path.StartsWith("research.")?"Кредиты, списываемые при начале исследования улучшения.":"Цена строительства или производства в кредитах.";
            if(n.Contains("buildseconds"))return "Игровое время от начала строительства до готовности здания.";
            if(n.Contains("productionseconds")||n.Contains("productionduration"))return "Время активного производства юнита на фабрике; ожидание в очереди считается отдельно.";
            if(n.Contains("productionmenuorder"))return "Позиция юнита в меню производства. Меньшее число ставит его раньше.";
            if(n.Contains("populationcost"))return "Место, которое один юнит занимает в лимите армии.";
            if(n.Contains("turretturn"))return "Максимальная скорость независимого поворота башни к цели.";
            if(n.Contains("turnspeed"))return "Максимальная скорость поворота корпуса при движении.";
            if(n.Contains("aimtolerance"))return "Допустимая угловая ошибка наведения башни, при которой разрешён выстрел.";
            if(n.Contains("projectilespeed"))return "Скорость полёта снаряда от орудия до точки попадания.";
            if(n.Contains("speed"))return "Максимальная скорость движения юнита.";
            if(n.Contains("stopfor"))return "Время остановки и подготовки перед выстрелом.";
            if(n.Contains("reload"))return "Интервал между выстрелами орудия.";
            if(n.Contains("damage"))return "Урон одного попадания по цели до применения модификаторов.";
            if(n.Contains("range"))return "Максимальная дальность выбора цели для стрельбы.";
            if(n.Contains("spread"))return "Максимальный угол отклонения снаряда от направления стрельбы.";
            if(n.Contains("burstsize"))return "Количество снарядов в одной очереди.";
            if(n.Contains("burstshotinterval"))return "Интервал между снарядами внутри одной очереди.";
            if(n.Contains("burstpause"))return "Пауза между завершёнными очередями стрельбы.";
            return f.Description;
        }
        private static readonly Dictionary<string,string> Tips=new Dictionary<string,string> {
            {"crowdMotionHeadingIntervals","Число проверяемых направлений движения в пределах допустимого поворота."},
            {"crowdMotionSpeedBands","Число вариантов скорости вперёд; ожидание и поворот на месте остаются отдельными вариантами."},
            {"crowdMotionLookaheadSeconds","Горизонт прогноза для выбора движения; безопасность фактического шага проверяется отдельно."},
            {"crowdMotionNeighborCap","Максимум соседних объектов для локальной оценки; при переполнении юнит консервативно ждёт."},
            {"factoryGateSeconds","Время полного открытия или закрытия ворот фабрики."},
            {"groupCorridorMaximumExcessPercent","Допустимое удлинение сертифицированного маршрута участника группы относительно его собственного оптимального пути. При отсутствии сертификата используется точный маршрут."},
            {"buildingSaleRefundRatio","Доля цены здания, возвращаемая владельцу после продажи."},
            {"buildingSaleCombatLockoutSec","Время после последнего полученного урона, в течение которого нельзя продать здание."},
            {"buildingSaleDemolitionSec","Время демонтажа здания после заказа продажи."},
            {"buildingRepairDurationSec","Время восстановления полного запаса здоровья здания; частичный ремонт занимает пропорционально меньше времени."},
            {"buildingRepairCostRatio","Цена полного ремонта как доля стоимости здания; частичный ремонт оплачивается пропорционально."},
            {"buildingRepairCombatLockoutSec","Время после последнего полученного урона, в течение которого нельзя начать ремонт."},
            {"incomePeriodSeconds","Период, для которого задаётся доход зданий; кредиты начисляются каждую секунду."},
            {"buildingCancellationRefundRatio","Доля уплаченной цены, возвращаемая при отмене строительства."},
            {"unitCancellationRefundRatio","Доля уплаченной цены, возвращаемая при отмене производства юнита."},
            {"armyCapacity","Лимит живых юнитов и мест, зарезервированных активным производством. Ожидающие заказы места не занимают."},
            {"constructionDamageMultiplier","Множитель входящего урона для недостроенных зданий."},
            {"constructionVisionMultiplier","Доля обычного радиуса обзора, доступная зданию во время строительства."},
            {"evacuationRetrySeconds","Интервал повторных попыток вывести юнита из заблокированного места строительства."},
            {"tankChassisSeconds","Время активного исследования улучшенного шасси Вепря."},
            {"tankChassisHealthBonusPercent","Бонус максимального здоровья Вепря после шасси, перемножается с защитными системами; сохраняет долю здоровья."},
            {"tankChassisSpeed","Скорость Вепрей владельца после завершения исследования шасси."},
            {"explorerAssaultSeconds","Время активного исследования штурмовых орудий Исследователя."},
            {"explorerAssaultBurstSize","Количество снарядов в очереди Исследователя после улучшения орудий."},
            {"explorerAssaultSpreadDeg","Максимальный разброс снарядов Исследователя после улучшения орудий."},
            {"shkvalGuidanceSeconds","Время активного исследования улучшенного наведения «Шквала»."},
            {"shkvalGuidanceRange","Дальность стрельбы «Шквала» владельца после завершения исследования наведения."},
            {"refineryUpgradeSeconds","Время улучшения переработчика до начала повышенного дохода."},
            {"refineryTurbineSpeed","Визуальная скорость вращения турбины готового переработчика."},
            {"selectionRingPadding","Расстояние между краем модели и кругом выделения на земле."},
            {"selectionRingWidth","Толщина зелёного круга выделения на земле."},
            {"selectionRingLift","Высота круга над поверхностью, предотвращающая мерцание при наложении."},
            {"followDistance","Дистанция, которую следующий юнит старается держать позади лидера."},
            {"followArrivalTolerance","Допустимое отклонение от точки следования, при котором юнит останавливается."},
            {"idleAutoDefenseMultiplier","Множитель радиуса поиска целей стоящим юнитом. Не разрешает движение и не увеличивает дальность оружия."},
            {"groupMarchMaximumStretch","Предельное отставание группы, после которого передний юнит ждёт остальных."},
            {"unitOrderQueueLimit","Максимум отложенных приказов одному юниту. При переполнении существующая очередь сохраняется."},
            {"enemyAdvanceDelaySeconds","Время сборки сил перед первым наступлением противника."},
            {"attackApproachRangeRatio","Доля дальности оружия, которую юнит сохраняет при сближении с целью."},
            {"attackRepathSeconds","Минимальный интервал между запросами нового пути при атаке."},
            {"projectileExtraRangePercent","Дополнительная дальность полёта снаряда за пределами дистанции выбора цели."},
            {"tankProjectileCollisionRadius","Радиус снаряда для проверки столкновений с объектами."}
        };
        internal static string Label(PlayableProfileField field)
        {
            if(CommonStat(field).label is string common)return common;
            if(Section(field)==Research)
            {
                if(field.Path.EndsWith(".cost",StringComparison.Ordinal)||field.FieldName.EndsWith("Cost",StringComparison.Ordinal))
                    return "Цена исследования";
                if(field.Path.EndsWith(".seconds",StringComparison.Ordinal)||field.FieldName is
                    "tankChassisSeconds" or "explorerAssaultSeconds" or "shkvalGuidanceSeconds" or "hawkSalvoResearchSeconds")
                    return "Время исследования";
                switch(field.FieldName)
                {
                    case "tankChassisSpeed":return "Максимальная скорость";
                    case "shkvalGuidanceRange":return "Дальность огня";
                    case "explorerAssaultBurstSize":return "Выстрелов в очереди";
                    case "explorerAssaultSpreadDeg":return "Разброс";
                    case "hawkUpgradedPreparationSeconds":return "Подготовка к выстрелу";
                    case "hawkUpgradedReloadSeconds":return "Перезарядка";
                }
            }
            return Labels.TryGetValue(field.Label,out var text)?text:field.Label;
        }
        internal static double DisplayScale(string unit)=>unit switch
        {
            "ms" or "мс"=>.001,
            "rad" or "рад" or "rad/s" or "рад/с" or "rad/s2" or "рад/с²"=>180/Math.PI,
            _=>1
        };
        internal static string Unit(string unit)=>unit switch
        {
            "credits" or "кред." or "кред"=>"кр.",
            "s" or "ms" or "мс"=>"с",
            "m"=>"м","m/s"=>"м/с","m/s2"=>"м/с²",
            "rad" or "рад" or "degrees" or "deg" or "град"=>"°",
            "rad/s" or "рад/с" or "degrees/s" or "deg/s"=>"°/с",
            "rad/s2" or "рад/с²" or "deg/s2"=>"°/с²",
            "battles"=>"боёв","ratio"=>"×","count" or "шт"=>"шт.","population"=>"мест",
            "index"=>"№","px"=>"пкс","orders"=>"приказов","orders/min"=>"пр./мин",
            "ratio/s"=>"доля/с","toggle"=>"0/1","weight"=>"баллов",
            "buildings"=>"зданий","units"=>"юнитов","tank"=>"Вепрей","changes"=>"смен",
            "intervals"=>"направлений","bands"=>"режимов","actors"=>"объектов",
            _=>unit
        };
    }
}
