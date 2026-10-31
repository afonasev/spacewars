using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class NativeBalanceAiProfileUiTests
{
    private static string Unit(string value)=>value=="s"?"с":value=="ratio"?"×":value=="weight"?"баллов":value=="buildings"?"зданий":value=="units"?"юнитов":value=="changes"?"смен":value;
    [UnityTest] public IEnumerator NamedMetadataControlsRenderPersistReloadAndReset()
    {
        var host=new GameObject("AI Balance Lab QA");var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");
        var texture=new RenderTexture(1280,800,24);texture.Create();panel.targetTexture=texture;
        var doc=host.AddComponent<UIDocument>();doc.panelSettings=panel;var root=doc.rootVisualElement;root.style.width=1280;root.style.height=800;OrbitalTheme.ConfigurePanel(panel,root);
        var nav=new NativeMenuNavigation(root);var directory=Path.Combine(Path.GetTempPath(),"spacewars-ai-lab-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            string path=Path.Combine(directory,"profiles.json");var store=new NativeBalanceStore(PlayableProfile.Default,path);var view=new NativeBalanceView(root,nav,store,()=>{});
            for(int i=0;i<5;i++)yield return null;
            nav.ActivateElement(view.Page.Q<Button>("lab-section-ИИ"));
            Assert.IsTrue(view.Page.Query<Label>().ToList().Any(l=>l.text=="После сохранения настройки применятся к новым матчам; текущий матч не меняется."));
            var fields=NativeBalanceFields.AiOpeningFields;
            Assert.That(view.Page.Query<TextField>().ToList().Count(f=>f.name.StartsWith("lab-ai-")),Is.EqualTo(fields.Count));
            Assert.IsNull(view.Page.Q("lab-ai-opening-weights-safe"),"Legacy opening weights are not active controls.");
            foreach(var field in fields)
            {
                var editor=view.Page.Q<TextField>("lab-ai-"+field.Path.Replace('.','-'));
                Assert.NotNull(editor,field.Path);Assert.AreEqual(field.Label,editor.label);
                StringAssert.Contains(field.Description,editor.tooltip);StringAssert.Contains(Unit(field.Unit),editor.tooltip);
                StringAssert.Contains("шаг "+field.Step.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture),editor.tooltip);
            }
            for(int i=0;i<5;i++)yield return null;Capture(texture,"ai-openings-midgame-1280x800");
            var target=fields.Single(f=>f.Path=="openingsMidgame.opening.doubleIncome");var input=view.Page.Q<TextField>("lab-ai-"+target.Path.Replace('.','-'));
            input.value=(target.Initial+target.Step).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.AreEqual(target.Initial,store.ActiveAiProfile.Value(target.Path),"Editing a draft does not rebind the active profile before save.");
            nav.ActivateElement(view.Page.Q<Button>("lab-save"));
            Assert.AreEqual(target.Initial+target.Step,store.ActiveAiProfile.Value(target.Path));
            var reloaded=new NativeBalanceStore(PlayableProfile.Default,path);
            Assert.AreEqual(target.Initial+target.Step,reloaded.ActiveAiProfile.Value(target.Path),"Last saved profile selection survives store reload.");
            Assert.AreEqual(PlayableProfile.Default.TankCreditCost,reloaded.Active.TankCreditCost);
            nav.ActivateElement(view.Page.Q<Button>("lab-ai-reset"));
            Assert.AreEqual(target.Initial,double.Parse(view.Page.Q<TextField>("lab-ai-"+target.Path.Replace('.','-')).value,System.Globalization.CultureInfo.InvariantCulture));
            nav.ActivateElement(view.Page.Q<Button>("lab-save"));
            Assert.AreEqual(target.Initial,new NativeBalanceStore(PlayableProfile.Default,path).ActiveAiProfile.Value(target.Path),"Named reset persists independently of gameplay settings.");
        }
        finally{nav.Dispose();panel.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(panel);Directory.Delete(directory,true);}
    }
    private static void Capture(RenderTexture texture,string name)
    {
        var folder=Environment.GetEnvironmentVariable("SPACEWARS_LAB_EVIDENCE")??Path.GetTempPath();Directory.CreateDirectory(folder);
        var previous=RenderTexture.active;var pixels=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
        try{RenderTexture.active=texture;pixels.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(folder,name+".png"),pixels.EncodeToPNG());}
        finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(pixels);}
    }
}
