using System;
using System.IO;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using Spacewars.Runtime;

namespace Spacewars.Presentation
{
    // Optional read-only measurement. It never changes pacing, focus, viewport, time or quality.
    internal sealed class NativeFrameTimingProbe : IDisposable
    {
        private readonly FrameTiming[] timings=new FrameTiming[1];
        private readonly ProfilerRecorder draws,batches,passes,triangles,main,render,gcBytes,gcAllocs;
        private long previous;
        internal NativeFrameTimingProbe(string directory)
        {
            draws=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Draw Calls Count",1);
            batches=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Batches Count",1);
            passes=ProfilerRecorder.StartNew(ProfilerCategory.Render,"SetPass Calls Count",1);
            triangles=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Triangles Count",1);
            main=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Main Thread",1);
            render=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Render Thread",1);
            gcBytes=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",1);
            gcAllocs=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"GC.Alloc",1,ProfilerRecorderOptions.StartImmediately|ProfilerRecorderOptions.WrapAroundWhenCapacityReached|ProfilerRecorderOptions.SumAllSamplesInFrame);
            var pipeline=GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            File.WriteAllText(Path.Combine(directory,"precise-render-host.json"),JsonUtility.ToJson(new Host {
                unity=Application.unityVersion,os=SystemInfo.operatingSystem,cpu=SystemInfo.processorType,gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),
                width=Screen.width,height=Screen.height,quality=QualitySettings.GetQualityLevel(),vsync=QualitySettings.vSyncCount,targetFrameRate=Application.targetFrameRate,
                frameTimingEnabled=FrameTimingManager.IsFeatureEnabled(),focused=Application.isFocused,fullScreenMode=Screen.fullScreenMode.ToString(),
                pipeline=GraphicsSettings.currentRenderPipeline?.name,renderScale=pipeline==null?-1:pipeline.renderScale,msaa=pipeline==null?-1:pipeline.msaaSampleCount,
                hdr=pipeline!=null&&pipeline.supportsHDR,shadowResolution=pipeline==null?-1:pipeline.mainLightShadowmapResolution,
                clockFrequency=System.Diagnostics.Stopwatch.Frequency,missing="-1 unavailable; FrameTiming data is delayed; exclude duplicate/nonpositive timing_stamp; CPU profile is inclusive and has per-thread CPU clock on macOS"
            },true));
        }
        [Serializable] private sealed class Host
        {
            public string unity,os,cpu,gpu,api,pipeline,fullScreenMode,missing;public int width,height,quality,vsync,targetFrameRate,msaa,shadowResolution;
            public bool frameTimingEnabled,focused,hdr;public float renderScale;public long clockFrequency;
        }
        private static long Value(ProfilerRecorder r)=>r.Valid&&r.Count>0?r.LastValue:-1;
        internal void Read(ref NativeRouteFrameSample s)
        {
            long now=System.Diagnostics.Stopwatch.GetTimestamp();s.StopwatchStamp=now;s.WallFrameMs=previous==0?-1:(now-previous)*1000d/System.Diagnostics.Stopwatch.Frequency;previous=now;
            s.UnityRealtime=Time.realtimeSinceStartupAsDouble;s.UnityUnscaledTime=Time.unscaledTimeAsDouble;s.CaptureDelta=Time.captureDeltaTime;s.MaximumDelta=Time.maximumDeltaTime;
            s.FixedStep=Time.inFixedTimeStep?1:0;s.Focused=Application.isFocused?1:0;s.Width=Screen.width;s.Height=Screen.height;s.Vsync=QualitySettings.vSyncCount;s.TargetFps=Application.targetFrameRate;
            s.DrawCalls=Value(draws);s.Batches=Value(batches);s.SetPass=Value(passes);s.Triangles=Value(triangles);s.MainThreadNs=Value(main);s.RenderThreadNs=Value(render);s.GcFrameBytes=Value(gcBytes);s.GcAllocBytes=Value(gcAllocs);
            s.GcAllocCalls=gcAllocs.Valid&&gcAllocs.Count>0?gcAllocs.GetSample(gcAllocs.Count-1).Count:-1;
            FrameTimingManager.CaptureFrameTimings();bool available=FrameTimingManager.GetLatestTimings(1,timings)>0;
            s.TimingStamp=available?timings[0].frameStartTimestamp:0;
            s.GpuMs=available&&timings[0].gpuFrameTime>0?timings[0].gpuFrameTime:-1;
            s.CpuFrameMs=available?timings[0].cpuFrameTime:-1;s.CpuMainMs=available?timings[0].cpuMainThreadFrameTime:-1;
            s.CpuRenderMs=available?timings[0].cpuRenderThreadFrameTime:-1;s.PresentWaitMs=available?timings[0].cpuMainThreadPresentWaitTime:-1;
        }
        public void Dispose(){draws.Dispose();batches.Dispose();passes.Dispose();triangles.Dispose();main.Dispose();render.Dispose();gcBytes.Dispose();gcAllocs.Dispose();}
    }
}
