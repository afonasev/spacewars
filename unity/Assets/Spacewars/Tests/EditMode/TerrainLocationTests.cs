using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class TerrainLocationTests
    {
        [Test] public void AuthoredKeysAreUniqueStableAndIndependentOfRepeatedDisplayLabels()
        {
            var a=new FoundryMap(new FoundryProfileData());var b=new FoundryMap(new FoundryProfileData());
            Assert.Less(a.Supports.Select(s=>s.Id).Distinct().Count(),a.Supports.Count);
            Assert.AreEqual(a.Supports.Count,a.Supports.Select(s=>s.SurfaceId).Distinct().Count());
            CollectionAssert.AreEqual(a.Supports.Select(s=>s.SurfaceId),b.Supports.Select(s=>s.SurfaceId));
            Assert.True(a.TryLocate(a.Point(-77,0),1,out var left));Assert.True(a.TryLocate(a.Point(77,0),1,out var right));Assert.AreNotEqual(left.SurfaceId,right.SurfaceId);
            Assert.True(a.CompatibleCombatSurface(left,right)); // Existing fire/fog remains a separate predicate.
        }
        [TestCase(.7)][TestCase(1)][TestCase(1.4)] public void BridgeTraversalUsesNamedPortalsWithFullFootprint(double radius)
        {
            var m=ThreeCrossingsMap.Default;
            foreach(double z in new[]{-m.CrossingZ,0,m.CrossingZ}){
                var start=new NavPoint(-20,z);Assert.True(m.TryLocate(start,radius,out var a));Assert.AreEqual("bank/west",a.SurfaceId);
                Assert.True(m.TryTraverse(a,new NavPoint(0,z),radius,out var deck));Assert.True(deck.SurfaceId.StartsWith("bridge/"));
                Assert.True(m.TryTraverse(deck,new NavPoint(20,z),radius,out var end));Assert.AreEqual("bank/east",end.SurfaceId);
                Assert.True(m.CompatibleCombatSurface(a,end));
            }
            Assert.True(m.TryLocate(new NavPoint(-20,18),radius,out var bank));Assert.False(m.TryTraverse(bank,new NavPoint(20,18),radius,out _));
            Assert.False(m.TryLocate(new NavPoint(0,m.CentralBridgeWidth/2-radius/2),radius,out _));
        }
        [TestCase(.7)][TestCase(1)][TestCase(1.4)] public void FoundryRampSeamsAndJunctionsKeepRegionIdentityAndHeight(double radius)
        {
            var m=new FoundryMap(new FoundryProfileData());
            foreach(int side in new[]{-1,1})foreach(int end in new[]{-1,1}){
                Assert.True(m.TryLocate(m.Point(0,end*35),radius,out var from));
                Assert.True(m.TryTraverse(from,m.Point(side*77,end*35),radius,out var to),side+"/"+end);
                Assert.AreEqual("upper-flank/"+side,to.SurfaceId);Assert.AreEqual(m.UpperHeight,m.SurfaceHeight(to.Position));
                Assert.True(m.TryTraverse(to,from.Position,radius,out var back));Assert.AreEqual(from.SurfaceId,back.SurfaceId);
                // Ground-to-rear diagonal crosses the explicit three-region junction.
                Assert.True(m.TryLocate(m.Point(side*25,end*41),radius,out var low));
                Assert.True(m.TryTraverse(low,m.Point(side*55,end*47),radius,out _),"junction "+side+"/"+end);
            }
            Assert.True(m.TryLocate(m.Point(77,85),radius,out var flank));Assert.False(m.TryTraverse(flank,m.Point(77,125),radius,out _));
        }
        [TestCase(.7)][TestCase(1)][TestCase(1.4)] public void ExactMultiRegionVerticesUseOnlyIncidentDeclaredTransitions(double radius)
        {
            var m=new FoundryMap(new FoundryProfileData());
            foreach(int side in new[]{-1,1})foreach(int end in new[]{-1,1}){
                var a=m.Point(side*27,end*43);var b=m.Point(side*29,end*45);
                Assert.True(m.TryLocate(a,radius,out var from));Assert.True(m.TryTraverse(from,b,radius,out var to));Assert.True(m.TryTraverse(to,a,radius,out _));
            }
        }
        [TestCase(.58)][TestCase(.7)][TestCase(1.4)] public void FoundryHomeFrontRampPortalsAllowBothDirectionsWithFullFootprints(double radius)
        {
            var m=new FoundryMap(new FoundryProfileData());
            foreach(int end in new[]{-1,1})foreach(double blockedX in new[]{-20d,20d})
                Assert.False(m.TryLocate(m.Point(blockedX,end*62),radius,out _),"Visible basalt pocket must remain blocked.");
            foreach(int end in new[]{-1,1})foreach(double x in new[]{-16d,0,16d}){
                var a=m.Point(x,end*62);var b=m.Point(x,end*58);Assert.True(m.TryLocate(a,radius,out var rear),"clear rear "+x+"/"+end);Assert.AreEqual("allied-rear/"+end,rear.SurfaceId);
                Assert.True(m.TryTraverse(rear,b,radius,out var front));Assert.AreEqual("front-ramp/"+end,front.SurfaceId);Assert.True(m.TryTraverse(front,a,radius,out var back));Assert.AreEqual(rear,back);
            }
        }
        [Test] public void WrongRegionAndHiddenBackgroundCannotBeSilentlyRelocated()
        {
            var m=ThreeCrossingsMap.Default;var deck=new NavPoint(-m.BaseCoordinate,-m.BaseCoordinate);
            Assert.True(m.TryLocate(deck,0,out var original));Assert.True(original.SurfaceId.StartsWith("deck/"));
            var wrong=new NavLocation(deck,"bank/west");Assert.False(m.IsValidLocation(wrong,0));Assert.False(m.TryTraverse(wrong,deck,0,out _));
            Assert.AreNotEqual(original,wrong);Assert.AreEqual(deck,wrong.Position);
        }
    }
}
