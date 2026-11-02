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
        private bool hideEvents;
        private int activeMetric=-1;
        private double cursorTime;
        private readonly List<VisualElement> graphCards=new();
        private Label cursorHeader;
        private VisualElement cursorRows,graphTip;
        private void ShowGraphs()
        {
            graphCards.Clear();
            var body=Row();body.style.flexGrow=1;body.style.minHeight=0;content.Add(body);
            var filters=new VisualElement();filters.style.width=142;filters.style.flexShrink=0;body.Add(filters);filters.Add(Text("ИГРОКИ",11,OrbitalTheme.Muted));
            foreach(var player in Result.Players)
            {
                var toggle=new Toggle(playerName(player)){value=visible.Contains(player.Owner),name="result-filter-"+player.Slot};
                ConfigureFilterToggle(toggle,playerColor(player));
                toggle.RegisterValueChangedCallback(e=>{CloseDetails(false);if(e.newValue)visible.Add(player.Owner);else visible.Remove(player.Owner);foreach(var g in graphs)g.RefreshEvents();InspectTime(cursorTime);});filters.Add(toggle);
            }
            var hide=new Toggle("Скрыть здания\nи исследования"){value=hideEvents,name="result-hide-events"};ConfigureFilterToggle(hide,OrbitalTheme.Ink);hide.style.height=36;hide.style.minHeight=36;hide.style.fontSize=11;hide.style.marginTop=12;hide.labelElement.style.height=32;hide.labelElement.style.minHeight=32;hide.labelElement.style.textOverflow=TextOverflow.Clip;hide.labelElement.style.overflow=Overflow.Visible;
            hide.RegisterValueChangedCallback(e=>{hideEvents=e.newValue;CloseDetails(false);foreach(var g in graphs){g.HideEvents=hideEvents;g.RefreshEvents();}});filters.Add(hide);
            var plots=new VisualElement{name="result-plots"};plots.style.flexGrow=1;plots.style.minWidth=0;plots.style.minHeight=0;body.Add(plots);
            double maxIncome=Result.Facts.Where(f=>f.Kind==MatchFactKind.Income).GroupBy(f=>new{f.Owner,Second=(int)Math.Floor(f.Seconds+1e-8)}).Select(g=>g.Sum(f=>f.Value)).DefaultIfEmpty(0).Max();
            double maxApm=apmWindow.HasValue?Result.Players.Where(p=>!p.IsAi).SelectMany(p=>Enumerable.Range(0,(int)Math.Ceiling(Result.Duration)+1).Select(s=>Result.Apm(p.Owner,s,apmWindow.Value))).DefaultIfEmpty(0).Max():0;
            double maxResearch=Result.Players.Select(p=>Result.ResearchCount(p.Owner,Result.Duration)).DefaultIfEmpty(0).Max();
            for(int rowIndex=0;rowIndex<2;rowIndex++)
            {
                var row=Row();row.style.flexGrow=1;row.style.flexBasis=0;row.style.minHeight=0;plots.Add(row);
                if(rowIndex==0){AddGraph(row,"Доход",maxIncome,0);AddGraph(row,"Армия",100,1);}
                else{AddGraph(row,"APM",maxApm,2);AddGraph(row,"Исследования",maxResearch,3);}
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
        private double MetricValue(int metric,PlayableOwner owner,double time)=>metric==0?Result.Income(owner,(int)Math.Floor(time+1e-8)):metric==1?Result.Army(owner,time):metric==2?(apmWindow.HasValue?Result.Apm(owner,time,apmWindow.Value):0):Result.ResearchCount(owner,time);
        private string MetricUnit(int metric)=>metric==2?" APM":metric==3?" шт.":"";
        private bool Applicable(int metric,MatchResultPlayer p)=>metric!=2||!p.IsAi||showAiApm;
        private bool GraphEvent(int metric,MatchFact f)
        {
            if(metric==3)return f.Kind==MatchFactKind.ResearchCompleted;
            bool building=f.Kind==MatchFactKind.BuildingCompleted||f.Kind==MatchFactKind.BuildingLost||f.Kind==MatchFactKind.BuildingSold;
            if(metric==1)return building&&(f.EntityKind==(int)PlayableBuildingKind.Factory||f.EntityKind==(int)PlayableBuildingKind.Hangar);
            return metric==0&&(f.Kind==MatchFactKind.RelayCaptured||f.Kind==MatchFactKind.RelayNeutralized||f.Kind==MatchFactKind.RefineryUpgradeCompleted||building&&(f.EntityKind==(int)PlayableBuildingKind.Headquarters||f.EntityKind==(int)PlayableBuildingKind.Outpost||f.EntityKind==(int)PlayableBuildingKind.Mine||f.EntityKind==(int)PlayableBuildingKind.Refinery));
        }
        private void AddGraph(VisualElement parent,string title,double maximum,int metric)
        {
            maximum=metric==3?Math.Max(4,Math.Ceiling(maximum/4)*4):Math.Max(1,maximum);
            var block=new VisualElement{name="result-graph-block-"+metric};block.style.flexGrow=1;block.style.flexBasis=0;block.style.minWidth=0;block.style.minHeight=0;block.style.backgroundColor=GraphPanel;block.style.paddingLeft=8;block.style.paddingRight=8;block.style.paddingTop=7;block.style.paddingBottom=4;block.style.marginRight=4;block.style.marginBottom=4;parent.Add(block);graphCards.Add(block);
            var caption=Text(title,12,OrbitalTheme.Ink);caption.style.flexShrink=0;caption.style.marginBottom=5;block.Add(caption);
            var row=Row();row.style.flexGrow=1;row.style.minHeight=0;block.Add(row);
            var axis=new VisualElement();axis.style.width=31;axis.style.marginBottom=24;axis.style.justifyContent=Justify.SpaceBetween;row.Add(axis);
            for(int i=0;i<5;i++){var label=Text((maximum*(4-i)/4).ToString("0.#"),10,OrbitalTheme.Muted);label.style.marginBottom=0;axis.Add(label);}
            var lines=new List<ResultGraph.Line>();
            foreach(var p in Result.Players.Where(p=>Applicable(metric,p)))
            {
                var points=new List<Vector2>();
                if(metric==1){foreach(var f in Result.Facts.Where(f=>f.Owner==p.Owner&&f.Kind==MatchFactKind.Army))points.Add(new Vector2((float)f.Seconds,(float)f.Value));}
                else if(metric==3){points.Add(Vector2.zero);foreach(var f in Result.Facts.Where(f=>f.Owner==p.Owner&&f.Kind==MatchFactKind.ResearchCompleted))points.Add(new Vector2((float)f.Seconds,(float)MetricValue(metric,p.Owner,f.Seconds)));}
                else for(int second=0;second<=Math.Ceiling(Result.Duration);second++)points.Add(new Vector2((float)Math.Min(second,Result.Duration),(float)MetricValue(metric,p.Owner,Math.Min(second,Result.Duration))));
                points.Add(new Vector2((float)Result.Duration,(float)MetricValue(metric,p.Owner,Result.Duration)));lines.Add(new ResultGraph.Line(p.Owner,p.Slot,playerColor(p),points));
            }
            var graph=new ResultGraph(lines,visible,Result.Duration,maximum,(time,position)=>InspectGraph(metric,time,position),Result.Facts.Where(f=>GraphEvent(metric,f)).ToArray(),f=>MetricValue(metric,f.Owner,f.Seconds),f=>MakeEventIcon(f,18,true),OpenEvents,metric==1?Result.Facts.Where(PlayerExitFact).ToArray():Array.Empty<MatchFact>(),f=>(f.Kind==MatchFactKind.PlayerLeft?"↗ ":"× ")+playerName(Result.Players.First(p=>p.Owner==f.Owner))+" · "+TimeLabel(f.Seconds)){name="result-graph-"+metric,focusable=true};
            graph.AddToClassList("result-focusable");graph.HideEvents=hideEvents;graph.style.flexGrow=1;graph.style.minHeight=0;graph.style.marginBottom=24;row.Add(graph);graphs.Add(graph);
            graph.RegisterCallback<GeometryChangedEvent>(_=>{if(activeMetric==metric&&popup==null&&graphTip.style.display.value==DisplayStyle.Flex)InspectGraph(metric,cursorTime,null);});
            graph.RegisterCallback<FocusInEvent>(_=>graph.style.borderLeftColor=OrbitalTheme.Cyan);graph.RegisterCallback<FocusOutEvent>(_=>graph.style.borderLeftColor=Color.clear);
            graph.RegisterCallback<PointerDownEvent>(e=>{if(e.target==graph){graph.Focus();activeMetric=metric;}});
            graph.RegisterCallback<PointerLeaveEvent>(_=>{if(!usingPad)graphTip.style.display=DisplayStyle.None;});
            var times=Row();times.style.flexShrink=0;times.style.justifyContent=Justify.SpaceBetween;times.style.paddingLeft=31;
            foreach(double t in new[]{0,Result.Duration/2,Result.Duration}){var text=Text(TimeLabel(t),10,OrbitalTheme.Muted);text.style.marginBottom=0;times.Add(text);}block.Add(times);
        }
        public void InspectTime(double time)
        {
            cursorTime=Math.Max(0,Math.Min(Result.Duration,time));foreach(var graph in graphs){graph.Cursor=cursorTime;graph.MarkDirtyRepaint();}
            if(graphs.Count>0)InspectGraph(activeMetric<0?0:activeMetric,cursorTime,null);
        }
        private void InspectGraph(int metric,double time,Vector2? local)
        {
            activeMetric=metric;cursorTime=Math.Max(0,Math.Min(Result.Duration,time));foreach(var graph in graphs){graph.Cursor=cursorTime;graph.MarkDirtyRepaint();}
            var chart=graphs[metric];
            if(popup!=null){if(local.HasValue&&!PointerInDetails(chart.LocalToWorld(local.Value)))CloseDetails(false);else return;}
            graphTip.style.display=DisplayStyle.Flex;cursorHeader.text=TimeLabel(cursorTime);cursorRows.Clear();
            var closest=local.HasValue?chart.NearestOwner(cursorTime,local.Value.y):(PlayableOwner?)null;
            foreach(var player in Result.Players.Where(p=>visible.Contains(p.Owner)&&Applicable(metric,p)))
            {
                var row=Row();row.name="result-hover-value-"+player.Slot;row.pickingMode=PickingMode.Ignore;row.style.height=23;row.style.minHeight=23;row.style.alignItems=Align.Center;
                var key=new VisualElement{pickingMode=PickingMode.Ignore};key.style.width=6;key.style.height=6;key.style.backgroundColor=playerColor(player);key.style.marginRight=7;row.Add(key);
                var name=Text(playerName(player),12,playerColor(player));name.pickingMode=PickingMode.Ignore;name.style.flexGrow=1;name.style.marginBottom=0;name.style.overflow=Overflow.Hidden;name.style.textOverflow=TextOverflow.Ellipsis;row.Add(name);
                var value=Text(MetricValue(metric,player.Owner,cursorTime).ToString("0.#")+MetricUnit(metric),12,playerColor(player));value.pickingMode=PickingMode.Ignore;value.style.marginBottom=0;value.style.unityTextAlign=TextAnchor.MiddleRight;value.style.unityFontStyleAndWeight=closest==player.Owner?FontStyle.Bold:FontStyle.Normal;row.Add(value);
                if(metric<2){var glyph=new OrbitalGlyph(metric==0?"credits":"army"){Tint=playerColor(player)};glyph.style.width=16;glyph.style.height=16;glyph.style.marginLeft=4;row.Add(glyph);}
                cursorRows.Add(row);
                if(metric!=2){
                    var counts=Row();counts.name="result-building-counts-"+player.Slot;counts.pickingMode=PickingMode.Ignore;counts.style.flexWrap=Wrap.Wrap;
                    foreach(PlayableBuildingKind kind in Enum.GetValues(typeof(PlayableBuildingKind))){
                        int count=Result.BuildingCount(player.Owner,kind,cursorTime);if(count==0||BuildingMetric(kind)!=metric)continue;
                        var cell=Row();cell.name="result-building-count-"+player.Slot+"-"+kind;cell.pickingMode=PickingMode.Ignore;cell.style.width=156;cell.style.height=22;cell.style.alignItems=Align.Center;
                        var icon=new OrbitalGlyph(OrbitalPrecision.BuildingGlyph(kind)){Tint=playerColor(player),pickingMode=PickingMode.Ignore};icon.style.width=16;icon.style.height=16;icon.style.marginRight=4;cell.Add(icon);
                        var label=Text(OfflinePadWorldActions.Name(kind)+" ×"+count,10,playerColor(player));label.pickingMode=PickingMode.Ignore;label.style.marginBottom=0;cell.Add(label);counts.Add(cell);
                    }
                    cursorRows.Add(counts);
                }
            }
            if(cursorRows.childCount==0)cursorRows.Add(Text("Нет выбранных игроков",11,OrbitalTheme.Muted));
            var position=local.HasValue?chart.LocalToWorld(local.Value):chart.LocalToWorld(new Vector2((float)(cursorTime/Math.Max(1,Result.Duration))*chart.contentRect.width,chart.contentRect.height*.4f));
            int columns=Math.Max(1,Math.Min(3,cursorRows.Children().Where(r=>r.name.StartsWith("result-building-counts-")).Select(r=>r.childCount).DefaultIfEmpty(0).Max()));
            float height=50;
            foreach(var row in cursorRows.Children())height+=(float)(row.name.StartsWith("result-building-counts-")?22*Math.Ceiling(row.childCount/(double)columns):27);
            PositionOverlay(graphTip,Root.WorldToLocal(position)+new Vector2(14,10),Math.Max(250,columns*156+22),height);
        }
    }
}
