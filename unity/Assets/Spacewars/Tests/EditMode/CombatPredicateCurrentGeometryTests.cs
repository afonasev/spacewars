using System;
using System.IO;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class CombatPredicateCurrentGeometryTests
    {
        static byte[] Bytes(Action<BinaryWriter> write){using(var m=new MemoryStream()){using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true))write(w);return m.ToArray();}}
        static void Run(CombatPoseWork work){int cap=2000000;while(work.Status==CombatPredicateStatus.Pending&&cap-->0)work.Advance(1);Assert.Greater(cap,0);}
        [TestCase(PlayableBuildingKind.Mine,false)]
        [TestCase(PlayableBuildingKind.ScientificCenter,false)]
        [TestCase(PlayableBuildingKind.ScientificCenter,true)]
        public void CurrentMineAndScannerPreparedSegmentsOverlapAndColdCursorMatchAuthoredSurface(PlayableBuildingKind kind,bool upgraded)
        {
            var p=PlayableProfile.Default;AiCombatMicroSafety.PrepareExecutionAssets(p);Assert.True(BuildingCombatGeometry.TryPreparedPose(p,kind,upgraded,new NavPoint(3,-2),.7,out var prepared,heading:.43,constructionProgress:.65));var native=BuildingCombatGeometry.Pose(p,kind,upgraded,new NavPoint(3,-2),.7,heading:.43,constructionProgress:.65);
            foreach(double radius in new[]{0,.06,.3})foreach(var from in new[]{new BallisticPoint(-4,1,-2),new BallisticPoint(3,15,-2),new BallisticPoint(3,1,-2),new BallisticPoint(20,5,20)}){
                var to=new BallisticPoint(7,-1,-2);bool hit=native.Segment(from,to,radius,out double fraction,out var point);Assert.True(prepared.TryCreateSegmentWork(from,to,radius,_=>true,out var work));work.Advance(7);CombatPoseWork cold;using(var r=new BinaryReader(new MemoryStream(Bytes(work.Write))))cold=CombatPoseWork.Read(r,prepared,_=>true);Run(work);Run(cold);Assert.AreEqual(hit,work.Hit);Assert.AreEqual(fraction,work.Fraction);Assert.AreEqual(point.X,work.Contact.X);Assert.AreEqual(point.Y,work.Contact.Y);Assert.AreEqual(point.Z,work.Contact.Z);CollectionAssert.AreEqual(Bytes(work.Write),Bytes(cold.Write));Assert.True(prepared.TryCreateOverlapWork(from,radius,_=>true,out work));Run(work);Assert.AreEqual(native.Overlaps(from,radius),work.Hit);
            }
        }
        [Test]public void BothScannerSnapshotVariantsBindTheirActualOcclusionModel()
        {
            var normal=new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.ScientificCenter,default,100,1,0,0,default);
            var upgraded=new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.ScientificCenter,default,100,1,0,0,default,scientificCenterUpgraded:true);
            var a=new AiCombatMicroCoverScene.Building(normal);var b=new AiCombatMicroCoverScene.Building(upgraded);Assert.False(a.Upgraded);Assert.True(b.Upgraded);Assert.False(upgraded.RefineryUpgraded);Assert.True(upgraded.ModelUpgraded);
            AiCombatMicroSafety.PrepareExecutionAssets(PlayableProfile.Default);Assert.True(BuildingCombatGeometry.TryPreparedPose(PlayableProfile.Default,a.Kind,a.Upgraded,default,0,out var pa));Assert.True(BuildingCombatGeometry.TryPreparedPose(PlayableProfile.Default,b.Kind,b.Upgraded,default,0,out var pb));CollectionAssert.AreNotEqual(Bytes(pa.WriteQueryBinding),Bytes(pb.WriteQueryBinding));
        }
        [Test]public void CurrentMainProfileBindingRoundTripsWorldAndRejectsDifferentEffectiveTurretValues()
        {
            const string source="native-domain-world-v1:c77962dd:source:96a32f63:ai-release-c569e3a03045";var p=PlayableProfile.Default;PlayableDomain original=null,cold=null;
            try{original=new PlayableDomain(p,905,false);var bytes=original.CaptureWorldBytes(7,source);cold=PlayableDomain.RestoreWorldBytes(bytes,p,7,source);CollectionAssert.AreEqual(bytes,cold.CaptureWorldBytes(7,source));var data=p.CopyData();data.turretRangeMultiplier+=.125;var changed=PlayableProfile.Create(data);CollectionAssert.AreNotEqual(WorldWire.Binding(p),WorldWire.Binding(changed));Assert.Throws<ArgumentException>(()=>PlayableDomain.RestoreWorldBytes(bytes,changed,7,source));}
            finally{foreach(var domain in new[]{original,cold})if(domain!=null){domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();}}
        }
    }
}
