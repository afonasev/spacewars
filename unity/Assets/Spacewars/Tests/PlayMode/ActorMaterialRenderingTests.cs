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
    public sealed class ActorMaterialRenderingTests
    {
        private Color ambient;private AmbientMode mode;private float audio,shadowDistance;
        [SetUp] public void Setup(){ambient=RenderSettings.ambientLight;mode=RenderSettings.ambientMode;audio=AudioListener.volume;AudioListener.volume=0;shadowDistance=((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).shadowDistance;}
        [TearDown] public void Restore(){RenderSettings.ambientLight=ambient;RenderSettings.ambientMode=mode;AudioListener.volume=audio;((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).shadowDistance=shadowDistance;}
        [TestCase("restrained")][TestCase("luminous")] public void NamedControlsValidateEveryRange(string variant)
        {
            var p=ActorMaterialProfile.Load(variant);p.Validate();Assert.AreEqual(variant,p.variant);
            var parameters=p.Parameters().ToArray();Assert.AreEqual(11,parameters.Length);Assert.AreEqual(parameters.Length,parameters.Select(x=>x.Path).Distinct().Count());
            foreach(var field in parameters){float original=field.Read();field.Write(field.Maximum+field.Step);Assert.Throws<ArgumentException>(()=>p.Validate(),field.Path);field.Write(float.NaN);Assert.Throws<ArgumentException>(()=>p.Validate());field.Write(original);}
            Assert.Greater(ActorMaterialProfile.Load("luminous").teamEmission,ActorMaterialProfile.Load().teamEmission);
        }
        [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Explorer)][TestCase(PlayableEntityKind.Shkval)]
        public void UpgradeAndOwnerPaintKeepFinishAndSharedMaterials(PlayableEntityKind kind)
        {
            var root=new GameObject("Actor finish ownership test");
            try{
                using var world=new PlayableWorld(root.transform,PlayableProfile.Default);var finish=world.MaterialFinish;
                var actor=world.Tank(1,true,kind);var enemy=world.Tank(2,false,kind);
                foreach(bool upgraded in new[]{false,true}){
                    if(upgraded)world.UpdateResearchModel(actor,new PlayableEntitySnapshot(1,PlayableOwner.Player,kind,default,100,false,0,0,0,upgraded:true));
                    var owner=new Color(.75f,.25f,.125f);PlayableWorld.PaintOwner(actor,owner);
                    foreach(var r in actor.Hull.GetComponentsInChildren<Renderer>())for(int slot=0;slot<r.sharedMaterials.Length;slot++){
                        var m=r.sharedMaterials[slot];var block=new MaterialPropertyBlock();r.GetPropertyBlock(block,slot);
                        if(m.name.EndsWith("team-primary")){Assert.AreEqual(StoredColor("_BaseColor",owner),block.GetColor("_BaseColor"));Assert.AreEqual(finish.armorSmoothness,block.GetFloat("_Smoothness"));Assert.AreEqual(finish.armorMetallic,block.GetFloat("_Metallic"));}
                        if(m.name.EndsWith("team-emissive"))Assert.AreEqual(StoredColor("_EmissionColor",owner*finish.teamEmission),block.GetColor("_EmissionColor"));
                        if(m.name.EndsWith("chassis-emissive"))Assert.AreEqual(StoredColor("_EmissionColor",m.GetColor("_EmissionColor")*(float)PlayableProfile.Default.TankChassisGlowIntensity*finish.emissionGain),block.GetColor("_EmissionColor"));
                    }
                }
                var team=enemy.Hull.GetComponentsInChildren<Renderer>().First(r=>r.sharedMaterial.name.EndsWith("team-primary"));
                Assert.AreEqual(1f,team.sharedMaterial.GetFloat("_Smoothness"),"Authored material asset remains unchanged");
            }finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        // Compare the exact engine property-block round trip (SetColor/GetColor includes color-space conversion).
        private static Color StoredColor(string property,Color color){var block=new MaterialPropertyBlock();block.SetColor(property,color);return block.GetColor(property);}
        private static Camera View(Transform root,RenderTexture target)
        {var c=new GameObject("Material preview camera").AddComponent<Camera>();c.transform.SetParent(root);c.targetTexture=target;c.fieldOfView=48;c.nearClipPlane=.1f;c.farClipPlane=700;c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=new Color(.075f,.10f,.13f);return c;}
        private static Texture2D Read(RenderTexture target)
        {var old=RenderTexture.active;try{RenderTexture.active=target;var t=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,target.width,target.height),0,0);t.Apply();return t;}finally{RenderTexture.active=old;}}
        private static int Changed(Color32[] a,Color32[] b,int tolerance=3)
        {int n=0;for(int i=0;i<a.Length;i++)if(Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b)>tolerance)n++;return n;}
        private static string Output(){var p=Environment.GetEnvironmentVariable("SPACEWARS_MATERIAL_EVIDENCE")??Path.GetFullPath("../.local/actor-material/captures");Directory.CreateDirectory(p);return p;}
        private static PlayableProfile Map(bool foundry)=>foundry?FoundryGreybox.LoadProfile():PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));
        [UnityTest] public IEnumerator CaptureMatchedMaterialAlternativesWithoutExtraGeometry()
        {
            var output=Output();var light=IndustrialLightingProfile.Load();
            foreach(bool foundry in new[]{false,true}){
                var profile=Map(foundry);var root=new GameObject("Material alternatives");var world=new PlayableWorld(root.transform,profile);world.InspectionReveal();
                var focus=world.Point(TerritoryRules.Sites(profile)[0].Position);var target=new RenderTexture(1600,1000,24);target.Create();var camera=View(root.transform,target);
                var glow=camera.gameObject.AddComponent<ActorGlowCamera>();var sun=new GameObject("Accepted industrial sun").AddComponent<Light>();sun.transform.SetParent(root.transform);light.ApplyScene(sun);sun.shadows=LightShadows.Soft;
                camera.gameObject.AddComponent<VisibleShadowCoverage>().Configure(root.transform,light);
                try{
                    foreach(float zoom in new[]{0f,14f}){
                        camera.orthographic=zoom==0;camera.orthographicSize=(float)profile.CameraOrthoSize;
                        camera.transform.position=focus+(zoom==0?new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ):new Vector3(0,zoom*1.35f,-zoom*.72f));camera.transform.LookAt(focus);
                        Color32[] before=null,restrained=null;int renderers=-1,vertices=-1;
                        foreach(string variant in new[]{"baseline","restrained","restrained-nobloom","luminous"}){
                            world.Clear();world.MaterialFinish=variant=="baseline"?null:ActorMaterialProfile.Load(variant=="luminous"?"luminous":"restrained");
                            var hq=world.Building(1,"Headquarters",true);hq.Root.position=focus;world.FaceBuilding(hq,camera);
                            var ally=world.Tank(2,true);ally.Root.position=world.Point(new NavPoint(TerritoryRules.Sites(profile)[0].Position.X+5,TerritoryRules.Sites(profile)[0].Position.Z-4));
                            var enemy=world.Tank(3,false);enemy.Root.position=world.Point(new NavPoint(TerritoryRules.Sites(profile)[0].Position.X-5,TerritoryRules.Sites(profile)[0].Position.Z-4));
                            for(int i=0;i<2;i++)yield return null;
                            if(variant=="baseline")glow.enabled=false;else {glow.enabled=true;glow.Configure(world.MaterialFinish);if(variant=="restrained-nobloom"){glow.Profile.TryGet<Bloom>(out var disabledBloom);disabledBloom.intensity.Override(0);}}
                            for(int i=0;i<8;i++)yield return null;
                            int count=root.GetComponentsInChildren<Renderer>().Length,verts=root.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.vertexCount);
                            if(renderers<0){renderers=count;vertices=verts;}else{Assert.AreEqual(renderers,count);Assert.AreEqual(vertices,verts);}
                            var image=Read(target);try{
                                var pixels=image.GetPixels32();Assert.Less(pixels.Count(c=>c.r>200&&c.b>200&&c.g<40),16,"No error pink");
                                if(variant=="baseline")before=pixels;else if(variant=="restrained"){restrained=pixels;Assert.Greater(Changed(before,pixels),100,"Material finish changes actual scene");}
                                else if(variant=="restrained-nobloom")Assert.Greater(Changed(restrained,pixels,0),10,"Existing lamps generate real bloom");
                                else Assert.Greater(Changed(restrained,pixels),30,"Two candidates visibly differ");
                                File.WriteAllBytes(Path.Combine(output,(foundry?"foundry":"crossings")+(zoom==0?"-gameplay":"-close")+"-"+variant+".png"),image.EncodeToPNG());
                            }finally{UnityEngine.Object.Destroy(image);}
                        }
                        File.AppendAllText(Path.Combine(output,"scenes.txt"),(foundry?"foundry":"crossings")+" zoom="+zoom+" renderers="+renderers+" vertices="+vertices+"; staged HQ and two tanks; no simulation seed; accepted lighting="+light.id+"@"+light.revision+"; profiles actor-industrial-finish-v1@1; Unity="+Application.unityVersion+" GPU="+SystemInfo.graphicsDeviceName+"\n");
                    }
                }finally{camera.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);world.Dispose();UnityEngine.Object.Destroy(root);}
                yield return null;
            }
        }
        [UnityTest] public IEnumerator CaptureExistingRoleGallery()
        {
            var root=new GameObject("Existing material role gallery");var world=new PlayableWorld(root.transform,PlayableProfile.Default);world.InspectionReveal();
            var target=new RenderTexture(1600,1000,24);target.Create();var camera=View(root.transform,target);camera.orthographic=true;camera.orthographicSize=8;
            camera.transform.position=new Vector3(0,14,-12);camera.transform.LookAt(new Vector3(0,1,0));var glow=camera.gameObject.AddComponent<ActorGlowCamera>();
            var sun=new GameObject("Accepted gallery sun").AddComponent<Light>();sun.transform.SetParent(root.transform);var lighting=IndustrialLightingProfile.Load();lighting.ApplyScene(sun);sun.shadows=LightShadows.Soft;camera.gameObject.AddComponent<VisibleShadowCoverage>().Configure(root.transform,lighting);
            int count=-1,vertices=-1;Color32[] baseline=null;
            try{
                foreach(string variant in new[]{"baseline","restrained","luminous"}){
                    world.Clear();world.MaterialFinish=variant=="baseline"?null:ActorMaterialProfile.Load(variant);
                    string[] kinds={"Headquarters","ScientificCenter","Refinery","Mine"};
                    for(int i=0;i<kinds.Length;i++){var actor=world.Building(10+i,kinds[i],true);actor.Root.position=new Vector3((i%2==0?-1:1)*4,0,i<2?0:6);world.FaceBuilding(actor,camera);}
                    var units=new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Shkval};
                    for(int i=0;i<3;i++){var actor=world.Tank(20+i,true,units[i]);world.UpdateResearchModel(actor,new PlayableEntitySnapshot(20+i,PlayableOwner.Player,units[i],default,100,false,0,0,0,upgraded:true));actor.Root.position=new Vector3((i-1)*4,0,-5);actor.Hull.localRotation=Quaternion.Euler(0,150,0);}
                    yield return null;yield return null;if(variant=="baseline")glow.enabled=false;else{glow.enabled=true;glow.Configure(world.MaterialFinish);}
                    for(int frame=0;frame<8;frame++)yield return null;
                    int next=root.GetComponentsInChildren<Renderer>().Length,nextVertices=root.GetComponentsInChildren<MeshFilter>().Sum(x=>x.sharedMesh.vertexCount);
                    if(count<0){count=next;vertices=nextVertices;}else{Assert.AreEqual(count,next);Assert.AreEqual(vertices,nextVertices);}
                    var image=Read(target);try{if(variant=="baseline")baseline=image.GetPixels32();else Assert.Greater(Changed(baseline,image.GetPixels32()),100);File.WriteAllBytes(Path.Combine(Output(),"gallery-"+variant+".png"),image.EncodeToPNG());}finally{UnityEngine.Object.Destroy(image);}
                }
                File.AppendAllText(Path.Combine(Output(),"scenes.txt"),"gallery: existing HQ/science/refinery/mine and upgraded tank/explorer/shkval; no simulation seed; orthographic8; renderers="+count+" vertices="+vertices+" unchanged across variants\n");
            }finally{camera.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);world.Dispose();UnityEngine.Object.Destroy(root);}
        }
        [UnityTest] public IEnumerator CameraVolumesAreIsolatedAndHiddenActorsEmitNothing()
        {
            var root=new GameObject("Per-camera emission isolation");var world=new PlayableWorld(root.transform,PlayableProfile.Default);world.InspectionReveal();
            var actor=world.Building(1,"ScientificCenter",false);actor.Root.position=Vector3.zero;
            var targets=new[]{new RenderTexture(640,480,24),new RenderTexture(640,480,24)};foreach(var t in targets)t.Create();
            var cameras=new[]{View(root.transform,targets[0]),View(root.transform,targets[1])};var glows=new ActorGlowCamera[2];var retained=new VolumeProfile[2];
            var sun=new GameObject("Isolation sun").AddComponent<Light>();sun.transform.SetParent(root.transform);IndustrialLightingProfile.Load().ApplyScene(sun);
            try{
                for(int i=0;i<2;i++){cameras[i].transform.position=new Vector3(0,5,-5);cameras[i].transform.LookAt(Vector3.zero);glows[i]=cameras[i].gameObject.AddComponent<ActorGlowCamera>();glows[i].Configure(ActorMaterialProfile.Load(),ActorGlowCamera.FirstLocalVolumeLayer+i);retained[i]=glows[i].Profile;Assert.True(retained[i].TryGet<Tonemapping>(out var tone));Assert.AreEqual(TonemappingMode.None,tone.mode.value);Assert.AreEqual(1<<(30+i),cameras[i].GetUniversalAdditionalCameraData().volumeLayerMask.value);}
                for(int n=0;n<8;n++)yield return null;
                var a=Read(targets[0]);var b=Read(targets[1]);
                try{
                    glows[1].Profile.TryGet<Bloom>(out var bloom);bloom.intensity.Override(1);
                    for(int n=0;n<8;n++)yield return null;
                    var a2=Read(targets[0]);var b2=Read(targets[1]);try{Assert.Zero(Changed(a.GetPixels32(),a2.GetPixels32(),0),"Other camera bloom must not leak");Assert.Greater(Changed(b.GetPixels32(),b2.GetPixels32(),0),20,"Independent bloom must actually render");}finally{UnityEngine.Object.Destroy(a2);UnityEngine.Object.Destroy(b2);}
                }finally{UnityEngine.Object.Destroy(a);UnityEngine.Object.Destroy(b);}
                // Gameplay vision removes concealed enemy roots; verify zero glow survives a hidden renderer.
                actor.Root.gameObject.SetActive(false);for(int n=0;n<8;n++)yield return null;var hidden=Read(targets[1]);
                glows[1].Profile.TryGet<Bloom>(out var hiddenBloom);hiddenBloom.intensity.Override(0);for(int n=0;n<8;n++)yield return null;var noGlow=Read(targets[1]);
                try{Assert.Zero(Changed(hidden.GetPixels32(),noGlow.GetPixels32(),0),"Concealed enemy has no luminous silhouette");File.WriteAllBytes(Path.Combine(Output(),"concealed-enemy.png"),hidden.EncodeToPNG());}finally{UnityEngine.Object.Destroy(hidden);UnityEngine.Object.Destroy(noGlow);}
                glows[0].enabled=false;Assert.False(cameras[0].GetUniversalAdditionalCameraData().renderPostProcessing);Assert.AreEqual(1,cameras[0].GetUniversalAdditionalCameraData().volumeLayerMask.value);glows[0].enabled=true;Assert.True(cameras[0].GetUniversalAdditionalCameraData().renderPostProcessing);
                foreach(var g in glows)UnityEngine.Object.Destroy(g);yield return null;yield return null;
                foreach(var p in retained)Assert.True(p==null,"Owned volume profile disposed");
            }finally{for(int i=0;i<2;i++){cameras[i].targetTexture=null;targets[i].Release();UnityEngine.Object.Destroy(targets[i]);}world.Dispose();UnityEngine.Object.Destroy(root);}
        }
    }
}
