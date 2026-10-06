using System;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private bool styleASceneEvidence;
        private int styleASceneStage, styleASceneCaptureFrame;
        private int styleASceneRefinerySlot, styleASceneScienceSlot;
        private float styleASceneStarted;
        private bool styleASceneScoutOrdered,styleASceneOwnerCaptured;

        private void DriveStyleASceneEvidence()
        {
            if(!styleASceneEvidence||evidence==null||view==null)return;
            if(styleASceneStarted==0)styleASceneStarted=Time.realtimeSinceStartup;
            if(Time.realtimeSinceStartup-styleASceneStarted>180||view.Outcome!=PlayableMatchOutcome.Playing||!string.IsNullOrEmpty(view.Failure))
            {
                Record("STYLE_A_SCENE_FAIL stage="+styleASceneStage+" tick="+view.Tick+" outcome="+view.Outcome+" failure="+view.Failure);
                Quit();styleASceneEvidence=false;return;
            }
            if(paused){Pause(false);return;}
            if(!styleASceneScoutOrdered)
            {
                var scout=view.Entities.FirstOrDefault(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Explorer);
                if(scout!=null){Submit(PlayableCommandKind.Move,new[]{scout.Id},new NavPoint(profile.EnemyHeadquartersX-profile.DefenderOffsetX,profile.HeadquartersZ));styleASceneScoutOrdered=true;}
            }
            if(!styleASceneOwnerCaptured&&view.Buildings.Any(b=>b.Owner==PlayableOwner.Enemy&&world.Actors.ContainsKey(b.Id)))
            {
                Record("STYLE_A_SCENE_ENEMY_VISIBLE tick="+view.Tick);
                RecordStyleAMaterials(view.Buildings.Where(b=>world.Actors.ContainsKey(b.Id)).Select(b=>b.Id).ToArray());
                ScreenCapture.CaptureScreenshot(Path.Combine(evidence,"style-a-playable-owner-scout.png"));
                styleASceneOwnerCaptured=true;
            }
            var home=view.Sites.FirstOrDefault(site=>site.Owner==PlayableOwner.Player&&site.Ready&&site.Site.Slots.Count>=2);
            if(home==null)return;
            var refinery=view.Buildings.FirstOrDefault(b=>b.Owner==PlayableOwner.Player&&b.SiteId==home.Site.Id&&b.SlotId==styleASceneRefinerySlot&&b.Kind==PlayableBuildingKind.Refinery);
            var science=view.Buildings.FirstOrDefault(b=>b.Owner==PlayableOwner.Player&&b.SiteId==home.Site.Id&&b.SlotId==styleASceneScienceSlot&&b.Kind==PlayableBuildingKind.ScientificCenter);
            switch(styleASceneStage)
            {
                case 0:
                    var free=home.Site.Slots.Where(slot=>!view.Buildings.Any(b=>b.SiteId==home.Site.Id&&b.SlotId==slot.Id)).Take(2).ToArray();
                    if(free.Length<2)throw new InvalidOperationException("Scene audit requires two empty own home slots");
                    styleASceneRefinerySlot=free[0].Id;styleASceneScienceSlot=free[1].Id;
                    Record("STYLE_A_SCENE_START seed="+view.Seed+" profile="+view.ProfileId+"@"+view.ProfileRevision+" refinery_slot="+styleASceneRefinerySlot+" science_slot="+styleASceneScienceSlot);
                    selectedSite=home.Site.Id;selectedSlot=styleASceneRefinerySlot;BuildSelected(PlayableBuildingKind.Refinery);
                    styleASceneStage=1;return;
                case 1:
                    if(refinery==null||refinery.Phase!=ConstructionPhase.Ready||view.Credits<profile.ScienceCreditCost)return;
                    selectedSite=home.Site.Id;selectedSlot=styleASceneScienceSlot;BuildSelected(PlayableBuildingKind.ScientificCenter);
                    styleASceneStage=2;return;
                case 2:
                    if(science==null||science.Phase!=ConstructionPhase.Ready||view.Credits<profile.RefineryUpgradeCost)return;
                    Submit(PlayableCommandKind.UpgradeRefinery,new[]{refinery.Id});
                    styleASceneStage=3;return;
                case 3:
                    if(!refinery.RefineryUpgraded||science.Phase!=ConstructionPhase.Ready)return;
                    if(!world.Actors.ContainsKey(refinery.Id)||!world.Actors.ContainsKey(science.Id))return;
                    Record("STYLE_A_SCENE_READY tick="+view.Tick+" seed="+view.Seed+" profile="+view.ProfileId+"@"+view.ProfileRevision+" refinery="+refinery.Id+" upgraded="+refinery.RefineryUpgraded+" science="+science.Id+" science_phase="+science.Phase);
                    RecordStyleAMaterials(refinery.Id,science.Id);
                    ScreenCapture.CaptureScreenshot(Path.Combine(evidence,"style-a-playable-wide.png"));
                    styleASceneCaptureFrame=Time.frameCount;styleASceneStage=4;return;
                case 4:
                    if(Time.frameCount<styleASceneCaptureFrame+3)return;
                    var middle=(world.Point(refinery.Position)+world.Point(science.Position))*.5f;
                    cameraView.transform.position=new Vector3(middle.x,(float)profile.CameraHeight,middle.z+(float)profile.CameraOffsetZ);
                    cameraView.orthographicSize=(float)profile.CameraMinZoom;
                    // Diagnostic composition derives from the existing HUD bounds; it is
                    // not a new player camera rule or tunable simulation/presentation value.
                    var fieldCenter=Screen.height-(top.worldBound.yMax+bottom.worldBound.yMin)*.5f;
                    var projected=cameraView.WorldToScreenPoint(middle);
                    cameraView.transform.position-=cameraView.transform.up*(fieldCenter-projected.y)*(2*cameraView.orthographicSize/Screen.height);
                    Record("STYLE_A_DETAIL_CAMERA position="+cameraView.transform.position+" ortho="+cameraView.orthographicSize+" field_center_screen_y="+fieldCenter);
                    styleASceneCaptureFrame=Time.frameCount;styleASceneStage=5;return;
                case 5:
                    if(Time.frameCount<styleASceneCaptureFrame+3)return;
                    ScreenCapture.CaptureScreenshot(Path.Combine(evidence,"style-a-playable-detail.png"));
                    styleASceneCaptureFrame=Time.frameCount;styleASceneStage=6;return;
                case 6:
                    if(Time.frameCount<styleASceneCaptureFrame+3)return;
                    Record("STYLE_A_SCENE_PASS wide_and_detail_captured=true enemy_visible_capture="+styleASceneOwnerCaptured);
                    Quit();styleASceneStage=7;return;
            }
        }

        private void RecordStyleAMaterials(params int[] ids)
        {
            foreach(var id in ids)
            {
                var actor=world.Actors[id];
                foreach(var renderer in actor.Hull.GetComponentsInChildren<Renderer>(true))
                    for(int slot=0;slot<renderer.sharedMaterials.Length;slot++)
                    {
                        var material=renderer.sharedMaterials[slot];
                        if(material==null)throw new InvalidOperationException("Missing Style A material on entity "+id);
                        var role=material.name;
                        var team=role.EndsWith("team-primary")||role.EndsWith("team-emissive");
                        var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,slot);
                        Record("STYLE_A_MATERIAL entity="+id+" role="+role+" emission="+material.GetColor("_EmissionColor")+" keyword="+material.IsKeywordEnabled("_EMISSION")+" owner_role="+team+" owner_color="+(team?block.GetColor("_BaseColor").ToString():"none"));
                    }
            }
        }
#endif
    }
}
