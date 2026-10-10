using System;
using System.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private bool spectatorMode,spectatorCollapsed;
        private PlayableOwner? spectatorPerspective;
        private PlayableSpectatorFrame spectatorFrame;
        private int inspectionEntityId;
        private VisualElement spectatorPanel,spectatorRows,spectatorHead;
        private Label spectatorTooltip;
        private readonly System.Collections.Generic.Dictionary<string,VisualElement> spectatorRowCache=new System.Collections.Generic.Dictionary<string,VisualElement>();
        private long spectatorSequence=-1;
        private double spectatorRate=1;
        private void ResetSpectatorPresentation()
        {
            spectatorMode=matchSetup?.Spectator==true;spectatorPerspective=null;spectatorFrame=null;inspectionEntityId=0;spectatorCollapsed=false;spectatorSequence=-1;spectatorRate=1;
            spectatorRowCache.Clear();spectatorRows?.Clear();if(spectatorPanel!=null){UpdateSpectatorSpeed();UpdateSpectatorCollapse();spectatorPanel.style.display=DisplayStyle.None;}if(spectatorTooltip!=null)spectatorTooltip.style.display=DisplayStyle.None;
            input?.ClearMode();if(input!=null)input.CommandInputEnabled=!spectatorMode;
        }
        private void ReadPresentationFrame()
        {
            if(spectatorMode){spectatorFrame=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);view=spectatorFrame?.Perspective(spectatorPerspective);ValidateInspection();}else view=matchSetup?.Participants!=null?runtime.ParticipantView(LocalOwnerId):runtime.Latest;
        }
        private void CreateSpectatorHud()
        {
            spectatorPanel=new VisualElement{name="spectator-summary"};spectatorPanel.style.position=Position.Absolute;spectatorPanel.style.left=20;spectatorPanel.style.top=20;spectatorPanel.style.width=632;spectatorPanel.style.maxWidth=Length.Percent(95);spectatorPanel.style.backgroundColor=PanelColor;spectatorPanel.style.display=DisplayStyle.None;OrbitalPrecision.Frame(spectatorPanel);root.Add(spectatorPanel);
            var controls=new VisualElement{name="spectator-controls"};controls.style.paddingLeft=8;controls.style.paddingRight=8;controls.style.paddingTop=8;controls.style.paddingBottom=8;controls.style.flexDirection=FlexDirection.Row;controls.style.height=46;controls.style.alignItems=Align.Center;spectatorPanel.Add(controls);
            foreach(double rate in new[]{.5,1,2,4}){var speed=Button(rate.ToString("0.#",System.Globalization.CultureInfo.InvariantCulture)+"×",()=>{runtime?.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,rate);spectatorRate=rate;UpdateSpectatorSpeed();});speed.name="spectator-speed-"+rate;speed.userData=rate;speed.style.width=42;speed.style.minWidth=42;speed.style.paddingLeft=2;speed.style.paddingRight=2;speed.style.paddingTop=0;speed.style.paddingBottom=0;speed.style.whiteSpace=WhiteSpace.NoWrap;speed.style.marginTop=0;speed.style.marginBottom=0;speed.style.height=26;speed.style.minHeight=26;speed.style.fontSize=12;speed.style.marginLeft=4;controls.Add(speed);}
            var spacer=new VisualElement();spacer.style.flexGrow=1;controls.Add(spacer);var fold=Button("‹",()=>{spectatorCollapsed=!spectatorCollapsed;UpdateSpectatorCollapse();});fold.name="spectator-collapse";fold.style.width=28;fold.style.minWidth=28;fold.style.paddingLeft=2;fold.style.paddingRight=2;fold.style.paddingTop=0;fold.style.paddingBottom=0;fold.style.marginTop=0;fold.style.marginBottom=0;fold.style.height=26;fold.style.minHeight=26;controls.Add(fold);
            spectatorHead=SpectatorRow();var widths=new[]{106,90,58,120,124,110};var names=new[]{"ИГРОК","КРЕДИТЫ","АРМИЯ","ЮНИТЫ","ПРОИЗВОДСТВО","АПГРЕЙДЫ"};for(int i=0;i<names.Length;i++){var label=SpectatorCell(names[i],widths[i]);label.style.fontSize=10;label.style.color=Muted;spectatorHead.Add(label);}spectatorHead.style.height=26;spectatorPanel.Add(spectatorHead);
            spectatorRows=new VisualElement();spectatorPanel.Add(spectatorRows);UpdateSpectatorSpeed();
            spectatorTooltip=new Label{name="spectator-tooltip",pickingMode=PickingMode.Ignore};spectatorTooltip.style.position=Position.Absolute;spectatorTooltip.style.backgroundColor=PanelColor;spectatorTooltip.style.paddingLeft=10;spectatorTooltip.style.paddingRight=10;spectatorTooltip.style.paddingTop=8;spectatorTooltip.style.paddingBottom=8;spectatorTooltip.style.maxWidth=300;spectatorTooltip.style.whiteSpace=WhiteSpace.Normal;spectatorTooltip.style.fontSize=12;spectatorTooltip.style.display=DisplayStyle.None;root.Add(spectatorTooltip);
            spectatorPanel.RegisterCallback<KeyDownEvent>(evt=>{if(evt.keyCode==KeyCode.Tab&&!ModalVisible()){ToggleMap();evt.StopImmediatePropagation();}},TrickleDown.TrickleDown);
        }
        private static VisualElement SpectatorRow(){var r=new VisualElement();r.style.flexDirection=FlexDirection.Row;r.style.alignItems=Align.Center;r.style.height=36;r.style.flexShrink=0;return r;}
        private static Label SpectatorCell(string text,float width){var l=new Label(text);l.style.width=width;l.style.minWidth=width;l.style.flexShrink=0;l.style.fontSize=11;l.style.marginLeft=0;l.style.marginRight=0;l.style.paddingLeft=8;l.style.unityTextAlign=TextAnchor.MiddleLeft;l.style.overflow=Overflow.Hidden;return l;}
        private VisualElement SpectatorIcons(float width){var e=new VisualElement();e.style.width=width;e.style.flexShrink=0;e.style.flexDirection=FlexDirection.Row;e.style.alignItems=Align.Center;e.style.overflow=Overflow.Hidden;return e;}
        private void SpectatorIcon(VisualElement parent,string glyph,string tooltip,double progress=-1,string badge=null)
        {
            int index=parent.userData is int n?n:0;parent.userData=index+1;
            VisualElement tile;if(index<parent.childCount)tile=parent[index];else{tile=new VisualElement{focusable=true};parent.Add(tile);var captured=tile;tile.RegisterCallback<PointerEnterEvent>(_=>ShowSpectatorTooltip(captured));tile.RegisterCallback<PointerLeaveEvent>(_=>spectatorTooltip.style.display=DisplayStyle.None);tile.RegisterCallback<FocusInEvent>(_=>ShowSpectatorTooltip(captured));tile.RegisterCallback<FocusOutEvent>(_=>spectatorTooltip.style.display=DisplayStyle.None);}
            tile.tooltip=tooltip;tile.Clear();tile.style.width=27;tile.style.height=28;tile.style.flexShrink=0;tile.style.marginRight=3;tile.style.backgroundColor=new Color(.12f,.19f,.21f,.9f);
            var icon=new OrbitalGlyph(glyph){pickingMode=PickingMode.Ignore};icon.style.width=23;icon.style.height=22;tile.Add(icon);
            if(progress>=0){var bar=new VisualElement{pickingMode=PickingMode.Ignore};bar.style.position=Position.Absolute;bar.style.left=0;bar.style.bottom=0;bar.style.width=Length.Percent((float)Math.Max(0,Math.Min(100,progress*100)));bar.style.height=2;bar.style.backgroundColor=Amber;tile.Add(bar);}
            if(badge!=null){var mark=new Label(badge){pickingMode=PickingMode.Ignore};mark.style.position=Position.Absolute;mark.style.right=0;mark.style.top=-5;mark.style.fontSize=11;mark.style.color=Cyan;tile.Add(mark);}
        }
        private void ShowSpectatorTooltip(VisualElement tile){spectatorTooltip.text=tile.tooltip;spectatorTooltip.style.left=Mathf.Clamp(tile.worldBound.xMin,8,Mathf.Max(8,root.resolvedStyle.width-308));spectatorTooltip.style.top=Mathf.Min(tile.worldBound.yMax+6,root.resolvedStyle.height-80);spectatorTooltip.style.display=DisplayStyle.Flex;spectatorTooltip.BringToFront();}
        private static void FinishIcons(VisualElement parent)
        {
            int count=parent.userData is int n?n:0;while(parent.childCount>count)parent.RemoveAt(parent.childCount-1);
            for(int i=0;i<count;i++)parent[i].style.display=i<4?DisplayStyle.Flex:DisplayStyle.None;
            if(count>4){var overflow=parent[3];overflow.tooltip=string.Join("\n",parent.Children().Skip(3).Select(e=>e.tooltip));overflow.Clear();var label=new Label("+"+(count-3)){pickingMode=PickingMode.Ignore};label.style.fontSize=11;label.style.unityTextAlign=TextAnchor.MiddleCenter;label.style.flexGrow=1;overflow.Add(label);}
        }
        private static string SpectatorUnitGlyph(PlayableEntityKind kind)=>kind==PlayableEntityKind.Explorer?"explorer":kind==PlayableEntityKind.Shkval?"shkval":"tank";
        private string SpectatorName(PlayableSnapshot player)=>matchSetup==null?player.OwnerId:matchSetup.ParticipantName((int)player.Owner);
        private int SpectatorTeam(PlayableSnapshot player)=>player.Team;
        private void SetSpectatorPerspective(PlayableOwner owner)
        {
            spectatorPerspective=spectatorPerspective==owner?null:(PlayableOwner?)owner;spectatorSequence=-1;input?.ClearMode();selection.Clear();selectedSite=selectedSlot=0;root.Focus();
            if(spectatorFrame!=null){view=spectatorFrame.Perspective(spectatorPerspective);ValidateInspection();}
        }
        private void ValidateInspection(){if(view!=null&&!view.Entities.Any(e=>e.Id==inspectionEntityId)&&!view.Buildings.Any(b=>b.Id==inspectionEntityId&&b.Phase!=ConstructionPhase.Pending))inspectionEntityId=0;}
        private void UpdateSpectatorCollapse(){spectatorHead.style.display=spectatorRows.style.display=spectatorCollapsed?DisplayStyle.None:DisplayStyle.Flex;spectatorPanel.Q<Button>("spectator-collapse").text=spectatorCollapsed?"›":"‹";}
        private void UpdateSpectatorSpeed(){foreach(var b in spectatorPanel.Query<Button>().ToList())if(b.userData is double rate)b.style.color=rate==spectatorRate?Cyan:Ink;}
        private void UpdateSpectatorHud()
        {
            if(spectatorPanel==null)return;spectatorPanel.style.display=spectatorMode&&!inLobby&&!preparing&&!ModalVisible()?DisplayStyle.Flex:DisplayStyle.None;if(!spectatorMode||spectatorFrame==null)return;
            top.style.display=spectatorPerspective.HasValue?DisplayStyle.Flex:DisplayStyle.None;if(!spectatorPerspective.HasValue)ShowArmyComposition(false);noticeLabel.style.display=DisplayStyle.None;
            if(spectatorSequence==spectatorFrame.Overview.Sequence)return;spectatorSequence=spectatorFrame.Overview.Sequence;
            var players=spectatorFrame.Players.OrderBy(SpectatorTeam).ThenBy(p=>(int)p.Owner).ToArray();var teams=players.Select(SpectatorTeam).Distinct().ToArray();
            var shades=new[]{new Color(.10f,.20f,.24f,.95f),new Color(.20f,.16f,.20f,.95f),new Color(.13f,.22f,.19f,.95f),new Color(.24f,.21f,.13f,.95f),new Color(.18f,.18f,.25f,.95f),new Color(.24f,.17f,.13f,.95f),new Color(.12f,.22f,.22f,.95f),new Color(.22f,.22f,.22f,.95f)};
            foreach(var player in players)
            {
                bool created=!spectatorRowCache.TryGetValue(player.OwnerId,out var row);if(created){row=SpectatorRow();spectatorRowCache.Add(player.OwnerId,row);spectatorRows.Add(row);}row.name="spectator-row-"+player.OwnerId;bool eliminated=player.OwnerEliminated||!player.Buildings.Any(b=>b.Owner==player.Owner&&TerritoryRules.Center(b.Kind)&&b.Phase!=ConstructionPhase.Pending);row.style.backgroundColor=eliminated?new Color(.13f,.15f,.16f,.95f):shades[Array.IndexOf(teams,SpectatorTeam(player))];row.style.opacity=eliminated ? .5f : 1;
                Button name;if(created){name=Button(SpectatorName(player),()=>SetSpectatorPerspective(player.Owner));name.name="spectator-player-"+player.OwnerId;name.style.width=106;name.style.minWidth=106;name.style.height=34;name.style.minHeight=34;name.style.fontSize=11;name.style.paddingLeft=8;name.style.paddingRight=2;name.style.marginRight=0;name.style.marginBottom=0;name.style.backgroundColor=Color.clear;name.style.unityTextAlign=TextAnchor.MiddleLeft;name.style.borderTopWidth=name.style.borderRightWidth=name.style.borderBottomWidth=0;name.style.borderLeftWidth=3;name.style.borderLeftColor=matchSetup!=null?LobbyPaint(player.Owner):Color.HSVToRGB((int)player.Owner/8f,.5f,.9f);name.style.color=spectatorPerspective==player.Owner?Cyan:Ink;name.tooltip=SpectatorName(player);row.Add(name);
                row.RegisterCallback<ClickEvent>(e=>{var target=e.target as VisualElement;for(var at=target;at!=null&&at!=row;at=at.parent)if(at.focusable)return;SetSpectatorPerspective(player.Owner);});
                row.Add(SpectatorCell("",90));row.Add(SpectatorCell("",58));row.Add(SpectatorIcons(120));row.Add(SpectatorIcons(124));row.Add(SpectatorIcons(110));}
                name=(Button)row[0];name.text=SpectatorName(player);name.style.color=spectatorPerspective==player.Owner?Cyan:Ink;((Label)row[1]).text=player.Credits+" +"+player.IncomePerSecond.ToString("0.#")+"/с";((Label)row[2]).text=player.Population.Living+"/"+player.Population.Capacity;
                var units=row[3];units.Clear();foreach(var kind in new[]{PlayableEntityKind.Explorer,PlayableEntityKind.Tank,PlayableEntityKind.Shkval}){int count=player.Entities.Count(e=>e.Owner==player.Owner&&e.Kind==kind);if(count==0)continue;var cell=new VisualElement();cell.style.flexDirection=FlexDirection.Row;var icon=new OrbitalGlyph(SpectatorUnitGlyph(kind));icon.style.width=20;icon.style.height=20;cell.Add(icon);var n=SpectatorCell(count.ToString(),18);n.style.paddingLeft=0;cell.Add(n);units.Add(cell);}
                var production=row[4];production.userData=0;foreach(var b in player.Buildings.Where(b=>b.Owner==player.Owner&&b.Phase!=ConstructionPhase.Ready).OrderBy(b=>b.Id))SpectatorIcon(production,OrbitalPrecision.BuildingGlyph(b.Kind),BuildingName(b.Kind),b.Progress);
                foreach(var b in player.Buildings.Where(b=>b.Owner==player.Owner&&b.Kind==PlayableBuildingKind.Factory).OrderBy(b=>b.Id)){var state=b.PrivateState;foreach(var order in state.Orders)SpectatorIcon(production,SpectatorUnitGlyph(order.Kind),UnitName(order.Kind),order.Active?order.Progress:0,state.Repeat&&state.RepeatKind==order.Kind?"∞":null);if(state.Repeat&&!state.Orders.Any(o=>o.Kind==state.RepeatKind))SpectatorIcon(production,SpectatorUnitGlyph(state.RepeatKind),UnitName(state.RepeatKind)+" · ожидает",0,"∞");}
                FinishIcons(production);var upgrades=row[5];upgrades.userData=0;foreach(var research in player.OwnerResearch.Where(r=>r.Complete||r.Active).OrderBy(r=>r.Kind))SpectatorIcon(upgrades,OrbitalPrecision.ResearchGlyph(research.Kind),ResearchName(research.Kind)+"\n"+ResearchDescription(research.Kind),research.Active?research.Progress:-1,research.Complete?"✓":null);FinishIcons(upgrades);
            }
            UpdateSpectatorCollapse();
        }
        private static string ResearchDescription(PlayableResearchKind kind)=>kind==PlayableResearchKind.TankChassis?"Улучшает ходовые характеристики танка.":kind==PlayableResearchKind.ExplorerAssaultGuns?"Усиливает вооружение исследователя.":"Улучшает наведение ракет Шквала.";
    }
}
