using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Spacewars.Tests.EditMode
{
    public sealed class TankDistantLodTests
    {
        static Mesh MeshOf(Renderer r)=>r is SkinnedMeshRenderer s?s.sharedMesh:r.GetComponent<MeshFilter>().sharedMesh;
        static PlayableSnapshot Frame(long tick,bool shot=false)=>new PlayableSnapshot("tank-test",1,1,7,tick+1,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,new NavGeometry(20,Array.Empty<NavObstacle>(),1),Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sounds:shot?new[]{new PlayableSoundEvent(1,tick,PlayableSoundKind.Shot,new NavPoint(0,0),PlayableEntityKind.Tank,unitId:3)}:Array.Empty<PlayableSoundEvent>());
        [Test] public void NearPreservesExactMeshesMaterialsRigAndDistantSharesAnimatedBones()
        {
            var original=Object.Instantiate(Resources.Load<GameObject>("StyleA/tankHybrid"));
            var candidate=Object.Instantiate(Resources.Load<GameObject>("StyleA/tankHybridLod"));
            try{
                var lod=candidate.GetComponent<TankDistantLod>();Assert.NotNull(lod);
                var full=original.GetComponentsInChildren<Renderer>(true).OrderBy(r=>r.name).ToArray();
                var near=lod.Renderers.OrderBy(r=>r.name).ToArray();Assert.AreEqual(12,near.Length);Assert.AreEqual(12,candidate.GetComponentsInChildren<Renderer>(true).Length);
                Assert.AreEqual(51612,lod.FullMeshes.Sum(m=>m.triangles.Length/3));Assert.AreEqual(25802,lod.DistantMeshes.Sum(m=>m.triangles.Length/3));
                TestContext.WriteLine("TANK_LOD_COST nearVertices="+lod.FullMeshes.Sum(m=>m.vertexCount)+" distantVertices="+lod.DistantMeshes.Sum(m=>m.vertexCount)+" nearSubmeshes="+lod.FullMeshes.Sum(m=>m.subMeshCount)+" distantSubmeshes="+lod.DistantMeshes.Sum(m=>m.subMeshCount)+" renderers="+near.Length+" materials="+near.Sum(r=>r.sharedMaterials.Length)+" nearMeshBytes="+lod.FullMeshes.Distinct().Sum(m=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m))+" addedDistantMeshBytes="+lod.DistantMeshes.Distinct().Sum(m=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m)));
                for(int i=0;i<full.Length;i++){
                    Assert.AreEqual(full[i].name,near[i].name);Assert.AreSame(MeshOf(full[i]),MeshOf(near[i]));CollectionAssert.AreEqual(full[i].sharedMaterials,near[i].sharedMaterials);
                    Assert.AreEqual(full[i].shadowCastingMode,near[i].shadowCastingMode);Assert.AreEqual(full[i].receiveShadows,near[i].receiveShadows);
                    Assert.True(near[i].enabled);int index=Array.IndexOf(lod.Renderers,near[i]);var far=lod.DistantMeshes[index];
                    Assert.AreEqual(MeshOf(near[i]).bounds,far.bounds);
                    Assert.True(far.triangles.All(v=>v>=0&&v<far.vertexCount));
                    if(near[i] is SkinnedMeshRenderer s){CollectionAssert.AreEqual(s.sharedMesh.bindposes,far.bindposes);Assert.AreEqual(far.vertexCount,far.boneWeights.Length);Assert.True(far.boneWeights.All(w=>w.weight0==1&&w.boneIndex0<s.bones.Length));}
                }
                var a=original.AddComponent<TankMechanismPresentation>();var b=candidate.AddComponent<TankMechanismPresentation>();a.Initialize();b.Initialize();
                var expected=original.GetComponentsInChildren<Transform>(true).Where(t=>t!=original.transform).ToDictionary(t=>t.name);
                var actual=candidate.GetComponentsInChildren<Transform>(true).Where(t=>t!=candidate.transform).ToDictionary(t=>t.name);
                CollectionAssert.AreEquivalent(expected.Keys,actual.Keys);
                for(int tick=0;tick<4;tick++){
                    var pose=new PlayableDisplayPose(new NavPoint(tick*.13,tick*.2),tick*.18,tick*.12);
                    a.Drive(pose,Frame(tick,tick==2),3);b.Drive(pose,Frame(tick,tick==2),3);
                    foreach(var row in expected){var t=actual[row.Key];Assert.AreEqual(row.Value.localPosition,t.localPosition,row.Key);Assert.AreEqual(row.Value.localRotation,t.localRotation,row.Key);Assert.AreEqual(row.Value.localScale,t.localScale,row.Key);}
                    lod.SelectScreenRatio(0,TankMechanismProfile.Load());Assert.True(lod.IsDistant);
                    foreach(var r in lod.Renderers.OfType<SkinnedMeshRenderer>())foreach(var bone in r.bones)Assert.True(bone.IsChildOf(candidate.transform));
                }
                lod.SelectScreenRatio(float.PositiveInfinity,TankMechanismProfile.Load());
                for(int i=0;i<lod.Renderers.Length;i++)Assert.AreSame(lod.FullMeshes[i],MeshOf(lod.Renderers[i]));
                Assert.Zero(candidate.GetComponentsInChildren<Collider>(true).Length);
            }finally{Object.DestroyImmediate(original);Object.DestroyImmediate(candidate);}
        }
        [Test] public void CameraProjectionAndHysteresisKeepNearAndPreventBoundaryChatter()
        {
            var model=Object.Instantiate(Resources.Load<GameObject>("StyleA/tankHybridLod"));var host=new GameObject("LOD camera");
            try{
                var lod=model.GetComponent<TankDistantLod>();var p=TankMechanismProfile.Load();p.Validate();
                lod.SelectScreenRatio(p.distantEnterScreenHeight+.001f,p);Assert.False(lod.IsDistant);
                lod.SelectScreenRatio(p.distantEnterScreenHeight,p);Assert.True(lod.IsDistant);
                for(int i=0;i<100;i++){lod.SelectScreenRatio(p.distantEnterScreenHeight+(i%2==0?-.001f:.001f),p);Assert.True(lod.IsDistant);}
                lod.SelectScreenRatio(p.distantExitScreenHeight,p);Assert.False(lod.IsDistant);
                foreach(float v in new[]{float.NaN,float.PositiveInfinity,-1}){lod.SelectScreenRatio(0,p);lod.SelectScreenRatio(v,p);Assert.False(lod.IsDistant);}
                var camera=host.AddComponent<Camera>();camera.transform.position=new Vector3(0,1,-20);camera.transform.LookAt(model.transform);camera.orthographic=true;
                camera.orthographicSize=2.3f;lod.Select(camera);Assert.False(lod.IsDistant);
                camera.orthographicSize=144;lod.Select(camera);Assert.True(lod.IsDistant);
                camera.orthographic=false;camera.transform.position=new Vector3(0,0,-3);camera.transform.rotation=Quaternion.identity;lod.Select(camera);Assert.False(lod.IsDistant);
                camera.transform.position=new Vector3(0,0,-300);lod.Select(camera);Assert.True(lod.IsDistant);
                camera.transform.position=new Vector3(0,0,300);lod.Select(camera);Assert.False(lod.IsDistant);
                var parameters=p.Parameters().Where(x=>x.Path.Contains("distant")).ToArray();Assert.AreEqual(2,parameters.Length);Assert.True(parameters.All(x=>!string.IsNullOrEmpty(x.Description)&&x.Step>0));
                p.distantExitScreenHeight=p.distantEnterScreenHeight;Assert.Throws<ArgumentException>(()=>p.Validate());
            }finally{Object.DestroyImmediate(model);Object.DestroyImmediate(host);}
        }
        [Test] public void ProductionWorldSelectsThreeLevelDetailAndKeepsOriginalUpgradeAssetFull()
        {
            var host=new GameObject("LOD world");var camera=new GameObject("LOD world camera").AddComponent<Camera>();
            PlayableWorld world=null;
            try{
                world=new PlayableWorld(host.transform,PlayableProfile.Default);var tank=world.Tank(3,true);
                Assert.NotNull(tank.DetailLod);camera.orthographic=true;camera.orthographicSize=144;camera.transform.position=new Vector3(0,20,-20);camera.transform.LookAt(tank.Root);
                PlayableWorld.UpdateHealth(tank,camera,1);Assert.AreEqual(2,tank.DetailLod.Level);PlayableWorld.PaintOwner(tank,new Color(.2f,.7f,.4f));
                Assert.AreEqual(0,tank.Hull.GetComponentsInChildren<Collider>(true).Length);
                Assert.IsNull(Resources.Load<GameObject>("StyleA/tankHybridUpgraded").GetComponent<TankDistantLod>(),"Unreviewed upgraded geometry keeps the full current source.");
            }finally{world?.Dispose();Object.DestroyImmediate(host);Object.DestroyImmediate(camera.gameObject);}
        }
    }
}
