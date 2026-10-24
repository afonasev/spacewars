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

public sealed class HeadquartersGameModelTests
{
    [UnityTest] public IEnumerator SharedBuildingPathUsesApprovedModelAndPreservesOwnerAndEnvelope()
    {
        var host=new GameObject("Headquarters game integration witness");
        var ambient=RenderSettings.ambientLight;var ambientMode=RenderSettings.ambientMode;
        PlayableWorld world=null;Material groundMaterial=null;
        try
        {
            var profile=PlayableProfile.Default;world=new PlayableWorld(host.transform,profile);
            var friendly=world.Building(7001,"Headquarters",true);
            var enemy=world.Building(7002,"Headquarters",false);
            foreach(var actor in new[]{friendly,enemy})
            {
                Assert.That(actor.Hull.name,Does.StartWith("headquarters-a"));
                Assert.IsTrue(actor.Hull.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="landmark__hq__command-entry"));
                Assert.IsEmpty(actor.Hull.GetComponentsInChildren<Collider>(true));
                Assert.That(actor.Hull.localScale.x,Is.EqualTo(profile.HeadquartersModelHeightMeters*profile.HeadquartersModelScale/4.2).Within(.00001));
                Assert.That(actor.Root.localScale,Is.EqualTo(Vector3.one));
                var support=actor.Hull.GetComponentsInChildren<Renderer>().Single(r=>r.sharedMaterials.Any(m=>m.name.EndsWith("foundation-concrete")));
                float radius=0;
                for(int i=0;i<8;i++)
                {
                    var point=support.localBounds.center+Vector3.Scale(support.localBounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    point=actor.Root.InverseTransformPoint(support.transform.TransformPoint(point));radius=Mathf.Max(radius,new Vector2(point.x,point.z).magnitude);
                }
                Assert.Greater(actor.Selection.transform.localScale.x,radius+(float)profile.BuildingSelectionPadding);
            }
            AssertPaint(friendly,new Color(.19f,.72f,.77f));AssertPaint(enemy,new Color(.93f,.34f,.23f));
            var colors=new[]{new Color(.19f,.72f,.77f),new Color(.82f,.22f,.16f),new Color(.25f,.66f,.30f),new Color(.66f,.34f,.8f)};
            var sourceColors=Resources.Load<GameObject>("StyleA/headquarters-a").GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials).Select(m=>m.GetColor("_BaseColor")).ToArray();
            foreach(var color in colors){PlayableWorld.PaintOwner(friendly,color);AssertPaint(friendly,color);AssertPaint(enemy,new Color(.93f,.34f,.23f));}
            CollectionAssert.AreEqual(sourceColors,Resources.Load<GameObject>("StyleA/headquarters-a").GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials).Select(m=>m.GetColor("_BaseColor")).ToArray());
            friendly.Root.position=new Vector3(-2.5f,0,0);enemy.Root.position=new Vector3(2.5f,0,0);
            friendly.Selection.SetActive(true);enemy.Selection.SetActive(true);
            foreach(var actor in new[]{friendly,enemy})foreach(var t in actor.Root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
            var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.transform.SetParent(host.transform);floor.layer=30;floor.transform.position=Vector3.down*.02f;
            groundMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));groundMaterial.color=new Color(.30f,.32f,.34f);floor.GetComponent<Renderer>().sharedMaterial=groundMaterial;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.6f,.62f,.65f);
            var light=new GameObject("Witness daylight").AddComponent<Light>();light.transform.SetParent(host.transform);light.type=LightType.Directional;light.intensity=1.6f;light.transform.rotation=Quaternion.Euler(50,-35,0);
            var camera=new GameObject("Shared world witness camera").AddComponent<Camera>();camera.transform.SetParent(host.transform);camera.orthographic=true;camera.orthographicSize=4.5f;camera.cullingMask=1<<30;camera.nearClipPlane=.05f;camera.farClipPlane=100;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.30f,.32f,.34f);camera.transform.position=new Vector3(1,7,12);camera.transform.LookAt(new Vector3(0,1,0));camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            foreach(var actor in new[]{friendly,enemy})
            {
                world.FaceBuilding(actor,camera);
                var expected=Vector3.ProjectOnPlane(-camera.transform.forward,Vector3.up).normalized;
                Assert.That(Vector3.Dot(actor.Hull.forward,expected),Is.GreaterThan(.9999));
            }
            yield return null;
            string evidence=Environment.GetEnvironmentVariable("HEADQUARTERS_GAME_EVIDENCE");
            if(!string.IsNullOrEmpty(evidence)){Directory.CreateDirectory(evidence);Capture(camera,Path.Combine(evidence,"headquarters-game-path.png"));}
            world.Remove(7001);yield return null;
            var rebuilt=world.Building(7001,"Headquarters",true);Assert.That(rebuilt.Hull.name,Does.StartWith("headquarters-a"));AssertPaint(rebuilt,colors[0]);
            world.Rebind(profile);yield return null;Assert.IsEmpty(world.Actors);
            var restarted=world.Building(7003,"Headquarters",false);Assert.That(restarted.Hull.name,Does.StartWith("headquarters-a"));AssertPaint(restarted,new Color(.93f,.34f,.23f));
        }
        finally{world?.Dispose();Object.Destroy(host);if(groundMaterial)Object.Destroy(groundMaterial);RenderSettings.ambientLight=ambient;RenderSettings.ambientMode=ambientMode;}
    }
    static void AssertPaint(PlayableWorld.Actor actor,Color color)
    {
        int body=0,lamps=0;
        foreach(var renderer in actor.Hull.GetComponentsInChildren<Renderer>())for(int i=0;i<renderer.sharedMaterials.Length;i++)
        {
            string role=renderer.sharedMaterials[i].name;if(!role.EndsWith("team-primary")&&!role.EndsWith("team-emissive"))continue;
            var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,i);Assert.AreEqual(StoredColor("_BaseColor",color),block.GetColor("_BaseColor"));
            if(role.EndsWith("team-emissive")){lamps++;Assert.AreEqual(StoredColor("_EmissionColor",color*actor.MaterialFinish.TeamEmissionFor(role)),block.GetColor("_EmissionColor"));}else body++;
        }
        Assert.Greater(body,0);Assert.Greater(lamps,0);
    }
    // Match ActorMaterialRenderingTests: exact native color-space round trip, without tolerance.
    static Color StoredColor(string property,Color color)
    {var block=new MaterialPropertyBlock();block.SetColor(property,color);return block.GetColor(property);}
    static void Capture(Camera camera,string path)
    {
        var target=new RenderTexture(1600,1000,24);var texture=new Texture2D(1600,1000,TextureFormat.RGB24,false);var previous=RenderTexture.active;
        try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,1600,1000),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());}
        finally{camera.targetTexture=null;RenderTexture.active=previous;target.Release();Object.Destroy(target);Object.Destroy(texture);}
    }
}
