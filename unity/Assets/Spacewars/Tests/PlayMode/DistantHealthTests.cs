using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object=UnityEngine.Object;

namespace Spacewars.Tests.PlayMode
{
    public sealed class DistantHealthTests
    {
        Action restore,flush;ModelDetailProfile priorDetail;GameObject host;float priorTimeScale;
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        [SetUp] public void Setup()
        {
            restore=NativePreferenceWriter.Capture();flush=NativePreferenceWriter.Flush;priorDetail=ModelDetailProfile.Current.Copy();priorTimeScale=Time.timeScale;
            PlayerPrefs.DeleteKey(NativeUserSettings.HideDistantHealthKey);NativeUserSettings.AlwaysHealth=true;ModelDetailProfile.UseSession(ModelDetailProfile.Load());
            host=new GameObject("Distant health fixture");
        }
        [TearDown] public void Cleanup()
        {Time.timeScale=priorTimeScale;NativePreferenceWriter.Flush=flush;restore();PlayerPrefs.Save();ModelDetailProfile.UseSession(priorDetail);Object.DestroyImmediate(host);}
        Camera Camera(string name,float zoom)
        {var camera=new GameObject(name).AddComponent<Camera>();camera.transform.SetParent(host.transform);camera.orthographic=true;camera.orthographicSize=zoom;camera.transform.position=new Vector3(0,26,-37);camera.transform.LookAt(Vector3.zero);return camera;}
        [Test] public void EachLocalCameraHidesAllBarsAtBoundaryAndRestoresExistingRulesOnPause()
        {
            using(var nearWorld=new PlayableWorld(host.transform,PlayableProfile.Default,staged:true))
            using(var farWorld=new PlayableWorld(host.transform,PlayableProfile.Default,staged:true)){
                var near=Camera("Near local view",9.6f);var far=Camera("Far local view",ModelDetailProfile.Current.healthHideZoom);
                var closeActors=new[]{nearWorld.Tank(1,true),nearWorld.Building(2,"Factory",true)};
                var farActors=new[]{farWorld.Tank(1,true),farWorld.Building(2,"Factory",true)};
                Time.timeScale=0;Assert.True(NativeUserSettings.HideDistantHealth,"Default is enabled");
                for(int i=0;i<2;i++){
                    PlayableWorld.UpdateHealth(closeActors[i],near,.4f,true);PlayableWorld.UpdateHealth(farActors[i],far,.4f,true);
                    Assert.True(closeActors[i].Health.parent.gameObject.activeSelf);Assert.False(farActors[i].Health.parent.gameObject.activeSelf,"Selected and damaged bars are also hidden");
                    Assert.True(farActors[i].Root.gameObject.activeSelf,"Model and selection are independent");
                    far.orthographicSize=ModelDetailProfile.Current.healthHideZoom-.001f;PlayableWorld.UpdateHealth(farActors[i],far,.4f,true);Assert.True(farActors[i].Health.parent.gameObject.activeSelf);
                    far.orthographicSize=25.6f;NativeUserSettings.HideDistantHealth=false;PlayableWorld.UpdateHealth(farActors[i],far,.4f,true);Assert.True(farActors[i].Health.parent.gameObject.activeSelf);
                    NativeUserSettings.HideDistantHealth=true;far.orthographicSize=ModelDetailProfile.Current.healthHideZoom;
                }
                NativeUserSettings.AlwaysHealth=false;
                PlayableWorld.UpdateHealth(closeActors[0],near,1,false);Assert.False(closeActors[0].Health.parent.gameObject.activeSelf);
                PlayableWorld.UpdateHealth(closeActors[0],near,.7f,false);Assert.True(closeActors[0].Health.parent.gameObject.activeSelf);
                PlayableWorld.UpdateHealth(closeActors[0],near,1,true);Assert.True(closeActors[0].Health.parent.gameObject.activeSelf);
            }
        }
        [Test] public void PerspectiveUsesCameraDepthAndDisablingOptionRestoresBars()
        {
            using(var world=new PlayableWorld(host.transform,PlayableProfile.Default,staged:true)){
                var camera=Camera("Perspective view",1);camera.orthographic=false;camera.fieldOfView=60;camera.transform.rotation=Quaternion.identity;camera.transform.position=new Vector3(0,0,-40);
                var actor=world.Tank(1,true);PlayableWorld.UpdateHealth(actor,camera,.5f,true);Assert.False(actor.Health.parent.gameObject.activeSelf);
                camera.transform.position=new Vector3(0,0,-20);PlayableWorld.UpdateHealth(actor,camera,.5f,true);Assert.True(actor.Health.parent.gameObject.activeSelf);
                camera.transform.position=new Vector3(0,0,-40);NativeUserSettings.HideDistantHealth=false;PlayableWorld.UpdateHealth(actor,camera,.5f,true);Assert.True(actor.Health.parent.gameObject.activeSelf);
            }
        }
        [Test] public void PreferenceFlushAndFailedWriteRetainPreviousChoice()
        {
            int saves=0;NativePreferenceWriter.Flush=()=>{PlayerPrefs.Save();saves++;};NativeUserSettings.HideDistantHealth=false;
            Assert.AreEqual(1,saves);Assert.AreEqual(0,PlayerPrefs.GetInt(NativeUserSettings.HideDistantHealthKey));Assert.False(NativeUserSettings.HideDistantHealth);
            NativePreferenceWriter.Flush=()=>throw new IOException("Injected storage failure");Assert.Throws<IOException>(()=>NativeUserSettings.HideDistantHealth=true);
            Assert.False(NativeUserSettings.HideDistantHealth);Assert.AreEqual(0,PlayerPrefs.GetInt(NativeUserSettings.HideDistantHealthKey));
        }
        static string Output=>Environment.GetEnvironmentVariable("SPACEWARS_DISTANT_HEALTH_OUTPUT");
        static byte[] Capture(RenderTexture target,string name)
        {
            var previous=RenderTexture.active;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            try{RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();var bytes=image.EncodeToPNG();if(!string.IsNullOrEmpty(Output)){Directory.CreateDirectory(Output);File.WriteAllBytes(Path.Combine(Output,name+".png"),bytes);}return bytes;}
            finally{RenderTexture.active=previous;Object.DestroyImmediate(image);}
        }
        [UnityTest] public IEnumerator InterfaceTogglePersistsAcrossReopenAndResetRestoresDefault()
        {
            var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");var doc=host.AddComponent<UIDocument>();doc.panelSettings=panel;OrbitalTheme.ConfigurePanel(panel,doc.rootVisualElement);
            var target=new RenderTexture(1280,800,0);target.Create();panel.targetTexture=target;
            using(var nav=new NativeMenuNavigation(doc.rootVisualElement))try{
                var page=NativeSettingsView.Open(doc.rootVisualElement,nav,()=>{});nav.ActivateElement(page.Q<Button>("settings-tab-Интерфейс"));yield return null;yield return null;
                var toggle=page.Q<Toggle>("settings-hide-distant-health");Assert.True(toggle.value);nav.ActivateElement(toggle);Assert.False(NativeUserSettings.HideDistantHealth);
                nav.Back();page=NativeSettingsView.Open(doc.rootVisualElement,nav,()=>{});nav.ActivateElement(page.Q<Button>("settings-tab-Интерфейс"));yield return null;yield return null;
                Assert.False(page.Q<Toggle>("settings-hide-distant-health").value,"Reopened UI reads stored choice");
                // Exercise the actual reset flow, including display confirmation.
                nav.ActivateElement(page.Q<Button>("settings-reset-all"));nav.ActivateElement(doc.rootVisualElement.Q<Button>("settings-confirm-accept"));nav.ActivateElement(doc.rootVisualElement.Q<Button>("settings-confirm-accept"));
                Assert.True(NativeUserSettings.HideDistantHealth);Assert.True(page.Q<Toggle>("settings-hide-distant-health").value);
                nav.ActivateElement(page.Q<Button>("settings-tab-Интерфейс"));yield return null;yield return null;Capture(target,"settings-interface");
            }finally{panel.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(panel);}
        }
        [UnityTest] public IEnumerator MatchedAuthoredSceneShowsFarToggleAndNearRestoration()
        {
            var profile=PlayableProfile.ThreeCrossingsDefault;var hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
            typeof(PlayableBootstrap).GetField("profile",Flags).SetValue(hud,profile);typeof(PlayableBootstrap).GetMethod("CreateWorld",Flags).Invoke(hud,null);
            var camera=(Camera)typeof(PlayableBootstrap).GetField("cameraView",Flags).GetValue(hud);var world=(PlayableWorld)typeof(PlayableBootstrap).GetField("world",Flags).GetValue(hud);
            world.InspectionReveal();var center=world.Point(profile.Headquarters(PlayableOwner.Player));
            var actors=new[]{world.Building(1,"Headquarters",true),world.Building(2,"Factory",true),world.Tank(3,true),world.Tank(4,false,PlayableEntityKind.Shkval),world.Tank(5,true,PlayableEntityKind.Explorer)};
            var offsets=new[]{new Vector3(-5,0,4),new Vector3(5,0,4),new Vector3(-5,0,-4),new Vector3(0,0,-4),new Vector3(5,0,-4)};
            for(int i=0;i<actors.Length;i++)actors[i].Root.position=center+offsets[i];
            camera.transform.position=center+new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);camera.transform.LookAt(center);
            var target=new RenderTexture(1280,800,24){antiAliasing=4};target.Create();camera.targetTexture=target;
            try{
                byte[] farOff=null,farOn=null;
                foreach(var state in new[]{(name:"far-before",zoom:25.6f,hide:false),(name:"far-after",zoom:25.6f,hide:true),(name:"near-after",zoom:9.6f,hide:true)}){
                    camera.orthographicSize=state.zoom;NativeUserSettings.HideDistantHealth=state.hide;
                    for(int frame=0;frame<6;frame++){foreach(var actor in actors)PlayableWorld.UpdateHealth(actor,camera,.67f,true);yield return null;}
                    foreach(var actor in actors)Assert.AreEqual(state.name!="far-after",actor.Health.parent.gameObject.activeSelf);
                    camera.Render();var bytes=Capture(target,state.name);if(state.name=="far-before")farOff=bytes;else if(state.name=="far-after")farOn=bytes;
                }
                Assert.False(farOff.SequenceEqual(farOn),"Actual rendered frames must show the visibility change");
            }finally{camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);}
        }
    }
}
