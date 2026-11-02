using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    // UI consumes only a frozen publication. It never calculates gameplay contributions.
    public sealed partial class PostMatchResultsView : INativeMenuScope
    {
        public VisualElement Root{get;}
        public MatchResult Result{get;}
        public int SelectedTab{get;private set;}
        private readonly VisualElement content;
        private readonly HashSet<PlayableOwner> visible;
        private readonly List<ResultGraph> graphs=new List<ResultGraph>();
        private readonly Func<MatchResultPlayer,string> playerName;
        private readonly Func<MatchResultPlayer,Color> playerColor;
        private readonly Func<MatchFact,string> factName;
        private readonly Func<MatchFact,string> factIcon;
        private readonly double? apmWindow;
        private readonly bool showAiApm;
        private readonly Button[] tabs=new Button[3];
        private readonly int[] logSlots={0,1};private readonly bool[] aiLogs={false,false};
        public Button FirstTab=>tabs[0];
        public PostMatchResultsView(MatchResult result,Action overview,Action menu,Action repeat,
            Func<MatchResultPlayer,string> playerName=null,Func<MatchResultPlayer,Color> playerColor=null,
            Func<MatchFact,string> factName=null,Func<MatchFact,string> factIcon=null,double? apmWindow=60,bool showAiApm=false)
        {
            Result=result??throw new ArgumentNullException(nameof(result));this.apmWindow=apmWindow;this.showAiApm=showAiApm;
            this.playerName=playerName??(p=>p.Name!=p.Id?p.Name:(p.IsAi?"ИИ ":"Игрок ")+(p.Slot+1));this.playerColor=playerColor??(p=>Palette[p.Slot%Palette.Length]);
            this.factName=factName??FactLabel;this.factIcon=factIcon??(f=>f.Kind==MatchFactKind.AiDecision?"◆":f.Kind==MatchFactKind.ResearchOrdered||f.Kind==MatchFactKind.RefineryUpgradeOrdered?"◆":f.Kind==MatchFactKind.BuildingOrdered?"⬡":f.EntityKind==(int)PlayableEntityKind.Explorer?"◇":f.EntityKind==(int)PlayableEntityKind.Shkval?"△":"▣");
            visible=new HashSet<PlayableOwner>(result.Players.Select(p=>p.Owner));logSlots[1]=Math.Min(1,result.Players.Count-1);
            Root=new VisualElement{name="post-match-results"};Root.style.position=Position.Absolute;Root.style.left=0;Root.style.right=0;Root.style.top=0;Root.style.bottom=0;Root.style.backgroundColor=OrbitalTheme.Background;Root.style.paddingLeft=22;Root.style.paddingRight=22;Root.style.paddingTop=14;Root.style.paddingBottom=14;
            var header=Row();header.style.flexShrink=0;header.style.justifyContent=Justify.SpaceBetween;header.style.alignItems=Align.Center;Root.Add(header);
            var heading=new VisualElement();header.Add(heading);
            var title=Text(result.Manual?"МАТЧ ЗАВЕРШЁН":result.WinnerTeam.HasValue?"ПОБЕДА · КОМАНДА "+(result.WinnerTeam.Value+ (result.Players.Any(p=>p.Team==0)?1:0)):"РЕЗУЛЬТАТЫ МАТЧА",22,OrbitalTheme.Ink);title.name="result-title";heading.Add(title);
            var metadata=Text(TimeLabel(result.Duration)+"  ·  Профиль: "+result.ProfileLabel,11,OrbitalTheme.Muted);metadata.style.maxWidth=Length.Percent(48);metadata.style.whiteSpace=WhiteSpace.Normal;header.Add(metadata);
            var nav=Row();nav.style.flexShrink=0;nav.style.marginTop=6;nav.style.marginBottom=8;Root.Add(nav);
            string[] names={"Результаты","Графики","Лог действий"};for(int i=0;i<3;i++){int page=i;tabs[i]=OrbitalTheme.Action(names[i],()=>ShowTab(page),"result-tab-"+i);CompactButton(tabs[i]);tabs[i].style.flexGrow=1;nav.Add(tabs[i]);}
            content=new VisualElement{name="result-content"};content.style.flexGrow=1;content.style.minHeight=0;Root.Add(content);

            OrbitalTheme.Install(Root);Root.style.paddingBottom=42;OrbitalTheme.Footer(Root);
            var actions=Row();actions.name="result-actions";actions.style.flexShrink=0;actions.style.justifyContent=Justify.FlexEnd;actions.style.marginTop=8;Root.Add(actions);
            actions.Add(OrbitalTheme.Action("Обзор карты",overview,"result-overview"));actions.Add(OrbitalTheme.Action("В меню",menu,"result-menu"));actions.Add(OrbitalTheme.Action("Повторить",repeat,"result-repeat",true));foreach(var b in actions.Children()){CompactButton((Button)b);b.style.width=132;b.style.marginLeft=6;}
            InstallDetails();
            ShowTab(0);
        }
        internal static readonly Color GraphPanel=new Color(.065f,.10f,.12f);
        public static readonly Color[] Palette={new Color(.2f,.85f,.78f),new Color(.96f,.58f,.35f),new Color(.52f,.66f,1),new Color(.86f,.66f,.98f),new Color(.91f,.84f,.3f),new Color(.54f,.88f,.42f),new Color(.98f,.55f,.76f),new Color(.77f,.8f,.85f)};
        private static VisualElement Row(){var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;return row;}
        private static void CompactButton(Button button){button.style.minHeight=32;button.style.height=32;button.style.fontSize=14;button.style.marginBottom=0;button.style.paddingTop=4;button.style.paddingBottom=4;button.style.paddingLeft=12;button.style.paddingRight=12;}
        private static Label Text(string text,int size,Color color){var label=new Label(text);label.style.fontSize=size;label.style.color=color;label.style.marginBottom=6;return label;}
        public static string TimeLabel(double seconds){int n=(int)Math.Floor(seconds+1e-8);return (n/60).ToString("00")+":"+(n%60).ToString("00");}
        public void ShowTab(int index)
        {
            RememberFocus();CloseDetails(false);graphTip.style.display=DisplayStyle.None;inspecting=false;SelectedTab=index;content.Clear();graphs.Clear();activeMetric=-1;for(int i=0;i<tabs.Length;i++)tabs[i].EnableInClassList("orbital-primary",i==index);
            if(index==0)ShowTable();else if(index==1)ShowGraphs();else ShowLogs();
        }
        private void ShowLogs()
        {
            var row=Row();row.style.flexGrow=1;row.style.minHeight=0;row.style.flexWrap=Wrap.Wrap;content.Add(row);for(int side=0;side<2;side++)AddLog(row,side);
        }
        private void AddLog(VisualElement parent,int side)
        {
            var column=new VisualElement();column.style.flexGrow=1;column.style.flexBasis=Length.Percent(48);column.style.minWidth=270;column.style.minHeight=0;column.style.marginRight=8;parent.Add(column);
            var selector=new DropdownField("Игрок",Result.Players.Select(playerName).ToList(),logSlots[side]){name="result-log-player-"+side};column.Add(selector);
            var player=Result.Players[logSlots[side]];selector.style.color=playerColor(player);
            var toggle=new Toggle("Решения ИИ"){value=aiLogs[side],name="result-ai-log-"+side};ConfigureFilterToggle(toggle,OrbitalTheme.Ink);toggle.style.display=player.IsAi?DisplayStyle.Flex:DisplayStyle.None;column.Add(toggle);
            var scroll=OrbitalTheme.Scroll();scroll.style.flexGrow=1;scroll.style.minHeight=0;column.Add(scroll);
            void Render(){scroll.Clear();foreach(var f in Result.Facts.Where(f=>f.Owner==player.Owner&&(f.Kind==MatchFactKind.UnitOrdered||f.Kind==MatchFactKind.BuildingOrdered||f.Kind==MatchFactKind.RefineryUpgradeOrdered||f.Kind==MatchFactKind.ResearchOrdered||player.IsAi&&aiLogs[side]&&f.Kind==MatchFactKind.AiDecision)).OrderBy(f=>f.Tick).ThenBy(f=>f.Sequence)){var entry=Row();entry.name="result-log-event-"+f.Sequence;entry.style.alignItems=Align.Center;
                entry.Add(Text(TimeLabel(f.Seconds)+"   ",14,OrbitalTheme.Muted));
                if(f.Kind==MatchFactKind.AiDecision)entry.Add(Text(factIcon(f)+"   ",14,OrbitalTheme.Cyan));else{var icon=MakeEventIcon(f,25,true,includeOrders:true);icon.style.marginRight=4;entry.Add(icon);}
                var description=Text(factName(f),14,f.Kind==MatchFactKind.AiDecision?OrbitalTheme.Cyan:OrbitalTheme.Ink);description.style.whiteSpace=WhiteSpace.Normal;description.style.flexShrink=1;entry.Add(description);entry.style.paddingTop=8;entry.style.paddingBottom=8;entry.style.borderBottomWidth=1;entry.style.borderBottomColor=OrbitalTheme.Line;scroll.Add(entry);}if(scroll.childCount==0)scroll.Add(Text("Нет заказанных действий",13,OrbitalTheme.Muted));}
            toggle.RegisterValueChangedCallback(e=>{aiLogs[side]=e.newValue;Render();});selector.RegisterValueChangedCallback(_=>{logSlots[side]=selector.index;ShowTab(2);});Render();
        }
        public static string FactLabel(MatchFact fact)
        {
            if(fact.Kind==MatchFactKind.RefineryUpgradeOrdered||fact.Kind==MatchFactKind.RefineryUpgradeCompleted)return "Улучшение завода";
            if(fact.Kind==MatchFactKind.ResearchOrdered||fact.Kind==MatchFactKind.ResearchCompleted)return PlayableResearchRules.Name((PlayableResearchKind)fact.EntityKind);
            if(fact.Kind==MatchFactKind.UnitCompleted||fact.Kind==MatchFactKind.UnitOrdered)return OfflinePadWorldActions.Name((PlayableEntityKind)fact.EntityKind);
            if(fact.Kind==MatchFactKind.BuildingCompleted||fact.Kind==MatchFactKind.BuildingOrdered||fact.Kind==MatchFactKind.BuildingLost||fact.Kind==MatchFactKind.BuildingSold)return OfflinePadWorldActions.Name((PlayableBuildingKind)fact.EntityKind);
            if(fact.Kind!=MatchFactKind.AiDecision)return fact.Name;
            switch((MatchAiDecisionKind)fact.EntityKind){
                case MatchAiDecisionKind.Opening:return "Стартовый план: "+((PlayableAiOpening)(int)fact.Value switch {
                    PlayableAiOpening.Safe=>"безопасное развитие",PlayableAiOpening.GreedySafe=>"экономическое развитие",
                    PlayableAiOpening.GreedyMine=>"захват шахты",PlayableAiOpening.BlindRush=>"танковая атака",
                    PlayableAiOpening.ExplorerAllIn=>"атака исследователями",PlayableAiOpening.DoubleMineExplorerRush=>"две шахты и исследователи",
                    PlayableAiOpening.Air=>"воздушная атака",_=>"неизвестный план ("+fact.Value.ToString("0")+")"});
                case MatchAiDecisionKind.OpeningCompleted:return "Стартовый план завершён";
                case MatchAiDecisionKind.OpeningAborted:return "Стартовый план прерван";
                case MatchAiDecisionKind.Strategy:return "Стратегия: "+new[]{"массовый штурм","контроль карты","рейды"}[(int)fact.Value];
                default:return fact.Name;
            }
        }
    }
}
