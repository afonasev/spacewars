using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

public sealed class BuildingExteriorLightTests
{
    static readonly string[] Kinds={"Headquarters","Refinery","Refinery","Factory","Outpost"};
    static readonly string[] Keys={"headquarters","refinery","refinery-upgraded","factory","outpost"};
    static readonly Color[] Colors={new Color(.82f,.22f,.16f),new Color(.25f,.66f,.30f),new Color(.66f,.34f,.8f),new Color(.19f,.72f,.77f)};
    static Color Stored(string property,Color value){var b=new MaterialPropertyBlock();b.SetColor(property,value);return b.GetColor(property);}

    [Test] public void AllExteriorLampsFollowOwnerAcrossRepaintAndGraphicsRefresh()
    {
        var host=new GameObject("Exterior light contracts");
        try
        {
            using var world=new PlayableWorld(host.transform,PlayableProfile.Default);
            for(int k=0;k<Kinds.Length;k++)
            {
                var actor=world.Building(500+k,Kinds[k],true,k==2);
                var sources=actor.Hull.GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials).Where(m=>m.HasProperty("_BaseColor")).ToArray();
                var baseColors=sources.Select(m=>m.GetColor("_BaseColor")).ToArray();
                foreach(var color in Colors)
                {
                    PlayableWorld.PaintOwner(actor,color);AssertExterior(actor,color);
                    world.ApplyGraphics(ActorMaterialProfile.Load("luminous"),IndustrialLightingProfile.Load());AssertExterior(actor,color);
                    world.ApplyGraphics(ActorMaterialProfile.Load(),IndustrialLightingProfile.Load());AssertExterior(actor,color);
                }
                CollectionAssert.AreEqual(baseColors,sources.Select(m=>m.GetColor("_BaseColor")).ToArray(),"Shared source materials must remain unchanged");
            }
        }
        finally{Object.DestroyImmediate(host);}
    }
    [Test] public void OrdinaryOutpostUsesSelectedArmoredForkAtExistingScale()
    {
        var host=new GameObject("Production outpost model contract");
        try
        {
            var profile=PlayableProfile.Default;
            using var world=new PlayableWorld(host.transform,profile);
            var actor=world.Building(550,"Outpost",true);
            Assert.AreEqual("outpost-b(Clone)",actor.Hull.name);
            Assert.AreEqual((float)(profile.OutpostModelHeightMeters/4.5094*profile.OutpostModelScale),actor.Hull.localScale.x);
            Assert.IsEmpty(actor.Hull.GetComponentsInChildren<Collider>(true));
            var bounds=new Bounds();bool first=true;
            foreach(var renderer in actor.Hull.GetComponentsInChildren<Renderer>())
            {if(first){bounds=renderer.bounds;first=false;}else bounds.Encapsulate(renderer.bounds);}
            Assert.That(bounds.size.y,Is.EqualTo((float)(profile.OutpostModelHeightMeters*profile.OutpostModelScale)).Within(.001f));
            Assert.AreEqual(actor.MaterialFinish.buildingLampEmission,actor.MaterialFinish.TeamEmissionFor("outpost-comparison-team-emissive"));
        }
        finally{Object.DestroyImmediate(host);}
    }
    static void AssertExterior(PlayableWorld.Actor actor,Color color)
    {
        int exterior=0;
        foreach(var r in actor.Hull.GetComponentsInChildren<Renderer>())for(int i=0;i<r.sharedMaterials.Length;i++)
        {
            var m=r.sharedMaterials[i];
            if(m.name=="factory-armored-threshold-team-primary")
            {
                var paint=new MaterialPropertyBlock();r.GetPropertyBlock(paint,i);
                Assert.AreEqual(Stored("_BaseColor",color),paint.GetColor("_BaseColor"),"Gate threshold also follows owner hue");
            }
            if(!m.name.EndsWith("emissive")||m.name.EndsWith("work-emissive"))continue;
            exterior++;Assert.That(m.name,Does.EndWith("team-emissive"),r.name+" must use owner hue");
            var b=new MaterialPropertyBlock();r.GetPropertyBlock(b,i);Assert.AreEqual(Stored("_BaseColor",color),b.GetColor("_BaseColor"));
            float gain=actor.MaterialFinish.TeamEmissionFor(m.name);Assert.Greater(gain,0,"Glow adds light to the same owner-colored base");
            Assert.AreEqual(Stored("_EmissionColor",color*gain),b.GetColor("_EmissionColor"));
            Assert.IsTrue(m.IsKeywordEnabled("_EMISSION"));
        }
        Assert.Greater(exterior,0);
    }
    [Test] public void ProductionFlashesFollowOwnerAndStopWhenIdle()
    {
        var host=new GameObject("Factory process flash contract");
        try
        {
            using var world=new PlayableWorld(host.transform,PlayableProfile.Default);var a=world.Building(600,"Factory",true);
            var work=a.Hull.GetComponentsInChildren<Renderer>().Where(r=>r.name.StartsWith("workGlow",StringComparison.Ordinal)).ToArray();Assert.AreEqual(3,work.Length);
            foreach(var color in Colors)
            {
                PlayableWorld.PaintOwner(a,color);bool seen=false;
                for(int frame=0;frame<20;frame++)
                {
                    a.Factory.Apply(FactoryModelPresentation.Phase.Working,0,frame*.05f);
                    foreach(var r in work)
                    {
                        var b=new MaterialPropertyBlock();r.GetPropertyBlock(b,0);var e=b.GetColor("_EmissionColor");
                        if(e.maxColorComponent<=0)continue;
                        seen=true;
                        int i=Array.IndexOf(work,r);float seconds=frame*.05f;var profile=a.Factory.Profile;
                        float wave=Mathf.Sin(seconds*profile.flashRate*6.283185f+i*2.1f)*Mathf.Sin(seconds*profile.flashRate*4.7f+i);
                        Assert.AreEqual(Stored("_EmissionColor",color*Mathf.Max(0,wave)*profile.flashIntensity),e);
                        Assert.IsTrue(r.sharedMaterial.IsKeywordEnabled("_EMISSION"));
                        world.ApplyGraphics(ActorMaterialProfile.Load("luminous"),IndustrialLightingProfile.Load());
                        r.GetPropertyBlock(b,0);Assert.AreEqual(e,b.GetColor("_EmissionColor"),"Graphics refresh must preserve current colored flash");
                    }
                }
                Assert.IsTrue(seen,"Working factory must still flash");
                // Repainting an active flash must be immediate, without advancing its phase.
                a.Factory.Apply(FactoryModelPresentation.Phase.Working,0,.35f);
                PlayableWorld.PaintOwner(a,Color.white);PlayableWorld.PaintOwner(a,color);
                var immediate=work.Select(r=>{var b=new MaterialPropertyBlock();r.GetPropertyBlock(b,0);return b.GetColor("_EmissionColor");}).ToArray();
                a.Factory.Apply(FactoryModelPresentation.Phase.Working,0,.35f);
                CollectionAssert.AreEqual(immediate,work.Select(r=>{var b=new MaterialPropertyBlock();r.GetPropertyBlock(b,0);return b.GetColor("_EmissionColor");}).ToArray());
                a.Factory.Apply(FactoryModelPresentation.Phase.Idle,0,1);
                foreach(var r in work){var b=new MaterialPropertyBlock();r.GetPropertyBlock(b,0);Assert.AreEqual(Stored("_EmissionColor",Color.clear),b.GetColor("_EmissionColor"));}
                AssertExterior(a,color);
            }
        }
        finally{Object.DestroyImmediate(host);}
    }
    [UnityTest] public IEnumerator CaptureSharedRuntimeModels()
    {
        string output=Environment.GetEnvironmentVariable("BUILDING_LIGHT_EVIDENCE");
        var host=new GameObject("Owner light visual witness");var ambient=RenderSettings.ambientLight;var mode=RenderSettings.ambientMode;Material floorMat=null;
        try
        {
            using var world=new PlayableWorld(host.transform,PlayableProfile.Default);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.60f,.62f,.65f);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.transform.SetParent(host.transform);floor.layer=30;floor.transform.localScale=new Vector3(4,1,3);floor.transform.position=Vector3.down*.02f;
            floorMat=new Material(Shader.Find("Universal Render Pipeline/Lit"));floorMat.color=new Color(.30f,.32f,.34f);floor.GetComponent<Renderer>().sharedMaterial=floorMat;
            var light=new GameObject("Review light").AddComponent<Light>();light.transform.SetParent(host.transform);light.type=LightType.Directional;light.intensity=1.6f;light.transform.rotation=Quaternion.Euler(50,-35,0);
            var camera=new GameObject("Review camera").AddComponent<Camera>();camera.transform.SetParent(host.transform);camera.orthographic=true;camera.orthographicSize=4.4f;camera.cullingMask=1<<30;camera.nearClipPlane=.05f;camera.farClipPlane=100;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=floorMat.color;camera.transform.position=new Vector3(0,8,14);camera.transform.LookAt(new Vector3(0,1.2f,0));camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            for(int k=0;k<Kinds.Length;k++)
            {
                camera.transform.position=k==3?new Vector3(14,8,0):new Vector3(0,8,14);camera.transform.LookAt(new Vector3(0,1.2f,0));
                for(int i=0;i<Colors.Length;i++)
                {
                    var a=world.Building(800+i,Kinds[k],true,k==2);PlayableWorld.PaintOwner(a,Colors[i]);a.Root.position=k==3?new Vector3(0,0,(i-1.5f)*5.1f):new Vector3((i-1.5f)*5.1f,0,0);world.FaceBuilding(a,camera);
                    foreach(var t in a.Hull.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
                }
                yield return null;
                if(!string.IsNullOrEmpty(output)){Directory.CreateDirectory(output);Capture(camera,Path.Combine(output,Keys[k]+".png"));}
                if(k==3)
                {
                    for(int i=0;i<Colors.Length;i++)world.Actors[800+i].Factory.Apply(FactoryModelPresentation.Phase.Working,0,.1f);
                    if(!string.IsNullOrEmpty(output))Capture(camera,Path.Combine(output,"factory-working.png"));
                }
                for(int i=0;i<Colors.Length;i++)world.Remove(800+i);
                yield return null;
            }
        }
        finally{Object.Destroy(host);if(floorMat)Object.Destroy(floorMat);RenderSettings.ambientLight=ambient;RenderSettings.ambientMode=mode;}
    }
    static void Capture(Camera camera,string path)
    {
        var target=new RenderTexture(2400,1000,24);var texture=new Texture2D(2400,1000,TextureFormat.RGB24,false);var old=RenderTexture.active;
        try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,2400,1000),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());}
        finally{camera.targetTexture=null;RenderTexture.active=old;target.Release();Object.Destroy(target);Object.Destroy(texture);}
    }
}
