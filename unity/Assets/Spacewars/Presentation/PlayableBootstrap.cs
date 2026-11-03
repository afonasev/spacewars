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
        private static readonly Unity.Profiling.ProfilerMarker RouteServiceMarker=new Unity.Profiling.ProfilerMarker("Spacewars.Main.RouteService");
        private static readonly Unity.Profiling.ProfilerMarker RenderMarker=new Unity.Profiling.ProfilerMarker("Spacewars.Main.Render");
        private static readonly Unity.Profiling.ProfilerMarker FogMarker=new Unity.Profiling.ProfilerMarker("Spacewars.Main.Fog");
        private static readonly Unity.Profiling.ProfilerMarker HudMarker=new Unity.Profiling.ProfilerMarker("Spacewars.Main.Hud");
        private static readonly Unity.Profiling.ProfilerMarker MapsMarker=new Unity.Profiling.ProfilerMarker("Spacewars.Main.Maps");
        private readonly CertifiedRouteLane routeLane=new CertifiedRouteLane();
        private PlayableProfile profile;
        internal static PlayableProfile LoadNormalProfile()=>PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));
        private PlayableRuntime runtime;
        private long loggedFailureGeneration;
        private PlayableInput input;
        private readonly PlayableOrderMarkers orderMarkers=new PlayableOrderMarkers();
        private PlayableOrderMarkerLayer orderMarkerLayer;
        private void SelectionMarkerEvent(){selectionRosterExpanded=false;EndSelectionRosterFocus();orderMarkers.Selected(view,selection,Time.unscaledTimeAsDouble);if(view!=null&&!paused&&!inLobby)SharedAudio?.Selection(view.OwnerId,string.Join(",",selection.OrderBy(id=>id))+":"+selectedSite+":"+selectedSlot+":"+primarySelection);}
        private void UpdateOrderMarkers(PlayableCommandReceipt[] receipts){orderMarkers.Observe(view,receipts,Time.unscaledTimeAsDouble);var marks=orderMarkers.Visible(view,selection,Time.unscaledTimeAsDouble);orderMarkerLayer?.Set(marks);compactMap?.SetOrderMarkers(marks);tacticalMap?.SetOrderMarkers(marks);}
        private PlayableWorld world;
        private Camera cameraView;
        private UnityHostRouteService routeService;
        private readonly HashSet<int> selection=new HashSet<int>();
        private readonly Dictionary<int,GameObject> shells=new Dictionary<int,GameObject>();
        private VisualElement root,top,bottom,modal,dragBox;
        private VisualElement hudRow,buildRegion,armyRegion,commandRegion,buildActions,modalCard;
        private Label creditsLabel,selectionLabel,noticeLabel,queueLabel,modalTitle,modalCaption,objective;
        private ProgressBar progress;
        private Button hawkButton,shkvalButton,explorerButton,tankButton,resumeButton,modalRestartButton,modalExitButton,provingStartButton,provingLabButton,hudFocusButton,buildGauss,buildRepairTower,buildTurret,buildHangar,buildFactory,buildRefinery,buildScience,buildCenter,cancelBuilding;
        private Label siteLabel;
        private int selectedSite,selectedSlot;
        private long generation,sequence,noticeSequence,lastTick=-1;
        private bool paused,menuPaused,restarting,quitting;
        private bool routePerformanceEvidence;
        private string notice="";
        private string evidence;
        private StreamWriter metrics,actions;
        private NativeRouteFrameTelemetry routeFrames;
        private NativeFrameTimingProbe preciseFrames;
        private float started;
        private int captures;
        private double lastMainUpdateMs,maxMainUpdateMs,maxSteadyFrameMs;
        private double routeServiceFrameMs,renderFrameMs,hudFrameMs,mapsFrameMs,audioFrameMs;
        private double RouteStageElapsed(ref long began)
        {
            if(began==0)return 0;
            long now=System.Diagnostics.Stopwatch.GetTimestamp();double elapsed=(now-began)*1000d/System.Diagnostics.Stopwatch.Frequency;began=now;return elapsed;
        }
        private int longSteadyFrames;
        private PlayableSnapshot view;
        private readonly List<NavPoint> pads=new List<NavPoint>();
        private GameObject startupLoadingHost;
        private PanelSettings startupLoadingPanel;
        private bool initializingMenu;
        private System.Collections.IEnumerator Start()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            InitializeQaDeadline();
#endif
            Runtime.NavigationArrivalDiagnostics.StartIfRequested();Runtime.OriginalGroupDiagnostics.StartIfRequested();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-tankDistantLodEvidence")>=0){gameObject.AddComponent<TankDistantEvidence>();enabled=false;yield break;}
#endif
            initializingMenu=true;nativeMainMenuEnabled=true;
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
            nativeMainMenuEnabled=true;
