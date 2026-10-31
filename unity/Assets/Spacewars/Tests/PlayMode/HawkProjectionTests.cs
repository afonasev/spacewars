using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
public sealed class HawkProjectionTests
{
    [Test] public void AircraftCircleOnlyAppearsWhileSelectedAndKeepsCollisionRadiusSeparate()
    {
        var data=PlayableProfile.Default.CopyData();double physicalRadius=data.hawkRadius;
        var p=PlayableProfile.Create(data,new ProvingGroundMap());var host=new GameObject("Air selection lifecycle");var world=new PlayableWorld(host.transform,p,staged:true);
        try
        {
            var actor=world.Tank(99,true,PlayableEntityKind.Hawk);
            var e=new PlayableEntitySnapshot(99,PlayableOwner.Player,PlayableEntityKind.Hawk,new NavPoint(-5,-6),60,false,0,0,0,altitude:3);
            var snapshot=new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,new[]{e},Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null);
            Assert.False(actor.Selection.activeSelf,"New aircraft must not flash a circle before selection");
            world.UpdateAircraft(actor,e,snapshot);
            Assert.False(actor.Selection.activeSelf);Assert.IsNull(actor.AirProjection,"Unselected aircraft does not allocate a terrain projection");
            world.UpdateAircraft(actor,e,snapshot,true);
            Assert.True(actor.Selection.activeSelf);
            Assert.AreEqual(p.HawkSelectionRadius+p.SelectionRingPadding+p.SelectionRingWidth,actor.Selection.transform.localScale.x,1e-6);
            Assert.Less(p.HawkSelectionRadius,p.HawkRadius);Assert.AreEqual(physicalRadius,p.HawkRadius,"Visual radius must not change physics");
            var mesh=actor.AirProjection;
            world.UpdateAircraft(actor,e,snapshot,false);Assert.False(actor.Selection.activeSelf);
            world.UpdateAircraft(actor,e,snapshot,true);Assert.True(actor.Selection.activeSelf);Assert.AreSame(mesh,actor.AirProjection);
            var blocked=new PlayableEntitySnapshot(100,PlayableOwner.Player,PlayableEntityKind.Tank,e.Position,60,false,0,0,0);
            var obstructed=new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,new[]{e,blocked},Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null);
            world.UpdateAircraft(actor,e,obstructed,true);Assert.False(actor.Selection.activeSelf,"Selection must not paint a circle over another vehicle");
        }
        finally{world.Dispose();UnityEngine.Object.DestroyImmediate(host);}
    }
    [Test] public void AircraftCircleConformsToRampAndStaysUnderDisplayedBody()
    {
        var map=new FoundryMap(new FoundryProfileData());var ramp=map.Supports.First(s=>s.Gradient.X!=0||s.Gradient.Z!=0);var point=new NavPoint((ramp.Bounds.MinX+ramp.Bounds.MaxX)/2,(ramp.Bounds.MinZ+ramp.Bounds.MaxZ)/2);
        var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);var host=new GameObject("Air projection ramp");var world=new PlayableWorld(host.transform,p,staged:true);
        try
        {
            var actor=world.Tank(99,true,PlayableEntityKind.Hawk);var e=new PlayableEntitySnapshot(99,PlayableOwner.Player,PlayableEntityKind.Hawk,point,60,false,0,0,0,altitude:12);
            var snapshot=new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,new[]{e},Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null);
            world.UpdateAircraft(actor,e,snapshot,true);Assert.True(actor.Selection.activeSelf);Assert.AreEqual(point.X,actor.Selection.transform.position.x,1e-5);Assert.AreEqual(point.Z,actor.Selection.transform.position.z,1e-5);
            foreach(var vertex in actor.AirProjection.vertices){var actual=actor.Selection.transform.TransformPoint(vertex);var location=new NavPoint(actual.x,actual.z);Assert.AreEqual(world.AirGroundPoint(location).y+p.SelectionRingLift,actual.y,1e-5);}
            Assert.AreEqual(12,actor.Root.position.y);Assert.Greater(actor.AirProjection.vertexCount,4);Assert.Zero(actor.Root.GetComponentsInChildren<Collider>().Length);
        }
        finally{world.Dispose();UnityEngine.Object.DestroyImmediate(host);}
    }
}
