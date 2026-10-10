using System;
using System.Linq;
using Spacewars.Input;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private readonly OfflinePadSelection ordinaryPadSelection=new OfflinePadSelection();
        private readonly OfflinePadGroups ordinaryPadBases=new OfflinePadGroups();
        private OrbitalBattleRing padAuxiliaryRing;
        private VisualElement padAreaPreview;
        private Texture2D padWhiteArrow,padRedArrow;
        private Gamepad previousSeatPad;
        private int padGroupSlots,padBasePage,padOriginalHq,padRememberedBuilding;
        private NavPoint padCursorGround;
        private bool padCursorInitialized;
        private bool WorldPadOwnsToolkit=>input?.WorldInputEnabled==true&&SeatPad!=null&&(SeatPad.leftStick.ReadValue().magnitude>battleInputProfile.deadzone||SeatPad.dpad.ReadValue().sqrMagnitude>0||SeatPad.buttonSouth.isPressed||SeatPad.buttonSouth.wasReleasedThisFrame||SeatPad.buttonEast.isPressed||SeatPad.buttonEast.wasReleasedThisFrame);
        private bool PadAuxiliaryWheel=>battleGestures.Mode=="groupWheel"||battleGestures.Mode=="groupAssign"||battleGestures.Mode=="baseWheel";
        private void EnsureOrdinaryPadUi()
        {
            if(battleCursorVisual!=null)return;
            padWhiteArrow=AngularCommandCursor.Create(false);padRedArrow=AngularCommandCursor.Create(true);
            battleCursorVisual=new VisualElement{name="gamepad-angular-cursor",pickingMode=PickingMode.Ignore};battleCursorVisual.style.position=Position.Absolute;battleCursorVisual.style.width=battleCursorVisual.style.height=AngularCommandCursor.Size;root.Add(battleCursorVisual);
            padAreaPreview=new VisualElement{name="gamepad-selection-preview",pickingMode=PickingMode.Ignore};padAreaPreview.style.position=Position.Absolute;padAreaPreview.style.borderTopWidth=padAreaPreview.style.borderBottomWidth=padAreaPreview.style.borderLeftWidth=padAreaPreview.style.borderRightWidth=1;padAreaPreview.style.borderTopColor=padAreaPreview.style.borderBottomColor=padAreaPreview.style.borderLeftColor=padAreaPreview.style.borderRightColor=Color.white;padAreaPreview.style.backgroundColor=new Color(1,1,1,.045f);padAreaPreview.style.display=DisplayStyle.None;root.Add(padAreaPreview);
            padAuxiliaryRing=new OrbitalBattleRing{name="gamepad-group-base-ring"};root.Add(padAuxiliaryRing);
        }
        private void HideOrdinaryPadUi()
        {if(battleCursorVisual!=null)battleCursorVisual.style.display=DisplayStyle.None;if(padAreaPreview!=null)padAreaPreview.style.display=DisplayStyle.None;if(padAuxiliaryRing!=null)padAuxiliaryRing.style.display=DisplayStyle.None;}
        private OfflinePadSelectable[] OrdinaryPadUnits()=>view.Entities.Where(e=>e.Health>0).Select(e=>new OfflinePadSelectable(e.Id,(int)e.Kind,e.Owner==view.Owner,e.Position.X,e.Position.Z,PlayableUnitRules.Radius(profile,e.Kind),OfflinePadSelection.InViewport(cameraView,world.Point(e.Position)))).ToArray();
        private OfflinePadBase[] OrdinaryBases()=>view.Buildings.Where(b=>b.Owner==view.Owner&&b.Phase==ConstructionPhase.Ready&&TerritoryRules.Center(b.Kind)).Select(b=>new OfflinePadBase(b.Id,b.Position.X,b.Position.Z,b.Id==padOriginalHq)).ToArray();
        private int OrdinaryPadHit()
        {
            double Distance(NavPoint point)=>(point.X-padCursorGround.X)*(point.X-padCursorGround.X)+(point.Z-padCursorGround.Z)*(point.Z-padCursorGround.Z);
            return view.Entities.Where(e=>e.Health>0&&Distance(e.Position)<=Math.Pow(PlayableUnitRules.Radius(profile,e.Kind),2)).Select(e=>new{e.Id,e.Position})
                .Concat(view.Buildings.Where(b=>b.Health>0&&b.Phase!=ConstructionPhase.Pending&&Distance(b.Position)<=Math.Pow(TerritoryRules.Radius(profile,b.Kind),2)).Select(b=>new{b.Id,b.Position})).OrderBy(e=>Distance(e.Position)).ThenBy(e=>e.Id).Select(e=>e.Id).FirstOrDefault();
        }
        private void FocusPadPoint(NavPoint point)
        {var center=Ground(SeatScreenCenter);cameraView.transform.position+=new Vector3((float)(point.X-center.X),0,(float)(point.Z-center.Z));padCursorGround=Ground(SeatScreenCenter);battleCursor=SeatScreenCenter;}
        private OfflinePadAction[] OrdinaryPadActions(out int pages)
        {
            pages=1;
            if(battleGestures.Mode=="groupWheel"||battleGestures.Mode=="groupAssign")
            {
                if(padGroupSlots==0)padGroupSlots=keyboardGroups.SlotCount(KeyboardEntities());
                bool assign=battleGestures.Mode=="groupAssign";
                return keyboardGroups.PadActions(selection,KeyboardEntities(),assign,padGroupSlots).Select(s=>new OfflinePadAction(s.Id,"Группа "+s.Id,s.Enabled,assign?"RT: отпустить — заменить; удерживать — добавить":"Отпустите RB")).ToArray();
            }
            padGroupSlots=0;
            if(battleGestures.Mode=="baseWheel")
            {
                var bases=OrdinaryBases();return OfflinePadGroups.Bases(bases,padOriginalHq,padBasePage,(int)battleInputProfile.basePageSize,out pages).Select(s=>{var b=bases.FirstOrDefault(b=>b.Id.ToString()==s.Id);return new OfflinePadAction(s.Id,s.Id==padOriginalHq.ToString()?"Первый штаб":s.Enabled?BuildingName(view.Buildings.First(v=>v.Id==b.Id).Kind):"Штаб уничтожен",s.Enabled,"Отпустите LB · ← → страницы"){Point=s.Enabled?new NavPoint(b.X,b.Z):(NavPoint?)null};}).ToArray();
            }
            return battleGestures.Mode=="buildingWheel"?CurrentBattleActions():Array.Empty<OfflinePadAction>();
        }
        private void SetPadSelection(int[] ids,bool keepGroup=false)
        {selection.Clear();foreach(int id in ids)selection.Add(id);selectedSite=selectedSlot=primarySelection=0;battleDismissed=false;confirmSaleBuilding=0;if(!keepGroup)keyboardGroups.Observe(selection);SelectionMarkerEvent();}
        private bool OrdinaryPadMenuIntent(OfflinePadIntent intent,OfflinePadAction[] actions)
        {
            if(intent.Kind=="assignGroup"||intent.Kind=="addGroup")
            {
                if(actions.Any(a=>a.Sector.Id==intent.Id&&a.Sector.Enabled)&&int.TryParse(intent.Id,out int index))
                {if(intent.Kind=="addGroup")keyboardGroups.Add(index,selection,KeyboardEntities());else{var ids=keyboardGroups.Apply(index,true,Time.unscaledTimeAsDouble,selection,KeyboardEntities(),battleInputProfile.doubleTapMs,out _);SetPadSelection(ids,true);}runtime.RecordHumanAction(LocalOwnerId);}return true;
            }
            if(intent.Kind=="cycleGroup"||intent.Kind=="recallGroup")
            {
                var entities=KeyboardEntities();var ids=intent.Kind=="cycleGroup"?keyboardGroups.Cycle(selection,entities):int.TryParse(intent.Id,out int index)?keyboardGroups.RecallPad(index,entities):null;
                if(ids!=null)
                {
                    SetPadSelection(ids,true);runtime.RecordHumanAction(LocalOwnerId);
                    if(intent.Kind=="cycleGroup"){var point=KeyboardControlGroups.Focus(ids,entities,padCursorGround.X,padCursorGround.Z,battleInputProfile.controlGroupFocusRadius);if(point.HasValue)FocusPadPoint(new NavPoint(point.Value.X,point.Value.Z));}
                    var building=view.Buildings.FirstOrDefault(b=>ids.Contains(b.Id)&&b.Owner==view.Owner);if(building!=null&&!mapOpen){padRememberedBuilding=building.Id;battleGestures.SetMode("buildingWheel");}
                }return true;
            }
            if(intent.Kind=="selectAllArmy"){padRememberedBuilding=0;SetPadSelection(view.Entities.Where(e=>e.Owner==view.Owner&&e.Health>0).Select(e=>e.Id).ToArray());runtime.RecordHumanAction(LocalOwnerId);return true;}
            if(intent.Kind=="cycleBase"||intent.Kind=="selectBase")
            {
                var point=intent.Kind=="cycleBase"?ordinaryPadBases.CycleBase(OrdinaryBases(),padOriginalHq):actions.FirstOrDefault(a=>a.Sector.Id==intent.Id&&a.Sector.Enabled)?.Point is NavPoint p?new OfflinePadBase(0,p.X,p.Z):(OfflinePadBase?)null;
                if(point.HasValue)FocusPadPoint(new NavPoint(point.Value.X,point.Value.Z));return true;
            }
            return false;
        }
        private void PollOrdinaryPad()
        {
            var pad=SeatPad;
            if(pad!=previousSeatPad){bool lost=previousSeatPad!=null&&!previousSeatPad.added;battleGestures.Cancel();previousSeatPad=pad;if(lost&&!paused){(localCoordinator??this).PauseFrom(this,true);notice="Геймпад отключён. Подключите устройство и продолжите матч.";}}
            if(pad==null||!pad.added||paused||!localInputFocused||input?.WorldInputEnabled!=true){battleGestures.Cancel();HideOrdinaryPadUi();return;}
            EnsureOrdinaryPadUi();if(!padCursorInitialized){padCursorGround=Ground(SeatScreenCenter);battleCursor=SeatScreenCenter;padCursorInitialized=true;padOriginalHq=view.Buildings.FirstOrDefault(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Headquarters)?.Id??0;}
            if(padRememberedBuilding!=0&&!view.Buildings.Any(b=>b.Id==padRememberedBuilding&&b.Owner==view.Owner)){padRememberedBuilding=0;battleGestures.Cancel();}
            int mask=(pad.buttonSouth.isPressed?1:0)|(pad.buttonEast.isPressed?2:0)|(pad.buttonWest.isPressed?4:0)|(pad.selectButton.isPressed?8:0)|(pad.startButton.isPressed?16:0)|(pad.rightShoulder.isPressed?32:0)|(pad.leftShoulder.isPressed?64:0)|(pad.rightTrigger.isPressed?128:0);
            var ls=pad.leftStick.ReadValue();var rs=pad.rightStick.ReadValue();bool activity=ls.magnitude>battleInputProfile.deadzone||rs.magnitude>battleInputProfile.deadzone||mask!=0;
            if(activity)battleController=true;else if(SeatUsesKeyboard&&Mouse.current?.delta.ReadValue().sqrMagnitude>0)battleController=false;
            if(battleGestures.Blocked&&(ls.magnitude>battleInputProfile.deadzone||rs.magnitude>battleInputProfile.deadzone)){HideOrdinaryPadUi();return;}
            if(!PadAuxiliaryWheel)
            {
                string mode=mapOpen?"tacticalMap":battleRally?"rallyTarget":battleRing.style.display.value==DisplayStyle.Flex?"buildingWheel":"world";
                if(battleGestures.Mode!=mode)battleGestures.SetMode(mode);
            }
            float Axis(float value)=>Mathf.Abs(value)<=battleInputProfile.deadzone?0:Mathf.Sign(value)*(Mathf.Abs(value)-battleInputProfile.deadzone)/(1-battleInputProfile.deadzone);
            if(!battleGestures.Blocked&&!PadAuxiliaryWheel&&battleGestures.Mode!="buildingWheel")
            {
                float dt=Time.unscaledDeltaTime;var right=Vector3.ProjectOnPlane(cameraView.transform.right,Vector3.up).normalized;var forward=Vector3.ProjectOnPlane(cameraView.transform.forward,Vector3.up).normalized;
                var delta=mapOpen?new Vector3(Axis(rs.x),0,-Axis(rs.y)):(right*Axis(rs.x)+forward*Axis(rs.y));delta*=LocalControlSettings.CursorSpeed*dt;
                padCursorGround=new NavPoint(Math.Clamp(padCursorGround.X+delta.x,-profile.ArenaHalfExtent,profile.ArenaHalfExtent),Math.Clamp(padCursorGround.Z+delta.z,-profile.ArenaHalfExtent,profile.ArenaHalfExtent));
                if(!mapOpen)
                {
                    var pan=(right*Axis(ls.x)+forward*Axis(ls.y))*LocalControlSettings.CameraSpeed*dt;cameraView.transform.position+=pan;if(pan.sqrMagnitude>0)padCursorGround=Ground(SeatScreenCenter);
                    cameraView.orthographicSize=Mathf.Clamp(cameraView.orthographicSize+((pad.dpad.down.isPressed?1:0)-(pad.dpad.up.isPressed?1:0))*battleInputProfile.zoomSpeed*dt,(float)profile.CameraMinZoom,(float)profile.CameraMaxZoom);
                    if(pad.leftStickButton.wasPressedThisFrame){ResetKeyboardCamera();padCursorGround=Ground(SeatScreenCenter);}
                }
            }
            else if(battleGestures.Mode=="baseWheel"){if(pad.dpad.left.wasPressedThisFrame)padBasePage=Math.Max(0,padBasePage-1);if(pad.dpad.right.wasPressedThisFrame)padBasePage++;}
            var projected=cameraView.WorldToScreenPoint(world.Point(padCursorGround));var rect=cameraView.pixelRect;battleCursor=new Vector2(Mathf.Clamp(projected.x,rect.xMin,rect.xMax),Mathf.Clamp(projected.y,rect.yMin,rect.yMax));if(!mapOpen)padCursorGround=Ground(battleCursor);
            var actions=OrdinaryPadActions(out int pages);int? sector=OfflinePadGestures.RingSector(ls.x,-ls.y,actions.Length,battleInputProfile.radialDeadzone);battleSector=sector.HasValue?actions[sector.Value].Sector.Id:null;
            foreach(var intent in battleGestures.StepDetailed(Time.unscaledTimeAsDouble*1000,mask,true,true,battleInputProfile,mapOpen||OrdinaryPadHit()==0,sector.HasValue?actions[sector.Value].Sector:null))
            {
                if(intent.Kind=="pause"){if(localCoordinator!=null)localCoordinator.PauseFrom(this,true);else PauseFrom(this,true);break;}
                if(OrdinaryPadMenuIntent(intent,actions))continue;
                if(intent.Kind=="activate"||intent.Kind=="repeat"||intent.Kind=="sell"){ExecuteBattleAction(intent.Id,intent.Kind!="activate",true);if(battleRally)battleGestures.SetMode("rallyTarget");continue;}
                if(intent.Kind=="deselect"){padRememberedBuilding=0;CloseBattleContext();continue;}
                if(intent.Kind=="select")
                {
                    int hit=OrdinaryPadHit();if(view.Buildings.Any(b=>b.Id==hit&&b.Owner==view.Owner)||PickPad(padCursorGround)){Select(battleCursor,battleCursor,false);padRememberedBuilding=view.Buildings.FirstOrDefault(b=>selection.Contains(b.Id)&&b.Owner==view.Owner)?.Id??0;}
                    else SetPadSelection(ordinaryPadSelection.Tap(Time.unscaledTimeAsDouble*1000,hit,OrdinaryPadUnits(),battleInputProfile.doubleTapMs));runtime.RecordHumanAction(LocalOwnerId);
                }
                else if(intent.Kind=="selectCircle"||intent.Kind=="selectScreen"||intent.Kind=="selectMapCircle"){double radius=intent.Kind=="selectScreen"?double.PositiveInfinity:(intent.HeldMs-battleInputProfile.holdMs)/1000*battleInputProfile.selectionGrowth;SetPadSelection(OfflinePadSelection.Area(OrdinaryPadUnits(),padCursorGround.X,padCursorGround.Z,radius,intent.Kind=="selectMapCircle"));runtime.RecordHumanAction(LocalOwnerId);}
                else if(intent.Kind=="cameraJump"){if(padRememberedBuilding!=0){Submit(PlayableCommandKind.SetRally,new[]{padRememberedBuilding},padCursorGround);SetPadSelection(new[]{padRememberedBuilding});battleGestures.SetMode("buildingWheel");}else FocusPadPoint(padCursorGround);}
                else if(intent.Kind=="context"&&mapOpen&&padRememberedBuilding!=0){SetPadSelection(new[]{padRememberedBuilding});battleGestures.SetMode("buildingWheel");}
                else if(intent.Kind=="context"||intent.Kind=="attackMove"){if(mapOpen)Submit(intent.Kind=="attackMove"?PlayableCommandKind.AttackMove:PlayableCommandKind.Move,null,padCursorGround);else Order(battleCursor,intent.Kind=="attackMove",false);}
                else if(intent.Kind=="hold"||intent.Kind=="stop")Submit(intent.Kind=="hold"?PlayableCommandKind.Hold:PlayableCommandKind.Stop);
                else if(intent.Kind=="rally"){Submit(PlayableCommandKind.SetRally,new[]{padRememberedBuilding!=0?padRememberedBuilding:battleAnchor},padCursorGround);battleRally=false;}
            }
            if(!PadAuxiliaryWheel){mapOpen=battleGestures.Map;tacticalOverlay.style.display=mapOpen?DisplayStyle.Flex:DisplayStyle.None;}
            actions=OrdinaryPadActions(out pages);sector=OfflinePadGestures.RingSector(ls.x,-ls.y,actions.Length,battleInputProfile.radialDeadzone);
            if(PadAuxiliaryWheel)
            {
                float size=Mathf.Min(280,Mathf.Min(root.contentRect.width-24,root.contentRect.height-100));padAuxiliaryRing.Resize(size);padAuxiliaryRing.style.left=(root.contentRect.width-size)/2;padAuxiliaryRing.style.top=Mathf.Max(50,(root.contentRect.height-size)/2);padAuxiliaryRing.PositionDetail(false,0);padAuxiliaryRing.Set(actions,battleGestures.Mode=="baseWheel"?"Базы":"Группы",battleGestures.Added?battleGestures.AddedSectorId:sector.HasValue?actions[sector.Value].Sector.Id:null,battleGestures.Progress,battleGestures.Mode=="baseWheel"?"headquarters":"army");padAuxiliaryRing.style.display=DisplayStyle.Flex;padAuxiliaryRing.BringToFront();battleRing.style.display=DisplayStyle.None;
            }else padAuxiliaryRing.style.display=DisplayStyle.None;
            Vector2 point;
            if(mapOpen){var uv=new PlayableMapTransform(profile.ArenaHalfExtent,profile.ArenaHalfExtent).Project(padCursorGround);point=tacticalMap.worldBound.position+new Vector2((float)uv.X*tacticalMap.worldBound.width,(float)uv.Z*tacticalMap.worldBound.height)-root.worldBound.position;}
            else point=PanelPoint(battleCursor);
            battleCursorVisual.style.backgroundImage=battleGestures.AttackPreview||!mapOpen&&HostileAt(battleCursor)?padRedArrow:padWhiteArrow;battleCursorVisual.style.left=point.x-AngularCommandCursor.Hotspot.x;battleCursorVisual.style.top=point.y-AngularCommandCursor.Hotspot.y;battleCursorVisual.style.display=battleController&&!PadAuxiliaryWheel&&battleGestures.Mode!="buildingWheel"?DisplayStyle.Flex:DisplayStyle.None;battleCursorVisual.BringToFront();
            if(battleGestures.SelectionHeldMs>0&&!PadAuxiliaryWheel)
            {
                float radius=(float)((battleGestures.SelectionHeldMs-battleInputProfile.holdMs)/1000*battleInputProfile.selectionGrowth);float pixels=mapOpen?radius*tacticalMap.worldBound.width/(2*(float)profile.ArenaHalfExtent):radius*cameraView.pixelRect.height/(2*cameraView.orthographicSize)/(localCoordinator??this).GetComponent<UIDocument>().panelSettings.scale;
                if(battleGestures.ScreenPreview){point=new Vector2(root.contentRect.width/2,root.contentRect.height/2);pixels=Mathf.Max(root.contentRect.width,root.contentRect.height)/2;}padAreaPreview.style.left=point.x-pixels;padAreaPreview.style.top=point.y-pixels;padAreaPreview.style.width=padAreaPreview.style.height=pixels*2;padAreaPreview.style.borderTopLeftRadius=padAreaPreview.style.borderTopRightRadius=padAreaPreview.style.borderBottomLeftRadius=padAreaPreview.style.borderBottomRightRadius=pixels;padAreaPreview.style.display=DisplayStyle.Flex;
            }else padAreaPreview.style.display=DisplayStyle.None;
        }
    }
}
