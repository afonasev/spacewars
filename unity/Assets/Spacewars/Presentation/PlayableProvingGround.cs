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
        private VisualElement provingGroundLayer,provingLeft,provingRight;
        private Label provingStatus;
        private VisualElement provingUnitDetail;
        private int provingSide;
        private PlayableEntityKind? provingUnit;
        private VisualElement provingConstruction,provingMapParent;
        private Button provingPhaseButton;
        private int provingMapIndex;
        private bool ProvingPreparation=>provingGround!=null&&!provingGroundBattle&&!provingGroundBusy;
        private void RestoreProvingMap()
        {
            if(provingMapParent==null)return;
            var frame=compactMap.parent;frame.RemoveFromHierarchy();provingMapParent.Insert(Math.Min(provingMapIndex,provingMapParent.childCount),frame);
            frame.style.position=Position.Relative;frame.style.left=frame.style.bottom=StyleKeyword.Auto;
            provingMapParent=null;
        }
        private void CloseProvingConstruction(){provingConstruction?.RemoveFromHierarchy();provingConstruction=null;}

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
            loadingScreen.style.display=DisplayStyle.None;paused=false;runtime.RequestPause(false);SetMatchUi(true);CreateProvingGroundUi();
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
            RestoreProvingMap();CloseProvingConstruction();
            provingGroundLayer?.RemoveFromHierarchy();provingGroundLayer=new VisualElement{name="proving-ground",pickingMode=PickingMode.Ignore};
            provingGroundLayer.style.position=Position.Absolute;provingGroundLayer.style.left=provingGroundLayer.style.right=provingGroundLayer.style.top=provingGroundLayer.style.bottom=0;root.Add(provingGroundLayer);
            provingLeft=provingRight=null;provingStatus=null;provingUnitDetail=null;

            provingLeft=ProvingPanel(0);provingRight=ProvingPanel(1);
            var phaseRow=new VisualElement{pickingMode=PickingMode.Ignore};phaseRow.style.position=Position.Absolute;phaseRow.style.left=Length.Percent(26);phaseRow.style.right=Length.Percent(26);phaseRow.style.top=8;phaseRow.style.alignItems=Align.Center;
            provingPhaseButton=Button(provingGroundBattle?"К расстановке":provingStartButton.text,()=>{if(provingGroundBattle)ResetProvingGround();else StartProvingGround();});
            provingPhaseButton.name="proving-phase";provingPhaseButton.style.width=200;provingPhaseButton.style.height=40;phaseRow.Add(provingPhaseButton);provingGroundLayer.Add(phaseRow);
            ProvingMouseOnly(phaseRow);
            provingStatus=LobbyText("ПОЛИГОН · свободное управление",14);provingStatus.name="proving-status";
            provingStatus.pickingMode=PickingMode.Ignore;provingStatus.style.position=Position.Absolute;
            provingStatus.style.left=Length.Percent(26);provingStatus.style.right=Length.Percent(26);provingStatus.style.top=70;
            provingStatus.style.whiteSpace=WhiteSpace.Normal;provingStatus.style.unityTextAlign=TextAnchor.UpperCenter;provingGroundLayer.Add(provingStatus);
            if(provingGroundBattle){provingStatus.text="ПОЛИГОН · общие вложения в армию и исследования";return;}
            // The live sandbox uses the ordinary HUD row, which reserves space for
            // the minimap beside construction/production/research context cards.
            ProvingMouseOnly(provingGroundLayer);provingGroundLayer.BringToFront();root.Focus();
        }
        private VisualElement ProvingPanel(int side)
        {
            var panel=Panel();panel.name="proving-side-"+side;panel.style.top=8;panel.style.width=Length.Percent(20);panel.style.minWidth=280;panel.style.maxHeight=Length.Percent(62);
            panel.style.paddingLeft=panel.style.paddingRight=10;panel.style.paddingTop=panel.style.paddingBottom=8;
            if(side==0)panel.style.left=8;else panel.style.right=8;provingGroundLayer.Add(panel);
            var heading=LobbyText("ИГРОК "+(side+1),16,side==0?new Color(.4f,.75f,1):new Color(1,.5f,.45f));heading.style.marginBottom=4;heading.style.flexShrink=0;panel.Add(heading);
            var costs=LobbyText("",12);costs.style.marginBottom=6;costs.style.flexShrink=0;costs.name="proving-cost-"+side;panel.Add(costs);
            if(provingGroundBattle)return panel;
            var tabs=new VisualElement{name="proving-tab-"+side};tabs.style.flexDirection=FlexDirection.Row;tabs.style.flexShrink=0;tabs.style.marginBottom=6;panel.Add(tabs);
            foreach(var title in new[]{"Юниты","Исследования"}){
                var value=title;var tab=Button(title,()=>{provingTabs[side]=value;provingUnit=null;CreateProvingGroundUi();});
                tab.name="proving-tab-"+side+"-"+(title=="Юниты"?"units":"research");tab.style.flexGrow=1;tab.style.minWidth=0;tab.style.flexBasis=0;tab.style.whiteSpace=WhiteSpace.NoWrap;
                tab.style.fontSize=13;tab.style.paddingLeft=tab.style.paddingRight=8;tab.style.unityTextAlign=TextAnchor.MiddleCenter;
                tab.style.height=tab.style.minHeight=32;tab.style.flexShrink=0;tab.style.marginBottom=0;tab.style.marginRight=title=="Юниты"?6:0;
                bool active=provingTabs[side]==title;tab.EnableInClassList("proving-tab-active",active);
                tab.style.backgroundColor=active?new Color(.10f,.25f,.29f):OrbitalTheme.Panel;
                tab.style.color=active?OrbitalTheme.Cyan:OrbitalTheme.Muted;
                tab.style.borderTopColor=tab.style.borderBottomColor=tab.style.borderLeftColor=tab.style.borderRightColor=active?OrbitalTheme.Cyan:OrbitalTheme.Line;
                tab.style.unityFontStyleAndWeight=active?FontStyle.Bold:FontStyle.Normal;tabs.Add(tab);
            }
            var list=OrbitalTheme.Scroll();list.style.flexShrink=1;list.style.minHeight=0;panel.Add(list);
            if(provingTabs[side]=="Юниты"){
                foreach(PlayableEntityKind kind in Enum.GetValues(typeof(PlayableEntityKind))){if(!PlayableUnitRules.Supported(kind))continue;var k=kind;
                    var b=Button(UnitName(k),()=>{provingUnit=provingSide==side&&provingUnit==k?null:(PlayableEntityKind?)k;provingSide=side;provingStatus.text="Игрок "+(side+1)+": "+UnitName(k)+" — укажите точку на своей половине";});b.name="proving-unit-"+side+"-"+kind;b.style.flexShrink=0;b.style.height=b.style.minHeight=34;b.style.fontSize=14;b.style.marginBottom=4;
                    b.style.paddingLeft=36;b.style.unityTextAlign=TextAnchor.MiddleLeft;
                    var icon=new OrbitalGlyph(kind.ToString().ToLowerInvariant());icon.style.position=Position.Absolute;icon.style.left=6;icon.style.top=6;icon.style.width=24;icon.style.height=20;b.Add(icon);
                    b.RegisterCallback<PointerEnterEvent>(_=>ShowProvingUnitDetail(b,side,k));
                    b.RegisterCallback<PointerLeaveEvent>(_=>{if(provingUnitDetail!=null)provingUnitDetail.style.display=DisplayStyle.None;});list.Add(b);}
                foreach(var action in new[]{Button("Убрать последний юнит",()=>runtime.RemoveProvingGroundUnits(side,false)),Button("Очистить юнитов стороны",()=>runtime.RemoveProvingGroundUnits(side,true))}){
                    action.style.height=action.style.minHeight=30;action.style.fontSize=13;action.style.marginBottom=4;list.Add(action);
                }
            }else{
                foreach(PlayableResearchKind kind in Enum.GetValues(typeof(PlayableResearchKind))){
                    if(PlayableResearchRules.Local(kind))continue;
                    var k=kind;string title=PlayableResearchRules.Name(k);
                    var row=new VisualElement{name="proving-research-row-"+side+"-"+k};row.style.flexDirection=FlexDirection.Row;row.style.alignItems=Align.Center;row.style.marginBottom=4;row.style.minHeight=24;row.style.flexShrink=0;row.style.width=Length.Percent(100);list.Add(row);
                    var toggle=new Toggle(){name="proving-research-"+side+"-"+k,tooltip=title,value=ProvingView(side)?.OwnerResearch.Any(o=>o.Kind==k&&o.Complete)==true};
                    toggle.AddToClassList("proving-research");
                    toggle.style.width=20;toggle.style.minWidth=20;toggle.style.maxWidth=20;toggle.style.height=toggle.style.minHeight=toggle.style.maxHeight=22;toggle.style.flexShrink=0;toggle.style.marginLeft=toggle.style.marginTop=toggle.style.marginRight=toggle.style.marginBottom=0;toggle.style.paddingLeft=toggle.style.paddingRight=0;
                    var check=toggle.Q(className:"unity-toggle__input");check.style.paddingLeft=check.style.paddingRight=0;check.style.minWidth=0;check.style.flexGrow=1;check.style.marginLeft=check.style.marginRight=0;
                    var mark=toggle.Q(className:"unity-toggle__checkmark");mark.style.width=mark.style.minWidth=18;mark.style.height=mark.style.minHeight=18;mark.style.marginLeft=mark.style.marginRight=0;
                    row.Add(toggle);var label=new Label(ProvingResearchName(k)){name="proving-research-label-"+side+"-"+k};label.style.fontSize=13;label.style.marginLeft=2;label.style.marginRight=0;label.style.paddingLeft=label.style.paddingRight=0;label.style.whiteSpace=WhiteSpace.Normal;label.style.flexGrow=1;label.style.flexShrink=1;label.style.flexBasis=0;label.style.minWidth=0;row.Add(label);
                    label.RegisterCallback<ClickEvent>(_=>{if(toggle.enabledSelf)toggle.value=!toggle.value;});
                    row.RegisterCallback<PointerEnterEvent>(_=>ShowProvingDetail(row,side,title+"\n"+PlayableResearchRules.Description(profile,k)+(PlayableResearchRules.Previous(k).HasValue?"\nСначала исследуются предыдущие уровни.":"")+"\n\nПрименяется сразу · без зданий. Снимите галку, чтобы отменить."));
                    row.RegisterCallback<PointerLeaveEvent>(_=>{if(provingUnitDetail!=null)provingUnitDetail.style.display=DisplayStyle.None;});
                    toggle.RegisterValueChangedCallback(e=>{if(!runtime.SetProvingGroundResearch(side,k,e.newValue))provingStatus.text="Действие сейчас недоступно";});
                }
            }
            return panel;
        }
        private static string ProvingResearchName(PlayableResearchKind kind)=>kind switch {
            PlayableResearchKind.TankChassis=>"Танк: шасси",PlayableResearchKind.ExplorerAssaultGuns=>"Исследователь: орудия",
            PlayableResearchKind.ShkvalGuidance=>"Шквал: наведение",PlayableResearchKind.HawkSalvoSystems=>"Ястреб: залповый огонь",
            PlayableResearchKind.UltimateWeaponSystems=>"Урон III",PlayableResearchKind.UltimateDefensiveSystems=>"Здоровье III",
            PlayableResearchKind.ImprovedWeaponSystems=>"Урон I",PlayableResearchKind.AdvancedWeaponSystems=>"Урон II",
            PlayableResearchKind.ImprovedDefensiveSystems=>"Здоровье I",PlayableResearchKind.AdvancedDefensiveSystems=>"Здоровье II",_=>PlayableResearchRules.Name(kind)
        };
        private void ShowProvingUnitDetail(VisualElement button,int side,PlayableEntityKind kind)=>ShowProvingDetail(button,side,PlayableMenuDescriptions.Unit(profile,kind,ProvingView(side)?.OwnerResearch,freeEconomy:true));
        private void ShowProvingDetail(VisualElement button,int side,string description)
        {
            if(provingUnitDetail==null){
                provingUnitDetail=new VisualElement{name="proving-unit-detail",pickingMode=PickingMode.Ignore};
                provingUnitDetail.style.position=Position.Absolute;OrbitalPrecision.Frame(provingUnitDetail);provingGroundLayer.Add(provingUnitDetail);
                var text=new Label{name="proving-unit-description",pickingMode=PickingMode.Ignore};
                text.style.whiteSpace=WhiteSpace.Normal;text.style.color=OrbitalTheme.Ink;text.style.fontSize=13;
                text.style.paddingLeft=text.style.paddingRight=10;text.style.paddingTop=text.style.paddingBottom=6;provingUnitDetail.Add(text);
            }
            var label=provingUnitDetail.Q<Label>();label.text=description;
            float width=Mathf.Min(320,root.contentRect.width-24);
            float height=label.MeasureTextSize(label.text,width-20,VisualElement.MeasureMode.Exactly,0,VisualElement.MeasureMode.Undefined).y+16;
            var anchor=provingGroundLayer.WorldToLocal(button.worldBound.position);
            float left=side==0?provingLeft.layout.x+provingLeft.layout.width+8:provingRight.layout.x-width-8;
            provingUnitDetail.style.width=width;provingUnitDetail.style.left=Mathf.Clamp(left,6,Mathf.Max(6,root.contentRect.width-width-6));
            provingUnitDetail.style.top=Mathf.Clamp(anchor.y,6,Mathf.Max(6,root.contentRect.height-height-6));
            provingUnitDetail.style.display=DisplayStyle.Flex;provingUnitDetail.BringToFront();
        }
        private bool ProvingGroundOverUi(Vector2 point)=>provingGround!=null&&(provingLeft?.worldBound.Contains(point)==true||provingRight?.worldBound.Contains(point)==true||provingConstruction?.worldBound.Contains(point)==true||provingPhaseButton?.worldBound.Contains(point)==true);
        private static void ProvingMouseOnly(VisualElement panel)
        {
            // Keep pointer callbacks, but never let Toolkit focus/submit navigation capture camera keys.
            panel.focusable=false;panel.tabIndex=-1;
            foreach(var control in panel.Query<VisualElement>().ToList()){control.focusable=false;control.tabIndex=-1;}
        }
        private void CloseProvingMenu()
        {
            menuPaused=false;modal.style.display=DisplayStyle.None;CloseChildMenu();menuNavigation.SetScope(null);SyncModalFocus();root.Focus();
        }
        private void UpdateProvingMenu()
        {
            bool proving=provingGround!=null;
            provingStartButton.EnableInClassList("orbital-primary",proving&&!provingGroundBattle);
            provingStartButton.style.unityFontStyleAndWeight=FontStyle.Bold;
            resumeButton.EnableInClassList("orbital-primary",!proving||provingGroundBattle);
            modalRestartButton.EnableInClassList("orbital-primary",!proving);
            provingStartButton.style.display=proving&&!provingGroundBattle?DisplayStyle.Flex:DisplayStyle.None;
            provingLabButton.style.display=proving?DisplayStyle.Flex:DisplayStyle.None;
            modalRestartButton.text=proving?(provingGroundBattle?"К расстановке":"Сбросить"):"Начать заново";
            if(!proving){resumeButton.text="Продолжить";return;}
            modalRestartButton.style.display=DisplayStyle.Flex;pauseMain.style.display=modalExitButton.style.display=DisplayStyle.None;
            modalTitle.text="Полигон";resumeButton.text="Продолжить";
        }
        // Ordinary input handles maps/camera. This is the sole preparation world-click path.
        private NavPoint ProvingPlacementPoint(Vector2 screen)
        {
            if(provingUnit!=PlayableEntityKind.Hawk)return Ground(screen);
            var ray=cameraView.ScreenPointToRay(screen);
            // The proving map is flat. Test every possible clearance plane and keep
            // the nearest valid body center, including flight above buildings.
            double center=PlayableUnitRules.HawkBodyHeight(profile)/2;
            var heights=new[]{profile.HawkClearance+center}.Concat(view.Buildings.Where(b=>b.Phase!=ConstructionPhase.Pending).Select(b=>PlayableUnitRules.AirBuildingHeight(profile,b.Kind)+profile.HawkClearance+center));
            double nearest=double.PositiveInfinity;NavPoint result=new NavPoint(double.NaN,double.NaN);
            foreach(double height in heights){
                if(!new Plane(Vector3.up,new Vector3(0,(float)height,0)).Raycast(ray,out float distance)||distance>=nearest)continue;
                var at=ray.GetPoint(distance);var point=new NavPoint(at.x,at.z);double actual=profile.HawkClearance+center;
                foreach(var building in view.Buildings.Where(b=>b.Phase!=ConstructionPhase.Pending))
                    if(ProvingDistance(point,building.Position)<TerritoryRules.Radius(profile,building.Kind)+profile.HawkRadius)actual=Math.Max(actual,PlayableUnitRules.AirBuildingHeight(profile,building.Kind)+profile.HawkClearance+center);
                if(Math.Abs(actual-height)>1e-6)continue;
                nearest=distance;result=point;
            }
            return result;
        }
        private void PlaceProvingGround(NavPoint point)
        {
            if(!ProvingPreparation)return;
            if(!provingUnit.HasValue&&OpenProvingConstruction(point))return;
            CloseProvingConstruction();
            if(!provingUnit.HasValue)return;
            if(!runtime.PlaceProvingGroundUnit(provingSide,provingUnit.Value,point))provingStatus.text="Расстановка недоступна";
        }
        private bool OpenProvingConstruction(NavPoint point)
        {
            var sites=profile.AuthoredMap.Sites(profile);
            var building=view.Buildings.OrderBy(b=>ProvingDistance(b.Position,point)).FirstOrDefault(b=>ProvingDistance(b.Position,point)<=TerritoryRules.Radius(profile,b.Kind)+profile.BuildingPickRadius);
            var pads=sites.SelectMany(site=>site.Slots.Concat(site.DefenseSlots(profile)).Select(slot=>new{Site=site,Slot=slot})).OrderBy(p=>ProvingDistance(p.Slot.Position,point)).ToArray();
            var pad=pads.FirstOrDefault();
            bool free=pad!=null&&!view.Buildings.Any(b=>b.SiteId==pad.Site.Id&&b.SlotId==pad.Slot.Id)&&ProvingDistance(pad.Slot.Position,point)<=profile.BuildingPickRadius;
            if(free&&building!=null&&ProvingDistance(building.Position,point)<ProvingDistance(pad.Slot.Position,point))free=false;
            if(!free&&(building==null||building.Owner==LocalOwner))return false;
            CloseProvingConstruction();provingConstruction=Panel();provingConstruction.name="proving-construction";provingConstruction.style.width=280;provingConstruction.style.maxHeight=Length.Percent(80);
            provingGroundLayer.Add(provingConstruction);
            var anchor=free?pad.Slot.Position:building.Position;
            var screen=cameraView.WorldToScreenPoint(new Vector3((float)anchor.X,0,(float)anchor.Z));var at=PanelPoint(screen);
            provingConstruction.style.left=Mathf.Clamp(at.x,8,Mathf.Max(8,root.contentRect.width-288));
            provingConstruction.style.top=Mathf.Clamp(at.y,8,Mathf.Max(8,root.contentRect.height-330));
            int side=free?(pad.Site.Id-1)%2:(int)building.Owner;
            provingConstruction.Add(LobbyText("ИГРОК "+(side+1)+" · "+(free?"ПЛОЩАДКА":BuildingName(building.Kind)),16));
            if(free){
                foreach(var kind in ProvingGroundSetup.Buildable.Where(k=>TerritoryRules.DefenseSlot(pad.Slot.Id)==(TerritoryRules.Defensive(k)))){var k=kind;var position=pad.Slot.Position;
                    var button=Button(BuildingName(k)+" · бесплатно",()=>{ProvingCommand(side,PlayableCommandKind.BuildAt,site:pad.Site.Id,slot:pad.Slot.Id,buildingKind:k,parent:ProvingView(side).Buildings.First(b=>b.SiteId==pad.Site.Id&&b.SlotId==0).Id);CloseProvingConstruction();});
                    button.name="proving-build-"+kind;provingConstruction.Add(button);
                }
            }else{
                int site=building.SiteId,slot=building.SlotId;
                if(building.Kind==PlayableBuildingKind.Factory||building.Kind==PlayableBuildingKind.Hangar){
                    foreach(var kind in Enum.GetValues(typeof(PlayableEntityKind)).Cast<PlayableEntityKind>().Where(k=>PlayableUnitRules.Supported(k)&&(k==PlayableEntityKind.Hawk)==(building.Kind==PlayableBuildingKind.Hangar))){var k=kind;
                        provingConstruction.Add(Button(UnitName(k)+" · произвести",()=>ProvingCommand(side,k==PlayableEntityKind.Tank?PlayableCommandKind.QueueTank:k==PlayableEntityKind.Explorer?PlayableCommandKind.QueueExplorer:k==PlayableEntityKind.Shkval?PlayableCommandKind.QueueShkval:PlayableCommandKind.QueueHawk,new[]{building.Id})));}
                }

                if(building.Kind==PlayableBuildingKind.Refinery||building.Kind==PlayableBuildingKind.ScientificCenter||building.Kind==PlayableBuildingKind.Factory||building.Kind==PlayableBuildingKind.Hangar){
                    bool upgraded=building.ModelUpgraded||building.PrivateState?.Upgrade?.Complete==true;
                    var upgrade=Button(upgraded?"Улучшено ✓":"Улучшить · бесплатно",()=>{ProvingCommand(side,building.Kind==PlayableBuildingKind.ScientificCenter?PlayableCommandKind.QueueResearch:building.Kind==PlayableBuildingKind.Refinery?PlayableCommandKind.UpgradeRefinery:PlayableCommandKind.UpgradeProducer,new[]{building.Id},research:PlayableResearchKind.ScientificCenterUpgrade);CloseProvingConstruction();});
                    bool science=building.Kind==PlayableBuildingKind.ScientificCenter||(building.Kind==PlayableBuildingKind.Refinery?PlayableResearchRules.UpgradedCenterReady(ProvingView(side).Buildings,building.Owner):building.PrivateState?.Upgrade?.BlockedReason==null);
                    upgrade.name="proving-upgrade";upgrade.SetEnabled(!upgraded&&science);upgrade.tooltip=!science?(building.Kind==PlayableBuildingKind.Refinery?"Нужен готовый собственный улучшенный научный центр":building.PrivateState?.Upgrade?.BlockedReason??"Нужен готовый собственный Научный центр"):upgraded?"Здание уже улучшено":"Бесплатно · "+profile.ProvingGroundActionSeconds+" с";provingConstruction.Add(upgrade);
                }else provingConstruction.Add(LobbyText("У этого здания нет улучшения",14));
                if(slot!=0){var remove=Button("Убрать здание",()=>{ProvingCommand(side,building.Progress<1?PlayableCommandKind.CancelBuilding:PlayableCommandKind.SellBuilding,new[]{building.Id});CloseProvingConstruction();});remove.name="proving-remove-building";provingConstruction.Add(remove);}
            }
            var close=Button("Закрыть",()=>{CloseProvingConstruction();root.Focus();});provingConstruction.Add(close);ProvingMouseOnly(provingConstruction);provingConstruction.BringToFront();return true;
        }
        private PlayableSnapshot ProvingView(int side)=>runtime?.ParticipantView(side==0?"player-1":"enemy-1");
        private void ProvingCommand(int side,PlayableCommandKind kind,int[] ids=null,int site=0,int slot=0,PlayableBuildingKind buildingKind=PlayableBuildingKind.Headquarters,int parent=0,PlayableResearchKind research=PlayableResearchKind.TankChassis)
        {
            var command=new PlayableCommand(generation,++sequence,side==0?"player-1":"enemy-1",kind,ids,siteId:site,slotId:slot,buildingKind:buildingKind,parentId:parent,researchKind:research);
            if(!runtime.EditProvingGround(command))provingStatus.text="Действие сейчас недоступно";
        }
        private void UpdateProvingGroundUi()
        {
            if(provingGround==null||provingGroundBusy||runtime==null)return;
            for(int side=0;side<2;side++){
                var cost=runtime.ProvingGroundCosts(side);var label=provingGroundLayer?.Q<Label>("proving-cost-"+side);
                if(label!=null)label.text="Армия: "+cost.Army.ToString("N0")+"\nИсследования: "+cost.Research.ToString("N0")+"\nВсего: "+(cost.Army+cost.Research).ToString("N0");
                if(provingGroundBattle||provingTabs[side]!="Исследования")continue;
                var view=ProvingView(side);
                foreach(PlayableResearchKind kind in Enum.GetValues(typeof(PlayableResearchKind))){var toggle=provingGroundLayer?.Q<Toggle>("proving-research-"+side+"-"+kind);if(toggle==null)continue;
                    var order=view.OwnerResearch.FirstOrDefault(o=>o.Kind==kind);toggle.SetValueWithoutNotify(order!=null);toggle.SetEnabled(true);
                    toggle.tooltip=order?.Complete==true?"Применено · снимите галку для отмены":order?.Active==true?"Выполняется · "+(int)(order.Progress*100)+"%":order!=null?"В очереди":PlayableResearchRules.Name(kind);}
            }
            if(runtime.ProvingGroundEditStatus!=null&&provingStatus!=null)provingStatus.text=runtime.ProvingGroundEditStatus;
        }
        private static double ProvingDistance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
        private void StartProvingGround()
        {
            if(provingGroundBusy||provingGroundBattle||runtime==null)return;
            if(!runtime.StartProvingGroundBattle()){provingStatus.text="Не удалось начать бой.";return;}
            CloseProvingMenu();provingGroundBattle=true;paused=menuPaused=false;provingUnit=null;
            CloseMap();RestoreProvingMap();SetMatchUi(true);CreateProvingGroundUi();menuNavigation.SetScope(null);root.Focus();
        }
        private void ResetProvingGround(){if(provingGroundBusy||provingGround==null)return;CloseProvingMenu();if(provingGroundBattle)provingGround=runtime.ProvingGroundResetSetup??provingGround;provingGroundBattle=false;paused=true;RebuildProvingGround();}
        private void ExitProvingGround()
        {
            if(provingGround==null||provingGroundBusy)return;
            CloseProvingMenu();provingGroundBusy=true;input.WorldInputEnabled=input.CommandInputEnabled=false;menuNavigation.SetScope(null);
            StartCoroutine(ReturnFromProvingGround());
        }
        private IEnumerator ReturnFromProvingGround()
        {
            runtime?.RequestStop();while(runtime!=null&&!runtime.IsStopped)yield return null;
            runtime=null;view=null;routeService?.Dispose();routeService=null;DisposeLocalChildren();ResetLocalViewport();ResetPostMatch();world.Clear();selection.Clear();CloseMap();
            RestoreProvingMap();CloseProvingConstruction();
            RestoreProvingMap();CloseProvingConstruction();provingGroundLayer?.RemoveFromHierarchy();provingGroundLayer=null;provingGround=null;provingGroundBattle=provingGroundBusy=false;
            inLobby=true;paused=menuPaused=false;matchSetup=null;keyboardHomePosition=provingReturnCameraPosition;keyboardHomeRotation=provingReturnCameraRotation;keyboardHomeZoom=provingReturnCameraZoom;ResetKeyboardCamera();SetMatchUi(false);NativeGameplayAudioProfile.UseSession(balanceStore.ActiveAudio);ModelDetailProfile.UseSession(balanceStore.ActiveModelDetail);
            childMenu=laboratoryPage.Page;laboratoryPage.RestoreFromProvingGround();
        }
    }
}
