using System;
using System.Linq;
using System.Collections.Generic;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private RadialCardProfile selectionDockProfile;
        private VisualElement selectionGroupPanel,selectionSummary,selectionStats;
        private Label selectionGroupCount,selectionCost,selectionRosterHint;
        private Button selectionExpand;
        private PlayableEntitySnapshot[] selectionDockUnits=Array.Empty<PlayableEntitySnapshot>();
        private bool selectionRosterExpanded,selectionRosterPadFocus,selectionRosterReleaseGate;
        private int selectionRosterFocusId,selectionRosterColumns=10;
        private string selectionMembership,selectionSummarySignature,selectionStatsSignature;
        private float selectionSummaryWidth;
        private readonly List<(Label Key,Label Value)> selectionStatCells=new();

        private void ConfigureSelectionDock()
        {
            selectionDockProfile=RadialCardProfile.Load();
            selectionCost=SelectionText("selection-cost");selectionCost.style.flexShrink=0;dockIdentity.Add(selectionCost);
            selectionStats=new VisualElement{name="selection-stats"};selectionStats.style.flexDirection=FlexDirection.Row;selectionStats.style.flexWrap=Wrap.Wrap;dockContents.Add(selectionStats);
            selectionGroupPanel=new VisualElement{name="selection-group-panel"};dockBody.Add(selectionGroupPanel);
            var header=new VisualElement{name="selection-group-header"};header.style.flexDirection=FlexDirection.Row;header.style.alignItems=Align.Center;selectionGroupPanel.Add(header);
            selectionGroupCount=SelectionText("selection-group-count");selectionGroupCount.style.flexGrow=1;header.Add(selectionGroupCount);
            selectionExpand=Button("⌃",()=>ToggleSelectionRoster(false));selectionExpand.name="selection-expand";selectionExpand.tooltip="Развернуть состав · LT на геймпаде";header.Add(selectionExpand);
            selectionSummary=new VisualElement{name="selection-summary"};selectionSummary.style.flexDirection=FlexDirection.Row;selectionSummary.style.flexWrap=Wrap.Wrap;selectionGroupPanel.Add(selectionSummary);
            selectionGroupPanel.Add(selectionRoster);
            selectionRosterHint=SelectionText("selection-roster-hint");selectionRosterHint.text="Крестовина — выбор · A — юнит · B — назад";selectionGroupPanel.Add(selectionRosterHint);
            selectionGroupPanel.style.display=selectionStats.style.display=selectionCost.style.display=DisplayStyle.None;
        }
        private Label SelectionText(string name)
        {
            var l=new Label{name=name,pickingMode=PickingMode.Ignore};l.style.color=Ink;l.style.fontSize=selectionDockProfile.selectionFont;
            l.style.marginLeft=l.style.marginRight=l.style.marginTop=l.style.marginBottom=0;
            l.style.paddingLeft=l.style.paddingRight=l.style.paddingTop=l.style.paddingBottom=0;return l;
        }
        private void ToggleSelectionRoster(bool controller)
        {
            if(selectionDockUnits.Length<2||spectatorMode)return;
            selectionRosterExpanded=!selectionRosterExpanded;selectionRosterPadFocus=controller&&selectionRosterExpanded;
            selectionRosterFocusId=selectionDockUnits.Any(e=>e.Id==selectionRosterFocusId)?selectionRosterFocusId:selectionDockUnits[0].Id;
            if(!selectionRosterPadFocus)root.Focus();
            UpdateSelectionRosterVisibility();LayoutBuildingDock(root.resolvedStyle.width);
        }
        private void EndSelectionRosterFocus()
        {
            if(!selectionRosterPadFocus)return;
            selectionRosterPadFocus=false;selectionRosterReleaseGate=true;root?.Focus();UpdateSelectionRosterVisibility();
        }
        private void SelectRosterUnit(int id,bool remove)
        {
            if(!selectionDockUnits.Any(e=>e.Id==id&&e.Health>0)||spectatorMode)return;
            runtime?.RecordHumanAction(LocalOwnerId);
            if(remove){selection.Remove(id);SelectionMarkerEvent();}
            else SetPadSelection(new[]{id});
            selectionRosterExpanded=false;EndSelectionRosterFocus();root.Focus();
        }
        private void RefreshSelectionDock(PlayableBuildingSnapshot building,PlayableEntitySnapshot[] units)
        {
            selectionDockUnits=building==null?units.Where(e=>e.Health>0).OrderBy(e=>e.Id).ToArray():Array.Empty<PlayableEntitySnapshot>();
            var ids=string.Join(",",selectionDockUnits.Select(e=>e.Id));
            if(ids!=selectionMembership)
            {
                // A subset caused by deaths retains expansion; an explicit replacement starts compact.
                bool deathsOnly=selectionMembership!=null&&selectionDockUnits.Length>1&&selectionDockUnits.All(e=>selectionMembership.Split(',').Contains(e.Id.ToString()));
                if(!deathsOnly){selectionRosterExpanded=false;EndSelectionRosterFocus();}
                selectionMembership=ids;
            }
            bool group=selectionDockUnits.Length>1,single=selectionDockUnits.Length==1;
            if(!group){selectionRosterExpanded=false;EndSelectionRosterFocus();}
            dockIdentity.style.display=group?DisplayStyle.None:DisplayStyle.Flex;
            selectionGroupPanel.style.display=group?DisplayStyle.Flex:DisplayStyle.None;
            contextStats.style.display=DisplayStyle.None;
            armyRegion.Q("context-health").style.display=building!=null?DisplayStyle.Flex:DisplayStyle.None;
            selectionStats.style.display=single?DisplayStyle.Flex:DisplayStyle.None;selectionCost.style.display=single?DisplayStyle.Flex:DisplayStyle.None;
            foreach(var id in selectionTiles.Keys.Where(id=>!selectionDockUnits.Any(e=>e.Id==id)).ToArray())
            {selectionTiles[id].RemoveFromHierarchy();selectionTiles.Remove(id);}
            if(group)
            {
                selectionGroupCount.text="ВЫБРАНО "+selectionDockUnits.Length;
                var groups=selectionDockUnits.GroupBy(e=>(e.Kind,e.Upgraded)).OrderBy(g=>g.Key.Kind).ThenBy(g=>g.Key.Upgraded).ToArray();
                string signature=string.Join(";",groups.Select(g=>g.Key+":"+g.Count()));
                if(signature!=selectionSummarySignature)
                {
                    selectionSummarySignature=signature;selectionSummary.Clear();selectionSummaryWidth=0;
                    foreach(var g in groups)
                    {
                        var item=new VisualElement{name="selection-type-"+g.Key.Kind+"-"+g.Key.Upgraded};item.style.flexDirection=FlexDirection.Row;item.style.alignItems=Align.Center;item.style.marginRight=selectionDockProfile.selectionGap*2;
                        item.tooltip=UnitName(g.Key.Kind)+(g.Key.Upgraded?" · улучшенный":"")+" × "+g.Count();
                        var art=new ModelPortrait("selection-type-portrait");art.Set(RadialActionPresentation.UnitArtwork(g.Key.Kind,g.Key.Upgraded));art.style.width=art.style.height=selectionDockProfile.selectionIconSize;item.Add(art);
                        var count=SelectionText("selection-type-count");count.text=g.Count().ToString();item.Add(count);selectionSummary.Add(item);
                        selectionSummaryWidth+=selectionDockProfile.selectionIconSize+count.text.Length*selectionDockProfile.selectionFont+selectionDockProfile.selectionGap*2;
                    }
                }
                foreach(var entity in selectionDockUnits)
                {
                    if(!selectionTiles.TryGetValue(entity.Id,out var tile))
                    {
                        int id=entity.Id;tile=Button("",()=>SelectRosterUnit(id,(input?.AssignedKeyboard??Keyboard.current)?.shiftKey.isPressed==true||(input?.AssignedKeyboard??Keyboard.current)?.ctrlKey.isPressed==true));tile.name="selection-unit-"+id;
                        var portrait=new ModelPortrait("selected-unit-portrait"){name="selected-unit-artwork"};portrait.style.position=Position.Absolute;portrait.style.left=portrait.style.right=portrait.style.top=0;portrait.style.bottom=5;tile.Add(portrait);
                        var hp=new VisualElement{name="unit-health",pickingMode=PickingMode.Ignore};hp.style.position=Position.Absolute;hp.style.left=2;hp.style.bottom=2;hp.style.height=2;hp.style.backgroundColor=Cyan;tile.Add(hp);
                        selectionRoster.Add(tile);selectionTiles.Add(id,tile);
                    }
                    tile.Q<ModelPortrait>().Set(RadialActionPresentation.UnitArtwork(entity.Kind,entity.Upgraded));
                    tile.Q("unit-health").style.width=Length.Percent((float)(100*PlayableUnitRules.HealthRatio(profile,entity)));
                    tile.tooltip=PlayableMenuDescriptions.Unit(profile,entity.Kind,view.OwnerResearch,entity,freeEconomy:provingGround!=null);
                }
                if(!selectionDockUnits.Any(e=>e.Id==selectionRosterFocusId))selectionRosterFocusId=selectionDockUnits[0].Id;
            }
            if(single)UpdateSelectionStats(selectionDockUnits[0]);
            UpdateSelectionRosterVisibility();
        }
        private void UpdateSelectionStats(PlayableEntitySnapshot unit)
        {
            var description=PlayableMenuDescriptions.Unit(profile,unit.Kind,view.OwnerResearch,unit,freeEconomy:provingGround!=null);
            selectionCost.text=provingGround!=null?"Бесплатно":"◆ "+PlayableUnitRules.Cost(profile,unit.Kind).ToString("0.##");
            if(description==selectionStatsSignature)return;selectionStatsSignature=description;
            var lines=description.Split('\n').Where(s=>!string.IsNullOrWhiteSpace(s)).Skip(2).Where(s=>!s.StartsWith("Стоимость:")&&s!="Бесплатно").ToArray();
            while(selectionStatCells.Count<lines.Length)
            {
                var cell=new VisualElement{name="selection-stat"};cell.style.width=Length.Percent(50);cell.style.flexDirection=FlexDirection.Row;cell.style.alignItems=Align.Center;cell.style.paddingRight=selectionDockProfile.selectionGap;cell.style.borderBottomWidth=1;cell.style.borderBottomColor=OrbitalTheme.Line;
                var key=SelectionText("selection-stat-label");key.style.color=Muted;key.style.flexGrow=1;key.style.flexShrink=1;key.style.whiteSpace=WhiteSpace.Normal;key.style.marginRight=selectionDockProfile.selectionGap;
                var value=SelectionText("selection-stat-value");value.style.flexShrink=0;value.style.unityFontStyleAndWeight=FontStyle.Bold;cell.Add(key);cell.Add(value);selectionStats.Add(cell);selectionStatCells.Add((key,value));
            }
            for(int i=0;i<selectionStatCells.Count;i++)
            {
                var (key,value)=selectionStatCells[i];key.parent.style.display=i<lines.Length?DisplayStyle.Flex:DisplayStyle.None;if(i>=lines.Length)continue;
                int split=lines[i].IndexOf(':');key.text=(split<0?lines[i]:lines[i].Substring(0,split)).Replace("Урон за попадание","Урон").Replace("Дальность огня","Дальность");value.text=split<0?"":lines[i].Substring(split+1).Trim();
            }
        }
        private void UpdateSelectionRosterVisibility()
        {
            if(selectionExpand==null)return;
            bool expanded=selectionRosterExpanded&&selectionDockUnits.Length>1;
            selectionRoster.style.display=expanded?DisplayStyle.Flex:DisplayStyle.None;
            selectionExpand.text=expanded?"⌄":"⌃";selectionExpand.tooltip=(expanded?"Свернуть":"Развернуть")+" состав · LT на геймпаде";
            selectionRosterHint.style.display=selectionRosterPadFocus?DisplayStyle.Flex:DisplayStyle.None;
            foreach(var pair in selectionTiles)
            {
                var color=selectionRosterPadFocus&&pair.Key==selectionRosterFocusId?Cyan:OrbitalTheme.Line;
                pair.Value.style.borderTopColor=pair.Value.style.borderBottomColor=pair.Value.style.borderLeftColor=pair.Value.style.borderRightColor=color;
            }
        }
        private void LayoutUnitSelectionDock(float available)
        {
            var p=selectionDockProfile;bool group=selectionDockUnits.Length>1;
            float grid=p.selectionColumns*(p.selectionTileWidth+p.selectionGap);
            float desired=group?(selectionRosterExpanded?Mathf.Max(grid,selectionSummaryWidth):Mathf.Max(p.dockActiveWidth,selectionSummaryWidth))+p.dockPadding*2:p.selectionWidth;
            armyRegion.style.width=Mathf.Min(desired,available);dockPortrait.style.width=dockPortrait.style.height=p.selectionIconSize;
            dockContents.style.display=group?DisplayStyle.None:DisplayStyle.Flex;
            selectionExpand.style.width=selectionExpand.style.height=selectionExpand.style.minHeight=p.selectionIconSize;
            selectionExpand.style.marginLeft=p.selectionGap;selectionExpand.style.marginTop=selectionExpand.style.marginBottom=0;
            selectionExpand.style.paddingTop=selectionExpand.style.paddingBottom=0;selectionExpand.style.fontSize=p.selectionFont+4;
            selectionRoster.style.marginTop=p.selectionGap;selectionSummary.style.marginTop=p.selectionGap;
            selectionGroupCount.style.fontSize=selectionCost.style.fontSize=selectionRosterHint.style.fontSize=p.selectionFont;
            foreach(var label in selectionSummary.Query<Label>().ToList())label.style.fontSize=p.selectionFont+2;
            foreach(var pair in selectionStatCells){pair.Key.style.fontSize=pair.Value.style.fontSize=p.selectionFont;pair.Key.parent.style.minHeight=p.selectionRowHeight;}
            selectionRosterColumns=Mathf.Max(1,Mathf.Min((int)p.selectionColumns,Mathf.FloorToInt((Mathf.Min(desired,available)-p.dockPadding*2)/(p.selectionTileWidth+p.selectionGap))));
            foreach(var tile in selectionTiles.Values)
            {
                tile.style.width=tile.style.minWidth=tile.style.maxWidth=p.selectionTileWidth;tile.style.height=tile.style.minHeight=tile.style.maxHeight=p.selectionTileHeight;
                tile.style.paddingLeft=tile.style.paddingRight=tile.style.paddingTop=tile.style.paddingBottom=0;
                tile.style.marginLeft=tile.style.marginTop=0;tile.style.marginRight=tile.style.marginBottom=p.selectionGap;
                tile.style.flexShrink=0;tile.style.flexGrow=0;
            }
        }
        private bool PollSelectionRosterPad(Gamepad pad)
        {
            bool unavailable=spectatorMode||mapOpen||inLobby||preparing||paused||restarting||quitting||ResultsVisible||view?.Outcome!=PlayableMatchOutcome.Playing||PadAuxiliaryWheel||signalWheelOpen;
            if(unavailable){EndSelectionRosterFocus();return false;}
            if(selectionRosterReleaseGate)
            {
                if(!pad.buttonSouth.isPressed&&!pad.buttonEast.isPressed&&!pad.buttonWest.isPressed&&!pad.buttonNorth.isPressed&&!pad.leftTrigger.isPressed&&pad.dpad.ReadValue()==Vector2.zero)selectionRosterReleaseGate=false;
                battleGestures.Cancel();return true;
            }
            if(pad.leftTrigger.wasPressedThisFrame&&selectionDockUnits.Length>1)
            {
                battleController=true;battleGestures.Cancel();ToggleSelectionRoster(true);
                if(!selectionRosterPadFocus)selectionRosterReleaseGate=true;return true;
            }
            if(!selectionRosterPadFocus)return false;
            if(selectionDockUnits.Length<2){EndSelectionRosterFocus();return true;}
            battleController=true;battleGestures.Cancel();
            if(pad.startButton.wasPressedThisFrame){EndSelectionRosterFocus();if(localCoordinator!=null)localCoordinator.PauseFrom(this,true);else PauseFrom(this,true);return true;}
            if(pad.buttonEast.wasPressedThisFrame){EndSelectionRosterFocus();return true;}
            int index=Array.FindIndex(selectionDockUnits,e=>e.Id==selectionRosterFocusId);if(index<0)index=0;
            int delta=pad.dpad.right.wasPressedThisFrame?1:pad.dpad.left.wasPressedThisFrame?-1:pad.dpad.down.wasPressedThisFrame?selectionRosterColumns:pad.dpad.up.wasPressedThisFrame?-selectionRosterColumns:0;
            index=Mathf.Clamp(index+delta,0,selectionDockUnits.Length-1);selectionRosterFocusId=selectionDockUnits[index].Id;UpdateSelectionRosterVisibility();
            if(pad.buttonSouth.wasPressedThisFrame)SelectRosterUnit(selectionRosterFocusId,false);
            return true;
        }
    }
}
