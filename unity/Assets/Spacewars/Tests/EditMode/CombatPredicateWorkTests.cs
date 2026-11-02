using System;
using System.IO;
using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class CombatPredicateWorkTests
    {
        static byte[] Bytes(Action<BinaryWriter> write){using(var m=new MemoryStream()){using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true))write(w);return m.ToArray();}}
        static CombatPoseWork Cold(CombatPoseWork work,BuildingCombatPose pose){using(var r=new BinaryReader(new MemoryStream(Bytes(work.Write))))return CombatPoseWork.Read(r,pose,_=>true);}
        static TerrainLineWork Cold(TerrainLineWork work,TerrainLineInput input){using(var r=new BinaryReader(new MemoryStream(Bytes(work.Write))))return TerrainLineWork.Read(r,input,_=>true);}
        static void Run(CombatPoseWork work){int cap=2000000;while(work.Status==CombatPredicateStatus.Pending&&cap-->0)Assert.AreEqual(1,work.Advance(1));Assert.Greater(cap,0,"bounded authored test query must terminate");}
        static void Run(TerrainLineWork work){int cap=2000000;while(work.Status==CombatPredicateStatus.Pending&&cap-->0)Assert.AreEqual(1,work.Advance(1));Assert.Greater(cap,0);}
        [TestCase(PlayableBuildingKind.Headquarters,0,1)]
        [TestCase(PlayableBuildingKind.Factory,0,1)]
        [TestCase(PlayableBuildingKind.Factory,1,.35)]
        [TestCase(PlayableBuildingKind.Hangar,.5,1)]
        [TestCase(PlayableBuildingKind.Hangar,1,1)]
        [TestCase(PlayableBuildingKind.Refinery,0,.5)]
        [TestCase(PlayableBuildingKind.SentryTurret,0,1)]
        [TestCase(PlayableBuildingKind.SentryTurret,0,.4)]
        [TestCase(PlayableBuildingKind.RepairTower,0,.4)]
        [TestCase(PlayableBuildingKind.GaussTower,0,.6)]
        public void PreparedPoseSegmentAndOverlapExactlyMatchNative(PlayableBuildingKind kind,double opening,double progress)
        {
            var p=PlayableProfile.Default;var pose=BuildingCombatGeometry.Pose(p,kind,false,new NavPoint(3,-2),.7,heading:.43,opening:opening,constructionProgress:progress);
            Assert.True(BuildingCombatGeometry.TryPreparedPose(p,kind,false,new NavPoint(3,-2),.7,out var prepared,heading:.43,opening:opening,constructionProgress:progress));
            foreach(var a in new[]{new BallisticPoint(-4,1,-2),new BallisticPoint(3,15,-2),new BallisticPoint(3,1,-2),new BallisticPoint(20,5,20)})foreach(double radius in new[]{0,.06,.3}){
                var b=new BallisticPoint(7,-1,-2);bool expected=pose.Segment(a,b,radius,out double fraction,out var point);
                Assert.True(prepared.TryCreateSegmentWork(a,b,radius,_=>true,out var work));work.Advance(7);var cold=Cold(work,prepared);Assert.True(work.TryFork(_=>true,out var fork));Run(work);Run(cold);Run(fork);
                Assert.AreEqual(expected,work.Hit,$"{kind} {radius}");Assert.AreEqual(fraction,work.Fraction);Assert.AreEqual(point.X,work.Contact.X);Assert.AreEqual(point.Y,work.Contact.Y);Assert.AreEqual(point.Z,work.Contact.Z);CollectionAssert.AreEqual(Bytes(work.Write),Bytes(cold.Write));CollectionAssert.AreEqual(Bytes(work.Write),Bytes(fork.Write));
                Assert.True(prepared.TryCreateOverlapWork(a,radius,_=>true,out var overlap));overlap.Advance(4);var twin=Cold(overlap,prepared);Run(overlap);Run(twin);Assert.AreEqual(pose.Overlaps(a,radius),overlap.Hit,$"overlap {kind} {radius} at({a.X},{a.Y},{a.Z}) ");CollectionAssert.AreEqual(Bytes(overlap.Write),Bytes(twin.Write));
            }
        }
        [TestCase(PlayableBuildingKind.SentryTurret,1,.3)]
        [TestCase(PlayableBuildingKind.RepairTower,.35,-.2)]
        [TestCase(PlayableBuildingKind.GaussTower,.6,.45)]
        public void ArticulatedPitchRecoilAndConstructionMatchNative(PlayableBuildingKind kind,double progress,double pitch)
        {
            var p=PlayableProfile.Default;BuildingCombatGeometry.Pose(p,kind,false,default,0);
            Assert.True(BuildingCombatGeometry.TryPreparedTurretPose(p,kind,new NavPoint(1,-2),.3,.7,pitch,progress,.03,out var pose));
            foreach(double radius in new[]{0,.06}){
                var a=new BallisticPoint(-5,2,-2);var b=new BallisticPoint(6,.4,-2);bool expected=pose.Segment(a,b,radius,out var fraction,out _);
                Assert.True(pose.TryCreateSegmentWork(a,b,radius,_=>true,out var work));work.Advance(51);var cold=Cold(work,pose);Run(work);Run(cold);Assert.AreEqual(expected,work.Hit);Assert.AreEqual(fraction,work.Fraction);CollectionAssert.AreEqual(Bytes(work.Write),Bytes(cold.Write));
            }
        }
        [TestCase(PlayableBuildingKind.Headquarters)]
        [TestCase(PlayableBuildingKind.Hangar)]
        [TestCase(PlayableBuildingKind.SentryTurret)]
        public void SegmentRootAggregationMatchesNativeWithHugeFiniteEndpoints(PlayableBuildingKind kind)
        {
            var pose=BuildingCombatGeometry.Pose(PlayableProfile.Default,kind,false,default,0,opening:.5);
            foreach(double extent in new[]{1e150,1e200,1e300}){
                var a=new BallisticPoint(-extent,1,extent);var b=new BallisticPoint(extent,-1,-extent);bool hit=pose.Segment(a,b,.06,out double fraction,out var point);
                Assert.True(pose.TryCreateSegmentWork(a,b,.06,_=>true,out var work));work.Advance(19);var cold=Cold(work,pose);Run(work);Run(cold);Assert.AreEqual(hit,work.Hit);Assert.AreEqual(fraction,work.Fraction);Assert.AreEqual(point.X,work.Contact.X);Assert.AreEqual(point.Y,work.Contact.Y);Assert.AreEqual(point.Z,work.Contact.Z);CollectionAssert.AreEqual(Bytes(work.Write),Bytes(cold.Write));
            }
        }
        [Test]public void MixedScaleBoundaryUsesExactlyTheNativeRaySamples()
        {
            var a=new BallisticPoint(0,2,0);
            for(int n=1;n<=17;n++)foreach(double direction in new[]{-1d,1d}){
                double x=.025*n;long bits=BitConverter.DoubleToInt64Bits(x);x=BitConverter.Int64BitsToDouble(bits+(long)direction);
                var b=new BallisticPoint(x,2+1e-8,1e-9);int calls=0;Assert.True(PlayableBallistics.TerrainLineClear(a,b,0,Array.Empty<NavObstacle>(),1.74,_=>{calls++;return -1;}));
                var input=Line(a,b,0,Array.Empty<NavObstacle>(),_=>-1);Assert.AreEqual(calls,input.HeightCount,"source-native sample-count oracle near .025 ceil boundary");
            }
        }
        [Test]public void ImpossibleTerminalAndAdmissionRefusalCannotFabricateProof()
        {
            var pose=BuildingCombatGeometry.Pose(PlayableProfile.Default,PlayableBuildingKind.Headquarters,false,default,0);Assert.True(pose.TryCreateSegmentWork(new BallisticPoint(-5,1,0),new BallisticPoint(5,1,0),0,_=>true,out var work));byte[] initial=Bytes(work.Write);Assert.False(work.TryFork(_=>false,out _));CollectionAssert.AreEqual(initial,Bytes(work.Write));
            using(var r=new BinaryReader(new MemoryStream(initial))){Assert.Throws<InvalidOperationException>(()=>CombatPoseWork.Read(r,pose,_=>false));Assert.Zero(r.BaseStream.Position);}
            byte[] bad=(byte[])initial.Clone();bad[bad.Length-12]=1;Assert.Throws<ArgumentException>(()=>{using(var r=new BinaryReader(new MemoryStream(bad)))CombatPoseWork.Read(r,pose,_=>true);});
            var input=Line(new BallisticPoint(0,1,0),new BallisticPoint(1,1,0),0,Array.Empty<NavObstacle>(),_=>0);Assert.True(TerrainLineWork.TryCreate(input,_=>true,out var ray));byte[] rayInitial=Bytes(ray.Write);Assert.False(ray.TryFork(_=>false,out _));CollectionAssert.AreEqual(rayInitial,Bytes(ray.Write));
            using(var r=new BinaryReader(new MemoryStream(rayInitial))){Assert.Throws<InvalidOperationException>(()=>TerrainLineWork.Read(r,input,_=>false));Assert.Zero(r.BaseStream.Position);}
            byte[] rayBad=(byte[])rayInitial.Clone();rayBad[rayBad.Length-4]=1;Assert.Throws<ArgumentException>(()=>{using(var r=new BinaryReader(new MemoryStream(rayBad)))TerrainLineWork.Read(r,input,_=>true);});
        }
        [Test]public void MeshGrantsRefusalAndWrongSnapshotNeverAdvance()
        {
            var pose=BuildingCombatGeometry.Pose(PlayableProfile.Default,PlayableBuildingKind.Hangar,false,default,0,opening:.5);
            Assert.False(pose.TryCreateSegmentWork(new BallisticPoint(0,12,0),new BallisticPoint(0,-1,0),.1,_=>false,out _));
            Assert.True(pose.TryCreateSegmentWork(new BallisticPoint(0,12,0),new BallisticPoint(0,-1,0),.1,_=>true,out var work));byte[] before=Bytes(work.Write);Assert.Zero(work.Advance(0,()=>throw new Exception("zero grant admission")));Assert.Zero(work.Advance(1,()=>false));CollectionAssert.AreEqual(before,Bytes(work.Write));
            work.Advance(25);var other=BuildingCombatGeometry.Pose(PlayableProfile.Default,PlayableBuildingKind.Hangar,false,default,0,opening:1);Assert.Throws<ArgumentException>(()=>Cold(work,other));
            byte[] negative=Bytes(work.Write);for(int i=negative.Length-8;i<negative.Length;i++)negative[i]=255;Assert.Throws<ArgumentException>(()=>{using(var r=new BinaryReader(new MemoryStream(negative)))CombatPoseWork.Read(r,pose,_=>true);});
            byte[] malformed=Bytes(work.Write);malformed[0]=99;Assert.Throws<ArgumentException>(()=>{using(var r=new BinaryReader(new MemoryStream(malformed)))CombatPoseWork.Read(r,pose,_=>true);});
        }
        static TerrainLineInput Line(BallisticPoint a,BallisticPoint b,double radius,NavObstacle[] solids,Func<NavPoint,double> height)
        {Assert.True(TerrainLineInput.TryCreate(a,b,radius,1.74,solids.Length,long.MaxValue,_=>true,out var input));for(int i=0;i<input.HeightCount;i++)input.AppendHeight(height(input.Sample(i).Ground));foreach(var s in solids)input.AppendSolid(s);input.Seal();return input;}
        [TestCase(0)][TestCase(.04)][TestCase(.3)]
        public void TerrainRaysExactlyMatchNativeAtSupportAndOcclusionBoundaries(double radius)
        {
            var polygon=new NavPolygon(new[]{new NavPoint(-1,-1),new NavPoint(1,-1),new NavPoint(1,1),new NavPoint(-1,1)});
            foreach(var solids in new[]{Array.Empty<NavObstacle>(),new[]{new NavObstacle(-1,-1,1,1)},new[]{new NavObstacle(polygon,.2,1.3)},new[]{new NavObstacle(new NavPoint(0,0),1)}})
            foreach(var a in new[]{new BallisticPoint(-2,1,0),new BallisticPoint(-2,1.74+radius,1+radius),new BallisticPoint(-2,.5,2)}){
                var b=new BallisticPoint(2,1.5,0);Func<NavPoint,double> height=q=>q.X<0?.1:.1+.2*q.X;var input=Line(a,b,radius,solids,height);Assert.True(TerrainLineWork.TryCreate(input,_=>true,out var work));
                byte[] initial=Bytes(work.Write);Assert.Zero(work.Advance(0));Assert.Zero(work.Advance(1,()=>false));CollectionAssert.AreEqual(initial,Bytes(work.Write));
                work.Advance(13);var cold=Cold(work,input);Assert.True(work.TryFork(_=>true,out var fork));Run(work);Run(cold);Run(fork);Assert.AreEqual(PlayableBallistics.TerrainLineClear(a,b,radius,solids,1.74,height),work.Clear);CollectionAssert.AreEqual(Bytes(work.Write),Bytes(cold.Write));CollectionAssert.AreEqual(Bytes(work.Write),Bytes(fork.Write));
            }
        }
        [Test]public void SnapshotAdmissionIncompleteAndMalformedCursorAreRejected()
        {
            var a=new BallisticPoint(0,1,0);var b=new BallisticPoint(1,1,0);Assert.False(TerrainLineInput.TryCreate(a,b,0,1,0,0,_=>throw new Exception("unpaid input allocation"),out _));Assert.False(TerrainLineInput.TryCreate(a,b,0,1,0,long.MaxValue,_=>false,out _));Assert.True(TerrainLineInput.TryCreate(a,b,0,1,0,long.MaxValue,_=>true,out var partial));Assert.Throws<ArgumentException>(()=>TerrainLineWork.TryCreate(partial,_=>true,out _));Assert.Throws<InvalidOperationException>(()=>partial.Seal());
            var input=Line(a,b,0,Array.Empty<NavObstacle>(),_=>0);Assert.False(TerrainLineWork.TryCreate(input,_=>false,out _));Assert.True(TerrainLineWork.TryCreate(input,_=>true,out var work));work.Advance(1);
            var foreign=Line(a,b,0,Array.Empty<NavObstacle>(),_=>1);Assert.Throws<ArgumentException>(()=>Cold(work,foreign));byte[] bad=Bytes(work.Write);bad[0]=2;Assert.Throws<ArgumentException>(()=>{using(var r=new BinaryReader(new MemoryStream(bad)))TerrainLineWork.Read(r,input,_=>true);});
        }
    }
}
