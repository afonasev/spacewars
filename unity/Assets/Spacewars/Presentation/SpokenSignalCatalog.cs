using System.Collections.Generic;
using Spacewars.Simulation;
namespace Spacewars.Presentation
{
    // Stable authored vocabulary; keys also name packaged, offline speech clips.
    public static class SpokenSignalCatalog
    {
        public static readonly IReadOnlyDictionary<string,string> Phrases=new Dictionary<string,string>{
            {"system-building-attacked","Здание атаковано"},{"system-units-attacked","Юниты атакованы"},
            {"system-building-ready","Здание готово"},{"system-unit-ready","Юнит готов"},{"system-research-ready","Исследование готово"},
            {"system-unit-captured","Юнит захвачен"},{"system-mine-captured","Шахта захвачена"},{"system-relay-lost","Вышка потеряна"},
            {"system-mine-hit","Мина!"},{"system-mine-triggered","Мина сработала"},{"system-upgrade-ready","Улучшение готово"},
            {"system-army-limit","Лимит армии"},{"system-credits-needed","Нет кредитов"},{"system-exit-blocked","Выход заблокирован"},
            {"ally-attack","Атаковать!"},{"ally-defend","Защищать!"},{"ally-recon","Разведать!"},
            {"ally-advance","Выдвигаюсь"},{"ally-engage","Вступил в бой с противником!"},{"ally-raid","Начинаю рейд"},
            {"ally-retreat","Отступаю"},{"ally-need-defense","Нужна защита!"}};
        public static string Key(MatchEventKind kind)
        {
            switch(kind){
                case MatchEventKind.BuildingAttacked:return "system-building-attacked";
                case MatchEventKind.UnitsAttacked:return "system-units-attacked";
                case MatchEventKind.BuildingCompleted:return "system-building-ready";
                case MatchEventKind.UnitReady:return "system-unit-ready";
                case MatchEventKind.ResearchCompleted:return "system-research-ready";
                case MatchEventKind.UnitCaptured:return "system-unit-captured";
                case MatchEventKind.MineCaptured:return "system-mine-captured";
                case MatchEventKind.RelayControlLost:return "system-relay-lost";
                case MatchEventKind.MineHit:return "system-mine-hit";
                case MatchEventKind.MineTriggered:return "system-mine-triggered";
                case MatchEventKind.UpgradeCompleted:return "system-upgrade-ready";
                case MatchEventKind.ProductionCapacity:return "system-army-limit";
                case MatchEventKind.ProductionCredits:return "system-credits-needed";
                case MatchEventKind.ProductionExit:return "system-exit-blocked";
                default:return null;
            }
        }
        public static string Key(TeamSignalKind kind)=>kind==TeamSignalKind.Attack?"ally-attack":kind==TeamSignalKind.Defend?"ally-defend":"ally-recon";
        // Categorical urgency, not designer balance: loss/mine hit > combat > coordination > completion.
        public static int Priority(MatchEventKind kind)=>kind==MatchEventKind.MineHit||kind==MatchEventKind.UnitCaptured||kind==MatchEventKind.MineCaptured||kind==MatchEventKind.RelayControlLost?4:
            kind==MatchEventKind.BuildingAttacked||kind==MatchEventKind.UnitsAttacked?3:kind==MatchEventKind.BuildingCompleted||kind==MatchEventKind.UnitReady||kind==MatchEventKind.ResearchCompleted||kind==MatchEventKind.UpgradeCompleted?1:2;
    }
}