#endif
            startupLoadingHost=new GameObject("Menu loading");startupLoadingHost.transform.SetParent(transform);
            startupLoadingPanel=ScriptableObject.CreateInstance<PanelSettings>();
            startupLoadingPanel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");startupLoadingPanel.sortingOrder=100;
            var doc=startupLoadingHost.AddComponent<UIDocument>();doc.panelSettings=startupLoadingPanel;
            OrbitalTheme.ConfigurePanel(startupLoadingPanel,doc.rootVisualElement);
            var loading=OrbitalTheme.Loading(doc.rootVisualElement,"Загрузка меню…");
            // Let UI Toolkit lay out and paint before synchronous resource initialization.
            yield return null;yield return null;
            bool failed=false;
            yield return LoadingPreparation.Run(InitializeGame(loading),ex=>{Debug.LogException(ex);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if(qaDeadlinePath!=null)BeginQaFinish("FAIL initialization "+ex,true);
#endif
            failed=true;loading.Q<Label>("loading-status").text="Не удалось загрузить меню. Перезапустите игру.";});
            if(failed)yield break;
            initializingMenu=false;
            yield return null;yield return null;
            if(root==null&&GetComponent<OfflineTwoLocalBootstrap>()==null&&GetComponent<FoundryGreybox>()==null&&GetComponent<ThreeCrossingsInspection>()==null){loading.Q<Label>("loading-status").text="Не удалось загрузить меню. Перезапустите игру.";yield break;}
            Destroy(startupLoadingHost);Destroy(startupLoadingPanel);startupLoadingHost=null;startupLoadingPanel=null;
        }
        internal static bool BypassTwoLocalOnce;
        private System.Collections.IEnumerator InitializeGame(VisualElement loading)
        {
                localInputFocused=Application.isFocused;var commandLine=Environment.GetCommandLineArgs();
                #if DEVELOPMENT_BUILD || UNITY_EDITOR
                if(Array.IndexOf(commandLine,"-repairReview")>=0){gameObject.AddComponent<RepairTowerReviewCapture>();enabled=false;yield break;}
                if(Array.IndexOf(commandLine,"-sentryReview")>=0){gameObject.AddComponent<SentryTurretReviewCapture>();enabled=false;yield break;}
                if(Array.IndexOf(commandLine,"-tankComparison")>=0){gameObject.AddComponent<TankComparisonEvidence>();enabled=false;yield break;}
                if(Array.IndexOf(commandLine,"-groundTrailVideo")>=0){gameObject.AddComponent<GroundTrailVideoEvidence>();enabled=false;yield break;}
#endif
                if(Array.IndexOf(commandLine,"-foundryGreybox")>=0){gameObject.AddComponent<FoundryGreybox>();enabled=false;yield break;}
                if(Array.IndexOf(commandLine,"-twoLocalHumans")>=0&&!BypassTwoLocalOnce){gameObject.AddComponent<OfflineTwoLocalBootstrap>();enabled=false;yield break;}
                BypassTwoLocalOnce=false;
                if(Array.IndexOf(commandLine,"-threeCrossingsEvidence")>=0){gameObject.AddComponent<ThreeCrossingsInspection>();enabled=false;yield break;}
                profile=LoadNormalProfile();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if(Array.IndexOf(commandLine,"-orbitalHudEvidence")>=0)
                {
                    // Diagnostic economy only: expose occupied queues promptly; never a shipping default.
                    var fixture=profile.CopyData();fixture.startingCredits=10000;fixture.factoryBuildSeconds=1;fixture.scienceBuildSeconds=1;
                    profile=PlayableProfile.Create(fixture,profile.AuthoredMap,"UI review");
                }
#endif
                // Render cadence belongs to this prototype profile; the domain remains 30 Hz.
                NativeDisplaySettings.ApplyFrameRate(profile.RenderTargetFramesPerSecond);
                if(!NativeSilentStartup.IsSilent)AudioListener.volume=NativeUserSettings.Volume;
                Application.runInBackground=true;
                var args=Environment.GetCommandLineArgs();for(int i=0;i+1<args.Length;i++)if(args[i]=="-playableEvidence")evidence=args[i+1];
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                battleBenchmark=Array.IndexOf(args,"-battleBenchmark")>=0;
                responsiveUiEvidence=Array.IndexOf(args,"-responsiveUiEvidence")>=0;
                battleHudUnattended=battleBenchmark||Array.IndexOf(args,"-provingGroundEvidence")>=0||Array.IndexOf(args,"-fleetFinishEvidence")>=0||Array.IndexOf(args,"-battleHudEvidence")>=0||Array.IndexOf(args,"-hawkFlightEvidence")>=0||Array.IndexOf(args,"-hawkCombatEvidence")>=0;
                for(int i=0;i+1<args.Length;i++)if(args[i]=="-resultUiEvidence")resultUiEvidence=args[i+1];
                for(int i=0;i+1<args.Length;i++)if(args[i]=="-resultActionEvidence")resultActionEvidence=args[i+1];
                styleASceneEvidence=Array.IndexOf(args,"-styleASceneEvidence")>=0;
                routePerformanceEvidence=Array.IndexOf(args,"-routePerformanceEvidence")>=0;
                if(routePerformanceEvidence&&evidence==null)throw new ArgumentException("Route performance evidence requires -playableEvidence directory.");
                if(resultActionEvidence!=null&&resultActionEvidence!="restart"&&resultActionEvidence!="exit"&&resultActionEvidence!="manual")throw new ArgumentException("Unsupported result action evidence mode.");
#endif
                if(evidence!=null){Directory.CreateDirectory(evidence);metrics=new StreamWriter(Path.Combine(evidence,"native-live.csv"));metrics.WriteLine("elapsed,generation,tick,tick_cpu_ms,tick_interval_ms,command_ms,command_backlog,nav_requests,nav_answers,frame_ms,units,credits,paused,outcome,errors,heap_bytes,nav_pending,missed_deadlines,max_tick_cpu_ms,main_update_ms,max_main_update_ms,max_steady_frame_ms,long_steady_frames,gc0_count,render_frame,buildings,pending_buildings,ready_outposts,ready_mines,income_per_second,fog_targets,fog_uploads,fog_scans,map_uploads,memory_models,own_science,active_refinery_upgrades,upgraded_refineries");actions=new StreamWriter(Path.Combine(evidence,"native-input.txt"));routeFrames=new NativeRouteFrameTelemetry(Path.Combine(evidence,"native-route-frames.csv"));if(PerformanceProfile.Enabled||Environment.GetEnvironmentVariable("SPACEWARS_FRAME_TIMING_ONLY")=="1"||Environment.GetEnvironmentVariable("SPACEWARS_FRAME_TIMING_LIGHT")=="1")preciseFrames=new NativeFrameTimingProbe(evidence,Environment.GetEnvironmentVariable("SPACEWARS_FRAME_TIMING_LIGHT")=="1");}
                started=Time.realtimeSinceStartup;
                InitializeMusic();InitializeGameplayAudio();
                // The menu is opaque: do not build an invisible battlefield only to discard it at launch.
                CreateWorldCore(true);
                loading.Q<Label>("loading-status").text="Подготовка интерфейса…";yield return null;
                mapTerrain=new PlayableMapTerrain(profile,staged:true);yield return mapTerrain.Preparation;
                CreateHud();CreateSpectatorHud();yield return null;
                input=gameObject.AddComponent<PlayableInput>();
                input.Capture=()=>{if(evidence!=null){CaptureProductionSnapshot(++captures);ScreenCapture.CaptureScreenshot(Path.Combine(evidence,"native-"+captures.ToString("D2")+".png"));Record("capture "+captures);}};
                input.Select=Select;input.Order=Order;input.Stop=()=>Submit(PlayableCommandKind.Stop);input.Hold=()=>Submit(PlayableCommandKind.Hold);
                input.CancelContext=()=>{if(CancelScanTarget())return true;if(CancelTeamSignal())return true;if(mapOpen){CloseMap();return true;}return CloseBattleContext();};input.TogglePause=()=>Pause(!menuPaused);input.Restart=Restart;input.FocusLost=()=>{if(!battleHudUnattended&&!routePerformanceEvidence)Pause(true);};
                input.IsPointerOverUi=OverUi;input.IsKeyboardInUi=()=>MenuOwnsInput||spectatorMode&&root?.focusController?.focusedElement is VisualElement focus&&focus!=root||!ProvingPreparation&&(root?.focusController?.focusedElement is Button||root?.focusController?.focusedElement is TextField);
                input.MapAt=MapAt;input.MapSelect=MapSelect;input.MapPan=PanMap;input.MapOrder=MapOrder;input.ToggleMap=ToggleMap;input.CloseMap=CloseMap;
                input.Pan=Pan;input.Zoom=Zoom;input.Drag=DrawDrag;
                BindKeyboardCommands();BindChatInput();
                input.AttackModeChanged=active=>{commandCursor?.Set(false);notice="";};
                for(int i=0;i+1<args.Length;i++)if(args[i]=="-lobbyEvidence")lobbyEvidence=args[i+1];
                for(int i=0;i+1<args.Length;i++)if(args[i]=="-matchSeed"){lobbySetup.ExplicitSeed=int.Parse(args[i+1],System.Globalization.CultureInfo.InvariantCulture);lobbySetup.HasExplicitSeed=true;}
                lobbyTerrain=new PlayableMapTerrain(profile,staged:true);yield return lobbyTerrain.Preparation;
                CreateLobby();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                for(int i=0;i+1<args.Length;i++)if(args[i]=="-orbitalUiEvidence")orbitalEvidence=args[i+1];
                if(orbitalEvidence!=null)StartCoroutine(CaptureOrbitalUiEvidence());
                if(battleBenchmark)StartCoroutine(LoadingPreparation.Run(RunBattleBenchmark(args),error=>{Debug.LogException(error);runtime?.RequestStop();Application.Quit(1);}));
#endif
                // Evidence must enter through the current menu/lobby lifecycle too.
                if(evidence!=null&&Array.IndexOf(args,"-battleBenchmark")<0&&lobbyEvidence==null&&Array.IndexOf(args,"-provingGroundEvidence")<0&&Array.IndexOf(args,"-fleetFinishEvidence")<0)StartCoroutine(StartEvidenceMatch());
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if(evidence!=null&&Array.IndexOf(args,"-fleetFinishEvidence")>=0)StartCoroutine(LoadingPreparation.Run(CaptureFleetFinishEvidence(),e=>{File.WriteAllText(Path.Combine(evidence,"fleet-result.txt"),"FAIL "+e);Debug.LogException(e);runtime?.RequestStop();Flush();Application.Quit(1);}));
                if(evidence!=null&&Array.IndexOf(args,"-provingGroundEvidence")>=0)StartCoroutine(LoadingPreparation.Run(CaptureProvingGroundEvidence(),e=>{File.WriteAllText(Path.Combine(evidence,"proving-result.txt"),"FAIL "+e);Debug.LogException(e);runtime?.RequestStop();Flush();Application.Quit();}));
                if(lobbyEvidence!=null&&Array.IndexOf(args,"-lobbyManual")<0)StartCoroutine(CaptureLobbyEvidence());
                for(int i=0;i+1<args.Length;i++)if(args[i]=="-screenAssignmentEvidence")StartScreenAssignmentEvidence(args[i+1]);
                if(evidence!=null&&Array.IndexOf(args,"-battleHudEvidence")>=0)StartCoroutine(CaptureBattleHudEvidence());
                if(evidence!=null&&Array.IndexOf(args,"-hawkFlightEvidence")>=0)StartCoroutine(CaptureHawkFlightEvidence());
                if(evidence!=null&&Array.IndexOf(args,"-hawkCombatEvidence")>=0)StartCoroutine(CaptureHawkCombatEvidence());
#endif
        }
        private void CreateWorld()=>CreateWorldCore(false);
        private void CreateWorldCore(bool staged)
        {
            var lighting=ActiveLighting;
            if(localCoordinator==null){var sun=new GameObject("Sun").AddComponent<Light>();sun.transform.SetParent(transform);lighting.ApplyScene(sun);}
            cameraView=new GameObject("Command camera").AddComponent<Camera>();cameraView.enabled=!(initializingMenu||preparingPresentation||(localCoordinator?.preparingPresentation??false));cameraView.transform.SetParent(transform);cameraView.orthographic=true;cameraView.orthographicSize=(float)profile.CameraOrthoSize;
            cameraView.transform.position=new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);cameraView.transform.LookAt(Vector3.zero);
            if(profile.AuthoredMap!=null&&matchSetup?.Spectator!=true){var start=profile.Headquarters(LocalOwner);cameraView.transform.position+=new Vector3((float)start.X,0,(float)start.Z);}
            RememberKeyboardCamera();
            cameraView.nearClipPlane=.1f;cameraView.farClipPlane=300;cameraView.backgroundColor=new Color(.045f,.075f,.10f);cameraView.clearFlags=CameraClearFlags.SolidColor;
            world=new PlayableWorld(transform,profile,staged:staged){OwnerPaint=LobbyPaint};
            cameraView.gameObject.AddComponent<ContactOcclusionCamera>().Configure(ContactOcclusionProfile.Load());
            cameraView.gameObject.AddComponent<ActorGlowCamera>().Configure(ActorMaterialProfile.Load());
            cameraView.gameObject.AddComponent<CameraAntialiasing>().Configure(CameraAntialiasingProfile.Load());
            cameraView.gameObject.AddComponent<VisibleShadowCoverage>().Configure(transform,lighting);
            cameraView.gameObject.AddComponent<NativeGraphicsBinding>().Configure(world,profile.AuthoredMap?.Id,transform.GetComponentsInChildren<Light>().FirstOrDefault(l=>l.type==LightType.Directional)??FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l=>l.type==LightType.Directional),WhiteMeridianLightingVariants.Supports(profile.AuthoredMap?.Id)?lighting:null);
            // Authored map uses the same profile coordinates as the domain.
            if(profile.AuthoredMap==null)foreach(var obstacle in PlayableMap.SolidObstacles(profile))world.Obstacle(obstacle);

        }
        private void CreateHud()
        {
            var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");panel.scaleMode=PanelScaleMode.ConstantPixelSize;
            var doc=localCoordinator==null?gameObject.AddComponent<UIDocument>():localCoordinator.GetComponent<UIDocument>();if(localCoordinator==null){doc.panelSettings=panel;ownedPanelSettings=panel;}else{Destroy(panel);panel=doc.panelSettings;}
            panelRoot=doc.rootVisualElement;root=new VisualElement{name="local-seat-hud"};root.style.position=Position.Absolute;root.style.left=root.style.right=root.style.top=root.style.bottom=0;panelRoot.Add(root);if(localCoordinator==null)OrbitalTheme.ConfigurePanel(panel,panelRoot);root.pickingMode=PickingMode.Ignore;root.focusable=true;root.tabIndex=-1;OrbitalTheme.Install(root);menuNavigation=new NativeMenuNavigation(root,()=>MenuPad,()=>WorldPadOwnsToolkit||WorldKeyboardOwnsToolkit,()=>MenuOwnsInput||SeatUsesKeyboard){Focused=()=>localInputFocused,WorldOwnsNavigation=()=>WorldCameraOwnsToolkit,ShowGamepadHints=()=>MenuPadHints};
            orderMarkerLayer=new PlayableOrderMarkerLayer(location=>PlayableOrderMarkerLayer.WorldPoint(root,cameraView,profile,location));root.Insert(0,orderMarkerLayer);
            root.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");root.style.fontSize=16;root.style.color=Ink;
            top=Panel();top.name="match-header";top.style.right=20;top.style.top=16;top.style.minHeight=48;top.style.flexDirection=FlexDirection.Row;top.style.flexWrap=Wrap.Wrap;top.style.alignItems=Align.Center;StyleRegion(top);root.Add(top);
            var brand=new Label("SPACEWARS");brand.style.fontSize=18;brand.style.color=Cyan;brand.style.unityFontStyleAndWeight=FontStyle.Bold;brand.style.letterSpacing=2;brand.style.marginRight=20;brand.style.display=DisplayStyle.None;top.Add(brand);
            objective=new Label("ЦЕЛЬ  /  Захватите территории · уничтожьте центры противника");objective.style.flexGrow=1;objective.style.minWidth=140;objective.style.fontSize=12;objective.style.maxWidth=340;objective.style.marginRight=20;objective.style.color=Ink;objective.style.whiteSpace=WhiteSpace.Normal;objective.style.display=DisplayStyle.None;top.Add(objective);
            creditsLabel=new Label();creditsLabel.style.fontSize=23;creditsLabel.style.unityFontStyleAndWeight=FontStyle.Bold;creditsLabel.style.color=Cyan;creditsLabel.style.marginRight=20;top.Add(creditsLabel);
            hudFocusButton=new Button(()=>ShowArmyComposition(true)){text="Армия",name="army-capacity"};StyleAction(hudFocusButton);top.Add(hudFocusButton);CreateArmyComposition();var quickRestart=Button("Заново",Restart);quickRestart.style.display=DisplayStyle.None;top.Add(quickRestart);hudLobbyButton=Button("В лобби",ReturnToLobby);hudLobbyButton.style.display=DisplayStyle.None;top.Add(hudLobbyButton);var quickQuit=Button("Выйти",Quit);quickQuit.style.display=DisplayStyle.None;top.Add(quickQuit);
            bottom=Panel();bottom.style.left=20;bottom.style.right=20;bottom.style.bottom=18;bottom.name="match-command-deck";bottom.style.minHeight=0;bottom.style.backgroundColor=Color.clear;bottom.style.paddingLeft=0;bottom.style.paddingRight=0;bottom.style.paddingTop=0;bottom.style.paddingBottom=0;bottom.pickingMode=PickingMode.Ignore;root.Add(bottom);
            hudRow=new VisualElement();hudRow.style.flexDirection=FlexDirection.Row;hudRow.style.alignItems=Align.FlexEnd;hudRow.pickingMode=PickingMode.Ignore;bottom.Add(hudRow);
            CreateMaps(hudRow);
            buildRegion=new VisualElement();buildRegion.style.width=220;buildRegion.style.display=DisplayStyle.None;hudRow.Add(buildRegion);
            siteLabel=new Label("Выберите свободную площадку");siteLabel.style.whiteSpace=WhiteSpace.Normal;buildRegion.Add(siteLabel);
            buildActions=new VisualElement();buildRegion.Add(buildActions);
            buildGauss=Button("Гаусс-башня",()=>BuildSelected(PlayableBuildingKind.GaussTower));buildActions.Add(buildGauss);
            buildRepairTower=Button("Ремонтная вышка",()=>BuildSelected(PlayableBuildingKind.RepairTower));buildActions.Add(buildRepairTower);
            buildTurret=Button("Пулемётная турель",()=>BuildSelected(PlayableBuildingKind.SentryTurret));buildActions.Add(buildTurret);
            buildHangar=Button("Ангар",()=>BuildSelected(PlayableBuildingKind.Hangar));buildActions.Add(buildHangar);
            buildFactory=Button("⬡ Фабрика",()=>BuildSelected(PlayableBuildingKind.Factory));buildActions.Add(buildFactory);
            buildRefinery=Button("⬡ Переработка",()=>BuildSelected(PlayableBuildingKind.Refinery));buildActions.Add(buildRefinery);
            buildScience=Button("Научный центр",()=>BuildSelected(PlayableBuildingKind.ScientificCenter));buildActions.Add(buildScience);
            buildCenter=Button("⬡ Построить",()=>{var site=view.Sites.FirstOrDefault(x=>x.Site.Id==selectedSite);if(site!=null)BuildSelected(site.Site.Kind);});buildActions.Add(buildCenter);
            cancelBuilding=Button("Отменить стройку",()=>Submit(PlayableCommandKind.CancelBuilding));buildActions.Add(cancelBuilding);
            armyRegion=new VisualElement();armyRegion.style.flexGrow=1;armyRegion.style.flexBasis=0;armyRegion.style.minWidth=380;armyRegion.style.marginLeft=14;StyleRegion(armyRegion);armyRegion.name="battle-context";hudRow.Add(armyRegion);
            selectionLabel=new Label("Выберите фабрику или танки");selectionLabel.style.fontSize=18;selectionLabel.style.unityFontStyleAndWeight=FontStyle.Bold;selectionLabel.style.whiteSpace=WhiteSpace.Normal;selectionLabel.style.marginBottom=12;armyRegion.Add(selectionLabel);
            var productionActions=new VisualElement{name="production-actions"};productionActions.style.display=DisplayStyle.None;productionActions.style.flexDirection=FlexDirection.Row;armyRegion.Add(productionActions);
            hawkButton=Button("Ястреб · "+profile.HawkCreditCost,()=>Submit(PlayableCommandKind.QueueHawk,new[]{productionFactory}));productionActions.Add(hawkButton);
            shkvalButton=Button("Шквал · "+profile.ShkvalCreditCost,()=>Submit(PlayableCommandKind.QueueShkval,new[]{productionFactory}));productionActions.Add(shkvalButton);
            explorerButton=Button("Исследователь · "+profile.ExplorerCreditCost,()=>Submit(PlayableCommandKind.QueueExplorer,new[]{productionFactory}));productionActions.Add(explorerButton);
            tankButton=Button("Танк · "+profile.TankCreditCost,()=>Submit(PlayableCommandKind.QueueTank,new[]{productionFactory}));productionActions.Add(tankButton);
            foreach(var action in new[]{shkvalButton,explorerButton,tankButton}){action.style.flexGrow=1;action.style.flexBasis=0;action.style.height=46;action.style.fontSize=14;action.style.whiteSpace=WhiteSpace.Normal;action.style.unityTextAlign=TextAnchor.MiddleCenter;}
            progress=new ProgressBar{lowValue=0,highValue=100,title="Нет производства"};progress.style.height=22;armyRegion.Add(progress);
            StyleProgress(progress);
            queueLabel=new Label();armyRegion.Add(queueLabel);CreateProductionHud(armyRegion);CreateScienceHud(armyRegion);CreateSelectionRoster(armyRegion);armyRegion.Add(cancelBuilding);
            commandRegion=new VisualElement();commandRegion.style.width=220;commandRegion.style.marginLeft=14;commandRegion.style.display=DisplayStyle.None;hudRow.Add(commandRegion);
            var holdAction=Button("HOLD · H",()=>Submit(PlayableCommandKind.Hold));holdAction.name="command-hold";commandRegion.Add(holdAction);
            var stopAction=Button("Стоп · S",()=>Submit(PlayableCommandKind.Stop));stopAction.name="command-stop";commandRegion.Add(stopAction);CreateBuildingLifecycleHud(commandRegion);
            var commandHint=new Label("ПКМ — приказ\nA — атаковать\nF6 — выбрать действие");commandHint.style.whiteSpace=WhiteSpace.Normal;commandHint.style.color=Muted;commandHint.style.fontSize=13;commandHint.style.marginTop=10;commandRegion.Add(commandHint);armyRegion.Add(lifecycleLabel);CreateBattleRing();
            noticeLabel=new Label();noticeLabel.style.marginTop=8;noticeLabel.style.fontSize=13;noticeLabel.style.whiteSpace=WhiteSpace.Normal;noticeLabel.style.backgroundColor=PanelColor;noticeLabel.style.color=new Color(.92f,.78f,.48f);bottom.Add(noticeLabel);
            modal=new VisualElement();modal.style.position=Position.Absolute;modal.style.left=0;modal.style.right=0;modal.style.top=0;modal.style.bottom=0;modal.style.backgroundColor=new Color(.015f,.035f,.045f,.84f);modal.style.alignItems=Align.Center;modal.style.justifyContent=Justify.Center;modal.style.display=DisplayStyle.None;root.Add(modal);
            modalCard=new VisualElement{name="match-menu-card"};modalCard.AddToClassList("orbital-match-menu");modalCard.style.width=540;modalCard.style.maxWidth=Length.Percent(100);modalCard.style.paddingLeft=30;modalCard.style.paddingRight=30;modalCard.style.paddingTop=28;modalCard.style.paddingBottom=28;StyleRegion(modalCard);modal.Add(modalCard);
            var modalEyebrow=Section("SPACEWARS  /  КОМАНДОВАНИЕ");modalEyebrow.name="match-menu-eyebrow";modalCard.Add(modalEyebrow);modalTitle=new Label();modalTitle.style.fontSize=32;modalTitle.style.unityFontStyleAndWeight=FontStyle.Bold;modalTitle.style.color=Cyan;modalTitle.style.unityTextAlign=TextAnchor.MiddleCenter;modalTitle.style.marginTop=16;modalTitle.style.marginBottom=8;modalCard.Add(modalTitle);
            modalCaption=new Label("Бой приостановлен");modalCaption.style.unityTextAlign=TextAnchor.MiddleCenter;modalCaption.style.color=Muted;modalCaption.style.marginBottom=20;modalCard.Add(modalCaption);
            resumeButton=Button("Продолжить",()=>Pause(false));modalCard.Add(resumeButton);
            provingStartButton=Button("Начать бой",StartProvingGround);provingStartButton.name="proving-start";provingStartButton.style.display=DisplayStyle.None;modalCard.Add(provingStartButton);
            modalRestartButton=Button("Начать заново",Restart);modalCard.Add(modalRestartButton);
            modalCard.Add(pauseSettings=OrbitalTheme.Action("Настройки",OpenPauseSettings,"pause-settings"));
            provingLabButton=Button("Выйти в лабораторию",ExitProvingGround);provingLabButton.name="proving-exit";provingLabButton.style.display=DisplayStyle.None;modalCard.Add(provingLabButton);
            modalCard.Add(pauseMain=OrbitalTheme.Action("В главное меню",ReturnToMainMenu,"pause-main"));
            modalExitButton=Button("Выйти",Quit);modalCard.Add(modalExitButton);modalLobbyButton=Button("В лобби",ReturnToLobby);modalLobbyButton.style.display=DisplayStyle.None;CreatePostMatchActions();CreateCampaignHud();
            foreach(var action in modalCard.Query<Button>().ToList()){action.style.marginRight=0;action.style.height=44;action.style.minHeight=44;action.style.fontSize=18;action.style.marginBottom=7;}
            resumeButton.AddToClassList("orbital-primary");modalRestartButton.AddToClassList("orbital-primary");
            CreateDragFrame();
            root.RegisterCallback<GeometryChangedEvent>(_=>ApplyResponsiveHud());
            root.RegisterCallback<KeyDownEvent>(evt=>
            {
                if(menuNavigation.Active||!SeatUsesKeyboard&&!MenuOwnsInput)return;
                if(spectatorMode&&evt.keyCode==KeyCode.Tab&&!ModalVisible()&&root.focusController?.focusedElement!=root){ToggleMap();}
                else if(ProvingPreparation&&!menuPaused)return;
                else if(evt.keyCode==KeyCode.F6)ToggleHudFocus();
                else if(evt.keyCode==KeyCode.Tab&&(ModalVisible()||root.focusController?.focusedElement is Button))MoveHudFocus(evt.shiftKey);
                else if((evt.keyCode==KeyCode.Return||evt.keyCode==KeyCode.KeypadEnter||evt.keyCode==KeyCode.Space)&&root.focusController?.focusedElement is Button focused&&HudActionVisible(focused)){menuNavigation.ActivateElement(focused);}
                else if(evt.keyCode==KeyCode.Escape&&ModalVisible())
                {
                    if(paused&&view?.Outcome==PlayableMatchOutcome.Playing)Pause(false);
                    else SyncModalFocus();
                }
                else return;
                evt.StopImmediatePropagation();
            });
            CreateChatHud();
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
        private bool OverUi(Vector2 p){if(cameraView!=null&&!cameraView.pixelRect.Contains(p))return true;if(root?.panel==null)return false;var q=RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(p.x,Screen.height-p.y));return orbitalOpen||OrbitalHudAtPanel(q)||ChatMessageAtPanel(q)||ProvingGroundOverUi(q)||(spectatorMode&&spectatorPanel!=null&&spectatorPanel.worldBound.Contains(q))||top.worldBound.Contains(q)||compactMap.parent.worldBound.Contains(q)||armyRegion.worldBound.Contains(q)||(armyComposition.style.display.value==DisplayStyle.Flex&&armyComposition.worldBound.Contains(q))||(battleRing.style.display.value==DisplayStyle.Flex&&battleRing.ContainsPanelPoint(q))||(ResultsVisible&&postMatch.Root.worldBound.Contains(q))||(modal.style.display==DisplayStyle.Flex&&modal.worldBound.Contains(q));}

        private void StartSession()=>StartSessionCore(true);
        private void StartSessionCore(bool initializeLocal){CloseChat();ResetSpectatorPresentation();gameplayAudio?.ResetAudio();keyboardGroups.Reset();ResetKeyboardCamera();ResetPostMatch();ClearArtilleryEffects();confirmSaleBuilding=0;menuPaused=paused=false;routeService?.Dispose();routeService=new UnityHostRouteService(routeLane);world.Clear();CloseMap();input?.ClearMode();foreach(var shell in shells.Values)Destroy(shell);shells.Clear();runtime=CreateSessionRuntime(++generation);BindMatchLighting(true);if(matchSetup!=null)matchSetup=runtime.LobbyConfiguration;ResetChat();generation=runtime.Generation;sequence=runtime.HumanSequenceWatermark;noticeSequence=0;selection.Clear();selectedSite=selectedSlot=0;restarting=false;modalWasVisible=false;root.Focus();notice="";if(initializeLocal)ConfigureLocalPresentations();Record("start");}
        private void Select(Vector2 from,Vector2 to,bool shift){if(ProvingPreparation&&(provingUnit.HasValue||(to-from).magnitude<input.SelectionDragPixels&&OpenProvingConstruction(Ground(to)))){if(provingUnit.HasValue)PlaceProvingGround(ProvingPlacementPoint(to));return;}if(spectatorMode){inspectionEntityId=Pick(to,false);ValidateInspection();return;}try{SelectCore(from,to,shift);}finally{SelectionMarkerEvent();}}
        private void SelectCore(Vector2 from,Vector2 to,bool shift)
        {
            if(view==null)return;runtime?.RecordHumanAction(LocalOwnerId);battleDismissed=false;battleRally=false;confirmSaleBuilding=0;root.Focus();selectedSite=selectedSlot=0;bool box=(to-from).magnitude>profile.SelectionDragPixels;
            if(box)
            {
                var ids=BoxSelectionUnits(from,to).ToArray();
                if(!shift)selection.Clear();else selection.RemoveWhere(id=>!view.Entities.Any(e=>e.Id==id&&e.Owner==view.Owner));
                foreach(int id in ids)selection.Add(id);
            }
            else
            {
                if(PickPad(Ground(to))){keyboardGroups.Observe(selection);Record("site="+selectedSite+" slot="+selectedSlot);return;}
                int id=Pick(to,true);bool unit=view.Entities.Any(e=>e.Id==id&&e.Owner==view.Owner);
                if(!shift||!unit)selection.Clear();else selection.RemoveWhere(selected=>!view.Entities.Any(e=>e.Id==selected&&e.Owner==view.Owner));
                if(id!=0)selection.Add(id);
            }
            keyboardGroups.Observe(selection);
            Record("select screen="+to+" resolution="+Screen.width+"x"+Screen.height+" ground="+Ground(to).X+","+Ground(to).Z+" ids="+string.Join(",",selection));notice="";
        }
        private int Pick(Vector2 point,bool onlyOwn)
        {
            int picked=0;float best=float.MaxValue;
            foreach(var e in view.Entities)if(!onlyOwn||e.Owner==LocalOwner)
            {
                var center=cameraView.WorldToScreenPoint(world.EntityPoint(e)+Vector3.up*.5f);
                var rim=cameraView.WorldToScreenPoint(world.EntityPoint(e)+Vector3.right*(float)(PlayableUnitRules.Radius(profile,e.Kind)*profile.TargetPickRadiusMultiplier)+Vector3.up*.5f);
                float d=Vector2.Distance(point,center);
                if(center.z>0&&d<Vector2.Distance(center,rim)&&d<best){best=d;picked=e.Id;}
            }
            foreach(var b in view.Buildings)if(b.Phase!=ConstructionPhase.Pending&&(!onlyOwn||b.Owner==LocalOwner))
            {
                var center=cameraView.WorldToScreenPoint(world.Point(b.Position)+Vector3.up);
                var rim=cameraView.WorldToScreenPoint(world.Point(b.Position)+Vector3.right*(float)profile.BuildingPickRadius+Vector3.up);
                float d=Vector2.Distance(point,center);
                if(center.z>0&&d<Vector2.Distance(center,rim)&&d<best){best=d;picked=b.Id;}
            }
            return picked;
        }
        private int PickWreck(Vector2 point)
        {
            int picked=0;float best=float.MaxValue;
            foreach(var wreck in view.Wrecks){
                if(!wreck.Blocks(view.Tick))continue;var center=cameraView.WorldToScreenPoint(world.Point(wreck.Position));
                var rim=cameraView.WorldToScreenPoint(world.Point(wreck.Position)+Vector3.right*(float)(wreck.Radius*profile.TargetPickRadiusMultiplier));float distance=Vector2.Distance(point,center);
                if(center.z>0&&distance<Vector2.Distance(center,rim)&&distance<best){best=distance;picked=wreck.Id;}
            }
            return picked;
        }
        private bool IsWreckAt(Vector2 point)=>view!=null&&world!=null&&cameraView!=null&&Pick(point,false)==0&&PickWreck(point)!=0;
        private NavPoint Ground(Vector2 p)
        {
            var ray=cameraView.ScreenPointToRay(p);if(profile.AuthoredMap==null){if(new Plane(Vector3.up,Vector3.zero).Raycast(ray,out float distance)){var v=ray.GetPoint(distance);return new NavPoint(v.x,v.z);}}
            else{double nearest=double.PositiveInfinity;NavPoint hit=default(NavPoint);foreach(var support in profile.AuthoredMap.Supports){var gradient=support.Gradient;double denominator=ray.direction.y-gradient.X*ray.direction.x-gradient.Z*ray.direction.z;if(System.Math.Abs(denominator)<1e-9)continue;double t=(support.HeightAt(new NavPoint(ray.origin.x,ray.origin.z))-ray.origin.y)/denominator;var at=ray.GetPoint((float)t);var point=new NavPoint(at.x,at.z);if(t>=0&&t<nearest&&support.Contains(point)){nearest=t;hit=point;}}if(!double.IsInfinity(nearest))return hit;}
            // Camera footprint and minimap panning require finite projection even outside support.
            if(new Plane(Vector3.up,Vector3.zero).Raycast(ray,out float fallback)){var v=ray.GetPoint(fallback);return new NavPoint(v.x,v.z);}
            return new NavPoint(double.NaN,double.NaN);
        }
        private void Order(Vector2 p,bool attackMode,bool append)
        {
            if(matchSetup?.Spectator==true)return;
            if(battleRally){CompleteRallyPlacement(Ground(p),battleController);return;}
            var target=Ground(p);int id=Pick(p,false);bool enemy=HostileAt(p);
            var selectedTurret=view.Buildings.FirstOrDefault(b=>TerritoryRules.ArmedDefense(b.Kind)&&b.Owner==LocalOwner&&selection.Contains(b.Id));
            if(selectedTurret!=null){if(enemy)Submit(PlayableCommandKind.Attack,new[]{selectedTurret.Id},target,id);return;}
            if(id==0&&SelectedRallyProducer()==null){int wreckId=PickWreck(p);if(wreckId!=0){var wreck=view.Wrecks.Single(w=>w.Id==wreckId);Submit(PlayableCommandKind.Attack,null,wreck.Position,wreck.Id,mode:append?PlayableOrderMode.Append:PlayableOrderMode.Replace);return;}}
            if(SelectedRallyProducer()!=null)Submit(PlayableCommandKind.SetRally,new[]{SelectedRallyProducer().Id},target);
            else if(!attackMode && view.Entities.Any(e=>e.Id==id&&!IsOpponent(e.Owner))) { if(!append)Submit(PlayableCommandKind.Follow,null,target,id); }
            else if(!enemy&&view.Buildings.Any(b=>b.Id==id&&!IsOpponent(b.Owner)))return;
            else Submit(enemy?PlayableCommandKind.Attack:attackMode?PlayableCommandKind.AttackMove:PlayableCommandKind.Move,null,target,enemy?id:0,mode:append?PlayableOrderMode.Append:PlayableOrderMode.Replace);
        }
        private void Submit(PlayableCommandKind kind,int[] ids=null,NavPoint target=default(NavPoint),int targetId=0,long productionOrderId=0,PlayableEntityKind unitKind=PlayableEntityKind.Tank,PlayableResearchKind researchKind=PlayableResearchKind.TankChassis,PlayableOrderMode mode=PlayableOrderMode.Replace)
        {
            if(BotSeat||runtime==null||paused||restarting||quitting||matchSetup?.Spectator==true)return;
            var recipients=ids??selection.ToArray();
            var destinations=kind==PlayableCommandKind.Move||kind==PlayableCommandKind.AttackMove?HumanMoveDestination.Resolve(view.ActiveProfile??profile,view,recipients,target):new[]{new HumanMoveDestination.Group(recipients,target)};
            foreach(var destination in destinations){
                var command=new PlayableCommand(generation,++sequence,LocalOwnerId,kind,destination.Entities,destination.Target,targetId:targetId,productionOrderId:productionOrderId,unitKind:unitKind,researchKind:researchKind,mode:mode);
                var result=runtime.TrySubmit(command);AudioCommand(command,result.Accepted);if(result.Accepted)orderMarkers.Submitted(view,command);
                if(mode!=PlayableOrderMode.Append||!result.Accepted){noticeSequence=sequence;notice=result.Accepted?"":"Не удалось отправить приказ: "+Friendly(result.Status);}
                Record(kind+" ids="+string.Join(",",destination.Entities)+" target="+destination.Target.X+","+destination.Target.Z+" order="+productionOrderId+" admission="+result.Status);
            }
        }
        private string Friendly(PlayableCommandStatus status){switch(status){case PlayableCommandStatus.Accepted:return "";case PlayableCommandStatus.Cancelled:return "";case PlayableCommandStatus.Applied:return "";case PlayableCommandStatus.InsufficientCredits:return "Недостаточно кредитов";case PlayableCommandStatus.OccupiedPad:return "Площадка занята";case PlayableCommandStatus.InvalidEntity:return "Недоступный объект для приказа";case PlayableCommandStatus.InvalidTarget:return "Недоступная цель";case PlayableCommandStatus.Overflow:return "Слишком много приказов, повторите";default:return "Приказ отклонён";}}
        private void Pause(bool value){if(value)CloseOrbital();if(ProvingPreparation){provingUnit=null;CloseProvingConstruction();}if(localCoordinator!=null){localCoordinator.PauseFrom(this,value);return;}if(runtime==null||restarting||quitting||(!value&&matchSetup!=null&&!matchSetup.Spectator&&localPresentations.Any(seat=>!seat.SeatReady)))return;if(runtime.Result!=null){resultOverview=false;ShowPostMatch();return;}confirmSaleBuilding=0;finishConfirmation=false;if(finishMatch!=null)finishMatch.text="Завершить матч";menuPaused=value;paused=value||view?.Mission?.TutorialPauseHeld==true;BroadcastPause(value);if(!value){CloseChildMenu();menuNavigation?.SetScope(null);}runtime.RequestPause(value);input?.ClearMode();CloseMap();Record("pause="+value);}
        private void Restart(){if(provingGround!=null){ResetProvingGround();return;}if(localCoordinator!=null){localCoordinator.Restart();return;}if(runtime==null||restarting||quitting)return;Record("restart requested");restarting=true;runtime.RequestStop();}
        private void Quit(){
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if(qaDeadlinePath!=null){BeginQaFinish(routePerformanceStarted<0?"FAIL entry did not reach match":"route harness requested exit",routePerformanceStarted<0);return;}
#endif
            if(localCoordinator!=null){localCoordinator.Quit();return;}if(quitting)return;Record("exit requested");quitting=true;runtime?.RequestStop();}
        private void Pan(Vector2 axis){if(BotCameraLocked)return;cameraView.transform.position+=NativeCameraScrollSettings.Displacement(cameraView.transform,axis,NativeCameraScrollSettings.ArrowSpeed(profile.CameraPanSpeed),Time.unscaledDeltaTime);Record("camera pan");}
        private void Zoom(float delta){if(BotCameraLocked)return;cameraView.orthographicSize=Mathf.Clamp(cameraView.orthographicSize-delta*(float)profile.CameraZoomSpeed,(float)profile.CameraMinZoom,provingGround!=null?Mathf.Max(keyboardHomeZoom,(float)profile.CameraMaxZoom):(float)profile.CameraMaxZoom);Record("camera zoom");}
        private void Update()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if(ServiceQaCompletion())return;
