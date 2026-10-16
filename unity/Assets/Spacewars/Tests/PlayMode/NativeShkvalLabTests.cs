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
    [UnityTest] public IEnumerator MarkerControlsSaveReloadAndApplyThroughLab()
    {
        var host=new GameObject("Shkval lab controls");var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");
        var doc=host.AddComponent<UIDocument>();doc.panelSettings=panel;var root=doc.rootVisualElement;OrbitalTheme.Install(root);root.style.width=1280;root.style.height=900;
        var navigation=new NativeMenuNavigation(root);var directory=Path.Combine(Path.GetTempPath(),"spacewars-shkval-lab-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var texture=new RenderTexture(1280,900,24);texture.Create();panel.targetTexture=texture;OrbitalTheme.ConfigurePanel(panel,root);
        try{
            var baseline=PlayableProfile.Default;var path=Path.Combine(directory,"balance.json");var store=new NativeBalanceStore(baseline,path);bool closed=false;
            var view=new NativeBalanceView(root,navigation,store,()=>closed=true);
            yield return null;view.Page.Q<TextField>("lab-search").value="marker";yield return null;
            var radius=view.Page.Q<TextField>("lab-field-shkvalMarkerStartRadius");var opacity=view.Page.Q<TextField>("lab-field-shkvalMarkerOpacity");Assert.NotNull(radius);Assert.NotNull(opacity);
            Assert.Greater(radius.worldBound.width,150);Assert.Greater(opacity.worldBound.height,0);Assert.LessOrEqual(opacity.worldBound.yMax,900);Assert.LessOrEqual(opacity.worldBound.xMax,1280);
            radius.value="2.3";opacity.value="0.35";
            view.Page.Q<Button>("lab-save").Focus();navigation.Activate();yield return null;
            int revision=(int)store.State["revisions"][store.State["revisions"].Count()-1]["revision"];
            var saved=new NativeBalanceStore(baseline,path).Resolve(revision);Assert.That(saved.ShkvalMarkerStartRadius,Is.EqualTo(2.3));Assert.That(saved.ShkvalMarkerOpacity,Is.EqualTo(.35));
            Assert.AreEqual(revision,store.Active.Revision);
            yield return null;yield return null;
            var folder=Path.Combine(Environment.GetEnvironmentVariable("SPACEWARS_LAB_EVIDENCE")??directory,"shkval");Directory.CreateDirectory(folder);
            var previous=RenderTexture.active;var pixels=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
            try{RenderTexture.active=texture;pixels.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(folder,"marker-controls.png"),pixels.EncodeToPNG());}finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(pixels);}
            view.Page.Q<Button>("lab-back").Focus();navigation.Activate();Assert.True(closed);Assert.AreEqual(revision,store.Active.Revision);Assert.IsNull(NativeBalanceFields.ValidateLiveDifference(baseline,store.Active));
        }finally{navigation.Dispose();panel.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(panel);Directory.Delete(directory,true);}
    }
}
