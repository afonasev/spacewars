using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Input;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    // Opt-in source fixture route, one authority. Ordinary PlayableBootstrap and
    // its historical AI/crisis policy remain separate and unchanged.
    public sealed partial class OfflineTwoLocalBootstrap : MonoBehaviour
    {
        private sealed class Viewport
        {
            public Camera Camera;public OfflinePadCamera CameraState; public PlayableWorld World;public Transform Root;
            public VisualElement Hud,MapPanel;public Label Status,Notice;public VisualElement CursorMarker;public Button BuildFactory,QueueExplorer;public int Site,Slot,Parent;
            public PlayableMapTerrain Terrain; public PlayableMapSurface Map;
            public readonly HashSet<int> Selection=new HashSet<int>();
            public PlayableSnapshot View; public NavPoint Cursor;public readonly OfflinePadGroups Groups=new OfflinePadGroups();public int OriginalHq,BasePage;public int? GroupSlots;public int Anchor;
        }
        private PlayableProfile profile;
        [Serializable] private sealed class LocalPaint {public string[] colors;}
        private Color[] ownerPaint;
        private Color Paint(PlayableOwner? owner)=>owner.HasValue?ownerPaint[(int)owner.Value]:Color.white;
        private NativeLocalInputProfile padProfile;
        private PlayableRuntime runtime;
        private OfflineLocalSession session;
        private readonly Viewport[] views=new Viewport[2];
        private Gamepad assignedPad;private Keyboard assignedKeyboard;private Mouse assignedMouse;
        private PlayableInput keyboard;
        private readonly OfflinePadGestures gestures=new OfflinePadGestures();
        private readonly OfflinePadSelection padSelection=new OfflinePadSelection();
        private VisualElement root,lobby;private Label readiness;private OfflinePadRing padRing;private Button bindKeyboardButton,resumeButton;private int padMenuIndex=1;private bool pendingPadResume;
        private PlayableMapTerrain sharedTerrain;private PlayableMapSurface sharedMap;

        private bool focused=true,wasPaused=true,keyboardAttack;
        private string evidence;private long renderedSequence=-1;
        private string syntheticEvidence;
        public OfflineLocalSession Session=>session;
        private void Start()
        {
            AudioListener.volume=0;Application.runInBackground=true;QualitySettings.vSyncCount=0;
            profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text));
            Application.targetFrameRate=profile.RenderTargetFramesPerSecond;
            padProfile=JsonUtility.FromJson<NativeLocalInputProfile>(Resources.Load<TextAsset>("NativeLocalInputProfile").text);padProfile.Validate();
            ownerPaint=JsonUtility.FromJson<LocalPaint>(Resources.Load<TextAsset>("NativeLocalPresentationProfile").text).colors.Select(color=>{if(!ColorUtility.TryParseHtmlString(color,out var paint))throw new ArgumentException("Invalid source owner paint.");return paint;}).ToArray();
            foreach(var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))camera.enabled=false;
            var args=Environment.GetCommandLineArgs();for(int i=0;i+1<args.Length;i++)if(args[i]=="-twoLocalEvidence")evidence=args[i+1];
            #if DEVELOPMENT_BUILD || UNITY_EDITOR
            for(int i=0;i+1<args.Length;i++)if(args[i]=="-twoLocalSyntheticEvidence"){syntheticEvidence=args[i+1];evidence=syntheticEvidence;}
