using System;
using System.Linq;
using System.Collections.Generic;
using Spacewars.Input;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private VisualElement armyComposition,selectionRoster;
        private readonly Dictionary<int,Button> selectionTiles=new Dictionary<int,Button>();
        private readonly Label[] armyCounts=new Label[3];
        private OrbitalBattleRing battleRing;
        private OfflinePadAction[] battleActions=Array.Empty<OfflinePadAction>();
        private NativeLocalInputProfile battleInputProfile;
        private readonly OfflinePadGestures battleGestures=new OfflinePadGestures();
        private bool battleDismissed,battleRally,battleController;
        private int battleAnchor,battleSite,battleSlot;
        private string battleSector;
        private int battleContextClosedFrame=-1;
        private void CreateArmyComposition()
        {
            armyComposition=new VisualElement{name="army-composition"};armyComposition.style.position=Position.Absolute;armyComposition.style.right=20;armyComposition.style.top=82;armyComposition.style.width=100;armyComposition.style.minWidth=100;armyComposition.style.maxWidth=100;StyleRegion(armyComposition);armyComposition.style.display=DisplayStyle.None;root.Add(armyComposition);
            OrbitalPrecision.Frame(armyComposition);
            for(int i=0;i<armyCounts.Length;i++){armyCounts[i]=new Label();armyCounts[i].style.fontSize=16;armyCounts[i].style.marginBottom=8;armyCounts[i].style.paddingLeft=38;var icon=new OrbitalGlyph(i==0?"explorer":i==1?"tank":"shkval");icon.style.position=Position.Absolute;icon.style.left=0;icon.style.top=0;armyCounts[i].Add(icon);armyComposition.Add(armyCounts[i]);}
            hudFocusButton.RegisterCallback<PointerEnterEvent>(_=>ShowArmyComposition(true));
            hudFocusButton.RegisterCallback<PointerLeaveEvent>(_=>ShowArmyComposition(root.focusController?.focusedElement==hudFocusButton));
            hudFocusButton.RegisterCallback<FocusInEvent>(_=>ShowArmyComposition(true));hudFocusButton.RegisterCallback<FocusOutEvent>(_=>ShowArmyComposition(false));
        }
        private void ShowArmyComposition(bool show){if(armyComposition!=null)armyComposition.style.display=show?DisplayStyle.Flex:DisplayStyle.None;}
        private void CreateSelectionRoster(VisualElement parent)
        {selectionRoster=new VisualElement{name="selection-roster"};selectionRoster.style.flexDirection=FlexDirection.Row;selectionRoster.style.flexWrap=Wrap.Wrap;parent.Add(selectionRoster);}
        private void CreateBattleRing()
        {
            battleRing=new OrbitalBattleRing();battleRing.InvokeAction=(id,hold)=>ExecuteBattleAction(id,hold,false);root.Add(battleRing);buildRegion.RemoveFromHierarchy();commandRegion.RemoveFromHierarchy();
            battleInputProfile=JsonUtility.FromJson<NativeLocalInputProfile>(Resources.Load<TextAsset>("NativeLocalInputProfile").text);battleInputProfile.Validate();
            ConfigurePrecisionHud();
            root.RegisterCallback<KeyDownEvent>(e=>{if(e.keyCode!=KeyCode.Escape||ModalVisible())return;if(battleContextClosedFrame==Time.frameCount){e.StopImmediatePropagation();return;}if(CloseBattleContext())e.StopImmediatePropagation();else if(root.focusController?.focusedElement is Button){Pause(true);e.StopImmediatePropagation();}});
        }
        private bool CloseBattleContext()
        {
            if(battleContextClosedFrame==Time.frameCount)return true;
            if(battleRally){battleContextClosedFrame=Time.frameCount;battleRally=false;battleDismissed=false;return true;}
            if(battleRing==null||battleRing.style.display.value!=DisplayStyle.Flex)return false;
            battleContextClosedFrame=Time.frameCount;battleDismissed=true;selection.Clear();selectedSite=selectedSlot=0;confirmSaleBuilding=0;battleRing.style.display=DisplayStyle.None;battleGestures.Cancel();root.Focus();return true;
        }
        private OfflinePadAction[] CurrentBattleActions()
        {
            if(view==null||paused||restarting||quitting||mapOpen||view.Outcome!=PlayableMatchOutcome.Playing)return Array.Empty<OfflinePadAction>();
            if(selectedSite!=0)return OfflinePadWorldActions.Build(view,profile,selectedSite,selectedSlot);
            if(selection.Count!=1)return Array.Empty<OfflinePadAction>();
            return OfflinePadWorldActions.Building(view,profile,selection.First());
        }
        private void UpdateBattleHud(PlayableBuildingSnapshot building)
        {
            if(PadAuxiliaryWheel){battleRing.style.display=DisplayStyle.None;return;}
            if(paused||view.Outcome!=PlayableMatchOutcome.Playing)ShowArmyComposition(false);
            var population=view.Population;hudFocusButton.text=""+(population==null?view.Entities.Count(e=>e.Owner==view.Owner).ToString():population.Living+" / "+population.Capacity);PackResourceRow();
            var kinds=new[]{PlayableEntityKind.Explorer,PlayableEntityKind.Tank,PlayableEntityKind.Shkval};
            for(int i=0;i<kinds.Length;i++){int count=view.Entities.Count(e=>e.Owner==view.Owner&&e.Kind==kinds[i]);armyCounts[i].text=count.ToString();armyCounts[i].style.display=count>0||!view.Entities.Any(e=>e.Owner==view.Owner)?DisplayStyle.Flex:DisplayStyle.None;}
            armyComposition.style.top=top.worldBound.yMax-root.worldBound.y+8;
            var units=view.Entities.Where(e=>e.Owner==view.Owner&&selection.Contains(e.Id)).ToArray();
            foreach(var id in selectionTiles.Keys.Where(id=>!units.Any(e=>e.Id==id)).ToArray()){selectionTiles[id].RemoveFromHierarchy();selectionTiles.Remove(id);}
            int columns=Mathf.Max(4,(int)((armyRegion.resolvedStyle.width>0?armyRegion.resolvedStyle.width:500)/50));float width=units.Length>columns*2?Mathf.Max(18,Mathf.Floor((armyRegion.resolvedStyle.width-30)/Mathf.Ceil(units.Length/2f))):46;
            foreach(var entity in units){if(!selectionTiles.TryGetValue(entity.Id,out var tile)){int id=entity.Id;tile=Button("",()=>{runtime?.RecordHumanAction(LocalOwnerId);if((input?.AssignedKeyboard??Keyboard.current)?.shiftKey.isPressed==true||(input?.AssignedKeyboard??Keyboard.current)?.ctrlKey.isPressed==true)selection.Remove(id);else{primarySelection=id;}SelectionMarkerEvent();});tile.name="selection-unit-"+id;tile.style.paddingLeft=2;tile.style.paddingRight=2;tile.style.marginRight=2;tile.style.marginBottom=2;tile.style.unityTextAlign=TextAnchor.MiddleCenter;var hp=new VisualElement{name="unit-health",pickingMode=PickingMode.Ignore};hp.style.position=Position.Absolute;hp.style.left=2;hp.style.bottom=2;hp.style.height=4;hp.style.backgroundColor=Cyan;tile.Add(hp);selectionRoster.Add(tile);selectionTiles.Add(id,tile);}
                tile.text=UnitSymbol(entity.Kind);tile.style.width=width;tile.style.height=28;tile.style.minHeight=28;tile.style.fontSize=width<30?12:18;tile.tooltip=UnitName(entity.Kind)+" · HP "+entity.Health+" / "+PlayableUnitRules.Health(profile,entity.Kind);tile.Q("unit-health").style.width=Length.Percent((float)(100*entity.Health/PlayableUnitRules.Health(profile,entity.Kind)));
            }
            selectionRoster.style.display=units.Length>1?DisplayStyle.Flex:DisplayStyle.None;
            if(building==null&&selectedSite!=0)selectionLabel.text=siteLabel.text;
            // Transactions move to the ring; queues and cancellations remain in context.
            var row=armyRegion.Q("production-actions");if(row!=null)row.style.display=DisplayStyle.None;
            upgradeRefinery.style.display=upgradeProgress.style.display.value==DisplayStyle.Flex?DisplayStyle.Flex:DisplayStyle.None;
            foreach(var button in researchButtons)button.style.display=DisplayStyle.None;
            sellBuilding.style.display=repairBuilding.style.display=DisplayStyle.None;
            noticeLabel.text=notice;noticeLabel.style.display=string.IsNullOrEmpty(notice)?DisplayStyle.None:DisplayStyle.Flex;noticeLabel.style.maxWidth=Mathf.Min(320,root.resolvedStyle.width-12);
            UpdatePrecisionContext(building);
            int anchor=building?.Id??0;if(anchor!=battleAnchor||selectedSite!=battleSite||selectedSlot!=battleSlot){battleDismissed=false;battleAnchor=anchor;battleSite=selectedSite;battleSlot=selectedSlot;confirmSaleBuilding=0;battleGestures.SetMode("buildingWheel");}
            battleActions=CurrentBattleActions();foreach(var action in battleActions)if(action.Sector.Hold=="sell"&&confirmSaleBuilding==battleAnchor&&battleAnchor!=0)action.Label="✓ Подтвердить продажу";bool show=battleActions.Length>0&&!battleDismissed&&!battleRally;
            battleRing.style.display=show?DisplayStyle.Flex:DisplayStyle.None;
            if(!show)return;
            var point=building?.Position??view.Sites.First(s=>s.Site.Id==selectedSite).Site.Position;
            if(selectedSite!=0&&selectedSlot!=0)point=view.Sites.First(s=>s.Site.Id==selectedSite).Site.Slots.First(s=>s.Id==selectedSlot).Position;
            Vector2 at=cameraView==null?new Vector2(root.resolvedStyle.width/2,root.resolvedStyle.height/2):PanelPoint(cameraView.WorldToScreenPoint(world.Point(point)));
            float w=root.resolvedStyle.width>0?root.resolvedStyle.width:1280,h=root.resolvedStyle.height>0?root.resolvedStyle.height:800;
            // Radius/gestures remain designer-owned by NativeLocalInputProfile. Pixel margins below
            // are packing invariants: keep interactive controls inside their viewport and above the deck.
            float deck=armyRegion.worldBound.yMin>0?armyRegion.worldBound.yMin-root.worldBound.y:h-160;
            float minY=Mathf.Max(58,top.worldBound.yMax-root.worldBound.y+8);
            float maxDiameter=Mathf.Min(battleInputProfile.radialRadius*2,Mathf.Max(100,deck-minY-12));
            float diameter=Mathf.Min(battleActions.Length<=2?210:280,maxDiameter);battleRing.Resize(diameter);
            float maxY=Mathf.Max(minY,Mathf.Min(h-diameter-10,deck-diameter-10));
            float left=Mathf.Clamp(at.x-diameter/2,8,Mathf.Max(8,w-diameter-8));battleRing.style.left=left;battleRing.style.top=Mathf.Clamp(at.y-diameter/2,minY,maxY);
            var shared=(localCoordinator??this).sharedMapFrame;
            if(shared!=null&&shared.resolvedStyle.display!=DisplayStyle.None)
            {
                var occluder=shared.worldBound;occluder.position-=root.worldBound.position;
                if(occluder.Overlaps(new Rect(left,battleRing.style.top.value.value,diameter,diameter)))
                {left=localViewportIndex%2==0?Mathf.Min(left,occluder.xMin-diameter-8):Mathf.Max(left,occluder.xMax+8);battleRing.style.left=left=Mathf.Clamp(left,8,Mathf.Max(8,w-diameter-8));}
            }
            battleRing.Set(battleActions,building==null?"Строительство":BuildingName(building.Kind),battleController?battleSector:null,battleGestures.Progress,building==null?"building":OrbitalPrecision.BuildingGlyph(building.Kind));battleRing.PositionDetail(left>w-left-diameter,left>w-left-diameter?left:w-left-diameter);
        }
        private int primarySelection;
        private bool battleHudUnattended;
        private static string UnitSymbol(PlayableEntityKind kind)=>kind==PlayableEntityKind.Explorer?"◇":kind==PlayableEntityKind.Shkval?"△":"▣";
        private void ExecuteBattleAction(string id,bool hold,bool controller)
        {
            // Resolve again against the current immutable view: stale UI must not emit a purchase.
            var action=CurrentBattleActions().FirstOrDefault(a=>a.Sector.Id==id);if(action==null||!action.Sector.Enabled)return;
            if(id=="rally"){battleRally=true;notice="";root.Focus();return;}
            if(hold&&action.Sector.Hold!= "repeat"&&controller==false)return;
            var command=hold?action.HoldCommand:action.Command;
            if(!controller&&action.Sector.Hold=="sell"&&!hold){if(confirmSaleBuilding!=battleAnchor){confirmSaleBuilding=battleAnchor;notice="Нажмите «Продать здание» ещё раз для подтверждения";return;}command=action.HoldCommand;confirmSaleBuilding=0;}
            if(command==null)return;
            if(action.Sector.Hold!="sell")confirmSaleBuilding=0;
            if(runtime==null)return;
            var intent=new PlayableCommand(generation,++sequence,LocalOwnerId,command.Kind,command.Entities,command.Point,targetId:command.Target,siteId:command.Site,slotId:command.Slot,parentId:command.Parent,buildingKind:command.Building,unitKind:command.Unit,researchKind:command.Research);
            var result=runtime.TrySubmit(intent);AudioCommand(intent,result.Accepted);
            noticeSequence=sequence;notice=result.Accepted?"":Friendly(result.Status);Record("radial "+id+" kind="+command.Kind+" admission="+result.Status);
            if(command.Kind==PlayableCommandKind.BuildAt&&result.Accepted){selection.Clear();selectedSite=selectedSlot=0;battleDismissed=true;battleRing.style.display=DisplayStyle.None;battleGestures.Cancel();}
            root.Focus();
        }
        private Vector2 battleCursor;
        private VisualElement battleCursorVisual;
        private void PollBattleController()=>PollOrdinaryPad();
    }
}
