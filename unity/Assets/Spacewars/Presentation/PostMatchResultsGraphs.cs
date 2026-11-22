using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PostMatchResultsView
    {
        private const int InventoryIconSize = 30;
        private const int InventoryRowHeight = InventoryIconSize+4;
        private bool hideEvents;
        private int activeGraphIndex=-1;
        private double cursorTime;
        private Label cursorHeader;
        private VisualElement cursorRows,graphTip;
        private bool showTeams;
        private ScrollView graphScroll;
        private readonly HashSet<PlayableOwner> visibleTeams=new();
        private IGrouping<int,MatchResultPlayer>[] teams;

        // The first slot supplies a unique renderer key for each disjoint team.
        private static PlayableOwner TeamKey(IGrouping<int,MatchResultPlayer> team)=>team.First().Owner;
        private string TeamName(int team)=>"Команда "+(team+(teams[0].Key==0?1:0));

        private void ShowGraphs()
        {
            if(teams==null)
            {
                teams=Result.Players.OrderBy(p=>p.Slot).GroupBy(p=>p.Team).OrderBy(g=>g.Key).ToArray();
                visibleTeams.UnionWith(teams.Select(TeamKey));
            }

            var controls=Row();
            controls.style.flexWrap=Wrap.Wrap;controls.style.flexShrink=0;controls.style.alignItems=Align.Center;controls.style.marginBottom=8;
            content.Add(controls);
            var mode=OrbitalTheme.Action("",null,"result-graph-mode",true);
            CompactButton(mode);mode.style.minWidth=112;mode.style.marginRight=8;
            controls.Add(mode);

            var legend=Row();
            legend.style.flexWrap=Wrap.Wrap;legend.style.flexGrow=1;legend.style.flexShrink=1;legend.style.minWidth=0;
            controls.Add(legend);
            var hide=new Toggle("Скрыть события"){value=hideEvents,name="result-hide-events"};
            ConfigureFilterToggle(hide,OrbitalTheme.Ink);
            hide.style.width=hide.style.minWidth=hide.style.maxWidth=225;hide.style.marginLeft=8;
            hide.RegisterValueChangedCallback(e=>{
                hideEvents=e.newValue;CloseDetails(false);
                foreach(var g in graphs){g.HideEvents=hideEvents;g.RefreshEvents();}
            });
            controls.Add(hide);

            graphScroll=OrbitalTheme.Scroll();
            graphScroll.name="result-plots";graphScroll.style.flexGrow=1;graphScroll.style.minHeight=0;
            content.Add(graphScroll);
            graphScroll.verticalScroller.valueChanged+=_=>{
                CloseDetails(false);
                graphTip.style.display=DisplayStyle.None;
            };
            mode.clicked+=()=>{
                showTeams=!showTeams;
                CloseDetails(false);graphTip.style.display=DisplayStyle.None;
                inspecting=false;activeGraphIndex=-1;selectedStack=-1;
                graphScroll.scrollOffset=Vector2.zero;
                RenderGraphs();
                mode.Focus();
            };
            RenderGraphs();

            void RenderGraphs()
            {
                mode.text=showTeams?"Команды":"Игроки";
                mode.tooltip=showTeams?"Переключить на игроков":"Переключить на команды";
                hide.style.display=showTeams?DisplayStyle.None:DisplayStyle.Flex;
                legend.Clear();graphs.Clear();graphScroll.Clear();
                if(showTeams)
                {
                    for(int i=0;i<teams.Length;i++)
                        AddLegendEntry(legend,TeamName(teams[i].Key),Palette[i%Palette.Length],TeamKey(teams[i]),visibleTeams,"result-team-filter-"+teams[i].Key);
                    AddGraph(graphScroll,"Доход",0,0);
                    AddGraph(graphScroll,"Армия",0,1);
                    if(Result.Domination)AddGraph(graphScroll,"Доминирование",Result.Terms?.DominationTarget??200,4);
                    return;
                }

                foreach(var player in Result.Players)
                    AddLegendEntry(legend,playerName(player),playerColor(player),player.Owner,visible,"result-filter-"+player.Slot);
                double maxIncome=Result.Facts.Where(f=>f.Kind==MatchFactKind.Income).GroupBy(f=>new{f.Owner,Second=(int)Math.Floor(f.Seconds+1e-8)}).Select(g=>g.Sum(f=>f.Value)).DefaultIfEmpty(0).Max();
                double maxApm=apmWindow.HasValue?Result.Players.Where(p=>Applicable(2,p)).SelectMany(p=>Enumerable.Range(0,(int)Math.Ceiling(Result.Duration)+1).Select(s=>Result.Apm(p.Owner,Math.Min(s,Result.Duration),apmWindow.Value))).DefaultIfEmpty(0).Max():0;
                double maxResearch=Result.Players.Select(p=>Result.ResearchCount(p.Owner,Result.Duration)).DefaultIfEmpty(0).Max();
                AddGraph(graphScroll,"Доход",maxIncome,0);
                if(Result.Domination)AddGraph(graphScroll,"Доминирование",Result.Terms?.DominationTarget??200,4);
                AddGraph(graphScroll,"Армия",100,1);
                AddGraph(graphScroll,"Исследования",maxResearch,3);
                AddGraph(graphScroll,"APM",maxApm,2);
            }
        }

        private void AddLegendEntry(VisualElement legend,string name,Color color,PlayableOwner key,HashSet<PlayableOwner> selection,string id)
        {
            var toggle=new Toggle(name){value=selection.Contains(key),name=id};
            toggle.style.height=29;toggle.style.minHeight=29;toggle.style.marginRight=8;
            toggle.style.backgroundColor=Color.clear;toggle.style.borderLeftWidth=0;
            toggle.style.maxWidth=Length.Percent(100);
            toggle.style.paddingLeft=7;toggle.style.paddingRight=7;toggle.style.flexDirection=FlexDirection.RowReverse;
            toggle.style.justifyContent=Justify.FlexEnd;
            toggle.labelElement.style.minWidth=0;toggle.labelElement.style.fontSize=12;
            toggle.labelElement.style.flexGrow=0;toggle.labelElement.style.marginRight=0;
            toggle.labelElement.style.overflow=Overflow.Hidden;toggle.labelElement.style.textOverflow=TextOverflow.Ellipsis;
            var input=toggle.Q(className:"unity-base-field__input");
            input.style.flexGrow=0;input.style.flexShrink=0;input.style.width=18;input.style.minWidth=18;
            input.style.borderLeftWidth=input.style.borderRightWidth=input.style.borderTopWidth=input.style.borderBottomWidth=0;
            input.style.backgroundColor=Color.clear;input.style.marginRight=5;
            var swatch=toggle.Q(className:"unity-toggle__checkmark");
            swatch.style.backgroundImage=StyleKeyword.None;swatch.style.width=15;swatch.style.height=2;
            swatch.style.borderLeftWidth=swatch.style.borderRightWidth=swatch.style.borderTopWidth=swatch.style.borderBottomWidth=0;
            swatch.style.marginLeft=swatch.style.marginRight=0;
            toggle.RegisterCallback<FocusInEvent>(_=>toggle.style.backgroundColor=GraphPanel);
            toggle.RegisterCallback<FocusOutEvent>(_=>toggle.style.backgroundColor=Color.clear);
            toggle.tooltip=name;
            UpdateStyle();
            toggle.RegisterValueChangedCallback(e=>{
                CloseDetails(false);
                if(e.newValue)selection.Add(key);else selection.Remove(key);
                UpdateStyle();
                foreach(var g in graphs)g.RefreshEvents();
                if(activeGraphIndex>=0)InspectTime(cursorTime);
            });
            legend.Add(toggle);

            void UpdateStyle()
            {
                var tint=toggle.value?color:OrbitalTheme.Muted;
                toggle.labelElement.style.color=tint;swatch.style.backgroundColor=tint;
                toggle.style.opacity=toggle.value?1f:.58f;
            }
        }
        private static void ConfigureFilterToggle(Toggle toggle,Color color)
        {
            toggle.style.width=132;toggle.style.minWidth=132;toggle.style.maxWidth=132;toggle.style.height=26;toggle.style.minHeight=26;toggle.style.fontSize=12;toggle.style.marginLeft=toggle.style.marginRight=0;toggle.style.marginBottom=3;toggle.style.paddingLeft=toggle.style.paddingRight=0;toggle.style.backgroundColor=Color.clear;
            toggle.style.borderLeftWidth=toggle.style.borderRightWidth=toggle.style.borderTopWidth=toggle.style.borderBottomWidth=0;
            toggle.labelElement.style.color=color;toggle.labelElement.style.minWidth=0;toggle.labelElement.style.width=104;toggle.labelElement.style.flexGrow=1;toggle.labelElement.style.flexShrink=1;toggle.labelElement.style.whiteSpace=WhiteSpace.Normal;toggle.labelElement.style.marginLeft=toggle.labelElement.style.marginRight=0;
            var input=toggle.Q(className:"unity-base-field__input");if(input!=null){input.style.width=22;input.style.minWidth=22;input.style.height=22;input.style.minHeight=22;input.style.flexGrow=0;input.style.flexShrink=0;input.style.alignSelf=Align.Center;input.style.backgroundColor=Color.clear;input.style.borderLeftWidth=input.style.borderRightWidth=input.style.borderTopWidth=input.style.borderBottomWidth=0;input.style.paddingLeft=input.style.paddingRight=input.style.paddingTop=input.style.paddingBottom=0;input.style.marginLeft=input.style.marginRight=0;}
            var check=toggle.Q(className:"unity-toggle__checkmark");if(check!=null){
                check.style.width=check.style.height=18;check.style.minWidth=check.style.minHeight=18;check.style.marginLeft=check.style.marginRight=0;
                // Runtime panels do not install the Editor's default checkmark image.
                // Draw from the field value so pointer and controller changes agree.
                check.style.backgroundImage=StyleKeyword.None;
                check.generateVisualContent+=ctx=>{var p=ctx.painter2D;p.strokeColor=toggle.value?OrbitalTheme.Cyan:OrbitalTheme.Muted;p.lineWidth=1.5f;
                    p.BeginPath();p.MoveTo(new Vector2(2,2));p.LineTo(new Vector2(16,2));p.LineTo(new Vector2(16,16));p.LineTo(new Vector2(2,16));p.ClosePath();p.Stroke();
                    if(toggle.value){p.strokeColor=Color.white;p.lineWidth=2.3f;p.BeginPath();p.MoveTo(new Vector2(5,9));p.LineTo(new Vector2(8,12));p.LineTo(new Vector2(14,5));p.Stroke();}};
                toggle.RegisterValueChangedCallback(_=>check.MarkDirtyRepaint());
            }
        }
        private static int BuildingMetric(PlayableBuildingKind kind)=>kind==PlayableBuildingKind.Headquarters||kind==PlayableBuildingKind.Outpost||kind==PlayableBuildingKind.Mine||kind==PlayableBuildingKind.Refinery?0:kind==PlayableBuildingKind.ScientificCenter?3:1;
        private double MetricValue(int metric,PlayableOwner owner,double time)=>metric==4?Result.DominationPoints(owner,time):metric==0?Result.Income(owner,(int)Math.Floor(time+1e-8)):metric==1?Result.Army(owner,time):metric==2?(apmWindow.HasValue?Result.Apm(owner,time,apmWindow.Value):0):Result.ResearchCount(owner,time);
        private string MetricUnit(int metric)=>metric==2?" APM":metric==3?" шт.":"";
        private bool Applicable(int metric,MatchResultPlayer p)=>metric!=2||!p.IsAi||showAiApm;
        private bool GraphEvent(int metric,MatchFact f)
        {
            if(metric==3)return f.Kind==MatchFactKind.ResearchCompleted;
            bool building=f.Kind==MatchFactKind.BuildingCompleted||f.Kind==MatchFactKind.BuildingLost||f.Kind==MatchFactKind.BuildingSold;
            bool producer=f.EntityKind==(int)PlayableBuildingKind.Factory||f.EntityKind==(int)PlayableBuildingKind.Hangar;
            bool upgrade=f.Kind==MatchFactKind.RefineryUpgradeCompleted;
            if(metric==1)return producer&&(building||upgrade);
            if(metric==4)return f.Kind is MatchFactKind.DominationLeadGained or MatchFactKind.DominationLeadLost;
            return metric==0&&(Result?.Domination!=true&&(f.Kind==MatchFactKind.RelayCaptured||f.Kind==MatchFactKind.RelayNeutralized)||upgrade&&!producer||building&&(f.EntityKind==(int)PlayableBuildingKind.Headquarters||f.EntityKind==(int)PlayableBuildingKind.Outpost||f.EntityKind==(int)PlayableBuildingKind.Mine||f.EntityKind==(int)PlayableBuildingKind.Refinery));
        }
        private double TeamValue(int metric,IGrouping<int,MatchResultPlayer> team,double time)
            =>team.Sum(player=>MetricValue(metric,player.Owner,time));

        private List<ResultGraph.Line> BuildTeamLines(int metric)
        {
            var lines=new List<ResultGraph.Line>(teams.Length);
            for(int i=0;i<teams.Length;i++)
            {
                var team=teams[i];
                IEnumerable<double> times;
                if(metric==0||metric==4)
                    times=Enumerable.Range(0,(int)Math.Ceiling(Result.Duration)+1).Select(second=>Math.Min(second,Result.Duration));
                else
                {
                    var owners=new HashSet<PlayableOwner>(team.Select(player=>player.Owner));
                    times=Result.Facts.Where(f=>f.Kind==MatchFactKind.Army&&owners.Contains(f.Owner))
                        .Select(f=>f.Seconds).Append(0).Append(Result.Duration).Distinct().OrderBy(time=>time);
                }
                var points=times.Select(time=>new Vector2((float)time,(float)TeamValue(metric,team,time))).ToList();
                lines.Add(new ResultGraph.Line(TeamKey(team),i,metric==4?playerColor(team.First()):Palette[i%Palette.Length],points));
            }
            return lines;
        }

        private List<ResultGraph.Line> BuildPlayerLines(int metric)
        {
            var lines=new List<ResultGraph.Line>();
            foreach(var p in Result.Players.Where(p=>Applicable(metric,p)))
            {
                var points=new List<Vector2>();
                if(metric==1)
                {
                    foreach(var f in Result.Facts.Where(f=>f.Owner==p.Owner&&f.Kind==MatchFactKind.Army))
                        points.Add(new Vector2((float)f.Seconds,(float)f.Value));
                }
                else if(metric==3)
                {
                    points.Add(Vector2.zero);
                    int count=0;
                    foreach(var f in Result.Facts.Where(f=>f.Owner==p.Owner&&f.Kind==MatchFactKind.ResearchCompleted))
                        points.Add(new Vector2((float)f.Seconds,++count));
                }
                else
                {
                    for(int second=0;second<=Math.Ceiling(Result.Duration);second++)
                    {
                        double time=Math.Min(second,Result.Duration);
                        points.Add(new Vector2((float)time,(float)MetricValue(metric,p.Owner,time)));
                    }
                }
                points.Add(new Vector2((float)Result.Duration,(float)MetricValue(metric,p.Owner,Result.Duration)));
                lines.Add(new ResultGraph.Line(p.Owner,p.Slot,playerColor(p),points));
            }
            return lines;
        }

        private void AddGraph(VisualElement parent,string title,double maximum,int metric)
        {
            var lines=showTeams||metric==4?BuildTeamLines(metric):BuildPlayerLines(metric);
            if(showTeams&&metric!=4)maximum=lines.SelectMany(l=>l.Points).Select(point=>(double)point.y).DefaultIfEmpty(0).Max();
            maximum=metric==3?Math.Max(4,Math.Ceiling(maximum/4)*4):Math.Max(1,maximum);

            var block=new VisualElement{name="result-graph-block-"+metric};
            block.style.height=330;block.style.flexShrink=0;block.style.backgroundColor=GraphPanel;
            block.style.paddingLeft=14;block.style.paddingRight=18;block.style.paddingTop=10;block.style.paddingBottom=8;
            block.style.marginBottom=10;
            block.style.borderTopLeftRadius=block.style.borderTopRightRadius=block.style.borderBottomLeftRadius=block.style.borderBottomRightRadius=6;
            parent.Add(block);
            var caption=Text(title,17,OrbitalTheme.Ink);
            caption.style.unityFontStyleAndWeight=FontStyle.Bold;caption.style.flexShrink=0;caption.style.marginBottom=7;block.Add(caption);
            var row=Row();row.style.flexGrow=1;row.style.minHeight=0;row.style.marginTop=ResultGraph.EventIconSize/2;block.Add(row);
            var axis=new VisualElement();axis.style.width=38;axis.style.flexShrink=0;axis.style.marginTop=2;axis.style.marginBottom=38;row.Add(axis);
            for(int i=0;i<5;i++)
            {
                var label=Text((maximum*(4-i)/4).ToString("0.#"),11,OrbitalTheme.Muted);
                label.style.position=Position.Absolute;label.style.top=Length.Percent(i*25);
                label.style.translate=new Translate(0,Length.Percent(-50),0);label.style.marginBottom=0;axis.Add(label);
            }

            var events=showTeams&&metric!=4?Array.Empty<MatchFact>():Result.Facts.Where(f=>GraphEvent(metric,f)).ToArray();
            var exits=!showTeams&&metric==1?Result.Facts.Where(PlayerExitFact).ToArray():Array.Empty<MatchFact>();
            int graphIndex=graphs.Count;
            var graph=new ResultGraph(metric,lines,showTeams||metric==4?visibleTeams:visible,Result.Duration,maximum,(time,position)=>InspectGraph(graphIndex,time,position),
                events,f=>MetricValue(metric,f.Owner,f.Seconds),f=>MakeEventIcon(f,ResultGraph.EventIconSize,brighten:true),OpenEvents,exits,
                f=>(f.Kind==MatchFactKind.PlayerLeft?"↗ ":"× ")+playerName(Result.Players.First(p=>p.Owner==f.Owner))+" · "+TimeLabel(f.Seconds)){name="result-graph-"+metric,focusable=true};
            graph.AddToClassList("result-focusable");graph.HideEvents=hideEvents;graph.style.flexGrow=1;graph.style.minHeight=0;graph.style.marginBottom=36;row.Add(graph);graphs.Add(graph);
            graph.RegisterCallback<GeometryChangedEvent>(_=>{if(activeGraphIndex==graphIndex&&popup==null&&graphTip.style.display.value==DisplayStyle.Flex)InspectGraph(graphIndex,cursorTime,null);});
            graph.RegisterCallback<FocusInEvent>(_=>{
                graph.style.borderLeftColor=OrbitalTheme.Cyan;
            });
            graph.RegisterCallback<FocusOutEvent>(_=>graph.style.borderLeftColor=Color.clear);
            graph.RegisterCallback<PointerDownEvent>(e=>{if(e.target==graph){graph.Focus();activeGraphIndex=graphIndex;}});
            graph.RegisterCallback<PointerLeaveEvent>(_=>{if(!usingPad)graphTip.style.display=DisplayStyle.None;});
            var times=Row();times.style.flexShrink=0;times.style.justifyContent=Justify.SpaceBetween;times.style.paddingLeft=38;
            block.Add(times);
            void LabelTimes()
            {
                times.Clear();
                int ticks=Result.Duration>0?graph.contentRect.width>=520?6:3:1;
                for(int i=0;i<ticks;i++)
                {
                    double time=ticks==1?0:Result.Duration*i/(ticks-1);
                    var label=Text(TimeLabel(time),11,OrbitalTheme.Muted);label.style.marginBottom=0;times.Add(label);
                }
            }
            LabelTimes();
            graph.RegisterCallback<GeometryChangedEvent>(e=>{if((e.oldRect.width>=520)!=(e.newRect.width>=520))LabelTimes();});
        }
        public void InspectTime(double time)
        {
            cursorTime=Math.Max(0,Math.Min(Result.Duration,time));foreach(var graph in graphs){graph.Cursor=cursorTime;graph.MarkDirtyRepaint();}
            if(graphs.Count>0)InspectGraph(activeGraphIndex<0?0:activeGraphIndex,cursorTime,null);
        }
        private void InspectGraph(int graphIndex,double time,Vector2? local)
        {
            activeGraphIndex=graphIndex;cursorTime=Math.Max(0,Math.Min(Result.Duration,time));foreach(var graph in graphs){graph.Cursor=cursorTime;graph.MarkDirtyRepaint();}
            var chart=graphs[graphIndex];
            int metric=chart.Metric;
            if(popup!=null){if(local.HasValue&&!PointerInDetails(chart.LocalToWorld(local.Value)))CloseDetails(false);else return;}
            graphTip.style.display=DisplayStyle.Flex;cursorHeader.text=TimeLabel(cursorTime);cursorRows.Clear();
            var closest=local.HasValue?chart.NearestOwner(cursorTime,local.Value.y):(PlayableOwner?)null;
            if(showTeams||metric==4)
            {
                for(int i=0;i<teams.Length;i++)
                {
                    var team=teams[i];
                    if(!visibleTeams.Contains(TeamKey(team)))continue;
                    var color=metric==4?playerColor(team.First()):Palette[i%Palette.Length];
                    var value=TeamValue(metric,team,cursorTime).ToString("0.#")+(metric==0?" кр./с":metric==4?" очк.":" ед.");
                    AddValueRow("result-team-hover-value-"+team.Key,TeamName(team.Key),value,color,closest==TeamKey(team));
                }
            }
            else foreach(var player in Result.Players.Where(p=>visible.Contains(p.Owner)&&Applicable(metric,p)))
            {
                var row=AddValueRow("result-hover-value-"+player.Slot,playerName(player),
                    MetricValue(metric,player.Owner,cursorTime).ToString("0.#")+MetricUnit(metric),playerColor(player),closest==player.Owner);
                if(metric<2){var glyph=new OrbitalGlyph(metric==0?"credits":"army"){Tint=playerColor(player)};glyph.style.width=16;glyph.style.height=16;glyph.style.marginLeft=4;row.Add(glyph);}
                if(metric!=2){
                    var counts=Row();counts.name="result-building-counts-"+player.Slot;counts.pickingMode=PickingMode.Ignore;counts.style.flexWrap=Wrap.Wrap;
                    foreach(PlayableBuildingKind kind in Enum.GetValues(typeof(PlayableBuildingKind))){
                        int count=Result.BuildingCount(player.Owner,kind,cursorTime);if(count==0||BuildingMetric(kind)!=metric)continue;
                        var cell=Row();cell.name="result-building-count-"+player.Slot+"-"+kind;cell.pickingMode=PickingMode.Ignore;cell.style.width=156;cell.style.height=InventoryRowHeight;cell.style.alignItems=Align.Center;
                        var icon=new OrbitalGlyph(OrbitalPrecision.BuildingGlyph(kind)){Tint=playerColor(player),pickingMode=PickingMode.Ignore};icon.style.width=InventoryIconSize;icon.style.height=InventoryIconSize;icon.style.marginRight=4;icon.FitEventContent(2);cell.Add(icon);
                        var label=Text(OfflinePadWorldActions.Name(kind)+" ×"+count,10,playerColor(player));label.pickingMode=PickingMode.Ignore;label.style.marginBottom=0;cell.Add(label);counts.Add(cell);
                    }
                    cursorRows.Add(counts);
                }
            }
            if(cursorRows.childCount==0)cursorRows.Add(Text(showTeams?"Нет выбранных команд":"Нет выбранных игроков",11,OrbitalTheme.Muted));
            var position=local.HasValue?chart.LocalToWorld(local.Value):chart.LocalToWorld(new Vector2((float)(cursorTime/(Result.Duration>0?Result.Duration:1))*chart.contentRect.width,chart.contentRect.height*.4f));
            int columns=Math.Max(1,Math.Min(3,cursorRows.Children().Where(r=>r.name.StartsWith("result-building-counts-")).Select(r=>r.childCount).DefaultIfEmpty(0).Max()));
            float height=50;
            foreach(var row in cursorRows.Children())height+=(float)(row.name.StartsWith("result-building-counts-")?InventoryRowHeight*Math.Ceiling(row.childCount/(double)columns):27);
            PositionOverlay(graphTip,Root.WorldToLocal(position)+new Vector2(14,10),Math.Max(250,columns*156+22),height);
        }
        private VisualElement AddValueRow(string id,string name,string value,Color color,bool emphasized)
        {
            var row=Row();row.name=id;row.pickingMode=PickingMode.Ignore;
            row.style.height=23;row.style.minHeight=23;row.style.alignItems=Align.Center;
            var nameLabel=Text(name,12,color);nameLabel.pickingMode=PickingMode.Ignore;nameLabel.style.flexGrow=1;
            nameLabel.style.marginBottom=0;nameLabel.style.overflow=Overflow.Hidden;nameLabel.style.textOverflow=TextOverflow.Ellipsis;
            var valueLabel=Text(value,12,color);valueLabel.pickingMode=PickingMode.Ignore;valueLabel.style.marginBottom=0;
            valueLabel.style.unityTextAlign=TextAnchor.MiddleRight;
            valueLabel.style.unityFontStyleAndWeight=emphasized?FontStyle.Bold:FontStyle.Normal;
            row.Add(nameLabel);row.Add(valueLabel);cursorRows.Add(row);
            return row;
        }
    }
}
