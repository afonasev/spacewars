using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Spacewars.Presentation;
using Spacewars.Simulation;
#if UNITY_EDITOR
using UnityEditor;
#endif
namespace Spacewars.Tests.PlayMode
{
    public sealed class IndustrialLightingRenderingTests
    {
        private Color ambient;private AmbientMode mode;private float volume;
#if UNITY_EDITOR
        private float oldShadowDistance;
#endif
        [SetUp] public void Setup(){ambient=RenderSettings.ambientLight;mode=RenderSettings.ambientMode;volume=AudioListener.volume;AudioListener.volume=0;
#if UNITY_EDITOR
            oldShadowDistance=new SerializedObject(GraphicsSettings.currentRenderPipeline).FindProperty("m_ShadowDistance").floatValue;
#endif
        }
        [TearDown] public void Cleanup(){RenderSettings.ambientLight=ambient;RenderSettings.ambientMode=mode;AudioListener.volume=volume;
#if UNITY_EDITOR
            var pipeline=new SerializedObject(GraphicsSettings.currentRenderPipeline);pipeline.FindProperty("m_ShadowDistance").floatValue=oldShadowDistance;pipeline.ApplyModifiedPropertiesWithoutUndo();
#endif
        }
        [Test] public void NamedProfileValidatesEveryControlAndAppliesSharedSun()
        {
            var p=IndustrialLightingProfile.Load();
            foreach(var f in p.Parameters())
            {
                Assert.IsNotEmpty(f.Description);Assert.Greater(f.Step,0);Assert.That(f.Path,Does.StartWith("visual.lighting."));
                float original=f.Read();foreach(var bad in new[]{float.NaN,float.PositiveInfinity,f.Minimum-f.Step,f.Maximum+f.Step})
                {f.Write(bad);Assert.Throws<ArgumentException>(()=>p.Validate(),f.Path);}f.Write(original);
            }
            var host=new GameObject("Shared profile sun");
            try
            {
                var sun=host.AddComponent<Light>();p.ApplyScene(sun);
                Assert.AreEqual(LightType.Directional,sun.type);Assert.AreEqual(p.sunIntensity,sun.intensity);
                Assert.AreEqual(Color.HSVToRGB(p.sunHue,p.sunSaturation,1),sun.color);
                Assert.AreEqual(NativeDisplaySettings.Shadows?LightShadows.Soft:LightShadows.None,sun.shadows);
                Assert.AreEqual(Color.HSVToRGB(p.ambientHue,p.ambientSaturation,p.ambientValue),RenderSettings.ambientLight);
            }finally{UnityEngine.Object.DestroyImmediate(host);}
        }
        private static Camera Camera(Transform parent,RenderTexture target)
        {
            var camera=new GameObject("Industrial preview camera").AddComponent<Camera>();camera.transform.SetParent(parent);
            camera.fieldOfView=48;camera.nearClipPlane=.1f;camera.farClipPlane=700;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.075f,.10f,.13f);camera.targetTexture=target;return camera;
        }
        private static Texture2D Read(RenderTexture target)
        {
            var previous=RenderTexture.active;
            try {RenderTexture.active=target;var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();return pixels;}
            finally{RenderTexture.active=previous;}
        }
        private static int Changed(Color32[] a,Color32[] b,int threshold)
        {return a.Zip(b,(x,y)=>Math.Abs(x.r-y.r)+Math.Abs(x.g-y.g)+Math.Abs(x.b-y.b)>threshold).Count(v=>v);}
        [UnityTest] public IEnumerator ExistingOpaqueMeshesCastReceiveAndFogMasksShadowContrast()
        {
            var root=new GameObject("Shadow and fog contract");
            var shader=Resources.Load<Shader>("TerritoryFog");var material=new Material(shader);
            var target=new RenderTexture(800,500,24);target.Create();
            var camera=Camera(root.transform,target);camera.transform.position=new Vector3(0,14,-9);camera.transform.LookAt(Vector3.zero);
            var sun=new GameObject("Contract sun").AddComponent<Light>();sun.transform.SetParent(root.transform);IndustrialLightingProfile.Load().ApplyScene(sun);
            material.SetColor("_BaseColor",new Color(.55f,.55f,.55f));material.SetTexture("_FogMask",Texture2D.blackTexture);
            var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.transform.SetParent(root.transform);ground.transform.position=new Vector3(0,-.1f,0);ground.transform.localScale=new Vector3(20,.2f,20);ground.GetComponent<Renderer>().sharedMaterial=material;
            var caster=GameObject.CreatePrimitive(PrimitiveType.Cube);caster.transform.SetParent(root.transform);caster.transform.position=new Vector3(0,1.5f,0);caster.transform.localScale=new Vector3(2,3,2);caster.GetComponent<Renderer>().sharedMaterial=material;
            Texture2D on=null,off=null,fogOn=null,fogOff=null;
            try
            {
                Assert.GreaterOrEqual(material.FindPass("ShadowCaster"),0);
                sun.shadows=LightShadows.Soft;for(int i=0;i<5;i++)yield return null;on=Read(target);
                sun.shadows=LightShadows.None;for(int i=0;i<5;i++)yield return null;off=Read(target);
                Assert.Greater(Changed(on.GetPixels32(),off.GetPixels32(),12),150,"Existing terrain shader must cast and receive real main light shadows");
                material.SetTexture("_FogMask",Texture2D.whiteTexture);sun.shadows=LightShadows.Soft;for(int i=0;i<5;i++)yield return null;fogOn=Read(target);
                sun.shadows=LightShadows.None;for(int i=0;i<5;i++)yield return null;fogOff=Read(target);
                Assert.Zero(Changed(fogOn.GetPixels32(),fogOff.GetPixels32(),0),"Fully concealed surface must not expose shadow contrast");
#if UNITY_EDITOR
                foreach(var message in ShaderUtil.GetShaderMessages(shader))Assert.AreNotEqual("Error",message.severity.ToString(),message.message);
#endif
            }
            finally
            {
                camera.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);
                foreach(var image in new[]{on,off,fogOn,fogOff})if(image)UnityEngine.Object.Destroy(image);
                UnityEngine.Object.Destroy(root);UnityEngine.Object.Destroy(material);
            }
        }
        [UnityTest] public IEnumerator DecorationsBeyondOldDistanceCastIntoGameplayView()
        {
#if UNITY_EDITOR
            var output=Environment.GetEnvironmentVariable("SPACEWARS_LIGHTING_EVIDENCE");
            if(string.IsNullOrEmpty(output))output=Path.GetFullPath("../.local/industrial-lighting/captures");Directory.CreateDirectory(output);
            var profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));
            var root=new GameObject("Decoration shadow isolation");var world=new PlayableWorld(root.transform,profile);world.InspectionReveal();
            var focus=world.Point(TerritoryRules.Sites(profile)[0].Position);
            var decor=root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name.StartsWith("Detail —")).ToArray();Assert.Greater(decor.Length,0);
            var target=new RenderTexture(1600,1000,24);target.Create();var camera=Camera(root.transform,target);camera.orthographic=true;camera.orthographicSize=(float)profile.CameraOrthoSize;
            camera.transform.position=focus+new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);camera.transform.LookAt(focus);
            var sun=new GameObject("Decoration test sun").AddComponent<Light>();sun.transform.SetParent(root.transform);
            var lighting=IndustrialLightingProfile.Load();lighting.ApplyScene(sun);sun.shadows=LightShadows.Soft;
            var pipeline=new SerializedObject(GraphicsSettings.currentRenderPipeline);
            int oldUpper=0,newUpper=0;int rendererCount=root.GetComponentsInChildren<Renderer>().Length;
            try
            {
                foreach(float distance in new[]{50f,lighting.shadowDistance})
                {
                    pipeline.Update();pipeline.FindProperty("m_ShadowDistance").floatValue=distance;pipeline.ApplyModifiedPropertiesWithoutUndo();
                    Color32[] cast=null,noCast=null;
                    foreach(bool enabled in new[]{true,false})
                    {
                        foreach(var r in decor)r.shadowCastingMode=enabled?ShadowCastingMode.On:ShadowCastingMode.Off;
                        for(int i=0;i<5;i++)yield return null;
                        var pixels=Read(target);
                        try{if(enabled)cast=pixels.GetPixels32();else noCast=pixels.GetPixels32();File.WriteAllBytes(Path.Combine(output,"crossings-decoration-distance"+distance+"-"+(enabled?"cast":"no-cast")+".png"),pixels.EncodeToPNG());}
                        finally{UnityEngine.Object.Destroy(pixels);}
                    }
                    int changed=0;for(int y=720;y<1000;y++)for(int x=0;x<1600;x++){int i=y*1600+x;var a=cast[i];var b=noCast[i];if((b.r+b.g+b.b)-(a.r+a.g+a.b)>12)changed++;}
                    if(distance==50)oldUpper=changed;else newUpper=changed;
                    Assert.AreEqual(rendererCount,root.GetComponentsInChildren<Renderer>().Length,"Only casting mode changes; geometry is identical");
                }
                File.WriteAllText(Path.Combine(output,"decoration-shadow-isolation.txt"),"Crossings gameplay camera; only decoration cast toggled; upper 28% pixels darkened more than 12 RGB sum: old50="+oldUpper+" candidate="+newUpper+"; distance="+lighting.shadowDistance+"; no actor/mesh added");
                Assert.Greater(newUpper,oldUpper+100,"Extended coverage must restore visible upper decoration shadows");
            }
            finally{camera.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);world.Dispose();UnityEngine.Object.Destroy(root);}
