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
            {"Chassis health bonus","Шасси · бонус здоровья"},{"Chassis price","Шасси · цена"},{"Chassis duration","Шасси · время исследования"},{"Chassis tank speed","Шасси · скорость танка"},
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
        internal const string Research="Исследования", Mechanics="Общие механики", Graphics="Графика и эффекты";
        private static string ResearchEntity(PlayableProfileField f)
        {
            if(f.Path.StartsWith("research.reinforcedConcrete."))return "Усиленный бетон";
            if(f.Path.StartsWith("research.centerUpgrade."))return "Улучшение научного центра";
            if(f.Path.StartsWith("research.weaponSystems."))return "Оружейные системы";
            if(f.Path.StartsWith("research.defensiveSystems."))return "Защитные системы";
            if(f.Path.StartsWith("research.tankChassis"))return "Улучшенное шасси";
            if(f.Path.StartsWith("research.explorerAssault"))return "Штурмовые орудия";
            if(f.Path.StartsWith("research.shkvalGuidance"))return "Улучшенное наведение";
            if(f.Group=="Ястреб · hawk-salvo-research-v1")return "Залповый огонь";
            return null;
        }
        private static bool Visual(PlayableProfileField f)
        {
            var n=f.FieldName??"";
            return f.Path.StartsWith("presentation.")||f.Group=="Selection ring"||f.Group.StartsWith("Бой ·")||
                n=="rallyFlagHeight"||n=="saleMarkerScale"||n=="refineryTurbineSpeed"||n=="tankChassisGlowIntensity"||
                n=="shkvalMarkerStartRadius"||n=="shkvalMarkerOpacity"||n.StartsWith("hawkDebris")||n=="hawkRotorSpeed"||n=="hawkMissileLength"||n=="hawkMissileVisualScale"||
                new[]{"gaussChargeIntensity","gaussRecoilMeters","gaussRecoilSeconds","gaussRecoilPeakFraction","gaussBeamGlowWidth","gaussBeamRiseFraction","gaussBeamSeconds","gaussBeamIntensity"}.Contains(n);
        }
        private static string ObjectEntity(PlayableProfileField f)
        {
            var n=(f.FieldName??"").ToLowerInvariant();
            if(n=="hawkroofseconds"||n=="hawkplatformseconds"||n=="hawkspinseconds")return "Ангар";
            if(n.StartsWith("gauss"))return "Гаусс-башня";
            if(n.StartsWith("repairtower"))return "Ремонтная вышка";
            if(n.StartsWith("turret"))return "Пулемётная турель";
            if(n.StartsWith("hawk"))return "Ястреб";
            if(n.Contains("tank"))return "Танк";
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
            if(f.Group=="Capture")return "Захват";
            if(f.Path.StartsWith("crowdMotion.")||f.Path.StartsWith("navigation."))return "Приказы и движение";
            if(f.FieldName=="unitOrderQueueLimit")return "Приказы и движение";
            if(f.FieldName=="idleAutoDefenseMultiplier")return "Бой";
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
            if(Visual(f))return Graphics;
            if(ResearchEntity(f)!=null||f.Path.StartsWith("research."))return Research;
            if(f.Path.StartsWith("production.")||f.Group=="Capture"||f.Group=="Construction"||f.Group.StartsWith("Здания · Жизненный"))return Mechanics;
            var obj=ObjectEntity(f);
            if(obj!=null)return obj=="Танк"||obj=="Исследователь"||obj=="Шквал"||obj=="Ястреб"?"Юниты":"Здания";
            if(f.Group=="Economy")return "Экономика";
            return Mechanics;
        }
        internal static string Entity(PlayableProfileField f)
        {
            string section=Section(f);
            if(section==Escalation){string name=f.FieldName??"";return name.StartsWith("orbitalMine",StringComparison.Ordinal)?"Мина-ловушка":name.StartsWith("orbitalHack",StringComparison.Ordinal)?"Взлом управления":name.StartsWith("orbitalScan",StringComparison.Ordinal)?"Сканирование":(name.StartsWith("orbitalThreshold",StringComparison.Ordinal))?"Пороги уровней":name.StartsWith("orbitalUi",StringComparison.Ordinal)||name.StartsWith("orbitalCard",StringComparison.Ordinal)||name=="orbitalPanelWidth"||name=="orbitalIconSize"||name=="orbitalLevelFontSize"?"Панель":"Перезарядка";}
            if(section==Research)return ResearchEntity(f)??"Прочие исследования";
            if(section=="ИИ")return f.Path.StartsWith("ai.research.")?"Исследования":"Боевое поведение";
            if(section==Mechanics)return SharedEntity(f);
            if(section=="Экономика")return "Доход и возвраты";
            if(section==Graphics)
            {
                if(f.Group.StartsWith("Здания · building-collapse"))return "Разрушение зданий";
                if(f.Group=="Selection ring")return "Круг выделения";
                if(f.Group.StartsWith("Бой ·"))return f.Group.Substring("Бой · ".Length);
                if(f.Path.StartsWith("presentation."))return f.Group;
                return ObjectEntity(f)??f.Group;
            }
            return ObjectEntity(f)??"Общие";
        }
        internal static string AiEntity(Spacewars.Simulation.Ai.AiProfileField f)=>f.Group.StartsWith("ИИ / ")?f.Group.Substring(5):f.Group;
        internal static string[] RelatedResearch(string obj)=>obj switch {
            "Танк"=>new[]{"Улучшенное шасси","Оружейные системы","Защитные системы"},
            "Исследователь"=>new[]{"Штурмовые орудия","Оружейные системы","Защитные системы"},
            "Шквал"=>new[]{"Улучшенное наведение","Оружейные системы","Защитные системы"},
            "Ястреб"=>new[]{"Залповый огонь","Оружейные системы","Защитные системы"},
            "Научный центр"=>new[]{"Улучшение научного центра","Усиленный бетон","Оружейные системы","Защитные системы"},
            _=>Array.Empty<string>()};
        internal static string Glyph(string name)=>name switch {
            "Ретранслятор"=>"relay-tower","Пулемётная турель"=>"sentry-turret","Гаусс-башня"=>"gauss-tower","Ремонтная вышка"=>"repair-tower",
            "Усиленный бетон"=>"reinforced-concrete","Ястреб"=>"hawk","Танк"=>"tank","Исследователь"=>"explorer","Шквал"=>"shkval","Штаб"=>"headquarters","Форпост"=>"outpost","Шахта"=>"mine","Фабрика"=>"factory","Ангар"=>"hangar","Переработчик"=>"refinery","Научный центр"=>"science","Улучшение научного центра"=>"science","Оружейные системы"=>"weapon-systems-1","Защитные системы"=>"defensive-systems-1",
            "Улучшенное шасси"=>"tank","Штурмовые орудия"=>"explorer","Улучшенное наведение"=>"shkval","Залповый огонь"=>"hawk",_=>null};
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
            return "Основные параметры";
        }
        internal static string Help(PlayableProfileField f)
        {
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
            {"tankChassisSeconds","Время активного исследования улучшенного шасси танка."},
            {"tankChassisHealthBonusPercent","Бонус максимального здоровья танка после шасси, перемножается с защитными системами; сохраняет долю здоровья."},
            {"tankChassisSpeed","Скорость танков владельца после завершения исследования шасси."},
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
        internal static string Label(PlayableProfileField field)=>Labels.TryGetValue(field.Label,out var text)?text:field.Label;
        internal static string Unit(string value){switch(value){case "credits":return "кр.";case "s":return "с";case "ms":return "мс";case "m":return "м";case "m/s":return "м/с";case "rad/s":return "рад/с";case "ratio":return "×";case "degrees":return "°";case "count":return "шт.";case "population":return "мест";case "orders":return "приказов";case "index":return "№";case "degrees/s":return "°/с";case "weight":return "вес";case "buildings":return "зданий";case "units":return "юнитов";case "changes":return "смен";case "intervals":return "направлений";case "bands":return "режимов";case "actors":return "объектов";default:return value;}}
    }
}
