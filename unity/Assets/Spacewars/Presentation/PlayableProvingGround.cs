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
        private NativeBalanceView laboratoryPage;
        private Vector3 provingReturnCameraPosition;
        private Quaternion provingReturnCameraRotation;
        private float provingReturnCameraZoom;
        private ProvingGroundSetup provingGround;
        private GameplayAudioProfile provingGroundAudio;
        private bool provingGroundBattle,provingGroundBusy;
        private VisualElement provingGroundLayer,provingLeft,provingRight,provingActions;
        private Label provingStatus;
        private int provingSide;
        private PlayableEntityKind? provingUnit;
        private PlayableBuildingKind? provingBuilding;
        private readonly string[] provingTabs={"Юниты","Юниты"};
        private void OpenProvingGround(PlayableProfile snapshot,GameplayAudioProfile audio)
        {
            if(provingGroundBusy||provingGround!=null)return;
            provingReturnCameraPosition=keyboardHomePosition;provingReturnCameraRotation=keyboardHomeRotation;provingReturnCameraZoom=keyboardHomeZoom;
            audio.Validate();provingGround=new ProvingGroundSetup(snapshot);provingGroundAudio=audio;
            laboratoryPage.Page.style.display=DisplayStyle.None;childMenu=null;menuNavigation.SetScope(null);
            campaignConfiguration=null;matchSetup=null;provingGroundBattle=false;
            RebuildProvingGround(true);
        }
        private void RebuildProvingGround(bool terrain=false)
        {
            if(provingGroundBusy||provingGround==null)return;
            provingGroundBusy=true;provingGroundLayer?.SetEnabled(false);preparing=preparingPresentation=true;
            input.WorldInputEnabled=input.CommandInputEnabled=false;
            StartCoroutine(LoadingPreparation.Run(PrepareProvingGround(terrain),FailProvingGround));
        }
        private IEnumerator PrepareProvingGround(bool terrain)
        {
            runtime?.RequestStop();while(runtime!=null&&!runtime.IsStopped)yield return null;
            profile=provingGround.Profile;NativeGameplayAudioProfile.UseSession(provingGroundAudio);
            var cameraPosition=cameraView.transform.position;var cameraRotation=cameraView.transform.rotation;float cameraZoom=cameraView.orthographicSize;
            StartSessionCore(false);paused=true;
            if(!terrain){cameraView.transform.SetPositionAndRotation(cameraPosition,cameraRotation);cameraView.orthographicSize=cameraZoom;}
            if(terrain){loadingScreen.Q<Label>("loading-status").text="Подготовка полигона…";loadingScreen.style.display=DisplayStyle.Flex;loadingScreen.BringToFront();yield return PrepareMapPresentation();yield return PrepareLocalPresentations();}
            while(!runtime.IsPrepared){if(runtime.IsStopped)throw new InvalidOperationException(runtime.Latest?.Failure??"Полигон остановлен.");yield return null;}
            ReadPresentationFrame();cameraView.enabled=true;
            if(terrain){
                // Use the same profile framing as an ordinary match, focused on the left headquarters.
                cameraView.orthographicSize=(float)profile.CameraOrthoSize;
                cameraView.transform.position=new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);
                cameraView.transform.LookAt(Vector3.zero);
                var home=profile.Headquarters(LocalOwner);
                cameraView.transform.position+=new Vector3((float)home.X,0,(float)home.Z);RememberKeyboardCamera();
            }
            ServiceRoutes();Render();UpdateMaps();
            inLobby=false;preparing=preparingPresentation=false;provingGroundBusy=false;
            loadingScreen.style.display=DisplayStyle.None;SetMatchUi(false);CreateProvingGroundUi();
        }
        private void FailProvingGround(Exception error)
        {
            Debug.LogException(error);preparing=preparingPresentation=false;provingGroundBusy=false;
            loadingScreen.style.display=DisplayStyle.None;notice="Ошибка полигона: "+error.Message;
            if(provingStatus!=null)provingStatus.text=notice;
            ExitProvingGround();
        }
        private void CreateProvingGroundUi()
        {
            provingGroundLayer?.RemoveFromHierarchy();provingGroundLayer=new VisualElement{name="proving-ground",pickingMode=PickingMode.Ignore};
            provingGroundLayer.style.position=Position.Absolute;provingGroundLayer.style.left=provingGroundLayer.style.right=provingGroundLayer.style.top=provingGroundLayer.style.bottom=0;root.Add(provingGroundLayer);
            provingLeft=provingRight=null;provingStatus=null;
            if(provingGroundBattle){
                provingActions=new VisualElement{name="proving-actions"};provingActions.style.position=Position.Absolute;provingActions.style.left=6;provingActions.style.top=6;provingActions.style.flexDirection=FlexDirection.Row;
                provingGroundLayer.Add(provingActions);
                var reset=Button("Вернуть расстановку",ResetProvingGround);reset.name="proving-reset";
                var laboratory=Button("В лабораторию",ExitProvingGround);laboratory.name="proving-exit";
                foreach(var action in new[]{reset,laboratory}){action.style.fontSize=12;action.style.height=action.style.minHeight=26;action.style.marginTop=action.style.marginBottom=0;action.style.marginRight=6;provingActions.Add(action);}
                provingGroundLayer.BringToFront();return;
            }
            provingLeft=ProvingPanel(0);provingRight=ProvingPanel(1);
            provingActions=Panel();provingActions.name="proving-actions";provingActions.style.left=Length.Percent(28);provingActions.style.right=Length.Percent(28);provingActions.style.top=12;provingGroundLayer.Add(provingActions);
            provingStatus=LobbyText("ПОДГОТОВКА · время остановлено",16);provingStatus.name="proving-status";provingActions.Add(provingStatus);
            provingActions.Add(Button("Начать бой",StartProvingGround));
            provingActions.Add(Button("Вернуть расстановку",ResetProvingGround));
            provingActions.Add(Button("Вернуться в лабораторию",ExitProvingGround));
            provingGroundLayer.BringToFront();
        }
        private VisualElement ProvingPanel(int side)
        {
            var panel=Panel();panel.name="proving-side-"+side;panel.style.top=12;panel.style.width=Length.Percent(24);panel.style.maxHeight=Length.Percent(76);
            if(side==0)panel.style.left=12;else panel.style.right=12;provingGroundLayer.Add(panel);
            panel.Add(LobbyText("ИГРОК "+(side+1),20,side==0?new Color(.4f,.75f,1):new Color(1,.5f,.45f)));
            var costs=LobbyText("Армия: "+provingGround.ArmyCost(side).ToString("N0")+"\nИсследования: "+provingGround.ResearchCost(side).ToString("N0")+"\nВсего: "+(provingGround.ArmyCost(side)+provingGround.ResearchCost(side)).ToString("N0"),15);costs.name="proving-cost-"+side;panel.Add(costs);
            panel.Add(LobbyText("Кредиты: "+profile.ProvingGroundStartingCredits.ToString("N0"),14));
            var tabs=new DropdownField(new System.Collections.Generic.List<string>{"Юниты","Здания","Исследования"},provingTabs[side]){name="proving-tab-"+side};panel.Add(tabs);
            tabs.RegisterValueChangedCallback(e=>{provingTabs[side]=e.newValue;provingUnit=null;provingBuilding=null;CreateProvingGroundUi();});
            var list=OrbitalTheme.Scroll();list.style.flexShrink=1;list.style.minHeight=0;panel.Add(list);
            if(provingTabs[side]=="Юниты"){
                foreach(PlayableEntityKind kind in Enum.GetValues(typeof(PlayableEntityKind))){if(!PlayableUnitRules.Supported(kind))continue;var k=kind;
                    var b=Button(UnitName(k)+" · "+PlayableUnitRules.Cost(profile,k),()=>{provingSide=side;provingUnit=k;provingBuilding=null;provingStatus.text="Игрок "+(side+1)+": "+UnitName(k)+" — укажите точку на своей половине";});b.name="proving-unit-"+side+"-"+kind;list.Add(b);}
                list.Add(Button("Убрать последний юнит",()=>{var u=provingGround.Units.LastOrDefault(x=>x.LogicalPlayer==side+1);if(u!=null){provingGround.Units.Remove(u);RebuildProvingGround();}}));
                list.Add(Button("Очистить юнитов стороны",()=>{provingGround.Units.RemoveAll(u=>u.LogicalPlayer==side+1);RebuildProvingGround();}));
            }else if(provingTabs[side]=="Здания"){
                foreach(var kind in ProvingGroundSetup.Buildable){var k=kind;list.Add(Button(BuildingName(k),()=>{provingSide=side;provingBuilding=k;provingUnit=null;provingStatus.text="Игрок "+(side+1)+": "+BuildingName(k)+" — выберите свободный слот";}));}
                list.Add(Button("Убрать последнее здание",()=>{var b=provingGround.Buildings.LastOrDefault(x=>x.LogicalPlayer==side+1);if(b!=null){provingGround.Buildings.Remove(b);RebuildProvingGround();}}));
            }else{
                foreach(PlayableResearchKind kind in Enum.GetValues(typeof(PlayableResearchKind))){
                    var k=kind;string title=PlayableResearchRules.Name(k)+" · "+PlayableResearchRules.Cost(profile,k).ToString("N0");
                    var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;row.style.alignItems=Align.Center;row.style.marginBottom=12;row.style.flexShrink=0;row.style.width=Length.Percent(100);list.Add(row);
                    var toggle=new Toggle(){name="proving-research-"+side+"-"+k,tooltip=title,value=provingGround.Research(side).Contains(k)};
                    toggle.style.width=32;toggle.style.minWidth=32;toggle.style.maxWidth=32;toggle.style.flexShrink=0;toggle.style.marginRight=8;toggle.style.marginBottom=0;
                    var check=toggle.Q(className:"unity-toggle__input");check.style.paddingLeft=check.style.paddingRight=0;check.style.minWidth=0;check.style.flexGrow=1;
                    row.Add(toggle);var label=new Label(title);label.style.whiteSpace=WhiteSpace.Normal;label.style.flexGrow=1;label.style.flexShrink=1;label.style.flexBasis=0;label.style.minWidth=0;row.Add(label);
                    label.RegisterCallback<ClickEvent>(_=>toggle.value=!toggle.value);
                    toggle.RegisterValueChangedCallback(e=>{provingGround.SetResearch(side,k,e.newValue);RebuildProvingGround();});
                }
            }
            return panel;
        }
        private bool ProvingGroundOverUi(Vector2 point)=>provingGround!=null&&(provingLeft?.worldBound.Contains(point)==true||provingRight?.worldBound.Contains(point)==true||provingActions?.worldBound.Contains(point)==true);
        private void TickProvingGround()
        {
            if(provingGround==null||provingGroundBusy)return;
            if(!provingGroundBattle){
                var mouse=Mouse.current;var keyboard=Keyboard.current;
                if(localInputFocused&&mouse!=null&&!ProvingGroundOverUi(PanelPoint(mouse.position.ReadValue()))){
                    float wheel=mouse.scroll.ReadValue().y;if(wheel!=0)Zoom(wheel);
                    if(keyboard!=null){var axis=new Vector2((keyboard.rightArrowKey.isPressed?1:0)-(keyboard.leftArrowKey.isPressed?1:0),(keyboard.upArrowKey.isPressed?1:0)-(keyboard.downArrowKey.isPressed?1:0));if(axis.sqrMagnitude>0)Pan(axis);}
                }
                if(localInputFocused&&mouse?.leftButton.wasReleasedThisFrame==true&&!ProvingGroundOverUi(PanelPoint(mouse.position.ReadValue())))PlaceProvingGround(Ground(mouse.position.ReadValue()));
                if(localInputFocused&&Keyboard.current?.escapeKey.wasPressedThisFrame==true){provingUnit=null;provingBuilding=null;}
            }
        }
        private void PlaceProvingGround(NavPoint point)
        {
            if(provingGroundBusy||provingGroundBattle||provingGround==null)return;
            string error;
            if(provingUnit.HasValue)error=provingGround.PlaceUnit(provingSide,provingUnit.Value,point);
            else if(provingBuilding.HasValue)error=provingGround.PlaceBuilding(provingSide,provingBuilding.Value,point);
            else return;
            if(error!=null){provingStatus.text=error;return;}RebuildProvingGround();
        }
        private void StartProvingGround()
        {
            if(provingGroundBusy||provingGroundBattle||runtime==null)return;
            if(!runtime.StartProvingGroundBattle()){provingStatus.text="Не удалось начать бой.";return;}
            provingGroundBattle=true;paused=menuPaused=false;sequence=Math.Max(sequence,1);provingUnit=null;provingBuilding=null;
            SetMatchUi(true);CreateProvingGroundUi();menuNavigation.SetScope(null);root.Focus();
        }
        private void ResetProvingGround(){if(provingGroundBusy||provingGround==null)return;provingGroundBattle=false;menuNavigation.SetScope(null);RebuildProvingGround();}
        private void ExitProvingGround()
        {
            if(provingGround==null||provingGroundBusy)return;
            provingGroundBusy=true;input.WorldInputEnabled=input.CommandInputEnabled=false;menuNavigation.SetScope(null);
            StartCoroutine(ReturnFromProvingGround());
        }
        private IEnumerator ReturnFromProvingGround()
        {
            runtime?.RequestStop();while(runtime!=null&&!runtime.IsStopped)yield return null;
            runtime=null;view=null;routeService?.Dispose();routeService=null;DisposeLocalChildren();ResetLocalViewport();ResetPostMatch();world.Clear();selection.Clear();CloseMap();
            provingGroundLayer?.RemoveFromHierarchy();provingGroundLayer=null;provingGround=null;provingGroundBattle=provingGroundBusy=false;
            inLobby=true;paused=menuPaused=false;matchSetup=null;keyboardHomePosition=provingReturnCameraPosition;keyboardHomeRotation=provingReturnCameraRotation;keyboardHomeZoom=provingReturnCameraZoom;ResetKeyboardCamera();SetMatchUi(false);NativeGameplayAudioProfile.UseSession(balanceStore.ActiveAudio);
            childMenu=laboratoryPage.Page;laboratoryPage.RestoreFromProvingGround();
        }
    }
}
