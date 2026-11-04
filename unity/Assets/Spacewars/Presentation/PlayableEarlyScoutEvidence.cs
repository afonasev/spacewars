#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
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
        private string earlyScoutOutput;
        private EarlyScoutPerformanceProbe earlyScoutProbe;
        private readonly List<object> earlyScoutSamples=new List<object>();
        private NativeRouteFrameSample[] earlyScoutFrames;private int earlyScoutFrameCount,earlyScoutDropped;
        private NativeFrameTimingProbe earlyScoutFrameProbe;
        private double earlyScoutBegan=-1,earlyScoutNextSample;private long earlyScoutStartTick,earlyScoutStartStamp;
        private bool earlyScoutMoveSent,earlyScoutFinished;
        private IEnumerator RunEarlyScoutEntry()
        {
            double deadline=Time.realtimeSinceStartupAsDouble+120;
            while((initializingMenu||nativeMainMenu?.ScreenRoot==null)&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            if(nativeMainMenu?.ScreenRoot==null){File.WriteAllText(Path.Combine(earlyScoutOutput,"failure.txt"),"Normal main menu preparation timeout");Application.Quit(1);yield break;}
            Screen.SetResolution(1280,720,FullScreenMode.Windowed);yield return null;yield return null;
            var play=nativeMainMenu.ScreenRoot.Q<Button>("main-play");play.Focus();menuNavigation.ActivateElement(play);
            while(lobbyScreen.style.display.value!=DisplayStyle.Flex&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            if(lobbyScreen.style.display.value!=DisplayStyle.Flex)throw new InvalidOperationException("Normal lobby entry failed");
            if(lobbySetup.Participants.Count!=2||lobbySetup.Participants[0].Human!=true||lobbySetup.Participants[1].Human!=false)throw new InvalidOperationException("Bind the normal two-seat human/AI roster");
            launchButton.Focus();menuNavigation.ActivateElement(launchButton);
            while((inLobby||preparing||runtime==null)&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            if(inLobby||preparing||runtime==null){File.WriteAllText(Path.Combine(earlyScoutOutput,"failure.txt"),"Normal match preparation timeout");runtime?.RequestStop();Application.Quit(1);}
        }
        private void DriveEarlyScoutEvidence()
        {
            if(earlyScoutOutput==null||earlyScoutFinished||runtime==null||view==null||inLobby||preparing)return;
            double now=Time.realtimeSinceStartupAsDouble;
            if(earlyScoutBegan<0){
                if(panelRoot.Q("native-main-menu")?.resolvedStyle.display==DisplayStyle.Flex)throw new InvalidOperationException("Main menu covers measured battlefield");
                earlyScoutFrames=new NativeRouteFrameSample[8192];earlyScoutFrameProbe=new NativeFrameTimingProbe(earlyScoutOutput);
                earlyScoutBegan=now;earlyScoutStartTick=view.Tick;earlyScoutStartStamp=System.Diagnostics.Stopwatch.GetTimestamp();
                File.WriteAllText(Path.Combine(earlyScoutOutput,"binding.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{seed=view.Seed,map=profile.AuthoredMap.Id,profile=profile.ProfileId,profile.Revision,profileData=profile.CopyData(),width=Screen.width,height=Screen.height,quality=QualitySettings.names[QualitySettings.GetQualityLevel()],device=SystemInfo.deviceModel,cpu=SystemInfo.processorType,gpu=SystemInfo.graphicsDeviceName,participants=view.Participants,recording=TestMatchLogSession.CurrentDirectory,clock="realtimeSinceStartupAsDouble; actual background runtime ticks; no Step/Pump in driver"},Newtonsoft.Json.Formatting.Indented));
            }
            double elapsed=now-earlyScoutBegan;
            var frame=new NativeRouteFrameSample();earlyScoutFrameProbe.Read(ref frame);
            if(earlyScoutFrameCount<earlyScoutFrames.Length)earlyScoutFrames[earlyScoutFrameCount++]=frame;else earlyScoutDropped++;
            if(!earlyScoutMoveSent&&elapsed>=10){
                var actor=view.Entities.First(e=>e.Owner==view.Owner&&e.Kind==PlayableEntityKind.Explorer);
                var goal=view.PublicScoutObjectives.Where(o=>o.Reachable&&o.SiteId!=view.HomeSiteId).OrderBy(o=>Math.Pow(o.Approach.X-actor.Position.X,2)+Math.Pow(o.Approach.Z-actor.Position.Z,2)).First();
                var command=new PlayableCommand(runtime.Generation,++sequence,view.OwnerId,PlayableCommandKind.Move,new[]{actor.Id},goal.Approach);
                var result=runtime.TrySubmit(command);if(!result.Accepted)throw new InvalidOperationException("Fixed human Move admission "+result.Status);earlyScoutMoveSent=true;
            }
            if(elapsed>=earlyScoutNextSample){
                earlyScoutNextSample=elapsed+.1;
                var owners=view.Participants;
                var perspectives=owners.Select(p=>runtime.ParticipantView(p.Id)).ToArray();
                var actors=perspectives.SelectMany(p=>p.Entities.Where(e=>e.Owner==p.Owner)).ToArray();
                earlyScoutSamples.Add(new{elapsed,tick=view.Tick,viewGeometry=view.Geometry.Revision,knowledge=perspectives.Select(p=>new{p.OwnerId,vision=p.Vision?.Revision,fog=p.Vision?.FogRevision,visibleGeometry=p.Geometry.Revision,knownBuildings=p.Vision?.KnownBuildings.Count,discoveredSites=p.DiscoveredSites.Count}).ToArray(),metrics=view.Metrics,units=runtime.DiagnosticGlobalUnits,buildings=runtime.DiagnosticGlobalBuildings,barrierPolls=runtime.BarrierPolls,payloadMs=runtime.PayloadBuildElapsedMilliseconds,heap=GC.GetTotalMemory(false),gc0=GC.CollectionCount(0),actors=actors.Select(e=>new{e.Id,e.Owner,e.Position,e.Moving,e.NavigationOutcome,order=e.CurrentOrder?.CommandSequence,stamp=e.OrderStamp?.Sequence,target=e.CurrentOrder?.Destination}).ToArray()});
            }
            if(elapsed<40)return;
            earlyScoutFinished=true;
            File.WriteAllText(Path.Combine(earlyScoutOutput,"samples.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{start=earlyScoutBegan,end=now,startStamp=earlyScoutStartStamp,endStamp=System.Diagnostics.Stopwatch.GetTimestamp(),startTick=earlyScoutStartTick,endTick=view.Tick,wall=elapsed,rawSpeed=(view.Tick-earlyScoutStartTick)/30d/elapsed,earlyScoutDropped,samples=earlyScoutSamples},Newtonsoft.Json.Formatting.Indented));
            File.WriteAllText(Path.Combine(earlyScoutOutput,"frames.json"),Newtonsoft.Json.JsonConvert.SerializeObject(earlyScoutFrames.Take(earlyScoutFrameCount).Select(f=>new{f.StopwatchStamp,f.WallFrameMs,f.TimingStamp,f.GpuMs,f.CpuFrameMs,f.CpuMainMs,f.CpuRenderMs,f.PresentWaitMs,f.MainThreadNs,f.RenderThreadNs,f.GcFrameBytes,f.GcAllocBytes,f.DrawCalls,f.Triangles}),Newtonsoft.Json.Formatting.None));
            earlyScoutFrameProbe.Dispose();earlyScoutFrameProbe=null;
            StartCoroutine(FinishEarlyScoutEvidence());
        }
        private IEnumerator FinishEarlyScoutEvidence()
        {
            // Rendered evidence is taken after the measured window, so screenshot
            // readback/storage cannot create a measured long frame.
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(earlyScoutOutput,"world-after-window.png"));
            runtime.RequestStop();double deadline=Time.realtimeSinceStartupAsDouble+30;
            while(!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            while(!routeService.StopDiagnosticWorker()&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            File.WriteAllText(Path.Combine(earlyScoutOutput,"runtime.json"),Newtonsoft.Json.JsonConvert.SerializeObject(earlyScoutProbe?.Capture(),Newtonsoft.Json.Formatting.None));
            TestMatchLogSession.PrepareForQuit();
            File.WriteAllText(Path.Combine(earlyScoutOutput,"complete.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{stopped=runtime.IsStopped,runtime.JournalFailure,failure=runtime.Latest.Failure,earlyScoutDropped,recording=TestMatchLogSession.CurrentDirectory}));
            Application.Quit(runtime.IsStopped&&runtime.Latest.Failure==null&&runtime.JournalFailure==null&&earlyScoutDropped==0?0:1);
        }
    }
}
#endif
