using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Tests.PlayMode
{
    public sealed class GaussTowerModelTests
    {
        [Test] public void ActiveModelHasThreeTiersCorrectMuzzleAndAuthoritativeBeam()
        {
            var root=new GameObject("Gauss model review");var p=PlayableProfile.Default;RenderTexture texture=null;Texture2D pixels=null;
            try
            {
                using var world=new PlayableWorld(root.transform,p);world.Fog.ShowPublic();
                var actor=world.Building(900,"GaussTower",true);world.FaceBuilding(actor);
                var rig=actor.Hull.GetComponent<SentryTurretRig>();Assert.NotNull(rig);Assert.NotNull(actor.Gauss);Assert.NotNull(actor.DetailLod);
                var materials=actor.Hull.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r=>r.sharedMaterials).ToArray();long previous=long.MaxValue;
                var sun=new GameObject("Review light").AddComponent<Light>();sun.transform.SetParent(root.transform);IndustrialLightingProfile.Load("three-crossings").ApplyScene(sun);
                var cam=new GameObject("Review camera").AddComponent<Camera>();cam.transform.SetParent(root.transform);cam.orthographic=true;cam.orthographicSize=2.1f;cam.nearClipPlane=.05f;cam.farClipPlane=100;cam.transform.position=new Vector3(4,3.5f,-5);cam.transform.LookAt(Vector3.up*1.15f);cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.1f,.12f,.14f);
                texture=new RenderTexture(1200,900,24);texture.Create();cam.targetTexture=texture;pixels=new Texture2D(1200,900,TextureFormat.RGB24,false);
                string output=Path.GetFullPath("../.local/gauss-visual");Directory.CreateDirectory(output);
                void Capture(string name){cam.Render();var old=RenderTexture.active;RenderTexture.active=texture;pixels.ReadPixels(new Rect(0,0,1200,900),0,0);pixels.Apply();RenderTexture.active=old;File.WriteAllBytes(Path.Combine(output,name),pixels.EncodeToPNG());}
                rig.SetWorldAngles(Math.PI/2,0);
                var pose=BuildingCombatGeometry.Pose(p,PlayableBuildingKind.GaussTower,false,default,0);
                foreach(var filter in actor.Hull.GetComponentsInChildren<MeshFilter>(true))
                {
                    var vertices=filter.sharedMesh.vertices;
                    for(int i=0;i<vertices.Length;i+=Math.Max(1,vertices.Length/13))
                    {
                        var point=filter.transform.TransformPoint(vertices[i]);var hit=pose.Closest(new BallisticPoint(point.x,point.y,point.z));
                        Assert.Less(Vector3.Distance(point,new Vector3((float)hit.X,(float)hit.Y,(float)hit.Z)),.001f,"Active close model and authority must coincide: "+filter.name);
                    }
                }
                for(int tier=0;tier<3;tier++)
                {
                    actor.DetailLod.SetDetail(tier);long triangles=actor.Hull.GetComponentsInChildren<MeshFilter>(true).Sum(f=>(long)f.sharedMesh.triangles.Length/3);Assert.Less(triangles,previous);previous=triangles;
                    CollectionAssert.AreEqual(materials,actor.Hull.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r=>r.sharedMaterials).ToArray());
                    foreach(double yaw in new[]{0d,Math.PI/2,Math.PI})foreach(double pitch in new[]{0d,-.2})
                    {
                        var start=GaussTowerGeometry.Muzzle(default,BuildingCombatGeometry.ApronLift,p.TurretModelScale,yaw,pitch);var end=new BallisticPoint(start.X+4*Math.Cos(yaw),start.Y,start.Z+4*Math.Sin(yaw));
                        var b=new PlayableBuildingSnapshot(900,PlayableOwner.Player,PlayableBuildingKind.GaussTower,default,450,1,0,0,default,turret:new PlayableTurretSnapshot(10,yaw,pitch,false,1,0,start,end,.1));
                        actor.Gauss.Drive(b,p);Assert.True(actor.Gauss.Beam.enabled);
                        var muzzle=(rig.Left.position+rig.Right.position)*.5f;Assert.Less(Vector3.Distance(muzzle,new Vector3((float)start.X,(float)start.Y,(float)start.Z)),.001f);
                        Assert.AreEqual((float)end.X,actor.Gauss.Beam.GetPosition(1).x,1e-5);
                    }
                    actor.Gauss.Drive(new PlayableBuildingSnapshot(900,PlayableOwner.Player,PlayableBuildingKind.GaussTower,default,450,1,0,0,default,turret:new PlayableTurretSnapshot(0,-Math.PI/2,0,false,1,0)),p);Assert.False(actor.Gauss.Beam.enabled);
                    Capture("panцir-lod-"+tier+".png");
                }
                actor.DetailLod.SetDetail(0);
                // Recoil shifts the real model and authority mesh by exactly the same authored distance.
                double remaining=p.GaussBeamSeconds-p.GaussRecoilSeconds*p.GaussRecoilPeakFraction;
                double recoil=GaussTowerGeometry.Recoil(p,remaining);Assert.AreEqual(p.GaussRecoilMeters,recoil,1e-9);
                var recoilStart=GaussTowerGeometry.Muzzle(default,BuildingCombatGeometry.ApronLift,p.TurretModelScale,Math.PI/2,0);
                actor.Gauss.Drive(new PlayableBuildingSnapshot(900,PlayableOwner.Player,PlayableBuildingKind.GaussTower,default,450,1,0,0,default,turret:new PlayableTurretSnapshot(10,Math.PI/2,0,false,1,0,recoilStart,new BallisticPoint(0,recoilStart.Y,8),remaining,recoil:recoil)),p);
                var poseType=typeof(BuildingCombatGeometry).Assembly.GetType("Spacewars.Simulation.SentryCombatPose");
                var poseValue=System.Activator.CreateInstance(poseType,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new object[]{default(NavPoint),BuildingCombatGeometry.ApronLift,p.TurretModelScale,Math.PI/2,0d,1d,false,true,recoil},null);
                var recoilPose=(BuildingCombatPose)System.Activator.CreateInstance(typeof(BuildingCombatPose),System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new[]{poseValue},null);
                foreach(var filter in actor.Hull.GetComponentsInChildren<MeshFilter>(true))
                {
                    var vertices=filter.sharedMesh.vertices;
                    for(int i=0;i<vertices.Length;i+=Math.Max(1,vertices.Length/13))
                    {
                        var point=filter.transform.TransformPoint(vertices[i]);var hit=recoilPose.Closest(new BallisticPoint(point.x,point.y,point.z));
                        Assert.Less(Vector3.Distance(point,new Vector3((float)hit.X,(float)hit.Y,(float)hit.Z)),.001f,"Recoil model/collision alignment");
                    }
                }
                float early=actor.Gauss.Envelope;Assert.Greater(early,0);
                actor.Gauss.Drive(new PlayableBuildingSnapshot(900,PlayableOwner.Player,PlayableBuildingKind.GaussTower,default,450,1,0,0,default,turret:new PlayableTurretSnapshot(10,Math.PI/2,0,false,1,0,recoilStart,new BallisticPoint(0,recoilStart.Y,8),.05)),p);Assert.Less(actor.Gauss.Envelope,early);
                actor.Gauss.Drive(new PlayableBuildingSnapshot(900,PlayableOwner.Player,PlayableBuildingKind.GaussTower,default,450,1,0,0,default,turret:new PlayableTurretSnapshot(10,Math.PI/2,0,false,1,0,charge:.8)),p);Assert.AreEqual(.8f,actor.Gauss.ChargeLevel);Assert.False(actor.Gauss.Beam.enabled);
                actor.Gauss.Drive(new PlayableBuildingSnapshot(900,PlayableOwner.Player,PlayableBuildingKind.GaussTower,default,450,1,0,0,default,turret:new PlayableTurretSnapshot(0,-Math.PI/2,0,false,1,0)),p);Assert.Zero(actor.Gauss.ChargeLevel);Assert.Zero(actor.Gauss.Envelope);
                var sentry=world.Building(901,"SentryTurret",true);sentry.Root.position=new Vector3(2.2f,0,0);world.FaceBuilding(sentry);cam.orthographicSize=3;cam.transform.position=new Vector3(5.1f,4.2f,-7);cam.transform.LookAt(new Vector3(1.1f,1.1f,0));Capture("armor-comparison.png");
                var beamStart=GaussTowerGeometry.Muzzle(default,BuildingCombatGeometry.ApronLift,p.TurretModelScale,-Math.PI/2,0);
                actor.Gauss.Drive(new PlayableBuildingSnapshot(900,PlayableOwner.Player,PlayableBuildingKind.GaussTower,default,450,1,0,0,default,turret:new PlayableTurretSnapshot(10,-Math.PI/2,0,false,1,0,beamStart,new BallisticPoint(0,beamStart.Y,-4),.1)),p);Capture("white-impulse.png");
            }
            finally{foreach(var camera in root.GetComponentsInChildren<Camera>())camera.targetTexture=null;if(texture){texture.Release();UnityEngine.Object.DestroyImmediate(texture);}if(pixels)UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
