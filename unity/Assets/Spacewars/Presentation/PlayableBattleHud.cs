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
        private readonly Label[] armyCounts=new Label[4];
        private OrbitalBattleRing battleRing;
        private OfflinePadAction[] battleActions=Array.Empty<OfflinePadAction>();
        private NativeLocalInputProfile battleInputProfile;
        private readonly OfflinePadGestures battleGestures=new OfflinePadGestures();
        private bool battleDismissed,battleRally,battleController,armyCompositionHovered;
        private int battleAnchor,battleSite,battleSlot;
        private string battleSector;
        private int battleContextClosedFrame=-1;
        private void CreateArmyComposition()
        {
            // Use the existing resource-row height, font and glyph grid; this is fixed HUD packing.
            armyComposition=new VisualElement{name="army-composition"};armyComposition.style.position=Position.Absolute;
            armyComposition.style.flexDirection=FlexDirection.Row;armyComposition.style.flexWrap=Wrap.NoWrap;armyComposition.style.alignItems=Align.Center;
            armyComposition.style.height=26;armyComposition.style.paddingTop=armyComposition.style.paddingBottom=2;
            armyComposition.style.display=DisplayStyle.None;root.Add(armyComposition);
            for(int i=0;i<armyCounts.Length;i++)
            {
                var count=armyCounts[i]=new Label();count.style.fontSize=12;count.style.height=24;count.style.flexShrink=0;
                count.style.marginLeft=count.style.marginRight=count.style.marginTop=count.style.marginBottom=0;
                count.style.paddingTop=count.style.paddingBottom=0;count.style.whiteSpace=WhiteSpace.NoWrap;count.style.unityTextAlign=TextAnchor.MiddleLeft;
                count.style.color=Ink;count.style.unityTextOutlineWidth=.15f;count.style.unityTextOutlineColor=PanelColor;
                var icon=new OrbitalGlyph(i==0?"explorer":i==1?"tank":i==2?"shkval":"hawk");icon.style.position=Position.Absolute;icon.style.left=0;icon.style.top=0;
                icon.Q<ModelPortrait>("glyph-model").SetOutlineColor(Ink);count.Add(icon);armyComposition.Add(count);
            }
            hudFocusButton.RegisterCallback<PointerEnterEvent>(_=>{armyCompositionHovered=true;ShowArmyComposition(true);});
            hudFocusButton.RegisterCallback<PointerLeaveEvent>(_=>{armyCompositionHovered=false;ShowArmyComposition(root.focusController?.focusedElement==hudFocusButton);});
            hudFocusButton.RegisterCallback<FocusInEvent>(_=>ShowArmyComposition(true));hudFocusButton.RegisterCallback<FocusOutEvent>(_=>ShowArmyComposition(false));
        }
        private void ShowArmyComposition(bool show){if(armyComposition!=null)armyComposition.style.display=(show||mapOpen)&&(!spectatorMode||spectatorPerspective.HasValue)?DisplayStyle.Flex:DisplayStyle.None;}
        private void UpdateArmyCompositionCounts()
        {
            if(view==null)return;
            var kinds=new[]{PlayableEntityKind.Explorer,PlayableEntityKind.Tank,PlayableEntityKind.Shkval,PlayableEntityKind.Hawk};
            for(int i=0;i<kinds.Length;i++){int count=view.Entities.Count(e=>e.Owner==view.Owner&&e.Kind==kinds[i]);armyCounts[i].text=count.ToString();armyCounts[i].style.display=count>0?DisplayStyle.Flex:DisplayStyle.None;}
            LayoutArmyComposition();
        }
        private void LayoutArmyComposition()
        {
            if(root==null||armyComposition==null||root.resolvedStyle.width<=0)return;
            bool resourcesLeft=localHumanCount>1&&localViewportIndex%2==0;
            float left=6,right=root.resolvedStyle.width-6,y=6;
            if(top.worldBound.width>0)
            {
                y=top.worldBound.yMin-root.worldBound.yMin;
                if(resourcesLeft)left=Mathf.Max(left,top.worldBound.xMax-root.worldBound.xMin+8);
                else right=Mathf.Min(right,top.worldBound.xMin-root.worldBound.xMin-8);
            }
            var fps=root.panel?.visualTree.Q<Label>("device-fps");
            if(NativeDevicePreferences.ShowFps&&fps!=null&&fps.worldBound.height>0)
            {
                var bounds=fps.worldBound;bounds.position-=root.worldBound.position;
                if(bounds.yMin<y+26&&bounds.yMax>y)
                {
                    if(resourcesLeft)right=Mathf.Min(right,bounds.xMin-8);
                    else left=Mathf.Max(left,bounds.xMax+8);
                }
            }
            // Reuse normal 24 px icons, or the existing 16 px resource glyph size when space is tight.
            float textWidth=0;int visibleCount=0;
            foreach(var count in armyCounts)
            {
                if(count.style.display.value==DisplayStyle.None)continue;
                float measured=count.MeasureTextSize(count.text??"0",0,VisualElement.MeasureMode.Undefined,0,VisualElement.MeasureMode.Undefined).x;
                textWidth+=float.IsNaN(measured)||float.IsInfinity(measured)?12:Mathf.Ceil(measured);visibleCount++;
            }
            float glyphSize=textWidth+visibleCount*(24+4+8)<=right-left?24:16;
            float gap=glyphSize==24?8:4,total=0;
            foreach(var count in armyCounts)
            {
                if(count.style.display.value==DisplayStyle.None)continue;
                float measured=count.MeasureTextSize(count.text??"0",0,VisualElement.MeasureMode.Undefined,0,VisualElement.MeasureMode.Undefined).x;
                float width=(float.IsNaN(measured)||float.IsInfinity(measured)?12:Mathf.Ceil(measured))+glyphSize+4+gap;
                count.style.width=width;count.style.paddingLeft=glyphSize+4;
                var icon=count.Q<OrbitalGlyph>();icon.style.width=icon.style.height=glyphSize;icon.style.top=(24-glyphSize)/2;
                total+=width;
            }
            armyComposition.style.width=total;armyComposition.style.left=resourcesLeft?right-total:left;
            armyComposition.style.right=StyleKeyword.Auto;armyComposition.style.top=y;
        }
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
            if(battleRally){battleContextClosedFrame=Time.frameCount;CancelRallyPlacement(battleController);return true;}
            if(battleRing==null||battleRing.style.display.value!=DisplayStyle.Flex)return false;
            battleContextClosedFrame=Time.frameCount;battleDismissed=true;selection.Clear();selectedSite=selectedSlot=0;confirmSaleBuilding=0;battleRing.style.display=DisplayStyle.None;battleGestures.Cancel();root.Focus();return true;
        }
        private OfflinePadAction[] CurrentBattleActions()
        {
            if(spectatorMode||view==null||paused||restarting||quitting||mapOpen||view.Outcome!=PlayableMatchOutcome.Playing)return Array.Empty<OfflinePadAction>();
            if(selectedSite!=0)return OfflinePadWorldActions.Build(view,profile,selectedSite,selectedSlot,freeEconomy:provingGround!=null);
            if(selection.Count!=1)return Array.Empty<OfflinePadAction>();
            return OfflinePadWorldActions.Building(view,profile,selection.First(),battleController,freeEconomy:provingGround!=null);
        }
        private void UpdateBattleHud(PlayableBuildingSnapshot building)
        {
            UpdateOrbitalHud();if(orbitalOpen){battleRing.style.display=DisplayStyle.None;return;}
            if(PadAuxiliaryWheel){battleRing.style.display=DisplayStyle.None;return;}
            if(paused||view.Outcome!=PlayableMatchOutcome.Playing)ShowArmyComposition(false);
            var population=view.Population;hudFocusButton.text=""+(population==null?view.Entities.Count(e=>e.Owner==view.Owner).ToString():population.Living+" / "+population.Capacity);PackResourceRow();
            UpdateArmyCompositionCounts();
            if(building==null&&selectedSite!=0)selectionLabel.text=siteLabel.text;
            // Transactions move to the ring; queues and cancellations remain in context.
            var row=armyRegion.Q("production-actions");if(row!=null)row.style.display=DisplayStyle.None;
            upgradeRefinery.style.display=building?.SystemsDisabled==true?DisplayStyle.None:upgradeProgress.style.display.value==DisplayStyle.Flex?DisplayStyle.Flex:DisplayStyle.None;
            foreach(var button in researchButtons)button.style.display=DisplayStyle.None;
            sellBuilding.style.display=repairBuilding.style.display=DisplayStyle.None;
            noticeLabel.text=notice;noticeLabel.style.display=string.IsNullOrEmpty(notice)?DisplayStyle.None:DisplayStyle.Flex;noticeLabel.style.maxWidth=Mathf.Min(320,root.resolvedStyle.width-12);
            UpdatePrecisionContext(building);
            int anchor=building?.Id??0;if(anchor!=battleAnchor||selectedSite!=battleSite||selectedSlot!=battleSlot){battleDismissed=false;battleAnchor=anchor;battleSite=selectedSite;battleSlot=selectedSlot;confirmSaleBuilding=0;battleGestures.SetMode("buildingWheel");}
            battleActions=CurrentBattleActions();foreach(var action in battleActions)if(action.Sector.Hold=="sell"&&confirmSaleBuilding==battleAnchor&&battleAnchor!=0)action.Label=provingGround!=null?"✓ Подтвердить удаление":"✓ Подтвердить продажу";bool show=battleActions.Length>0&&!battleDismissed&&!battleRally;
            battleRing.style.display=show?DisplayStyle.Flex:DisplayStyle.None;
            if(!show)return;
            battleRing.BringToFront();
            var point=building?.Position??view.Sites.First(s=>s.Site.Id==selectedSite).Site.Position;
            if(selectedSite!=0&&selectedSlot!=0)point=view.Sites.First(s=>s.Site.Id==selectedSite).Site.Slots.Concat(view.Sites.First(s=>s.Site.Id==selectedSite).Site.DefenseSlots(profile)).First(s=>s.Id==selectedSlot).Position;
            Vector2 at=cameraView==null?new Vector2(root.resolvedStyle.width/2,root.resolvedStyle.height/2):PanelPoint(cameraView.WorldToScreenPoint(world.Point(point)));
            float w=root.resolvedStyle.width>0?root.resolvedStyle.width:1280,h=root.resolvedStyle.height>0?root.resolvedStyle.height:800;
            // Radius/gestures remain designer-owned by NativeLocalInputProfile. Pixel margins below
            // are packing invariants: keep interactive controls inside their viewport and above the deck.
            float deck=armyRegion.worldBound.yMin>0?armyRegion.worldBound.yMin-root.worldBound.y:h-160;
            // Short local viewports reserve the measured header rather than a spare fixed row.
            float minY=Mathf.Max(h<500?32:58,top.worldBound.yMax-root.worldBound.y+(h<500?6:8));
            float maxDiameter=Mathf.Min(battleInputProfile.radialRadius*2,Mathf.Max(100,deck-minY-12));
            // The single-action disc packs one icon, a full build label and price; this is layout, not simulation tuning.
            float diameter=Mathf.Min(OrbitalBattleRing.IsCapturedSiteBuild(battleActions)?132:battleActions.Length<=2?210:280,maxDiameter);battleRing.Resize(diameter);
            float maxY=Mathf.Max(minY,Mathf.Min(h-diameter-10,deck-diameter-10));
            float left=Mathf.Clamp(at.x-diameter/2,8,Mathf.Max(8,w-diameter-8));battleRing.style.left=left;battleRing.style.top=Mathf.Clamp(at.y-diameter/2,minY,maxY);
            var shared=(localCoordinator??this).sharedMapFrame;
            if(shared!=null&&shared.resolvedStyle.display!=DisplayStyle.None)
            {
                var occluder=shared.worldBound;occluder.position-=root.worldBound.position;
                if(occluder.Overlaps(new Rect(left,battleRing.style.top.value.value,diameter,diameter)))
                {left=localViewportIndex%2==0?Mathf.Min(left,occluder.xMin-diameter-8):Mathf.Max(left,occluder.xMax+8);battleRing.style.left=left=Mathf.Clamp(left,8,Mathf.Max(8,w-diameter-8));}
            }
            // Reserve a complete side-by-side pair before positioning the card; no overlap fallback.
            if(w>=diameter+254){float reserve=Mathf.Min(battleRing.PreferredDetailWidth+12,w-diameter-12);bool cardLeft=left>w-left-diameter;left=cardLeft?Mathf.Max(left,reserve):Mathf.Min(left,w-diameter-reserve);battleRing.style.left=left;}
            battleRing.ConfigureInput(battleController,SeatPad);
            battleRing.Set(battleActions,building==null?"Строительство":BuildingName(building.Kind),battleController?battleSector:null,battleGestures.Progress,building==null?"building":OrbitalPrecision.BuildingArtwork(building));battleRing.PositionDetail(left>w-left-diameter,left>w-left-diameter?left:w-left-diameter,deck-6,battleRing.style.top.value.value,minY);
            if(!MouseBuildingMenuVisible(at,new Rect(left,battleRing.style.top.value.value,diameter,diameter)))battleRing.style.display=DisplayStyle.None;
        }
        private int primarySelection;
        private bool battleHudUnattended;
        private static string UnitSymbol(PlayableEntityKind kind)=>kind==PlayableEntityKind.Hawk?"✣":kind==PlayableEntityKind.Explorer?"◇":kind==PlayableEntityKind.Shkval?"△":"▣";
        private void ExecuteBattleAction(string id,bool hold,bool controller)
        {
            if(spectatorMode)return;
            // Resolve again against the current immutable view: stale UI must not emit a purchase.
            var action=CurrentBattleActions().FirstOrDefault(a=>a.Sector.Id==id);if(action==null||!action.Sector.Enabled)return;
            if(id=="rally"){BeginRallyPlacement(controller);return;}
            if(hold&&action.Sector.Hold!= "repeat"&&controller==false)return;
            var command=hold?action.HoldCommand:action.Command;
            if(!controller&&action.Sector.Hold=="sell"&&!hold){if(confirmSaleBuilding!=battleAnchor){confirmSaleBuilding=battleAnchor;notice=provingGround!=null?"Нажмите «Убрать здание» ещё раз для подтверждения":"Нажмите «Продать здание» ещё раз для подтверждения";return;}command=action.HoldCommand;confirmSaleBuilding=0;}
            if(command==null)return;
            if(action.Sector.Hold!="sell")confirmSaleBuilding=0;
            if(BotSeat||runtime==null)return;
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
