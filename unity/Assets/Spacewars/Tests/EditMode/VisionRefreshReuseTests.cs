using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class VisionRefreshReuseTests
    {
        private static PlayableVision Fresh()=>new PlayableVision(1,128,128,4,1.5);
        private static VisionSource S(double x,double radius=6)=>new VisionSource(new NavPoint(x,0),radius);
        private static KnownBuilding B(double heading=0,bool upgraded=false,int team=2)=>new KnownBuilding(7,team,PlayableOwner.Enemy,PlayableBuildingKind.Refinery,new NavPoint(10,0),heading,upgraded);
        private static string Bytes(PlayableVision v){using(var stream=new MemoryStream()){new DataContractJsonSerializer(typeof(PlayableVisionState)).WriteObject(stream,v.CaptureState());return Convert.ToBase64String(stream.ToArray());}}
        [Test] public void WarmRefreshMatchesColdStateThroughReorderMutationMemoryAndSkippedSnapshots()
        {
            var warm=Fresh();var state=Fresh().CaptureState();
            var inputs=new[]{new[]{S(10),S(-30)},new[]{S(-30),S(10)},new[]{S(10),S(10)},new[]{S(-30)},new[]{S(-30)},new[]{S(10)},new[]{S(10)},Array.Empty<VisionSource>(),new[]{S(10,8)}};
            var buildings=new[]{new[]{B()},new[]{B(.7,true)},new[]{B(.7,true)},Array.Empty<KnownBuilding>(),new[]{B(1)},Array.Empty<KnownBuilding>(),new[]{B(0,false,1)},new[]{B()},new[]{B()}};
            for(int step=0;step<inputs.Length;step++)for(int repeat=0;repeat<3;repeat++){
                var cold=Fresh();cold.RestoreState(state);cold.Refresh(inputs[step],buildings[step]);
                warm.Refresh(inputs[step],buildings[step]);Assert.AreEqual(Bytes(cold),Bytes(warm),"step="+step+" repeat="+repeat);
                // Input containers remain caller-owned. Replacing an entry must be observed.
                if(step==1&&repeat==0)buildings[step][0]=B(1.1,false);
                if(step==2&&repeat==0)inputs[step][1]=S(11);
                state=cold.CaptureState();
            }
            Assert.True(warm.IsDiscovered(new NavPoint(-30,0)));Assert.True(warm.Snapshot().KnownBuildings.Single().Id==7);
        }
        [Test] public void WarmEmptyInputCacheIsInvalidatedBySuccessfulRestore()
        {
            var source=Fresh();source.Refresh(new[]{S(10)},new[]{B()});
            var warm=Fresh();warm.Refresh(Array.Empty<VisionSource>(),Array.Empty<KnownBuilding>());warm.RestoreState(source.CaptureState());
            var cold=Fresh();cold.RestoreState(source.CaptureState());
            warm.Refresh(Array.Empty<VisionSource>(),Array.Empty<KnownBuilding>());cold.Refresh(Array.Empty<VisionSource>(),Array.Empty<KnownBuilding>());
            Assert.AreEqual(Bytes(cold),Bytes(warm));Assert.False(warm.IsVisible(new NavPoint(10,0)));Assert.AreEqual(1,warm.Snapshot().KnownBuildings.Count);
        }
        [Test] public void SameRecordIdentityDoesNotOverrideNumericFieldEquality()
        {
            // KnownBuilding itself accepts this value; preserve Refresh's existing
            // field equality rather than treating an identical reference as equal.
            var v=Fresh();var sources=new[]{S(10)};var buildings=new[]{B(double.NaN)};
            v.Refresh(sources,buildings);long revision=v.Snapshot().Revision;
            v.Refresh(sources,buildings);Assert.AreEqual(revision+1,v.Snapshot().Revision);
        }
        [Test] public void ReportRepeatedUnchangedRefreshWithoutTimingThreshold()
        {
            var v=Fresh();var sources=Enumerable.Range(0,64).Select(i=>new VisionSource(new NavPoint(i%8*8-28,i/8*8-28),6)).ToArray();
            var buildings=Enumerable.Range(0,32).Select(i=>new KnownBuilding(i+1,2,PlayableOwner.Enemy,PlayableBuildingKind.Factory,new NavPoint(i%8*8-28,i/8*8-28),0)).ToArray();
            v.Refresh(sources,buildings);var snapshot=v.Snapshot();string before=Bytes(v);const int count=10000;var watch=Stopwatch.StartNew();
            for(int i=0;i<count;i++)v.Refresh(sources,buildings);watch.Stop();
            Assert.AreEqual(before,Bytes(v));Assert.AreSame(snapshot,v.Snapshot());
            TestContext.WriteLine(string.Format(CultureInfo.InvariantCulture,"VISION_REFRESH count={0} sources=64 buildings=32 wall_ms={1:F6} profile={2}",count,watch.Elapsed.TotalMilliseconds,PerformanceProfile.Enabled));
        }
    }
}
