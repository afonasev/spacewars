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
public sealed class NativeShkvalLabTests
{
    [UnityTest] public IEnumerator HiddenMarkerValuesSurviveGameplayEdits()
    {
        var host=new GameObject("Shkval lab controls");var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");
        var doc=host.AddComponent<UIDocument>();doc.panelSettings=panel;var root=doc.rootVisualElement;OrbitalTheme.Install(root);root.style.width=1280;root.style.height=900;
        var navigation=new NativeMenuNavigation(root);var directory=Path.Combine(Path.GetTempPath(),"spacewars-shkval-lab-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var texture=new RenderTexture(1280,900,24);texture.Create();panel.targetTexture=texture;OrbitalTheme.ConfigurePanel(panel,root);
        try{
            var baseline=PlayableProfile.Default;var path=Path.Combine(directory,"balance.json");var store=new NativeBalanceStore(baseline,path);bool closed=false;
            var historical=store.Selected.CopyData();
            historical.shkvalMarkerStartRadius=2.3;
            historical.shkvalMarkerOpacity=.35;
            store.Save(historical,store.SelectedAudio);
            var savedRow=store.SelectedRow.ToString();
            int historicalRevision=store.SelectedVersion;
            var view=new NativeBalanceView(root,navigation,store,()=>closed=true);
            yield return null;view.Page.Q<TextField>("lab-search").value="marker";yield return null;
            Assert.IsNull(view.Page.Q<TextField>("lab-field-shkvalMarkerStartRadius"));
            Assert.IsNull(view.Page.Q<TextField>("lab-field-shkvalMarkerOpacity"));
            view.Page.Q<TextField>("lab-search").value="artillery.directHitDamage";yield return null;
            var direct=view.Page.Q<TextField>("lab-field-shkvalDirectHitDamage");Assert.NotNull(direct);Assert.Greater(direct.worldBound.width,150);direct.value="35";
            view.Page.Q<Button>("lab-save").Focus();navigation.Activate();yield return null;
            int revision=store.SelectedVersion;
            var reopened=new NativeBalanceStore(baseline,path);
            var saved=reopened.Resolve(revision);
            Assert.AreEqual(35,saved.ShkvalDirectHitDamage);
            Assert.AreEqual(2.3,saved.ShkvalMarkerStartRadius);
            Assert.AreEqual(.35,saved.ShkvalMarkerOpacity);
            Assert.AreEqual(savedRow,store.Versions.Single(row=>(int)row["revision"]==historicalRevision).ToString());
            view.Page.Q<TextField>("lab-search").value="artillery.baseBlastRadiusMultiplier";yield return null;
            Assert.IsNull(view.Page.Q<TextField>("lab-field-shkvalBaseBlastRadiusMultiplier"));
            view.Page.Q<TextField>("lab-search").value="artillery.baseBlastRadius";yield return null;
            var baseRadius=view.Page.Q<TextField>("lab-field-shkvalBaseBlastRadius");Assert.NotNull(baseRadius);Assert.Greater(baseRadius.worldBound.width,150);baseRadius.value="1.05";
            view.Page.Q<Button>("lab-save").Focus();navigation.Activate();yield return null;
            revision=(int)store.State["revisions"][store.State["revisions"].Count()-1]["revision"];
            Assert.AreEqual(1.05,PlayableUnitRules.ShkvalBlastRadius(new NativeBalanceStore(baseline,path).Resolve(revision),false));
            yield return null;yield return null;
            var folder=Path.Combine(Environment.GetEnvironmentVariable("SPACEWARS_LAB_EVIDENCE")??directory,"shkval");Directory.CreateDirectory(folder);
            var previous=RenderTexture.active;var pixels=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
            try{RenderTexture.active=texture;pixels.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(folder,"base-blast-radius-controls.png"),pixels.EncodeToPNG());}finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(pixels);}
            view.Page.Q<Button>("lab-back").Focus();navigation.Activate();Assert.True(closed);Assert.AreEqual(revision,store.Active.Revision);Assert.IsNull(NativeBalanceFields.ValidateLiveDifference(baseline,store.Active));
        }finally{navigation.Dispose();panel.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(panel);Directory.Delete(directory,true);}
    }
}
