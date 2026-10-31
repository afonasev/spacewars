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
        private VisualElement overlays,popup,popupAnchor,descriptionTip;
        private ScrollView popupScroll;
        private VisualElement descriptionAnchor;
        private readonly Dictionary<int,string> rememberedFocus=new();
        private static bool PlayerExitFact(MatchFact f)=>f.Kind==MatchFactKind.PlayerDefeated||f.Kind==MatchFactKind.PlayerLeft;
        private static bool ResearchFact(MatchFact f)=>f.Kind==MatchFactKind.ResearchCompleted||f.Kind==MatchFactKind.ResearchOrdered;
        private static bool UnitFact(MatchFact f)=>f.Kind==MatchFactKind.UnitCompleted||f.Kind==MatchFactKind.UnitOrdered||f.Kind==MatchFactKind.UnitDestroyed;
        private static bool UpgradeFact(MatchFact f)=>f.Kind==MatchFactKind.RefineryUpgradeCompleted||f.Kind==MatchFactKind.RefineryUpgradeOrdered;
        private string NameOf(MatchFact f)=>PlayerExitFact(f)?playerName(Result.Players.First(p=>p.Owner==f.Owner)):ResearchFact(f)?PlayableResearchRules.Name((PlayableResearchKind)f.EntityKind):UpgradeFact(f)?"Улучшение завода":UnitFact(f)?OfflinePadWorldActions.Name((PlayableEntityKind)f.EntityKind):OfflinePadWorldActions.Name((PlayableBuildingKind)f.EntityKind);
        private string GlyphOf(MatchFact f)=>PlayerExitFact(f)?"army":ResearchFact(f)?OrbitalPrecision.ResearchGlyph((PlayableResearchKind)f.EntityKind):UpgradeFact(f)?"upgrade":UnitFact(f)?((PlayableEntityKind)f.EntityKind).ToString().ToLowerInvariant():OrbitalPrecision.BuildingGlyph((PlayableBuildingKind)f.EntityKind);
        private Color FactColor(MatchFact f)=>playerColor(Result.Players.First(p=>p.Owner==f.Owner));
        private static bool Crossed(MatchFact f)=>f.Kind==MatchFactKind.BuildingSold||f.Kind==MatchFactKind.BuildingLost;
        private string EventDescription(MatchFact f)
        {
            string action=f.Kind==MatchFactKind.PlayerDefeated?"Побеждён":f.Kind==MatchFactKind.PlayerLeft?"Вышел из игры":f.Kind==MatchFactKind.BuildingSold?"Продано":f.Kind==MatchFactKind.BuildingLost?"Уничтожено":f.Kind==MatchFactKind.BuildingCompleted?"Построено":f.Kind==MatchFactKind.UnitCompleted?"Произведено":"Завершено";
            string description=ResearchFact(f)||UpgradeFact(f)?f.Name:"";
            return TimeLabel(f.Seconds)+" · "+playerName(Result.Players.First(p=>p.Owner==f.Owner))+"\n"+action+(string.IsNullOrEmpty(description)?"":"\n"+description);
        }
        private static string EntityDescription(MatchFact f)
        {
            if(ResearchFact(f)||UpgradeFact(f))return f.Name;
            if(UnitFact(f))return ((PlayableEntityKind)f.EntityKind) switch {
                PlayableEntityKind.Tank=>"Бронированная боевая машина с поворотной башней. Пушка поражает наземные цели.",
                PlayableEntityKind.Explorer=>"Колёсная разведывательная машина со скорострельным оружием.",
                PlayableEntityKind.Shkval=>"Ракетная боевая машина для огневой поддержки.",
                PlayableEntityKind.Hawk=>"Боевой вертолёт. Выпускает две ракеты залпом по наземным и воздушным целям.",_=>""};
            return ((PlayableBuildingKind)f.EntityKind) switch {
                PlayableBuildingKind.Factory=>"Производит наземную боевую технику.",PlayableBuildingKind.Hangar=>"Производит боевые вертолёты.",
                PlayableBuildingKind.Refinery=>"Приносит доход. Улучшение увеличивает выработку кредитов.",PlayableBuildingKind.Mine=>"Приносит доход с занятой ресурсной позиции.",
                PlayableBuildingKind.ScientificCenter=>"Позволяет проводить исследования и открывать улучшения.",PlayableBuildingKind.Headquarters=>"Главное здание базы и центр строительства.",PlayableBuildingKind.Outpost=>"Передовой центр строительства.",_=>""};
        }
        private static VisualElement ShoulderBadge(Button tab,string name,out Label symbol)
        {
            var holder=new VisualElement{name=name,pickingMode=PickingMode.Ignore};holder.style.position=Position.Absolute;if(name=="result-shoulder-left")holder.style.left=10;else holder.style.right=10;holder.style.top=0;holder.style.bottom=0;holder.style.width=38;holder.style.justifyContent=Justify.Center;
            var badge=new VisualElement{pickingMode=PickingMode.Ignore};badge.style.width=38;badge.style.height=22;holder.Add(badge);
            badge.generateVisualContent+=ctx=>{var p=ctx.painter2D;p.fillColor=new Color(.045f,.075f,.09f);p.strokeColor=OrbitalTheme.Cyan;p.lineWidth=1.2f;p.BeginPath();p.MoveTo(new Vector2(6,1));p.LineTo(new Vector2(31,1));p.LineTo(new Vector2(37,6));p.LineTo(new Vector2(37,21));p.LineTo(new Vector2(1,21));p.LineTo(new Vector2(1,6));p.ClosePath();p.Fill();p.Stroke();};
            symbol=new Label{pickingMode=PickingMode.Ignore};symbol.style.position=Position.Absolute;symbol.style.left=symbol.style.right=symbol.style.top=symbol.style.bottom=0;symbol.style.paddingLeft=symbol.style.paddingRight=symbol.style.paddingTop=symbol.style.paddingBottom=0;symbol.style.marginLeft=symbol.style.marginRight=symbol.style.marginTop=symbol.style.marginBottom=0;symbol.style.unityTextAlign=TextAnchor.MiddleCenter;symbol.style.fontSize=11;symbol.style.unityFontStyleAndWeight=FontStyle.Bold;symbol.style.color=OrbitalTheme.Ink;badge.Add(symbol);tab.Add(holder);holder.style.display=DisplayStyle.None;return holder;
        }
        private void InstallDetails()
        {
            Root.userData=this;Root.AddToClassList("result-spatial-navigation");
            overlays=new VisualElement{name="result-overlays",pickingMode=PickingMode.Ignore};overlays.style.position=Position.Absolute;overlays.style.left=overlays.style.right=overlays.style.top=overlays.style.bottom=0;Root.Add(overlays);
            graphTip=Panel("result-graph-tooltip");graphTip.pickingMode=PickingMode.Ignore;cursorHeader=Text("",12,OrbitalTheme.Ink);cursorHeader.pickingMode=PickingMode.Ignore;graphTip.Add(cursorHeader);cursorRows=new VisualElement{pickingMode=PickingMode.Ignore};graphTip.Add(cursorRows);overlays.Add(graphTip);graphTip.style.display=DisplayStyle.None;
            shoulderLeft=ShoulderBadge(tabs[0],"result-shoulder-left",out shoulderLeftSymbol);shoulderRight=ShoulderBadge(tabs[2],"result-shoulder-right",out shoulderRightSymbol);
            Root.RegisterCallback<PointerMoveEvent>(e=>{usingPad=false;pointerPosition=e.position;if(popup!=null&&!PointerInDetails(pointerPosition)){CloseDetails(false);for(int i=0;i<graphs.Count;i++)if(graphs[i].worldBound.Contains(pointerPosition)){InspectGraph(i,cursorTime,graphs[i].WorldToLocal(pointerPosition));break;}}});
            Root.RegisterCallback<PointerLeaveEvent>(_=>{if(!usingPad)CloseDetails(false);});
        }
        private Vector2 pointerPosition;
        private static VisualElement Panel(string name)
        {
            var panel=new VisualElement{name=name};panel.style.position=Position.Absolute;panel.style.backgroundColor=new Color(.085f,.15f,.19f,1f);panel.style.paddingLeft=9;panel.style.paddingRight=9;panel.style.paddingTop=7;panel.style.paddingBottom=7;
            panel.style.borderLeftWidth=panel.style.borderRightWidth=panel.style.borderTopWidth=panel.style.borderBottomWidth=1;panel.style.borderLeftColor=panel.style.borderRightColor=panel.style.borderTopColor=panel.style.borderBottomColor=OrbitalTheme.Line;return panel;
        }
        private void PositionOverlay(VisualElement element,Vector2 requested,float width,float height)
        {
            float rootWidth=float.IsNaN(Root.resolvedStyle.width)?1280:Root.resolvedStyle.width,rootHeight=float.IsNaN(Root.resolvedStyle.height)?720:Root.resolvedStyle.height;
            width=Math.Min(width,Math.Max(120,rootWidth-16));height=Math.Min(height,Math.Max(70,rootHeight-80));
            element.style.width=width;element.style.maxHeight=height;element.style.left=Mathf.Clamp(requested.x,8,Math.Max(8,rootWidth-width-8));element.style.top=Mathf.Clamp(requested.y,8,Math.Max(8,rootHeight-height-8));
        }
        private bool PointerInDetails(Vector2 position)
        {
            if(popup==null||popupAnchor==null)return false;
            Rect region=popup.worldBound;var anchor=popupAnchor.worldBound;region=Rect.MinMaxRect(Math.Min(region.xMin,anchor.xMin)-4,Math.Min(region.yMin,anchor.yMin)-4,Math.Max(region.xMax,anchor.xMax)+4,Math.Max(region.yMax,anchor.yMax)+4);
            if(descriptionTip!=null){var t=descriptionTip.worldBound;region=Rect.MinMaxRect(Math.Min(region.xMin,t.xMin),Math.Min(region.yMin,t.yMin),Math.Max(region.xMax,t.xMax),Math.Max(region.yMax,t.yMax));}
            return region.Contains(position);
        }
        private VisualElement MakeEventIcon(MatchFact f,int size)
        {
            var cell=new VisualElement{focusable=true};cell.AddToClassList("result-focusable");cell.style.width=size;cell.style.height=size;cell.style.minWidth=size;cell.style.flexShrink=0;
            var glyph=new OrbitalGlyph(GlyphOf(f)){Tint=FactColor(f)};glyph.style.width=size;glyph.style.height=size;if(UnitFact(f))glyph.Q<ModelPortrait>("glyph-model").SetOutlineColor(OrbitalTheme.Ink);cell.Add(glyph);
            if(Crossed(f)){var cross=Text("╱",size,FactColor(f));cross.pickingMode=PickingMode.Ignore;cross.style.position=Position.Absolute;cross.style.left=0;cross.style.top=-3;cell.Add(cross);}
            cell.RegisterCallback<FocusInEvent>(_=>cell.style.backgroundColor=new Color(.12f,.27f,.32f));cell.RegisterCallback<FocusOutEvent>(_=>cell.style.backgroundColor=Color.clear);return cell;
        }
        private void OpenEvents(VisualElement anchor,IReadOnlyList<MatchFact> facts)
        {
            if(popupAnchor==anchor&&popup!=null)return;
            OpenPopup(anchor,TimeLabel(facts.Min(f=>f.Seconds))+" · События: "+facts.Count,330,Math.Min(270,40+facts.Count*31));
            popupScroll=OrbitalTheme.Scroll();popupScroll.name="result-popup-scroll";popupScroll.style.maxHeight=220;popup.Add(popupScroll);
            foreach(var f in facts.OrderBy(f=>f.Tick).ThenBy(f=>f.Sequence))
            {
                var entry=Row();entry.name="result-popup-event-"+f.Sequence;entry.focusable=true;entry.AddToClassList("result-focusable");entry.style.minHeight=30;entry.style.alignItems=Align.Center;entry.Add(MakeEventIcon(f,19));
                var label=Text(TimeLabel(f.Seconds)+" · "+NameOf(f)+(f.Kind==MatchFactKind.BuildingSold?" · продано":f.Kind==MatchFactKind.BuildingLost?" · уничтожено":""),11,FactColor(f));label.style.whiteSpace=WhiteSpace.Normal;label.style.marginLeft=6;label.style.marginBottom=0;entry.Add(label);
                entry.userData=(Action)(()=>ShowDescription(entry,f));entry.RegisterCallback<PointerEnterEvent>(_=>ShowDescription(entry,f));entry.RegisterCallback<FocusInEvent>(_=>ShowDescription(entry,f));entry.RegisterCallback<GeometryChangedEvent>(_=>{if(descriptionAnchor==entry&&descriptionTip!=null)PlaceDescription(entry);});popupScroll.Add(entry);
            }
            FocusPopup();
        }
        private void OpenPopup(VisualElement anchor,string heading,float width,float height)
        {
            CloseDetails(false);graphTip.style.display=DisplayStyle.None;popupAnchor=anchor;popup=Panel("result-popup");if(!string.IsNullOrEmpty(heading))popup.Add(Text(heading,11,OrbitalTheme.Muted));overlays.Add(popup);
            var a=Root.WorldToLocal(anchor.worldBound.position);PositionOverlay(popup,new Vector2(a.x+anchor.worldBound.width*.5f-width*.5f,a.y-height-7),width,height);
            var tail=new VisualElement{pickingMode=PickingMode.Ignore};tail.style.position=Position.Absolute;tail.style.width=14;tail.style.height=9;tail.style.bottom=-9;
            tail.style.left=Mathf.Clamp(a.x+anchor.worldBound.width*.5f-popup.resolvedStyle.left-7,8,width-22);
            tail.generateVisualContent+=ctx=>{var p=ctx.painter2D;p.fillColor=new Color(.085f,.15f,.19f);p.strokeColor=OrbitalTheme.Line;p.lineWidth=1;p.BeginPath();p.MoveTo(Vector2.zero);p.LineTo(new Vector2(7,8));p.LineTo(new Vector2(14,0));p.ClosePath();p.Fill();p.Stroke();};popup.Add(tail);popup.RegisterCallback<GeometryChangedEvent>(_=>tail.style.left=Mathf.Clamp(Root.WorldToLocal(anchor.worldBound.center).x-popup.resolvedStyle.left-7,8,width-22));
        }
        private void ShowDescription(VisualElement anchor,MatchFact fact)
        {
            descriptionAnchor=anchor;descriptionTip?.RemoveFromHierarchy();descriptionTip=Panel("result-description");descriptionTip.Add(Text(NameOf(fact),12,FactColor(fact)));var body=Text(popupAnchor?.name?.StartsWith("result-cell-")==true?EntityDescription(fact):EventDescription(fact),12,OrbitalTheme.Ink);body.style.whiteSpace=WhiteSpace.Normal;descriptionTip.Add(body);overlays.Add(descriptionTip);
            PlaceDescription(anchor);
            anchor.schedule.Execute(()=>{if(descriptionTip!=null&&popup!=null&&popup.Contains(anchor))PlaceDescription(anchor);});
        }
        private void PlaceDescription(VisualElement anchor)
        {
            var a=Root.WorldToLocal(anchor.worldBound.position);float width=290,height=125;
            var cloud=popup==null?anchor.worldBound:popup.worldBound;float x=Root.WorldToLocal(new Vector2(cloud.xMax,cloud.yMin)).x+12;
            if(x+width>Root.resolvedStyle.width-8)x=Root.WorldToLocal(cloud.position).x-width-12;
            PositionOverlay(descriptionTip,new Vector2(x,a.y),width,height);
        }
        private void FocusPopup(){if(usingPad&&popup!=null){var first=popup.Query<VisualElement>(className:"result-focusable").ToList().FirstOrDefault();first?.Focus();}}
        private void CloseDetails(bool restore)
        {
            var anchor=popupAnchor;descriptionTip?.RemoveFromHierarchy();descriptionTip=null;descriptionAnchor=null;popup?.RemoveFromHierarchy();popup=null;popupScroll=null;popupAnchor=null;if(restore&&anchor?.panel!=null)anchor.Focus();
        }
        private void ShowTable()
        {
            var scroll=OrbitalTheme.Scroll(ScrollViewMode.VerticalAndHorizontal);scroll.name="result-table-scroll";scroll.style.flexGrow=1;content.Add(scroll);
            string[] columns={"Кредиты","Построено\nюнитов","Здания /\nулучшения","Исследования","Уничтожено\nюнитов","Уничтожено\nзданий","Очки"};
            var header=Row();header.style.minWidth=1000;header.style.backgroundColor=OrbitalTheme.Panel;scroll.Add(header);
            void Cell(VisualElement row,string text,float width,Color color,MatchResultPlayer player=null,int column=-1)
            {
                var label=Text(text,12,color);label.style.width=width;label.style.minWidth=width;label.style.flexShrink=0;label.style.paddingLeft=6;label.style.paddingRight=6;label.style.paddingTop=7;label.style.paddingBottom=7;label.style.marginBottom=0;if(column>=0||player==null&&width<180)label.style.unityTextAlign=TextAnchor.MiddleRight;row.Add(label);
                if(player==null)return;
                label.name="result-cell-"+player.Slot+"-"+column;label.focusable=true;label.AddToClassList("result-focusable");
                label.RegisterCallback<FocusInEvent>(_=>label.style.backgroundColor=new Color(.10f,.22f,.27f));label.RegisterCallback<FocusOutEvent>(_=>label.style.backgroundColor=Color.clear);
                if(column==1||column==2||column==6)
                {label.userData=(Action)(()=>OpenTotal(label,player,column));label.RegisterCallback<PointerEnterEvent>(_=>{usingPad=false;OpenTotal(label,player,column);});}
            }
            if(Result.TeamsVisible)Cell(header,"Команда",65,OrbitalTheme.Muted);Cell(header,"Игрок",180,OrbitalTheme.Muted);foreach(string column in columns)Cell(header,column,110,OrbitalTheme.Muted);
            int[] mapping={0,1,2,6,3,4,5};
            foreach(var player in Result.Ranked)
            {
                var row=Row();row.name="result-player-"+player.Slot;row.style.minWidth=1000;row.style.borderBottomWidth=1;row.style.borderBottomColor=OrbitalTheme.Line;scroll.Add(row);
                if(Result.TeamsVisible)Cell(row,(player.Team+(Result.Players.Any(p=>p.Team==0)?1:0)).ToString(),65,OrbitalTheme.Muted);Cell(row,playerName(player),180,playerColor(player));
                foreach(int c in mapping){string value=player.Column(c).ToString("0");if(c==2&&player.BuildingsUpgraded>0)value+=" / +"+player.BuildingsUpgraded;Cell(row,(Result.IsMaximum(player,c)?"★ ":"")+value,110,c==0||c==5?new Color(.95f,.76f,.43f):OrbitalTheme.Ink,player,c);}
            }
            scroll.Add(Text("Здания / улучшения: постройки и отдельно улучшения. Очки — вклад экономики и стоимости сущностей.",11,OrbitalTheme.Muted));
        }
        private void OpenTotal(VisualElement anchor,MatchResultPlayer player,int column)
        {
            if(popupAnchor==anchor&&popup!=null)return;
            var facts=Result.Facts.Where(f=>f.Owner==player.Owner&&(column==1?f.Kind==MatchFactKind.UnitCompleted:column==6?f.Kind==MatchFactKind.ResearchCompleted:f.Kind==MatchFactKind.BuildingCompleted||f.Kind==MatchFactKind.RefineryUpgradeCompleted)).GroupBy(f=>new{f.Kind,f.EntityKind}).ToArray();
            string heading="";
            OpenPopup(anchor,heading,Math.Min(350,Math.Max(205,facts.Length*75+20)),22+Math.Min(3,(facts.Length+3)/4)*43);
            popupScroll=OrbitalTheme.Scroll();popupScroll.name="result-popup-scroll";popupScroll.style.maxHeight=143;popup.Add(popupScroll);popupScroll.contentContainer.style.flexDirection=FlexDirection.Row;popupScroll.contentContainer.style.flexWrap=Wrap.Wrap;
            if(facts.Length==0)popupScroll.Add(Text("Нет завершённых за матч",11,OrbitalTheme.Muted));
            foreach(var group in facts)
            {
                var fact=group.Last();var cell=Row();cell.name="result-total-icon-"+fact.Kind+"-"+fact.EntityKind;cell.focusable=true;cell.AddToClassList("result-focusable");cell.style.width=75;cell.style.height=38;cell.style.alignItems=Align.Center;
                cell.Add(MakeEventIcon(fact,25));var count=Text("×"+group.Count(),12,FactColor(fact));count.style.marginBottom=0;count.style.marginLeft=4;cell.Add(count);cell.userData=(Action)(()=>ShowDescription(cell,fact));cell.RegisterCallback<PointerEnterEvent>(_=>ShowDescription(cell,fact));cell.RegisterCallback<FocusInEvent>(_=>ShowDescription(cell,fact));cell.RegisterCallback<GeometryChangedEvent>(_=>{if(descriptionAnchor==cell&&descriptionTip!=null)PlaceDescription(cell);});popupScroll.Add(cell);
            }
            FocusPopup();
        }
        private void RememberFocus(){var focus=Root.focusController?.focusedElement as VisualElement;if(focus!=null&&!string.IsNullOrEmpty(focus.name))rememberedFocus[SelectedTab]=focus.name;}
    }
}