#endif
            RenderSettings.ambientLight=new Color(.63f,.69f,.73f);
            var sun=new GameObject("Local fixture light").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.4f;sun.transform.rotation=Quaternion.Euler(48,-30,0);
            var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");
            panel.scaleMode=PanelScaleMode.ConstantPixelSize;var document=gameObject.AddComponent<UIDocument>();document.panelSettings=panel;
            root=document.rootVisualElement;root.pickingMode=PickingMode.Ignore;root.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");root.style.color=Color.white;
            var config=SourceFlatFixture.Load(Resources.Load<TextAsset>("OfflineFlatFixture").text,profile,Array.IndexOf(args,"-twoLocalAlliedFixture")>=0);
            runtime=new PlayableRuntime(config,1,startPaused:true);
            for(int i=0;i<2;i++)CreateViewport(i);padRing=new OfflinePadRing();root.Add(padRing);
            sharedTerrain=new PlayableMapTerrain(profile);sharedMap=new PlayableMapSurface(profile,sharedTerrain.Texture){OwnerPaint=Paint};
            sharedMap.style.position=Position.Absolute;sharedMap.style.width=(float)profile.MinimapCompactSize;sharedMap.style.height=(float)profile.MinimapCompactSize;
            sharedMap.style.left=Length.Percent(50);sharedMap.style.marginLeft=-(float)profile.MinimapCompactSize/2;sharedMap.style.bottom=0;root.Add(sharedMap);
            lobby=new VisualElement();lobby.style.position=Position.Absolute;lobby.style.left=Length.Percent(25);lobby.style.right=Length.Percent(25);lobby.style.top=Length.Percent(30);lobby.style.backgroundColor=new Color(.04f,.07f,.09f,.98f);root.Add(lobby);
            lobby.Add(new Label("ДВА ЛОКАЛЬНЫХ ИГРОКА · ЛОКАЛЬНАЯ АРЕНА"));
            readiness=new Label("Игрок 1: назначьте мышь/клавиатуру. Игрок 2: нажмите кнопку своего геймпада.");lobby.Add(readiness);
            bindKeyboardButton=new Button(()=>BindKeyboard()){text="Назначить мышь и клавиатуру игроку 1"};lobby.Add(bindKeyboardButton);
            resumeButton=new Button(()=>Resume()){text="Готовы · начать / продолжить"};lobby.Add(resumeButton);
            keyboard=gameObject.AddComponent<PlayableInput>();keyboard.WorldInputEnabled=false;keyboard.SourceSeatSemantics=true;
            keyboard.CanReadWorld=()=>session!=null&&!session.Paused&&assignedPad.added&&assignedKeyboard.added&&assignedMouse.added;
            keyboard.Select=(a,b,add)=>SelectMouse(a,b,add);keyboard.Order=(p,attack)=>Order(0,Ground(0,p),attack,false);
            keyboard.Stop=()=>Submit(0,PlayableCommandKind.Stop);keyboard.Hold=()=>Submit(0,PlayableCommandKind.Hold);
            keyboard.Pan=axis=>Pan(0,axis*(float)profile.CameraPanSpeed*Time.unscaledDeltaTime);keyboard.Zoom=z=>Zoom(0,z*(float)profile.CameraZoomSpeed);
            keyboard.TogglePause=()=>{if(session?.Paused==true)Resume();else if(views[0].MapPanel.style.display==DisplayStyle.Flex)views[0].MapPanel.style.display=DisplayStyle.None;else Pause();};keyboard.FocusLost=Pause;
            keyboard.ToggleMap=()=>views[0].MapPanel.style.display=views[0].MapPanel.style.display==DisplayStyle.Flex?DisplayStyle.None:DisplayStyle.Flex;
            keyboard.CloseMap=()=>views[0].MapPanel.style.display=DisplayStyle.None;
            keyboard.MapAt=p=>MapAt(p);keyboard.MapSelect=(map,a,b,add)=>MapSelect(map,a,b,add);
            keyboard.MapOrder=(map,p,attack)=>Order(0,MapGround(map,p),attack,true);
            keyboard.IsPointerOverUi=p=>OverHud(p)||!views[0].Camera.pixelRect.Contains(p);
            keyboard.Capture=Capture;keyboard.AttackModeChanged=active=>keyboardAttack=active;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if(syntheticEvidence!=null)StartCoroutine(SyntheticPlayerEvidence());
