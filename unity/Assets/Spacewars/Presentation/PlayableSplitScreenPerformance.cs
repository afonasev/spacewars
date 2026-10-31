using System;
using System.IO;
using System.Linq;
using System.Globalization;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private static bool SplitScreenPerformanceRequested=>Array.IndexOf(Environment.GetCommandLineArgs(),"-splitScreenPerformanceEvidence")>=0;
        private StreamWriter splitSamples;
        private int splitPhase=-1;
        private float splitNextSample;
        private readonly Vector3[] splitCameraPositions=new Vector3[4];
        private void DriveSplitScreenPerformance()
        {
            float now=Time.realtimeSinceStartup;
            if(routePerformanceStarted<0){
                if(spectatorMode||localPresentations.Count!=4||view.Participants.Count!=4||view.Participants.Any(p=>p.Control!=OfflineControl.Human)||runtime.DiagnosticGlobalUnits!=200||Screen.width!=3456||Screen.height!=2234||!(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset pipeline)||pipeline.renderScale!=1||localPresentations.Any(s=>s.view.Entities.Count(e=>e.Owner==s.LocalOwner)!=50)||string.IsNullOrEmpty(TestMatchLogSession.CurrentDirectory)){
                    BeginQaFinish("FAIL split-screen initial seats/units/logging/resolution identity width="+Screen.width+" height="+Screen.height,true);return;
                }
                routePerformanceStarted=now;QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
                splitSamples=new StreamWriter(Path.Combine(evidence,"split-screen-seats.csv"));
                splitSamples.WriteLine("elapsed,phase,tick,owner,units,health,moving,commands,camera_x,camera_y,camera_z,pixel_x,pixel_y,pixel_width,pixel_height,screen_width,screen_height,center_x,center_z,shadow_renderers,shadow_ms");
                for(int i=0;i<4;i++)splitCameraPositions[i]=localPresentations[i].cameraView.transform.position;
                Record("split-screen START warmup=15 separated=30 overlapping=30 combat=15 seed="+view.Seed+" map="+profile.AuthoredMap.Id+" width="+Screen.width+" height="+Screen.height+" quality="+QualitySettings.GetQualityLevel()+" vsync=0 fps=-1 logging="+TestMatchLogSession.CurrentDirectory+" syntheticPads=true");
            }
            float elapsed=now-routePerformanceStarted;
            if(!string.IsNullOrEmpty(view.Failure)||view.Metrics.Errors>0||paused){splitSamples?.Dispose();splitSamples=null;BeginQaFinish("FAIL split-screen gameplay/pause "+view.Failure,true);return;}
            int phase=elapsed<15?0:elapsed<45?1:elapsed<75?2:3;
            if(phase!=splitPhase){
                splitPhase=phase;Record("split-screen PHASE "+phase+" elapsed="+elapsed);
                if(phase==1||phase==3)foreach(var seat in localPresentations){
                    var own=seat.view.Entities.Where(e=>e.Owner==seat.LocalOwner&&e.Health>0).ToArray();
                    var center=new NavPoint(own.Average(e=>e.Position.X),own.Average(e=>e.Position.Z));
                    var goal=phase==1?new NavPoint(center.X*.70,center.Z*.70):new NavPoint(0,0);
                    seat.Submit(phase==1?PlayableCommandKind.Move:PlayableCommandKind.AttackMove,own.Select(e=>e.Id).ToArray(),goal);
                    Record("split-screen COMMAND owner="+seat.LocalOwnerId+" sequence="+seat.sequence+" phase="+phase+" actors="+own.Length);
                }
                if(phase==2)foreach(var seat in localPresentations)seat.cameraView.transform.position=splitCameraPositions[0];
                if(phase==3)for(int i=0;i<4;i++)localPresentations[i].cameraView.transform.position=splitCameraPositions[i];
                if(phase>0)ScreenCapture.CaptureScreenshot(Path.Combine(evidence,"split-screen-phase-"+phase+".png"));
            }
            if(phase<3&&localPresentations.Any(s=>s.view.Entities.Count(e=>e.Owner==s.LocalOwner&&e.Health>0)!=50)){
                splitSamples?.Dispose();splitSamples=null;BeginQaFinish("FAIL split-screen base workload fell below 200 before combat window",true);return;
            }
            if(now>=splitNextSample){
                splitNextSample=now+1;
                foreach(var seat in localPresentations){var own=seat.view.Entities.Where(e=>e.Owner==seat.LocalOwner).ToArray();var p=seat.cameraView.transform.position;var r=seat.cameraView.pixelRect;
                    splitSamples.WriteLine(string.Join(",",elapsed.ToString("R",CultureInfo.InvariantCulture),phase,seat.view.Tick,seat.LocalOwnerId,own.Length,own.Sum(e=>e.Health),own.Count(e=>e.Moving),seat.sequence,p.x,p.y,p.z,r.x,r.y,r.width,r.height,Screen.width,Screen.height,own.Average(e=>e.Position.X),own.Average(e=>e.Position.Z),seat.cameraView.GetComponent<VisibleShadowCoverage>().LastRendererCount,seat.cameraView.GetComponent<VisibleShadowCoverage>().LastCoverageMilliseconds));}
                splitSamples.Flush();
            }
            if(elapsed>=90){splitSamples.Dispose();splitSamples=null;BeginQaFinish("split-screen measurement completed");}
        }
#endif
    }
}
