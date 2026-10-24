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
        private IEnumerator CaptureProvingGroundEvidence()
        {
            void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);Record("proving PASS "+why);}
            void Click(Button button){Check(button!=null,"button exists");menuNavigation.ActivateElement(button);}
            IEnumerator Ready(){float end=Time.realtimeSinceStartup+30;while((provingGroundBusy||view==null)&&Time.realtimeSinceStartup<end)yield return null;Check(!provingGroundBusy&&view!=null,"ready");yield return null;}
            IEnumerator Frame(string name){yield return new WaitForSecondsRealtime(.3f);yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(evidence,name+".png"));yield return null;}
            yield return null;
            OpenLaboratory();yield return null;
            var initialCamera=cameraView.transform.rotation;float initialZoom=cameraView.orthographicSize;
            var lab=laboratoryPage;var field=lab.Page.Q<TextField>("lab-field-tankHealth");field.value="222";
            var initialVersions=balanceStore.Versions.Count;
            var path=balanceStore.StoragePath;string before=File.Exists(path)?File.ReadAllText(path):null;
            Click(lab.Page.Q<Button>("lab-proving-ground"));yield return Ready();
            Check(profile.TankHealth==222&&view.Tick==0&&view.Credits==10000,"draft snapshot and frozen initial setup");
            Check(cameraView.orthographicSize==(float)profile.CameraOrthoSize,"ordinary match camera distance");
            var offset=new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);
            Check(Quaternion.Angle(cameraView.transform.rotation,Quaternion.LookRotation(-offset))<.01f,"ordinary match camera angle");
            yield return Frame("00-match-camera");
            for(int side=0;side<2;side++){
                Click(provingGroundLayer.Q<Button>("proving-unit-"+side+"-Tank"));PlaceProvingGround(new NavPoint(side==0?-12:12,0));yield return Ready();
                provingGroundLayer.Q<DropdownField>("proving-tab-"+side).value="Здания";
                provingSide=side;provingUnit=null;provingBuilding=PlayableBuildingKind.Factory;
                PlaceProvingGround(profile.AuthoredMap.Sites(profile)[side].Slots[0].Position);yield return Ready();
            }
            Check(view.Entities.Count==2&&view.Buildings.Count==8&&view.Tick==0,"both armies and buildings ready while frozen");
            yield return Frame("01-preparation");
            provingGroundLayer.Q<DropdownField>("proving-tab-0").value="Исследования";
            provingGroundLayer.Q<Toggle>("proving-research-0-TankChassis").value=true;yield return Ready();
            Check(view.Entities.First(e=>e.Owner==PlayableOwner.Player).Upgraded,"instant research without science building");
            yield return Frame("02-research");
            var longToggle=provingGroundLayer.Q<Toggle>("proving-research-0-ImprovedDefensiveSystems");
            Check(longToggle.worldBound.xMax<=longToggle.GetFirstAncestorOfType<ScrollView>().contentViewport.worldBound.xMax,"long research checkbox fits panel");
            Zoom(10);float setupZoom=cameraView.orthographicSize;
            var armyCost=provingGround.ArmyCost(0);var researchCost=provingGround.ResearchCost(0);
            StartProvingGround();yield return new WaitForSecondsRealtime(1);
            Check(!paused&&view.Tick>0,"battle ticking");
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-provingGroundHudEvidence")>=0){
                Check(provingLeft==null&&provingRight==null&&provingGroundLayer.Q("proving-side-0")==null&&provingGroundLayer.Q("proving-side-1")==null,"preparation and cost panels absent in battle");
                Check(provingActions.Query<Button>().ToList().Count==2&&top.resolvedStyle.display==DisplayStyle.Flex&&bottom.resolvedStyle.display==DisplayStyle.Flex,"ordinary HUD and exactly two extra buttons");
                selection.Clear();selection.Add(view.Buildings.First(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory).Id);
                yield return Frame("03-ordinary-battle-hud");
                Check(provingActions.worldBound.height<48,"compact single-row actions");
                Click(provingGroundLayer.Q<Button>("proving-reset"));yield return Ready();
                Check(view.Tick==0&&provingGroundLayer.Q("proving-side-0")!=null&&provingGroundLayer.Q("proving-side-1")!=null,"reset button restores preparation panels");
                ExitProvingGround();float end=Time.realtimeSinceStartup+20;while(provingGround!=null&&Time.realtimeSinceStartup<end)yield return null;
                Check(provingGround==null&&field.value=="222","laboratory draft retained");
                File.WriteAllText(Path.Combine(evidence,"proving-result.txt"),"PASS focused battle HUD: no preparation/cost panels, ordinary HUD, two compact actions, reset restores panels, lab draft retained");Flush();Application.Quit();yield break;
            }
            var own=view.Entities.First(e=>e.Owner==PlayableOwner.Player);
            selection.Clear();selection.Add(own.Id);Submit(PlayableCommandKind.Hold);yield return new WaitForSecondsRealtime(.5f);
            Check(runtime.ReceiptsAfter(sequence-1).Any(r=>r.Sequence==sequence&&r.Status==PlayableCommandStatus.Applied),"left unit accepts manual command");
            var factory=view.Buildings.First(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory);
            Submit(PlayableCommandKind.QueueTank,new[]{factory.Id});yield return new WaitForSecondsRealtime(.5f);
            Check(view.Buildings.First(b=>b.Id==factory.Id).PrivateState.Orders.Count==1,"left production queue");
            Check(view.Buildings.Count==8&&view.Buildings.Where(b=>b.Owner==PlayableOwner.Enemy).All(b=>b.PrivateState==null||b.PrivateState.Orders.Count==0),"right side creates no buildings or production");
            Check(armyCost==provingGround.ArmyCost(0)&&researchCost==provingGround.ResearchCost(0),"costs remain fixed in combat");
            yield return Frame("03-battle");
            ResetProvingGround();yield return Ready();
            Check(cameraView.orthographicSize==setupZoom,"rebuild preserves camera zoom");
            Check(view.Tick==0&&view.Credits==10000&&view.Entities.Count==2&&view.Entities.First(e=>e.Owner==PlayableOwner.Player).Position.Equals(new NavPoint(-12,0)),"reset restores original army money and position");
            Check(view.Buildings.First(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory).PrivateState.Orders.Count==0,"reset clears production");
            ExitProvingGround();float until=Time.realtimeSinceStartup+20;while(provingGround!=null&&Time.realtimeSinceStartup<until)yield return null;
            Check(provingGround==null&&ReferenceEquals(lab,laboratoryPage)&&field.value=="222"&&lab.Page.resolvedStyle.display!=DisplayStyle.None,"return keeps same unsaved laboratory");
            Check(cameraView.transform.rotation==initialCamera&&cameraView.orthographicSize==initialZoom,"normal camera restored on exit");
            Check(balanceStore.Versions.Count==initialVersions&&before==(File.Exists(path)?File.ReadAllText(path):null),"no balance version saved");
            yield return Frame("04-return-laboratory");
            File.WriteAllText(Path.Combine(evidence,"proving-result.txt"),"PASS draft, preparation, both buildings, research, start, left command, production, no right AI, costs, reset, return; synthetic UI, not physical/human acceptance");
            Flush();Application.Quit();
        }
    }
}
#endif
