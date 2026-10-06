using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Input;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap : MonoBehaviour
    {
        private PlayableProfile profile;
        private PlayableRuntime runtime;
        private PlayableInput input;
        private PlayableWorld world;
        private Camera cameraView;
        private UnityNavigationRouter router;
        private SharedFlowRouter explorerRouter;private double nonTankRadius;
        private NavGeometry routedGeometry;
        private NavigationRequest deferredRequest;
        private NavigationAnswer deferredAnswer;
        private readonly HashSet<int> selection=new HashSet<int>();
        private readonly Dictionary<int,GameObject> shells=new Dictionary<int,GameObject>();
        private VisualElement root,top,bottom,modal,dragBox;
        private VisualElement hudRow,buildRegion,armyRegion,commandRegion,buildActions,modalCard;
        private Label creditsLabel,selectionLabel,noticeLabel,queueLabel,modalTitle,modalCaption,objective;
        private ProgressBar progress;
        private Button shkvalButton,explorerButton,tankButton,resumeButton,modalRestartButton,modalExitButton,hudFocusButton,buildFactory,buildRefinery,buildScience,buildCenter,cancelBuilding;
        private Label siteLabel;
        private int selectedSite,selectedSlot;
        private long generation,sequence,noticeSequence,lastTick=-1;
        private bool paused,restarting,quitting;
        private string notice="Выберите площадку у штаба и постройте фабрику. Танками захватывайте форпост и шахту.";
        private string evidence;
        private StreamWriter metrics,actions;
        private float started;
        private int captures;
        private double lastMainUpdateMs,maxMainUpdateMs,maxSteadyFrameMs;
        private int longSteadyFrames;
        private PlayableSnapshot view;
        private readonly List<NavPoint> pads=new List<NavPoint>();
        private void Start()
        {
            var args=Environment.GetCommandLineArgs();
            nativeMainMenuEnabled=!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SPACEWARS_UPDATE_ENDPOINT"))||Array.IndexOf(args,"-nativeMenu")>=0;
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
            nativeMainMenuEnabled=true;
#endif
            StartGame();
        }
        private void StartGame()
        {
            try
            {
                var commandLine=Environment.GetCommandLineArgs();
                if(Array.IndexOf(commandLine,"-twoLocalHumans")>=0){gameObject.AddComponent<OfflineTwoLocalBootstrap>();enabled=false;return;}
                if(Array.IndexOf(commandLine,"-threeCrossingsEvidence")>=0){gameObject.AddComponent<ThreeCrossingsInspection>();enabled=false;return;}
                profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));
                // Render cadence belongs to this prototype profile; the domain remains 30 Hz.
                QualitySettings.vSyncCount=0;Application.targetFrameRate=profile.RenderTargetFramesPerSecond;
                AudioListener.volume=0;
                Application.runInBackground=true;
                var args=Environment.GetCommandLineArgs();for(int i=0;i+1<args.Length;i++)if(args[i]=="-playableEvidence")evidence=args[i+1];
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                responsiveUiEvidence=Array.IndexOf(args,"-responsiveUiEvidence")>=0;
                for(int i=0;i+1<args.Length;i++)if(args[i]=="-resultUiEvidence")resultUiEvidence=args[i+1];
                for(int i=0;i+1<args.Length;i++)if(args[i]=="-resultActionEvidence")resultActionEvidence=args[i+1];
                styleASceneEvidence=Array.IndexOf(args,"-styleASceneEvidence")>=0;
                if(resultActionEvidence!=null&&resultActionEvidence!="restart"&&resultActionEvidence!="exit"&&resultActionEvidence!="manual")throw new ArgumentException("Unsupported result action evidence mode.");
#endif
                if(evidence!=null){Directory.CreateDirectory(evidence);metrics=new StreamWriter(Path.Combine(evidence,"native-live.csv"));metrics.WriteLine("elapsed,generation,tick,tick_cpu_ms,tick_interval_ms,command_ms,command_backlog,nav_requests,nav_answers,frame_ms,units,credits,paused,outcome,errors,heap_bytes,nav_pending,missed_deadlines,max_tick_cpu_ms,main_update_ms,max_main_update_ms,max_steady_frame_ms,long_steady_frames,gc0_count,render_frame,buildings,pending_buildings,ready_outposts,ready_mines,income_per_second,fog_targets,fog_uploads,fog_scans,map_uploads,memory_models,own_science,active_refinery_upgrades,upgraded_refineries");actions=new StreamWriter(Path.Combine(evidence,"native-input.txt"));}
                started=Time.realtimeSinceStartup;
                CreateWorld();CreateHud();
                input=gameObject.AddComponent<PlayableInput>();
                input.Capture=()=>{if(evidence!=null){CaptureProductionSnapshot(++captures);ScreenCapture.CaptureScreenshot(Path.Combine(evidence,"native-"+captures.ToString("D2")+".png"));Record("capture "+captures);}};
                input.Select=Select;input.Order=Order;input.Stop=()=>Submit(PlayableCommandKind.Stop);input.Hold=()=>Submit(PlayableCommandKind.Hold);
                input.TogglePause=()=>Pause(!paused);input.Restart=Restart;input.FocusLost=()=>Pause(true);
                input.IsPointerOverUi=OverUi;input.IsKeyboardInUi=()=>inLobby||preparing||root?.focusController?.focusedElement is Button||root?.focusController?.focusedElement is TextField;
                input.MapAt=MapAt;input.MapSelect=MapSelect;input.MapOrder=MapOrder;input.ToggleMap=ToggleMap;input.CloseMap=CloseMap;
                input.Pan=Pan;input.Zoom=Zoom;input.Drag=DrawDrag;
                input.AttackModeChanged=active=>{notice=active?"Выберите цель атаки или точку движения с атакой.":"";};
                for(int i=0;i+1<args.Length;i++)if(args[i]=="-lobbyEvidence")lobbyEvidence=args[i+1];
                CreateLobby();
                // Existing opt-in diagnostic routes retain their direct-match entry.
                if(evidence!=null&&lobbyEvidence==null){inLobby=false;menuScreen.style.display=lobbyScreen.style.display=DisplayStyle.None;SetMatchUi(true);StartSession();}
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if(lobbyEvidence!=null&&Array.IndexOf(args,"-lobbyManual")<0)StartCoroutine(CaptureLobbyEvidence());
#endif
            }catch(Exception ex){Debug.LogException(ex);enabled=false;}
        }
        private void CreateWorld()
        {
            RenderSettings.ambientLight=new Color(.63f,.69f,.73f);
            var sun=new GameObject("Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.4f;sun.transform.rotation=Quaternion.Euler(48,-30,0);
            cameraView=new GameObject("Command camera").AddComponent<Camera>();cameraView.orthographic=true;cameraView.orthographicSize=(float)profile.CameraOrthoSize;
            cameraView.transform.position=new Vector3(0,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);cameraView.transform.LookAt(Vector3.zero);
            if(profile.AuthoredMap!=null){var start=profile.Headquarters(PlayableOwner.Player);cameraView.transform.position+=new Vector3((float)start.X,0,(float)start.Z);}
            cameraView.nearClipPlane=.1f;cameraView.farClipPlane=300;cameraView.backgroundColor=new Color(.045f,.075f,.10f);cameraView.clearFlags=CameraClearFlags.SolidColor;
            world=new PlayableWorld(transform,profile);
            // Authored map uses the same profile coordinates as the domain.
            if(profile.AuthoredMap==null)foreach(var obstacle in PlayableMap.SolidObstacles(profile))world.Obstacle(obstacle);

        }
        private void CreateHud()
        {
            var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");panel.scaleMode=PanelScaleMode.ConstantPixelSize;
            var doc=gameObject.AddComponent<UIDocument>();doc.panelSettings=panel;root=doc.rootVisualElement;root.pickingMode=PickingMode.Ignore;root.focusable=true;root.tabIndex=-1;
            root.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");root.style.fontSize=16;root.style.color=Ink;
            top=Panel();top.style.left=20;top.style.right=20;top.style.top=16;top.style.minHeight=70;top.style.flexDirection=FlexDirection.Row;top.style.flexWrap=Wrap.Wrap;top.style.alignItems=Align.Center;StyleRegion(top);root.Add(top);
            var brand=new Label("SPACEWARS");brand.style.fontSize=25;brand.style.color=Cyan;brand.style.unityFontStyleAndWeight=FontStyle.Bold;brand.style.letterSpacing=2;brand.style.marginRight=26;top.Add(brand);
            objective=new Label("ЦЕЛЬ  /  Захватите территории · уничтожьте центры противника");objective.style.flexGrow=1;objective.style.minWidth=220;objective.style.color=Ink;objective.style.whiteSpace=WhiteSpace.Normal;top.Add(objective);
            creditsLabel=new Label();creditsLabel.style.fontSize=23;creditsLabel.style.unityFontStyleAndWeight=FontStyle.Bold;creditsLabel.style.color=Cyan;creditsLabel.style.marginRight=20;top.Add(creditsLabel);
            hudFocusButton=Button("Пауза",()=>Pause(!paused));top.Add(hudFocusButton);top.Add(Button("Заново",Restart));hudLobbyButton=Button("В лобби",ReturnToLobby);hudLobbyButton.style.display=DisplayStyle.None;top.Add(hudLobbyButton);top.Add(Button("Выйти",Quit));
            bottom=Panel();bottom.style.left=20;bottom.style.right=20;bottom.style.bottom=18;bottom.style.minHeight=(float)profile.MinimapCompactSize+74;StyleRegion(bottom);root.Add(bottom);
            hudRow=new VisualElement();hudRow.style.flexDirection=FlexDirection.Row;bottom.Add(hudRow);
            CreateMaps(hudRow);
            buildRegion=new VisualElement();buildRegion.style.width=220;StyleRegion(buildRegion);buildRegion.Add(Section("СТРОИТЕЛЬСТВО"));hudRow.Add(buildRegion);
            siteLabel=new Label("Выберите свободную площадку");siteLabel.style.whiteSpace=WhiteSpace.Normal;buildRegion.Add(siteLabel);
            buildActions=new VisualElement();buildRegion.Add(buildActions);
            buildFactory=Button("⬡ Фабрика",()=>BuildSelected(PlayableBuildingKind.Factory));buildActions.Add(buildFactory);
            buildRefinery=Button("⬡ Переработка",()=>BuildSelected(PlayableBuildingKind.Refinery));buildActions.Add(buildRefinery);
            buildScience=Button("Научный центр",()=>BuildSelected(PlayableBuildingKind.ScientificCenter));buildActions.Add(buildScience);
            buildCenter=Button("⬡ Построить",()=>{var site=view.Sites.FirstOrDefault(x=>x.Site.Id==selectedSite);if(site!=null)BuildSelected(site.Site.Kind);});buildActions.Add(buildCenter);
            cancelBuilding=Button("Отменить стройку",()=>Submit(PlayableCommandKind.CancelBuilding));buildActions.Add(cancelBuilding);
            armyRegion=new VisualElement();armyRegion.style.flexGrow=1;armyRegion.style.minWidth=380;armyRegion.style.marginLeft=14;StyleRegion(armyRegion);armyRegion.Add(Section("ВЫБОР И ОЧЕРЕДИ"));hudRow.Add(armyRegion);
            selectionLabel=new Label("Выберите фабрику или танки");selectionLabel.style.fontSize=18;armyRegion.Add(selectionLabel);
            var productionActions=new VisualElement();productionActions.style.flexDirection=FlexDirection.Row;armyRegion.Add(productionActions);
            shkvalButton=Button("Шквал · "+profile.ShkvalCreditCost,()=>Submit(PlayableCommandKind.QueueShkval,new[]{productionFactory}));productionActions.Add(shkvalButton);
            explorerButton=Button("Исследователь · "+profile.ExplorerCreditCost,()=>Submit(PlayableCommandKind.QueueExplorer,new[]{productionFactory}));productionActions.Add(explorerButton);
            tankButton=Button("Танк · "+profile.TankCreditCost,()=>Submit(PlayableCommandKind.QueueTank,new[]{productionFactory}));productionActions.Add(tankButton);
            foreach(var action in new[]{shkvalButton,explorerButton,tankButton}){action.style.flexGrow=1;action.style.flexBasis=0;action.style.height=46;action.style.fontSize=12;action.style.whiteSpace=WhiteSpace.Normal;action.style.unityTextAlign=TextAnchor.MiddleCenter;}
            progress=new ProgressBar{lowValue=0,highValue=100,title="Нет производства"};progress.style.height=22;armyRegion.Add(progress);
            StyleProgress(progress);
            queueLabel=new Label();armyRegion.Add(queueLabel);CreateProductionHud(armyRegion);CreateScienceHud(armyRegion);
            commandRegion=new VisualElement();commandRegion.style.width=220;commandRegion.style.marginLeft=14;StyleRegion(commandRegion);commandRegion.Add(Section("КОМАНДЫ"));hudRow.Add(commandRegion);
            commandRegion.Add(Button("HOLD · H",()=>Submit(PlayableCommandKind.Hold)));
            commandRegion.Add(Button("Стоп · S",()=>Submit(PlayableCommandKind.Stop)));CreateBuildingLifecycleHud(commandRegion);
            var commandHint=new Label("ЛКМ/рамка · Shift добавить\nПКМ приказ · A атака\nСтрелки/колесо камера\nF6 — фокус HUD");commandHint.style.whiteSpace=WhiteSpace.Normal;commandRegion.Add(commandHint);
            noticeLabel=new Label();noticeLabel.style.marginTop=10;noticeLabel.style.color=new Color(.92f,.78f,.48f);bottom.Add(noticeLabel);
            modal=new VisualElement();modal.style.position=Position.Absolute;modal.style.left=0;modal.style.right=0;modal.style.top=0;modal.style.bottom=0;modal.style.backgroundColor=new Color(.015f,.035f,.045f,.84f);modal.style.alignItems=Align.Center;modal.style.justifyContent=Justify.Center;modal.style.display=DisplayStyle.None;root.Add(modal);
            modalCard=new VisualElement();modalCard.style.width=460;modalCard.style.maxWidth=Length.Percent(100);modalCard.style.paddingLeft=30;modalCard.style.paddingRight=30;modalCard.style.paddingTop=28;modalCard.style.paddingBottom=28;StyleRegion(modalCard);modal.Add(modalCard);
            modalCard.Add(Section("SPACEWARS  /  МАТЧ"));modalTitle=new Label();modalTitle.style.fontSize=32;modalTitle.style.unityFontStyleAndWeight=FontStyle.Bold;modalTitle.style.color=Cyan;modalTitle.style.unityTextAlign=TextAnchor.MiddleCenter;modalTitle.style.marginTop=16;modalTitle.style.marginBottom=8;modalCard.Add(modalTitle);
            modalCaption=new Label("Бой приостановлен");modalCaption.style.unityTextAlign=TextAnchor.MiddleCenter;modalCaption.style.color=Muted;modalCaption.style.marginBottom=20;modalCard.Add(modalCaption);
            resumeButton=Button("Продолжить",()=>Pause(false));modalCard.Add(resumeButton);
            modalRestartButton=Button("Начать заново",Restart);modalCard.Add(modalRestartButton);
            modalExitButton=Button("Выйти",Quit);modalCard.Add(modalExitButton);modalLobbyButton=Button("В лобби",ReturnToLobby);modalLobbyButton.style.display=DisplayStyle.None;modalCard.Add(modalLobbyButton);
            dragBox=new VisualElement();dragBox.pickingMode=PickingMode.Ignore;dragBox.style.position=Position.Absolute;dragBox.style.backgroundColor=new Color(.15f,.85f,.75f,.12f);dragBox.style.borderLeftWidth=1;dragBox.style.borderRightWidth=1;dragBox.style.borderTopWidth=1;dragBox.style.borderBottomWidth=1;dragBox.style.borderLeftColor=Color.cyan;dragBox.style.borderRightColor=Color.cyan;dragBox.style.borderTopColor=Color.cyan;dragBox.style.borderBottomColor=Color.cyan;dragBox.style.display=DisplayStyle.None;root.Add(dragBox);
            root.RegisterCallback<GeometryChangedEvent>(_=>ApplyResponsiveHud());
            root.RegisterCallback<KeyDownEvent>(evt=>
            {
                if(evt.keyCode==KeyCode.F6)ToggleHudFocus();
                else if(evt.keyCode==KeyCode.Tab&&(ModalVisible()||root.focusController?.focusedElement is Button))MoveHudFocus(evt.shiftKey);
                else if(evt.keyCode==KeyCode.Escape&&ModalVisible())
                {
                    if(paused&&view?.Outcome==PlayableMatchOutcome.Playing)Pause(false);
                    else SyncModalFocus();
                }
                else return;
                evt.StopImmediatePropagation();
            });
            root.Focus();
        }
        private static void StyleProgress(ProgressBar bar)
        {
            // Shared established HUD palette and height, not a new gameplay tunable.
            bar.style.height=22;
            bar.Q<VisualElement>(className:"unity-progress-bar__background").style.backgroundColor=new Color(.10f,.17f,.19f);
            bar.Q<VisualElement>(className:"unity-progress-bar__progress").style.backgroundColor=new Color(.10f,.39f,.38f);
            bar.Q<Label>().style.color=Color.white;
        }
        private VisualElement Panel(){var p=new VisualElement();p.style.position=Position.Absolute;p.style.backgroundColor=PanelColor;p.style.paddingLeft=16;p.style.paddingRight=16;p.style.paddingTop=10;p.style.paddingBottom=10;return p;}
        private Button Button(string text,Action action){var b=new Button(()=>{action();if(ModalVisible())SyncModalFocus();else root.Focus();}){text=text,focusable=true};StyleAction(b);return b;}
        private bool OverUi(Vector2 p){if(root?.panel==null)return false;var q=RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(p.x,Screen.height-p.y));return top.worldBound.Contains(q)||bottom.worldBound.Contains(q)||(modal.style.display==DisplayStyle.Flex&&modal.worldBound.Contains(q));}
        private void DrawDrag(Vector2 a,Vector2 b,bool active){if(root?.panel==null)return;dragBox.style.display=active?DisplayStyle.Flex:DisplayStyle.None;var x=RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(a.x,Screen.height-a.y));var y=RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(b.x,Screen.height-b.y));dragBox.style.left=Mathf.Min(x.x,y.x);dragBox.style.top=Mathf.Min(x.y,y.y);dragBox.style.width=Mathf.Abs(x.x-y.x);dragBox.style.height=Mathf.Abs(x.y-y.y);}
        private void StartSession(){ClearArtilleryEffects();confirmSaleBuilding=0;router?.Dispose();explorerRouter=null;router=null;routedGeometry=null;deferredRequest=null;deferredAnswer=null;world.Clear();CloseMap();input?.ClearMode();foreach(var shell in shells.Values)Destroy(shell);shells.Clear();runtime=matchSetup==null?PlayableRuntime.CreateHumanMatch(profile,++generation,19092026):PlayableRuntime.CreateLobbyMatch(profile,++generation,matchSetup,startPaused:preparing);sequence=noticeSequence=0;selection.Clear();selectedSite=selectedSlot=0;paused=false;restarting=false;modalWasVisible=false;root.Focus();notice="Выберите площадку у штаба и постройте фабрику. Танками захватывайте форпост и шахту.";Record("start");}
        private void Select(Vector2 from,Vector2 to,bool shift)
        {
            if(view==null)return;confirmSaleBuilding=0;root.Focus();if(!shift)selection.Clear();selectedSite=selectedSlot=0;bool box=(to-from).magnitude>profile.SelectionDragPixels;
            if(box){foreach(var e in view.Entities)if(e.Owner==PlayableOwner.Player){var p=cameraView.WorldToScreenPoint(world.Point(e.Position));if(p.z>0&&p.x>=Mathf.Min(from.x,to.x)&&p.x<=Mathf.Max(from.x,to.x)&&p.y>=Mathf.Min(from.y,to.y)&&p.y<=Mathf.Max(from.y,to.y))selection.Add(e.Id);}}
            else{if(PickPad(Ground(to))){Record("site="+selectedSite+" slot="+selectedSlot);return;}int id=Pick(to,true);if(id!=0){if(shift&&!selection.Add(id))selection.Remove(id);else selection.Add(id);}}
            Record("select screen="+to+" resolution="+Screen.width+"x"+Screen.height+" ground="+Ground(to).X+","+Ground(to).Z+" ids="+string.Join(",",selection));notice=selection.Count==0?"Выберите танки или фабрику.":"ПКМ — движение, атака или точка сбора.";
        }
        private int Pick(Vector2 point,bool onlyOwn)
        {
            int picked=0;float best=float.MaxValue;
            foreach(var e in view.Entities)if(!onlyOwn||e.Owner==PlayableOwner.Player)
            {
                var center=cameraView.WorldToScreenPoint(world.Point(e.Position)+Vector3.up*.5f);
                var rim=cameraView.WorldToScreenPoint(world.Point(e.Position)+Vector3.right*(float)(PlayableUnitRules.Radius(profile,e.Kind)*profile.TargetPickRadiusMultiplier)+Vector3.up*.5f);
                float d=Vector2.Distance(point,center);
                if(center.z>0&&d<Vector2.Distance(center,rim)&&d<best){best=d;picked=e.Id;}
            }
            foreach(var b in view.Buildings)if(b.Phase!=ConstructionPhase.Pending&&(!onlyOwn||b.Owner==PlayableOwner.Player))
            {
                var center=cameraView.WorldToScreenPoint(world.Point(b.Position)+Vector3.up);
                var rim=cameraView.WorldToScreenPoint(world.Point(b.Position)+Vector3.right*(float)profile.BuildingPickRadius+Vector3.up);
                float d=Vector2.Distance(point,center);
                if(center.z>0&&d<Vector2.Distance(center,rim)&&d<best){best=d;picked=b.Id;}
            }
            return picked;
        }
        private NavPoint Ground(Vector2 p)
        {
            var ray=cameraView.ScreenPointToRay(p);if(profile.AuthoredMap==null){if(new Plane(Vector3.up,Vector3.zero).Raycast(ray,out float distance)){var v=ray.GetPoint(distance);return new NavPoint(v.x,v.z);}}
            else{double nearest=double.PositiveInfinity;NavPoint hit=default(NavPoint);foreach(var support in profile.AuthoredMap.Supports){var gradient=support.Gradient;double denominator=ray.direction.y-gradient.X*ray.direction.x-gradient.Z*ray.direction.z;if(System.Math.Abs(denominator)<1e-9)continue;double t=(support.HeightAt(new NavPoint(ray.origin.x,ray.origin.z))-ray.origin.y)/denominator;var at=ray.GetPoint((float)t);var point=new NavPoint(at.x,at.z);if(t>=0&&t<nearest&&support.Contains(point)){nearest=t;hit=point;}}if(!double.IsInfinity(nearest))return hit;}
            // Camera footprint and minimap panning require finite projection even outside support.
            if(new Plane(Vector3.up,Vector3.zero).Raycast(ray,out float fallback)){var v=ray.GetPoint(fallback);return new NavPoint(v.x,v.z);}
            return new NavPoint(double.NaN,double.NaN);
        }
        private void Order(Vector2 p,bool attackMode)
        {
            var target=Ground(p);int id=Pick(p,false);bool enemy=view.Entities.Any(e=>e.Id==id&&e.Owner==PlayableOwner.Enemy)||view.Buildings.Any(b=>b.Id==id&&b.Owner==PlayableOwner.Enemy);
            if(selection.Count==1&&view.Buildings.Any(b=>selection.Contains(b.Id)&&b.Kind==PlayableBuildingKind.Factory))Submit(PlayableCommandKind.SetRally,null,target);
            else Submit(enemy?PlayableCommandKind.Attack:attackMode?PlayableCommandKind.AttackMove:PlayableCommandKind.Move,null,target,enemy?id:0);
        }
        private void Submit(PlayableCommandKind kind,int[] ids=null,NavPoint target=default(NavPoint),int targetId=0,long productionOrderId=0,PlayableEntityKind unitKind=PlayableEntityKind.Tank,PlayableResearchKind researchKind=PlayableResearchKind.TankChassis)
        {
            if(runtime==null||paused||restarting||quitting)return;
            var result=runtime.TrySubmit(new PlayableCommand(generation,++sequence,"player-1",kind,ids??selection.ToArray(),target,targetId:targetId,productionOrderId:productionOrderId,unitKind:unitKind,researchKind:researchKind));
            noticeSequence=sequence;notice=result.Accepted?"Приказ отправлен":"Не удалось отправить приказ: "+Friendly(result.Status);
            Record(kind+" ids="+string.Join(",",ids??selection.ToArray())+" target="+target.X+","+target.Z+" order="+productionOrderId+" admission="+result.Status);
        }
        private string Friendly(PlayableCommandStatus status){switch(status){case PlayableCommandStatus.Accepted:return "Проверяем точку сбора";case PlayableCommandStatus.Cancelled:return "Проверка точки отменена";case PlayableCommandStatus.Applied:return "Приказ выполнен";case PlayableCommandStatus.InsufficientCredits:return "Недостаточно кредитов";case PlayableCommandStatus.OccupiedPad:return "Площадка занята";case PlayableCommandStatus.InvalidEntity:return "Выберите готовую фабрику или свои танки";case PlayableCommandStatus.InvalidTarget:return "Недоступная цель";case PlayableCommandStatus.Overflow:return "Слишком много приказов, повторите";default:return "Приказ отклонён";}}
        private void Pause(bool value){if(runtime==null||restarting||quitting||(!value&&matchSetup!=null&&!devicesReady))return;confirmSaleBuilding=0;paused=value;runtime.RequestPause(value);input?.ClearMode();CloseMap();Record("pause="+value);}
        private void Restart(){if(runtime==null||restarting||quitting)return;Record("restart requested");restarting=true;runtime.RequestStop();}
        private void Quit(){if(quitting)return;Record("exit requested");quitting=true;runtime?.RequestStop();}
        private void Pan(Vector2 axis){cameraView.transform.position+=new Vector3(axis.x,0,axis.y)*(float)profile.CameraPanSpeed*Time.unscaledDeltaTime;Record("camera pan");}
        private void Zoom(float delta){cameraView.orthographicSize=Mathf.Clamp(cameraView.orthographicSize-delta*(float)profile.CameraZoomSpeed,(float)profile.CameraMinZoom,(float)profile.CameraMaxZoom);Record("camera zoom");}
        private void Update()
        {
            long begin=System.Diagnostics.Stopwatch.GetTimestamp();
            // Diagnostic thresholds: discard startup's first five seconds and count >100ms frames.
            // These define evidence buckets, not gameplay or presentation tuning.
            if(Time.realtimeSinceStartup-started>=5){double ms=Time.unscaledDeltaTime*1000;maxSteadyFrameMs=Math.Max(maxSteadyFrameMs,ms);if(ms>100)longSteadyFrames++;}
            try{UpdateFrame();}
            finally{lastMainUpdateMs=(System.Diagnostics.Stopwatch.GetTimestamp()-begin)*1000d/System.Diagnostics.Stopwatch.Frequency;maxMainUpdateMs=Math.Max(maxMainUpdateMs,lastMainUpdateMs);}
        }
        private void UpdateFrame()
        {
            UpdateLobby();
            if(returningToLobby&&(runtime==null||runtime.IsStopped)){returningToLobby=false;runtime=null;view=null;world.Clear();selection.Clear();CloseMap();ShowLobby();return;}
            if(inLobby&&!preparing)return;
            if(runtime==null)return;
            if(runtime.IsStopped){if(quitting){Flush();Application.Quit();return;}if(restarting)StartSession();}
            view=runtime.Latest;if(view==null)return;
            input.WorldInputEnabled=!inLobby&&!preparing&&!returningToLobby&&devicesReady&&!paused&&!restarting&&!quitting&&view.Outcome==PlayableMatchOutcome.Playing&&string.IsNullOrEmpty(view.Failure);
            ServiceRoutes();Render();UpdateHud();UpdateMaps();UpdateLifecycleMarkers();
            foreach(var receipt in runtime.DrainReceipts()){if(receipt.Sequence>=noticeSequence){noticeSequence=receipt.Sequence;notice=Friendly(receipt.Status);}Record("receipt "+receipt.Sequence+" "+receipt.Status+" latency_ms="+receipt.LatencyMilliseconds);}
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            DriveResponsiveUiEvidence();
            DriveResultUiEvidence();
            DrivePrototypeResearchEvidence();
            DriveStyleASceneEvidence();
#endif
            if(view.Tick!=lastTick){lastTick=view.Tick;metrics?.WriteLine(string.Join(",",(Time.realtimeSinceStartup-started).ToString("F3",System.Globalization.CultureInfo.InvariantCulture),generation,view.Tick,view.Metrics.TickCpuMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),view.Metrics.TickIntervalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),view.Metrics.CommandLatencyMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),view.Metrics.CommandBacklog,runtime.Requests.Count,runtime.Answers.Count,Time.unscaledDeltaTime*1000,view.Entities.Count,view.Credits,paused,view.Outcome,view.Metrics.Errors,GC.GetTotalMemory(false),view.Metrics.NavigationPending,view.Metrics.MissedDeadlines,view.Metrics.MaximumTickCpu,lastMainUpdateMs,maxMainUpdateMs,maxSteadyFrameMs,longSteadyFrames,GC.CollectionCount(0),Time.frameCount,view.Buildings.Count,view.Buildings.Count(b=>b.Phase==ConstructionPhase.Pending),view.Buildings.Count(b=>b.Kind==PlayableBuildingKind.Outpost&&b.Phase==ConstructionPhase.Ready),view.Buildings.Count(b=>b.Kind==PlayableBuildingKind.Mine&&b.Phase==ConstructionPhase.Ready),view.IncomePerSecond,world.Fog.TargetBuilds,world.Fog.Uploads,world.Fog.ScannedFrames,mapTerrain.Uploads,world.MemoryCount,view.Buildings.Count(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.ScientificCenter&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.Lifecycle?.Selling!=true),view.Buildings.Count(b=>b.PrivateState?.Upgrade?.Active==true),view.Buildings.Count(b=>b.Owner==PlayableOwner.Player&&b.RefineryUpgraded)));if(view.Tick%30==0)Flush();}
        }
        private void ServiceRoutes()
        {
            if(deferredAnswer!=null){if(!runtime.Answers.TryEnqueue(deferredAnswer))return;deferredAnswer=null;}
            for(int i=0;i<profile.NavigationRequestsPerFrame;i++)
            {
                NavigationRequest request=deferredRequest;
                if(request==null&&!runtime.Requests.TryDequeue(out request))break;
                deferredRequest=null;
                if(request.Session!=generation)continue;
                var current=runtime.NavigationGeometry;
                if(!ReferenceEquals(request.BaseGeometry,current))
                {
                    if(request.Geometry.Revision<current.Revision)continue;
                    deferredRequest=request;return; // Worker may enqueue before publishing its newer immutable view.
                }
                if(!ReferenceEquals(request.Geometry,request.BaseGeometry)){
                    var heldRoute=new SharedFlowRouter(request.Geometry,request.Profile).FindPath(request.Start,request.Goal);
                    var heldAnswer=new NavigationAnswer(request,heldRoute);
                    if(!runtime.Answers.TryEnqueue(heldAnswer)){deferredAnswer=heldAnswer;return;}
                    continue;
                }
                if(!ReferenceEquals(routedGeometry,request.Geometry)){router?.Dispose();explorerRouter=null;router=new UnityNavigationRouter(request.Geometry,profile.Navigation);routedGeometry=request.Geometry;}
                NavPoint[] route;
                if(request.Profile.Radius!=profile.Navigation.Radius)
                {
                    // One native NavMesh bake remains authoritative for tank route proposals.
                    // Existing pure router supplies smaller-footprint routes; two overlapping
                    // same-agent-type NavMeshes would let tank queries pick scout clearances.
                    if(explorerRouter==null||nonTankRadius!=request.Profile.Radius){explorerRouter=new SharedFlowRouter(request.Geometry,request.Profile);nonTankRadius=request.Profile.Radius;}
                    route=explorerRouter.FindPath(request.Start,request.Goal);
                }
                else route=router.FindPath(request.Start,request.Goal);
                var answer=new NavigationAnswer(request,route);
                if(!runtime.Answers.TryEnqueue(answer)){deferredAnswer=answer;return;}
            }
        }
        private void Render()
        {
            world.Fog.Update(view.Vision,Time.unscaledDeltaTime);world.RenderMemories(view);world.RenderSites(view);
            var alive=new HashSet<int>();
            foreach(var e in view.Entities){alive.Add(e.Id);if(!world.Actors.TryGetValue(e.Id,out var a))a=world.Tank(e.Id,e.Owner==PlayableOwner.Player,e.Kind);world.UpdateResearchModel(a,e);a.Root.position=world.Point(e.Position);var slope=profile.AuthoredMap?.SurfaceGradient(e.Position)??default(NavPoint);a.Root.rotation=Quaternion.FromToRotation(Vector3.up,new Vector3((float)-slope.X,1,(float)-slope.Z).normalized);a.Hull.localRotation=Quaternion.Euler(0,90-(float)e.HullHeading*Mathf.Rad2Deg,0);a.Turret.localRotation=Quaternion.Euler(0,(float)(e.HullHeading-e.TurretHeading)*Mathf.Rad2Deg,0);a.Selection.SetActive(selection.Contains(e.Id));if(matchSetup!=null)PlayableWorld.PaintOwner(a,LobbyPaint(e.Owner));PlayableWorld.UpdateHealth(a,cameraView,(float)e.Health/PlayableUnitRules.Health(profile,e.Kind));}
            foreach(var b in view.Buildings){alive.Add(b.Id);if(b.Phase==ConstructionPhase.Pending)continue;if(!world.Actors.TryGetValue(b.Id,out var a))a=world.Building(b.Id,b.Kind.ToString(),b.Owner==PlayableOwner.Player,b.RefineryUpgraded);world.UpdateRefineryModel(a,b,view.Tick);a.Root.position=world.Point(b.Position);a.Root.rotation=Quaternion.Euler(0,-(float)b.Heading*Mathf.Rad2Deg,0);a.Root.localScale=new Vector3(1,Mathf.Lerp(.2f,1,(float)b.Progress),1);a.Selection.SetActive(selection.Contains(b.Id));if(matchSetup!=null)PlayableWorld.PaintOwner(a,LobbyPaint(b.Owner));int max=TerritoryRules.Health(profile,b.Kind);PlayableWorld.UpdateHealth(a,cameraView,(float)b.Health/max);}
            foreach(var id in world.Actors.Keys.ToArray())if(!alive.Contains(id)){world.Remove(id);selection.Remove(id);}
            RenderProjectiles();
        }
        private void UpdateHud()
        {
            if(matchSetup!=null)objective.text=matchSetup.MatchHumanName+" · Команда "+matchSetup.HumanTeam+"  /  "+matchSetup.MatchAiName+" · Команда "+matchSetup.AiTeam;
            creditsLabel.text=view.Credits+"  (+"+view.IncomePerSecond.ToString("0.#")+"/с)";
            UpdateSiteHud();
            var factory=view.Buildings.FirstOrDefault(b=>selection.Contains(b.Id)&&b.Kind==PlayableBuildingKind.Factory&&b.Owner==PlayableOwner.Player);
            var building=view.Buildings.FirstOrDefault(b=>selection.Contains(b.Id));
            tankButton.SetEnabled(factory!=null&&factory.Progress>=1&&factory.PrivateState?.Lifecycle?.Selling!=true&&view.Outcome==PlayableMatchOutcome.Playing&&!paused);
            selectionLabel.text=building!=null?BuildingName(building.Kind)+" · HP "+building.Health:"ЮНИТЫ · "+selection.Count+(view.Entities.Any(e=>selection.Contains(e.Id)&&e.Held)?" · HOLD":"");
            double value=building!=null&&building.Progress<1?building.Progress:factory?.ProductionProgress??0;
            progress.value=(float)Math.Min(100,value*100);progress.title=building!=null&&building.Phase==ConstructionPhase.Pending?(building.BlockedReason??"Ожидает начала"):building!=null&&building.Progress<1?"Строительство · "+(int)(value*100)+"%":factory!=null&&factory.QueueCount>0?"Танк · "+(int)Math.Min(100,value*100)+"%":"Нет активного производства";
            progress.style.display=building!=null&&(building.Progress<1||factory!=null)?DisplayStyle.Flex:DisplayStyle.None;
            UpdateProductionHud(factory);UpdateBuildingLifecycleHud(building);UpdateScienceHud(building);
            noticeLabel.text=notice;modal.style.display=paused||view.Outcome!=PlayableMatchOutcome.Playing||restarting||!string.IsNullOrEmpty(view.Failure)?DisplayStyle.Flex:DisplayStyle.None;
            resumeButton.style.display=view.Outcome==PlayableMatchOutcome.Playing&&!restarting?DisplayStyle.Flex:DisplayStyle.None;
            modalTitle.text=!string.IsNullOrEmpty(view.Failure)?"ОШИБКА СИМУЛЯЦИИ":restarting?"НОВЫЙ БОЙ…":view.Outcome==PlayableMatchOutcome.PlayerWon?"ПОБЕДА":view.Outcome==PlayableMatchOutcome.PlayerLost?"ЦЕНТРЫ ПОТЕРЯНЫ":"ПАУЗА";
            modalCaption.text=(!string.IsNullOrEmpty(view.Failure)?"Матч остановлен":restarting?"Подготовка нового матча":view.Outcome==PlayableMatchOutcome.PlayerWon?"Противник побеждён":view.Outcome==PlayableMatchOutcome.PlayerLost?"Ваши центры уничтожены":"Бой приостановлен")+"\nSeed "+view.Seed+" · "+view.ProfileId+"@"+view.ProfileRevision;modalCaption.style.whiteSpace=WhiteSpace.Normal;
            if(matchSetup!=null)modalCaption.text+="\n"+matchSetup.MatchHumanName+"  /  "+matchSetup.MatchAiName;
            modalTitle.style.color=!string.IsNullOrEmpty(view.Failure)||view.Outcome==PlayableMatchOutcome.PlayerLost?Danger:view.Outcome==PlayableMatchOutcome.PlayerWon?Cyan:Ink;
            SyncModalFocus();
            UpdateResponsiveVisibility();
        }
        private void Record(string message){actions?.WriteLine((Time.realtimeSinceStartup-started).ToString("F3")+" generation="+generation+" tick="+(view?.Tick??0)+" "+message);actions?.Flush();}
        private void Flush(){metrics?.Flush();actions?.Flush();}
        private void OnApplicationQuit(){runtime?.RequestStop();Flush();}
        private void OnDestroy(){ClearArtilleryEffects();if(artilleryTransparent)Destroy(artilleryTransparent);lobbyTerrain?.Dispose();mapTerrain?.Dispose();world?.Dispose();runtime?.RequestStop();router?.Dispose();metrics?.Dispose();actions?.Dispose();}
    }
}
