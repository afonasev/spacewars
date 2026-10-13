using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Spacewars.Presentation;
using Spacewars.Simulation;
#if UNITY_EDITOR
using UnityEditor;
#endif
namespace Spacewars.Tests.PlayMode
{
    public sealed class ContactOcclusionRenderingTests
    {
        private Color ambient;private AmbientMode mode;private float audio,shadowDistance;
        [SetUp] public void Setup(){ambient=RenderSettings.ambientLight;mode=RenderSettings.ambientMode;audio=AudioListener.volume;AudioListener.volume=0;shadowDistance=((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).shadowDistance;}
        [TearDown] public void Restore(){RenderSettings.ambientLight=ambient;RenderSettings.ambientMode=mode;AudioListener.volume=audio;((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).shadowDistance=shadowDistance;}
        [Test] public void ProfileAndInstalledAdapterValidate()
        {
            var p=ContactOcclusionProfile.Load();p.Validate();var parameters=p.Parameters().ToArray();Assert.AreEqual(3,parameters.Length);
            foreach(var field in parameters){float original=field.Read();field.Write(field.Maximum+field.Step);Assert.Throws<ArgumentException>(()=>p.Validate());field.Write(float.NaN);Assert.Throws<ArgumentException>(()=>p.Validate());field.Write(original);}
#if UNITY_EDITOR
            var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Spacewars/Content/FoundationRenderer.asset");
            var feature=renderer.rendererFeatures.OfType<ContactOcclusionFeature>().Single();var serialized=new SerializedObject(feature);
            Assert.False(serialized.FindProperty("m_Settings.AfterOpaque").boolValue);
            Assert.Zero(serialized.FindProperty("m_Settings.DirectLightingStrength").floatValue);
            Assert.AreEqual(0,serialized.FindProperty("m_Settings.Source").enumValueIndex);
            Assert.AreEqual(1,serialized.FindProperty("m_Settings.AOMethod").enumValueIndex);
            Assert.Less(renderer.rendererFeatures.IndexOf(feature),renderer.rendererFeatures.FindIndex(f=>f is OfflineCameraMirrorFeature));
#endif
        }
        private static Camera View(Transform root,RenderTexture target)
        {var c=new GameObject("Material preview camera").AddComponent<Camera>();c.transform.SetParent(root);c.targetTexture=target;c.fieldOfView=48;c.nearClipPlane=.1f;c.farClipPlane=700;c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=new Color(.075f,.10f,.13f);return c;}
        private static Texture2D Read(RenderTexture target)
        {var old=RenderTexture.active;try{RenderTexture.active=target;var t=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,target.width,target.height),0,0);t.Apply();return t;}finally{RenderTexture.active=old;}}
        private static int Changed(Color32[] a,Color32[] b,int tolerance=3)
        {int n=0;for(int i=0;i<a.Length;i++)if(Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b)>tolerance)n++;return n;}
        private static string Output(){var p=Environment.GetEnvironmentVariable("SPACEWARS_SSAO_EVIDENCE")??Path.GetFullPath("../.local/contact-ssao/captures");Directory.CreateDirectory(p);return p;}
        private static PlayableProfile Map(bool foundry)=>foundry?FoundryGreybox.LoadProfile():PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));
        [UnityTest] public IEnumerator CaptureMatchedContactOcclusion()
        {
            var output=Output();float previousScale=Time.timeScale;Time.timeScale=0;try {
            foreach(bool foundry in new[]{false,true}){
                var profile=Map(foundry);var light=IndustrialLightingProfile.Load(profile.AuthoredMap?.Id);var root=new GameObject("Contact SSAO comparison");var world=new PlayableWorld(root.transform,profile);world.InspectionReveal();
                var focus=world.Point(TerritoryRules.Sites(profile)[0].Position);var target=new RenderTexture(1600,1000,24);target.Create();var camera=View(root.transform,target);
                var glow=camera.gameObject.AddComponent<ActorGlowCamera>();glow.Configure(ActorMaterialProfile.Load());var ao=camera.gameObject.AddComponent<ContactOcclusionCamera>();ao.Configure(ContactOcclusionProfile.Load());var sun=new GameObject("Accepted industrial sun").AddComponent<Light>();sun.transform.SetParent(root.transform);light.ApplyScene(sun);sun.shadows=LightShadows.Soft;
                camera.gameObject.AddComponent<VisibleShadowCoverage>().Configure(root.transform,light);
                try{
                    foreach(float zoom in new[]{0f,7f}){
                        camera.orthographic=zoom==0;camera.orthographicSize=(float)profile.CameraOrthoSize;
                        var cameraFocus=focus+(zoom==0?Vector3.zero:new Vector3(0,0,-2));
                        camera.transform.position=cameraFocus+(zoom==0?new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ):new Vector3(0,zoom*1.35f,-zoom*.72f));camera.transform.LookAt(cameraFocus);
                        Color32[] before=null;int renderers=-1,vertices=-1;
                        foreach(string variant in new[]{"before","candidate"}){
                            world.Clear();world.MaterialFinish=ActorMaterialProfile.Load();
                            var hq=world.Building(1,"Headquarters",true);hq.Root.position=focus;world.FaceBuilding(hq,camera);
                            var ally=world.Tank(2,true);ally.Root.position=world.Point(new NavPoint(TerritoryRules.Sites(profile)[0].Position.X+5,TerritoryRules.Sites(profile)[0].Position.Z-4));
                            var enemy=world.Tank(3,false);enemy.Root.position=world.Point(new NavPoint(TerritoryRules.Sites(profile)[0].Position.X-5,TerritoryRules.Sites(profile)[0].Position.Z-4));
                            for(int i=0;i<2;i++)yield return null;
                            ao.enabled=variant=="candidate";
                            for(int i=0;i<8;i++)yield return null;
                            int count=root.GetComponentsInChildren<Renderer>().Length,verts=root.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.vertexCount);
                            if(renderers<0){renderers=count;vertices=verts;}else{Assert.AreEqual(renderers,count);Assert.AreEqual(vertices,verts);}
                            var image=Read(target);try{
                                var pixels=image.GetPixels32();Assert.Less(pixels.Count(c=>c.r>200&&c.b>200&&c.g<40),16,"No error pink");
                                if(variant=="before")before=pixels;else Assert.Greater(Changed(before,pixels),100,"SSAO changes actual contacts");
                                File.WriteAllBytes(Path.Combine(output,(foundry?"foundry":"crossings")+(zoom==0?"-gameplay":"-close")+"-"+variant+".png"),image.EncodeToPNG());
                            }finally{UnityEngine.Object.Destroy(image);}
                        }
                        File.AppendAllText(Path.Combine(output,"scenes.txt"),(foundry?"foundry":"crossings")+" zoom="+zoom+" renderers="+renderers+" vertices="+vertices+"; staged HQ and two tanks; no simulation seed; accepted lighting="+light.id+"@"+light.revision+"; profiles actor-industrial-finish-v1/restrained@1, contact-occlusion-v1@2; Unity="+Application.unityVersion+" GPU="+SystemInfo.graphicsDeviceName+"\n");
                    }
                }finally{camera.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);world.Dispose();UnityEngine.Object.Destroy(root);}
                yield return null;
            }
            }finally{Time.timeScale=previousScale;}
        }
        [UnityTest] public IEnumerator CameraContactAndMirrorIsolation()
        {
            var root=new GameObject("Per-camera emission isolation");var world=new PlayableWorld(root.transform,PlayableProfile.Default);world.InspectionReveal();
            var actor=world.Building(1,"ScientificCenter",false);actor.Root.position=Vector3.zero;
            var targets=new[]{new RenderTexture(640,480,24),new RenderTexture(640,480,24)};foreach(var t in targets)t.Create();
            var cameras=new[]{View(root.transform,targets[0]),View(root.transform,targets[1])};var glows=new ActorGlowCamera[2];var retained=new VolumeProfile[2];
            var sun=new GameObject("Isolation sun").AddComponent<Light>();sun.transform.SetParent(root.transform);IndustrialLightingProfile.Load().ApplyScene(sun);
            try{
                for(int i=0;i<2;i++){cameras[i].transform.position=new Vector3(0,5,-5);cameras[i].transform.LookAt(Vector3.zero);glows[i]=cameras[i].gameObject.AddComponent<ActorGlowCamera>();glows[i].Configure(ActorMaterialProfile.Load(),ActorGlowCamera.FirstLocalVolumeLayer+i);retained[i]=glows[i].Profile;Assert.True(retained[i].TryGet<Tonemapping>(out var tone));Assert.AreEqual(TonemappingMode.None,tone.mode.value);Assert.AreEqual(1<<(30+i),cameras[i].GetUniversalAdditionalCameraData().volumeLayerMask.value);}
                for(int n=0;n<8;n++)yield return null;
                var controls=new ContactOcclusionCamera[2];for(int i=0;i<2;i++){controls[i]=cameras[i].gameObject.AddComponent<ContactOcclusionCamera>();controls[i].Configure(ContactOcclusionProfile.Load());OfflineCameraMirrorFeature.Register(cameras[i]);}
                for(int n=0;n<8;n++)yield return null;
                var a=Read(targets[0]);var b=Read(targets[1]);
                try{
                    controls[1].enabled=false;
                    for(int n=0;n<8;n++)yield return null;
                    var a2=Read(targets[0]);var b2=Read(targets[1]);try{Assert.Zero(Changed(a.GetPixels32(),a2.GetPixels32(),0),"Other camera AO must not leak");Assert.Greater(Changed(b.GetPixels32(),b2.GetPixels32(),0),20,"Independent mirrored camera AO must actually render");}finally{UnityEngine.Object.Destroy(a2);UnityEngine.Object.Destroy(b2);}
                }finally{UnityEngine.Object.Destroy(a);UnityEngine.Object.Destroy(b);}
                // Compare concealed with absent under identical AO/bloom; terrain AO is legitimately nonzero.
                controls[1].enabled=true;actor.Root.gameObject.SetActive(false);actor.Health.parent.gameObject.SetActive(false);for(int n=0;n<8;n++)yield return null;var hidden=Read(targets[1]);
                world.Clear();for(int n=0;n<8;n++)yield return null;var noGlow=Read(targets[1]);
                try{Assert.Zero(Changed(hidden.GetPixels32(),noGlow.GetPixels32(),0),"Concealed enemy has no AO or luminous silhouette");File.WriteAllBytes(Path.Combine(Output(),"concealed-enemy.png"),hidden.EncodeToPNG());}finally{UnityEngine.Object.Destroy(hidden);UnityEngine.Object.Destroy(noGlow);}
                glows[0].enabled=false;Assert.False(cameras[0].GetUniversalAdditionalCameraData().renderPostProcessing);Assert.AreEqual(1,cameras[0].GetUniversalAdditionalCameraData().volumeLayerMask.value);glows[0].enabled=true;Assert.True(cameras[0].GetUniversalAdditionalCameraData().renderPostProcessing);
                foreach(var g in glows)UnityEngine.Object.Destroy(g);yield return null;yield return null;
                foreach(var p in retained)Assert.True(p==null,"Owned volume profile disposed");
            }finally{for(int i=0;i<2;i++){OfflineCameraMirrorFeature.Unregister(cameras[i]);cameras[i].targetTexture=null;targets[i].Release();UnityEngine.Object.Destroy(targets[i]);}world.Dispose();UnityEngine.Object.Destroy(root);}
        }
    }
}