#endif
            if(root==null||initializingMenu)return;
            long begin=System.Diagnostics.Stopwatch.GetTimestamp();
            routeServiceFrameMs=renderFrameMs=hudFrameMs=mapsFrameMs=audioFrameMs=0;
            // Diagnostic thresholds: discard startup's first five seconds and count >100ms frames.
            // These define evidence buckets, not gameplay or presentation tuning.
            if(Time.realtimeSinceStartup-started>=5){double ms=Time.unscaledDeltaTime*1000;maxSteadyFrameMs=Math.Max(maxSteadyFrameMs,ms);if(ms>100)longSteadyFrames++;}
            try{UpdateFrame();}
            finally{long audioStage=routeFrames==null?0:System.Diagnostics.Stopwatch.GetTimestamp();UpdateMusic();UpdateGameplayAudio();audioFrameMs=RouteStageElapsed(ref audioStage);lastMainUpdateMs=(System.Diagnostics.Stopwatch.GetTimestamp()-begin)*1000d/System.Diagnostics.Stopwatch.Frequency;maxMainUpdateMs=Math.Max(maxMainUpdateMs,lastMainUpdateMs);RecordRouteFrame();}
        }
        private void UpdateFrame()
        {
            try{UpdateFrameState();}
            finally{SyncSystemCursor();}
        }
        private void UpdateFrameState()
        {
            LogRuntimeFailure();
            TickMenuInput();UpdateLobby();
            if(preparingPresentation){if(runtime!=null)ServiceRoutes();return;} // Host preparation still needs its route pump; presentation remains covered.
            if(returningToLobby&&(runtime==null||runtime.IsStopped)){DisposeLocalChildren();ResetLocalViewport();ResetPostMatch();returningToLobby=false;runtime=null;view=null;world.Clear();selection.Clear();CloseMap();if(returningToMain){returningToMain=false;ShowMainMenu();}else ShowLobby();return;}
            if(inLobby&&!preparing){
                ClearIncomeMarkers();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if(resultEvidenceStage>=91)DriveResultUiEvidence();
#endif
                return;
            }
            if(runtime==null)return;
            if(runtime.IsStopped){if(quitting){if(Runtime.OriginalGroupDiagnostics.Active){if(!routeService.StopDiagnosticWorker())return;Runtime.OriginalGroupDiagnostics.Save(Path.Combine(evidence,"original-group-disposition.bin"));Runtime.OriginalGroupDiagnostics.Stop();}if(Runtime.NavigationArrivalDiagnostics.Active){if(!routeService.StopDiagnosticWorker())return;Runtime.NavigationArrivalDiagnostics.Save(Path.Combine(evidence,"arrival-diagnostic.bin"));Record("arrival-diagnostic FLUSH dropped="+Runtime.NavigationArrivalDiagnostics.Dropped);Runtime.NavigationArrivalDiagnostics.Stop();}Flush();Application.Quit();return;}if(restarting)StartSession();}
            ReadPresentationFrame();var captured=ownerFrame;if(view==null)return;
            RefreshLocalKeyboardBinding();input.WorldInputEnabled=!BotSeat&&childMenu==null&&!inLobby&&!preparing&&!returningToLobby&&SeatReady&&!menuPaused&&(!paused||ProvingPreparation)&&!restarting&&!quitting&&(view.Outcome==PlayableMatchOutcome.Playing||resultOverview)&&!ResultsVisible&&string.IsNullOrEmpty(view.Failure);input.CommandInputEnabled=input.WorldInputEnabled&&matchSetup?.Spectator!=true;
            input.PlacementInputEnabled=ProvingPreparation&&provingUnit.HasValue;
            RebindPresentation();
            long phase=routeFrames==null?0:System.Diagnostics.Stopwatch.GetTimestamp();
            using(RouteServiceMarker.Auto())ServiceRoutes();routeServiceFrameMs=RouteStageElapsed(ref phase);
            using(RenderMarker.Auto())Render();renderFrameMs=RouteStageElapsed(ref phase);
            using(HudMarker.Auto())UpdateHud();hudFrameMs=RouteStageElapsed(ref phase);
            using(MapsMarker.Auto())UpdateMaps();mapsFrameMs=RouteStageElapsed(ref phase);
            UpdateLifecycleMarkers();UpdateIncomeMarkers();UpdateTeamSignalPresentation();UpdateScanPresentation();UpdateMatchNotifications();PollBattleController();UpdateKeyboardPresentation();UpdateBotCamera();
            var allReceipts=runtime.DrainReceipts().ToArray();var markerReceipts=allReceipts.Where(r=>r.OwnerId==LocalOwnerId).ToArray();foreach(var receipt in markerReceipts){if(receipt.Sequence>=noticeSequence&&!(receipt.Status==PlayableCommandStatus.Applied&&receipt.Message==null)){noticeSequence=receipt.Sequence;notice=Friendly(receipt.Status);}Record("receipt "+receipt.Sequence+" "+receipt.Status+" latency_ms="+receipt.LatencyMilliseconds);}
            ObserveScanReceipts(markerReceipts);UpdateOrderMarkers(markerReceipts);if(orbitalOpen)orbitalModal.BringToFront();PresentLocalChildren(captured,allReceipts);foreach(var receipt in allReceipts)gameplayAudio?.Receipt(receipt,generation);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            DriveResponsiveUiEvidence();
            DriveResultUiEvidence();
            DrivePrototypeResearchEvidence();
            DriveStyleASceneEvidence();
            DriveRoutePerformanceEvidence();
#endif
            if(view.Tick!=lastTick){lastTick=view.Tick;metrics?.WriteLine(string.Join(",",(Time.realtimeSinceStartup-started).ToString("F3",System.Globalization.CultureInfo.InvariantCulture),generation,view.Tick,view.Metrics.TickCpuMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),view.Metrics.TickIntervalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),view.Metrics.CommandLatencyMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),view.Metrics.CommandBacklog,runtime.Requests.Count,runtime.Answers.Count,Time.unscaledDeltaTime*1000,view.Entities.Count,view.Credits,paused,view.Outcome,view.Metrics.Errors,GC.GetTotalMemory(false),view.Metrics.NavigationPending,view.Metrics.MissedDeadlines,view.Metrics.MaximumTickCpu,lastMainUpdateMs,maxMainUpdateMs,maxSteadyFrameMs,longSteadyFrames,GC.CollectionCount(0),Time.frameCount,view.Buildings.Count,view.Buildings.Count(b=>b.Phase==ConstructionPhase.Pending),view.Buildings.Count(b=>b.Kind==PlayableBuildingKind.Outpost&&b.Phase==ConstructionPhase.Ready),view.Buildings.Count(b=>b.Kind==PlayableBuildingKind.Mine&&b.Phase==ConstructionPhase.Ready),view.IncomePerSecond,world.Fog.TargetBuilds,world.Fog.Uploads,world.Fog.ScannedFrames,mapTerrain.Uploads,world.MemoryCount,view.Buildings.Count(b=>b.Owner==LocalOwner&&b.Kind==PlayableBuildingKind.ScientificCenter&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.Lifecycle?.Selling!=true),view.Buildings.Count(b=>b.PrivateState?.Upgrade?.Active==true),view.Buildings.Count(b=>b.Owner==LocalOwner&&b.RefineryUpgraded)));if(view.Tick%30==0)Flush();}
        }
        // Consume the authority's immutable failure on the Unity thread, before a
        // restart or return-to-lobby can replace the failed runtime.
        private void LogRuntimeFailure()
        {
            var failed=runtime;var snapshot=failed?.Latest;
            if(snapshot==null||string.IsNullOrEmpty(snapshot.Failure)||loggedFailureGeneration==snapshot.Generation)return;
            loggedFailureGeneration=snapshot.Generation;
            Debug.LogError("[Spacewars simulation failure] generation="+snapshot.Generation+" seed="+snapshot.Seed+" tick="+snapshot.Tick+" map="+snapshot.ActiveProfile?.AuthoredMap?.Id+"\n"+snapshot.Failure);
        }
        private void RecordRouteFrame()
        {
            if(routeFrames==null||runtime==null||view==null)return;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if(qaFinishing)return;
#endif
            var host=routeService?.CertifiedPublication;var counters=host?.Counters;
            int motionActor=0;NavPoint authorityPosition=default;Vector3 displayPosition=default;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if(routePerformanceActor>0&&world.Actors.TryGetValue(routePerformanceActor,out var motionModel)){
                for(int i=0;i<view.Entities.Count;i++)if(view.Entities[i].Id==routePerformanceActor){
                    motionActor=routePerformanceActor;authorityPosition=view.Entities[i].Position;displayPosition=motionModel.Root.position;break;
                }
            }
#endif
            var sample=new NativeRouteFrameSample{
                Elapsed=Time.realtimeSinceStartup-started,Frame=Time.frameCount,Generation=view.Generation,Tick=view.Tick,Sequence=view.Sequence,
                FrameMs=Time.unscaledDeltaTime*1000,UpdateMs=lastMainUpdateMs,RouteServiceMs=routeServiceFrameMs,RenderMs=renderFrameMs,HudMs=hudFrameMs,MapsMs=mapsFrameMs,AudioMs=audioFrameMs,Units=runtime.DiagnosticGlobalUnits,Buildings=runtime.DiagnosticGlobalBuildings,
                BarrierPolls=runtime.BarrierPolls,Reuses=runtime.BarrierPayloadReuses,Builds=runtime.BarrierPayloadPublications,Pumps=host?.Pumps??0,
                PrepareMs=host?.PreparationElapsedMilliseconds??0,ProviderMs=host?.ProviderElapsedMilliseconds??0,SolveMs=host?.SolveElapsedMilliseconds??0,
                ProjectionMs=host?.ProjectionElapsedMilliseconds??0,TransportWaitMs=host?.TransportWaitMilliseconds??0,TransportHoldMs=host?.TransportHoldMilliseconds??0,
                PublishMs=runtime.PayloadBuildElapsedMilliseconds,Pending=counters?.PendingSubscriptions??0,Ready=counters?.ReadyResults??0,
                QueueAge=counters?.PeakQueueAge??0,Retained=host?.RetainedBytes??0,PeakRetained=host?.PeakRetainedBytes??0,
                MotionActor=motionActor,AuthorityX=authorityPosition.X,AuthorityZ=authorityPosition.Z,DisplayX=displayPosition.x,DisplayY=displayPosition.y,DisplayZ=displayPosition.z,
                Heap=GC.GetTotalMemory(false),GC0=GC.CollectionCount(0),MainAllocated=GC.GetAllocatedBytesForCurrentThread(),WorkerAllocated=host?.AllocatedBytes??0};
            preciseFrames?.Read(ref sample);routeFrames.TryWrite(sample);
        }
        private void ServiceRoutes()=>routeService.Service(runtime,profile.NavigationRequestsPerFrame);
        private readonly PlayableUnitMotion unitMotion=new PlayableUnitMotion();
        private readonly HashSet<int> renderedAlive=new HashSet<int>();
        private readonly List<int> renderedRemoved=new List<int>();
        private void Render()
        {
            RefreshDragSelection();
            using var measuredPresentation=PerformanceProfile.Measure(ProfileStage.Presentation);
            using(PerformanceProfile.Measure(ProfileStage.WorldFog))using(FogMarker.Auto())world.Fog.Update(view.Vision,Time.unscaledDeltaTime);using(PerformanceProfile.Measure(ProfileStage.WorldMemories))world.RenderMemories(view,cameraView);using(PerformanceProfile.Measure(ProfileStage.WorldSites))world.RenderSites(view,cameraView);
            using(PerformanceProfile.Measure(ProfileStage.UnitMotion))unitMotion.BeginFrame(view,paused||preparing||restarting||inLobby,spectatorMode?spectatorRate:1,spectatorMode?spectatorFrame?.PreviousPerspective(spectatorPerspective):ownerFrame?.PreviousView(LocalOwnerId),spectatorMode?(spectatorFrame?.AgeSeconds??0):(ownerFrame?.AgeSeconds??0));
            double poseTime=Time.realtimeSinceStartupAsDouble;
            using(PerformanceProfile.Measure(ProfileStage.WorldAnimation))world.AdvanceAircraftAnimation(view,poseTime,spectatorMode?spectatorRate:1);
            var alive=renderedAlive;alive.Clear();
            using(PerformanceProfile.Measure(ProfileStage.WorldUnits))foreach(var e in view.Entities){alive.Add(e.Id);if(!world.Actors.TryGetValue(e.Id,out var a))a=world.Tank(e.Id,e.Owner==LocalOwner,e.Kind);world.UpdateResearchModel(a,e);var pose=unitMotion.Sample(e,poseTime);if(a.TankMechanism)a.TankMechanism.Drive(pose,view,e.Id);a.Root.position=e.Kind==PlayableEntityKind.Hawk?world.AirGroundPoint(pose.Position):world.Point(pose.Position);var slope=e.Kind==PlayableEntityKind.Hawk?default(NavPoint):profile.AuthoredMap?.SurfaceGradient(pose.Position)??default(NavPoint);a.Root.rotation=Quaternion.FromToRotation(Vector3.up,new Vector3((float)-slope.X,1,(float)-slope.Z).normalized);a.Hull.localRotation=Quaternion.Euler(0,90-(float)pose.Hull*Mathf.Rad2Deg,0);a.Turret.localRotation=Quaternion.Euler(0,(float)(pose.Hull-pose.Turret)*Mathf.Rad2Deg,0);a.Selection.SetActive(DisplayedSelection(e.Id));if(matchSetup!=null||campaignConfiguration!=null)PlayableWorld.PaintOwner(a,LobbyPaint(e.Owner));PlayableWorld.UpdateHealth(a,cameraView,(float)PlayableUnitRules.HealthRatio(profile,e),selection.Contains(e.Id));world.UpdateAircraft(a,e,view,DisplayedSelection(e.Id),pose);world.AimExplorer(a,e);}
            using(PerformanceProfile.Measure(ProfileStage.WorldBuildings))foreach(var b in view.Buildings){alive.Add(b.Id);if(b.Phase==ConstructionPhase.Pending)continue;if(!world.Actors.TryGetValue(b.Id,out var a))a=world.Building(b.Id,b.Kind.ToString(),b.Owner==LocalOwner,b.ModelUpgraded);world.UpdateRefineryModel(a,b,view.Tick);world.UpdateFactoryModel(a,b,view);a.Root.position=world.Point(b.Position);a.Root.rotation=Quaternion.Euler(0,-(float)b.Heading*Mathf.Rad2Deg,0);world.FaceBuilding(a,cameraView);a.Root.localScale=new Vector3(1,(float)BuildingCombatGeometry.ConstructionHeight(b.Progress),1);a.Sentry?.Drive(b,view,profile);a.Gauss?.Drive(b,profile);a.Healing?.Drive(b,view,profile);a.Selection.SetActive(DisplayedSelection(b.Id));if(matchSetup!=null||campaignConfiguration!=null)PlayableWorld.PaintOwner(a,LobbyPaint(b.Owner));double max=TerritoryRules.MaximumHealth(profile,b);PlayableWorld.UpdateHealth(a,cameraView,(float)(b.Health/max));}
            using(PerformanceProfile.Measure(ProfileStage.WorldBuildings))foreach(var hangar in view.Buildings.Where(b=>b.Kind==PlayableBuildingKind.Hangar))if(world.Actors.TryGetValue(hangar.Id,out var hangarActor))world.UpdateHangar(hangarActor,hangar,view.Tick);
            using(PerformanceProfile.Measure(ProfileStage.WorldContacts))RenderDirectContacts();using(PerformanceProfile.Measure(ProfileStage.WorldDestruction))world.RenderBuildingDestructions(view);
            using(PerformanceProfile.Measure(ProfileStage.WorldRemoval)){
                renderedRemoved.Clear();foreach(var id in world.Actors.Keys)if(!alive.Contains(id))renderedRemoved.Add(id);
                foreach(var id in renderedRemoved){world.Remove(id);selection.Remove(id);}
            }
            using(PerformanceProfile.Measure(ProfileStage.WorldRemoval))unitMotion.KeepVisible(alive);
            using(PerformanceProfile.Measure(ProfileStage.WorldWrecks))world.RenderMines(view,cameraView);world.RenderUnitWrecks(view);world.SelectEnvironmentDetail(cameraView);using(PerformanceProfile.Measure(ProfileStage.WorldTrails))world.RenderGroundTrails(view,cameraView);
            world.RenderTowerRange(view,selection,primarySelection,!inLobby&&!preparing&&!restarting&&!quitting,relaySiteId:selectedSite);
            using(PerformanceProfile.Measure(ProfileStage.WorldRally))world.RenderRallyFlags(view,selection,matchSetup?.Spectator==true);
            using(PerformanceProfile.Measure(ProfileStage.WorldProjectiles))RenderProjectiles();
        }
        private void UpdateHud()
        {
            UpdateChatHud();
            bool spectator=matchSetup?.Spectator==true;
            if(spectator)objective.text="ЗРИТЕЛЬ";
            else if(matchSetup?.Foundry==true)objective.text="ЧЁРНАЯ ПЛАВИЛЬНЯ · A1–A3 против B1–B3";else if(matchSetup!=null)objective.text=matchSetup.MatchHumanName+" · Команда "+matchSetup.HumanTeam+"  /  "+matchSetup.MatchAiName+" · Команда "+matchSetup.AiTeam;
            UpdateProvingGroundUi();creditsLabel.style.display=provingGround==null?DisplayStyle.Flex:DisplayStyle.None;
            creditsLabel.text=view.Credits+" <color=#A9BCC3>+"+view.IncomePerSecond.ToString("0.#")+"/с</color>";
            UpdateSiteHud();
            var factory=view.Buildings.FirstOrDefault(b=>(spectatorMode?b.Id==inspectionEntityId:selection.Contains(b.Id)&&b.Owner==LocalOwner)&&(b.Kind==PlayableBuildingKind.Factory||b.Kind==PlayableBuildingKind.Hangar));
            var building=view.Buildings.FirstOrDefault(b=>spectatorMode?b.Id==inspectionEntityId:selection.Contains(b.Id));
            tankButton.SetEnabled(factory!=null&&factory.Progress>=1&&factory.PrivateState?.Lifecycle?.Selling!=true&&view.Outcome==PlayableMatchOutcome.Playing&&!paused);
            selectionLabel.text=building!=null?BuildingName(building.Kind)+" · HP "+building.Health:selection.Count==0?"Ничего не выбрано":"ЮНИТЫ · "+selection.Count+(view.Entities.Any(e=>selection.Contains(e.Id)&&e.Held)?" · HOLD":"");
            double value=building!=null&&building.Progress<1?building.Progress:factory?.ProductionProgress??0;
            progress.value=(float)Math.Min(100,value*100);progress.title=building!=null&&building.Phase==ConstructionPhase.Pending?(building.BlockedReason??"Ожидает начала"):building!=null&&building.Progress<1?"Строительство · "+(int)(value*100)+"%":factory!=null&&factory.QueueCount>0?"Танк · "+(int)Math.Min(100,value*100)+"%":"Нет активного производства";
            progress.style.display=building!=null&&(building.Progress<1||factory!=null)?DisplayStyle.Flex:DisplayStyle.None;
            UpdateProductionHud(factory);UpdateBuildingLifecycleHud(building);UpdateScienceHud(building);
            bool hasUnits=view.Entities.Any(e=>selection.Contains(e.Id)),hasBuilding=!spectatorMode&&building?.PrivateState!=null;
            commandRegion.Q<Button>("command-hold").style.display=commandRegion.Q<Button>("command-stop").style.display=hasUnits?DisplayStyle.Flex:DisplayStyle.None;
            sellBuilding.style.display=repairBuilding.style.display=lifecycleLabel.style.display=hasBuilding?DisplayStyle.Flex:DisplayStyle.None;
            commandRegion.style.display=DisplayStyle.None;
            UpdateBattleHud(building);
            noticeLabel.text=notice;modal.style.display=(!campaignTutorialShowing||menuPaused)&&childMenu==null&&LocalMenuVisible&&(menuPaused&&runtime?.Result==null||view.Outcome!=PlayableMatchOutcome.Playing&&(campaignConfiguration!=null||runtime?.Result==null)||restarting||!string.IsNullOrEmpty(view.Failure))?DisplayStyle.Flex:DisplayStyle.None;
            pauseSettings.style.display=view.Outcome==PlayableMatchOutcome.Playing&&!restarting?DisplayStyle.Flex:DisplayStyle.None;pauseMain.style.display=campaignConfiguration!=null?DisplayStyle.Flex:DisplayStyle.None;
            modalExitButton.style.display=view.Outcome==PlayableMatchOutcome.Playing?DisplayStyle.None:DisplayStyle.Flex;
            resumeButton.style.display=view.Outcome==PlayableMatchOutcome.Playing&&!restarting?DisplayStyle.Flex:DisplayStyle.None;
            bool plainPause=menuPaused&&view.Outcome==PlayableMatchOutcome.Playing&&!restarting&&string.IsNullOrEmpty(view.Failure);
            modalCard.Q("match-menu-eyebrow").style.display=plainPause?DisplayStyle.None:DisplayStyle.Flex;
            modalCaption.style.display=plainPause?DisplayStyle.None:DisplayStyle.Flex;modalTitle.style.marginBottom=plainPause?20:8;
            modalTitle.text=!string.IsNullOrEmpty(view.Failure)?"ОШИБКА СИМУЛЯЦИИ":restarting?"НОВЫЙ БОЙ…":view.Outcome==PlayableMatchOutcome.PlayerWon||view.Outcome==PlayableMatchOutcome.TeamWon?"ПОБЕДА":view.Outcome==PlayableMatchOutcome.PlayerLost?(campaignConfiguration!=null?"АРМИЯ ПОТЕРЯНА":"ЦЕНТРЫ ПОТЕРЯНЫ"):"Пауза";
            modalCaption.text=plainPause?"":(!string.IsNullOrEmpty(view.Failure)?"Матч остановлен":restarting?"Подготовка нового матча":view.Outcome==PlayableMatchOutcome.PlayerWon?"Противник побеждён":view.Outcome==PlayableMatchOutcome.PlayerLost?(campaignConfiguration!=null?"Вся боевая армия уничтожена":"Ваши центры уничтожены"):"Бой приостановлен");modalCaption.style.whiteSpace=WhiteSpace.Normal;
            if(!plainPause&&matchSetup!=null)modalCaption.text+="\n"+matchSetup.MatchHumanName+"  /  "+matchSetup.MatchAiName;
            modalTitle.style.color=!string.IsNullOrEmpty(view.Failure)||view.Outcome==PlayableMatchOutcome.PlayerLost?Danger:view.Outcome==PlayableMatchOutcome.PlayerWon?Cyan:Ink;
            UpdateProvingMenu();
            if(menuPaused&&provingGround!=null)modal.BringToFront();
            SyncModalFocus();
            UpdatePostMatch();
            if(provingGround!=null)finishMatch.style.display=DisplayStyle.None;
            UpdateResponsiveVisibility();UpdateCampaignHud();
            if(spectator)
            {
                objective.style.display=DisplayStyle.None;creditsLabel.style.display=spectatorPerspective.HasValue?DisplayStyle.Flex:DisplayStyle.None;hudFocusButton.style.display=DisplayStyle.Flex;
                armyComposition.style.display=buildRegion.style.display=commandRegion.style.display=DisplayStyle.None;armyRegion.style.display=inspectionEntityId!=0?DisplayStyle.Flex:DisplayStyle.None;battleRing.style.display=DisplayStyle.None;
                noticeLabel.text="";noticeLabel.style.display=DisplayStyle.None;
                UpdateSpectatorHud();
            }
            else UpdateSpectatorHud();
        }
        private void Record(string message){
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if(qaDeadlinePath!=null&&message.Contains(" FAIL "))qaFailed=true;
#endif
            actions?.WriteLine((Time.realtimeSinceStartup-started).ToString("F3")+" generation="+generation+" tick="+(view?.Tick??0)+" "+message);actions?.Flush();}
        private void Flush(){metrics?.Flush();actions?.Flush();}
        private void OnApplicationQuit(){runtime?.RequestStop();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if(sixPlayerMotion!=null)FlushSixPlayerMotion();
#endif
            Flush();}
        private void OnDestroy(){battleCursorVisual?.RemoveFromHierarchy();StopAllCoroutines();SetSystemCursorHidden(false);if(startupLoadingHost)Destroy(startupLoadingHost);if(startupLoadingPanel)Destroy(startupLoadingPanel);DisposeLocalChildren();if(ownedPanelSettings)Destroy(ownedPanelSettings);if(localCoordinator!=null){runtime=null;root?.RemoveFromHierarchy();}if(cameraView!=null)Destroy(cameraView.gameObject);if(padWhiteArrow)Destroy(padWhiteArrow);if(padRedArrow)Destroy(padRedArrow);commandCursor?.Dispose();menuNavigation?.Dispose();if(nativeMainMenu!=null)Destroy(nativeMainMenu.gameObject);ClearArtilleryEffects();if(artilleryTransparent)Destroy(artilleryTransparent);projectileVisuals.Dispose();lobbyTerrain?.Dispose();mapTerrain?.Dispose();world?.Dispose();runtime?.RequestStop();routeService?.Dispose();routeFrames?.Dispose();preciseFrames?.Dispose();metrics?.Dispose();actions?.Dispose();}
    }
}
