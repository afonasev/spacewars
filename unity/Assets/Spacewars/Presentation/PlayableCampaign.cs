using System;
using System.Collections;
using System.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private OfflineMatchConfiguration campaignConfiguration;
        private Label campaignGoal,campaignHistory,campaignRadioText;
        private ScrollView campaignRadio;
        private Button campaignRetry;
        private VisualElement campaignTutorialLayer,campaignTutorialCard;
        private Label campaignTutorialTitle,campaignTutorialCopy;
        private Button campaignTutorialDismiss;
        private bool campaignTutorialShowing;
        private int campaignTutorialShowingTopic=-1;
        private int campaignTutorialLastDeviceTopic=-2;
        private string campaignTutorialDeviceKey="";
        private int campaignTutorialPendingShow=-1,campaignTutorialPendingDismiss=-1;
        private byte[] campaignRetryBytes;
        private bool campaignRetryRequested;
        private PlayableRuntime CreateSessionRuntime(long nextGeneration)
        {
            if(provingGround!=null)return new PlayableRuntime(provingGround.Configuration(),nextGeneration,startPaused:true);
            if(campaignConfiguration==null)return matchSetup==null?PlayableRuntime.CreateHumanMatch(profile,nextGeneration,19092026):PlayableRuntime.CreateLobbyMatch(profile,nextGeneration,matchSetup,startPaused:preparing);
            if(campaignRetryRequested&&campaignRetryBytes!=null){campaignRetryRequested=false;return PlayableRuntime.RestoreBytes(campaignRetryBytes,campaignConfiguration);}
            campaignRetryBytes=null;return new PlayableRuntime(campaignConfiguration,nextGeneration,startPaused:preparing);
        }
        private void LaunchCampaign()
        {
            if(preparing)return;
            inLobby=true;matchSetup=null;menuNavigation?.SetScope(null);preparing=preparingPresentation=true;cameraView.enabled=false;
            loadingScreen.Q<Label>("loading-status").text="Подготовка миссии…";panelRoot.Add(loadingScreen);loadingScreen.style.display=DisplayStyle.Flex;
            StartCoroutine(LoadingPreparation.Run(PrepareCampaign(),FailLobbyPreparation));
        }
        private IEnumerator PrepareCampaign()
        {
            yield return null;EnsureBalanceStore();
            profile=PlayableProfile.Create(balanceStore.Active.CopyData(),new CampaignOpeningMap(),"Спорная территория");
            campaignConfiguration=CampaignReconOpening.Configuration(profile);campaignRetryRequested=false;
            StartSessionCore(false);paused=true;
            yield return PrepareMapPresentation();
            while(!runtime.IsPrepared){if(runtime.IsStopped)throw new InvalidOperationException(runtime.Latest?.Failure??"Campaign preparation stopped.");yield return null;}
            yield return PrepareLocalPresentations();cameraView.enabled=true;
            ReadPresentationFrame();ServiceRoutes();Render();UpdateHud();UpdateMaps();yield return null;
            inLobby=false;preparing=preparingPresentation=false;loadingScreen.style.display=DisplayStyle.None;SetMatchUi(true);Pause(false);
        }
        private void CreateCampaignHud()
        {
            campaignGoal=OrbitalTheme.Text("","orbital-body");campaignGoal.name="campaign-current-goal";campaignGoal.style.position=Position.Absolute;campaignGoal.style.left=20;campaignGoal.style.top=72;campaignGoal.style.maxWidth=360;campaignGoal.style.paddingLeft=12;campaignGoal.style.paddingRight=12;campaignGoal.style.paddingTop=10;campaignGoal.style.paddingBottom=10;campaignGoal.style.backgroundColor=PanelColor;campaignGoal.style.whiteSpace=WhiteSpace.Normal;campaignGoal.style.display=DisplayStyle.None;root.Add(campaignGoal);
            campaignRadio=new ScrollView(ScrollViewMode.Vertical){name="campaign-radio"};campaignRadio.style.position=Position.Absolute;campaignRadio.style.left=20;campaignRadio.style.bottom=110;campaignRadio.style.width=540;campaignRadio.style.maxWidth=Length.Percent(48);campaignRadio.style.maxHeight=136;campaignRadio.style.backgroundColor=PanelColor;campaignRadio.style.paddingLeft=12;campaignRadio.style.paddingRight=12;campaignRadio.style.display=DisplayStyle.None;root.Add(campaignRadio);
            campaignRadioText=OrbitalTheme.Text("","orbital-body");campaignRadioText.name="campaign-radio-history";campaignRadioText.style.whiteSpace=WhiteSpace.Normal;campaignRadio.Add(campaignRadioText);
            campaignHistory=OrbitalTheme.Text("","orbital-body");campaignHistory.name="campaign-revealed-goals";campaignHistory.style.width=280;campaignHistory.style.flexShrink=0;campaignHistory.style.marginRight=20;campaignHistory.style.whiteSpace=WhiteSpace.Normal;campaignHistory.style.display=DisplayStyle.None;tacticalOverlay.Insert(0,campaignHistory);
            campaignRetry=Button("С контрольной точки",RetryCampaign);campaignRetry.name="campaign-retry-checkpoint";campaignRetry.style.display=DisplayStyle.None;modalCard.Insert(modalCard.IndexOf(modalRestartButton),campaignRetry);
            campaignTutorialLayer=new VisualElement{name="campaign-tutorial-layer"};campaignTutorialLayer.style.position=Position.Absolute;campaignTutorialLayer.style.left=0;campaignTutorialLayer.style.right=0;campaignTutorialLayer.style.top=0;campaignTutorialLayer.style.bottom=0;campaignTutorialLayer.style.alignItems=Align.Center;campaignTutorialLayer.style.justifyContent=Justify.Center;campaignTutorialLayer.style.backgroundColor=new Color(0,0,0,.52f);campaignTutorialLayer.style.display=DisplayStyle.None;campaignTutorialLayer.pickingMode=PickingMode.Position;root.Add(campaignTutorialLayer);
            campaignTutorialCard=OrbitalTheme.Card(campaignTutorialLayer,"campaign-tutorial-card");campaignTutorialCard.style.width=520;campaignTutorialCard.style.maxWidth=Length.Percent(90);campaignTutorialCard.style.paddingTop=22;campaignTutorialCard.style.paddingBottom=18;campaignTutorialCard.style.paddingLeft=24;campaignTutorialCard.style.paddingRight=24;
            campaignTutorialTitle=OrbitalTheme.Text("БРИФИНГ","orbital-title");campaignTutorialTitle.name="campaign-tutorial-title";campaignTutorialCard.Add(campaignTutorialTitle);
            campaignTutorialCopy=OrbitalTheme.Text("");campaignTutorialCopy.name="campaign-tutorial-copy";campaignTutorialCopy.style.whiteSpace=WhiteSpace.Normal;campaignTutorialCopy.style.marginTop=12;campaignTutorialCopy.style.marginBottom=16;campaignTutorialCard.Add(campaignTutorialCopy);
            campaignTutorialDismiss=OrbitalTheme.Action("Продолжить",DismissCampaignTutorial,"campaign-tutorial-dismiss",true);campaignTutorialCard.Add(campaignTutorialDismiss);
            root.RegisterCallback<KeyDownEvent>(e=>{if(!campaignTutorialShowing)return;if(e.keyCode==KeyCode.Escape&&campaignTutorialShowingTopic!=7){Pause(true);e.StopImmediatePropagation();}else if(e.keyCode==KeyCode.Escape||e.keyCode==KeyCode.Return||e.keyCode==KeyCode.Space){DismissCampaignTutorial();e.StopImmediatePropagation();}});
        }
        private void RetryCampaign()
        {
            var bytes=runtime?.CampaignCheckpoint??campaignRetryBytes;if(bytes==null||restarting)return;
            campaignRetryBytes=bytes;campaignRetryRequested=true;restarting=true;runtime.RequestStop();
        }
        private void UpdateCampaignHud()
        {
            var mission=view?.Mission;bool active=mission!=null&&!inLobby&&!preparing;
            campaignGoal.style.display=active&&!mapOpen?DisplayStyle.Flex:DisplayStyle.None;
            campaignHistory.style.display=active&&mapOpen?DisplayStyle.Flex:DisplayStyle.None;
            campaignRetry.style.display=active&&(runtime.HasCampaignCheckpoint||campaignRetryBytes!=null)?DisplayStyle.Flex:DisplayStyle.None;
            // Modal results, pause and tutorial cards own the foreground; retain radio history underneath.
            bool radioVisible=active&&!mapOpen&&view.Outcome==PlayableMatchOutcome.Playing&&!menuPaused&&!restarting&&string.IsNullOrEmpty(view.Failure)&&!mission.TutorialPauseHeld;
            campaignRadio.style.display=radioVisible?DisplayStyle.Flex:DisplayStyle.None;
            float mapHeight=compactMap.parent.resolvedStyle.height;if(!float.IsNaN(mapHeight))campaignRadio.style.bottom=mapHeight+20;
            if(!active){world?.SetCampaignGate(false);tacticalOverlay.style.flexDirection=FlexDirection.Column;if(mission==null&&!spectatorMode)creditsLabel.style.display=DisplayStyle.Flex;return;}
            paused=menuPaused||mission.TutorialPauseHeld;
            if((mission.TutorialSeen&0x80)==0&&input!=null)input.WorldInputEnabled=input.CommandInputEnabled=false;
            tacticalOverlay.style.flexDirection=FlexDirection.Row;
            world?.SetCampaignGate(!mission.GateOpen);
            campaignRadioText.text=string.Join("\n",mission.Radio.Select(line=>"›  "+line));
            campaignRadio.schedule.Execute(()=>campaignRadio.scrollOffset=new Vector2(0,Mathf.Max(0,campaignRadio.contentContainer.resolvedStyle.height-campaignRadio.contentViewport.resolvedStyle.height))).StartingIn(1);
            if(mission.Victory&&view.Outcome==PlayableMatchOutcome.PlayerWon&&PlayerPrefs.GetInt("campaign.disputed-territory.completed",0)!=1){PlayerPrefs.SetInt("campaign.disputed-territory.completed",1);PlayerPrefs.Save();}
            campaignGoal.text="ТЕКУЩАЯ ЦЕЛЬ\n"+string.Join("\n",mission.Objectives.Where(g=>!g.Complete).Select(g=>g.Text));
            campaignHistory.text="СПОРНАЯ ТЕРРИТОРИЯ\n\n"+string.Join("\n\n",mission.Objectives.Select(g=>(g.Complete?"✓ ":"• ")+g.Text));
            creditsLabel.style.display=buildRegion.style.display=DisplayStyle.None;
            compactMap.SetCampaignGoals(mission.Objectives);tacticalMap.SetCampaignGoals(mission.Objectives);
            UpdateCampaignTutorial(mission);
        }
        private void UpdateCampaignTutorial(CampaignMissionView mission)
        {
            if(runtime==null)return;
            if(view==null||view.Tick<=0){campaignTutorialShowing=false;campaignTutorialShowingTopic=-1;campaignTutorialLayer.style.display=DisplayStyle.None;return;}
            bool padActive=battleController&&SeatPad?.added==true;
            int topic=mission.TutorialQueued;
            if(campaignTutorialPendingDismiss>=0){if(topic==campaignTutorialPendingDismiss){campaignTutorialShowing=false;campaignTutorialShowingTopic=-1;campaignTutorialLayer.style.display=DisplayStyle.None;return;}campaignTutorialPendingDismiss=-1;}
            if(campaignTutorialPendingShow>=0){if(topic==campaignTutorialPendingShow)campaignTutorialPendingShow=-1;else{campaignTutorialShowing=false;campaignTutorialShowingTopic=-1;campaignTutorialLayer.style.display=DisplayStyle.None;return;}}
            if(topic<0)
            {
                if((mission.TutorialSeen&0x80)==0){campaignTutorialPendingShow=7;runtime.RequestCampaignTutorialControl(generation,new CampaignTutorialControl(CampaignTutorialControlKind.Show,7));}
                else if(padActive){int next=TutorialTopic(mission);if(next>=0){campaignTutorialPendingShow=next;runtime.RequestCampaignTutorialControl(generation,new CampaignTutorialControl(CampaignTutorialControlKind.Show,next));}}
                campaignTutorialShowing=false;campaignTutorialShowingTopic=-1;campaignTutorialLayer.style.display=DisplayStyle.None;return;
            }
            if(topic!=7&&!padActive)
            {
                if(mission.TutorialPauseHeld)runtime.RequestCampaignTutorialControl(generation,new CampaignTutorialControl(CampaignTutorialControlKind.SuspendForDevice));
                campaignTutorialShowing=false;campaignTutorialShowingTopic=-1;campaignTutorialLayer.style.display=DisplayStyle.None;return;
            }
            if(topic!=7&&mission.TutorialPauseHeld==false)runtime.RequestCampaignTutorialControl(generation,new CampaignTutorialControl(CampaignTutorialControlKind.ResumeForDevice));
            if(topic!=7&&campaignTutorialShowing&&campaignTutorialShowingTopic==topic&&NonPadInputChanged())
            {battleController=false;runtime.RequestCampaignTutorialControl(generation,new CampaignTutorialControl(CampaignTutorialControlKind.SuspendForDevice));campaignTutorialShowing=false;campaignTutorialShowingTopic=-1;campaignTutorialLayer.style.display=DisplayStyle.None;return;}
            campaignTutorialShowing=true;campaignTutorialShowingTopic=topic;campaignTutorialLayer.style.display=menuPaused?DisplayStyle.None:DisplayStyle.Flex;
            var pad=SeatPad;string deviceKey=pad==null?"none":pad.deviceId+":"+pad.layout+":"+pad.description.manufacturer;
            if(campaignTutorialLastDeviceTopic!=topic||campaignTutorialDeviceKey!=deviceKey){campaignTutorialTitle.text=topic==7?"СПОРНАЯ ТЕРРИТОРИЯ":TutorialTitle(topic);campaignTutorialCopy.text=topic==7?"Исследователь — лёгкая разведывательная боевая машина. Во Внешних мирах такие машины служат в корпоративных патрулях, полицейских подразделениях и отрядах специального назначения: они мобильны и вооружены, но плохо переносят огонь тяжёлой техники.":TutorialCopy(topic,pad);campaignTutorialLastDeviceTopic=topic;campaignTutorialDeviceKey=deviceKey;}
            if(!menuPaused&&menuNavigation!=null)menuNavigation.SetScope(campaignTutorialCard,()=>Pause(true),campaignTutorialDismiss);
        }
        private int TutorialTopic(CampaignMissionView m)
        {
            int seen= m.TutorialSeen|m.TutorialCompleted;
            bool Has(int i)=>(seen&(1<<i))!=0;
            if(!Has(0))return 0;if((m.TutorialCompleted&1)!=0&&!Has(1))return 1;if(m.Reinforced&&!Has(2))return 2;
            if(m.MineDestroyed&&!Has(4))return 4;if(m.FirstCombat&&!Has(3))return 3;
            if(m.ForkRevealed&&!Has(5))return 5;if(m.FinalReinforced&&!Has(6))return 6;return -1;
        }
        private static string TutorialTitle(int i)=>new[]{"ВЫДЕЛЕНИЕ","ПЕРЕМЕЩЕНИЕ","ВЫБОР ОТРЯДА","АТАКА","АТАКА С ПРОДВИЖЕНИЕМ","НАЗНАЧЕНИЕ ГРУПП","ВСЯ БОЕВАЯ АРМИЯ"}[i];
        private static string TutorialCopy(int i,Gamepad pad)
        {
            string A=NativeControllerGlyph.Symbol("A",pad),B=NativeControllerGlyph.Symbol("B",pad),Y=NativeControllerGlyph.Symbol("Y",pad);
            string family=(pad?.layout+" "+pad?.description.manufacturer).ToLowerInvariant();bool sony=family.Contains("dual")||family.Contains("sony"),nintendo=family.Contains("switch")||family.Contains("nintendo");string shoulder=sony?"R1":nintendo?"R":"RB",trigger=sony?"R2":nintendo?"ZR":"RT";
            return new[]{A+" — выбрать машину; удерживайте для выделения области.",B+" — отправить выбранных к указателю.","Удерживайте "+shoulder+" и выберите сектор, чтобы переключиться на отряд.",B+" на видимом противнике — атаковать.",Y+" — двигаться в атаке к точке.","Удерживайте "+trigger+", выберите сектор отряда и отпустите, чтобы назначить группу.","Быстро нажмите "+shoulder+" дважды, чтобы выбрать всю боевую армию."}[i];
        }
        private bool NonPadInputChanged()
        {
            var keyboard=Keyboard.current;var mouse=input?.AssignedMouse??Mouse.current;
            return keyboard!=null&&(keyboard.wKey.wasPressedThisFrame||keyboard.aKey.wasPressedThisFrame||keyboard.sKey.wasPressedThisFrame||keyboard.dKey.wasPressedThisFrame||keyboard.enterKey.wasPressedThisFrame||keyboard.escapeKey.wasPressedThisFrame||keyboard.spaceKey.wasPressedThisFrame||keyboard.leftArrowKey.wasPressedThisFrame||keyboard.rightArrowKey.wasPressedThisFrame||keyboard.upArrowKey.wasPressedThisFrame||keyboard.downArrowKey.wasPressedThisFrame)||
                mouse?.added==true&&(mouse.delta.ReadValue().sqrMagnitude>0||mouse.leftButton.wasPressedThisFrame||mouse.rightButton.wasPressedThisFrame||mouse.middleButton.wasPressedThisFrame);
        }
        private void DismissCampaignTutorial()
        {
            if(!campaignTutorialShowing||runtime==null)return;int topic=campaignTutorialShowingTopic;campaignTutorialPendingDismiss=topic;campaignTutorialShowing=false;campaignTutorialShowingTopic=-1;campaignTutorialLayer.style.display=DisplayStyle.None;menuNavigation?.SetScope(null);
            if(topic==7)runtime.RequestCampaignTutorialControl(generation,new CampaignTutorialControl(CampaignTutorialControlKind.Complete,topic));
            runtime.RequestCampaignTutorialControl(generation,new CampaignTutorialControl(CampaignTutorialControlKind.Dismiss,topic));
        }
        private void CompleteCampaignTutorialTopic(int topic){runtime?.RequestCampaignTutorialControl(generation,new CampaignTutorialControl(CampaignTutorialControlKind.Complete,topic));}
    }
}
