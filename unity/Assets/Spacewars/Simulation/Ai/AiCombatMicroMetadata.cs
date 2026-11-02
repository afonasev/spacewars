using System.Collections.Generic;

namespace Spacewars.Simulation.Ai
{
    // ai-combat-control-v1: initial tuning candidates, not measured human APM.
    public static class AiCombatMicroMetadata
    {
        public static IEnumerable<AiProfileField> Fields()
        {
            foreach(var difficulty in new[]{AiDifficulty.Recruit,AiDifficulty.Fighter,AiDifficulty.Veteran})
            {
                int i=(int)difficulty;string prefix="difficulty."+AiProfile.DifficultyId(difficulty)+".";
                string group="ИИ / Контроль / "+new[]{"Новичок","Боец","Ветеран"}[i];
                AiProfileField F(string key,string label,string text,string unit,double min,double max,double step,double value,bool integral=false)=>new AiProfileField(prefix+key,group,label,text,unit,min,max,step,value,integral);
                yield return F("control.ordersPerMinute","Полезных приказов в минуту","Общий темп по всей карте: микро, производство, здания, исследования и срочные приказы. Не измеренные человеческие APM.","orders/min",1,300,1,new[]{30,70,120}[i],true);
                yield return F("control.burst","Короткая серия","Максимум накопленных полезных приказов; простой не увеличивает запас сверх серии.","orders",1,8,1,new[]{2,3,4}[i],true);
                yield return F("control.fairRun","Серия одного вида задач","После этого числа полезных macro или micro приказов ожидающая другая сторона получает очередь.","orders",1,8,1,2,true);
                yield return F("control.reactionMaxSeconds","Верхняя граница реакции","Задержка выбирается между существующей минимальной реакцией сложности и этой границей один раз для нового намерения.","s",.1,6,.05,new[]{3d,1.5,.7}[i]);
                yield return F("micro.intensity","Интенсивность микро","Доля возможностей, на которые ИИ обращает внимание; выбор воспроизводим и не даёт скрытых данных.","ratio",0,1,.05,new[]{.1,.65,1}[i]);
                yield return F("micro.withdrawEnabled","Отвод и возврат","Разрешает короткий отход от фактического фокуса и возвращение после снятия опасности.","toggle",0,1,1,1,true);
                yield return F("micro.repairEnabled","Отправка на ремонт","Разрешает ремонт сильно пробитых в бою и свободных раненых вне боя.","toggle",0,1,1,new[]{0,1,1}[i],true);
                yield return F("micro.focusEnabled","Локальный фокус","Координирует доступные наземные цели без опасного подхода.","toggle",0,1,1,new[]{0,1,1}[i],true);
                yield return F("micro.airFocusEnabled","Фокус вертолётов","Координирует доступные воздушной группе цели с защитой от ПВО и отрыва.","toggle",0,1,1,new[]{0,1,1}[i],true);
                yield return F("micro.chaseEnabled","Прекращение опасной погони","Сменяет удаляющуюся цель при доступной местной альтернативе. Безопасность уже выбранного локального фокуса сохраняется.","toggle",0,1,1,new[]{0,1,1}[i],true);
                yield return F("micro.observedFireSeconds","Память видимого огня","Сколько хранится факт реально видимого выстрела известного противника по своему юниту; не раскрывает выбранные противником приказы.","s",.1,10,.1,2);
                yield return F("micro.focusAttackers","Сильный фокус","Минимум фактически атакующих видимых противников для отвода повреждённого юнита.","units",2,12,1,3,true);
                yield return F("micro.withdrawHealth","Здоровье для отвода","При сильном фокусе и фактическом уроне ниже этой доли здоровья проверяется безопасный отвод.","ratio",.05,1,.05,.7);
                yield return F("micro.lossPerSecond","Быстрая потеря здоровья","Доля максимального здоровья в секунду, позволяющая реагировать на сильный фактический урон.","ratio/s",.01,2,.01,.12);
                yield return F("micro.shortStep","Короткий отвод","Первое расстояние поиска безопасной позиции от атакующих.","m",.5,20,.5,4);
                yield return F("micro.maxWithdrawal","Глубина отвода","Максимальная глубина поиска, если короткое смещение не выводит из опасности.","m",1,40,.5,12);
                yield return F("micro.safetyMargin","Запас вне дальности","Дополнительная дистанция к наблюдаемой дальности атакующих при выборе безопасной точки.","m",0,8,.25,1);
                yield return F("micro.safeSeconds","Покой перед возвратом","Непрерывный интервал без фактического фокуса перед возвращением; защищает от дёрганья.","s",.1,10,.1,1.5);
                yield return F("micro.cooldownSeconds","Пауза повторного микро","Минимальный интервал нового локального приёма одного юнита после завершения.","s",.1,30,.1,new[]{8d,3,1.5}[i]);
                yield return F("micro.maxPhaseSeconds","Предел локального приёма","Ограничивает время временного micro владения, включая неудавшееся движение.","s",1,120,1,30);
                yield return F("micro.focusGroupSize","Стрелков на цель","Предел одного локального focus приказа; остаток выбирает другие доступные цели.","units",1,8,1,new[]{2,3,4}[i],true);
                yield return F("micro.overkillRatio","Допуск избыточного урона","При распределении фокуса учитывает уже назначенный ближайший огневой импульс.","ratio",1,3,.1,1.2);
                yield return F("micro.supportRadius","Радиус поддержки","Вертолёт не выбирает цель, уводящую его за пределы собственной боеспособной поддержки.","m",2,60,1,18);
                yield return F("micro.combatRepairHealth","Срочный ремонт","Порог здоровья для выхода из боя к доступной своей Ready вышке.","ratio",.05,.8,.05,.3);
                yield return F("micro.idleRepairHealth","Ремонт вне боя","Порог здоровья свободного юнита, не выполняющего атаку или срочную задачу.","ratio",.1,1,.05,.85);
                yield return F("micro.repairReturnHealth","Возврат после ремонта","Доля здоровья для безопасного возвращения к текущему боеспособному ядру.","ratio",.2,1,.05,.9);
                yield return F("micro.repairTravelSeconds","Разумный путь ремонта","Предел оценки пути и ожидания лечения, без бесплатного лечения или телепортации.","s",1,180,1,45);
                yield return F("micro.returnCoreUnits","Живое ядро для возврата","Минимум живых боеспособных бойцов рядом с текущей безопасной точкой группы.","units",2,12,1,2,true);
                yield return F("micro.returnCoreHealth","Здоровье ядра","Минимальная доля здоровья бойца, учитываемого в актуальном ядре для возврата.","ratio",.1,1,.05,.4);
            }
        }
    }
}
