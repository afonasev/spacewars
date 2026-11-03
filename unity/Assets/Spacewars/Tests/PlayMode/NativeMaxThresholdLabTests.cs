using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class NativeMaxThresholdLabTests
{
    string directory;
    [SetUp] public void Setup(){directory=Path.Combine(Path.GetTempPath(),"spacewars-max-threshold-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);}
    [TearDown] public void Cleanup(){if(Directory.Exists(directory))Directory.Delete(directory,true);}
    NativeBalanceStore Store()=>new NativeBalanceStore(PlayableProfile.Default,Path.Combine(directory,"balance.json"));
    static JObject LegacyRow(NativeBalanceStore store,double previous,double multiplier)
    {
        var row=(JObject)store.SelectedRow.DeepClone();var data=(JObject)row["data"];
        data.Remove("orbitalThresholdMax");data["orbitalThreshold5"]=previous;data["orbitalMaxThresholdMultiplier"]=multiplier;return row;
    }
    [TestCase(4000,2,8000)][TestCase(3000,3,9000)][TestCase(41,1.1,45.1)][TestCase(100000,10,1000000)]
    public void LegacyRowsRetainEffectiveThresholdAndNewSavesAreIndependent(double previous,double multiplier,double expected)
    {
        var store=Store();var row=LegacyRow(store,previous,multiplier);string original=row.ToString();
        var data=store.Compile(row).CopyData();Assert.AreEqual(expected,data.orbitalThresholdMax,1e-8);Assert.AreEqual(original,row.ToString(),"Compiling must not rewrite historical balance rows");
        // Saving an unchanged default is correctly rejected; represent a later designer edit.
        data.orbitalThreshold1+=1;
        var saved=store.Save(data,store.SelectedAudio);Assert.IsNull(store.SelectedRow["data"]["orbitalMaxThresholdMultiplier"]);Assert.AreEqual(expected,(double)store.SelectedRow["data"]["orbitalThresholdMax"],1e-8);
        var reloaded=Store().Resolve(saved.Revision).CopyData();Assert.AreEqual(expected,reloaded.orbitalThresholdMax,1e-8);reloaded.orbitalThreshold5=9999;Assert.AreEqual(expected,OrbitalAbilities.Threshold(5,reloaded),1e-8);
    }
    [Test] public void ExplicitAbsoluteAmountWinsAndInvalidLegacyMultipliersFail()
    {
        var store=Store();var row=LegacyRow(store,41,1.1);row["data"]["orbitalThresholdMax"]=12345.6;
        Assert.AreEqual(12345.6,store.Compile(row).CopyData().orbitalThresholdMax);
        foreach(double invalid in new[]{0d,11,1.15,double.NaN,double.PositiveInfinity}){
            row=LegacyRow(store,4000,invalid);Assert.That(()=>store.Compile(row),Throws.Exception);
        }
        row=LegacyRow(store,4000,2);row["data"]["orbitalMaxThresholdMultiplier"]="invalid";Assert.Throws<FormatException>(()=>store.Compile(row));
    }
    [UnityTest] public IEnumerator NumericMaxControlSavesReloadsAndDoesNotFollowV()
    {
        var host=new GameObject("independent MAX threshold lab QA");var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");
        var doc=host.AddComponent<UIDocument>();doc.panelSettings=panel;var root=doc.rootVisualElement;OrbitalTheme.Install(root);root.style.width=1280;root.style.height=800;
        var navigation=new NativeMenuNavigation(root);var texture=new RenderTexture(1280,800,24);texture.Create();panel.targetTexture=texture;OrbitalTheme.ConfigurePanel(panel,root);
        try
        {
            var store=Store();var view=new NativeBalanceView(root,navigation,store,()=>{});yield return null;
            navigation.ActivateElement(view.Page.Q<Button>("lab-section-Эскалация"));navigation.ActivateElement(view.Page.Q<Button>("lab-object-Пороги-уровней"));
            for(int i=0;i<5;i++)yield return null;
            var max=view.Page.Q<TextField>("lab-field-orbitalThresholdMax");Assert.NotNull(max);Assert.AreEqual("8000",max.value);Assert.IsNull(view.Page.Q("lab-field-orbitalMaxThresholdMultiplier"));
            Assert.Greater(max.worldBound.width,100);Assert.LessOrEqual(max.worldBound.xMax,1280);Assert.LessOrEqual(max.worldBound.yMax,800);Capture(texture,"max-threshold-hp.png");
            max.value="12345.6";view.Page.Q<TextField>("lab-field-orbitalThreshold5").value="4321";
            navigation.ActivateElement(view.Page.Q<Button>("lab-save"));yield return null;
            var saved=Store().Resolve(store.SelectedVersion).CopyData();Assert.AreEqual(12345.6,saved.orbitalThresholdMax);Assert.AreEqual(4321,saved.orbitalThreshold5);
            view.Page.Q<TextField>("lab-field-orbitalThreshold5").value="7654";navigation.ActivateElement(view.Page.Q<Button>("lab-save"));yield return null;
            saved=Store().Resolve(store.SelectedVersion).CopyData();Assert.AreEqual(12345.6,OrbitalAbilities.Threshold(5,saved));Assert.AreEqual(7654,saved.orbitalThreshold5);
            for(int i=0;i<5;i++)yield return null;Capture(texture,"max-threshold-independent.png");
        }
        finally{navigation.Dispose();panel.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(panel);}
    }
    static void Capture(RenderTexture texture,string name)
    {
        var folder=Environment.GetEnvironmentVariable("SPACEWARS_LAB_EVIDENCE");if(string.IsNullOrEmpty(folder))return;Directory.CreateDirectory(folder);
        var previous=RenderTexture.active;var pixels=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
        try{RenderTexture.active=texture;pixels.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(folder,name),pixels.EncodeToPNG());}
        finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(pixels);}
    }
}
