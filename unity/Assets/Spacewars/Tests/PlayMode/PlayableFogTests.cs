using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;

public sealed class PlayableFogTests
{
    private static readonly KnownBuilding[] Empty=Array.Empty<KnownBuilding>();
    [Test] public void MaskTransitionsAndStopsAllSettledRebuildsScansAndUploads()
    {
        var profile=PlayableProfile.Default;var vision=new PlayableVision(0,32,16,4,profile.FogEdgeFeather);
        using(var fog=new PlayableFogMask(profile))
        {
            vision.Refresh(new[]{new VisionSource(new NavPoint(0,0),4)},Empty);
            int center=PlayableVision.RasterResolution/2;
            fog.Update(vision.Snapshot(),0);float unseen=fog.Texture.GetPixel(center,center).r;
            fog.Update(vision.Snapshot(),.05f);float opening=fog.Texture.GetPixel(center,center).r;
            Assert.Less(opening,unseen);Assert.Greater(opening,0);
            fog.Update(vision.Snapshot(),1);Assert.Less(fog.Texture.GetPixel(center,center).r,.01f);
            long builds=fog.TargetBuilds,uploads=fog.Uploads,scans=fog.ScannedFrames;
            for(int i=0;i<120;i++)Assert.False(fog.Update(vision.Snapshot(),1f/60));
            Assert.AreEqual(builds,fog.TargetBuilds);Assert.AreEqual(uploads,fog.Uploads);Assert.AreEqual(scans,fog.ScannedFrames);
            vision.Refresh(Array.Empty<VisionSource>(),Empty);fog.Update(vision.Snapshot(),.05f);
            Assert.Greater(fog.Texture.GetPixel(center,center).r,0);Assert.Less(fog.Texture.GetPixel(center,center).r,profile.FogExploredOpacity);
            fog.Update(vision.Snapshot(),1);Assert.AreEqual(profile.FogExploredOpacity,fog.Texture.GetPixel(center,center).r,.005);
            Assert.AreEqual(profile.FogUnseenOpacity,fog.Texture.GetPixel(0,0).r,.005);
        }
    }
    [Test] public void MemoryOnlyChangesDoNotInvalidateTerrainMaskAndResetClearsHistory()
    {
        var p=PlayableProfile.Default;var vision=new PlayableVision(0,32,32,4,p.FogEdgeFeather);
        var sources=new[]{new VisionSource(new NavPoint(0,0),13)};
        using(var fog=new PlayableFogMask(p))
        {
            vision.Refresh(sources,Empty);fog.Update(vision.Snapshot(),1);long builds=fog.TargetBuilds,uploads=fog.Uploads;
            vision.Refresh(sources,new[]{new KnownBuilding(2,1,PlayableOwner.Enemy,PlayableBuildingKind.Factory,new NavPoint(10,0),0)});
            Assert.False(fog.Update(vision.Snapshot(),1));Assert.AreEqual(builds,fog.TargetBuilds);Assert.AreEqual(uploads,fog.Uploads);
            fog.Reset();Assert.AreEqual(p.FogUnseenOpacity,fog.Texture.GetPixel(128,128).r,.005);
        }
    }
    [Test] public void MovingMaskAllocationAndPixelBaseline()
    {
        // Fixed diagnostic trajectory, not balance tuning. Golden pixels pin the pre-optimization renderer.
        var p=PlayableProfile.Default;
        var vision=new PlayableVision(0,32,16,4,p.FogEdgeFeather);
        var views=new TeamVisionSnapshot[17];
        for(int step=0;step<views.Length;step++)
        {
            var sources=step==8||step==9?Array.Empty<VisionSource>():new[]{
                new VisionSource(new NavPoint(-30+step*4,-15+step*2),7),
                new VisionSource(new NavPoint(20-step*2,5),10),
                new VisionSource(new NavPoint(31,15-step),4)};
            vision.Refresh(sources,Empty);views[step]=vision.Snapshot();
        }
        for(int trial=0;trial<3;trial++)
        using(var fog=new PlayableFogMask(p))
        using(var digest=System.Security.Cryptography.SHA256.Create())
        {
            fog.Update(views[0],.016f);
            var times=new double[16];
            for(int step=1;step<views.Length;step++)
            {
                long start=System.Diagnostics.Stopwatch.GetTimestamp();
                fog.Update(views[step],step%3==0?.05f:.016f);
                times[step-1]=(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000d/System.Diagnostics.Stopwatch.Frequency;
                var colors=fog.Texture.GetPixels32();var raw=new byte[colors.Length*4];
                for(int i=0;i<colors.Length;i++){raw[4*i]=colors[i].r;raw[4*i+1]=colors[i].g;raw[4*i+2]=colors[i].b;raw[4*i+3]=colors[i].a;}
                digest.TransformBlock(raw,0,raw.Length,null,0);
            }
            digest.TransformFinalBlock(Array.Empty<byte>(),0,0);
            string hash=BitConverter.ToString(digest.Hash).Replace("-","").ToLowerInvariant();
            Assert.AreEqual("e01666089c1b9ec6f1fc06c069a44d6781d9d47eab9bf1aa19f79d3d667c44b8",hash);
            long sampleValue=0;int allocations=0;string unit;
            using(var recorder=Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Internal,"GC.Alloc",70000,Unity.Profiling.ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
            {
                fog.Update(views[1],.016f);
                recorder.Stop();
                Assert.True(recorder.Valid);Assert.Less(recorder.Count,recorder.Capacity,"Allocation recording must not truncate.");
                allocations=recorder.Count;unit=recorder.UnitType.ToString();
                for(int i=0;i<recorder.Count;i++)sampleValue+=recorder.GetSample(i).Value;
            }
            Assert.Zero(allocations,"Moving fog must not allocate an enumerator for each pixel.");
            using(var calibration=Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Internal,"GC.Alloc",100,Unity.Profiling.ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
            {
                var probe=new byte[1024];GC.KeepAlive(probe);calibration.Stop();
                Assert.Greater(calibration.Count,0,"Allocation recorder must detect the calibration allocation.");
            }
            Array.Sort(times);
            TestContext.WriteLine("FOG_BASELINE trial="+trial+" sample_value_sum="+sampleValue+" unit="+unit+" allocations="+allocations+" median_ms="+times[8].ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+" p95_ms="+times[15].ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+" sha256="+hash);
        }
    }
    [Test] public void RememberedModelHasNoLiveFeedbackAndIsRemovedOnVisibleConfirmation()
    {
        var p=PlayableProfile.Default;var parent=new GameObject("memory test");PlayableWorld world=null;
        try
        {
            world=new PlayableWorld(parent.transform,p);var vision=new PlayableVision(0,32,32,4,p.FogEdgeFeather);
            var building=new KnownBuilding(22,1,PlayableOwner.Enemy,PlayableBuildingKind.Factory,new NavPoint(10,0),.7);
            vision.Refresh(new[]{new VisionSource(new NavPoint(0,0),13)},new[]{building});
            vision.Refresh(new[]{new VisionSource(new NavPoint(-25,0),3)},Empty);
            Func<PlayableSnapshot> snapshot=()=>new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,new NavGeometry(32,Array.Empty<NavObstacle>(),1),Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot());
            world.RenderMemories(snapshot());Assert.AreEqual(1,world.MemoryCount);Assert.IsEmpty(world.Actors);
            var root=parent.GetComponentsInChildren<Transform>().Single(t=>t.name=="Remembered building 22");
            Assert.False(root.GetComponentsInChildren<Transform>().Any(t=>t.name=="Health"||t.name=="Selection"));
            vision.Refresh(new[]{new VisionSource(new NavPoint(0,0),13)},Empty);world.RenderMemories(snapshot());Assert.Zero(world.MemoryCount);
            var shader=Resources.Load<Shader>("TerritoryFog");Assert.IsNotNull(shader);Assert.True(shader.isSupported);
        }
        finally{world?.Dispose();UnityEngine.Object.DestroyImmediate(parent);}
    }
}