#else
            Assert.Ignore("Editor real-render regression");yield break;
#endif
        }
        [UnityTest] public IEnumerator VisibleCoverageExpandsPerCameraWithoutLeakingLayersOrPipelineState()
        {
#if UNITY_EDITOR
            var root=new GameObject("Far visible shadow coverage");var target=new RenderTexture(800,500,24);target.Create();
            var camera=Camera(root.transform,target);camera.orthographic=true;camera.orthographicSize=8;camera.farClipPlane=350;
            camera.transform.position=new Vector3(0,120,-120);camera.transform.LookAt(Vector3.zero);camera.cullingMask=1<<8;
            var material=new Material(Resources.Load<Shader>("TerritoryFog"));material.SetColor("_BaseColor",new Color(.55f,.55f,.55f));material.SetTexture("_FogMask",Texture2D.blackTexture);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.SetParent(root.transform);floor.transform.position=new Vector3(0,-.1f,0);floor.transform.localScale=new Vector3(20,.2f,20);floor.layer=8;floor.GetComponent<Renderer>().sharedMaterial=material;
            var caster=GameObject.CreatePrimitive(PrimitiveType.Cube);caster.transform.SetParent(root.transform);caster.transform.position=new Vector3(0,1.5f,0);caster.transform.localScale=new Vector3(2,3,2);caster.layer=8;caster.GetComponent<Renderer>().sharedMaterial=material;
            var sun=new GameObject("Far coverage sun").AddComponent<Light>();sun.transform.SetParent(root.transform);var lighting=IndustrialLightingProfile.Load();lighting.ApplyScene(sun);sun.shadows=LightShadows.Soft;
            var pipeline=new SerializedObject(GraphicsSettings.currentRenderPipeline);
            var coverage=camera.gameObject.AddComponent<VisibleShadowCoverage>();coverage.Configure(root.transform,lighting);coverage.enabled=false;
            Texture2D fixedRange=null,automatic=null;
            try
            {
                for(int i=0;i<5;i++)yield return null;fixedRange=Read(target);
                coverage.enabled=true;for(int i=0;i<5;i++)yield return null;automatic=Read(target);
                Assert.Greater(coverage.LastRequiredDistance,lighting.shadowDistance,"Visible geometry beyond profile minimum expands coverage");
                pipeline.Update();Assert.AreEqual(lighting.shadowDistance,pipeline.FindProperty("m_ShadowDistance").floatValue,"End-camera restores shared pipeline distance");
                Assert.Greater(Changed(fixedRange.GetPixels32(),automatic.GetPixels32(),12),150,"Far visible caster must produce actual rendered shadows");
                var clipping=new Plane[6];float expected=VisibleShadowCoverage.RequiredDistance(camera,root.GetComponentsInChildren<Renderer>(),lighting.shadowDistance,lighting.shadowCoveragePadding,clipping);
                var otherLayer=GameObject.CreatePrimitive(PrimitiveType.Cube);otherLayer.transform.SetParent(root.transform);otherLayer.transform.position=camera.transform.position+camera.transform.forward*260;otherLayer.layer=9;
                Assert.AreEqual(expected,VisibleShadowCoverage.RequiredDistance(camera,root.GetComponentsInChildren<Renderer>(),lighting.shadowDistance,lighting.shadowCoveragePadding,clipping),"Other split-screen layer does not affect coverage");
                otherLayer.layer=8;Assert.Greater(VisibleShadowCoverage.RequiredDistance(camera,root.GetComponentsInChildren<Renderer>(),lighting.shadowDistance,lighting.shadowCoveragePadding,clipping),expected);
                otherLayer.SetActive(false);Assert.AreEqual(expected,VisibleShadowCoverage.RequiredDistance(camera,root.GetComponentsInChildren<Renderer>(true),lighting.shadowDistance,lighting.shadowCoveragePadding,clipping),"Hidden objects do not affect coverage");
                camera.transform.position=new Vector3(0,140,-140);for(int i=0;i<5;i++)yield return null;
                Assert.Greater(coverage.LastRequiredDistance,expected,"Coverage tracks camera movement/zoom");
                coverage.enabled=false;pipeline.Update();Assert.AreEqual(lighting.shadowDistance,pipeline.FindProperty("m_ShadowDistance").floatValue);
                var output=Environment.GetEnvironmentVariable("SPACEWARS_LIGHTING_EVIDENCE");if(!string.IsNullOrEmpty(output)){Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,"far-visible-fixed-range.png"),fixedRange.EncodeToPNG());File.WriteAllBytes(Path.Combine(output,"far-visible-auto-coverage.png"),automatic.EncodeToPNG());File.WriteAllText(Path.Combine(output,"visible-coverage.txt"),"fixed minimum="+lighting.shadowDistance+"; automatic initial="+expected+"; moved camera="+coverage.LastRequiredDistance+"; separate layer and inactive bounds excluded; shared pipeline restored");}
            }
            finally{coverage.enabled=false;camera.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);if(fixedRange)UnityEngine.Object.Destroy(fixedRange);if(automatic)UnityEngine.Object.Destroy(automatic);UnityEngine.Object.Destroy(root);UnityEngine.Object.Destroy(material);}
