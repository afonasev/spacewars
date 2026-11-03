using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class ScanPresentationCacheTests
    {
        [Test] public void ExactBaselineLightAndClipSamplesRemainUnchanged()
        {
            using var s=new ScanPerformanceScene();
            Assert.AreEqual("4eda79e2029acae101dc40fa65a1c93a32eac157d5ff54d08360dfb8b8cf0603",s.Digest(),"all vertex, light and line values in full/hidden/partial views");
        }
        [Test] public void RepeatedReadsReuseImmutableArraysUntilPhaseChanges()
        {
            using var s=new ScanPerformanceScene();var faces=s.Faces(0);var lines=s.Lines(0);var first=faces[0];
            for(int i=0;i<20;i++){Assert.AreSame(faces,s.Faces(0));Assert.AreSame(lines,s.Lines(0));}
            s.Advance();Assert.AreNotSame(faces,s.Faces(0));Assert.AreNotSame(lines,s.Lines(0));
            Assert.AreEqual(first,faces[0],"published old phase remains unchanged");
        }
        [Test] public void ChangedSightAndProfileInvalidateTheRelevantView()
        {
            using var s=new ScanPerformanceScene();var faces=s.Faces(0);
            var hidden=new PlayableVision(0,32,32,4,s.Profile.FogEdgeFeather);hidden.Refresh(Array.Empty<VisionSource>(),Array.Empty<KnownBuilding>());
            Assert.IsEmpty((OrbitalScanTriangle[])s.Call("ScanTriangles",hidden.Snapshot()));Assert.AreSame(faces,s.Faces(0));
            var data=s.Profile.CopyData();data.orbitalScanOpacity=.5;var next=PlayableProfile.Create(data);
            typeof(PlayableDomain).GetField("profile",ScanPerformanceScene.F).SetValue(s.Domain,next);
            var rebound=s.Faces(0);Assert.AreNotSame(faces,rebound);Assert.Greater(rebound.Length,0);Assert.Less(rebound.Max(f=>f.Opacity),faces.Max(f=>f.Opacity));
        }
        [Test] public void SightHistoryDoesNotGrowCacheAndExpiryDropsDerivedReferences()
        {
            using var s=new ScanPerformanceScene();foreach(int i in new[]{0,1,2})s.Faces(i);
            var cache=(IDictionary)typeof(PlayableDomain).GetField("scanViews",ScanPerformanceScene.F).GetValue(s.Domain);
            for(int i=0;i<20;i++){
                var v=new PlayableVision(2,32,32,4,s.Profile.FogEdgeFeather);v.Refresh(new[]{new VisionSource(new NavPoint(8+i*.01,0),6)},Array.Empty<KnownBuilding>());
                s.Call("ScanTriangles",v.Snapshot());Assert.LessOrEqual(cache.Count,3);
            }
            for(int i=0;i<100;i++)s.Advance();Assert.Zero(cache.Count);Assert.IsEmpty(s.Faces(0));Assert.IsEmpty(s.Lines(0));Assert.Zero(cache.Count);
        }
    }
}
