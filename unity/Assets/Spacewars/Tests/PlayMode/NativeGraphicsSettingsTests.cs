using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
namespace Spacewars.Tests.PlayMode
{
    public sealed class NativeGraphicsSettingsTests
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        private readonly Dictionary<string,float> floats=new Dictionary<string,float>();private readonly Dictionary<string,int> ints=new Dictionary<string,int>();
        private readonly List<GameObject> hosts=new List<GameObject>();private readonly List<PanelSettings> panels=new List<PanelSettings>();
        private Color ambient;private AmbientMode ambientMode;private float shadowDistance;
        private static T Get<T>(object obj,string name)=>(T)obj.GetType().GetField(name,Flags).GetValue(obj);
        private static void Call(object obj,string name,params object[] values)=>obj.GetType().GetMethod(name,Flags).Invoke(obj,values);
        [SetUp] public void Setup()
        {
            Assert.IsNotEmpty(Environment.GetEnvironmentVariable("SPACEWARS_GRAPHICS_PREFS_NAMESPACE"),"QA must isolate device preferences");
            floats.Clear();ints.Clear();foreach(var key in NativeGraphicsSettings.PreferenceKeys){if(PlayerPrefs.HasKey(key)){if(key.EndsWith("Enabled"))ints[key]=PlayerPrefs.GetInt(key);else floats[key]=PlayerPrefs.GetFloat(key);}PlayerPrefs.DeleteKey(key);}PlayerPrefs.Save();
            ambient=RenderSettings.ambientLight;ambientMode=RenderSettings.ambientMode;shadowDistance=UniversalRenderPipeline.asset.shadowDistance;
        }
        [TearDown] public void Cleanup()
        {
            foreach(var host in hosts)if(host)UnityEngine.Object.DestroyImmediate(host);hosts.Clear();foreach(var panel in panels)if(panel)UnityEngine.Object.DestroyImmediate(panel);panels.Clear();
            foreach(var key in NativeGraphicsSettings.PreferenceKeys){if(ints.TryGetValue(key,out int i))PlayerPrefs.SetInt(key,i);else if(floats.TryGetValue(key,out float f))PlayerPrefs.SetFloat(key,f);else PlayerPrefs.DeleteKey(key);}PlayerPrefs.Save();
            RenderSettings.ambientLight=ambient;RenderSettings.ambientMode=ambientMode;UniversalRenderPipeline.asset.shadowDistance=shadowDistance;
        }
        private static IEnumerable<SurfaceParameter> Effective()=>CameraAntialiasingProfile.Load().Parameters().Concat(ContactOcclusionProfile.Load().Parameters()).Concat(ActorMaterialProfile.Load().Parameters()).Concat(IndustrialLightingProfile.Load().Parameters()).Concat(IndustrialLightingProfile.Load("three-crossings-greybox-v1").Parameters());
        private static void Legacy(string path,float value)=>PlayerPrefs.SetFloat(NativeGraphicsSettings.PreferenceKeys.Single(k=>k.EndsWith(path)),value);
        [Test] public void DeveloperMetadataStaysInLabAndLegacyPlayerOverridesAreIgnored()
        {
            var parameters=NativeGraphicsSettings.Parameters;Assert.AreEqual(45,parameters.Count);Assert.AreEqual(45,parameters.Select(p=>p.Path).Distinct().Count());
            var resources=new[]{"CameraAntialiasingProfile","ContactOcclusionProfile","ActorMaterialProfile","IndustrialLightingProfile","ThreeCrossingsLightingProfile"};var originals=resources.Select(x=>Resources.Load<TextAsset>(x).text).ToArray();
            foreach(var p in parameters){Assert.IsNotEmpty(NativeGraphicsSettings.Label(p));Assert.IsNotEmpty(NativeGraphicsSettings.Group(p));Assert.Greater(p.Step,0);
                if(p.Path!=NativeGraphicsSettings.AaPath){Legacy(p.Path,p.Maximum);Assert.Throws<ArgumentException>(()=>NativeGraphicsSettings.Set(p.Path,p.Maximum));Assert.AreEqual(p.Read(),NativeGraphicsSettings.Read(p));}}
            foreach(var p in Effective())Assert.AreEqual(parameters.Single(x=>x.Path==p.Path).Read(),p.Read(),p.Path+" authored Lab values win over legacy player tuning");
            var aa=parameters.Single(p=>p.Path==NativeGraphicsSettings.AaPath);NativeGraphicsSettings.Set(aa.Path,2);Assert.AreEqual(2,CameraAntialiasingProfile.Load().mode);
            Assert.Throws<ArgumentOutOfRangeException>(()=>NativeGraphicsSettings.Set(aa.Path,float.NaN));Assert.Throws<ArgumentOutOfRangeException>(()=>NativeGraphicsSettings.Set(aa.Path,3));Assert.Throws<ArgumentOutOfRangeException>(()=>NativeGraphicsSettings.Set(aa.Path,.5f));Legacy(aa.Path,float.NaN);Assert.AreEqual(aa.Read(),NativeGraphicsSettings.Read(aa));
            // Lab uses these same metadata setters on its own raw profile copy.
            foreach(var name in resources){var raw=Resources.Load<TextAsset>(name).text;IEnumerable<SurfaceParameter> fields;
                if(name=="CameraAntialiasingProfile")fields=JsonUtility.FromJson<CameraAntialiasingProfile>(raw).Parameters();else if(name=="ContactOcclusionProfile")fields=JsonUtility.FromJson<ContactOcclusionProfile>(raw).Parameters();else if(name=="ActorMaterialProfile")fields=JsonUtility.FromJson<ActorMaterialProfile>(raw).Parameters();else fields=JsonUtility.FromJson<IndustrialLightingProfile>(raw).Parameters();
                foreach(var p in fields){p.Write(p.Maximum);Assert.AreEqual(p.Maximum,p.Read(),"Lab metadata setter remains available");}}
            NativeGraphicsSettings.Reset();foreach(var p in Effective())Assert.AreEqual(parameters.Single(x=>x.Path==p.Path).Read(),p.Read());CollectionAssert.AreEqual(originals,resources.Select(x=>Resources.Load<TextAsset>(x).text).ToArray());
        }
        [Test] public void EffectTogglesUseAuthoredIntensityAndResetRestoresAcceptedDefaults()
        {
            Legacy("visual.actorFinish.bloomIntensity",.33f);Legacy("visual.contactOcclusion.intensity",2.1f);
            NativeGraphicsSettings.BloomEnabled=false;NativeGraphicsSettings.OcclusionEnabled=false;Assert.Zero(ActorMaterialProfile.Load().bloomIntensity);Assert.Zero(ContactOcclusionProfile.Load().intensity);
            NativeGraphicsSettings.BloomEnabled=true;NativeGraphicsSettings.OcclusionEnabled=true;Assert.AreEqual(.16f,ActorMaterialProfile.Load().bloomIntensity);Assert.AreEqual(3,ContactOcclusionProfile.Load().intensity);
            float music=NativeAudioSettings.Music;NativeGraphicsSettings.Reset();Assert.AreEqual(music,NativeAudioSettings.Music);Assert.AreEqual(1,CameraAntialiasingProfile.Load().mode);Assert.AreEqual(.16f,ActorMaterialProfile.Load().bloomIntensity);Assert.AreEqual(3,ContactOcclusionProfile.Load().intensity);
        }
        private static string Output(){var path=Environment.GetEnvironmentVariable("SPACEWARS_GRAPHICS_EVIDENCE")??Path.GetFullPath("../.local/graphics-settings/captures");Directory.CreateDirectory(path);return path;}
        private static Texture2D Read(RenderTexture target){var previous=RenderTexture.active;try{RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();return image;}finally{RenderTexture.active=previous;}}
        private static void Save(RenderTexture target,string name){var image=Read(target);try{File.WriteAllBytes(Path.Combine(Output(),name+".png"),image.EncodeToPNG());}finally{UnityEngine.Object.Destroy(image);}}
        [UnityTest] public IEnumerator PlayerGraphicsUiHasOnlyDiscreteChoicesAndResetAtBothViewports()
        {
            var host=new GameObject("Player graphics settings UI");hosts.Add(host);var panel=ScriptableObject.CreateInstance<PanelSettings>();panels.Add(panel);panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");var doc=host.AddComponent<UIDocument>();doc.panelSettings=panel;OrbitalTheme.ConfigurePanel(panel,doc.rootVisualElement);
            var target=new RenderTexture(800,620,0);target.Create();panel.targetTexture=target;using(var nav=new NativeMenuNavigation(doc.rootVisualElement)){
            try{
                var page=NativeSettingsView.Open(doc.rootVisualElement,nav,()=>{},new NativeLocalControlSettings());nav.ActivateElement(page.Q<Button>("settings-tab-Графика"));yield return null;yield return null;
                Assert.IsNull(page.Q("settings-graphics-advanced"));Assert.Zero(page.Query<Slider>().ToList().Count(s=>s.name.StartsWith("settings-graphics-")),"No fine graphics tuning in player menu");
                var aa=page.Q<DropdownField>("settings-graphics-aa");CollectionAssert.AreEqual(new[]{"Выключено","MSAA 4×","SMAA · высокое качество"},aa.choices);Assert.AreEqual(1,aa.index);aa.index=0;
                page.Q<Toggle>("settings-graphics-ssao").value=false;page.Q<Toggle>("settings-graphics-bloom").value=false;
                nav.Back();page=NativeSettingsView.Open(doc.rootVisualElement,nav,()=>{},new NativeLocalControlSettings());nav.ActivateElement(page.Q<Button>("settings-tab-Графика"));yield return null;yield return null;
                Assert.Zero(page.Q<DropdownField>("settings-graphics-aa").index);Assert.False(page.Q<Toggle>("settings-graphics-ssao").value);Assert.False(page.Q<Toggle>("settings-graphics-bloom").value);
                foreach(var size in new[]{new Vector2Int(800,620),new Vector2Int(1280,800)}){
                    panel.targetTexture=null;target.Release();target.width=size.x;target.height=size.y;target.Create();panel.targetTexture=target;yield return null;yield return null;
                    var bloom=page.Q<Toggle>("settings-graphics-bloom");bloom.Focus();page.Q<ScrollView>("settings-scroll").ScrollTo(bloom);yield return null;yield return null;nav.Activate();Assert.True(bloom.value);nav.Activate();Assert.False(bloom.value);Save(target,"player-graphics-"+size.x+"x"+size.y);
                }
                var restore=NativePreferenceWriter.Capture();try {nav.ActivateElement(page.Q<Button>("settings-reset-all"));nav.ActivateElement(doc.rootVisualElement.Q<Button>("settings-confirm-accept"));nav.ActivateElement(doc.rootVisualElement.Q<Button>("settings-confirm-accept"));Assert.AreEqual(1,page.Q<DropdownField>("settings-graphics-aa").index);Assert.True(page.Q<Toggle>("settings-graphics-bloom").value);Assert.True(page.Q<Toggle>("settings-graphics-ssao").value);}finally{restore();PlayerPrefs.Save();}
            }finally{panel.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);}}yield return null;
        }
        private static Color TeamPaint(PlayableWorld.Actor actor)
        {foreach(var renderer in actor.Hull.GetComponentsInChildren<Renderer>(true))for(int slot=0;slot<renderer.sharedMaterials.Length;slot++)if(renderer.sharedMaterials[slot].name.EndsWith("team-primary")){var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,slot);return block.GetColor("_BaseColor");}throw new Exception("No owner material");}
        private static float Armor(PlayableWorld.Actor actor)
        {foreach(var renderer in actor.Hull.GetComponentsInChildren<Renderer>(true))for(int slot=0;slot<renderer.sharedMaterials.Length;slot++)if(renderer.sharedMaterials[slot].name.EndsWith("team-primary")){var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,slot);return block.GetFloat("_Smoothness");}throw new Exception("No armor material");}
        [UnityTest] public IEnumerator LiveOrdinaryAndTwoLocalViewsKeepPaintGeometryAndFogPrivacy()
        {
            for(int stage=0;stage<2;stage++){
                NativeGraphicsSettings.Reset();var host=new GameObject("Live graphics stage "+stage);hosts.Add(host);Camera[] cameras;PlayableWorld[] worlds;
                if(stage==0){var hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;typeof(PlayableBootstrap).GetField("profile",Flags).SetValue(hud,PlayableProfile.Default);Call(hud,"CreateWorld");cameras=new[]{Get<Camera>(hud,"cameraView")};worlds=new[]{Get<PlayableWorld>(hud,"world")};worlds[0].InspectionReveal();worlds[0].Building(1,"Headquarters",true);worlds[0].Tank(2,true);}
                else{var hud=host.AddComponent<OfflineTwoLocalBootstrap>();hud.enabled=false;Call(hud,"Start");Call(hud,"Render",Get<PlayableRuntime>(hud,"runtime").OfflineFrame);var views=Get<Array>(hud,"views").Cast<object>().ToArray();cameras=views.Select(v=>Get<Camera>(v,"Camera")).ToArray();worlds=views.Select(v=>Get<PlayableWorld>(v,"World")).ToArray();}
                var actors=worlds.Select(w=>w.Actors.Values.First(a=>a.Hull)).ToArray();var paints=actors.Select(TeamPaint).ToArray();var geometry=worlds.Select(w=>w.GraphicsRoot.GetComponentsInChildren<Renderer>(true).Select(r=>r.GetInstanceID()).ToArray()).ToArray();
                for(int n=0;n<8;n++)yield return null;foreach(var camera in cameras)Assert.AreEqual(4,CameraAntialiasingDiagnostics.Samples(camera),"Initial selected screen AA");
                NativeGraphicsSettings.Set(NativeGraphicsSettings.AaPath,0);NativeGraphicsSettings.OcclusionEnabled=false;NativeGraphicsSettings.BloomEnabled=false;Legacy("visual.actorFinish.armorSmoothness",.2f);
                for(int n=0;n<8;n++)yield return null;
                for(int i=0;i<cameras.Length;i++){Assert.AreEqual(1,CameraAntialiasingDiagnostics.Samples(cameras[i]),"Live screen AA Off");Assert.Zero(cameras[i].GetComponent<ContactOcclusionCamera>().Profile.intensity);Assert.True(cameras[i].GetComponent<ActorGlowCamera>().Profile.TryGet<Bloom>(out var bloom));Assert.Zero(bloom.intensity.value);Assert.AreEqual(paints[i],TeamPaint(actors[i]),"Exact owner paint retained");Assert.That(Armor(actors[i]),Is.EqualTo(.28f).Within(.00001f));CollectionAssert.AreEqual(geometry[i],worlds[i].GraphicsRoot.GetComponentsInChildren<Renderer>(true).Select(r=>r.GetInstanceID()).ToArray());}
                Legacy("visual.lighting.sunIntensity",.5f);Assert.That(host.GetComponentsInChildren<Light>().Single(l=>l.type==LightType.Directional).intensity,Is.EqualTo(IndustrialLightingProfile.Load().sunIntensity).Within(.00001f));
                Legacy("visual.threeCrossings.lighting.sunIntensity",1.8f);Assert.That(host.GetComponentsInChildren<Light>().Single(l=>l.type==LightType.Directional).intensity,Is.EqualTo(IndustrialLightingProfile.Load().sunIntensity).Within(.00001f),"Other map light settings do not leak");
                NativeGraphicsSettings.Set(NativeGraphicsSettings.AaPath,2);for(int n=0;n<8;n++)yield return null;foreach(var camera in cameras){Assert.AreEqual(1,CameraAntialiasingDiagnostics.Samples(camera));Assert.AreEqual(AntialiasingMode.SubpixelMorphologicalAntiAliasing,camera.GetUniversalAdditionalCameraData().antialiasing);}
                NativeGraphicsSettings.Reset();for(int n=0;n<8;n++)yield return null;foreach(var camera in cameras){Assert.AreEqual(4,CameraAntialiasingDiagnostics.Samples(camera),"Reset live AA4");Assert.AreEqual(3,camera.GetComponent<ContactOcclusionCamera>().Profile.intensity);Assert.True(camera.GetComponent<ActorGlowCamera>().Profile.TryGet<Tonemapping>(out var tone));Assert.AreEqual(TonemappingMode.None,tone.mode.value);}
                var target=new RenderTexture(1000,700,24){antiAliasing=4};target.Create();cameras[0].targetTexture=target;
                try{
                    for(int n=0;n<8;n++)yield return null;Save(target,stage==0?"ordinary-default":"local-default");NativeGraphicsSettings.Set(NativeGraphicsSettings.AaPath,0);Assert.AreEqual(1,target.antiAliasing,"Live RT sample reallocation");for(int n=0;n<8;n++)yield return null;Assert.AreEqual(1,CameraAntialiasingDiagnostics.Samples(cameras[0]));
                    NativeGraphicsSettings.Set(NativeGraphicsSettings.AaPath,1);Assert.AreEqual(4,target.antiAliasing);var hidden=worlds[0].Building(990,"ScientificCenter",false);hidden.Root.gameObject.SetActive(false);hidden.Health.parent.gameObject.SetActive(false);NativeGraphicsSettings.BloomEnabled=true;for(int n=0;n<8;n++)yield return null;var concealed=Read(target);worlds[0].Remove(990);for(int n=0;n<8;n++)yield return null;var absent=Read(target);try{CollectionAssert.AreEqual(concealed.GetPixels32(),absent.GetPixels32(),"Live material/effect changes never reveal hidden enemy");}finally{UnityEngine.Object.Destroy(concealed);UnityEngine.Object.Destroy(absent);}
                    for(int i=0;i<cameras.Length;i++){Assert.AreEqual(stage==0?29:30+i,cameras[i].GetComponent<ActorGlowCamera>().VolumeLayer);if(stage==1)Assert.True(OfflineCameraMirrorFeature.IsRegistered(cameras[i]));}
                }finally{cameras[0].targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);}
                UnityEngine.Object.Destroy(host);yield return null;yield return null;
            }
            // No stale camera/world callbacks after owned roots are destroyed.
            NativeGraphicsSettings.Reset();yield return null;
        }
        [UnityTest] public IEnumerator NewCrossingsViewInheritsOverridesAndUsesOnlyItsOwnLightingGroup()
        {
            NativeGraphicsSettings.Set(NativeGraphicsSettings.AaPath,0);NativeGraphicsSettings.BloomEnabled=false;Legacy("visual.actorFinish.armorSmoothness",.35f);
            Legacy("visual.lighting.sunIntensity",.5f);Legacy("visual.threeCrossings.lighting.sunIntensity",1.7f);
            var profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));
            var host=new GameObject("Inherited Crossings graphics");hosts.Add(host);var hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;typeof(PlayableBootstrap).GetField("profile",Flags).SetValue(hud,profile);Call(hud,"CreateWorld");
            var camera=Get<Camera>(hud,"cameraView");var world=Get<PlayableWorld>(hud,"world");var actor=world.Building(1,"Headquarters",true);var sun=host.GetComponentsInChildren<Light>().Single(l=>l.type==LightType.Directional);
            Assert.AreEqual(IndustrialLightingProfile.Load(profile.AuthoredMap.Id).sunIntensity,sun.intensity);Assert.That(Armor(actor),Is.EqualTo(.28f).Within(.00001f));Assert.True(camera.GetComponent<ActorGlowCamera>().Profile.TryGet<Bloom>(out var bloom));Assert.Zero(bloom.intensity.value);
            for(int n=0;n<8;n++)yield return null;Assert.AreEqual(1,CameraAntialiasingDiagnostics.Samples(camera),"New screen view inherits saved AA Off");
            Legacy("visual.lighting.sunIntensity",1.1f);Assert.AreEqual(IndustrialLightingProfile.Load(profile.AuthoredMap.Id).sunIntensity,sun.intensity,"General lighting never changes Crossings");Legacy("visual.threeCrossings.lighting.sunIntensity",1.6f);Assert.AreEqual(IndustrialLightingProfile.Load(profile.AuthoredMap.Id).sunIntensity,sun.intensity,"Legacy player map lighting is ignored");
            NativeGraphicsSettings.Reset();for(int n=0;n<8;n++)yield return null;Assert.AreEqual(4,CameraAntialiasingDiagnostics.Samples(camera));Assert.AreEqual(IndustrialLightingProfile.Load(profile.AuthoredMap.Id).sunIntensity,sun.intensity);Assert.That(Armor(actor),Is.EqualTo(.28f).Within(.00001f));
        }
    }
}
