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
    public sealed class ModelDetailLodTests
    {
        static readonly string[] Keys={"tankHybrid","tankHybridUpgraded","explorer","explorerUpgraded","shkval","shkval-guidance-3","hawk","headquarters-a","factory-armored","mine-armored","outpost-b","research-scanner","refinery","refineryUpgraded","sentry-turret","hangar"};
        static Mesh MeshOf(Renderer r)=>r is SkinnedMeshRenderer s?s.sharedMesh:r.GetComponent<MeshFilter>().sharedMesh;
        static string PathOf(Transform t,Transform root)=>t==root?"":PathOf(t.parent,root)+"/"+t.name;
        [TestCaseSource(nameof(Keys))] public void ThreeLevelsKeepExactOriginalRigMaterialsBoundsAndNearMeshReferences(string key)
        {
            var original=Object.Instantiate(Resources.Load<GameObject>("StyleA/"+key));var model=Object.Instantiate(Resources.Load<GameObject>("StyleA/Lod/"+key));
            try{
                var lod=model.GetComponent<ModelDetailLod>();Assert.NotNull(lod);
                var source=original.GetComponentsInChildren<Renderer>(true).ToDictionary(r=>PathOf(r.transform,original.transform));
                Assert.AreEqual(source.Count,lod.Renderers.Length);Assert.AreEqual(source.Count,model.GetComponentsInChildren<Renderer>(true).Length);
                var transforms=model.GetComponentsInChildren<Transform>(true);var rest=transforms.Select(t=>(t.localPosition,t.localRotation,t.localScale,t.gameObject.activeSelf)).ToArray();
                long Tri(Mesh[] meshes)=>meshes.Sum(m=>(long)m.triangles.Length/3);
                Assert.Greater(Tri(lod.OriginalMeshes),Tri(lod.NormalMeshes));Assert.Greater(Tri(lod.NormalMeshes),Tri(lod.FarMeshes));
                for(int n=0;n<lod.Renderers.Length;n++){
                    var r=lod.Renderers[n];var full=source[PathOf(r.transform,model.transform)];Assert.AreSame(MeshOf(full),lod.OriginalMeshes[n]);CollectionAssert.AreEqual(full.sharedMaterials,r.sharedMaterials);Assert.AreEqual(full.shadowCastingMode,r.shadowCastingMode);Assert.AreEqual(full.receiveShadows,r.receiveShadows);Assert.AreEqual(full.enabled,r.enabled);
                    foreach(var mesh in new[]{lod.NormalMeshes[n],lod.FarMeshes[n]}){
                        Assert.AreEqual(MeshOf(full).bounds,mesh.bounds);Assert.AreEqual(MeshOf(full).subMeshCount,mesh.subMeshCount);Assert.True(mesh.triangles.All(v=>v>=0&&v<mesh.vertexCount));
                        if(r is SkinnedMeshRenderer s){CollectionAssert.AreEqual(lod.OriginalMeshes[n].bindposes,mesh.bindposes);Assert.AreEqual(mesh.vertexCount,mesh.boneWeights.Length);Assert.True(mesh.boneWeights.All(w=>w.weight0==1&&w.boneIndex0<s.bones.Length));Assert.True(s.bones.All(b=>b.IsChildOf(model.transform)));}
                    }
                    // The new coarse recipe constrains geometry to the original
                    // envelope; copying a Bounds value must not hide outliers.
                    var envelope=lod.OriginalMeshes[n].bounds;envelope.Expand(.0002f);
                    Assert.True(lod.FarMeshes[n].vertices.All(v=>!float.IsNaN(v.sqrMagnitude)&&!float.IsInfinity(v.sqrMagnitude)&&envelope.Contains(v)),"Far vertex escaped original envelope: "+key+"/"+r.name);
                }
                foreach(int level in new[]{1,2,0,2,1,0}){
                    lod.SetDetail(level);var expected=level==0?lod.OriginalMeshes:level==1?lod.NormalMeshes:lod.FarMeshes;
                    for(int n=0;n<lod.Renderers.Length;n++)Assert.AreSame(expected[n],MeshOf(lod.Renderers[n]));
                    CollectionAssert.AreEqual(rest,transforms.Select(t=>(t.localPosition,t.localRotation,t.localScale,t.gameObject.activeSelf)).ToArray());
                }
                Assert.AreEqual(original.GetComponentsInChildren<Collider>(true).Length,model.GetComponentsInChildren<Collider>(true).Length);
            }finally{Object.DestroyImmediate(original);Object.DestroyImmediate(model);}
        }
        [Test] public void SessionCloneChangesActualCameraSelectionWithoutChangingModelOrDraft()
        {
            var model=Object.Instantiate(Resources.Load<GameObject>("StyleA/Lod/tankHybrid"));var camera=new GameObject("LOD session camera").AddComponent<Camera>();
            try{
                var lod=model.GetComponent<ModelDetailLod>();camera.orthographic=true;camera.transform.position=new Vector3(0,0,-30);camera.transform.LookAt(model.transform);
                var size=lod.ReferenceBounds.size;camera.orthographicSize=Mathf.Max(size.x,size.y,size.z)/(.065f*2);
                ModelDetailProfile.UseSession(null);lod.Select(camera);Assert.AreEqual(1,lod.Level);
                var custom=ModelDetailProfile.Load();custom.farEnter=.07f;custom.farExit=.08f;ModelDetailProfile.UseSession(custom);custom.farEnter=.03f;
                lod.SetDetail(0);lod.Select(camera);Assert.AreEqual(2,lod.Level,"Saved/preview session drives actual selector");Assert.AreEqual(.07f,ModelDetailProfile.Current.farEnter,"Session owns a copy");
                ModelDetailProfile.UseSession(null);lod.SetDetail(0);lod.Select(camera);Assert.AreEqual(1,lod.Level);
            }finally{ModelDetailProfile.UseSession(null);Object.DestroyImmediate(model);Object.DestroyImmediate(camera.gameObject);}
        }
        [TestCase(false)][TestCase(true)] public void ActualGameplayZoomReturnsCloseNormalFarAndBack(bool upgraded)
        {
            var host=new GameObject("Calibrated detail world");var camera=new GameObject("Command camera").AddComponent<Camera>();PlayableWorld world=null;
            try{
                var p=PlayableProfile.Default;world=new PlayableWorld(host.transform,p);var actor=world.Tank(901,true,PlayableEntityKind.Tank);
                if(upgraded){Object.DestroyImmediate(actor.Hull.gameObject);actor.Hull=Object.Instantiate(Resources.Load<GameObject>("StyleA/Lod/tankHybridUpgraded"),actor.Root).transform;actor.Hull.localScale=Vector3.one*(float)(p.TankModelRadius*p.TankModelScale/1.585);actor.DetailLod=actor.Hull.GetComponent<ModelDetailLod>();}
                camera.orthographic=true;camera.transform.position=new Vector3((float)p.CameraOffsetX,(float)p.CameraHeight,(float)p.CameraOffsetZ);camera.transform.LookAt(Vector3.zero);
                var zooms=new[]{p.CameraMinZoom,p.CameraOrthoSize,p.CameraMaxZoom,p.CameraOrthoSize,p.CameraMinZoom};var levels=new[]{0,1,2,1,0};
                for(int i=0;i<zooms.Length;i++){camera.orthographicSize=(float)zooms[i];actor.DetailLod.Select(camera);Assert.AreEqual(levels[i],actor.DetailLod.Level,"Gameplay zoom="+zooms[i]+" ratio="+actor.DetailLod.ProjectedScreenRatio(camera));}
            }finally{world?.Dispose();Object.DestroyImmediate(host);Object.DestroyImmediate(camera.gameObject);}
        }
        [Test] public void FarFactorySlatKeepsFlatPanelNormalsAcrossHardCorners()
        {
            var lod=Resources.Load<GameObject>("StyleA/Lod/factory-armored").GetComponent<ModelDetailLod>();
            int row=Array.FindIndex(lod.Renderers,r=>r.name=="door-slat");Assert.GreaterOrEqual(row,0);
            var mesh=lod.FarMeshes[row];var v=mesh.vertices;var t=mesh.triangles;var normals=mesh.normals;
            int largest=Enumerable.Range(0,t.Length/3).OrderByDescending(i=>Vector3.Cross(v[t[i*3+1]]-v[t[i*3]],v[t[i*3+2]]-v[t[i*3]]).sqrMagnitude).First()*3;
            var face=Vector3.Cross(v[t[largest+1]]-v[t[largest]],v[t[largest+2]]-v[t[largest]]).normalized;
            for(int i=0;i<3;i++)Assert.Greater(Vector3.Dot(normals[t[largest+i]],face),.98f,"Broad panel inherited rounded corner/bevel normal");
        }
        [TestCase("tankHybrid")][TestCase("tankHybridUpgraded")]
        public void ReducedTankKeepsExactChassisTracksWheelsAndSkin(string key)
        {
            var lod=Resources.Load<GameObject>("StyleA/Lod/"+key).GetComponent<ModelDetailLod>();int protectedRows=0;
            for(int i=0;i<lod.Renderers.Length;i++)if(lod.Renderers[i] is SkinnedMeshRenderer||lod.Renderers[i].name.StartsWith("modelRoot",StringComparison.Ordinal)){
                Assert.AreSame(lod.OriginalMeshes[i],lod.NormalMeshes[i]);Assert.AreSame(lod.OriginalMeshes[i],lod.FarMeshes[i]);protectedRows++;
            }
            Assert.GreaterOrEqual(protectedRows,6);
        }
        [Test] public void ScienceUsesNewBaseScannerAtExistingScaleAndFootprint()
        {
            var host=new GameObject("Science binding");var p=PlayableProfile.Default;PlayableWorld world=null;
            try{
                world=new PlayableWorld(host.transform,p);var a=world.Building(501,"ScientificCenter",true);
                Assert.NotNull(a.Hull.GetComponentsInChildren<Transform>(true).SingleOrDefault(t=>t.name=="scannerTilt"));
                Assert.False(a.Hull.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="innerRing"));
                Assert.That(a.Hull.localScale.x,Is.EqualTo((float)(p.ScienceModelHeightMeters*p.ScienceModelScale/1.6407)).Within(.00001f));
                Assert.NotNull(a.DetailLod);Assert.True(a.Hull.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Any(m=>m.name=="research-scanner-team-primary"));
                var markers=a.Hull.GetComponentsInChildren<Transform>(true);var min=markers.Single(t=>t.name=="footprintMin");var max=markers.Single(t=>t.name=="footprintMax");
                Assert.That(Mathf.Abs(max.localPosition.x-min.localPosition.x),Is.EqualTo(2.9f).Within(.0001f));
            }finally{world?.Dispose();Object.DestroyImmediate(host);}
        }
        [Test] public void CameraProjectionHysteresisDirectJumpsAndInvalidViews()
        {
            var model=Object.Instantiate(Resources.Load<GameObject>("StyleA/Lod/explorer"));var host=new GameObject("Detail camera");
            try{
                var lod=model.GetComponent<ModelDetailLod>();var p=ModelDetailProfile.Load();p.Validate();Assert.AreEqual(4,p.Parameters().Count());
                lod.SelectScreenRatio(p.normalEnter,p);Assert.AreEqual(1,lod.Level);
                for(int i=0;i<20;i++){lod.SelectScreenRatio(p.normalEnter+(i%2==0?-.001f:.001f),p);Assert.AreEqual(1,lod.Level);}
                lod.SelectScreenRatio(p.farEnter,p);Assert.AreEqual(2,lod.Level);
                lod.SelectScreenRatio(p.farEnter+.001f,p);Assert.AreEqual(2,lod.Level);
                lod.SelectScreenRatio(p.farExit,p);Assert.AreEqual(1,lod.Level);
                lod.SelectScreenRatio(p.normalExit,p);Assert.AreEqual(0,lod.Level);
                lod.SelectScreenRatio(0,p);Assert.AreEqual(2,lod.Level);lod.SelectScreenRatio(1,p);Assert.AreEqual(0,lod.Level);
                foreach(float v in new[]{float.NaN,float.PositiveInfinity,-1}){lod.SetDetail(2);lod.SelectScreenRatio(v,p);Assert.AreEqual(0,lod.Level);}
                var c=host.AddComponent<Camera>();c.transform.position=new Vector3(0,1,-20);c.transform.LookAt(model.transform);c.orthographic=true;c.orthographicSize=1;lod.Select(c);Assert.AreEqual(0,lod.Level);c.orthographicSize=1000;lod.Select(c);Assert.AreEqual(2,lod.Level);
                c.orthographic=false;c.transform.position=new Vector3(0,0,-3);c.transform.rotation=Quaternion.identity;lod.Select(c);Assert.AreEqual(0,lod.Level);c.transform.position=new Vector3(0,0,-1000);lod.Select(c);Assert.AreEqual(2,lod.Level);c.transform.position=new Vector3(0,0,1000);lod.Select(c);Assert.AreEqual(0,lod.Level);
                p.farExit=p.normalEnter;Assert.Throws<ArgumentException>(()=>p.Validate());
            }finally{Object.DestroyImmediate(model);Object.DestroyImmediate(host);}
        }
        [Test] public void OrdinaryWorldBindsUnitsBuildingsAndIndependentLocalViews()
        {
            var host=new GameObject("Detail world");PlayableWorld world=null;
            try{
                world=new PlayableWorld(host.transform,PlayableProfile.Default);
                foreach(var kind in new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Shkval,PlayableEntityKind.Hawk})Assert.NotNull(world.Tank(20+(int)kind,true,kind).DetailLod,kind.ToString());
                foreach(var kind in Enum.GetValues(typeof(PlayableBuildingKind)).Cast<PlayableBuildingKind>())Assert.NotNull(world.Building(100+(int)kind,kind.ToString(),true).DetailLod,kind.ToString());
                var a=world.Tank(300,true);var b=world.Tank(301,false);a.DetailLod.SetDetail(2);Assert.AreEqual(0,b.DetailLod.Level);Assert.AreSame(b.DetailLod.OriginalMeshes[0],MeshOf(b.DetailLod.Renderers[0]));PlayableWorld.PaintOwner(a,new Color(.2f,.6f,.8f));
            }finally{world?.Dispose();Object.DestroyImmediate(host);}
        }
    }
}
