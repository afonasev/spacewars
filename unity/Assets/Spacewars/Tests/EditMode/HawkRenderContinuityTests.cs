using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Tests.EditMode
{
    public sealed class HawkRenderContinuityTests
    {
        static PlayableEntitySnapshot Hawk(double x,double altitude)=>new PlayableEntitySnapshot(3,PlayableOwner.Player,PlayableEntityKind.Hawk,new NavPoint(x,0),60,false,0,0,0,held:true,altitude:altitude);
        static PlayableSnapshot View(long tick,PlayableEntitySnapshot e)=>new PlayableSnapshot("motion",1,1,7,tick+1,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,new NavGeometry(1024,Array.Empty<NavObstacle>(),1),new[]{e},Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null);
        static OfflinePresentationFrame Frame(PlayableSnapshot v,OfflinePresentationFrame prior)=> (OfflinePresentationFrame)Activator.CreateInstance(typeof(OfflinePresentationFrame),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{new Dictionary<string,PlayableSnapshot>{{v.OwnerId,v}},Array.Empty<OfflineReceipt>(),0L,prior},null);
        [Test] public void OwnerFrameRetainsOnlyPreviousTickAndHeldHawkInterpolatesAfterSkippedRender()
        {
            var a=View(0,Hawk(0,3));var b=View(1,Hawk(1,4));var c=View(2,Hawk(2,5));
            var f=Frame(c,Frame(b,Frame(a,null)));Assert.AreSame(b,f.PreviousView(c.OwnerId));
            var same=Frame(c,f);Assert.AreSame(b,same.PreviousView(c.OwnerId));Assert.IsNull(f.PreviousView("foreign-owner"));Assert.GreaterOrEqual(f.AgeSeconds,0);
            var motion=new PlayableUnitMotion();motion.BeginFrame(a);motion.Sample(a.Entities[0],0);
            motion.BeginFrame(c,previous:f.PreviousView(c.OwnerId),snapshotAge:1d/60);
            var pose=motion.Sample(c.Entities[0],1);Assert.AreEqual(1.5,pose.Position.X,1e-10);Assert.AreEqual(4.5,pose.Altitude,1e-10);
            Assert.IsNull(Frame(View(5,Hawk(5,5)),f).PreviousView(c.OwnerId));
        }
    }
}
