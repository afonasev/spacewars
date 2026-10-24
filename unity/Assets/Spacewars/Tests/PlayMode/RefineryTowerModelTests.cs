using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

public sealed class RefineryTowerModelTests
{
    static Bounds LocalBounds(GameObject model)
    {
        var bounds=new Bounds();bool first=true;
        foreach(var f in model.GetComponentsInChildren<MeshFilter>(true))
            foreach(var p in new[]{f.sharedMesh.bounds.min,f.sharedMesh.bounds.max})
            {var v=model.transform.InverseTransformPoint(f.transform.TransformPoint(p));if(first){bounds=new Bounds(v,Vector3.zero);first=false;}else bounds.Encapsulate(v);}
        return bounds;
    }
    [Test] public void TowerTiersKeepFoundationEnvelopeMaterialRolesAndSeparateRotor()
    {
        var basic=Resources.Load<GameObject>("StyleA/refinery");var upgraded=Resources.Load<GameObject>("StyleA/refineryUpgraded");
        Assert.NotNull(basic);Assert.NotNull(upgraded);
        var a=LocalBounds(basic);var b=LocalBounds(upgraded);
        Assert.Less(Vector3.Distance(a.size,b.size),.001f);Assert.Less(Vector3.Distance(a.center,b.center),.001f);
        Assert.That(a.size.x,Is.EqualTo(2.7f).Within(.002));Assert.That(a.size.y,Is.EqualTo(2.545f).Within(.002));Assert.LessOrEqual(a.size.z,2.414f);
        foreach(var prefab in new[]{basic,upgraded})
        {
            Assert.IsEmpty(prefab.GetComponentsInChildren<Collider>(true));
            var support=prefab.GetComponentsInChildren<MeshFilter>(true).Single(m=>m.name.EndsWith(":foundation-concrete"));
            Assert.That(support.sharedMesh.bounds.size.x,Is.EqualTo(2.7f).Within(.001));
            Assert.That(support.sharedMesh.bounds.size.z,Is.EqualTo(2.35f).Within(.001));
            Assert.NotNull(prefab.GetComponentsInChildren<Transform>(true).SingleOrDefault(t=>t.name=="modelRoot"));
            foreach(var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            foreach(var mat in renderer.sharedMaterials)
            {Assert.AreEqual("Universal Render Pipeline/Lit",mat.shader.name);Assert.IsTrue(mat.shader.isSupported);}
            foreach(string role in new[]{"team-primary","neutral-metal","foundation-concrete"})
                Assert.IsTrue(prefab.GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials).Any(m=>m.name.EndsWith(role)&&m.GetTexture("_BaseMap")),role+" must retain PBR surface");
        }
        Assert.IsFalse(basic.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="turbineYaw"));
        var rotor=upgraded.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="turbineYaw");
        Assert.AreEqual(1,rotor.GetComponentsInChildren<MeshFilter>().Length);
        Assert.That(rotor.GetComponentInChildren<MeshFilter>().sharedMesh.bounds.size.z,Is.LessThan(.15f),"Rotor blade plane must remain perpendicular to runtime local Z axis");
    }

    [Test] public void CornerBeaconsKeepOwnerHueThroughUpgradeRepaintAndGraphicsRefresh()
    {
        var host=new GameObject("Refinery beacon ownership");
        try
        {
            using var world=new PlayableWorld(host.transform,PlayableProfile.Default);
            var actor=world.Building(7003,"Refinery",true);
            var untouched=world.Building(7004,"Refinery",true);
            var shared=untouched.Hull.GetComponentsInChildren<Renderer>().Single(r=>r.sharedMaterial.name=="refinery-beacon-team-emissive").sharedMaterial;
            var sharedColor=shared.GetColor("_EmissionColor");
            foreach(bool upgraded in new[]{false,true})
            {
                world.UpdateRefineryModel(actor,new PlayableBuildingSnapshot(7003,PlayableOwner.Player,PlayableBuildingKind.Refinery,default,123,1,0,0,default,refineryUpgraded:upgraded),15);
                foreach(var color in new[]{new Color(.19f,.72f,.77f),new Color(.93f,.34f,.23f),new Color(.44f,.75f,.20f)})
                foreach(string variant in new[]{"restrained","luminous"})
                {
                    var finish=ActorMaterialProfile.Load(variant);
                    PlayableWorld.PaintOwner(actor,color);
                    world.ApplyGraphics(finish,IndustrialLightingProfile.Load());
                    var lamps=actor.Hull.GetComponentsInChildren<Renderer>().Single(r=>r.sharedMaterial.name=="refinery-beacon-team-emissive");
                    var actual=new MaterialPropertyBlock();lamps.GetPropertyBlock(actual,0);
                    var expected=new MaterialPropertyBlock();expected.SetColor("_BaseColor",color);expected.SetColor("_EmissionColor",color*finish.refineryBeaconEmission);
                    Assert.AreEqual(expected.GetColor("_BaseColor"),actual.GetColor("_BaseColor"));
                    Assert.AreEqual(expected.GetColor("_EmissionColor"),actual.GetColor("_EmissionColor"));
                    Assert.Less(finish.refineryBeaconEmission,finish.teamEmission,"Corner lights retain saturated owner hue");
                    Assert.AreSame(shared,lamps.sharedMaterial);Assert.AreEqual(sharedColor,shared.GetColor("_EmissionColor"));
                    var body=actor.Hull.GetComponentsInChildren<Renderer>().Single(r=>r.sharedMaterial.name=="refinery-body-team-emissive");
                    body.GetPropertyBlock(actual,0);expected.SetColor("_EmissionColor",color*finish.TeamEmissionFor(body.sharedMaterial.name));
                    Assert.AreEqual(expected.GetColor("_BaseColor"),actual.GetColor("_BaseColor"));
                    Assert.AreEqual(expected.GetColor("_EmissionColor"),actual.GetColor("_EmissionColor"),"Body lamps follow owner hue with brighter team emission");
                }
            }
        }
        finally{Object.DestroyImmediate(host);}
    }

    [UnityTest] public IEnumerator OrdinaryWorldSwapsTowerTierAndRendersTickDrivenRotor()
    {
        var host=new GameObject("refinery runtime witness");var previousAmbient=RenderSettings.ambientLight;var previousMode=RenderSettings.ambientMode;
        PlayableWorld world=null;Material groundMat=null;
        try
        {
            world=new PlayableWorld(host.transform,PlayableProfile.Default);
            var basic=world.Building(7001,"Refinery",true);var upgrade=world.Building(7002,"Refinery",true);var root=upgrade.Root;
            basic.Root.position=new Vector3(-1.65f,0,0);upgrade.Root.position=new Vector3(1.65f,0,0);
            basic.Hull.localRotation=Quaternion.identity;upgrade.Hull.localRotation=Quaternion.identity;
            var snapshot=new PlayableBuildingSnapshot(7002,PlayableOwner.Player,PlayableBuildingKind.Refinery,default,123,1,0,0,default,refineryUpgraded:true);
            world.UpdateRefineryModel(upgrade,snapshot,0);Assert.AreSame(root,upgrade.Root);Assert.True(upgrade.RefineryUpgraded);Assert.NotNull(upgrade.Turbine);
            upgrade.Hull.localRotation=Quaternion.identity;
            foreach(var actor in new[]{basic,upgrade})
            {
                var support=actor.Hull.GetComponentsInChildren<Renderer>().Single(r=>r.name.EndsWith(":foundation-concrete"));
                var bounds=support.localBounds;float radius=0;
                for(int i=0;i<8;i++)
                {
                    var corner=bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    var point=actor.Root.InverseTransformPoint(support.transform.TransformPoint(corner));
                    radius=Mathf.Max(radius,new Vector2(point.x,point.z).magnitude);
                }
                Assert.Greater(radius,0);
                Assert.Greater(actor.Selection.transform.localScale.x,radius+(float)PlayableProfile.Default.BuildingSelectionPadding,"Both refinery tiers keep their selection footprint");
            }
            var before=upgrade.Turbine.localRotation;var position=upgrade.Turbine.localPosition;
            world.UpdateRefineryModel(upgrade,snapshot,15);Assert.Greater(Quaternion.Angle(before,upgrade.Turbine.localRotation),1);
            var paused=upgrade.Turbine.localRotation;world.UpdateRefineryModel(upgrade,snapshot,15);Assert.AreEqual(paused,upgrade.Turbine.localRotation);Assert.AreEqual(position,upgrade.Turbine.localPosition);
            var color=new Color(.19f,.72f,.77f);PlayableWorld.PaintOwner(basic,color);PlayableWorld.PaintOwner(upgrade,color);
            foreach(var actor in new[]{basic,upgrade})foreach(var t in actor.Hull.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
            var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.transform.SetParent(host.transform);floor.transform.position=new Vector3(0,-.01f,0);floor.transform.localScale=Vector3.one*3;floor.layer=30;Object.Destroy(floor.GetComponent<Collider>());
            groundMat=new Material(Shader.Find("Universal Render Pipeline/Lit"));groundMat.SetColor("_BaseColor",new Color(.25f,.27f,.28f));floor.GetComponent<Renderer>().sharedMaterial=groundMat;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.52f,.55f,.60f);
            var light=new GameObject("refinery witness light").AddComponent<Light>();light.transform.SetParent(host.transform);light.type=LightType.Directional;light.intensity=1.8f;light.transform.rotation=Quaternion.Euler(50,-35,0);
            var camera=new GameObject("refinery witness camera").AddComponent<Camera>();camera.transform.SetParent(host.transform);camera.orthographic=true;camera.orthographicSize=4;camera.cullingMask=1<<30;camera.nearClipPlane=.05f;camera.farClipPlane=100;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.25f,.27f,.28f);camera.transform.position=new Vector3(6,7,11);camera.transform.LookAt(new Vector3(0,.8f,0));camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            yield return null;
            string evidence=Environment.GetEnvironmentVariable("REFINERY_MODEL_EVIDENCE");
            if(!string.IsNullOrEmpty(evidence))
            {
                Directory.CreateDirectory(evidence);Capture(camera,Path.Combine(evidence,"playmode-pair.png"));
                world.UpdateRefineryModel(upgrade,snapshot,30);yield return null;Capture(camera,Path.Combine(evidence,"playmode-rotor-90.png"));
            }
        }
        finally
        {
            world?.Dispose();Object.Destroy(host);if(groundMat)Object.Destroy(groundMat);RenderSettings.ambientLight=previousAmbient;RenderSettings.ambientMode=previousMode;
        }
    }
    static void Capture(Camera camera,string path)
    {
        var target=new RenderTexture(1600,1000,24);var texture=new Texture2D(1600,1000,TextureFormat.RGB24,false);var previous=RenderTexture.active;
        try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,1600,1000),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());}
        finally{camera.targetTexture=null;RenderTexture.active=previous;target.Release();Object.Destroy(target);Object.Destroy(texture);}
    }
}
