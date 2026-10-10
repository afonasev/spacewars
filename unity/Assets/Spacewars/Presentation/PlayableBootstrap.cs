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
        private readonly CertifiedRouteLane routeLane=new CertifiedRouteLane();
        private PlayableProfile profile;
        private PlayableRuntime runtime;
        private PlayableInput input;
        private readonly PlayableOrderMarkers orderMarkers=new PlayableOrderMarkers();
        private PlayableOrderMarkerLayer orderMarkerLayer;
        private void SelectionMarkerEvent(){orderMarkers.Selected(view,selection,Time.unscaledTimeAsDouble);if(view!=null&&!paused&&!inLobby)SharedAudio?.Selection(view.OwnerId,string.Join(",",selection.OrderBy(id=>id))+":"+selectedSite+":"+selectedSlot+":"+primarySelection);}
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
        private Button shkvalButton,explorerButton,tankButton,resumeButton,modalRestartButton,modalExitButton,hudFocusButton,buildFactory,buildRefinery,buildScience,buildCenter,cancelBuilding;
        private Label siteLabel;
        private int selectedSite,selectedSlot;
        private long generation,sequence,noticeSequence,lastTick=-1;
        private bool paused,restarting,quitting;
        private string notice="";
        private string evidence;
        private StreamWriter metrics,actions;
        private NativeRouteFrameTelemetry routeFrames;
        private float started;
        private int captures;
        private double lastMainUpdateMs,maxMainUpdateMs,maxSteadyFrameMs;
        private int longSteadyFrames;
        private PlayableSnapshot view;
        private readonly List<NavPoint> pads=new List<NavPoint>();
        private GameObject startupLoadingHost;
        private PanelSettings startupLoadingPanel;
        private System.Collections.IEnumerator Start()
        {
            nativeMainMenuEnabled=true;
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
            StartGame();
            yield return null;yield return null;
            if(root==null&&GetComponent<OfflineTwoLocalBootstrap>()==null&&GetComponent<FoundryGreybox>()==null&&GetComponent<ThreeCrossingsInspection>()==null){loading.Q<Label>("loading-status").text="Не удалось загрузить меню. Перезапустите игру.";yield break;}
            Destroy(startupLoadingHost);Destroy(startupLoadingPanel);startupLoadingHost=null;startupLoadingPanel=null;
        }
        internal static bool BypassTwoLocalOnce;
        private void StartGame()
        {
            try
            {
                localInputFocused=Application.isFocused;var commandLine=Environment.GetCommandLineArgs();
                if(Array.IndexOf(commandLine,"-foundryGreybox")>=0){gameObject.AddComponent<FoundryGreybox>();enabled=false;return;}
                if(Array.IndexOf(commandLine,"-twoLocalHumans")>=0&&!BypassTwoLocalOnce){gameObject.AddComponent<OfflineTwoLocalBootstrap>();enabled=false;return;}
                BypassTwoLocalOnce=false;
                if(Array.IndexOf(commandLine,"-threeCrossingsEvidence")>=0){gameObject.AddComponent<ThreeCrossingsInspection>();enabled=false;return;}
                profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if(Array.IndexOf(commandLine,"-orbitalHudEvidence")>=0)
                {
                    // Diagnostic economy only: expose occupied queues promptly; never a shipping default.
                    var fixture=profile.CopyData();fixture.startingCredits=10000;fixture.factoryBuildSeconds=1;fixture.scienceBuildSeconds=1;
                    profile=PlayableProfile.Create(fixture,profile.AuthoredMap,"UI review");
                }
#endif
                // Render cadence belongs to this prototype profile; the domain remains 30 Hz.
                QualitySettings.vSyncCount=0;Application.targetFrameRate=profile.RenderTargetFramesPerSecond;
                AudioListener.volume=NativeUserSettings.Volume;
                Application.runInBackground=true;
                var args=Environment.GetCommandLineArgs();for(int i=0;i+1<args.Length;i++)if(args[i]=="-playableEvidence")evidence=args[i+1];
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                responsiveUiEvidence=Array.IndexOf(args,"-responsiveUiEvidence")>=0;
                battleHudUnattended=Array.IndexOf(args,"-battleHudEvidence")>=0;
                for(int i=0;i+1<args.Length;i++)if(args[i]=="-resultUiEvidence")resultUiEvidence=args[i+1];
                for(int i=0;i+1<args.Length;i++)if(args[i]=="-resultActionEvidence")resultActionEvidence=args[i+1];
                styleASceneEvidence=Array.IndexOf(args,"-styleASceneEvidence")>=0;
                if(resultActionEvidence!=null&&resultActionEvidence!="restart"&&resultActionEvidence!="exit"&&resultActionEvidence!="manual")throw new ArgumentException("Unsupported result action evidence mode.");
#endif
                if(evidence!=null){Directory.CreateDirectory(evidence);metrics=new StreamWriter(Path.Combine(evidence,"native-live.csv"));metrics.WriteLine("elapsed,generation,tick,tick_cpu_ms,tick_interval_ms,command_ms,command_backlog,nav_requests,nav_answers,frame_ms,units,credits,paused,outcome,errors,heap_bytes,nav_pending,missed_deadlines,max_tick_cpu_ms,main_update_ms,max_main_update_ms,max_steady_frame_ms,long_steady_frames,gc0_count,render_frame,buildings,pending_buildings,ready_outposts,ready_mines,income_per_second,fog_targets,fog_uploads,fog_scans,map_uploads,memory_models,own_science,active_refinery_upgrades,upgraded_refineries");actions=new StreamWriter(Path.Combine(evidence,"native-input.txt"));routeFrames=new NativeRouteFrameTelemetry(Path.Combine(evidence,"native-route-frames.csv"));}
                started=Time.realtimeSinceStartup;
                InitializeMusic();InitializeGameplayAudio();
                CreateWorld();CreateHud();CreateSpectatorHud();
                input=gameObject.AddComponent<PlayableInput>();
                input.Capture=()=>{if(evidence!=null){CaptureProductionSnapshot(++captures);ScreenCapture.CaptureScreenshot(Path.Combine(evidence,"native-"+captures.ToString("D2")+".png"));Record("capture "+captures);}};
                input.Select=Select;input.Order=Order;input.Stop=()=>Submit(PlayableCommandKind.Stop);input.Hold=()=>Submit(PlayableCommandKind.Hold);
                input.CancelContext=()=>{if(mapOpen){CloseMap();return true;}return CloseBattleContext();};input.TogglePause=()=>Pause(!paused);input.Restart=Restart;input.FocusLost=()=>{if(!battleHudUnattended)Pause(true);};
                input.IsPointerOverUi=OverUi;input.IsKeyboardInUi=()=>MenuOwnsInput||spectatorMode&&root?.focusController?.focusedElement is VisualElement focus&&focus!=root||root?.focusController?.focusedElement is Button||root?.focusController?.focusedElement is TextField;
                input.MapAt=MapAt;input.MapSelect=MapSelect;input.MapOrder=MapOrder;input.ToggleMap=ToggleMap;input.CloseMap=CloseMap;
                input.Pan=Pan;input.Zoom=Zoom;input.Drag=DrawDrag;
                BindKeyboardCommands();
                input.AttackModeChanged=active=>{commandCursor?.Set(false);notice="";};
                for(int i=0;i+1<args.Length;i++)if(args[i]=="-lobbyEvidence")lobbyEvidence=args[i+1];
                for(int i=0;i+1<args.Length;i++)if(args[i]=="-matchSeed"){lobbySetup.ExplicitSeed=int.Parse(args[i+1],System.Globalization.CultureInfo.InvariantCulture);lobbySetup.HasExplicitSeed=true;}
                CreateLobby();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                for(int i=0;i+1<args.Length;i++)if(args[i]=="-orbitalUiEvidence")orbitalEvidence=args[i+1];
                if(orbitalEvidence!=null)StartCoroutine(CaptureOrbitalUiEvidence());
#endif
                // Evidence must enter through the current menu/lobby lifecycle too.
                if(evidence!=null&&lobbyEvidence==null)StartCoroutine(StartEvidenceMatch());
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if(lobbyEvidence!=null&&Array.IndexOf(args,"-lobbyManual")<0)StartCoroutine(CaptureLobbyEvidence());
                if(evidence!=null&&Array.IndexOf(args,"-battleHudEvidence")>=0)StartCoroutine(CaptureBattleHudEvidence());
#endif
            }catch(Exception ex){Debug.LogException(ex);enabled=false;}
        }
        private void CreateWorld()
        {
            RenderSettings.ambientLight=new Color(.63f,.69f,.73f);
            if(localCoordinator==null){var sun=new GameObject("Sun").AddComponent<Light>();sun.transform.SetParent(transform);sun.type=LightType.Directional;sun.intensity=1.4f;sun.transform.rotation=Quaternion.Euler(48,-30,0);NativeDisplaySettings.ConfigureSun(sun);}
            cameraView=new GameObject("Command camera").AddComponent<Camera>();cameraView.transform.SetParent(transform);cameraView.orthographic=true;cameraView.orthographicSize=(float)profile.CameraOrthoSize;
            cameraView.transform.position=new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);cameraView.transform.LookAt(Vector3.zero);
            if(profile.AuthoredMap!=null&&matchSetup?.Spectator!=true){var start=profile.Headquarters(LocalOwner);cameraView.transform.position+=new Vector3((float)start.X,0,(float)start.Z);}
            RememberKeyboardCamera();
            cameraView.nearClipPlane=.1f;cameraView.farClipPlane=300;cameraView.backgroundColor=new Color(.045f,.075f,.10f);cameraView.clearFlags=CameraClearFlags.SolidColor;
            world=new PlayableWorld(transform,profile){OwnerPaint=LobbyPaint};
            // Authored map uses the same profile coordinates as the domain.
            if(profile.AuthoredMap==null)foreach(var obstacle in PlayableMap.SolidObstacles(profile))world.Obstacle(obstacle);

        }
        private void CreateHud()
        {
            var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");panel.scaleMode=PanelScaleMode.ConstantPixelSize;
            var doc=localCoordinator==null?gameObject.AddComponent<UIDocument>():localCoordinator.GetComponent<UIDocument>();if(localCoordinator==null){doc.panelSettings=panel;ownedPanelSettings=panel;}else{Destroy(panel);panel=doc.panelSettings;}
            panelRoot=doc.rootVisualElement;root=new VisualElement{name="local-seat-hud"};root.style.position=Position.Absolute;root.style.left=root.style.right=root.style.top=root.style.bottom=0;panelRoot.Add(root);if(localCoordinator==null)OrbitalTheme.ConfigurePanel(panel,panelRoot);root.pickingMode=PickingMode.Ignore;root.focusable=true;root.tabIndex=-1;OrbitalTheme.Install(root);menuNavigation=new NativeMenuNavigation(root,()=>SeatPad,()=>WorldPadOwnsToolkit,()=>SeatUsesKeyboard){Focused=()=>localInputFocused};
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
            buildFactory=Button("⬡ Фабрика",()=>BuildSelected(PlayableBuildingKind.Factory));buildActions.Add(buildFactory);
            buildRefinery=Button("⬡ Переработка",()=>BuildSelected(PlayableBuildingKind.Refinery));buildActions.Add(buildRefinery);
            buildScience=Button("Научный центр",()=>BuildSelected(PlayableBuildingKind.ScientificCenter));buildActions.Add(buildScience);
            buildCenter=Button("⬡ Построить",()=>{var site=view.Sites.FirstOrDefault(x=>x.Site.Id==selectedSite);if(site!=null)BuildSelected(site.Site.Kind);});buildActions.Add(buildCenter);
            cancelBuilding=Button("Отменить стройку",()=>Submit(PlayableCommandKind.CancelBuilding));buildActions.Add(cancelBuilding);
            armyRegion=new VisualElement();armyRegion.style.flexGrow=1;armyRegion.style.flexBasis=0;armyRegion.style.minWidth=380;armyRegion.style.marginLeft=14;StyleRegion(armyRegion);armyRegion.name="battle-context";hudRow.Add(armyRegion);
            selectionLabel=new Label("Выберите фабрику или танки");selectionLabel.style.fontSize=18;selectionLabel.style.unityFontStyleAndWeight=FontStyle.Bold;selectionLabel.style.whiteSpace=WhiteSpace.Normal;selectionLabel.style.marginBottom=12;armyRegion.Add(selectionLabel);
            var productionActions=new VisualElement{name="production-actions"};productionActions.style.display=DisplayStyle.None;productionActions.style.flexDirection=FlexDirection.Row;armyRegion.Add(productionActions);
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
            modalCard.Add(OrbitalTheme.Action("Управление",OpenPauseHelp,"pause-controls"));
            modalCard.Add(pauseSettings=OrbitalTheme.Action("Настройки",OpenPauseSettings,"pause-settings"));
            modalRestartButton=Button("Начать заново",Restart);modalCard.Add(modalRestartButton);
            modalCard.Add(pauseMain=OrbitalTheme.Action("В главное меню",ReturnToMainMenu,"pause-main"));
            modalExitButton=Button("Выйти",Quit);modalCard.Add(modalExitButton);modalLobbyButton=Button("В лобби",ReturnToLobby);modalLobbyButton.style.display=DisplayStyle.None;CreatePostMatchActions();
            foreach(var action in modalCard.Query<Button>().ToList()){action.style.marginRight=0;action.style.height=44;action.style.minHeight=44;action.style.fontSize=18;action.style.marginBottom=7;}
            OrbitalTheme.Footer(modalCard,true);
            resumeButton.AddToClassList("orbital-primary");modalRestartButton.AddToClassList("orbital-primary");
            dragBox=new VisualElement();dragBox.pickingMode=PickingMode.Ignore;dragBox.style.position=Position.Absolute;dragBox.style.backgroundColor=new Color(.15f,.85f,.75f,.12f);dragBox.style.borderLeftWidth=1;dragBox.style.borderRightWidth=1;dragBox.style.borderTopWidth=1;dragBox.style.borderBottomWidth=1;dragBox.style.borderLeftColor=Color.cyan;dragBox.style.borderRightColor=Color.cyan;dragBox.style.borderTopColor=Color.cyan;dragBox.style.borderBottomColor=Color.cyan;dragBox.style.display=DisplayStyle.None;root.Add(dragBox);
            root.RegisterCallback<GeometryChangedEvent>(_=>ApplyResponsiveHud());
            root.RegisterCallback<KeyDownEvent>(evt=>
            {
                if(menuNavigation.Active)return;
                if(spectatorMode&&evt.keyCode==KeyCode.Tab&&!ModalVisible()&&root.focusController?.focusedElement!=root){ToggleMap();}
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
        private bool OverUi(Vector2 p){if(cameraView!=null&&!cameraView.pixelRect.Contains(p))return true;if(root?.panel==null)return false;var q=RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(p.x,Screen.height-p.y));return (spectatorMode&&spectatorPanel!=null&&spectatorPanel.worldBound.Contains(q))||top.worldBound.Contains(q)||compactMap.parent.worldBound.Contains(q)||armyRegion.worldBound.Contains(q)||(armyComposition.style.display.value==DisplayStyle.Flex&&armyComposition.worldBound.Contains(q))||(battleRing.style.display.value==DisplayStyle.Flex&&battleRing.ContainsPanelPoint(q))||(ResultsVisible&&postMatch.Root.worldBound.Contains(q))||(modal.style.display==DisplayStyle.Flex&&modal.worldBound.Contains(q));}
        private void DrawDrag(Vector2 a,Vector2 b,bool active){if(root?.panel==null)return;dragBox.style.display=active?DisplayStyle.Flex:DisplayStyle.None;var x=PanelPoint(a);var y=PanelPoint(b);dragBox.style.left=Mathf.Min(x.x,y.x);dragBox.style.top=Mathf.Min(x.y,y.y);dragBox.style.width=Mathf.Abs(x.x-y.x);dragBox.style.height=Mathf.Abs(x.y-y.y);}
        private void StartSession(){ResetSpectatorPresentation();gameplayAudio?.ResetAudio();keyboardGroups.Reset();ResetKeyboardCamera();ResetPostMatch();ClearArtilleryEffects();confirmSaleBuilding=0;routeService?.Dispose();routeService=new UnityHostRouteService(routeLane);world.Clear();CloseMap();input?.ClearMode();foreach(var shell in shells.Values)Destroy(shell);shells.Clear();runtime=matchSetup==null?PlayableRuntime.CreateHumanMatch(profile,++generation,19092026):PlayableRuntime.CreateLobbyMatch(profile,++generation,matchSetup,startPaused:preparing);sequence=noticeSequence=0;selection.Clear();selectedSite=selectedSlot=0;paused=false;restarting=false;modalWasVisible=false;root.Focus();notice="";ConfigureLocalPresentations();Record("start");}
        private void Select(Vector2 from,Vector2 to,bool shift){if(spectatorMode){inspectionEntityId=Pick(to,false);ValidateInspection();return;}try{SelectCore(from,to,shift);}finally{SelectionMarkerEvent();}}
        private void SelectCore(Vector2 from,Vector2 to,bool shift)
        {
            if(view==null)return;runtime?.RecordHumanAction(LocalOwnerId);battleDismissed=false;battleRally=false;confirmSaleBuilding=0;root.Focus();selectedSite=selectedSlot=0;bool box=(to-from).magnitude>profile.SelectionDragPixels;
            if(box)
            {
                var ids=view.Entities.Where(e=>e.Owner==view.Owner&&e.Health>0).Where(e=>{var p=cameraView.WorldToScreenPoint(world.Point(e.Position));return p.z>0&&p.x>=Mathf.Min(from.x,to.x)&&p.x<=Mathf.Max(from.x,to.x)&&p.y>=Mathf.Min(from.y,to.y)&&p.y<=Mathf.Max(from.y,to.y);}).Select(e=>e.Id).ToArray();
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
                var center=cameraView.WorldToScreenPoint(world.Point(e.Position)+Vector3.up*.5f);
                var rim=cameraView.WorldToScreenPoint(world.Point(e.Position)+Vector3.right*(float)(PlayableUnitRules.Radius(profile,e.Kind)*profile.TargetPickRadiusMultiplier)+Vector3.up*.5f);
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
            if(SelectedRallyProducer()!=null)Submit(PlayableCommandKind.SetRally,new[]{SelectedRallyProducer().Id},target);
            else if(!attackMode && view.Entities.Any(e=>e.Id==id&&!IsOpponent(e.Owner))) { if(!append)Submit(PlayableCommandKind.Follow,null,target,id); }
            else if(!enemy&&view.Buildings.Any(b=>b.Id==id&&!IsOpponent(b.Owner)))return;
            else Submit(enemy?PlayableCommandKind.Attack:attackMode?PlayableCommandKind.AttackMove:PlayableCommandKind.Move,null,target,enemy?id:0,mode:append?PlayableOrderMode.Append:PlayableOrderMode.Replace);
        }
        private void Submit(PlayableCommandKind kind,int[] ids=null,NavPoint target=default(NavPoint),int targetId=0,long productionOrderId=0,PlayableEntityKind unitKind=PlayableEntityKind.Tank,PlayableResearchKind researchKind=PlayableResearchKind.TankChassis,PlayableOrderMode mode=PlayableOrderMode.Replace)
        {
            if(runtime==null||paused||restarting||quitting||matchSetup?.Spectator==true)return;
            var command=new PlayableCommand(generation,++sequence,LocalOwnerId,kind,ids??selection.ToArray(),target,targetId:targetId,productionOrderId:productionOrderId,unitKind:unitKind,researchKind:researchKind,mode:mode);
            var result=runtime.TrySubmit(command);AudioCommand(command,result.Accepted);if(result.Accepted)orderMarkers.Submitted(view,command);
            if(mode!=PlayableOrderMode.Append||!result.Accepted){noticeSequence=sequence;notice=result.Accepted?"":"Не удалось отправить приказ: "+Friendly(result.Status);}
            Record(kind+" ids="+string.Join(",",ids??selection.ToArray())+" target="+target.X+","+target.Z+" order="+productionOrderId+" admission="+result.Status);
        }
        private string Friendly(PlayableCommandStatus status){switch(status){case PlayableCommandStatus.Accepted:return "";case PlayableCommandStatus.Cancelled:return "";case PlayableCommandStatus.Applied:return "";case PlayableCommandStatus.InsufficientCredits:return "Недостаточно кредитов";case PlayableCommandStatus.OccupiedPad:return "Площадка занята";case PlayableCommandStatus.InvalidEntity:return "Недоступный объект для приказа";case PlayableCommandStatus.InvalidTarget:return "Недоступная цель";case PlayableCommandStatus.Overflow:return "Слишком много приказов, повторите";default:return "Приказ отклонён";}}
        private void Pause(bool value){if(localCoordinator!=null){localCoordinator.PauseFrom(this,value);return;}if(runtime==null||restarting||quitting||(!value&&matchSetup!=null&&!matchSetup.Spectator&&localPresentations.Any(seat=>!seat.SeatReady)))return;if(runtime.Result!=null){resultOverview=false;ShowPostMatch();return;}confirmSaleBuilding=0;finishConfirmation=false;if(finishMatch!=null)finishMatch.text="Завершить матч";paused=value;BroadcastPause(value);if(!value){CloseChildMenu();menuNavigation?.SetScope(null);}runtime.RequestPause(value);input?.ClearMode();CloseMap();Record("pause="+value);}
        private void Restart(){if(localCoordinator!=null){localCoordinator.Restart();return;}if(runtime==null||restarting||quitting)return;Record("restart requested");restarting=true;runtime.RequestStop();}
        private void Quit(){if(localCoordinator!=null){localCoordinator.Quit();return;}if(quitting)return;Record("exit requested");quitting=true;runtime?.RequestStop();}
        private void Pan(Vector2 axis){cameraView.transform.position+=NativeCameraScrollSettings.Displacement(cameraView.transform,axis,NativeCameraScrollSettings.ArrowSpeed(profile.CameraPanSpeed),Time.unscaledDeltaTime);Record("camera pan");}
        private void Zoom(float delta){cameraView.orthographicSize=Mathf.Clamp(cameraView.orthographicSize-delta*(float)profile.CameraZoomSpeed,(float)profile.CameraMinZoom,(float)profile.CameraMaxZoom);Record("camera zoom");}
        private void Update()
        {
            if(root==null)return;
            long begin=System.Diagnostics.Stopwatch.GetTimestamp();
            // Diagnostic thresholds: discard startup's first five seconds and count >100ms frames.
            // These define evidence buckets, not gameplay or presentation tuning.
            if(Time.realtimeSinceStartup-started>=5){double ms=Time.unscaledDeltaTime*1000;maxSteadyFrameMs=Math.Max(maxSteadyFrameMs,ms);if(ms>100)longSteadyFrames++;}
            try{UpdateFrame();}
            finally{UpdateMusic();UpdateGameplayAudio();lastMainUpdateMs=(System.Diagnostics.Stopwatch.GetTimestamp()-begin)*1000d/System.Diagnostics.Stopwatch.Frequency;maxMainUpdateMs=Math.Max(maxMainUpdateMs,lastMainUpdateMs);RecordRouteFrame();}
        }
        private void UpdateFrame()
        {
            try{UpdateFrameState();}
            finally{SyncSystemCursor();}
        }
        private void UpdateFrameState()
        {
            TickMenuInput();UpdateLobby();
            if(returningToLobby&&(runtime==null||runtime.IsStopped)){DisposeLocalChildren();ResetLocalViewport();ResetPostMatch();returningToLobby=false;runtime=null;view=null;world.Clear();selection.Clear();CloseMap();if(returningToMain){returningToMain=false;ShowMainMenu();}else ShowLobby();return;}
            if(inLobby&&!preparing){
                ClearIncomeMarkers();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if(resultEvidenceStage>=91)DriveResultUiEvidence();
#endif
                return;
            }
            if(runtime==null)return;
            if(runtime.IsStopped){if(quitting){Flush();Application.Quit();return;}if(restarting)StartSession();}
            var captured=matchSetup?.Participants!=null&&!matchSetup.Spectator?runtime.OfflineFrame:null;ReadPresentationFrame();if(view==null)return;
            RefreshLocalKeyboardBinding();input.WorldInputEnabled=childMenu==null&&!inLobby&&!preparing&&!returningToLobby&&SeatReady&&!paused&&!restarting&&!quitting&&(view.Outcome==PlayableMatchOutcome.Playing||resultOverview)&&!ResultsVisible&&string.IsNullOrEmpty(view.Failure);input.CommandInputEnabled=input.WorldInputEnabled&&matchSetup?.Spectator!=true;
            RebindPresentation();ServiceRoutes();Render();UpdateHud();UpdateMaps();UpdateLifecycleMarkers();UpdateIncomeMarkers();PollBattleController();UpdateKeyboardPresentation();
            var allReceipts=runtime.DrainReceipts().ToArray();var markerReceipts=allReceipts.Where(r=>r.OwnerId==LocalOwnerId).ToArray();foreach(var receipt in markerReceipts){if(receipt.Sequence>=noticeSequence&&!(receipt.Status==PlayableCommandStatus.Applied&&receipt.Message==null)){noticeSequence=receipt.Sequence;notice=Friendly(receipt.Status);}Record("receipt "+receipt.Sequence+" "+receipt.Status+" latency_ms="+receipt.LatencyMilliseconds);}
            UpdateOrderMarkers(markerReceipts);PresentLocalChildren(captured,allReceipts);foreach(var receipt in allReceipts)gameplayAudio?.Receipt(receipt,generation);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            DriveResponsiveUiEvidence();
            DriveResultUiEvidence();
            DrivePrototypeResearchEvidence();
            DriveStyleASceneEvidence();
#endif
            if(view.Tick!=lastTick){lastTick=view.Tick;metrics?.WriteLine(string.Join(",",(Time.realtimeSinceStartup-started).ToString("F3",System.Globalization.CultureInfo.InvariantCulture),generation,view.Tick,view.Metrics.TickCpuMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),view.Metrics.TickIntervalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),view.Metrics.CommandLatencyMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),view.Metrics.CommandBacklog,runtime.Requests.Count,runtime.Answers.Count,Time.unscaledDeltaTime*1000,view.Entities.Count,view.Credits,paused,view.Outcome,view.Metrics.Errors,GC.GetTotalMemory(false),view.Metrics.NavigationPending,view.Metrics.MissedDeadlines,view.Metrics.MaximumTickCpu,lastMainUpdateMs,maxMainUpdateMs,maxSteadyFrameMs,longSteadyFrames,GC.CollectionCount(0),Time.frameCount,view.Buildings.Count,view.Buildings.Count(b=>b.Phase==ConstructionPhase.Pending),view.Buildings.Count(b=>b.Kind==PlayableBuildingKind.Outpost&&b.Phase==ConstructionPhase.Ready),view.Buildings.Count(b=>b.Kind==PlayableBuildingKind.Mine&&b.Phase==ConstructionPhase.Ready),view.IncomePerSecond,world.Fog.TargetBuilds,world.Fog.Uploads,world.Fog.ScannedFrames,mapTerrain.Uploads,world.MemoryCount,view.Buildings.Count(b=>b.Owner==LocalOwner&&b.Kind==PlayableBuildingKind.ScientificCenter&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.Lifecycle?.Selling!=true),view.Buildings.Count(b=>b.PrivateState?.Upgrade?.Active==true),view.Buildings.Count(b=>b.Owner==LocalOwner&&b.RefineryUpgraded)));if(view.Tick%30==0)Flush();}
        }
        private void RecordRouteFrame()
        {
            if(routeFrames==null||runtime==null||view==null)return;
            var host=routeService?.CertifiedPublication;var counters=host?.Counters;
            routeFrames.TryWrite(new NativeRouteFrameSample{
                Elapsed=Time.realtimeSinceStartup-started,Frame=Time.frameCount,Generation=view.Generation,Tick=view.Tick,Sequence=view.Sequence,
                FrameMs=Time.unscaledDeltaTime*1000,UpdateMs=lastMainUpdateMs,Units=runtime.DiagnosticGlobalUnits,Buildings=runtime.DiagnosticGlobalBuildings,
                BarrierPolls=runtime.BarrierPolls,Reuses=runtime.BarrierPayloadReuses,Builds=runtime.BarrierPayloadPublications,Pumps=host?.Pumps??0,
                PrepareMs=host?.PreparationElapsedMilliseconds??0,ProviderMs=host?.ProviderElapsedMilliseconds??0,SolveMs=host?.SolveElapsedMilliseconds??0,
                ProjectionMs=host?.ProjectionElapsedMilliseconds??0,TransportWaitMs=host?.TransportWaitMilliseconds??0,TransportHoldMs=host?.TransportHoldMilliseconds??0,
                PublishMs=runtime.PayloadBuildElapsedMilliseconds,Pending=counters?.PendingSubscriptions??0,Ready=counters?.ReadyResults??0,
                QueueAge=counters?.PeakQueueAge??0,Retained=host?.RetainedBytes??0,PeakRetained=host?.PeakRetainedBytes??0,
                Heap=GC.GetTotalMemory(false),GC0=GC.CollectionCount(0),MainAllocated=GC.GetAllocatedBytesForCurrentThread(),WorkerAllocated=host?.AllocatedBytes??0});
        }
        private void ServiceRoutes()=>routeService.Service(runtime,profile.NavigationRequestsPerFrame);
        private void Render()
        {
            world.Fog.Update(view.Vision,Time.unscaledDeltaTime);world.RenderMemories(view,cameraView);world.RenderSites(view,cameraView);
            var alive=new HashSet<int>();
            foreach(var e in view.Entities){alive.Add(e.Id);if(!world.Actors.TryGetValue(e.Id,out var a))a=world.Tank(e.Id,e.Owner==LocalOwner,e.Kind);world.UpdateResearchModel(a,e);a.Root.position=world.Point(e.Position);var slope=profile.AuthoredMap?.SurfaceGradient(e.Position)??default(NavPoint);a.Root.rotation=Quaternion.FromToRotation(Vector3.up,new Vector3((float)-slope.X,1,(float)-slope.Z).normalized);a.Hull.localRotation=Quaternion.Euler(0,90-(float)e.HullHeading*Mathf.Rad2Deg,0);a.Turret.localRotation=Quaternion.Euler(0,(float)(e.HullHeading-e.TurretHeading)*Mathf.Rad2Deg,0);a.Selection.SetActive(selection.Contains(e.Id));if(matchSetup!=null)PlayableWorld.PaintOwner(a,LobbyPaint(e.Owner));PlayableWorld.UpdateHealth(a,cameraView,(float)e.Health/PlayableUnitRules.Health(profile,e.Kind),selection.Contains(e.Id));}
            foreach(var b in view.Buildings){alive.Add(b.Id);if(b.Phase==ConstructionPhase.Pending)continue;if(!world.Actors.TryGetValue(b.Id,out var a))a=world.Building(b.Id,b.Kind.ToString(),b.Owner==LocalOwner,b.RefineryUpgraded);world.UpdateRefineryModel(a,b,view.Tick);a.Root.position=world.Point(b.Position);a.Root.rotation=Quaternion.Euler(0,-(float)b.Heading*Mathf.Rad2Deg,0);world.FaceBuilding(a,cameraView);a.Root.localScale=new Vector3(1,Mathf.Lerp(.2f,1,(float)b.Progress),1);a.Selection.SetActive(selection.Contains(b.Id));if(matchSetup!=null)PlayableWorld.PaintOwner(a,LobbyPaint(b.Owner));int max=TerritoryRules.Health(profile,b.Kind);PlayableWorld.UpdateHealth(a,cameraView,(float)b.Health/max);}
            foreach(var id in world.Actors.Keys.ToArray())if(!alive.Contains(id)){world.Remove(id);selection.Remove(id);}
            world.RenderRallyFlags(view,selection,matchSetup?.Spectator==true);
            RenderProjectiles();
        }
        private void UpdateHud()
        {
            bool spectator=matchSetup?.Spectator==true;
            if(spectator)objective.text="ЗРИТЕЛЬ";
            else if(matchSetup?.Foundry==true)objective.text="ЧЁРНАЯ ПЛАВИЛЬНЯ · A1–A3 против B1–B3";else if(matchSetup!=null)objective.text=matchSetup.MatchHumanName+" · Команда "+matchSetup.HumanTeam+"  /  "+matchSetup.MatchAiName+" · Команда "+matchSetup.AiTeam;
            creditsLabel.text=view.Credits+"   +"+view.IncomePerSecond.ToString("0.#")+"/с";
            UpdateSiteHud();
            var factory=view.Buildings.FirstOrDefault(b=>(spectatorMode?b.Id==inspectionEntityId:selection.Contains(b.Id)&&b.Owner==LocalOwner)&&b.Kind==PlayableBuildingKind.Factory);
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
            noticeLabel.text=notice;modal.style.display=childMenu==null&&LocalMenuVisible&&(paused&&runtime?.Result==null||view.Outcome!=PlayableMatchOutcome.Playing&&runtime?.Result==null||restarting||!string.IsNullOrEmpty(view.Failure))?DisplayStyle.Flex:DisplayStyle.None;
            modalCard.Q("pause-controls").style.display=view.Outcome==PlayableMatchOutcome.Playing&&!restarting?DisplayStyle.Flex:DisplayStyle.None;pauseSettings.style.display=view.Outcome==PlayableMatchOutcome.Playing&&!restarting?DisplayStyle.Flex:DisplayStyle.None;pauseMain.style.display=DisplayStyle.None;
            modalExitButton.style.display=view.Outcome==PlayableMatchOutcome.Playing?DisplayStyle.None:DisplayStyle.Flex;
            resumeButton.style.display=view.Outcome==PlayableMatchOutcome.Playing&&!restarting?DisplayStyle.Flex:DisplayStyle.None;
            bool plainPause=paused&&view.Outcome==PlayableMatchOutcome.Playing&&!restarting&&string.IsNullOrEmpty(view.Failure);
            modalCard.Q("match-menu-eyebrow").style.display=plainPause?DisplayStyle.None:DisplayStyle.Flex;
            modalCaption.style.display=plainPause?DisplayStyle.None:DisplayStyle.Flex;modalTitle.style.marginBottom=plainPause?20:8;
            modalTitle.text=!string.IsNullOrEmpty(view.Failure)?"ОШИБКА СИМУЛЯЦИИ":restarting?"НОВЫЙ БОЙ…":view.Outcome==PlayableMatchOutcome.PlayerWon||view.Outcome==PlayableMatchOutcome.TeamWon?"ПОБЕДА":view.Outcome==PlayableMatchOutcome.PlayerLost?"ЦЕНТРЫ ПОТЕРЯНЫ":"Пауза";
            modalCaption.text=plainPause?"":(!string.IsNullOrEmpty(view.Failure)?"Матч остановлен":restarting?"Подготовка нового матча":view.Outcome==PlayableMatchOutcome.PlayerWon?"Противник побеждён":view.Outcome==PlayableMatchOutcome.PlayerLost?"Ваши центры уничтожены":"Бой приостановлен")+"\nПрофиль: "+profile.DisplayName+" · "+view.ProfileRevision;modalCaption.style.whiteSpace=WhiteSpace.Normal;
            if(!plainPause&&!string.IsNullOrEmpty(runtime?.BalanceApplyStatus))modalCaption.text+="\n"+runtime.BalanceApplyStatus;
            if(!plainPause&&matchSetup!=null)modalCaption.text+="\n"+matchSetup.MatchHumanName+"  /  "+matchSetup.MatchAiName;
            modalTitle.style.color=!string.IsNullOrEmpty(view.Failure)||view.Outcome==PlayableMatchOutcome.PlayerLost?Danger:view.Outcome==PlayableMatchOutcome.PlayerWon?Cyan:Ink;
            SyncModalFocus();
            UpdatePostMatch();
            UpdateResponsiveVisibility();
            if(spectator)
            {
                objective.style.display=DisplayStyle.Flex;creditsLabel.style.display=spectatorPerspective.HasValue?DisplayStyle.Flex:DisplayStyle.None;hudFocusButton.style.display=DisplayStyle.None;
                armyComposition.style.display=buildRegion.style.display=commandRegion.style.display=DisplayStyle.None;armyRegion.style.display=inspectionEntityId!=0?DisplayStyle.Flex:DisplayStyle.None;battleRing.style.display=DisplayStyle.None;
                noticeLabel.text="";noticeLabel.style.display=DisplayStyle.None;
                UpdateSpectatorHud();
            }
            else UpdateSpectatorHud();
        }
        private void Record(string message){actions?.WriteLine((Time.realtimeSinceStartup-started).ToString("F3")+" generation="+generation+" tick="+(view?.Tick??0)+" "+message);actions?.Flush();}
        private void Flush(){metrics?.Flush();actions?.Flush();}
        private void OnApplicationQuit(){runtime?.RequestStop();Flush();}
        private void OnDestroy(){SetSystemCursorHidden(false);if(startupLoadingHost)Destroy(startupLoadingHost);if(startupLoadingPanel)Destroy(startupLoadingPanel);DisposeLocalChildren();if(ownedPanelSettings)Destroy(ownedPanelSettings);if(localCoordinator!=null){runtime=null;root?.RemoveFromHierarchy();}if(cameraView!=null)Destroy(cameraView.gameObject);if(padWhiteArrow)Destroy(padWhiteArrow);if(padRedArrow)Destroy(padRedArrow);commandCursor?.Dispose();menuNavigation?.Dispose();if(nativeMainMenu!=null)Destroy(nativeMainMenu.gameObject);ClearArtilleryEffects();if(artilleryTransparent)Destroy(artilleryTransparent);projectileVisuals.Dispose();lobbyTerrain?.Dispose();mapTerrain?.Dispose();world?.Dispose();runtime?.RequestStop();routeService?.Dispose();routeFrames?.Dispose();metrics?.Dispose();actions?.Dispose();}
    }
}
