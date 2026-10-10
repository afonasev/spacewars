using System;
using System.Collections.Generic;
using System.Linq;
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
        private PlayableBootstrap localCoordinator,localPauseOwner;
        private bool localInputFocused=true;
        private readonly List<PlayableBootstrap> localPresentations=new List<PlayableBootstrap>();
        private NativeLobbyParticipant localBinding;
        private int localRosterIndex,localViewportIndex,localHumanCount=1;
        private VisualElement panelRoot,sharedMapFrame;
        private PanelSettings ownedPanelSettings;
        private PlayableMapTerrain sharedMapTerrain;
        private PlayableMapSurface sharedLocalMap;
        private Label sharedMapClock;
        private PlayableMapSurface CompactInputMap=>(localCoordinator??this).sharedLocalMap??compactMap;
        private void CreateSharedLocalMap()
        {
            if(localPresentations.Count<2)return;
            sharedMapTerrain=new PlayableMapTerrain(profile);
            sharedMapFrame=new VisualElement{name="ordinary-shared-minimap"};sharedMapFrame.style.position=Position.Absolute;sharedMapFrame.style.left=Length.Percent(localPresentations.Count==3?75:50);StyleRegion(sharedMapFrame);OrbitalPrecision.Frame(sharedMapFrame,MinimapOutline,localPresentations.Count==2);
            sharedMapFrame.style.paddingLeft=sharedMapFrame.style.paddingRight=sharedMapFrame.style.paddingTop=sharedMapFrame.style.paddingBottom=4;
            if(localPresentations.Count==2)sharedMapFrame.style.bottom=0;
            else sharedMapFrame.style.top=Length.Percent(localPresentations.Count==3?75:50);
            panelRoot.Add(sharedMapFrame);
            sharedLocalMap=new PlayableMapSurface(profile,sharedMapTerrain.Texture){OwnerPaint=owner=>LobbyPaint(owner)};sharedLocalMap.style.flexShrink=0;sharedMapFrame.Add(sharedLocalMap);
            sharedMapClock=new Label();sharedMapClock.style.fontSize=12;sharedMapClock.style.color=Ink;sharedMapClock.style.unityTextAlign=TextAnchor.MiddleCenter;sharedMapClock.style.height=20;sharedMapClock.style.flexShrink=0;sharedMapClock.style.marginTop=sharedMapClock.style.marginBottom=sharedMapClock.style.marginLeft=sharedMapClock.style.marginRight=0;sharedMapFrame.Add(sharedMapClock);
            sharedMapLayoutCallback=_=>LayoutSharedLocalMap();panelRoot.RegisterCallback(sharedMapLayoutCallback);LayoutSharedLocalMap();
            foreach(var seat in localPresentations)seat.root.Q("tactical-minimap-frame").style.display=DisplayStyle.None;
        }
        private EventCallback<GeometryChangedEvent> sharedMapLayoutCallback;
        private void LayoutSharedLocalMap()
        {
            if(sharedLocalMap==null||sharedMapFrame==null)return;
            // Fixed packing: 8 px spare-quadrant perimeter, 4 px frame inset and 20 px clock.
            // Equal axes preserve world projection proportions at every screen size.
            float size=localPresentations.Count==3?Mathf.Max(64,Mathf.Min(panelRoot.contentRect.width*.5f-24,panelRoot.contentRect.height*.5f-44)):144;
            sharedLocalMap.style.width=sharedLocalMap.style.height=size;sharedMapFrame.style.width=size+8;sharedMapFrame.style.height=size+28;sharedMapFrame.style.marginLeft=-(size+8)/2;
            if(localPresentations.Count>2)sharedMapFrame.style.marginTop=-(size+28)/2;
        }
        private void UpdateSharedLocalMap(OfflinePresentationFrame frame)
        {
            if(sharedLocalMap==null||frame==null)return;
            sharedMapFrame.style.display=paused||ResultsVisible||inLobby?DisplayStyle.None:DisplayStyle.Flex;
            sharedMapTerrain.UpdateUnion(localPresentations.Select(seat=>seat.world.Fog).ToArray());
            var markers=OfflineMapProjection.Shared(frame,localPresentations.Select(seat=>seat.LocalOwnerId).ToArray()).Select(m=>new PlayableMapMark(m.Id,m.Position,Enum.TryParse<PlayableBuildingKind>(m.Kind,out var kind)?kind:default(PlayableBuildingKind),m.State,m.Owner)).ToArray();
            sharedLocalMap.SetPublic(markers,profile.ArenaHalfExtent);sharedLocalMap.SetOrderMarkers(Array.Empty<PlayableQueueMarker>());
            long seconds=view.Tick/30;sharedMapClock.text=(seconds/60).ToString("D2")+":"+(seconds%60).ToString("D2");
        }
        private void DisposeSharedLocalMap(){if(sharedMapLayoutCallback!=null){panelRoot.UnregisterCallback(sharedMapLayoutCallback);sharedMapLayoutCallback=null;}sharedMapFrame?.RemoveFromHierarchy();sharedMapFrame=null;sharedLocalMap=null;sharedMapTerrain?.Dispose();sharedMapTerrain=null;}
        private void ExpandLocalMenu(bool expanded)
        {
            if(!expanded){ConfigureLocalViewport(localViewportIndex,localHumanCount);return;}
            root.style.left=root.style.top=0;root.style.width=root.style.height=Length.Percent(100);root.BringToFront();
        }
        private NativeLocalControlSettings localControlSettings;
        private readonly Dictionary<int,NativeLocalControlSettings> localControlPreferences=new Dictionary<int,NativeLocalControlSettings>();
        private NativeLocalControlSettings LocalControlSettings
        {
            get{var coordinator=localCoordinator??this;if(localBinding==null)return localControlSettings??(localControlSettings=new NativeLocalControlSettings());int device=localHumanCount==1?0:localBinding.DeviceId;if(!coordinator.localControlPreferences.TryGetValue(device,out var settings)){settings=device==0?(coordinator.localControlSettings??(coordinator.localControlSettings=new NativeLocalControlSettings())):new NativeLocalControlSettings();coordinator.localControlPreferences.Add(device,settings);}return settings;}
        }
        private PlayableOwner LocalOwner=>localBinding==null?(view?.Owner??PlayableOwner.Player):(PlayableOwner)localRosterIndex;
        private string LocalOwnerId=>localBinding==null?"player-1":localRosterIndex==0?"player-1":"foundry-"+(localRosterIndex+1);
        private Vector2 SeatScreenCenter=>cameraView==null?new Vector2(Screen.width/2f,Screen.height/2f):cameraView.pixelRect.center;
        private bool SeatUsesKeyboard=>localBinding==null||localBinding.DeviceId==0||localHumanCount==1;
        private Gamepad SeatPad
        {
            get
            {
                if(localBinding==null)return Gamepad.current;
                if(localBinding.DeviceId>0)return Gamepad.all.FirstOrDefault(p=>p.deviceId==localBinding.DeviceId);
                var reserved=new HashSet<int>((matchSetup?.Participants??new List<NativeLobbyParticipant>()).Where(p=>p.Human&&p.DeviceId>0).Select(p=>p.DeviceId));
                return Gamepad.current!=null&&!reserved.Contains(Gamepad.current.deviceId)?Gamepad.current:Gamepad.all.FirstOrDefault(p=>!reserved.Contains(p.deviceId));
            }
        }
        private bool SeatReady=>localBinding==null?devicesReady:localHumanCount==1?(Keyboard.current?.added==true&&Mouse.current?.added==true||SeatPad?.added==true):localBinding.DeviceId>0?SeatPad?.added==true:Keyboard.current?.added==true&&Mouse.current?.added==true;
        private bool LocalMenuVisible=>localCoordinator==null?localPauseOwner==null||localPauseOwner==this:localCoordinator.localPauseOwner==this;
        private static Rect LocalRect(int index,int count)=>count<=1?new Rect(0,0,1,1):count==2?new Rect(index*.5f,0,.5f,1):new Rect(index%2*.5f,1-(index/2+1)*.5f,.5f,.5f);
        private void ConfigureLocalPresentations()
        {
            DisposeLocalChildren();localPresentations.Add(this);
            var humans=matchSetup?.Participants?.Select((p,i)=>new{Participant=p,Index=i}).Where(p=>p.Participant.Human).ToArray();
            if(humans==null||humans.Length==0){localBinding=null;localHumanCount=1;ResetLocalViewport();return;}
            localHumanCount=humans.Length;
            for(int index=0;index<humans.Length;index++)
            {
                var seat=this;
                if(index>0)
                {
                    var host=new GameObject("Ordinary local human "+index);host.transform.SetParent(transform,false);
                    seat=host.AddComponent<PlayableBootstrap>();seat.enabled=false;seat.localCoordinator=this;seat.localInputFocused=localInputFocused;seat.profile=profile;seat.matchSetup=matchSetup;seat.generation=generation;seat.runtime=runtime;seat.localHumanCount=humans.Length;
                    localPresentations.Add(seat);
                }
                seat.localBinding=humans[index].Participant;seat.localRosterIndex=humans[index].Index;seat.localViewportIndex=index;
                seat.view=runtime.ParticipantView(seat.LocalOwnerId);
                if(index>0){seat.CreateWorld();seat.CreateHud();seat.input=seat.gameObject.AddComponent<PlayableInput>();seat.BindLocalKeyboardInput();}
                seat.input.AssignedKeyboard=seat.SeatUsesKeyboard?Keyboard.current:null;seat.input.AssignedMouse=seat.SeatUsesKeyboard?Mouse.current:null;seat.input.enabled=seat.SeatUsesKeyboard;
                seat.inLobby=false;seat.root.Q("tactical-minimap-frame").style.display=DisplayStyle.Flex;seat.SetMatchUi(true);seat.ConfigureLocalViewport(index,humans.Length);
                var own=seat.view.Buildings.FirstOrDefault(b=>b.Owner==seat.LocalOwner&&b.Kind==PlayableBuildingKind.Headquarters);
                if(own!=null){var center=seat.Ground(seat.SeatScreenCenter);seat.cameraView.transform.position+=new Vector3((float)(own.Position.X-center.X),0,(float)(own.Position.Z-center.Z));seat.RememberKeyboardCamera();}
                seat.root.RegisterCallback<PointerDownEvent>(evt=>{if(!seat.SeatUsesKeyboard){evt.StopImmediatePropagation();seat.root.focusController?.IgnoreEvent(evt);}},TrickleDown.TrickleDown);
            }
            CreateSharedLocalMap();
        }
        private void RefreshLocalKeyboardBinding()
        {if(SeatUsesKeyboard){input.AssignedKeyboard=Keyboard.current;input.AssignedMouse=Mouse.current;}}
        private void BindLocalKeyboardInput()
        {
            input.Select=Select;input.Order=Order;input.Stop=()=>Submit(PlayableCommandKind.Stop);input.Hold=()=>Submit(PlayableCommandKind.Hold);
            input.CancelContext=()=>{if(mapOpen){CloseMap();return true;}return CloseBattleContext();};input.TogglePause=()=>Pause(!paused);input.Restart=Restart;input.FocusLost=()=>Pause(true);
            input.IsPointerOverUi=OverUi;input.IsKeyboardInUi=()=>MenuOwnsInput||root?.focusController?.focusedElement is VisualElement focused&&root.Contains(focused)&&(focused is Button||focused is TextField);
            input.MapAt=MapAt;input.MapSelect=MapSelect;input.MapOrder=MapOrder;input.ToggleMap=ToggleMap;input.CloseMap=CloseMap;input.Pan=Pan;input.Zoom=Zoom;input.Drag=DrawDrag;BindKeyboardCommands();
            input.AttackModeChanged=active=>{commandCursor?.Set(false);notice="";};
        }
        private void ConfigureLocalViewport(int index,int count)
        {
            var rect=LocalRect(index,count);cameraView.rect=rect;cameraView.cullingMask=1<<(8+index);world.SetPresentationLayer(8+index);
            root.style.left=Length.Percent(rect.x*100);root.style.top=Length.Percent((1-rect.y-rect.height)*100);root.style.right=root.style.bottom=StyleKeyword.Auto;
            root.style.width=Length.Percent(rect.width*100);root.style.height=Length.Percent(rect.height*100);root.style.overflow=Overflow.Hidden;
        }
        private void ResetLocalViewport()
        {
            localBinding=null;localRosterIndex=localViewportIndex=0;localHumanCount=1;localPauseOwner=null;
            if(root!=null){root.style.left=root.style.right=root.style.top=root.style.bottom=0;root.style.width=root.style.height=StyleKeyword.Auto;root.style.overflow=Overflow.Visible;}
            if(cameraView!=null){cameraView.rect=new Rect(0,0,1,1);cameraView.cullingMask=~0;world?.SetPresentationLayer(0);}
            if(input!=null)input.enabled=true;
        }
        private void DisposeLocalChildren()
        {
            foreach(var seat in localPresentations.Where(s=>s!=this).ToArray()){seat.input.WorldInputEnabled=false;seat.runtime=null;Destroy(seat.gameObject);}
            localPresentations.Clear();DisposeSharedLocalMap();
        }
        private void PresentLocalChildren(OfflinePresentationFrame frame,PlayableCommandReceipt[] receipts)
        {
            if(frame==null)return;
            foreach(var seat in localPresentations.Where(s=>s!=this))
            {
                seat.RefreshLocalKeyboardBinding();seat.view=frame.Views[seat.LocalOwnerId];seat.paused=paused;seat.restarting=restarting;seat.quitting=quitting;
                seat.input.WorldInputEnabled=seat.SeatReady&&!paused&&!preparing&&!returningToLobby&&!restarting&&!quitting&&seat.view.Outcome==PlayableMatchOutcome.Playing&&!ResultsVisible;
                seat.RebindPresentation();seat.Render();seat.UpdateHud();seat.UpdateMaps();seat.UpdateLifecycleMarkers();seat.PollBattleController();seat.UpdateKeyboardPresentation();seat.UpdateOrderMarkers(receipts.Where(r=>r.OwnerId==seat.LocalOwnerId).ToArray());
            }
            UpdateSharedLocalMap(frame);
        }
        private void PauseFrom(PlayableBootstrap seat,bool value){localPauseOwner=value?seat:null;Pause(value);}
        private void BroadcastPause(bool value)
        {
            if(value&&localPauseOwner==null)localPauseOwner=this;
            foreach(var seat in localPresentations){seat.root.style.display=value&&seat!=localPauseOwner?DisplayStyle.None:DisplayStyle.Flex;seat.ExpandLocalMenu(value&&seat==localPauseOwner);seat.paused=value;seat.input?.ClearMode();seat.battleGestures.Cancel();seat.CloseMap();if(!value){seat.CloseChildMenu();seat.menuNavigation?.SetScope(null);}}
            if(!value)localPauseOwner=null;
        }
    }
}
