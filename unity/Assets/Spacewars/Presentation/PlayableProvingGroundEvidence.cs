#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        // Opt-in, ordinary live sandbox path. Historical frozen captures remain in Git/evidence.
        private IEnumerator CaptureProvingGroundEvidence()
        {
            void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);Record("sandbox PASS "+why);}
            void Click(Button button){Check(button!=null,"button exists");menuNavigation.ActivateElement(button);}
            IEnumerator Until(Func<bool> ready,string why,float seconds=30){float end=Time.realtimeSinceStartup+seconds;while(!ready()&&Time.realtimeSinceStartup<end){Check(runtime==null||provingGroundBusy||!runtime.IsStopped,runtime?.Latest?.Failure??"host alive");yield return null;}Check(ready(),why);yield return null;}
            IEnumerator Frame(string name){
                // Pin presentation for the photograph only. Shared Editor input can
                // rebind Mouse.current and edge-pan between asynchronous GPU frames.
                bool lockPose=provingGround!=null&&input!=null;bool enabled=input?.enabled==true;
                if(lockPose){input.enabled=false;ResetKeyboardCamera();}
                yield return new WaitForSecondsRealtime(.3f);yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(evidence,name+".png"));
                yield return new WaitForEndOfFrame();yield return new WaitForEndOfFrame();
                if(lockPose&&input!=null)input.enabled=enabled;
            }
            bool visualOnly=Array.IndexOf(Environment.GetCommandLineArgs(),"-provingGroundVisualOnly")>=0;
            yield return null;OpenLaboratory();yield return null;
            var lab=laboratoryPage;var field=lab.Page.Q<TextField>("lab-field-tankHealth");field.value="222";
            int versions=balanceStore.Versions.Count;string before=File.Exists(balanceStore.StoragePath)?File.ReadAllText(balanceStore.StoragePath):null;
            var search=lab.Page.Q<TextField>("lab-search");search.value="provingGround.actionSeconds";yield return null;
            Check(lab.Page.Q<TextField>("lab-field-provingGroundActionSeconds")!=null,"named action duration in Lab");
            yield return Frame("00-lab-duration");search.value="";yield return null;field=lab.Page.Q<TextField>("lab-field-tankHealth");
            Click(lab.Page.Q<Button>("lab-proving-ground"));
            yield return Until(()=>!provingGroundBusy&&view!=null&&view.Tick>0,"sandbox runs immediately");
            Check(!paused&&input.CommandInputEnabled&&profile.TankHealth==222,"live draft and ordinary command input before Start");
            Check(creditsLabel.resolvedStyle.display==DisplayStyle.None&&view.Credits==0,"credits hidden and unused");
            var keyboard=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
            var mouse=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();
            UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior=UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            UnityEngine.InputSystem.InputSystem.settings.editorInputBehaviorInPlayMode=UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            localInputFocused=true;input.SetFocus(true);input.AssignedKeyboard=keyboard;input.AssignedMouse=mouse;keyboard.MakeCurrent();mouse.MakeCurrent();root.Focus();
            // ResetChat intentionally suppresses held input until a neutral sample.
            // Match PlayableInputTests: process owned virtual state and Poll in one
            // transaction, before automatic Update can rebind Keyboard.current.
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState());
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse,new UnityEngine.InputSystem.LowLevel.MouseState{position=cameraView.pixelRect.center});
            UnityEngine.InputSystem.InputSystem.Update();input.Poll();
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-provingResearchChainEvidence")>=0){
                yield return Until(()=>!initializingMenu&&startupLoadingHost==null&&cameraView.isActiveAndEnabled&&loadingScreen.resolvedStyle.display==DisplayStyle.None,"presentation loading complete");
                for(int side=0;side<2;side++){
                    var site=profile.AuthoredMap.Sites(profile)[side];PlaceProvingGround(site.Slots[1].Position);
                    Click(provingConstruction.Q<Button>("proving-build-ScientificCenter"));
                }
                yield return Until(()=>Enumerable.Range(0,2).All(side=>ProvingView(side).Buildings.Any(b=>(int)b.Owner==side&&b.Kind==PlayableBuildingKind.ScientificCenter&&b.Progress>=1)),"both science centers ready");
                for(int side=0;side<2;side++)Click(provingGroundLayer.Q<Button>("proving-tab-"+side+"-research"));
                yield return null;
                provingGroundLayer.Q<Toggle>("proving-research-0-AdvancedWeaponSystems").value=true;
                provingGroundLayer.Q<Toggle>("proving-research-1-AdvancedDefensiveSystems").value=true;
                yield return Until(()=>Enumerable.Range(0,2).All(side=>ProvingView(side).OwnerResearch.Count==2),"II commands automatically enqueue I and II");
                for(int side=0;side<2;side++){
                    var first=side==0?PlayableResearchKind.ImprovedWeaponSystems:PlayableResearchKind.ImprovedDefensiveSystems;
                    var second=side==0?PlayableResearchKind.AdvancedWeaponSystems:PlayableResearchKind.AdvancedDefensiveSystems;
                    var orders=ProvingView(side).OwnerResearch;
                    Check(orders[0].Kind==first&&orders[0].Active&&!orders[0].Complete&&orders[1].Kind==second&&!orders[1].Active,"I active before II "+side);
                    UpdateProvingGroundUi();
                    Check(provingGroundLayer.Q<Toggle>("proving-research-"+side+"-"+first).value&&provingGroundLayer.Q<Toggle>("proving-research-"+side+"-"+second).value,"both checkboxes selected immediately "+side);
                }
                yield return Frame("chain-01-both-levels-selected");
                yield return Until(()=>Enumerable.Range(0,2).All(side=>ProvingView(side).OwnerResearch[0].Complete&&ProvingView(side).OwnerResearch[1].Active),"I finishes before II starts");
                yield return Frame("chain-02-second-level-active");
                yield return Until(()=>Enumerable.Range(0,2).All(side=>ProvingView(side).OwnerResearch.All(o=>o.Complete)),"both chains complete",20);
                for(int side=0;side<2;side++)Check(runtime.ProvingGroundCosts(side).Research==ProvingView(side).OwnerResearch.Sum(o=>PlayableResearchRules.Cost(profile,o.Kind)),"both original research costs counted "+side);
                yield return Frame("chain-03-completed");
                UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse);
                File.WriteAllText(Path.Combine(evidence,"proving-result.txt"),"PASS actual II checkbox commands automatically select/queue I first on both sides; ordered timed completion and both costs verified");runtime.RequestStop();Flush();Application.Quit();yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-provingPanelEvidence")>=0){
                yield return Until(()=>!initializingMenu&&startupLoadingHost==null&&cameraView.isActiveAndEnabled&&loadingScreen.resolvedStyle.display==DisplayStyle.None,"presentation loading complete");
                yield return new WaitForSecondsRealtime(2);
                yield return Frame("panel-01-units");
                for(int side=0;side<2;side++){
                    var unit=provingGroundLayer.Q<Button>("proving-unit-"+side+"-Tank");
                    var tabs=provingGroundLayer.Q("proving-tab-"+side);
                    Check(tabs.worldBound.yMax+5<=unit.worldBound.yMin,"tab row separated from unit list "+side);
                    var unitTab=tabs.Q<Button>("proving-tab-"+side+"-units");var researchTab=tabs.Q<Button>("proving-tab-"+side+"-research");
                    Check(Mathf.Abs(unitTab.resolvedStyle.width-researchTab.resolvedStyle.width)<.5f,"equal tab widths "+side);
                    Check((side==0?provingLeft:provingRight).resolvedStyle.width<root.contentRect.width*.24f,"panel narrower than previous 24 percent "+side);
                    Check(researchTab.MeasureTextSize(researchTab.text,0,VisualElement.MeasureMode.Undefined,0,VisualElement.MeasureMode.Undefined).x<=researchTab.contentRect.width,"research tab label fits "+side);
                    using(var enter=PointerEnterEvent.GetPooled()){enter.target=unit;unit.SendEvent(enter);}
                    yield return null;yield return Frame("panel-0"+(side+2)+"-hover");
                    Check(provingUnitDetail.resolvedStyle.display==DisplayStyle.Flex,"unit hover card visible "+side);
                    Check(provingUnitDetail.worldBound.xMin>=0&&provingUnitDetail.worldBound.xMax<=root.worldBound.xMax&&provingUnitDetail.worldBound.yMax<=root.worldBound.yMax,"hover card inside viewport "+side);
                    using(var leave=PointerLeaveEvent.GetPooled()){leave.target=unit;unit.SendEvent(leave);}
                }
                Click(provingGroundLayer.Q<Button>("proving-tab-0-research"));yield return null;yield return Frame("panel-04-research");
                Click(provingGroundLayer.Q<Button>("proving-tab-1-research"));yield return null;yield return new WaitForSecondsRealtime(1);
                for(int side=0;side<2;side++){
                    var panel=side==0?provingLeft:provingRight;
                    Check(panel.layout.y<=12,"panel at upper corner "+side);
                    foreach(PlayableResearchKind kind in Enum.GetValues(typeof(PlayableResearchKind))){
                        var row=panel.Q("proving-research-row-"+side+"-"+kind);
                        Check(row.worldBound.yMin>=panel.worldBound.yMin&&row.worldBound.yMax<=panel.worldBound.yMax-4,"research visible without scroll "+side+" "+kind);
                        var mark=row.Q(className:"unity-toggle__checkmark");var label=row.Q<Label>("proving-research-label-"+side+"-"+kind);
                        Check(label.worldBound.xMin-mark.worldBound.xMax<=6,"checkbox close to label "+side+" "+kind);
                    }
                }
                yield return Frame("panel-05-all-research");
                foreach(var kind in new[]{PlayableResearchKind.HawkSalvoSystems,PlayableResearchKind.AdvancedDefensiveSystems}){
                    var row=provingLeft.Q("proving-research-row-0-"+kind);
                    using(var enter=PointerEnterEvent.GetPooled()){enter.target=row;row.SendEvent(enter);}
                    yield return null;yield return Frame("panel-06-"+kind);
                    Check(provingUnitDetail.Q<Label>().text.Contains(PlayableResearchRules.Description(profile,kind)),"shared research effect card "+kind);
                    using(var leave=PointerLeaveEvent.GetPooled()){leave.target=row;row.SendEvent(leave);}
                }
                UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse);
                File.WriteAllText(Path.Combine(evidence,"proving-result.txt"),"PASS panel spacing, icons, shared hover descriptions and distinct selected tabs; no battle/full/build");runtime.RequestStop();Flush();Application.Quit();yield break;
            }
            var cameraBefore=cameraView.transform.position;
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.RightArrow));
            UnityEngine.InputSystem.InputSystem.Update();input.Poll();
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState());
            UnityEngine.InputSystem.InputSystem.Update();input.Poll();
            Check(Vector3.Distance(cameraBefore,cameraView.transform.position)>.01f,"native camera keys before Start");
            ResetKeyboardCamera(); // Keep subsequent UI evidence in the ordinary base framing.
            ToggleMap();yield return null;Check(mapOpen,"Tab map available before Start");CloseMap();
            for(int side=0;side<2;side++){
                Click(provingGroundLayer.Q<Button>("proving-unit-"+side+"-Tank"));PlaceProvingGround(new NavPoint(side==0?-12:12,-9));
                yield return Until(()=>view.Entities.Count==side+1,"manual placement preserves live host");
                Click(provingGroundLayer.Q<Button>("proving-unit-"+side+"-Tank"));
                var site=profile.AuthoredMap.Sites(profile)[side];
                PlaceProvingGround(site.Slots[0].Position);Click(provingConstruction.Q<Button>("proving-build-Factory"));
                // Scientific construction requires a completed factory or hangar.
                // Respect the ordinary tech prerequisite; retain the ten-building
                // and three-research assertions and the existing deadlines.
                yield return Until(()=>view.Buildings.Any(b=>b.SiteId==site.Id&&b.Kind==PlayableBuildingKind.Factory&&b.Phase==ConstructionPhase.Ready),"factory prerequisite ready on side "+side);
                PlaceProvingGround(site.Slots[1].Position);Click(provingConstruction.Q<Button>("proving-build-ScientificCenter"));
            }
            long setupGeneration=generation;
            yield return Until(()=>view.Buildings.Count==10,"both-side ordinary construction admitted");
            var constructing=view.Buildings.First(b=>b.Owner==LocalOwner&&b.Kind==PlayableBuildingKind.Factory);selection.Clear();selection.Add(constructing.Id);
            yield return Frame("01-live-construction");
            yield return Until(()=>view.Buildings.Count(b=>b.Progress>=1)==10,"five-second buildings complete");
            var own=view.Entities.Single(e=>e.Owner==LocalOwner);selection.Clear();selection.Add(own.Id);Submit(PlayableCommandKind.Hold);
            long holdSequence=sequence;yield return Until(()=>runtime.ReceiptsAfter(holdSequence-1).Any(r=>r.Sequence==holdSequence&&r.Status==PlayableCommandStatus.Applied),"ordinary own command before Start");
            var factory=view.Buildings.Single(b=>b.Owner==LocalOwner&&b.Kind==PlayableBuildingKind.Factory);
            selection.Clear();selection.Add(factory.Id);
            if(visualOnly){Check(CurrentBattleActions().All(a=>!a.Price.HasValue&&!a.Detail.ToLowerInvariant().Contains("кредит")),"production ring has no credit amounts or affordability blocks");ExecuteBattleAction(factory.Id+":tank",false,false);}else Submit(PlayableCommandKind.QueueTank,new[]{factory.Id});Submit(PlayableCommandKind.QueueExplorer,new[]{factory.Id});Submit(PlayableCommandKind.QueueShkval,new[]{factory.Id});
            yield return Until(()=>view.Buildings.Single(b=>b.Id==factory.Id).PrivateState.Orders.Count==3,"ordinary free production queue");
            yield return new WaitForSecondsRealtime(.6f);yield return Frame("02-production-queue");
            var rightFactory=ProvingView(1).Buildings.Single(b=>b.Owner==PlayableOwner.Enemy&&b.Kind==PlayableBuildingKind.Factory);
            ProvingCommand(1,PlayableCommandKind.QueueTank,new[]{rightFactory.Id});
            var center=view.Buildings.Single(b=>b.Owner==LocalOwner&&b.Kind==PlayableBuildingKind.ScientificCenter);
            selection.Clear();selection.Add(center.Id);
            if(visualOnly){Check(CurrentBattleActions().All(a=>!a.Price.HasValue&&!a.Detail.ToLowerInvariant().Contains("кредит")),"research ring has no credit amounts or affordability blocks");ExecuteBattleAction(center.Id+":tank-chassis",false,false);}else Submit(PlayableCommandKind.QueueResearch,new[]{center.Id},researchKind:PlayableResearchKind.TankChassis);
            Submit(PlayableCommandKind.QueueResearch,new[]{center.Id},researchKind:PlayableResearchKind.ImprovedWeaponSystems);
            Submit(PlayableCommandKind.QueueResearch,new[]{center.Id},researchKind:PlayableResearchKind.ImprovedDefensiveSystems);
            yield return Until(()=>view.OwnerResearch.Count==3,"three ordinary research orders");
            Check(view.OwnerResearch.Count(o=>o.Active)==1&&view.OwnerResearch.Count(o=>!o.Active&&!o.Complete)==2,"active and waiting research");
            Check(view.OwnerResearch.First(o=>o.Active).Duration==5,"five-second research terms");
            yield return Frame("03-research-queue");
            if(visualOnly){
                Pause(true);yield return null;yield return Frame("05-before-start-menu");
                UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse);
                File.WriteAllText(Path.Combine(evidence,"proving-result.txt"),"PASS visual-only ordinary camera, queue and bold Start captures; no repeated battle/reset or new gameplay qualification");runtime.RequestStop();Flush();Application.Quit();yield break;
            }
            yield return Until(()=>view.OwnerResearch.Count(o=>o.Complete)==3&&runtime.ProvingGroundCosts(0).Army>profile.TankCreditCost&&runtime.ProvingGroundCosts(1).Army>profile.TankCreditCost,"cumulative produced and research costs",45);
            Check(generation==setupGeneration&&view.Credits==0,"host and free economy persist through edits");
            double researchCost=profile.TankChassisCost+profile.WeaponSystemsLevel1Cost+profile.DefensiveSystemsLevel1Cost;
            Check(runtime.ProvingGroundCosts(0).Research==researchCost,"original research comparison prices");
            double armyBefore=runtime.ProvingGroundCosts(1).Army;int rightCount=view.Entities.Count(e=>e.Owner==PlayableOwner.Enemy);
            runtime.RemoveProvingGroundUnits(1,false);yield return Until(()=>view.Entities.Count(e=>e.Owner==PlayableOwner.Enemy)<rightCount,"remove one created unit");
            Check(runtime.ProvingGroundCosts(1).Army==armyBefore,"loss retains total investment");
            provingUnit=null;selection.Clear();yield return Frame("04-live-cost-comparison");
            Pause(true);yield return null;
            Check(provingStartButton.ClassListContains("orbital-primary")&&provingStartButton.resolvedStyle.unityFontStyleAndWeight==FontStyle.Bold,"bold primary Start");
            yield return Frame("05-before-start-menu");Click(provingStartButton);
            yield return Until(()=>provingGroundBattle&&!paused&&view.Tick>0,"opening attack from live sandbox");
            Check(provingGroundLayer.Q<Label>("proving-cost-0")!=null&&provingGroundLayer.Q<Label>("proving-cost-1")!=null,"both costs remain in battle");
            yield return Frame("06-battle-costs");Pause(true);yield return null;yield return Frame("07-battle-menu");
            Click(modalRestartButton);yield return Until(()=>!provingGroundBusy&&ProvingPreparation&&!paused&&view.Tick>0,"return to live placement");
            Check(view.OwnerResearch.Count(o=>o.Complete)==3,"return keeps completed setup research");
            ExitProvingGround();yield return Until(()=>provingGround==null,"return to laboratory");
            Check(ReferenceEquals(lab,laboratoryPage)&&field.value=="222"&&versions==balanceStore.Versions.Count,"same unsaved draft and no new version");
            Check(before==(File.Exists(balanceStore.StoragePath)?File.ReadAllText(balanceStore.StoragePath):null),"Lab storage unchanged");
            yield return Frame("08-lab-roundtrip");UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse);
            File.WriteAllText(Path.Combine(evidence,"proving-result.txt"),"PASS PG-FB-6 live sandbox, pre-start own commands, camera/map, both-side five-second construction/production, three research orders, free credits, original-price cumulative costs including removed units, bold Start, battle costs, reset and unsaved Lab roundtrip. Editor/synthetic only.");Flush();Application.Quit();
        }
    }
}
#endif
