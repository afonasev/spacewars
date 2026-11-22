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

        [Test]
        public void SkatModelKeepsThreeDistinctTiersAndFitsTheHangarShaft()
        {
            var root = new GameObject("Skat visual contract");
            var profile = PlayableProfile.Default;
            try
            {
                using var world = new PlayableWorld(root.transform, profile);
                var actor = world.Tank(101, true, PlayableEntityKind.Skat);
                var lod = actor.DetailLod;
                Assert.NotNull(lod);
                var renderer = lod.Renderers.Single();
                var materials = renderer.sharedMaterials;
                var paint = Resources.Load<Material>("StyleA/hawk-team-primary");
                Assert.NotNull(paint);
                int paintSlot = Array.IndexOf(materials, paint);
                Assert.GreaterOrEqual(paintSlot, 0);
                Assert.NotNull(paint.GetTexture("_BaseMap"));
                Assert.NotNull(paint.GetTexture("_BumpMap"));
                var owner = new Color(.65f,.21f,.19f);
                PlayableWorld.PaintOwner(actor,owner);
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties,paintSlot);
                Color applied = properties.GetColor("_BaseColor");
                Assert.Greater(applied.r,applied.g);
                Assert.Greater(applied.g,applied.b);
                long previous = long.MaxValue;
                for (int tier = 0; tier < 3; tier++)
                {
                    lod.SetDetail(tier);
                    long triangles = lod.Renderers.Sum(renderer =>
                        (long)renderer.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3);
                    Assert.Less(triangles, previous);
                    var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    Assert.AreEqual(mesh.vertexCount,mesh.uv.Length);
                    Assert.AreEqual(materials.Length,mesh.subMeshCount);
                    CollectionAssert.AreEqual(materials,renderer.sharedMaterials);
                    previous = triangles;
                }

                var bounds = lod.ReferenceBounds;
                double radius = Math.Sqrt(Math.Pow(Math.Max(Math.Abs(bounds.min.x),Math.Abs(bounds.max.x)),2) +
                    Math.Pow(Math.Max(Math.Abs(bounds.min.z),Math.Abs(bounds.max.z)),2))*PlayableUnitRules.SkatModelScale;
                double shaft = .94 * HawkHangarGeometry.ModelHeight / HawkHangarGeometry.AuthorHeight;
                Assert.Less(radius, shaft);
                Assert.NotNull(Resources.Load<Texture2D>("DockPortraits/skat"));
                var drone = new PlayableEntitySnapshot(101,PlayableOwner.Player,PlayableEntityKind.Skat,
                    default,60,false,102,0,0,altitude:2);
                var patient = new PlayableEntitySnapshot(102,PlayableOwner.Player,PlayableEntityKind.Tank,
                    new NavPoint(2,0),50,false,0,0,0);
                var view = new PlayableSnapshot(profile.ProfileId,profile.Revision,1,7,1,30,
                    RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,new[]{drone,patient},
                    Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),null,null,activeProfile:profile);
                world.UpdateAircraft(actor,drone,view);
                var beam = actor.Hull.Find("Skat beam").GetComponent<LineRenderer>();
                Assert.True(beam.enabled);
                Assert.AreEqual(actor.RepairEmitter.position,beam.GetPosition(0));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void SkatFacesTheLaunchHeadingOnTheHangarPlatform()
        {
            var root=new GameObject("Skat launch heading");
            try
            {
                using var world=new PlayableWorld(root.transform,PlayableProfile.Default,staged:true);
                var actor=world.Building(101,"Hangar",true);
                const double heading=.8;
                var snapshot=new PlayableBuildingSnapshot(101,PlayableOwner.Player,PlayableBuildingKind.Hangar,
                    default,100,1,0,0,default,heading:heading,launchPhase:3,launchKind:PlayableEntityKind.Skat);
                world.UpdateHangar(actor,snapshot,1);
                var parked=actor.Hull.Find("stagedSkat");
                Assert.NotNull(parked);
                var emitter=parked.GetComponentsInChildren<Transform>().Single(t=>t.name=="repairEmitter");
                var nose=Vector3.ProjectOnPlane(emitter.position-parked.position,Vector3.up).normalized;
                var exit=new Vector3((float)Math.Cos(heading),0,(float)Math.Sin(heading));
                Assert.Greater(Vector3.Dot(nose,exit),.99f);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void BeamFollowsDisplayedAircraftAtWideZoomInTheSeatLayer()
        {
            var root = new GameObject("Repair tower presentation regression");
            var targetTexture = new RenderTexture(640, 360, 24);
            var profile = PlayableProfile.Default;
            try
            {
                using var world = new PlayableWorld(root.transform, profile);
                const int seatLayer = 8;
                world.SetPresentationLayer(seatLayer);
                var tower = world.Building(10, "RepairTower", true);
                var aircraft = world.Tank(20, true, PlayableEntityKind.Hawk);
                aircraft.Root.position = new Vector3(3, 6, 4);

                var camera = new GameObject("Seat camera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform);
                camera.enabled = false;
                camera.targetTexture = targetTexture;
                camera.cullingMask = 1 << seatLayer;
                camera.orthographic = true;
                camera.orthographicSize = (float)profile.CameraMaxZoom;

                var building = new PlayableBuildingSnapshot(10, PlayableOwner.Player, PlayableBuildingKind.RepairTower,
                    default, 100, 1, 0, 0, default, phase: ConstructionPhase.Ready,
                    healing: new PlayableRepairTowerSnapshot(20, 0, 0));
                PlayableSnapshot View(params PlayableEntitySnapshot[] entities) =>
                    new PlayableSnapshot(profile.ProfileId, profile.Revision, 1, 7, 1, 100, RuntimeStatus.Running,
                        false, PlayableMatchOutcome.Playing, 1000, null, entities, new[] { building },
                        Array.Empty<PlayableProjectileSnapshot>(), null, null, activeProfile: profile);

                var hawk = new PlayableEntitySnapshot(20, PlayableOwner.Player, PlayableEntityKind.Hawk,
                    new NavPoint(4, 5), 50, true, 0, 0, 0, altitude: 7);
                tower.Healing.Drive(building, View(hawk), profile, camera, world.Actors);

                var beam = tower.Healing.Beam;
                Assert.True(beam.enabled);
                Assert.AreEqual(seatLayer, beam.gameObject.layer);
                var emitter = tower.Hull.GetComponentsInChildren<Transform>(true).Single(t => t.name == "repairEmitter");
                Assert.AreEqual(emitter.position, beam.GetPosition(0));
                Assert.AreEqual(aircraft.Root.position + Vector3.up * (float)(PlayableUnitRules.HawkBodyHeight(profile) / 2),
                    beam.GetPosition(1));
                Assert.GreaterOrEqual(beam.startWidth * camera.scaledPixelHeight / (2 * camera.orthographicSize), 1.99f);

                tower.Healing.Drive(building, View(), profile, camera, world.Actors);
                Assert.False(beam.enabled, "The rendered actor alone must not reveal a target absent from the perspective.");

                var tank = new PlayableEntitySnapshot(20, PlayableOwner.Player, PlayableEntityKind.Tank,
                    new NavPoint(4, 5), 50, false, 0, 0, 0);
                tower.Healing.Drive(building, View(tank), profile, camera, world.Actors);
                Assert.True(beam.enabled);
                Assert.AreEqual((float)profile.TurretLaserWidth, beam.startWidth);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(targetTexture);
            }
        }
    }
}
