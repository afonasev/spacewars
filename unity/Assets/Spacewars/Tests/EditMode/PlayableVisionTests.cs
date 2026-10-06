using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using System.Reflection;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableVisionTests
    {
        private static readonly KnownBuilding[] Empty=Array.Empty<KnownBuilding>();
        private static VisionSource Source(double x,double z,double radius=13)=>new VisionSource(new NavPoint(x,z),radius);
        private static KnownBuilding Building(int id=1,double x=10,int team=2)=>new KnownBuilding(id,team,PlayableOwner.Enemy,PlayableBuildingKind.Factory,new NavPoint(x,0),.7);
        [Test] public void ExactCirclesDoNotUseDiscoveredCellsOrObstacleOcclusion()
        {
            var vision=new PlayableVision(1,32,20,4,1.5);
            vision.Refresh(new[]{Source(0,0)},Empty);
            Assert.True(vision.IsVisible(new NavPoint(13,0)));
            Assert.True(vision.IsVisible(new NavPoint(13.00005,0)));
            Assert.False(vision.IsVisible(new NavPoint(13.001,0)));
            Assert.True(vision.IsDiscovered(new NavPoint(13.1,0)));
            Assert.False(vision.IsVisible(new NavPoint(13.1,0)));
            Assert.False(vision.IsVisible(new NavPoint(double.NaN,0)));
        }
        [Test] public void AlliedSourcesUnionWhileOpposingKnowledgeRemainsIndependent()
        {
            var a=new PlayableVision(1,32,32,4,1.5);var b=new PlayableVision(2,32,32,4,1.5);
            a.Refresh(new[]{Source(-20,0,5),Source(20,0,5)},Empty);b.Refresh(new[]{Source(0,20,5)},Empty);
            Assert.True(a.IsVisible(new NavPoint(20,0)));Assert.True(a.IsVisible(new NavPoint(-20,0)));
            Assert.False(b.IsVisible(new NavPoint(20,0)));Assert.False(a.IsDiscovered(new NavPoint(0,20)));
        }
        [Test] public void HistorySurvivesSkippedSnapshotsAndPublishedBuffersDoNotMutate()
        {
            var v=new PlayableVision(1,32,16,4,1.5);v.Refresh(new[]{Source(-20,0,4)},Empty);var first=v.Snapshot();
            var bytes=first.Coverage.ToArray();
            v.Refresh(new[]{Source(0,0,4)},Empty);v.Refresh(new[]{Source(20,0,4)},Empty);
            var latest=v.Snapshot();Assert.True(v.IsDiscovered(new NavPoint(-20,0)));Assert.True(v.IsDiscovered(new NavPoint(0,0)));
            Assert.False(v.IsVisible(new NavPoint(-20,0)));Assert.Greater(latest.Coverage.Sum(x=>(int)x),bytes.Sum(x=>(int)x));
            CollectionAssert.AreEqual(bytes,first.Coverage);
            Assert.Throws<NotSupportedException>(()=>((IList<byte>)first.Coverage)[0]=123);
            Assert.AreEqual(16,latest.HalfDepth);Assert.AreEqual(32,latest.HalfWidth);
        }
        [Test] public void StationarySourcesReuseSnapshotAndDoNotRescanCoverage()
        {
            var v=new PlayableVision(1,32,32,4,1.5);var a=Source(0,0);var b=Source(20,0);
            v.Refresh(new[]{a,b},Empty);var snapshot=v.Snapshot();long count=v.CoverageUpdates;
            for(int i=0;i<100;i++)v.Refresh(new[]{b,a},Empty);
            Assert.AreSame(snapshot,v.Snapshot());Assert.AreEqual(count,v.CoverageUpdates);
        }
        [Test] public void MemoryPersistsThroughUnseenDestructionAndClearsOnlyOnRevisit()
        {
            var v=new PlayableVision(1,32,32,4,1.5);var building=Building();
            v.Refresh(new[]{Source(0,0)},new[]{building});var observed=v.Snapshot();Assert.AreEqual(1,observed.KnownBuildings.Count);
            v.Refresh(new[]{Source(-25,0)},new[]{building});v.Refresh(new[]{Source(-25,0)},Empty);
            Assert.AreSame(building,v.Snapshot().KnownBuildings.Single());
            v.Refresh(new[]{Source(0,0)},Empty);Assert.IsEmpty(v.Snapshot().KnownBuildings);Assert.AreEqual(1,observed.KnownBuildings.Count);
        }
        [Test] public void UnknownBuildingsAndHiddenReplacementDoNotChangeMemory()
        {
            var v=new PlayableVision(1,32,32,4,1.5);v.Refresh(new[]{Source(-25,0,3)},new[]{Building()});Assert.IsEmpty(v.Snapshot().KnownBuildings);
            v.Refresh(new[]{Source(0,0)},new[]{Building()});v.Refresh(new[]{Source(-25,0,3)},new[]{Building(2)});
            Assert.AreEqual(1,v.Snapshot().KnownBuildings.Single().Id);
            v.Refresh(new[]{Source(0,0)},new[]{Building(2)});Assert.AreEqual(2,v.Snapshot().KnownBuildings.Single().Id);
        }
        [Test] public void PendingHasNoVisionAndFoundationUsesReleaseMultiplier()
        {
            var p=PlayableProfile.Default;
            foreach(PlayableBuildingKind kind in Enum.GetValues(typeof(PlayableBuildingKind)))
            {
                Assert.Zero(PlayableVision.BuildingRadius(p,kind,ConstructionPhase.Pending));
                Assert.AreEqual(PlayableVision.BuildingRadius(p,kind,ConstructionPhase.Ready)*.5,PlayableVision.BuildingRadius(p,kind,ConstructionPhase.Constructing));
            }
            Assert.AreEqual(22,PlayableVision.BuildingRadius(p,PlayableBuildingKind.Headquarters,ConstructionPhase.Ready));
        }
        [Test] public void ProfileDefaultsMatchReleaseAndRejectMissingVision()
        {
            var data=(PlayableProfileData)typeof(PlayableProfile).GetMethod("DefaultData",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            var p=PlayableProfile.Create(data);Assert.AreEqual(PlayableProfile.RequiredProfileId,p.ProfileId);Assert.AreEqual(PlayableProfile.Default.FogRevealMs,p.FogRevealMs);
            Assert.AreEqual(4,p.VisionCellSize);Assert.AreEqual(14,p.MinimapLandmarkSize);
            data.headquartersVisionRange=0;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
            data.headquartersVisionRange=22;data.fogUnseenOpacity=2;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
        }
        [Test] public void MemoryRecordHasNoPrivateOrLiveFields()
        {
            CollectionAssert.AreEquivalent(new[]{"Id","Team","Owner","Kind","Position","Heading","RefineryUpgraded"},typeof(KnownBuilding).GetProperties().Select(p=>p.Name));
        }
    }
}
