using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Spacewars.Input.Tests
{
    public static class NativeControlsHelpImageScenario
    {
        public static IEnumerator RenderAndNavigate(GameObject host,VisualElement root)
        {
            bool closed=false;var invoking=new Button(){text="Управление"};root.Add(invoking);invoking.Focus();
            using(var navigation=new NativeMenuNavigation(root))
            {
                var page=NativeControlsHelp.Open(root,navigation,()=>{closed=true;invoking.Focus();});
                Assert.IsNull(page.Q("controller-diagram"),"The image replaces overlaid labels.");Assert.IsNull(page.Q("help-context-actions"),"The finished page replaces adjacent lists.");
                string[] names={"field","buildings","groups","map","keyboard"};
                foreach(var size in new[]{new Vector2Int(1280,720),new Vector2Int(1920,1080),new Vector2Int(640,720)})
                {
                    var panel=host.GetComponent<UIDocument>().panelSettings;var target=new RenderTexture(size.x,size.y,24);target.Create();panel.targetTexture=target;
                    try
                    {
                        yield return null;yield return null;
                        foreach(int tab in Enumerable.Range(0,5))
                        {
                            navigation.ActivateElement(page.Q<Button>("help-tab-"+tab));yield return null;yield return null;
                            var diagram=page.Q<Image>("controls-help-image");var texture=Resources.Load<Texture2D>("ControlsHelp/"+names[tab]);Assert.NotNull(texture);Assert.AreSame(texture,diagram.image);Assert.AreEqual(ScaleMode.ScaleToFit,diagram.scaleMode);
                            Assert.GreaterOrEqual(texture.width,1500);Assert.GreaterOrEqual(texture.height,840);Assert.GreaterOrEqual(diagram.worldBound.height,Mathf.Max(220, page.Q<ScrollView>("controls-scroll").contentViewport.worldBound.height-65));Assert.Greater(diagram.worldBound.width,0);
                            var scroll=page.Q<ScrollView>("controls-scroll");Assert.AreEqual(ScrollerVisibility.Hidden,scroll.horizontalScrollerVisibility);Assert.LessOrEqual(diagram.worldBound.width,scroll.contentViewport.worldBound.width+.5f,"Image remains within viewport width.");
                            Assert.AreEqual(tab<4?DisplayStyle.Flex:DisplayStyle.None,page.Q<Label>("help-controller-equivalents").style.display.value);Assert.IsNull(page.Q<Label>("controls-help-image-error"));
                            foreach(var button in page.Query<Button>().ToList()){Assert.GreaterOrEqual(button.worldBound.xMin,page.worldBound.xMin-.5f);Assert.LessOrEqual(button.worldBound.xMax,page.worldBound.xMax+.5f);}
                            string output=Environment.GetEnvironmentVariable("CONTROLS_HELP_EVIDENCE");
                            if(!string.IsNullOrEmpty(output))
                            {
                                Directory.CreateDirectory(output);var old=RenderTexture.active;RenderTexture.active=target;var png=new Texture2D(size.x,size.y,TextureFormat.RGB24,false);
                                try{png.ReadPixels(new Rect(0,0,size.x,size.y),0,0);png.Apply();File.WriteAllBytes(Path.Combine(output,names[tab]+"-"+size.x+"x"+size.y+".png"),png.EncodeToPNG());}
                                finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(png);}
                            }
                        }
                    }
                    finally{panel.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);}
                }
                navigation.Back();Assert.IsTrue(closed);Assert.IsNull(root.Q("controls-help"));Assert.AreSame(invoking,root.focusController.focusedElement);
            }
            invoking.RemoveFromHierarchy();
        }
    }

    public sealed class NativeControlsHelpImagesTests : InputTestFixture
    {
        GameObject host;
        [TearDown] public override void TearDown(){if(host!=null)UnityEngine.Object.DestroyImmediate(host);base.TearDown();}
        VisualElement CreateRoot()
        {
            host=new GameObject("finished controls help test");var app=host.AddComponent<PlayableBootstrap>();app.enabled=false;const BindingFlags f=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(PlayableBootstrap).GetField("profile",f).SetValue(app,PlayableProfile.Default);typeof(PlayableBootstrap).GetMethod("CreateHud",f).Invoke(app,null);return (VisualElement)typeof(PlayableBootstrap).GetField("root",f).GetValue(app);
        }
        [UnityTest] public IEnumerator AllFiveFinishedPagesFitAndReturnFocus()
        {var root=CreateRoot();yield return NativeControlsHelpImageScenario.RenderAndNavigate(host,root);}

        [UnityTest] public IEnumerator EquivalentGlyphsFollowTheCurrentControllerOnEveryGamepadSheet()
        {
            var root=CreateRoot();InputSystem.RegisterLayout("{\"name\":\"DualHelpPad\",\"extend\":\"Gamepad\"}");InputSystem.RegisterLayout("{\"name\":\"SwitchHelpPad\",\"extend\":\"Gamepad\"}");
            Gamepad current=(Gamepad)InputSystem.AddDevice("DualHelpPad");
            using(var navigation=new NativeMenuNavigation(root,()=>current))
            {
                var page=NativeControlsHelp.Open(root,navigation,()=>{});yield return null;
                foreach(int tab in Enumerable.Range(0,4)){navigation.ActivateElement(page.Q<Button>("help-tab-"+tab));StringAssert.Contains("A → ✕",page.Q<Label>("help-controller-equivalents").text);StringAssert.Contains("Y → △",page.Q<Label>("help-controller-equivalents").text);}
                current=(Gamepad)InputSystem.AddDevice("SwitchHelpPad");navigation.ActivateElement(page.Q<Button>("help-tab-3"));StringAssert.Contains("A → B",page.Q<Label>("help-controller-equivalents").text);StringAssert.Contains("B → A",page.Q<Label>("help-controller-equivalents").text);
                navigation.ActivateElement(page.Q<Button>("help-tab-4"));Assert.AreEqual(DisplayStyle.None,page.Q<Label>("help-controller-equivalents").style.display.value);navigation.Back();
            }
        }
    }
}
