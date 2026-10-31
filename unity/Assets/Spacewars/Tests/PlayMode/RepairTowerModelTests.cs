using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Tests.PlayMode
{
    public sealed class RepairTowerModelTests
    {
        [Test]public void DetailTiersKeepRigMaterialsAndBeamFollowsObservedGroundAndAirTargets()
        {
            var root=new GameObject("Repair model regression");var p=PlayableProfile.Default;
            try
            {
                using var world=new PlayableWorld(root.transform,p);var actor=world.Building(10,"RepairTower",true);world.FaceBuilding(actor);
                var lod=actor.DetailLod;Assert.NotNull(lod);var rig=actor.Hull.GetComponent<SentryTurretRig>();Assert.NotNull(rig);
                var materials=actor.Hull.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r=>r.sharedMaterials).ToArray();long previous=long.MaxValue;
                // Compare imported near geometry against the independent authoritative mesh export.
                // This catches a stale export or misplaced elbow after an authored arm revision.
                rig.SetWorldAngles(Math.PI/2,0);
                var pose=BuildingCombatGeometry.Pose(p,PlayableBuildingKind.RepairTower,false,default,0);
                foreach(var filter in actor.Hull.GetComponentsInChildren<MeshFilter>(true))
                {
                    var vertices=filter.sharedMesh.vertices;
                    for(int i=0;i<vertices.Length;i+=Math.Max(1,vertices.Length/11))
                    {
                        var point=filter.transform.TransformPoint(vertices[i]);var hit=pose.Closest(new BallisticPoint(point.x,point.y,point.z));
                        Assert.Less(Vector3.Distance(point,new Vector3((float)hit.X,(float)hit.Y,(float)hit.Z)),.001f,"Authoritative repair surface must coincide with imported close geometry: "+filter.name);
                    }
                }
                for(int tier=0;tier<3;tier++)
                {
                    lod.SetDetail(tier);long triangles=actor.Hull.GetComponentsInChildren<MeshFilter>(true).Sum(f=>(long)f.sharedMesh.triangles.Length/3);Assert.Less(triangles,previous);previous=triangles;
                    CollectionAssert.AreEqual(materials,actor.Hull.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r=>r.sharedMaterials).ToArray());
                    foreach(double yaw in new[]{0d,Math.PI/2,Math.PI})foreach(double pitch in new[]{0d,Math.PI/2})
                    {
                        var b=new PlayableBuildingSnapshot(10,PlayableOwner.Player,PlayableBuildingKind.RepairTower,default,300,1,0,0,default,healing:new PlayableRepairTowerSnapshot(20,yaw,pitch));
                        var unit=new PlayableEntitySnapshot(20,PlayableOwner.Player,pitch==0?PlayableEntityKind.Tank:PlayableEntityKind.Hawk,new NavPoint(4,3),50,false,0,0,0,altitude:7);
                        PlayableSnapshot View(bool visible)=>new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,visible?new[]{unit}:Array.Empty<PlayableEntitySnapshot>(),new[]{b},Array.Empty<PlayableProjectileSnapshot>(),null,null,activeProfile:p);
                        actor.Healing.Drive(b,View(true),p);Assert.True(actor.Healing.Beam.enabled);
                        var expected=new Vector3((float)(Math.Cos(yaw)*Math.Cos(pitch)),(float)Math.Sin(pitch),(float)(Math.Sin(yaw)*Math.Cos(pitch)));Assert.Greater(Vector3.Dot(rig.Forward,expected),.999f);
                        var emitter=actor.Hull.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="repairEmitter");Assert.Less(Vector3.Distance(emitter.position,actor.Healing.Beam.GetPosition(0)),1e-5);
                        Assert.AreEqual(4,actor.Healing.Beam.GetPosition(1).x,1e-5);actor.Healing.Drive(b,View(false),p);Assert.False(actor.Healing.Beam.enabled,"Hidden/missing target must never leak through beam");
                    }
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
