using System;
using System.Collections.Generic;
using Spacewars.Simulation;

namespace Spacewars.Presentation
{
    internal static class NativeBalanceLabels
    {
        private static readonly Dictionary<string,string> Labels=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
            {"Science health","Научный центр · здоровье"},{"Science construction time","Научный центр · время строительства"},{"Science vision","Научный центр · обзор"},{"Science price","Научный центр · цена"},
            {"Upgrade price","Улучшение переработчика · цена"},{"Upgrade duration","Улучшение переработчика · время"},{"Upgraded income","Улучшенный переработчик · доход"},{"Turbine rotation","Скорость вращения турбины"},
            {"Chassis price","Шасси · цена"},{"Chassis duration","Шасси · время исследования"},{"Chassis tank speed","Шасси · скорость танка"},
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
            {"Army capacity","Вместимость армии"},{"Following distance","Дистанция следования"},{"Following arrival tolerance","Допуск прибытия при следовании"},{"Idle auto-defense range","Дальность автообороны"},{"Enemy advance delay","Задержка наступления противника"},{"Attack approach","Дистанция сближения для атаки"},{"Attack repath","Интервал обновления пути атаки"}
        };
        internal static string Label(PlayableProfileField field)=>Labels.TryGetValue(field.Label,out var text)?text:field.Label;
        internal static string Unit(string value){switch(value){case "credits":return "кр.";case "s":return "с";case "ms":return "мс";case "m":return "м";case "m/s":return "м/с";case "rad/s":return "рад/с";case "ratio":return "×";case "degrees":return "°";case "count":return "шт.";default:return value;}}
    }
}
