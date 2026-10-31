#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        [Serializable] sealed class DetailLabReport {public bool saved,reloaded,draftIsolated,muted;public int version,actualLevel;public float farEnter,farExit,screenRatio;}
        private DetailLabReport detailLabReport;
        private IEnumerator LabPhotograph(string name){yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(evidence,name+".png"));yield return new WaitForEndOfFrame();yield return new WaitForEndOfFrame();}
        private IEnumerator EditModelDetailLabEvidence()
        {
            if(!NativeSilentStartup.IsSilent)throw new InvalidOperationException("LOD lab evidence requires mute");
            Screen.SetResolution(1920,1080,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.5f);
            var page=laboratoryPage.Page;menuNavigation.ActivateElement(page.Q<Button>("lab-section-Графика"));yield return null;
            foreach(string field in new[]{"normalEnter","normalExit","farEnter","farExit"})if(page.Q<TextField>("visual.modelDetail."+field)==null)throw new InvalidOperationException("Missing native LOD control "+field);
            yield return LabPhotograph("lod-lab-default");
            menuNavigation.ActivateElement(page.Q<Button>("lab-copy"));root.Q<TextField>("lab-name").value="LOD · проверка";menuNavigation.ActivateElement(root.Q<Button>("lab-name-accept"));yield return null;
            float before=balanceStore.ActiveModelDetail.farEnter;
            page.Q<TextField>("visual.modelDetail.farEnter").value="7";page.Q<TextField>("visual.modelDetail.farExit").value="8";
            if(balanceStore.ActiveModelDetail.farEnter!=before||ModelDetailProfile.Current.farEnter!=before)throw new InvalidOperationException("LOD draft leaked into active session");
            yield return LabPhotograph("lod-lab-draft");menuNavigation.ActivateElement(page.Q<Button>("lab-save"));yield return null;
            var restored=new NativeBalanceStore(profile,balanceStore.StoragePath,false);
            if(Mathf.Abs(restored.ActiveModelDetail.farEnter-.07f)>.00001f||Mathf.Abs(restored.ActiveModelDetail.farExit-.08f)>.00001f)throw new InvalidOperationException("LOD profile save/reload failed");
            detailLabReport=new DetailLabReport{saved=true,reloaded=true,draftIsolated=true,muted=true,version=restored.SelectedVersion,farEnter=restored.ActiveModelDetail.farEnter,farExit=restored.ActiveModelDetail.farExit};
            yield return LabPhotograph("lod-lab-saved");
        }
        private IEnumerator CaptureModelDetailLabSelection(PlayableWorld.Actor tank)
        {
            var lod=tank.DetailLod;var target=lod.transform.TransformPoint(lod.ReferenceBounds.center);
            cameraView.orthographicSize=(float)profile.CameraOrthoSize;cameraView.transform.position=target+new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);cameraView.transform.LookAt(target);
            lod.SetDetail(0);lod.Select(cameraView);detailLabReport.actualLevel=lod.Level;detailLabReport.screenRatio=lod.ProjectedScreenRatio(cameraView);
            if(lod.Level!=2||Mathf.Abs(ModelDetailProfile.Current.farEnter-.07f)>.00001f)throw new InvalidOperationException("Native lab profile did not reach polygon LOD selector");
            yield return LabPhotograph("lod-lab-polygon");File.WriteAllText(Path.Combine(evidence,"lod-lab-result.json"),JsonUtility.ToJson(detailLabReport,true));Debug.Log("MODEL_DETAIL_NATIVE_LAB_PASS saved/reloaded/polygon");enabled=true;Quit();
        }
        [Serializable] sealed class DetailCameraRow { public string resource,zoomName;public float zoom,screenRatio;public int beforeLevel,afterLevel,returnLevel; }
        [Serializable] sealed class DetailCameraReport { public string classification="ACTUAL_PLAYER_SANDBOX_COMMAND_CAMERA_NOT_MATCH_DEVICE_OR_FPS";public bool muted;public int profileRevision;public DetailCameraRow[] rows; }
        // Development-only opt-in: uses the ordinary sandbox host, world, lighting,
        // HUD and command camera. Fixed pause keeps camera A/B deterministic.
        private IEnumerator CaptureModelDetailCalibration(PlayableWorld.Actor[] units)
        {
            if(!NativeSilentStartup.IsSilent)throw new InvalidOperationException("Calibration requires silent startup");
            var extras=new[]{world.Building(500001,"ScientificCenter",true),world.Building(500002,"Factory",true)};
            extras[0].Root.position=world.Point(new NavPoint(-24,6));extras[1].Root.position=world.Point(new NavPoint(-18,6));
            var subjects=units.Concat(extras).ToArray();var all=world.Actors.Values.Concat(extras).Distinct().ToArray();
            var baseline=new ModelDetailProfile{revision=1,normalEnter=.10f,normalExit=.12f,farEnter=.025f,farExit=.035f};baseline.Validate();
            var current=ModelDetailProfile.Load();var rows=new List<DetailCameraRow>();
            IEnumerator Photograph(string name){yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(evidence,name+".png"));yield return new WaitForEndOfFrame();yield return new WaitForEndOfFrame();}
            foreach(var subject in subjects){
                var lod=subject.DetailLod;if(!lod)throw new InvalidOperationException("Missing calibrated detail controller");
                var target=lod.transform.TransformPoint(lod.ReferenceBounds.center);string key=subject.Hull.name.Replace("(Clone)","");
                var zooms=new[]{(name:"close",value:profile.CameraMinZoom),(name:"normal",value:profile.CameraOrthoSize),(name:"far",value:profile.CameraMaxZoom)};
                foreach(var zoom in zooms){
                    cameraView.orthographicSize=(float)zoom.value;cameraView.transform.position=target+new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);cameraView.transform.LookAt(target);
                    float fieldCenter=Screen.height-(top.worldBound.yMax+bottom.worldBound.yMin)*.5f;
                    cameraView.transform.position-=cameraView.transform.up*(fieldCenter-cameraView.WorldToScreenPoint(target).y)*(2*cameraView.orthographicSize/Screen.height);
                    foreach(var actor in all)if(actor.DetailLod){actor.DetailLod.SetDetail(0);actor.DetailLod.SelectScreenRatio(actor.DetailLod.ProjectedScreenRatio(cameraView),baseline);}
                    int before=lod.Level;yield return Photograph("camera-"+key+"-"+zoom.name+"-before");
                    foreach(var actor in all)if(actor.DetailLod){actor.DetailLod.SetDetail(0);actor.DetailLod.Select(cameraView);}
                    int after=lod.Level;float ratio=lod.ProjectedScreenRatio(cameraView);yield return Photograph("camera-"+key+"-"+zoom.name+"-after");
                    // Exercise return from far without resetting the per-instance state.
                    cameraView.orthographicSize=(float)profile.CameraOrthoSize;lod.Select(cameraView);
                    rows.Add(new DetailCameraRow{resource=key,zoomName=zoom.name,zoom=(float)zoom.value,screenRatio=ratio,beforeLevel=before,afterLevel=after,returnLevel=lod.Level});
                }
            }
            var tank=rows.Where(r=>r.resource.StartsWith("tankHybrid",StringComparison.Ordinal)).ToArray();
            if(tank.Length!=3||tank[0].afterLevel!=0||tank[1].afterLevel!=1||tank[2].afterLevel!=2||tank[2].returnLevel!=1)throw new InvalidOperationException("Calibrated tank must use original/normal/far and return to normal");
            File.WriteAllText(Path.Combine(evidence,"camera-calibration.json"),JsonUtility.ToJson(new DetailCameraReport{muted=true,profileRevision=current.revision,rows=rows.ToArray()},true));
            Debug.Log("MODEL_DETAIL_CAMERA_CALIBRATION_PASS rows="+rows.Count);enabled=true;Quit();
        }
    }
}
#endif
