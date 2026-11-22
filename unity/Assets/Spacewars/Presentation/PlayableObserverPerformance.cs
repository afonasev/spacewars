using System;
using System.IO;
using System.Linq;
using System.Globalization;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private static bool ObserverPerformanceRequested=>Array.IndexOf(Environment.GetCommandLineArgs(),"-observerPerformanceEvidence")>=0;
        private int observerScreenshots;
        private void DriveObserverPerformance()
        {
            float now=Time.realtimeSinceStartup;
            int seconds=SplitScaleArgument("-observerSeconds",180);
            if(seconds<60||seconds>600)throw new ArgumentException("Observer duration must be 60..600 seconds");
            if(routePerformanceStarted<0){
                if(!spectatorMode||localPresentations.Count!=4||view.Participants.Count!=8||view.Participants.Any(p=>p.Control!=OfflineControl.Ai)||string.IsNullOrEmpty(TestMatchLogSession.CurrentDirectory)){
                    BeginQaFinish("FAIL observer roster/cameras/logging",true);return;
                }
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);spectatorRate=4;UpdateSpectatorSpeed();
                routePerformanceStarted=now;QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
                splitSamples=new StreamWriter(Path.Combine(evidence,"split-scale-seats.csv"));
                splitSamples.WriteLine("elapsed,phase,tick,owner,units,moving,health,commands,center_x,center_z,global_units,heap,gc0,buildings,credits,target_tps");
                Record("split-scale START unitsPerSeat=1 seconds="+seconds+" observer=true bots=8 cameras=4 speed=4 targetTps="+runtime.TargetTicksPerSecond+" seed="+view.Seed+" map="+profile.AuthoredMap.Id+" profile="+profile.ProfileId+"@"+profile.Revision+" width="+Screen.width+" height="+Screen.height+" quality="+QualitySettings.GetQualityLevel()+" logging="+TestMatchLogSession.CurrentDirectory);
            }
            float elapsed=now-routePerformanceStarted;
            if(!string.IsNullOrEmpty(view.Failure)||view.Metrics.Errors>0||paused){splitSamples?.Dispose();splitSamples=null;BeginQaFinish("FAIL observer gameplay/pause "+view.Failure,true);return;}
            if(now>=splitNextSample){
                splitNextSample=now+1;
                foreach(var perspective in spectatorFrame.Players){var own=perspective.Entities.Where(e=>e.Owner==perspective.Owner).ToArray();
                    splitSamples.WriteLine(string.Join(",",elapsed.ToString("R",CultureInfo.InvariantCulture),elapsed<15?0:1,perspective.Tick,perspective.OwnerId,own.Length,own.Count(e=>e.Moving),own.Sum(e=>e.Health),0,own.Length==0?0:own.Average(e=>e.Position.X),own.Length==0?0:own.Average(e=>e.Position.Z),runtime.DiagnosticGlobalUnits,GC.GetTotalMemory(false),GC.CollectionCount(0),perspective.Buildings.Count(b=>b.Owner==perspective.Owner),perspective.Credits,runtime.TargetTicksPerSecond));}
                splitSamples.Flush();
            }
            if(observerScreenshots==0&&elapsed>=15||observerScreenshots==1&&elapsed>=seconds-5){
                ScreenCapture.CaptureScreenshot(Path.Combine(evidence,"observer-"+observerScreenshots+".png"));observerScreenshots++;
            }
            if(elapsed>=seconds){splitSamples.Dispose();splitSamples=null;BeginQaFinish("split-scale measurement completed");}
        }
#endif
    }
}