#else
            Assert.Ignore("Editor real-render regression");yield break;
#endif
        }
        [UnityTest] public IEnumerator CaptureMatchedMapsAndExistingActorsWithoutAddingDetails()
        {
            var output=Environment.GetEnvironmentVariable("SPACEWARS_LIGHTING_EVIDENCE");
            if(string.IsNullOrEmpty(output))output=Path.GetFullPath("../.local/industrial-lighting/captures");Directory.CreateDirectory(output);
#if UNITY_EDITOR
            var baseline=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Spacewars/Tests/Fixtures/IndustrialLighting/Baseline.shader");Assert.NotNull(baseline);
#else
            Assert.Ignore("Editor-only exact baseline shader comparison; no Player acceptance claimed.");yield break;
#endif
#if UNITY_EDITOR
            var candidate=Resources.Load<Shader>("TerritoryFog");var lighting=IndustrialLightingProfile.Load();
            var pipeline=new SerializedObject(GraphicsSettings.currentRenderPipeline);var softShadows=pipeline.FindProperty("m_SoftShadowsSupported");bool originalSoftShadows=softShadows.boolValue;
            try {
            foreach(bool foundry in new[]{true,false})
            {
                var profile=foundry?FoundryGreybox.LoadProfile():PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));
                var root=new GameObject("Matched industrial map preview");var world=new PlayableWorld(root.transform,profile);world.InspectionReveal();
                var site=TerritoryRules.Sites(profile)[0];var focus=world.Point(site.Position);
                var headquarters=world.Building(9001,"Headquarters",true);headquarters.Root.position=focus;world.FaceBuilding(headquarters);
                var tank=world.Tank(9002,true);tank.Root.position=world.Point(new NavPoint(site.Position.X+6,site.Position.Z-4));
                var enemy=world.Tank(9003,false);enemy.Root.position=world.Point(new NavPoint(site.Position.X-6,site.Position.Z-4));
                var materials=root.GetComponentsInChildren<MeshRenderer>().SelectMany(r=>r.sharedMaterials).Where(m=>m.shader==candidate).Distinct().ToArray();
                int renderers=root.GetComponentsInChildren<Renderer>().Length;int vertices=root.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.vertexCount);
                var sun=new GameObject("Matched sun").AddComponent<Light>();sun.transform.SetParent(root.transform);
                var target=new RenderTexture(1600,1000,24);target.Create();var camera=Camera(root.transform,target);
                var coverage=camera.gameObject.AddComponent<VisibleShadowCoverage>();coverage.Configure(root.transform,lighting);coverage.enabled=false;
                try
                {
                    foreach(float zoom in new[]{18f,32f,0f})
                    {
                        camera.orthographic=zoom==0;
                        if(zoom==0){camera.orthographicSize=(float)profile.CameraOrthoSize;camera.transform.position=focus+new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);}
                        else camera.transform.position=focus+new Vector3(0,zoom*1.35f,-zoom*.72f);
                        camera.transform.LookAt(focus);
                        Color32[] before=null;
                        foreach(string variant in new[]{"before","after","after-no-shadows"})
                        {
                            coverage.enabled=variant!="before";
                            pipeline.Update();softShadows.boolValue=variant!="before";pipeline.ApplyModifiedPropertiesWithoutUndo();
                            foreach(var m in materials){m.shader=variant=="before"?baseline:candidate;if(variant!="before")lighting.ApplyMaterial(m);}
                            if(variant=="before")
                            {pipeline.FindProperty("m_ShadowDistance").floatValue=50;pipeline.ApplyModifiedPropertiesWithoutUndo();RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.63f,.69f,.73f);sun.type=LightType.Directional;sun.color=Color.white;sun.intensity=1.4f;sun.shadowStrength=1;sun.transform.rotation=Quaternion.Euler(48,-30,0);sun.shadows=LightShadows.Soft;}
                            else {lighting.ApplyScene(sun);sun.shadows=variant=="after"?LightShadows.Soft:LightShadows.None;}
                            for(int i=0;i<5;i++)yield return null;
                            var pixels=Read(target);
                            try
                            {
                                var colors=pixels.GetPixels32();Assert.Less(colors.Count(c=>c.r>200&&c.b>200&&c.g<40),16,"No shader error pink");
                                Assert.Greater(colors.Where((_,i)=>i%173==0).Select(c=>((int)c.r<<16)|((int)c.g<<8)|c.b).Distinct().Count(),32,"Real rendered scene");
                                if(variant=="before")before=colors;else Assert.Greater(Changed(before,colors,12),colors.Length/20,"Candidate must visibly change the actual scene");
                                File.WriteAllBytes(Path.Combine(output,(foundry?"foundry":"crossings")+(zoom==0?"-gameplay":"-zoom"+zoom)+"-"+variant+".png"),pixels.EncodeToPNG());
                            }finally{UnityEngine.Object.Destroy(pixels);}
                            Assert.AreEqual(renderers,root.GetComponentsInChildren<Renderer>().Length);Assert.AreEqual(vertices,root.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.vertexCount));
                        }
                    }
                    File.WriteAllText(Path.Combine(output,(foundry?"foundry":"crossings")+"-scene.txt"),"baseline=71b1e1d5; preview actors only, no simulation seed; inspection FOV48 zoom18/32; gameplay orthographic from named PlayableProfile; 1600x1000; renderers="+renderers+" vertices="+vertices+"; profile="+lighting.id+"@"+lighting.revision+"; Unity="+Application.unityVersion+"; GPU="+SystemInfo.graphicsDeviceName);
                }
                finally{camera.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);world.Dispose();UnityEngine.Object.Destroy(root);}
                yield return null;
            }
            } finally {softShadows.boolValue=originalSoftShadows;pipeline.ApplyModifiedPropertiesWithoutUndo();}
#endif
        }
    }
}
