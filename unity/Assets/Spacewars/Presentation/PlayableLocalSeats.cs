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
        private VisualElement panelRoot,sharedMapFrame,sharedMapBackdrop;
        private SplitScreenDividers splitDividers;
        private PanelSettings ownedPanelSettings;
        private PlayableMapTerrain sharedMapTerrain;
        private PlayableMapSurface sharedLocalMap;
        private Label sharedMapClock;
        private OfflinePresentationFrame sharedMapProjection;
        private bool sharedMapHasOpposingTeams;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private static readonly bool disableSharedMapReuse=Environment.GetEnvironmentVariable("SPACEWARS_QA_DISABLE_SHARED_MAP_REUSE")=="1";
#endif
        private PlayableMapSurface CompactInputMap=>(localCoordinator??this).sharedLocalMap??compactMap;
        private int FreeScreenSlot=>Enumerable.Range(0,4).FirstOrDefault(slot=>localPresentations.All(seat=>seat.localViewportIndex!=slot));
        private void CreateSharedLocalMap()
        {
            if(localPresentations.Count<2)return;
            panelRoot.AddToClassList("local-split-screen");OrbitalTheme.LayoutDeviceDiagnostics(panelRoot);
            if(localPresentations.Count==3){
                // No camera clears the unused quadrant. Cover its stale framebuffer before drawing the map.
                sharedMapBackdrop=new VisualElement{name="shared-map-backdrop",pickingMode=PickingMode.Ignore};sharedMapBackdrop.style.position=Position.Absolute;
                sharedMapBackdrop.style.left=Length.Percent(FreeScreenSlot%2*50);sharedMapBackdrop.style.top=Length.Percent(FreeScreenSlot/2*50);sharedMapBackdrop.style.width=sharedMapBackdrop.style.height=Length.Percent(50);
                sharedMapBackdrop.style.backgroundColor=new Color(.035f,.05f,.055f,1);panelRoot.Add(sharedMapBackdrop);
            }
            splitDividers=new SplitScreenDividers();panelRoot.Add(splitDividers);
            sharedMapTerrain=new PlayableMapTerrain(profile, overheadTerrain: tacticalTerrain?.CompactTerrain);
            sharedMapFrame=new VisualElement{name="ordinary-shared-minimap"};sharedMapFrame.style.position=Position.Absolute;sharedMapFrame.style.left=Length.Percent(localPresentations.Count==3?(FreeScreenSlot%2==0?25:75):50);StyleRegion(sharedMapFrame);OrbitalPrecision.Frame(sharedMapFrame,MinimapOutline,localPresentations.Count==2);
            sharedMapFrame.style.paddingLeft=sharedMapFrame.style.paddingRight=sharedMapFrame.style.paddingTop=sharedMapFrame.style.paddingBottom=4;
            if(localPresentations.Count==2)sharedMapFrame.style.bottom=0;
            else sharedMapFrame.style.top=Length.Percent(localPresentations.Count==3?(FreeScreenSlot/2==0?25:75):50);
            panelRoot.Add(sharedMapFrame);sharedMapFrame.RegisterCallback<GeometryChangedEvent>(_=>UpdateLocalDividers());
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
            UpdateLocalDividers();
        }
        private void UpdateSharedLocalMap(OfflinePresentationFrame frame)
        {
            if(sharedLocalMap==null||frame==null)return;
            using var measure=PerformanceProfile.Measure(ProfileStage.SharedLocalMap);
            sharedMapFrame.style.display=paused||ResultsVisible||inLobby||localPresentations.Any(seat=>seat.mapOpen)?DisplayStyle.None:DisplayStyle.Flex;
            bool terrainChanged;
            using(PerformanceProfile.Measure(ProfileStage.SharedMapTerrain))
                terrainChanged=sharedMapTerrain.UpdateUnion(localPresentations.Where(seat=>spectatorMode||!seat.BotSeat).Select(seat=>seat.world.Fog).ToArray());
            // Participant projections are immutable. Spectator perspectives and
            // interactive previews have their own inputs and can change within a tick.
            bool rebuild=spectatorMode||!ReferenceEquals(sharedMapProjection,frame);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            rebuild|=disableSharedMapReuse;
#endif
            if(rebuild)
            {
                using var projection=PerformanceProfile.Measure(ProfileStage.SharedMapProjection);
                var markers=OfflineMapProjection.Shared(frame,localPresentations.Where(seat=>spectatorMode||!seat.BotSeat).Select(seat=>seat.LocalOwnerId).ToArray()).Select(m=>new PlayableMapMark(m.Id,m.Position,Enum.TryParse<PlayableBuildingKind>(m.Kind,out var kind)?kind:default(PlayableBuildingKind),m.State,m.Owner)).ToArray();
                sharedLocalMap.SetPublic(markers,profile.ArenaHalfExtent);
                var scanSeats=localPresentations.Where(seat=>(spectatorMode||!seat.BotSeat)&&seat.view!=null).ToArray();
                sharedLocalMap.SetScanLayers(scanSeats.Select(seat=>seat.view.ScanTriangles).ToArray(),scanSeats.Select(seat=>seat.view.ScanLines).ToArray());
                sharedMapHasOpposingTeams=scanSeats.Select(seat=>seat.view.Team).Distinct().Skip(1).Any();
                var nuclearSignals=!spectatorMode&&scanSeats.Length>0&&!sharedMapHasOpposingTeams
                    ?scanSeats.SelectMany(seat=>seat.view.Orbital.NuclearStrikes).Where(strike=>strike.AlliedSignal)
                        .GroupBy(strike=>strike.Id).Select(group=>group.First()).ToArray()
                    :Array.Empty<OrbitalNuclearSnapshot>();
                sharedLocalMap.SetNuclearSignals(nuclearSignals);
                sharedLocalMap.SetOrderMarkers(Array.Empty<PlayableQueueMarker>());
                long seconds=view.Tick/30;
                sharedMapClock.text=(seconds/60).ToString("D2")+":"+(seconds%60).ToString("D2");
                sharedMapProjection=spectatorMode?null:frame;
            }
            else if(terrainChanged)sharedLocalMap.MarkDirtyRepaint();
            var scanning=localPresentations.FirstOrDefault(seat=>seat.scanTargeting&&seat.localInputFocused);
            if(scanning==null||scanning.NuclearTargeting&&sharedMapHasOpposingTeams)sharedLocalMap.SetScanPreview(null,0);
            else scanning.SetOrbitalMapPreview(sharedLocalMap,scanning.ScanPreviewPoint());
            UpdateLocalDividers();
        }
        private void UpdateLocalDividers()
        {
            if(splitDividers==null)return;
            bool visible=!paused&&!ResultsVisible&&!inLobby&&!preparing&&localPresentations.Count>=2;
            if(sharedMapBackdrop!=null)sharedMapBackdrop.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;
            splitDividers.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;
            Rect? map=null;
            if(sharedMapFrame!=null&&sharedMapFrame.style.display.value!=DisplayStyle.None&&sharedMapFrame.worldBound.width>0)
            {var r=sharedMapFrame.worldBound;r.position=splitDividers.WorldToLocal(r.position);map=r;}
            splitDividers.Set(localPresentations.Count,map);
            if(visible)OrbitalTheme.LayoutDeviceDiagnostics(panelRoot,splitDividers,map.HasValue?sharedMapFrame:null);
        }
        private void DisposeSharedLocalMap(){panelRoot?.RemoveFromClassList("local-split-screen");OrbitalTheme.LayoutDeviceDiagnostics(panelRoot);splitDividers?.RemoveFromHierarchy();splitDividers=null;if(sharedMapLayoutCallback!=null){panelRoot.UnregisterCallback(sharedMapLayoutCallback);sharedMapLayoutCallback=null;}sharedMapBackdrop?.RemoveFromHierarchy();sharedMapBackdrop=null;sharedMapFrame?.RemoveFromHierarchy();sharedMapFrame=null;sharedLocalMap=null;sharedMapProjection=null;sharedMapTerrain?.Dispose();sharedMapTerrain=null;}
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
        private PlayableOwner LocalOwner=>spectatorMode&&spectatorPerspective.HasValue?spectatorPerspective.Value:localBinding==null?(view?.Owner??PlayableOwner.Player):(PlayableOwner)localRosterIndex;
        private string LocalOwnerId=>spectatorMode&&spectatorPerspective.HasValue?((int)spectatorPerspective.Value==0?"player-1":"foundry-"+((int)spectatorPerspective.Value+1)):localBinding==null?"player-1":localRosterIndex==0?"player-1":"foundry-"+(localRosterIndex+1);
        private Vector2 SeatScreenCenter=>cameraView==null?new Vector2(Screen.width/2f,Screen.height/2f):cameraView.pixelRect.center;
        private bool SeatUsesKeyboard=>localCoordinator==null&&spectatorMode||localBinding==null||localBinding.Human&&localBinding.DeviceId==0;
        private Gamepad SeatPad
        {
            get
            {
                if(localBinding==null)return Gamepad.current;
                if(!localBinding.Human)return null;
                if(localBinding.DeviceId>0)return Gamepad.all.FirstOrDefault(p=>p.deviceId==localBinding.DeviceId);
                return null;
            }
        }
        private bool SeatReady=>BotSeat|| (localBinding==null?devicesReady:localBinding.DeviceId>0?SeatPad?.added==true:Keyboard.current?.added==true&&Mouse.current?.added==true);
        private bool LocalMenuVisible=>localCoordinator==null?localPauseOwner==null||localPauseOwner==this:localCoordinator.localPauseOwner==this;
        private static Rect LocalRect(int index,int count)=>count<=1?new Rect(0,0,1,1):count==2?new Rect(index*.5f,0,.5f,1):new Rect(index%2*.5f,1-(index/2+1)*.5f,.5f,.5f);
        private void ConfigureLocalPresentations()=>LoadingPreparation.Drain(PrepareLocalPresentations());
        private System.Collections.IEnumerator PrepareLocalPresentations()
        {
            DisposeLocalChildren();localPresentations.Add(this);
            if(matchSetup!=null)NativeScreenLayout.Normalize(matchSetup);
            var humans=matchSetup?.Participants?.Select((p,i)=>new{Participant=p,Index=i}).Where(p=>p.Participant.Human||p.Participant.ScreenSlot>=0).OrderByDescending(p=>p.Participant.Human).ThenBy(p=>p.Index).ToArray();
            if(humans==null||humans.Length==0){localBinding=null;localHumanCount=1;ResetLocalViewport();yield break;}
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
                seat.localBinding=humans[index].Participant;seat.localRosterIndex=humans[index].Index;seat.localViewportIndex=humans[index].Participant.ScreenSlot;
                seat.spectatorMode=matchSetup.Spectator;
                if(seat.spectatorMode){seat.spectatorPerspective=(PlayableOwner)seat.localRosterIndex;seat.spectatorFrame=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);seat.spectatorCollapsed=true;}
                seat.view=runtime.ParticipantView(seat.LocalOwnerId);
                if(index>0){seat.CreateWorldCore(true);yield return seat.world.Preparation;
                    seat.mapTerrain=new PlayableMapTerrain(profile, staged:true, overheadTerrain: tacticalTerrain?.CompactTerrain);yield return seat.mapTerrain.Preparation;
                    seat.CreateHud();loadingScreen?.BringToFront();seat.input=seat.gameObject.AddComponent<PlayableInput>();seat.BindLocalKeyboardInput();}
                seat.input.AssignedKeyboard=seat.SeatUsesKeyboard?Keyboard.current:null;seat.input.AssignedMouse=seat.SeatUsesKeyboard?Mouse.current:null;seat.input.enabled=seat.SeatUsesKeyboard;
                seat.inLobby=false;seat.root.Q("tactical-minimap-frame").style.display=DisplayStyle.Flex;seat.SetMatchUi(true);seat.ConfigureLocalViewport(seat.localViewportIndex,humans.Length);
                var own=seat.view.Buildings.FirstOrDefault(b=>b.Owner==seat.LocalOwner&&b.Kind==PlayableBuildingKind.Headquarters);
                if(own!=null){var center=seat.Ground(seat.SeatScreenCenter);seat.cameraView.transform.position+=new Vector3((float)(own.Position.X-center.X),0,(float)(own.Position.Z-center.Z));seat.RememberKeyboardCamera();}
                seat.root.RegisterCallback<PointerDownEvent>(evt=>{if(!seat.SeatUsesKeyboard&&!seat.MenuOwnsInput){evt.StopImmediatePropagation();seat.root.focusController?.IgnoreEvent(evt);}},TrickleDown.TrickleDown);
            }
            if (!matchSetup.Spectator && profile.AuthoredMap is ThreeCrossingsMap &&
                humans.Length == 4 && humans.All(seat => seat.Participant.Human))
                foreach (var seat in localPresentations)
                    seat.cameraView.GetComponent<ContactOcclusionCamera>().SetTemporalEnabled(true);
            CreateSharedLocalMap();
        }
        private void RefreshLocalKeyboardBinding()
        {if(SeatUsesKeyboard){input.AssignedKeyboard=Keyboard.current;input.AssignedMouse=Mouse.current;}}
        private void BindLocalKeyboardInput()
        {
            input.Select=Select;input.Order=Order;input.Stop=()=>Submit(PlayableCommandKind.Stop);input.Hold=()=>Submit(PlayableCommandKind.Hold);
            input.CancelContext=()=>{if(CancelScanTarget())return true;if(CancelTeamSignal())return true;if(mapOpen){CloseMap();return true;}return CloseBattleContext();};input.TogglePause=()=>Pause(!paused);input.Restart=Restart;input.FocusLost=()=>{var coordinator=localCoordinator??this;if(!coordinator.battleHudUnattended&&!coordinator.routePerformanceEvidence)Pause(true);};
            input.IsPointerOverUi=OverUi;input.IsKeyboardInUi=()=>MenuOwnsInput||root?.focusController?.focusedElement is VisualElement focused&&root.Contains(focused)&&(focused is Button||focused is TextField);
            input.MapAt=MapAt;input.MapSelect=MapSelect;input.MapPan=PanMap;input.MapOrder=MapOrder;input.ToggleMap=ToggleMap;input.CloseMap=CloseMap;input.Pan=Pan;input.Zoom=Zoom;input.Drag=DrawDrag;BindKeyboardCommands();BindChatInput();
            input.AttackModeChanged=active=>{commandCursor?.Set(false);notice="";};
        }
        private void ConfigureLocalViewport(int index,int count)
        {
            var rect=LocalRect(index,count);cameraView.rect=rect;cameraView.cullingMask=1<<(8+index);world.SetPresentationLayer(8+index);
            bool optimizeTerrain = count == 4 && profile.AuthoredMap is ThreeCrossingsMap;
            world.SetSplitScreenDepthInstancing(optimizeTerrain);
            if (optimizeTerrain && TerrainMaterialCacheCamera.DeviceSupportsCache)
            {
                var cache = cameraView.GetComponent<TerrainMaterialCacheCamera>();
                if (!cache) cache = cameraView.gameObject.AddComponent<TerrainMaterialCacheCamera>();
                cache.enabled = true;
                cache.Configure(world);
            }
            else
                RemoveTerrainCache();
            root.style.left=Length.Percent(rect.x*100);root.style.top=Length.Percent((1-rect.y-rect.height)*100);root.style.right=root.style.bottom=StyleKeyword.Auto;
            root.style.width=Length.Percent(rect.width*100);root.style.height=Length.Percent(rect.height*100);root.style.overflow=Overflow.Hidden;
        }
        private void ResetLocalViewport()
        {
            localBinding=null;localRosterIndex=localViewportIndex=0;localHumanCount=1;localPauseOwner=null;
            if(root!=null){root.style.left=root.style.right=root.style.top=root.style.bottom=0;root.style.width=root.style.height=StyleKeyword.Auto;root.style.overflow=Overflow.Visible;}
            RemoveTerrainCache();
            world?.SetSplitScreenDepthInstancing(false);
            if(cameraView!=null){cameraView.rect=new Rect(0,0,1,1);cameraView.cullingMask=~0;world?.SetPresentationLayer(0);}
            if(input!=null)input.enabled=true;
        }
        private void RemoveTerrainCache()
        {
            if (!cameraView || !cameraView.TryGetComponent<TerrainMaterialCacheCamera>(out var cache))
                return;
            cache.enabled = false;
            Destroy(cache);
        }

        private void DisposeLocalChildren()
        {
            foreach (var seat in localPresentations)
                if (seat.cameraView)
                    seat.cameraView.GetComponent<ContactOcclusionCamera>()?.SetTemporalEnabled(false);
            foreach(var seat in localPresentations.Where(s=>s!=this).ToArray()){seat.input.WorldInputEnabled=false;seat.runtime=null;Destroy(seat.gameObject);}
            localPresentations.Clear();DisposeSharedLocalMap();
        }
        private void PresentLocalChildren(OfflinePresentationFrame frame,PlayableCommandReceipt[] receipts)
        {
            if(frame==null)return;
            using var measure=PerformanceProfile.Measure(ProfileStage.ChildPresentation);
            foreach(var seat in localPresentations.Where(s=>s!=this))
            {
                seat.RefreshLocalKeyboardBinding();seat.ownerFrame=frame;seat.spectatorFrame=spectatorFrame;seat.spectatorRate=spectatorRate;seat.view=spectatorMode?spectatorFrame.Perspective(seat.spectatorPerspective):frame.Views[seat.LocalOwnerId];seat.paused=paused;seat.restarting=restarting;seat.quitting=quitting;
                seat.DrainHumanOrders();
                seat.input.WorldInputEnabled=!seat.BotSeat&&seat.SeatReady&&!paused&&!preparing&&!returningToLobby&&!restarting&&!quitting&&seat.view.Outcome==PlayableMatchOutcome.Playing&&!ResultsVisible;
                seat.input.CommandInputEnabled=seat.input.WorldInputEnabled&&seat.matchSetup?.Spectator!=true;
                seat.RebindPresentation();seat.Render();seat.UpdateHud();seat.UpdateMaps();seat.UpdateTeamSignalPresentation();seat.UpdateScanPresentation();seat.ObserveScanReceipts(receipts);seat.UpdateMatchNotifications();seat.UpdateLifecycleMarkers();seat.UpdateIncomeMarkers();seat.PollBattleController();seat.UpdateKeyboardPresentation();seat.UpdateBotCamera();seat.UpdateOrderMarkers(receipts.Where(r=>r.OwnerId==seat.LocalOwnerId).ToArray());if(seat.orbitalOpen)seat.orbitalModal.BringToFront();
            }
            UpdateSharedLocalMap(frame);
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private static readonly bool disableLocalFogRasterReuse=Environment.GetEnvironmentVariable("SPACEWARS_QA_DISABLE_FOG_RASTER_REUSE")=="1";
        private static readonly bool disableLocalTrailMeshReuse=Environment.GetEnvironmentVariable("SPACEWARS_QA_DISABLE_TRAIL_MESH_REUSE")=="1";
#endif
        private bool SharesLocalTrailMeshes
        {
            get
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if(disableLocalTrailMeshReuse)return false;
#endif
                return localHumanCount==4&&ownerFrame!=null&&matchSetup?.Spectator==false&&profile.AuthoredMap is ThreeCrossingsMap;
            }
        }
        private PlayableWorld LocalTrailMeshSource()
        {
            foreach(var seat in (localCoordinator??this).localPresentations)
            {
                if(seat==this)break;
                if(ReferenceEquals(seat.ownerFrame,ownerFrame)&&seat.view?.Team==view.Team&&
                    ReferenceEquals(seat.view.Vision,view.Vision))
                    return seat.world;
            }
            return null;
        }
        private PlayableFogMask LocalFogRasterSource()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if(disableLocalFogRasterReuse)return null;
#endif
            if(localCoordinator==null||view.Vision==null)return null;
            foreach(var seat in localCoordinator.localPresentations)
            {
                if(seat==this)break;
                if(ReferenceEquals(seat.view?.Vision,view.Vision))return seat.world?.Fog;
            }
            return null;
        }
        private void PauseFrom(PlayableBootstrap seat,bool value){localPauseOwner=value?this:null;Pause(value);}
        private void BroadcastPause(bool value)
        {
            if(value&&localPauseOwner==null)localPauseOwner=this;
            foreach(var seat in localPresentations){seat.root.style.display=value&&seat!=localPauseOwner?DisplayStyle.None:DisplayStyle.Flex;seat.ExpandLocalMenu(value&&seat==localPauseOwner);seat.paused=value;if(value){seat.CloseChat();seat.CloseOrbital();}seat.input?.ClearMode();seat.battleGestures.Cancel();seat.CloseMap();if(!value){seat.CloseChildMenu();seat.menuNavigation?.SetScope(null);}}
            if(!value)localPauseOwner=null;
            UpdateLocalDividers();
        }
    }
}