#endif
        }
        private void CreateViewport(int index)
        {
            var v=views[index]=new Viewport();v.Root=new GameObject("LocalSeat "+index+" presentation").transform;v.Root.SetParent(transform);
            v.World=new PlayableWorld(v.Root,profile);v.Terrain=new PlayableMapTerrain(profile);
            v.Camera=new GameObject("LocalSeat "+index+" camera").AddComponent<Camera>();v.Camera.orthographic=false;OfflineCameraMirrorFeature.Register(v.Camera);
            // Source renderer rects(count=2): full-height equal left/right halves.
            v.Camera.rect=new Rect(index*.5f,0,.5f,1);v.Camera.cullingMask=1<<(8+index);v.Camera.backgroundColor=new Color(.045f,.075f,.1f);v.Camera.clearFlags=CameraClearFlags.SolidColor;

            var view=runtime.OfflineFrame.Views[index==0?"owner-11":"owner-28"];v.View=view;v.OriginalHq=view.Buildings.FirstOrDefault(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Headquarters)?.Id??0;var own=view.Entities.Where(e=>e.Owner==view.Owner).ToArray();v.CameraState=new OfflinePadCamera(own.Sum(e=>e.Position.X)/Math.Max(1,own.Length),own.Sum(e=>e.Position.Z)/Math.Max(1,own.Length),padProfile);v.CameraState.Apply(v.Camera);v.Cursor=Center(index);
            v.Hud=new VisualElement();v.Hud.style.position=Position.Absolute;v.Hud.style.left=Length.Percent(index*50);v.Hud.style.width=Length.Percent(50);v.Hud.style.top=0;v.Hud.style.backgroundColor=new Color(.04f,.07f,.09f,.92f);root.Add(v.Hud);
            v.CursorMarker=new VisualElement();v.CursorMarker.pickingMode=PickingMode.Ignore;v.CursorMarker.style.position=Position.Absolute;v.CursorMarker.style.borderLeftColor=Color.white;v.CursorMarker.style.borderRightColor=Color.white;v.CursorMarker.style.borderTopColor=Color.white;v.CursorMarker.style.borderBottomColor=Color.white;root.Add(v.CursorMarker);if(index==0)v.CursorMarker.style.display=DisplayStyle.None;
            v.Status=new Label();v.Hud.Add(v.Status);v.Notice=new Label();v.Hud.Add(v.Notice);
            v.Hud.Add(new Label(index==0?"ЛКМ выбор · ПКМ приказ · A атака · Tab карта":"A выбор · B приказ / удержание атака · X стоп / удержание HOLD · View карта"));
            if(index==0){var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;v.Hud.Add(row);v.BuildFactory=new Button(()=>Build(0)){text="Фабрика · выбранная своя площадка"};row.Add(v.BuildFactory);v.QueueExplorer=new Button(()=>Submit(0,PlayableCommandKind.QueueExplorer)){text="Исследователь · выбранная фабрика"};row.Add(v.QueueExplorer);}
            v.MapPanel=new VisualElement();v.MapPanel.style.position=Position.Absolute;v.MapPanel.style.left=Length.Percent(index*50+25);v.MapPanel.style.width=Math.Min((float)profile.MinimapTacticalSize,Screen.width/2f);v.MapPanel.style.marginLeft=-Math.Min((float)profile.MinimapTacticalSize,Screen.width/2f)/2;v.MapPanel.style.top=Length.Percent(20);v.MapPanel.style.height=Math.Min((float)profile.MinimapTacticalSize,Screen.width/2f);v.MapPanel.style.display=DisplayStyle.None;root.Add(v.MapPanel);
            v.Map=new PlayableMapSurface(profile,v.Terrain.Texture){OwnerPaint=Paint};v.Map.style.width=Length.Percent(100);v.Map.style.height=Length.Percent(100);v.MapPanel.Add(v.Map);
        }
        private void BindKeyboard(){assignedKeyboard=Keyboard.current;assignedMouse=Mouse.current;TryBind();}
        private void PollPadLobby()
        {
            if(assignedPad==null||!assignedPad.added||!focused||session!=null&&!session.Paused)return;
            if(session==null)padMenuIndex=0;
            if(assignedPad.dpad.up.wasPressedThisFrame||assignedPad.dpad.left.wasPressedThisFrame)padMenuIndex=Math.Max(0,padMenuIndex-1);
            if(assignedPad.dpad.down.wasPressedThisFrame||assignedPad.dpad.right.wasPressedThisFrame)padMenuIndex=Math.Min(1,padMenuIndex+1);
            if(assignedPad.buttonSouth.wasPressedThisFrame){if(padMenuIndex==0){BindKeyboard();padMenuIndex=1;}else pendingPadResume=true;}
            if(session!=null&&(assignedPad.buttonEast.wasPressedThisFrame||assignedPad.startButton.wasPressedThisFrame))pendingPadResume=true;
            (padMenuIndex==0?bindKeyboardButton:resumeButton).Focus();
            // Confirmation consumes the press in non-combat UI. The existing
            // two-device neutral/ready gate still controls the actual resume.
            if(pendingPadResume&&session!=null&&Neutral(assignedPad)&&Neutral(assignedKeyboard)&&Neutral(assignedMouse)){Resume();if(!session.Paused)pendingPadResume=false;}
        }
        private void TryBind()
        {
            if(session!=null||assignedPad==null||assignedKeyboard==null||assignedMouse==null)return;
            keyboard.AssignedKeyboard=assignedKeyboard;keyboard.AssignedMouse=assignedMouse;
            session=new OfflineLocalSession(runtime,new[]{OfflineLocalSession.Bind("local-keyboard","owner-11","keyboard+mouse:"+assignedKeyboard.deviceId+":"+assignedMouse.deviceId),OfflineLocalSession.Bind("local-gamepad","owner-28","gamepad:"+assignedPad.deviceId)});
        }
        private static bool Neutral(UnityEngine.InputSystem.InputDevice device)
        {
            if(device==null||!device.added)return false;
            if(device is Gamepad pad)return pad.allControls.OfType<ButtonControl>().All(c=>!c.isPressed);
            return device!=null&&device.allControls.OfType<ButtonControl>().All(c=>!c.isPressed);
        }
        private void Update()
        {
            if(runtime==null)return;
            if(assignedPad==null){foreach(var pad in Gamepad.all)if(pad.allControls.OfType<ButtonControl>().Any(c=>c.wasPressedThisFrame)){assignedPad=pad;TryBind();break;}}
            TryBind();PollPadLobby();
            if(session==null){Render(runtime.OfflineFrame);return;}
            session.SampleDevice("local-keyboard",assignedKeyboard.added&&assignedMouse.added,Neutral(assignedKeyboard)&&Neutral(assignedMouse));
            session.SampleDevice("local-gamepad",assignedPad.added,Neutral(assignedPad));
            if(session.Paused&&!wasPaused){keyboard.ClearMode();gestures.Cancel();foreach(var v in views)v.MapPanel.style.display=DisplayStyle.None;}
            wasPaused=session.Paused;keyboard.WorldInputEnabled=!session.Paused&&focused;
            session.ReadFrame();Render(session.Frame);Route();PollPad();
            var k=views[0].CursorMarker;k.BringToFront();k.style.display=keyboardAttack&&assignedMouse.added?DisplayStyle.Flex:DisplayStyle.None;if(keyboardAttack&&assignedMouse.added){var p=PanelPoint(assignedMouse.position.ReadValue());float radius=padProfile.cursorRadius*OfflinePadCamera.PixelsPerMeter(views[0].Camera,views[0].World.Point(views[0].Cursor));k.style.left=p.x-radius;k.style.top=p.y-radius;k.style.width=radius*2;k.style.height=radius*2;k.style.borderLeftWidth=k.style.borderRightWidth=k.style.borderTopWidth=k.style.borderBottomWidth=padProfile.cursorWidth*OfflinePadCamera.PixelsPerMeter(views[0].Camera,views[0].World.Point(views[0].Cursor));k.style.borderLeftColor=k.style.borderRightColor=k.style.borderTopColor=k.style.borderBottomColor=Color.red;}
            lobby.style.display=session.Paused?DisplayStyle.Flex:DisplayStyle.None;
            readiness.text=session.WaitingSeat==null?"Оба устройства на месте. Отпустите кнопки и подтвердите готовность.":"Ожидается "+session.WaitingSeat+" · верните назначенное устройство и отпустите кнопки";
        }
        private void Resume()
        {
            if(session==null)return;foreach(var seat in session.Seats)if(seat.Connected&&seat.Neutral)session.Ready(seat.Id);
            if(session.Resume()){keyboard.ClearMode();gestures.Cancel();wasPaused=false;}
        }
        private void Pause(){session?.Pause();keyboard?.ClearMode();gestures.Cancel();}
        private void OnApplicationFocus(bool value){focused=value;if(!value){pendingPadResume=false;Pause();}}
        private void PollPad()
        {
            var v=views[1];if(!assignedPad.added){gestures.Cancel();padRing.style.display=DisplayStyle.None;return;}
            if(v.Anchor!=0&&!v.View.Buildings.Any(b=>b.Id==v.Anchor&&b.Owner==v.View.Owner)){v.Anchor=0;gestures.SetMode("world");}
            bool wheel=gestures.Mode=="buildingWheel"||gestures.Mode=="groupWheel"||gestures.Mode=="groupAssign"||gestures.Mode=="baseWheel";
            if(!gestures.Blocked&&!session.Paused&&focused){
                float Axis(float x)=>Mathf.Abs(x)<=padProfile.deadzone?0:Mathf.Sign(x)*(Mathf.Abs(x)-padProfile.deadzone)/(1-padProfile.deadzone);
                var rs=assignedPad.rightStick.ReadValue();var ls=assignedPad.leftStick.ReadValue();float dt=Time.unscaledDeltaTime;
                if(!wheel){
                    v.Cursor=new NavPoint(Math.Clamp(v.Cursor.X+(gestures.Map?1:-1)*Axis(rs.x)*padProfile.cursorSpeed*dt,-profile.ArenaHalfExtent,profile.ArenaHalfExtent),Math.Clamp(v.Cursor.Z+(gestures.Map?-1:1)*Axis(rs.y)*padProfile.cursorSpeed*dt,-profile.ArenaHalfExtent,profile.ArenaHalfExtent));
                    if(!gestures.Map){var delta=new Vector2(-Axis(ls.x),Axis(ls.y))*padProfile.cameraSpeed*dt;Pan(1,delta);if(delta.sqrMagnitude>0)v.Cursor=Center(1);
                        v.CameraState.AdjustZoom(((assignedPad.dpad.down.isPressed?1:0)-(assignedPad.dpad.up.isPressed?1:0))*padProfile.zoomSpeed*dt,padProfile);v.CameraState.Apply(v.Camera);
                        if(assignedPad.leftStickButton.wasPressedThisFrame){v.CameraState.Reset();v.CameraState.Apply(v.Camera);v.Cursor=Center(1);}}
                }else if(gestures.Mode=="baseWheel"){if(assignedPad.dpad.left.wasPressedThisFrame)v.BasePage=Math.Max(0,v.BasePage-1);if(assignedPad.dpad.right.wasPressedThisFrame)v.BasePage++;}
            }
            var actions=PadMenuActions(v,out int pages);var stick=assignedPad.leftStick.ReadValue();var index=OfflinePadGestures.RingSector(stick.x,-stick.y,actions.Length,padProfile.radialDeadzone);var sector=index.HasValue?actions[index.Value].Sector:null;
            int mask=(assignedPad.buttonSouth.isPressed?1:0)|(assignedPad.buttonEast.isPressed?2:0)|(assignedPad.buttonWest.isPressed?4:0)|(assignedPad.selectButton.isPressed?8:0)|(assignedPad.startButton.isPressed?16:0)|(assignedPad.rightShoulder.isPressed?32:0)|(assignedPad.leftShoulder.isPressed?64:0)|(assignedPad.rightTrigger.isPressed?128:0);
            var intents=gestures.StepDetailed(Time.unscaledTimeAsDouble*1000,mask,assignedPad.added,focused&&!session.Paused,padProfile,gestures.Map||PadEntity(1,v.Cursor)==0,sector);
            foreach(var intent in intents){
                if(intent.Kind=="pause"){Pause();break;}
                if(PadMenuIntent(intent,actions,v))continue;
                if(intent.Kind=="cameraJump"){if(v.Anchor!=0){new OfflinePadActionCommand{Kind=PlayableCommandKind.SetRally,Entities=new[]{v.Anchor},Point=v.Cursor}.Submit(session,session.Seats[1].Id);gestures.SetMode("buildingWheel");}else Focus(1,v.Cursor);continue;}
                if(intent.Kind=="select")SelectPadPoint(Time.unscaledTimeAsDouble*1000);
                if(intent.Kind=="selectCircle"||intent.Kind=="selectMapCircle"||intent.Kind=="selectScreen"){v.Selection.Clear();double radius=intent.Kind=="selectScreen"?double.PositiveInfinity:(intent.HeldMs-padProfile.holdMs)/1000*padProfile.selectionGrowth;foreach(var id in OfflinePadSelection.Area(PadUnits(1),v.Cursor.X,v.Cursor.Z,radius,intent.Kind=="selectMapCircle"))v.Selection.Add(id);}
                if(intent.Kind=="context"&&gestures.Map&&v.Anchor!=0){gestures.SetMode("buildingWheel");continue;}
                if(intent.Kind=="context"||intent.Kind=="attackMove")Order(1,v.Cursor,intent.Kind=="attackMove",gestures.Map||intent.Kind=="attackMove");
                if(intent.Kind=="stop"||intent.Kind=="hold")Submit(1,intent.Kind=="stop"?PlayableCommandKind.Stop:PlayableCommandKind.Hold);
            }
            v.CursorMarker.style.borderLeftColor=v.CursorMarker.style.borderRightColor=v.CursorMarker.style.borderTopColor=v.CursorMarker.style.borderBottomColor=gestures.AttackPreview?Color.red:Color.white;var cursorScreen=OfflinePadCamera.ProjectScreen(v.Camera,v.World.Point(v.Cursor));var point=PanelPoint(new Vector2(cursorScreen.x,cursorScreen.y));float pixelsPerMeter=OfflinePadCamera.PixelsPerMeter(v.Camera,v.World.Point(v.Cursor));float cursorRadius=padProfile.cursorRadius*pixelsPerMeter,width=padProfile.cursorWidth*pixelsPerMeter;v.CursorMarker.style.left=point.x-cursorRadius;v.CursorMarker.style.top=point.y-cursorRadius;v.CursorMarker.style.width=cursorRadius*2;v.CursorMarker.style.height=cursorRadius*2;v.CursorMarker.style.borderLeftWidth=width;v.CursorMarker.style.borderRightWidth=width;v.CursorMarker.style.borderTopWidth=width;v.CursorMarker.style.borderBottomWidth=width;v.CursorMarker.style.display=v.Camera.pixelRect.Contains(new Vector2(cursorScreen.x,cursorScreen.y))?DisplayStyle.Flex:DisplayStyle.None;
            if(gestures.Map){var uv=new PlayableMapTransform(profile.ArenaHalfExtent,profile.ArenaHalfExtent).Project(v.Cursor);var mapRect=v.Map.worldBound;cursorRadius=padProfile.cursorRadius*mapRect.width/(2*(float)profile.ArenaHalfExtent);v.CursorMarker.style.left=mapRect.x+(float)uv.X*mapRect.width-cursorRadius;v.CursorMarker.style.top=mapRect.y+(float)uv.Z*mapRect.height-cursorRadius;v.CursorMarker.style.width=cursorRadius*2;v.CursorMarker.style.height=cursorRadius*2;v.CursorMarker.style.display=DisplayStyle.Flex;}
            v.CursorMarker.BringToFront();if(session.Paused)v.CursorMarker.style.display=DisplayStyle.None;
            v.CursorMarker.style.borderTopLeftRadius=v.CursorMarker.style.borderTopRightRadius=v.CursorMarker.style.borderBottomLeftRadius=v.CursorMarker.style.borderBottomRightRadius=cursorRadius;
            v.MapPanel.style.display=gestures.Map?DisplayStyle.Flex:DisplayStyle.None;

            actions=PadMenuActions(v,out pages);index=OfflinePadGestures.RingSector(stick.x,-stick.y,actions.Length,padProfile.radialDeadzone);
            padRing.Set(actions,gestures.Added?gestures.AddedSectorId:index.HasValue?actions[index.Value].Sector.Id:null,gestures.Mode,v.BasePage,pages,gestures.Added,gestures.Progress,padProfile.radialRadius);if(actions.Length>0){padRing.BringToFront();v.CursorMarker.style.display=DisplayStyle.None;}
            if(session.Paused)padRing.style.display=DisplayStyle.None;
        }
        private static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
        private void Submit(int index,PlayableCommandKind kind,NavPoint point=default(NavPoint),int target=0)
        {
            if(session==null)return;var v=views[index];var result=session.Submit(session.Seats[index].Id,kind,v.Selection.ToArray(),point,target);v.Notice.text=result.Status.ToString();
        }
        private void Build(int index)
        {
            if(session==null)return;var v=views[index];if(v.Site==0||v.Slot==0||v.Parent==0)return;
            session.Submit(session.Seats[index].Id,PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:v.Site,slotId:v.Slot,parentId:v.Parent);
        }
        private OfflinePadSelectable[] PadUnits(int index)
        {
            var v=views[index];return v.View.Entities.Select(e=>new OfflinePadSelectable(e.Id,(int)e.Kind,e.Owner==v.View.Owner,e.Position.X,e.Position.Z,PlayableUnitRules.Radius(profile,e.Kind),OfflinePadSelection.InViewport(v.Camera,v.World.Point(e.Position)))).ToArray();
        }
        private int PadEntity(int index,NavPoint point)
        {
            var v=views[index];var candidates=v.View.Entities.Where(e=>e.Health>0&&Distance(e.Position,point)<=PlayableUnitRules.Radius(profile,e.Kind)).Select(e=>new{e.Id,e.Position}).Concat(v.View.Buildings.Where(b=>b.Health>0&&b.Phase!=ConstructionPhase.Pending&&Distance(b.Position,point)<=TerritoryRules.Radius(profile,b.Kind)).Select(b=>new{b.Id,b.Position}));
            return candidates.OrderBy(e=>Distance(e.Position,point)).ThenBy(e=>e.Id.ToString(),StringComparer.Ordinal).Select(e=>e.Id).FirstOrDefault();
        }
        private void SelectPadPoint(double now)
        {
            var v=views[1];int entity=PadEntity(1,v.Cursor);
            foreach(var site in v.View.DiscoveredSites){
                var center=v.View.Buildings.FirstOrDefault(b=>b.SiteId==site.Id&&b.SlotId==0&&b.Phase!=ConstructionPhase.Pending);
                if(center?.Owner==v.View.Owner&&center.Phase==ConstructionPhase.Ready){
                    foreach(var slot in site.Slots)if(!v.View.Buildings.Any(b=>b.SiteId==site.Id&&b.SlotId==slot.Id)&&TerritoryRules.Contains(v.Cursor,slot.Position,profile.OrdinaryPadRadius,true,false,slot.Heading)){
                        v.Selection.Clear();v.Anchor=0;v.Site=site.Id;v.Slot=slot.Id;v.Parent=center.Id;gestures.SetMode("buildingWheel");return;
                    }
                }
                if(center==null&&TerritoryRules.Contains(v.Cursor,site.Position,TerritoryRules.Radius(profile,site.Kind),false,site.Kind==PlayableBuildingKind.Mine)){
                    v.Selection.Clear();v.Anchor=0;v.Site=site.Id;v.Slot=v.Parent=0;gestures.SetMode("buildingWheel");return;
                }
            }
            var building=v.View.Buildings.FirstOrDefault(b=>b.Id==entity&&b.Owner==v.View.Owner);
            if(building!=null){v.Selection.Clear();v.Selection.Add(building.Id);v.Anchor=building.Id;v.Site=v.Slot=v.Parent=0;gestures.SetMode("buildingWheel");return;}
            var units=PadUnits(1);int hit=units.Any(u=>u.Id==entity)?entity:0;
            // SOURCE only changes the remembered anchor on an explicit anchor
            // selection/cancel/all-army/build. Map/rally returns retain it.
            v.Selection.Clear();foreach(var id in padSelection.Tap(now,hit,units,padProfile.doubleTapMs))v.Selection.Add(id);
        }
        private void SelectPoint(int index,NavPoint p)
        {
            var v=views[index];v.Selection.Clear();v.Site=v.Slot=v.Parent=0;var e=v.View.Entities.Where(e=>e.Owner==v.View.Owner).OrderBy(e=>Distance(e.Position,p)).FirstOrDefault();
            if(e!=null&&Distance(e.Position,p)<=PlayableUnitRules.Radius(profile,e.Kind)*profile.TargetPickRadiusMultiplier){v.Selection.Add(e.Id);return;}
            var b=v.View.Buildings.Where(b=>b.Owner==v.View.Owner).OrderBy(b=>Distance(b.Position,p)).FirstOrDefault();if(b!=null&&Distance(b.Position,p)<=profile.BuildingPickRadius){v.Selection.Add(b.Id);return;}
            foreach(var site in v.View.Sites.Where(site=>site.Owner==v.View.Owner&&site.Ready))foreach(var slot in site.Site.Slots)if(!v.View.Buildings.Any(building=>building.SiteId==site.Site.Id&&building.SlotId==slot.Id)&&Distance(slot.Position,p)<=profile.OrdinaryPadRadius){v.Site=site.Site.Id;v.Slot=slot.Id;v.Parent=site.CenterId;return;}
        }
        private void SelectMouse(Vector2 from,Vector2 to,bool add)
        {
            var v=views[0];if((to-from).magnitude<=profile.SelectionDragPixels){var prior=add?v.Selection.ToArray():Array.Empty<int>();SelectPoint(0,Ground(0,to));foreach(var id in prior)v.Selection.Add(id);return;}
            if(!add)v.Selection.Clear();foreach(var e in v.View.Entities.Where(e=>e.Owner==v.View.Owner)){var p=OfflinePadCamera.ProjectScreen(v.Camera,v.World.Point(e.Position));if(p.x>=Math.Min(from.x,to.x)&&p.x<=Math.Max(from.x,to.x)&&p.y>=Math.Min(from.y,to.y)&&p.y<=Math.Max(from.y,to.y))v.Selection.Add(e.Id);}
        }
        private bool AtEntity(int index,NavPoint point)=>views[index].View.Entities.Any(e=>Distance(e.Position,point)<=PlayableUnitRules.Radius(profile,e.Kind)*profile.TargetPickRadiusMultiplier)||views[index].View.Buildings.Any(b=>Distance(b.Position,point)<=profile.BuildingPickRadius);
        private void Order(int index,NavPoint p,bool attack,bool map)
        {
            var v=views[index];if(v.Selection.Count==1&&v.View.Buildings.Any(b=>v.Selection.Contains(b.Id)&&b.Owner==v.View.Owner)){if(index==1)return;Submit(index,PlayableCommandKind.SetRally,p);return;}
            if(!map&&!attack){int leader=v.View.Entities.Where(e=>!v.View.IsHostile(e.Owner)&&Distance(e.Position,p)<=PlayableUnitRules.Radius(profile,e.Kind)*profile.TargetPickRadiusMultiplier).Select(e=>e.Id).FirstOrDefault();if(leader!=0){Submit(index,PlayableCommandKind.Follow,p,leader);return;}}
            int target=map?0:v.View.Entities.Where(e=>v.View.IsHostile(e.Owner)&&Distance(e.Position,p)<=PlayableUnitRules.Radius(profile,e.Kind)*profile.TargetPickRadiusMultiplier).Select(e=>e.Id).FirstOrDefault();
            if(target==0&&!map)target=v.View.Buildings.Where(b=>v.View.IsHostile(b.Owner)&&Distance(b.Position,p)<=profile.BuildingPickRadius).Select(b=>b.Id).FirstOrDefault();
            Submit(index,target!=0?PlayableCommandKind.Attack:attack?PlayableCommandKind.AttackMove:PlayableCommandKind.Move,p,target);
        }
        private NavPoint Ground(int index,Vector2 screen){var ray=OfflinePadCamera.ScreenRay(views[index].Camera,screen);new Plane(Vector3.up,Vector3.zero).Raycast(ray,out var distance);var point=ray.GetPoint(distance);return new NavPoint(point.x,point.z);}
        private NavPoint Center(int i)=>Ground(i,views[i].Camera.pixelRect.center);
        private void Focus(int i,NavPoint p){views[i].CameraState.Focus(p.X,p.Z);views[i].CameraState.Apply(views[i].Camera);}
        private void Pan(int i,Vector2 delta){var p=Center(i);Focus(i,new NavPoint(Math.Clamp(p.X+delta.x,-profile.ArenaHalfExtent,profile.ArenaHalfExtent),Math.Clamp(p.Z+delta.y,-profile.ArenaHalfExtent,profile.ArenaHalfExtent)));}
        private void Zoom(int i,float delta){views[i].CameraState.AdjustZoom(-delta,padProfile);views[i].CameraState.Apply(views[i].Camera);}
        private Vector2 PanelPoint(Vector2 p)=>RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(p.x,Screen.height-p.y));
        private bool OverHud(Vector2 p)=>views.Any(v=>v.Hud.worldBound.Contains(PanelPoint(p)))||lobby.resolvedStyle.display==DisplayStyle.Flex;
        private int MapAt(Vector2 p)=>views[0].MapPanel.resolvedStyle.display==DisplayStyle.Flex&&views[0].Map.worldBound.Contains(PanelPoint(p))?2:sharedMap.worldBound.Contains(PanelPoint(p))?1:0;
        private NavPoint MapGround(int map,Vector2 p){var element=map==2?views[0].Map:sharedMap;return element.Ground(element.WorldToLocal(PanelPoint(p)));}
        private void MapSelect(int map,Vector2 a,Vector2 b,bool add)
        {
            if(map==1||(b-a).magnitude<=profile.SelectionDragPixels){Focus(0,MapGround(map,b));if(map==2)views[0].MapPanel.style.display=DisplayStyle.None;return;}
            if(!add)views[0].Selection.Clear();foreach(var id in PlayableMapView.SelectOwnUnits(views[0].View,MapGround(map,a),MapGround(map,b)))views[0].Selection.Add(id);
        }
        private void Route()
        {
            while(runtime.Requests.TryDequeue(out var r)){var path=new SharedFlowRouter(r.Geometry,r.Profile).FindPath(r.Start,r.Goal);if(!runtime.Answers.TryEnqueue(new NavigationAnswer(r,path)))throw new InvalidOperationException("Navigation answer capacity.");}
        }
        private void Render(OfflinePresentationFrame frame)
        {
            renderedSequence=frame.Sequence;
            for(int i=0;i<2;i++)
            {
                var v=views[i];if(v==null)continue;v.View=frame.Views[i==0?"owner-11":"owner-28"];
                v.World.Fog.Update(v.View.Vision,Time.unscaledDeltaTime);v.World.RenderMemories(v.View);v.World.RenderSites(v.View);
                var alive=new HashSet<int>();foreach(var e in v.View.Entities){alive.Add(e.Id);if(!v.World.Actors.TryGetValue(e.Id,out var actor))actor=v.World.Tank(e.Id,e.Owner==v.View.Owner,e.Kind);PlayableWorld.PaintOwner(actor,Paint(e.Owner));actor.Root.position=v.World.Point(e.Position);actor.Hull.localRotation=Quaternion.Euler(0,90-(float)e.HullHeading*Mathf.Rad2Deg,0);actor.Turret.localRotation=Quaternion.Euler(0,(float)(e.HullHeading-e.TurretHeading)*Mathf.Rad2Deg,0);actor.Selection.SetActive(e.Owner==v.View.Owner&&v.Selection.Contains(e.Id));PlayableWorld.UpdateHealth(actor,v.Camera,(float)e.Health/PlayableUnitRules.Health(profile,e.Kind));}
                foreach(var b in v.View.Buildings){if(b.Phase==ConstructionPhase.Pending)continue;alive.Add(b.Id);if(!v.World.Actors.TryGetValue(b.Id,out var actor))actor=v.World.Building(b.Id,b.Kind.ToString(),b.Owner==v.View.Owner,b.RefineryUpgraded);PlayableWorld.PaintOwner(actor,Paint(b.Owner));actor.Root.position=v.World.Point(b.Position);actor.Root.rotation=Quaternion.Euler(0,-(float)b.Heading*Mathf.Rad2Deg,0);actor.Root.localScale=new Vector3(1,Mathf.Lerp(.2f,1,(float)b.Progress),1);actor.Selection.SetActive(b.Owner==v.View.Owner&&v.Selection.Contains(b.Id));PlayableWorld.UpdateHealth(actor,v.Camera,(float)b.Health/TerritoryRules.Health(profile,b.Kind));}
                foreach(var id in v.World.Actors.Keys.ToArray())if(!alive.Contains(id)){v.World.Remove(id);v.Selection.Remove(id);}
                // Reserved presentation layers are a technical camera isolation bound.
                foreach(var t in v.Root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=8+i;
                v.Terrain.Update(v.World.Fog);var rect=v.Camera.pixelRect;v.Map.Set(v.View,v.Selection,new[]{Ground(i,rect.min),Ground(i,new Vector2(rect.xMax,rect.yMin)),Ground(i,rect.max),Ground(i,new Vector2(rect.xMin,rect.yMax))});
                v.BuildFactory?.SetEnabled(v.Site>0&&v.Slot>0&&v.View.Sites.Any(s=>s.Site.Id==v.Site&&s.Owner==v.View.Owner&&s.Ready)&&!v.View.Buildings.Any(b=>b.SiteId==v.Site&&b.SlotId==v.Slot));
                v.QueueExplorer?.SetEnabled(v.View.Buildings.Any(b=>v.Selection.Contains(b.Id)&&b.Owner==v.View.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.Phase==ConstructionPhase.Ready));
                v.Status.text=$"Игрок {i+1} · {v.View.OwnerId} · команда {v.View.Team} · {v.View.Credits} · выбор {v.Selection.Count} · tick {frame.Tick}";
                if(session!=null&&session.OwnerReceipts.TryGetValue(v.View.OwnerId,out var receipts)&&receipts.Count>0)v.Notice.text=string.Join(" · ",receipts.Select(r=>r.OwnerId+" #"+r.Sequence+": "+r.Status));
            }
            if(views[0]!=null&&sharedMap!=null){sharedTerrain.UpdateUnion(views.Select(v=>v.World.Fog).ToArray());var markers=OfflineMapProjection.Shared(frame,new[]{"owner-11","owner-28"}).Select(m=>new PlayableMapMark(m.Id,m.Position,Enum.TryParse<PlayableBuildingKind>(m.Kind,out var kind)?kind:default(PlayableBuildingKind),m.State,m.Owner)).ToArray();sharedMap.SetPublic(markers,profile.ArenaHalfExtent);}
        }
        private void Capture(){if(string.IsNullOrEmpty(evidence))return;Directory.CreateDirectory(evidence);ScreenCapture.CaptureScreenshot(Path.Combine(evidence,"two-local.png"));File.WriteAllText(Path.Combine(evidence,"frame.json"),$"{{\"generation\":{session?.Frame.Generation??1},\"tick\":{session?.Frame.Tick??0},\"sequence\":{session?.Frame.Sequence??0},\"seed\":19092026,\"route\":\"source-flat-fixture\"}}");}
        private void OnDestroy(){session?.Dispose();runtime?.RequestStop();foreach(var v in views){v?.World?.Dispose();v?.Terrain?.Dispose();if(v?.Camera){OfflineCameraMirrorFeature.Unregister(v.Camera);Destroy(v.Camera.gameObject);}}sharedTerrain?.Dispose();}
    }
}
