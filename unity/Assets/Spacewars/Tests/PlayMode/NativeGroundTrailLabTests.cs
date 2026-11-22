using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using System.Linq;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
public sealed class NativeGroundTrailLabTests
{
    [UnityTest] public IEnumerator HistoricalLifetimeSurvivesGameplayEditsAndMigratesOldRevisions()
    {
        var host=new GameObject("Trail lab controls");var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");
        var doc=host.AddComponent<UIDocument>();doc.panelSettings=panel;var root=doc.rootVisualElement;OrbitalTheme.Install(root);root.style.width=1280;root.style.height=900;
        var navigation=new NativeMenuNavigation(root);var directory=Path.Combine(Path.GetTempPath(),"spacewars-trail-lab-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var texture=new RenderTexture(1280,900,24);texture.Create();panel.targetTexture=texture;OrbitalTheme.ConfigurePanel(panel,root);
        try{
            var baseline=PlayableProfile.Default;var path=Path.Combine(directory,"balance.json");var store=new NativeBalanceStore(baseline,path);bool closed=false;
            var historical=store.Selected.CopyData();
            historical.groundTrailLifetimeSeconds=20;
            var detail=store.SelectedModelDetail;
            detail.trailCrowdThreshold=24;
            store.Save(historical,store.SelectedAudio,modelDetail:detail);
            var view=new NativeBalanceView(root,navigation,store,()=>closed=true);
            yield return null;view.Page.Q<TextField>("lab-search").value="groundTrail";yield return null;
            Assert.IsNull(view.Page.Q<TextField>("lab-field-groundTrailLifetimeSeconds"));
            view.Page.Q<TextField>("lab-search").value="visual.modelDetail";yield return null;
            Assert.IsNull(view.Page.Q<TextField>("visual.modelDetail.trailCrowdThreshold"));
            view.Page.Q<TextField>("lab-search").value="tank.health";
            view.Page.Q<TextField>("lab-field-tankHealth").value=(store.Selected.TankHealth+1).ToString();
            view.Page.Q<Button>("lab-save").Focus();navigation.Activate();yield return null;
            int revision=store.SelectedVersion;
            var reopened=new NativeBalanceStore(baseline,path);
            Assert.AreEqual(20,reopened.Resolve(revision).GroundTrailLifetimeSeconds);
            reopened.Select(revision);
            Assert.AreEqual(24,reopened.SelectedModelDetail.trailCrowdThreshold);
            reopened.ResetRelease();
            Assert.AreEqual(10,reopened.Selected.GroundTrailLifetimeSeconds);
            var old=(Newtonsoft.Json.Linq.JObject)store.State["revisions"][0];((Newtonsoft.Json.Linq.JObject)old["data"]).Remove("groundTrailLifetimeSeconds");Assert.AreEqual(10,store.Compile(old).GroundTrailLifetimeSeconds);
            Assert.AreEqual(revision,store.Active.Revision);
            yield return null;yield return null;
            var folder=Path.Combine(Environment.GetEnvironmentVariable("SPACEWARS_LAB_EVIDENCE")??directory,"trails");Directory.CreateDirectory(folder);
            var previous=RenderTexture.active;var pixels=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
            try{RenderTexture.active=texture;pixels.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(folder,"hidden-controls.png"),pixels.EncodeToPNG());}finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(pixels);}
            view.Page.Q<Button>("lab-back").Focus();navigation.Activate();Assert.True(closed);Assert.AreEqual(revision,store.Active.Revision);Assert.IsNull(NativeBalanceFields.ValidateLiveDifference(baseline,store.Active));
        }finally{navigation.Dispose();panel.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(panel);Directory.Delete(directory,true);}
    }
}
