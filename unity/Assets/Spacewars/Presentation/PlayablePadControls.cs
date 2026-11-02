using System;
using System.Linq;
using Spacewars.Input;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

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
        private int[] padGroupSlots;
        private int padOriginalHq,padRememberedBuilding;
        private NavPoint padCursorGround;
        private bool padCursorInitialized;
        private double padRightStickLastActive=double.NegativeInfinity;
        private bool systemCursorHidden;
        private bool PadControlsCursor=>battleController&&SeatPad?.added==true&&input?.WorldInputEnabled==true;
        private void SetSystemCursorHidden(bool hidden)
        {
            if(localCoordinator!=null)return;
            systemCursorHidden=hidden;Cursor.visible=!hidden;
        }
        private void SyncSystemCursor()
        {
            if(localCoordinator!=null)return;
            // One coordinator policy serves match, split seats, menus and focus restoration.
            SetSystemCursorHidden(localInputFocused&&!MouseCursorVisible);
        }
        private void OnDisable()=>SetSystemCursorHidden(false);
        private bool WorldPadOwnsToolkit=>orbitalOpen||orbitalReleaseGate||selectionRosterPadFocus||selectionRosterReleaseGate||!orbitalOpen&&!signalWheelOpen&&input?.WorldInputEnabled==true&&SeatPad!=null&&(SeatPad.leftStick.ReadValue().magnitude>NativeDevicePreferences.Deadzone||SeatPad.dpad.ReadValue().sqrMagnitude>0||SeatPad.buttonSouth.isPressed||SeatPad.buttonSouth.wasReleasedThisFrame||SeatPad.buttonEast.isPressed||SeatPad.buttonEast.wasReleasedThisFrame||SeatPad.buttonNorth.isPressed||SeatPad.buttonNorth.wasReleasedThisFrame);
        private bool PadAuxiliaryWheel=>battleGestures.Mode=="groupWheel"||battleGestures.Mode=="groupAssign"||battleGestures.Mode=="baseWheel";
        private void EnsureOrdinaryPadUi()
        {
            if(battleCursorVisual!=null)return;
            padWhiteArrow=AngularCommandCursor.Create(false);padRedArrow=AngularCommandCursor.Create(true);
            battleCursorVisual=new VisualElement{name="gamepad-angular-cursor",pickingMode=PickingMode.Ignore};battleCursorVisual.style.position=Position.Absolute;battleCursorVisual.style.width=battleCursorVisual.style.height=AngularCommandCursor.Size;root.Add(battleCursorVisual);var queuePreview=new Label("+"){name="pad-queue-preview",pickingMode=PickingMode.Ignore};queuePreview.style.position=Position.Absolute;queuePreview.style.left=AngularCommandCursor.Size-4;queuePreview.style.top=-8;queuePreview.style.fontSize=20;queuePreview.style.color=OrbitalTheme.Cyan;queuePreview.style.display=DisplayStyle.None;battleCursorVisual.Add(queuePreview);
            padAreaPreview=new VisualElement{name="gamepad-selection-preview",pickingMode=PickingMode.Ignore};padAreaPreview.style.position=Position.Absolute;padAreaPreview.style.borderTopWidth=padAreaPreview.style.borderBottomWidth=padAreaPreview.style.borderLeftWidth=padAreaPreview.style.borderRightWidth=1;padAreaPreview.style.borderTopColor=padAreaPreview.style.borderBottomColor=padAreaPreview.style.borderLeftColor=padAreaPreview.style.borderRightColor=Color.white;padAreaPreview.style.backgroundColor=new Color(1,1,1,.045f);padAreaPreview.style.display=DisplayStyle.None;root.Add(padAreaPreview);
            padAuxiliaryRing=new OrbitalBattleRing{name="gamepad-group-base-ring"};root.Add(padAuxiliaryRing);
        }
        private Rect? AuxiliaryMapObstacle()
        {
            var map=(localCoordinator??this).sharedMapFrame;
            if(map==null||map.resolvedStyle.display==DisplayStyle.None||map.worldBound.width<=0)return null;
            var bounds=map.worldBound;bounds.position-=root.worldBound.position;return bounds;
        }
        private void HideOrdinaryPadUi()
        {if(battleCursorVisual!=null)battleCursorVisual.style.display=DisplayStyle.None;if(padAreaPreview!=null)padAreaPreview.style.display=DisplayStyle.None;if(padAuxiliaryRing!=null)padAuxiliaryRing.style.display=DisplayStyle.None;world?.RenderRallyPreview(null,view?.Owner??PlayableOwner.Player);}
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
        private Vector3 PadPointerPlane(Vector2 screen)
        {
            // A fixed projection plane supplies camera displacement, never terrain hits.
            var ray=cameraView.ScreenPointToRay(screen);new Plane(Vector3.up,Vector3.zero).Raycast(ray,out float distance);return ray.GetPoint(distance);
        }
        private void FocusPadArmy()
        {
            var units=OrdinaryPadUnits().Where(u=>u.Own).Select(u=>{
                var e=view.Entities.Single(e=>e.Id==u.Id);var p=cameraView.WorldToScreenPoint(world.Point(e.Position));
                return new OfflinePadSelectable(u.Id,u.Kind,true,p.x,p.y,0,p.z>0);
            }).Where(u=>u.Visible).ToArray();
            var focus=OfflinePadGroups.FocusViewport(units,cameraView.pixelRect);
            if(!focus.HasValue)return;
            cameraView.transform.position+=PadPointerPlane(new Vector2((float)focus.Value.X,(float)focus.Value.Z))-PadPointerPlane(SeatScreenCenter);
            battleCursor=SeatScreenCenter;padCursorGround=Ground(battleCursor);
        }
        private OfflinePadAction[] OrdinaryPadActions(out int pages)
        {
            pages=1;
            if(battleGestures.Mode=="groupWheel"||battleGestures.Mode=="groupAssign")
            {
                // Freeze IDs as well as count: a held direction must keep its group after an add.
                if(padGroupSlots==null)padGroupSlots=AuxiliaryWheelPresentation.GroupSlots(keyboardGroups,KeyboardEntities());
                bool assign=battleGestures.Mode=="groupAssign";
                return AuxiliaryWheelPresentation.Groups(keyboardGroups,selection,KeyboardEntities(),view,assign,padGroupSlots,battleGestures.Added?battleGestures.AddedSectorId:null);
            }
            padGroupSlots=null;
            if(battleGestures.Mode=="baseWheel")
            {
                return AuxiliaryWheelPresentation.Bases(view,padOriginalHq);
            }
            return battleGestures.Mode=="buildingWheel"?CurrentBattleActions():Array.Empty<OfflinePadAction>();
        }
        private void SetPadSelection(int[] ids,bool keepGroup=false)
        {selection.Clear();foreach(int id in ids)selection.Add(id);selectedSite=selectedSlot=primarySelection=0;battleDismissed=false;confirmSaleBuilding=0;if(!keepGroup)keyboardGroups.Observe(selection);SelectionMarkerEvent();if(selection.Count>0)CompleteCampaignTutorialTopic(0);}
        private bool OrdinaryPadMenuIntent(OfflinePadIntent intent,OfflinePadAction[] actions)
        {
            if(intent.Kind=="assignGroup"||intent.Kind=="addGroup")
            {
                if(actions.Any(a=>a.Sector.Id==intent.Id&&(intent.Kind=="addGroup"?a.Sector.CanAdd:a.Sector.CanAssign))&&int.TryParse(intent.Id,out int index))
                {if(intent.Kind=="addGroup")keyboardGroups.Add(index,selection,KeyboardEntities());else{var ids=keyboardGroups.Apply(index,true,Time.unscaledTimeAsDouble,selection,KeyboardEntities(),battleInputProfile.doubleTapMs,out _);SetPadSelection(ids,true);}CompleteCampaignTutorialTopic(5);runtime.RecordHumanAction(LocalOwnerId);}return true;
            }
            if(intent.Kind=="cycleGroup"||intent.Kind=="recallGroup")
            {
                var entities=KeyboardEntities();var ids=intent.Kind=="cycleGroup"?keyboardGroups.Cycle(selection,entities):int.TryParse(intent.Id,out int index)?keyboardGroups.RecallPad(index,entities):null;
                if(ids!=null)
                {
                    SetPadSelection(ids,true);CompleteCampaignTutorialTopic(2);runtime.RecordHumanAction(LocalOwnerId);
                    if(intent.Kind=="cycleGroup"){var point=KeyboardControlGroups.Focus(ids,entities,padCursorGround.X,padCursorGround.Z,battleInputProfile.controlGroupFocusRadius);if(point.HasValue)FocusPadPoint(new NavPoint(point.Value.X,point.Value.Z));}
                    var building=view.Buildings.FirstOrDefault(b=>ids.Contains(b.Id)&&b.Owner==view.Owner);if(building!=null&&!mapOpen){padRememberedBuilding=building.Id;battleGestures.SetMode("buildingWheel");}
                }return true;
            }
            if(intent.Kind=="selectAllArmy"){padRememberedBuilding=0;SetPadSelection(view.Entities.Where(e=>e.Owner==view.Owner&&e.Health>0).Select(e=>e.Id).ToArray());FocusPadArmy();CompleteCampaignTutorialTopic(6);runtime.RecordHumanAction(LocalOwnerId);return true;}
            if(intent.Kind=="cycleBase"||intent.Kind=="selectBase")
            {
                var point=intent.Kind=="cycleBase"?ordinaryPadBases.CycleBase(OrdinaryBases(),padOriginalHq):actions.FirstOrDefault(a=>a.Sector.Id==intent.Id&&a.Sector.Enabled)?.Point is NavPoint p?new OfflinePadBase(0,p.X,p.Z):(OfflinePadBase?)null;
                if(point.HasValue)FocusPadPoint(new NavPoint(point.Value.X,point.Value.Z));return true;
            }
            return false;
        }
        private void PollOrdinaryPad()
        {
            if(BotCameraLocked){HideOrdinaryPadUi();return;}
            var pad=SeatPad;
            if(localBinding!=null)battleController=!SeatUsesKeyboard&&pad?.added==true;
            if(SeatUsesKeyboard&&input?.AssignedMouse?.delta.ReadValue().sqrMagnitude>0)battleController=false;
            if(pad!=previousSeatPad){padRightStickLastActive=double.NegativeInfinity;bool lost=previousSeatPad!=null&&!previousSeatPad.added;battleGestures.Cancel();previousSeatPad=pad;if(lost&&!paused){(localCoordinator??this).PauseFrom(this,true);notice="Геймпад отключён. Подключите устройство и продолжите матч.";}}
            if(pad==null||!pad.added||chatOpen||paused||!localInputFocused||input?.WorldInputEnabled!=true){CancelTeamSignal();battleGestures.Cancel();EndSelectionRosterFocus();HideOrdinaryPadUi();return;}
            EnsureOrdinaryPadUi();if(PollOrbitalPad(pad))return;if(PollSelectionRosterPad(pad))return;if(PollTeamSignalPad(pad))return;if(!padCursorInitialized){padCursorGround=Ground(SeatScreenCenter);battleCursor=SeatScreenCenter;padCursorInitialized=true;padOriginalHq=view.Buildings.FirstOrDefault(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Headquarters)?.Id??0;}
            if(padRememberedBuilding!=0&&!view.Buildings.Any(b=>b.Id==padRememberedBuilding&&b.Owner==view.Owner)){padRememberedBuilding=0;battleGestures.Cancel();}
            int mask=(pad.buttonSouth.isPressed?1:0)|(pad.buttonEast.isPressed?2:0)|(pad.buttonWest.isPressed?4:0)|(pad.selectButton.isPressed?8:0)|(pad.startButton.isPressed?16:0)|(pad.rightShoulder.isPressed?32:0)|(pad.leftShoulder.isPressed?64:0)|(pad.buttonNorth.isPressed?256:0);
            var ls=pad.leftStick.ReadValue();var rs=pad.rightStick.ReadValue();
            bool rightStickActive=Mathf.Abs(rs.x)>NativeDevicePreferences.Deadzone||Mathf.Abs(rs.y)>NativeDevicePreferences.Deadzone;
            if(rightStickActive)padRightStickLastActive=Time.unscaledTimeAsDouble;
            bool activity=ls.magnitude>NativeDevicePreferences.Deadzone||rs.magnitude>NativeDevicePreferences.Deadzone||pad.dpad.ReadValue().sqrMagnitude>0||pad.leftStickButton.isPressed||mask!=0;
            var mouse=input.AssignedMouse??Mouse.current;
            bool mouseActivity=mouse?.added==true&&(mouse.delta.ReadValue().sqrMagnitude>0||mouse.scroll.ReadValue().sqrMagnitude>0||mouse.leftButton.wasPressedThisFrame||mouse.rightButton.wasPressedThisFrame||mouse.middleButton.wasPressedThisFrame);
            if(activity)battleController=true;else if(SeatUsesKeyboard&&mouseActivity)battleController=false;
            if(!scanTargeting&&battleGestures.Blocked&&(ls.magnitude>NativeDevicePreferences.Deadzone||rs.magnitude>NativeDevicePreferences.Deadzone)){HideOrdinaryPadUi();return;}
            if(!PadAuxiliaryWheel&&!scanTargeting)
            {
                string mode=battleRally?(mapOpen?"rallyMap":"rallyTarget"):mapOpen?"tacticalMap":battleRing.style.display.value==DisplayStyle.Flex?"buildingWheel":"world";
                if(battleGestures.Mode!=mode)battleGestures.SetMode(mode);
            }
            float Axis(float value)=>Mathf.Abs(value)<=NativeDevicePreferences.Deadzone?0:Mathf.Sign(value)*(Mathf.Abs(value)-NativeDevicePreferences.Deadzone)/(1-NativeDevicePreferences.Deadzone);
            if((scanTargeting||!battleGestures.Blocked)&&!PadAuxiliaryWheel&&battleGestures.Mode!="buildingWheel"&&!pad.rightShoulder.isPressed)
            {
                float dt=Time.unscaledDeltaTime;var right=Vector3.ProjectOnPlane(cameraView.transform.right,Vector3.up).normalized;var forward=Vector3.ProjectOnPlane(cameraView.transform.forward,Vector3.up).normalized;
                if(mapOpen){
                    float speed=LocalControlSettings.MapCursorSpeed*2*(float)profile.ArenaHalfExtent*dt;
                    padCursorGround=new NavPoint(Math.Clamp(padCursorGround.X+Axis(ls.x)*speed,-profile.ArenaHalfExtent,profile.ArenaHalfExtent),Math.Clamp(padCursorGround.Z+Axis(ls.y)*speed,-profile.ArenaHalfExtent,profile.ArenaHalfExtent));
                }else{
                    float pixels=cameraView.pixelRect.height/(2*cameraView.orthographicSize);
                    battleCursor+=new Vector2(Axis(rs.x),Axis(rs.y))*LocalControlSettings.CursorSpeed*pixels*dt;
                }
                if(!mapOpen)
                {
                    var pan=(right*Axis(ls.x)*(NativeDevicePreferences.InvertX?-1:1)+forward*Axis(ls.y)*(NativeDevicePreferences.InvertY?-1:1))*LocalControlSettings.CameraSpeed*dt;cameraView.transform.position+=pan;if(pan.sqrMagnitude>0&&!rightStickActive&&Time.unscaledTimeAsDouble-padRightStickLastActive>=LocalControlSettings.CursorRecenterIdleMs/1000.0)battleCursor=SeatScreenCenter;
                    cameraView.orthographicSize=Mathf.Clamp(cameraView.orthographicSize+((pad.dpad.down.isPressed?1:0)-(pad.dpad.up.isPressed?1:0))*battleInputProfile.zoomSpeed*dt,(float)profile.CameraMinZoom,(float)profile.CameraMaxZoom);
                    if(pad.leftStickButton.wasPressedThisFrame){ResetKeyboardCamera();battleCursor=SeatScreenCenter;}
                }
            }
            if(!scanTargeting&&!mapOpen&&!battleGestures.Blocked&&!PadAuxiliaryWheel&&battleGestures.Mode=="world"&&!pad.leftShoulder.isPressed&&!pad.rightShoulder.isPressed){
                if(pad.dpad.right.wasPressedThisFrame)FocusMatchNotification(false);
                else if(pad.dpad.left.wasPressedThisFrame)FocusMatchNotification(true);
            }
            var rect=cameraView.pixelRect;
            // Every seat may use the whole shared mini-map, but cannot point at
            // another player's world outside that explicitly shared UI surface.
            if(mapOpen||MapAt(battleCursor)!=1)battleCursor=new Vector2(Mathf.Clamp(battleCursor.x,rect.xMin,rect.xMax),Mathf.Clamp(battleCursor.y,rect.yMin,rect.yMax));
            if(!mapOpen)padCursorGround=MapAt(battleCursor)==1?MapGround(1,battleCursor):Ground(battleCursor);
            if(PollScanTargetPad(pad))return;
            var actions=OrdinaryPadActions(out int pages);int? sector=OfflinePadGestures.RingSector(ls.x,-ls.y,actions.Length,battleInputProfile.radialDeadzone);battleSector=sector.HasValue?actions[sector.Value].Sector.Id:null;
            foreach(var intent in battleGestures.StepDetailed(Time.unscaledTimeAsDouble*1000,mask,true,true,battleInputProfile,mapOpen||OrdinaryPadHit()==0,sector.HasValue?actions[sector.Value].Sector:null))
            {
                if(intent.Kind=="pause"){if(localCoordinator!=null)localCoordinator.PauseFrom(this,true);else PauseFrom(this,true);break;}
                if(OrdinaryPadMenuIntent(intent,actions))continue;
                if(intent.Kind=="activate"||intent.Kind=="repeat"||intent.Kind=="sell"){ExecuteBattleAction(intent.Id,intent.Kind!="activate",true);if(battleRally)battleGestures.SetMode("rallyTarget");continue;}
                if(intent.Kind=="cancelRally"){CancelRallyPlacement(true);continue;}
                if(intent.Kind=="deselect"){padRememberedBuilding=0;CloseBattleContext();continue;}
                if(intent.Kind=="select")
                {
                    int hit=OrdinaryPadHit();if(view.Buildings.Any(b=>b.Id==hit&&b.Owner==view.Owner)||PickPad(padCursorGround)){Select(battleCursor,battleCursor,false);padRememberedBuilding=view.Buildings.FirstOrDefault(b=>selection.Contains(b.Id)&&b.Owner==view.Owner)?.Id??0;}
                    else SetPadSelection(ordinaryPadSelection.Tap(Time.unscaledTimeAsDouble*1000,hit,OrdinaryPadUnits(),battleInputProfile.doubleTapMs));runtime.RecordHumanAction(LocalOwnerId);
                }
                else if(intent.Kind=="selectCircle"||intent.Kind=="selectScreen"||intent.Kind=="selectMapCircle"){double radius=intent.Kind=="selectScreen"?double.PositiveInfinity:(intent.HeldMs-battleInputProfile.holdMs)/1000*battleInputProfile.selectionGrowth;SetPadSelection(OfflinePadSelection.Area(OrdinaryPadUnits(),padCursorGround.X,padCursorGround.Z,radius,intent.Kind=="selectMapCircle"));runtime.RecordHumanAction(LocalOwnerId);}
                else if(intent.Kind=="cameraJump")FocusPadPoint(padCursorGround);
                else if(intent.Kind=="context"&&mapOpen&&padRememberedBuilding!=0){SetPadSelection(new[]{padRememberedBuilding});battleGestures.SetMode("buildingWheel");}
                else if(intent.Kind=="context"||intent.Kind=="attackMove"){int map=MapAt(battleCursor);if(!mapOpen&&map==1)MapOrder(map,battleCursor,intent.Kind=="attackMove",intent.Append);else if(mapOpen||intent.Kind=="attackMove")Submit(intent.Kind=="attackMove"?PlayableCommandKind.AttackMove:PlayableCommandKind.Move,null,padCursorGround,mode:intent.Append?PlayableOrderMode.Append:PlayableOrderMode.Replace);else Order(battleCursor,false,intent.Append);}
                else if(intent.Kind=="hold"||intent.Kind=="stop")Submit(intent.Kind=="hold"?PlayableCommandKind.Hold:PlayableCommandKind.Stop);
                else if(intent.Kind=="rally")CompleteRallyPlacement(padCursorGround,true);
            }
            if(!PadAuxiliaryWheel){mapOpen=battleGestures.Map;tacticalOverlay.style.display=mapOpen?DisplayStyle.Flex:DisplayStyle.None;SyncTacticalMapHud();}
            actions=OrdinaryPadActions(out pages);sector=OfflinePadGestures.RingSector(ls.x,-ls.y,actions.Length,battleInputProfile.radialDeadzone);
            if(PadAuxiliaryWheel)
            {
                float size=Mathf.Min(280,Mathf.Min(root.contentRect.width-24,root.contentRect.height-100));padAuxiliaryRing.Resize(size);padAuxiliaryRing.style.left=(root.contentRect.width-size)/2;padAuxiliaryRing.style.top=Mathf.Max(50,(root.contentRect.height-size)/2);padAuxiliaryRing.ConfigureInput(true,SeatPad,battleGestures.Mode);padAuxiliaryRing.Set(actions,battleGestures.Mode=="baseWheel"?"Базы":battleGestures.Mode=="groupAssign"?"Запись":"Группы",battleGestures.Added?battleGestures.AddedSectorId:sector.HasValue?actions[sector.Value].Sector.Id:null,battleGestures.Progress,battleGestures.Mode=="baseWheel"?"headquarters":"army");padAuxiliaryRing.FitAuxiliary(root.contentRect.width,root.contentRect.height,AuxiliaryMapObstacle());padAuxiliaryRing.style.display=DisplayStyle.Flex;padAuxiliaryRing.BringToFront();battleRing.style.display=DisplayStyle.None;
            }else padAuxiliaryRing.style.display=DisplayStyle.None;
            Vector2 point;
            if(mapOpen){var uv=new PlayableMapTransform(profile.ArenaHalfExtent,profile.ArenaHalfExtent).Project(padCursorGround);point=tacticalMap.worldBound.position+new Vector2((float)uv.X*tacticalMap.worldBound.width,(float)uv.Z*tacticalMap.worldBound.height)-root.worldBound.position;}
            else point=PanelPoint(battleCursor);
            var cursorParent=!mapOpen&&MapAt(battleCursor)==1&&(localCoordinator??this).sharedLocalMap!=null?(localCoordinator??this).sharedMapFrame:root;
            if(battleCursorVisual.parent!=cursorParent)cursorParent.Add(battleCursorVisual);
            if(cursorParent!=root)point=GlobalPanelPoint(battleCursor)-cursorParent.worldBound.position;
            UpdateRallyPreview();
            battleCursorVisual.Q<Label>("pad-queue-preview").style.display=battleGestures.QueuePreview?DisplayStyle.Flex:DisplayStyle.None;
            battleCursorVisual.style.backgroundImage=battleRally?Resources.Load<Texture2D>("OrbitalIcons/rally"):battleGestures.AttackPreview||!mapOpen&&MapAt(battleCursor)==0&&(HostileAt(battleCursor)||IsWreckAt(battleCursor))?padRedArrow:padWhiteArrow;
            battleCursorVisual.style.unityBackgroundImageTintColor=battleRally?LobbyPaint(view.Owner):Color.white;var hotspot=battleRally?new Vector2(AngularCommandCursor.Size/2,AngularCommandCursor.Size):AngularCommandCursor.Hotspot;battleCursorVisual.style.left=point.x-hotspot.x;battleCursorVisual.style.top=point.y-hotspot.y;battleCursorVisual.style.display=battleController&&!PadAuxiliaryWheel&&battleGestures.Mode!="buildingWheel"&&(!battleRally||mapOpen)?DisplayStyle.Flex:DisplayStyle.None;battleCursorVisual.BringToFront();
            if(battleGestures.SelectionHeldMs>0&&!PadAuxiliaryWheel)
            {
                float radius=(float)((battleGestures.SelectionHeldMs-battleInputProfile.holdMs)/1000*battleInputProfile.selectionGrowth);float pixels=mapOpen?radius*tacticalMap.worldBound.width/(2*(float)profile.ArenaHalfExtent):radius*cameraView.pixelRect.height/(2*cameraView.orthographicSize)/(localCoordinator??this).GetComponent<UIDocument>().panelSettings.scale;
                bool screen=battleGestures.ScreenPreview;
                padAreaPreview.style.left=screen?0:point.x-pixels;padAreaPreview.style.top=screen?0:point.y-pixels;
                padAreaPreview.style.width=screen?root.contentRect.width:pixels*2;padAreaPreview.style.height=screen?root.contentRect.height:pixels*2;
                padAreaPreview.style.borderTopLeftRadius=padAreaPreview.style.borderTopRightRadius=padAreaPreview.style.borderBottomLeftRadius=padAreaPreview.style.borderBottomRightRadius=screen?0:pixels;padAreaPreview.style.display=DisplayStyle.Flex;
            }else padAreaPreview.style.display=DisplayStyle.None;
        }
    }
}
