using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
namespace Spacewars.Tests.PlayMode
{
    public sealed class RingHangarPresentationTests
    {
        static string Output(){var path=Environment.GetEnvironmentVariable("SPACEWARS_HANGAR_EVIDENCE");if(string.IsNullOrEmpty(path))path=Path.Combine(Application.temporaryCachePath,"ring-hangar-evidence");Directory.CreateDirectory(path);return path;}
        static void Save(RenderTexture rt,string file){var previous=RenderTexture.active;RenderTexture.active=rt;var t=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);t.Apply();File.WriteAllBytes(Path.Combine(Output(),file),t.EncodeToPNG());UnityEngine.Object.Destroy(t);RenderTexture.active=previous;}
        [UnityTest] public IEnumerator ApprovedModelAndOwnerColorsRenderWithFactoryForScale()
        {
            var root=new GameObject("Hangar visual inspection");var target=new RenderTexture(1400,900,24);target.Create();
            var oldAmbient=RenderSettings.ambientLight;var oldMode=RenderSettings.ambientMode;
            try{
                using var world=new PlayableWorld(root.transform,PlayableProfile.Default,staged:true);
                var hangar=world.Building(1,"Hangar",true);var factory=world.Building(2,"Factory",true);
                hangar.Health.parent.gameObject.SetActive(false);factory.Health.parent.gameObject.SetActive(false);
                hangar.Root.position=new Vector3(-2,0,0);factory.Root.position=new Vector3(2,0,0);
                Assert.AreEqual(14,hangar.Hull.GetComponentsInChildren<Renderer>().Length);Assert.AreEqual(8,hangar.Hull.GetComponentsInChildren<Transform>().Count(t=>t.name.StartsWith("roofPetal")));Assert.NotNull(hangar.Hull.GetComponentsInChildren<Transform>().Single(t=>t.name=="launchPlatform"));Assert.Zero(hangar.Hull.GetComponentsInChildren<Collider>().Length);
                Assert.NotNull(OrbitalGlyph.TextureFor("hangar"));Assert.AreEqual("hangar",OrbitalPrecision.BuildingGlyph(PlayableBuildingKind.Hangar));
                var team=hangar.Hull.GetComponentsInChildren<Renderer>().Single(r=>r.transform.parent.name=="modelRoot"&&r.sharedMaterial.name.EndsWith("team-primary"));
                var material=team.sharedMaterial;var before=material.GetColor("_BaseColor");var block=new MaterialPropertyBlock();
                PlayableWorld.PaintOwner(hangar,Color.red);team.GetPropertyBlock(block,0);Assert.Greater(block.GetColor("_BaseColor").r,block.GetColor("_BaseColor").b);Assert.AreEqual(before,material.GetColor("_BaseColor"));
                PlayableWorld.PaintOwner(hangar,new Color(.19f,.72f,.77f));
                var camera=new GameObject("Hangar gallery camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.targetTexture=target;camera.orthographic=true;camera.orthographicSize=4;camera.nearClipPlane=.1f;camera.farClipPlane=100;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.11f,.14f,.16f);camera.transform.position=new Vector3(0,9,-11);camera.transform.LookAt(new Vector3(0,.6f,0));
                var light=new GameObject("Hangar sun").AddComponent<Light>();light.transform.SetParent(root.transform);light.type=LightType.Directional;light.intensity=1.4f;light.transform.rotation=Quaternion.Euler(45,-30,0);light.shadows=LightShadows.Soft;
                RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.48f,.52f,.58f);
                for(int i=0;i<10;i++)yield return null;Save(target,"hangar-and-factory.png");
                factory.Root.gameObject.SetActive(false);camera.orthographicSize=2.25f;camera.transform.position=hangar.Root.position+new Vector3(3.6f,5,-5);camera.transform.LookAt(hangar.Root.position+Vector3.up*.7f);
                for(int i=0;i<5;i++)yield return null;Save(target,"hangar-close.png");camera.targetTexture=null;
            }finally{RenderSettings.ambientLight=oldAmbient;RenderSettings.ambientMode=oldMode;UnityEngine.Object.Destroy(root);target.Release();UnityEngine.Object.Destroy(target);}
        }
        [UnityTest] public IEnumerator LaboratoryHasHangarIconAndEditableIndependentPriceTime()
        {
            var root=new GameObject("Hangar lab QA");var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");var doc=root.AddComponent<UIDocument>();doc.panelSettings=panel;
            var texture=new RenderTexture(1280,800,24);texture.Create();panel.targetTexture=texture;
            var dir=Path.Combine(Output(),"lab-data");Directory.CreateDirectory(dir);
            try{
                var ui=doc.rootVisualElement;ui.style.width=1280;ui.style.height=800;OrbitalTheme.ConfigurePanel(panel,ui);
                var nav=new NativeMenuNavigation(ui);var store=new NativeBalanceStore(PlayableProfile.Default,Path.Combine(dir,"balance.json"));var view=new NativeBalanceView(ui,nav,store,()=>{});
                for(int i=0;i<5;i++)yield return null;
                nav.ActivateElement(view.Page.Q<Button>("lab-section-Здания"));
                var button=view.Page.Q<Button>("lab-object-Ангар");Assert.NotNull(button);Assert.NotNull(button.Q<OrbitalGlyph>());nav.ActivateElement(button);
                for(int i=0;i<5;i++)yield return null;
                var cost=view.Page.Q<TextField>("lab-field-hangarCreditCost");var time=view.Page.Q<TextField>("lab-field-hangarBuildSeconds");Assert.NotNull(cost);Assert.NotNull(time);
                Save(texture,"hangar-laboratory.png");cost.value="450";time.value="25";
                Assert.True(view.Page.Q<Button>("lab-save").enabledSelf);Assert.IsNull(view.Page.Q<TextField>("lab-field-factoryCreditCost"));
                nav.ActivateElement(view.Page.Q<Button>("lab-save"));Assert.AreEqual(450,store.Selected.HangarCreditCost);Assert.AreEqual(25,store.Selected.HangarBuildSeconds);Assert.AreEqual(300,store.Selected.FactoryCreditCost);
            }finally{panel.targetTexture=null;UnityEngine.Object.Destroy(root);UnityEngine.Object.Destroy(panel);texture.Release();UnityEngine.Object.Destroy(texture);}
        }
    }
}
