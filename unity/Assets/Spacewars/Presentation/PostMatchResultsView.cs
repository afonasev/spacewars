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
    public sealed class PostMatchResultsView
    {
        public VisualElement Root{get;}
        public MatchResult Result{get;}
        public int SelectedTab{get;private set;}
        private readonly VisualElement content;
        private readonly Label values;
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
            this.factName=factName??FactLabel;this.factIcon=factIcon??(f=>f.Kind==MatchFactKind.AiDecision?"◆":f.Kind==MatchFactKind.BuildingCompleted?"⬡":f.EntityKind==(int)PlayableEntityKind.Explorer?"◇":f.EntityKind==(int)PlayableEntityKind.Shkval?"△":"▣");
            visible=new HashSet<PlayableOwner>(result.Players.Select(p=>p.Owner));logSlots[1]=Math.Min(1,result.Players.Count-1);
            Root=new VisualElement{name="post-match-results"};Root.style.position=Position.Absolute;Root.style.left=0;Root.style.right=0;Root.style.top=0;Root.style.bottom=0;Root.style.backgroundColor=OrbitalTheme.Background;Root.style.paddingLeft=22;Root.style.paddingRight=22;Root.style.paddingTop=14;Root.style.paddingBottom=14;
            var header=Row();header.style.flexShrink=0;header.style.justifyContent=Justify.SpaceBetween;header.style.alignItems=Align.Center;Root.Add(header);
            var heading=new VisualElement();header.Add(heading);
            var title=Text(result.Manual?"МАТЧ ЗАВЕРШЁН":result.WinnerTeam.HasValue?"ПОБЕДА · КОМАНДА "+(result.WinnerTeam.Value+ (result.Players.Any(p=>p.Team==0)?1:0)):"РЕЗУЛЬТАТЫ МАТЧА",22,OrbitalTheme.Ink);title.name="result-title";heading.Add(title);
            var metadata=Text(TimeLabel(result.Duration)+"  ·  Профиль: "+result.ProfileLabel,11,OrbitalTheme.Muted);metadata.style.maxWidth=Length.Percent(48);metadata.style.whiteSpace=WhiteSpace.Normal;header.Add(metadata);
            var nav=Row();nav.style.flexShrink=0;nav.style.marginTop=6;nav.style.marginBottom=8;Root.Add(nav);
            string[] names={"Результаты","Графики","Лог действий"};for(int i=0;i<3;i++){int page=i;tabs[i]=OrbitalTheme.Action(names[i],()=>ShowTab(page),"result-tab-"+i);CompactButton(tabs[i]);tabs[i].style.flexGrow=1;nav.Add(tabs[i]);}
            content=new VisualElement{name="result-content"};content.style.flexGrow=1;content.style.minHeight=0;Root.Add(content);
            values=Text("",12,OrbitalTheme.Ink);values.name="result-graph-values";values.style.whiteSpace=WhiteSpace.Normal;
            OrbitalTheme.Install(Root);Root.style.paddingBottom=42;OrbitalTheme.Footer(Root);
            var actions=Row();actions.name="result-actions";actions.style.flexShrink=0;actions.style.justifyContent=Justify.FlexEnd;actions.style.marginTop=8;Root.Add(actions);
            actions.Add(OrbitalTheme.Action("Обзор карты",overview,"result-overview"));actions.Add(OrbitalTheme.Action("В меню",menu,"result-menu"));actions.Add(OrbitalTheme.Action("Повторить",repeat,"result-repeat",true));foreach(var b in actions.Children()){CompactButton((Button)b);b.style.width=132;b.style.marginLeft=6;}
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
            SelectedTab=index;content.Clear();graphs.Clear();for(int i=0;i<tabs.Length;i++)tabs[i].EnableInClassList("orbital-primary",i==index);
            if(index==0)ShowTable();else if(index==1)ShowGraphs();else ShowLogs();
        }
        private void ShowTable()
        {
            var scroll=OrbitalTheme.Scroll(ScrollViewMode.VerticalAndHorizontal);scroll.style.flexGrow=1;content.Add(scroll);
            string[] columns={"Кредиты","Построено\nюнитов","Построено\nзданий","Уничтожено\nюнитов","Уничтожено\nзданий","Очки"};
            var header=Row();header.style.minWidth=960;header.style.backgroundColor=OrbitalTheme.Panel;scroll.Add(header);
            void Cell(VisualElement row,string text,float width,Color color,bool numeric=false){var label=Text(text,14,color);label.style.width=width;label.style.minWidth=width;label.style.paddingLeft=12;label.style.paddingTop=6;label.style.paddingBottom=6;if(numeric)label.style.unityTextAlign=TextAnchor.MiddleRight;row.Add(label);}
            if(Result.TeamsVisible)Cell(header,"Команда",90,OrbitalTheme.Muted);Cell(header,"Игрок",210,OrbitalTheme.Muted);foreach(var column in columns)Cell(header,column,110,OrbitalTheme.Muted,true);
            foreach(var player in Result.Ranked){var row=Row();row.name="result-player-"+player.Slot;row.style.minWidth=960;row.style.borderBottomWidth=1;row.style.borderBottomColor=OrbitalTheme.Line;scroll.Add(row);
                if(Result.TeamsVisible)Cell(row,(player.Team+(Result.Players.Any(p=>p.Team==0)?1:0)).ToString(),90,OrbitalTheme.Muted);Cell(row,playerName(player),210,playerColor(player));
                for(int c=0;c<6;c++)Cell(row,(Result.IsMaximum(player,c)?"★ ":"")+player.Column(c).ToString("0"),110,c==0||c==5?new Color(.95f,.76f,.43f):OrbitalTheme.Ink,true);
            }
            scroll.Add(Text("ОЧКИ  /  Вклад экономики + стоимости завершённых и уничтоженных сущностей",12,OrbitalTheme.Muted));
        }
        private void ShowGraphs()
        {
            var body=Row();body.style.flexGrow=1;body.style.minHeight=0;content.Add(body);
            var filters=new VisualElement();filters.style.width=150;filters.style.flexShrink=0;body.Add(filters);filters.Add(Text("ИГРОКИ",12,OrbitalTheme.Muted));
            foreach(var player in Result.Players){var toggle=new Toggle(playerName(player)){value=visible.Contains(player.Owner),name="result-filter-"+player.Slot};toggle.style.color=playerColor(player);toggle.style.fontSize=13;toggle.style.minHeight=24;toggle.style.marginBottom=4;toggle.style.backgroundColor=Color.clear;toggle.style.borderLeftWidth=toggle.style.borderRightWidth=toggle.style.borderTopWidth=toggle.style.borderBottomWidth=0;toggle.labelElement.style.color=playerColor(player);toggle.labelElement.style.width=96;toggle.labelElement.style.minWidth=96;toggle.labelElement.style.flexShrink=0;toggle.labelElement.style.whiteSpace=WhiteSpace.Normal;toggle.RegisterValueChangedCallback(e=>{if(e.newValue)visible.Add(player.Owner);else visible.Remove(player.Owner);foreach(var graph in graphs)graph.MarkDirtyRepaint();InspectTime(0);});filters.Add(toggle);}
            var plots=new VisualElement{name="result-plots"};plots.style.flexGrow=1;plots.style.minWidth=0;plots.style.minHeight=0;body.Add(plots);
            var maxIncome=Result.Facts.Where(f=>f.Kind==MatchFactKind.Income).GroupBy(f=>new{f.Owner,Second=(int)Math.Floor(f.Seconds+1e-8)}).Select(g=>g.Sum(f=>f.Value)).DefaultIfEmpty(0).Max();
            AddGraph(plots,"Доход",maxIncome,"кр/с",0);AddGraph(plots,"Армия",100,"вместимость",1);
            if(apmWindow.HasValue){double maxApm=Result.Players.Where(p=>!p.IsAi).SelectMany(p=>Enumerable.Range(0,(int)Math.Ceiling(Result.Duration)+1).Select(s=>Result.Apm(p.Owner,s,apmWindow.Value))).DefaultIfEmpty(0).Max();AddGraph(plots,"APM",maxApm,"действий/мин · последние 60 с",2);}
            else plots.Add(Text("APM · окно расчёта ожидает решения",14,OrbitalTheme.Muted));
            content.Add(values);InspectTime(0);
        }
        private void AddGraph(VisualElement parent,string title,double maximum,string unit,int metric)
        {
            var block=new VisualElement{name="result-graph-block-"+metric};block.style.flexGrow=1;block.style.flexBasis=0;block.style.minHeight=0;block.style.backgroundColor=GraphPanel;block.style.paddingLeft=6;block.style.paddingRight=6;block.style.paddingTop=6;block.style.paddingBottom=6;block.style.marginBottom=4;parent.Add(block);
            var caption=Text(title+"  /  "+unit,13,OrbitalTheme.Ink);caption.style.flexShrink=0;caption.style.marginBottom=3;block.Add(caption);var row=Row();row.style.flexGrow=1;row.style.minHeight=0;block.Add(row);
            var axis=new VisualElement();axis.style.width=44;axis.style.justifyContent=Justify.SpaceBetween;row.Add(axis);axis.Add(Text(maximum.ToString("0.#"),11,OrbitalTheme.Muted));axis.Add(Text((maximum/2).ToString("0.#"),11,OrbitalTheme.Muted));axis.Add(Text("0",11,OrbitalTheme.Muted));
            var lines=new List<ResultGraph.Line>();foreach(var p in Result.Players){if(metric==2&&p.IsAi&&!showAiApm)continue;var points=new List<Vector2>();
                if(metric==1){foreach(var f in Result.Facts.Where(f=>f.Owner==p.Owner&&f.Kind==MatchFactKind.Army))points.Add(new Vector2((float)f.Seconds,(float)f.Value));points.Add(new Vector2((float)Result.Duration,(float)Result.Army(p.Owner,Result.Duration)));}
                else for(int s=0;s<=Math.Ceiling(Result.Duration);s++)points.Add(new Vector2((float)Math.Min(s,Result.Duration),(float)(metric==0?Result.Income(p.Owner,s):p.IsAi?0:Result.Apm(p.Owner,Math.Min(s,Result.Duration),apmWindow.Value))));
                lines.Add(new ResultGraph.Line(p.Owner,p.Slot,playerColor(p),points));}
            var graph=new ResultGraph(lines,visible,Result.Duration,maximum,InspectTime){name="result-graph-"+metric};graph.style.flexGrow=1;graph.style.minHeight=0;row.Add(graph);graphs.Add(graph);
            var times=Row();times.style.flexShrink=0;times.style.justifyContent=Justify.SpaceBetween;times.style.paddingLeft=44;times.Add(Text("00:00",11,OrbitalTheme.Muted));times.Add(Text(TimeLabel(Result.Duration/2),11,OrbitalTheme.Muted));times.Add(Text(TimeLabel(Result.Duration),11,OrbitalTheme.Muted));block.Add(times);
        }
        public void InspectTime(double time)
        {
            time=Math.Max(0,Math.Min(Result.Duration,time));values.text=TimeLabel(time)+"  /  "+string.Join("   ·   ",Result.Players.Where(p=>visible.Contains(p.Owner)).Select(p=>playerName(p)+": "+Result.Income(p.Owner,(int)Math.Floor(time+1e-8)).ToString("0.#")+" кр/с, "+Result.Army(p.Owner,time).ToString("0.#")+" армия"+((!p.IsAi||showAiApm)&&apmWindow.HasValue?", "+Result.Apm(p.Owner,time,apmWindow.Value).ToString("0.#")+" APM":"")));
            foreach(var graph in graphs){graph.Cursor=time;graph.MarkDirtyRepaint();}
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
            var toggle=new Toggle("Решения ИИ"){value=aiLogs[side],name="result-ai-log-"+side};toggle.style.display=player.IsAi?DisplayStyle.Flex:DisplayStyle.None;column.Add(toggle);
            var scroll=OrbitalTheme.Scroll();scroll.style.flexGrow=1;scroll.style.minHeight=0;column.Add(scroll);
            void Render(){scroll.Clear();foreach(var f in Result.Facts.Where(f=>f.Owner==player.Owner&&(f.Kind==MatchFactKind.UnitCompleted||f.Kind==MatchFactKind.BuildingCompleted||player.IsAi&&aiLogs[side]&&f.Kind==MatchFactKind.AiDecision)).OrderBy(f=>f.Tick).ThenBy(f=>f.Sequence)){var entry=Text(TimeLabel(f.Seconds)+"   "+factIcon(f)+"   "+factName(f),14,f.Kind==MatchFactKind.AiDecision?OrbitalTheme.Cyan:OrbitalTheme.Ink);entry.style.whiteSpace=WhiteSpace.Normal;entry.style.paddingTop=8;entry.style.paddingBottom=8;entry.style.borderBottomWidth=1;entry.style.borderBottomColor=OrbitalTheme.Line;scroll.Add(entry);}if(scroll.childCount==0)scroll.Add(Text("Нет завершённых действий",13,OrbitalTheme.Muted));}
            toggle.RegisterValueChangedCallback(e=>{aiLogs[side]=e.newValue;Render();});selector.RegisterValueChangedCallback(_=>{logSlots[side]=selector.index;ShowTab(2);});Render();
        }
        public static string FactLabel(MatchFact fact)
        {
            if(fact.Kind==MatchFactKind.UnitCompleted)return OfflinePadWorldActions.Name((PlayableEntityKind)fact.EntityKind);
            if(fact.Kind==MatchFactKind.BuildingCompleted)return OfflinePadWorldActions.Name((PlayableBuildingKind)fact.EntityKind);
            if(fact.Kind!=MatchFactKind.AiDecision)return fact.Name;
            switch((MatchAiDecisionKind)fact.EntityKind){
                case MatchAiDecisionKind.Opening:return "Стартовый план: "+new[]{"безопасное развитие","экономическое развитие","захват шахты","танковая атака","атака исследователями","две шахты и исследователи"}[(int)fact.Value];
                case MatchAiDecisionKind.OpeningCompleted:return "Стартовый план завершён";
                case MatchAiDecisionKind.OpeningAborted:return "Стартовый план прерван";
                case MatchAiDecisionKind.Strategy:return "Стратегия: "+new[]{"массовый штурм","контроль карты","рейды"}[(int)fact.Value];
                default:return fact.Name;
            }
        }
    }
    internal sealed class ResultGraph:VisualElement
    {
        internal sealed class Line{internal readonly PlayableOwner Owner;internal readonly int Slot;internal readonly Color Color;internal readonly List<Vector2> Points;internal Line(PlayableOwner owner,int slot,Color color,List<Vector2> points){Owner=owner;Slot=slot;Color=color;Points=points;}}
        private readonly List<Line> lines;private readonly HashSet<PlayableOwner> visible;private readonly double duration,maximum;internal double Cursor;
        internal ResultGraph(List<Line> lines,HashSet<PlayableOwner> visible,double duration,double maximum,Action<double> inspect)
        {this.lines=lines;this.visible=visible;this.duration=Math.Max(1,duration);this.maximum=Math.Max(1,maximum);generateVisualContent+=Draw;RegisterCallback<PointerMoveEvent>(e=>inspect(Math.Max(0,Math.Min(duration,e.localPosition.x/Math.Max(1,contentRect.width)*duration))));style.backgroundColor=PostMatchResultsView.GraphPanel;style.overflow=Overflow.Hidden;}
        private Vector2 Point(Vector2 p)=>new Vector2(p.x/(float)duration*contentRect.width,contentRect.height-p.y/(float)maximum*(contentRect.height-4)-2);
        private void Draw(MeshGenerationContext context)
        {
            var painter=context.painter2D;painter.lineWidth=1;painter.strokeColor=OrbitalTheme.Line;for(int i=0;i<3;i++){float y=i*contentRect.height/2;painter.BeginPath();painter.MoveTo(new Vector2(0,y));painter.LineTo(new Vector2(contentRect.width,y));painter.Stroke();}
            // Draw larger slot numbers first so the player listed higher in the match roster stays visible on overlap.
            foreach(var line in lines.Where(l=>visible.Contains(l.Owner)).OrderByDescending(l=>l.Slot)){painter.strokeColor=line.Color;painter.lineWidth=2;
                for(int i=1;i<line.Points.Count;i++){var from=Point(line.Points[i-1]);var to=Point(line.Points[i]);var corner=new Vector2(to.x,from.y);painter.BeginPath();painter.MoveTo(from);painter.LineTo(corner);painter.LineTo(to);painter.Stroke();}
            }
            painter.strokeColor=OrbitalTheme.Cyan;painter.lineWidth=1;painter.BeginPath();float x=(float)(Cursor/duration)*contentRect.width;painter.MoveTo(new Vector2(x,0));painter.LineTo(new Vector2(x,contentRect.height));painter.Stroke();
        }
    }
}
