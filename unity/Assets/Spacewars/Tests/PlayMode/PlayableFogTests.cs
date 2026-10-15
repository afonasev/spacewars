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
    [TestCase(32d,16d,4)]
    [TestCase(360d,360d,256)]
    [TestCase(19d,240d,64)]
    public void BoundedRasterMatchesAllSourceReference(double halfWidth,double halfDepth,int count)
    {
        var p=PlayableProfile.Default;int size=PlayableVision.RasterResolution;
        var vision=new PlayableVision(0,halfWidth,halfDepth,4,p.FogEdgeFeather);
        var random=new System.Random(7108);var sources=new VisionSource[count];
        using(var fog=new PlayableFogMask(p))
        {
            var current=Enumerable.Repeat((float)p.FogUnseenOpacity,size*size).ToArray();
            var expected=new Color32[current.Length];double referenceMs=0,boundedMs=0;
            for(int step=0;step<4;step++)
            {
                for(int n=0;n<count;n++)sources[n]=new VisionSource(new NavPoint((random.NextDouble()*2.4-1.2)*halfWidth,(random.NextDouble()*2.4-1.2)*halfDepth),1+random.NextDouble()*12);
                vision.Refresh(step==2?Array.Empty<VisionSource>():sources,Empty);var view=vision.Snapshot();
                float seconds=step==3?1f:.016f;
                var timer=System.Diagnostics.Stopwatch.StartNew();
                for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                {
                    int index=y*size+x;double wx=((x+.5)/size*2-1)*view.HalfWidth,wz=((y+.5)/size*2-1)*view.HalfDepth;
                    float visible=0;
                    for(int n=0;n<view.Sources.Count;n++)
                    {
                        var source=view.Sources[n];double dx=wx-source.Position.X,dz=wz-source.Position.Z;
                        visible=Math.Max(visible,(float)Math.Max(0,Math.Min(1,(source.Radius+p.FogEdgeFeather-Math.Sqrt(dx*dx+dz*dz))/p.FogEdgeFeather)));
                    }
                    float explored=view.Coverage[index]/255f;
                    float target=Mathf.Lerp(Mathf.Lerp((float)p.FogUnseenOpacity,(float)p.FogExploredOpacity,explored),0,visible);
                    current[index]=Mathf.MoveTowards(current[index],target,seconds*1000/(float)(target<current[index]?p.FogRevealMs:p.FogConcealMs));
                    expected[index]=new Color32((byte)Mathf.RoundToInt(Mathf.Clamp01(current[index])*255),(byte)Mathf.RoundToInt(Mathf.Clamp01(explored)*255),0,255);
                }
                referenceMs+=timer.Elapsed.TotalMilliseconds;timer.Restart();
                fog.Update(view,seconds);boundedMs+=timer.Elapsed.TotalMilliseconds;
                CollectionAssert.AreEqual(expected,fog.Texture.GetPixels32(),"Every channel, including transitions, must match at step "+step);
            }
            long referenceSamples=3L*size*size*count;
            Assert.Less(fog.TargetSourceSamples,referenceSamples);
            if(count==256)Assert.Less(fog.TargetSourceSamples,referenceSamples/10,"Large-map raster must avoid scanning all sources at every pixel.");
            TestContext.WriteLine("FOG_BOUNDED sources="+count+" reference_ms="+referenceMs+" bounded_ms="+boundedMs+" reference_samples="+referenceSamples+" bounded_samples="+fog.TargetSourceSamples);
        }
    }
    [Test] public void CachedMapPaintKeepsEveryPixelForFadesUnionResetAndPublicTransitions()
    {
        var p=PlayableProfile.ThreeCrossingsDefault;int size=PlayableVision.RasterResolution;
        var a=new PlayableVision(0,64,64,4,p.FogEdgeFeather);var b=new PlayableVision(1,64,64,4,p.FogEdgeFeather);
        using(var fog=new PlayableFogMask(p))using(var other=new PlayableFogMask(p))using(var map=new PlayableMapTerrain(p))
        {
            var ground=(Color[])typeof(PlayableMapTerrain).GetField("terrain",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(map);
            var tint=new Color((float)p.FogTintR,(float)p.FogTintG,(float)p.FogTintB);var expected=new Color32[size*size];
            for(int step=0;step<32;step++){
                if(step==16){fog.Reset();other.Reset();}
                if(step==20)map.ShowPublicTerrain();
                if(step%4==0){
                    a.Refresh(step==12?Array.Empty<VisionSource>():new[]{new VisionSource(new NavPoint(-30+step,step-20),7)},Empty);
                    b.Refresh(new[]{new VisionSource(new NavPoint(24-step,8),10)},Empty);
                }
                fog.Update(a.Snapshot(),step%5==0?.05f:.016f);other.Update(b.Snapshot(),.016f);
                bool union=step%2==0;if(union)map.UpdateUnion(new[]{fog,other});else map.Update(fog);
                var first=fog.Texture.GetPixels32();var second=other.Texture.GetPixels32();
                for(int i=0;i<expected.Length;i++){
                    byte opacity=union?Math.Min(first[i].r,second[i].r):first[i].r;
                    expected[i]=Color.Lerp(ground[i]*1.3f,tint,opacity/255f*.72f);
                }
                CollectionAssert.AreEqual(expected,map.Texture.GetPixels32(),"Every original map pixel at "+step);
            }
            Assert.Less(fog.TransitionPixels,32L*size*size/4,"Only unsettled pixels should fade.");
            Assert.Less(fog.TargetPixels,8L*size*size/2,"Unchanged input colors must be reused.");
            Assert.Less(map.PaintedPixels,32L*size*size/2,"Unchanged map colors must be reused.");
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
            var expected=new Vector3((float)p.CameraOffsetX,0,(float)p.CameraOffsetZ);expected=expected==Vector3.zero?Vector3.back:expected.normalized;
            Assert.That(Vector3.Dot(root.GetChild(0).forward,expected),Is.GreaterThan(.999),"Fog memories face the approved current camera regardless of remembered domain heading.");
            var live=world.Building(23,"Factory",false);world.FaceBuilding(live);
            Assert.That(Vector3.Dot(root.GetChild(0).forward,live.Hull.forward),Is.GreaterThan(.999),"Memory and actual live model use the same current presentation front.");
            vision.Refresh(new[]{new VisionSource(new NavPoint(0,0),13)},Empty);world.RenderMemories(snapshot());Assert.Zero(world.MemoryCount);
            var shader=Resources.Load<Shader>("TerritoryFog");Assert.IsNotNull(shader);Assert.True(shader.isSupported);
        }
        finally{world?.Dispose();UnityEngine.Object.DestroyImmediate(parent);}
    }
}
