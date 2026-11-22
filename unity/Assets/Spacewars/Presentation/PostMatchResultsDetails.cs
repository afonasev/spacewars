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
        private const int DetailIconSize = 52;
        private const int TotalIconSize = 56;
        private const int TotalCellWidth = 100;
        private const int TotalCellHeight = 62;
        private VisualElement overlays,popup,popupAnchor,descriptionTip;
        private ScrollView popupScroll,descriptionScroll;
        private VisualElement descriptionAnchor;
        private readonly Dictionary<int,string> rememberedFocus=new();
        private static bool PlayerExitFact(MatchFact f)=>f.Kind==MatchFactKind.PlayerDefeated||f.Kind==MatchFactKind.PlayerLeft;
        private static bool ResearchFact(MatchFact f)=>f.Kind==MatchFactKind.ResearchCompleted||f.Kind==MatchFactKind.ResearchOrdered;
        private static bool UnitFact(MatchFact f)=>f.Kind==MatchFactKind.UnitCompleted||f.Kind==MatchFactKind.UnitOrdered||f.Kind==MatchFactKind.UnitDestroyed;
        private static bool UpgradeFact(MatchFact f)=>f.Kind==MatchFactKind.RefineryUpgradeCompleted||f.Kind==MatchFactKind.RefineryUpgradeOrdered;
        private static bool RelayTransferFact(MatchFact f)=>f.Kind==MatchFactKind.RelayTransferredOut||f.Kind==MatchFactKind.RelayTransferredIn;
        private string NameOf(MatchFact f)=>f.Kind==MatchFactKind.DominationLeadGained?"Доминирование":f.Kind==MatchFactKind.DominationLeadLost?"Доминирование прервано":RelayTransferFact(f)||f.Kind==MatchFactKind.RelayCaptured||f.Kind==MatchFactKind.RelayNeutralized?f.Name:PlayerExitFact(f)?playerName(Result.Players.First(p=>p.Owner==f.Owner)):ResearchFact(f)?PlayableResearchRules.Name((PlayableResearchKind)f.EntityKind):UpgradeFact(f)?"Улучшение: "+OfflinePadWorldActions.Name((PlayableBuildingKind)f.EntityKind):UnitFact(f)?OfflinePadWorldActions.Name((PlayableEntityKind)f.EntityKind):OfflinePadWorldActions.Name((PlayableBuildingKind)f.EntityKind);
        private string GlyphOf(MatchFact f)=>f.Kind is MatchFactKind.DominationLeadGained or MatchFactKind.DominationLeadLost?"relay-tower":PlayerExitFact(f)?"army":
            ResearchFact(f)?RadialActionPresentation.ResearchArtwork((PlayableResearchKind)f.EntityKind):
            UpgradeFact(f)?OrbitalPrecision.BuildingArtwork((PlayableBuildingKind)f.EntityKind,true):
            UnitFact(f)?RadialActionPresentation.UnitArtwork((PlayableEntityKind)f.EntityKind,false):
            OrbitalPrecision.BuildingGlyph((PlayableBuildingKind)f.EntityKind);
        private Color FactColor(MatchFact f)=>playerColor(Result.Players.First(p=>p.Owner==f.Owner));
        private static bool Crossed(MatchFact f)=>f.Kind==MatchFactKind.BuildingSold||f.Kind==MatchFactKind.BuildingLost||f.Kind==MatchFactKind.RelayNeutralized;
        private string EventDescription(MatchFact f)
        {
            string action=f.Kind switch
            {
                MatchFactKind.RelayTransferredOut=>"Передана союзнику",
                MatchFactKind.RelayTransferredIn=>"Получена от союзника",
                MatchFactKind.RelayCaptured=>"Захвачено",
                MatchFactKind.RelayNeutralized=>"Контроль потерян",
                MatchFactKind.PlayerDefeated=>"Побеждён",
                MatchFactKind.PlayerLeft=>"Вышел из игры",
                MatchFactKind.BuildingSold=>"Продано",
                MatchFactKind.BuildingLost or MatchFactKind.UnitDestroyed=>"Уничтожено",
                MatchFactKind.BuildingCompleted=>"Построено",
                MatchFactKind.UnitCompleted=>"Произведено",
                MatchFactKind.UnitOrdered or MatchFactKind.BuildingOrdered or MatchFactKind.RefineryUpgradeOrdered or MatchFactKind.ResearchOrdered=>"Заказано",
                _=>"Завершено"
            };
            return TimeLabel(f.Seconds)+" · "+playerName(Result.Players.First(p=>p.Owner==f.Owner))+"\n"+action;
        }

        private string EntityDescription(MatchFact fact)
        {
            if(ResearchFact(fact))
            {
                var completed=Result.Facts.Where(f=>f.Owner==fact.Owner&&f.Kind==MatchFactKind.ResearchCompleted&&
                    f.Sequence<fact.Sequence).Select(f=>new PlayableResearchOrderSnapshot(f.Sequence,
                        (PlayableResearchKind)f.EntityKind,0,0,0,0,false,true));
                var researchKind=(PlayableResearchKind)fact.EntityKind;
                return PlayableResearchRules.Description(matchProfile,researchKind)+"\n"+
                    string.Join("\n",PlayableMenuDescriptions.ResearchCardStats(matchProfile,researchKind,completed));
            }
            if(UpgradeFact(fact))
            {
                var detail=PlayableMenuDescriptions.LocalBuildingUpgrade(matchProfile,(PlayableBuildingKind)fact.EntityKind);
                return detail.Description+"\n"+string.Join("\n",detail.Stats);
            }
            if(UnitFact(fact))
                {
                var unit=(PlayableEntityKind)fact.EntityKind;
                var details=WithoutTitle(PlayableMenuDescriptions.Unit(matchProfile,unit));
                if(unit==PlayableEntityKind.Tank)
                    details="Бронированная боевая машина с поворотной башней. Пушка поражает наземные цели."+
                        details.Substring(details.IndexOf('\n'));
                return details;
            }
            if(PlayerExitFact(fact))return "";
            if(fact.Kind==MatchFactKind.RelayCaptured||fact.Kind==MatchFactKind.RelayNeutralized||RelayTransferFact(fact))
                return PlayableMenuDescriptions.RelayDescription;
            var kind=(PlayableBuildingKind)fact.EntityKind;
            var stats=PlayableMenuDescriptions.DefenseStats(matchProfile,kind,null);
            return WithoutTitle(PlayableMenuDescriptions.Building(matchProfile,kind))+
                (stats.Length>0?"\n"+string.Join("\n",stats):"");
        }

        private static string WithoutTitle(string text)=>text.Substring(text.IndexOf('\n')+1);

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
            Root.RegisterCallback<PointerMoveEvent>(e=>
            {
                usingPad=false;
                pointerPosition=e.position;
                if(popup!=null&&!PointerInDetails(pointerPosition))
                {
                    CloseDetails(false);
                    for(int i=0;i<graphs.Count;i++)
                        if(graphs[i].worldBound.Contains(pointerPosition))
                        {
                            InspectGraph(i,cursorTime,graphs[i].WorldToLocal(pointerPosition));
                            break;
                        }
                }
                else if(popup==null&&descriptionAnchor!=null&&!PointerInDetails(pointerPosition))
                    HideDescription();
            });
            Root.RegisterCallback<GeometryChangedEvent>(_=>
            {
                if(descriptionAnchor!=null)PlaceDescription(descriptionAnchor);
            });
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
            var anchorElement=popupAnchor??descriptionAnchor;
            if(anchorElement==null)return false;
            var anchor=anchorElement.worldBound;
            Rect region=popup?.worldBound??anchor;
            region=Rect.MinMaxRect(Math.Min(region.xMin,anchor.xMin)-4,Math.Min(region.yMin,anchor.yMin)-4,Math.Max(region.xMax,anchor.xMax)+4,Math.Max(region.yMax,anchor.yMax)+4);
            if(descriptionTip!=null){var t=descriptionTip.worldBound;region=Rect.MinMaxRect(Math.Min(region.xMin,t.xMin),Math.Min(region.yMin,t.yMin),Math.Max(region.xMax,t.xMax),Math.Max(region.yMax,t.yMax));}
            return region.Contains(position);
        }
        private VisualElement MakeEventIcon(MatchFact f,int size,bool currentCount=false,bool includeOrders=false,bool brighten=false)
        {
            var cell=new VisualElement{focusable=true};cell.AddToClassList("result-focusable");cell.style.width=size;cell.style.height=size;cell.style.minWidth=size;cell.style.flexShrink=0;
            var color=FactColor(f);
            var glyph=new OrbitalGlyph(GlyphOf(f)){Tint=brighten?Color.Lerp(color,Color.white,.45f):color};
            glyph.style.width=size;glyph.style.height=size;
            var portrait=glyph.Q<ModelPortrait>("glyph-model");
            glyph.FitEventContent(3);
            if(UnitFact(f))portrait.SetOutlineColor(new Color(OrbitalTheme.Ink.r,OrbitalTheme.Ink.g,OrbitalTheme.Ink.b,.75f));
            else if(brighten)portrait.AddOutlineStroke(1.25f);
            cell.Add(glyph);
            bool showUpgradeMark=UpgradeFact(f)||ResearchFact(f)&&
                RadialActionPresentation.ResearchUsesUpgradeMark((PlayableResearchKind)f.EntityKind);
            if(showUpgradeMark&&!portrait.HasUpgradeMark)
            {
                var mark=new PortraitUpgradeMark{name="portrait-upgrade-mark",pickingMode=PickingMode.Ignore};
                var profile=RadialCardProfile.Load();
                if(ResearchFact(f))
                    ResearchIcon.LayoutBadge(mark,size,profile.researchRankSize);
                else
                    mark.style.width=mark.style.height=Mathf.Max(7,size*profile.buildingUpgradeBadgeSize);
                mark.style.display=DisplayStyle.Flex;
                glyph.Add(mark);
            }
            if(Crossed(f))
                cell.Add(RemovalCross(size));
            bool inventory=UnitFact(f)||f.Kind==MatchFactKind.BuildingOrdered||f.Kind==MatchFactKind.BuildingCompleted||Crossed(f)&&f.Kind!=MatchFactKind.RelayNeutralized;
            if(currentCount&&inventory){
                int count=includeOrders?Result.InventoryAtOrder(f):UnitFact(f)?Result.UnitCount(f.Owner,(PlayableEntityKind)f.EntityKind,f.Seconds):Result.BuildingCount(f.Owner,(PlayableBuildingKind)f.EntityKind,f.Seconds);
                string value=count.ToString();float width=3+value.Length*8+2;
                cell.style.flexDirection=FlexDirection.Row;cell.style.width=cell.style.minWidth=size+width;glyph.style.flexShrink=0;
                var number=Text(value,12,FactColor(f));number.name="result-current-count";number.pickingMode=PickingMode.Ignore;number.tooltip=(includeOrders?"В наличии и заказано на ":"В наличии на ")+TimeLabel(f.Seconds)+": "+value;
                number.style.marginBottom=0;number.style.marginLeft=3;number.style.marginRight=0;number.style.paddingLeft=number.style.paddingRight=0;number.style.flexShrink=0;number.style.alignSelf=Align.FlexEnd;cell.Add(number);
            }
            cell.RegisterCallback<FocusInEvent>(_=>cell.style.backgroundColor=new Color(.12f,.27f,.32f));cell.RegisterCallback<FocusOutEvent>(_=>cell.style.backgroundColor=Color.clear);return cell;
        }
        private static VisualElement RemovalCross(int size)
        {
            var cross=new VisualElement{name="result-removal-cross",pickingMode=PickingMode.Ignore};
            cross.style.position=Position.Absolute;cross.style.left=0;cross.style.top=0;
            cross.style.width=size;cross.style.height=size;
            cross.generateVisualContent+=ctx=>{
                var painter=ctx.painter2D;
                float start=size*.18f,end=size*.82f;
                void Stroke(Color color,float width)
                {
                    painter.strokeColor=color;painter.lineWidth=width;
                    painter.BeginPath();painter.MoveTo(new Vector2(start,start));painter.LineTo(new Vector2(end,end));
                    painter.MoveTo(new Vector2(end,start));painter.LineTo(new Vector2(start,end));painter.Stroke();
                }
                Stroke(new Color(.09f,.03f,.04f),5);
                Stroke(new Color(1f,.29f,.32f),2.5f);
            };
            return cross;
        }
        private void OpenEvents(VisualElement anchor,IReadOnlyList<MatchFact> facts)
        {
            if(popupAnchor==anchor&&popup!=null)return;
            const int rowHeight = DetailIconSize+6;
            int listHeight = Math.Min(290,facts.Count*rowHeight);
            OpenPopup(anchor,TimeLabel(facts.Min(f=>f.Seconds))+" · События: "+facts.Count,390,listHeight+40);
            popupScroll=OrbitalTheme.Scroll();popupScroll.name="result-popup-scroll";popupScroll.style.maxHeight=listHeight;popup.Add(popupScroll);
            foreach(var f in facts.OrderBy(f=>f.Tick).ThenBy(f=>f.Sequence))
            {
                var entry=Row();entry.name="result-popup-event-"+f.Sequence;entry.focusable=true;entry.AddToClassList("result-focusable");entry.style.minHeight=rowHeight;entry.style.alignItems=Align.Center;entry.Add(MakeEventIcon(f,DetailIconSize,true,brighten:true));
                var label=Text(TimeLabel(f.Seconds)+" · "+NameOf(f)+(f.Kind==MatchFactKind.BuildingSold?" · продано":f.Kind==MatchFactKind.BuildingLost?" · уничтожено":""),11,FactColor(f));label.style.whiteSpace=WhiteSpace.Normal;label.style.marginLeft=6;label.style.marginBottom=0;entry.Add(label);
                entry.userData=(Action)(()=>ShowDescription(entry,f));entry.RegisterCallback<PointerEnterEvent>(_=>ShowDescription(entry,f));entry.RegisterCallback<FocusInEvent>(_=>ShowDescription(entry,f));entry.RegisterCallback<GeometryChangedEvent>(_=>{if(descriptionAnchor==entry&&descriptionTip!=null)PlaceDescription(entry);});popupScroll.Add(entry);
            }
            FocusPopup();
            if(!usingPad&&facts.Count==1)
            {
                var entry=popup.Q("result-popup-event-"+facts[0].Sequence);
                if(entry!=null)ShowDescription(entry,facts[0]);
            }
        }
        private void OpenPopup(VisualElement anchor,string heading,float width,float height)
        {
            CloseDetails(false);graphTip.style.display=DisplayStyle.None;popupAnchor=anchor;popup=Panel("result-popup");if(!string.IsNullOrEmpty(heading))popup.Add(Text(heading,11,OrbitalTheme.Muted));overlays.Add(popup);
            var a=Root.WorldToLocal(anchor.worldBound.position);
            float below=a.y+anchor.worldBound.height+7;
            bool opensBelow=a.y-height-7<8&&below+height<=Root.resolvedStyle.height-8;
            PositionOverlay(popup,new Vector2(a.x+anchor.worldBound.width*.5f-width*.5f,opensBelow?below:a.y-height-7),width,height);
            var tail=new VisualElement{pickingMode=PickingMode.Ignore};tail.style.position=Position.Absolute;tail.style.width=14;tail.style.height=9;
            if(opensBelow)tail.style.top=-9;else tail.style.bottom=-9;
            tail.style.left=Mathf.Clamp(a.x+anchor.worldBound.width*.5f-popup.resolvedStyle.left-7,8,width-22);
            tail.generateVisualContent+=ctx=>{
                var p=ctx.painter2D;p.fillColor=new Color(.085f,.15f,.19f);p.strokeColor=OrbitalTheme.Line;p.lineWidth=1;
                float edge=opensBelow?8:0,tip=opensBelow?0:8;
                p.BeginPath();p.MoveTo(new Vector2(0,edge));p.LineTo(new Vector2(7,tip));p.LineTo(new Vector2(14,edge));p.ClosePath();p.Fill();p.Stroke();
            };
            popup.Add(tail);popup.RegisterCallback<GeometryChangedEvent>(_=>tail.style.left=Mathf.Clamp(Root.WorldToLocal(anchor.worldBound.center).x-popup.resolvedStyle.left-7,8,width-22));
        }
        private void BindDirectDescription(VisualElement anchor,MatchFact fact)
        {
            anchor.AddToClassList("result-focusable");
            anchor.userData=(Action)(()=>ShowDescription(anchor,fact));
            anchor.RegisterCallback<PointerEnterEvent>(_=>ShowDescription(anchor,fact));
            anchor.RegisterCallback<FocusInEvent>(_=>ShowDescription(anchor,fact));
            anchor.RegisterCallback<FocusOutEvent>(_=>{if(usingPad&&descriptionAnchor==anchor)HideDescription();});
        }

        private void HideDescription()
        {
            descriptionTip?.RemoveFromHierarchy();
            descriptionTip=null;descriptionAnchor=null;descriptionScroll=null;
        }

        private void ShowDescription(VisualElement anchor,MatchFact fact)
        {
            if(descriptionAnchor==anchor&&descriptionTip!=null)return;
            HideDescription();
            descriptionAnchor=anchor;
            descriptionTip=Panel("result-description");
            descriptionTip.pickingMode=PickingMode.Ignore;
            var title=Text(NameOf(fact),12,FactColor(fact));
            title.style.flexShrink=0;
            descriptionTip.Add(title);
            string detail=EntityDescription(fact);
            string context=popupAnchor?.name?.StartsWith("result-cell-")==true?"":EventDescription(fact);
            var body=Text(string.Join("\n\n",new[]{context,detail}.Where(t=>!string.IsNullOrEmpty(t))),12,OrbitalTheme.Ink);
            body.style.whiteSpace=WhiteSpace.Normal;
            body.style.flexShrink=0;
            descriptionScroll=new ScrollView
            {
                name="result-description-scroll",
                horizontalScrollerVisibility=ScrollerVisibility.Hidden,
                verticalScrollerVisibility=ScrollerVisibility.Auto
            };
            descriptionScroll.style.flexGrow=0;
            descriptionScroll.style.flexShrink=1;
            descriptionScroll.style.minHeight=0;
            descriptionScroll.Add(body);
            descriptionTip.Add(descriptionScroll);
            overlays.Add(descriptionTip);
            descriptionTip.RegisterCallback<GeometryChangedEvent>(e=>
            {
                if(descriptionAnchor==anchor&&e.newRect.size!=e.oldRect.size)PlaceDescription(anchor);
            });
            PlaceDescription(anchor);
        }

        private void PlaceDescription(VisualElement anchor)
        {
            const float edge=8;
            float rootWidth=Root.resolvedStyle.width,rootHeight=Root.resolvedStyle.height;
            if(float.IsNaN(rootWidth)||float.IsNaN(rootHeight))return;
            float width=Mathf.Clamp(rootWidth-2*edge,0,310);
            float maxHeight=Mathf.Max(0,rootHeight-2*edge);
            // The current height is only for positioning. The cap follows the viewport.
            float height=descriptionTip.resolvedStyle.height;
            height=Mathf.Min(height>0?height:230,maxHeight);
            var a=Root.WorldToLocal(anchor.worldBound.position);
            var cloud=popup?.worldBound??anchor.worldBound;
            float x=Root.WorldToLocal(new Vector2(cloud.xMax,cloud.yMin)).x+12;
            if(x+width>rootWidth-edge)x=Root.WorldToLocal(cloud.position).x-width-12;
            if(popup==null&&x<edge)
            {
                x=a.x;
                a.y-=height+7;
            }
            descriptionTip.style.width=width;
            descriptionTip.style.maxHeight=maxHeight;
            descriptionTip.style.left=Mathf.Clamp(x,edge,Mathf.Max(edge,rootWidth-width-edge));
            descriptionTip.style.top=Mathf.Clamp(a.y,edge,Mathf.Max(edge,rootHeight-height-edge));
        }
        private void FocusPopup(){if(usingPad&&popup!=null){var first=popup.Query<VisualElement>(className:"result-focusable").ToList().FirstOrDefault();first?.Focus();}}
        private void CloseDetails(bool restore)
        {
            var anchor=popupAnchor;HideDescription();popup?.RemoveFromHierarchy();popup=null;popupScroll=null;popupAnchor=null;if(restore&&anchor?.panel!=null)anchor.Focus();
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
        }
        private void OpenTotal(VisualElement anchor,MatchResultPlayer player,int column)
        {
            if(popupAnchor==anchor&&popup!=null)return;
            var facts=Result.Facts.Where(f=>f.Owner==player.Owner&&(column==1?f.Kind==MatchFactKind.UnitCompleted:column==6?f.Kind==MatchFactKind.ResearchCompleted:f.Kind==MatchFactKind.BuildingCompleted||f.Kind==MatchFactKind.RefineryUpgradeCompleted)).GroupBy(f=>new{f.Kind,f.EntityKind})
                .OrderBy(g=>column==1&&RadialActionPresentation.UnitDisplaysLast((PlayableEntityKind)g.Key.EntityKind)).ToArray();
            int columns = Math.Clamp(facts.Length,2,4);
            float availableWidth = float.IsNaN(Root.resolvedStyle.width)?1280:Root.resolvedStyle.width;
            float width = Math.Min(columns*TotalCellWidth+28,availableWidth-16);
            columns = Math.Max(1,(int)(width-28)/TotalCellWidth);
            int rows = Math.Clamp((facts.Length+columns-1)/columns,1,3);
            int listHeight = rows*TotalCellHeight;
            OpenPopup(anchor,"",width,listHeight+20);
            popupScroll=OrbitalTheme.Scroll();popupScroll.name="result-popup-scroll";popupScroll.style.maxHeight=listHeight;popup.Add(popupScroll);popupScroll.contentContainer.style.flexDirection=FlexDirection.Row;popupScroll.contentContainer.style.flexWrap=Wrap.Wrap;
            if(facts.Length==0)popupScroll.Add(Text("Нет завершённых за матч",11,OrbitalTheme.Muted));
            foreach(var group in facts)
            {
                var fact=group.Last();var cell=Row();cell.name="result-total-icon-"+fact.Kind+"-"+fact.EntityKind;cell.focusable=true;cell.AddToClassList("result-focusable");cell.style.width=TotalCellWidth;cell.style.height=TotalCellHeight;cell.style.flexShrink=0;cell.style.alignItems=Align.Center;
                cell.Add(MakeEventIcon(fact,TotalIconSize));var count=Text("×"+group.Count(),13,FactColor(fact));count.style.marginBottom=0;count.style.marginLeft=4;cell.Add(count);cell.userData=(Action)(()=>ShowDescription(cell,fact));cell.RegisterCallback<PointerEnterEvent>(_=>ShowDescription(cell,fact));cell.RegisterCallback<FocusInEvent>(_=>ShowDescription(cell,fact));cell.RegisterCallback<GeometryChangedEvent>(_=>{if(descriptionAnchor==cell&&descriptionTip!=null)PlaceDescription(cell);});popupScroll.Add(cell);
            }
            FocusPopup();
        }
        private void RememberFocus(){var focus=Root.focusController?.focusedElement as VisualElement;if(focus!=null&&!string.IsNullOrEmpty(focus.name))rememberedFocus[SelectedTab]=focus.name;}
    }
}
