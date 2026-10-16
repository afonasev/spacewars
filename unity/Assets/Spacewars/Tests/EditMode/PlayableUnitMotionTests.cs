using System;
using System.Collections.Generic;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableUnitMotionTests
    {
        private const double Step=1d/30d;
        private static PlayableEntitySnapshot Entity(double x,double z=0,double hull=0,double turret=0,bool held=false,string surface="ground")
            =>new PlayableEntitySnapshot(3,PlayableOwner.Player,PlayableEntityKind.Explorer,new NavPoint(x,z),100,true,0,hull,turret,held:held,location:new NavLocation(new NavPoint(x,z),surface));
        private static PlayableSnapshot Frame(PlayableEntitySnapshot e,long tick,long generation=1,bool paused=false,PlayableOwner owner=PlayableOwner.Player,int geometry=1)
            =>new PlayableSnapshot("motion",1,generation,7,tick+1,tick,RuntimeStatus.Running,paused,PlayableMatchOutcome.Playing,0,
                new NavGeometry(1024,Array.Empty<NavObstacle>(),geometry),new[]{e},Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,owner:owner);
        [Test] public void EveryHighRefreshSubframeStaysBetweenReceivedEndpointsAndNeverExtrapolates()
        {
            var motion=new PlayableUnitMotion();var first=Entity(0);motion.BeginFrame(Frame(first,0));motion.Sample(first,0);
            for(int tick=1;tick<=60;tick++){
                var entity=Entity(tick);var frame=Frame(entity,tick);double start=tick*Step;
                for(int sub=0;sub<8;sub++){
                    motion.BeginFrame(frame);var pose=motion.Sample(entity,start+sub*Step/8);
                    Assert.AreEqual(tick-1+sub/8d,pose.Position.X,1e-10);
                    Assert.AreEqual(0,pose.Position.Z);Assert.AreEqual(tick,entity.Position.X);
                }
            }
            var last=Entity(60);motion.BeginFrame(Frame(last,60));Assert.AreEqual(60,motion.Sample(last,100).Position.X);
        }
        [TestCase(.5)][TestCase(2)][TestCase(4)] public void PlaybackRateScalesOnlyInterpolationTime(double speed)
        {
            var m=new PlayableUnitMotion();var a=Entity(0);m.BeginFrame(Frame(a,0),speed:speed);m.Sample(a,0);
            var b=Entity(1);m.BeginFrame(Frame(b,1),speed:speed);m.Sample(b,1);
            Assert.AreEqual(.5,m.Sample(b,1+Step/speed*.5).Position.X,1e-10);
            Assert.AreEqual(1,m.Sample(b,2).Position.X);Assert.AreEqual(1,b.Position.X);
        }
        [Test] public void AcceleratedSkippedRenderUsesLastConsecutiveAuthoritySegmentWithoutCuttingCorner()
        {
            var m=new PlayableUnitMotion();var a=Entity(0);m.BeginFrame(Frame(a,0),speed:4);m.Sample(a,0);
            var corner=Entity(1);var end=Entity(1,1);
            m.BeginFrame(Frame(end,2),speed:4,previous:Frame(corner,1));
            Assert.AreEqual(new NavPoint(1,0),m.Sample(end,0).Position);
            Assert.AreEqual(new NavPoint(1,.5),m.Sample(end,Step/8).Position);
            var geometry=new NavGeometry(3,new[]{new NavObstacle(-.5,.2,.8,2)},1);
            Assert.True(geometry.SegmentFree(new NavPoint(1,0),m.Sample(end,Step/8).Position,.1));
            // No old fog-visible entity / surface or context mismatch: snap, no ghost.
            var next=Entity(10);m.BeginFrame(Frame(next,4),speed:4,previous:Frame(Entity(3),2));Assert.AreEqual(10,m.Sample(next,2).Position.X);
            var upper=Entity(20,surface:"upper");m.BeginFrame(Frame(upper,6),speed:4,previous:Frame(Entity(19),5));Assert.AreEqual(20,m.Sample(upper,3).Position.X);
            m.BeginFrame(Frame(Entity(30,surface:"upper"),8,paused:true),speed:4,previous:Frame(upper,7));Assert.AreEqual(30,m.Sample(Entity(30,surface:"upper"),4).Position.X);
        }
        [Test] public void ReceivedSnapshotAgeDoesNotRestartInterpolationAtRenderArrival()
        {
            var m=new PlayableUnitMotion();var a=Entity(0);m.BeginFrame(Frame(a,0),speed:4);m.Sample(a,0);
            var b=Entity(1);m.BeginFrame(Frame(b,1),speed:4,snapshotAge:Step/8);
            Assert.AreEqual(.5,m.Sample(b,0).Position.X,1e-10);
            m.BeginFrame(Frame(b,1),speed:4,snapshotAge:Step/4);
            Assert.AreEqual(1,m.Sample(b,Step/8).Position.X,1e-10);
        }
        [Test] public void HullAndTurretTakeShortestTurnAcrossAngleWrap()
        {
            var motion=new PlayableUnitMotion();var a=Entity(0,hull:Math.PI-.1,turret:-Math.PI+.1);
            motion.BeginFrame(Frame(a,0));motion.Sample(a,0);
            var b=Entity(0,hull:-Math.PI+.1,turret:Math.PI-.1);motion.BeginFrame(Frame(b,1));motion.Sample(b,Step);
            var middle=motion.Sample(b,Step*1.5);Assert.AreEqual(Math.PI,Math.Abs(middle.Hull),1e-10);Assert.AreEqual(Math.PI,Math.Abs(middle.Turret),1e-10);
            var end=motion.Sample(b,Step*2);Assert.AreEqual(b.HullHeading,end.Hull);Assert.AreEqual(b.TurretHeading,end.Turret);
        }
        [Test] public void PauseVisibilityGenerationPerspectiveGeometryAndSurfaceChangesSnapWithoutGhostMotion()
        {
            var m=new PlayableUnitMotion();var a=Entity(0);m.BeginFrame(Frame(a,0));m.Sample(a,0);
            var b=Entity(8);m.BeginFrame(Frame(b,1));m.Sample(b,Step);Assert.AreEqual(4,m.Sample(b,Step*1.5).Position.X,1e-10);
            m.BeginFrame(Frame(b,1,paused:true));Assert.AreEqual(8,m.Sample(b,Step*1.5).Position.X);Assert.AreEqual(8,m.Sample(b,1).Position.X);
            m.KeepVisible(new HashSet<int>());var appeared=Entity(20);m.BeginFrame(Frame(appeared,2));Assert.AreEqual(20,m.Sample(appeared,2).Position.X);
            var restarted=Entity(50);m.BeginFrame(Frame(restarted,0,generation:2));Assert.AreEqual(50,m.Sample(restarted,3).Position.X);
            var perspective=Entity(-20);m.BeginFrame(Frame(perspective,1,generation:2,owner:PlayableOwner.Enemy));Assert.AreEqual(-20,m.Sample(perspective,4).Position.X);
            var changed=Entity(60);m.BeginFrame(Frame(changed,2,generation:2,owner:PlayableOwner.Enemy,geometry:2));Assert.AreEqual(60,m.Sample(changed,5).Position.X);
            var upper=Entity(70,surface:"upper");m.BeginFrame(Frame(upper,3,generation:2,owner:PlayableOwner.Enemy,geometry:2));Assert.AreEqual(70,m.Sample(upper,6).Position.X);
            var held=Entity(75,held:true,surface:"upper");m.BeginFrame(Frame(held,4,generation:2,owner:PlayableOwner.Enemy,geometry:2));Assert.AreEqual(75,m.Sample(held,7).Position.X);
            var skipped=Entity(80,surface:"upper");m.BeginFrame(Frame(skipped,8,generation:2,owner:PlayableOwner.Enemy,geometry:2));Assert.AreEqual(80,m.Sample(skipped,8).Position.X);
        }
        [Test] public void EarlyNextTickKeepsTheAuthoritativeCornerInsteadOfCuttingAcrossSolidTerrain()
        {
            var m=new PlayableUnitMotion();var a=Entity(0);m.BeginFrame(Frame(a,0));m.Sample(a,-Step);
            var b=Entity(1);m.BeginFrame(Frame(b,1));m.Sample(b,0);Assert.AreEqual(.5,m.Sample(b,Step*.5).Position.X,1e-10);
            var c=Entity(1,1);m.BeginFrame(Frame(c,2));var corner=m.Sample(c,Step*.5);Assert.AreEqual(new NavPoint(1,0),corner.Position);
            var pose=m.Sample(c,Step);Assert.AreEqual(new NavPoint(1,.5),pose.Position);
            var geometry=new NavGeometry(3,new[]{new NavObstacle(-.5,.2,.8,2)},1);
            Assert.True(geometry.IsFree(pose.Position,.1));Assert.True(geometry.SegmentFree(corner.Position,pose.Position,.1));
            Assert.AreEqual(new NavPoint(1,1),c.Position);
        }
    }
}
