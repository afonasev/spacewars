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

namespace Spacewars.Tests.PlayMode
{
    public sealed class CrossingsRockShadowRenderingTests
    {
        [UnityTest] public IEnumerator SolidRockShadowsCloseLightLeaksAndRespectFog()
        {
            var profile=PlayableProfile.ThreeCrossingsDefault;
            var lighting=IndustrialLightingProfile.Load(profile.AuthoredMap.Id);
            var root=new GameObject("Rock shadow regression");
            var target=new RenderTexture(1600,1000,24);target.Create();
            var pixels=new Texture2D(1600,1000,TextureFormat.RGB24,false);
            var ambient=RenderSettings.ambientLight;var mode=RenderSettings.ambientMode;
            var pipeline=(UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;var distance=pipeline.shadowDistance;
            var output=Environment.GetEnvironmentVariable("SPACEWARS_ROCK_SHADOW_EVIDENCE")??Path.Combine(Application.dataPath,"..","testResults","rock-shadow");
            Directory.CreateDirectory(output);
            using(var world=new PlayableWorld(root.transform,profile))
            try
            {
                var renderers=root.GetComponentsInChildren<MeshRenderer>();var block=new MaterialPropertyBlock();
                var rocks=renderers.Where(r=>{r.GetPropertyBlock(block);return block.GetFloat("_SurfaceRole")==4 && r.GetComponent<MeshFilter>().sharedMesh.name=="Broad passage-readable rock ledges";}).ToArray();
                Assert.IsNotEmpty(rocks);
                Assert.IsTrue(rocks.All(r=>r.shadowCastingMode==ShadowCastingMode.TwoSided),"Production rock casters close hard-normal facet seams");
                Assert.IsTrue(renderers.Except(rocks).All(r=>r.shadowCastingMode!=ShadowCastingMode.TwoSided),"Fix is confined to rock casters");
                int vertices=root.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.vertexCount),colliders=root.GetComponentsInChildren<Collider>().Length;
                var sun=new GameObject("Saved map sun").AddComponent<Light>();sun.transform.SetParent(root.transform);
                var camera=new GameObject("Gameplay camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);
                camera.orthographic=true;camera.orthographicSize=(float)profile.CameraOrthoSize;camera.nearClipPlane=.1f;camera.farClipPlane=700;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.075f,.10f,.13f);camera.targetTexture=target;
                var focus=world.Point(new NavPoint(40,0));camera.transform.position=focus+new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);camera.transform.LookAt(focus);
                camera.gameObject.AddComponent<VisibleShadowCoverage>().Configure(root.transform,lighting);
                camera.gameObject.AddComponent<ContactOcclusionCamera>().Configure(ContactOcclusionProfile.Load());
                camera.gameObject.AddComponent<ActorGlowCamera>().Configure(ActorMaterialProfile.Load());
                File.WriteAllText(Path.Combine(output,"metrics.txt"),$"{world.SurfaceRevision}; {lighting.id}@{lighting.revision}; normal={profile.CameraOrthoSize}; production east mine; 1600x1000; no simulation seed; Editor only.\n");
                foreach(bool low in new[]{false,true})
                {
                    lighting.ApplyScene(sun);if(low)sun.transform.rotation=Quaternion.Euler(20,125,0);
                    Color32[] before=null,fixedImage=null,hidden=null;
                    foreach(var variant in new[]{"before","fixed","no-shadow","hidden-before","hidden-fixed"})
                    {
                        foreach(var rock in rocks)rock.shadowCastingMode=variant.EndsWith("before")?ShadowCastingMode.On:ShadowCastingMode.TwoSided;
                        sun.shadows=variant=="no-shadow"?LightShadows.None:LightShadows.Soft;
                        if(variant.StartsWith("hidden"))world.InspectionHide();else world.InspectionReveal();
                        for(int f=0;f<8;f++)yield return null;
                        camera.Render();var previous=RenderTexture.active;
                        try{RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();}
                        finally{RenderTexture.active=previous;}
                        var colors=pixels.GetPixels32();
                        Assert.Less(colors.Count(p=>p.r>200&&p.b>200&&p.g<40),16,"No shader error pink");
                        if(variant=="before")before=colors;
                        if(variant=="fixed")fixedImage=colors;
                        if(variant=="hidden-before")hidden=colors;
                        if(variant=="hidden-fixed")Assert.IsTrue(hidden.SequenceEqual(colors),"Fully concealed output cannot reveal the caster correction");
                        if(variant=="no-shadow")
                        {
                            int closedLeaks=0,retainedShadow=0;
                            for(int i=0;i<colors.Length;i++)
                            {
                                int lit=colors[i].r+colors[i].g+colors[i].b,old=before[i].r+before[i].g+before[i].b,now=fixedImage[i].r+fixedImage[i].g+fixedImage[i].b;
                                if(lit-now>24)retainedShadow++;
                                if(lit-now>24&&old-now>12)closedLeaks++;
                            }
                            Assert.Greater(retainedShadow,1000,"Real cast shadows are retained, not disabled");
                            Assert.Greater(closedLeaks,200,"Two-sided rock casters close visible leaks in the old shadow footprint");
                            File.AppendAllText(Path.Combine(output,"metrics.txt"),$"lowSun={low}: retainedShadowPixels={retainedShadow}; closedLeakPixels={closedLeaks}\n");
                        }
                        if(variant=="before"||variant=="fixed")File.WriteAllBytes(Path.Combine(output,(low?"low-":"standard-")+variant+".png"),pixels.EncodeToPNG());
                    }
                }
                Assert.AreEqual(vertices,root.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.vertexCount));
                Assert.AreEqual(colliders,root.GetComponentsInChildren<Collider>().Length);
            }
            finally
            {
                RenderSettings.ambientLight=ambient;RenderSettings.ambientMode=mode;pipeline.shadowDistance=distance;
                UnityEngine.Object.Destroy(root);UnityEngine.Object.Destroy(pixels);target.Release();UnityEngine.Object.Destroy(target);
            }
        }
    }
}
