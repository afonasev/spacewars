using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class VisionArrayTraversalTests
    {
        static TeamVisionSnapshot Snapshot(VisionSource[] sources)=>new TeamVisionSnapshot(1,0,0,128,128,4,sources,Array.Empty<long>(),new byte[PlayableVision.RasterResolution*PlayableVision.RasterResolution],Array.Empty<KnownBuilding>());
        static bool Original(IReadOnlyList<VisionSource> current,NavPoint point)
        {
            if(double.IsNaN(point.X)||double.IsInfinity(point.X)||double.IsNaN(point.Z)||double.IsInfinity(point.Z))return false;
            foreach(var s in current){double x=point.X-s.Position.X,z=point.Z-s.Position.Z;if(x*x+z*z<=(s.Radius+0.0001)*(s.Radius+0.0001))return true;}return false;
        }
        [Test] public void StaticAndSnapshotKeepExactBoundariesNonfiniteAndSourceOrder()
        {
            var center=new VisionSource(new NavPoint(0,0),3);double edge=3.0001;double above=BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(edge)+1);
            var points=new List<NavPoint>{default,new NavPoint(edge,0),new NavPoint(above,0),new NavPoint(-edge,0),new NavPoint(double.NaN,0),new NavPoint(0,double.NaN),new NavPoint(double.PositiveInfinity,0),new NavPoint(0,double.NegativeInfinity),new NavPoint(double.MaxValue,double.MaxValue)};
            var random=new Random(34251);for(int i=0;i<2000;i++)points.Add(new NavPoint(random.NextDouble()*40-20,random.NextDouble()*40-20));
            foreach(var sources in new[]{Array.Empty<VisionSource>(),new[]{default(VisionSource)},new[]{center},new[]{center,new VisionSource(new NavPoint(8,-2),4),center},new[]{new VisionSource(new NavPoint(8,-2),4),center}}){
                var snapshot=Snapshot(sources);foreach(var p in points){bool expected=Original(sources,p);Assert.AreEqual(expected,PlayableVision.Visible(sources,p));Assert.AreEqual(expected,snapshot.IsVisible(p));}
            }
            Assert.True(PlayableVision.Visible(new[]{center},new NavPoint(edge,0)));Assert.False(PlayableVision.Visible(new[]{center},new NavPoint(above,0)));
        }
        sealed class EnumerationOnly : IReadOnlyList<VisionSource>
        {
            public int Count=>throw new InvalidOperationException("Generic visibility never indexed this collection.");
            public VisionSource this[int i]=>throw new InvalidOperationException("Generic visibility never indexed this collection.");
            public IEnumerator<VisionSource> GetEnumerator(){yield return new VisionSource(default,2);}
            IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        }
        [Test] public void PublicGenericFallbackRetainsEnumerationSemantics()
        {
            var source=new EnumerationOnly();Assert.True(PlayableVision.Visible(source,default));Assert.False(PlayableVision.Visible(source,new NavPoint(3,0)));Assert.False(PlayableVision.Visible(source,new NavPoint(double.NaN,0)));
        }
        [Test] public void DetachedSourceArrayRemainsClonedReadOnlyAndIndependentOfLiveRefresh()
        {
            var sources=new[]{new VisionSource(default,3)};var snapshot=Snapshot(sources);sources[0]=new VisionSource(new NavPoint(20,20),1);
            Assert.True(snapshot.IsVisible(default));Assert.False(snapshot.IsVisible(new NavPoint(20,20)));Assert.AreEqual(default(NavPoint),snapshot.Sources[0].Position);
            Assert.Throws<NotSupportedException>(()=>((IList<VisionSource>)snapshot.Sources)[0]=sources[0]);
            var v=new PlayableVision(1,32,32,4,1.5);v.Refresh(new[]{new VisionSource(default,3)},Array.Empty<KnownBuilding>());var old=v.Snapshot();v.Refresh(sources,Array.Empty<KnownBuilding>());
            Assert.True(old.IsVisible(default));Assert.False(v.IsVisible(default));Assert.True(v.IsVisible(new NavPoint(20,20)));Assert.False(old.IsVisible(new NavPoint(20,20)));
        }
        [Test] public void ReportStaticLiveAndSnapshotTraversalWithoutTimingThreshold()
        {
            var sources=Enumerable.Range(0,64).Select(i=>new VisionSource(new NavPoint(i%8*8-28,i/8*8-28),3)).ToArray();var v=new PlayableVision(1,128,128,4,1.5);v.Refresh(sources,Array.Empty<KnownBuilding>());var snapshot=v.Snapshot();var points=Enumerable.Range(0,64).Select(i=>new NavPoint(i%8*10-35,i/8*10-35)).ToArray();
            int expected=points.Count(p=>Original(sources,p));const int count=10000;
            foreach(var query in new[]{new KeyValuePair<string,Func<NavPoint,bool>>("array",p=>PlayableVision.Visible(sources,p)),new KeyValuePair<string,Func<NavPoint,bool>>("live",v.IsVisible),new KeyValuePair<string,Func<NavPoint,bool>>("snapshot",snapshot.IsVisible)}){
                int found=0;var watch=Stopwatch.StartNew();for(int i=0;i<count;i++)foreach(var p in points)if(query.Value(p))found++;watch.Stop();Assert.AreEqual(expected*count,found);TestContext.WriteLine($"VISION_ARRAY kind={query.Key} queries={count*points.Length} hits={found} wall_ms={watch.Elapsed.TotalMilliseconds:F6} profile={PerformanceProfile.Enabled}");
            }
        }
    }
}
