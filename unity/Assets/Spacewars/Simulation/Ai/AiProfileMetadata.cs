using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation.Ai
{
    public sealed class AiProfileField
    {
        public string Path {get;} public string Group {get;} public string Label {get;} public string Description {get;} public string Unit {get;}
        public double Minimum {get;} public double Maximum {get;} public double Step {get;} public double Initial {get;} public bool Integral {get;}
        public AiProfileField(string path,string group,string label,string description,string unit,double minimum,double maximum,double step,double initial,bool integral)
        {Path=path;Group=group;Label=label;Description=description;Unit=unit;Minimum=minimum;Maximum=maximum;Step=step;Initial=initial;Integral=integral;}
        public void Validate(double value)
        {if(double.IsNaN(value)||double.IsInfinity(value)||value<Minimum||value>Maximum||(Integral&&value!=Math.Truncate(value)))throw new ArgumentOutOfRangeException(Path);}
    }
    // Initial candidates copied from native-strategic-ai/tuning-baseline.json, not measured balance.
    // Later packages consume their fields; merely registering them does not implement their policies.
    public static class AiProfileMetadata
    {
        public static IReadOnlyList<AiProfileField> Fields {get;}=Array.AsReadOnly(new AiProfileField[] {
            new AiProfileField("difficulty.recruit.majorArmies","ИИ / Сложность","Максимум основных армий","Потолок major armies; увеличение сверх утверждённого cap запрещено.","groups",1d,2d,1d,2d,true),
            new AiProfileField("difficulty.recruit.decisionSeconds","ИИ / Сложность","Интервал решений","Интервал owner strategic decisions.","s",0.25d,6d,0.05d,3d,false),
            new AiProfileField("difficulty.recruit.reactionSeconds","ИИ / Сложность","Задержка реакции","Минимальная задержка реакции на новый observed fact.","s",0.1d,5d,0.05d,2d,false),
            new AiProfileField("difficulty.recruit.actionsPerDecision","ИИ / Сложность","Команд за решение","Максимум принятых действий за решение; budget независим от игрового дохода.","commands",1d,8d,1d,2d,true),
            new AiProfileField("difficulty.recruit.estimateNoise","ИИ / Сложность","Ошибка оценки","Ограниченный seeded шум оценки полезности, не игровой урон.","ratio",0d,0.5d,0.01d,0.25d,false),
            new AiProfileField("difficulty.recruit.scoutAssignments","ИИ / Сложность","Разведывательных назначений","Число отдельных разведывательных миссий; combat raid требует major army.","assignments",1d,3d,1d,1d,true),
            new AiProfileField("difficulty.fighter.majorArmies","ИИ / Сложность","Максимум основных армий","Потолок major armies; увеличение сверх утверждённого cap запрещено.","groups",1d,3d,1d,3d,true),
            new AiProfileField("difficulty.fighter.decisionSeconds","ИИ / Сложность","Интервал решений","Интервал owner strategic decisions.","s",0.25d,6d,0.05d,1.5d,false),
            new AiProfileField("difficulty.fighter.reactionSeconds","ИИ / Сложность","Задержка реакции","Минимальная задержка реакции на новый observed fact.","s",0.1d,5d,0.05d,1d,false),
            new AiProfileField("difficulty.fighter.actionsPerDecision","ИИ / Сложность","Команд за решение","Максимум принятых действий за решение; budget независим от игрового дохода.","commands",1d,8d,1d,3d,true),
            new AiProfileField("difficulty.fighter.estimateNoise","ИИ / Сложность","Ошибка оценки","Ограниченный seeded шум оценки полезности, не игровой урон.","ratio",0d,0.5d,0.01d,0.12d,false),
            new AiProfileField("difficulty.fighter.scoutAssignments","ИИ / Сложность","Разведывательных назначений","Число отдельных разведывательных миссий; combat raid требует major army.","assignments",1d,3d,1d,2d,true),
            new AiProfileField("difficulty.veteran.majorArmies","ИИ / Сложность","Максимум основных армий","Потолок major armies; увеличение сверх утверждённого cap запрещено.","groups",1d,4d,1d,4d,true),
            new AiProfileField("difficulty.veteran.decisionSeconds","ИИ / Сложность","Интервал решений","Интервал owner strategic decisions.","s",0.25d,6d,0.05d,0.5d,false),
            new AiProfileField("difficulty.veteran.reactionSeconds","ИИ / Сложность","Задержка реакции","Минимальная задержка реакции на новый observed fact.","s",0.1d,5d,0.05d,0.3d,false),
            new AiProfileField("difficulty.veteran.actionsPerDecision","ИИ / Сложность","Команд за решение","Максимум принятых действий за решение; budget независим от игрового дохода.","commands",1d,8d,1d,4d,true),
            new AiProfileField("difficulty.veteran.estimateNoise","ИИ / Сложность","Ошибка оценки","Ограниченный seeded шум оценки полезности, не игровой урон.","ratio",0d,0.5d,0.01d,0.04d,false),
            new AiProfileField("difficulty.veteran.scoutAssignments","ИИ / Сложность","Разведывательных назначений","Число отдельных разведывательных миссий; combat raid требует major army.","assignments",1d,3d,1d,3d,true),
            new AiProfileField("decision.repeatOrderSeconds","ИИ / Внимание","Интервал повторного приказа","Cooldown одинакового army order.","s",0.5d,10d,0.5d,2d,false),
            new AiProfileField("decision.intentAgingSeconds","ИИ / Внимание","Срок повышения приоритета","Время подъёма полезного ожидающего intent в приоритете.","s",1d,60d,1d,10d,false),
            new AiProfileField("decision.retrySeconds","ИИ / Внимание","Пауза после отказа","Backoff отказа на неизменившихся условиях.","s",1d,20d,1d,3d,false),
            new AiProfileField("decision.retryLimit","ИИ / Внимание","Предел повторных попыток","После лимита отмена намерения/новая оценка причины.","attempts",1d,8d,1d,3d,true),
            new AiProfileField("economy.incomeWindowSeconds","ИИ / Экономика","Окно оценки дохода","Окно settled income и capacity.","s",5d,120d,5d,30d,false),
            new AiProfileField("economy.reserveSeconds","ИИ / Экономика","Горизонт резерва","Резерв в пределах этого горизонта подтверждённого расхода.","s",0d,60d,1d,15d,false),
            new AiProfileField("economy.reservationExpirySeconds","ИИ / Экономика","Срок денежного обязательства","Максимальный срок стратегического накопления без прогресса.","s",5d,180d,5d,45d,false),
            new AiProfileField("economy.excessBankDeadlineSeconds","ИИ / Экономика","Допустимое ожидание расхода","Окно допустимого excess bank при useful affordable action.","s",5d,120d,5d,20d,false),
            new AiProfileField("economy.idleLineDeadlineSeconds","ИИ / Экономика","Допустимый простой линии","Окно незагруженной affordable production line.","s",1d,30d,1d,8d,false),
            new AiProfileField("economy.factoryUtilization","ИИ / Экономика","Загрузка перед расширением","Минимальная загрузка для устойчивого scaling.","ratio",0.4d,1d,0.05d,0.8d,false),
            new AiProfileField("economy.factoryLaunchCycles","ИИ / Экономика","Финансируемые циклы новой линии","Сколько циклов новой линии нужно профинансировать при scaling.","cycles",1d,5d,1d,2d,true),
            new AiProfileField("economy.expansionRiskRatio","ИИ / Экономика","Риск расширения","Порог известной угрозы относительно выделяемой силы.","ratio",0.2d,1.5d,0.05d,0.8d,false),
            new AiProfileField("economy.repairPriorityHealthRatio","ИИ / Экономика","Порог приоритета ремонта","Порог ускоренного приоритета полезного ремонта; legality игры отдельно.","ratio",0.1d,0.95d,0.05d,0.6d,false),
            new AiProfileField("armies.minimumUnits","ИИ / Армии","Минимум юнитов новой армии","Стартовый порог viability новой major group; role force также проверяется.","units",2d,12d,1d,3d,true),
            new AiProfileField("armies.assemblyDeadlineSeconds","ИИ / Армии","Срок сбора армии","Deadline сбора; переход к меньшей полезной цели либо regroup.","s",5d,90d,5d,20d,false),
            new AiProfileField("armies.stallSeconds","ИИ / Армии","Срок без продвижения","Нет meaningful progress — перепланирование.","s",10d,120d,5d,30d,false),
            new AiProfileField("armies.offensiveOverdueSeconds","ИИ / Армии","Срок до новой инициативы","Deadline полезной инициативы при доступной достаточной силе.","s",10d,180d,5d,45d,false),
            new AiProfileField("armies.retreatThreatRatio","ИИ / Армии","Соотношение сил для отхода","Observed local hostile force / own force для оценки отхода.","ratio",1d,3d,0.05d,1.35d,false),
            new AiProfileField("armies.reinforcementMinimumUnits","ИИ / Армии","Минимум подкрепления","Группировка подкреплений; безопасный короткий join может идти раньше.","units",1d,8d,1d,2d,true),
            new AiProfileField("strategy.commitmentSeconds","ИИ / Стратегия","Приверженность стратегии","Минимальное сохранение обычного плана кроме emergency.","s",5d,120d,5d,30d,false),
            new AiProfileField("strategy.switchMargin","ИИ / Стратегия","Преимущество для смены","Преимущество нового плана над действующим.","utility",0d,0.5d,0.01d,0.15d,false),
            new AiProfileField("scouting.revisitSeconds","ИИ / Разведка","Интервал повторной разведки","Срок перепроверки важных известных площадок.","s",10d,180d,5d,45d,false),
            new AiProfileField("scouting.contactHalfLifeSeconds","ИИ / Разведка","Старение разведданных","Период снижения уверенности устаревшего контакта вдвое.","s",10d,300d,5d,60d,false),
            new AiProfileField("scouting.blindRushBudgetScale","ИИ / Разведка","Разведка при слепом раше","Доля обычного scouting budget при blind rush.","ratio",0d,1d,0.05d,0.25d,false),
            new AiProfileField("opening.forceUnits","ИИ / Дебюты","Начальная боевая группа","Минимум готовых Tank для военного milestone; ArmyRegistry дополнительно проверяет жизнеспособность.","units",1d,12d,1d,3d,true),
            new AiProfileField("opening.greedyRefineries","ИИ / Дебюты","Доход жадного дебюта","Готовых Refinery до перехода к фабрике.","buildings",1d,6d,1d,2d,true),
            new AiProfileField("opening.deadlineSeconds","ИИ / Дебюты","Срок выполнения дебюта","Fallback deadline дебюта, если milestone не продвигается.","s",30d,300d,10d,120d,false),
            new AiProfileField("phase.minimumResidenceSeconds","ИИ / Фазы игры","Минимальная длительность фазы","Hysteresis early/mid/late.","s",10d,180d,5d,30d,false),
            new AiProfileField("phase.midIncomeStartingRatio","ИИ / Фазы игры","Доход для средней фазы","Settled income относительно стартового для раннего mid signal.","ratio",1d,6d,0.25d,2d,false),
            new AiProfileField("phase.lateIncomeStartingRatio","ИИ / Фазы игры","Доход для поздней фазы","Доходный late signal совместно с tech/force.","ratio",2d,12d,0.25d,4d,false),
            new AiProfileField("phase.midFallbackSeconds","ИИ / Фазы игры","Предельное время ранней фазы","Временной fallback в mid если экономический сигнал недоступен.","s",120d,600d,30d,240d,false),
            new AiProfileField("phase.lateFallbackSeconds","ИИ / Фазы игры","Предельное время средней фазы","Временной fallback в late.","s",300d,1800d,30d,600d,false),
            new AiProfileField("team.claimTtlSeconds","ИИ / Команда","Срок командного назначения","Срок claim без наблюдаемого прогресса/подтверждения.","s",5d,120d,5d,30d,false),
            new AiProfileField("team.maximumAssemblyWaitSeconds","ИИ / Команда","Ожидание союзника","Предел ожидания совместной атаки.","s",0d,60d,1d,15d,false),
            new AiProfileField("team.minimumHomeReserveShare","ИИ / Команда","Доля домашнего резерва","Доля собственной боевой силы для разумной домашней защиты при помощи.","ratio",0d,0.6d,0.05d,0.2d,false),
            new AiProfileField("endgame.growDeadlineSeconds","ИИ / Поздняя игра","Срок оборонительного накопления","Максимум defend-grow без перехода к новому плану.","s",30d,360d,10d,120d,false),
            new AiProfileField("crisis.deficitRatio","ИИ / Кризис","Порог экономического отставания","Own estimated sustainable income / enemy conservative estimate для crisis signal.","ratio",0.2d,0.9d,0.05d,0.55d,false),
            new AiProfileField("crisis.minimumConfidence","ИИ / Кризис","Уверенность для кризисной оценки","Минимальная уверенность в экономическом дефиците.","ratio",0.3d,0.95d,0.05d,0.6d,false),
            new AiProfileField("crisis.sustainSeconds","ИИ / Кризис","Длительность плохой ситуации","Сколько должен держаться adverse signal до эпизода.","s",10d,120d,5d,30d,false),
            new AiProfileField("crisis.riskProbability","ИИ / Кризис","Вероятность последнего удара","Базовая вероятность all-in, одна проба на episode; характер корректирует.","probability",0d,0.9d,0.05d,0.35d,false),
            new AiProfileField("crisis.episodeCooldownSeconds","ИИ / Кризис","Пауза между кризисами","Минимальный интервал между раздельными crisis episodes.","s",30d,300d,10d,90d,false),
            new AiProfileField("crisis.preparationDeadlineSeconds","ИИ / Кризис","Срок подготовки последнего удара","Максимум подготовки all-in до выхода/обоснованной отмены.","s",5d,90d,5d,30d,false),
            new AiProfileField("crisis.maximumLiquidatedValueShare","ИИ / Кризис","Предел продажи инфраструктуры","Предел доли стоимости инфраструктуры в sale plan; survival exclusions жёсткие.","ratio",0d,0.6d,0.05d,0.35d,false),
            new AiProfileField("opening.weights.safe","ИИ / Дебюты","Безопасный дебют","Базовый вес допустимого opening до поправок характера.","weight",0.1d,5d,0.1d,1d,false),
            new AiProfileField("opening.weights.greedy","ИИ / Дебюты","Жадный дебют","Базовый вес допустимого opening до поправок характера.","weight",0.1d,5d,0.1d,1d,false),
            new AiProfileField("opening.weights.expansion","ИИ / Дебюты","Дебют расширения","Базовый вес допустимого opening до поправок характера.","weight",0.1d,5d,0.1d,1d,false),
            new AiProfileField("opening.weights.blindRush","ИИ / Дебюты","Слепой раш","Базовый вес допустимого opening до поправок характера.","weight",0.1d,5d,0.1d,1d,false),
            new AiProfileField("opening.weights.scoutPressure","ИИ / Дебюты","Атака после разведки","Базовый вес допустимого opening до поправок характера.","weight",0.1d,5d,0.1d,1d,false),
            new AiProfileField("composition.early.reconBudgetShare","ИИ / Состав","Доля разведки","Доля бюджета; нормализовать все роли фазы, исключить unavailable roles.","ratio",0d,1d,0.05d,0.1d,false),
            new AiProfileField("composition.early.lineBudgetShare","ИИ / Состав","Доля основной линии","Доля бюджета; нормализовать все роли фазы, исключить unavailable roles.","ratio",0d,1d,0.05d,0.75d,false),
            new AiProfileField("composition.early.supportBudgetShare","ИИ / Состав","Доля поддержки","Доля бюджета; нормализовать все роли фазы, исключить unavailable roles.","ratio",0d,1d,0.05d,0.15d,false),
            new AiProfileField("composition.mid.reconBudgetShare","ИИ / Состав","Доля разведки","Доля бюджета; нормализовать все роли фазы, исключить unavailable roles.","ratio",0d,1d,0.05d,0.1d,false),
            new AiProfileField("composition.mid.lineBudgetShare","ИИ / Состав","Доля основной линии","Доля бюджета; нормализовать все роли фазы, исключить unavailable roles.","ratio",0d,1d,0.05d,0.6d,false),
            new AiProfileField("composition.mid.supportBudgetShare","ИИ / Состав","Доля поддержки","Доля бюджета; нормализовать все роли фазы, исключить unavailable roles.","ratio",0d,1d,0.05d,0.3d,false),
            new AiProfileField("composition.late.reconBudgetShare","ИИ / Состав","Доля разведки","Доля бюджета; нормализовать все роли фазы, исключить unavailable roles.","ratio",0d,1d,0.05d,0.05d,false),
            new AiProfileField("composition.late.lineBudgetShare","ИИ / Состав","Доля основной линии","Доля бюджета; нормализовать все роли фазы, исключить unavailable roles.","ratio",0d,1d,0.05d,0.55d,false),
            new AiProfileField("composition.late.supportBudgetShare","ИИ / Состав","Доля поддержки","Доля бюджета; нормализовать все роли фазы, исключить unavailable roles.","ratio",0d,1d,0.05d,0.4d,false),
            new AiProfileField("utility.target.threat","ИИ / Оценка целей","Вес угрозы","Вес нормализованного признака из algorithm-recipes.md.","weight",0d,1d,0.05d,0.3d,false),
            new AiProfileField("utility.target.economicValue","ИИ / Оценка целей","Вес экономической ценности","Вес нормализованного признака из algorithm-recipes.md.","weight",0d,1d,0.05d,0.25d,false),
            new AiProfileField("utility.target.reachability","ИИ / Оценка целей","Вес достижимости","Вес нормализованного признака из algorithm-recipes.md.","weight",0d,1d,0.05d,0.2d,false),
            new AiProfileField("utility.target.opportunity","ИИ / Оценка целей","Вес возможности атаки","Вес нормализованного признака из algorithm-recipes.md.","weight",0d,1d,0.05d,0.15d,false),
            new AiProfileField("utility.target.newFrontCost","ИИ / Оценка целей","Цена нового фронта","Вес нормализованного признака из algorithm-recipes.md.","weight",0d,1d,0.05d,0.1d,false),
            new AiProfileField("utility.scout.informationGain","ИИ / Оценка целей","Ценность разведданных","Вес нормализованного признака из algorithm-recipes.md.","weight",0d,1d,0.05d,0.5d,false),
            new AiProfileField("utility.scout.travelCost","ИИ / Оценка целей","Цена пути","Вес нормализованного признака из algorithm-recipes.md.","weight",0d,1d,0.05d,0.2d,false),
            new AiProfileField("utility.scout.riskCost","ИИ / Оценка целей","Цена разведывательного риска","Вес нормализованного признака из algorithm-recipes.md.","weight",0d,1d,0.05d,0.3d,false),
            new AiProfileField("utility.forceHorizonSeconds","ИИ / Оценка силы","Общий горизонт оценки урона","Один горизонт DPS для всех unit kinds/сторон; не собственный reload.","s",1d,30d,1d,5d,false),
        });
        public static AiProfileField Field(string path)=>Fields.Single(f=>f.Path==path);
    }
}
